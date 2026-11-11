using System.Text;
using System.Text.Json;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;

namespace LlmGlue.Tests;

public sealed class GenerationTests
{
    [Fact]
    public async Task OpenCodeMatchesGoldenAndContainsVerifiedVariantsOnly()
    {
        var artifact = await new OpenCodeGenerator().GenerateAsync(Snapshot(), TestContext.Current.CancellationToken);
        ArtifactValidator.Validate(artifact);
        using var actual = JsonDocument.Parse(artifact.Content);
        using var expected = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "golden", "opencode.json")));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.DoesNotContain("api_key", Encoding.UTF8.GetString(artifact.Content), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiteLlmMatchesGoldenAndUsesEnvironmentSecretReference()
    {
        var artifact = await new LiteLlmGenerator().GenerateAsync(Snapshot(), TestContext.Current.CancellationToken);
        ArtifactValidator.Validate(artifact);
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "golden", "litellm.yaml")).ReplaceLineEndings("\n");
        Assert.Equal(expected, Encoding.UTF8.GetString(artifact.Content));
        Assert.DoesNotContain("sk-", expected, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CentralOpenCodeArtifactRoutesStableAliasThroughLiteLlm()
    {
        var artifact = await new OpenCodeGenerator(new Uri("https://litellm.example.test"))
            .GenerateAsync(Snapshot(), TestContext.Current.CancellationToken);
        ArtifactValidator.Validate(artifact);
        using var json = JsonDocument.Parse(artifact.Content);
        var provider = json.RootElement.GetProperty("providers").GetProperty("llm-glue-llama-cpp-lab");
        Assert.Equal("https://litellm.example.test/v1", provider.GetProperty("settings").GetProperty("baseURL").GetString());
        Assert.Equal("{env:LLM_GLUE_INFERENCE_API_KEY}", provider.GetProperty("settings").GetProperty("apiKey").GetString());
        Assert.Equal("llama-cpp-lab-qwen-model", provider.GetProperty("models").GetProperty("qwen-model").GetProperty("modelID").GetString());
        Assert.DoesNotContain("integration-secret", Encoding.UTF8.GetString(artifact.Content), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotPublishUnavailableOrInvalidDeployments()
    {
        var deployment = Qualified() with { Status = DeploymentStatus.Unavailable };
        var artifact = await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [deployment]), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(artifact.Content);
        Assert.Empty(json.RootElement.GetProperty("providers").EnumerateObject());
    }

    [Fact]
    public async Task DoesNotPublishDeploymentWithUnknownReasoningCapability()
    {
        var deployment = Qualified() with
        {
            Capabilities = Qualified().Capabilities with
            {
                Reasoning = new(ReasoningModeKind.Unknown, [])
            }
        };
        var artifact = await new OpenCodeGenerator().GenerateAsync(
            DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [deployment]),
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(artifact.Content);
        Assert.Empty(json.RootElement.GetProperty("providers").EnumerateObject());
    }

    public static DiscoverySnapshot Snapshot() => DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [Qualified()]);

    public static ModelDeployment Qualified()
    {
        var now = DateTimeOffset.UnixEpoch;
        static Capability Cap(bool supported, DateTimeOffset time) => new(supported ? SupportState.Supported : SupportState.Unsupported,
            [new Evidence<string>(supported ? "supported" : "unsupported", "test", time, VerificationState.Verified)]);
        return new(
            new("llama.cpp", "lab", "qwen-model", "Qwen Test", "qwen-model", Family: "qwen", SourceEndpoint: "http://127.0.0.1:8081"),
            DeploymentStatus.Available, now, now,
            new Evidence<long>(262144, "gguf", now, VerificationState.Reported),
            new Evidence<long>(65536, "props", now, VerificationState.Observed),
            new(Cap(true, now), Cap(false, now), Cap(true, now), Cap(true, now),
                new(ReasoningModeKind.Boolean, ["off", "on"], new Evidence<string>("off,on", "probe", now, VerificationState.Verified))),
            [
                new("thinking-on", [new("temperature", 0.6, "reference"), new("enable_thinking", true, "probe")], "probe", true),
                new("unverified", [new("top_k", 20, "guess")], "guess", false)
            ],
            new("llama.cpp", "v0.9.0", true, new Dictionary<string, string>()), []);
    }
}
