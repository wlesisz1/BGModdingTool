using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class GameCompatibilityValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-lcc-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly LccDatabase _lcc;

    public GameCompatibilityValidatorTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        File.WriteAllText(Path.Combine(_root, "lcc-mods.json"), """
            [
              {"id": 1, "name": "Old BG2 Mod", "games": ["BG2EE"], "safe": 2, "tp2": "oldmod",
               "urls": [], "status": [], "compatibilities": {}},
              {"id": 2, "name": "Broken Mod", "games": ["EET"], "safe": 0, "tp2": "brokenmod",
               "urls": [], "status": [], "compatibilities": {"conflicts": [3]}},
              {"id": 3, "name": "Rival Mod", "games": ["EET"], "safe": 2, "tp2": "rivalmod",
               "urls": [], "status": [], "compatibilities": {"conflicts": [2]}}
            ]
            """);
        _lcc = new LccDatabase(paths);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static BuildEntry Entry(string tp2) => new() { Tp2 = tp2 };
    private static readonly Dictionary<string, ModMetadata> NoMeta = [];

    [Fact]
    public void Warns_when_mod_does_not_support_target_game()
    {
        var issues = GameCompatibilityValidator.Validate(
            [Entry("OLDMOD/SETUP-OLDMOD.TP2")], GameType.EET, NoMeta, _lcc);

        Assert.Contains(issues, i => i.Contains("oldmod") && i.Contains("does not declare"));
    }

    [Fact]
    public void No_warning_when_game_is_supported_or_no_data()
    {
        var issues = GameCompatibilityValidator.Validate(
            [Entry("RIVALMOD/RIVALMOD.TP2"), Entry("TOTALLY-UNKNOWN.TP2")], GameType.EET, NoMeta, _lcc);

        Assert.DoesNotContain(issues, i => i.Contains("unknown"));
        Assert.DoesNotContain(issues, i => i.Contains("rivalmod") && i.Contains("does not declare"));
    }

    [Fact]
    public void Warns_about_safe_zero_and_conflicts()
    {
        var issues = GameCompatibilityValidator.Validate(
            [Entry("BROKENMOD.TP2"), Entry("RIVALMOD.TP2")], GameType.EET, NoMeta, _lcc);

        Assert.Contains(issues, i => i.Contains("problematic"));
        Assert.Contains(issues, i => i.Contains("Conflict"));
        Assert.Single(issues, i => i.Contains("Conflict")); // reported once, not per side
    }

    [Fact]
    public void ResolveRefs_replaces_ids_with_names_and_collects_links()
    {
        var (text, refs) = _lcc.ResolveRefs("It must be installed before [[1]] and [[999]].");

        Assert.Equal("It must be installed before \"Old BG2 Mod\" and \"mod #999\".", text);
        Assert.Equal(2, refs.Count);
        Assert.Equal("https://riwspy.github.io/lcc-docs/en/#m1", refs[0].Url);
        Assert.Equal("Old BG2 Mod", refs[0].Name);
    }

    [Fact]
    public void Pi_metadata_alone_can_approve_target_game()
    {
        var meta = new Dictionary<string, ModMetadata>
        {
            ["oldmod"] = new ModMetadata { Games = ["EET"] },
        };
        var issues = GameCompatibilityValidator.Validate(
            [Entry("OLDMOD.TP2")], GameType.EET, meta, _lcc);

        Assert.DoesNotContain(issues, i => i.Contains("does not declare"));
    }
}
