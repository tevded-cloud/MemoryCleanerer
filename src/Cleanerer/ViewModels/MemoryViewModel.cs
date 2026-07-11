using System.Collections.ObjectModel;
using System.Windows.Threading;
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
    private readonly MemoryInfoService _memoryInfo = new();
    private readonly GpuUsageService _gpu = new();
    private readonly DispatcherTimer _timer;

    /// <summary>Cleanup results, newest first.</summary>
    public ObservableCollection<TaskResult> Results { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Windows' own memory-load percentage (0-100), refreshed once per second.</summary>
    [ObservableProperty]
    private int _loadPercent;

    /// <summary>Color band for the usage gauge, derived from <see cref="LoadPercent"/> via <see cref="GaugeScale"/>.</summary>
    [ObservableProperty]
    private GaugeLevel _gaugeLevel;

    /// <summary>Physical memory currently in use, e.g. <c>13,079 MB</c>.</summary>
    [ObservableProperty]
    private string _usedFormatted = "0 MB";

    /// <summary>Total installed physical memory, e.g. <c>32,581 MB</c>.</summary>
    [ObservableProperty]
    private string _totalFormatted = "0 MB";

    /// <summary>Page file usage, e.g. <c>27,817 MB (47%)</c>.</summary>
    [ObservableProperty]
    private string _pagefileLine = "0 MB (0%)";

    /// <summary>Process virtual address space usage, e.g. <c>13,079 MB (40%)</c>.</summary>
    [ObservableProperty]
    private string _virtualLine = "0 MB (0%)";

    /// <summary>System (file) cache size, e.g. <c>1,204 MB</c>.</summary>
    [ObservableProperty]
    private string _systemCacheLine = "0 MB";

    /// <summary>GPU load, e.g. <c>7%</c>. Empty while counters initialize or when unavailable
    /// (the view hides the whole stat pair then).</summary>
    [ObservableProperty]
    private string _gpuLine = string.Empty;

    /// <summary>Session average physical memory used, e.g. <c>16,662 MB (51%)</c>.</summary>
    [ObservableProperty]
    private string _avgLine = "0 MB (0%)";

    /// <summary>Session maximum physical memory used, e.g. <c>16,662 MB (51%)</c>.</summary>
    [ObservableProperty]
    private string _maxLine = "0 MB (0%)";

    /// <summary>Session minimum physical memory used, e.g. <c>16,662 MB (51%)</c>.</summary>
    [ObservableProperty]
    private string _minLine = "0 MB (0%)";

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

        // Populate immediately so the gauge doesn't sit at 0% for the first second, then
        // poll once a second while this page is shown.
        RefreshSnapshot();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _timer.Tick += (_, _) => RefreshSnapshot();
        _timer.Start();

        // Surface automatic cleanups the scheduler performs in the same results list. The event is
        // already marshalled onto the UI thread by SchedulerService.
        SchedulerService.Instance.ActionCompleted += OnAutoActionCompleted;
    }

    /// <summary>
    /// Detaches from live sources (the poll timer and the scheduler event). Called by the view on
    /// Unload so a navigated-away page stops polling and does not leak as a dangling subscriber.
    /// </summary>
    public void Detach()
    {
        _timer.Stop();
        SchedulerService.Instance.ActionCompleted -= OnAutoActionCompleted;
    }

    private void OnAutoActionCompleted(string message)
    {
        // Newest on top, matching the manual-task rows.
        Results.Insert(0, new TaskResult(true, "Automatic cleanup", message));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    private void RefreshSnapshot()
    {
        MemorySnapshot snapshot = _memoryInfo.Read();
        SessionStats stats = _memoryInfo.SessionStats;

        LoadPercent = snapshot.LoadPercent;
        GaugeLevel = GaugeScale.Classify(snapshot.LoadPercent);
        UsedFormatted = ByteFormat.Megabytes(snapshot.UsedBytes);
        TotalFormatted = ByteFormat.Megabytes(snapshot.TotalBytes);

        double pageFilePercent = PercentOf(snapshot.PageFileUsedBytes, snapshot.PageFileTotalBytes);
        double virtualPercent = PercentOf(snapshot.VirtualUsedBytes, snapshot.VirtualTotalBytes);

        PagefileLine = ByteFormat.MegabytesWithPercent(snapshot.PageFileUsedBytes, pageFilePercent);
        VirtualLine = ByteFormat.MegabytesWithPercent(snapshot.VirtualUsedBytes, virtualPercent);
        SystemCacheLine = ByteFormat.Megabytes(snapshot.SystemCacheBytes);

        // Empty until the GPU counters finish their background build (or forever on machines
        // without them); the view hides the whole "GPU:" pair while this is empty.
        int? gpuPercent = _gpu.TryRead();
        GpuLine = gpuPercent is int gpu ? $"{gpu}%" : string.Empty;

        AvgLine = ByteFormat.MegabytesWithPercent(stats.AvgUsedBytes, stats.AvgPercent);
        MaxLine = ByteFormat.MegabytesWithPercent(stats.MaxUsedBytes, stats.MaxPercent);
        MinLine = ByteFormat.MegabytesWithPercent(stats.MinUsedBytes, stats.MinPercent);
    }

    private static double PercentOf(long value, long total) => total <= 0 ? 0 : value * 100.0 / total;

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
