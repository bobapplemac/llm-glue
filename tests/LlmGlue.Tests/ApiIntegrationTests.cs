using System.Net;
using System.Net.Http.Json;
using LlmGlue.Api;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Generation;
using LlmGlue.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LlmGlue.Tests;

public sealed class ApiIntegrationTests
{
    [Fact]
    public async Task PublishesArtifactWithEtagAndHonorsConditionalGet()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IConfigurationGenerator, OpenCodeGenerator>();
        builder.Services.AddSingleton<IConfigurationGenerator, LiteLlmGenerator>();
        builder.Services.AddSingleton(new ArtifactStore());
        builder.Services.AddSingleton<ApiState>();
        var app = builder.Build();
        app.MapGlueApi();
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await app.Services.GetRequiredService<ApiState>().PublishAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var health = await client.GetAsync("api/v1/health", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            var artifact = await client.GetAsync("api/v1/artifacts/opencode", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, artifact.StatusCode);
            Assert.Equal("application/json", artifact.Content.Headers.ContentType!.MediaType);
            Assert.NotNull(artifact.Headers.ETag);
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/artifacts/opencode");
            request.Headers.IfNoneMatch.Add(artifact.Headers.ETag);
            var unchanged = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        }
        finally { await app.StopAsync(TestContext.Current.CancellationToken); }
    }

    [Theory]
    [InlineData("Bearer correct", "correct", true)]
    [InlineData("bearer correct", "correct", true)]
    [InlineData("Bearer wrong", "correct", false)]
    [InlineData(null, "correct", false)]
    public void BearerComparisonIsStrictAndNonLeaking(string? header, string expected, bool valid)
    {
        Assert.Equal(valid, ApiAuthenticationMiddleware.TryBearer(header, expected));
    }

    [Fact]
    public async Task CorrelationMiddlewarePreservesValidIdsAndReplacesUnsafeValues()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.MapGet("/trace", (HttpContext context) => context.TraceIdentifier);
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var validRequest = new HttpRequestMessage(HttpMethod.Get, "trace");
            validRequest.Headers.Add(CorrelationIds.HeaderName, "client-123");
            using var valid = await client.SendAsync(validRequest, TestContext.Current.CancellationToken);
            Assert.Equal("client-123", valid.Headers.GetValues(CorrelationIds.HeaderName).Single());
            Assert.Equal("client-123", await valid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            using var unsafeRequest = new HttpRequestMessage(HttpMethod.Get, "trace");
            unsafeRequest.Headers.TryAddWithoutValidation(CorrelationIds.HeaderName, "secret value with spaces");
            using var replaced = await client.SendAsync(unsafeRequest, TestContext.Current.CancellationToken);
            var replacement = replaced.Headers.GetValues(CorrelationIds.HeaderName).Single();
            Assert.True(CorrelationIds.IsValid(replacement));
            Assert.NotEqual("secret value with spaces", replacement);
        }
        finally { await app.StopAsync(TestContext.Current.CancellationToken); }
    }

    [Fact]
    public void ArtifactHistoryIsBoundedAndNoOpsSameContent()
    {
        var store = new ArtifactStore(2);
        var one = GenerationHelpersForTest("one");
        var two = GenerationHelpersForTest("two");
        var three = GenerationHelpersForTest("three");
        store.Put("test", "1", DateTimeOffset.UnixEpoch, one);
        store.Put("test", "same", DateTimeOffset.UnixEpoch, one);
        store.Put("test", "2", DateTimeOffset.UnixEpoch, two);
        store.Put("test", "3", DateTimeOffset.UnixEpoch, three);
        Assert.Equal(2, store.History("test").Count);
        Assert.Equal("3", store.Latest("test")!.Revision);
    }

    [Fact]
    public async Task AgentSubmissionAcceptsCanonicalInventoryAndRejectsStaleOrTamperedState()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IConfigurationGenerator, OpenCodeGenerator>();
        builder.Services.AddSingleton(new ArtifactStore());
        builder.Services.AddSingleton<ApiState>();
        var app = builder.Build();
        app.MapGlueApi();
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var current = LlmGlue.Core.Domain.DiscoverySnapshot.Create(DateTimeOffset.UtcNow, [GenerationTests.Qualified()]);
            var accepted = await client.PostAsJsonAsync("api/v1/agent/inventory", current, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

            var stale = LlmGlue.Core.Domain.DiscoverySnapshot.Create(current.CapturedAt.AddMinutes(-1), []);
            var conflict = await client.PostAsJsonAsync("api/v1/agent/inventory", stale, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            var tampered = current with { ContentHash = "not-canonical" };
            var bad = await client.PostAsJsonAsync("api/v1/agent/inventory", tampered, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        finally { await app.StopAsync(TestContext.Current.CancellationToken); }
    }

    private static GeneratedArtifact GenerationHelpersForTest(string value)
    {
        var content = System.Text.Encoding.UTF8.GetBytes($"{{\"providers\":{{\"{value}\":{{}}}}}}");
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));
        return new("application/json", content, hash, 1);
    }
}
