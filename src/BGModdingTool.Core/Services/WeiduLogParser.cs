using System.Text.RegularExpressions;
using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Parses weidu.log — the community source of truth for installed components.
/// Line format: ~TP2~ #lang #component // Component Name: Version
/// </summary>
public static partial class WeiduLogParser
{
    [GeneratedRegex(@"^\s*~(?<tp2>[^~]+)~\s+#(?<lang>\d+)\s+#(?<comp>\d+)(?:\s*//\s*(?<name>.*?))?\s*$")]
    private static partial Regex LineRegex();

    public static List<WeiduLogEntry> Parse(string logContent)
    {
        var entries = new List<WeiduLogEntry>();
        foreach (var rawLine in logContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("//") || string.IsNullOrWhiteSpace(line)) continue;
            var m = LineRegex().Match(line);
            if (!m.Success) continue;

            string? name = m.Groups["name"].Success ? m.Groups["name"].Value : null;
            string? version = null;
            if (name is not null)
            {
                // "Component Name: v35.19" — version is the suffix after the last ": "
                var idx = name.LastIndexOf(": ", StringComparison.Ordinal);
                if (idx > 0 && idx >= name.Length - 20)
                {
                    version = name[(idx + 2)..].Trim();
                    name = name[..idx].Trim();
                }
            }

            entries.Add(new WeiduLogEntry(
                m.Groups["tp2"].Value.Trim().ToUpperInvariant(),
                int.Parse(m.Groups["lang"].Value),
                int.Parse(m.Groups["comp"].Value),
                name, version));
        }
        return entries;
    }

    public static List<WeiduLogEntry> ParseFile(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : [];

    /// <summary>Groups consecutive entries of the same tp2+language into build entries (preserving order).</summary>
    public static List<BuildEntry> ToBuildEntries(IEnumerable<WeiduLogEntry> entries)
    {
        var result = new List<BuildEntry>();
        foreach (var e in entries)
        {
            var last = result.Count > 0 ? result[^1] : null;
            if (last is null || last.Tp2 != e.Tp2 || last.LanguageIndex != e.LanguageIndex)
            {
                last = new BuildEntry { Tp2 = e.Tp2, LanguageIndex = e.LanguageIndex };
                result.Add(last);
            }
            last.Components.Add(e.ComponentNumber);
        }
        return result;
    }
}
