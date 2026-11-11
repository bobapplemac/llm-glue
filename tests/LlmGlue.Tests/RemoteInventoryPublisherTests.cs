using System.Net;
using LlmGlue.Services;

namespace LlmGlue.Tests;

public sealed class RemoteInventoryPublisherTests
{
    [Fact]
    public async Task PostsCanonicalSnapshotWithWriteBearer()
    {
        string? path = null;
        string? authorization = null;
        string? body = null;
        var handler = new TestHttpHandler(request =>
        {
            path = request.RequestUri!.AbsolutePath;
            authorization = request.Headers.Authorization?.ToString();
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://glue.example.test/") };

        await new RemoteInventoryPublisher(http, "write-secret").PublishAsync(
            GenerationTests.Snapshot(), TestContext.Current.CancellationToken);

        Assert.Equal("/api/v1/agent/inventory", path);
        Assert.Equal("Bearer write-secret", authorization);
        Assert.Contains(GenerationTests.Snapshot().ContentHash, body, StringComparison.Ordinal);
    }
}
