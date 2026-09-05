using System.Collections.ObjectModel;
using Avalonia.Threading;
using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;
using BGModdingTool.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BGModdingTool.App.ViewModels;

public partial class InstallViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cts;
    private InstallReport? _lastReport;

    public ObservableCollection<GameInstance> Instances => _main.Instances.Items;
    public ObservableCollection<BuildDefinition> Builds => _main.Builds.Items;
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty] public partial GameInstance? SelectedInstance { get; set; }
    /// <summary>Which snapshot the selected instance's game folder currently equals.</summary>
    [ObservableProperty] public partial string InstanceStateText { get; set; } = "";
    partial void OnSelectedInstanceChanged(GameInstance? value) => InstanceStateText = value?.CurrentStateText ?? "";
    [ObservableProperty] public partial BuildDefinition? SelectedBuild { get; set; }
    [ObservableProperty] public partial string WeiduPath { get; set; } = "";
    [ObservableProperty] public partial string SelectedGameLanguage { get; set; } = "en_US";
    public string[] GameLanguages { get; } =
        ["en_US", "cs_CZ", "de_DE", "es_ES", "fr_FR", "it_IT", "ja_JP", "ko_KR", "pl_PL", "pt_BR", "ru_RU", "tr_TR", "uk_UA", "zh_CN"];
    [ObservableProperty] public partial bool IsInstalling { get; set; }
    [ObservableProperty] public partial bool ContinueOnFailure { get; set; }
    [ObservableProperty] public partial bool CanRollback { get; set; }
    [ObservableProperty] public partial bool AwaitingInput { get; set; }
    [ObservableProperty] public partial string UserInputText { get; set; } = "";
    private TaskCompletionSource<string?>? _inputTcs;

    /// <summary>Called from the install thread when the installer waits for input; completes when the user answers.</summary>
    private Task<string?> RequestInputAsync(string outputTail, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            _inputTcs = tcs;
            UserInputText = "";
            AwaitingInput = true;
            _main.Status = "The installer is waiting for input — type an answer below the log.";
            AppLog.Info("installer prompt suspected; last output: " + outputTail.Replace('\n', '|')[^Math.Min(200, outputTail.Length)..]);
        });
        // The runner cancels the request when the installer exits by itself
        // (a long silent step is not a prompt) — hide the input bar again.
        ct.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_inputTcs, tcs)) return;
            AwaitingInput = false;
            _inputTcs = null;
            tcs.TrySetResult(null);
            _main.Status = "The installer continued on its own — no input was needed.";
            AppLog.Info("installer prompt withdrawn (process exited)");
        }));
        return tcs.Task;
    }

    [RelayCommand]
    private void SendInput()
    {
        AwaitingInput = false;
        _inputTcs?.TrySetResult(UserInputText);
        _inputTcs = null;
    }

    [RelayCommand]
    private void AbortInput()
    {
        AwaitingInput = false;
        _inputTcs?.TrySetResult(null); // null kills the installer process
        _inputTcs = null;
    }

    public InstallViewModel(MainViewModel main)
    {
        _main = main;
        WeiduPath = main.Config.WeiduPath ?? "";
        SelectedGameLanguage = main.Config.PreferredLanguage ?? "en_US";
        // When there is exactly one option, pre-select it.
        Instances.CollectionChanged += (_, _) => AutoSelect();
        Builds.CollectionChanged += (_, _) => AutoSelect();
        AutoSelect();
    }

    private void AutoSelect()
    {
        if (SelectedInstance is null && Instances.Count == 1) SelectedInstance = Instances[0];
        if (SelectedBuild is null && Builds.Count == 1) SelectedBuild = Builds[0];
    }

    /// <summary>Writes the current install output to logs/install-&lt;timestamp&gt;-&lt;suffix&gt;.log; returns the path.</summary>
    private string? WriteInstallLog(string suffix)
    {
        if (LogLines.Count == 0) return null;
        try
        {
            var path = Path.Combine(_main.Paths.LogsDir, $"install-{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}.log");
            File.WriteAllLines(path, LogLines);
            AppLog.Info($"install log written: {path}");
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Error("could not write the install log", ex);
            return null;
        }
    }

    [RelayCommand]
    private void SaveLog()
    {
        var path = WriteInstallLog("manual");
        if (path is null) { _main.Status = "Log is empty or could not be written."; return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_main.Paths.LogsDir) { UseShellExecute = true }); } catch { }
        _main.Status = $"Saved log: {path}";
    }

    [RelayCommand]
    private async Task PickWeiduAsync()
    {
        var files = await DialogService.PickFilesAsync("Select weidu.exe (or any setup-*.exe)", "WeiDU", "*.exe");
        if (files.Count > 0) WeiduPath = files[0];
    }

    partial void OnWeiduPathChanged(string value)
    {
        _main.Config.WeiduPath = string.IsNullOrWhiteSpace(value) ? null : value;
        _main.SaveConfig();
    }

    partial void OnSelectedGameLanguageChanged(string value)
    {
        _main.Config.PreferredLanguage = value;
        _main.SaveConfig();
    }

    [RelayCommand]
    private async Task DownloadWeiduAsync()
    {
        IsInstalling = true;
        try
        {
            using var downloader = new ModDownloader();
            var progress = new Progress<string>(s => _main.Status = s);
            var path = await downloader.DownloadWeiduAsync(_main.Paths.ToolsDir, progress);
            WeiduPath = path;
            _main.Status = $"Downloaded WeiDU: {path}";
        }
        catch (Exception ex) { _main.Status = "WeiDU download failed: " + ex.Message; }
        finally { IsInstalling = false; }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (SelectedInstance is null || SelectedBuild is null)
        {
            _main.Status = "Select an instance and a build.";
            return;
        }
        var weiduExe = _main.ResolveWeiduPath();
        if (weiduExe is null)
        {
            _main.Status = "weidu.exe not found — download it with the button next to the path, or pick it manually.";
            return;
        }

        // Flatten included builds ("packages") into one entry list.
        var expandWarnings = new List<string>();
        var flatEntries = _main.BuildStore.ExpandEntries(SelectedBuild, expandWarnings);
        if (flatEntries.Count == 0)
        {
            _main.Status = "The build has no entries to install." +
                (expandWarnings.Count > 0 ? " " + string.Join(" ", expandWarnings) : "");
            return;
        }
        var flatBuild = new BuildDefinition
        {
            Name = SelectedBuild.Name,
            GameType = SelectedBuild.GameType,
            Entries = flatEntries,
        };

        // Recipe sanity checks before touching the instance.
        var unresolved = flatEntries
            .Where(e => e.ExtraArgs?.Contains(ModRecipe.UnresolvedMarker) == true)
            .Select(e => e.Tp2).ToList();
        if (unresolved.Count > 0)
        {
            _main.Status = "Install blocked: entries with an unresolved source-game path " +
                $"({string.Join(", ", unresolved)}) — fill in the arguments on the Builds tab.";
            return;
        }

        LogLines.Clear();
        foreach (var w in expandWarnings) LogLines.Add("⚠ " + w);
        var constraints = OrderRuleStore.BuildConstraintMap(
            _main.ModLibrary.List(), new OrderRuleStore(_main.Paths).Load());
        foreach (var issue in OrderValidator.Validate(flatEntries, constraints))
            LogLines.Add("⚠ Order: " + issue);
        foreach (var issue in GameCompatibilityValidator.Validate(
            flatEntries, SelectedBuild.GameType, constraints, _main.Lcc))
            LogLines.Add("⚠ Compatibility: " + issue);
        _lastReport = null;
        CanRollback = false;
        IsInstalling = true;
        _cts = new CancellationTokenSource();

        var orchestrator = new InstallOrchestrator(
            _main.ModLibrary, _main.SnapshotService,
            new WeiduRunner(weiduExe) { GameLanguage = SelectedGameLanguage },
            Path.Combine(_main.Paths.DataRoot, "limits-ledger.json"));

        void AppendLine(string line) => Dispatcher.UIThread.Post(() =>
        {
            // oggdec/sox progress spam ("  42% decoded.") would flood the buffer and
            // push the real errors out; WeiDU's own .DEBUG file keeps everything anyway.
            if (line.TrimEnd().EndsWith("% decoded.", StringComparison.Ordinal)) return;
            // Orchestrator markers go to the app log too, so a hang/crash shows where it stopped.
            if (line.StartsWith("✔") || line.StartsWith("ℹ") || line.StartsWith("⚠") || line.StartsWith("!!!"))
                AppLog.Info("install: " + line);
            LogLines.Add(line);
            if (LogLines.Count > 20000) LogLines.RemoveAt(0);
        });

        try
        {
            // Run the whole install on a worker thread: file copies and log parsing must
            // never block the UI thread (an unresponsive window looks like a crash).
            var instance = SelectedInstance;
            var mode = ContinueOnFailure ? OnEntryFailure.Continue : OnEntryFailure.Abort;
            var progress = new Progress<string>(p => _main.Status = p);
            var token = _cts.Token;
            var report = await Task.Run(() => orchestrator.InstallBuildAsync(
                instance, flatBuild, mode, AppendLine, progress, token, RequestInputAsync));

            _lastReport = report;
            CanRollback = true;
            var failed = report.Results.Count(r => !r.ExitOk || r.Failed);
            var warned = report.Results.Count(r => r.HadWarnings);
            _main.Status = report.AllSucceeded
                ? $"Install finished: {report.Results.Count} mods, all OK" + (warned > 0 ? $" ({warned} with warnings)." : ".")
                : $"Install finished with problems: {failed} mods failed" +
                  (report.Aborted ? " (aborted after the first failure)." : ".");
            WriteInstallLog(report.AllSucceeded ? "ok" : "failed");
        }
        catch (OperationCanceledException)
        {
            _main.Status = "Install cancelled. You can restore the pre-install snapshot on the Instances tab.";
            WriteInstallLog("cancelled");
        }
        catch (Exception ex)
        {
            AppLog.Error("install crashed", ex);
            _main.Status = "Install failed: " + ex.Message;
            WriteInstallLog("crashed");
        }
        finally
        {
            IsInstalling = false;
            _cts = null;
            InstanceStateText = SelectedInstance?.CurrentStateText ?? "";
            _main.Instances.RefreshState();
        }
    }

    [RelayCommand]
    private void CancelInstall()
    {
        // Unblock a pending input request so the watchdog can react to the cancel.
        AwaitingInput = false;
        _inputTcs?.TrySetResult(null);
        _inputTcs = null;
        _cts?.Cancel();
    }

    [RelayCommand]
    private async Task RollbackAsync()
    {
        if (SelectedInstance is null || _lastReport is null) return;
        IsInstalling = true;
        try
        {
            var progress = new Progress<(long done, long total, string file)>(p =>
                _main.Status = $"Rollback: {p.done}/{p.total} files…");
            await _main.SnapshotService.RestoreAsync(SelectedInstance, _lastReport.PreSnapshot, progress);
            _main.Status = $"Restored the pre-install state (\"{_lastReport.PreSnapshot.Label}\").";
            CanRollback = false;
        }
        catch (Exception ex) { _main.Status = "Rollback failed: " + ex.Message; }
        finally { IsInstalling = false; }
    }
}
