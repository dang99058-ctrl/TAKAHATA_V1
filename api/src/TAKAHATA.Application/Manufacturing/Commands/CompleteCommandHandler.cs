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
    public class CompleteCommandHandler : IRequestHandler<CompleteCommand, ErrorOr<ManufacturingProcessResponse>>
    {
        private readonly ILogger<CompleteCommandHandler> _logger;
        private readonly IMachineRepository _machineRepository;
        private readonly IPartProcessRepository _partProcessRepository;
        private readonly IPartRepository _partRepository;
        private readonly IPLCService _iPLCService;

        public CompleteCommandHandler(
            IPartProcessRepository partProcessRepository,
            IPartRepository partRepository,
            ILogger<CompleteCommandHandler> logger,
            IMachineRepository machineRepository,
            IPLCService iPLCService)
        {
            _partProcessRepository = partProcessRepository;
            _logger = logger;
            _machineRepository = machineRepository;
            _iPLCService = iPLCService;
            _partRepository = partRepository;
        }

        public async Task<ErrorOr<ManufacturingProcessResponse>> Handle(
            CompleteCommand request,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation(
                    """
                    Bắt đầu hoàn thành quá trình gia công:
                    MachineId={MachineId},
                    PartId={PartId},
                    ProcessId={ProcessId},
                    ProgrammingTime={ProgrammingTime},
                    MachineTime={MachineTime},
                    ProcessedQuantity={ProcessedQuantity},
                    PerformedBy={PerformedBy}
                    """,
                    request.MachineId,
                    request.PartId,
                    request.ProcessId,
                    request.ProgrammingTime,
                    request.MachineTime,
                    request.ProcessedQuantity,
                    request.PerformedBy);

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

                _logger.LogInformation(
                    """
                    Thông tin máy:
                    MachineId={MachineId},
                    MachineName={MachineName},
                    IsAutomatic={IsAutomatic}
                    """,
                    machine.Id,
                    machine.Name,
                    machine.IsAutomatic);

                // Ghi xuống PLC
                //var result = await _iPLCService.WriteMachineData(machine, 0, 0, 3);
                var result = await _iPLCService.WriteMachineStatus(machine, 3);

                _logger.LogInformation(
                    """
                    Kết quả ghi dữ liệu xuống PLC:
                    MachineId={MachineId},
                    IsSuccess={IsSuccess},
                    IsAutomatic={IsAutomatic}
                    """,
                    machineId,
                    result.IsSuccess,
                    machine.IsAutomatic);

                if (result.IsSuccess || !machine.IsAutomatic)
                {
                    var partId = request.PartId;
                    var processId = request.ProcessId;

                    var part = await _partRepository.GetByIdAsync(partId);
                    if (part == null)
                    {
                        _logger.LogWarning(
                            "Không tìm thấy Part. PartId={PartId}",
                            partId);

                        return Errors.Part.NotFound;
                    }

                    var partProcess = await _partProcessRepository.GetByIdAsync(processId);
                    if (partProcess == null)
                    {
                        _logger.LogWarning(
                            "Không tìm thấy công đoạn. ProcessId={ProcessId}",
                            processId);

                        return Errors.Part.NotFoundProcess;
                    }

                    if (request.ProcessedQuantity > part.Quantity)
                    {
                        _logger.LogWarning(
                            """
                            Số lượng gia công không hợp lệ:
                            RequestQuantity={RequestQuantity},
                            PartQuantity={PartQuantity}
                            """,
                            request.ProcessedQuantity,
                            part.Quantity);

                        return Errors.Part.IncorrectQuantity;
                    }

                    _logger.LogInformation(
                        """
                        Giá trị trước khi cộng dồn:
                        WaitingTime={WaitingTime},
                        SetupTime={SetupTime},
                        ProgrammingTime={ProgrammingTime},
                        WorkingTime={WorkingTime},
                        ProcessedQuantity={ProcessedProcessedQuantity},
                        PartQuantity={PartQuantity}
                        """,
                        partProcess.WaitingTime,
                        partProcess.SetupTime,
                        partProcess.ProgrammingTime,
                        partProcess.WorkingTime,
                        partProcess.ProcessedQuantity,
                        part.Quantity);

                    if (partProcess.EndTime.HasValue)
                    {
                        partProcess.PendingEndTime = null;
                    }
                    else
                    {
                        partProcess.PendingEndTime = DateTime.Now;
                    }

                    // Kiểm tra xem số lượng người dùng gửi có hợp lệ không
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

                    // Nếu số lượng lớn hơn kế hoạch thì tự động đặt bằng kế hoạch.
                    var oldProcessedQuantity = partProcess.ProcessedQuantity;

                    partProcess.ProcessedQuantity += request.ProcessedQuantity;

                    _logger.LogInformation(
                        """
                        ProcessedQuantity:
                        {OldQuantity} + {AddedQuantity} = {NewQuantity}
                        """,
                        oldProcessedQuantity,
                        request.ProcessedQuantity,
                        partProcess.ProcessedQuantity);

                    if (processedQuantity + request.ProcessedQuantity == part.Quantity)
                    {
                        var rootProcessId = partProcess.ReferenceId ?? partProcess.Id;
                        var processChain = await _partProcessRepository.GetRelatedProcessesAsync(
                            [rootProcessId]);

                        await _partProcessRepository.RemoveEquivalent(
                            partProcess,
                            part.Code,
                            part.IndexId);

                        foreach (var relatedProcess in processChain)
                            relatedProcess.Status = ProcessStatus.Completed;
                    }
                    else
                        partProcess.Status = ProcessStatus.PartiallyCompleted;

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
                                Máy tự động nhưng ProgrammingTime không có giá trị.
                                MachineId={MachineId},
                                ProcessId={ProcessId}
                                """,
                                machineId,
                                processId);
                        }
                    }
                    else
                    {
                        if (request.MachineTime.HasValue)
                        {
                            var oldWorkingTime =
                                partProcess.WorkingTime;

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
                                Máy thủ công nhưng MachineTime không có giá trị.
                                MachineId={MachineId},
                                ProcessId={ProcessId}
                                """,
                                machineId,
                                processId);
                        }
                    }

                    _logger.LogInformation(
                        """
                        Dữ liệu trước khi cập nhật:
                        WaitingTime={WaitingTime},
                        SetupTime={SetupTime},
                        ProgrammingTime={ProgrammingTime},
                        WorkingTime={WorkingTime},
                        ProcessedQuantity={ProcessedQuantity},
                        Status={Status},
                        EndTime={EndTime}
                        """,
                        partProcess.WaitingTime,
                        partProcess.SetupTime,
                        partProcess.ProgrammingTime,
                        partProcess.WorkingTime,
                        partProcess.ProcessedQuantity,
                        partProcess.Status,
                        partProcess.EndTime);

                    partProcess = await _partProcessRepository.UpdateAsync(partProcess);

                    if (partProcess == null)
                    {
                        _logger.LogError(
                            """
                            Cập nhật PartProcess thất bại.
                            ProcessId={ProcessId}
                            """,
                            processId);

                        return Errors.Part.UpdatePartProcessFalse;
                    }

                    _logger.LogInformation(
                        """
                        Hoàn thành quá trình gia công thành công:
                        ProcessId={ProcessId},
                        WaitingTime={WaitingTime},
                        SetupTime={SetupTime},
                        ProgrammingTime={ProgrammingTime},
                        WorkingTime={WorkingTime},
                        ProcessedQuantity={ProcessedQuantity},
                        Status={Status}
                        """,
                        partProcess.Id,
                        partProcess.WaitingTime,
                        partProcess.SetupTime,
                        partProcess.ProgrammingTime,
                        partProcess.WorkingTime,
                        partProcess.ProcessedQuantity,
                        partProcess.Status);

                    return new ManufacturingProcessResponse
                    {
                        Id = partProcess.Id,
                    };
                }
                else
                {
                    _logger.LogError(
                        """
                        Không thể hoàn thành quá trình gia công do ghi PLC thất bại:
                        MachineId={MachineId},
                        ProcessId={ProcessId}
                        """,
                        request.MachineId,
                        request.ProcessId);

                    return Errors.Machine.IsProcessingPLC;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    """
                    Lỗi không hoàn thành được process:
                    MachineId={MachineId},
                    PartId={PartId},
                    ProcessId={ProcessId}
                    """,
                    request.MachineId,
                    request.PartId,
                    request.ProcessId);

                return Errors.Part.CompletePartProcessFalse;
            }
        }
    }
}
