using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Abstractions;

public interface IModelDiscoveryAdapter
{
    string ProviderType { get; }
    Task<IReadOnlyList<ModelDeployment>> DiscoverAsync(CancellationToken cancellationToken);
}

public interface IRuntimeInspector
{
    Task<ModelDeployment> InspectAsync(ModelDeployment deployment, CancellationToken cancellationToken);
}

public interface IModelMetadataEnricher
{
    Task<ModelDeployment> EnrichAsync(ModelDeployment deployment, CancellationToken cancellationToken);
}

public interface ICapabilityProbe
{
    Task<ModelDeployment> ProbeAsync(ModelDeployment deployment, CancellationToken cancellationToken);
}

public interface IConfigurationGenerator
{
    string TargetType { get; }
    ValueTask<GeneratedArtifact> GenerateAsync(DiscoverySnapshot snapshot, CancellationToken cancellationToken);
}

public sealed record GeneratedArtifact(string MediaType, byte[] Content, string ContentHash, int SchemaVersion);

public interface IConfigurationTarget
{
    string TargetType { get; }
    string TargetId { get; }
    Task ApplyAsync(GeneratedArtifact artifact, CancellationToken cancellationToken);
}
