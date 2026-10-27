using ErrorOr;
using MapsterMapper;
using MediatR;
using TAKAHATA.Application.Common.Interface;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Contracts.IndexAndPart;
using TAKAHATA.Domain.Common.Errors;
using TAKAHATA.Domain.MasterData;
using Index = TAKAHATA.Domain.MasterData.Index;

namespace TAKAHATA.Application.IndexAndPart.Commands
{
    public class UpdatePartCommandHandler : IRequestHandler<UpdatePartCommand, ErrorOr<PartResponse>>
    {
        private readonly IMapper _mapper;
        private readonly IPartRepository _partRepository;
        private readonly IIndexRepository _indexRepository;
        private readonly ICurrentUserService _currentUserService;

        public UpdatePartCommandHandler(
            IMapper mapper,
            IPartRepository partRepository,
            IIndexRepository indexRepository,
            ICurrentUserService currentUserService)
        {
            _partRepository = partRepository;
            _indexRepository = indexRepository;
            _currentUserService = currentUserService;
            _mapper = mapper;
        }

        public async Task<ErrorOr<PartResponse>> Handle(
            UpdatePartCommand request,
            CancellationToken cancellationToken)
        {
            var part = await _partRepository.GetByIdAsync(request.Id);
            if (part == null)
                return Errors.Part.NotFound;
            if (part.IsClosed)
                return Errors.Part.IsClosed;

            // 1. Create Index if not exists
            var index = await _indexRepository.GetAsync(request.IndexCode);
            if (index == null)
            {
                index = new Index
                {
                    Code = request.IndexCode,
                    CreateBy = _currentUserService.UserId,
                    CreateAt = DateTime.Now
                };

                index = await _indexRepository.AddAsync(index);

                if (index == null)
                    return Errors.Index.CreateFalse;
            }

            //if (await _partRepository.IsExistedAsync(request.PartCode) && part.Code != request.PartCode)
            //    return Errors.Part.DuplicatePartCode;

            // 3. Update Part
            part.Code = request.PartCode;
            part.IndexId = index.Id;
            part.UpdateBy = _currentUserService.UserId;
            part.UpdateAt = DateTime.Now;
            part.Quantity = request.Quantity != 0 ? request.Quantity : 1;
            part.Prioritize = request.Prioritize;
            part.PartType = request.PartType;
            part.NewRepair = request.NewRepair;

            var requestProcesses = request.PartProcesses ?? new List<PartProcessRequest>();

            var newProcesses = requestProcesses
                .Where(x => x.Id == 0)
                .ToList();

            var updateProcesses = requestProcesses
                .Where(x => x.Id != 0)
                .ToList();

            var cutoverLockedProcesses = part.Processes
                .Where(process => IsCutoverLocked(process, part.Processes))
                .ToList();
            foreach (var lockedProcess in cutoverLockedProcesses)
            {
                var requestedProcess = updateProcesses
                    .FirstOrDefault(process => process.Id == lockedProcess.Id);

                if (requestedProcess == null ||
                    requestedProcess.MachineId != lockedProcess.MachineId ||
                    requestedProcess.PlantDate != lockedProcess.PlantDate ||
                    requestedProcess.StartTime != lockedProcess.StartTime ||
                    requestedProcess.EndTime != lockedProcess.EndTime ||
                    requestedProcess.WorkingTime != lockedProcess.WorkingTime ||
                    requestedProcess.SetupTime != lockedProcess.SetupTime ||
                    requestedProcess.WaitingTime != lockedProcess.WaitingTime ||
                    requestedProcess.Status != lockedProcess.Status ||
                    requestedProcess.EstimatedTime != lockedProcess.EstimatedTime ||
                    requestedProcess.ProgrammingTime != lockedProcess.ProgrammingTime ||
                    (requestedProcess.ProcessedQuantity ?? 0) != lockedProcess.ProcessedQuantity ||
                    requestedProcess.PerformedBy != lockedProcess.PerformedBy ||
                    (requestedProcess.Note ?? string.Empty) != (lockedProcess.Note ?? string.Empty) ||
                    (requestedProcess.WorkerNote ?? string.Empty) != lockedProcess.WorkerNote)
                {
                    return Errors.Part.ProcessCutoverLocked;
                }
            }

            // 4. Remove deleted processes
            var requestUpdateProcessIds = updateProcesses
                .Select(x => x.Id)
                .ToList();

            var removeProcesses = part.Processes
                .Where(x => !requestUpdateProcessIds.Contains(x.Id))
                .ToList();

            foreach (var removeProcess in removeProcesses)
            {
                if (cutoverLockedProcesses.Any(process => process.Id == removeProcess.Id))
                    return Errors.Part.ProcessCutoverLocked;

                part.Processes.Remove(removeProcess);
            }

            // 5. Update existing processes
            foreach (var process in updateProcesses)
            {
                if (cutoverLockedProcesses.Any(existing => existing.Id == process.Id))
                    continue;

                var existingProcess = part.Processes
                    .FirstOrDefault(x => x.Id == process.Id);

                if (existingProcess == null)
                    return Errors.Part.NotFoundProcess;

                existingProcess.Note = process.Note;
                existingProcess.WorkerNote = process.WorkerNote ?? string.Empty;
                existingProcess.MachineId = process.MachineId;
                existingProcess.PlantDate = process.PlantDate;
                existingProcess.StartTime = process.StartTime;
                existingProcess.EndTime = process.EndTime;
                existingProcess.WorkingTime = process.WorkingTime;
                existingProcess.SetupTime = process.SetupTime;
                existingProcess.WaitingTime = process.WaitingTime;
                existingProcess.Status = process.Status;
                existingProcess.PerformedBy = process.PerformedBy;

                existingProcess.UpdateBy = _currentUserService.UserId;
                existingProcess.UpdateAt = DateTime.Now;
                existingProcess.EstimatedTime = process.EstimatedTime;
                existingProcess.ProgrammingTime = process.ProgrammingTime;
                existingProcess.ProcessedQuantity = process.ProcessedQuantity ?? 0;
            }

            // 6. Add new processes
            foreach (var process in newProcesses)
            {
                part.Processes.Add(new PartProcess
                {
                    CreateAt = DateTime.Now,
                    CreateBy = _currentUserService.UserId,

                    MachineId = process.MachineId,
                    Note = process.Note,
                    WorkerNote = process.WorkerNote ?? string.Empty,
                    Status = process.Status,
                    PlantDate = process.PlantDate,
                    StartTime = process.StartTime,
                    EndTime = process.EndTime,
                    WorkingTime = process.WorkingTime,
                    SetupTime = process.SetupTime,
                    WaitingTime = process.WaitingTime,
                    PerformedBy = process.PerformedBy,
                    EstimatedTime = process.EstimatedTime,
                    ProgrammingTime = process.ProgrammingTime,
                    ProcessedQuantity = process.ProcessedQuantity ?? 0,
                });
            }

            part.Processes
                .OrderBy(p => p.PlantDate)
                .Select((p, index) => new { Process = p, Sequence = index + 1 })
                .ToList()
                .ForEach(x =>
                {
                    if (!cutoverLockedProcesses.Any(process => process.Id == x.Process.Id))
                        x.Process.Sequence = x.Sequence;
                });

            // 7. Save update
            part = await _partRepository.UpdateAsync(part);

            if (part == null)
                return Errors.Part.UpdateFalse;

            return _mapper.Map<PartResponse>(part);
        }

        private static bool IsCutoverLocked(
            PartProcess process,
            IEnumerable<PartProcess> partProcesses)
        {
            if (!process.EndTime.HasValue ||
                process.EndTime.Value.TimeOfDay != TimeSpan.Zero)
            {
                return false;
            }

            var rootProcessId = process.ReferenceId ?? process.Id;
            return partProcesses.Any(candidate =>
                (candidate.ReferenceId == rootProcessId ||
                 candidate.ReferenceId == process.Id) &&
                candidate.PlantDate > process.PlantDate);
        }
    }
}
