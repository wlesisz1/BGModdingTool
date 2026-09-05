namespace BGModdingTool.Core.Services;

/// <summary>All tool-owned filesystem locations. Nothing outside DataRoot is ever written.</summary>
public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null)
    {
        DataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BGModdingTool");
    }

    public string DataRoot { get; }
    public string ConfigFile => Path.Combine(DataRoot, "config.json");
    public string BuildsDir => Path.Combine(DataRoot, "builds");
    public string InstancesDir => Path.Combine(DataRoot, "instances");
    public string ModsDir => Path.Combine(DataRoot, "mods");
    public string ToolsDir => Path.Combine(DataRoot, "tools");
    public string LogsDir => Path.Combine(DataRoot, "logs");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(BuildsDir);
        Directory.CreateDirectory(InstancesDir);
        Directory.CreateDirectory(ModsDir);
        Directory.CreateDirectory(ToolsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
