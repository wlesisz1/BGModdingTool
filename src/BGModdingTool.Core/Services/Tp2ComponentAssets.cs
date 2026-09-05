using System.Text.RegularExpressions;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Extracts, per component, the image files a tp2 copies (portrait choices
/// like Helga's "Portrait: Default" vs "Alt NWN portrait"). Static text
/// analysis only: BEGIN blocks are numbered by DESIGNATED when present,
/// otherwise by order, exactly like WeiDU does.
/// </summary>
public static partial class Tp2ComponentAssets
{
    [GeneratedRegex(@"^\s*BEGIN\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex BeginLine();

    [GeneratedRegex(@"DESIGNATED\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Designated();

    [GeneratedRegex(@"[~""']([^~""'\r\n]+?\.(?:bmp|png))[~""']", RegexOptions.IgnoreCase)]
    private static partial Regex ImagePath();

    /// <summary>Component number → image files (absolute paths that exist on disk).</summary>
    public static Dictionary<int, List<string>> ImagesByComponent(string packageRoot, string tp2RelativePath)
    {
        var result = new Dictionary<int, List<string>>();
        var tp2Path = Path.Combine(packageRoot, tp2RelativePath);
        if (!File.Exists(tp2Path)) return result;

        var text = File.ReadAllText(tp2Path);
        var modFolder = Path.GetDirectoryName(tp2RelativePath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        if (modFolder.Length == 0)
            modFolder = Path.GetFileNameWithoutExtension(tp2RelativePath).Replace("setup-", "", StringComparison.OrdinalIgnoreCase);

        var starts = BeginLine().Matches(text).Select(m => m.Index).ToList();
        var nextIndex = 0;
        for (var i = 0; i < starts.Count; i++)
        {
            var block = text.Substring(starts[i], (i + 1 < starts.Count ? starts[i + 1] : text.Length) - starts[i]);
            // Only the header (before the first real action) may carry DESIGNATED, but
            // WeiDU allows it anywhere in the block's preamble — search the block.
            var designated = Designated().Match(block);
            var number = designated.Success ? int.Parse(designated.Groups[1].Value) : nextIndex;
            nextIndex = number + 1;

            foreach (Match m in ImagePath().Matches(block))
            {
                var raw = m.Groups[1].Value.Replace("%MOD_FOLDER%", modFolder).Replace('\\', '/');
                var candidate = Path.GetFullPath(Path.Combine(packageRoot, raw));
                if (!File.Exists(candidate)) continue;
                if (!result.TryGetValue(number, out var list)) result[number] = list = [];
                if (!list.Contains(candidate, StringComparer.OrdinalIgnoreCase)) list.Add(candidate);
            }
        }
        return result;
    }
}
