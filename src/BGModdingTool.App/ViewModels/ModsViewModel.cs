using System.Collections.ObjectModel;
using System.ComponentModel;
using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;
using BGModdingTool.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BGModdingTool.App.ViewModels;

/// <summary>One item in the background download queue.</summary>
public partial class DownloadJob : ViewModelBase
{
    public required string Url { get; init; }
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "Queued";
    [ObservableProperty] public partial bool IsFailed { get; set; }
    public bool IsPending => !IsFailed && Status is "Queued";
}

/// <summary>A build row in the mod↔build membership matrix.</summary>
public partial class BuildMembershipItem : ViewModelBase
{
    public required string BuildName { get; init; }
    public GameType GameType { get; init; }
    [ObservableProperty] public partial bool IsChecked { get; set; }
    public string Display => $"{BuildName} ({GameType})";
}

public partial class ModsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private bool _queueRunning;
    private bool _syncingMemberships;

    /// <summary>Full library (used e.g. by the build editor's mod picker).</summary>
    public ObservableCollection<ModPackage> Items { get; } = [];
    /// <summary>Search-filtered view shown in the library list.</summary>
    public ObservableCollection<ModPackage> FilteredItems { get; } = [];
    public ObservableCollection<DownloadJob> DownloadQueue { get; } = [];
    /// <summary>Game-compatible builds with a checkbox = "this mod is in that build".</summary>
    public ObservableCollection<BuildMembershipItem> BuildMemberships { get; } = [];

    [ObservableProperty]
    public partial ModPackage? Selected { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string DownloadUrl { get; set; } = "";

    [ObservableProperty] public partial bool HasLcc { get; set; }
    [ObservableProperty] public partial string LccDescription { get; set; } = "";
    [ObservableProperty] public partial string LccMetaLine { get; set; } = "";
    [ObservableProperty] public partial string LccPageUrl { get; set; } = "";
    public ObservableCollection<LccNoteItem> LccNotes { get; } = [];
    /// <summary>Mods referenced as [[id]] inside the LCC description, as clickable links.</summary>
    public ObservableCollection<LccNoteRef> LccDescriptionLinks { get; } = [];
    [ObservableProperty] public partial string? ReadmePath { get; set; }
    public bool HasReadme => ReadmePath is not null;
    partial void OnReadmePathChanged(string? value) => OnPropertyChanged(nameof(HasReadme));

    [RelayCommand]
    private void OpenReadme()
    {
        if (ReadmePath is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ReadmePath) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open the readme: " + ex.Message; }
    }

    [RelayCommand]
    private void SearchWeb()
    {
        if (Selected is null) return;
        OpenUrl(BuildsViewModel.GoogleUrl(Selected.Metadata.Name ?? Selected.ModFolderName));
    }

    public ModsViewModel(MainViewModel main)
    {
        _main = main;
        Reload();
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open the link: " + ex.Message; }
    }

    partial void OnSelectedChanged(ModPackage? value)
    {
        LccNotes.Clear();
        LccDescriptionLinks.Clear();
        HasLcc = false;
        LccDescription = "";
        LccMetaLine = "";
        LccPageUrl = "";
        ReadmePath = value is null ? null : ModLibrary.FindReadme(value);
        RefreshMemberships();
        if (value is null) return;
        var lcc = _main.Lcc.FindByTp2(value.Tp2LogName);
        if (lcc is null) return;
        HasLcc = true;
        var (descriptionText, descriptionRefs) = _main.Lcc.ResolveRefs(lcc.Description);
        LccDescription = descriptionText;
        foreach (var r in descriptionRefs) LccDescriptionLinks.Add(r);
        LccPageUrl = LccDatabase.PageUrl(lcc.Id);
        foreach (var n in lcc.Notes) LccNotes.Add(LccNoteItem.Resolved(n, _main.Lcc));
        var quality = lcc.Safe switch { 2 => "🟢 good quality", 1 => "🟡 with caveats", _ => "🟥 problematic/obsolete" };
        LccMetaLine = $"LCC: {quality} · games: {string.Join(", ", lcc.Games)}" +
                      (lcc.LastUpdate is null ? "" : $" · updated: {lcc.LastUpdate}") +
                      (lcc.Status.Count > 0 ? $" · status: {string.Join(", ", lcc.Status)}" : "");
    }

    [RelayCommand]
    private Task DownloadLccAsync() => _main.DownloadLccAsync();

    public void Reload()
    {
        Items.Clear();
        foreach (var m in _main.ModLibrary.List()) Items.Add(m);
        ApplyFilter();
        RefreshMemberships();
        _main.Catalog?.Refresh(); // keep "in library" flags in the catalog current
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var filter = SearchText.Trim();
        foreach (var m in Items)
        {
            if (filter.Length == 0
                || (m.Metadata.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || m.ModFolderName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (m.Metadata.Author?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                FilteredItems.Add(m);
            }
        }
    }

    // ---------- mod ↔ build membership matrix ----------

    private bool EntryMatchesMod(BuildEntry entry, ModPackage mod)
    {
        if (entry.IncludeBuild is not null) return false;
        if (entry.ModId is not null && entry.ModId == mod.Id) return true;
        var entryKey = InstallOrderService.NormalizeKey(entry.Tp2);
        return mod.AllTp2s().Any(t => InstallOrderService.NormalizeKey(t) == entryKey);
    }

    private void RefreshMemberships()
    {
        _syncingMemberships = true;
        BuildMemberships.Clear();
        if (Selected is not null)
        {
            // Declared games from the mod's own metadata + LCC; empty = unknown = compatible everywhere.
            var declared = new List<string>(Selected.Metadata.Games);
            var lcc = _main.Lcc.FindByTp2(Selected.Tp2LogName);
            if (lcc is not null) declared.AddRange(lcc.Games);

            foreach (var build in _main.BuildStore.List())
            {
                if (!GameCompatibilityValidator.IsCompatible(build.GameType, declared)) continue;
                var item = new BuildMembershipItem
                {
                    BuildName = build.Name,
                    GameType = build.GameType,
                    IsChecked = build.Entries.Any(e => EntryMatchesMod(e, Selected)),
                };
                item.PropertyChanged += OnMembershipToggled;
                BuildMemberships.Add(item);
            }
        }
        _syncingMemberships = false;
    }

    private void OnMembershipToggled(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingMemberships || e.PropertyName != nameof(BuildMembershipItem.IsChecked)) return;
        if (sender is not BuildMembershipItem item || Selected is null) return;

        var build = _main.BuildStore.Load(item.BuildName);
        if (build is null) { _main.Status = $"Build \"{item.BuildName}\" no longer exists."; return; }
        var mod = Selected;

        if (item.IsChecked)
        {
            if (!build.Entries.Any(en => EntryMatchesMod(en, mod)))
                _ = AddToBuildAsync(build, mod);
        }
        else
        {
            var removed = build.Entries.RemoveAll(en => EntryMatchesMod(en, mod));
            if (removed > 0)
            {
                _main.BuildStore.Save(build);
                _main.Status = $"Removed \"{mod.Metadata.Name ?? mod.ModFolderName}\" from build \"{build.Name}\".";
                AppLog.Info($"removed {mod.Tp2LogName} from build \"{build.Name}\" (Mods tab)");
            }
            _main.Builds.Reload();
        }
    }

    /// <summary>
    /// Adds the mod to a build. The English language index is looked up with
    /// WeiDU on a worker thread — never block the UI thread on it (a blocking
    /// wait deadlocks with the awaits inside the runner).
    /// </summary>
    private async Task AddToBuildAsync(BuildDefinition build, ModPackage mod)
    {
        var name = mod.Metadata.Name ?? mod.ModFolderName;
        try
        {
            var languageIndex = 0;
            var weiduExe = _main.ResolveWeiduPath();
            if (weiduExe is not null)
            {
                _main.Status = $"Adding \"{name}\" to build \"{build.Name}\"…";
                // Index 0 is often Russian/French; ask WeiDU for the English one.
                languageIndex = await Task.Run(() =>
                    new WeiduRunner(weiduExe).FindLanguageIndexAsync(mod.RootPath, mod.Tp2RelativePath));
            }
            // Re-read: the build may have changed while WeiDU was running.
            var current = _main.BuildStore.Load(build.Name) ?? build;
            if (current.Entries.Any(en => EntryMatchesMod(en, mod))) return;
            current.Entries.Add(new BuildEntry
            {
                ModId = mod.Id,
                Tp2 = mod.Tp2LogName,
                LanguageIndex = languageIndex,
                Components = [0],
            });
            _main.BuildStore.Save(current);
            AppLog.Info($"added {mod.Tp2LogName} to build \"{current.Name}\" (Mods tab, language {languageIndex})");
            _main.Status = $"Added \"{name}\" to build \"{current.Name}\" " +
                           "(component 0; adjust components and run Auto-sort on the Builds tab).";
        }
        catch (Exception ex)
        {
            AppLog.Error($"adding {mod.Tp2LogName} to build \"{build.Name}\" failed", ex);
            _main.Status = $"Could not add \"{name}\" to build \"{build.Name}\": {ex.Message}";
        }
        finally
        {
            _main.Builds.Reload();
        }
    }

    // ---------- background download queue ----------

    /// <summary>Adds a URL to the download queue and starts processing it in the background.</summary>
    public void EnqueueDownload(string url, string? name = null)
    {
        url = url.Trim();
        if (url.Length == 0) return;
        DownloadQueue.Add(new DownloadJob { Url = url, Name = name ?? url });
        _ = ProcessQueueAsync();
    }

    [RelayCommand]
    private void DismissJob(DownloadJob? job)
    {
        // Only failed or not-yet-started jobs can be removed; the running one finishes on its own.
        if (job is { IsFailed: true } or { Status: "Queued" })
            DownloadQueue.Remove(job);
    }

    private async Task ProcessQueueAsync()
    {
        if (_queueRunning) return;
        _queueRunning = true;
        try
        {
            while (DownloadQueue.FirstOrDefault(j => j.IsPending) is { } job)
            {
                await RunJobAsync(job);
            }
        }
        finally { _queueRunning = false; }
    }

    private async Task RunJobAsync(DownloadJob job)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "bgmt-download-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            job.Status = "Starting…";
            using var downloader = new ModDownloader();
            var progress = new Progress<string>(s => job.Status = s);
            var archive = await downloader.DownloadArchiveAsync(job.Url, tempDir, progress);
            job.Status = "Importing…";
            var pkg = await _main.ModLibrary.ImportArchiveAsync(archive);
            Reload();
            _main.Status = $"Downloaded and imported mod \"{pkg.Metadata.Name ?? pkg.ModFolderName}\".";
            DownloadQueue.Remove(job);
        }
        catch (Exception ex)
        {
            job.IsFailed = true;
            job.Status = "Failed: " + ex.Message;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [RelayCommand]
    private void DownloadFromUrl()
    {
        var url = DownloadUrl.Trim();
        if (url.Length == 0)
        {
            _main.Status = "Paste a mod link (GitHub repo, release, or a direct archive link).";
            return;
        }
        EnqueueDownload(url);
        DownloadUrl = "";
    }

    [RelayCommand]
    private void OpenModFolder()
    {
        if (Selected is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Selected.RootPath) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open folder: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ImportArchivesAsync()
    {
        var files = await DialogService.PickFilesAsync(
            "Select mod archives", "Mod archives", "*.zip", "*.7z", "*.rar", "*.tar.gz", "*.iemod");
        if (files.Count == 0) return;

        IsBusy = true;
        try
        {
            foreach (var file in files)
            {
                _main.Status = $"Importing: {Path.GetFileName(file)}…";
                try
                {
                    var pkg = await _main.ModLibrary.ImportArchiveAsync(file);
                    _main.Status = $"Imported mod \"{pkg.Metadata.Name ?? pkg.ModFolderName}\".";
                }
                catch (Exception ex)
                {
                    _main.Status = $"Import failed for {Path.GetFileName(file)}: {ex.Message}";
                }
            }
            Reload();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var folder = await DialogService.PickFolderAsync("Select a directory with an extracted mod");
        if (folder is null) return;
        IsBusy = true;
        try
        {
            var pkg = await _main.ModLibrary.ImportDirectoryAsync(folder);
            Reload();
            _main.Status = $"Imported mod \"{pkg.Metadata.Name ?? pkg.ModFolderName}\".";
        }
        catch (Exception ex) { _main.Status = "Import failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        try
        {
            _main.ModLibrary.Delete(Selected);
            _main.Status = $"Removed mod \"{Selected.Metadata.Name ?? Selected.ModFolderName}\" from the library.";
            Selected = null;
            Reload();
        }
        catch (Exception ex) { _main.Status = "Deletion failed: " + ex.Message; }
    }
}
