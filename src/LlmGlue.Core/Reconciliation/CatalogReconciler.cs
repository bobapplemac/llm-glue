using LlmGlue.Core.Domain;

namespace LlmGlue.Core.Reconciliation;

public sealed record ReconciliationResult(DiscoverySnapshot Snapshot, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Removed);

public static class CatalogReconciler
{
    public static ReconciliationResult Reconcile(DiscoverySnapshot? previous, IEnumerable<ModelDeployment> discovered, DateTimeOffset now, TimeSpan removalGrace)
    {
        var incoming = discovered.ToDictionary(Key, StringComparer.Ordinal);
        var conflicts = incoming.Values
            .GroupBy(x => x.Identity.Route, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => $"Route '{x.Key}' is claimed by {string.Join(", ", x.Select(y => Key(y)).Order(StringComparer.Ordinal))}.")
            .Order(StringComparer.Ordinal).ToArray();

        var values = incoming.Values.ToList();
        var removed = new List<string>();
        if (previous is not null)
        {
            foreach (var old in previous.Deployments.Where(x => !incoming.ContainsKey(Key(x))))
            {
                if (now - old.LastSeenAt >= removalGrace) removed.Add(Key(old));
                else values.Add(old with
                {
                    Status = DeploymentStatus.Unavailable,
                    Warnings = old.Warnings.Concat(["Model was absent from the latest discovery and is retained during the removal grace period."]).Distinct(StringComparer.Ordinal).ToArray()
                });
            }
        }

        if (conflicts.Length != 0)
        {
            var conflictRoutes = incoming.Values.GroupBy(x => x.Identity.Route, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            values = values.Select(x => conflictRoutes.Contains(x.Identity.Route) ? x with { Status = DeploymentStatus.Invalid, Warnings = x.Warnings.Concat(["Conflicting client route."]).ToArray() } : x).ToList();
        }

        return new(DiscoverySnapshot.Create(now, values), conflicts, removed.Order(StringComparer.Ordinal).ToArray());
    }

    public static string Key(ModelDeployment deployment) => $"{deployment.Identity.ProviderId}/{deployment.Identity.InstanceId}/{deployment.Identity.ModelId}";
}
