using ErrorOr;
using TAKAHATA.Contracts.Settings;
using TAKAHATA.Domain.MasterData;
using TAKAHATA.Domain.PLC;

namespace TAKAHATA.Application.Common.Interface.Services
{
    public interface IPLCService
    {
        Task<PLCResult<WriteResult>> WriteMachineData(Machine machine, int partId, int processId, int status);
        Task<PLCResult<WriteResult>> WriteMachineIdentifiers(
            Machine machine,
            int partId,
            int processId);
        Task<PLCResult<WriteResult>> WriteMachineStatus(Machine machine, int status);
        Task<PLCResult<WriteResult>> WriteProcessStatus(Machine machine, int status);
        Task<PLCResult<ReadResult>> ReadMachineData(Machine machine);
        Task<PLCResult<List<ReadResult>>> ReadMachinesData(List<Machine> machines);
        Task<PLCResult<WriteResult>> WriteMachineData(
            int statusBitAddress,
            int partBitAddress,
            int partProcessBitAddress,
            int processStatusBitAddress,
            int statusValue,
            int partValue,
            int partProcessValue,
            int processStatusValue);

        Task<PLCResult<ReadMachineBitsResponse>> ReadMachineData(
            int statusBitAddress,
            int partBitAddress,
            int partProcessBitAddress,
            int processStatusBitAddress);

        Task<PLCResult<bool>> IsProcessing(Machine machine);
    }
}
