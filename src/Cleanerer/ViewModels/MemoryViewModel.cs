using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleanerer.Services;

namespace Cleanerer.ViewModels;

/// <summary>
/// A single row in the Memory page results list.
/// </summary>
/// <param name="Ok">Whether the task succeeded (drives the ✓ / ✗ glyph and colour).</param>
/// <param name="Title">Bold action label, e.g. "Trim working sets".</param>
/// <param name="Detail">Muted secondary line, e.g. "freed 1,204 MB · Trimmed 143 processes".</param>
public record TaskResult(bool Ok, string Title, string Detail);

/// <summary>
/// View-model backing the Memory page task buttons and results list. Each command runs
/// the corresponding <see cref="CleanerService"/> method (off the UI thread for the native
/// ones) and prepends a <see cref="TaskResult"/> so the newest result shows first.
/// </summary>
public partial class MemoryViewModel : ObservableObject
{
    private readonly CleanerService _cleaner = new();

    /// <summary>Cleanup results, newest first.</summary>
    public ObservableCollection<TaskResult> Results { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>True while no task is running; bound to button IsEnabled to block concurrency.</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>True when there is at least one result (drives the list vs. empty-state).</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>True when there are no results yet (drives the dashed empty-state card).</summary>
    public bool IsEmpty => Results.Count == 0;

    public MemoryViewModel()
    {
        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(IsEmpty));
        };
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    [RelayCommand]
    private async Task TrimAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            CleanResult result = await Task.Run(_cleaner.TrimWorkingSets);
            AddResult("Trim working sets", result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            CleanResult result = await Task.Run(_cleaner.ClearSystemCache);
            AddResult("Clear system cache", result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearClipboardAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Clipboard access must stay on the UI (STA) thread — do NOT wrap in Task.Run.
            CleanResult result = _cleaner.ClearClipboard();
            AddResult("Clear clipboard", result);
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RestartExplorerAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            CleanResult result = await Task.Run(_cleaner.RestartExplorer);
            AddResult("Restart Explorer", result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddResult(string title, CleanResult result)
    {
        string detail = result.Message;
        if (result.Success && result.BytesFreed > 0)
        {
            detail = $"freed {ByteFormat.Megabytes(result.BytesFreed)} · {result.Message}";
        }

        // Newest on top.
        Results.Insert(0, new TaskResult(result.Success, title, detail));
    }
}
