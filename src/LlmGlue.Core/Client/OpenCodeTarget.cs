using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Generation;

namespace LlmGlue.Core.Client;

public sealed class OpenCodeTarget(string path, AtomicFileInstaller installer, ITargetActivator activator, string ownerId = "default") : IConfigurationTarget
{
    private const int OwnershipSchemaVersion = 2;
    private const string LegacyOwnerId = "legacy-v1";
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

        var previousOwnership = await LoadOwnershipAsync(existingProviders, cancellationToken);
        var nextOwnership = previousOwnership.Clone();
        var currentOwnership = nextOwnership.Controller(ownerId);
        var previouslyManagedProviders = new HashSet<string>(currentOwnership.Providers, StringComparer.Ordinal);
        var requestedDisabled = DisabledProviderNames(generated, "generated");
        var existingDisabled = DisabledProviders(existing);
        foreach (var stale in currentOwnership.DisabledProviders.Except(requestedDisabled, StringComparer.Ordinal).ToArray())
        {
            currentOwnership.DisabledProviders.Remove(stale);
            if (!nextOwnership.Controllers.Where(x => x.Key != ownerId).Any(x => x.Value.DisabledProviders.Contains(stale)))
                RemoveString(existingDisabled, stale);
        }
        foreach (var provider in requestedDisabled)
        {
            if (!ContainsString(existingDisabled, provider))
            {
                existingDisabled.Add(provider);
                currentOwnership.DisabledProviders.Add(provider);
            }
            else if (currentOwnership.DisabledProviders.Contains(provider)
                || nextOwnership.Controllers.Where(x => x.Key != ownerId).Any(x => x.Value.DisabledProviders.Contains(provider)))
            {
                currentOwnership.DisabledProviders.Add(provider);
                if (nextOwnership.Controllers.TryGetValue(LegacyOwnerId, out var legacy)) legacy.DisabledProviders.Remove(provider);
            }
        }
        if (existingDisabled.Count == 0) existing.Remove("disabled_providers");
        else existing["disabled_providers"] = existingDisabled;

        foreach (var stale in currentOwnership.Providers.Except(generatedProviders.Select(x => x.Key), StringComparer.Ordinal).ToArray())
            existingProviders.Remove(stale);
        currentOwnership.Providers.Clear();
        foreach (var provider in generatedProviders)
        {
            var conflictingOwner = nextOwnership.Controllers.FirstOrDefault(x => x.Key != ownerId && x.Key != LegacyOwnerId && x.Value.Providers.Contains(provider.Key));
            if (!string.IsNullOrEmpty(conflictingOwner.Key))
                throw new InvalidOperationException($"OpenCode provider '{provider.Key}' is owned by another configured artifact source.");
            var legacyOwned = nextOwnership.Controllers.TryGetValue(LegacyOwnerId, out var legacy) && legacy.Providers.Contains(provider.Key);
            if (existingProviders.ContainsKey(provider.Key) && !legacyOwned && !previouslyManagedProviders.Contains(provider.Key))
                throw new InvalidOperationException($"OpenCode provider '{provider.Key}' already exists without ownership by this artifact source.");
            if (legacyOwned) legacy!.Providers.Remove(provider.Key);
            existingProviders[provider.Key] = provider.Value?.DeepClone();
            currentOwnership.Providers.Add(provider.Key);
        }
        nextOwnership.RemoveEmptyControllers();

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

    private async Task<OwnershipState> LoadOwnershipAsync(JsonObject existingProviders, CancellationToken cancellationToken)
    {
        if (!File.Exists(OwnershipPath)) return new();
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(OwnershipPath, cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"Invalid OpenCode ownership state: {OwnershipPath}");
        if (schema.GetInt32() == 1)
        {
            if (!root.TryGetProperty("disabledProviders", out var disabled) || disabled.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Invalid OpenCode ownership state: {OwnershipPath}");
            return new OwnershipState(new Dictionary<string, ControllerOwnership>(StringComparer.Ordinal)
            {
                [LegacyOwnerId] = new(
                    existingProviders.Select(x => x.Key).Where(x => x.StartsWith("llm-glue-", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal),
                    StringSet(disabled))
            });
        }
        if (schema.GetInt32() != OwnershipSchemaVersion || !root.TryGetProperty("controllers", out var controllers) || controllers.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Invalid OpenCode ownership state: {OwnershipPath}");
        var values = new Dictionary<string, ControllerOwnership>(StringComparer.Ordinal);
        foreach (var controller in controllers.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(controller.Name) || controller.Value.ValueKind != JsonValueKind.Object
                || !controller.Value.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array
                || !controller.Value.TryGetProperty("disabledProviders", out var disabled) || disabled.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Invalid OpenCode ownership state: {OwnershipPath}");
            values[controller.Name] = new(StringSet(providers), StringSet(disabled));
        }
        return new OwnershipState(values);
    }

    private async Task SaveOwnershipAsync(OwnershipState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(OwnershipPath))!;
        Directory.CreateDirectory(directory);
        if (state.Controllers.Count == 0 && !File.Exists(OwnershipPath)) return;
        var controllers = state.Controllers.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(
            x => x.Key,
            x => new
            {
                providers = x.Value.Providers.Order(StringComparer.Ordinal).ToArray(),
                disabledProviders = x.Value.DisabledProviders.Order(StringComparer.Ordinal).ToArray()
            }, StringComparer.Ordinal);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = OwnershipSchemaVersion,
            controllers
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

    private static HashSet<string> StringSet(JsonElement array) => new(array.EnumerateArray().Select(x =>
        x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())
            ? x.GetString()!
            : throw new InvalidDataException("Ownership provider names must be nonempty strings.")), StringComparer.Ordinal);

    private sealed class ControllerOwnership(HashSet<string> providers, HashSet<string> disabledProviders)
    {
        public HashSet<string> Providers { get; } = providers;
        public HashSet<string> DisabledProviders { get; } = disabledProviders;
        public ControllerOwnership Clone() => new(new(Providers, StringComparer.Ordinal), new(DisabledProviders, StringComparer.Ordinal));
    }

    private sealed class OwnershipState(Dictionary<string, ControllerOwnership>? controllers = null)
    {
        public Dictionary<string, ControllerOwnership> Controllers { get; } = controllers ?? new(StringComparer.Ordinal);
        public ControllerOwnership Controller(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("OpenCode ownership source ID must be nonempty.");
            if (!Controllers.TryGetValue(id, out var value)) Controllers[id] = value = new(new(StringComparer.Ordinal), new(StringComparer.Ordinal));
            return value;
        }
        public OwnershipState Clone() => new(Controllers.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal));
        public void RemoveEmptyControllers()
        {
            foreach (var key in Controllers.Where(x => x.Value.Providers.Count == 0 && x.Value.DisabledProviders.Count == 0).Select(x => x.Key).ToArray())
                Controllers.Remove(key);
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
