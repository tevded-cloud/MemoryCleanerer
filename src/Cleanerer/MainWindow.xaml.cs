using System.Windows;

namespace Cleanerer;

/// <summary>
/// Main shell window: custom title bar chrome (minimize / maximize-restore / close)
/// plus the sidebar-navigated content area driven by MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
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

    private void UpdateMaximizeRestoreGlyph()
    {
        // Segoe MDL2 Assets: E922 = Maximize, E923 = Restore.
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }
}
