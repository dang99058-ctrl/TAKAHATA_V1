namespace TAKAHATA.Application.Common.Interface.Repositories
{
    public interface IUnitOfWorkRepository
    {
        Task<int> FinalizeExpiredProcessStopsAsync(DateTime cutoff);
        Task<bool> HasUnendedInProgressProcessAsync(
            int machineId,
            DateOnly reportDate);
        Task<DateOnly?> PrepareDailyCutoverAsync(
            int machineId,
            DateOnly previousDate);
        Task MarkDailyCutoverCommandSentAsync(int machineId, DateOnly reportDate);
        Task CancelDailyCutoverAsync(int machineId, DateOnly reportDate);
        Task UpdateDataAsync(
               int statusBitAddress,
               int status,
               int partId,
               int processId,
               double workingTime,
               double waitingTime,
               double setupTime);
    }
}
