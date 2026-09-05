using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class BuildHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-history-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly BuildStore _store;

    public BuildHistoryTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new BuildStore(paths);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Save_archives_previous_version_and_skips_identical_content()
    {
        var b = new BuildDefinition { Name = "x", Entries = [new BuildEntry { Tp2 = "A", Components = [0] }] };
        _store.Save(b);                                   // first save: nothing to archive
        Assert.Empty(_store.ListHistory("x"));

        b.Entries.Add(new BuildEntry { Tp2 = "B", Components = [0] });
        _store.Save(b);                                   // archives the 1-entry version
        var history = _store.ListHistory("x");
        Assert.Single(history);
        Assert.Equal(1, history[0].EntryCount);

        var restored = _store.LoadVersion(history[0].File)!;
        Assert.Equal(["A"], restored.Entries.Select(e => e.Tp2));
    }

    [Fact]
    public void Diff_reports_added_removed_and_changed_components()
    {
        var mine = new BuildDefinition
        {
            Name = "m",
            Entries =
            [
                new BuildEntry { Tp2 = "KEEP/KEEP.TP2", Components = [0, 1] },
                new BuildEntry { Tp2 = "MINE/MINE.TP2", Components = [0] },
            ],
        };
        var generated = new BuildDefinition
        {
            Name = "m",
            Entries =
            [
                new BuildEntry { Tp2 = "KEEP/KEEP.TP2", Components = [0, 2] },
                new BuildEntry { Tp2 = "NEW/NEW.TP2", Components = [0] },
            ],
        };

        var diff = BuildStore.Diff(mine, generated);

        Assert.Contains(diff, d => d.StartsWith("- removed: MINE"));
        Assert.Contains(diff, d => d.StartsWith("+ added: NEW"));
        Assert.Contains(diff, d => d.Contains("KEEP/KEEP.TP2: components -[1] +[2]"));
    }
}
