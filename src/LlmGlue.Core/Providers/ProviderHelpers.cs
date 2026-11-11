using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Providers;

internal static class ProviderHelpers
{
    public static Capability Capability(bool? value, string source, DateTimeOffset now) => new(
        value switch { true => SupportState.Supported, false => SupportState.Unsupported, _ => SupportState.Unknown },
        value is null ? [] : [new Evidence<string>(value.Value ? "supported" : "unsupported", source, now, VerificationState.Reported)]);

    public static ModelCapabilities Capabilities(bool? vision, bool? tools, ReasoningCapabilities reasoning, string source, DateTimeOffset now) => new(
        Capability(true, source, now), Capability(vision, source, now), Capability(true, source, now), Capability(tools, source, now), reasoning);

    public static ModelCapabilities Capabilities(bool? textInput, bool? vision, bool? textOutput, bool? tools, ReasoningCapabilities reasoning, string source, DateTimeOffset now) => new(
        Capability(textInput, source, now), Capability(vision, source, now), Capability(textOutput, source, now), Capability(tools, source, now), reasoning);
}
