namespace TAKAHATA.Application.Common.Interface.Repositories
{
    public interface IUnitOfWorkRepository
    {
        Task<DateOnly?> PrepareDailyCutoverAsync(
            int machineId,
            DateOnly previousDate);
        Task MarkDailyCutoverCommandSentAsync(int machineId, DateOnly reportDate);
        Task CancelDailyCutoverAsync(int machineId, DateOnly reportDate);
        Task<DateOnly?> ClearPendingDailyCutoverAsync(int machineId);
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
