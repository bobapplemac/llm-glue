using System.Net;
using Microsoft.Net.Http.Headers;

namespace LlmGlue.Api;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapGlueApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapGet("/health", () => Results.Ok(new { status = "healthy", version = ProductInfo.Revision, schemaVersion = 1 }));
        api.MapGet("/inventory", (ApiState state, HttpContext context) => Etag(context, state.Snapshot.ContentHash, state.Snapshot));
        api.MapGet("/models", (ApiState state, HttpContext context) => Etag(context, state.Snapshot.ContentHash, new { schemaVersion = 1, revision = state.Snapshot.ContentHash, models = state.Snapshot.Deployments }));
        api.MapGet("/artifacts/{target}", (string target, ApiState state, HttpContext context) => Artifact(target, state, context));
        api.MapGet("/status", (ApiState state) => Results.Ok(new
        {
            version = ProductInfo.Revision,
            schemaVersion = 1,
            revision = state.Snapshot.ContentHash,
            modelCount = state.Snapshot.Deployments.Count,
            publishedModelCount = state.PublishedSnapshot.Deployments.Count,
            excludedModelCount = state.Snapshot.Deployments.Count - state.PublishedSnapshot.Deployments.Count,
            artifacts = new[] { "opencode", "litellm" }.Where(x => state.Store.Latest(x) is not null).ToArray()
        }));
        api.MapPost("/agent/inventory", async (LlmGlue.Core.Domain.DiscoverySnapshot snapshot, ApiState state, CancellationToken cancellationToken) =>
        {
            if (snapshot.SchemaVersion != 1) return Results.BadRequest(new { error = "unsupported_schema" });
            var canonical = LlmGlue.Core.Domain.DiscoverySnapshot.Create(snapshot.CapturedAt, snapshot.Deployments);
            if (!string.Equals(canonical.ContentHash, snapshot.ContentHash, StringComparison.Ordinal)) return Results.BadRequest(new { error = "invalid_content_hash" });
            if (!await state.PublishAsync(canonical, cancellationToken))
                return Results.Conflict(new { error = "stale_inventory" });
            return Results.Accepted(value: new { revision = canonical.ContentHash });
        });
        api.MapPost("/tls/reload", (HttpContext context, IServiceProvider services) => TlsOperation(context, services, regenerate: false, force: false));
        api.MapPost("/tls/regenerate", (bool force, HttpContext context, IServiceProvider services) => TlsOperation(context, services, regenerate: true, force));
        return endpoints;
    }

    private static IResult TlsOperation(HttpContext context, IServiceProvider services, bool regenerate, bool force)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("LlmGlue.TlsManagement");
        if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
        {
            logger.LogWarning("Rejected non-loopback TLS management request from {RemoteAddress}", context.Connection.RemoteIpAddress);
            return Results.Json(new { error = "local_only" }, statusCode: StatusCodes.Status403Forbidden);
        }
        var authentication = services.GetRequiredService<ApiAuthenticationOptions>();
        if (!string.IsNullOrEmpty(authentication.WriteToken)
            && !ApiAuthenticationMiddleware.TryBearer(context.Request.Headers.Authorization, authentication.WriteToken))
            return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        var tls = services.GetService<LlmGlue.Security.ReloadableServerTlsCertificate>();
        if (tls is null) return Results.BadRequest(new { error = "https_not_enabled" });
        try
        {
            var result = regenerate ? tls.Regenerate(force) : tls.Reload();
            logger.LogInformation(
                "TLS certificate {Operation}; changed={Changed}; certificate SHA-256 {PreviousCertificateSha256} -> {CertificateSha256}; public-key SHA-256 {PreviousPublicKeySha256} -> {PublicKeySha256}",
                regenerate ? "regenerated" : "reloaded", result.Changed, result.PreviousCertificateSha256, result.CertificateSha256,
                result.PreviousPublicKeySha256, result.PublicKeySha256);
            return Results.Ok(result);
        }
        catch (LlmGlue.Core.Configuration.ConfigurationException ex)
        {
            logger.LogWarning("TLS certificate operation rejected: {Error}", ex.Message);
            return Results.BadRequest(new { error = "invalid_tls_material", detail = ex.Message });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            logger.LogWarning("TLS certificate operation failed: {Error}", ex.Message);
            return Results.Json(new { error = "tls_material_update_failed", detail = ex.Message }, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static IResult Artifact(string target, ApiState state, HttpContext context)
    {
        var stored = state.Store.Latest(target);
        if (stored is null) return Results.NotFound(new { error = "artifact_not_found" });
        var etag = $"\"{stored.Artifact.ContentHash}\"";
        if (context.Request.Headers.IfNoneMatch.Any(x => string.Equals(x, etag, StringComparison.Ordinal))) return Results.StatusCode(StatusCodes.Status304NotModified);
        context.Response.Headers.ETag = etag;
        context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
        context.Response.Headers["X-LLM-Glue-Revision"] = stored.Revision;
        return Results.File(stored.Artifact.Content, stored.Artifact.MediaType);
    }

    private static IResult Etag<T>(HttpContext context, string hash, T value)
    {
        var etag = $"\"{hash}\"";
        if (context.Request.Headers.IfNoneMatch.Any(x => string.Equals(x, etag, StringComparison.Ordinal))) return Results.StatusCode(StatusCodes.Status304NotModified);
        context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = "no-cache";
        return Results.Json(value);
    }
}
