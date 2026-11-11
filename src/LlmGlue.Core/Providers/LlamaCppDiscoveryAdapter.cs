using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Providers;

public sealed class LlamaCppDiscoveryAdapter(HttpClient httpClient, string instanceId, TimeProvider? timeProvider = null) : IModelDiscoveryAdapter
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public string ProviderType => "llama.cpp";

    public async Task<IReadOnlyList<ModelDeployment>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        using var routerResponse = await httpClient.GetAsync("models", cancellationToken);
        if (routerResponse.IsSuccessStatusCode)
        {
            using var json = await JsonDocument.ParseAsync(await routerResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return ParseRouter(json.RootElement, now);
        }
        if (routerResponse.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)) routerResponse.EnsureSuccessStatusCode();

        var models = await httpClient.GetFromJsonAsync<JsonDocument>("v1/models", cancellationToken) ?? throw new HttpRequestException("llama.cpp returned no models.");
        using (models)
        {
            var props = await TryGetProps(cancellationToken);
            return ParseDirect(models.RootElement, props, now);
        }
    }

    private async Task<JsonElement?> TryGetProps(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("props", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return doc.RootElement.Clone();
    }

    private IReadOnlyList<ModelDeployment> ParseDirect(JsonElement root, JsonElement? props, DateTimeOffset now)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        return data.EnumerateArray().Select(item =>
        {
            var id = item.GetProperty("id").GetString() ?? throw new JsonException("llama.cpp model id is missing.");
            var native = TryPathLong(item, "meta", "n_ctx_train");
            var effective = props is { } p ? TryPathLong(p, "default_generation_settings", "n_ctx") : null;
            return Create(id, id, native, effective, true, now, "direct");
        }).OrderBy(x => x.Identity.ModelId, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<ModelDeployment> ParseRouter(JsonElement root, DateTimeOffset now)
    {
        var array = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("models", out var models) ? models : default;
        if (array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(item =>
        {
            var id = GetString(item, "id") ?? GetString(item, "model") ?? GetString(item, "name") ?? throw new JsonException("llama.cpp router model id is missing.");
            var status = GetString(item, "status");
            var loaded = string.Equals(status, "loaded", StringComparison.OrdinalIgnoreCase);
            var native = TryPathLong(item, "meta", "n_ctx_train");
            var effective = TryPathLong(item, "status", "args", "ctx_size") ?? TryPathLong(item, "status", "args", "n_ctx");
            return Create(id, id, native, effective, loaded, now, "router");
        }).OrderBy(x => x.Identity.ModelId, StringComparer.Ordinal).ToArray();
    }

    private ModelDeployment Create(string id, string display, long? native, long? effective, bool loaded, DateTimeOffset now, string mode) => new(
        new ModelIdentity("llama.cpp", instanceId, id, display, id, SourceEndpoint: httpClient.BaseAddress?.ToString()), DeploymentStatus.Available, now, now,
        native is > 0 ? new Evidence<long>(native.Value, $"llama.cpp:{mode}:native", now, VerificationState.Reported) : null,
        effective is > 0 ? new Evidence<long>(effective.Value, $"llama.cpp:{mode}:effective", now, VerificationState.Observed) : null,
        ProviderHelpers.Capabilities(null, null, new(ReasoningModeKind.Unknown, []), $"llama.cpp:{mode}", now), [],
        new RuntimeDetails("llama.cpp", null, loaded, new SortedDictionary<string, string>(StringComparer.Ordinal) { ["mode"] = mode }),
        ["Modalities, tools, and reasoning require metadata or bounded probe evidence."]);

    private static string? GetString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? TryPathLong(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path) if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) return null;
        return current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out var value) ? value : null;
    }
}
