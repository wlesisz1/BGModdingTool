using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Turns LCC community data into automatic ordering knowledge:
/// - mod categories map to install-order tiers (community-standard order:
///   fixpacks → conversions → quests → NPCs → stores → spells/items → kits →
///   gameplay tweaks → AI/tactics → cosmetics → UI),
/// - "requires" dependencies become before/after constraints,
/// - built-in recipes override tiers (DLC Merger first, EET early, *_end last).
/// </summary>
public static class LccOrderService
{
    public const int DefaultTier = 70;

    private static readonly Dictionary<string, int> CategoryTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Patch non officiel"] = 10,            // fixpacks
        ["Conversion"] = 20,                    // EET, BGT…
        ["Quête"] = 30,                         // quests
        ["PNJ One Day"] = 40,
        ["PNJ (autre)"] = 40,
        ["PNJ recrutable"] = 45,                // joinable NPCs
        ["Forgeron et marchand"] = 50,          // stores
        ["Sort et objet"] = 55,                 // spells & items
        ["Kit"] = 60,
        ["Utilitaire"] = 65,
        ["Gameplay"] = 70,                      // tweaks
        ["Personnalisation du groupe"] = 75,
        ["Script et tactique"] = 80,            // AI/tactics (SCS) — late
        ["Cosmétique"] = 85,
        ["Portrait et son"] = 85,
        ["Interface"] = 90,                     // GUI — last of the regular mods
    };

    /// <summary>
    /// Install-order tier for an entry, or null when nothing is known (entry
    /// keeps its position). The EET/IWD/NWN family gets fixed anchors that
    /// encode the order from the IWD1_EET and NWNForBG readmes:
    /// EET → mods → IWD1 → IWD2 → NWN core+map → mods → party-banter →
    /// IWD_EET_End → IWD_EET_integration → NWN campaign (comp. 20) → EET_end.
    /// </summary>
    public static int? TierFor(BuildEntry entry, LccDatabase? lcc)
    {
        var key = InstallOrderService.NormalizeKey(entry.Tp2);
        var recipe = ModRecipeCatalog.Find(entry.Tp2);
        if (recipe is { MustBeLast: true }) return 200;
        switch (key)
        {
            case "dlcmerger": return 5;
            case "eefixpack": return 10;               // !GAME_IS eet — must precede the EET conversion
            case "eet": return 20;
            case "eeex": return 22;                    // no dependencies; docs: "near the beginning", before mods that use it
            case "imoen_forever": return 24;           // Tweak_early: before quest/NPC mods
            case "planarspheremod": return 26;         // raw area overwrites: first of the quest block
            case "allthingsmazzy": return 57;          // after NPC mods, before tweaks
            case "rr": return 56;                      // proficiency-changing mods go BEFORE Talents of Faerûn
            case "dw_talents": return 58;              // must precede NWNForBG (spell conflicts)
            case "herthimoney": return 59;             // after ALL NPC-adding mods
            case "iwd1_eet": return 62;
            case "iwd2_eet": return 63;
            case "nwnforbg":
                // The dedicated-campaign component goes after all other campaign mods.
                return entry.Components.Count == 1 && entry.Components[0] == 20 ? 99 : 64;
            case "rot": return 66;                     // after the IWD-EET mods
            case "stratagems": return 80;
            case "atweaks": return 82;                 // after SCS and RR
            case "a7-totlm-bg2ee": return 83;          // after SCS (IWD spell import defers)
            case "cdtweaks": return 84;
            case "bp-bgt-worldmap": return 95;
            case "iwd_eet-party-banter": return 96;
            case "iwd_eet_end": return 97;
            case "iwd_eet_integration": return 98;
            case "eet_tweaks": return 100;             // "last or close to last"
            case "eet_gui": return 190;
        }

        var mod = lcc?.FindByTp2(entry.Tp2);
        if (mod is null || mod.Categories.Count == 0) return null;
        int? best = null;
        foreach (var category in mod.Categories)
        {
            if (CategoryTiers.TryGetValue(category, out var tier) && (best is null || tier < best))
                best = tier;
        }
        return best;
    }

    /// <summary>Human-readable placement advice for a tier — shown as a tip in the build editor.</summary>
    public static string PlacementTip(int tier) => tier switch
    {
        <= 5 => "Platform utility (e.g. DLC Merger) — install first, before everything else.",
        <= 10 => "Fixpack — install at the very beginning, before content mods.",
        <= 20 => "Conversion (EET/BGT) — early, right after fixpacks / BG:EE-side mods.",
        <= 30 => "Quest mod — early-mid: after fixpacks and conversions, before NPC mods.",
        <= 45 => "NPC mod — after quest mods, before item/kit/tweak mods.",
        <= 50 => "Stores & merchants — mid-order, after NPC mods.",
        <= 55 => "Spells & items — mid-order, before kits.",
        <= 60 => "Kits — after spell/item mods, before gameplay tweaks.",
        <= 65 => "Utility — position depends on the tool; check its readme.",
        <= 70 => "Gameplay tweaks — late, after all content-adding mods.",
        <= 75 => "Party customization — late, after tweaks.",
        <= 80 => "AI / tactics (e.g. SCS) — near the end, after everything that adds content.",
        <= 85 => "Cosmetics / portraits / sound — very late.",
        <= 95 => "UI / worldmap — after all content and tactical mods.",
        _ => "Finalizer (EET_end and similar) — the very last entries of the build.",
    };

    /// <summary>Short tier label for list rows, or null when unknown.</summary>
    public static string? TierLabel(BuildEntry entry, LccDatabase? lcc)
    {
        var tier = TierFor(entry, lcc);
        if (tier is null) return null;
        var mod = lcc?.FindByTp2(entry.Tp2);
        var category = mod?.Categories.FirstOrDefault();
        if (category is not null) return LccCategories.EnglishLabel(category);
        return tier switch
        {
            <= 10 => "Fixpack", <= 20 => "Conversion", <= 80 => "Tactics", _ => "Late",
        };
    }

    /// <summary>
    /// Adds LCC "requires" dependencies to a constraint map: a required mod
    /// must be installed before the mod that requires it.
    /// </summary>
    public static Dictionary<string, ModMetadata> WithLccRequires(
        Dictionary<string, ModMetadata> constraints,
        IEnumerable<BuildEntry> entries,
        LccDatabase? lcc)
    {
        if (lcc is null) return constraints;
        foreach (var entry in entries.Where(e => e.IncludeBuild is null))
        {
            var mod = lcc.FindByTp2(entry.Tp2);
            if (mod?.Compatibilities is null) continue;
            foreach (var requiredId in mod.Compatibilities.RequireIds)
            {
                var required = lcc.FindById(requiredId);
                if (required is null || string.IsNullOrEmpty(required.Tp2) || required.Tp2 == "non-weidu") continue;
                // A dependency on a late-tier mod (worldmap, UI) means "needs it
                // present", not "install it first" — those go AFTER their users.
                if (TierFor(new BuildEntry { Tp2 = required.Tp2 }, lcc) is >= 90) continue;
                var key = InstallOrderService.NormalizeKey(entry.Tp2);
                if (!constraints.TryGetValue(key, out var meta))
                    constraints[key] = meta = new ModMetadata();
                if (!meta.After.Contains(required.Tp2, StringComparer.OrdinalIgnoreCase))
                    meta.After.Add(required.Tp2);
            }
        }
        return constraints;
    }
}
