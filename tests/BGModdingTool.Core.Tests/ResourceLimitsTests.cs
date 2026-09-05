using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class ResourceLimitsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-limits-" + Guid.NewGuid().ToString("N")[..8]);
    public ResourceLimitsTests() => Directory.CreateDirectory(Path.Combine(_root, "override"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void WriteSplState(IEnumerable<int> ids) =>
        File.WriteAllLines(Path.Combine(_root, "override", "SPLSTATE.IDS"),
            new[] { "IDS V1.0" }.Concat(ids.Select(i => $"{i} STATE_{i}")));

    [Fact]
    public void Counts_only_ids_within_the_engine_limit_and_lists_overflow()
    {
        WriteSplState(Enumerable.Range(0, 256).Concat([256, 257]));
        var u = ResourceLimits.Measure(_root).Single(x => x.Name == ResourceLimits.SplState);
        Assert.Equal(256, u.Used);
        Assert.Equal([256, 257], u.OverLimitIds);
        Assert.True(u.IsOver);
    }

    [Fact]
    public void Describe_reports_delta_near_limit_and_overflow()
    {
        WriteSplState(Enumerable.Range(0, 230));
        var before = ResourceLimits.Measure(_root);
        WriteSplState(Enumerable.Range(0, 250));
        var after = ResourceLimits.Measure(_root);
        var line = Assert.Single(ResourceLimits.Describe(before, after, "MOD/X.TP2"));
        Assert.Contains("+20 by MOD/X.TP2", line);
        Assert.Contains("near the limit", line);

        WriteSplState(Enumerable.Range(0, 256).Concat([256, 260]));
        var over = ResourceLimits.Measure(_root);
        var overLine = Assert.Single(ResourceLimits.Describe(after, over, "MOD/Y.TP2"));
        Assert.StartsWith("!!! SPLSTATE.IDS OVERFLOW", overLine);
        Assert.Contains("256..260", overLine);
    }

    [Fact]
    public void Ledger_round_trips_and_replaces_same_entry()
    {
        var ledger = Path.Combine(_root, "ledger.json");
        ResourceLimits.AppendLedger(ledger, new LimitLedgerEntry("A/A.TP2", [0, 1], DateTimeOffset.UtcNow, new() { ["SPLSTATE.IDS"] = 5 }));
        ResourceLimits.AppendLedger(ledger, new LimitLedgerEntry("A/A.TP2", [0, 1], DateTimeOffset.UtcNow, new() { ["SPLSTATE.IDS"] = 7 }));
        var entries = ResourceLimits.LoadLedger(ledger);
        Assert.Single(entries);
        Assert.Equal(7, entries[0].Deltas["SPLSTATE.IDS"]);
    }
}
