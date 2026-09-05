using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class BuildStoreRenameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-rename-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly BuildStore _store;

    public BuildStoreRenameTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new BuildStore(paths);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Rename_moves_build_and_updates_includes_in_other_builds()
    {
        _store.Save(new BuildDefinition { Name = "questy", Entries = [new BuildEntry { Tp2 = "A" }] });
        _store.Save(new BuildDefinition
        {
            Name = "master",
            Entries = [new BuildEntry { Tp2 = "", IncludeBuild = "questy" }],
        });

        _store.Rename("questy", "questy-v2");

        Assert.Null(_store.Load("questy"));
        Assert.NotNull(_store.Load("questy-v2"));
        Assert.Equal("questy-v2", _store.Load("master")!.Entries[0].IncludeBuild);
    }

    [Fact]
    public void Rename_to_existing_name_throws()
    {
        _store.Save(new BuildDefinition { Name = "a" });
        _store.Save(new BuildDefinition { Name = "b" });
        Assert.Throws<InvalidOperationException>(() => _store.Rename("a", "b"));
    }

    [Fact]
    public void Duplicate_creates_numbered_copies()
    {
        _store.Save(new BuildDefinition { Name = "x", Entries = [new BuildEntry { Tp2 = "A" }] });

        Assert.Equal("x (copy)", _store.Duplicate("x"));
        Assert.Equal("x (copy 2)", _store.Duplicate("x"));
        Assert.Single(_store.Load("x (copy)")!.Entries);
    }
}
