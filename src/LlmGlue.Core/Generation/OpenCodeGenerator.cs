using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Generation;

public sealed class OpenCodeGenerator(
    Uri? inferenceBaseUrl = null,
    string? inferenceApiKey = null,
    bool embedInferenceApiKey = true) : IConfigurationGenerator
{
    public const string InferenceApiKeyEnvironment = "LLM_GLUE_INFERENCE_API_KEY";
    public string TargetType => "opencode";

    public ValueTask<GeneratedArtifact> GenerateAsync(DiscoverySnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (inferenceBaseUrl is not null && embedInferenceApiKey && string.IsNullOrEmpty(inferenceApiKey))
            throw new InvalidOperationException("An inference API key is required when embedded OpenCode credentials are enabled.");
        var providers = new JsonObject();
        var deployments = GenerationHelpers.Publishable(snapshot).ToArray();
        foreach (var group in deployments.GroupBy(x => new { x.Identity.ProviderId, x.Identity.InstanceId, x.Identity.SourceEndpoint }).OrderBy(x => x.Key.ProviderId, StringComparer.Ordinal).ThenBy(x => x.Key.InstanceId, StringComparer.Ordinal))
        {
            var models = new JsonObject();
            foreach (var deployment in group.OrderBy(x => x.Identity.ModelId, StringComparer.Ordinal))
                models[deployment.Identity.ModelId] = CreateModel(deployment);
            var endpoint = (inferenceBaseUrl?.AbsoluteUri ?? group.Key.SourceEndpoint!).TrimEnd('/');
            if (!endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) endpoint += "/v1";
            var settings = new JsonObject { ["baseURL"] = endpoint };
            if (inferenceBaseUrl is not null)
                settings["apiKey"] = embedInferenceApiKey ? inferenceApiKey : $"{{env:{InferenceApiKeyEnvironment}}}";
            var provider = new JsonObject
            {
                ["name"] = $"LLM Glue {group.Key.InstanceId}",
                ["package"] = "@opencode/ai/providers/openai-compatible",
                ["settings"] = settings,
                ["models"] = models
            };
            if (inferenceBaseUrl is not null && !embedInferenceApiKey) provider["env"] = new JsonArray(InferenceApiKeyEnvironment);
            providers[GenerationHelpers.Slug("llm-glue", group.Key.ProviderId, group.Key.InstanceId)] = provider;
        }
        var root = new JsonObject { ["$schema"] = "https://opencode.ai/config.json", ["providers"] = providers };
        if (inferenceBaseUrl is null && deployments.Any(x => x.Identity.ProviderId == "ollama"))
            root["disabled_providers"] = new JsonArray("ollama");
        var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }) + "\n";
        return ValueTask.FromResult(GenerationHelpers.Artifact("application/json", text));
    }

    private JsonObject CreateModel(ModelDeployment deployment)
    {
        var input = new JsonArray();
        if (deployment.Capabilities.TextInput.State == SupportState.Supported) input.Add("text");
        if (deployment.Capabilities.ImageInput.State == SupportState.Supported) input.Add("image");
        var output = new JsonArray();
        if (deployment.Capabilities.TextOutput.State == SupportState.Supported) output.Add("text");
        var capabilities = new JsonObject
        {
            ["temperature"] = true
        };
        if (input.Count != 0) capabilities["input"] = input;
        if (output.Count != 0) capabilities["output"] = output;
        if (deployment.Capabilities.Tools.State != SupportState.Unknown)
            capabilities["tools"] = deployment.Capabilities.Tools.State == SupportState.Supported;
        if (deployment.Capabilities.Reasoning.Kind != ReasoningModeKind.Unknown)
            capabilities["reasoning"] = Reasoning(deployment.Capabilities.Reasoning);
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
