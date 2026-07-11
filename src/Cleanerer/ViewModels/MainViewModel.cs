using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cleanerer.Views;

namespace Cleanerer.ViewModels;

/// <summary>
/// A single entry in the title-bar navigation strip.
/// </summary>
public record NavItem(string Title, string Subtitle, string Glyph, string Key);

/// <summary>
/// View-model for the main shell window: owns the nav item list and swaps
/// the content area between the placeholder pages as selection changes.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    /// <summary>All nav items, in display order: Memory, Processes, Options, About.</summary>
    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new NavItem("Memory", "Usage, gauge & cleanup", "", "Memory"),
        new NavItem("Processes", "Watch, trim & auto-manage", "", "Processes"),
        new NavItem("Options", "Timers, thresholds & startup", "", "Options"),
        new NavItem("About", "Version & credits", "", "About"),
    };

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private object? _currentPage;

    public MainViewModel()
    {
        SelectedNav = NavItems[0];
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

        // A page constructor that throws inside this binding-driven call would otherwise be
        // swallowed by the WPF binding engine and look like a dead nav click — log and surface it.
        try
        {
            CurrentPage = value.Key switch
            {
                "Memory" => new MemoryView(),
                "Processes" => new ProcessesView(),
                "Options" => new OptionsView(),
                "About" => new AboutView(),
                _ => null,
            };
        }
        catch (System.Exception ex)
        {
            App.LogError($"Navigate({value.Key})", ex);
            CurrentPage = new System.Windows.Controls.TextBlock
            {
                Text = $"This page failed to load:\n{ex.Message}\n\nSee %AppData%\\Cleanerer\\error.log.",
                Margin = new System.Windows.Thickness(32),
                TextWrapping = System.Windows.TextWrapping.Wrap,
            };
        }
    }
}
