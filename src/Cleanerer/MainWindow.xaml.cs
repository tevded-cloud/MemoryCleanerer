using System.ComponentModel;
using System.Windows;
using Cleanerer.Services;

namespace Cleanerer;

/// <summary>
/// Main shell window: custom title bar chrome (minimize / maximize-restore / close)
/// plus the sidebar-navigated content area driven by MainViewModel.
///
/// Minimize keeps the normal taskbar behavior (it does NOT go to the tray). Only Close
/// backgrounds the app: when <see cref="AppSettings.RunInBackground"/> is on, closing cancels
/// and hides the window instead, leaving <see cref="TrayService"/> running; otherwise it is a
/// real shutdown. <see cref="AllowRealExit"/> is how the tray's Exit menu item forces the latter.
/// </summary>
public partial class MainWindow : Window
{
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizeRestoreGlyph();
        UpdateMaximizeRestoreGlyph();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Lets a genuine shutdown (the tray's Exit command) skip the background-mode hide below,
    /// even though it does not go through this instance's Close() call.
    /// </summary>
    internal void AllowRealExit()
    {
        _reallyExit = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyExit && SettingsService.Instance.Current.RunInBackground)
        {
            e.Cancel = true;
            Hide();
            TrayService.Instance.NotifyHiddenToTray();
            return;
        }

        base.OnClosing(e);
    }

    private void UpdateMaximizeRestoreGlyph()
    {
        // Segoe MDL2 Assets: E922 = Maximize, E923 = Restore.
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }
}
