using System.Net;
using System.Net.Http.Headers;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Client;

namespace LlmGlue.Tests;

public sealed class ArtifactSyncClientTests
{
    [Fact]
    public async Task SendsBearerAndUsesEtagForIdempotentSecondPoll()
    {
        var artifact = await new LlmGlue.Core.Generation.OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
        var requests = 0;
        var handler = new TestHttpHandler(request =>
        {
            requests++;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            if (request.Headers.IfNoneMatch.Any()) return new HttpResponseMessage(HttpStatusCode.NotModified);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(artifact.Content) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(artifact.MediaType);
            response.Headers.ETag = new EntityTagHeaderValue($"\"{artifact.ContentHash}\"");
            response.Headers.Add("X-LLM-Glue-Revision", "revision-1");
            return response;
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var target = new RecordingTarget();
        var client = new ArtifactSyncClient(http, [target], "test-token");
        var first = Assert.Single(await client.SyncOnceAsync(TestContext.Current.CancellationToken));
        var second = Assert.Single(await client.SyncOnceAsync(TestContext.Current.CancellationToken));
        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(1, target.Applications);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task TargetFailureIsReportedWithoutAdvancingEtag()
    {
        var artifact = await new LlmGlue.Core.Generation.OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
        var requests = 0;
        var handler = new TestHttpHandler(_ =>
        {
            requests++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(artifact.Content) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(artifact.MediaType);
            response.Headers.ETag = new EntityTagHeaderValue($"\"{artifact.ContentHash}\"");
            return response;
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new ArtifactSyncClient(http, [new RecordingTarget(fail: true)]);
        Assert.NotNull(Assert.Single(await client.SyncOnceAsync(TestContext.Current.CancellationToken)).Error);
        Assert.NotNull(Assert.Single(await client.SyncOnceAsync(TestContext.Current.CancellationToken)).Error);
        Assert.Equal(2, requests);
    }

    private sealed class RecordingTarget(bool fail = false) : IConfigurationTarget
    {
        public string TargetType => "opencode";
        public int Applications { get; private set; }
        public Task ApplyAsync(GeneratedArtifact artifact, CancellationToken cancellationToken)
        {
            Applications++;
            return fail ? Task.FromException(new InvalidOperationException("activation failed")) : Task.CompletedTask;
        }
    }
}
