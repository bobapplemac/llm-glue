using System.Net;
using LlmGlue.Core.Configuration;
using LlmGlue.Operations;

namespace LlmGlue.Tests;

public sealed class DoctorTests
{
    [Fact]
    public async Task ReturnsSuccessWhenProvidersAreHealthy()
    {
        var result = await CliCommands.DoctorAsync(
            Options([Provider("ollama")]),
            TestContext.Current.CancellationToken,
            provider => Client(provider, _ => TestHttpHandler.Json("{}")));

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ReturnsFailureForProviderHttpError()
    {
        var result = await CliCommands.DoctorAsync(
            Options([Provider("llama.cpp")]),
            TestContext.Current.CancellationToken,
            provider => Client(provider, _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ReturnsFailureForUnavailableProvider()
    {
        var result = await CliCommands.DoctorAsync(
            Options([Provider("ollama")]),
            TestContext.Current.CancellationToken,
            provider => Client(provider, _ => throw new HttpRequestException("offline")));

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ReturnsFailureForProviderTimeout()
    {
        var result = await CliCommands.DoctorAsync(
            Options([Provider("ollama")]),
            TestContext.Current.CancellationToken,
            provider => Client(provider, _ => throw new TaskCanceledException("timed out")));

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CliCommands.DoctorAsync(
            Options([Provider("ollama")]),
            cancellation.Token,
            provider => Client(provider, _ => throw new OperationCanceledException(cancellation.Token))));
    }

    [Fact]
    public async Task ReturnsFailureWhenConfiguredSecretEnvironmentIsMissing()
    {
        var environment = $"LLM_GLUE_TEST_MISSING_{Guid.NewGuid():N}";
        var options = Options([], clientTokenEnvironment: environment);

        var result = await CliCommands.DoctorAsync(
            options,
            TestContext.Current.CancellationToken,
            _ => throw new InvalidOperationException("No providers expected."));

        Assert.Equal(1, result);
    }

    private static HttpClient Client(ProviderOptions provider, Func<HttpRequestMessage, HttpResponseMessage> handler)
        => new(new TestHttpHandler(handler)) { BaseAddress = provider.Url };

    private static ProviderOptions Provider(string type) => new(
        type,
        "test-provider",
        new Uri("http://provider.test/"),
        [],
        true,
        false,
        CapabilityProbeMode.Off,
        5);

    private static GlueOptions Options(IReadOnlyList<ProviderOptions> providers, string? clientTokenEnvironment = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"llm-glue-doctor-{Guid.NewGuid():N}");
        return new GlueOptions(
            new RoleOptions(providers.Count > 0, true, clientTokenEnvironment is not null),
            new AgentOptions(providers, [], null, null, null, 30, 120),
            new ServerOptions(new Uri("http://127.0.0.1:8765"), null, null, null, null, null, false, [], "state", 5),
            new ClientOptions(null, clientTokenEnvironment, null, 30, []),
            new LoggingOptions("Information", "logs/llm-glue.log", 10, 7),
            Path.Combine(root, "llm-glue.conf"),
            root);
    }
}
