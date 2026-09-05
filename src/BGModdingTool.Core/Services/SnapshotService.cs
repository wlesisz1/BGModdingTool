using System.Security.Cryptography;
using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

public sealed class SnapshotManifest
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Creation time in the user's time zone (the id and CreatedAt are UTC).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime CreatedAtLocal => CreatedAt.ToLocalTime().DateTime;
    /// <summary>relative path -> SHA-256 hex of content.</summary>
    public Dictionary<string, string> Files { get; init; } = [];
}

/// <summary>
/// Content-addressed snapshot store per instance:
///   snapshots/objects/ab/&lt;sha256&gt;   — unique file contents (deduplicated)
///   snapshots/&lt;id&gt;.manifest.json   — path -> hash map
///   snapshots/hashcache.json          — (path,size,mtime) -> hash, avoids re-hashing
/// Restore materializes a manifest back into game/ exactly (extra files removed).
/// </summary>
public sealed class SnapshotService
{
    private sealed class HashCacheEntry { public long Size { get; set; } public long MTimeTicks { get; set; } public string Hash { get; set; } = ""; }

    /// <summary>
    /// Snapshot headers (id, label, time) without the file map. Manifests of a big
    /// game hold tens of thousands of entries each; listing them fully froze the UI.
    /// </summary>
    public IReadOnlyList<SnapshotManifest> ListHeaders(GameInstance instance)
    {
        if (!Directory.Exists(instance.SnapshotsPath)) return [];
        var result = new List<SnapshotManifest>();
        foreach (var file in Directory.EnumerateFiles(instance.SnapshotsPath, "*.manifest.json"))
        {
            var header = ReadHeader(file);
            if (header is not null) result.Add(header);
        }
        return result.OrderBy(m => m.CreatedAt).ToList();
    }

    /// <summary>Full manifest (with the file map) for a snapshot id.</summary>
    public SnapshotManifest? Load(GameInstance instance, string id) =>
        JsonStore.Load<SnapshotManifest>(ManifestPath(instance, id));

    private static SnapshotManifest? ReadHeader(string path)
    {
        try
        {
            // The header properties are serialized before "files"; read only the start of the file.
            using var fs = File.OpenRead(path);
            var buffer = new byte[Math.Min(fs.Length, 64 * 1024)];
            var read = fs.Read(buffer, 0, buffer.Length);
            string? id = null, label = null; DateTimeOffset? created = null;
            var reader = new System.Text.Json.Utf8JsonReader(buffer.AsSpan(0, read), isFinalBlock: false, state: default);
            string? prop = null;
            while (reader.Read())
            {
                if (reader.CurrentDepth == 1 && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
                {
                    prop = reader.GetString();
                    if (string.Equals(prop, "files", StringComparison.OrdinalIgnoreCase)) break;
                    continue;
                }
                if (reader.CurrentDepth != 1 || prop is null) continue;
                switch (prop.ToLowerInvariant())
                {
                    case "id": id = reader.GetString(); break;
                    case "label": label = reader.GetString(); break;
                    case "createdat": created = reader.GetDateTimeOffset(); break;
                }
                prop = null;
                if (id is not null && label is not null && created is not null) break;
            }
            if (id is null) return null;
            return new SnapshotManifest { Id = id, Label = label ?? id, CreatedAt = created ?? DateTimeOffset.MinValue };
        }
        catch { return null; }
    }

    public IReadOnlyList<SnapshotManifest> List(GameInstance instance)
    {
        if (!Directory.Exists(instance.SnapshotsPath)) return [];
        return Directory.EnumerateFiles(instance.SnapshotsPath, "*.manifest.json")
            .Select(f => JsonStore.Load<SnapshotManifest>(f))
            .Where(m => m is not null).Cast<SnapshotManifest>()
            .OrderBy(m => m.CreatedAt).ToList();
    }

    public async Task<SnapshotManifest> TakeAsync(GameInstance instance, string label,
        IProgress<(long done, long total, string file)>? progress = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var objectsDir = Path.Combine(instance.SnapshotsPath, "objects");
            Directory.CreateDirectory(objectsDir);
            var cachePath = Path.Combine(instance.SnapshotsPath, "hashcache.json");
            var cache = JsonStore.Load<Dictionary<string, HashCacheEntry>>(cachePath) ?? [];

            var manifest = new SnapshotManifest
            {
                Id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                Label = label,
            };

            var files = Directory.EnumerateFiles(instance.GamePath, "*", SearchOption.AllDirectories).ToList();
            long done = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(instance.GamePath, file);
                var info = new FileInfo(file);

                string hash;
                if (cache.TryGetValue(rel, out var c) && c.Size == info.Length && c.MTimeTicks == info.LastWriteTimeUtc.Ticks)
                {
                    hash = c.Hash;
                }
                else
                {
                    using var stream = File.OpenRead(file);
                    hash = Convert.ToHexStringLower(SHA256.HashData(stream));
                    cache[rel] = new HashCacheEntry { Size = info.Length, MTimeTicks = info.LastWriteTimeUtc.Ticks, Hash = hash };
                }

                var objPath = ObjectPath(objectsDir, hash);
                if (!File.Exists(objPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(objPath)!);
                    File.Copy(file, objPath + ".tmp", overwrite: true);
                    File.Move(objPath + ".tmp", objPath, overwrite: true);
                }

                manifest.Files[rel] = hash;
                progress?.Report((++done, files.Count, rel));
            }

            JsonStore.Save(cachePath, cache);
            JsonStore.Save(ManifestPath(instance, manifest.Id), manifest);
            SetCurrent(instance, manifest);
            return manifest;
        }, ct);
    }

    /// <summary>Records which snapshot the game folder now equals (shown as the instance's state).</summary>
    public static void SetCurrent(GameInstance instance, SnapshotManifest manifest)
    {
        instance.CurrentSnapshotId = manifest.Id;
        instance.CurrentSnapshotLabel = manifest.Label;
        instance.CurrentSnapshotAt = manifest.CreatedAt;
        instance.ModifiedSinceSnapshot = false;
        JsonStore.Save(Path.Combine(instance.RootPath, "instance.json"), instance);
    }

    /// <summary>Marks the folder as changed since its snapshot (an installer is about to write).</summary>
    public static void MarkModified(GameInstance instance)
    {
        if (instance.ModifiedSinceSnapshot) return;
        instance.ModifiedSinceSnapshot = true;
        JsonStore.Save(Path.Combine(instance.RootPath, "instance.json"), instance);
    }

    public async Task RestoreAsync(GameInstance instance, SnapshotManifest manifest,
        IProgress<(long done, long total, string file)>? progress = null, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            var objectsDir = Path.Combine(instance.SnapshotsPath, "objects");
            long done = 0, total = manifest.Files.Count;

            // 1. Write/overwrite every file from the manifest.
            foreach (var (rel, hash) in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                var dest = Path.Combine(instance.GamePath, rel);
                var obj = ObjectPath(objectsDir, hash);
                if (!File.Exists(obj))
                    throw new InvalidOperationException($"Snapshot object missing for '{rel}' ({hash}). Store is corrupted.");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(obj, dest + ".restoretmp", overwrite: true);
                File.Move(dest + ".restoretmp", dest, overwrite: true);
                progress?.Report((++done, total, rel));
            }

            // 2. Remove files that did not exist at snapshot time.
            var wanted = new HashSet<string>(manifest.Files.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(instance.GamePath, "*", SearchOption.AllDirectories).ToList())
            {
                var rel = Path.GetRelativePath(instance.GamePath, file);
                if (!wanted.Contains(rel)) File.Delete(file);
            }
            RemoveEmptyDirs(instance.GamePath);

            // Hash cache mtimes are now stale; drop it so the next snapshot re-verifies.
            var cachePath = Path.Combine(instance.SnapshotsPath, "hashcache.json");
            if (File.Exists(cachePath)) File.Delete(cachePath);
            SetCurrent(instance, manifest);
        }, ct);
    }

    public void Delete(GameInstance instance, SnapshotManifest manifest)
    {
        File.Delete(ManifestPath(instance, manifest.Id));
        PruneUnreferencedObjects(instance);
    }

    /// <summary>Deletes blobs no manifest references any more.</summary>
    public void PruneUnreferencedObjects(GameInstance instance)
    {
        var objectsDir = Path.Combine(instance.SnapshotsPath, "objects");
        if (!Directory.Exists(objectsDir)) return;
        var referenced = List(instance).SelectMany(m => m.Files.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in Directory.EnumerateFiles(objectsDir, "*", SearchOption.AllDirectories))
        {
            if (!referenced.Contains(Path.GetFileName(obj))) File.Delete(obj);
        }
        RemoveEmptyDirs(objectsDir);
    }

    private static string ManifestPath(GameInstance instance, string id) =>
        Path.Combine(instance.SnapshotsPath, id + ".manifest.json");

    private static string ObjectPath(string objectsDir, string hash) =>
        Path.Combine(objectsDir, hash[..2], hash);

    private static void RemoveEmptyDirs(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
    }
}
