using TAKAHATA.Contracts.Common;
using TAKAHATA.Contracts.IndexAndPart;
using TAKAHATA.Contracts.Report;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Application.Common.Interface.Repositories
{
    public interface IPartProcessRepository : IBaseRepository<PartProcess>
    {
        Task<List<int>> GetPrioritizeMachineIdsByMachineIdsAndDateAsync(List<int> machineIds, DateTime date);

        Task<int> GetProcessedQuantityByReferenceId(int? referenceId);
        Task<bool> IsCutoverLockedAsync(int processId);
        Task<List<PartProcess>> GetInProgressProcessesByMachineIdsAsync(
            List<int> machineIds,
            DateOnly plantDate);
        Task<List<PartProcess>> GetRelatedProcessesAsync(HashSet<int> processIds);
        Task HandleExtend();
        Task HandleExtend(DateOnly targetDate);
        Task RemoveEquivalent(
            PartProcess partProcess,
            string partCode,
            int indexId,
            DateOnly? effectiveDate = null);
        Task<SearchResponse<List<ManufacturingProcessResponse>>> SearchByMachineIdAsync(
            int machineId,
            int? page,
            int? pageSize,
            DateOnly? plantDate = null);
        Task<List<PartProcessKPIResponse>> SearchKPIAsync(
            string? searchMachineKeyword,
            string? searchIndexKeyword,
            string? searchPartKeyword,
            DateTime from,
            DateTime to);

        Task<bool> UpdateWorkingTimeAsync(
            int id,
            double? workingTime,
            CancellationToken cancellationToken = default);
    }
}
