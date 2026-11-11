using System.Net.Http.Json;
using System.Text.Json;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Providers;

public sealed class OllamaDiscoveryAdapter(HttpClient httpClient, string instanceId, TimeProvider? timeProvider = null) : IModelDiscoveryAdapter
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public string ProviderType => "ollama";

    public async Task<IReadOnlyList<ModelDeployment>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var version = await httpClient.GetFromJsonAsync<VersionResponse>("api/version", cancellationToken) ?? throw new HttpRequestException("Ollama returned an empty version response.");
        var tags = await httpClient.GetFromJsonAsync<TagsResponse>("api/tags", cancellationToken) ?? new TagsResponse([]);
        var ps = await httpClient.GetFromJsonAsync<PsResponse>("api/ps", cancellationToken) ?? new PsResponse([]);
        var running = ps.Models.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var result = new List<ModelDeployment>();
        foreach (var tag in tags.Models.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            using var response = await httpClient.PostAsJsonAsync("api/show", new { model = tag.Name, verbose = false }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var show = await response.Content.ReadFromJsonAsync<ShowResponse>(cancellationToken: cancellationToken) ?? throw new HttpRequestException($"Ollama show response for '{tag.Name}' was empty.");
            var native = show.ModelInfo.Where(x => x.Key.EndsWith(".context_length", StringComparison.Ordinal) && x.Value.ValueKind == JsonValueKind.Number && x.Value.TryGetInt64(out _)).Select(x => x.Value.GetInt64()).DefaultIfEmpty().Max();
            var capabilities = show.Capabilities.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reasoning = ParseThinking(show.Thinking, now);
            long? effective = running.TryGetValue(tag.Name, out var active) ? active.ContextLength : null;
            result.Add(new ModelDeployment(
                new ModelIdentity("ollama", instanceId, tag.Name, tag.Name, tag.Name, Family: show.Details?.Family ?? tag.Details?.Family, Quantization: show.Details?.QuantizationLevel ?? tag.Details?.QuantizationLevel, SourceEndpoint: httpClient.BaseAddress?.ToString()),
                DeploymentStatus.Available, now, now,
                native > 0 ? new Evidence<long>(native, "ollama:/api/show:model_info", now, VerificationState.Reported) : null,
                effective is > 0 ? new Evidence<long>(effective.Value, "ollama:/api/ps", now, VerificationState.Observed) : null,
                ProviderHelpers.Capabilities(capabilities.Contains("vision"), capabilities.Contains("tools"), reasoning, "ollama:/api/show:capabilities", now),
                [], new RuntimeDetails("ollama", version.Version, effective is not null, new SortedDictionary<string, string>(StringComparer.Ordinal)), []));
        }
        return result;
    }

    private static ReasoningCapabilities ParseThinking(JsonElement? thinking, DateTimeOffset now)
    {
        if (thinking is null || thinking.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return new(ReasoningModeKind.Unknown, []);
        if (!thinking.Value.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) return new(ReasoningModeKind.Unknown, []);
        var modes = values.EnumerateArray().Select(x => x.ValueKind switch { JsonValueKind.True => "on", JsonValueKind.False => "off", JsonValueKind.String => x.GetString()!, _ => string.Empty }).Where(x => x.Length != 0).Distinct(StringComparer.Ordinal).ToArray();
        var kind = modes.All(x => x is "on" or "off") ? (modes.SequenceEqual(["off"]) ? ReasoningModeKind.None : ReasoningModeKind.Boolean) : ReasoningModeKind.NamedEffort;
        return new(kind, modes, new Evidence<string>(string.Join(',', modes), "ollama:/api/show:thinking", now, VerificationState.Reported));
    }

    private sealed record VersionResponse(string Version);
    private sealed record TagsResponse(ModelTag[] Models);
    private sealed record PsResponse(RunningModel[] Models);
    private sealed record RunningModel(string Name, [property: System.Text.Json.Serialization.JsonPropertyName("context_length")] long ContextLength);
    private sealed record ModelTag(string Name, ModelDetails? Details);
    private sealed record ModelDetails(string? Family, [property: System.Text.Json.Serialization.JsonPropertyName("quantization_level")] string? QuantizationLevel);
    private sealed record ShowResponse([property: System.Text.Json.Serialization.JsonPropertyName("model_info")] Dictionary<string, JsonElement> ModelInfo, string[] Capabilities, JsonElement? Thinking, ModelDetails? Details);
}
