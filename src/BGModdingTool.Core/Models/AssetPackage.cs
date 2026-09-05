namespace BGModdingTool.Core.Models;

/// <summary>Kind of a non-WeiDU asset pack, decides where its files are deployed.</summary>
public enum AssetKind
{
    Portraits,   // player portraits (BMP/PNG)
    Soundset,    // character voice sets (WAV)
    Music,       // music replacement (MUS/ACM/WAV/OGG in the game's music folder)
    Override,    // loose game resources dropped into override/
}

/// <summary>
/// A plain file pack (zip/rar/7z/folder) that is not a WeiDU mod: portrait
/// packs, soundsets, music. Stored extracted under DataRoot/assets/&lt;id&gt;.
/// </summary>
public sealed class AssetPackage
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string RootPath { get; init; }
    public AssetKind Kind { get; set; }
    public int FileCount { get; set; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
}

/// <summary>Where an asset package's files were copied, so they can be removed exactly.</summary>
public sealed class AssetDeployment
{
    public required string InstanceId { get; init; }
    public required string TargetDirectory { get; init; }
    public List<string> Files { get; init; } = [];
    public DateTimeOffset DeployedAt { get; init; } = DateTimeOffset.UtcNow;
}
