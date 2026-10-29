using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Common.Interface.Services;
using TAKAHATA.Contracts.IndexAndPart;
using TAKAHATA.Domain.Common.Errors;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Application.Manufacturing.Commands
{
    public class BeginSettingUpCommandHandler : IRequestHandler<BeginSettingUpCommand, ErrorOr<ManufacturingProcessResponse>>
    {
        private readonly ILogger<BeginSettingUpCommandHandler> _logger;
        private readonly IPartProcessRepository _partProcessRepository;
        private readonly IMachineRepository _machineRepository;
        private readonly IPLCService _iPLCService;

        public BeginSettingUpCommandHandler(
            ILogger<BeginSettingUpCommandHandler> logger,
            IPartProcessRepository partProcessRepository,
            IMachineRepository machineRepository,
            IPLCService iPLCService)
        {
            _logger = logger;
            _partProcessRepository = partProcessRepository;
            _machineRepository = machineRepository;
            _iPLCService = iPLCService;
        }

        public async Task<ErrorOr<ManufacturingProcessResponse>> Handle(BeginSettingUpCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Bắt đầu quá trình gá đặt");
            var machineId = request.MachineId;
            var machine = await _machineRepository.GetByIdAsync(machineId);

            if (machine == null)
                return Errors.Machine.NotFound;

            if (await _partProcessRepository.IsCutoverLockedAsync(request.ProcessId))
                return Errors.Part.ProcessCutoverLocked;

            if (machine.IsAutomatic)
            {
                var checkResult = await _iPLCService.IsProcessing(machine);

                // Đọc trạng thái không thành công
                if (!checkResult.IsSuccess)
                    return Errors.Machine.TimeOut;

                // Trạng thái dưới PLC vẫn đang running thì không được tạm dừng
                if (checkResult.Value)
                    return Errors.Machine.IsProcessingPLC;
            }

            // Trạng thái trong DB chưa thay đổi
            if (await _machineRepository.IsProcessing(machineId))
                return Errors.Machine.IsProcessing;

            var result = await _iPLCService.WriteMachineData(machine, 0, 0, 5);

            if (result.IsSuccess || !machine.IsAutomatic)
            {
                var partProcess = await _partProcessRepository.GetByIdAsync(request.ProcessId);
                if (partProcess == null)
                    return Errors.Part.NotFoundProcess;

                partProcess.EndTime = null;
                partProcess.PendingEndTime = null;
                if (partProcess.StartTime == null)
                    partProcess.StartTime = DateTime.Now;

                partProcess.IsSettingUp = true;
                await _partProcessRepository.UpdateAsync(partProcess);

                _logger.LogInformation($"Bắt đầu gá đặt thành công");

                return new ManufacturingProcessResponse
                {
                    Id = partProcess.Id,
                };
            }
            else
            {
                return Errors.Machine.IsProcessingPLC;
            }
        }
    }
}
