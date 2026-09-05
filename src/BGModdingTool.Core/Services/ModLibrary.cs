using BGModdingTool.Core.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Central mod storage. Mods are imported from archives (zip/7z/rar/tar) or
/// directories, extracted under mods/&lt;id&gt;/, and cataloged by discovering
/// their .tp2 file and optional Project Infinity mod.ini metadata.
/// </summary>
public sealed class ModLibrary(AppPaths paths)
{
    public IReadOnlyList<ModPackage> List()
    {
        if (!Directory.Exists(paths.ModsDir)) return [];
        var result = new List<ModPackage>();
        foreach (var dir in Directory.EnumerateDirectories(paths.ModsDir))
        {
            var meta = JsonStore.Load<ModPackage>(Path.Combine(dir, "modpackage.json"));
            if (meta is null) continue;
            // Migration for packages cataloged before multi-tp2 support.
            if (meta.Tp2Variants.Count == 0)
            {
                meta.Tp2Variants = DiscoverTp2s(meta.RootPath, meta.Tp2RelativePath);
                JsonStore.Save(Path.Combine(dir, "modpackage.json"), meta);
            }
            result.Add(meta);
        }
        return result.OrderBy(m => m.Metadata.Name ?? m.ModFolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<ModPackage> ImportArchiveAsync(string archivePath, CancellationToken ct = default)
    {
        var staging = Path.Combine(paths.ModsDir, ".staging-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        try
        {
            await Task.Run(() => ArchiveFactory.WriteToDirectory(archivePath, staging,
                new ExtractionOptions { ExtractFullPath = true, Overwrite = true }), ct);
            return FinishImport(staging, Path.GetFileNameWithoutExtension(archivePath));
        }
        catch
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
            throw;
        }
    }

    public async Task<ModPackage> ImportDirectoryAsync(string sourceDir, CancellationToken ct = default)
    {
        var staging = Path.Combine(paths.ModsDir, ".staging-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await Task.Run(() =>
            {
                foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var dest = Path.Combine(staging, Path.GetRelativePath(sourceDir, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, overwrite: true);
                }
            }, ct);
            return FinishImport(staging, Path.GetFileName(sourceDir.TrimEnd('\\', '/')));
        }
        catch
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
            throw;
        }
    }

    public void Delete(ModPackage package)
    {
        var full = Path.GetFullPath(package.RootPath);
        if (!full.StartsWith(Path.GetFullPath(paths.ModsDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to delete '{full}': not inside the mod library.");
        Directory.Delete(full, recursive: true);
    }

    /// <summary>
    /// Copies the mod's folder (and its tp2, if it sits outside the folder)
    /// into an instance game directory, ready for WeiDU.
    /// </summary>
    public void DeployToGame(ModPackage package, string gameDir)
    {
        // Copy the primary mod folder plus the top folder of every tp2 variant
        // (multi-installer mods can keep them in separate directories).
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { package.ModFolderName };
        foreach (var tp2 in package.AllTp2s())
        {
            var top = tp2.Replace('\\', '/').Split('/');
            if (top.Length > 1) folders.Add(top[0]);
        }
        foreach (var folder in folders)
        {
            var src = Path.Combine(package.RootPath, folder);
            if (Directory.Exists(src)) CopyTree(src, Path.Combine(gameDir, folder));
        }

        // Top-level tp2 / setup-*.exe variants (older mods keep tp2 next to the folder).
        foreach (var file in Directory.EnumerateFiles(package.RootPath))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".tp2", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".tph", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, Path.Combine(gameDir, name), overwrite: true);
            }
        }
    }

    /// <summary>
    /// Best local readme of a package: prefers English, then html &gt; md &gt; txt &gt; pdf,
    /// then shallow paths. Null when the package ships no readme.
    /// </summary>
    public static string? FindReadme(ModPackage package)
    {
        if (!Directory.Exists(package.RootPath)) return null;
        string[] exts = [".html", ".htm", ".md", ".txt", ".pdf"];
        var candidates = Directory.EnumerateFiles(package.RootPath, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return exts.Contains(Path.GetExtension(f).ToLowerInvariant())
                       && (name.Contains("readme", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("read me", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("lisez", StringComparison.OrdinalIgnoreCase)
                           || name.Equals("README.md", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
        if (candidates.Count == 0) return null;

        int LangScore(string f)
        {
            var p = f.ToLowerInvariant();
            if (p.Contains("english") || p.Contains("en_us") || p.Contains("-en.") || p.Contains("_en.") || p.Contains("\\en\\") || p.Contains("/en/")) return 0;
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"(german|deutsch|french|francais|polish|polski|russian|italian|spanish|espanol|czech|chinese|schinese|de_de|fr_fr|pl_pl|ru_ru|it_it|es_es|cs_cz|zh_cn|_de\.|_fr\.|_pl\.|_ru\.|_it\.|_es\.|_cn\.)")) return 2;
            return 1;
        }
        int ExtScore(string f) => Array.IndexOf(exts, Path.GetExtension(f).ToLowerInvariant());
        int Depth(string f) => Path.GetRelativePath(package.RootPath, f).Count(c => c is '\\' or '/');

        return candidates
            .OrderBy(LangScore).ThenBy(ExtScore).ThenBy(Depth).ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>Finds a weidu binary shipped with any library mod (setup-*.exe is weidu).</summary>
    public string? FindBundledWeidu()
    {
        foreach (var pkg in List())
        {
            var exe = Directory.EnumerateFiles(pkg.RootPath, "setup-*.exe", SearchOption.AllDirectories).FirstOrDefault()
                   ?? Directory.EnumerateFiles(pkg.RootPath, "weidu.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is not null) return exe;
        }
        return null;
    }

    private ModPackage FinishImport(string staging, string fallbackName)
    {
        // Locate the tp2. Prefer setup-*.tp2, prefer shallow paths.
        var tp2 = Directory.EnumerateFiles(staging, "*.tp2", SearchOption.AllDirectories)
            .OrderBy(p => p.Count(c => c is '\\' or '/'))
            .ThenByDescending(p => Path.GetFileName(p).StartsWith("setup-", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No .tp2 file found — this does not look like a WeiDU mod.");

        // The mod folder is the tp2's directory, unless the tp2 sits at package
        // root (old-style layout) — then it's the folder named after the mod.
        var tp2Dir = Path.GetDirectoryName(tp2)!;
        string modFolderName;
        string packageRootSrc;
        if (Path.GetFullPath(tp2Dir).Equals(Path.GetFullPath(staging), StringComparison.OrdinalIgnoreCase))
        {
            var baseName = Path.GetFileNameWithoutExtension(tp2);
            if (baseName.StartsWith("setup-", StringComparison.OrdinalIgnoreCase)) baseName = baseName[6..];
            modFolderName = Directory.EnumerateDirectories(staging)
                .Select(Path.GetFileName)
                .FirstOrDefault(d => string.Equals(d, baseName, StringComparison.OrdinalIgnoreCase))
                ?? baseName;
            packageRootSrc = staging;
        }
        else
        {
            modFolderName = Path.GetFileName(tp2Dir);
            packageRootSrc = Path.GetDirectoryName(tp2Dir)!;
        }

        var id = SanitizeId(modFolderName) + "-" + Guid.NewGuid().ToString("N")[..6];
        var finalRoot = Path.Combine(paths.ModsDir, id);

        if (Path.GetFullPath(packageRootSrc).Equals(Path.GetFullPath(staging), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(staging, finalRoot);
        }
        else
        {
            // Archive had wrapper directories — lift the real package root out.
            Directory.Move(packageRootSrc, finalRoot);
            try { Directory.Delete(staging, recursive: true); } catch { }
        }

        var tp2Rel = Path.GetRelativePath(finalRoot, Path.Combine(finalRoot,
            Path.GetRelativePath(packageRootSrc, tp2)));

        var metadata = ReadMetadata(finalRoot, modFolderName);
        var package = new ModPackage
        {
            Id = id,
            RootPath = finalRoot,
            Tp2RelativePath = tp2Rel,
            Tp2Variants = DiscoverTp2s(finalRoot, tp2Rel),
            ModFolderName = modFolderName,
            Metadata = metadata,
        };
        if (string.IsNullOrEmpty(package.Metadata.Name))
            package.Metadata.Name = fallbackName;

        JsonStore.Save(Path.Combine(finalRoot, "modpackage.json"), package);
        return package;
    }

    /// <summary>All tp2 files in the package, primary first, then by depth/name.</summary>
    private static List<string> DiscoverTp2s(string root, string primaryRel)
    {
        if (!Directory.Exists(root)) return [primaryRel];
        var all = Directory.EnumerateFiles(root, "*.tp2", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p))
            .OrderBy(p => !p.Equals(primaryRel, StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p.Count(c => c is '\\' or '/'))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return all.Count > 0 ? all : [primaryRel];
    }

    private static ModMetadata ReadMetadata(string root, string modFolderName)
    {
        // Project Infinity convention: <modfolder>/<tp2-basename>.ini or mod.ini
        var candidates = new[]
        {
            Path.Combine(root, modFolderName, modFolderName + ".ini"),
            Path.Combine(root, modFolderName, "setup-" + modFolderName + ".ini"),
            Path.Combine(root, modFolderName, "mod.ini"),
        };
        foreach (var ini in candidates)
        {
            if (File.Exists(ini))
            {
                try { return ProjectInfinityIni.Parse(File.ReadAllText(ini)); }
                catch { /* best-effort metadata */ }
            }
        }
        return new ModMetadata();
    }

    private static string SanitizeId(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : char.ToLowerInvariant(c)).ToArray());
    }

    private static void CopyTree(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
