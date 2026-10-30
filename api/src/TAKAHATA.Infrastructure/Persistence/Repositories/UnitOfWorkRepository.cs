using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Common.Interface.Services;
using TAKAHATA.Application.Services;
using TAKAHATA.Domain.Common.Enum;
using TAKAHATA.Domain.DataLog;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Infrastructure.Persistence.Repositories
{
    public class UnitOfWorkRepository : IUnitOfWorkRepository
    {
        private const int DailyCutoverResponseStatus = 8;

        private readonly AppDbContext _dbContext;
        private readonly ILogger<UnitOfWorkRepository> _logger;
        private readonly IPLCService _plcService;

        public UnitOfWorkRepository(
            AppDbContext dbContext,
            ILogger<UnitOfWorkRepository> logger,
            IPLCService plcService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _plcService = plcService;
        }

        public Task<bool> HasUnendedInProgressProcessAsync(
            int machineId,
            DateOnly reportDate)
        {
            return _dbContext.PartProcesses
                .AnyAsync(process =>
                    process.MachineId == machineId &&
                    process.PlantDate == reportDate &&
                    process.EndTime == null &&
                    process.Status == ProcessStatus.InProgress &&
                    process.Part != null &&
                    !process.Part.IsClosed);
        }

        public async Task<int> FinalizeExpiredProcessStopsAsync(DateTime cutoff)
        {
            var expiredStops = await _dbContext.PartProcesses
                .Where(process =>
                    process.PendingEndTime.HasValue &&
                    process.EndTime == null &&
                    process.PendingEndTime <= cutoff)
                .Select(process => new
                {
                    process.Id,
                    PendingEndTime = process.PendingEndTime!.Value
                })
                .ToListAsync();

            var finalizedCount = 0;
            foreach (var expiredStop in expiredStops)
            {
                finalizedCount += await _dbContext.PartProcesses
                    .Where(process =>
                        process.Id == expiredStop.Id &&
                        process.EndTime == null &&
                        process.PendingEndTime == expiredStop.PendingEndTime)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            process => process.EndTime,
                            expiredStop.PendingEndTime)
                        .SetProperty(
                            process => process.PendingEndTime,
                            (DateTime?)null));
            }

            return finalizedCount;
        }

        public async Task<DateOnly?> PrepareDailyCutoverAsync(
            int machineId,
            DateOnly previousDate)
        {
            var pendingSummary = await _dbContext.MachineDailyTimeSummaries
                .FirstOrDefaultAsync(summary =>
                    summary.MachineId == machineId &&
                    summary.PendingFinalPackets > 0);
            if (pendingSummary != null && pendingSummary.ReportDate < previousDate)
            {
                ClearPendingCutoverState(pendingSummary);
                _logger.LogWarning(
                    "Đã hết hạn trạng thái chờ phản hồi chốt ngày cũ để không chặn lệnh chốt mới. " +
                    "MachineId={MachineId}, ExpiredReportDate={ExpiredReportDate}, NewReportDate={NewReportDate}",
                    machineId,
                    pendingSummary.ReportDate,
                    previousDate);
            }
            else if (pendingSummary != null)
            {
                _logger.LogWarning(
                    "Bỏ qua lệnh chốt mới cho máy {MachineId}: vẫn đang chờ phản hồi cho ngày {ReportDate}.",
                    machineId,
                    pendingSummary.ReportDate);
                return pendingSummary.ReportDate;
            }

            var previousSummary = await _dbContext.MachineDailyTimeSummaries
                .FirstOrDefaultAsync(summary =>
                    summary.MachineId == machineId &&
                    summary.ReportDate == previousDate);

            if (previousSummary == null)
            {
                previousSummary = new MachineDailyTimeSummary
                {
                    MachineId = machineId,
                    ReportDate = previousDate
                };
                _dbContext.MachineDailyTimeSummaries.Add(previousSummary);
            }

            var machine = await _dbContext.Machines
                .FirstOrDefaultAsync(candidate => candidate.Id == machineId);
            if (machine == null)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy máy Id={machineId} khi chuẩn bị chốt ngày.");
            }

            previousSummary.PendingFinalPackets = 1;
            previousSummary.CutoverCommandSent = false;
            previousSummary.ProcessTimesReset = false;
            previousSummary.WasRunningAtCutover =
                machine.MachineStatus == MachineStatus.Running;
            previousSummary.FirstRunningAt = null;

            await _dbContext.SaveChangesAsync();
            return null;
        }

        public async Task MarkDailyCutoverCommandSentAsync(
            int machineId,
            DateOnly reportDate)
        {
            var summary = await GetDailySummaryAsync(machineId, reportDate);
            summary.CutoverCommandSent = true;
            await _dbContext.SaveChangesAsync();
        }

        public async Task CancelDailyCutoverAsync(int machineId, DateOnly reportDate)
        {
            var summary = await GetDailySummaryAsync(machineId, reportDate);
            summary.PendingFinalPackets = 0;
            summary.CutoverCommandSent = false;
            summary.WasRunningAtCutover = null;
            summary.FirstRunningAt = null;
            await _dbContext.SaveChangesAsync();
        }

        public async Task<DateOnly?> ClearPendingDailyCutoverAsync(int machineId)
        {
            var pendingSummary = await _dbContext.MachineDailyTimeSummaries
                .Where(summary =>
                    summary.MachineId == machineId &&
                    summary.PendingFinalPackets > 0)
                .OrderBy(summary => summary.ReportDate)
                .FirstOrDefaultAsync();

            if (pendingSummary == null)
                return null;

            ClearPendingCutoverState(pendingSummary);
            await _dbContext.SaveChangesAsync();

            _logger.LogWarning(
                "Đã gỡ trạng thái chờ phản hồi chốt ngày theo yêu cầu admin. " +
                "MachineId={MachineId}, ReportDate={ReportDate}. " +
                "Chỉ thực hiện sau khi xác nhận PLC không còn gửi phản hồi cũ.",
                machineId,
                pendingSummary.ReportDate);

            return pendingSummary.ReportDate;
        }

        public async Task UpdateDataAsync(
            int statusBitAddress,
            int status,
            int partId,
            int processId,
            double workingTime,
            double waitingTime,
            double setupTime)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object> { ["MachineLog"] = true });

            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                var reportDate = VietnamTime.Today;
                MachineDailyTimeSummary? dailySummary = null;
                var isFinalPacket = status == DailyCutoverResponseStatus;
                Machine? machine = null;
                var effectiveProcessId = processId;
                int? continuationPartIdToWrite = null;
                int? continuationProcessIdToWrite = null;
                if (statusBitAddress != 0)
                {
                    machine = await _dbContext.Machines.FirstOrDefaultAsync(
                        candidate => candidate.StatusBitAddress == statusBitAddress);

                    if (machine != null)
                    {
                        if (!isFinalPacket &&
                            (MachineStatus)status == MachineStatus.Running)
                        {
                            var activeProcessId = processId != 0
                                ? processId
                                : machine.PartProcessId;
                            var activeProcessPlantDate = await _dbContext.PartProcesses
                                .Where(process => process.Id == activeProcessId)
                                .Select(process => process.PlantDate)
                                .FirstOrDefaultAsync();
                            var cutoverReportDate = activeProcessPlantDate?.AddDays(-1) ??
                                VietnamTime.Today.AddDays(-1);
                            var cutoverSummary = await _dbContext.MachineDailyTimeSummaries
                                .FirstOrDefaultAsync(summary =>
                                    summary.MachineId == machine.Id &&
                                    summary.ReportDate == cutoverReportDate &&
                                    (summary.PendingFinalPackets > 0 ||
                                     summary.ProcessTimesReset) &&
                                    summary.WasRunningAtCutover == false);
                            if (cutoverSummary != null &&
                                (!cutoverSummary.FirstRunningAt.HasValue ||
                                 activeProcessPlantDate.HasValue &&
                                 DateOnly.FromDateTime(cutoverSummary.FirstRunningAt.Value) !=
                                 activeProcessPlantDate.Value))
                            {
                                var runningAt = VietnamTime.Now;
                                var runningDate =
                                    activeProcessPlantDate.HasValue &&
                                    activeProcessPlantDate.Value > VietnamTime.Today
                                        ? activeProcessPlantDate.Value
                                        : DateOnly.FromDateTime(runningAt);
                                cutoverSummary.FirstRunningAt = runningDate.ToDateTime(
                                    TimeOnly.FromDateTime(runningAt));
                            }
                        }

                        if (isFinalPacket)
                        {
                            dailySummary = await _dbContext.MachineDailyTimeSummaries
                                .Where(summary =>
                                    summary.MachineId == machine.Id &&
                                    summary.PendingFinalPackets > 0)
                                .OrderBy(summary => summary.ReportDate)
                                .FirstOrDefaultAsync();

                            if (dailySummary == null)
                            {
                                _logger.LogWarning(
                                    "Nhận gói chốt thời gian status=8 nhưng không có lệnh chốt đang chờ. " +
                                    "Bỏ qua thời gian để tránh ghi sai ngày. " +
                                    "StatusBitAddress={StatusBitAddress}, WorkingTime={WorkingTime}, " +
                                    "WaitingTime={WaitingTime}, SetupTime={SetupTime}",
                                    statusBitAddress,
                                    workingTime,
                                    waitingTime,
                                    setupTime);
                                await transaction.CommitAsync();
                                return;
                            }

                            reportDate = dailySummary.ReportDate;
                            effectiveProcessId = processId != 0
                                ? processId
                                : machine.PartProcessId;
                        }
                        else
                        {
                            var currentDate = VietnamTime.Today;
                            var previousDate = currentDate.AddDays(-1);
                            dailySummary = await _dbContext.MachineDailyTimeSummaries
                                .Where(summary =>
                                    summary.MachineId == machine.Id &&
                                    summary.PendingFinalPackets > 0 &&
                                    summary.ReportDate >= previousDate)
                                .OrderBy(summary => summary.ReportDate)
                                .FirstOrDefaultAsync();
                            if (dailySummary != null)
                            {
                                reportDate = dailySummary.ReportDate;
                                machine.MachineStatus = (MachineStatus)status;
                                machine.MachineStatusChangeAt = DateTime.Now;
                                effectiveProcessId = await ResolveCurrentProcessIdAsync(
                                    machine.Id,
                                    processId,
                                    partId != 0 ? partId : machine.PartId,
                                    machine.PartProcessId,
                                    reportDate);

                                _logger.LogInformation(
                                    "Đang chờ phản hồi chốt ngày {ReportDate}; lưu thời gian PLC vào công đoạn thuộc ngày chốt và giữ nguyên công đoạn hiện tại của máy {MachineCode}. " +
                                    "ProcessId={ProcessId}, WorkingTime={WorkingTime}, WaitingTime={WaitingTime}, SetupTime={SetupTime}",
                                    reportDate,
                                    machine.Code,
                                    effectiveProcessId,
                                    workingTime,
                                    waitingTime,
                                    setupTime);
                            }
                            else
                            {
                                var stalePendingSummary = await _dbContext.MachineDailyTimeSummaries
                                    .Where(summary =>
                                        summary.MachineId == machine.Id &&
                                        summary.PendingFinalPackets > 0 &&
                                        summary.ReportDate < previousDate)
                                    .OrderBy(summary => summary.ReportDate)
                                    .FirstOrDefaultAsync();
                                if (stalePendingSummary != null)
                                {
                                    ClearPendingCutoverState(stalePendingSummary);
                                    _logger.LogWarning(
                                        "Đã hết hạn trạng thái chờ chốt ngày cũ khi xử lý gói PLC hiện tại; " +
                                        "status=8 đến sau thời điểm này sẽ không được ghép với ngày cũ. " +
                                        "MachineCode={MachineCode}, ExpiredReportDate={ExpiredReportDate}, CurrentDate={CurrentDate}",
                                        machine.Code,
                                        stalePendingSummary.ReportDate,
                                        currentDate);
                                }

                                machine.MachineStatus = (MachineStatus)status;
                                machine.MachineStatusChangeAt = DateTime.Now;
                                if (partId != 0)
                                    machine.PartId = partId;
                                effectiveProcessId = await ResolveCurrentProcessIdAsync(
                                    machine.Id,
                                    processId,
                                    partId != 0 ? partId : machine.PartId,
                                    machine.PartProcessId,
                                    currentDate);
                                if (effectiveProcessId != 0)
                                {
                                    var processDate = await _dbContext.PartProcesses
                                        .Where(partProcess =>
                                            partProcess.Id == effectiveProcessId)
                                        .Select(partProcess => new
                                        {
                                            partProcess.PlantDate,
                                            partProcess.ReferenceId,
                                            partProcess.EndTime,
                                            partProcess.PendingEndTime,
                                            partProcess.Status
                                        })
                                        .FirstOrDefaultAsync();

                                    if (processDate?.PlantDate.HasValue == true &&
                                        (processDate.ReferenceId.HasValue ||
                                         processDate.PlantDate.Value > currentDate ||
                                         (processDate.EndTime.HasValue ||
                                          processDate.PendingEndTime.HasValue) &&
                                         (processDate.Status is
                                             ProcessStatus.Paused or
                                             ProcessStatus.PartiallyCompleted or
                                             ProcessStatus.Completed)))
                                    {
                                        reportDate = processDate.PlantDate.Value;
                                    }
                                }

                                if (effectiveProcessId != 0)
                                    machine.PartProcessId = effectiveProcessId;
                            }
                        }
                    }
                }

                if (machine != null && isFinalPacket)
                {
                    var rootProcessId = effectiveProcessId == 0
                        ? 0
                        : await _dbContext.PartProcesses
                            .Where(partProcess => partProcess.Id == effectiveProcessId)
                            .Select(partProcess => partProcess.ReferenceId ?? partProcess.Id)
                            .FirstOrDefaultAsync();

                    var eligibleProcesses = _dbContext.PartProcesses
                        .Where(partProcess =>
                            partProcess.MachineId == machine.Id &&
                            partProcess.PlantDate == reportDate &&
                            partProcess.EndTime == null &&
                            partProcess.Status == ProcessStatus.InProgress &&
                            partProcess.Part != null &&
                            !partProcess.Part.IsClosed)
                        .OrderByDescending(partProcess => partProcess.Id);

                    var activeProcessId = rootProcessId == 0
                        ? 0
                        : await eligibleProcesses
                            .Where(partProcess =>
                                partProcess.Id == rootProcessId ||
                                partProcess.ReferenceId == rootProcessId)
                            .Select(partProcess => partProcess.Id)
                            .FirstOrDefaultAsync();

                    if (activeProcessId == 0)
                    {
                        activeProcessId = await eligibleProcesses
                            .Select(partProcess => partProcess.Id)
                            .FirstOrDefaultAsync();
                    }

                    if (activeProcessId != 0)
                    {
                        effectiveProcessId = activeProcessId;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Không tìm thấy công đoạn đang thực hiện thuộc ngày chốt. " +
                            "Bỏ qua ghi thời gian công đoạn để tránh lưu sang ngày khác. " +
                            "MachineId={MachineId}, IncomingProcessId={IncomingProcessId}, ReportDate={ReportDate}",
                            machine.Id,
                            processId,
                            reportDate);
                        effectiveProcessId = 0;
                    }
                }
                else if (machine == null && isFinalPacket)
                {
                    _logger.LogWarning(
                        "Nhận gói chốt thời gian status=8 từ địa chỉ chưa đăng ký. " +
                        "StatusBitAddress={StatusBitAddress}",
                        statusBitAddress);
                    await transaction.CommitAsync();
                    return;
                }

                if (machine != null)
                {
                    dailySummary ??= await _dbContext.MachineDailyTimeSummaries
                        .FirstOrDefaultAsync(summary =>
                            summary.MachineId == machine.Id &&
                            summary.ReportDate == reportDate);

                    if (dailySummary == null)
                    {
                        dailySummary = new MachineDailyTimeSummary
                        {
                            MachineId = machine.Id,
                            ReportDate = reportDate
                        };
                        _dbContext.MachineDailyTimeSummaries.Add(dailySummary);
                    }

                    dailySummary.WorkingTime = Math.Round(
                        dailySummary.WorkingTime + workingTime,
                        2);
                    dailySummary.WaitingTime = Math.Round(
                        dailySummary.WaitingTime + waitingTime,
                        2);
                    dailySummary.SetupTime = Math.Round(
                        dailySummary.SetupTime + setupTime,
                        2);

                    if (isFinalPacket)
                        dailySummary.PendingFinalPackets = 0;
                }

                if (machine != null && effectiveProcessId != 0)
                {
                    var processDailySummary = await _dbContext.PartProcessDailyTimeSummaries
                        .FirstOrDefaultAsync(summary =>
                            summary.MachineId == machine.Id &&
                            summary.PartProcessId == effectiveProcessId &&
                            summary.ReportDate == reportDate);

                    if (processDailySummary == null)
                    {
                        processDailySummary = new PartProcessDailyTimeSummary
                        {
                            MachineId = machine.Id,
                            PartProcessId = effectiveProcessId,
                            ReportDate = reportDate
                        };
                        _dbContext.PartProcessDailyTimeSummaries.Add(processDailySummary);
                    }

                    processDailySummary.WorkingTime = Math.Round(
                        processDailySummary.WorkingTime + workingTime,
                        2);
                    processDailySummary.WaitingTime = Math.Round(
                        processDailySummary.WaitingTime + waitingTime,
                        2);
                    processDailySummary.SetupTime = Math.Round(
                        processDailySummary.SetupTime + setupTime,
                        2);
                }

                PartProcess? process = null;
                if (effectiveProcessId != 0)
                {
                    process = await _dbContext.PartProcesses
                        .Include(partProcess => partProcess.Part)
                        .FirstOrDefaultAsync(partProcess =>
                            partProcess.Id == effectiveProcessId &&
                            partProcess.Part != null &&
                            !partProcess.Part.IsClosed);
                }

                if (process != null)
                {
                    process.WorkingTime = Math.Round(
                        (process.WorkingTime ?? 0d) + workingTime,
                        2);
                    process.WaitingTime = Math.Round(
                        (process.WaitingTime ?? 0d) + waitingTime,
                        2);
                    process.SetupTime = Math.Round(
                        (process.SetupTime ?? 0d) + setupTime,
                        2);

                    if (process.PendingEndTime.HasValue)
                    {
                        _logger.LogInformation(
                            "Đã nhận gói PLC trong thời gian chờ chốt; tiếp tục cộng dồn vào công đoạn. " +
                            "ProcessId={ProcessId}, PendingEndTime={PendingEndTime}, " +
                            "WorkingTime={WorkingTime}, WaitingTime={WaitingTime}, SetupTime={SetupTime}",
                            process.Id,
                            process.PendingEndTime,
                            workingTime,
                            waitingTime,
                            setupTime);
                    }

                    if (isFinalPacket &&
                        process.Status == ProcessStatus.InProgress &&
                        process.PlantDate <= reportDate &&
                        dailySummary is { ProcessTimesReset: false })
                    {
                        var currentDate = reportDate.AddDays(1);
                        var cutoverAt = currentDate.ToDateTime(TimeOnly.MinValue);

                        await _dbContext.SaveChangesAsync();

                        process.EndTime = cutoverAt;
                        process.PendingEndTime = null;

                        var rootProcessId = process.ReferenceId ?? process.Id;
                        var continuation = await _dbContext.PartProcesses
                            .FirstOrDefaultAsync(candidate =>
                                candidate.MachineId == machine!.Id &&
                                candidate.PlantDate == currentDate &&
                                (candidate.Id == rootProcessId ||
                                 candidate.ReferenceId == rootProcessId) &&
                                candidate.Status == ProcessStatus.InProgress);

                        if (continuation == null)
                        {
                            continuation = new PartProcess
                            {
                                CreateAt = VietnamTime.Now,
                                CreateBy = process.CreateBy,
                                MachineId = process.MachineId,
                                PlantDate = currentDate,
                                StartTime = null,
                                EndTime = null,
                                EstimatedTime = process.EstimatedTime,
                                PerformedBy = null,
                                Note = process.Note,
                                Status = ProcessStatus.InProgress,
                                PartId = process.PartId,
                                Sequence = process.Sequence,
                                WorkingTime = 0,
                                SetupTime = 0,
                                WaitingTime = 0,
                                ProgrammingTime = 0,
                                AssemblyTime = 0,
                                MeasurementTime = 0,
                                ProcessedQuantity = 0,
                                WorkerNote = string.Empty,
                                IsOverdue = true,
                                ReferenceId = rootProcessId,
                                IsSettingUp = false
                            };
                            _dbContext.PartProcesses.Add(continuation);
                            await _dbContext.SaveChangesAsync();
                        }

                        var duplicatePlans = await _dbContext.PartProcesses
                            .Where(candidate =>
                                candidate.Id != continuation.Id &&
                                candidate.PlantDate == currentDate &&
                                candidate.MachineId == machine!.Id &&
                                candidate.Part != null &&
                                candidate.Part.Code == process.Part!.Code &&
                                candidate.Part.IndexId == process.Part.IndexId)
                            .ToListAsync();

                        foreach (var duplicate in duplicatePlans)
                        {
                            if (IsSafeToRemoveUnstartedPlan(duplicate))
                            {
                                _logger.LogInformation(
                                    "Xóa kế hoạch gốc ngày mới chưa bắt đầu vì trùng mã part, index và máy " +
                                    "với kế hoạch tiếp nối. " +
                                    "PreviousProcessId={PreviousProcessId}, RemovedProcessId={RemovedProcessId}, " +
                                    "ContinuationProcessId={ContinuationProcessId}",
                                    process.Id,
                                    duplicate.Id,
                                    continuation.Id);
                                _dbContext.PartProcesses.Remove(duplicate);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Giữ kế hoạch ngày mới trùng mã part, index và máy vì đã có dữ liệu thực tế. " +
                                    "PreviousProcessId={PreviousProcessId}, PreservedProcessId={PreservedProcessId}, " +
                                    "ContinuationProcessId={ContinuationProcessId}",
                                    process.Id,
                                    duplicate.Id,
                                    continuation.Id);
                            }
                        }

                        var currentSummary = await _dbContext.MachineDailyTimeSummaries
                            .FirstOrDefaultAsync(summary =>
                                summary.MachineId == machine!.Id &&
                                summary.ReportDate == currentDate);
                        if (currentSummary == null)
                        {
                            currentSummary = new MachineDailyTimeSummary
                            {
                                MachineId = machine!.Id,
                                ReportDate = currentDate
                            };
                            _dbContext.MachineDailyTimeSummaries.Add(currentSummary);
                        }

                        machine!.PartProcessId = continuation.Id;
                        machine.PartId = process.PartId;
                        continuationPartIdToWrite = process.PartId;
                        continuationProcessIdToWrite = continuation.Id;
                        dailySummary.ProcessTimesReset = true;

                        _logger.LogInformation(
                            "Đã chốt thời gian và tạo/nhận diện kế hoạch tiếp nối qua ngày. " +
                            "MachineCode={MachineCode}, PreviousProcessId={PreviousProcessId}, " +
                            "ContinuationProcessId={ContinuationProcessId}, ReportDate={ReportDate}",
                            machine?.Code,
                            process.Id,
                            continuation.Id,
                            reportDate);
                    }

                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                if (machine != null &&
                    continuationPartIdToWrite.HasValue &&
                    continuationProcessIdToWrite.HasValue)
                {
                    var writeResult = await _plcService.WriteMachineIdentifiers(
                        machine,
                        continuationPartIdToWrite.Value,
                        continuationProcessIdToWrite.Value);
                    if (!writeResult.IsSuccess)
                    {
                        _logger.LogError(
                            writeResult.Error,
                            "Đã tạo tiếp nối nhưng không ghi được PartId/ProcessId xuống PLC. " +
                            "MachineCode={MachineCode}, PartId={PartId}, ProcessId={ProcessId}",
                            machine.Code,
                            continuationPartIdToWrite.Value,
                            continuationProcessIdToWrite.Value);
                    }
                }

                _logger.LogInformation(
                    "Cập nhật dữ liệu PLC thành công. " +
                    "StatusBitAddress={StatusBitAddress}, Status={Status}, " +
                    "PartId={PartId}, ProcessId={ProcessId}, WorkingTime={WorkingTime}, " +
                    "WaitingTime={WaitingTime}, SetupTime={SetupTime}, ReportDate={ReportDate}, " +
                    "EffectiveProcessId={EffectiveProcessId}, StoredWorkingTime={StoredWorkingTime}, " +
                    "StoredWaitingTime={StoredWaitingTime}, StoredSetupTime={StoredSetupTime}, " +
                    "IsFinalPacket={IsFinalPacket}",
                    statusBitAddress,
                    status,
                    partId,
                    processId,
                    workingTime,
                    waitingTime,
                    setupTime,
                    reportDate,
                    effectiveProcessId,
                    process?.WorkingTime,
                    process?.WaitingTime,
                    process?.SetupTime,
                    isFinalPacket);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(
                    ex,
                    "Cập nhật dữ liệu PLC thất bại. StatusBitAddress={StatusBitAddress}, ProcessId={ProcessId}",
                    statusBitAddress,
                    processId);
                throw;
            }
        }

        private static bool IsSafeToRemoveUnstartedPlan(PartProcess process)
        {
            return process.Status == ProcessStatus.Planned &&
                process.StartTime == null &&
                process.EndTime == null &&
                process.ProcessedQuantity == 0 &&
                process.WorkingTime.GetValueOrDefault() == 0 &&
                process.WaitingTime.GetValueOrDefault() == 0 &&
                process.SetupTime.GetValueOrDefault() == 0 &&
                process.ProgrammingTime.GetValueOrDefault() == 0 &&
                process.AssemblyTime.GetValueOrDefault() == 0 &&
                process.MeasurementTime.GetValueOrDefault() == 0;
        }

        private async Task<MachineDailyTimeSummary> GetDailySummaryAsync(
            int machineId,
            DateOnly reportDate)
        {
            var summary = await _dbContext.MachineDailyTimeSummaries
                .FirstOrDefaultAsync(item =>
                    item.MachineId == machineId &&
                    item.ReportDate == reportDate);

            return summary ?? throw new InvalidOperationException(
                $"Không tìm thấy dữ liệu tổng hợp ngày {reportDate} cho máy Id={machineId}.");
        }

        private static void ClearPendingCutoverState(MachineDailyTimeSummary summary)
        {
            summary.PendingFinalPackets = 0;
            summary.CutoverCommandSent = false;
            summary.WasRunningAtCutover = null;
            summary.FirstRunningAt = null;
        }

        private async Task<int> ResolveCurrentProcessIdAsync(
            int machineId,
            int processId,
            int partId,
            int currentMachineProcessId,
            DateOnly currentDate)
        {
            if (processId == 0)
            {
                var currentPendingStopProcessId = await _dbContext.PartProcesses
                    .Where(process =>
                        process.Id == currentMachineProcessId &&
                        process.MachineId == machineId &&
                        process.PartId == partId &&
                        process.PendingEndTime.HasValue &&
                        process.Part != null &&
                        !process.Part.IsClosed)
                    .Select(process => process.Id)
                    .FirstOrDefaultAsync();
                if (currentPendingStopProcessId != 0)
                    return currentPendingStopProcessId;

                var currentProcessId = await _dbContext.PartProcesses
                    .Where(process =>
                        process.Id == currentMachineProcessId &&
                        process.MachineId == machineId &&
                        process.PlantDate <= currentDate &&
                        process.EndTime == null &&
                        process.Status == ProcessStatus.InProgress)
                    .Select(process => process.Id)
                    .FirstOrDefaultAsync();
                if (currentProcessId != 0)
                    return currentProcessId;

                return await _dbContext.PartProcesses
                    .Where(process =>
                        process.MachineId == machineId &&
                        process.PlantDate <= currentDate &&
                        process.EndTime == null &&
                        process.Status == ProcessStatus.InProgress)
                    .OrderByDescending(process => process.PlantDate)
                    .ThenByDescending(process => process.Id)
                    .Select(process => process.Id)
                    .FirstOrDefaultAsync();
            }

            var rootProcessId = await _dbContext.PartProcesses
                .Where(process => process.Id == processId)
                .Select(process => process.ReferenceId ?? process.Id)
                .FirstOrDefaultAsync();

            var futureProcessId = await _dbContext.PartProcesses
                .Where(process =>
                    process.Id == processId &&
                    process.MachineId == machineId &&
                    process.PlantDate > currentDate &&
                    process.EndTime == null &&
                    (process.Status == ProcessStatus.InProgress || process.IsSettingUp) &&
                    process.Part != null &&
                    !process.Part.IsClosed)
                .Select(process => process.Id)
                .FirstOrDefaultAsync();
            if (futureProcessId != 0)
                return futureProcessId;

            var pendingStopProcessId = await _dbContext.PartProcesses
                .Where(process =>
                    process.Id == processId &&
                    process.MachineId == machineId &&
                    process.PartId == partId &&
                    process.PendingEndTime.HasValue &&
                    process.Part != null &&
                    !process.Part.IsClosed)
                .Select(process => process.Id)
                .FirstOrDefaultAsync();
            if (pendingStopProcessId != 0)
            {
                _logger.LogInformation(
                    "Nhận gói PLC cuối trong thời gian chờ chốt công đoạn. " +
                    "MachineId={MachineId}, ProcessId={ProcessId}, PartId={PartId}",
                    machineId,
                    pendingStopProcessId,
                    partId);
                return pendingStopProcessId;
            }

            var recentlyEndedProcessId = await _dbContext.PartProcesses
                .Where(process =>
                    process.Id == processId &&
                    process.MachineId == machineId &&
                    process.PartId == partId &&
                    process.PlantDate <= currentDate &&
                    process.EndTime >= VietnamTime.Now.AddMinutes(-5) &&
                    (process.Status == ProcessStatus.Paused ||
                     process.Status == ProcessStatus.PartiallyCompleted ||
                     process.Status == ProcessStatus.Completed) &&
                    process.Part != null &&
                    !process.Part.IsClosed)
                .Select(process => process.Id)
                .FirstOrDefaultAsync();
            if (recentlyEndedProcessId != 0)
            {
                _logger.LogInformation(
                    "Nhận gói PLC đến trễ sau khi chốt công đoạn; cộng đúng vào công đoạn theo ID PLC. " +
                    "MachineId={MachineId}, ProcessId={ProcessId}, PartId={PartId}",
                    machineId,
                    recentlyEndedProcessId,
                    partId);
                return recentlyEndedProcessId;
            }

            var continuationId = await _dbContext.PartProcesses
                .Where(process =>
                    process.MachineId == machineId &&
                    process.PlantDate == currentDate &&
                    process.EndTime == null &&
                    (process.Status == ProcessStatus.InProgress || process.IsSettingUp) &&
                    process.ReferenceId == rootProcessId)
                .OrderByDescending(process => process.Id)
                .Select(process => process.Id)
                .FirstOrDefaultAsync();
            if (continuationId != 0)
                return continuationId;

            var incomingProcessId = await _dbContext.PartProcesses
                .Where(process =>
                    process.Id == processId &&
                    process.MachineId == machineId &&
                    process.PlantDate <= currentDate &&
                    process.Status == ProcessStatus.InProgress)
                .Select(process => process.Id)
                .FirstOrDefaultAsync();
            if (incomingProcessId != 0)
                return incomingProcessId;

            var machineProcessId = await _dbContext.PartProcesses
                .Where(process =>
                    process.Id == currentMachineProcessId &&
                    process.MachineId == machineId &&
                    process.PlantDate <= currentDate &&
                    process.EndTime == null &&
                    (process.Status == ProcessStatus.InProgress || process.IsSettingUp) &&
                    (process.Id == rootProcessId ||
                     process.ReferenceId == rootProcessId))
                .Select(process => process.Id)
                .FirstOrDefaultAsync();
            if (machineProcessId != 0)
                return machineProcessId;

            var activeProcessId = await _dbContext.PartProcesses
                .Where(process =>
                    process.MachineId == machineId &&
                    process.PlantDate <= currentDate &&
                    process.EndTime == null &&
                    process.Status == ProcessStatus.InProgress &&
                    (process.Id == rootProcessId ||
                     process.ReferenceId == rootProcessId))
                .OrderByDescending(process => process.PlantDate)
                .ThenByDescending(process => process.Id)
                .Select(process => process.Id)
                .FirstOrDefaultAsync();
            if (activeProcessId != 0)
                return activeProcessId;

            return 0;
        }
    }
}
