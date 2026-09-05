using System.Text.Json;
using System.Text.Json.Serialization;
using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

/// <summary>One mod entry from the LCC community database.</summary>
public sealed class LccMod
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>English description when the translation overlay is loaded (French otherwise).</summary>
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>Community warnings/remarks about the mod (English when overlay loaded).</summary>
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
    [JsonPropertyName("games")] public List<string> Games { get; set; } = [];
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = [];
    /// <summary>Quality: 2 = good, 1 = caveats, 0 = problematic/obsolete/non-WeiDU.</summary>
    [JsonPropertyName("safe")] public int Safe { get; set; }
    [JsonPropertyName("tp2")] public string Tp2 { get; set; } = "";
    [JsonPropertyName("urls")] public List<string> Urls { get; set; } = [];
    [JsonPropertyName("status")] public List<string> Status { get; set; } = [];
    [JsonPropertyName("last_update")] public string? LastUpdate { get; set; }
    [JsonPropertyName("compatibilities")] public LccCompat? Compatibilities { get; set; }

    /// <summary>Release/update year for list rows ("" when unknown).</summary>
    [JsonIgnore] public string Year => LastUpdate is { Length: >= 4 } ? LastUpdate[..4] : "";
    /// <summary>Quality marker: 🟢 good, 🟡 caveats, 🟥 problematic/obsolete.</summary>
    [JsonIgnore] public string QualityEmoji => Safe switch { 2 => "🟢", 1 => "🟡", _ => "🟥" };
    /// <summary>Categories with English display labels.</summary>
    [JsonIgnore] public string CategoriesEnglish => LccCategories.EnglishLabels(Categories);
}

/// <summary>
/// LCC compatibility lists mix mod ids (numbers) with engine requirements
/// (strings like "ToB", "EE version &gt;= 2.6"), so they are parsed leniently.
/// </summary>
public sealed class LccCompat
{
    [JsonPropertyName("conflicts")] public List<JsonElement> ConflictsRaw { get; set; } = [];
    [JsonPropertyName("requires")] public List<JsonElement> RequiresRaw { get; set; } = [];

    [JsonIgnore] public IEnumerable<int> ConflictIds =>
        ConflictsRaw.Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32());
    [JsonIgnore] public IEnumerable<int> RequireIds =>
        RequiresRaw.Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32());
    [JsonIgnore] public IEnumerable<string> RequireNotes =>
        RequiresRaw.Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!);
}

/// <summary>A resolved [[id]] cross-reference from an LCC note.</summary>
public sealed record LccNoteRef(int Id, string Name, string Url);

/// <summary>Per-mod translation overlay entry (db/mods_en.json).</summary>
internal sealed class LccTranslation
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("notes")] public List<string>? Notes { get; set; }
}

/// <summary>
/// La Couronne de Cuivre (github.com/RiwsPy/lcc-docs) community mod database:
/// ~2100 IE mods with game compatibility, quality ratings and known conflicts.
/// Cached locally at DataRoot/lcc-mods.json; refreshed on demand.
/// </summary>
public sealed class LccDatabase(AppPaths paths)
{
    /// <summary>The public mod catalog page; #m&lt;id&gt; anchors point at individual mods.</summary>
    public const string SiteBaseUrl = "https://riwspy.github.io/lcc-docs/en/";

    private const string SourceUrl = "https://raw.githubusercontent.com/RiwsPy/lcc-docs/main/db/mods.json";
    private const string EnglishUrl = "https://raw.githubusercontent.com/RiwsPy/lcc-docs/main/db/mods_en.json";

    private Dictionary<string, LccMod>? _byTp2;
    private Dictionary<int, LccMod>? _byId;

    public string CachePath => Path.Combine(paths.DataRoot, "lcc-mods.json");
    public string EnglishCachePath => Path.Combine(paths.DataRoot, "lcc-mods-en.json");
    public bool IsAvailable => File.Exists(CachePath);
    public DateTime? CacheDate => IsAvailable ? File.GetLastWriteTime(CachePath) : null;
    public int Count { get { EnsureLoaded(); return _byId?.Count ?? 0; } }

    public async Task<int> DownloadAsync(HttpClient? http = null, CancellationToken ct = default)
    {
        var own = http is null;
        http ??= new HttpClient();
        try
        {
            var json = await http.GetStringAsync(SourceUrl, ct);
            // Validate before replacing the cache.
            var mods = JsonSerializer.Deserialize<List<LccMod>>(json) ?? [];
            if (mods.Count == 0) throw new InvalidOperationException("The downloaded LCC database is empty.");
            File.WriteAllText(CachePath + ".tmp", json);
            File.Move(CachePath + ".tmp", CachePath, overwrite: true);

            try
            {
                var enJson = await http.GetStringAsync(EnglishUrl, ct);
                _ = JsonSerializer.Deserialize<List<LccTranslation>>(enJson);
                File.WriteAllText(EnglishCachePath + ".tmp", enJson);
                File.Move(EnglishCachePath + ".tmp", EnglishCachePath, overwrite: true);
            }
            catch { /* base DB works without the translation overlay */ }

            _byTp2 = null;
            _byId = null;
            return mods.Count;
        }
        finally
        {
            if (own) http.Dispose();
        }
    }

    /// <summary>All mods in the database (empty when no cache is available).</summary>
    public IReadOnlyCollection<LccMod> All()
    {
        EnsureLoaded();
        return _byId is null ? Array.Empty<LccMod>() : _byId.Values;
    }

    public LccMod? FindByTp2(string tp2)
    {
        EnsureLoaded();
        if (_byTp2 is null) return null;
        return _byTp2.TryGetValue(InstallOrderService.NormalizeKey(tp2), out var mod) ? mod : null;
    }

    public static string PageUrl(int id) => $"{SiteBaseUrl}#m{id}";

    /// <summary>
    /// Resolves [[id]] cross-references in an LCC note to mod names and
    /// returns the readable text plus links to the referenced mods' LCC pages.
    /// </summary>
    public (string Text, List<LccNoteRef> Refs) ResolveRefs(string note)
    {
        var refs = new List<LccNoteRef>();
        var text = System.Text.RegularExpressions.Regex.Replace(note, @"\[\[(\d+)\]\]", match =>
        {
            var id = int.Parse(match.Groups[1].Value);
            var referenced = FindById(id);
            var name = referenced?.Name ?? $"mod #{id}";
            if (!refs.Any(r => r.Id == id))
                refs.Add(new LccNoteRef(id, name, PageUrl(id)));
            return $"\"{name}\"";
        });
        return (text, refs);
    }

    public LccMod? FindById(int id)
    {
        EnsureLoaded();
        return _byId is not null && _byId.TryGetValue(id, out var mod) ? mod : null;
    }

    private void EnsureLoaded()
    {
        if (_byTp2 is not null || !IsAvailable) return;
        try
        {
            var mods = JsonSerializer.Deserialize<List<LccMod>>(File.ReadAllText(CachePath)) ?? [];
            _byTp2 = new Dictionary<string, LccMod>(StringComparer.OrdinalIgnoreCase);
            _byId = [];
            foreach (var mod in mods)
            {
                _byId[mod.Id] = mod;
                if (!string.IsNullOrEmpty(mod.Tp2) && mod.Tp2 != "non-weidu")
                    _byTp2.TryAdd(InstallOrderService.NormalizeKey(mod.Tp2), mod);
            }

            // English overlay: replace description/notes where a translation exists.
            if (File.Exists(EnglishCachePath))
            {
                var translations = JsonSerializer.Deserialize<List<LccTranslation>>(File.ReadAllText(EnglishCachePath)) ?? [];
                foreach (var t in translations)
                {
                    if (!_byId.TryGetValue(t.Id, out var mod)) continue;
                    if (!string.IsNullOrWhiteSpace(t.Description)) mod.Description = t.Description;
                    if (t.Notes is { Count: > 0 }) mod.Notes = t.Notes;
                }
            }
        }
        catch
        {
            _byTp2 = null;
            _byId = null; // corrupted cache — behave as unavailable
        }
    }
}
