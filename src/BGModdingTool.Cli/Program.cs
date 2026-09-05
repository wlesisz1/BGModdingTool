// Headless tooling for BGModdingTool.
//   dotnet run --project src/BGModdingTool.Cli            → seed  (download proven mods + starter presets)
//   dotnet run --project src/BGModdingTool.Cli superpack  → build "Modpack" builds from EVERYTHING in the library
using System.Text.RegularExpressions;
using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "seed";

var paths = new AppPaths();
paths.EnsureCreated();
var config = JsonStore.Load<AppConfig>(paths.ConfigFile) ?? new AppConfig();
var lcc = new LccDatabase(paths);
var library = new ModLibrary(paths);
var buildStore = new BuildStore(paths);
using var downloader = new ModDownloader();

Console.WriteLine($"Data root: {paths.DataRoot}  |  mode: {mode}");
if (!lcc.IsAvailable)
{
    Console.WriteLine("Downloading LCC database…");
    await lcc.DownloadAsync();
}

string? weiduPath = null;
if (!string.IsNullOrWhiteSpace(config.WeiduPath) && File.Exists(config.WeiduPath)) weiduPath = config.WeiduPath;
weiduPath ??= File.Exists(Path.Combine(paths.ToolsDir, "weidu.exe")) ? Path.Combine(paths.ToolsDir, "weidu.exe") : null;
if (weiduPath is null)
{
    Console.WriteLine("Downloading WeiDU…");
    weiduPath = await downloader.DownloadWeiduAsync(paths.ToolsDir, new Progress<string>(Console.WriteLine));
}
var weidu = new WeiduRunner(weiduPath);

var byKey = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
void IndexLibrary()
{
    byKey.Clear();
    foreach (var pkg in library.List())
        foreach (var tp2 in pkg.AllTp2s())
            byKey.TryAdd(InstallOrderService.NormalizeKey(tp2), pkg);
}
IndexLibrary();

async Task<List<ModComponent>> ListComponents(ModPackage pkg, string? tp2Rel = null)
{
    try
    {
        return await weidu.ListComponentsAsync(pkg.RootPath, tp2Rel ?? pkg.Tp2RelativePath, 0);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[warn] component listing failed for {pkg.ModFolderName}: {ex.Message}");
        return [];
    }
}

// "Install in batch mode" components (DavidW's mods) only make sense for
// interactive installs — they defer the real install and write a .bat file.
// With --force-install-list they must never be selected (same rule as PI).
static bool IsInteractiveOnly(ModComponent c) =>
    Regex.IsMatch(c.Label, @"batch mode|batch install|DO NOT USE WITH PROJECT INFINITY", RegexOptions.IgnoreCase);

static List<int> GroupFirstAll(List<ModComponent> components)
{
    var result = new List<int>();
    var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var c in components)
    {
        if (IsInteractiveOnly(c)) continue;
        var arrow = c.Label.IndexOf(" -> ", StringComparison.Ordinal);
        if (arrow > 0 && !seenGroups.Add(c.Label[..arrow])) continue;
        result.Add(c.Number);
    }
    return result;
}

static List<int> ScsHardcore(List<ModComponent> components)
{
    var include = new Regex(@"initialise|smarter|improved|better|potions|high-level abilities|more consistent|tougher|harder|increase", RegexOptions.IgnoreCase);
    var exclude = new Regex(@"remove|reduce|easier|ease-of-use|weaker|cosmetic|NPC customisation|joinable", RegexOptions.IgnoreCase);
    return GroupFirstAll(components.Where(c => include.IsMatch(c.Label) && !exclude.IsMatch(c.Label)).ToList());
}

var languageCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
async Task<int> EnglishIndex(ModPackage pkg, string tp2Rel)
{
    var cacheKey = pkg.Id + "|" + tp2Rel;
    if (!languageCache.TryGetValue(cacheKey, out var idx))
    {
        idx = await weidu.FindLanguageIndexAsync(pkg.RootPath, tp2Rel);
        languageCache[cacheKey] = idx;
    }
    return idx;
}

// Entries are built with the mod's English language index — index 0 is
// often Russian/French for mods from those communities.
async Task<BuildEntry> Entry(string key, List<int> components, string? tp2Rel = null, string? extraArgs = null, string? stdin = null)
{
    var pkg = byKey[key];
    var tp2 = tp2Rel ?? pkg.AllTp2s().First(t => InstallOrderService.NormalizeKey(t) == key);
    return new BuildEntry
    {
        ModId = pkg.Id,
        Tp2 = ModPackage.ToLogName(tp2),
        LanguageIndex = await EnglishIndex(pkg, tp2),
        Components = components,
        ExtraArgs = extraArgs,
        StdinInput = stdin,
    };
}

bool Has(string key) => byKey.ContainsKey(key);

var overwrite = args.Contains("--overwrite", StringComparer.OrdinalIgnoreCase);

// A build that already exists is the USER's document (it may carry manual
// decisions). Never overwrite it silently: save the generated one next to it
// and print the differences, unless --overwrite was given explicitly.
void SaveBuild(BuildDefinition build)
{
    var existing = buildStore.Load(build.Name);
    if (existing is not null && !overwrite)
    {
        var diff = BuildStore.Diff(existing, build);
        var generatedName = $"{build.Name} (generated {DateTime.Now:yyyy-MM-dd HH:mm})";
        build.Name = generatedName;
        buildStore.Save(build);
        Console.WriteLine($"[build] \"{existing.Name}\" already exists — kept untouched; generated version saved as \"{generatedName}\".");
        Console.WriteLine(diff.Count == 0
            ? "   (identical to the existing build)"
            : $"   differences vs your build ({diff.Count}):" + string.Concat(diff.Select(d => "\n     " + d)));
        return;
    }
    buildStore.Save(build);
    Console.WriteLine($"[build] \"{build.Name}\" — {build.Entries.Count} entries" + (existing is not null ? " (overwritten; previous version archived in builds/history)" : ""));
}

void Validate(params string[] names)
{
    Console.WriteLine();
    var rules = new OrderRuleStore(paths);
    foreach (var name in names)
    {
        var build = buildStore.Load(name);
        if (build is null) continue;
        var warnings = new List<string>();
        var flat = buildStore.ExpandEntries(build, warnings);
        var constraints = OrderRuleStore.BuildConstraintMap(library.List(), rules.Load());
        LccOrderService.WithLccRequires(constraints, flat, lcc);
        var issues = warnings
            .Concat(OrderValidator.Validate(flat, constraints))
            .Concat(GameCompatibilityValidator.Validate(flat, build.GameType, constraints, lcc))
            .ToList();
        Console.WriteLine($"\"{name}\": {(issues.Count == 0 ? "order OK" : string.Join("\n   ⚠ ", [$"{issues.Count} issues:", .. issues]))}");
    }
}

if (mode == "seed")
{
    var wanted = new (string Key, string? FallbackUrl)[]
    {
        ("stratagems", "https://github.com/Gibberlings3/SwordCoastStratagems"),
        ("ascension", "https://github.com/Gibberlings3/Ascension"),
        ("cdtweaks", "https://github.com/Gibberlings3/Tweaks-Anthology"),
        ("bg1npc", "https://github.com/Gibberlings3/BG1NPC"),
        ("bg1ub", "https://github.com/Pocket-Plane-Group/bg1ub"),
        ("iwd_eet_end", "https://github.com/The-Gate-Project/IWD_EET_End"),
    };
    foreach (var (key, fallback) in wanted)
    {
        if (byKey.ContainsKey(key)) { Console.WriteLine($"[skip] {key} — already in the library"); continue; }
        var url = lcc.FindByTp2(key)?.Urls.FirstOrDefault(u => u.Contains("github.com", StringComparison.OrdinalIgnoreCase)) ?? fallback;
        if (url is null) { Console.WriteLine($"[warn] {key} — no downloadable URL"); continue; }
        Console.WriteLine($"[download] {key} ← {url}");
        var temp = Path.Combine(Path.GetTempPath(), "bgmt-seed-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var archive = await downloader.DownloadArchiveAsync(url, temp, new Progress<string>(s => Console.WriteLine("   " + s)));
            var pkg = await library.ImportArchiveAsync(archive);
            Console.WriteLine($"[ok] imported {pkg.Metadata.Name ?? pkg.ModFolderName}");
        }
        catch (Exception ex) { Console.WriteLine($"[FAIL] {key}: {ex.Message}"); }
        finally { try { Directory.Delete(temp, recursive: true); } catch { } }
    }
    IndexLibrary();

    if (Has("stratagems"))
    {
        SaveBuild(new BuildDefinition
        {
            Name = "SCS Hardcore",
            GameType = GameType.EET,
            Description = "Sword Coast Stratagems tuned for maximum difficulty. Set the in-game SCS difficulty to Insane/LoB.",
            Entries = [await Entry("stratagems", ScsHardcore(await ListComponents(byKey["stratagems"])))],
        });
    }
    Validate("SCS Hardcore");
    Console.WriteLine("\nDone.");
    return;
}

if (mode == "resort-check")
{
    // Proves that the app's Auto-sort (same constraints + tiers) reproduces the
    // saved order of every build — i.e. the reviewed rules live in code, not in the file.
    var rulesStore = new OrderRuleStore(paths);
    foreach (var build in buildStore.List())
    {
        var entries = build.Entries.ToList();
        var constraints = OrderRuleStore.BuildConstraintMap(library.List(), rulesStore.Load());
        LccOrderService.WithLccRequires(constraints, entries, lcc);
        var sorted = InstallOrderService.SortByMetadata(entries, constraints,
            tierOf: e => LccOrderService.TierFor(e, lcc)).Sorted;
        var moved = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (!ReferenceEquals(entries[i], sorted[i]))
                moved.Add($"{i + 1}: {entries[i].Tp2 ?? entries[i].IncludeBuild} → {sorted[i].Tp2 ?? sorted[i].IncludeBuild}");
        }
        Console.WriteLine($"\"{build.Name}\" ({entries.Count} entries): " +
            (moved.Count == 0 ? "Auto-sort leaves the order UNCHANGED ✔" : $"{moved.Count} position(s) would change:"));
        foreach (var m in moved.Take(15)) Console.WriteLine("   " + m);
    }
    return;
}

if (mode != "superpack")
{
    Console.WriteLine($"Unknown mode '{mode}'. Use: seed | superpack | resort-check");
    return;
}

// ======================= SUPERPACK =======================
// Classify every library mod: BG:EE-side (installed before EET) vs EET-side.
var special = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "eet", "eet_end", "eet_gui", "dlcmerger",
    "iwd1_eet", "iwd2_eet", "nwnforbg", "iwd_eet-party-banter", "iwd_eet_end", "iwd_eet_integration",
    "how_eet",            // incompatible with IWD1_EET — deliberately excluded
    "deadgardens",        // LCC: conflicts with Check the Bodies (which is bigger) — excluded
    "g3anniversary",      // joke mini-quest — excluded on request
    "irememberyou",       // review: tp2 references folder 'irememberyou' but package folder differs; ar4900.bcs not EET-aware — broken
    "dnt",                // review: unfinished v0.9 anime crossover (Tomoyo/Clannad) — excluded on quality grounds
    "eefixpack",          // handled explicitly: installed on BOTH instances before the EET conversion
    "stratagems",         // hardcore selection, anchored in the tactics tier
    // secondary tp2s inside packages that are not standalone mods:
    "eet_modconverter", "bgee_to_eet_mod_checker",
};

var preEet = new List<ModPackage>();
var eetSide = new List<(ModPackage Pkg, string Note)>();
var skipped = new List<string>();

foreach (var pkg in library.List().DistinctBy(p => InstallOrderService.NormalizeKey(p.Tp2RelativePath)))
{
    var key = InstallOrderService.NormalizeKey(pkg.Tp2RelativePath);
    if (special.Contains(key)) continue;

    var lccMod = lcc.FindByTp2(key);
    var games = lccMod?.Games.Select(GameCompatibilityValidator.NormalizeToken).ToHashSet() ?? [];

    if (games.Count == 0)
    {
        eetSide.Add((pkg, "unknown compat — verify"));
    }
    else if (games.Contains("EET"))
    {
        eetSide.Add((pkg, ""));
    }
    else if ((games.Contains("BGEE") || games.Contains("SOD")) && !games.Contains("BG2EE"))
    {
        preEet.Add(pkg);
    }
    else if (games.Contains("BG2EE"))
    {
        eetSide.Add((pkg, "declares BG2:EE but not EET — verify"));
    }
    else
    {
        skipped.Add($"{pkg.Metadata.Name ?? key} (games: {string.Join(",", games)})");
    }
}

Console.WriteLine($"\nClassified: {preEet.Count} BG:EE-side, {eetSide.Count} EET-side, {skipped.Count} skipped.");
foreach (var s in skipped) Console.WriteLine($"[skip] {s}");
foreach (var (pkg, note) in eetSide.Where(x => x.Note.Length > 0))
    Console.WriteLine($"[verify] {pkg.Metadata.Name ?? pkg.ModFolderName}: {note}");
if (Has("how_eet")) Console.WriteLine("[note] HoW_EET excluded (already included in IWD1_EET).");
if (Has("deadgardens")) Console.WriteLine("[note] Dead Gardens excluded (LCC conflict with Check the Bodies).");

// Community ordering rules now live in OrderRuleStore.BuiltInRules (Core),
// shared with the app, so the constraint map here is exactly the app's.
Dictionary<string, ModMetadata> Constraints(IEnumerable<BuildEntry> entries)
{
    var map = OrderRuleStore.BuildConstraintMap(library.List(), new OrderRuleStore(paths).Load());
    LccOrderService.WithLccRequires(map, entries, lcc);
    return map;
}

List<BuildEntry> Sorted(List<BuildEntry> entries, Dictionary<BuildEntry, int>? anchorRanks = null)
{
    if (entries.Count <= 1) return entries;
    var result = InstallOrderService.SortByMetadata(entries, Constraints(entries),
        tierOf: e => anchorRanks is not null && anchorRanks.TryGetValue(e, out var rank)
            ? rank
            : LccOrderService.TierFor(e, lcc));
    if (result.HadCycle)
        Console.WriteLine($"[warn] ordering cycle among: {string.Join(", ", result.Unresolved)}");
    return result.Sorted;
}

// Component adjustments from the documentation review (see docs/MODPACK-REVIEW.md).
// Removed: components that cannot install on EET, deprecated ones, interactive-only
// ones, documented conflicts, and cheat/twink tweaks a sane default pack should skip.
var removeComponents = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
{
    ["blackhearts"] = [20005],                                  // REQUIRE bg2ee AND NOT eet
    ["drizztsaga"] = [2],                                       // DEPRECATED "Raise the XP cap"
    ["imoen_forever"] = [15, 10],                               // 15: bgee-only; 10: FORBIDDEN by Reflections of Destiny
    ["rot"] = [1, 2],                                           // kit pack (non-EE only), biffing (classic only)
    ["planarspheremod"] = [624, 625],                           // overpowered store; "not bugfixed" teleport add-on
    ["shardsofice"] = [2],                                      // Summon Cow joke component
    ["a7-testyourmettle"] = [11],                               // replaced by author-recommended 12 (75% XP cut)
    ["bg1npc"] = [100, 90],                                     // 100: Tutu/BGT-only; 90: beta, conflicts with inn tweaks
    ["allthingsmazzy"] = [3],                                   // epilogue override suppresses other mods' epilogues
    ["stratagems"] = [3550, 6020, 2080, 4030, 4100, 4240],      // deprecated / IWDEE-only / blocked by Talents of Faerûn
    ["atweaks"] = [150, 152, 153, 155, 156],                    // PnP Fiends family — SCS Improved Fiends (6510) wins
    // Rogue Rebalancing overlaps Talents of Faerûn: ToF's revised HLAs (60200) already contain RR's
    // thief/bard HLAs, ToF 41000 rebalances the same kits, and ToF 81100 replaces the proficiency
    // tables RR 0 edits. Keep RR's content/items/encounters, drop its system rewrites.
    ["rr"] = [0, 1, 2, 4, 5, 6],
    ["gorgon"] = [4],                                           // random loot on beggars/children — economy tweak
    ["cdtweaks"] =
    [
        0,                                                      // Batch Installer (interactive-only)
        2999, 3008, 3020, 3040, 3210, 3183, 3494, 3495, 3496, 3497, 2090, // cheats / twinks / no XP cap
        2050, 2060, 2010, 2020,                                 // 150% XP and 89k BG1/SoD caps (first-option artifacts)
        2080, 2160, 2300, 2350, 2352, 2360, 2370, 2380, 2410,   // auto-skipped when Talents of Faerûn is installed
        6000, 6010, 6020, 6030, 6040, 6050, 6060, 6070, 6080, 6090, 6100, 6110, 6120, 6130, 6140, 6150, 6160, 6170,
        6180, 6190, 6200, 6210, 6220, 6230, 6240, 6250, 6260, 6270, 6280, 6290, 6300, 6310, 6320, 6330, 6340, 6350, // require EEex
    ],
    ["eet_tweaks"] = [1060, 2000, 2050, 2060, 2080],            // Polish-only voices; 2.95M cap; 150% XP; EEex-only
    ["bp-bgt-worldmap"] = [1, 3],                               // impossible on the EE engine
};
var addComponents = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
{
    ["a7-testyourmettle"] = [12],                               // "Reduce by 75% (recommended)"
};

List<int> Adjust(string key, List<int> comps)
{
    if (removeComponents.TryGetValue(key, out var remove)) comps = comps.Where(c => !remove.Contains(c)).ToList();
    if (addComponents.TryGetValue(key, out var add)) comps = comps.Concat(add.Where(c => !comps.Contains(c))).OrderBy(c => c).ToList();
    // BG1NPC readme: the Player-Initiated Dialogues component (200) "must be installed after
    // all other BG1NPC components".
    if (key == "bg1npc" && comps.Remove(200)) comps.Add(200);
    return comps;
}

async Task<BuildEntry> GenericEntry(ModPackage pkg)
{
    var key = InstallOrderService.NormalizeKey(pkg.Tp2RelativePath);
    var comps = Adjust(key, GroupFirstAll(await ListComponents(pkg)));
    if (comps.Count == 0) comps = [0];
    Console.WriteLine($"   {pkg.Metadata.Name ?? key}: {comps.Count} components");
    return await Entry(key, comps);
}

// ---------- build 1: BG1 + SoD (pre-EET, on the BG:EE instance) ----------
{
    Console.WriteLine("\nReading components for the BG:EE-side build…");
    var entries = new List<BuildEntry>();
    if (Has("dlcmerger")) entries.Add(await Entry("dlcmerger", [1, 10]));
    var generic = new List<BuildEntry>();
    // EE Fixpack goes on the BG:EE side too (before the EET conversion reads this instance).
    if (Has("eefixpack")) generic.Add(await GenericEntry(byKey["eefixpack"]));
    foreach (var pkg in preEet) generic.Add(await GenericEntry(pkg));
    entries.AddRange(Sorted(generic));

    SaveBuild(new BuildDefinition
    {
        Name = "Modpack: BG1+SoD (pre-EET)",
        GameType = GameType.BGEE,
        Description = "Everything from the library that belongs on the BG:EE instance BEFORE the EET conversion. " +
                      "DLC Merger first (Steam/GOG). Install on the BG:EE instance, then point the EET entry of " +
                      "\"Modpack: EET Everything\" at that instance's game folder.",
        Entries = entries,
    });
}

// ---------- build 2: EET Everything ----------
{
    Console.WriteLine("\nReading components for the EET build (this takes a while)…");
    var eetRecipe = ModRecipeCatalog.Find("eet");

    // One global topological sort using the SAME knowledge the app's Auto-sort
    // uses (LccOrderService tiers + built-in family rules), so re-sorting in
    // the UI reproduces this order instead of scrambling it.
    var entries = new List<BuildEntry>();
    void Anchored(BuildEntry e, int _) => entries.Add(e);

    if (Has("eet"))
        Anchored(await Entry("eet", [0], tp2Rel: "EET/EET.tp2",
            extraArgs: eetRecipe is null ? null : ModRecipeCatalog.RenderArgs(eetRecipe, null)), 1);

    // EE Fixpack must run on the BG2:EE base BEFORE the EET conversion (tier 10 < EET's 20).
    if (Has("eefixpack")) entries.Add(await GenericEntry(byKey["eefixpack"]));
    foreach (var (pkg, _) in eetSide)
        entries.Add(await GenericEntry(pkg));

    if (Has("iwd1_eet")) Anchored(await Entry("iwd1_eet", GroupFirstAll(await ListComponents(byKey["iwd1_eet"]))), 62);
    if (Has("iwd2_eet")) Anchored(await Entry("iwd2_eet", GroupFirstAll(await ListComponents(byKey["iwd2_eet"]))), 63);
    if (Has("nwnforbg")) Anchored(await Entry("nwnforbg", [0, 10]), 64);
    if (Has("stratagems"))
        Anchored(await Entry("stratagems", Adjust("stratagems", ScsHardcore(await ListComponents(byKey["stratagems"])))), 80);
    if (Has("iwd_eet-party-banter")) Anchored(await Entry("iwd_eet-party-banter", GroupFirstAll(await ListComponents(byKey["iwd_eet-party-banter"]))), 96);
    if (Has("iwd_eet_end")) Anchored(await Entry("iwd_eet_end", GroupFirstAll(await ListComponents(byKey["iwd_eet_end"]))), 97);
    if (Has("iwd_eet_integration")) Anchored(await Entry("iwd_eet_integration", GroupFirstAll(await ListComponents(byKey["iwd_eet_integration"]))), 98);
    if (Has("nwnforbg")) Anchored(await Entry("nwnforbg", [20]), 99);
    if (Has("eet_end")) Anchored(await Entry("eet_end", [0], tp2Rel: "EET_end/EET_end.tp2"), 200);

    entries = Sorted(entries);

    SaveBuild(new BuildDefinition
    {
        Name = "Modpack: EET Everything",
        GameType = GameType.EET,
        Description = "Every EET-capable mod from the library: EET core (SET THE BG:EE PATH on the Builds tab!), " +
                      "content mods, IWD1+IWD2, NWNForBG, gameplay/tactics (Ascension, SCS hardcore, tweaks), " +
                      "IWD-EET finalizers, cosmetics/UI, EET_end last. Install \"Modpack: BG1+SoD (pre-EET)\" " +
                      "on the BG:EE instance FIRST. Review component selections before installing.",
        Entries = entries,
    });
}

Validate([.. buildStore.List().Select(b => b.Name).Where(n => n.StartsWith("Modpack:", StringComparison.OrdinalIgnoreCase))]);
Console.WriteLine("\nDone.");
