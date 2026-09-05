using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

public sealed record SortResult(List<BuildEntry> Sorted, List<string> Unresolved)
{
    public bool HadCycle => Unresolved.Count > 0;
}

/// <summary>
/// Install-order helpers. Two strategies:
/// 1. Topological sort from Project Infinity Before/After metadata (stable:
///    unconstrained entries keep their current relative order).
/// 2. Sort by a community master order list (one mod per line, BWS/EET style).
/// </summary>
public static class InstallOrderService
{
    /// <summary>
    /// Normalizes any tp2 spelling to a comparison key:
    /// "STRATAGEMS/SETUP-STRATAGEMS.TP2", "setup-stratagems.tp2", "stratagems"
    /// all become "stratagems".
    /// </summary>
    public static string NormalizeKey(string tp2)
    {
        var name = tp2.Replace('\\', '/').Split('/')[^1].Trim().ToLowerInvariant();
        if (name.EndsWith(".tp2")) name = name[..^4];
        if (name.StartsWith("setup-")) name = name[6..];
        return name;
    }

    /// <summary>
    /// Topological sort honoring Before/After constraints. When
    /// <paramref name="tierOf"/> is given, unconstrained entries additionally
    /// gravitate to their install-order tier (fixpacks first, UI last…);
    /// entries with equal tier keep their current relative order.
    /// </summary>
    public static SortResult SortByMetadata(
        IReadOnlyList<BuildEntry> entries,
        IReadOnlyDictionary<string, ModMetadata> metadataByModKey,
        Func<BuildEntry, int?>? tierOf = null)
    {
        var n = entries.Count;
        var keyOf = entries.Select(e => NormalizeKey(e.Tp2)).ToList();
        var indexByKey = new Dictionary<string, int>();
        for (var i = 0; i < n; i++) indexByKey.TryAdd(keyOf[i], i);

        // edges[a] = set of b meaning "a must come before b"
        var edges = Enumerable.Range(0, n).Select(_ => new HashSet<int>()).ToArray();
        var indegree = new int[n];

        void AddEdge(int before, int after)
        {
            if (before == after) return;
            if (edges[before].Add(after)) indegree[after]++;
        }

        for (var i = 0; i < n; i++)
        {
            if (!metadataByModKey.TryGetValue(keyOf[i], out var meta)) continue;
            foreach (var dep in meta.After.Select(NormalizeKey))
                if (indexByKey.TryGetValue(dep, out var j)) AddEdge(j, i);
            foreach (var dep in meta.Before.Select(NormalizeKey))
                if (indexByKey.TryGetValue(dep, out var j)) AddEdge(i, j);
        }

        // Stable Kahn: take the ready node with the lowest (tier, original index).
        var tiers = new int[n];
        for (var i = 0; i < n; i++)
            tiers[i] = tierOf?.Invoke(entries[i]) ?? LccOrderService.DefaultTier;

        var ready = new PriorityQueue<int, (int Tier, int Index)>();
        for (var i = 0; i < n; i++) if (indegree[i] == 0) ready.Enqueue(i, (tiers[i], i));

        var order = new List<int>(n);
        while (ready.Count > 0)
        {
            var i = ready.Dequeue();
            order.Add(i);
            foreach (var j in edges[i])
                if (--indegree[j] == 0) ready.Enqueue(j, (tiers[j], j));
        }

        // Cycle: append leftovers in original order and report them.
        var unresolved = new List<string>();
        if (order.Count < n)
        {
            var placed = order.ToHashSet();
            for (var i = 0; i < n; i++)
            {
                if (placed.Contains(i)) continue;
                order.Add(i);
                unresolved.Add(entries[i].Tp2);
            }
        }

        return new SortResult([.. order.Select(i => entries[i])], unresolved);
    }

    /// <summary>
    /// Sorts entries by a master order list (one mod name / tp2 per line;
    /// blank lines and lines starting with # or ; are ignored). Entries not on
    /// the list keep their relative order and land after the listed ones.
    /// </summary>
    public static List<BuildEntry> SortByMasterList(IReadOnlyList<BuildEntry> entries, string masterListContent)
    {
        var rank = new Dictionary<string, int>();
        var next = 0;
        foreach (var rawLine in masterListContent.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';') || line.StartsWith("//")) continue;
            rank.TryAdd(NormalizeKey(line), next++);
        }

        return [.. entries
            .Select((e, i) => (Entry: e, Index: i,
                Rank: rank.TryGetValue(NormalizeKey(e.Tp2), out var r) ? r : int.MaxValue))
            .OrderBy(x => x.Rank).ThenBy(x => x.Index)
            .Select(x => x.Entry)];
    }
}
