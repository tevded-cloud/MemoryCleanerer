using System.IO;
using System.Windows;
using Cleanerer.Services;

namespace Cleanerer;

/// <summary>
/// Interaction logic for App.xaml. Starts the automatic-cleanup scheduler and the tray icon once
/// at startup; the scheduler, settings service, and tray service are process-wide singletons
/// shared with the view-models / main window.
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>Crash/error log beside settings.json — WPF swallows exceptions thrown inside
    /// binding-driven code paths, so without this a failing page constructor looks like "nothing
    /// happened". Never throws.</summary>
    internal static void LogError(string context, Exception ex)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cleanerer");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogError("DispatcherUnhandledException", args.Exception);
            System.Windows.MessageBox.Show(
                $"Cleanerer hit an unexpected error:\n\n{args.Exception.Message}\n\nDetails were written to %AppData%\\Cleanerer\\error.log.",
                "Cleanerer", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogError("AppDomain.UnhandledException", ex);
            }
        };

        // Kicks off the 30s scheduler tick. It reads current settings and reacts to changes via
        // SettingsService.SettingsChanged, so nothing else needs to poke it.
        SchedulerService.Instance.Start();

        // base.OnStartup already created and showed the StartupUri window by this point, so
        // Application.MainWindow is set. The tray icon needs the window to Show()/Activate() it
        // back from a background-mode Hide().
        if (MainWindow is MainWindow mainWindow)
        {
            TrayService.Instance.Initialize(mainWindow);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TrayService.Instance.Dispose();
        base.OnExit(e);
    }
}
