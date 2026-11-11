using System.Net.Http.Headers;
using System.Net.Http.Json;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Reconciliation;

namespace LlmGlue.Services;

public sealed class RemoteInventoryPublisher(HttpClient httpClient, string? writeToken)
{
    public async Task PublishAsync(DiscoverySnapshot snapshot, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/agent/inventory")
        {
            Content = JsonContent.Create(snapshot)
        };
        if (!string.IsNullOrEmpty(writeToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", writeToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class RemoteAgentDiscoveryService(
    IEnumerable<IModelDiscoveryAdapter> adapters,
    RemoteInventoryPublisher publisher,
    ILogger<RemoteAgentDiscoveryService> logger,
    TimeProvider timeProvider,
    TimeSpan interval,
    TimeSpan removalGrace) : BackgroundService
{
    private DiscoverySnapshot _previous = DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var discovered = new List<ModelDeployment>();
            foreach (var adapter in adapters)
            {
                try
                {
                    var models = await adapter.DiscoverAsync(stoppingToken);
                    discovered.AddRange(models);
                    logger.LogInformation("Provider {Provider} discovered {Count} deployments", adapter.ProviderType, models.Count);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogError(ex, "Provider {Provider} discovery failed; retaining prior deployments through grace", adapter.ProviderType); }
            }

            var reconciled = CatalogReconciler.Reconcile(_previous, discovered, timeProvider.GetUtcNow(), removalGrace);
            _previous = reconciled.Snapshot;
            try
            {
                await publisher.PublishAsync(reconciled.Snapshot, stoppingToken);
                logger.LogInformation("Submitted inventory revision {Revision} with {Count} deployments", reconciled.Snapshot.ContentHash, reconciled.Snapshot.Deployments.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Remote inventory submission failed; local inventory is retained for retry"); }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
