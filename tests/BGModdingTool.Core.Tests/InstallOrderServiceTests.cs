using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class InstallOrderServiceTests
{
    private static BuildEntry Entry(string tp2) => new() { Tp2 = tp2 };

    [Fact]
    public void NormalizeKey_strips_path_setup_prefix_and_extension()
    {
        Assert.Equal("stratagems", InstallOrderService.NormalizeKey("STRATAGEMS/SETUP-STRATAGEMS.TP2"));
        Assert.Equal("stratagems", InstallOrderService.NormalizeKey("setup-stratagems.tp2"));
        Assert.Equal("stratagems", InstallOrderService.NormalizeKey("stratagems"));
    }

    [Fact]
    public void SortByMetadata_respects_after_constraint_and_is_stable()
    {
        // stratagems declares After: bg2fixpack, but comes first in the build.
        var entries = new List<BuildEntry>
        {
            Entry("STRATAGEMS/SETUP-STRATAGEMS.TP2"),
            Entry("CDTWEAKS/SETUP-CDTWEAKS.TP2"),
            Entry("BG2FIXPACK/SETUP-BG2FIXPACK.TP2"),
        };
        var meta = new Dictionary<string, ModMetadata>
        {
            ["stratagems"] = new ModMetadata { After = ["SETUP-BG2FIXPACK.TP2"] },
        };

        var result = InstallOrderService.SortByMetadata(entries, meta);

        Assert.False(result.HadCycle);
        var keys = result.Sorted.Select(e => InstallOrderService.NormalizeKey(e.Tp2)).ToList();
        Assert.True(keys.IndexOf("bg2fixpack") < keys.IndexOf("stratagems"));
        // cdtweaks is unconstrained: keeps its position relative to what's possible (stays before fixpack).
        Assert.Equal("cdtweaks", keys[0]);
    }

    [Fact]
    public void SortByMetadata_respects_before_constraint()
    {
        var entries = new List<BuildEntry> { Entry("b"), Entry("a") };
        var meta = new Dictionary<string, ModMetadata>
        {
            ["a"] = new ModMetadata { Before = ["b"] },
        };

        var result = InstallOrderService.SortByMetadata(entries, meta);

        Assert.Equal(["a", "b"], result.Sorted.Select(e => e.Tp2));
    }

    [Fact]
    public void SortByMetadata_reports_cycles_and_keeps_original_order_for_them()
    {
        var entries = new List<BuildEntry> { Entry("a"), Entry("b") };
        var meta = new Dictionary<string, ModMetadata>
        {
            ["a"] = new ModMetadata { After = ["b"] },
            ["b"] = new ModMetadata { After = ["a"] },
        };

        var result = InstallOrderService.SortByMetadata(entries, meta);

        Assert.True(result.HadCycle);
        Assert.Equal(["a", "b"], result.Sorted.Select(e => e.Tp2));
    }

    [Fact]
    public void SortByMetadata_with_tiers_moves_low_tier_entries_first()
    {
        var entries = new List<BuildEntry> { Entry("ui-mod"), Entry("fixpack-mod"), Entry("quest-mod") };
        var tiers = new Dictionary<string, int> { ["ui-mod"] = 90, ["fixpack-mod"] = 10, ["quest-mod"] = 30 };

        var result = InstallOrderService.SortByMetadata(
            entries, new Dictionary<string, Models.ModMetadata>(),
            tierOf: e => tiers[e.Tp2]);

        Assert.Equal(["fixpack-mod", "quest-mod", "ui-mod"], result.Sorted.Select(e => e.Tp2));
    }

    [Fact]
    public void SortByMasterList_orders_listed_mods_and_appends_unlisted()
    {
        var entries = new List<BuildEntry>
        {
            Entry("CDTWEAKS/SETUP-CDTWEAKS.TP2"),
            Entry("UNKNOWNMOD/SETUP-UNKNOWNMOD.TP2"),
            Entry("BG2FIXPACK/SETUP-BG2FIXPACK.TP2"),
        };
        const string masterList = """
            # community install order
            bg2fixpack
            stratagems
            cdtweaks
            """;

        var sorted = InstallOrderService.SortByMasterList(entries, masterList);

        Assert.Equal(
            ["bg2fixpack", "cdtweaks", "unknownmod"],
            sorted.Select(e => InstallOrderService.NormalizeKey(e.Tp2)));
    }
}
