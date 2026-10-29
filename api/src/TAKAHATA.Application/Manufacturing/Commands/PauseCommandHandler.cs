using ErrorOr;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Common.Interface.Services;
using TAKAHATA.Contracts.IndexAndPart;
using TAKAHATA.Domain.Common.Errors;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Application.Manufacturing.Commands
{
    public class PauseCommandHandler : IRequestHandler<PauseCommand, ErrorOr<ManufacturingProcessResponse>>
    {
        private readonly IMachineRepository _machineRepository;
        private readonly IPartProcessRepository _partProcessRepository;
        private readonly IPLCService _iPLCService;
        private readonly IPartRepository _partRepository;

        private readonly ILogger<PauseCommandHandler> _logger;

        public PauseCommandHandler(
            IPartProcessRepository partProcessRepository,
            IMachineRepository machineRepository,
            IPLCService iPLCService,
            IPartRepository partRepository,
            ILogger<PauseCommandHandler> logger)
        {
            _partProcessRepository = partProcessRepository;
            _machineRepository = machineRepository;
            _iPLCService = iPLCService;
            _partRepository = partRepository;
            _logger = logger;
        }

        public async Task<ErrorOr<ManufacturingProcessResponse>> Handle(
            PauseCommand request,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                """
                Bắt đầu tạm dừng quá trình gia công:
                MachineId={MachineId},
                PartId={PartId},
                ProcessId={ProcessId},
                ProgrammingTime={ProgrammingTime},
                MachineTime={MachineTime}
                """,
                request.MachineId,
                request.PartId,
                request.ProcessId,
                request.ProgrammingTime,
                request.MachineTime);

            var machineId = request.MachineId;
            var machine = await _machineRepository.GetByIdAsync(machineId);

            if (machine == null)
            {
                _logger.LogWarning(
                    "Không tìm thấy máy. MachineId={MachineId}",
                    machineId);

                return Errors.Machine.NotFound;
            }

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

            // Ghi xuống PLC
            //var result = await _iPLCService.WriteMachineData(machine, 0, 0, 2);
            var result = await _iPLCService.WriteMachineStatus(machine, 2);

            if (result.IsSuccess || !machine.IsAutomatic)
            {
                var partProcess = await _partProcessRepository.GetByIdAsync(request.ProcessId);
                if (partProcess == null)
                {
                    _logger.LogWarning(
                        "Không tìm thấy công đoạn. ProcessId={ProcessId}",
                        request.ProcessId);

                    return Errors.Part.NotFoundProcess;
                }

                var part = await _partRepository.GetByIdAsync(request.PartId);
                if (part == null)
                {
                    _logger.LogWarning(
                        "Không tìm thấy Part. PartId={PartId}",
                        request.PartId);

                    return Errors.Part.NotFound;
                }

                var processedQuantity =
                    await _partProcessRepository.GetProcessedQuantityByReferenceId(
                        partProcess.ReferenceId ?? partProcess.Id);

                if (processedQuantity + request.ProcessedQuantity > part.Quantity)
                {
                    _logger.LogWarning(
                        """
                        Số lượng gia công không hợp lệ.
                        ProcessedQuantity={ProcessedQuantity},
                        PartQuantity={PartQuantity}
                        """,
                        processedQuantity + request.ProcessedQuantity,
                        part.Quantity);

                    return Errors.Part.IncorrectQuantity;
                }

                partProcess.Status = ProcessStatus.Paused;

                _logger.LogInformation(
                    """
                    Thời gian trước khi cộng dồn:
                    WaitingTime={WaitingTime},
                    SetupTime={SetupTime},
                    ProgrammingTime={ProgrammingTime},
                    WorkingTime={WorkingTime}
                    """,
                    partProcess.WaitingTime,
                    partProcess.SetupTime,
                    partProcess.ProgrammingTime,
                    partProcess.WorkingTime);

                partProcess.PendingEndTime = DateTime.Now;
                partProcess.EndTime = null;

                // Nếu số lượng > kế hoạch tự động set bằng.
                partProcess.ProcessedQuantity += request.ProcessedQuantity;

                if (partProcess.ProcessedQuantity > part.Quantity)
                    partProcess.ProcessedQuantity = part.Quantity;

                partProcess.PerformedBy = request.PerformedBy;

                if (!string.IsNullOrEmpty(request.WorkerNote))
                    partProcess.WorkerNote = request.WorkerNote;

                if (machine.IsAutomatic)
                {
                    if (request.ProgrammingTime.HasValue)
                    {
                        var oldProgrammingTime = partProcess.ProgrammingTime;

                        partProcess.ProgrammingTime = Math.Round(
                            partProcess.ProgrammingTime.GetValueOrDefault()
                                + request.ProgrammingTime.Value,
                            2,
                            MidpointRounding.AwayFromZero);

                        _logger.LogInformation(
                            "ProgrammingTime: {OldValue} + {AddedValue} = {NewValue}",
                            oldProgrammingTime,
                            request.ProgrammingTime.Value,
                            partProcess.ProgrammingTime);
                    }
                    else
                    {
                        _logger.LogWarning(
                            """
                            ProgrammingTime không có giá trị.
                            MachineId={MachineId}, ProcessId={ProcessId}
                            """,
                            request.MachineId,
                            request.ProcessId);
                    }
                }
                else
                {
                    if (request.MachineTime.HasValue)
                    {
                        var oldWorkingTime = partProcess.WorkingTime;

                        partProcess.WorkingTime = Math.Round(
                            partProcess.WorkingTime.GetValueOrDefault()
                                + request.MachineTime.Value,
                            2,
                            MidpointRounding.AwayFromZero);

                        _logger.LogInformation(
                            "WorkingTime: {OldValue} + {AddedValue} = {NewValue}",
                            oldWorkingTime,
                            request.MachineTime.Value,
                            partProcess.WorkingTime);
                    }
                    else
                    {
                        _logger.LogWarning(
                            """
                            MachineTime không có giá trị.
                            MachineId={MachineId}, ProcessId={ProcessId}
                            """,
                            request.MachineId,
                            request.ProcessId);
                    }
                }

                _logger.LogInformation(
                    """
                    Dữ liệu trước khi cập nhật:
                    WaitingTime={WaitingTime},
                    SetupTime={SetupTime},
                    ProgrammingTime={ProgrammingTime},
                    WorkingTime={WorkingTime},
                    ProcessedQuantity={ProcessedQuantity}
                    """,
                    partProcess.WaitingTime,
                    partProcess.SetupTime,
                    partProcess.ProgrammingTime,
                    partProcess.WorkingTime,
                    partProcess.ProcessedQuantity);

                partProcess =
                    await _partProcessRepository.UpdateAsync(partProcess);

                if (partProcess == null)
                {
                    _logger.LogError(
                        "Cập nhật công đoạn thất bại. ProcessId={ProcessId}",
                        request.ProcessId);

                    return Errors.Part.UpdatePartProcessFalse;
                }

                _logger.LogInformation(
                    """
                    Tạm dừng quá trình gia công thành công.
                    ProcessId={ProcessId},
                    WaitingTime={WaitingTime},
                    SetupTime={SetupTime},
                    ProgrammingTime={ProgrammingTime},
                    WorkingTime={WorkingTime}
                    """,
                    partProcess.Id,
                    partProcess.WaitingTime,
                    partProcess.SetupTime,
                    partProcess.ProgrammingTime,
                    partProcess.WorkingTime);

                return new ManufacturingProcessResponse
                {
                    Id = partProcess.Id,
                };
            }
            else
            {
                _logger.LogError(
                    """
                    Không thể tạm dừng quá trình gia công do ghi PLC thất bại.
                    MachineId={MachineId}, ProcessId={ProcessId}
                    """,
                    request.MachineId,
                    request.ProcessId);

                return Errors.Machine.IsProcessingPLC;
            }
        }
    }
}
