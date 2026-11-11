using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Generation;

namespace LlmGlue.Core.Client;

public sealed class OpenCodeTarget(string path, AtomicFileInstaller installer, ITargetActivator activator) : IConfigurationTarget
{
    private const int OwnershipSchemaVersion = 1;
    private string OwnershipPath => path + ".llm-glue-ownership.json";
    public string TargetType => "opencode";

    public async Task ApplyAsync(GeneratedArtifact artifact, CancellationToken cancellationToken)
    {
        ArtifactValidator.Validate(artifact);
        var destination = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var targetLock = new FileStream(destination + ".llm-glue-target.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        var generated = Parse(artifact.Content);
        var existing = File.Exists(path) ? Parse(await File.ReadAllBytesAsync(path, cancellationToken)) : new JsonObject { ["$schema"] = "https://opencode.ai/config.json" };
        var generatedProviders = generated["providers"] as JsonObject ?? throw new InvalidDataException("Generated OpenCode config has no providers.");
        JsonObject existingProviders;
        if (existing["providers"] is null) existing["providers"] = existingProviders = new JsonObject();
        else existingProviders = existing["providers"] as JsonObject ?? throw new InvalidDataException("Existing OpenCode providers must be an object.");

        var previousOwnership = await LoadOwnershipAsync(cancellationToken);
        var requestedDisabled = DisabledProviderNames(generated, "generated");
        var existingDisabled = DisabledProviders(existing);
        foreach (var stale in previousOwnership.Except(requestedDisabled, StringComparer.Ordinal))
            RemoveString(existingDisabled, stale);
        var nextOwnership = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in requestedDisabled)
        {
            if (!ContainsString(existingDisabled, provider))
            {
                existingDisabled.Add(provider);
                nextOwnership.Add(provider);
            }
            else if (previousOwnership.Contains(provider)) nextOwnership.Add(provider);
        }
        if (existingDisabled.Count == 0) existing.Remove("disabled_providers");
        else existing["disabled_providers"] = existingDisabled;

        foreach (var key in existingProviders.Select(x => x.Key).Where(x => x.StartsWith("llm-glue-", StringComparison.Ordinal)).ToArray()) existingProviders.Remove(key);
        foreach (var provider in generatedProviders) existingProviders[provider.Key] = provider.Value?.DeepClone();

        var bytes = JsonSerializer.SerializeToUtf8Bytes(existing, new JsonSerializerOptions { WriteIndented = true });
        await SaveOwnershipAsync(nextOwnership, cancellationToken);
        try
        {
            await installer.ApplyAsync(path, bytes, ValidateJsonAsync, activator.ActivateAndVerifyAsync, cancellationToken);
        }
        catch (Exception applyError)
        {
            try { await SaveOwnershipAsync(previousOwnership, CancellationToken.None); }
            catch (Exception ownershipError)
            {
                throw new InvalidOperationException("OpenCode apply failed and ownership-state rollback also failed.", new AggregateException(applyError, ownershipError));
            }
            throw;
        }
    }

    private async Task<HashSet<string>> LoadOwnershipAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(OwnershipPath)) return new(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(OwnershipPath, cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != OwnershipSchemaVersion ||
            !root.TryGetProperty("disabledProviders", out var providers) || providers.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Invalid OpenCode ownership state: {OwnershipPath}");
        return new HashSet<string>(providers.EnumerateArray().Select(x =>
            x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())
                ? x.GetString()!
                : throw new InvalidDataException("Ownership provider names must be nonempty strings.")), StringComparer.Ordinal);
    }

    private async Task SaveOwnershipAsync(IEnumerable<string> providers, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(OwnershipPath))!;
        Directory.CreateDirectory(directory);
        var ordered = providers.Order(StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0 && !File.Exists(OwnershipPath)) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = OwnershipSchemaVersion,
            disabledProviders = ordered
        }, new JsonSerializerOptions { WriteIndented = true });
        if (File.Exists(OwnershipPath) && (await File.ReadAllBytesAsync(OwnershipPath, cancellationToken)).AsSpan().SequenceEqual(bytes)) return;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(OwnershipPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, OwnershipPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static HashSet<string> DisabledProviderNames(JsonObject root, string source)
    {
        if (root["disabled_providers"] is null) return new(StringComparer.Ordinal);
        if (root["disabled_providers"] is not JsonArray array) throw new InvalidDataException($"OpenCode {source} disabled_providers must be an array.");
        return new HashSet<string>(array.Select(x => JsonProviderName(x, $"OpenCode {source} disabled provider names must be nonempty strings.")), StringComparer.Ordinal);
    }

    private static JsonArray DisabledProviders(JsonObject existing)
    {
        if (existing["disabled_providers"] is null) return [];
        if (existing["disabled_providers"] is not JsonArray array) throw new InvalidDataException("Existing OpenCode disabled_providers must be an array.");
        foreach (var item in array) _ = JsonProviderName(item, "Existing OpenCode disabled provider names must be nonempty strings.");
        return (JsonArray)array.DeepClone();
    }

    private static string JsonProviderName(JsonNode? node, string message)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name)) return name;
        throw new InvalidDataException(message);
    }

    private static bool ContainsString(JsonArray array, string value) => array.Any(x => JsonProviderName(x, "Existing OpenCode disabled provider names must be nonempty strings.") == value);
    private static void RemoveString(JsonArray array, string value)
    {
        for (var index = array.Count - 1; index >= 0; index--)
            if (JsonProviderName(array[index], "Existing OpenCode disabled provider names must be nonempty strings.") == value) array.RemoveAt(index);
    }

    private static JsonObject Parse(ReadOnlyMemory<byte> bytes)
    {
        var node = JsonNode.Parse(bytes.Span, new JsonNodeOptions(), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return node as JsonObject ?? throw new InvalidDataException("OpenCode configuration root must be an object.");
    }

    private static Task ValidateJsonAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        _ = Parse(bytes);
        return Task.CompletedTask;
    }
}
