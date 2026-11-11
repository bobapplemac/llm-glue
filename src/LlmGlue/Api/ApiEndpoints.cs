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
        return endpoints;
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
