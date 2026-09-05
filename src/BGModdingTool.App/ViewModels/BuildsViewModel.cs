using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;
using BGModdingTool.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BGModdingTool.App.ViewModels;

public partial class BuildEntryViewModel : ViewModelBase
{
    [ObservableProperty] public partial string Tp2 { get; set; } = "";
    [ObservableProperty] public partial string? ModId { get; set; }
    [ObservableProperty] public partial string? IncludeBuildName { get; set; }
    [ObservableProperty] public partial string ModDisplayName { get; set; } = "";
    /// <summary>1-based position in the build (kept current by the parent view model).</summary>
    [ObservableProperty] public partial int Position { get; set; }
    /// <summary>LCC category / install-order tier label shown on the row (e.g. "Quête").</summary>
    [ObservableProperty] public partial string TierLabel { get; set; } = "";
    [ObservableProperty] public partial int LanguageIndex { get; set; }
    /// <summary>Component numbers as space-separated text.</summary>
    [ObservableProperty] public partial string ComponentsText { get; set; } = "";
    /// <summary>Extra WeiDU arguments, appended verbatim.</summary>
    [ObservableProperty] public partial string ExtraArgsText { get; set; } = "";
    /// <summary>Answers piped to the installer's stdin, one per line.</summary>
    [ObservableProperty] public partial string StdinText { get; set; } = "";

    public bool IsInclude => IncludeBuildName is not null;
    public bool IsMod => IncludeBuildName is null;

    public List<int> ParseComponents() => [.. ComponentsText
        .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
        .Select(s => int.TryParse(s, out var n) ? n : -1)
        .Where(n => n >= 0)];

    public BuildEntry ToEntry() => new()
    {
        IncludeBuild = IncludeBuildName,
        ModId = ModId,
        Tp2 = Tp2,
        LanguageIndex = LanguageIndex,
        Components = ParseComponents(),
        ExtraArgs = string.IsNullOrWhiteSpace(ExtraArgsText) ? null : ExtraArgsText.Trim(),
        StdinInput = string.IsNullOrWhiteSpace(StdinText) ? null : StdinText,
    };

    public static BuildEntryViewModel From(BuildEntry entry, string modDisplayName) => new()
    {
        IncludeBuildName = entry.IncludeBuild,
        ModId = entry.ModId,
        Tp2 = entry.Tp2,
        LanguageIndex = entry.LanguageIndex,
        ComponentsText = string.Join(' ', entry.Components),
        ExtraArgsText = entry.ExtraArgs ?? "",
        StdinText = entry.StdinInput ?? "",
        ModDisplayName = modDisplayName,
    };

    public static BuildEntryViewModel ForInclude(string buildName) => new()
    {
        IncludeBuildName = buildName,
        Tp2 = "",
        ModDisplayName = $"📦 Package: {buildName}",
    };
}

public sealed record LanguageChoice(int Index, string Name)
{
    public override string ToString() => $"{Index}: {Name}";
}

public partial class ComponentChoice : ViewModelBase
{
    public int Number { get; init; }
    public string Label { get; init; } = "";
    /// <summary>
    /// Mutually-exclusive subcomponent group ("Group -> Choice" labels share
    /// the prefix); only one choice per group can be installed.
    /// </summary>
    public string? GroupKey { get; init; }
    public bool IsGrouped => GroupKey is not null;
    /// <summary>
    /// "Install in batch mode"-style components exist only for interactive
    /// installs (they defer the install and write a .bat); with our
    /// non-interactive --force-install-list they must not be selected.
    /// </summary>
    public bool IsInteractiveOnly =>
        System.Text.RegularExpressions.Regex.IsMatch(Label, @"batch mode|batch install|DO NOT USE WITH PROJECT INFINITY",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public string? GroupTooltip => IsInteractiveOnly
        ? "Interactive-only component (batch mode). This tool installs non-interactively, exactly like Project Infinity — leave it unchecked."
        : GroupKey is null ? null : $"One-of group: only a single \"{GroupKey}\" variant can be installed.";
    [ObservableProperty] public partial bool IsSelected { get; set; }
    /// <summary>Thumbnails of the images this component copies (portrait choices), loaded lazily.</summary>
    public ObservableCollection<Avalonia.Media.Imaging.Bitmap> PreviewImages { get; } = [];
    [ObservableProperty] public partial bool HasPreviewImages { get; set; }
}

/// <summary>One recipe-defined installer prompt rendered as a dropdown.</summary>
public partial class StdinQuestionViewModel : ViewModelBase
{
    public string Text { get; init; } = "";
    public List<StdinOption> Options { get; init; } = [];
    [ObservableProperty] public partial StdinOption? SelectedOption { get; set; }
}

public partial class BuildsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly OrderRuleStore _orderRules;
    private readonly Dictionary<(string ModId, int Lang), List<ModComponent>> _componentCache = [];
    private readonly List<ComponentChoice> _allComponentChoices = [];
    private int _loadSeq;
    private bool _syncingSelection;
    private ModRecipe? _currentRecipe;

    public ObservableCollection<BuildDefinition> Items { get; } = [];
    /// <summary>All entries of the selected build, in install order (the model).</summary>
    public ObservableCollection<BuildEntryViewModel> Entries { get; } = [];
    /// <summary>Entries shown in the list: Entries filtered by <see cref="EntryFilter"/>, with positions numbered.</summary>
    public ObservableCollection<BuildEntryViewModel> VisibleEntries { get; } = [];
    [ObservableProperty] public partial string EntryFilter { get; set; } = "";
    public ObservableCollection<LanguageChoice> EntryLanguages { get; } = [];
    public ObservableCollection<ComponentChoice> EntryComponents { get; } = [];
    public ObservableCollection<StdinQuestionViewModel> StdinQuestions { get; } = [];
    public ObservableCollection<string> ValidationIssues { get; } = [];
    public ObservableCollection<LccNoteItem> LccNotes { get; } = [];
    public ObservableCollection<string> ModTp2Variants { get; } = [];
    public GameType[] GameTypes { get; } =
        [GameType.BGEE, GameType.BG2EE, GameType.IWDEE, GameType.EET, GameType.BGT, GameType.BG2Classic];

    [ObservableProperty] public partial BuildDefinition? Selected { get; set; }
    [ObservableProperty] public partial BuildEntryViewModel? SelectedEntry { get; set; }
    [ObservableProperty] public partial LanguageChoice? SelectedEntryLanguage { get; set; }
    [ObservableProperty] public partial string NewBuildName { get; set; } = "";
    [ObservableProperty] public partial string RenameText { get; set; } = "";
    [ObservableProperty] public partial string ComponentFilter { get; set; } = "";
    [ObservableProperty] public partial GameType SelectedGameType { get; set; } = GameType.BG2EE;
    [ObservableProperty] public partial ModPackage? ModToAdd { get; set; }
    [ObservableProperty] public partial string? SelectedTp2Variant { get; set; }
    [ObservableProperty] public partial bool HasMultipleTp2 { get; set; }
    [ObservableProperty] public partial BuildDefinition? BuildToInclude { get; set; }
    [ObservableProperty] public partial bool IsLoadingComponents { get; set; }
    [ObservableProperty] public partial string ComponentsInfo { get; set; } = "";
    [ObservableProperty] public partial string RecipeTitle { get; set; } = "";
    [ObservableProperty] public partial string RecipeInstructions { get; set; } = "";
    [ObservableProperty] public partial bool HasRecipe { get; set; }
    [ObservableProperty] public partial bool RecipeNeedsSourcePath { get; set; }
    [ObservableProperty] public partial GameInstance? SourceInstance { get; set; }
    [ObservableProperty] public partial bool HasLccInfo { get; set; }
    [ObservableProperty] public partial string LccTitle { get; set; } = "";
    [ObservableProperty] public partial string LccPageUrl { get; set; } = "";
    /// <summary>Local readme of the selected entry's mod (null when none ships).</summary>
    [ObservableProperty] public partial string? ReadmePath { get; set; }
    public bool HasReadme => ReadmePath is not null;
    /// <summary>External links for the selected entry: PI metadata (forum, homepage, download) + LCC URLs.</summary>
    public ObservableCollection<LccNoteRef> EntryLinks { get; } = [];

    partial void OnReadmePathChanged(string? value) => OnPropertyChanged(nameof(HasReadme));

    [RelayCommand]
    private void OpenReadme()
    {
        if (ReadmePath is null) return;
        try { Process.Start(new ProcessStartInfo(ReadmePath) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open the readme: " + ex.Message; }
    }

    /// <summary>Opens a web search for the mod in the default browser.</summary>
    public static string GoogleUrl(string modName) =>
        "https://www.google.com/search?q=" + Uri.EscapeDataString($"{modName} baldur's gate mod");

    [RelayCommand]
    private void SearchWeb()
    {
        if (SelectedEntry is null || SelectedEntry.IsInclude) return;
        var name = SelectedEntry.ModDisplayName;
        var bracket = name.IndexOf(" [", StringComparison.Ordinal);
        if (bracket > 0) name = name[..bracket];
        OpenUrl(GoogleUrl(name));
    }

    private void CollectEntryLinks(BuildEntryViewModel? entry, LccMod? lccMod)
    {
        EntryLinks.Clear();
        ReadmePath = null;
        if (entry is null || entry.IsInclude) return;

        var pkg = entry.ModId is null ? null : _main.ModLibrary.List().FirstOrDefault(m => m.Id == entry.ModId);
        if (pkg is not null)
        {
            ReadmePath = ModLibrary.FindReadme(pkg);
            void Add(string label, string? url)
            {
                if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
                if (EntryLinks.Any(l => l.Url.Equals(url, StringComparison.OrdinalIgnoreCase))) return;
                EntryLinks.Add(new LccNoteRef(0, label, url));
            }
            Add("Homepage", pkg.Metadata.Homepage);
            Add("Forum", pkg.Metadata.Forum);
            Add("Download", pkg.Metadata.DownloadUri);
            Add("Readme (web)", pkg.Metadata.Readme);
        }
        foreach (var url in lccMod?.Urls ?? [])
        {
            if (EntryLinks.Any(l => l.Url.Equals(url, StringComparison.OrdinalIgnoreCase))) continue;
            var label = url.Contains("github.com", StringComparison.OrdinalIgnoreCase) ? "GitHub"
                      : url.Contains("gibberlings3", StringComparison.OrdinalIgnoreCase) ? "G3"
                      : url.Contains("spellholdstudios", StringComparison.OrdinalIgnoreCase) ? "SHS"
                      : url.Contains("beamdog", StringComparison.OrdinalIgnoreCase) ? "Beamdog forum"
                      : url.Contains("pocketplane", StringComparison.OrdinalIgnoreCase) ? "PPG"
                      : new Uri(url).Host.Replace("www.", "");
            EntryLinks.Add(new LccNoteRef(0, label, url));
        }
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open the link: " + ex.Message; }
    }
    [ObservableProperty] public partial bool ValidationOk { get; set; } = true;
    /// <summary>Pre-install estimate of hard-limit usage (SPLSTATE) for the current entry list.</summary>
    [ObservableProperty] public partial string LimitsForecast { get; set; } = "";
    [ObservableProperty] public partial bool LimitsOver { get; set; }
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial bool AutoSave { get; set; }
    /// <summary>Archived versions of the selected build (newest first).</summary>
    public ObservableCollection<BuildVersion> HistoryVersions { get; } = [];
    [ObservableProperty] public partial BuildVersion? SelectedHistoryVersion { get; set; }

    private void RefreshHistory()
    {
        HistoryVersions.Clear();
        SelectedHistoryVersion = null;
        if (Selected is null) return;
        foreach (var v in _main.BuildStore.ListHistory(Selected.Name)) HistoryVersions.Add(v);
    }

    /// <summary>Replaces the current build with an archived version (the current one is archived first).</summary>
    [RelayCommand]
    private void RestoreVersion()
    {
        if (Selected is null || SelectedHistoryVersion is null) return;
        var version = _main.BuildStore.LoadVersion(SelectedHistoryVersion.File);
        if (version is null) { _main.Status = "Could not read that version."; return; }
        var name = Selected.Name;
        version.Name = name;
        _main.BuildStore.Save(version);
        AppLog.Info($"restored build \"{name}\" from {SelectedHistoryVersion.File}");
        Reload();
        Selected = Items.FirstOrDefault(b => b.Name == name);
        _main.Status = $"Restored \"{name}\" from {SelectedHistoryVersion.SavedAt:g} ({version.Entries.Count} entries). The replaced version is in history too.";
    }
    private string _savedSnapshot = "";
    private CancellationTokenSource? _autoSaveCts;
    private readonly HashSet<BuildEntryViewModel> _tracked = new(ReferenceEqualityComparer.Instance);

    public ObservableCollection<ModPackage> AvailableMods => _main.Mods.Items;
    public ObservableCollection<GameInstance> AvailableInstances => _main.Instances.Items;

    public BuildsViewModel(MainViewModel main)
    {
        _main = main;
        _orderRules = new OrderRuleStore(main.Paths);
        AutoSave = main.Config.AutoSaveBuilds;
        Entries.CollectionChanged += (_, _) => { RefreshEntryViews(); MarkChanged(); };
        Reload();
    }

    partial void OnAutoSaveChanged(bool value)
    {
        _main.Config.AutoSaveBuilds = value;
        _main.SaveConfig();
        if (value && IsDirty) ScheduleAutoSave();
    }

    // ---------- unsaved-changes tracking ----------

    private string Snapshot() =>
        System.Text.Json.JsonSerializer.Serialize(Entries.Select(e => e.ToEntry()).ToList(), JsonStore.Options);

    /// <summary>Recomputes IsDirty against the last saved state; schedules auto-save when enabled.</summary>
    private void MarkChanged()
    {
        foreach (var e in Entries)
        {
            if (_tracked.Add(e)) e.PropertyChanged += OnEntryPropertyChanged;
        }
        if (Selected is null) { IsDirty = false; return; }
        IsDirty = Snapshot() != _savedSnapshot;
        if (IsDirty && AutoSave) ScheduleAutoSave();
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Position/TierLabel are derived, not persisted.
        if (e.PropertyName is nameof(BuildEntryViewModel.Position) or nameof(BuildEntryViewModel.TierLabel)) return;
        MarkChanged();
    }

    private void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        var cts = _autoSaveCts = new CancellationTokenSource();
        _ = Task.Delay(600, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!cts.IsCancellationRequested && IsDirty && AutoSave && Selected is not null) SaveBuildCore(quiet: true);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Saves whatever is being edited right now (used before switching builds and on app exit).</summary>
    public void SaveIfDirty()
    {
        if (IsDirty && Selected is not null) SaveBuildCore(quiet: false);
    }

    partial void OnSelectedChanging(BuildDefinition? value)
    {
        // Never lose edits when the user picks another build: persist the current one first.
        if (IsDirty && Selected is not null && !ReferenceEquals(value, Selected))
        {
            SaveBuildCore(quiet: false);
            _main.Status = $"Saved \"{Selected.Name}\" before switching builds.";
        }
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        if (Selected is null) return;
        var name = Selected.Name;
        _autoSaveCts?.Cancel();
        IsDirty = false;
        Reload();
        Selected = Items.FirstOrDefault(b => b.Name == name);
        _main.Status = $"Discarded unsaved changes to \"{name}\".";
    }

    partial void OnEntryFilterChanged(string value) => RefreshEntryViews();

    /// <summary>Renumbers positions from the full list and rebuilds the filtered view.</summary>
    private void RefreshEntryViews()
    {
        for (var i = 0; i < Entries.Count; i++) Entries[i].Position = i + 1;

        var filter = EntryFilter.Trim();
        VisibleEntries.Clear();
        foreach (var e in Entries)
        {
            if (filter.Length == 0
                || e.ModDisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.Tp2.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.TierLabel.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.Position.ToString() == filter)
            {
                VisibleEntries.Add(e);
            }
        }
    }

    public void Reload()
    {
        Items.Clear();
        foreach (var b in _main.BuildStore.List()) Items.Add(b);
    }

    // ---------- build list ----------

    partial void OnSelectedChanged(BuildDefinition? value)
    {
        Entries.Clear();
        SelectedEntry = null;
        RenameText = value?.Name ?? "";
        if (value is not null)
        {
            SelectedGameType = value.GameType;
            var mods = _main.ModLibrary.List().ToDictionary(m => m.Id, m => m);
            foreach (var e in value.Entries)
            {
                var vm = BuildEntryViewModel.From(e, DisplayNameFor(e, mods));
                vm.TierLabel = LccOrderService.TierLabel(e, _main.Lcc) ?? "";
                Entries.Add(vm);
            }
        }
        _savedSnapshot = Snapshot();
        MarkChanged();
        ValidateNow();
        RefreshHistory();
    }

    private static string DisplayNameFor(BuildEntry e, Dictionary<string, ModPackage> mods)
    {
        if (e.IncludeBuild is not null) return $"📦 Package: {e.IncludeBuild}";
        if (e.ModId is not null && mods.TryGetValue(e.ModId, out var m))
        {
            var name = m.Metadata.Name ?? m.ModFolderName;
            if (m.AllTp2s().Count() > 1 &&
                !ModPackage.ToLogName(m.Tp2RelativePath).Equals(e.Tp2, StringComparison.OrdinalIgnoreCase))
                name += $" [{Path.GetFileNameWithoutExtension(e.Tp2)}]";
            return name;
        }
        return "(from weidu.log)";
    }

    [RelayCommand]
    private void CreateBuild()
    {
        var name = string.IsNullOrWhiteSpace(NewBuildName) ? $"build-{DateTime.Now:yyyyMMdd-HHmm}" : NewBuildName.Trim();
        _main.BuildStore.Save(new BuildDefinition { Name = name, GameType = SelectedGameType });
        NewBuildName = "";
        Reload();
        Selected = Items.FirstOrDefault(b => b.Name == name);
        _main.Status = $"Created build \"{name}\".";
    }

    [RelayCommand]
    private void RenameBuild()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(RenameText)) return;
        var oldName = Selected.Name;
        var newName = RenameText.Trim();
        if (newName == oldName) return;
        try
        {
            _main.BuildStore.Rename(oldName, newName);
            Reload();
            Selected = Items.FirstOrDefault(b => b.Name == newName);
            _main.Status = $"Renamed build \"{oldName}\" → \"{newName}\" (builds that include it were updated too).";
        }
        catch (Exception ex) { _main.Status = "Rename failed: " + ex.Message; }
    }

    [RelayCommand]
    private void DuplicateBuild()
    {
        if (Selected is null) return;
        try
        {
            var copyName = _main.BuildStore.Duplicate(Selected.Name);
            Reload();
            Selected = Items.FirstOrDefault(b => b.Name == copyName);
            _main.Status = $"Duplicated build as \"{copyName}\".";
        }
        catch (Exception ex) { _main.Status = "Duplication failed: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenBuildsFolder()
    {
        try { Process.Start(new ProcessStartInfo(_main.Paths.BuildsDir) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open folder: " + ex.Message; }
    }

    [RelayCommand]
    private void DeleteBuild()
    {
        if (Selected is null) return;
        _main.BuildStore.Delete(Selected.Name);
        _main.Status = $"Deleted build \"{Selected.Name}\".";
        Selected = null;
        Reload();
    }

    [RelayCommand]
    private void SaveBuild() => SaveBuildCore(quiet: false);

    private void SaveBuildCore(bool quiet)
    {
        if (Selected is null) return;
        _autoSaveCts?.Cancel();
        Selected.GameType = SelectedGameType;
        Selected.Entries = [.. Entries.Select(e => e.ToEntry())];
        _main.BuildStore.Save(Selected);
        _savedSnapshot = Snapshot();
        IsDirty = false;
        ValidateNow();
        RefreshHistory();
        if (!quiet) _main.Status = $"Saved build \"{Selected.Name}\" ({Selected.Entries.Count} entries).";
        else AppLog.Info($"auto-saved build \"{Selected.Name}\" ({Selected.Entries.Count} entries)");
    }

    [RelayCommand]
    private async Task ImportFromWeiduLogAsync()
    {
        var files = await DialogService.PickFilesAsync("Select weidu.log", "weidu.log", "*.log");
        if (files.Count == 0) return;
        var name = string.IsNullOrWhiteSpace(NewBuildName) ? $"import-{DateTime.Now:yyyyMMdd-HHmm}" : NewBuildName.Trim();
        var build = BuildStore.FromWeiduLog(name, SelectedGameType, await File.ReadAllTextAsync(files[0]));
        _main.BuildStore.Save(build);
        NewBuildName = "";
        Reload();
        Selected = Items.FirstOrDefault(b => b.Name == name);
        _main.Status = $"Imported build from weidu.log ({build.Entries.Count} mods).";
    }

    // ---------- entry list editing ----------

    partial void OnModToAddChanged(ModPackage? value)
    {
        ModTp2Variants.Clear();
        SelectedTp2Variant = null;
        if (value is null) { HasMultipleTp2 = false; return; }
        foreach (var v in value.AllTp2s()) ModTp2Variants.Add(v);
        HasMultipleTp2 = ModTp2Variants.Count > 1;
        SelectedTp2Variant = ModTp2Variants.FirstOrDefault();
    }

    [RelayCommand]
    private void AddModEntry()
    {
        if (ModToAdd is null) return;
        var tp2Rel = SelectedTp2Variant ?? ModToAdd.Tp2RelativePath;
        var name = ModToAdd.Metadata.Name ?? ModToAdd.ModFolderName;
        if (ModToAdd.AllTp2s().Count() > 1)
            name += $" [{Path.GetFileNameWithoutExtension(tp2Rel)}]";
        var vm = new BuildEntryViewModel
        {
            ModId = ModToAdd.Id,
            Tp2 = ModPackage.ToLogName(tp2Rel),
            ModDisplayName = name,
            LanguageIndex = 0,
            ComponentsText = "0",
        };
        vm.TierLabel = LccOrderService.TierLabel(vm.ToEntry(), _main.Lcc) ?? "";
        Entries.Add(vm);
        ValidateNow();
        _main.Status = $"Added \"{name}\" to the build (position {Entries.Count}).";

        // Language index 0 is not reliably English — look up the English index in the background.
        var weiduExe = _main.ResolveWeiduPath();
        if (weiduExe is not null)
        {
            var pkg = ModToAdd;
            _ = Task.Run(async () =>
            {
                var idx = await new WeiduRunner(weiduExe).FindLanguageIndexAsync(pkg.RootPath, tp2Rel);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.LanguageIndex = idx);
            });
        }
    }

    [RelayCommand]
    private void AddInclude()
    {
        if (BuildToInclude is null) return;
        if (Selected is not null && BuildToInclude.Name.Equals(Selected.Name, StringComparison.OrdinalIgnoreCase))
        {
            _main.Status = "A build cannot include itself.";
            return;
        }
        Entries.Add(BuildEntryViewModel.ForInclude(BuildToInclude.Name));
        ValidateNow();
    }

    [RelayCommand]
    private void RemoveEntry()
    {
        if (SelectedEntry is null) return;
        var removed = SelectedEntry;
        Entries.Remove(removed);
        ValidateNow();
        _main.Status = $"Removed \"{removed.ModDisplayName}\" ({removed.Tp2}) from the build.";
    }

    [RelayCommand]
    private void MoveEntryUp()
    {
        if (SelectedEntry is null) return;
        var i = Entries.IndexOf(SelectedEntry);
        if (i > 0) { Entries.Move(i, i - 1); ValidateNow(); }
    }

    [RelayCommand]
    private void MoveEntryDown()
    {
        if (SelectedEntry is null) return;
        var i = Entries.IndexOf(SelectedEntry);
        if (i >= 0 && i < Entries.Count - 1) { Entries.Move(i, i + 1); ValidateNow(); }
    }

    /// <summary>Moves an entry to a 1-based position (clamped); used by the context menu and drag-and-drop.</summary>
    public void MoveEntryTo(BuildEntryViewModel entry, int position)
    {
        var from = Entries.IndexOf(entry);
        if (from < 0 || Entries.Count < 2) return;
        var to = Math.Clamp(position, 1, Entries.Count) - 1;
        if (to == from) return;
        Entries.Move(from, to);
        ValidateNow();
        SelectedEntry = entry;
        _main.Status = $"Moved \"{entry.ModDisplayName}\" to position {to + 1}.";
    }

    [RelayCommand]
    private void MoveEntryToTop() { if (SelectedEntry is not null) MoveEntryTo(SelectedEntry, 1); }

    [RelayCommand]
    private void MoveEntryToBottom() { if (SelectedEntry is not null) MoveEntryTo(SelectedEntry, Entries.Count); }

    [RelayCommand]
    private async Task MoveEntryToPositionAsync()
    {
        if (SelectedEntry is null) return;
        var entry = SelectedEntry;
        var answer = await DialogService.PromptAsync("Move entry",
            $"New position for \"{entry.ModDisplayName}\" (1–{Entries.Count}):", entry.Position.ToString());
        if (answer is null) return;
        if (int.TryParse(answer.Trim(), out var pos)) MoveEntryTo(entry, pos);
        else _main.Status = $"\"{answer}\" is not a position number.";
    }

    /// <summary>Inserts a copy of the selected entry right below it (same components/args; edit the copy).</summary>
    [RelayCommand]
    private void DuplicateEntry()
    {
        if (SelectedEntry is null) return;
        var source = SelectedEntry;
        var copy = source.IsInclude
            ? BuildEntryViewModel.ForInclude(source.IncludeBuildName!)
            : BuildEntryViewModel.From(source.ToEntry(), source.ModDisplayName);
        copy.TierLabel = source.TierLabel;
        Entries.Insert(Entries.IndexOf(source) + 1, copy);
        ValidateNow();
        SelectedEntry = copy;
        _main.Status = $"Duplicated \"{source.ModDisplayName}\" (position {copy.Position}).";
    }

    // ---------- sorting ----------

    private Dictionary<string, ModMetadata> LoadConstraints(IEnumerable<BuildEntry>? entriesForRequires = null)
    {
        var map = OrderRuleStore.BuildConstraintMap(_main.ModLibrary.List(), _orderRules.Load());
        if (entriesForRequires is not null)
            LccOrderService.WithLccRequires(map, entriesForRequires, _main.Lcc);
        return map;
    }

    [RelayCommand]
    private void SortByMetadata()
    {
        if (Entries.Count == 0) return;
        var pairs = Entries.Select(vm => (Vm: vm, Entry: vm.ToEntry())).ToList();
        var entries = pairs.Select(p => p.Entry).ToList();
        var result = InstallOrderService.SortByMetadata(
            entries, LoadConstraints(entries),
            tierOf: e => LccOrderService.TierFor(e, _main.Lcc));
        ApplyOrder(result.Sorted, pairs);
        _main.Status = result.HadCycle
            ? $"Sorted, but a dependency cycle was found ({string.Join(", ", result.Unresolved)}) — those entries kept their order."
            : "Sorted automatically: mod metadata, your order rules, LCC dependencies and category tiers.";
    }

    [RelayCommand]
    private async Task SortByMasterListAsync()
    {
        if (Entries.Count == 0) return;
        var files = await DialogService.PickFilesAsync(
            "Select an install-order list (text, one mod per line)", "Order list", "*.txt", "*.ini", "*.log", "*.*");
        if (files.Count == 0) return;
        var pairs = Entries.Select(vm => (Vm: vm, Entry: vm.ToEntry())).ToList();
        var sorted = InstallOrderService.SortByMasterList(
            pairs.Select(p => p.Entry).ToList(), await File.ReadAllTextAsync(files[0]));
        ApplyOrder(sorted, pairs);
        _main.Status = $"Sorted by \"{Path.GetFileName(files[0])}\". Mods not on the list went to the end.";
    }

    private void ApplyOrder(List<BuildEntry> sorted, List<(BuildEntryViewModel Vm, BuildEntry Entry)> pairs)
    {
        var vmByEntry = pairs.ToDictionary(p => p.Entry, p => p.Vm);
        Entries.Clear();
        foreach (var e in sorted) Entries.Add(vmByEntry[e]);
        ValidateNow();
    }

    // ---------- order validation ----------

    /// <summary>Re-checks the current entry order against mod metadata, user rules, LCC data and recipe requirements.</summary>
    public void ValidateNow()
    {
        ValidationIssues.Clear();
        if (Entries.Count == 0) { ValidationOk = true; return; }

        var warnings = new List<string>();
        var def = new BuildDefinition
        {
            Name = Selected?.Name ?? "(editing)",
            GameType = SelectedGameType,
            Entries = [.. Entries.Select(e => e.ToEntry())],
        };
        var flat = _main.BuildStore.ExpandEntries(def, warnings);
        var constraints = LoadConstraints(flat);
        foreach (var w in warnings) ValidationIssues.Add(w);
        foreach (var issue in OrderValidator.Validate(flat, constraints))
            ValidationIssues.Add(issue);
        foreach (var issue in GameCompatibilityValidator.Validate(flat, SelectedGameType, constraints, _main.Lcc))
            ValidationIssues.Add(issue);
        ValidationOk = ValidationIssues.Count == 0;
        UpdateLimitsForecast(flat);
    }

    private void UpdateLimitsForecast(IReadOnlyList<BuildEntry> flat)
    {
        try
        {
            var ledger = ResourceLimits.LoadLedger(Path.Combine(_main.Paths.DataRoot, "limits-ledger.json"));
            var packages = _main.ModLibrary.List();
            string? RootOf(BuildEntry e) =>
                (e.ModId is not null ? packages.FirstOrDefault(p => p.Id == e.ModId) : null)?.RootPath
                ?? packages.FirstOrDefault(p => InstallOrderService.NormalizeKey(p.Tp2LogName) == InstallOrderService.NormalizeKey(e.Tp2))?.RootPath;
            var r = LimitForecast.SplState(flat, SelectedGameType, ledger, RootOf);
            LimitsForecast = LimitForecast.Summarize(r);
            LimitsOver = r.IsOver;
        }
        catch (Exception ex)
        {
            LimitsForecast = "limits forecast unavailable: " + ex.Message;
            LimitsOver = false;
        }
    }

    [RelayCommand]
    private void CheckOrder()
    {
        ValidateNow();
        _main.Status = ValidationOk
            ? "Order OK — no conflicts with metadata, rules, LCC data or recipes."
            : $"{ValidationIssues.Count} order/compatibility problems found — see the list under the build entries.";
    }

    [RelayCommand]
    private void OpenOrderRules()
    {
        try
        {
            var path = _orderRules.EnsureFileExists();
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            _main.Status = $"Order rules: {path} — click \"Check order\" after editing.";
        }
        catch (Exception ex) { _main.Status = "Could not open the rules file: " + ex.Message; }
    }

    // ---------- selected entry details ----------

    partial void OnSelectedEntryChanged(BuildEntryViewModel? value) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadEntryDetailsAsync(value));

    partial void OnSelectedEntryLanguageChanged(LanguageChoice? value)
    {
        if (_syncingSelection || value is null || SelectedEntry is null) return;
        if (SelectedEntry.LanguageIndex == value.Index) return;
        SelectedEntry.LanguageIndex = value.Index;
        var entry = SelectedEntry;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadComponentsAsync(entry, ++_loadSeq));
    }

    partial void OnSourceInstanceChanged(GameInstance? value)
    {
        if (_syncingSelection || value is null || SelectedEntry is null || _currentRecipe?.ArgsTemplate is null) return;
        SelectedEntry.ExtraArgsText = ModRecipeCatalog.RenderArgs(_currentRecipe, value.GamePath) ?? "";
        _main.Status = $"Arguments set with the path of instance \"{value.Name}\".";
    }

    [RelayCommand]
    private async Task PickSourceFolderAsync()
    {
        if (SelectedEntry is null || _currentRecipe?.ArgsTemplate is null) return;
        var folder = await DialogService.PickFolderAsync("Select the source game directory (with chitin.key)");
        if (folder is null) return;
        SelectedEntry.ExtraArgsText = ModRecipeCatalog.RenderArgs(_currentRecipe, folder) ?? "";
        _main.Status = $"Arguments set with path: {folder}";
    }

    private (ModPackage Pkg, string Tp2Rel)? ResolvePackageTp2(BuildEntryViewModel entry)
    {
        if (entry.ModId is null) return null;
        var pkg = _main.ModLibrary.List().FirstOrDefault(m => m.Id == entry.ModId);
        if (pkg is null) return null;
        var entryTp2 = entry.Tp2.Replace('\\', '/');
        var tp2 = pkg.AllTp2s().FirstOrDefault(v =>
            v.Replace('\\', '/').Equals(entryTp2, StringComparison.OrdinalIgnoreCase))
            ?? pkg.Tp2RelativePath;
        return (pkg, tp2);
    }

    private async Task LoadEntryDetailsAsync(BuildEntryViewModel? entry)
    {
        var seq = ++_loadSeq;
        _syncingSelection = true;
        EntryLanguages.Clear();
        EntryComponents.Clear();
        _allComponentChoices.Clear();
        ComponentFilter = "";
        StdinQuestions.Clear();
        SelectedEntryLanguage = null;
        SourceInstance = null;
        _syncingSelection = false;
        ComponentsInfo = "";

        _currentRecipe = entry is null || entry.IsInclude ? null : ModRecipeCatalog.Find(entry.Tp2);
        HasRecipe = _currentRecipe is not null;
        RecipeTitle = _currentRecipe?.Title ?? "";
        RecipeInstructions = _currentRecipe?.Instructions ?? "";
        RecipeNeedsSourcePath = _currentRecipe?.ArgsTemplate?.Contains(ModRecipe.PathPlaceholder) == true;

        // LCC community info: quality flag, warnings and placement advice.
        LccNotes.Clear();
        var lccMod = entry is null || entry.IsInclude ? null : _main.Lcc.FindByTp2(entry.Tp2);
        HasLccInfo = lccMod is not null;
        LccTitle = lccMod is null ? "" :
            $"ℹ LCC: {lccMod.Name}" + lccMod.Safe switch
            {
                2 => " · 🟢",
                1 => " · 🟡 with caveats",
                _ => " · 🟥 problematic/obsolete",
            };
        LccPageUrl = lccMod is null ? "" : LccDatabase.PageUrl(lccMod.Id);
        CollectEntryLinks(entry, lccMod);
        if (lccMod is not null && entry is not null)
        {
            foreach (var n in lccMod.Notes) LccNotes.Add(LccNoteItem.Resolved(n, _main.Lcc, "⚠ "));
            foreach (var req in lccMod.Compatibilities?.RequireNotes ?? [])
                LccNotes.Add(LccNoteItem.Plain("Requires: " + req));
            var tier = LccOrderService.TierFor(entry.ToEntry(), _main.Lcc);
            if (tier is not null)
                LccNotes.Add(LccNoteItem.Plain("📍 " + LccOrderService.PlacementTip(tier.Value)));
            LccNotes.Add(LccNoteItem.Plain($"Games: {string.Join(", ", lccMod.Games)}" +
                         (lccMod.LastUpdate is null ? "" : $" · updated: {lccMod.LastUpdate}")));
        }

        if (entry is null || entry.IsInclude) return;

        SetUpStdinQuestions(entry);

        var resolved = ResolvePackageTp2(entry);
        if (resolved is null)
        {
            ComponentsInfo = entry.ModId is null
                ? "Entry imported from weidu.log — no library package; edit component numbers by hand."
                : "The mod is no longer in the library — edit component numbers by hand.";
            return;
        }
        var weiduExe = _main.ResolveWeiduPath();
        if (weiduExe is null)
        {
            ComponentsInfo = "weidu.exe not found — download it on the Install tab to browse components.";
            return;
        }

        IsLoadingComponents = true;
        try
        {
            var runner = new WeiduRunner(weiduExe);
            var langs = await runner.ListLanguagesAsync(resolved.Value.Pkg.RootPath, resolved.Value.Tp2Rel);
            if (seq != _loadSeq) return;
            _syncingSelection = true;
            EntryLanguages.Clear();
            if (langs.Count == 0) langs.Add((0, "(default)"));
            foreach (var (idx, name) in langs) EntryLanguages.Add(new LanguageChoice(idx, name));
            SelectedEntryLanguage = EntryLanguages.FirstOrDefault(l => l.Index == entry.LanguageIndex)
                                    ?? EntryLanguages[0];
            _syncingSelection = false;
        }
        catch (Exception ex)
        {
            if (seq == _loadSeq)
            {
                ComponentsInfo = "Failed to read languages: " + ex.Message;
                IsLoadingComponents = false;
            }
            return;
        }

        await LoadComponentsAsync(entry, seq);
    }

    private void SetUpStdinQuestions(BuildEntryViewModel entry)
    {
        if (_currentRecipe?.StdinQuestions is not { Count: > 0 } questions) return;

        // Pre-select answers from the entry's saved stdin, falling back to defaults.
        var savedLines = entry.StdinText.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var saved = i < savedLines.Length ? savedLines[i].Trim() : null;
            var vm = new StdinQuestionViewModel
            {
                Text = q.Text,
                Options = [.. q.Options],
            };
            vm.SelectedOption = vm.Options.FirstOrDefault(o => o.Value == saved)
                                ?? vm.Options.FirstOrDefault(o => o.Value == q.DefaultValue);
            vm.PropertyChanged += OnStdinAnswerChanged;
            StdinQuestions.Add(vm);
        }
        SyncStdinFromQuestions();
    }

    private void OnStdinAnswerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StdinQuestionViewModel.SelectedOption)) SyncStdinFromQuestions();
    }

    private void SyncStdinFromQuestions()
    {
        if (SelectedEntry is null || StdinQuestions.Count == 0) return;
        SelectedEntry.StdinText = string.Join('\n',
            StdinQuestions.Select(q => q.SelectedOption?.Value ?? ""));
    }

    private async Task LoadComponentsAsync(BuildEntryViewModel entry, int seq)
    {
        EntryComponents.Clear();
        _allComponentChoices.Clear();
        var resolved = ResolvePackageTp2(entry);
        var weiduExe = _main.ResolveWeiduPath();
        if (resolved is null || weiduExe is null) return;

        IsLoadingComponents = true;
        try
        {
            var (pkg, tp2Rel) = resolved.Value;
            var cacheKey = (pkg.Id + "|" + tp2Rel, entry.LanguageIndex);
            if (!_componentCache.TryGetValue(cacheKey, out var components))
            {
                components = await new WeiduRunner(weiduExe)
                    .ListComponentsAsync(pkg.RootPath, tp2Rel, entry.LanguageIndex);
                _componentCache[cacheKey] = components;
            }
            if (seq != _loadSeq) return;

            var selected = entry.ParseComponents().ToHashSet();
            _allComponentChoices.Clear();
            foreach (var c in components)
            {
                var arrow = c.Label.IndexOf(" -> ", StringComparison.Ordinal);
                var choice = new ComponentChoice
                {
                    Number = c.Number,
                    Label = c.Label,
                    GroupKey = arrow > 0 ? c.Label[..arrow] : null,
                    IsSelected = selected.Contains(c.Number),
                };
                choice.PropertyChanged += OnComponentToggled;
                _allComponentChoices.Add(choice);
            }
            ApplyComponentFilter();
            _ = LoadComponentImagesAsync(pkg, tp2Rel, seq);

            // Warn when the saved selection already violates a one-of group.
            var conflictGroups = _allComponentChoices
                .Where(c => c.IsSelected && c.GroupKey is not null)
                .GroupBy(c => c.GroupKey!)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            var interactiveSelected = _allComponentChoices.Where(c => c.IsSelected && c.IsInteractiveOnly).Select(c => c.Number).ToList();
            ComponentsInfo = components.Count == 0
                ? "WeiDU returned no component list — edit component numbers by hand."
                : $"{components.Count} components."
                  + (conflictGroups.Count == 0 ? "" :
                      $" ⚠ Multiple variants selected in one-of group(s): {string.Join("; ", conflictGroups)} — keep only one of each.")
                  + (interactiveSelected.Count == 0 ? "" :
                      $" ⚠ Component(s) {string.Join(", ", interactiveSelected)} are interactive-only (batch mode) — uncheck them; this tool installs non-interactively.");
        }
        catch (Exception ex)
        {
            if (seq == _loadSeq) ComponentsInfo = "Failed to read components: " + ex.Message;
        }
        finally
        {
            if (seq == _loadSeq) IsLoadingComponents = false;
        }
    }

    /// <summary>Attaches portrait thumbnails (from the tp2's COPY lines) to their components.</summary>
    private async Task LoadComponentImagesAsync(ModPackage pkg, string tp2Rel, int seq)
    {
        Dictionary<int, List<string>> images;
        try
        {
            images = await Task.Run(() => Tp2ComponentAssets.ImagesByComponent(pkg.RootPath, tp2Rel));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"component image scan failed for {pkg.ModFolderName}", ex);
            return;
        }
        if (seq != _loadSeq || images.Count == 0) return;

        foreach (var choice in _allComponentChoices)
        {
            if (!images.TryGetValue(choice.Number, out var files)) continue;
            // Prefer the large portrait (…L.bmp) first; cap at 4 thumbnails per component.
            foreach (var file in files.OrderByDescending(f => Path.GetFileNameWithoutExtension(f).EndsWith("L", StringComparison.OrdinalIgnoreCase)).Take(4))
            {
                if (seq != _loadSeq) return;
                var bmp = await Task.Run(() =>
                {
                    try { using var s = File.OpenRead(file); return Avalonia.Media.Imaging.Bitmap.DecodeToHeight(s, 96); }
                    catch { return null; }
                });
                if (seq != _loadSeq) { bmp?.Dispose(); return; }
                if (bmp is not null)
                {
                    choice.PreviewImages.Add(bmp);
                    choice.HasPreviewImages = true;
                }
            }
        }
    }

    private void OnComponentToggled(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ComponentChoice.IsSelected) || SelectedEntry is null) return;

        // Radio behavior for mutually-exclusive subcomponents: picking one
        // variant of a "Group -> Choice" set deselects its siblings.
        if (sender is ComponentChoice { IsSelected: true, GroupKey: not null } picked)
        {
            foreach (var sibling in _allComponentChoices)
            {
                if (!ReferenceEquals(sibling, picked) && sibling.GroupKey == picked.GroupKey && sibling.IsSelected)
                    sibling.IsSelected = false; // re-enters this handler; harmless
            }
        }

        // Selection state lives on the full list, so a filtered view can't drop choices.
        SelectedEntry.ComponentsText = string.Join(' ',
            _allComponentChoices.Where(c => c.IsSelected).Select(c => c.Number).OrderBy(n => n));
    }

    partial void OnComponentFilterChanged(string value) => ApplyComponentFilter();

    private void ApplyComponentFilter()
    {
        EntryComponents.Clear();
        var filter = ComponentFilter.Trim();
        foreach (var c in _allComponentChoices)
        {
            if (filter.Length == 0
                || c.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || c.Number.ToString() == filter)
            {
                EntryComponents.Add(c);
            }
        }
    }

    /// <summary>
    /// Selects the components currently visible in the (possibly filtered)
    /// list — only the first variant of each one-of group.
    /// </summary>
    [RelayCommand]
    private void SelectVisibleComponents()
    {
        var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in EntryComponents)
        {
            if (c.IsInteractiveOnly) continue;
            if (c.GroupKey is not null && !seenGroups.Add(c.GroupKey)) continue;
            c.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectVisibleComponents()
    {
        foreach (var c in EntryComponents) c.IsSelected = false;
    }
}
