using System.Net;
using LlmGlue.Core.Providers;

namespace LlmGlue.Tests;

public sealed class LlamaCppDiscoveryAdapterTests
{
    [Fact]
    public async Task DirectServerSeparatesNativeAndEffectiveContext()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/models" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/v1/models" => TestHttpHandler.Json("{\"data\":[{\"id\":\"qwen.gguf\",\"meta\":{\"n_ctx_train\":262144}}]}"),
            "/props" => TestHttpHandler.Json("{\"default_generation_settings\":{\"n_ctx\":65536}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "local").DiscoverAsync(TestContext.Current.CancellationToken));
        Assert.Equal(262144, model.NativeContext!.Value);
        Assert.Equal(65536, model.EffectiveContext!.Value);
        Assert.Equal("direct", model.Runtime.Properties["mode"]);
    }

    [Fact]
    public async Task RouterDoesNotTreatUnloadedModelAsLoaded()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/models" => TestHttpHandler.Json("[{\"id\":\"gemma.gguf\",\"status\":\"unloaded\",\"meta\":{\"n_ctx_train\":131072}}]"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "router").DiscoverAsync(TestContext.Current.CancellationToken));
        Assert.False(model.Runtime.Loaded);
        Assert.Equal("router", model.Runtime.Properties["mode"]);
    }
}
