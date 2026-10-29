using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Services;

namespace TAKAHATA.Infrastructure.Services
{
    public sealed class ProcessStopFinalizerBackgroundService : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PlcDrainWindow = TimeSpan.FromSeconds(90);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ProcessStopFinalizerBackgroundService> _logger;

        public ProcessStopFinalizerBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<ProcessStopFinalizerBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repository = scope.ServiceProvider
                        .GetRequiredService<IUnitOfWorkRepository>();
                    var cutoff = VietnamTime.Now - PlcDrainWindow;
                    var finalizedCount =
                        await repository.FinalizeExpiredProcessStopsAsync(cutoff);

                    if (finalizedCount > 0)
                    {
                        _logger.LogWarning(
                            "Đã chốt {ProcessCount} công đoạn sau 90 giây chờ gói PLC cuối không nhận được. " +
                            "EndTime được giữ theo thời điểm người dùng yêu cầu dừng.",
                            finalizedCount);
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(
                        exception,
                        "Lỗi khi chốt các công đoạn đã hết thời gian chờ gói PLC cuối.");
                }

                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }
}
