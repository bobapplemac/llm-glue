using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Providers;
using LlmGlue.Core.Reconciliation;

namespace LlmGlue.Tests;

public sealed class AgentModelExclusionsTests
{
    [Theory]
    [InlineData("qwen-model", true)]
    [InlineData("QWEN-*", true)]
    [InlineData("Qwen Test", true)]
    [InlineData("llama.cpp/qwen-*", true)]
    [InlineData("llama.cpp/lab/qwen-model", true)]
    [InlineData("qwen-mode?", true)]
    [InlineData("gemma*", false)]
    public void MatchesIdentityNamesAndWildcardsCaseInsensitively(string pattern, bool expected)
    {
        Assert.Equal(expected, ModelExclusionPatterns.Matches(pattern, GenerationTests.Qualified()));
    }

    [Fact]
    public void ApplyRemovesOnlyMatchingDeployments()
    {
        var included = GenerationTests.Qualified();
        var excluded = included with { Identity = included.Identity with { ModelId = "bench-01-02", DisplayName = "Bench 01 02", Route = "bench-01-02" } };

        var result = ModelExclusionPatterns.Apply([included, excluded], ["bench-*"]);

        Assert.Equal("qwen-model", Assert.Single(result).Identity.ModelId);
    }

    [Fact]
    public async Task DiscoveryDecoratorFiltersBeforeDownstreamProbeComposition()
    {
        IModelDiscoveryAdapter adapter = new AgentExclusionDiscoveryAdapter(
            new FixedAdapter(GenerationTests.Qualified()), ["qwen-*"]);
        using var http = new HttpClient(new TestHttpHandler(_ => throw new InvalidOperationException("Excluded deployment must not be probed.")))
        {
            BaseAddress = new Uri("http://provider.test/")
        };
        adapter = new ProbingDiscoveryAdapter(adapter, new InferenceRouteProbe(
            http, "llama.cpp", CapabilityProbeMode.AllowLoad, TimeSpan.FromSeconds(1)));

        var result = await adapter.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    private sealed class FixedAdapter(ModelDeployment deployment) : IModelDiscoveryAdapter
    {
        public string ProviderType => "test";
        public Task<IReadOnlyList<ModelDeployment>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDeployment>>([deployment]);
    }
}
