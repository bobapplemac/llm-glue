namespace LlmGlue.Core.Configuration;

public enum CapabilityProbeMode { Off, LoadedOnly, AllowLoad }

public sealed record GlueOptions(
    RoleOptions Roles,
    AgentOptions Agent,
    ServerOptions Server,
    ClientOptions Client,
    LoggingOptions Logging,
    string ConfigPath,
    string BaseDirectory)
{
    public IReadOnlyList<ProviderOptions> Providers => Agent.Providers;
}

public sealed record RoleOptions(bool Agent, bool Server, bool Client);

public sealed record AgentOptions(
    IReadOnlyList<ProviderOptions> Providers,
    IReadOnlyList<string> ExcludeModels,
    Uri? SubmitTo,
    string? WriteTokenEnvironment,
    string? WriteToken,
    bool AllowInvalidServerCertificate,
    string? TrustedServerPublicKeySha256,
    int DiscoveryIntervalSeconds,
    int RemovalGraceSeconds);

public sealed record ProviderOptions(
    string Type,
    string Name,
    Uri Url,
    IReadOnlyList<string> ModelDirectories,
    bool InspectGguf,
    bool HashModelFiles,
    CapabilityProbeMode ProbeMode,
    int ProbeTimeoutSeconds,
    int DiscoveryTimeoutSeconds = 300,
    IReadOnlyList<ModelPathMapping>? ModelPathMappings = null);

public sealed record ModelPathMapping(string ProviderPrefix, string LocalDirectory);

public sealed record ServerOptions(
    Uri Listen,
    Uri? OpenCodeInferenceUrl,
    bool EmbedOpenCodeInferenceApiKey,
    string? OpenCodeInferenceApiKeyEnvironment,
    string? OpenCodeInferenceApiKey,
    string OpenCodeNamespace,
    IReadOnlyList<OpenCodeNamespaceOverrideOptions> OpenCodeNamespaceOverrides,
    string LiteLlmNamespace,
    string? ReadTokenEnvironment,
    string? WriteTokenEnvironment,
    string? ReadToken,
    string? WriteToken,
    bool AcknowledgeInsecureRemoteHttp,
    bool TlsAutoGenerate,
    string? TlsCertificatePath,
    string? TlsPrivateKeyPath,
    string? TlsCertificatePasswordEnvironment,
    string? TlsCertificatePassword,
    IReadOnlyList<string> TlsServerNames,
    IReadOnlyList<ExclusionRuleOptions> Exclusions,
    string StateDirectory,
    int ArtifactRetention);

public sealed record OpenCodeNamespaceOverrideOptions(
    string Namespace,
    string? ProviderId,
    string? InstanceId);

public sealed record ExclusionRuleOptions(
    string? ProviderId,
    string? InstanceId,
    string? ModelId,
    string? DisplayName,
    string? ProviderModel,
    string? ModelPattern = null);

public sealed record ClientOptions(
    Uri? Source,
    string? TokenEnvironment,
    string? Token,
    bool AllowInvalidServerCertificate,
    string? TrustedServerPublicKeySha256,
    int PollIntervalSeconds,
    IReadOnlyList<TargetOptions> Targets);

public sealed record TargetOptions(string Type, string Path, string? PrimaryConfigPath, string? ServiceName, Uri? HealthUrl, bool DenyBuiltInOpenCodeProvider);

public sealed record LoggingOptions(string Level, string File, int ArchiveSizeMb, int Retention);

public sealed class ConfigurationException(string message) : Exception(message);

public static class SecretResolver
{
    public static string? Resolve(string? literal, string? environmentName) => literal ?? (environmentName is null ? null : Environment.GetEnvironmentVariable(environmentName));
}
