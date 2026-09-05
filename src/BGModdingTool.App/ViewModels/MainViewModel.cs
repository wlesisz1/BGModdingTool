using BGModdingTool.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BGModdingTool.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public AppPaths Paths { get; }
    public AppConfig Config { get; }
    public InstanceService InstanceService { get; }
    public ModLibrary ModLibrary { get; }
    public SnapshotService SnapshotService { get; }
    public BuildStore BuildStore { get; }
    public LccDatabase Lcc { get; }

    public InstancesViewModel Instances { get; }
    public ModsViewModel Mods { get; }
    public BuildsViewModel Builds { get; }
    public InstallViewModel Install { get; }
    public CatalogViewModel Catalog { get; }
    public AssetsViewModel Assets { get; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Ready.";

    public MainViewModel()
    {
        Paths = new AppPaths();
        Paths.EnsureCreated();
        Config = JsonStore.Load<AppConfig>(Paths.ConfigFile) ?? new AppConfig();
        InstanceService = new InstanceService(Paths);
        ModLibrary = new ModLibrary(Paths);
        SnapshotService = new SnapshotService();
        BuildStore = new BuildStore(Paths);
        Lcc = new LccDatabase(Paths);

        Instances = new InstancesViewModel(this);
        Mods = new ModsViewModel(this);
        Builds = new BuildsViewModel(this);
        Install = new InstallViewModel(this);
        Catalog = new CatalogViewModel(this);
        Assets = new AssetsViewModel(this);

        if (!Lcc.IsAvailable) _ = DownloadLccAsync(silent: true);
    }

    /// <summary>Fetches/refreshes the LCC community mod database (game compat, warnings, conflicts).</summary>
    public async Task DownloadLccAsync(bool silent = false)
    {
        try
        {
            if (!silent) Status = "Downloading the LCC mod database…";
            var count = await Lcc.DownloadAsync();
            Status = $"LCC database updated: {count} mods.";
            Builds.ValidateNow();
            Catalog.Refresh();
        }
        catch (Exception ex)
        {
            if (!silent) Status = "Could not download the LCC database: " + ex.Message;
        }
    }

    public void SaveConfig() => JsonStore.Save(Paths.ConfigFile, Config);

    // Every status message is part of the app's narrative — persist it, and
    // classify obvious failures so they stand out when reading the log.
    partial void OnStatusChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || value.Contains("error", StringComparison.OrdinalIgnoreCase)
            || value.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || value.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            AppLog.Error("status: " + value);
        else
            AppLog.Info("status: " + value);
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Paths.LogsDir) { UseShellExecute = true }); }
        catch (Exception ex) { Status = "Could not open the logs folder: " + ex.Message; }
    }

    /// <summary>Config path → downloaded tools/weidu.exe → setup-*.exe bundled with a library mod.</summary>
    public string? ResolveWeiduPath()
    {
        if (!string.IsNullOrWhiteSpace(Config.WeiduPath) && File.Exists(Config.WeiduPath))
            return Config.WeiduPath;
        var tools = Path.Combine(Paths.ToolsDir, "weidu.exe");
        if (File.Exists(tools)) return tools;
        return ModLibrary.FindBundledWeidu();
    }
}
