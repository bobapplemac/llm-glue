using System.Net;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Providers;

namespace LlmGlue.Tests;

public sealed class InferenceRouteProbeTests
{
    [Fact]
    public async Task VerifiesOnlyObservedTextRouteEvidence()
    {
        string? path = null;
        var handler = new TestHttpHandler(request =>
        {
            path = request.RequestUri!.AbsolutePath;
            return TestHttpHandler.Json("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var deployment = GenerationTests.Qualified() with
        {
            Capabilities = GenerationTests.Qualified().Capabilities with { Reasoning = new(ReasoningModeKind.Unknown, []) }
        };

        var probed = await new InferenceRouteProbe(http, "llama.cpp", CapabilityProbeMode.LoadedOnly, TimeSpan.FromSeconds(2))
            .ProbeAsync(deployment, TestContext.Current.CancellationToken);

        Assert.Equal("/v1/chat/completions", path);
        Assert.Contains(probed.Capabilities.TextOutput.Evidence, x => x.Verification == VerificationState.Verified);
        Assert.Equal(ReasoningModeKind.Unknown, probed.Capabilities.Reasoning.Kind);
        Assert.Equal("verified", probed.Runtime.Properties["probe.text_route"]);
    }

    [Fact]
    public async Task LoadedOnlyModeNeverCallsDormantRoute()
    {
        var requests = 0;
        var handler = new TestHttpHandler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var deployment = GenerationTests.Qualified() with { Runtime = GenerationTests.Qualified().Runtime with { Loaded = false } };

        var result = await new InferenceRouteProbe(http, "llama.cpp", CapabilityProbeMode.LoadedOnly, TimeSpan.FromSeconds(2))
            .ProbeAsync(deployment, TestContext.Current.CancellationToken);

        Assert.Same(deployment, result);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task FailedProbeFailsClosedWithoutPublishingResponseBody()
    {
        var handler = new TestHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("secret provider diagnostic")
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var result = await new InferenceRouteProbe(http, "llama.cpp", CapabilityProbeMode.AllowLoad, TimeSpan.FromSeconds(2))
            .ProbeAsync(GenerationTests.Qualified(), TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStatus.Unavailable, result.Status);
        Assert.DoesNotContain(result.Warnings, x => x.Contains("secret provider diagnostic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsesNativeOllamaChatContract()
    {
        string? path = null;
        var handler = new TestHttpHandler(request => { path = request.RequestUri!.AbsolutePath; return TestHttpHandler.Json("{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}"); });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var result = await new InferenceRouteProbe(http, "ollama", CapabilityProbeMode.AllowLoad, TimeSpan.FromSeconds(2))
            .ProbeAsync(GenerationTests.Qualified(), TestContext.Current.CancellationToken);

        Assert.Equal("/api/chat", path);
        Assert.Equal(DeploymentStatus.Available, result.Status);
    }

    [Fact]
    public async Task CancelsProbeAtConfiguredTimeout()
    {
        var handler = new TestHttpHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/"), Timeout = Timeout.InfiniteTimeSpan };

        var result = await new InferenceRouteProbe(http, "llama.cpp", CapabilityProbeMode.AllowLoad, TimeSpan.FromMilliseconds(25))
            .ProbeAsync(GenerationTests.Qualified(), TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStatus.Unavailable, result.Status);
        Assert.Contains(result.Warnings, x => x.Contains("timed out", StringComparison.Ordinal));
    }
}
