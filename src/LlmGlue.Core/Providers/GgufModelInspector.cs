using System.Security.Cryptography;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Gguf;

namespace LlmGlue.Core.Providers;

internal sealed class GgufModelInspector(
    IReadOnlyList<string> allowedDirectories,
    IReadOnlyList<ModelPathMapping> modelPathMappings,
    bool hashModelFiles)
{
    public async Task<ModelDeployment> EnrichAsync(ModelDeployment deployment, CancellationToken cancellationToken, string? providerConfirmedPath = null)
    {
        var path = ProviderPathOrExactRoute(providerConfirmedPath, deployment.Identity.ModelId);
        if (path is null)
            return deployment with { Warnings = AddWarning(deployment, "No provider-confirmed GGUF path or exact allowlisted filename matched this routable model ID; file metadata remains unknown.") };

        try
        {
            var metadata = GgufMetadataReader.Read(path, allowedDirectories);
            var architecture = Text(metadata, "general.architecture");
            var nativeContext = architecture is null ? null : Integer(metadata, $"{architecture}.context_length");
            var properties = new SortedDictionary<string, string>(deployment.Runtime.Properties.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), StringComparer.Ordinal)
            {
                ["gguf.version"] = metadata.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["gguf.metadata"] = "observed"
            };
            Copy(metadata, properties, "general.architecture", "gguf.architecture");
            Copy(metadata, properties, "general.type", "gguf.type");
            Copy(metadata, properties, "general.file_type", "gguf.file_type");
            Copy(metadata, properties, "split.count", "gguf.split_count");
            var mtpLayers = architecture is null ? null : Integer(metadata, $"{architecture}.nextn_predict_layers");
            if (mtpLayers is > 0)
            {
                properties["gguf.mtp"] = "present";
                properties["gguf.mtp_layers"] = mtpLayers.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            string? hash = deployment.Identity.FileHash;
            if (hashModelFiles)
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                properties["gguf.file_hash"] = "sha256-observed";
            }

            var warnings = AddWarning(deployment, providerConfirmedPath is null
                ? "GGUF metadata was inspected only after an exact allowlisted filename matched a provider-confirmed route; the local path is not published."
                : "GGUF metadata was inspected only after the provider-reported path resolved inside a configured directory; the local path is not published.");
            if (mtpLayers is > 0)
                warnings = warnings.Concat([$"GGUF metadata reports {mtpLayers.Value} NextN/MTP layer(s); this is a runtime artifact trait, not a client-selectable variant."])
                    .Distinct(StringComparer.Ordinal).ToArray();

            return deployment with
            {
                Identity = deployment.Identity with
                {
                    Family = deployment.Identity.Family ?? architecture,
                    FileHash = hash
                },
                NativeContext = deployment.NativeContext ?? (nativeContext is > 0
                    ? new Evidence<long>(nativeContext.Value, "gguf:metadata", deployment.LastSeenAt, VerificationState.Observed)
                    : null),
                Runtime = deployment.Runtime with { Properties = properties },
                Warnings = warnings
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or GgufFormatException)
        {
            return deployment with { Warnings = AddWarning(deployment, $"GGUF inspection failed safely ({ex.GetType().Name}); local filesystem details are not published.") };
        }
    }

    private string? ProviderPathOrExactRoute(string? providerConfirmedPath, string modelId)
    {
        if (!string.IsNullOrWhiteSpace(providerConfirmedPath))
        {
            if (!providerConfirmedPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) return null;
            var mapping = modelPathMappings
                .Where(candidate => HasPathPrefix(providerConfirmedPath, candidate.ProviderPrefix))
                .OrderByDescending(candidate => candidate.ProviderPrefix.Length)
                .FirstOrDefault();
            if (mapping is null) return providerConfirmedPath;
            var suffix = providerConfirmedPath[mapping.ProviderPrefix.Length..].TrimStart('/', '\\');
            return Path.Combine(mapping.LocalDirectory, suffix.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        }
        return FindExactRouteFile(modelId);
    }

    private static bool HasPathPrefix(string path, string prefix) => string.Equals(path, prefix, StringComparison.Ordinal)
        || path.Length > prefix.Length
        && path.StartsWith(prefix, StringComparison.Ordinal)
        && path[prefix.Length] is '/' or '\\';

    private string? FindExactRouteFile(string modelId)
    {
        var filename = Path.GetFileName(modelId);
        if (string.IsNullOrWhiteSpace(filename) || !filename.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var directory in allowedDirectories)
        {
            var candidate = Path.Combine(directory, filename);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string[] AddWarning(ModelDeployment deployment, string warning) => deployment.Warnings.Concat([warning]).Distinct(StringComparer.Ordinal).ToArray();
    private static string? Text(GgufMetadata metadata, string key) => metadata.Values.TryGetValue(key, out var value) ? value as string : null;
    private static long? Integer(GgufMetadata metadata, string key)
    {
        if (!metadata.Values.TryGetValue(key, out var value) || value is null) return null;
        try { return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException) { return null; }
    }
    private static void Copy(GgufMetadata metadata, IDictionary<string, string> destination, string source, string target)
    {
        if (metadata.Values.TryGetValue(source, out var value) && value is not null)
            destination[target] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
