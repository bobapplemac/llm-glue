using System.Net;
using LlmGlue.Core.Client;

namespace LlmGlue.Tests;

public sealed class ServiceRestartActivatorTests
{
    [Fact]
    public async Task HealthWindowCanSurviveMoreThanTenColdStartFailures()
    {
        var handler = new SequenceHandler(Enumerable.Repeat(HttpStatusCode.ServiceUnavailable, 11).Append(HttpStatusCode.OK));
        using var client = new HttpClient(handler);

        await ServiceRestartActivator.WaitForHealthAsync(client, new Uri("http://localhost/health"), 60, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal(12, handler.RequestCount);
    }

    [Fact]
    public async Task HealthWindowRemainsBounded()
    {
        var handler = new SequenceHandler(Enumerable.Repeat(HttpStatusCode.ServiceUnavailable, 3));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceRestartActivator.WaitForHealthAsync(client, new Uri("http://localhost/health"), 3, TimeSpan.Zero, TestContext.Current.CancellationToken));

        Assert.Equal(3, handler.RequestCount);
    }

    private sealed class SequenceHandler(IEnumerable<HttpStatusCode> statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(_statuses.Dequeue()));
        }
    }
}
