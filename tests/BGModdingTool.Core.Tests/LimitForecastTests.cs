using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class LimitForecastTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-forecast-" + Guid.NewGuid().ToString("N")[..8]);
    public LimitForecastTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Sums_measured_ledger_rows_and_reports_the_overflowing_entry()
    {
        var ledger = new List<LimitLedgerEntry>
        {
            new("DW_TALENTS/DW_TALENTS.TP2", [40150], DateTimeOffset.UtcNow, new() { ["SPLSTATE.IDS"] = 8 }),
            new("DW_TALENTS/DW_TALENTS.TP2", [60200], DateTimeOffset.UtcNow, new() { ["SPLSTATE.IDS"] = 26 }),
            new("DW_TALENTS/DW_TALENTS.TP2", [80000], DateTimeOffset.UtcNow, new() { ["SPLSTATE.IDS"] = 11 }),
            new("STRATAGEMS/SETUP-STRATAGEMS.TP2", [5900], DateTimeOffset.UtcNow, new() { ["SPLSTATE.IDS"] = 52 }),
            new("ATWEAKS/SETUP-ATWEAKS.TP2", [160], DateTimeOffset.UtcNow, new() { ["SPLSTATE.IDS"] = 15 }),
        };
        var flat = new List<BuildEntry>
        {
            new() { Tp2 = "DW_TALENTS/DW_TALENTS.TP2", Components = [40150, 60200, 80000, 90000] },
            new() { Tp2 = "STRATAGEMS/SETUP-STRATAGEMS.TP2", Components = [5900, 6000] },
            new() { Tp2 = "ATWEAKS/SETUP-ATWEAKS.TP2", Components = [100, 160] },
        };
        var r = LimitForecast.SplState(flat, GameType.EET, ledger, _ => null);
        Assert.Equal(152 + 45 + 52 + 15, r.Total); // 249 + 15 = 264
        Assert.True(r.IsOver, $"total={r.Total} limit={r.Limit} left={r.Left} lines={string.Join(";", r.Lines.Select(l => l.Tp2 + "=" + l.Amount))}");
        Assert.Equal(3, r.Overflow!.Position);
        Assert.All(r.Lines, l => Assert.Equal("measured", l.Source));

        // Without the Detectable-Spells component the same build fits.
        flat[2] = new BuildEntry { Tp2 = "ATWEAKS/SETUP-ATWEAKS.TP2", Components = [100] };
        var ok = LimitForecast.SplState(flat, GameType.EET, ledger, _ => null);
        Assert.False(ok.IsOver);
        Assert.Equal(7, ok.Left);
    }

    [Fact]
    public void Static_scan_counts_distinct_spell_state_additions_in_mod_code()
    {
        Directory.CreateDirectory(Path.Combine(_root, "mymod", "lib"));
        File.WriteAllText(Path.Combine(_root, "mymod", "setup-mymod.tp2"),
            "BEGIN ~x~\nADD_SPLSTATE MY_STATE_A\nADD_SPLSTATE ~MY_STATE_B~\n");
        File.WriteAllText(Path.Combine(_root, "mymod", "lib", "more.tpa"),
            "ADD_IDS_ENTRY ~SPLSTATE.IDS~ ~MY_STATE_C~\nADD_SPLSTATE MY_STATE_A // duplicate\n");
        Assert.Equal(3, LimitForecast.StaticEstimate(_root));

        var flat = new List<BuildEntry> { new() { Tp2 = "MYMOD/SETUP-MYMOD.TP2", Components = [0] } };
        var r = LimitForecast.SplState(flat, GameType.BG2EE, [], _ => _root);
        Assert.Equal("≈ code scan", Assert.Single(r.Lines).Source);
        Assert.Equal(155, r.Total);
    }
}
