using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Contracts.Common;
using TAKAHATA.Contracts.Equipment;
using TAKAHATA.Contracts.IndexAndPart;
using TAKAHATA.Domain.DataLog;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Infrastructure.Persistence.Repositories
{
    public class PartRepository : BaseRepository<Part>, IPartRepository
    {
        private readonly IMapper _mapper;

        public PartRepository(
            AppDbContext dbContext,
            IMapper mapper) : base(dbContext)
        {
            _mapper = mapper;
        }

        public override async Task<Part?> DeleteByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var part = await _dbContext.Parts
                .FirstOrDefaultAsync(item => item.Id == id && !item.IsClosed, cancellationToken);

            if (part == null)
                return null;

            _dbContext.Parts.Remove(part);
            var affectedRows = await _dbContext.SaveChangesAsync(cancellationToken);
            if (affectedRows == 0)
                return null;

            var hasRemainingParts = await _dbContext.Parts
                .IgnoreQueryFilters()
                .AnyAsync(item => item.IndexId == part.IndexId, cancellationToken);
            if (!hasRemainingParts)
            {
                var index = await _dbContext.Indexes
                    .FirstOrDefaultAsync(item => item.Id == part.IndexId, cancellationToken);
                if (index != null)
                {
                    _dbContext.Indexes.Remove(index);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            return part;
        }

        public async Task<Part?> CloseByIdAsync(
            int id,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            var part = await _dbContext.Parts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (part == null || part.IsClosed)
                return null;

            part.IsClosed = true;
            part.UpdateBy = userId;
            part.UpdateAt = DateTime.Now;

            var affectedRows = await _dbContext.SaveChangesAsync(cancellationToken);
            return affectedRows > 0 ? part : null;
        }

        public override async Task<Part?> UpdateAsync(Part entity, CancellationToken cancellationToken = default)
        {
            if (entity.IsClosed)
                return null;

            await SynchronizeEditedProcessTimesAsync(entity, cancellationToken);

            var result = await base.UpdateAsync(entity, cancellationToken);
            if (result != null)
            {
                var indexes = await _dbContext.Indexes
                    .Where(index => !_dbContext.Parts
                        .IgnoreQueryFilters()
                        .Any(part => part.IndexId == index.Id))
                    .ToArrayAsync(cancellationToken);
                if (indexes != null && indexes.Count() != 0)
                {
                    _dbContext.Indexes.RemoveRange(indexes);
                    await _dbContext.SaveChangesAsync();
                }
            }

            return result;
        }

        private async Task SynchronizeEditedProcessTimesAsync(
            Part part,
            CancellationToken cancellationToken)
        {
            var edits = part.Processes
                .Where(process => process.Id != 0)
                .Select(process =>
                {
                    var entry = _dbContext.Entry(process);
                    var originalWorkingTime = entry.Property(item => item.WorkingTime).OriginalValue;
                    var originalWaitingTime = entry.Property(item => item.WaitingTime).OriginalValue;
                    var originalSetupTime = entry.Property(item => item.SetupTime).OriginalValue;

                    return new ProcessTimeEdit(
                        process,
                        entry.Property(item => item.MachineId).OriginalValue,
                        entry.Property(item => item.PlantDate).OriginalValue,
                        originalWorkingTime,
                        originalWaitingTime,
                        originalSetupTime,
                        originalWorkingTime != process.WorkingTime ||
                        originalWaitingTime != process.WaitingTime ||
                        originalSetupTime != process.SetupTime);
                })
                .Where(edit => edit.HasTimeChanges)
                .ToList();

            if (edits.Count == 0)
                return;

            var processIds = edits.Select(edit => edit.Process.Id).Distinct().ToList();
            var machineIds = edits
                .SelectMany(edit => new[]
                {
                    edit.OriginalMachineId,
                    edit.Process.MachineId
                })
                .Distinct()
                .ToList();
            var reportDates = edits
                .SelectMany(edit => new[]
                {
                    edit.OriginalPlantDate,
                    edit.Process.PlantDate
                })
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .Distinct()
                .ToList();

            var processSummaries = await _dbContext.PartProcessDailyTimeSummaries
                .Where(summary =>
                    processIds.Contains(summary.PartProcessId) ||
                    machineIds.Contains(summary.MachineId) &&
                    reportDates.Contains(summary.ReportDate))
                .ToListAsync(cancellationToken);
            var machineSummaries = await _dbContext.MachineDailyTimeSummaries
                .Where(summary =>
                    machineIds.Contains(summary.MachineId) &&
                    reportDates.Contains(summary.ReportDate))
                .ToListAsync(cancellationToken);

            var machineTimeDeltas =
                new Dictionary<(int MachineId, DateOnly ReportDate), TimeValues>();
            var targetMachineDates =
                new HashSet<(int MachineId, DateOnly ReportDate)>();

            foreach (var edit in edits)
            {
                var process = edit.Process;
                var oldKey = edit.OriginalPlantDate.HasValue
                    ? (edit.OriginalMachineId, edit.OriginalPlantDate.Value)
                    : ((int MachineId, DateOnly ReportDate)?)null;
                var newKey = process.PlantDate.HasValue
                    ? (process.MachineId, process.PlantDate.Value)
                    : ((int MachineId, DateOnly ReportDate)?)null;

                var oldSummary = oldKey.HasValue
                    ? processSummaries.FirstOrDefault(summary =>
                        summary.PartProcessId == process.Id &&
                        summary.MachineId == oldKey.Value.MachineId &&
                        summary.ReportDate == oldKey.Value.ReportDate)
                    : null;
                var targetSummary = newKey.HasValue
                    ? processSummaries.FirstOrDefault(summary =>
                        summary.PartProcessId == process.Id &&
                        summary.MachineId == newKey.Value.MachineId &&
                        summary.ReportDate == newKey.Value.ReportDate)
                    : null;

                var oldValues = oldSummary == null
                    ? new TimeValues(
                        edit.OriginalWorkingTime ?? 0,
                        edit.OriginalWaitingTime ?? 0,
                        edit.OriginalSetupTime ?? 0)
                    : new TimeValues(
                        oldSummary.WorkingTime,
                        oldSummary.WaitingTime,
                        oldSummary.SetupTime);
                var newValues = new TimeValues(
                    process.WorkingTime ?? 0,
                    process.WaitingTime ?? 0,
                    process.SetupTime ?? 0);

                if (oldKey == newKey && newKey.HasValue)
                {
                    var targetPreviousValues = targetSummary == null
                        ? oldValues
                        : new TimeValues(
                            targetSummary.WorkingTime,
                            targetSummary.WaitingTime,
                            targetSummary.SetupTime);
                    targetSummary ??= AddProcessSummary(
                        processSummaries,
                        process.Id,
                        newKey.Value.MachineId,
                        newKey.Value.ReportDate);

                    SetProcessSummaryTimes(targetSummary, newValues);
                    AddMachineTimeDelta(
                        machineTimeDeltas,
                        newKey.Value,
                        newValues - targetPreviousValues);
                    targetMachineDates.Add(newKey.Value);
                    continue;
                }

                if (oldKey.HasValue)
                {
                    if (oldSummary != null)
                        SetProcessSummaryTimes(oldSummary, TimeValues.Zero);

                    AddMachineTimeDelta(
                        machineTimeDeltas,
                        oldKey.Value,
                        TimeValues.Zero - oldValues);
                }

                if (newKey.HasValue)
                {
                    var targetPreviousValues = targetSummary == null
                        ? TimeValues.Zero
                        : new TimeValues(
                            targetSummary.WorkingTime,
                            targetSummary.WaitingTime,
                            targetSummary.SetupTime);
                    targetSummary ??= AddProcessSummary(
                        processSummaries,
                        process.Id,
                        newKey.Value.MachineId,
                        newKey.Value.ReportDate);
                    SetProcessSummaryTimes(targetSummary, newValues);
                    AddMachineTimeDelta(
                        machineTimeDeltas,
                        newKey.Value,
                        newValues - targetPreviousValues);
                    targetMachineDates.Add(newKey.Value);
                }
            }

            foreach (var (key, delta) in machineTimeDeltas)
            {
                var machineSummary = machineSummaries.FirstOrDefault(summary =>
                    summary.MachineId == key.MachineId &&
                    summary.ReportDate == key.ReportDate);

                if (machineSummary == null)
                {
                    if (!targetMachineDates.Contains(key))
                        continue;

                    machineSummary = new MachineDailyTimeSummary
                    {
                        MachineId = key.MachineId,
                        ReportDate = key.ReportDate,
                        WorkingTime = processSummaries
                            .Where(summary =>
                                summary.MachineId == key.MachineId &&
                                summary.ReportDate == key.ReportDate)
                            .Sum(summary => summary.WorkingTime),
                        WaitingTime = processSummaries
                            .Where(summary =>
                                summary.MachineId == key.MachineId &&
                                summary.ReportDate == key.ReportDate)
                            .Sum(summary => summary.WaitingTime),
                        SetupTime = processSummaries
                            .Where(summary =>
                                summary.MachineId == key.MachineId &&
                                summary.ReportDate == key.ReportDate)
                            .Sum(summary => summary.SetupTime)
                    };
                    _dbContext.MachineDailyTimeSummaries.Add(machineSummary);
                    machineSummaries.Add(machineSummary);
                    continue;
                }

                machineSummary.WorkingTime = Math.Round(
                    machineSummary.WorkingTime + delta.WorkingTime,
                    2);
                machineSummary.WaitingTime = Math.Round(
                    machineSummary.WaitingTime + delta.WaitingTime,
                    2);
                machineSummary.SetupTime = Math.Round(
                    machineSummary.SetupTime + delta.SetupTime,
                    2);
            }
        }

        private PartProcessDailyTimeSummary AddProcessSummary(
            List<PartProcessDailyTimeSummary> processSummaries,
            int processId,
            int machineId,
            DateOnly reportDate)
        {
            var summary = new PartProcessDailyTimeSummary
            {
                PartProcessId = processId,
                MachineId = machineId,
                ReportDate = reportDate
            };
            _dbContext.PartProcessDailyTimeSummaries.Add(summary);
            processSummaries.Add(summary);
            return summary;
        }

        private static void SetProcessSummaryTimes(
            PartProcessDailyTimeSummary summary,
            TimeValues values)
        {
            summary.WorkingTime = values.WorkingTime;
            summary.WaitingTime = values.WaitingTime;
            summary.SetupTime = values.SetupTime;
        }

        private static void AddMachineTimeDelta(
            Dictionary<(int MachineId, DateOnly ReportDate), TimeValues> deltas,
            (int MachineId, DateOnly ReportDate) key,
            TimeValues delta)
        {
            deltas[key] = deltas.GetValueOrDefault(key, TimeValues.Zero) + delta;
        }

        private sealed record ProcessTimeEdit(
            PartProcess Process,
            int OriginalMachineId,
            DateOnly? OriginalPlantDate,
            double? OriginalWorkingTime,
            double? OriginalWaitingTime,
            double? OriginalSetupTime,
            bool HasTimeChanges);

        private readonly record struct TimeValues(
            double WorkingTime,
            double WaitingTime,
            double SetupTime)
        {
            public static TimeValues Zero => new(0, 0, 0);

            public static TimeValues operator +(TimeValues left, TimeValues right) =>
                new(
                    left.WorkingTime + right.WorkingTime,
                    left.WaitingTime + right.WaitingTime,
                    left.SetupTime + right.SetupTime);

            public static TimeValues operator -(TimeValues left, TimeValues right) =>
                new(
                    left.WorkingTime - right.WorkingTime,
                    left.WaitingTime - right.WaitingTime,
                    left.SetupTime - right.SetupTime);
        }

        public async Task<SearchResponse<List<PartResponse>>> SearchAsync(
            string? indexCode,
            string? partCode,
            List<ProcessStatus>? statuses,
            DateOnly? plantFrom,
            DateOnly? plantTo,
            DateTime? startFrom,
            DateTime? startTo,
            int page,
            int pageSize)
        {

            var currentPage = Math.Max(page, 1);
            var currentPageSize = Math.Clamp(pageSize, 1, 100);

            var query = _dbContext.PartProcesses
                .AsNoTracking()
                .AsQueryable();

            // Tìm kiếm theo index code
            if (!string.IsNullOrWhiteSpace(indexCode))
            {
                var indexIds = _dbContext.Indexes
                    .Where(i => i.Code.Contains(indexCode.Trim()))
                    .Select(i => i.Id)
                    .Distinct()
                    .ToList();

                if (indexIds.Count != 0)
                {
                    var partIds = _dbContext.Parts
                        .Where(p => indexIds.Contains(p.IndexId))
                        .Select(i => i.Id)
                        .Distinct()
                        .ToList();

                    if (partIds.Count != 0)
                        query = query.Where(x => partIds.Contains(x.PartId));
                    else
                        return new SearchResponse<List<PartResponse>>(
                            currentPage,
                            currentPageSize,
                            0,
                            []);
                }
                else
                    return new SearchResponse<List<PartResponse>>(
                        currentPage,
                        currentPageSize,
                        0,
                        []);
            }

            // Tìm kiếm theo mã Part
            if (!string.IsNullOrWhiteSpace(partCode))
            {
                var keyword = partCode.Trim();

                query = query.Where(x =>
                    x.Part != null &&
                    x.Part.Code.Contains(keyword));
            }

            // Lọc theo trạng thái
            if (statuses is { Count: > 0 })
            {
                query = query.Where(x =>
                    x.Part != null &&
                    (x.Part.IsClosed || statuses.Contains(x.Status)));
            }

            // Lọc ngày kế hoạch
            if (plantFrom.HasValue)
            {
                query = query.Where(x =>
                    x.Part != null &&
                    x.PlantDate.HasValue &&
                    x.PlantDate.Value >= plantFrom.Value);
            }

            if (plantTo.HasValue)
            {
                query = query.Where(x =>
                    x.Part != null &&
                    x.PlantDate.HasValue &&
                    x.PlantDate.Value <= plantTo.Value);
            }

            // Lọc ngày thực tế
            if (startFrom.HasValue)
            {
                query = query.Where(x =>
                    x.Part != null &&
                    (x.Part.IsClosed ||
                     (x.StartTime.HasValue && x.StartTime.Value.Date >= startFrom.Value)));
            }

            if (startTo.HasValue)
            {
                query = query.Where(x =>
                    x.Part != null &&
                    (x.Part.IsClosed ||
                     (x.StartTime.HasValue && x.StartTime.Value.Date <= startTo.Value)));
            }


            // Mỗi Part chỉ được tính là một item
            var groupedQuery = query
                .GroupBy(x => x.PartId)
                .Select(group => new
                {
                    PartId = group.Key,
                    LatestPlantDate = group.Max(x => x.PlantDate)
                });

            var totalItems = await groupedQuery.CountAsync();

            var pagedPartIds = await groupedQuery
                .OrderByDescending(x => x.LatestPlantDate)
                .ThenBy(x => x.PartId)
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
                .Select(x => x.PartId)
                .ToListAsync();

            var parts = await _dbContext.Parts
                .AsNoTracking()
                .Where(x => pagedPartIds.Contains(x.Id))
                .Include(x => x.Index)
                .Include(x => x.Processes)
                .ToListAsync();

            // Giữ đúng thứ tự phân trang ban đầu
            var partOrder = pagedPartIds
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index);

            parts = parts
                .OrderBy(x => partOrder[x.Id])
                .ToList();

            var machines = _dbContext.Machines.ToList();

            var data = parts
                .Select(x =>
                {
                    var part = _mapper.Map<PartResponse>(x);
                    part.Processes = part.Processes
                        .Where(p =>
                            (!plantFrom.HasValue ||
                             (p.PlantDate.HasValue && p.PlantDate.Value >= plantFrom.Value)) &&
                            (!plantTo.HasValue ||
                             (p.PlantDate.HasValue && p.PlantDate.Value <= plantTo.Value)))
                        .OrderBy(p => p.Sequence)
                        .ToList();

                    foreach (var processes in part.Processes)
                    {
                        var machine = machines.FirstOrDefault(m => m.Id == processes.MachineId);
                        if (machine != null)
                            processes.Machine = _mapper.Map<MachineResponse>(machine);
                    }

                    return part;
                })
                .OrderBy(d => d.CreateAt)
                .ToList();

            return new SearchResponse<List<PartResponse>>(
                currentPage,
                currentPageSize,
                totalItems,
                data);
        }

        public async Task<bool> IsExistedAsync(string partCode)
        {
            var part = await _dbContext.Parts.FirstOrDefaultAsync(p => p.Code == partCode);
            return part != null;
        }

        public override async Task<Part?> GetByIdAsync(int id)
        {
            return await _dbContext.Parts
                .Include(p => p.Index)
                .Include(p => p.Processes)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<Part?> ApproveAsync(int id)
        {
            var part = _dbContext.Parts.FirstOrDefault(x => x.Id == id && !x.IsClosed);
            if (part == null)
                return null;

            part.Status = ProcessStatus.Planned;
            _dbContext.Parts.Update(part);

            var effectedRow = await _dbContext.SaveChangesAsync();

            return effectedRow != 0 ? part : null;
        }

        public async Task<IEnumerable<Part>> GetByIdsAsync(HashSet<int> partIds)
        {
            return await _dbContext.Parts
                .Where(p => partIds.Contains(p.Id))
                .Include(p => p.Index)
                .ToListAsync();
        }
    }
}
