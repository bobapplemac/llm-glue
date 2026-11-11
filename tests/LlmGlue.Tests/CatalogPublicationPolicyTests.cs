using System.Text.Json;
using LlmGlue.Api;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Generation;
using LlmGlue.Core.Reconciliation;

namespace LlmGlue.Tests;

public sealed class CatalogPublicationPolicyTests
{
    [Fact]
    public void ExactScopedRuleHidesOnlyMatchingDeploymentAndKeepsDiagnostics()
    {
        var included = GenerationTests.Qualified();
        var hidden = included with
        {
            Identity = included.Identity with { ModelId = "hidden", DisplayName = "Hidden", Route = "hidden" }
        };
        var snapshot = LlmGlue.Core.Domain.DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [included, hidden]);
        var rule = new ExclusionRuleOptions("llama.cpp", "lab", "hidden", null, null);

        var view = CatalogPublicationPolicy.Apply(snapshot, [rule]);

        Assert.Equal(2, view.Diagnostics.Deployments.Count);
        Assert.Single(view.Published.Deployments);
        Assert.Equal("qwen-model", view.Published.Deployments[0].Identity.ModelId);
        Assert.Contains("Excluded from client publication", view.Diagnostics.Deployments.Single(x => x.Identity.ModelId == "hidden").Warnings.Single());
    }

    [Fact]
    public void ScopedWildcardRuleHidesMatchesAndKeepsDiagnostics()
    {
        var included = GenerationTests.Qualified();
        var hidden = included with
        {
            Identity = included.Identity with { ModelId = "bench-01-02", DisplayName = "Bench 01 02", Route = "bench-01-02" }
        };
        var snapshot = LlmGlue.Core.Domain.DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [included, hidden]);
        var rule = new ExclusionRuleOptions("llama.cpp", "lab", null, null, null, "bench-*");

        var view = CatalogPublicationPolicy.Apply(snapshot, [rule]);

        Assert.Equal(2, view.Diagnostics.Deployments.Count);
        Assert.Equal("qwen-model", Assert.Single(view.Published.Deployments).Identity.ModelId);
        Assert.Contains("model_pattern=bench-*", view.Diagnostics.Deployments.Single(x => x.Identity.ModelId == "bench-01-02").Warnings.Single());
    }

    [Fact]
    public async Task ApiStateGeneratesFromFilteredViewAndRejectsOlderInventory()
    {
        var store = new ArtifactStore();
        var state = new ApiState(
            [new OpenCodeGenerator(), new LiteLlmGenerator()],
            store,
            [new ExclusionRuleOptions(null, null, "qwen-model", null, null)]);
        var recent = GenerationTests.Snapshot() with { CapturedAt = DateTimeOffset.UtcNow };
        Assert.True(await state.PublishAsync(recent, TestContext.Current.CancellationToken));
        Assert.Empty(state.PublishedSnapshot.Deployments);
        Assert.Contains("Excluded from client publication", Assert.Single(state.Snapshot.Deployments).Warnings.Single());
        using (var json = JsonDocument.Parse(store.Latest("opencode")!.Artifact.Content))
            Assert.Empty(json.RootElement.GetProperty("providers").EnumerateObject());

        var older = LlmGlue.Core.Domain.DiscoverySnapshot.Create(recent.CapturedAt.AddMinutes(-1), []);
        Assert.False(await state.PublishAsync(older, TestContext.Current.CancellationToken));
        Assert.Single(state.Snapshot.Deployments);
    }
}
