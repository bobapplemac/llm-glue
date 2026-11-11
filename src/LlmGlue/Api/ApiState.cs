using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;

namespace LlmGlue.Api;

public sealed class ApiState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IReadOnlyDictionary<string, IConfigurationGenerator> _generators;
    private DiscoverySnapshot _snapshot = DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []);

    public ApiState(IEnumerable<IConfigurationGenerator> generators, ArtifactStore store, TimeProvider? timeProvider = null)
    {
        _generators = generators.ToDictionary(x => x.TargetType, StringComparer.Ordinal);
        Store = store;
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    public ArtifactStore Store { get; }
    public TimeProvider TimeProvider { get; }
    public DiscoverySnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async Task PublishAsync(DiscoverySnapshot snapshot, CancellationToken cancellationToken)
    {
        var generated = new List<(string Target, GeneratedArtifact Artifact)>();
        foreach (var pair in _generators.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var artifact = await pair.Value.GenerateAsync(snapshot, cancellationToken);
            ArtifactValidator.Validate(artifact);
            generated.Add((pair.Key, artifact));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var item in generated) Store.Put(item.Target, snapshot.ContentHash, TimeProvider.GetUtcNow(), item.Artifact);
            Volatile.Write(ref _snapshot, snapshot);
        }
        finally { _gate.Release(); }
    }
}
