using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmGlue.Core.Domain;

public sealed record DiscoverySnapshot(
    int SchemaVersion,
    DateTimeOffset CapturedAt,
    IReadOnlyList<ModelDeployment> Deployments,
    string ContentHash)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static DiscoverySnapshot Create(DateTimeOffset capturedAt, IEnumerable<ModelDeployment> deployments)
    {
        var ordered = deployments
            .OrderBy(x => x.Identity.ProviderId, StringComparer.Ordinal)
            .ThenBy(x => x.Identity.InstanceId, StringComparer.Ordinal)
            .ThenBy(x => x.Identity.ModelId, StringComparer.Ordinal)
            .Select(Normalize)
            .ToArray();
        var hashInput = JsonSerializer.Serialize(new { schemaVersion = 1, deployments = ordered }, JsonOptions);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        return new DiscoverySnapshot(1, capturedAt, ordered, hash);
    }

    private static ModelDeployment Normalize(ModelDeployment value) => value with
    {
        SamplingProfiles = value.SamplingProfiles.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray(),
        Runtime = value.Runtime with
        {
            Properties = new SortedDictionary<string, string>(value.Runtime.Properties.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), StringComparer.Ordinal)
        },
        Warnings = value.Warnings.OrderBy(x => x, StringComparer.Ordinal).ToArray()
    };
}
