using LlmGlue.Api;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;
using LlmGlue.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlmGlue.Tests;

public sealed class AgentDiscoveryServiceTests
{
    [Fact]
    public async Task PublicationFailureDoesNotTerminateBackgroundService()
    {
        var state = new ApiState([new FailingGenerator()], new ArtifactStore());
        var service = new AgentDiscoveryService([new EmptyAdapter()], state, NullLogger<AgentDiscoveryService>.Instance, TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class EmptyAdapter : IModelDiscoveryAdapter
    {
        public string ProviderType => "test";
        public Task<IReadOnlyList<ModelDeployment>> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDeployment>>([]);
    }

    private sealed class FailingGenerator : IConfigurationGenerator
    {
        public string TargetType => "test";
        public ValueTask<GeneratedArtifact> GenerateAsync(DiscoverySnapshot snapshot, CancellationToken cancellationToken) => throw new IOException("injected persistence/generation failure");
    }
}
