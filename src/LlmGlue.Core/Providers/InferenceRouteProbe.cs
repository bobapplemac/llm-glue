using System.Net.Http.Json;
using System.Text.Json;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Providers;

public sealed class InferenceRouteProbe(
    HttpClient httpClient,
    string providerType,
    CapabilityProbeMode mode,
    TimeSpan timeout,
    TimeProvider? timeProvider = null) : ICapabilityProbe
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<ModelDeployment> ProbeAsync(ModelDeployment deployment, CancellationToken cancellationToken)
    {
        if (mode == CapabilityProbeMode.Off || mode == CapabilityProbeMode.LoadedOnly && deployment.Runtime.Loaded != true) return deployment;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var request = CreateRequest(deployment.Identity.Route);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes) throw new InvalidDataException("Probe response exceeded the size limit.");
            await using var input = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
            using var buffer = new MemoryStream();
            await CopyBoundedAsync(input, buffer, timeoutSource.Token);
            using var document = JsonDocument.Parse(buffer.ToArray());
            if (!HasExpectedResponse(document.RootElement)) throw new InvalidDataException("Probe response did not match the expected provider schema.");

            var observedAt = _time.GetUtcNow();
            return deployment with
            {
                Capabilities = deployment.Capabilities with
                {
                    TextInput = Verified(deployment.Capabilities.TextInput, $"{providerType}:bounded-route-probe", observedAt),
                    TextOutput = Verified(deployment.Capabilities.TextOutput, $"{providerType}:bounded-route-probe", observedAt)
                },
                Runtime = deployment.Runtime with
                {
                    Properties = new SortedDictionary<string, string>(deployment.Runtime.Properties.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), StringComparer.Ordinal)
                    {
                        ["probe.text_route"] = "verified"
                    }
                }
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(deployment, "Bounded text-route probe timed out; no additional capabilities were inferred.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException)
        {
            return Failed(deployment, $"Bounded text-route probe failed ({ex.GetType().Name}); response details were not published and no additional capabilities were inferred.");
        }
    }

    private HttpRequestMessage CreateRequest(string route)
    {
        object payload;
        string path;
        if (providerType == "ollama")
        {
            path = "api/chat";
            payload = new { model = route, messages = new[] { new { role = "user", content = "Reply with OK." } }, stream = false, options = new { num_predict = 1 } };
        }
        else
        {
            path = "v1/chat/completions";
            payload = new { model = route, messages = new[] { new { role = "user", content = "Reply with OK." } }, stream = false, max_tokens = 1 };
        }
        return new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
    }

    private bool HasExpectedResponse(JsonElement root)
    {
        if (providerType == "ollama")
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() != 0;
    }

    private static Capability Verified(Capability capability, string source, DateTimeOffset observedAt) => capability with
    {
        State = SupportState.Supported,
        Evidence = capability.Evidence.Concat([new Evidence<string>("supported", source, observedAt, VerificationState.Verified)]).ToArray()
    };

    private static ModelDeployment Failed(ModelDeployment deployment, string warning) => deployment with
    {
        Status = DeploymentStatus.Unavailable,
        Warnings = deployment.Warnings.Concat([warning]).Distinct(StringComparer.Ordinal).ToArray()
    };

    private static async Task CopyBoundedAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) return;
            total += read;
            if (total > MaximumResponseBytes) throw new InvalidDataException("Probe response exceeded the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}

public sealed class ProbingDiscoveryAdapter(IModelDiscoveryAdapter inner, ICapabilityProbe probe) : IModelDiscoveryAdapter
{
    public string ProviderType => inner.ProviderType;

    public async Task<IReadOnlyList<ModelDeployment>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var discovered = await inner.DiscoverAsync(cancellationToken);
        var result = new List<ModelDeployment>(discovered.Count);
        foreach (var deployment in discovered) result.Add(await probe.ProbeAsync(deployment, cancellationToken));
        return result;
    }
}
