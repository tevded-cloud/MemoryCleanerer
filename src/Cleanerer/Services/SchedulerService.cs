using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly RulesService _rulesService;
    private readonly CleanerService _cleaner = new();
    private readonly MemoryInfoService _memoryInfo = new();
    private readonly ProcessMonitorService _monitor = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<ScheduleDecider.TriggerKey, DateTime> _lastRun = new();

    /// <summary>When each PID was last auto-trimmed, feeding <see cref="RuleEngine.TrimCooldown"/>.</summary>
    private readonly Dictionary<int, DateTime> _lastTrimByPid = new();

    /// <summary>
    /// (rule, PID) pairs whose guard-blocked warning has already been reported, so a standing rule
    /// against a protected process does not spam the results log every 30-second tick. Pruned when
    /// the PID disappears.
    /// </summary>
    private readonly HashSet<(Guid RuleId, int Pid)> _reportedBlocks = new();

    private AppSettings _settings;
    private bool _running;
    private bool _busy;

    /// <summary>
    /// Raised when an automatic cleanup finishes, with a display message such as
    /// "Auto-trim freed 512 MB". Always fired on the UI dispatcher thread when one exists.
    /// </summary>
    public event Action<string>? ActionCompleted;

    /// <summary>
    /// Raised when an auto-management RULE acts (or is blocked), e.g.
    /// "Rule 'chrome &gt; 2048 MB': trimmed chrome (PID 1234)". Consumed by the Processes page status
    /// line. Rule outcomes are ALSO sent to <see cref="ActionCompleted"/> so the Memory page results
    /// log records them too. Always fired on the UI dispatcher thread when one exists.
    /// </summary>
    public event Action<string>? RuleActionReported;

    private SchedulerService() : this(SettingsService.Instance, RulesService.Instance)
    {
    }

    // Non-singleton constructor kept internal-friendly for potential future tests; the pure
    // decision logic lives in ScheduleDecider / RuleEngine, which is what the unit tests exercise.
    private SchedulerService(SettingsService settingsService, RulesService rulesService)
    {
        _settingsService = settingsService;
        _rulesService = rulesService;
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
        // One run at a time: a slow trim (or a full process sweep for the rules pass) must not stack
        // up behind the 30s timer.
        if (_busy)
        {
            return;
        }

        DateTime now = DateTime.Now;
        int loadPercent = _memoryInfo.Read().LoadPercent;

        IReadOnlyList<ScheduledAction> actions = ScheduleDecider.Decide(now, _settings, loadPercent, _lastRun);
        bool anyEnabledRules = _rulesService.Current.Any(r => r.Enabled);

        // Nothing to do this tick: neither a scheduled cleanup nor any enabled rule.
        if (actions.Count == 0 && !anyEnabledRules)
        {
            return;
        }

        // Stamp every fired cleanup lane now so the next tick sees the cooldown/interval reset even
        // if the work is still running.
        foreach (ScheduledAction action in actions)
        {
            foreach (TriggerReason reason in action.Reasons)
            {
                _lastRun[new ScheduleDecider.TriggerKey(action.Kind, reason)] = now;
            }
        }

        _busy = true;
        _ = Task.Run(() => RunTick(actions, now));
    }

    private void RunTick(IReadOnlyList<ScheduledAction> actions, DateTime now)
    {
        try
        {
            RunActions(actions);
            RunRules(now);
        }
        finally
        {
            _busy = false;
        }
    }

    private void RunActions(IReadOnlyList<ScheduledAction> actions)
    {
        foreach (ScheduledAction action in actions)
        {
            CleanResult result = action.Kind == CleanupKind.Trim
                ? _cleaner.TrimWorkingSets()
                : _cleaner.ClearSystemCache();

            RaiseCompleted(BuildMessage(action.Kind, result));
        }
    }

    /// <summary>
    /// The auto-management rules pass, run off the UI thread every tick. Samples every process, asks
    /// the pure <see cref="RuleEngine"/> what is due, then executes the unblocked hits through the
    /// whitelist-checked <see cref="ProcessMonitorService"/> entry points (defense in depth: the guard
    /// is re-checked against the live process name at kill/trim time, not just the sample).
    /// </summary>
    private void RunRules(DateTime now)
    {
        IReadOnlyList<ProcessRule> rules = _rulesService.Current;
        if (!rules.Any(r => r.Enabled))
        {
            return;
        }

        IReadOnlyList<ProcessSample> samples = _monitor.Sample();
        int ownPid = Environment.ProcessId;
        IReadOnlyList<RuleHit> hits = RuleEngine.Evaluate(samples, rules, ownPid, now, _lastTrimByPid);

        foreach (RuleHit hit in hits)
        {
            if (hit.BlockedByGuard)
            {
                // Report a given protected target once per (rule, PID) so a standing "kill lsass"
                // rule does not flood the log every 30 seconds.
                if (_reportedBlocks.Add((hit.Rule.Id, hit.Target.Pid)))
                {
                    Report($"⚠ blocked: rule '{DescribeRule(hit.Rule)}' targets protected process {hit.Target.Name} (PID {hit.Target.Pid})");
                }
                continue;
            }

            if (hit.EffectiveAction == RuleAction.Kill)
            {
                (bool ok, string message) = _monitor.KillProcessChecked(hit.Target.Pid, hit.Target.Name);
                Report(FormatResult(hit, ok, ok ? "killed" : "kill failed", message));
            }
            else
            {
                (bool ok, string message) = _monitor.TrimProcessChecked(hit.Target.Pid, hit.Target.Name);
                if (ok)
                {
                    _lastTrimByPid[hit.Target.Pid] = now;
                }
                Report(FormatResult(hit, ok, ok ? "trimmed" : "trim failed", message));
            }
        }

        PruneByLivePids(samples);
    }

    private static string DescribeRule(ProcessRule rule)
    {
        string name = string.IsNullOrWhiteSpace(rule.MatchName) ? "*" : rule.MatchName.Trim();
        return $"{name} > {rule.ThresholdMb} MB";
    }

    private static string FormatResult(RuleHit hit, bool ok, string verb, string detail)
    {
        string rule = DescribeRule(hit.Rule);
        return ok
            ? $"Rule '{rule}': {verb} {hit.Target.Name} (PID {hit.Target.Pid})"
            : $"Rule '{rule}': {verb} for {hit.Target.Name} (PID {hit.Target.Pid}), {detail}";
    }

    /// <summary>Drops per-PID bookkeeping for processes that no longer exist (incl. ones we killed).</summary>
    private void PruneByLivePids(IReadOnlyList<ProcessSample> samples)
    {
        var live = new HashSet<int>(samples.Count);
        foreach (ProcessSample sample in samples)
        {
            live.Add(sample.Pid);
        }

        foreach (int pid in _lastTrimByPid.Keys.Where(p => !live.Contains(p)).ToList())
        {
            _lastTrimByPid.Remove(pid);
        }

        _reportedBlocks.RemoveWhere(key => !live.Contains(key.Pid));
    }

    /// <summary>Sends a rule outcome to both the Processes status line and the Memory results log.</summary>
    private void Report(string message)
    {
        RaiseOnUi(RuleActionReported, message);
        RaiseOnUi(ActionCompleted, message);
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

    private void RaiseCompleted(string message) => RaiseOnUi(ActionCompleted, message);

    private static void RaiseOnUi(Action<string>? handler, string message)
    {
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
