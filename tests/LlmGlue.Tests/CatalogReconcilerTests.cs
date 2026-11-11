using LlmGlue.Core.Domain;
using LlmGlue.Core.Reconciliation;

namespace LlmGlue.Tests;

public sealed class CatalogReconcilerTests
{
    [Fact]
    public void RetainsMissingModelDuringGraceThenRemovesIt()
    {
        var old = GenerationTests.Qualified() with { LastSeenAt = DateTimeOffset.UnixEpoch };
        var previous = DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [old]);
        var duringGrace = CatalogReconciler.Reconcile(previous, [], DateTimeOffset.UnixEpoch.AddMinutes(5), TimeSpan.FromMinutes(10));
        Assert.Equal(DeploymentStatus.Unavailable, Assert.Single(duringGrace.Snapshot.Deployments).Status);
        var expired = CatalogReconciler.Reconcile(previous, [], DateTimeOffset.UnixEpoch.AddMinutes(11), TimeSpan.FromMinutes(10));
        Assert.Empty(expired.Snapshot.Deployments);
        Assert.Single(expired.Removed);
    }

    [Fact]
    public void MarksAmbiguousRoutesInvalid()
    {
        var one = GenerationTests.Qualified();
        var two = one with { Identity = one.Identity with { ProviderId = "ollama", ModelId = "other" } };
        var result = CatalogReconciler.Reconcile(null, [one, two], DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(10));
        Assert.Single(result.Conflicts);
        Assert.All(result.Snapshot.Deployments, x => Assert.Equal(DeploymentStatus.Invalid, x.Status));
    }
}
