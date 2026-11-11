using System.Net;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Providers;

namespace LlmGlue.Tests;

public sealed class OllamaDiscoveryAdapterTests
{
    [Fact]
    public async Task DiscoversNativeAndEffectiveContextAndExplicitThinking()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/version" => TestHttpHandler.Json("{\"version\":\"0.40.16\"}"),
            "/api/tags" => TestHttpHandler.Json("{\"models\":[{\"name\":\"reasoner\",\"details\":{\"family\":\"test\",\"quantization_level\":\"Q4\"}}]}"),
            "/api/ps" => TestHttpHandler.Json("{\"models\":[{\"name\":\"reasoner\",\"context_length\":32768}]}"),
            "/api/show" => TestHttpHandler.Json("{\"model_info\":{\"test.context_length\":131072},\"capabilities\":[\"completion\",\"tools\"],\"thinking\":{\"values\":[false,true],\"default\":true},\"details\":{\"family\":\"test\",\"quantization_level\":\"Q4\"}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var adapter = new OllamaDiscoveryAdapter(client, "local");
        var model = Assert.Single(await adapter.DiscoverAsync(TestContext.Current.CancellationToken));
        Assert.Equal(131072, model.NativeContext!.Value);
        Assert.Equal(32768, model.EffectiveContext!.Value);
        Assert.Equal(ReasoningModeKind.Boolean, model.Capabilities.Reasoning.Kind);
        Assert.Equal(["off", "on"], model.Capabilities.Reasoning.Modes);
        Assert.Collection(model.SamplingProfiles,
            profile =>
            {
                Assert.Equal("thinking-off", profile.Name);
                Assert.Equal("none", Assert.Single(profile.Parameters).Value);
                Assert.True(profile.TransportVerified);
            },
            profile =>
            {
                Assert.Equal("thinking-on", profile.Name);
                Assert.Equal("medium", Assert.Single(profile.Parameters).Value);
                Assert.True(profile.TransportVerified);
            });
        Assert.Equal(SupportState.Unsupported, model.Capabilities.ImageInput.State);
    }

    [Fact]
    public async Task OmittedThinkingRemainsUnknown()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/version" => TestHttpHandler.Json("{\"version\":\"0.40.16\"}"),
            "/api/tags" => TestHttpHandler.Json("{\"models\":[{\"name\":\"plain\"}]}"),
            "/api/ps" => TestHttpHandler.Json("{\"models\":[]}"),
            "/api/show" => TestHttpHandler.Json("{\"model_info\":{},\"capabilities\":[\"completion\"]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var model = Assert.Single(await new OllamaDiscoveryAdapter(client, "local").DiscoverAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ReasoningModeKind.Unknown, model.Capabilities.Reasoning.Kind);
        Assert.Null(model.NativeContext);
        Assert.Null(model.EffectiveContext);
    }

    [Fact]
    public async Task DoesNotPublishReasoningProfilesForUnqualifiedOllamaVersion()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/version" => TestHttpHandler.Json("{\"version\":\"0.12.0\"}"),
            "/api/tags" => TestHttpHandler.Json("{\"models\":[{\"name\":\"reasoner\"}]}"),
            "/api/ps" => TestHttpHandler.Json("{\"models\":[]}"),
            "/api/show" => TestHttpHandler.Json("{\"model_info\":{},\"capabilities\":[\"completion\",\"thinking\"],\"thinking\":{\"values\":[false,true]}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var model = Assert.Single(await new OllamaDiscoveryAdapter(client, "local").DiscoverAsync(TestContext.Current.CancellationToken));
        Assert.Empty(model.SamplingProfiles);
    }
}
