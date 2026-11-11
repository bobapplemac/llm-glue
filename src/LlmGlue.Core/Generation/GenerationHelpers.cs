using System.Security.Cryptography;
using System.Text;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Generation;

internal static class GenerationHelpers
{
    public static GeneratedArtifact Artifact(string mediaType, string text) => new(mediaType, Encoding.UTF8.GetBytes(text), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), 1);

    public static IEnumerable<ModelDeployment> Publishable(DiscoverySnapshot snapshot) => snapshot.Deployments.Where(x =>
        x.Status is DeploymentStatus.Available or DeploymentStatus.Degraded &&
        !string.IsNullOrWhiteSpace(x.Identity.SourceEndpoint) &&
        x.Capabilities.TextInput.State != SupportState.Unsupported &&
        x.Capabilities.TextOutput.State != SupportState.Unsupported);

    public static string Slug(params string[] values)
    {
        var text = string.Join('-', values).ToLowerInvariant();
        var result = new StringBuilder(text.Length);
        var lastDash = false;
        foreach (var character in text)
        {
            var valid = char.IsAsciiLetterOrDigit(character);
            if (valid) { result.Append(character); lastDash = false; }
            else if (!lastDash) { result.Append('-'); lastDash = true; }
        }
        return result.ToString().Trim('-');
    }
}
