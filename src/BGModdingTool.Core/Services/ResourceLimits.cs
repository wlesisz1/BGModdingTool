using System.Text.Json;

namespace BGModdingTool.Core.Services;

/// <summary>One measured engine resource: how much of a limited table is used.</summary>
public sealed record LimitUsage(string Name, int Used, int? Limit, IReadOnlyList<int> OverLimitIds)
{
    public bool IsOver => OverLimitIds.Count > 0 || (Limit is { } l && Used > l);
    public bool IsNearLimit => Limit is { } l && Used >= l * 0.94 && !IsOver;
    public override string ToString() => Limit is { } l ? $"{Name} {Used}/{l}" : $"{Name} {Used}";
}

/// <summary>One line of the limits ledger: what a build entry consumed (measured, not guessed).</summary>
public sealed record LimitLedgerEntry(string Tp2, List<int> Components, DateTimeOffset When, Dictionary<string, int> Deltas);

/// <summary>
/// Measures engine tables with hard or practical limits (SPLSTATE.IDS has 256 spell
/// states: 0–255; entries above are silently dead) so an install can report, per
/// entry, how much of each budget was consumed, and warn before the table overflows.
/// </summary>
public static class ResourceLimits
{
    public const string SplState = "SPLSTATE.IDS";
    public const int SplStateLimit = 256;

    public static IReadOnlyList<LimitUsage> Measure(string gameDir)
    {
        var result = new List<LimitUsage>();
        var splstate = IdsIds(Path.Combine(gameDir, "override", SplState));
        if (splstate is not null)
            result.Add(new LimitUsage(SplState, splstate.Count(i => i < SplStateLimit), SplStateLimit,
                splstate.Where(i => i >= SplStateLimit).OrderBy(i => i).ToList()));

        // Detectable-Spells style custom stats live at 400+; no hard engine limit, but a budget worth seeing.
        var stats = IdsIds(Path.Combine(gameDir, "override", "STATS.IDS"));
        if (stats is not null)
            result.Add(new LimitUsage("STATS.IDS custom (400+)", stats.Count(i => i >= 400), null, []));

        var proj = IdsIds(Path.Combine(gameDir, "override", "PROJECTL.IDS"));
        if (proj is not null) result.Add(new LimitUsage("PROJECTL.IDS", proj.Count, null, []));

        var kits = TwoDaRows(Path.Combine(gameDir, "override", "KITLIST.2DA"));
        if (kits is not null) result.Add(new LimitUsage("KITLIST.2DA kits", kits.Value, null, []));

        var tlk = TlkStrings(gameDir);
        if (tlk is not null) result.Add(new LimitUsage("dialog.tlk strings", tlk.Value, null, []));
        return result;
    }

    /// <summary>Human-readable changes between two measurements; empty when nothing moved.</summary>
    public static List<string> Describe(IReadOnlyList<LimitUsage> before, IReadOnlyList<LimitUsage> after, string who)
    {
        var lines = new List<string>();
        foreach (var a in after)
        {
            var b = before.FirstOrDefault(x => x.Name == a.Name);
            var delta = a.Used - (b?.Used ?? 0);
            var newOver = a.OverLimitIds.Except(b?.OverLimitIds ?? []).ToList();
            if (delta == 0 && newOver.Count == 0) continue;
            var text = $"📊 {a}" + (delta != 0 ? $" ({(delta > 0 ? "+" : "")}{delta} by {who})" : "");
            if (newOver.Count > 0)
                text = $"!!! {a.Name} OVERFLOW: {who} wrote {newOver.Count} id(s) above the engine limit " +
                       $"({newOver[0]}..{newOver[^1]}) — the engine ignores them; scripts/effects using them are dead.";
            else if (a.IsNearLimit)
                text += $" ⚠ near the limit — {a.Limit - a.Used} left";
            lines.Add(text);
        }
        return lines;
    }

    /// <summary>Per-table consumption (including over-limit ids) between two measurements.</summary>
    public static Dictionary<string, int> Deltas(IReadOnlyList<LimitUsage> before, IReadOnlyList<LimitUsage> after)
    {
        var result = new Dictionary<string, int>();
        foreach (var a in after)
        {
            var b = before.FirstOrDefault(x => x.Name == a.Name);
            var delta = a.Used + a.OverLimitIds.Count - (b is null ? 0 : b.Used + b.OverLimitIds.Count);
            if (delta != 0) result[a.Name] = delta;
        }
        return result;
    }

    public static void AppendLedger(string ledgerPath, LimitLedgerEntry entry)
    {
        var list = LoadLedger(ledgerPath);
        list.RemoveAll(e => e.Tp2.Equals(entry.Tp2, StringComparison.OrdinalIgnoreCase) && e.Components.SequenceEqual(entry.Components));
        list.Add(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
        File.WriteAllText(ledgerPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static List<LimitLedgerEntry> LoadLedger(string ledgerPath)
    {
        if (!File.Exists(ledgerPath)) return [];
        try { return JsonSerializer.Deserialize<List<LimitLedgerEntry>>(File.ReadAllText(ledgerPath)) ?? []; }
        catch { return []; }
    }

    private static HashSet<int>? IdsIds(string path)
    {
        if (!File.Exists(path)) return null;
        var set = new HashSet<int>();
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out var id)) set.Add(id);
        }
        return set;
    }

    private static int? TwoDaRows(string path)
    {
        if (!File.Exists(path)) return null;
        // 2DA: signature, default value, header, then rows.
        return Math.Max(0, File.ReadLines(path).Count(l => l.Trim().Length > 0) - 3);
    }

    private static int? TlkStrings(string gameDir)
    {
        foreach (var candidate in new[] { Path.Combine(gameDir, "lang", "en_US", "dialog.tlk"), Path.Combine(gameDir, "dialog.tlk") })
        {
            if (!File.Exists(candidate)) continue;
            using var fs = File.OpenRead(candidate);
            var header = new byte[18];
            if (fs.Read(header, 0, 18) < 18) return null;
            return BitConverter.ToInt32(header, 10);
        }
        return null;
    }
}
