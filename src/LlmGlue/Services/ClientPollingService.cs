using LlmGlue.Core.Client;
using LlmGlue.Observability;

namespace LlmGlue.Services;

public sealed class ClientPollingService(ArtifactSyncClient client, TimeSpan interval, ILogger<ClientPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = CorrelationIds.BeginScope(logger);
            try
            {
                var results = await client.SyncOnceAsync(stoppingToken);
                foreach (var result in results)
                {
                    if (result.Error is null) logger.LogInformation("Target {Target} sync {State} at revision {Revision}", result.Target, result.Changed ? "applied" : "unchanged", result.Revision);
                    else logger.LogError("Target {Target} sync failed: {Error}", result.Target, result.Error);
                }
                failures = results.Any(x => x.Error is not null) ? Math.Min(failures + 1, 6) : 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { failures = Math.Min(failures + 1, 6); logger.LogError(ex, "Client polling cycle failed"); }

            var multiplier = Math.Pow(2, failures);
            var jitter = 0.8 + Random.Shared.NextDouble() * 0.4;
            var delay = TimeSpan.FromMilliseconds(Math.Min(TimeSpan.FromMinutes(5).TotalMilliseconds, interval.TotalMilliseconds * multiplier * jitter));
            await Task.Delay(delay, stoppingToken);
        }
    }
}
