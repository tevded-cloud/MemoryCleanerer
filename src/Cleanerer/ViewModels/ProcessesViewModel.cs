using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleanerer.Services;

namespace Cleanerer.ViewModels;

/// <summary>
/// View-model backing the Processes page: polls <see cref="ProcessMonitorService.Sample"/>
/// every 2 seconds and keeps <see cref="Rows"/> updated in place (matched by PID) so the
/// grid's selection and column sort survive across ticks. Search filtering and default sort
/// are exposed through <see cref="RowsView"/>, which the view binds the grid's ItemsSource to.
/// </summary>
public partial class ProcessesViewModel : ObservableObject
{
    private readonly ProcessMonitorService _monitor = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _statusClearTimer;
    private readonly ICollectionView _rowsView;
    private bool _isSampling;

    /// <summary>Every known process, keyed implicitly by PID (one <see cref="ProcessRowViewModel"/> per PID).</summary>
    public ObservableCollection<ProcessRowViewModel> Rows { get; } = new();

    /// <summary>Filtered + sortable view over <see cref="Rows"/> that the grid binds to.</summary>
    public ICollectionView RowsView => _rowsView;

    /// <summary>Free-text filter over process name (case-insensitive contains match).</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>e.g. <c>"187 processes · 14,932 MB total working set"</c>.</summary>
    [ObservableProperty]
    private string _aggregateLine = "0 processes · 0 MB total working set";

    /// <summary>Feedback for the most recent Trim/Kill action, or null once it has cleared.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>True when <see cref="StatusMessage"/> describes a failure (drives its color).</summary>
    [ObservableProperty]
    private bool _statusIsError;

    public ProcessesViewModel()
    {
        _rowsView = CollectionViewSource.GetDefaultView(Rows);
        _rowsView.Filter = FilterRow;
        _rowsView.SortDescriptions.Add(
            new SortDescription(nameof(ProcessRowViewModel.WorkingSetBytes), ListSortDirection.Descending));

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();

        _statusClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusClearTimer.Tick += (_, _) =>
        {
            StatusMessage = null;
            _statusClearTimer.Stop();
        };

        // Populate immediately so the grid isn't empty for the first 2 seconds.
        _ = RefreshAsync();
    }

    /// <summary>
    /// Stops the poll timer and the status-clear timer. Called by the view on Unload so a
    /// navigated-away page stops sampling every process twice a second (see
    /// <see cref="MemoryViewModel.Detach"/> for the same pattern on the Memory page).
    /// The auto-management rules moved to <see cref="RulesPanelViewModel"/> on the Options page.
    /// </summary>
    public void Detach()
    {
        _pollTimer.Stop();
        _statusClearTimer.Stop();
    }

    partial void OnSearchTextChanged(string value) => _rowsView.Refresh();

    private bool FilterRow(object item)
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            return true;
        }

        return item is ProcessRowViewModel row
            && row.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshAsync()
    {
        // Guard against overlapping ticks: a heavily loaded machine could take longer than
        // 2 seconds to enumerate every process.
        if (_isSampling)
        {
            return;
        }

        _isSampling = true;
        try
        {
            IReadOnlyList<ProcessSample> samples = await Task.Run(_monitor.Sample);
            MergeSamples(samples);
        }
        finally
        {
            _isSampling = false;
        }
    }

    /// <summary>
    /// Reconciles <see cref="Rows"/> with a fresh set of samples by PID: existing rows are
    /// updated in place, rows for processes that exited are removed, and rows are added for
    /// newly seen processes. This (rather than clearing and repopulating) is what keeps the
    /// grid's selection and sort stable across ticks.
    /// </summary>
    private void MergeSamples(IReadOnlyList<ProcessSample> samples)
    {
        var byPid = new Dictionary<int, ProcessSample>(samples.Count);
        foreach (ProcessSample sample in samples)
        {
            byPid[sample.Pid] = sample;
        }

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            ProcessRowViewModel row = Rows[i];
            if (byPid.TryGetValue(row.Pid, out var sample))
            {
                row.ApplySample(sample);
                byPid.Remove(row.Pid);
            }
            else
            {
                Rows.RemoveAt(i);
            }
        }

        foreach (ProcessSample sample in byPid.Values)
        {
            Rows.Add(new ProcessRowViewModel(sample, TrimRowAsync, KillRowAsync));
        }

        long totalWorkingSet = 0;
        foreach (ProcessRowViewModel row in Rows)
        {
            totalWorkingSet += row.WorkingSetBytes;
        }

        string processWord = Rows.Count == 1 ? "process" : "processes";
        AggregateLine = $"{Rows.Count} {processWord} · {ByteFormat.Megabytes(totalWorkingSet)} total working set";
    }

    private async Task TrimRowAsync(ProcessRowViewModel row)
    {
        (bool ok, string message) = await Task.Run(() => _monitor.TrimProcess(row.Pid));
        SetStatus(ok, $"Trim {row.Name} ({row.Pid}): {message}");
    }

    private async Task KillRowAsync(ProcessRowViewModel row)
    {
        // The manual button goes through the whitelist too: this app runs elevated with
        // SeDebugPrivilege, so a misclicked kill on csrss/lsass would crash the session.
        (bool ok, string message) = await Task.Run(() => _monitor.KillProcessChecked(row.Pid, row.Name));
        SetStatus(ok, $"Kill {row.Name} ({row.Pid}): {message}");

        if (ok)
        {
            Rows.Remove(row);
        }
    }

    private void SetStatus(bool ok, string message)
    {
        StatusMessage = message;
        StatusIsError = !ok;

        _statusClearTimer.Stop();
        _statusClearTimer.Start();
    }
}
