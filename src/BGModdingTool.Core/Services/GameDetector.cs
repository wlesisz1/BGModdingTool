using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Best-effort game type detection from a directory. Heuristics are scored;
/// the UI always lets the user override the result.
/// </summary>
public static class GameDetector
{
    public static GameType Detect(string gameDir)
    {
        if (!File.Exists(Path.Combine(gameDir, "chitin.key")))
            return GameType.Unknown;

        // EET / BGT are conversions recorded in weidu.log on top of a base game.
        var weiduLog = Path.Combine(gameDir, "weidu.log");
        if (File.Exists(weiduLog))
        {
            var log = File.ReadAllText(weiduLog).ToUpperInvariant();
            if (log.Contains("SETUP-EET.TP2") || log.Contains("EET/EET.TP2")) return GameType.EET;
            if (log.Contains("SETUP-BGT.TP2") || log.Contains("BGT/BGT.TP2")) return GameType.BGT;
        }

        var isEE = Directory.Exists(Path.Combine(gameDir, "lang"))
                   || File.Exists(Path.Combine(gameDir, "engine.lua"));

        if (!isEE)
        {
            // classic engines
            if (File.Exists(Path.Combine(gameDir, "bgmain.exe"))) return GameType.BG2Classic;
            return GameType.Unknown;
        }

        if (HasFile(gameDir, "Icewind.exe") || HasMovie(gameDir, "IWDINTRO"))
            return GameType.IWDEE;

        // BG1:EE vs BG2:EE — SoD dlc / intro movies are the most stable markers.
        if (HasFile(gameDir, "sod-dlc.key") || HasFile(gameDir, "sod-dlc.zip") || HasMovie(gameDir, "BGENTER"))
            return GameType.BGEE;
        if (HasMovie(gameDir, "INTRO15F") || HasMovie(gameDir, "MELISSAN") || HasMovie(gameDir, "RESTDUNG"))
            return GameType.BG2EE;

        return GameType.Unknown;
    }

    private static bool HasFile(string dir, string name) => File.Exists(Path.Combine(dir, name));

    private static bool HasMovie(string dir, string baseName)
    {
        var movies = Path.Combine(dir, "movies");
        if (!Directory.Exists(movies)) return false;
        return Directory.EnumerateFiles(movies, baseName + ".*").Any();
    }
}
