using System.IO;
using System.Threading;
using System.Windows;
using Cleanerer.Interop;
using Cleanerer.Services;

namespace Cleanerer;

/// <summary>
/// Interaction logic for App.xaml. Starts the automatic-cleanup scheduler and the tray icon once
/// at startup; the scheduler, settings service, and tray service are process-wide singletons
/// shared with the view-models / main window. Enforces a single instance per session: a second
/// launch asks the first to show its window (even from the tray) and exits immediately.
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// Session-wide message id a second instance broadcasts before exiting; MainWindow's message
    /// hook listens for it and restores the window. Registered once per process.
    /// </summary>
    internal static readonly int ShowExistingInstanceMessage =
        NativeMethods.RegisterWindowMessage("Cleanerer.ShowExistingInstance");

    // Held (not just created) for the app's whole lifetime; the OS releases it on exit.
    private static Mutex? _singleInstanceMutex;
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
        // Single instance per user session ("Local\" scope): if the mutex already exists, wake
        // the running instance and bail out before any window or service spins up.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\Cleanerer.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowExistingInstanceMessage, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogError("DispatcherUnhandledException", args.Exception);
            System.Windows.MessageBox.Show(
                $"Memory Cleanerer hit an unexpected error:\n\n{args.Exception.Message}\n\nDetails were written to %AppData%\\Cleanerer\\error.log.",
                "Memory Cleanerer", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // The tray icon is initialized by MainWindow.OnSourceInitialized, not here: WPF creates
        // the StartupUri window only after the Startup event completes, so Application.MainWindow
        // is still null at this point.

        // Once startup settles, drop the working set: WPF/JIT startup leaves a lot of pages
        // resident that are never touched again. One-shot timer, disposes itself.
        var startupTrim = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        startupTrim.Tick += (_, _) =>
        {
            startupTrim.Stop();
            CleanerService.TrimSelf();
        };
        startupTrim.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TrayService.Instance.Dispose();
        base.OnExit(e);
    }
}
