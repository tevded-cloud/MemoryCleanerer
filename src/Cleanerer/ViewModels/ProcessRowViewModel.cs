using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleanerer.Services;

namespace Cleanerer.ViewModels;

/// <summary>
/// A single row in the Processes page grid. Instances are long-lived and updated in place
/// (see <see cref="ProcessesViewModel"/>) every poll tick rather than being recreated, so the
/// grid's selection and sort order survive across ticks.
/// </summary>
public partial class ProcessRowViewModel : ObservableObject
{
    /// <summary>Process id. Stable for the lifetime of this row (rows are keyed by PID).</summary>
    public int Pid { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private long _workingSetBytes;

    [ObservableProperty]
    private long _privateBytes;

    [ObservableProperty]
    private double _cpuPercent;

    /// <summary>e.g. <c>"1,204 MB"</c>. Kept alongside the raw value so the grid can sort on
    /// <see cref="WorkingSetBytes"/> (numeric) while displaying this formatted string.</summary>
    [ObservableProperty]
    private string _workingSetFormatted = "0 MB";

    /// <summary>e.g. <c>"812 MB"</c>. See <see cref="WorkingSetFormatted"/> for why both exist.</summary>
    [ObservableProperty]
    private string _privateFormatted = "0 MB";

    /// <summary>e.g. <c>"3.4%"</c>. See <see cref="WorkingSetFormatted"/> for why both exist.</summary>
    [ObservableProperty]
    private string _cpuPercentFormatted = "0.0%";

    /// <summary>Trims this process's working set (see <see cref="ProcessMonitorService.TrimProcess"/>).</summary>
    public IAsyncRelayCommand TrimCommand { get; }

    /// <summary>Kills this process, whitelist-checked (see <see cref="ProcessMonitorService.KillProcessChecked"/>).</summary>
    public IAsyncRelayCommand KillCommand { get; }

    public ProcessRowViewModel(ProcessSample sample, Func<ProcessRowViewModel, Task> trim, Func<ProcessRowViewModel, Task> kill)
    {
        Pid = sample.Pid;
        _name = sample.Name;
        ApplySample(sample);

        TrimCommand = new AsyncRelayCommand(() => trim(this));
        KillCommand = new AsyncRelayCommand(() => kill(this));
    }

    /// <summary>Updates this row's properties from a fresh sample. Called in place on every poll
    /// tick instead of replacing the row, so bound UI state (selection, etc.) is preserved.</summary>
    public void ApplySample(ProcessSample sample)
    {
        Name = sample.Name;
        WorkingSetBytes = sample.WorkingSetBytes;
        PrivateBytes = sample.PrivateBytes;
        CpuPercent = sample.CpuPercent;

        WorkingSetFormatted = ByteFormat.Megabytes(sample.WorkingSetBytes);
        PrivateFormatted = ByteFormat.Megabytes(sample.PrivateBytes);
        CpuPercentFormatted = sample.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }
}
