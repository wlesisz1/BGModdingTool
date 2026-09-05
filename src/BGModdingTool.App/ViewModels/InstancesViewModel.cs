using System.Collections.ObjectModel;
using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;
using BGModdingTool.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BGModdingTool.App.ViewModels;

public partial class InstancesViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    public ObservableCollection<GameInstance> Items { get; } = [];
    public ObservableCollection<SnapshotItem> Snapshots { get; } = [];
    /// <summary>Which snapshot the selected instance's game folder currently equals.</summary>
    [ObservableProperty] public partial string SelectedStateText { get; set; } = "";

    [ObservableProperty]
    public partial GameInstance? Selected { get; set; }

    [ObservableProperty]
    public partial string NewInstanceName { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool ConfirmingDelete { get; set; }

    [ObservableProperty]
    public partial string RenameText { get; set; } = "";

    public InstancesViewModel(MainViewModel main)
    {
        _main = main;
        Reload();
    }

    partial void OnSelectedChanged(GameInstance? value)
    {
        ConfirmingDelete = false;
        RenameText = value?.Name ?? "";
        Snapshots.Clear();
        SelectedStateText = value?.CurrentStateText ?? "";
        RefreshLaunchExes();
        if (value is null) return;
        // Headers only (fast); the full file map is loaded when a snapshot is restored.
        foreach (var s in _main.SnapshotService.ListHeaders(value))
            Snapshots.Add(new SnapshotItem(s, s.Id == value.CurrentSnapshotId));
    }

    /// <summary>Called after anything that changed the selected instance's folder.</summary>
    public void RefreshState() => OnSelectedChanged(Selected);

    [RelayCommand]
    private void RenameInstance()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(RenameText)) return;
        var instance = Selected;
        instance.Name = RenameText.Trim();
        _main.InstanceService.SaveMetadata(instance);
        Reload();
        Selected = Items.FirstOrDefault(i => i.Id == instance.Id);
        _main.Status = $"Renamed instance to \"{instance.Name}\".";
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (Selected is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Selected.GamePath) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open folder: " + ex.Message; }
    }

    /// <summary>Executables found in the game folder that can start the game (EEex's InfinityLoader first).</summary>
    public ObservableCollection<string> LaunchExes { get; } = [];
    [ObservableProperty] public partial string? SelectedLaunchExe { get; set; }
    private bool _syncingLaunchExe;

    private static readonly string[] KnownGameExes =
        ["InfinityLoader.exe", "EEex.exe", "Baldur.exe", "Icewind.exe", "SiegeOfDragonspear.exe", "bgmain.exe", "BGMain.exe", "idmain.exe"];

    private void RefreshLaunchExes()
    {
        _syncingLaunchExe = true;
        try
        {
            LaunchExes.Clear();
            if (Selected is null || !Directory.Exists(Selected.GamePath)) { SelectedLaunchExe = null; return; }
            var found = Directory.EnumerateFiles(Selected.GamePath, "*.exe")
                .Select(Path.GetFileName)
                .Where(n => n is not null
                            && !n.StartsWith("setup-", StringComparison.OrdinalIgnoreCase)
                            && !n.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
                            && !n.Equals("weidu.exe", StringComparison.OrdinalIgnoreCase)
                            && !n.Equals("oalinst.exe", StringComparison.OrdinalIgnoreCase))
                .Select(n => n!)
                .OrderBy(n => { var i = Array.FindIndex(KnownGameExes, k => k.Equals(n, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 99 : i; })
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var n in found) LaunchExes.Add(n);
            // Remembered choice wins; otherwise EEex's loader (the game crashes without it once EEex is in), then the game exe.
            var remembered = Selected.LaunchExe;
            SelectedLaunchExe = remembered is not null && found.Contains(remembered, StringComparer.OrdinalIgnoreCase)
                ? found.First(n => n.Equals(remembered, StringComparison.OrdinalIgnoreCase))
                : found.FirstOrDefault();
        }
        finally { _syncingLaunchExe = false; }
    }

    partial void OnSelectedLaunchExeChanged(string? value)
    {
        if (_syncingLaunchExe || Selected is null || value is null) return;
        if (string.Equals(Selected.LaunchExe, value, StringComparison.OrdinalIgnoreCase)) return;
        Selected.LaunchExe = value;
        _main.InstanceService.SaveMetadata(Selected);
        _main.Status = $"\"{Selected.Name}\" will launch with {value}.";
    }

    [RelayCommand]
    private void LaunchGame()
    {
        if (Selected is null) return;
        var exe = SelectedLaunchExe is null ? null : Path.Combine(Selected.GamePath, SelectedLaunchExe);
        if (exe is null || !File.Exists(exe))
        {
            _main.Status = "No game executable found in this instance.";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Selected.GamePath,
            });
            _main.Status = $"Launched {SelectedLaunchExe} from \"{Selected.Name}\".";
        }
        catch (Exception ex) { _main.Status = "Could not launch the game: " + ex.Message; }
    }

    public void Reload()
    {
        Items.Clear();
        foreach (var i in _main.InstanceService.List()) Items.Add(i);
    }

    [RelayCommand]
    private async Task CreateInstanceAsync()
    {
        var source = await DialogService.PickFolderAsync("Select the original game directory (with chitin.key)");
        if (source is null) return;

        var type = GameDetector.Detect(source);
        if (type == GameType.Unknown)
        {
            _main.Status = "Could not identify the game in this directory (no chitin.key or unknown variant). " +
                           "The instance will be created with type Unknown — you can change it later.";
        }

        var name = string.IsNullOrWhiteSpace(NewInstanceName)
            ? $"{type.DisplayName()} {DateTime.Now:yyyy-MM-dd}"
            : NewInstanceName.Trim();

        IsBusy = true;
        try
        {
            var progress = new Progress<(long done, long total, string file)>(p =>
                _main.Status = $"Copying game: {p.done}/{p.total} files…");
            var instance = await _main.InstanceService.CreateAsync(name, source, type, progress);
            Reload();
            Selected = Items.FirstOrDefault(i => i.Id == instance.Id);
            NewInstanceName = "";
            _main.Status = $"Created instance \"{instance.Name}\" ({type.DisplayName()}).";
        }
        catch (Exception ex)
        {
            _main.Status = "Failed to create instance: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void RequestDelete() => ConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => ConfirmingDelete = false;

    [RelayCommand]
    private void ConfirmDelete()
    {
        if (Selected is null) return;
        try
        {
            _main.InstanceService.Delete(Selected);
            _main.Status = $"Deleted instance \"{Selected.Name}\".";
            Selected = null;
            Reload();
        }
        catch (Exception ex) { _main.Status = "Deletion failed: " + ex.Message; }
        finally { ConfirmingDelete = false; }
    }

    [RelayCommand]
    private async Task TakeSnapshotAsync()
    {
        if (Selected is null) return;
        IsBusy = true;
        try
        {
            var progress = new Progress<(long done, long total, string file)>(p =>
                _main.Status = $"Snapshot: {p.done}/{p.total} files…");
            var snap = await _main.SnapshotService.TakeAsync(Selected, $"manual {DateTime.Now:yyyy-MM-dd HH:mm}", progress);
            OnSelectedChanged(Selected);
            _main.Status = $"Snapshot \"{snap.Label}\" saved.";
        }
        catch (Exception ex) { _main.Status = "Snapshot failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RestoreSnapshotAsync(SnapshotItem? item)
    {
        if (Selected is null || item is null) return;
        var snapshot = _main.SnapshotService.Load(Selected, item.Manifest.Id);
        if (snapshot is null) { _main.Status = "Snapshot manifest could not be read."; return; }
        IsBusy = true;
        try
        {
            var progress = new Progress<(long done, long total, string file)>(p =>
                _main.Status = $"Restoring: {p.done}/{p.total} files…");
            await _main.SnapshotService.RestoreAsync(Selected, snapshot, progress);
            OnSelectedChanged(Selected);
            _main.Status = $"Restored state \"{snapshot.Label}\" from {snapshot.CreatedAt.ToLocalTime():g}.";
        }
        catch (Exception ex) { _main.Status = "Restore failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void DeleteSnapshot(SnapshotItem? item)
    {
        var snapshot = item?.Manifest;
        if (Selected is null || snapshot is null) return;
        try
        {
            _main.SnapshotService.Delete(Selected, snapshot);
            OnSelectedChanged(Selected);
            _main.Status = $"Deleted snapshot \"{snapshot.Label}\".";
        }
        catch (Exception ex) { _main.Status = "Snapshot deletion failed: " + ex.Message; }
    }
}

/// <summary>A snapshot row: the manifest plus whether the game folder currently equals it.</summary>
public sealed record SnapshotItem(SnapshotManifest Manifest, bool IsCurrent)
{
    public string Label => Manifest.Label;
    public DateTime CreatedAtLocal => Manifest.CreatedAtLocal;
    public string Marker => IsCurrent ? "\u25c0 current state of the game folder" : "";
}
