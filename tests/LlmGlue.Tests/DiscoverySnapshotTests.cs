using LlmGlue.Core.Domain;

namespace LlmGlue.Tests;

public sealed class DiscoverySnapshotTests
{
    [Fact]
    public void HashIsIndependentOfCaptureTimeAndInputOrder()
    {
        var first = Deployment("b");
        var second = Deployment("a");
        var one = DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [first, second]);
        var two = DiscoverySnapshot.Create(DateTimeOffset.UtcNow, [second, first]);
        Assert.Equal(one.ContentHash, two.ContentHash);
        Assert.Equal("a", two.Deployments[0].Identity.ModelId);
    }

    private static ModelDeployment Deployment(string id)
    {
        var capability = new Capability(SupportState.Unknown, []);
        return new(new("test", "instance", id, id, id), DeploymentStatus.Available, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            null, null, new(capability, capability, capability, capability, new(ReasoningModeKind.Unknown, [])), [],
            new("test", "1", false, new Dictionary<string, string>()), []);
    }
}
