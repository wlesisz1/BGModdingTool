using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>One archived version of a build.</summary>
public sealed record BuildVersion(string File, DateTime SavedAt, int EntryCount);

/// <summary>Persists build definitions as JSON files under builds/, with per-build version history.</summary>
public sealed class BuildStore(AppPaths paths)
{
    public IReadOnlyList<BuildDefinition> List()
    {
        if (!Directory.Exists(paths.BuildsDir)) return [];
        return Directory.EnumerateFiles(paths.BuildsDir, "*.json")
            .Select(f => JsonStore.Load<BuildDefinition>(f))
            .Where(b => b is not null).Cast<BuildDefinition>()
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Versions kept per build in builds/history/&lt;name&gt;/ (oldest pruned).</summary>
    public const int HistoryLimit = 40;

    /// <summary>
    /// Saves the build. The previous on-disk version is archived first, so
    /// every edit (including auto-saves and generator runs) can be undone.
    /// </summary>
    public void Save(BuildDefinition build)
    {
        ArchiveCurrent(build.Name);
        build.UpdatedAt = DateTimeOffset.UtcNow;
        JsonStore.Save(PathFor(build.Name), build);
    }

    public string HistoryDir(string name) => Path.Combine(paths.BuildsDir, "history", SafeFileName(name));

    private void ArchiveCurrent(string name)
    {
        var current = PathFor(name);
        if (!File.Exists(current)) return;
        var dir = HistoryDir(name);
        Directory.CreateDirectory(dir);
        var content = File.ReadAllText(current);
        // Skip when nothing changed since the last archived version.
        var last = Directory.EnumerateFiles(dir, "*.json").OrderByDescending(f => f).FirstOrDefault();
        if (last is not null && File.ReadAllText(last) == content) return;
        File.WriteAllText(Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"), content);
        foreach (var old in Directory.EnumerateFiles(dir, "*.json").OrderByDescending(f => f).Skip(HistoryLimit))
            File.Delete(old);
    }

    /// <summary>Archived versions of a build, newest first.</summary>
    public IReadOnlyList<BuildVersion> ListHistory(string name)
    {
        var dir = HistoryDir(name);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.json")
            .OrderByDescending(f => f)
            .Select(f =>
            {
                var b = JsonStore.Load<BuildDefinition>(f);
                return new BuildVersion(f, File.GetLastWriteTime(f), b?.Entries.Count ?? 0);
            })
            .ToList();
    }

    public BuildDefinition? LoadVersion(string file) => JsonStore.Load<BuildDefinition>(file);

    /// <summary>Human-readable differences between two builds (entries added/removed, components changed).</summary>
    public static List<string> Diff(BuildDefinition oldBuild, BuildDefinition newBuild)
    {
        static string Key(BuildEntry e) => e.IncludeBuild is not null
            ? "include:" + e.IncludeBuild.ToLowerInvariant()
            : InstallOrderService.NormalizeKey(e.Tp2) + "|" + string.Join(',', e.Components.Take(1));
        var oldByKey = oldBuild.Entries.ToDictionary(Key, e => e);
        var newByKey = newBuild.Entries.ToDictionary(Key, e => e);
        var lines = new List<string>();
        foreach (var (k, e) in oldByKey.Where(kv => !newByKey.ContainsKey(kv.Key)))
            lines.Add($"- removed: {e.Tp2 ?? e.IncludeBuild}");
        foreach (var (k, e) in newByKey.Where(kv => !oldByKey.ContainsKey(kv.Key)))
            lines.Add($"+ added: {e.Tp2 ?? e.IncludeBuild}");
        foreach (var (k, o) in oldByKey)
        {
            if (!newByKey.TryGetValue(k, out var n) || o.IncludeBuild is not null) continue;
            var removed = o.Components.Except(n.Components).ToList();
            var added = n.Components.Except(o.Components).ToList();
            if (removed.Count > 0 || added.Count > 0)
                lines.Add($"~ {o.Tp2}: components " +
                          (removed.Count > 0 ? $"-[{string.Join(' ', removed)}] " : "") +
                          (added.Count > 0 ? $"+[{string.Join(' ', added)}]" : ""));
            if (o.LanguageIndex != n.LanguageIndex) lines.Add($"~ {o.Tp2}: language {o.LanguageIndex} → {n.LanguageIndex}");
        }
        return lines;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    public void Delete(string name) { var p = PathFor(name); if (File.Exists(p)) File.Delete(p); }

    public BuildDefinition? Load(string name) => JsonStore.Load<BuildDefinition>(PathFor(name));

    /// <summary>Creates a build from an existing weidu.log (import of a current installation).</summary>
    public static BuildDefinition FromWeiduLog(string name, GameType gameType, string weiduLogContent)
    {
        var entries = WeiduLogParser.ToBuildEntries(WeiduLogParser.Parse(weiduLogContent));
        return new BuildDefinition { Name = name, GameType = gameType, Entries = entries };
    }

    /// <summary>
    /// Renames a build. Other builds that include it by name are updated too,
    /// so packages don't silently break.
    /// </summary>
    public void Rename(string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) throw new ArgumentException("The name cannot be empty.");
        if (newName.Equals(oldName, StringComparison.Ordinal)) return;
        var build = Load(oldName) ?? throw new InvalidOperationException($"Build \"{oldName}\" does not exist.");
        if (Load(newName) is not null && !newName.Equals(oldName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Build \"{newName}\" already exists.");

        build.Name = newName;
        Save(build);
        if (!newName.Equals(oldName, StringComparison.OrdinalIgnoreCase)) Delete(oldName);

        foreach (var other in List())
        {
            if (other.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)) continue;
            var changed = false;
            foreach (var e in other.Entries.Where(e =>
                string.Equals(e.IncludeBuild, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                e.IncludeBuild = newName;
                changed = true;
            }
            if (changed) Save(other);
        }
    }

    /// <summary>Copies a build under a fresh "(kopia)" name and returns the new name.</summary>
    public string Duplicate(string name)
    {
        var build = Load(name) ?? throw new InvalidOperationException($"Build \"{name}\" does not exist.");
        var copyName = name + " (copy)";
        var n = 2;
        while (Load(copyName) is not null) copyName = $"{name} (copy {n++})";
        build.Name = copyName;
        Save(build);
        return copyName;
    }

    /// <summary>
    /// Flattens a build: include-entries are replaced by the referenced build's
    /// entries (recursively, cycle-safe). Problems land in <paramref name="warnings"/>.
    /// </summary>
    public List<BuildEntry> ExpandEntries(BuildDefinition build, List<string> warnings, HashSet<string>? visiting = null)
    {
        visiting ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!visiting.Add(build.Name))
        {
            warnings.Add($"Build include cycle at \"{build.Name}\" — skipping the nested include.");
            return [];
        }
        var result = new List<BuildEntry>();
        foreach (var entry in build.Entries)
        {
            if (entry.IncludeBuild is null) { result.Add(entry); continue; }
            var sub = Load(entry.IncludeBuild);
            if (sub is null)
            {
                warnings.Add($"Build \"{entry.IncludeBuild}\" (included in \"{build.Name}\") does not exist — skipping.");
                continue;
            }
            result.AddRange(ExpandEntries(sub, warnings, visiting));
        }
        visiting.Remove(build.Name);
        return result;
    }

    private string PathFor(string name) => Path.Combine(paths.BuildsDir, SafeFileName(name) + ".json");
}
