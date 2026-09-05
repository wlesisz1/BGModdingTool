using System.Diagnostics;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Process-wide diagnostic log: DataRoot/logs/app-YYYY-MM-DD.log.
/// Cheap, thread-safe, never throws — logging must not break the app.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _logsDir;

    public static string? LogsDirectory => _logsDir;

    public static void Initialize(string logsDir)
    {
        _logsDir = logsDir;
        try { Directory.CreateDirectory(logsDir); } catch { }
        Info($"=== session start · {Environment.OSVersion} · .NET {Environment.Version} · pid {Environment.ProcessId}");
    }

    public static string? CurrentFile =>
        _logsDir is null ? null : Path.Combine(_logsDir, $"app-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO ", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}" +
                   (ex is null ? "" : Environment.NewLine + "    " + ex.ToString().Replace(Environment.NewLine, Environment.NewLine + "    "));
        Trace.WriteLine(line);
        var file = CurrentFile;
        if (file is null) return;
        try
        {
            lock (Gate) File.AppendAllText(file, line + Environment.NewLine);
        }
        catch { /* never let logging fail the caller */ }
    }
}
