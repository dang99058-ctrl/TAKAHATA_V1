using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Common.Interface.Services;
using TAKAHATA.Contracts.IndexAndPart;
using TAKAHATA.Domain.Common.Errors;

namespace TAKAHATA.Application.Manufacturing.Commands
{
    public class CancelSettingUpCommandHandler : IRequestHandler<CancelSettingUpCommand, ErrorOr<ManufacturingProcessResponse>>
    {
        private readonly ILogger<CancelSettingUpCommandHandler> _logger;
        private readonly IPartProcessRepository _partProcessRepository;
        private readonly IMachineRepository _machineRepository;
        private readonly IPLCService _iPLCService;


        public CancelSettingUpCommandHandler(
            IPLCService iPLCService,
            IMachineRepository machineRepository,
            IPartProcessRepository partProcessRepository,
            ILogger<CancelSettingUpCommandHandler> logger)
        {
            _iPLCService = iPLCService;
            _machineRepository = machineRepository;
            _partProcessRepository = partProcessRepository;
            _logger = logger;
        }

        public async Task<ErrorOr<ManufacturingProcessResponse>> Handle(CancelSettingUpCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Bắt đầu quá trình gá đặt");
            var machineId = request.MachineId;
            var machine = await _machineRepository.GetByIdAsync(machineId);

            if (machine == null)
                return Errors.Machine.NotFound;

            if (await _partProcessRepository.IsCutoverLockedAsync(request.ProcessId))
                return Errors.Part.ProcessCutoverLocked;

            var result = await _iPLCService.WriteMachineData(machine, 0, 0, 7);

            if (result.IsSuccess)
            {
                var partProcess = await _partProcessRepository.GetByIdAsync(request.ProcessId);
                if (partProcess == null)
                    return Errors.Part.NotFoundProcess;

                partProcess.IsSettingUp = false;
                await _partProcessRepository.UpdateAsync(partProcess);

                _logger.LogInformation($"Bắt đầu gá đặt thành công");

                return new ManufacturingProcessResponse
                {
                    Id = partProcess.Id,
                };
            }
            else
            {
                return Errors.Machine.TimeOut;
            }
        }
    }
}
