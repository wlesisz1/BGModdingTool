using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Checks a (flattened) build against ordering constraints: PI metadata,
/// user order rules, and built-in recipe requirements (e.g. EET_end last).
/// Pure position checking — never modifies the build.
/// </summary>
public static class OrderValidator
{
    public static List<string> Validate(
        IReadOnlyList<BuildEntry> entries,
        IReadOnlyDictionary<string, ModMetadata> constraints)
    {
        var issues = new List<string>();
        var real = entries.Where(e => e.IncludeBuild is null).ToList();

        // First occurrence of every mod key. Ordering constraints are judged on
        // first installations — a mod split into several entries (e.g. NWNForBG
        // core + late campaign component) satisfies "after X" via its first entry.
        var firstIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < real.Count; i++)
            firstIndex.TryAdd(InstallOrderService.NormalizeKey(real[i].Tp2), i);

        foreach (var key in firstIndex.Keys)
        {
            if (!constraints.TryGetValue(key, out var meta)) continue;

            foreach (var dep in meta.After.Select(InstallOrderService.NormalizeKey).Distinct())
            {
                if (firstIndex.TryGetValue(dep, out var j) && j > firstIndex[key])
                    issues.Add($"\"{key}\" should come AFTER \"{dep}\" but is before it (positions {firstIndex[key] + 1} and {j + 1}).");
            }
            foreach (var dep in meta.Before.Select(InstallOrderService.NormalizeKey).Distinct())
            {
                if (firstIndex.TryGetValue(dep, out var j) && j < firstIndex[key])
                    issues.Add($"\"{key}\" should come BEFORE \"{dep}\" but is after it (positions {firstIndex[key] + 1} and {j + 1}).");
            }
        }

        // Recipe requirements (e.g. EET_end must be the last entry).
        for (var i = 0; i < real.Count; i++)
        {
            var recipe = ModRecipeCatalog.Find(real[i].Tp2);
            if (recipe is { MustBeLast: true } && i != real.Count - 1)
                issues.Add($"\"{recipe.Title}\" must be the LAST entry, but is at position {i + 1}/{real.Count}.");
        }

        return issues.Distinct().ToList();
    }
}
