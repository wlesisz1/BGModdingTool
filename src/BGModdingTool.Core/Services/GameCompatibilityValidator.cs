using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Checks whether a build's mods declare support for the build's target game,
/// merging two sources: the mod's own Project Infinity metadata and the LCC
/// community database. Warns only when at least one source has data and none
/// of them lists the target game — no data means no verdict, not a warning.
/// </summary>
public static class GameCompatibilityValidator
{
    /// <summary>Game tokens (normalized: uppercase, no ':') accepted for each target.</summary>
    public static HashSet<string> AcceptedTokens(GameType target) => target switch
    {
        GameType.BGEE => ["BGEE", "SOD"],
        GameType.BG2EE => ["BG2EE"],
        GameType.IWDEE => ["IWDEE"],
        GameType.EET => ["EET"],
        GameType.BGT => ["BGT"],
        GameType.BG2Classic => ["BG2"],
        _ => [],
    };

    public static string NormalizeToken(string game) =>
        game.Replace(":", "").Replace(" ", "").ToUpperInvariant();

    /// <summary>
    /// True when the declared game list allows the target game. No data at all
    /// (empty list) means "no verdict" and counts as compatible.
    /// </summary>
    public static bool IsCompatible(GameType target, IEnumerable<string> declaredGames)
    {
        var declared = declaredGames.Select(NormalizeToken).ToHashSet();
        if (declared.Count == 0) return true;
        var accepted = AcceptedTokens(target);
        return accepted.Count == 0 || declared.Overlaps(accepted);
    }

    public static List<string> Validate(
        IReadOnlyList<BuildEntry> entries,
        GameType targetGame,
        IReadOnlyDictionary<string, ModMetadata> metadataByKey,
        LccDatabase? lcc)
    {
        var issues = new List<string>();
        var accepted = AcceptedTokens(targetGame);
        if (accepted.Count == 0) return issues;

        var real = entries.Where(e => e.IncludeBuild is null).ToList();
        var presentIds = new Dictionary<int, string>();
        foreach (var e in real)
        {
            var m = lcc?.FindByTp2(e.Tp2);
            if (m is not null) presentIds.TryAdd(m.Id, m.Name);
        }

        foreach (var entry in real)
        {
            var key = InstallOrderService.NormalizeKey(entry.Tp2);
            var declared = new HashSet<string>();
            var hasData = false;

            if (metadataByKey.TryGetValue(key, out var meta) && meta.Games.Count > 0)
            {
                hasData = true;
                foreach (var g in meta.Games) declared.Add(NormalizeToken(g));
            }
            var lccMod = lcc?.FindByTp2(entry.Tp2);
            if (lccMod is not null)
            {
                if (lccMod.Games.Count > 0)
                {
                    hasData = true;
                    foreach (var g in lccMod.Games) declared.Add(NormalizeToken(g));
                }
                if (lccMod.Safe == 0)
                    issues.Add($"\"{key}\" is flagged in the LCC database as problematic/obsolete ({lccMod.Name}).");
                foreach (var conflictId in lccMod.Compatibilities?.ConflictIds ?? [])
                {
                    if (presentIds.TryGetValue(conflictId, out var conflictName) && lccMod.Id < conflictId)
                        issues.Add($"Conflict per LCC database: \"{lccMod.Name}\" vs \"{conflictName}\" — do not install both.");
                }
                foreach (var requiredId in lccMod.Compatibilities?.RequireIds ?? [])
                {
                    if (!presentIds.ContainsKey(requiredId))
                    {
                        var requiredName = lcc?.FindById(requiredId)?.Name ?? $"#{requiredId}";
                        issues.Add($"\"{lccMod.Name}\" requires \"{requiredName}\" (per LCC), which is not in this build.");
                    }
                }
            }

            if (hasData && !declared.Overlaps(accepted))
            {
                issues.Add($"\"{key}\" does not declare support for {targetGame.DisplayName()} " +
                           $"(supports: {string.Join(", ", declared.OrderBy(x => x))}).");
            }
        }

        return issues.Distinct().ToList();
    }
}
