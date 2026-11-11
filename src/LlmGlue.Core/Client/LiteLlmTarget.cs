using System.Text;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Generation;
using YamlDotNet.RepresentationModel;

namespace LlmGlue.Core.Client;

public sealed class LiteLlmTarget(string generatedPath, string primaryConfigPath, AtomicFileInstaller installer, ITargetActivator activator) : IConfigurationTarget
{
    public string TargetType => "litellm";
    public string TargetId => $"litellm:{Path.GetFullPath(generatedPath)}";

    public async Task ApplyAsync(GeneratedArtifact artifact, CancellationToken cancellationToken)
    {
        ArtifactValidator.Validate(artifact);
        if (!File.Exists(primaryConfigPath)) throw new InvalidOperationException($"LiteLLM primary config '{primaryConfigPath}' does not exist.");
        var primary = Load(await File.ReadAllTextAsync(primaryConfigPath, cancellationToken));
        var generated = Load(Encoding.UTF8.GetString(artifact.Content));
        EnsureIncluded(primary);
        EnsureNoAliasCollisions(primary, generated);
        await installer.ApplyAsync(generatedPath, artifact.Content, ValidateYamlAsync, activator.ActivateAndVerifyAsync, cancellationToken);
    }

    private void EnsureIncluded(YamlStream primary)
    {
        var root = Root(primary);
        if (!root.Children.TryGetValue(new YamlScalarNode("include"), out var include)) throw new InvalidOperationException("LiteLLM primary config does not include the LLM Glue generated file.");
        var expected = Path.GetFileName(generatedPath);
        var values = include switch
        {
            YamlScalarNode scalar => [scalar.Value],
            YamlSequenceNode sequence => sequence.Children.OfType<YamlScalarNode>().Select(x => x.Value).ToArray(),
            _ => []
        };
        if (!values.Any(x => string.Equals(Path.GetFileName(x), expected, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException($"LiteLLM primary config include does not reference '{expected}'.");
    }

    private static void EnsureNoAliasCollisions(YamlStream primary, YamlStream generated)
    {
        var manual = Aliases(Root(primary)).ToHashSet(StringComparer.Ordinal);
        var collisions = Aliases(Root(generated)).Where(manual.Contains).Order(StringComparer.Ordinal).ToArray();
        if (collisions.Length != 0) throw new InvalidOperationException($"LiteLLM alias collision with operator-owned model_list: {string.Join(", ", collisions)}.");
    }

    private static IEnumerable<string> Aliases(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("model_list"), out var list) || list is not YamlSequenceNode sequence) yield break;
        foreach (var entry in sequence.Children.OfType<YamlMappingNode>())
            if (entry.Children.TryGetValue(new YamlScalarNode("model_name"), out var value) && value is YamlScalarNode scalar && scalar.Value is not null) yield return scalar.Value;
    }

    private static Task ValidateYamlAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var stream = Load(Encoding.UTF8.GetString(bytes.Span));
        if (!Root(stream).Children.ContainsKey(new YamlScalarNode("model_list"))) throw new InvalidDataException("Generated LiteLLM YAML must contain model_list.");
        return Task.CompletedTask;
    }

    private static YamlStream Load(string text)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(text);
        stream.Load(reader);
        if (stream.Documents.Count != 1) throw new InvalidDataException("Expected one YAML document.");
        return stream;
    }

    private static YamlMappingNode Root(YamlStream stream) => stream.Documents[0].RootNode as YamlMappingNode ?? throw new InvalidDataException("YAML root must be a mapping.");
}
