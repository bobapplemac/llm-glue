namespace LlmGlue.Core.Domain;

public enum SupportState { Unknown, Unsupported, Supported }
public enum VerificationState { Reported, Configured, Observed, Verified }

public sealed record Evidence<T>(
    T? Value,
    string Source,
    DateTimeOffset ObservedAt,
    VerificationState Verification,
    string? Warning = null);

public sealed record Capability(
    SupportState State,
    IReadOnlyList<Evidence<string>> Evidence);
