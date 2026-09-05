using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>A user-defined ordering rule for one mod, independent of the mod's own files.</summary>
public sealed class OrderRule
{
    /// <summary>Mod key: tp2 name in any spelling ("stratagems", "setup-stratagems.tp2"…).</summary>
    public required string Mod { get; set; }
    public List<string> Before { get; set; } = [];
    public List<string> After { get; set; } = [];
    public string? Comment { get; set; }
}

public sealed class OrderRulesFile
{
    public List<OrderRule> Rules { get; set; } = [];
}

/// <summary>
/// User-editable order rules at DataRoot/order-rules.json — Project Infinity
/// Before/After semantics, but stored by the user, so mods whose authors ship
/// no metadata can still be constrained without touching the mod itself.
/// </summary>
public sealed class OrderRuleStore(AppPaths paths)
{
    public string FilePath => Path.Combine(paths.DataRoot, "order-rules.json");

    public List<OrderRule> Load()
    {
        try
        {
            return JsonStore.Load<OrderRulesFile>(FilePath)?.Rules ?? [];
        }
        catch
        {
            return []; // malformed user file must not break validation
        }
    }

    /// <summary>Creates a template file with an example rule if none exists; returns the path.</summary>
    public string EnsureFileExists()
    {
        if (!File.Exists(FilePath))
        {
            JsonStore.Save(FilePath, new OrderRulesFile
            {
                Rules =
                [
                    new OrderRule
                    {
                        Mod = "example-mod",
                        After = ["bg2fixpack"],
                        Before = ["eet_end"],
                        Comment = "Example: this mod goes after BG2 Fixpack and before EET_end. " +
                                  "Mod names match the tp2 in any spelling ('stratagems', 'setup-stratagems.tp2').",
                    },
                ],
            });
        }
        return FilePath;
    }

    /// <summary>
    /// Community-standard ordering rules that mod metadata usually omits
    /// (from the mods' readmes and the IWD-EET family docs). Always applied.
    /// </summary>
    public static readonly IReadOnlyList<OrderRule> BuiltInRules =
    [
        // EE Fixpack: REQUIRE_PREDICATE !GAME_IS eet on every component — it must run
        // on the base game BEFORE the EET conversion (and separately on BG:EE).
        new() { Mod = "eefixpack", Before = ["eet", "stratagems", "cdtweaks", "atweaks", "dw_talents", "rr"] },
        // SCS readme: Ascension and Talents of Faerûn before SCS; SCS detects RR kits/items.
        new() { Mod = "stratagems", After = ["ascension", "dw_talents", "rr"] },
        // aTweaks + RR readmes: "never install any component of Rogue Rebalancing after aTweaks";
        // SCS readme: aTweaks was designed to be installed after SCS.
        new() { Mod = "atweaks", After = ["rr", "stratagems"] },
        // ToF readme (beta 8): mods that change the proficiency system are compatible only if
        // installed BEFORE Talents of Faerûn — Rogue Rebalancing is one of them.
        new() { Mod = "rr", After = ["eefixpack"], Before = ["dw_talents", "stratagems", "atweaks", "cdtweaks"] },
        // ToF readme: "ToF absolutely cannot be installed before any UI mod".
        new() { Mod = "eet_gui", Before = ["dw_talents"] },
        // TotLM readme: install after SCS/IWDification so its IWD-spell import defers to theirs.
        new() { Mod = "a7-totlm-bg2ee", After = ["stratagems", "iwd1_eet"] },
        new() { Mod = "cdtweaks", After = ["stratagems", "atweaks", "rr"] },
        // EET Tweaks readme: "installed last or close to last", after CRE/ITM/SPL-altering mods.
        new() { Mod = "eet_tweaks", After = ["cdtweaks", "iwd_eet_integration", "iwd_eet_end"] },
        new() { Mod = "nwnaribeth", After = ["nwnforbg"] },
        new() { Mod = "dw_talents", Before = ["nwnforbg"] },
        // Imoen 4 Ever readme: BG2 main component "treated as a quest mod … before any other
        // NPC mods that add interjections in SoA chapter 2&3"; ini Type = Tweak_early.
        new() { Mod = "imoen_forever", After = ["eet", "eefixpack"], Before = ["bg1npc", "bg1npcsoa", "ninde", "paina", "saradas_magic_2", "sellswords", "dearnise", "allthingsmazzy", "atweaks", "cdtweaks"] },
        // Planar Sphere Mod readme: raw-overwrites AR0412/AR0419/AR0420 — "install early before
        // any other mod that modify these areas".
        new() { Mod = "planarspheremod", After = ["eet"], Before = ["a7-testyourmettle", "assassinations", "sellswords", "ctb", "rot", "dc"] },
        // Region of Terror ini/GitHub: after the IWD-EET mods, before the IWD finalizers/worldmap.
        new() { Mod = "rot", After = ["iwd1_eet", "iwd2_eet", "ctb", "eefixpack"], Before = ["iwd_eet_end", "bp-bgt-worldmap"] },
        // All Things Mazzy readme: after major quest/NPC mods (Ninde, Pai'Na, Saradas… must precede
        // it for crossmod), before tweaks.
        new() { Mod = "allthingsmazzy", After = ["ninde", "paina", "saradas_magic_2", "bg1npcsoa", "dearnise", "nalia_at_last", "assassinations", "com_encounters", "imoen_forever"], Before = ["dw_talents", "stratagems", "cdtweaks"] },
        // Heroes, Thieves and Moneylenders readme: after ALL mods that add new NPCs.
        new() { Mod = "herthimoney", After = ["ninde", "paina", "saradas_magic_2", "safana", "allthingsmazzy", "bg1npcsoa", "dearnise", "nalia_at_last", "helga", "ophysiabg1", "bg1aerie", "bg1npc", "nwnaribeth"] },
        // Autumn's Twilight readme: Pai'Na must be installed BEFORE it for crossmod content.
        new() { Mod = "autumns_twilight", After = ["paina"] },
        // Cowled Menace docs: install SCS after it.
        new() { Mod = "cowledmenace", Before = ["stratagems"] },
        // BG1NPC ini: Before Framed/cdtweaks/atweaks.
        new() { Mod = "bg1npc", Before = ["framed", "cdtweaks", "atweaks"] },
        // Shards of Ice ini: before cdtweaks and stratagems.
        new() { Mod = "shardsofice", Before = ["cdtweaks", "stratagems"] },
        // RoorenArt ini: before cdtweaks.
        new() { Mod = "roorenart_bg2", Before = ["cdtweaks"] },
        // Reflections of Destiny ini: before stratagems.
        new() { Mod = "reflections_of_destiny", Before = ["stratagems"] },
        // IWD-EET family (IWD1_EET readme)
        new() { Mod = "iwd2_eet", After = ["iwd1_eet"] },
        new() { Mod = "nwnforbg", After = ["iwd1_eet", "iwd2_eet"] },
        new() { Mod = "iwd_eet-party-banter", After = ["iwd1_eet", "iwd2_eet", "nwnforbg"] },
        new() { Mod = "iwd_eet_end", After = ["iwd_eet-party-banter"] },
        new() { Mod = "iwd_eet_integration", After = ["iwd_eet_end"] },
        new() { Mod = "a7-totlm-bg2ee", After = ["iwd1_eet"] },
    ];

    /// <summary>
    /// Merges Project Infinity metadata from library packages, built-in
    /// community rules and user rules into one constraint map
    /// (normalized key → Before/After). All sources are additive.
    /// </summary>
    public static Dictionary<string, ModMetadata> BuildConstraintMap(
        IEnumerable<ModPackage> packages, IEnumerable<OrderRule> userRules)
    {
        userRules = BuiltInRules.Concat(userRules);
        var map = new Dictionary<string, ModMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var pkg in packages)
        {
            foreach (var tp2 in pkg.AllTp2s())
            {
                var key = InstallOrderService.NormalizeKey(tp2);
                if (!map.TryGetValue(key, out var meta))
                    map[key] = meta = new ModMetadata();
                meta.Before.AddRange(pkg.Metadata.Before);
                meta.After.AddRange(pkg.Metadata.After);
                foreach (var g in pkg.Metadata.Games)
                    if (!meta.Games.Contains(g)) meta.Games.Add(g);
            }
        }
        foreach (var rule in userRules)
        {
            var key = InstallOrderService.NormalizeKey(rule.Mod);
            if (!map.TryGetValue(key, out var meta))
                map[key] = meta = new ModMetadata();
            meta.Before.AddRange(rule.Before);
            meta.After.AddRange(rule.After);
        }
        return map;
    }
}
