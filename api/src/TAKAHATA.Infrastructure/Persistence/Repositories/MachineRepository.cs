using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Services;
using TAKAHATA.Contracts.Common;
using TAKAHATA.Contracts.Equipment;
using TAKAHATA.Contracts.IndexAndPart;
using TAKAHATA.Contracts.Report;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Infrastructure.Persistence.Repositories
{
    public class MachineRepository : BaseRepository<Machine>, IMachineRepository
    {
        private readonly IMapper _mapper;

        public MachineRepository(
            AppDbContext dbContext,
            IMapper mapper) : base(dbContext)
        {
            _mapper = mapper;
        }

        public override async Task<Machine?> DeleteByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            if (_dbContext.MachineMaintenanceProcesses.Any(m => m.MachineId == id) || _dbContext.PartProcesses.Any(m => m.MachineId == id))
                return null;

            return await base.DeleteByIdAsync(id, cancellationToken);
        }

        public async Task CreateDefaultMachine()
        {
            var defaultMachines = new List<Machine>
            {
                new()
                {
                    Code = "28",
                    Type = "EDM",
                    Name = "AgieCharmilles Form 300",
                    Brand = "AgieCharmilles"
                },
                new()
                {
                    Code = "22",
                    Type = "EDM",
                    Name = "Sodick AG40L",
                    Brand = "Sodick"
                },
                new()
                {
                    Code = "24",
                    Type = "EDM",
                    Name = "Sodick AG60L",
                    Brand = "Sodick"
                },
                new()
                {
                    Code = "46",
                    Type = "Wire cut",
                    Name = "Sodick ALN600G",
                    Brand = "Sodick"
                },
                new()
                {
                    Code = "23",
                    Type = "Wire cut",
                    Name = "Sodick VZ300L",
                    Brand = "Sodick"
                },
                new()
                {
                    Code = "25",
                    Type = "Milling Center",
                    Name = "Okuma MB-46VA",
                    Brand = "Okuma"
                },
                new()
                {
                    Code = "61",
                    Type = "Milling Center",
                    Name = "Okuma MB-56VB",
                    Brand = "Okuma"
                },
                new()
                {
                    Code = "2",
                    Type = "Milling Center",
                    Name = "Fanuc D21MIA",
                    Brand = "Fanuc"
                },
                new()
                {
                    Code = "19",
                    Type = "Surface Grinding",
                    Name = "Okamoto ACC64ST",
                    Brand = "Okamoto"
                },
                new()
                {
                    Code = "38",
                    Type = "Surface Grinding",
                    Name = "Okamoto ACC52DX",
                    Brand = "Okamoto"
                },
                new()
                {
                    Code = "42",
                    Type = "Cylinder Grinding",
                    Name = "Mitsui MSG-818PC-NC",
                    Brand = "Mitsui"
                },
                new()
                {
                    Code = "20",
                    Type = "Cylinder Grinding",
                    Name = "Okamoto Max 15N-ID",
                    Brand = "Okamoto"
                },
                new()
                {
                    Code = "41",
                    Type = "Cylinder Grinding",
                    Name = "SHIGIYA GPS-30B.50",
                    Brand = "SHIGIYA"
                },
                new()
                {
                    Code = "5",
                    Type = "Lathe",
                    Name = "Okuma L250",
                    Brand = "Okuma"
                },
                new()
                {
                    Code = "58",
                    Type = "Lathe",
                    Name = "Citizen A20",
                    Brand = "Citizen"
                },
                new()
                {
                    Code = "55",
                    Type = "NC Milling",
                    Name = "Yamasaki YZ-352NCR",
                    Brand = "Yamasaki"
                },
                new()
                {
                    Code = "3",
                    Type = "NC Milling",
                    Name = "Shizuoka VHR-AN",
                    Brand = "Shizuoka"
                },
                new()
                {
                    Code = "1",
                    Type = "Hobbing",
                    Name = "Hamai 60SP-NC",
                    Brand = "Hamai"
                },
                new()
                {
                    Code = "60",
                    Type = "Hobbing",
                    Name = "Hamai 60SP-ETL",
                    Brand = "Hamai"
                },
                new()
                {
                    Code = "59",
                    Type = "Hole drill",
                    Name = "Sodick K1C",
                    Brand = "Sodick"
                }
            };

            if (!await _dbContext.Machines.AnyAsync())
            {
                await _dbContext.Machines.AddRangeAsync(defaultMachines);
                await _dbContext.SaveChangesAsync();
            }
        }

        public async Task<bool> IsExistedAsync(string name, string code)
        {
            var machine = await _dbContext.Machines
                .FirstOrDefaultAsync(m => m.Name == name || m.Code == code);
            return machine != null;
        }

        public async Task<SearchResponse<List<MachineResponse>>> SearchAsync(
            string? keyword,
            int? page,
            int? pageSize)
        {
            var query = _dbContext.Machines
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var searchText = keyword.Trim();

                query = query.Where(x =>
                    x.Name.Contains(searchText) ||
                    x.Code.Contains(searchText) ||
                    x.Brand.Contains(searchText));
            }

            var currentPage = Math.Max(page ?? 1, 1);
            var currentPageSize = Math.Clamp(pageSize ?? 10, 1, 100);

            var totalItems = await query.CountAsync();

            var values = await query
                .OrderByDescending(x => x.CreateAt)
                    .ThenBy(x => x.Id)
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
                .ToListAsync();

            var data = values
                .Select(v => _mapper.Map<MachineResponse>(v))
                .ToList();

            return new SearchResponse<List<MachineResponse>>(
                       currentPage,
                       currentPageSize,
                       totalItems,
                       data);
        }

        public async Task<bool> IsExistedExceptIdAsync(string name, string code, int id)
        {
            var machine = await _dbContext.Machines
                .FirstOrDefaultAsync(m => (m.Name == name || m.Code == code) && m.Id != id);
            return machine != null;
        }

        public async Task<IEnumerable<Machine>> GetAsync()
        {
            return await _dbContext.Machines.ToListAsync();
        }

        public async Task<IEnumerable<Machine>> GetAutomaticMachineAsync()
        {
            return await _dbContext.Machines.Where(m => m.IsAutomatic).ToListAsync();
        }

        public async Task<SearchResponse<List<MachineScheduleResponse>>> SearchSchedule(
            string? keyword,
            DateOnly? plantFrom,
            DateOnly? plantTo,
            int page,
            int pageSize)
        {
            var currentPage = Math.Max(page, 1);
            var currentPageSize = Math.Clamp(pageSize, 1, 100);

            keyword = keyword?.Trim();

            var machineIds = await _dbContext.Machines
                 .Where(x =>
                     string.IsNullOrEmpty(keyword) ||
                     x.Code.Contains(keyword) ||
                     x.Name.Contains(keyword))
                 .Select(x => x.Id)
                 .ToListAsync();

            #region PartProcesses

            var partProcessQuery = _dbContext.PartProcesses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    machineIds.Contains(x.MachineId));

            if (plantFrom.HasValue || plantTo.HasValue)
            {
                var rangeStart = plantFrom?.ToDateTime(TimeOnly.MinValue);
                var rangeEndExclusive = plantTo?.AddDays(1).ToDateTime(TimeOnly.MinValue);
                partProcessQuery = partProcessQuery.Where(x =>
                    ((plantFrom == null || x.PlantDate >= plantFrom) &&
                     (plantTo == null || x.PlantDate <= plantTo)) ||
                    (x.StartTime != null &&
                     (rangeEndExclusive == null || x.StartTime < rangeEndExclusive) &&
                     (rangeStart == null || x.EndTime == null || x.EndTime > rangeStart)));
            }

            var partProcesses = await partProcessQuery
                .Include(x => x.Part)
                    .ThenInclude(p => p.Index)
                .ToListAsync();

            var processRootIds = partProcesses
                .Select(process => process.ReferenceId ?? process.Id)
                .Distinct()
                .ToList();
            var processIds = partProcesses
                .Select(process => process.Id)
                .Distinct()
                .ToList();
            var processTotals = await _dbContext.PartProcesses
                .Where(process =>
                    processRootIds.Contains(process.ReferenceId ?? process.Id))
                .GroupBy(process => process.ReferenceId ?? process.Id)
                .Select(group => new
                {
                    RootId = group.Key,
                    TotalProcessedQuantity = group.Sum(process => process.ProcessedQuantity)
                })
                .ToDictionaryAsync(
                    group => group.RootId,
                    group => group.TotalProcessedQuantity);
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

            var groupedPart = partProcesses
                .GroupBy(x => x.MachineId)
                .Select(x => new MachineLatestSchedule
                {
                    MachineId = x.Key,
                    LatestPlantDate = x.Max(y => y.PlantDate)
                });
            #endregion

            #region MaintenanceProcesses

            var maintenanceQuery = _dbContext.MachineMaintenanceProcesses
                .AsNoTracking()
                .Where(x => machineIds.Contains(x.MachineId));

            if (plantFrom.HasValue)
                maintenanceQuery = maintenanceQuery.Where(x => x.PlantDate >= plantFrom.Value);

            if (plantTo.HasValue)
                maintenanceQuery = maintenanceQuery.Where(x => x.PlantDate <= plantTo.Value);

            var maintenanceProcesses = await maintenanceQuery.ToListAsync();

            var groupedMaintenance = maintenanceProcesses
                .GroupBy(x => x.MachineId)
                .Select(x => new MachineLatestSchedule
                {
                    MachineId = x.Key,
                    LatestPlantDate = x.Max(y => y.PlantDate)
                });

            #endregion
            // Gộp 2 nguồn
            var groupedMachines = groupedPart
                .Concat(groupedMaintenance)
                .GroupBy(x => x.MachineId)
                .Select(x => new
                {
                    MachineId = x.Key,
                    LatestPlantDate = x.Max(y => y.LatestPlantDate)
                });

            var totalItems = groupedMachines.Count();

            var pagedMachineIds = groupedMachines
                .OrderByDescending(x => x.LatestPlantDate)
                .ThenBy(x => x.MachineId)
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
                .Select(x => x.MachineId)
                .ToList();

            var machines = await _dbContext.Machines
                .AsNoTracking()
                .Where(x => pagedMachineIds.Contains(x.Id))
                .ToListAsync();

            var partLookup = partProcesses
                .GroupBy(x => x.MachineId)
                .ToDictionary(x => x.Key, x => x.ToList());

            var maintenanceLookup = maintenanceProcesses
                .GroupBy(x => x.MachineId)
                .ToDictionary(x => x.Key, x => x.ToList());

            var result = new List<MachineScheduleResponse>();

            foreach (var machine in machines)
            {
                var response = _mapper.Map<MachineScheduleResponse>(machine);

                if (partLookup.TryGetValue(machine.Id, out var partProcess))
                {
                    response.Processes.AddRange(partProcess.SelectMany(process =>
                        GetScheduleDates(process, plantFrom, plantTo)
                            .Select(scheduleDate => new MachineProcessResponse
                            {
                                Id = process.Id,
                                CreateAt = process.CreateAt,
                                CreateBy = process.CreateBy,
                                UpdateAt = process.UpdateAt,
                                UpdateBy = process.UpdateBy,
                                PlantDate = scheduleDate,
                                StartTime = process.StartTime,
                                EndTime = process.EndTime ?? process.PendingEndTime,
                                WorkingTime = process.WorkingTime,
                                Status = process.Status,
                                Note = process.Note,
                                PartId = process.PartId,
                                PartCode = process.Part?.Code ?? string.Empty,
                                Quantity = process.Part?.Quantity ?? 0,
                                Type = MachineProcessType.Manufacturing,
                                Prioritize = process.Part?.Prioritize ?? false,
                                IsClosed = process.Part?.IsClosed ?? false,
                                EstimatedTime = process.EstimatedTime,
                                PartType = process.Part?.PartType,
                                IsOverdue = process.IsOverdue,
                                IsCutoverCompleted =
                                    process.EndTime.HasValue &&
                                    process.EndTime.Value.TimeOfDay == TimeSpan.Zero &&
                                    continuationRows.Any(continuation =>
                                        (continuation.RootId ==
                                             (process.ReferenceId ?? process.Id) ||
                                         continuation.ReferenceId == process.Id) &&
                                        continuation.PlantDate > process.PlantDate),
                                IndexCode = process.Part?.Index?.Code ?? string.Empty,
                                ReferenceId = process.ReferenceId,
                                ProcessedQuantity = process.ProcessedQuantity,
                                TotalProcessedQuantity = processTotals.GetValueOrDefault(
                                    process.ReferenceId ?? process.Id,
                                    process.ProcessedQuantity),
                            })));
                }

                if (maintenanceLookup.TryGetValue(machine.Id, out var maintenances))
                {
                    response.Processes.AddRange(maintenances.Select(m => new MachineProcessResponse
                    {
                        Id = m.Id,
                        CreateAt = m.CreateAt,
                        CreateBy = m.CreateBy,
                        UpdateAt = m.UpdateAt,
                        UpdateBy = m.UpdateBy,
                        PlantDate = m.PlantDate,
                        StartTime = m.StartTime,
                        EndTime = m.EndTime,
                        Status = m.Status,
                        Note = m.Note ?? string.Empty,
                        PartCode = string.Empty,
                        Quantity = 0,
                        EstimatedTime = m.EstimatedTime,
                        Type = MachineProcessType.Maintenance
                    }));
                }

                response.Processes = response.Processes
                    .OrderByDescending(x => x.PlantDate)
                    .ToList();

                result.Add(response);
            }

            return new SearchResponse<List<MachineScheduleResponse>>(
                currentPage,
                currentPageSize,
                totalItems,
                result);
        }

        private static IEnumerable<DateOnly> GetScheduleDates(
            PartProcess process,
            DateOnly? plantFrom,
            DateOnly? plantTo)
        {
            if (plantFrom == null && plantTo == null)
            {
                if (process.PlantDate.HasValue)
                    yield return process.PlantDate.Value;
                yield break;
            }

            if (process.Status is ProcessStatus.Completed or
                ProcessStatus.Paused or
                ProcessStatus.PartiallyCompleted ||
                process.Part?.IsClosed == true)
            {
                if (process.PlantDate.HasValue &&
                    (!plantFrom.HasValue || process.PlantDate.Value >= plantFrom.Value) &&
                    (!plantTo.HasValue || process.PlantDate.Value <= plantTo.Value))
                {
                    yield return process.PlantDate.Value;
                }

                yield break;
            }

            if (process.PlantDate.HasValue &&
                (!plantFrom.HasValue || process.PlantDate.Value >= plantFrom.Value) &&
                (!plantTo.HasValue || process.PlantDate.Value <= plantTo.Value))
            {
                yield return process.PlantDate.Value;
            }

            if (!process.StartTime.HasValue)
            {
                yield break;
            }

            if (!process.EndTime.HasValue)
            {
                if (!process.PlantDate.HasValue)
                {
                    var openProcessDate = DateOnly.FromDateTime(process.StartTime.Value);
                    if ((!plantFrom.HasValue || openProcessDate >= plantFrom.Value) &&
                        (!plantTo.HasValue || openProcessDate <= plantTo.Value))
                    {
                        yield return openProcessDate;
                    }
                }

                yield break;
            }

            var processStartDate = process.EndTime?.TimeOfDay == TimeSpan.Zero
                ? process.PlantDate
                : process.StartTime.HasValue
                    ? DateOnly.FromDateTime(process.StartTime.Value)
                    : process.PlantDate;
            if (!processStartDate.HasValue)
                yield break;

            var processEndDate = DateOnly.FromDateTime(process.EndTime!.Value);
            if (process.EndTime?.TimeOfDay == TimeSpan.Zero)
                processEndDate = processEndDate.AddDays(-1);

            var firstDate = plantFrom.HasValue && plantFrom.Value > processStartDate.Value
                ? plantFrom.Value
                : processStartDate.Value;
            var lastDate = plantTo.HasValue && plantTo.Value < processEndDate
                ? plantTo.Value
                : processEndDate;

            for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                if (date != process.PlantDate)
                    yield return date;
            }
        }

        public async Task<List<MachineKPIResponse>> SearchKPIAsync(string? searchMachineKeyword, DateTime from, DateTime to)
        {
            var machineQuery = _dbContext.Machines.AsQueryable().AsNoTracking();
            if (!string.IsNullOrEmpty(searchMachineKeyword))
                machineQuery = machineQuery.Where(m => m.Code.Contains(searchMachineKeyword) || m.Name.Contains(searchMachineKeyword));

            var machines = await machineQuery.ToListAsync();
            if (machines == null || machines.Count == 0)
                return [];

            var machineIds = machines.Select(m => m.Id).ToList();
            var reportStart = from.Date;
            var reportEnd = to.Date.AddDays(1);

            var partProcesses = await _dbContext.PartProcesses
                .IgnoreQueryFilters()
                .Where(p =>
                    machineIds.Contains(p.MachineId) &&
                    p.StartTime != null &&
                    p.StartTime < reportEnd &&
                    (p.EndTime == null || p.EndTime > reportStart))
                .ToListAsync();

            var maintenanceProcesses = await _dbContext.MachineMaintenanceProcesses
                .Where(p =>
                    machineIds.Contains(p.MachineId) &&
                    p.StartTime != null &&
                    p.StartTime < reportEnd &&
                    (p.EndTime == null || p.EndTime > reportStart))
                .ToListAsync();

            var dailyTimeSummaries = await _dbContext.MachineDailyTimeSummaries
                .Where(summary =>
                    machineIds.Contains(summary.MachineId) &&
                    summary.ReportDate >= DateOnly.FromDateTime(reportStart) &&
                    summary.ReportDate <= DateOnly.FromDateTime(to.Date))
                .ToDictionaryAsync(
                    summary => (summary.MachineId, summary.ReportDate));

            var result = new List<MachineKPIResponse>();
            var now = DateTime.Now;

            foreach (var machine in machines)
            {
                for (var date = DateOnly.FromDateTime(reportStart);
                    date <= DateOnly.FromDateTime(to.Date);
                    date = date.AddDays(1))
                {
                    var dayStart = date.ToDateTime(TimeOnly.MinValue);
                    var dayEnd = dayStart.AddDays(1);
                    var workingTime = SumOverlapHours(
                        partProcesses.Where(p => p.MachineId == machine.Id),
                        dayStart,
                        dayEnd,
                        now,
                        limitOpenProcessesToPlantDate: true);
                    var maintenanceTime = SumOverlapHours(
                        maintenanceProcesses.Where(p => p.MachineId == machine.Id),
                        dayStart,
                        dayEnd,
                        now);
                    dailyTimeSummaries.TryGetValue((machine.Id, date), out var dailySummary);
                    if (dailySummary is not null)
                        workingTime = dailySummary.WorkingTime;

                    result.Add(new MachineKPIResponse
                    {
                        ReportDate = date,
                        Name = machine.Name,
                        Code = machine.Code,
                        Brand = machine.Brand,
                        Type = machine.Type,
                        MachineStatus = machine.MachineStatus,
                        MaintenanceTime = maintenanceTime,
                        WorkingTime = workingTime,
                        WaitingTime = dailySummary?.WaitingTime,
                        SetupTime = dailySummary?.SetupTime,
                        UtilizationRate = workingTime / 24d * 100,
                        LostTime = Math.Max(24d - workingTime, 0),
                    });
                }
            }

            return result
                .OrderBy(kpi => kpi.ReportDate)
                .ThenBy(kpi => kpi.Code)
                .ToList();
        }

        private static double SumOverlapHours(
            IEnumerable<Process> processes,
            DateTime dayStart,
            DateTime dayEnd,
            DateTime now,
            bool limitOpenProcessesToPlantDate = false)
        {
            return processes.Sum(process =>
            {
                if (!process.StartTime.HasValue)
                    return 0;

                if (limitOpenProcessesToPlantDate &&
                    !process.EndTime.HasValue &&
                    (process.PlantDate ??
                     DateOnly.FromDateTime(process.StartTime.Value)) !=
                    DateOnly.FromDateTime(dayStart))
                {
                    return 0;
                }

                var start = process.StartTime.Value > dayStart
                    ? process.StartTime.Value
                    : dayStart;
                var processEnd = process.EndTime ?? now;
                var end = processEnd < dayEnd ? processEnd : dayEnd;

                return end > start ? (end - start).TotalHours : 0;
            });
        }

        public Task<IEnumerable<Machine>> GetByIdsAsync(List<int> machineIds)
        {
            return _dbContext.Machines
                .Where(m => machineIds.Contains(m.Id))
                .AsNoTracking()
                .ToListAsync()
                .ContinueWith(t => t.Result.AsEnumerable());
        }

        public async Task<bool> IsProcessing(int machineId)
        {
            return await _dbContext.PartProcesses
                .AnyAsync(pp =>
                    pp.MachineId == machineId
                    && pp.Part != null
                    && !pp.Part.IsClosed
                    && pp.StartTime != null
                    && pp.EndTime == null
                    && pp.Status == ProcessStatus.InProgress);
        }

        public async Task<IEnumerable<int>> GetHasPlantMachine(List<int> machineIds)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);

            return await _dbContext.PartProcesses
                 .Where(p =>
                     machineIds.Contains(p.MachineId) &&
                     p.PlantDate == today &&
                     p.Part != null &&
                     !p.Part.IsClosed)
                 .Select(pp => pp.MachineId)
                 .Distinct()
                 .ToListAsync();
        }
    }
}