using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Reconciliation;

namespace LlmGlue.Api;

public sealed class ApiState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IReadOnlyDictionary<string, IConfigurationGenerator> _generators;
    private readonly IReadOnlyList<ExclusionRuleOptions> _exclusions;
    private DiscoverySnapshot _snapshot = DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []);
    private DiscoverySnapshot _publishedSnapshot = DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []);

    public ApiState(IEnumerable<IConfigurationGenerator> generators, ArtifactStore store, IReadOnlyList<ExclusionRuleOptions>? exclusions = null, TimeProvider? timeProvider = null)
    {
        _generators = generators.ToDictionary(x => x.TargetType, StringComparer.Ordinal);
        _exclusions = exclusions ?? [];
        Store = store;
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    public ArtifactStore Store { get; }
    public TimeProvider TimeProvider { get; }
    public DiscoverySnapshot Snapshot => Volatile.Read(ref _snapshot);
    public DiscoverySnapshot PublishedSnapshot => Volatile.Read(ref _publishedSnapshot);

    public async Task<bool> PublishAsync(DiscoverySnapshot snapshot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (snapshot.CapturedAt < _snapshot.CapturedAt) return false;
            var view = CatalogPublicationPolicy.Apply(snapshot, _exclusions);
            var generated = new List<(string Target, GeneratedArtifact Artifact)>();
            foreach (var pair in _generators.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var artifact = await pair.Value.GenerateAsync(view.Published, cancellationToken);
                ArtifactValidator.Validate(artifact);
                generated.Add((pair.Key, artifact));
            }

            foreach (var item in generated) Store.Put(item.Target, view.Diagnostics.ContentHash, TimeProvider.GetUtcNow(), item.Artifact);
            Volatile.Write(ref _publishedSnapshot, view.Published);
            Volatile.Write(ref _snapshot, view.Diagnostics);
            return true;
        }
        finally { _gate.Release(); }
    }
}
