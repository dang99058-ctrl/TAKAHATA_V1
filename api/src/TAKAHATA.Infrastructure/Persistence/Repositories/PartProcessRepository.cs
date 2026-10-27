using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Services;
using TAKAHATA.Contracts.Common;
using TAKAHATA.Contracts.IndexAndPart;
using TAKAHATA.Contracts.Report;
using TAKAHATA.Domain.MasterData;
using Index = TAKAHATA.Domain.MasterData.Index;

namespace TAKAHATA.Infrastructure.Persistence.Repositories
{
    public class PartProcessRepository : BaseRepository<PartProcess>, IPartProcessRepository
    {
        private readonly ILogger<PartProcessRepository> _logger;

        public PartProcessRepository(
            AppDbContext dbContext,
            ILogger<PartProcessRepository> logger) : base(dbContext)
        {
            _logger = logger;
        }

        public override async Task<PartProcess?> GetByIdAsync(int id)
        {
            return await _dbContext.PartProcesses
                .FirstOrDefaultAsync(process =>
                    process.Id == id &&
                    process.Part != null &&
                    !process.Part.IsClosed);
        }

        public async Task<bool> UpdateWorkingTimeAsync(
            int id,
            double? workingTime,
            CancellationToken cancellationToken = default)
        {
            var affectedRows = await _dbContext.PartProcesses
                .Where(x =>
                    x.Id == id &&
                    x.Part != null &&
                    !x.Part.IsClosed)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        x => x.WorkingTime,
                        workingTime),
                    cancellationToken);

            return affectedRows > 0;
        }

        public async Task HandleExtend()
        {
            var today = VietnamTime.Today;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                var partProcesses = await _dbContext.PartProcesses
                    .Where(pp =>
                        pp.Part != null &&
                        !pp.Part.IsClosed &&
                        pp.PlantDate < today &&
                        (pp.Status == ProcessStatus.Paused ||
                         pp.Status == ProcessStatus.PartiallyCompleted ||
                         pp.Status == ProcessStatus.InProgress ||
                         pp.Status == ProcessStatus.Planned))
                    .Include(pp => pp.Part)
                    .OrderByDescending(pp => pp.Id)
                    .ToListAsync();

                if (partProcesses.Count == 0)
                {
                    await transaction.CommitAsync();
                    return;
                }

                var processedRoots = new HashSet<int>();
                foreach (var partProcess in partProcesses)
                {
                    var part = partProcess.Part;
                    if (part == null)
                        continue;

                    var rootProcessId = partProcess.ReferenceId ?? partProcess.Id;
                    if (!processedRoots.Add(rootProcessId))
                    {
                        _logger.LogWarning(
                            "Bỏ qua bản ghi gia hạn trùng trong cùng một chuỗi công đoạn. " +
                            "ProcessId={ProcessId}, RootProcessId={RootProcessId}, PlantDate={PlantDate}",
                            partProcess.Id,
                            rootProcessId,
                            partProcess.PlantDate);
                        continue;
                    }

                    var processedQuantity = await GetProcessedQuantityByReferenceId(
                        rootProcessId);

                    if (processedQuantity >= part.Quantity)
                    {
                        var completedChain = await _dbContext.PartProcesses
                            .Where(candidate =>
                                candidate.Id == rootProcessId ||
                                candidate.ReferenceId == rootProcessId)
                            .ToListAsync();
                        foreach (var completedProcess in completedChain)
                            completedProcess.Status = ProcessStatus.Completed;

                        foreach (var duplicate in await _dbContext.PartProcesses
                            .Where(candidate =>
                                candidate.Id != partProcess.Id &&
                                candidate.PlantDate == today &&
                                candidate.MachineId == partProcess.MachineId &&
                                candidate.Part != null &&
                                candidate.Part.Code == part.Code &&
                                candidate.Part.IndexId == part.IndexId)
                            .ToListAsync())
                        {
                            if (IsSafeToRemoveUnstartedPlan(duplicate))
                                _dbContext.PartProcesses.Remove(duplicate);
                        }
                        continue;
                    }

                    if (partProcess.Status != ProcessStatus.Completed)
                    {
                        _logger.LogInformation(
                            "Không tự chuyển kế hoạch chưa hoàn thành sang ngày hiện tại; " +
                            "chờ chốt ngày thành công. ProcessId={ProcessId}, " +
                            "RootProcessId={RootProcessId}, Status={Status}, PlantDate={PlantDate}",
                            partProcess.Id,
                            rootProcessId,
                            partProcess.Status,
                            partProcess.PlantDate);
                        continue;
                    }

                    var equivalentToday = await _dbContext.PartProcesses
                        .Where(candidate =>
                            candidate.Id != partProcess.Id &&
                            candidate.PlantDate == today &&
                            candidate.MachineId == partProcess.MachineId &&
                            candidate.Part != null &&
                            candidate.Part.Code == part.Code &&
                            candidate.Part.IndexId == part.IndexId)
                        .ToListAsync();

                    if (partProcess.Status == ProcessStatus.InProgress)
                    {
                        partProcess.IsOverdue = true;
                        if (partProcess.Id != rootProcessId)
                            partProcess.ReferenceId = rootProcessId;

                        var existingContinuation = equivalentToday.FirstOrDefault(candidate =>
                            candidate.Status == ProcessStatus.InProgress &&
                            (candidate.Id == rootProcessId ||
                             candidate.ReferenceId == rootProcessId));

                        if (existingContinuation == null)
                        {
                            var activeEquivalent = equivalentToday.FirstOrDefault(candidate =>
                                !IsSafeToRemoveUnstartedPlan(candidate));

                            if (activeEquivalent != null)
                            {
                                if (activeEquivalent.Status == ProcessStatus.InProgress &&
                                    (activeEquivalent.ReferenceId == null ||
                                     activeEquivalent.ReferenceId == rootProcessId))
                                {
                                    activeEquivalent.ReferenceId = rootProcessId;
                                    activeEquivalent.IsOverdue = true;
                                    existingContinuation = activeEquivalent;
                                }
                                else
                                {
                                    _logger.LogWarning(
                                        "Không thể tạo kế hoạch tiếp nối đang chạy vì ngày hôm nay " +
                                        "đã có kế hoạch cùng part/sequence với dữ liệu thực tế. " +
                                        "PreviousProcessId={PreviousProcessId}, TodayProcessId={TodayProcessId}",
                                        partProcess.Id,
                                        activeEquivalent.Id);
                                    continue;
                                }
                            }
                        }

                        if (existingContinuation == null)
                        {
                            existingContinuation = new PartProcess
                            {
                                CreateAt = VietnamTime.Now,
                                CreateBy = partProcess.CreateBy,
                                MachineId = partProcess.MachineId,
                                PlantDate = today,
                                StartTime = null,
                                EndTime = null,
                                EstimatedTime = partProcess.EstimatedTime,
                                Note = partProcess.Note,
                                Status = ProcessStatus.InProgress,
                                PartId = partProcess.PartId,
                                Sequence = partProcess.Sequence,
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
                            _dbContext.PartProcesses.Add(existingContinuation);
                            await _dbContext.SaveChangesAsync();
                        }

                        partProcess.EndTime ??= today.ToDateTime(TimeOnly.MinValue);

                        var duplicateTodayPlans = equivalentToday
                            .Where(candidate => candidate.Id != existingContinuation.Id)
                            .ToList();

                        foreach (var duplicate in duplicateTodayPlans)
                        {
                            if (IsSafeToRemoveUnstartedPlan(duplicate))
                            {
                                _logger.LogInformation(
                                    "Xóa kế hoạch gốc hôm nay chưa bắt đầu vì trùng mã part, index và máy " +
                                    "với kế hoạch tiếp nối đang chạy. " +
                                    "PreviousProcessId={PreviousProcessId}, RemovedProcessId={RemovedProcessId}, " +
                                    "ContinuationProcessId={ContinuationProcessId}",
                                    partProcess.Id,
                                    duplicate.Id,
                                    existingContinuation.Id);
                                _dbContext.PartProcesses.Remove(duplicate);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Giữ kế hoạch hôm nay trùng mã part, index và máy vì đã có dữ liệu thực tế. " +
                                    "PreviousProcessId={PreviousProcessId}, PreservedProcessId={PreservedProcessId}, " +
                                    "ContinuationProcessId={ContinuationProcessId}",
                                    partProcess.Id,
                                    duplicate.Id,
                                    existingContinuation.Id);
                            }
                        }

                        var machine = await _dbContext.Machines
                            .FirstOrDefaultAsync(candidate => candidate.Id == partProcess.MachineId);
                        if (machine != null)
                            machine.PartProcessId = existingContinuation.Id;
                    }
                    else if (partProcess.Status == ProcessStatus.Paused ||
                        partProcess.Status == ProcessStatus.PartiallyCompleted)
                    {
                        partProcess.IsOverdue = true;

                        var existingContinuation = equivalentToday.FirstOrDefault(candidate =>
                            candidate.Id == rootProcessId ||
                            candidate.ReferenceId == rootProcessId);

                        var activeEquivalent = equivalentToday.FirstOrDefault(candidate =>
                            !IsSafeToRemoveUnstartedPlan(candidate));

                        if (existingContinuation == null && activeEquivalent != null)
                        {
                            if (activeEquivalent.ReferenceId == null ||
                                activeEquivalent.ReferenceId == rootProcessId)
                            {
                                activeEquivalent.ReferenceId = rootProcessId;
                                activeEquivalent.IsOverdue = true;
                                existingContinuation = activeEquivalent;
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Không thể liên kết kế hoạch gia hạn vì kế hoạch hôm nay " +
                                    "đã thuộc một chuỗi công đoạn khác. " +
                                    "PreviousProcessId={PreviousProcessId}, TodayProcessId={TodayProcessId}",
                                    partProcess.Id,
                                    activeEquivalent.Id);
                                continue;
                            }
                        }

                        if (existingContinuation == null)
                        {
                            existingContinuation = new PartProcess
                            {
                                CreateAt = VietnamTime.Now,
                                CreateBy = partProcess.CreateBy,
                                MachineId = partProcess.MachineId,
                                PlantDate = today,
                                EstimatedTime = partProcess.EstimatedTime,
                                Note = partProcess.Note,
                                Status = partProcess.Status,
                                PartId = partProcess.PartId,
                                Sequence = partProcess.Sequence,
                                ProgrammingTime = 0,
                                ProcessedQuantity = 0,
                                WorkerNote = string.Empty,
                                IsOverdue = true,
                                ReferenceId = rootProcessId
                            };
                            _dbContext.PartProcesses.Add(existingContinuation);
                        }

                        var duplicateTodayPlans = await _dbContext.PartProcesses
                            .Where(candidate =>
                                candidate.Id != existingContinuation.Id &&
                                candidate.PlantDate == today &&
                                candidate.MachineId == partProcess.MachineId &&
                                candidate.Part != null &&
                                candidate.Part.Code == part.Code &&
                                candidate.Part.IndexId == part.IndexId)
                            .ToListAsync();

                        foreach (var duplicate in duplicateTodayPlans)
                        {
                            if (IsSafeToRemoveUnstartedPlan(duplicate))
                            {
                                _logger.LogInformation(
                                    "Xóa kế hoạch gốc hôm nay chưa bắt đầu vì trùng mã part, index và máy " +
                                    "với kế hoạch gia hạn. " +
                                    "PreviousProcessId={PreviousProcessId}, RemovedProcessId={RemovedProcessId}, " +
                                    "ContinuationProcessId={ContinuationProcessId}",
                                    partProcess.Id,
                                    duplicate.Id,
                                    existingContinuation.Id);
                                _dbContext.PartProcesses.Remove(duplicate);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Giữ kế hoạch hôm nay trùng mã part, index và máy vì đã có dữ liệu thực tế. " +
                                    "PreviousProcessId={PreviousProcessId}, PreservedProcessId={PreservedProcessId}, " +
                                    "ContinuationProcessId={ContinuationProcessId}",
                                    partProcess.Id,
                                    duplicate.Id,
                                    existingContinuation.Id);
                            }
                        }
                    }
                    else
                    {
                        var conflictingTodayPlan = equivalentToday.FirstOrDefault(candidate =>
                            !IsSafeToRemoveUnstartedPlan(candidate));

                        if (conflictingTodayPlan != null &&
                            (conflictingTodayPlan.ReferenceId == null ||
                             conflictingTodayPlan.ReferenceId == rootProcessId))
                        {
                            conflictingTodayPlan.ReferenceId = rootProcessId;
                            if (partProcess.Id != rootProcessId)
                                partProcess.ReferenceId = rootProcessId;
                            partProcess.IsOverdue = true;
                            continue;
                        }

                        if (conflictingTodayPlan != null)
                        {
                            _logger.LogWarning(
                                "Không thể gộp kế hoạch gia hạn vì kế hoạch hôm nay đã có dữ liệu " +
                                "thực tế thuộc chuỗi khác. Giữ nguyên cả hai để bảo toàn sản lượng. " +
                                "PreviousProcessId={PreviousProcessId}, TodayProcessId={TodayProcessId}",
                                partProcess.Id,
                                conflictingTodayPlan.Id);
                            continue;
                        }

                        foreach (var duplicate in equivalentToday)
                        {
                            _dbContext.PartProcesses.Remove(duplicate);
                        }

                        partProcess.PlantDate = today;
                    }
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
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

        public async Task<SearchResponse<List<ManufacturingProcessResponse>>> SearchByMachineIdAsync(
            int machineId,
            int? page,
            int? pageSize,
            DateOnly? plantDate = null)
        {
            var currentPage = Math.Max(page ?? 1, 1);
            var currentPageSize = Math.Clamp(pageSize ?? 10, 1, 100);

            var selectedDate = plantDate ?? VietnamTime.Today;

            var partProcesses = _dbContext.PartProcesses
                .AsNoTracking()
                .Where(x =>
                    x.MachineId == machineId &&
                    x.PlantDate == selectedDate &&
                    x.Part != null &&
                    !x.Part.IsClosed);

            var totalItems = await partProcesses.CountAsync();

            var data = await partProcesses
                .OrderByDescending(x => x.Status)
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
                .Select(x => new ManufacturingProcessResponse
                {
                    Id = x.Id,
                    CreateAt = x.CreateAt,
                    CreateBy = x.CreateBy,
                    UpdateAt = x.UpdateAt,
                    UpdateBy = x.UpdateBy,

                    PartId = x.Part != null ? x.Part.Id : 0,
                    PartCode = x.Part != null
                        ? x.Part.Code ?? string.Empty
                        : string.Empty,
                    Quantity = x.Part != null
                        ? x.Part.Quantity
                        : 0,

                    ProcessedQuantity = x.ProcessedQuantity,
                    TotalProcessedQuantity = _dbContext.PartProcesses
                        .Where(pp =>
                            pp.Id == (x.ReferenceId ?? x.Id) ||
                            pp.ReferenceId == (x.ReferenceId ?? x.Id))
                        .Sum(pp => pp.ProcessedQuantity),
                    ReferenceId = x.ReferenceId,

                    IndexId = x.Part != null
                        ? x.Part.IndexId
                        : 0,
                    IndexCode = x.Part != null && x.Part.Index != null
                        ? x.Part.Index.Code ?? string.Empty
                        : string.Empty,

                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    EstimatedTime = x.EstimatedTime,
                    WorkerNote = x.WorkerNote,
                    WaitingTime = x.WaitingTime,
                    WorkingTime = x.WorkingTime,
                    PerformedBy = x.PerformedBy,
                    SetupTime = x.SetupTime,
                    ProgrammingTime = x.ProgrammingTime,
                    Prioritize = x.Part != null ? x.Part.Prioritize : false,

                    Note = x.Note ?? string.Empty,
                    Status = x.Part != null && x.Part.Status == ProcessStatus.PendingApproval ? ProcessStatus.PendingApproval : x.Status,
                    ProcessType = ManufacturingProcessType.PartProcess,
                    IsOverdue = x.IsOverdue,
                    TotalTime = (x.WorkingTime ?? 0) + (x.SetupTime ?? 0) + (x.WaitingTime ?? 0) + (x.ProgrammingTime ?? 0),
                    IsSettingUp = x.IsSettingUp,
                })
                .ToListAsync();

            var processRootIds = data
                .Select(process => process.ReferenceId ?? process.Id)
                .Distinct()
                .ToList();
            var processIds = data
                .Select(process => process.Id)
                .Distinct()
                .ToList();
            var continuationRows = await _dbContext.PartProcesses
                .Where(process =>
                    process.ReferenceId.HasValue &&
                    (processRootIds.Contains(process.ReferenceId.Value) ||
                     processIds.Contains(process.ReferenceId.Value)))
                .Select(process => new
                {
                    RootId = process.ReferenceId!.Value,
                    process.ReferenceId,
                    process.PlantDate
                })
                .ToListAsync();

            foreach (var process in data)
            {
                var rootProcessId = process.ReferenceId ?? process.Id;
                process.IsCutoverCompleted =
                    process.EndTime?.TimeOfDay == TimeSpan.Zero &&
                    continuationRows.Any(continuation =>
                        (continuation.RootId == rootProcessId ||
                         continuation.ReferenceId == process.Id) &&
                        continuation.PlantDate > selectedDate);
            }

            var cutoverProcessIds = data
                .Where(process =>
                    process.IsCutoverCompleted ||
                    process.ReferenceId.HasValue)
                .Select(process => process.Id)
                .ToList();
            if (cutoverProcessIds.Count > 0)
            {
                var dailyProcessSummaries = await _dbContext.PartProcessDailyTimeSummaries
                    .Where(summary =>
                        summary.MachineId == machineId &&
                        cutoverProcessIds.Contains(summary.PartProcessId) &&
                        summary.ReportDate == selectedDate)
                    .ToDictionaryAsync(summary =>
                        summary.PartProcessId);

                foreach (var process in data.Where(item =>
                    item.IsCutoverCompleted || item.ReferenceId.HasValue))
                {
                    if (!dailyProcessSummaries.TryGetValue(process.Id, out var summary))
                        continue;

                    process.WorkingTime = summary.WorkingTime;
                    process.WaitingTime = summary.WaitingTime;
                    process.SetupTime = summary.SetupTime;
                    process.TotalTime =
                        summary.WorkingTime +
                        summary.WaitingTime +
                        summary.SetupTime +
                        (process.ProgrammingTime ?? 0);
                }
            }

            var maintenanceProcesses = await _dbContext.MachineMaintenanceProcesses
                .AsNoTracking()
                .Where(x => x.MachineId == machineId && x.PlantDate == selectedDate)
                .ToListAsync();

            foreach (var x in maintenanceProcesses)
            {
                data.Add(new ManufacturingProcessResponse
                {
                    Id = x.Id,
                    CreateAt = x.CreateAt,
                    CreateBy = x.CreateBy,
                    UpdateAt = x.UpdateAt,
                    UpdateBy = x.UpdateBy,
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    EstimatedTime = x.EstimatedTime,
                    PerformedBy = x.PerformedBy,
                    Prioritize = true,

                    Note = x.Note ?? string.Empty,
                    Status = x.Status,
                    PartCode = "Bảo trì",
                    ProcessType = ManufacturingProcessType.MaintenanceProcesses,
                });
            }

            return new SearchResponse<List<ManufacturingProcessResponse>>(
                currentPage,
                currentPageSize,
                totalItems,
                data);
        }

        public async Task<List<PartProcessKPIResponse>> SearchKPIAsync(
            string? searchMachineKeyword,
            string? searchIndexKeyword,
            string? searchPartKeyword,
            DateTime from,
            DateTime to)
        {
            const int batchSize = 2000;

            var query = _dbContext.PartProcesses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchMachineKeyword))
            {
                var ms = await _dbContext.Machines
                    .Where(m => m.Name.Contains(searchMachineKeyword) || m.Code.Contains(searchMachineKeyword))
                    .Select(m => m.Id).ToListAsync();

                if (ms != null && ms.Count != 0)
                    query = query.Where(p => ms.Contains(p.MachineId));
                else
                    return [];
            }

            if (!string.IsNullOrEmpty(searchIndexKeyword))
            {
                var idxIds = await _dbContext.Indexes.Where(i => i.Code.Contains(searchIndexKeyword)).Select(i => i.Id).ToListAsync();
                if (idxIds != null && idxIds.Count != 0)
                {
                    var partIds = await _dbContext.Parts
                        .IgnoreQueryFilters()
                        .Where(p => idxIds.Contains(p.IndexId))
                        .Select(p => p.Id)
                        .ToListAsync();
                    if (partIds != null && partIds.Count != 0)
                        query = query.Where(p => partIds.Contains(p.PartId));
                    else
                        return [];
                }
                else
                    return [];
            }

            if (!string.IsNullOrEmpty(searchPartKeyword))
            {
                var partIds = await _dbContext.Parts
                    .IgnoreQueryFilters()
                    .Where(p => p.Code.Contains(searchPartKeyword))
                    .Select(p => p.Id)
                    .ToListAsync();
                if (partIds != null && partIds.Count != 0)
                    query = query.Where(p => partIds.Contains(p.PartId));
                else
                    return [];
            }

            var reportStart = from.Date;
            var reportEnd = to.Date.AddDays(1);
            query = query
                .Where(p =>
                    (p.StartTime != null &&
                     p.StartTime < reportEnd &&
                     (p.EndTime == null || p.EndTime > reportStart)) ||
                    (p.ReferenceId != null &&
                     p.PlantDate >= DateOnly.FromDateTime(reportStart) &&
                     p.PlantDate <= DateOnly.FromDateTime(to.Date)) ||
                    (p.EndTime != null &&
                     p.PlantDate >= DateOnly.FromDateTime(reportStart) &&
                     p.PlantDate <= DateOnly.FromDateTime(to.Date)));

            var result = query.Join(
                _dbContext.Parts
                    .IgnoreQueryFilters()
                    .AsNoTracking(),
                pp => pp.PartId,
                p => p.Id,
                (pp, p) => new PartProcessKPIResponse
                {
                    // Part
                    PartId = p.Id,
                    PartCode = p.Code,
                    Quantity = p.Quantity,
                    IndexId = p.IndexId,

                    // PartProcess
                    PartProcessId = pp.Id,
                    ReferenceId = pp.ReferenceId,
                    PlantDate = pp.PlantDate,
                    StartTime = pp.StartTime,
                    EndTime = pp.EndTime,
                    Status = pp.Status,
                    SetupTime = pp.SetupTime,
                    WaitingTime = pp.WaitingTime,
                    WorkingTime = pp.WorkingTime,
                    ProgrammingTime = pp.ProgrammingTime,
                    TotalTime = (pp.WorkingTime ?? 0) + (pp.SetupTime ?? 0) + (pp.WaitingTime ?? 0) /*+ (pp.ProgrammingTime ?? 0)*/,
                    MachineId = pp.MachineId,
                    EstimatedTime = pp.EstimatedTime,
                    PartType = p.PartType,
                    NewRepair = p.NewRepair,
                    ProcessedQuantity = pp.ProcessedQuantity
                });

            var partProcesses = await result.ToListAsync();

            // Lấy thông tin machines
            var machineIds = partProcesses
                .Select(p => p.MachineId)
                .Distinct()
                .ToList();

            var machines = await _dbContext.Machines
                .Where(m => machineIds.Contains(m.Id))
                .ToListAsync();

            // Lấy thông tin indexes
            var indexIds = partProcesses
                .Select(p => p.IndexId)
                .Distinct()
                .ToList();

            var indexes = new List<Index>();
            foreach (var batch in indexIds.Chunk(batchSize))
            {
                indexes.AddRange(await _dbContext.Indexes
                    .Where(p => batch.Contains(p.Id))
                    .ToListAsync());
            }

            foreach (var partProcess in partProcesses)
            {
                var machine = machines.FirstOrDefault(m => m.Id == partProcess.MachineId);
                if (machine != null)
                {
                    partProcess.MachineName = machine.Name;
                    partProcess.MachineCode = machine.Code;
                }

                var index = indexes.FirstOrDefault(i => i.Id == partProcess.IndexId);
                if (index != null)
                {
                    partProcess.IndexCode = index.Code;
                }
            }

            if (partProcesses.Count == 0)
                return [];

            var fromDate = DateOnly.FromDateTime(reportStart);
            var toDate = DateOnly.FromDateTime(to.Date);
            var partProcessIds = partProcesses
                .Select(process => process.PartProcessId)
                .Distinct()
                .ToList();
            var processSummaryIds = partProcessIds
                .Distinct()
                .ToList();
            var continuationRootIds = await _dbContext.PartProcesses
                .Where(process =>
                    process.ReferenceId.HasValue &&
                    partProcessIds.Contains(process.ReferenceId.Value))
                .Select(process => process.ReferenceId!.Value)
                .Distinct()
                .ToHashSetAsync();
            var reportMachineIds = partProcesses
                .Select(process => process.MachineId)
                .Distinct()
                .ToList();

            var dailyProcessSummaries = await _dbContext.PartProcessDailyTimeSummaries
                .Where(summary =>
                    processSummaryIds.Contains(summary.PartProcessId) &&
                    reportMachineIds.Contains(summary.MachineId) &&
                    summary.ReportDate >= fromDate &&
                    summary.ReportDate <= toDate)
                .ToDictionaryAsync(
                    summary => (summary.MachineId, summary.PartProcessId, summary.ReportDate));

            var dailyMachineSummaries = await _dbContext.MachineDailyTimeSummaries
                .Where(summary =>
                    reportMachineIds.Contains(summary.MachineId) &&
                    summary.ReportDate >= fromDate &&
                    summary.ReportDate <= toDate)
                .ToDictionaryAsync(
                    summary => (summary.MachineId, summary.ReportDate));

            var allProcessesInReportRange = await _dbContext.PartProcesses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(process =>
                    reportMachineIds.Contains(process.MachineId) &&
                    ((process.StartTime != null &&
                      process.StartTime < reportEnd &&
                      (process.EndTime == null || process.EndTime > reportStart)) ||
                     (process.ReferenceId != null &&
                      process.PlantDate >= fromDate &&
                      process.PlantDate <= toDate)))
                .Select(process => new
                {
                    process.MachineId,
                    process.StartTime,
                    process.EndTime,
                    process.PlantDate
                })
                .ToListAsync();

            var activeProcessCounts = new Dictionary<(int MachineId, DateOnly Date), int>();
            foreach (var process in allProcessesInReportRange)
            {
                var processStartDate = process.PlantDate ??
                    (process.StartTime.HasValue
                        ? DateOnly.FromDateTime(process.StartTime.Value)
                        : null);
                if (!processStartDate.HasValue)
                    continue;

                var processEndDate = process.PlantDate ??
                    (process.EndTime.HasValue
                        ? GetLastActiveDate(process.EndTime.Value)
                        : processStartDate.Value);
                var firstDate = processStartDate.Value > fromDate
                    ? processStartDate.Value
                    : fromDate;
                var lastDate = processEndDate < toDate ? processEndDate : toDate;

                for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
                {
                    var key = (process.MachineId, date);
                    activeProcessCounts[key] = activeProcessCounts.GetValueOrDefault(key) + 1;
                }
            }

            var dailyRows = new List<PartProcessKPIResponse>();
            foreach (var process in partProcesses)
            {
                var hasContinuation = continuationRootIds.Contains(
                    process.PartProcessId);
                var isContinuationChain =
                    process.ReferenceId.HasValue || hasContinuation;
                var processStartDate = process.PlantDate ??
                    (process.StartTime.HasValue
                        ? DateOnly.FromDateTime(process.StartTime.Value)
                        : null);
                if (!processStartDate.HasValue)
                    continue;

                var processEndDate = process.PlantDate ??
                    (process.EndTime.HasValue
                        ? GetLastActiveDate(process.EndTime.Value)
                        : processStartDate.Value);
                var firstDate = processStartDate.Value > fromDate
                    ? processStartDate.Value
                    : fromDate;
                var lastDate = processEndDate < toDate ? processEndDate : toDate;
                if (firstDate > lastDate)
                    continue;

                var isMultiDay =
                    process.ReferenceId.HasValue ||
                    hasContinuation ||
                    processStartDate.Value != processEndDate;
                var isCutoverSource =
                    hasContinuation &&
                    process.EndTime?.TimeOfDay == TimeSpan.Zero;

                for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
                {
                    dailyProcessSummaries.TryGetValue(
                        (process.MachineId, process.PartProcessId, date),
                        out var processSummary);
                    dailyMachineSummaries.TryGetValue(
                        (process.MachineId, date),
                        out var machineSummary);

                    var useMachineSummary =
                        processSummary == null &&
                        isMultiDay &&
                        activeProcessCounts.GetValueOrDefault((process.MachineId, date)) == 1 &&
                        machineSummary != null;

                    var useProcessTimes = !isMultiDay || process.ReferenceId.HasValue;

                    var workingTime = isCutoverSource
                        ? GetHigherTimeValue(process.WorkingTime, processSummary?.WorkingTime)
                        : processSummary?.WorkingTime ??
                            (useProcessTimes
                                ? process.WorkingTime
                                : useMachineSummary ? machineSummary!.WorkingTime : null);
                    var waitingTime = isCutoverSource
                        ? GetHigherTimeValue(process.WaitingTime, processSummary?.WaitingTime)
                        : processSummary?.WaitingTime ??
                            (useProcessTimes
                                ? process.WaitingTime
                                : useMachineSummary ? machineSummary!.WaitingTime : null);
                    var setupTime = isCutoverSource
                        ? GetHigherTimeValue(process.SetupTime, processSummary?.SetupTime)
                        : processSummary?.SetupTime ??
                            (useProcessTimes
                                ? process.SetupTime
                                : useMachineSummary ? machineSummary!.SetupTime : null);

                    dailyRows.Add(CreateDailyReportRow(
                        process,
                        date,
                        isMultiDay,
                        date == processStartDate.Value &&
                            !process.ReferenceId.HasValue
                            ? process.StartTime
                            : null,
                        process.EndTime.HasValue &&
                            !(isMultiDay &&
                              process.EndTime.Value.TimeOfDay == TimeSpan.Zero) &&
                            DateOnly.FromDateTime(process.EndTime.Value) == date
                            ? process.EndTime
                            : null,
                        workingTime,
                        waitingTime,
                        setupTime,
                        machineSummary == null
                            ? null
                            : machineSummary.WorkingTime +
                              machineSummary.WaitingTime +
                              machineSummary.SetupTime));
                }
            }

            return dailyRows;
        }

        private static DateOnly GetLastActiveDate(DateTime endTime)
        {
            var endDate = DateOnly.FromDateTime(endTime);
            return endTime.TimeOfDay == TimeSpan.Zero
                ? endDate.AddDays(-1)
                : endDate;
        }

        private static double? GetHigherTimeValue(double? processTime, double? dailySummaryTime)
        {
            if (!processTime.HasValue)
                return dailySummaryTime;

            if (!dailySummaryTime.HasValue)
                return processTime;

            return Math.Max(processTime.Value, dailySummaryTime.Value);
        }

        private static PartProcessKPIResponse CreateDailyReportRow(
            PartProcessKPIResponse process,
            DateOnly reportDate,
            bool isMultiDay,
            DateTime? startTime,
            DateTime? endTime,
            double? workingTime,
            double? waitingTime,
            double? setupTime,
            double? dailyMachineTotalTime)
        {
            return new PartProcessKPIResponse
            {
                MachineId = process.MachineId,
                MachineName = process.MachineName,
                MachineCode = process.MachineCode,
                IndexId = process.IndexId,
                IndexCode = process.IndexCode,
                PartId = process.PartId,
                PartCode = process.PartCode,
                PartProcessId = process.PartProcessId,
                Quantity = process.Quantity,
                ProcessedQuantity = process.ProcessedQuantity,
                ReportDate = reportDate,
                IsMultiDay = isMultiDay,
                DailyMachineTotalTime = dailyMachineTotalTime,
                WorkingTime = workingTime,
                SetupTime = setupTime,
                WaitingTime = waitingTime,
                ProgrammingTime = isMultiDay ? 0 : process.ProgrammingTime,
                TotalTime = workingTime.HasValue ||
                    waitingTime.HasValue ||
                    setupTime.HasValue
                    ? (workingTime ?? 0) + (waitingTime ?? 0) + (setupTime ?? 0)
                    : isMultiDay ? null : process.TotalTime,
                PlantDate = process.PlantDate,
                StartTime = startTime,
                EndTime = endTime,
                EstimatedTime = process.EstimatedTime,
                PartType = process.PartType,
                NewRepair = process.NewRepair,
                Status = process.Status
            };
        }

        public async Task<int> GetProcessedQuantityByReferenceId(int? referenceId)
        {
            if (!referenceId.HasValue)
                return 0;

            return await _dbContext.PartProcesses
                .Where(pp => pp.Id == referenceId || pp.ReferenceId == referenceId)
                .SumAsync(pp => pp.ProcessedQuantity);
        }

        public async Task<bool> IsCutoverLockedAsync(int processId)
        {
            var process = await _dbContext.PartProcesses
                .Where(candidate => candidate.Id == processId)
                .Select(candidate => new
                {
                    candidate.Id,
                    candidate.ReferenceId,
                    candidate.PlantDate,
                    candidate.EndTime
                })
                .FirstOrDefaultAsync();

            if (process?.EndTime is not DateTime endTime ||
                endTime.TimeOfDay != TimeSpan.Zero ||
                !process.PlantDate.HasValue)
            {
                return false;
            }

            var rootProcessId = process.ReferenceId ?? process.Id;
            return await _dbContext.PartProcesses.AnyAsync(candidate =>
                (candidate.ReferenceId == rootProcessId ||
                 candidate.ReferenceId == process.Id) &&
                candidate.PlantDate > process.PlantDate);
        }

        public async Task<List<int>> GetPrioritizeMachineIdsByMachineIdsAndDateAsync(List<int> machineIds, DateTime date)
        {
            var dateOnly = DateOnly.FromDateTime(date);
            var query = _dbContext.PartProcesses.AsNoTracking().AsQueryable();

            query = query.Where(pp =>
                pp.PlantDate != null &&
                pp.PlantDate.Value == dateOnly &&
                pp.Part != null &&
                !pp.Part.IsClosed &&
                pp.Part.Prioritize);

            if (machineIds.Count != 0)
                query = query.Where(pp => machineIds.Contains(pp.MachineId));

            return await query.Select(pp => pp.MachineId).ToListAsync();
        }

        public async Task<List<PartProcess>> GetRelatedProcessesAsync(HashSet<int> processIds)
        {
            var relatedProcesses = await _dbContext.PartProcesses
                .Where(pp =>
                    pp.Part != null &&
                    !pp.Part.IsClosed &&
                    (processIds.Contains(pp.Id) || (pp.ReferenceId.HasValue && processIds.Contains(pp.ReferenceId.Value))))
                .ToListAsync();

            return relatedProcesses;
        }

        public async Task RemoveEquivalent(PartProcess partProcess, int indexId)
        {
            var today = VietnamTime.Today;
            var rootProcessId = partProcess.ReferenceId ?? partProcess.Id;

            var todayProcesses = await _dbContext.PartProcesses
                .Where(pp =>
                    pp.Id != partProcess.Id &&
                    pp.PlantDate >= today &&
                    pp.PartId == partProcess.PartId &&
                    pp.MachineId == partProcess.MachineId &&
                    pp.Sequence == partProcess.Sequence &&
                    pp.Part != null &&
                    !pp.Part.IsClosed &&
                    pp.Part.IndexId == indexId &&
                    (pp.ReferenceId == rootProcessId || pp.ReferenceId == null))
                .ToListAsync();

            var safeDuplicates = todayProcesses
                .Where(IsSafeToRemoveUnstartedPlan)
                .ToList();

            _dbContext.PartProcesses.RemoveRange(safeDuplicates);

            foreach (var unsafeDuplicate in todayProcesses.Except(safeDuplicates))
            {
                _logger.LogWarning(
                    "Không xóa kế hoạch tương ứng khi công đoạn hoàn thành vì bản ghi đã có dữ liệu thực tế. " +
                    "CompletedProcessId={CompletedProcessId}, PreservedProcessId={PreservedProcessId}",
                    partProcess.Id,
                    unsafeDuplicate.Id);
            }
        }
    }
}
