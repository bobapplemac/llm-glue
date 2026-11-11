using System.Net;
using System.Net.Http.Headers;
using LlmGlue.Core.Abstractions;

namespace LlmGlue.Core.Client;

public sealed record TargetSyncResult(string Target, bool Changed, string? Revision, string? Error);

public sealed class ArtifactSyncClient(HttpClient httpClient, IEnumerable<IConfigurationTarget> targets, string? bearerToken = null, int maximumArtifactBytes = 4 * 1024 * 1024)
{
    private readonly IReadOnlyDictionary<string, IConfigurationTarget> _targets = targets.ToDictionary(x => x.TargetType, StringComparer.Ordinal);
    private readonly Dictionary<string, EntityTagHeaderValue> _etags = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<TargetSyncResult>> SyncOnceAsync(CancellationToken cancellationToken)
    {
        var results = new List<TargetSyncResult>();
        foreach (var pair in _targets.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/artifacts/{Uri.EscapeDataString(pair.Key)}");
                if (bearerToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                if (_etags.TryGetValue(pair.Key, out var etag)) request.Headers.IfNoneMatch.Add(etag);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotModified) { results.Add(new(pair.Key, false, Header(response, "X-LLM-Glue-Revision"), null)); continue; }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 0 and var length && length > maximumArtifactBytes) throw new InvalidDataException("Artifact exceeds configured size limit.");
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken);
                if (memory.Length > maximumArtifactBytes) throw new InvalidDataException("Artifact exceeds configured size limit.");
                var artifact = new GeneratedArtifact(response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", memory.ToArray(), response.Headers.ETag?.Tag.Trim('"') ?? string.Empty, 1);
                await pair.Value.ApplyAsync(artifact, cancellationToken);
                if (response.Headers.ETag is not null) _etags[pair.Key] = response.Headers.ETag;
                results.Add(new(pair.Key, true, Header(response, "X-LLM-Glue-Revision"), null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new(pair.Key, false, null, ex.Message));
            }
        }
        return results;
    }

    private static string? Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
