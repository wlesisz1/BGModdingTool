namespace BGModdingTool.Core.Services;

/// <summary>Global tool settings, persisted at DataRoot/config.json.</summary>
public sealed class AppConfig
{
    /// <summary>Registered original game installations (never modified).</summary>
    public List<SourceGame> SourceGames { get; set; } = [];
    /// <summary>Explicit path to weidu.exe; when null, a harvested setup-*.exe is used.</summary>
    public string? WeiduPath { get; set; }
    /// <summary>Game language directory used for installs (WeiDU --use-lang).</summary>
    public string? PreferredLanguage { get; set; } = "en_US";
    /// <summary>Save the edited build automatically shortly after every change.</summary>
    public bool AutoSaveBuilds { get; set; } = true;
}

public sealed class SourceGame
{
    public required string Path { get; set; }
    public Models.GameType GameType { get; set; }
    public string? Label { get; set; }
}
