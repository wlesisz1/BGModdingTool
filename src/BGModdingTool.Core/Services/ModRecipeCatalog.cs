using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>
/// A built-in "recipe" for a known conversion/platform mod: what it needs
/// (source game paths, ordering, stdin answers) and recommended arguments.
/// </summary>
public sealed record ModRecipe(
    string Key,
    string Title,
    string Instructions,
    /// <summary>Recommended extra args; {PATH} is replaced with the source game directory (quoted).</summary>
    string? ArgsTemplate = null,
    /// <summary>Game whose install/instance directory fills {PATH}.</summary>
    GameType? SourcePathGame = null,
    bool MustBeLast = false,
    /// <summary>Answers piped to stdin for ACTION_READLN prompts (one per line).</summary>
    string? RecommendedStdin = null,
    /// <summary>Structured installer prompts, rendered as dropdowns in the UI; answers joined by newlines become stdin.</summary>
    IReadOnlyList<StdinQuestion>? StdinQuestions = null)
{
    public const string PathPlaceholder = "{PATH}";
    /// <summary>Literal marker left in args when no matching instance was found; installs are blocked while present.</summary>
    public const string UnresolvedMarker = "<SET-PATH";
}

/// <summary>One installer prompt (ACTION_READLN) with its known answers.</summary>
public sealed record StdinQuestion(string Text, IReadOnlyList<StdinOption> Options, string DefaultValue);
public sealed record StdinOption(string Label, string Value);

/// <summary>
/// Knowledge base for big conversion mods (EET, IWD1/2-EET, NWNForBG,
/// DLCMerger…), keyed by normalized tp2 name. Verified against the readmes
/// and tp2 sources of the actual packages (The-Gate-Project/Tipun editions
/// of the IWD mods are self-contained — they do NOT ask for game paths).
/// </summary>
public static class ModRecipeCatalog
{
    private static readonly Dictionary<string, ModRecipe> ByKey = new(StringComparer.OrdinalIgnoreCase);

    static ModRecipeCatalog()
    {
        Add(new ModRecipe(
            Key: "eet",
            Title: "EET — core (setup-EET)",
            Instructions:
                "Install on a CLEAN BG2:EE instance (translations are fine), as the first mod after any BG:EE-side mods. " +
                "Requires the path to the BG:EE+SoD directory — point it at your BG:EE instance (if you mod BG:EE, e.g. DLC Merger, " +
                "do it on an instance and pass that instance here). Steam/GOG: install DLC Merger on BG:EE before EET. Component 0. " +
                "args-list flags: p = path given up front, s = no desktop shortcut, b = skip biffing.",
            ArgsTemplate: "--args-list sp {PATH}",
            SourcePathGame: GameType.BGEE));

        Add(new ModRecipe(
            Key: "eet_end",
            Title: "EET — finalization (setup-EET_end)",
            Instructions:
                "REQUIRED, installed as the LAST mod (GUI mods can be the exception). " +
                "Component 0 = standard install, 1 = additionally update saves (back your saves up first). " +
                "When adding mods later: uninstall EET_end, add the mods, reinstall EET_end.",
            MustBeLast: true));

        Add(new ModRecipe(
            Key: "eet_gui",
            Title: "EET — optional SoD GUI (setup-EET_gui)",
            Instructions:
                "Optional Siege of Dragonspear-style GUI. Can be installed at the very end, even after EET_end. " +
                "Install before other GUI mods. Component 0."));

        Add(new ModRecipe(
            Key: "dlcmerger",
            Title: "DLC Merger (Argent77)",
            Instructions:
                "For Steam/GOG copies with Siege of Dragonspear: merges the SoD DLC into BG:EE — REQUIRED before installing " +
                "any BG:EE mods (and before passing that instance to EET). Install as the first mod on the BG:EE instance. " +
                "Use the \"Merge Siege of Dragonspear DLC\" component (non-interactive). The custom-DLC component prompts for a " +
                "file name (would need stdin answers) and \"merge all DLCs\" needs a 64-bit WeiDU. " +
                "Note: WeiDU fails on DLC archive paths containing spaces. The Beamdog edition does not need this mod."));

        Add(new ModRecipe(
            Key: "dw_talents",
            Title: "Talents of Faerûn (beta)",
            Instructions:
                "EET: \"probably largely compatible … limited testing\"; the code has explicit EET branches and the changelog " +
                "fixed EET-specific bugs (ability scores on SoD/EET, kits on the BG part of EET, subraces, Imoen UI glitches). " +
                "The author's own EET testing had EET_end installed BEFORE ToF (contra the official EET order). " +
                "Order: as late as possible, before SCS; proficiency-changing mods (Rogue Rebalancing, Tweaks proficiency " +
                "components) and ANY UI mod must be installed before ToF. Incompatible with other sphere-system, subrace or " +
                "school-restriction mods. Start a NEW game. dw_talents.ini: start_eet=bg by default — set soa/sod/tob if you " +
                "start EET elsewhere; 3p/original_game.ini re-kits Viconia (Shar), Aerie (air elementalist), Yeslick, Tiax and " +
                "makes Viconia a drow subrace — edit it to disable. Never select the 'batch mode' component (interactive only)."));

        Add(new ModRecipe(
            Key: "eefixpack",
            Title: "Enhanced Edition Fixpack",
            Instructions:
                "Every component has REQUIRE_PREDICATE !GAME_IS eet — it cannot install after the EET conversion. " +
                "Install it FIRST on each base game: on the BG:EE instance (after DLC Merger) and on the BG2:EE instance " +
                "BEFORE setup-EET. Readme: \"after the official patches, but before other mods with the sole exception of DLC Merger\"."));

        Add(new ModRecipe(
            Key: "bwquest",
            Title: "The Black Rose Part 1: Market Prices",
            Instructions:
                "Single-component quest mod. Its voice audio is installed by AT_INTERACTIVE_EXIT (BWQuest/audio-install.bat), " +
                "which WeiDU skips in non-interactive installs — run that .bat in the instance folder afterwards if you want the audio."));

        Add(new ModRecipe(
            Key: "bgt",
            Title: "Baldur's Gate Trilogy (classic BGT-WeiDU)",
            Instructions:
                "Installs on classic BG2 (ToB, patched). The installer asks for the classic BG1+TotSC directory — " +
                "prepare a separate BG1+TotSC install and provide its path. The installer is interactive; " +
                "if the install stalls on the path prompt, check the readme for current command-line arguments.",
            SourcePathGame: GameType.Unknown));

        // --- The-Gate-Project / Tipun family (self-contained resources) ---

        const string gateOrder =
            "Order per readme: EET → other mods → IWD1_EET → IWD2_EET → other mods → NWNForBG (comp. 0 and map 10/11) → " +
            "other mods → IWD_EET-party-banter → IWD_EET_End → IWD_EET_integration (all components) → NWNForBG (comp. 20) → EET_end.";

        Add(new ModRecipe(
            Key: "iwd1_eet",
            Title: "IWD1-EET (Icewind Dale + HoW + TotLM in EET)",
            Instructions:
                "Resources are BUNDLED — does not require an IWD:EE install or any paths. Installs on top of a finished EET. " +
                "Includes Heart of Winter and Trial of the Luremaster. Do NOT combine with HoW_EET (already included). " + gateOrder));

        Add(new ModRecipe(
            Key: "iwd2_eet",
            Title: "IWD2-EET (Icewind Dale II in EET)",
            Instructions:
                "Resources are BUNDLED — does not require an IWD2 install or any paths. Installs on top of a finished EET, " +
                "AFTER IWD1_EET. " + gateOrder));

        Add(new ModRecipe(
            Key: "how_eet",
            Title: "HoW_EET (Heart of Winter for BG:EE/EET)",
            Instructions:
                "Alpha version. Resources bundled. On EET install AFTER setup-EET and BEFORE Trials of the Luremaster (Argent77). " +
                "INCOMPATIBLE with IWD1_EET (HoW is already included there) — pick one.",
            RecommendedStdin: "1",
            StdinQuestions:
            [
                new StdinQuestion("Patch existing save games?",
                    [new StdinOption("No (recommended)", "1"), new StdinOption("Yes", "2")],
                    DefaultValue: "1"),
            ]));

        Add(new ModRecipe(
            Key: "nwnforbg",
            Title: "NWNForBG (Neverwinter Nights in BG2/EET)",
            Instructions:
                "Resources are BUNDLED — does not require an NWN install. Components (verified from the tp2): 0 = main, " +
                "10 = place NWN locations on BP-BGT Worldmap, 11 = separate worldmap for NWN areas (pick ONE of 10/11; " +
                "the IWD1_EET readme's '11/12' is off by one), 20 = dedicated campaign (BG2:EE/EET only). " +
                "Install component 20 AFTER other campaign-adding mods (e.g. IWD_EET_integration), just before EET_end. " +
                "Install Spell Revisions (main component) BEFORE this mod; IWDification before it if you want its spell versions. " +
                "BP-BGT-Worldmap (v13+) must be installed AFTER NWNForBG. Skip Tweaks Anthology's \"NPCs Cannot Use Doors\" (breaks a quest)."));

        Add(new ModRecipe(
            Key: "bp-bgt-worldmap",
            Title: "BP-BGT Worldmap",
            Instructions:
                "Big worldmap for BGT/EET. Install LATE — after all area/quest-adding mods " +
                "(in particular after NWNForBG), but before EET_end."));

        Add(new ModRecipe(
            Key: "iwd_eet_integration",
            Title: "IWD_EET_integration",
            Instructions:
                "Integrates the IWD campaigns with EET (campaign selection etc.). Per the IWD-EET family readme: install all components, " +
                "AFTER IWD_EET_End and BEFORE NWNForBG component 20 and EET_end. " + gateOrder));

        Add(new ModRecipe(
            Key: "iwd_eet-party-banter",
            Title: "IWD_EET-party-banter",
            Instructions:
                "Party banter for the IWD campaigns in EET. Install after the IWD1/IWD2_EET mods, BEFORE IWD_EET_End. " + gateOrder));

        Add(new ModRecipe(
            Key: "iwd_eet_end",
            Title: "IWD_EET_End",
            Instructions:
                "Finalization of the IWD-EET family (a separate The-Gate-Project mod — if it is not in your library, download it " +
                "from GitHub: The-Gate-Project/IWD_EET_End). Install after IWD_EET-party-banter, before IWD_EET_integration and EET_end."));

        // K4thos' classic IWD-in-EET (different mod than IWD1_EET) kept for
        // completeness — it DOES read resources from an IWD:EE install.
        Add(new ModRecipe(
            Key: "iwd_eet",
            Title: "IWD-in-EET (K4thos)",
            Instructions:
                "K4thos' classic IWD-in-EET (a different mod than Tipun's IWD1_EET!). Requires an installed IWD:EE — " +
                "its directory path is provided during installation. Check this version's readme for the exact arguments.",
            ArgsTemplate: "--args-list p {PATH}",
            SourcePathGame: GameType.IWDEE));
    }

    private static void Add(ModRecipe recipe) => ByKey[recipe.Key] = recipe;

    /// <summary>Finds a recipe for any tp2 spelling ("EET/SETUP-EET.TP2", "setup-eet.tp2", "eet").</summary>
    public static ModRecipe? Find(string tp2) =>
        ByKey.TryGetValue(InstallOrderService.NormalizeKey(tp2), out var r) ? r : null;

    /// <summary>
    /// Renders the recipe's args template. {PATH} becomes the quoted source dir
    /// when known, otherwise a visible unresolved marker that blocks installs.
    /// </summary>
    public static string? RenderArgs(ModRecipe recipe, string? sourceGameDir)
    {
        if (recipe.ArgsTemplate is null) return null;
        if (!recipe.ArgsTemplate.Contains(ModRecipe.PathPlaceholder)) return recipe.ArgsTemplate;
        var value = sourceGameDir is not null
            ? $"\"{sourceGameDir}\""
            : $"\"{ModRecipe.UnresolvedMarker}: {recipe.SourcePathGame?.DisplayName() ?? "source game directory"}>\"";
        return recipe.ArgsTemplate.Replace(ModRecipe.PathPlaceholder, value);
    }
}
