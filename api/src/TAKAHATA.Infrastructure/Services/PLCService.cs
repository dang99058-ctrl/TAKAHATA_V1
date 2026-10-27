using EasyModbus;
using Microsoft.Extensions.Logging;
using TAKAHATA.Application.Common.Interface.Services;
using TAKAHATA.Contracts.Settings;
using TAKAHATA.Domain.Common.Enum;
using TAKAHATA.Domain.PLC;
using Machine = TAKAHATA.Domain.MasterData.Machine;

namespace TAKAHATA.Infrastructure.Services
{
    public class PLCService : IPLCService
    {
        private readonly ILogger<PLCService> _logger;
        private readonly IPLCSettingsService _settingsService;

        public PLCService(
            IPLCSettingsService settingsService,
            ILogger<PLCService> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task<PLCResult<bool>> IsProcessing(Machine machine)
        {
            _logger.LogInformation($"{nameof(IsProcessing)}: Start");

            var ip = _settingsService.GetIpAddress();
            var port = _settingsService.GetPort();

            _logger.LogInformation($"{nameof(IsProcessing)}: Connection info {ip}:{port}");

            var modbusClient = new ModbusClient(ip, port);

            try
            {
                modbusClient.Connect();

                var status = modbusClient.ReadHoldingRegisters(machine.StatusBitAddress, 1);

                if (status[0] == (int)MachineStatus.Running)
                    return PLCResult<bool>.Success(true);
                else
                    return PLCResult<bool>.Success(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(ReadMachineData)}: {ex.Message}");
                return PLCResult<bool>.Fail(ex);
            }
            finally
            {
                if (modbusClient != null && modbusClient.Connected)
                {
                    modbusClient.Disconnect();
                }
            }
        }

        public async Task<PLCResult<ReadResult>> ReadMachineData(Machine machine)
        {
            _logger.LogInformation($"{nameof(ReadMachineData)}: Start");

            var ip = _settingsService.GetIpAddress();
            var port = _settingsService.GetPort();

            _logger.LogInformation($"{nameof(ReadMachineData)}: Connection info {ip}:{port}");

            var modbusClient = new ModbusClient(ip, port);

            try
            {
                modbusClient.Connect();

                var status = modbusClient.ReadHoldingRegisters(machine.StatusBitAddress, 1);
                var partId = modbusClient.ReadHoldingRegisters(machine.PartBitAddress, 1);
                var partProcessId = modbusClient.ReadHoldingRegisters(machine.PartProcessBitAddress, 1);

                return PLCResult<ReadResult>.Success(new ReadResult
                {
                    Status = status[0],
                    PartId = partId[0],
                    PartProcessId = partProcessId[0]
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(ReadMachineData)}: {ex.Message}");
                return PLCResult<ReadResult>.Fail(ex);
            }
            finally
            {
                if (modbusClient != null && modbusClient.Connected)
                {
                    modbusClient.Disconnect();
                }
            }
        }

        public async Task<PLCResult<List<ReadResult>>> ReadMachinesData(List<Machine> machines)
        {
            _logger.LogInformation($"{nameof(ReadMachinesData)}: Start");

            var ip = _settingsService.GetIpAddress();
            var port = _settingsService.GetPort();

            _logger.LogInformation($"{nameof(ReadMachinesData)}: Connection info {ip}:{port}");

            var modbusClient = new ModbusClient(ip, port);

            try
            {
                modbusClient.Connect();

                var results = new List<ReadResult>();

                foreach (var machine in machines)
                {
                    var status = modbusClient.ReadHoldingRegisters(machine.StatusBitAddress, 1);
                    var partId = modbusClient.ReadHoldingRegisters(machine.PartBitAddress, 1);
                    var partProcessId = modbusClient.ReadHoldingRegisters(machine.PartProcessBitAddress, 1);

                    var readResult = new ReadResult
                    {
                        Status = status[0],
                        PartId = partId[0],
                        PartProcessId = partProcessId[0]
                    };
                    results.Add(readResult);

                    await Task.Delay(50);
                }

                return PLCResult<List<ReadResult>>.Success(results);
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(ReadMachinesData)}: {ex.Message}");
                return PLCResult<List<ReadResult>>.Fail(ex);
            }
            finally
            {
                if (modbusClient != null && modbusClient.Connected)
                {
                    modbusClient.Disconnect();
                }
            }
        }

        public async Task<PLCResult<ReadMachineBitsResponse>> ReadMachineData(
            int statusBitAddress,
            int partBitAddress,
            int partProcessBitAddress,
            int processStatusBitAddress)
        {
            _logger.LogInformation($"{nameof(ReadMachinesData)}: Start");

            var ip = _settingsService.GetIpAddress();
            var port = _settingsService.GetPort();

            _logger.LogInformation($"{nameof(ReadMachinesData)}: Connection info {ip}:{port}");

            var modbusClient = new ModbusClient(ip, port);

            try
            {
                modbusClient.Connect();

                var status = modbusClient.ReadHoldingRegisters(statusBitAddress, 1);
                var partId = modbusClient.ReadHoldingRegisters(partBitAddress, 1);
                var partProcessId = modbusClient.ReadHoldingRegisters(partProcessBitAddress, 1);
                var processStatus = modbusClient.ReadHoldingRegisters(processStatusBitAddress, 1);

                return PLCResult<ReadMachineBitsResponse>.Success(new ReadMachineBitsResponse(status[0], partId[0], partProcessId[0], processStatus[0]));
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(ReadMachinesData)}: {ex.Message}");
                return PLCResult<ReadMachineBitsResponse>.Fail(ex);
            }
            finally
            {
                if (modbusClient != null && modbusClient.Connected)
                {
                    modbusClient.Disconnect();
                }
            }
        }

        public async Task<PLCResult<WriteResult>> WriteMachineData(Machine machine, int partId, int processId, int status)
        {
            _logger.LogInformation($"{nameof(WriteMachineData)}: Start");

            var ip = _settingsService.GetIpAddress();
            var port = _settingsService.GetPort();

            _logger.LogInformation($"{nameof(WriteMachineData)}: Connection info {ip}:{port}");

            var modbusClient = new ModbusClient(ip, port);

            try
            {
                modbusClient.Connect();

                int lastProcessId = modbusClient.ReadHoldingRegisters(machine.PartProcessBitAddress, 1)[0];

                modbusClient.WriteSingleRegister(machine.PartBitAddress, partId);
                modbusClient.WriteSingleRegister(machine.PartProcessBitAddress, processId);
                modbusClient.WriteSingleRegister(machine.ProcessStatusBitAddress, status);

                return PLCResult<WriteResult>.Success(new WriteResult
                {
                    BeforeProcessId = lastProcessId,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(WriteMachineData)}: {ex.Message}");
                return PLCResult<WriteResult>.Fail(ex);
            }
            finally
            {
                if (modbusClient != null && modbusClient.Connected)
                {
                    modbusClient.Disconnect();
                }
            }
        }

        public async Task<PLCResult<WriteResult>> WriteMachineStatus(Machine machine, int status)
        {
            _logger.LogInformation($"{nameof(WriteMachineStatus)}: Start");

            var ip = _settingsService.GetIpAddress();
            var port = _settingsService.GetPort();

            _logger.LogInformation($"{nameof(WriteMachineStatus)}: Connection info {ip}:{port}");

            var modbusClient = new ModbusClient(ip, port);

            try
            {
                modbusClient.Connect();

                int lastProcessId = modbusClient.ReadHoldingRegisters(machine.PartProcessBitAddress, 1)[0];

                modbusClient.WriteSingleRegister(machine.ProcessStatusBitAddress, status);

                return PLCResult<WriteResult>.Success(new WriteResult
                {
                    BeforeProcessId = lastProcessId,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(WriteMachineStatus)}: {ex.Message}");
                return PLCResult<WriteResult>.Fail(ex);
            }
            finally
            {
                if (modbusClient != null && modbusClient.Connected)
                {
                    modbusClient.Disconnect();
                }
            }
        }

        public Task<PLCResult<WriteResult>> WriteProcessStatus(Machine machine, int status)
        {
            _logger.LogInformation(
                "{Method}: Writing process status {Status} to register {Address}",
                nameof(WriteProcessStatus),
                status,
                machine.ProcessStatusBitAddress);

            var modbusClient = new ModbusClient(
                _settingsService.GetIpAddress(),
                _settingsService.GetPort());

            try
            {
                modbusClient.Connect();
                modbusClient.WriteSingleRegister(machine.ProcessStatusBitAddress, status);
                return Task.FromResult(
                    PLCResult<WriteResult>.Success(new WriteResult()));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{Method}: Failed to write process status for machine {MachineCode}",
                    nameof(WriteProcessStatus),
                    machine.Code);
                return Task.FromResult(PLCResult<WriteResult>.Fail(ex));
            }
            finally
            {
                if (modbusClient.Connected)
                    modbusClient.Disconnect();
            }
        }

        public async Task<PLCResult<WriteResult>> WriteMachineData(
            int statusBitAddress,
            int partBitAddress,
            int partProcessBitAddress,
            int processStatusBitAddress,
            int statusValue,
            int partValue,
            int partProcessValue,
            int processStatusValue)
        {
            _logger.LogInformation($"{nameof(WriteMachineData)}: Start");

            var ip = _settingsService.GetIpAddress();
            var port = _settingsService.GetPort();

            _logger.LogInformation($"{nameof(WriteMachineData)}: Connection info {ip}:{port}");

            var modbusClient = new ModbusClient(ip, port);

            try
            {
                modbusClient.Connect();

                modbusClient.WriteSingleRegister(statusBitAddress, statusValue);
                modbusClient.WriteSingleRegister(partBitAddress, partValue);
                modbusClient.WriteSingleRegister(partProcessBitAddress, partProcessValue);
                modbusClient.WriteSingleRegister(processStatusBitAddress, processStatusValue);

                return PLCResult<WriteResult>.Success(new WriteResult());
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(WriteMachineData)}: {ex.Message}");
                return PLCResult<WriteResult>.Fail(ex);
            }
            finally
            {
                if (modbusClient != null && modbusClient.Connected)
                {
                    modbusClient.Disconnect();
                }
            }
        }

        public bool TestConnection()
        {
            try
            {
                var client = new ModbusClient(_settingsService.GetIpAddress(), _settingsService.GetPort());

                client.Connect();

                bool currentValue = client.ReadCoils(10, 1)[0];

                bool newValue = !currentValue;

                client.WriteSingleCoil(10, newValue);

                client.Disconnect();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(TestConnection)}: {ex.Message}");
                return false;
            }
        }
    }
}

