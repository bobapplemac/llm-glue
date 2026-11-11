using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Generation;

public sealed record CatalogStateArtifact(string Target, GeneratedArtifact Artifact);
public sealed record CatalogState(DiscoverySnapshot Snapshot, DateTimeOffset SavedAt, IReadOnlyList<CatalogStateArtifact> Artifacts);

public sealed partial class CatalogStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly string _root;
    private readonly string _revisions;
    private readonly int _retention;

    public CatalogStateRepository(string directory, int retention = 5)
    {
        _root = Path.GetFullPath(directory);
        _revisions = Path.Combine(_root, "revisions");
        _retention = retention > 0 ? retention : throw new ArgumentOutOfRangeException(nameof(retention));
    }

    public CatalogState? Load()
    {
        var currentPath = Path.Combine(_root, "current");
        if (!File.Exists(currentPath)) return null;
        var revision = File.ReadAllText(currentPath, Encoding.UTF8).Trim();
        if (!RevisionPattern().IsMatch(revision)) throw new InvalidDataException("Persisted current revision is invalid.");
        var directory = OwnedRevisionPath(revision);
        var manifest = JsonSerializer.Deserialize<StateManifest>(File.ReadAllBytes(Path.Combine(directory, "manifest.json")), JsonOptions)
            ?? throw new InvalidDataException("Persisted catalog manifest is empty.");
        if (manifest.SchemaVersion != 1) throw new InvalidDataException($"Unsupported persisted state schema {manifest.SchemaVersion}.");
        var canonical = DiscoverySnapshot.Create(manifest.Snapshot.CapturedAt, manifest.Snapshot.Deployments);
        if (!string.Equals(canonical.ContentHash, manifest.Snapshot.ContentHash, StringComparison.Ordinal))
            throw new InvalidDataException("Persisted inventory hash validation failed.");

        var artifacts = new List<CatalogStateArtifact>();
        foreach (var item in manifest.Artifacts)
        {
            if (artifacts.Any(x => string.Equals(x.Target, item.Target, StringComparison.Ordinal))) throw new InvalidDataException($"Persisted artifact target '{item.Target}' is duplicated.");
            if (!TargetPattern().IsMatch(item.Target) || Path.GetFileName(item.File) != item.File)
                throw new InvalidDataException("Persisted artifact manifest contains an invalid target or filename.");
            var content = File.ReadAllBytes(Path.Combine(directory, item.File));
            var hash = Convert.ToHexStringLower(SHA256.HashData(content));
            if (!string.Equals(hash, item.ContentHash, StringComparison.Ordinal)) throw new InvalidDataException($"Persisted artifact '{item.Target}' hash validation failed.");
            var artifact = new GeneratedArtifact(item.MediaType, content, hash, item.SchemaVersion);
            ArtifactValidator.Validate(artifact);
            artifacts.Add(new(item.Target, artifact));
        }
        if (!string.Equals(StateId(canonical, artifacts), revision, StringComparison.Ordinal))
            throw new InvalidDataException("Persisted state revision validation failed.");
        return new(canonical, manifest.SavedAt, artifacts);
    }

    public async Task SaveAsync(DiscoverySnapshot snapshot, IReadOnlyList<CatalogStateArtifact> artifacts, DateTimeOffset savedAt, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_revisions);
        var stateId = StateId(snapshot, artifacts);
        var final = OwnedRevisionPath(stateId);
        if (!Directory.Exists(final))
        {
            var stage = Path.Combine(_revisions, $".stage-{Guid.NewGuid():N}");
            EnsureOwned(stage);
            Directory.CreateDirectory(stage);
            try
            {
                var entries = new List<ArtifactManifest>();
                foreach (var item in artifacts.OrderBy(x => x.Target, StringComparer.Ordinal))
                {
                    if (!TargetPattern().IsMatch(item.Target)) throw new InvalidDataException($"Invalid persisted artifact target '{item.Target}'.");
                    ArtifactValidator.Validate(item.Artifact);
                    var actualHash = Convert.ToHexStringLower(SHA256.HashData(item.Artifact.Content));
                    if (!string.Equals(actualHash, item.Artifact.ContentHash, StringComparison.Ordinal)) throw new InvalidDataException($"Artifact '{item.Target}' has an invalid content hash.");
                    var filename = item.Target + ".artifact";
                    await WriteDurableAsync(Path.Combine(stage, filename), item.Artifact.Content, cancellationToken);
                    entries.Add(new(item.Target, filename, item.Artifact.MediaType, item.Artifact.ContentHash, item.Artifact.SchemaVersion));
                }
                var manifest = new StateManifest(1, savedAt, snapshot, entries);
                await WriteDurableAsync(Path.Combine(stage, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions), cancellationToken);
                Directory.Move(stage, final);
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            }
        }

        var marker = Encoding.UTF8.GetBytes(stateId + "\n");
        var markerStage = Path.Combine(_root, $".current-{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteDurableAsync(markerStage, marker, cancellationToken);
            File.Move(markerStage, Path.Combine(_root, "current"), overwrite: true);
        }
        finally
        {
            if (File.Exists(markerStage)) File.Delete(markerStage);
        }
        Trim(stateId);
    }

    private void Trim(string current)
    {
        var candidates = Directory.EnumerateDirectories(_revisions)
            .Where(path => RevisionPattern().IsMatch(Path.GetFileName(path)) && File.Exists(Path.Combine(path, "manifest.json")))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .Where(path => !string.Equals(Path.GetFileName(path), current, StringComparison.Ordinal))
            .Skip(Math.Max(0, _retention - 1));
        foreach (var path in candidates)
        {
            EnsureOwned(path);
            Directory.Delete(path, recursive: true);
        }
    }

    private string OwnedRevisionPath(string revision)
    {
        if (!RevisionPattern().IsMatch(revision)) throw new InvalidDataException("Catalog revision must be a lowercase SHA-256 hash.");
        var path = Path.Combine(_revisions, revision);
        EnsureOwned(path);
        return path;
    }

    private void EnsureOwned(string path)
    {
        var root = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetFullPath(path).StartsWith(root, comparison)) throw new InvalidOperationException("State path escaped the configured state directory.");
    }

    private static async Task WriteDurableAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static string StateId(DiscoverySnapshot snapshot, IEnumerable<CatalogStateArtifact> artifacts)
    {
        var identity = new StringBuilder(snapshot.ContentHash);
        foreach (var artifact in artifacts.OrderBy(x => x.Target, StringComparer.Ordinal))
            identity.Append('\n').Append(artifact.Target).Append('=').Append(artifact.Artifact.ContentHash);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())));
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();
    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetPattern();

    private sealed record StateManifest(int SchemaVersion, DateTimeOffset SavedAt, DiscoverySnapshot Snapshot, IReadOnlyList<ArtifactManifest> Artifacts);
    private sealed record ArtifactManifest(string Target, string File, string MediaType, string ContentHash, int SchemaVersion);
}
