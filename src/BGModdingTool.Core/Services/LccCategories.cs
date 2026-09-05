namespace BGModdingTool.Core.Services;

/// <summary>English display labels for LCC's French category names.</summary>
public static class LccCategories
{
    private static readonly Dictionary<string, string> English = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Patch non officiel"] = "Fixpack",
        ["Conversion"] = "Conversion",
        ["Quête"] = "Quest",
        ["PNJ recrutable"] = "NPC (joinable)",
        ["PNJ One Day"] = "NPC (one-day)",
        ["PNJ (autre)"] = "NPC (other)",
        ["Forgeron et marchand"] = "Stores & merchants",
        ["Sort et objet"] = "Spells & items",
        ["Kit"] = "Kit",
        ["Gameplay"] = "Gameplay",
        ["Script et tactique"] = "AI & tactics",
        ["Personnalisation du groupe"] = "Party customization",
        ["Cosmétique"] = "Cosmetic",
        ["Portrait et son"] = "Portraits & sound",
        ["Interface"] = "UI",
        ["Utilitaire"] = "Tool / utility",
        ["GemRB"] = "GemRB",
    };

    public static string EnglishLabel(string category) =>
        English.TryGetValue(category, out var label) ? label : category;

    public static string EnglishLabels(IEnumerable<string> categories) =>
        string.Join(", ", categories.Select(EnglishLabel));
}
