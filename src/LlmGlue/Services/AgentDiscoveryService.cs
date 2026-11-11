using LlmGlue.Api;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Reconciliation;
using LlmGlue.Observability;

namespace LlmGlue.Services;

public sealed class AgentDiscoveryService(
    IEnumerable<IModelDiscoveryAdapter> adapters,
    ApiState state,
    ILogger<AgentDiscoveryService> logger,
    TimeSpan interval,
    TimeSpan removalGrace,
    IReadOnlyList<string> excludeModels) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = CorrelationIds.BeginScope(logger);
            var discovered = new List<LlmGlue.Core.Domain.ModelDeployment>();
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

            var filtered = ModelExclusionPatterns.Apply(discovered, excludeModels);
            if (filtered.Count != discovered.Count)
                logger.LogInformation("Agent exclusions removed {Count} deployments from export", discovered.Count - filtered.Count);
            try
            {
                var now = state.TimeProvider.GetUtcNow();
                var baseline = DiscoverySnapshot.Create(state.Snapshot.CapturedAt, ModelExclusionPatterns.Apply(state.Snapshot.Deployments, excludeModels));
                var reconciled = CatalogReconciler.Reconcile(baseline, filtered, now, removalGrace);
                foreach (var conflict in reconciled.Conflicts) logger.LogError("Catalog conflict: {Conflict}", conflict);
                await state.PublishAsync(reconciled.Snapshot, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Catalog publication failed; retaining last-known-good state for retry"); }
            await Task.Delay(interval, stoppingToken);
        }
    }
}
