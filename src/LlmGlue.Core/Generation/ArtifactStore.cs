using LlmGlue.Core.Abstractions;

namespace LlmGlue.Core.Generation;

public sealed record StoredArtifact(string Target, string Revision, DateTimeOffset CreatedAt, GeneratedArtifact Artifact);

public sealed class ArtifactStore(int retention = 5)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<StoredArtifact>> _history = new(StringComparer.Ordinal);

    public void Put(string target, string revision, DateTimeOffset createdAt, GeneratedArtifact artifact)
    {
        ArtifactValidator.Validate(artifact);
        lock (_gate)
        {
            if (!_history.TryGetValue(target, out var history)) _history[target] = history = [];
            if (history.Count != 0 && history[0].Artifact.ContentHash == artifact.ContentHash) return;
            history.Insert(0, new(target, revision, createdAt, artifact));
            if (history.Count > retention) history.RemoveRange(retention, history.Count - retention);
        }
    }

    public StoredArtifact? Latest(string target)
    {
        lock (_gate) return _history.TryGetValue(target, out var history) && history.Count != 0 ? history[0] : null;
    }

    public IReadOnlyList<StoredArtifact> History(string target)
    {
        lock (_gate) return _history.TryGetValue(target, out var history) ? history.ToArray() : [];
    }
}
