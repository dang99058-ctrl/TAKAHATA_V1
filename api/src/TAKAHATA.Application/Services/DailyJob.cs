using Microsoft.Extensions.Logging;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Common.Interface.Services;
using TAKAHATA.Contracts.Common;

namespace TAKAHATA.Application.Services
{
    public class DailyJob
    {
        private readonly ILogger<DailyJob> _logger;
        private readonly IPartProcessRepository _partProcessRepository;
        private readonly IMachineRepository _machineRepository;
        private readonly IUnitOfWorkRepository _unitOfWorkRepository;
        private readonly IPLCService _plcService;

        public DailyJob(
            ILogger<DailyJob> logger,
            IPartProcessRepository partProcessRepository,
            IMachineRepository machineRepository,
            IUnitOfWorkRepository unitOfWorkRepository,
            IPLCService plcService)
        {
            _logger = logger;
            _partProcessRepository = partProcessRepository;
            _machineRepository = machineRepository;
            _unitOfWorkRepository = unitOfWorkRepository;
            _plcService = plcService;
        }

        public async Task ExecuteAsync()
        {
            await ExecuteAsync(VietnamTime.Today);
        }

        public async Task ExecuteAsync(DateOnly targetDate)
        {
            _logger.LogInformation(
                "DailyJob gia hạn kế hoạch sang ngày {TargetDate}, bắt đầu lúc {Time}",
                targetDate,
                DateTimeOffset.Now);
            await _partProcessRepository.HandleExtend(targetDate);

            var machines = (await _machineRepository.GetAsync())
                .ToDictionary(machine => machine.Id);
            var inProgressProcesses = await _partProcessRepository
                .GetInProgressProcessesByMachineIdsAsync(
                    machines.Keys.ToList(),
                    targetDate);
            var failures = new List<Exception>();

            foreach (var process in inProgressProcesses.Where(process =>
                process.ReferenceId.HasValue))
            {
                if (!machines.TryGetValue(process.MachineId, out var machine) ||
                    !machine.IsAutomatic ||
                    machine.PartBitAddress <= 0 ||
                    machine.PartProcessBitAddress <= 0)
                {
                    continue;
                }

                var result = await _plcService.WriteMachineIdentifiers(
                    machine,
                    process.PartId,
                    process.Id);
                if (!result.IsSuccess)
                {
                    var error = result.Error ?? new InvalidOperationException(
                        $"Không thể khôi phục PartId/ProcessId xuống PLC cho máy {machine.Code}.");
                    _logger.LogError(
                        error,
                        "Không ghi được PartId/ProcessId của kế hoạch tiếp nối xuống PLC. " +
                        "MachineCode={MachineCode}, PartId={PartId}, ProcessId={ProcessId}",
                        machine.Code,
                        process.PartId,
                        process.Id);
                    failures.Add(error);
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Không thể ghi PartId/ProcessId của một hoặc nhiều kế hoạch tiếp nối xuống PLC.",
                    failures);
            }

            _logger.LogInformation(
                "DailyJob gia hạn kế hoạch sang ngày {TargetDate} hoàn thành",
                targetDate);
        }

        public async Task SendDailyTimeCutoverCommandAsync()
        {
            await SendDailyTimeCutoverCommandAsync(VietnamTime.Today.AddDays(-1), null);
        }

        public async Task SendDailyTimeCutoverCommandAsync(
            DateOnly reportDate,
            int? targetMachineId)
        {
            var currentDate = reportDate.AddDays(1);
            var machines = (await _machineRepository.GetAsync())
                .Where(machine => !targetMachineId.HasValue ||
                    machine.Id == targetMachineId.Value)
                .ToList();

            if (targetMachineId.HasValue && machines.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy máy Id={targetMachineId.Value} để test chốt thời gian.");
            }

            var failures = new List<Exception>();

            foreach (var machine in machines)
            {
                if (!await _unitOfWorkRepository.HasUnendedInProgressProcessAsync(
                    machine.Id,
                    reportDate))
                {
                    _logger.LogInformation(
                        "Không gửi lệnh chốt thời gian cho máy {MachineCode}: " +
                        "không có công đoạn đang thực hiện chưa kết thúc ngày {ReportDate}.",
                        machine.Code,
                        reportDate);
                    continue;
                }

                if (machine.ProcessStatusBitAddress <= 0)
                {
                    if (targetMachineId.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"Máy {machine.Code} chưa cấu hình ProcessStatusBitAddress.");
                    }

                    _logger.LogWarning(
                        "Bỏ qua gửi lệnh chốt thời gian cho máy {MachineCode}: chưa cấu hình ProcessStatusBitAddress",
                        machine.Code);
                    continue;
                }

                var pendingReportDate = await _unitOfWorkRepository.PrepareDailyCutoverAsync(
                    machine.Id,
                    reportDate);
                if (pendingReportDate.HasValue)
                {
                    if (targetMachineId.HasValue)
                    {
                        throw new DailyCutoverPendingException(
                            $"Máy {machine.Code} vẫn đang chờ PLC phản hồi chốt ngày " +
                            $"{pendingReportDate.Value:dd/MM/yyyy}. " +
                            "Chưa gửi lệnh chốt mới để tránh gán nhầm phản hồi PLC.");
                    }

                    _logger.LogInformation(
                        "Không gửi lệnh chốt mới cho máy {MachineCode}: đang chờ phản hồi chốt ngày trước đó.",
                        machine.Code);
                    continue;
                }

                var result = await _plcService.WriteProcessStatus(machine, 8);
                if (!result.IsSuccess)
                {
                    var error = result.Error ?? new InvalidOperationException(
                        $"Không gửi được lệnh chốt thời gian cho máy {machine.Code}.");
                    await _unitOfWorkRepository.CancelDailyCutoverAsync(
                        machine.Id,
                        reportDate);
                    _logger.LogError(
                        error,
                        "Gửi lệnh chốt thời gian 8 thất bại cho máy {MachineCode}",
                        machine.Code);
                    failures.Add(error);
                    continue;
                }

                await _unitOfWorkRepository.MarkDailyCutoverCommandSentAsync(
                    machine.Id,
                    reportDate);
                _logger.LogInformation(
                    "Đã gửi lệnh chốt thời gian 8 cho máy {MachineCode}; ngày tổng hợp {ReportDate}, ngày tiếp nối {ContinuationDate}",
                    machine.Code,
                    reportDate,
                    currentDate);
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Không gửi được lệnh chốt thời gian cho một hoặc nhiều máy.",
                    failures);
            }
        }

    }

    public sealed class DailyCutoverPendingException(string message) : Exception(message)
    {
    }
}
