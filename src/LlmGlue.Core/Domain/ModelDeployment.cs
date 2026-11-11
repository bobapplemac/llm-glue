namespace LlmGlue.Core.Domain;

public enum DeploymentStatus { Discovered, Available, Degraded, Unavailable, Invalid }
public enum ReasoningModeKind { None, Boolean, NamedEffort, TokenBudget, Unknown }

public sealed record ModelIdentity(
    string ProviderId,
    string InstanceId,
    string ModelId,
    string DisplayName,
    string Route,
    string? Repository = null,
    string? Revision = null,
    string? Family = null,
    string? FileHash = null,
    string? Quantization = null);

public sealed record ReasoningCapabilities(
    ReasoningModeKind Kind,
    IReadOnlyList<string> Modes,
    Evidence<string>? Evidence = null);

public sealed record SamplingParameter(string Name, object Value, string Provenance);

public sealed record SamplingProfile(
    string Name,
    IReadOnlyList<SamplingParameter> Parameters,
    string Provenance,
    bool TransportVerified);

public sealed record ModelCapabilities(
    Capability TextInput,
    Capability ImageInput,
    Capability TextOutput,
    Capability Tools,
    ReasoningCapabilities Reasoning);

public sealed record RuntimeDetails(
    string Implementation,
    string? Version,
    bool? Loaded,
    IReadOnlyDictionary<string, string> Properties);

public sealed record ModelDeployment(
    ModelIdentity Identity,
    DeploymentStatus Status,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset LastSeenAt,
    Evidence<long>? NativeContext,
    Evidence<long>? EffectiveContext,
    ModelCapabilities Capabilities,
    IReadOnlyList<SamplingProfile> SamplingProfiles,
    RuntimeDetails Runtime,
    IReadOnlyList<string> Warnings);
