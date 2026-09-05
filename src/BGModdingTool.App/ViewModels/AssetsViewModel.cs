using System.Collections.ObjectModel;
using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;
using BGModdingTool.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BGModdingTool.App.ViewModels;

/// <summary>One previewable file of an asset pack: a thumbnail for images, a play button for audio.</summary>
public partial class AssetPreviewItem : ViewModelBase
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path);
    public bool IsImage => System.IO.Path.GetExtension(Path).ToLowerInvariant() is ".bmp" or ".png";
    public bool IsAudio => System.IO.Path.GetExtension(Path).ToLowerInvariant() is ".wav" or ".ogg" or ".mp3" or ".mus" or ".acm";
    /// <summary>IE .mus files are text playlists of .acm segments — shown as text instead of played.</summary>
    public bool IsMusPlaylist => System.IO.Path.GetExtension(Path).Equals(".mus", StringComparison.OrdinalIgnoreCase);
    public bool CanPlayInApp => Services.AudioPreviewPlayer.CanPlayInApp(Path);
    public string SizeInfo { get; init; } = "";
    [ObservableProperty] public partial string PlaylistText { get; set; } = "";
    [ObservableProperty] public partial Avalonia.Media.Imaging.Bitmap? Thumbnail { get; set; }
    [ObservableProperty] public partial bool IsPlaying { get; set; }

    /// <summary>Set by the owning view model; keeps the row's buttons independent of ancestor-binding tricks.</summary>
    public Action<AssetPreviewItem>? PlayRequested { get; set; }
    public Action<AssetPreviewItem>? OpenRequested { get; set; }

    [RelayCommand] private void Play() => PlayRequested?.Invoke(this);
    [RelayCommand] private void Open() => OpenRequested?.Invoke(this);
}

/// <summary>A deployment row shown for the selected asset package.</summary>
public sealed record AssetDeploymentItem(string InstanceName, string TargetDirectory, int FileCount, DateTimeOffset DeployedAt)
{
    public string Display => $"{InstanceName} → {TargetDirectory} ({FileCount} files, {DeployedAt.ToLocalTime():g})";
}

/// <summary>
/// "Assets" tab: portrait packs, soundsets, music and loose override files —
/// plain archives that need no WeiDU. Imported once, deployed per instance,
/// removable file-by-file thanks to the deployment manifest.
/// </summary>
public partial class AssetsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly AssetLibrary _library;
    private readonly AudioPreviewPlayer _player = new();
    private int _previewSeq;
    private const int MaxThumbnails = 300;

    public ObservableCollection<AssetPackage> Items { get; } = [];
    public ObservableCollection<AssetDeploymentItem> DeploymentsOfSelected { get; } = [];
    public ObservableCollection<AssetPreviewItem> ImagePreviews { get; } = [];
    public ObservableCollection<AssetPreviewItem> AudioPreviews { get; } = [];
    [ObservableProperty] public partial string PreviewInfo { get; set; } = "";
    public ObservableCollection<GameInstance> Instances => _main.Instances.Items;
    public AssetKind[] Kinds { get; } = [AssetKind.Portraits, AssetKind.Soundset, AssetKind.Music, AssetKind.Override];

    [ObservableProperty] public partial AssetPackage? Selected { get; set; }
    [ObservableProperty] public partial AssetKind SelectedKind { get; set; }
    [ObservableProperty] public partial GameInstance? TargetInstance { get; set; }
    [ObservableProperty] public partial string TargetPreview { get; set; } = "";
    [ObservableProperty] public partial bool TargetIsOutsideInstance { get; set; }
    [ObservableProperty] public partial string RelevantFilesInfo { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public AssetsViewModel(MainViewModel main)
    {
        _main = main;
        _library = new AssetLibrary(main.Paths);
        Reload();
    }

    public void Reload()
    {
        Items.Clear();
        foreach (var a in _library.List()) Items.Add(a);
        RefreshDetails();
    }

    partial void OnSelectedChanged(AssetPackage? value)
    {
        if (value is not null) SelectedKind = value.Kind;
        RefreshDetails();
        _ = LoadPreviewsAsync(value);
    }

    // ---------- preview ----------

    private async Task LoadPreviewsAsync(AssetPackage? package)
    {
        var seq = ++_previewSeq;
        StopAudio();
        ImagePreviews.Clear();
        AudioPreviews.Clear();
        PreviewInfo = "";
        if (package is null) return;

        var files = Directory.EnumerateFiles(package.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var images = new List<AssetPreviewItem>();
        var audio = new List<AssetPreviewItem>();
        foreach (var f in files)
        {
            var item = new AssetPreviewItem
            {
                Path = f,
                SizeInfo = $"{new FileInfo(f).Length / 1024.0:F0} KB",
                PlayRequested = PlayAudio,
                OpenRequested = OpenFile,
            };
            if (item.IsImage) images.Add(item);
            else if (item.IsAudio) audio.Add(item);
        }
        foreach (var i in images.Take(MaxThumbnails)) ImagePreviews.Add(i);
        foreach (var a in audio)
        {
            if (a.IsMusPlaylist)
            {
                try
                {
                    var lines = File.ReadLines(a.Path).Where(l => l.Trim().Length > 0).Take(12).ToList();
                    a.PlaylistText = string.Join("  ·  ", lines.Select(l => l.Trim()));
                }
                catch { /* preview only */ }
            }
            AudioPreviews.Add(a);
        }
        PreviewInfo = $"{images.Count} image(s), {audio.Count} audio file(s)" +
                      (images.Count > MaxThumbnails ? $" — showing the first {MaxThumbnails} thumbnails" : "") + ".";

        // Decode thumbnails off the UI thread, a few at a time, newest selection wins.
        foreach (var item in ImagePreviews.ToList())
        {
            if (seq != _previewSeq) return;
            var bmp = await Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(item.Path);
                    return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 110);
                }
                catch { return null; }
            });
            if (seq != _previewSeq) { bmp?.Dispose(); return; }
            item.Thumbnail = bmp;
        }
    }

    private static void AudioLog(string line) => AppLog.Info("audio: " + line);

    [RelayCommand]
    private void PlayAudio(AssetPreviewItem? item)
    {
        if (item is null) return;
        AudioLog($"PlayAudio invoked: {item.Path} (isPlaying={item.IsPlaying}, canPlayInApp={item.CanPlayInApp})");
        try
        {
            if (item.IsPlaying) { StopAudio(); return; }
            StopAudio();
            item.IsPlaying = true;
            _main.Status = $"Playing {item.Name}…";
            _player.Play(item.Path, error => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AudioLog($"PlaybackStopped: {item.Name} error={(error?.ToString() ?? "none")}");
                item.IsPlaying = false;
                _main.Status = error is null
                    ? $"Finished: {item.Name}"
                    : $"Playback error for {item.Name}: {error.Message}";
            }));
            AudioLog($"Play started: {item.Name}, player.IsPlaying={_player.IsPlaying}");
            if (!AudioPreviewPlayer.CanPlayInApp(item.Path))
            {
                item.IsPlaying = false;
                _main.Status = $"Opened {item.Name} in the default player — in-app preview handles WAV/OGG/MP3; " +
                               "Interplay ACM/MUS uses a proprietary codec.";
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"audio: play failed for {item.Path}", ex);
            item.IsPlaying = false;
            _main.Status = "Playback failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void StopAudio()
    {
        _player.Stop();
        foreach (var a in AudioPreviews) a.IsPlaying = false;
    }

    [RelayCommand]
    private void OpenFile(AssetPreviewItem? item)
    {
        if (item is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Path) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open file: " + ex.Message; }
    }

    partial void OnSelectedKindChanged(AssetKind value)
    {
        if (Selected is null || Selected.Kind == value) return;
        Selected.Kind = value;
        _library.Save(Selected);
        RefreshDetails();
    }

    partial void OnTargetInstanceChanged(GameInstance? value) => RefreshDetails();

    private void RefreshDetails()
    {
        DeploymentsOfSelected.Clear();
        TargetPreview = "";
        TargetIsOutsideInstance = false;
        RelevantFilesInfo = "";
        if (Selected is null) return;

        var relevant = _library.RelevantFiles(Selected).Count();
        RelevantFilesInfo = $"{relevant} file(s) of this kind will be deployed (package has {Selected.FileCount} files in total).";

        var instancesById = Instances.ToDictionary(i => i.Id, i => i.Name);
        foreach (var d in _library.Deployments(Selected))
        {
            DeploymentsOfSelected.Add(new AssetDeploymentItem(
                instancesById.TryGetValue(d.InstanceId, out var n) ? n : d.InstanceId,
                d.TargetDirectory, d.Files.Count, d.DeployedAt));
        }

        if (TargetInstance is not null)
        {
            TargetPreview = AssetLibrary.TargetDirectory(Selected, TargetInstance);
            TargetIsOutsideInstance = AssetLibrary.TargetIsOutsideInstance(Selected, TargetInstance);
        }
    }

    [RelayCommand]
    private async Task ImportArchivesAsync()
    {
        var files = await DialogService.PickFilesAsync(
            "Select asset packs (portraits, soundsets, music)", "Archives", "*.zip", "*.7z", "*.rar", "*.tar.gz");
        if (files.Count == 0) return;
        IsBusy = true;
        try
        {
            foreach (var file in files)
            {
                _main.Status = $"Importing {Path.GetFileName(file)}…";
                try
                {
                    var pkg = await _library.ImportArchiveAsync(file);
                    _main.Status = $"Imported \"{pkg.Name}\" as {pkg.Kind} ({pkg.FileCount} files).";
                }
                catch (Exception ex) { _main.Status = $"Import failed for {Path.GetFileName(file)}: {ex.Message}"; }
            }
            Reload();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var folder = await DialogService.PickFolderAsync("Select a folder with portraits / sounds / music");
        if (folder is null) return;
        IsBusy = true;
        try
        {
            var pkg = await _library.ImportDirectoryAsync(folder);
            Reload();
            _main.Status = $"Imported \"{pkg.Name}\" as {pkg.Kind} ({pkg.FileCount} files).";
        }
        catch (Exception ex) { _main.Status = "Import failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (Selected is null || TargetInstance is null)
        {
            _main.Status = "Select an asset pack and a target instance.";
            return;
        }
        IsBusy = true;
        try
        {
            var d = await _library.DeployAsync(Selected, TargetInstance);
            RefreshDetails();
            _main.Status = $"Deployed {d.Files.Count} file(s) of \"{Selected.Name}\" to {d.TargetDirectory}.";
        }
        catch (Exception ex) { _main.Status = "Deploy failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Undeploy()
    {
        if (Selected is null || TargetInstance is null) return;
        try
        {
            var removed = _library.Undeploy(Selected, TargetInstance);
            RefreshDetails();
            _main.Status = $"Removed {removed} deployed file(s) of \"{Selected.Name}\" from instance \"{TargetInstance.Name}\".";
        }
        catch (Exception ex) { _main.Status = "Undeploy failed: " + ex.Message; }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (_library.Deployments(Selected).Count > 0)
        {
            _main.Status = "This pack is still deployed — undeploy it from its instances first.";
            return;
        }
        try
        {
            _library.Delete(Selected);
            _main.Status = $"Removed asset pack \"{Selected.Name}\".";
            Selected = null;
            Reload();
        }
        catch (Exception ex) { _main.Status = "Deletion failed: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Selected is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Selected.RootPath) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open folder: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenTargetFolder()
    {
        if (string.IsNullOrEmpty(TargetPreview) || !Directory.Exists(TargetPreview)) { _main.Status = "Target folder does not exist yet."; return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TargetPreview) { UseShellExecute = true }); }
        catch (Exception ex) { _main.Status = "Could not open folder: " + ex.Message; }
    }
}
