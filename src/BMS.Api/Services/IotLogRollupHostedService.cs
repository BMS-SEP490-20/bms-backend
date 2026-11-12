using System;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BMS.Api.Services;

/// <summary>
/// Periodically aggregates raw IoT telemetry logs into daily summary documents (<c>iot_daily_summaries</c>)
/// before the 90-day MongoDB TTL index expires detailed high-frequency records.
/// Ensures multi-year cold-chain reporting remains fast and lightweight.
/// </summary>
public sealed class IotLogRollupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IotLogRollupHostedService> _logger;

    public IotLogRollupHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<IotLogRollupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay initial rollup so MongoDB indexes and startup seeding finish first
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var telemetryService = scope.ServiceProvider.GetRequiredService<IIotTelemetryService>();

                var nowUtc = DateTime.UtcNow;
                int yesterdayCount = await telemetryService.RunDailyRollupAsync(nowUtc.AddDays(-1), stoppingToken);
                int todayCount = await telemetryService.RunDailyRollupAsync(nowUtc, stoppingToken);

                if (yesterdayCount > 0 || todayCount > 0)
                {
                    _logger.LogInformation(
                        "[IoT Rollup] Updated daily summaries (yesterday: {Yesterday}, today: {Today}).",
                        yesterdayCount, todayCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IoT Rollup] Failed to execute scheduled daily summary rollup.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
