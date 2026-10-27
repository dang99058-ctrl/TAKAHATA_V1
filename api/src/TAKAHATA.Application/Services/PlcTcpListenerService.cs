using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TAKAHATA.Application.Common.Interface.Repositories;

namespace TAKAHATA.Application.Services
{
    public sealed class PlcTcpListenerService : BackgroundService
    {
        private int _port;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
        private readonly ILogger<PlcTcpListenerService> _logger;
        private long _connectionId;

        private readonly IServiceScopeFactory _serviceScopeFactory;

        public PlcTcpListenerService(
            ILogger<PlcTcpListenerService> logger,
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;

            _port = configuration.GetValue<int>("PortSettings:Port", 4090);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var logScope = _logger.BeginScope(
                new Dictionary<string, object>
                {
                    ["MachineLog"] = true
                });

            // Đảm bảo phần khởi động listener chạy nền.
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunListenerAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "PLC TCP listener lỗi. Thử lại sau {Seconds} giây",
                        RetryDelay.TotalSeconds);
                }

                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("PLC TCP Service đã dừng");
        }

        private async Task RunListenerAsync(CancellationToken stoppingToken)
        {
            // Mỗi lượt chạy có token và danh sách kết nối riêng.
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var connections = new ConcurrentDictionary<long, Task>();
            var listener = new TcpListener(IPAddress.Any, _port);

            try
            {
                listener.Start();

                _logger.LogInformation("PLC TCP Server đang lắng nghe tại 0.0.0.0:{Port}", _port);

                while (!sessionCts.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(sessionCts.Token);

                    long id = Interlocked.Increment(ref _connectionId);

                    var task = ReceiveAsync(client, sessionCts.Token);

                    connections[id] = task;

                    // Theo dõi và dọn task khi kết nối kết thúc.
                    _ = ObserveConnectionAsync(id, task, connections);
                }
            }
            finally
            {
                // Ngừng nhận kết nối mới.
                listener.Stop();

                // Hủy ReadAsync của tất cả kết nối trong lượt chạy này.
                sessionCts.Cancel();

                try
                {
                    await Task.WhenAll(connections.Values);
                }
                catch (OperationCanceledException)
                    when (sessionCts.IsCancellationRequested)
                {
                    // Hủy khi dừng hoặc khởi động lại listener.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lỗi khi dọn các kết nối PLC");
                }

                _logger.LogInformation("Đã đóng lượt lắng nghe TCP port {Port}", _port);
            }
        }

        private async Task ObserveConnectionAsync(
            long id,
            Task task,
            ConcurrentDictionary<long, Task> connections)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                // Quan sát lỗi để không bỏ sót exception của task nền.
                _logger.LogError(
                    ex,
                    "Tác vụ kết nối PLC {ConnectionId} gặp lỗi",
                    id);
            }
            finally
            {
                connections.TryRemove(id, out _);
            }
        }

        private async Task ReceiveAsync(
            TcpClient client,
            CancellationToken cancellationToken)
        {
            string remote = "Unknown";

            using (client)
            {
                try
                {
                    remote = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

                    _logger.LogInformation("PLC đã kết nối: {Remote}", remote);

                    using NetworkStream stream = client.GetStream();

                    byte[] buffer = new byte[4096];

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        int count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);

                        if (count == 0)
                        {
                            _logger.LogInformation("PLC chủ động đóng kết nối: {Remote}", remote);
                            break;
                        }

                        string text = Encoding.ASCII.GetString(buffer, 0, count);

                        _logger.LogInformation(
                            """
                            Nhận dữ liệu PLC: {Remote}
                            Số byte: {Count}
                            TEXT: {Text}
                            """,
                            remote,
                            count,
                            text);

                        // Thanh ghi status | trạng thái máy (8 = gói chốt ngày) | mã Part | mã Process | Machine Running | Machine Waiting | Machine Setup
                        // Format 1100 | 1 | 23 | 159 | 0.50 | 1.00 | 0.03
                        if (!string.IsNullOrEmpty(text))
                        {
                            var input = text.Replace(" ", "");

                            string[] parts = input.Split('|');

                            if (parts.Length != 7)
                            {
                                Console.WriteLine("Chuỗi phải chứa đúng 5 giá trị.");
                                return;
                            }

                            if (int.TryParse(parts[0], out int statusBitAddress)
                                && int.TryParse(parts[1], out int status)
                                && int.TryParse(parts[2], out int partId)
                                && int.TryParse(parts[3], out int processId)
                                &&
                                double.TryParse(
                                    parts[4],
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out double workingTime)
                                &&
                                double.TryParse(
                                    parts[5],
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out double waitingTime)
                                &&
                                double.TryParse(
                                    parts[6],
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out double setupTime))
                            {
                                await using var scope = _serviceScopeFactory.CreateAsyncScope();

                                var machineRepository = scope.ServiceProvider.GetRequiredService<IUnitOfWorkRepository>();

                                await machineRepository.UpdateDataAsync(
                                    statusBitAddress,
                                    status,
                                    partId,
                                    processId,
                                    workingTime,
                                    waitingTime,
                                    setupTime);

                                continue;
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Bản tin PLC chứa giá trị không hợp lệ: {Text}",
                                    text);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Dừng ứng dụng hoặc khởi động lại listener.
                }
                catch (Exception ex) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug(
                        ex,
                        "Đang đóng kết nối PLC: {Remote}",
                        remote);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Lỗi nhận dữ liệu PLC {Remote}. " +
                        "Đóng kết nối này; server tiếp tục lắng nghe",
                        remote);
                }
                finally
                {
                    _logger.LogInformation(
                        "Kết thúc kết nối PLC: {Remote}",
                        remote);
                }
            }
        }
    }
}