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
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
