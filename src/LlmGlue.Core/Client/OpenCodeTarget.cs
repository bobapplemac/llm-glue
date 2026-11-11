using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Generation;

namespace LlmGlue.Core.Client;

public sealed class OpenCodeTarget(string path, AtomicFileInstaller installer, ITargetActivator activator) : IConfigurationTarget
{
    public string TargetType => "opencode";

    public async Task ApplyAsync(GeneratedArtifact artifact, CancellationToken cancellationToken)
    {
        ArtifactValidator.Validate(artifact);
        var generated = Parse(artifact.Content);
        var existing = File.Exists(path) ? Parse(await File.ReadAllBytesAsync(path, cancellationToken)) : new JsonObject { ["$schema"] = "https://opencode.ai/config.json" };
        var generatedProviders = generated["providers"] as JsonObject ?? throw new InvalidDataException("Generated OpenCode config has no providers.");
        var existingProviders = existing["providers"] as JsonObject;
        if (existingProviders is null) existing["providers"] = existingProviders = new JsonObject();

        foreach (var key in existingProviders.Select(x => x.Key).Where(x => x.StartsWith("llm-glue-", StringComparison.Ordinal)).ToArray()) existingProviders.Remove(key);
        foreach (var provider in generatedProviders) existingProviders[provider.Key] = provider.Value?.DeepClone();

        var bytes = JsonSerializer.SerializeToUtf8Bytes(existing, new JsonSerializerOptions { WriteIndented = true });
        await installer.ApplyAsync(path, bytes, ValidateJsonAsync, activator.ActivateAndVerifyAsync, cancellationToken);
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
