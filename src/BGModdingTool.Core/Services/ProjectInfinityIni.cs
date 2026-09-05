using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Parser for Project Infinity-style mod metadata ini files
/// (a [Metadata] section with Name/Author/Description/Before/After etc.).
/// Community standard: https://github.com/ALIENQuake/ProjectInfinity
/// </summary>
public static class ProjectInfinityIni
{
    public static ModMetadata Parse(string iniContent)
    {
        var meta = new ModMetadata();
        var inMetadata = false;
        foreach (var rawLine in iniContent.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inMetadata = line.Equals("[Metadata]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inMetadata) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length == 0) continue;

            switch (key.ToLowerInvariant())
            {
                case "name": meta.Name = value; break;
                case "author": meta.Author = value; break;
                case "description": meta.Description = value; break;
                // Some inis list several readmes ("url-english.html, url-%LANGUAGE%.html");
                // keep the first concrete one so it opens as a link.
                case "readme":
                    meta.Readme = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .FirstOrDefault(v => !v.Contains('%')) ?? value;
                    break;
                case "forum": meta.Forum = value; break;
                case "homepage": meta.Homepage = value; break;
                case "download": meta.DownloadUri = value; break;
                case "labeltype": meta.LabelType = value; break;
                case "type": meta.Games = SplitList(value); break;
                case "before": meta.Before = SplitList(value, upper: true); break;
                case "after": meta.After = SplitList(value, upper: true); break;
            }
        }
        return meta;
    }

    private static List<string> SplitList(string value, bool upper = false) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(v => upper ? v.ToUpperInvariant() : v)];
}
