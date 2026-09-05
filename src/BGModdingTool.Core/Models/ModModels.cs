namespace BGModdingTool.Core.Models;

/// <summary>A mod package stored in the library (an extracted WeiDU mod).</summary>
public sealed class ModPackage
{
    public required string Id { get; init; }
    /// <summary>Path to the extracted package root in the library.</summary>
    public required string RootPath { get; init; }
    /// <summary>Primary tp2 file path relative to the package root (e.g. "stratagems/setup-stratagems.tp2").</summary>
    public required string Tp2RelativePath { get; init; }
    /// <summary>
    /// All tp2 files in the package, primary first. Multi-installer mods
    /// (e.g. EET: setup-EET, setup-EET_end, setup-EET_gui) have several.
    /// </summary>
    public List<string> Tp2Variants { get; set; } = [];
    /// <summary>The mod folder that must be copied into the game dir (e.g. "stratagems").</summary>
    public required string ModFolderName { get; init; }
    public ModMetadata Metadata { get; init; } = new();
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Canonical tp2 name as it appears in weidu.log (uppercased, e.g. "STRATAGEMS/SETUP-STRATAGEMS.TP2").</summary>
    public string Tp2LogName => ToLogName(Tp2RelativePath);

    public IEnumerable<string> AllTp2s() => Tp2Variants.Count > 0 ? Tp2Variants : [Tp2RelativePath];

    public static string ToLogName(string tp2RelativePath) => tp2RelativePath.Replace('\\', '/').ToUpperInvariant();
}

/// <summary>Project Infinity-style metadata (mod.ini [Metadata] section), best-effort.</summary>
public sealed class ModMetadata
{
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Readme { get; set; }
    public string? Forum { get; set; }
    public string? Homepage { get; set; }
    public string? DownloadUri { get; set; }
    public string? LabelType { get; set; }
    /// <summary>Games this mod supports (metadata keys like "BG2:EE", "EET").</summary>
    public List<string> Games { get; set; } = [];
    /// <summary>Ordering hints: tp2 names this mod must come before / after.</summary>
    public List<string> Before { get; set; } = [];
    public List<string> After { get; set; } = [];
}

/// <summary>One installable component of a mod, as reported by weidu --list-components-json.</summary>
public sealed record ModComponent(int Number, string Label, string? Subgroup = null, bool Forced = false);
