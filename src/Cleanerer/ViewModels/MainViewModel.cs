using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Cleanerer.Views;

namespace Cleanerer.ViewModels;

/// <summary>
/// A single entry in the left sidebar navigation.
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
        new NavItem("Memory", "Usage, gauge & cleanup", "", "Memory"),
        new NavItem("Processes", "Watch, trim & auto-manage", "", "Processes"),
        new NavItem("Options", "Timers, thresholds & startup", "", "Options"),
        new NavItem("About", "Version & credits", "", "About"),
    };

    /// <summary>Sidebar section: "MONITOR" (Memory, Processes).</summary>
    public IEnumerable<NavItem> MonitorItems => NavItems.Take(2);

    /// <summary>Sidebar section: "CONTROL" (Options).</summary>
    public IEnumerable<NavItem> ControlItems => NavItems.Skip(2).Take(1);

    /// <summary>Bottom-docked entry: About.</summary>
    public IEnumerable<NavItem> AboutItems => NavItems.Skip(3).Take(1);

    // Each sidebar section (ListBox) gets its own selection property. They must NOT share one:
    // a section that can't find the newly-selected item in its ItemsSource pushes null back
    // through a shared two-way binding, wiping the navigation. Navigate() coordinates them.
    [ObservableProperty]
    private NavItem? _selectedMonitor;

    [ObservableProperty]
    private NavItem? _selectedControl;

    [ObservableProperty]
    private NavItem? _selectedAbout;

    [ObservableProperty]
    private object? _currentPage;

    /// <summary>Guards against the selection-changed handlers re-entering while Navigate syncs the three lists.</summary>
    private bool _navigating;

    public MainViewModel()
    {
        Navigate(NavItems[0]);
    }

    partial void OnSelectedMonitorChanged(NavItem? value) => OnSectionSelected(value);

    partial void OnSelectedControlChanged(NavItem? value) => OnSectionSelected(value);

    partial void OnSelectedAboutChanged(NavItem? value) => OnSectionSelected(value);

    private void OnSectionSelected(NavItem? value)
    {
        // Ignore the nulls produced when Navigate clears the other sections, and ignore
        // re-entrant callbacks while a navigation is already in flight.
        if (value is not null && !_navigating)
        {
            Navigate(value);
        }
    }

    private void Navigate(NavItem item)
    {
        _navigating = true;
        try
        {
            SelectedMonitor = MonitorItems.Contains(item) ? item : null;
            SelectedControl = ControlItems.Contains(item) ? item : null;
            SelectedAbout = AboutItems.Contains(item) ? item : null;
        }
        finally
        {
            _navigating = false;
        }

        CurrentPage = item.Key switch
        {
            "Memory" => new MemoryView(),
            "Processes" => new ProcessesView(),
            "Options" => new OptionsView(),
            "About" => new AboutView(),
            _ => null,
        };
    }
}
