using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class SnapshotServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-test-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly GameInstance _instance;
    private readonly SnapshotService _snapshots = new();

    public SnapshotServiceTests()
    {
        _instance = new GameInstance
        {
            Id = "test", Name = "test", GameType = GameType.BG2EE,
            SourcePath = "n/a", RootPath = _root,
        };
        Directory.CreateDirectory(_instance.GamePath);
        Directory.CreateDirectory(_instance.SnapshotsPath);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void WriteGameFile(string rel, string content)
    {
        var p = Path.Combine(_instance.GamePath, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
    }

    [Fact]
    public async Task Snapshot_and_restore_roundtrip_removes_new_files_and_restores_modified_ones()
    {
        WriteGameFile("chitin.key", "original-key");
        WriteGameFile("override/spell.spl", "original-spell");

        var snap = await _snapshots.TakeAsync(_instance, "pre");

        WriteGameFile("chitin.key", "MODIFIED");
        WriteGameFile("override/newfile.itm", "added-by-mod");
        File.Delete(Path.Combine(_instance.GamePath, "override/spell.spl"));

        await _snapshots.RestoreAsync(_instance, snap);

        Assert.Equal("original-key", File.ReadAllText(Path.Combine(_instance.GamePath, "chitin.key")));
        Assert.Equal("original-spell", File.ReadAllText(Path.Combine(_instance.GamePath, "override/spell.spl")));
        Assert.False(File.Exists(Path.Combine(_instance.GamePath, "override/newfile.itm")));
    }

    [Fact]
    public async Task Identical_content_is_stored_once()
    {
        WriteGameFile("a.txt", "same-content");
        WriteGameFile("b.txt", "same-content");

        await _snapshots.TakeAsync(_instance, "s1");

        var objects = Directory.EnumerateFiles(
            Path.Combine(_instance.SnapshotsPath, "objects"), "*", SearchOption.AllDirectories).ToList();
        Assert.Single(objects);
    }

    [Fact]
    public async Task Deleting_last_snapshot_prunes_objects()
    {
        WriteGameFile("a.txt", "data");
        var snap = await _snapshots.TakeAsync(_instance, "s1");

        _snapshots.Delete(_instance, snap);

        Assert.Empty(_snapshots.List(_instance));
        var objectsDir = Path.Combine(_instance.SnapshotsPath, "objects");
        Assert.True(!Directory.Exists(objectsDir) ||
                    !Directory.EnumerateFiles(objectsDir, "*", SearchOption.AllDirectories).Any());
    }
}
