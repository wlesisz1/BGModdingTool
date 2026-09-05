using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Manages game instances: full, independent copies of a source game install.
/// The source directory is only ever read, never written.
/// </summary>
public sealed class InstanceService(AppPaths paths)
{
    public IReadOnlyList<GameInstance> List()
    {
        if (!Directory.Exists(paths.InstancesDir)) return [];
        var result = new List<GameInstance>();
        foreach (var dir in Directory.EnumerateDirectories(paths.InstancesDir))
        {
            var meta = JsonStore.Load<GameInstance>(Path.Combine(dir, "instance.json"));
            if (meta is not null) result.Add(meta);
        }
        return result.OrderBy(i => i.CreatedAt).ToList();
    }

    public async Task<GameInstance> CreateAsync(
        string name, string sourceGameDir, GameType gameType,
        IProgress<(long done, long total, string file)>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(Path.Combine(sourceGameDir, "chitin.key")))
            throw new InvalidOperationException($"'{sourceGameDir}' does not look like an Infinity Engine game (no chitin.key).");

        var id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var root = Path.Combine(paths.InstancesDir, id);
        var instance = new GameInstance
        {
            Id = id, Name = name, GameType = gameType,
            SourcePath = Path.GetFullPath(sourceGameDir),
            RootPath = root,
        };

        Directory.CreateDirectory(root);
        try
        {
            await Task.Run(() => CopyTree(sourceGameDir, instance.GamePath, progress, ct), ct);
            Directory.CreateDirectory(instance.SnapshotsPath);
            JsonStore.Save(Path.Combine(root, "instance.json"), instance);
            return instance;
        }
        catch
        {
            // Failed/cancelled creation must not leave a half-instance behind.
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
            throw;
        }
    }

    public void Delete(GameInstance instance)
    {
        // Refuse to delete anything outside our own instances dir.
        var full = Path.GetFullPath(instance.RootPath);
        if (!full.StartsWith(Path.GetFullPath(paths.InstancesDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to delete '{full}': not inside the instances directory.");
        Directory.Delete(full, recursive: true);
    }

    public void SaveMetadata(GameInstance instance) =>
        JsonStore.Save(Path.Combine(instance.RootPath, "instance.json"), instance);

    private static void CopyTree(string source, string target,
        IProgress<(long, long, string)>? progress, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList();
        long total = files.Count, done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: false);
            progress?.Report((++done, total, rel));
        }
    }
}
