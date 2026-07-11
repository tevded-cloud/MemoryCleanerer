using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Cleanerer.Interop;
using Cleanerer.Services;

namespace Cleanerer;

/// <summary>
/// Main shell window: custom title bar chrome (minimize / maximize-restore / close, plus the
/// top-bar navigation strip) hosting the content area driven by MainViewModel.
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

    /// <summary>
    /// Windows 11 rounds standard windows automatically, but a custom-chrome (WindowChrome)
    /// window like this one renders square corners unless explicitly told otherwise via DWM.
    /// Older Windows 10 doesn't know this attribute at all, so failures are swallowed silently —
    /// the app must still run there, just without rounded corners.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(
                hwnd,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference,
                sizeof(int));
        }
        catch
        {
            // Attribute unsupported (pre-Windows 11) or dwmapi unavailable — not fatal.
        }
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
