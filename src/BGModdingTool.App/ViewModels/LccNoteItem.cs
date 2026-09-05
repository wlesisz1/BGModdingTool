using BGModdingTool.Core.Services;

namespace BGModdingTool.App.ViewModels;

/// <summary>An LCC note with [[id]] references resolved to names + links.</summary>
public sealed class LccNoteItem
{
    public string Text { get; init; } = "";
    public List<LccNoteRef> Links { get; init; } = [];
    public bool HasLinks => Links.Count > 0;

    public static LccNoteItem Plain(string text) => new() { Text = text };

    public static LccNoteItem Resolved(string note, LccDatabase lcc, string prefix = "")
    {
        var (text, refs) = lcc.ResolveRefs(note);
        return new LccNoteItem { Text = prefix + text, Links = refs };
    }
}
