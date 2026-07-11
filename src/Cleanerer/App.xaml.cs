using System.Windows;
using Cleanerer.Services;

namespace Cleanerer;

/// <summary>
/// Interaction logic for App.xaml. Starts the automatic-cleanup scheduler once at startup; the
/// scheduler and settings service are process-wide singletons shared with the view-models.
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Kicks off the 30s scheduler tick. It reads current settings and reacts to changes via
        // SettingsService.SettingsChanged, so nothing else needs to poke it.
        SchedulerService.Instance.Start();
    }
}
