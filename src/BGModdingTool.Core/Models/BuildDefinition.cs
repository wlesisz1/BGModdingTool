namespace BGModdingTool.Core.Models;

/// <summary>
/// A reproducible mod configuration: ordered list of (mod, language, components).
/// Serialized to JSON under builds/. Re-playable on a fresh instance.
/// </summary>
public sealed class BuildDefinition
{
    public required string Name { get; set; }
    public GameType GameType { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BuildEntry> Entries { get; set; } = [];
}

/// <summary>
/// All selected components of one mod, installed as one WeiDU batch —
/// or (when IncludeBuild is set) a reference to another build whose entries
/// are spliced in at this position at install/validation time.
/// </summary>
public sealed class BuildEntry
{
    /// <summary>Name of another build to include at this position (a "package"). When set, the other fields are ignored.</summary>
    public string? IncludeBuild { get; set; }
    /// <summary>Library mod id this entry refers to (null for entries imported from a bare weidu.log).</summary>
    public string? ModId { get; set; }
    /// <summary>tp2 name as used by weidu.log (e.g. "STRATAGEMS/SETUP-STRATAGEMS.TP2").</summary>
    public required string Tp2 { get; set; }
    public int LanguageIndex { get; set; }
    public List<int> Components { get; set; } = [];
    /// <summary>
    /// Extra WeiDU command-line arguments appended verbatim, e.g.
    /// EET's --args-list p "path to BG:EE" for non-interactive path input.
    /// </summary>
    public string? ExtraArgs { get; set; }
    /// <summary>
    /// Text piped to the installer's stdin, for mods that ask questions via
    /// ACTION_READLN (e.g. HoW_EET's "Patch existing save games"). One answer
    /// per line. When null, stdin is closed immediately.
    /// </summary>
    public string? StdinInput { get; set; }
}
