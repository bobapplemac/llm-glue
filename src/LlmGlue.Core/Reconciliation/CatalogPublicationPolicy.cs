using LlmGlue.Core.Configuration;
using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Reconciliation;

public sealed record PublicationView(DiscoverySnapshot Diagnostics, DiscoverySnapshot Published);

public static class CatalogPublicationPolicy
{
    public static PublicationView Apply(DiscoverySnapshot snapshot, IReadOnlyList<ExclusionRuleOptions> rules)
    {
        if (rules.Count == 0) return new(snapshot, snapshot);

        var diagnostics = new List<ModelDeployment>(snapshot.Deployments.Count);
        var published = new List<ModelDeployment>(snapshot.Deployments.Count);
        foreach (var deployment in snapshot.Deployments)
        {
            var matches = rules.Select((rule, index) => (rule, index)).Where(x => Matches(deployment, x.rule)).ToArray();
            if (matches.Length == 0)
            {
                diagnostics.Add(deployment);
                published.Add(deployment);
                continue;
            }

            var descriptions = matches.Select(x => $"server exclusion rule {x.index + 1} ({Describe(x.rule)})");
            diagnostics.Add(deployment with
            {
                Warnings = deployment.Warnings.Concat([$"Excluded from client publication by {string.Join(", ", descriptions)}."])
                    .Distinct(StringComparer.Ordinal).ToArray()
            });
        }

        return new(
            DiscoverySnapshot.Create(snapshot.CapturedAt, diagnostics),
            DiscoverySnapshot.Create(snapshot.CapturedAt, published));
    }

    private static bool Matches(ModelDeployment deployment, ExclusionRuleOptions rule)
    {
        var identity = deployment.Identity;
        return Match(rule.ProviderId, identity.ProviderId)
            && Match(rule.InstanceId, identity.InstanceId)
            && Match(rule.ModelId, identity.ModelId)
            && Match(rule.DisplayName, identity.DisplayName)
            && Match(rule.ProviderModel, $"{identity.ProviderId}/{identity.InstanceId}/{identity.ModelId}")
            && (rule.ModelPattern is null || ModelExclusionPatterns.Matches(rule.ModelPattern, deployment));
    }

    private static bool Match(string? expected, string actual) => expected is null || string.Equals(expected, actual, StringComparison.Ordinal);

    private static string Describe(ExclusionRuleOptions rule) => string.Join(", ", new[]
    {
        Pair("provider_id", rule.ProviderId), Pair("instance_id", rule.InstanceId), Pair("model_id", rule.ModelId),
        Pair("display_name", rule.DisplayName), Pair("provider_model", rule.ProviderModel), Pair("model_pattern", rule.ModelPattern)
    }.Where(x => x is not null));

    private static string? Pair(string name, string? value) => value is null ? null : $"{name}={value}";
}
