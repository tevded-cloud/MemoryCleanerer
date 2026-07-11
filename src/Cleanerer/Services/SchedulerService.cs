using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Cleanerer.Services;

/// <summary>
/// Drives automatic cleanup. A single 30-second <see cref="DispatcherTimer"/> asks the pure
/// <see cref="ScheduleDecider"/> what is due (interval trims/cache clears and threshold-triggered
/// ones with a cooldown), then runs the chosen <see cref="CleanerService"/> operations off the UI
/// thread. Completions are surfaced through <see cref="ActionCompleted"/> as a human-readable
/// message, already marshalled onto the UI thread, so the Memory page can append them directly.
///
/// A process-wide singleton (<see cref="Instance"/>) shared with the settings service, matching the
/// app's "no DI container" style. Start it once at app startup via <see cref="Start"/>.
/// </summary>
public class SchedulerService
{
    /// <summary>Shared instance used by the running app.</summary>
    public static SchedulerService Instance { get; } = new SchedulerService();

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly SettingsService _settingsService;
    private readonly CleanerService _cleaner = new();
    private readonly MemoryInfoService _memoryInfo = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<ScheduleDecider.TriggerKey, DateTime> _lastRun = new();

    private AppSettings _settings;
    private bool _running;
    private bool _busy;

    /// <summary>
    /// Raised when an automatic cleanup finishes, with a display message such as
    /// "Auto-trim freed 512 MB". Always fired on the UI dispatcher thread when one exists.
    /// </summary>
    public event Action<string>? ActionCompleted;

    private SchedulerService() : this(SettingsService.Instance)
    {
    }

    // Non-singleton constructor kept internal-friendly for potential future tests; the pure
    // decision logic lives in ScheduleDecider, which is what the unit tests exercise.
    private SchedulerService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Current;
        settingsService.SettingsChanged += OnSettingsChanged;

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Seeds interval baselines to "now" and starts ticking. Idempotent.</summary>
    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        SeedIntervalBaselines(DateTime.Now);
        _timer.Start();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _settings = settings;
        // A newly enabled interval should count from now rather than firing instantly.
        SeedIntervalBaselines(DateTime.Now);
    }

    /// <summary>
    /// Ensures each enabled interval lane has a baseline so it fires N minutes from now, not on
    /// the very next tick. Existing baselines (a countdown already in progress) are left alone.
    /// </summary>
    private void SeedIntervalBaselines(DateTime now)
    {
        SeedIfMissing(new ScheduleDecider.TriggerKey(CleanupKind.Trim, TriggerReason.Interval), _settings.TrimIntervalEnabled, now);
        SeedIfMissing(new ScheduleDecider.TriggerKey(CleanupKind.ClearCache, TriggerReason.Interval), _settings.CacheIntervalEnabled, now);
    }

    private void SeedIfMissing(ScheduleDecider.TriggerKey key, bool enabled, DateTime now)
    {
        if (enabled && !_lastRun.ContainsKey(key))
        {
            _lastRun[key] = now;
        }
    }

    private void Tick()
    {
        // One run at a time: a slow trim must not stack up behind the 30s timer.
        if (_busy)
        {
            return;
        }

        DateTime now = DateTime.Now;
        int loadPercent = _memoryInfo.Read().LoadPercent;

        IReadOnlyList<ScheduledAction> actions = ScheduleDecider.Decide(now, _settings, loadPercent, _lastRun);
        if (actions.Count == 0)
        {
            return;
        }

        // Stamp every fired lane now so the next tick sees the cooldown/interval reset even if the
        // work is still running.
        foreach (ScheduledAction action in actions)
        {
            foreach (TriggerReason reason in action.Reasons)
            {
                _lastRun[new ScheduleDecider.TriggerKey(action.Kind, reason)] = now;
            }
        }

        _busy = true;
        _ = Task.Run(() => RunActions(actions));
    }

    private void RunActions(IReadOnlyList<ScheduledAction> actions)
    {
        try
        {
            foreach (ScheduledAction action in actions)
            {
                CleanResult result = action.Kind == CleanupKind.Trim
                    ? _cleaner.TrimWorkingSets()
                    : _cleaner.ClearSystemCache();

                RaiseCompleted(BuildMessage(action.Kind, result));
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private static string BuildMessage(CleanupKind kind, CleanResult result)
    {
        string verb = kind == CleanupKind.Trim ? "Auto-trim" : "Auto cache clear";

        if (!result.Success)
        {
            return $"{verb} failed: {result.Message}";
        }

        return result.BytesFreed > 0
            ? $"{verb} freed {ByteFormat.Megabytes(result.BytesFreed)}"
            : $"{verb} completed ({result.Message})";
    }

    private void RaiseCompleted(string message)
    {
        Action<string>? handler = ActionCompleted;
        if (handler is null)
        {
            return;
        }

        Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => handler(message));
        }
        else
        {
            handler(message);
        }
    }
}
