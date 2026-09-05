using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class OrderValidatorTests
{
    private static BuildEntry Entry(string tp2) => new() { Tp2 = tp2 };

    [Fact]
    public void Reports_violated_after_constraint()
    {
        var entries = new List<BuildEntry>
        {
            Entry("STRATAGEMS/SETUP-STRATAGEMS.TP2"),
            Entry("BG2FIXPACK/SETUP-BG2FIXPACK.TP2"),
        };
        var constraints = new Dictionary<string, ModMetadata>
        {
            ["stratagems"] = new ModMetadata { After = ["bg2fixpack"] },
        };

        var issues = OrderValidator.Validate(entries, constraints);

        Assert.Single(issues);
        Assert.Contains("stratagems", issues[0]);
    }

    [Fact]
    public void Correct_order_yields_no_issues()
    {
        var entries = new List<BuildEntry>
        {
            Entry("BG2FIXPACK/SETUP-BG2FIXPACK.TP2"),
            Entry("STRATAGEMS/SETUP-STRATAGEMS.TP2"),
        };
        var constraints = new Dictionary<string, ModMetadata>
        {
            ["stratagems"] = new ModMetadata { After = ["bg2fixpack"] },
        };

        Assert.Empty(OrderValidator.Validate(entries, constraints));
    }

    [Fact]
    public void Reports_must_be_last_recipe_violation()
    {
        var entries = new List<BuildEntry>
        {
            Entry("EET/EET_END.TP2"),
            Entry("CDTWEAKS/SETUP-CDTWEAKS.TP2"),
        };

        var issues = OrderValidator.Validate(entries, new Dictionary<string, ModMetadata>());

        Assert.Contains(issues, i => i.Contains("LAST"));
    }

    [Fact]
    public void User_rules_merge_with_package_metadata()
    {
        var packages = new List<ModPackage>
        {
            new()
            {
                Id = "x", RootPath = "n/a", Tp2RelativePath = "stratagems/setup-stratagems.tp2",
                ModFolderName = "stratagems",
                Metadata = new ModMetadata { After = ["bg2fixpack"] },
            },
        };
        var rules = new List<OrderRule>
        {
            new() { Mod = "setup-stratagems.tp2", Before = ["eet_end"] },
        };

        var map = OrderRuleStore.BuildConstraintMap(packages, rules);

        // Built-in community rules are merged too, so check containment, not equality.
        Assert.Contains("bg2fixpack", map["stratagems"].After);
        Assert.Contains("eet_end", map["stratagems"].Before);
    }
}

public class BuildStoreExpandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-builds-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly BuildStore _store;

    public BuildStoreExpandTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new BuildStore(paths);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Expands_included_builds_in_place()
    {
        _store.Save(new BuildDefinition
        {
            Name = "questy",
            Entries = [new BuildEntry { Tp2 = "A" }, new BuildEntry { Tp2 = "B" }],
        });
        var master = new BuildDefinition
        {
            Name = "master",
            Entries =
            [
                new BuildEntry { Tp2 = "X" },
                new BuildEntry { Tp2 = "", IncludeBuild = "questy" },
                new BuildEntry { Tp2 = "Y" },
            ],
        };

        var warnings = new List<string>();
        var flat = _store.ExpandEntries(master, warnings);

        Assert.Empty(warnings);
        Assert.Equal(["X", "A", "B", "Y"], flat.Select(e => e.Tp2));
    }

    [Fact]
    public void Missing_included_build_warns_and_is_skipped()
    {
        var master = new BuildDefinition
        {
            Name = "master",
            Entries = [new BuildEntry { Tp2 = "", IncludeBuild = "nie-ma" }],
        };

        var warnings = new List<string>();
        var flat = _store.ExpandEntries(master, warnings);

        Assert.Empty(flat);
        Assert.Single(warnings);
    }

    [Fact]
    public void Include_cycles_are_broken_with_warning()
    {
        _store.Save(new BuildDefinition
        {
            Name = "a",
            Entries = [new BuildEntry { Tp2 = "MOD-A" }, new BuildEntry { Tp2 = "", IncludeBuild = "b" }],
        });
        _store.Save(new BuildDefinition
        {
            Name = "b",
            Entries = [new BuildEntry { Tp2 = "MOD-B" }, new BuildEntry { Tp2 = "", IncludeBuild = "a" }],
        });

        var warnings = new List<string>();
        var flat = _store.ExpandEntries(_store.Load("a")!, warnings);

        Assert.Equal(["MOD-A", "MOD-B"], flat.Select(e => e.Tp2));
        Assert.Single(warnings);
    }
}
