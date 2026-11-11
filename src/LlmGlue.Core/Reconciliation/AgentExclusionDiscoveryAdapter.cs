using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Reconciliation;

public sealed class AgentExclusionDiscoveryAdapter(
    IModelDiscoveryAdapter inner,
    IReadOnlyList<string> patterns) : IModelDiscoveryAdapter
{
    public string ProviderType => inner.ProviderType;

    public async Task<IReadOnlyList<ModelDeployment>> DiscoverAsync(CancellationToken cancellationToken) =>
        ModelExclusionPatterns.Apply(await inner.DiscoverAsync(cancellationToken), patterns);
}
