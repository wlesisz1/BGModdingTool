namespace BGModdingTool.Core.Models;

public enum GameType
{
    Unknown = 0,
    BGEE,       // Baldur's Gate: Enhanced Edition (incl. SoD)
    BG2EE,      // Baldur's Gate II: Enhanced Edition
    IWDEE,      // Icewind Dale: Enhanced Edition
    EET,        // Enhanced Edition Trilogy (installed on top of BG2EE)
    BGT,        // classic Baldur's Gate Trilogy (BG2 ToB engine)
    BG2Classic, // classic BG2 ToB (BGT target before conversion)
}

public static class GameTypeExtensions
{
    public static string DisplayName(this GameType type) => type switch
    {
        GameType.BGEE => "Baldur's Gate: EE",
        GameType.BG2EE => "Baldur's Gate II: EE",
        GameType.IWDEE => "Icewind Dale: EE",
        GameType.EET => "Enhanced Edition Trilogy",
        GameType.BGT => "Baldur's Gate Trilogy (classic)",
        GameType.BG2Classic => "Baldur's Gate II (classic)",
        _ => "Unknown",
    };

    /// <summary>Identifier used by Project Infinity-style metadata (mod.ini "Type" games list).</summary>
    public static string MetadataKey(this GameType type) => type switch
    {
        GameType.BGEE => "BG:EE",
        GameType.BG2EE => "BG2:EE",
        GameType.IWDEE => "IWD:EE",
        GameType.EET => "EET",
        GameType.BGT => "BGT",
        GameType.BG2Classic => "BG2",
        _ => "?",
    };
}
