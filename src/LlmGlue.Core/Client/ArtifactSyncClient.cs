using System.Net;
using System.Net.Http.Headers;
using LlmGlue.Core.Abstractions;

namespace LlmGlue.Core.Client;

public sealed record TargetSyncResult(string Target, bool Changed, string? Revision, string? Error);

public sealed class ArtifactSyncClient(HttpClient httpClient, IEnumerable<IConfigurationTarget> targets, string? bearerToken = null, int maximumArtifactBytes = 4 * 1024 * 1024)
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IConfigurationTarget>> _targets = targets
        .GroupBy(x => x.TargetType, StringComparer.Ordinal)
        .ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<IConfigurationTarget>)x.OrderBy(target => target.TargetId, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    private readonly Dictionary<string, EntityTagHeaderValue> _etags = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<TargetSyncResult>> SyncOnceAsync(CancellationToken cancellationToken)
    {
        var results = new List<TargetSyncResult>();
        foreach (var group in _targets.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/artifacts/{Uri.EscapeDataString(group.Key)}");
                if (bearerToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                if (_etags.TryGetValue(group.Key, out var etag)) request.Headers.IfNoneMatch.Add(etag);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var revision = Header(response, "X-LLM-Glue-Revision");
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    results.AddRange(group.Value.Select(target => new TargetSyncResult(target.TargetId, false, revision, null)));
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 0 and var length && length > maximumArtifactBytes) throw new InvalidDataException("Artifact exceeds configured size limit.");
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken);
                if (memory.Length > maximumArtifactBytes) throw new InvalidDataException("Artifact exceeds configured size limit.");
                var artifact = new GeneratedArtifact(response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", memory.ToArray(), response.Headers.ETag?.Tag.Trim('"') ?? string.Empty, 1);
                var allApplied = true;
                foreach (var target in group.Value)
                {
                    try
                    {
                        await target.ApplyAsync(artifact, cancellationToken);
                        results.Add(new(target.TargetId, true, revision, null));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        allApplied = false;
                        results.Add(new(target.TargetId, false, revision, ex.Message));
                    }
                }
                if (allApplied && response.Headers.ETag is not null) _etags[group.Key] = response.Headers.ETag;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.AddRange(group.Value.Select(target => new TargetSyncResult(target.TargetId, false, null, ex.Message)));
            }
        }
        return results;
    }

    private static string? Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
