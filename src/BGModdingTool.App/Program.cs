using Avalonia;
using System;
using System.Threading.Tasks;
using BGModdingTool.Core.Services;

namespace BGModdingTool.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Diagnostics first: every unhandled failure ends up in logs/app-<date>.log.
        var paths = new AppPaths();
        paths.EnsureCreated();
        AppLog.Initialize(paths.LogsDir);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("Unhandled exception (AppDomain)", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            AppLog.Info("=== session end");
        }
        catch (Exception ex)
        {
            AppLog.Error("Fatal: the application crashed", ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace()
            // Binding errors and swallowed handler exceptions go to logs/app-<date>.log too.
            .AfterSetup(_ => Avalonia.Logging.Logger.Sink = new Services.AvaloniaLogSink());
}
