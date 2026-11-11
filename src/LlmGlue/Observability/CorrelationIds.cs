using System.Diagnostics;

namespace LlmGlue.Observability;

public static class CorrelationIds
{
    public const string HeaderName = "X-Correlation-ID";

    public static string Create() => ActivityTraceId.CreateRandom().ToString();

    public static bool IsValid(string? value) => value is { Length: > 0 and <= 64 } && value.All(character =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    public static IDisposable BeginScope(ILogger logger, string? value = null) =>
        logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = IsValid(value) ? value : Create() })!;
}

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[CorrelationIds.HeaderName].FirstOrDefault();
        var correlationId = CorrelationIds.IsValid(supplied) ? supplied! : CorrelationIds.Create();
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIds.HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        using var scope = CorrelationIds.BeginScope(logger, correlationId);
        await next(context);
    }
}
