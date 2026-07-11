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
        CurrentPage = value?.Key switch
        {
            "Memory" => new MemoryView(),
            "Processes" => new ProcessesView(),
            "Options" => new OptionsView(),
            "About" => new AboutView(),
            _ => null,
        };
    }
}
