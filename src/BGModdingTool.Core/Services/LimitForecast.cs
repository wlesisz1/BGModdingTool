using System.Text.RegularExpressions;
using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>What one build entry is expected to consume, and how we know.</summary>
public sealed record ForecastLine(int Position, string Tp2, int Amount, string Source);

public sealed record ForecastResult(
    string Table, int Baseline, int Limit, int Total,
    List<ForecastLine> Lines, List<string> NoData, ForecastLine? Overflow)
{
    public bool IsOver => Total > Limit;
    public int Left => Limit - Total;
}

/// <summary>
/// Pre-install estimate of SPLSTATE.IDS usage for a build: measured numbers from the
/// limits ledger where an entry was installed before, a static scan of the mod's code
/// otherwise (marked ≈), and the game's baseline.
/// </summary>
public static partial class LimitForecast
{
    /// <summary>Spell states already present before any build entry (vanilla + the EET/EEex core measured on 2026-09-02).</summary>
    public static int BaselineFor(GameType game) => game switch
    {
        GameType.BG2EE or GameType.EET => 152,
        _ => 140,
    };

    public static ForecastResult SplState(
        IReadOnlyList<BuildEntry> flat, GameType game, IReadOnlyList<LimitLedgerEntry> ledger,
        Func<BuildEntry, string?> packageRootOf)
    {
        var baseline = BaselineFor(game);
        var lines = new List<ForecastLine>();
        var noData = new List<string>();
        var total = baseline;
        ForecastLine? overflow = null;

        for (var i = 0; i < flat.Count; i++)
        {
            var e = flat[i];
            if (e.Tp2 is null || e.IncludeBuild is not null) continue;
            var key = InstallOrderService.NormalizeKey(e.Tp2);
            var wanted = e.Components.ToHashSet();

            // Measured: ledger rows for this mod whose components are all part of this entry.
            var measuredRows = ledger
                .Where(l => InstallOrderService.NormalizeKey(l.Tp2) == key && l.Components.All(wanted.Contains))
                .ToList();
            int amount; string source;
            if (measuredRows.Count > 0)
            {
                amount = measuredRows.Sum(l => l.Deltas.GetValueOrDefault(ResourceLimits.SplState));
                source = "measured";
            }
            else
            {
                var root = packageRootOf(e);
                if (root is null) { noData.Add(e.Tp2); continue; }
                amount = StaticEstimate(root);
                source = "≈ code scan";
            }
            if (amount == 0) continue;
            total += amount;
            var line = new ForecastLine(i + 1, e.Tp2, amount, source);
            lines.Add(line);
            if (overflow is null && total > ResourceLimits.SplStateLimit) overflow = line;
        }
        return new ForecastResult(ResourceLimits.SplState, baseline, ResourceLimits.SplStateLimit, total, lines, noData, overflow);
    }

    /// <summary>
    /// Counts distinct spell-state additions visible in a mod's WeiDU code. Under-counts
    /// library-driven additions (Detectable Spells item labels) — hence "≈".
    /// </summary>
    public static int StaticEstimate(string packageRoot)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(packageRoot, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".tp2" or ".tpa" or ".tph")) continue;
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }
                foreach (Match m in AddSplState().Matches(text)) names.Add(m.Groups[1].Value);
                foreach (Match m in AddIdsSplState().Matches(text)) names.Add(m.Groups[1].Value);
            }
        }
        catch { }
        return names.Count;
    }

    // ADD_SPLSTATE NAME  |  ADD_IDS_ENTRY ~SPLSTATE.IDS~ ~NAME~ … (name may be a variable → counted once per literal)
    [GeneratedRegex(@"ADD_SPLSTATE\s+~?""?([A-Za-z0-9_#%]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddSplState();
    [GeneratedRegex(@"ADD_IDS_ENTRY\s+~?""?splstate(?:\.ids)?~?""?\s+~?""?([A-Za-z0-9_#%]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddIdsSplState();

    public static string Summarize(ForecastResult r)
    {
        var top = r.Lines.OrderByDescending(l => l.Amount).Take(6)
            .Select(l => $"{l.Amount:+#;-#;0} {ShortName(l.Tp2)} #{l.Position} ({l.Source})");
        var head = r.IsOver
            ? $"✖ {r.Table}: {r.Total}/{r.Limit} — overflow at #{r.Overflow!.Position} {ShortName(r.Overflow.Tp2)}; entries above the limit get dead ids"
            : r.Left <= 16
                ? $"⚠ {r.Table}: {r.Total}/{r.Limit} — only {r.Left} left"
                : $"✔ {r.Table}: {r.Total}/{r.Limit} ({r.Left} left)";
        var parts = new List<string> { head, $"   base {r.Baseline}, " + (r.Lines.Count == 0 ? "no consumers known" : string.Join(", ", top)) };
        if (r.NoData.Count > 0) parts.Add($"   no data for {r.NoData.Count} entr{(r.NoData.Count == 1 ? "y" : "ies")} not in the library");
        return string.Join('\n', parts);
    }

    private static string ShortName(string tp2)
    {
        var name = Path.GetFileNameWithoutExtension(tp2.Replace('\\', '/').Split('/')[^1]);
        return name.StartsWith("setup-", StringComparison.OrdinalIgnoreCase) ? name[6..] : name;
    }
}
