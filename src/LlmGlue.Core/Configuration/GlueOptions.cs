namespace LlmGlue.Core.Configuration;

public sealed record GlueOptions(
    RoleOptions Roles,
    IReadOnlyList<ProviderOptions> Providers,
    ServerOptions Server,
    ClientOptions Client,
    LoggingOptions Logging,
    string ConfigPath,
    string BaseDirectory);

public sealed record RoleOptions(bool Agent, bool Server, bool Client);

public sealed record ProviderOptions(
    string Type,
    string Name,
    Uri Url,
    IReadOnlyList<string> ModelDirectories);

public sealed record ServerOptions(Uri Listen, string? ReadTokenEnvironment, string? WriteTokenEnvironment, bool AcknowledgeInsecureRemoteHttp);

public sealed record ClientOptions(Uri? Source, string? TokenEnvironment, int PollIntervalSeconds, IReadOnlyList<TargetOptions> Targets);

public sealed record TargetOptions(string Type, string Path, string? PrimaryConfigPath, string? ServiceName, Uri? HealthUrl);

public sealed record LoggingOptions(string Level, string File, int ArchiveSizeMb, int Retention);

public sealed class ConfigurationException(string message) : Exception(message);
