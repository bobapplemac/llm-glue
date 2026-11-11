using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Reconciliation;

public static class ModelExclusionPatterns
{
    public static IReadOnlyList<ModelDeployment> Apply(IEnumerable<ModelDeployment> deployments, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0) return deployments as IReadOnlyList<ModelDeployment> ?? deployments.ToArray();
        return deployments.Where(deployment => !patterns.Any(pattern => Matches(pattern, deployment))).ToArray();
    }

    public static bool Matches(string pattern, ModelDeployment deployment) =>
        WildcardMatch(pattern, deployment.Identity.ModelId)
        || WildcardMatch(pattern, deployment.Identity.DisplayName)
        || WildcardMatch(pattern, deployment.Identity.Route)
        || WildcardMatch(pattern, $"{deployment.Identity.ProviderId}/{deployment.Identity.ModelId}")
        || WildcardMatch(pattern, $"{deployment.Identity.ProviderId}/{deployment.Identity.InstanceId}/{deployment.Identity.ModelId}");

    internal static bool WildcardMatch(string pattern, string value)
    {
        var patternIndex = 0;
        var valueIndex = 0;
        var starIndex = -1;
        var retryValueIndex = -1;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length && (pattern[patternIndex] == '?'
                || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                patternIndex++;
                valueIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                retryValueIndex = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++retryValueIndex;
            }
            else return false;
        }
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*') patternIndex++;
        return patternIndex == pattern.Length;
    }
}
