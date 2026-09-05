using BGModdingTool.Core.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Library of non-WeiDU asset packs (portraits, soundsets, music, loose
/// override files) and their deployment to game instances.
///
/// Deployment targets follow the engines' rules:
///  - EE games read player portraits and soundsets from the user's Documents
///    folder ("Baldur's Gate II - Enhanced Edition\portraits" etc.), NOT from
///    the game directory — this is the one place the tool writes outside an
///    instance directory. Every deployed file is recorded so it can be removed.
///  - Classic games read portraits/sounds from the game directory.
///  - Music and override files always go into the instance's game directory.
/// </summary>
public sealed class AssetLibrary(AppPaths paths)
{
    public string AssetsDir => Path.Combine(paths.DataRoot, "assets");

    public IReadOnlyList<AssetPackage> List()
    {
        if (!Directory.Exists(AssetsDir)) return [];
        return Directory.EnumerateDirectories(AssetsDir)
            .Select(d => JsonStore.Load<AssetPackage>(Path.Combine(d, "asset.json")))
            .Where(a => a is not null).Cast<AssetPackage>()
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<AssetPackage> ImportArchiveAsync(string archivePath, CancellationToken ct = default)
    {
        var id = SanitizeId(Path.GetFileNameWithoutExtension(archivePath)) + "-" + Guid.NewGuid().ToString("N")[..6];
        var root = Path.Combine(AssetsDir, id);
        Directory.CreateDirectory(root);
        try
        {
            await Task.Run(() => ArchiveFactory.WriteToDirectory(archivePath, root,
                new ExtractionOptions { ExtractFullPath = true, Overwrite = true }), ct);
            return Finish(id, root, Path.GetFileNameWithoutExtension(archivePath));
        }
        catch
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            throw;
        }
    }

    public async Task<AssetPackage> ImportDirectoryAsync(string sourceDir, CancellationToken ct = default)
    {
        var name = Path.GetFileName(sourceDir.TrimEnd('\\', '/'));
        var id = SanitizeId(name) + "-" + Guid.NewGuid().ToString("N")[..6];
        var root = Path.Combine(AssetsDir, id);
        try
        {
            await Task.Run(() =>
            {
                foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var dest = Path.Combine(root, Path.GetRelativePath(sourceDir, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, overwrite: true);
                }
            }, ct);
            return Finish(id, root, name);
        }
        catch
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            throw;
        }
    }

    public void Save(AssetPackage package) => JsonStore.Save(Path.Combine(package.RootPath, "asset.json"), package);

    public void Delete(AssetPackage package)
    {
        var full = Path.GetFullPath(package.RootPath);
        if (!full.StartsWith(Path.GetFullPath(AssetsDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to delete '{full}': not inside the asset library.");
        Directory.Delete(full, recursive: true);
    }

    // ---------- deployment ----------

    /// <summary>Documents sub-folder the EE engine uses for user data of a given game.</summary>
    public static string? EeDocumentsFolderName(GameType game) => game switch
    {
        GameType.BGEE => "Baldur's Gate - Enhanced Edition",
        GameType.BG2EE or GameType.EET => "Baldur's Gate II - Enhanced Edition",
        GameType.IWDEE => "Icewind Dale - Enhanced Edition",
        _ => null,
    };

    /// <summary>Resolves where a package of the given kind goes for an instance.</summary>
    public static string TargetDirectory(AssetPackage package, GameInstance instance)
    {
        var eeDocs = EeDocumentsFolderName(instance.GameType);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return package.Kind switch
        {
            AssetKind.Portraits => eeDocs is not null
                ? Path.Combine(documents, eeDocs, "portraits")
                : Path.Combine(instance.GamePath, "portraits"),
            AssetKind.Soundset => eeDocs is not null
                ? Path.Combine(documents, eeDocs, "sounds")
                : Path.Combine(instance.GamePath, "sounds"),
            AssetKind.Music => Path.Combine(instance.GamePath, "music"),
            _ => Path.Combine(instance.GamePath, "override"),
        };
    }

    /// <summary>True when the target lives outside the instance (EE Documents folder).</summary>
    public static bool TargetIsOutsideInstance(AssetPackage package, GameInstance instance) =>
        !Path.GetFullPath(TargetDirectory(package, instance))
            .StartsWith(Path.GetFullPath(instance.GamePath), StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<AssetDeployment> Deployments(AssetPackage package) =>
        JsonStore.Load<List<AssetDeployment>>(DeploymentsPath(package)) ?? [];

    /// <summary>
    /// Copies the package's relevant files (flattened — portrait/sound folders
    /// have no sub-structure) into the target and records every written path.
    /// </summary>
    public async Task<AssetDeployment> DeployAsync(AssetPackage package, GameInstance instance, CancellationToken ct = default)
    {
        var target = TargetDirectory(package, instance);
        var deployment = new AssetDeployment { InstanceId = instance.Id, TargetDirectory = target };
        var flatten = package.Kind is AssetKind.Portraits or AssetKind.Soundset;

        await Task.Run(() =>
        {
            Directory.CreateDirectory(target);
            foreach (var file in RelevantFiles(package))
            {
                ct.ThrowIfCancellationRequested();
                var rel = flatten ? Path.GetFileName(file) : Path.GetRelativePath(package.RootPath, file);
                var dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
                deployment.Files.Add(dest);
            }
        }, ct);

        var all = Deployments(package).Where(d => d.InstanceId != instance.Id).ToList();
        all.Add(deployment);
        JsonStore.Save(DeploymentsPath(package), all);
        return deployment;
    }

    /// <summary>Removes exactly the files a previous deployment wrote.</summary>
    public int Undeploy(AssetPackage package, GameInstance instance)
    {
        var all = Deployments(package).ToList();
        var mine = all.Where(d => d.InstanceId == instance.Id).ToList();
        var removed = 0;
        foreach (var d in mine)
        {
            foreach (var f in d.Files)
            {
                if (File.Exists(f)) { File.Delete(f); removed++; }
            }
        }
        JsonStore.Save(DeploymentsPath(package), all.Except(mine).ToList());
        return removed;
    }

    /// <summary>Files that matter for the package kind (asset.json / readmes are skipped).</summary>
    public IEnumerable<string> RelevantFiles(AssetPackage package)
    {
        var extensions = package.Kind switch
        {
            AssetKind.Portraits => new[] { ".bmp", ".png" },
            AssetKind.Soundset => new[] { ".wav", ".ogg" },
            AssetKind.Music => new[] { ".mus", ".acm", ".wav", ".ogg" },
            _ => null,
        };
        return Directory.EnumerateFiles(package.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("asset.json", StringComparison.OrdinalIgnoreCase)
                        && !Path.GetFileName(f).Equals("deployments.json", StringComparison.OrdinalIgnoreCase)
                        && (extensions is null || extensions.Contains(Path.GetExtension(f).ToLowerInvariant())));
    }

    // ---------- helpers ----------

    private AssetPackage Finish(string id, string root, string name)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
        var package = new AssetPackage
        {
            Id = id,
            Name = name,
            RootPath = root,
            Kind = DetectKind(files),
            FileCount = files.Count,
        };
        Save(package);
        return package;
    }

    /// <summary>Guesses the kind from what dominates the file list.</summary>
    public static AssetKind DetectKind(IReadOnlyCollection<string> files)
    {
        int Count(params string[] exts) => files.Count(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()));
        var images = Count(".bmp", ".png");
        var wavs = Count(".wav", ".ogg");
        var music = Count(".mus", ".acm");
        var inMusicFolder = files.Count(f => f.Replace('\\', '/').Contains("/music/", StringComparison.OrdinalIgnoreCase));
        if (music > 0 || inMusicFolder > wavs / 2 && inMusicFolder > 0) return AssetKind.Music;
        if (images >= wavs && images > 0) return AssetKind.Portraits;
        if (wavs > 0) return AssetKind.Soundset;
        return AssetKind.Override;
    }

    private string DeploymentsPath(AssetPackage package) => Path.Combine(package.RootPath, "deployments.json");

    private static string SanitizeId(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : char.ToLowerInvariant(c)).ToArray());
    }
}
