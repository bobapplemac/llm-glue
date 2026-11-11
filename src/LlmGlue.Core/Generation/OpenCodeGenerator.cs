using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Generation;

public sealed class OpenCodeGenerator(Uri? inferenceBaseUrl = null) : IConfigurationGenerator
{
    public const string InferenceApiKeyEnvironment = "LLM_GLUE_INFERENCE_API_KEY";
    public string TargetType => "opencode";

    public ValueTask<GeneratedArtifact> GenerateAsync(DiscoverySnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var providers = new JsonObject();
        foreach (var group in GenerationHelpers.Publishable(snapshot).GroupBy(x => new { x.Identity.ProviderId, x.Identity.InstanceId, x.Identity.SourceEndpoint }).OrderBy(x => x.Key.ProviderId, StringComparer.Ordinal).ThenBy(x => x.Key.InstanceId, StringComparer.Ordinal))
        {
            var models = new JsonObject();
            foreach (var deployment in group.OrderBy(x => x.Identity.ModelId, StringComparer.Ordinal))
                models[deployment.Identity.ModelId] = CreateModel(deployment);
            var endpoint = (inferenceBaseUrl?.AbsoluteUri ?? group.Key.SourceEndpoint!).TrimEnd('/');
            if (!endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) endpoint += "/v1";
            var settings = new JsonObject { ["baseURL"] = endpoint };
            if (inferenceBaseUrl is not null) settings["apiKey"] = $"{{env:{InferenceApiKeyEnvironment}}}";
            providers[GenerationHelpers.Slug("llm-glue", group.Key.ProviderId, group.Key.InstanceId)] = new JsonObject
            {
                ["name"] = $"LLM Glue {group.Key.InstanceId}",
                ["package"] = "@opencode/ai/providers/openai-compatible",
                ["settings"] = settings,
                ["models"] = models
            };
        }
        var root = new JsonObject { ["$schema"] = "https://opencode.ai/config.json", ["providers"] = providers };
        var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }) + "\n";
        return ValueTask.FromResult(GenerationHelpers.Artifact("application/json", text));
    }

    private JsonObject CreateModel(ModelDeployment deployment)
    {
        var input = new JsonArray("text");
        if (deployment.Capabilities.ImageInput.State == SupportState.Supported) input.Add("image");
        var capabilities = new JsonObject
        {
            ["tools"] = deployment.Capabilities.Tools.State == SupportState.Supported,
            ["input"] = input,
            ["output"] = new JsonArray("text"),
            ["temperature"] = true,
            ["reasoning"] = Reasoning(deployment.Capabilities.Reasoning)
        };
        var model = new JsonObject
        {
            ["modelID"] = inferenceBaseUrl is null ? deployment.Identity.Route : LiteLlmGenerator.Alias(deployment),
            ["name"] = deployment.Identity.DisplayName,
            ["capabilities"] = capabilities
        };
        if (deployment.Identity.Family is not null) model["family"] = deployment.Identity.Family;
        var context = deployment.EffectiveContext?.Value ?? deployment.NativeContext?.Value;
        if (context is > 0) model["limit"] = new JsonObject { ["context"] = context.Value };
        var variants = new JsonArray();
        foreach (var profile in deployment.SamplingProfiles.Where(x => x.TransportVerified).OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var body = new JsonObject();
            foreach (var parameter in profile.Parameters.OrderBy(x => x.Name, StringComparer.Ordinal)) body[parameter.Name] = JsonValue.Create(parameter.Value);
            variants.Add(new JsonObject { ["id"] = profile.Name, ["body"] = body });
        }
        if (variants.Count != 0) model["variants"] = variants;
        return model;
    }

    private static JsonObject Reasoning(ReasoningCapabilities reasoning) => reasoning.Kind switch
    {
        ReasoningModeKind.None => new JsonObject { ["supported"] = false },
        ReasoningModeKind.Boolean => new JsonObject { ["supported"] = true, ["toggle"] = true },
        ReasoningModeKind.NamedEffort => new JsonObject { ["supported"] = true, ["effort"] = new JsonArray(reasoning.Modes.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) },
        ReasoningModeKind.TokenBudget => new JsonObject { ["supported"] = true },
        _ => new JsonObject { ["supported"] = false }
    };
}
