using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Contracts.Common;
using TAKAHATA.Contracts.Equipment;
using TAKAHATA.Contracts.IndexAndPart;
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
