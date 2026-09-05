using System.Collections.ObjectModel;
using BGModdingTool.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BGModdingTool.App.ViewModels;

/// <summary>A URL on a catalog mod, with a one-click download when it looks fetchable.</summary>
public sealed record CatalogUrlItem(string Url, bool IsDownloadable);

/// <summary>A catalog row: the LCC mod plus whether it is already in the local library.</summary>
public sealed record CatalogItem(LccMod Mod, bool InLibrary)
{
    public string InLibraryMark => InLibrary ? "✔ in library" : "";
}

/// <summary>
/// In-app browser for the LCC community catalog (~2100 mods): search, filter
/// by game, read descriptions/warnings, and queue GitHub downloads straight
/// into the mod library.
/// </summary>
public partial class CatalogViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private bool _syncingFilters;

    public ObservableCollection<CatalogItem> Items { get; } = [];
    public ObservableCollection<LccNoteItem> Notes { get; } = [];
    public ObservableCollection<CatalogUrlItem> Urls { get; } = [];
    public ObservableCollection<string> CategoryFilters { get; } = ["All categories"];
    public string[] GameFilters { get; } =
        ["All games", "BGEE", "SoD", "BG2EE", "EET", "IWDEE", "BGT", "BG2", "BG", "Tutu", "IWD", "IWD2", "PST", "PSTEE"];

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial string SelectedGameFilter { get; set; } = "All games";
    [ObservableProperty] public partial string SelectedCategoryFilter { get; set; } = "All categories";
    [ObservableProperty] public partial CatalogItem? Selected { get; set; }
    [ObservableProperty] public partial string DetailsMeta { get; set; } = "";
    /// <summary>Description with [[id]] references resolved to mod names.</summary>
    [ObservableProperty] public partial string DescriptionText { get; set; } = "";
    public ObservableCollection<LccNoteRef> DescriptionLinks { get; } = [];
    [ObservableProperty] public partial string CountInfo { get; set; } = "";
    [ObservableProperty] public partial bool IsInLibrary { get; set; }

    public CatalogViewModel(MainViewModel main)
    {
        _main = main;
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSelectedGameFilterChanged(string value) => Refresh();
    partial void OnSelectedCategoryFilterChanged(string value) { if (!_syncingFilters) Refresh(); }

    private void RebuildCategoryFilters()
    {
        var labels = _main.Lcc.All()
            .SelectMany(m => m.Categories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(LccCategories.EnglishLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (CategoryFilters.Count == labels.Count + 1) return;

        _syncingFilters = true;
        var current = SelectedCategoryFilter;
        CategoryFilters.Clear();
        CategoryFilters.Add("All categories");
        foreach (var label in labels) CategoryFilters.Add(label);
        SelectedCategoryFilter = CategoryFilters.Contains(current) ? current : "All categories";
        _syncingFilters = false;
    }

    public void Refresh()
    {
        RebuildCategoryFilters();
        Items.Clear();
        var filter = SearchText.Trim();
        var game = SelectedGameFilter == "All games" ? null : SelectedGameFilter;
        var category = SelectedCategoryFilter == "All categories" ? null : SelectedCategoryFilter;
        var all = _main.Lcc.All()
            .Where(m =>
                (game is null || m.Games.Contains(game, StringComparer.OrdinalIgnoreCase)) &&
                (category is null || m.Categories.Any(c =>
                    LccCategories.EnglishLabel(c).Equals(category, StringComparison.OrdinalIgnoreCase))) &&
                (filter.Length == 0
                 || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                 || m.Tp2.Contains(filter, StringComparison.OrdinalIgnoreCase)
                 || m.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Library membership by normalized tp2 key, computed once per refresh.
        var libraryKeys = _main.ModLibrary.List()
            .SelectMany(p => p.AllTp2s())
            .Select(InstallOrderService.NormalizeKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in all)
        {
            var inLibrary = m.Tp2 != "non-weidu" &&
                            libraryKeys.Contains(InstallOrderService.NormalizeKey(m.Tp2));
            Items.Add(new CatalogItem(m, inLibrary));
        }
        CountInfo = _main.Lcc.IsAvailable
            ? $"{all.Count} of {_main.Lcc.Count} mods"
            : "LCC database not downloaded yet — use \"Update LCC database\" on the Mods tab.";
    }

    partial void OnSelectedChanged(CatalogItem? value)
    {
        Notes.Clear();
        Urls.Clear();
        DescriptionLinks.Clear();
        DetailsMeta = "";
        DescriptionText = "";
        IsInLibrary = false;
        if (value is null) return;
        var mod = value.Mod;
        var (descriptionText, descriptionRefs) = _main.Lcc.ResolveRefs(mod.Description);
        DescriptionText = descriptionText;
        foreach (var r in descriptionRefs) DescriptionLinks.Add(r);

        var quality = mod.Safe switch { 2 => "🟢 good quality", 1 => "🟡 with caveats", _ => "🟥 problematic/obsolete" };
        DetailsMeta = $"{quality} · games: {string.Join(", ", mod.Games)}" +
                      (mod.Categories.Count > 0 ? $" · {mod.CategoriesEnglish}" : "") +
                      (mod.LastUpdate is null ? "" : $" · updated: {mod.LastUpdate}");

        foreach (var n in mod.Notes) Notes.Add(LccNoteItem.Resolved(n, _main.Lcc, "⚠ "));
        foreach (var req in mod.Compatibilities?.RequireNotes ?? [])
            Notes.Add(LccNoteItem.Plain("Requires: " + req));

        foreach (var url in mod.Urls)
            Urls.Add(new CatalogUrlItem(url, IsDownloadableUrl(url)));

        IsInLibrary = value.InLibrary;
    }

    private static bool IsDownloadableUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
         || url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
         || url.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
         || url.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)
         || url.EndsWith(".iemod", StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void Download(CatalogUrlItem? item)
    {
        if (item is null || Selected is null) return;
        _main.Mods.EnqueueDownload(item.Url, Selected.Mod.Name);
        _main.Status = $"Queued download: {Selected.Mod.Name} — watch the queue on the Mods tab.";
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open the link: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenLccPage()
    {
        if (Selected is not null) OpenUrl(LccDatabase.PageUrl(Selected.Mod.Id));
    }

    [RelayCommand]
    private void SearchWeb()
    {
        if (Selected is not null) OpenUrl(BuildsViewModel.GoogleUrl(Selected.Mod.Name));
    }
}
