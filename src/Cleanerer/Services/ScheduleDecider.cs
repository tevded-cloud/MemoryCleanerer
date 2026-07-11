using System;
using System.Collections.Generic;

namespace Cleanerer.Services;

/// <summary>Which cleanup operation an automatic action performs.</summary>
public enum CleanupKind
{
    Trim,
    ClearCache,
}

/// <summary>Why an action fired: on a fixed interval, or because a load threshold was crossed.</summary>
public enum TriggerReason
{
    Interval,
    Threshold,
}

/// <summary>
/// One cleanup the scheduler should perform this tick, with every reason that called for it.
/// A single <see cref="CleanupKind"/> is emitted at most once per tick even when both its
/// interval and its threshold are due (so a busy machine never double-trims); the combined
/// <see cref="Reasons"/> let the caller stamp all the matching last-run times.
/// </summary>
public record ScheduledAction(CleanupKind Kind, IReadOnlyList<TriggerReason> Reasons);

/// <summary>
/// Pure, side-effect-free decision logic for the automatic cleanup scheduler. Given the current
/// time, the settings, the live memory-load percentage, and when each (kind, reason) last ran, it
/// returns the actions due right now. No <see cref="DateTime.Now"/> or IO lives here, so the rules
/// — interval math, threshold firing, cooldown, and dedupe — are fully unit-testable.
/// </summary>
public static class ScheduleDecider
{
    /// <summary>
    /// After a threshold-triggered action, the same action is suppressed for this long so
    /// sustained high memory usage does not re-fire it on every tick (hysteresis / cooldown).
    /// </summary>
    public static readonly TimeSpan ThresholdCooldown = TimeSpan.FromMinutes(5);

    /// <summary>Composite key identifying a single trigger lane for last-run bookkeeping.</summary>
    public readonly record struct TriggerKey(CleanupKind Kind, TriggerReason Reason);

    /// <summary>
    /// Computes the cleanups due at <paramref name="now"/>.
    /// </summary>
    /// <param name="now">Current time (injected; never read from the clock here).</param>
    /// <param name="settings">Current user settings.</param>
    /// <param name="loadPercent">Live memory-load percentage (0-100).</param>
    /// <param name="lastRun">
    /// Last time each trigger lane fired. A missing key means "never fired": an interval fires on
    /// the first eligible tick, and a threshold fires immediately on the first breach.
    /// </param>
    public static IReadOnlyList<ScheduledAction> Decide(
        DateTime now,
        AppSettings settings,
        int loadPercent,
        IReadOnlyDictionary<TriggerKey, DateTime> lastRun)
    {
        var actions = new List<ScheduledAction>(2);

        AddIfDue(actions, CleanupKind.Trim, now, loadPercent, lastRun,
            intervalEnabled: settings.TrimIntervalEnabled,
            intervalMinutes: settings.TrimIntervalMinutes,
            thresholdEnabled: settings.TrimThresholdEnabled,
            thresholdPercent: settings.TrimThresholdPercent);

        AddIfDue(actions, CleanupKind.ClearCache, now, loadPercent, lastRun,
            intervalEnabled: settings.CacheIntervalEnabled,
            intervalMinutes: settings.CacheIntervalMinutes,
            thresholdEnabled: settings.CacheThresholdEnabled,
            thresholdPercent: settings.CacheThresholdPercent);

        return actions;
    }

    private static void AddIfDue(
        List<ScheduledAction> actions,
        CleanupKind kind,
        DateTime now,
        int loadPercent,
        IReadOnlyDictionary<TriggerKey, DateTime> lastRun,
        bool intervalEnabled,
        int intervalMinutes,
        bool thresholdEnabled,
        int thresholdPercent)
    {
        var reasons = new List<TriggerReason>(2);

        if (intervalEnabled &&
            Elapsed(now, lastRun, new TriggerKey(kind, TriggerReason.Interval)) >= TimeSpan.FromMinutes(intervalMinutes))
        {
            reasons.Add(TriggerReason.Interval);
        }

        if (thresholdEnabled &&
            loadPercent >= thresholdPercent &&
            Elapsed(now, lastRun, new TriggerKey(kind, TriggerReason.Threshold)) >= ThresholdCooldown)
        {
            reasons.Add(TriggerReason.Threshold);
        }

        if (reasons.Count > 0)
        {
            actions.Add(new ScheduledAction(kind, reasons));
        }
    }

    /// <summary>
    /// Time since a lane last fired. Returns <see cref="TimeSpan.MaxValue"/> when it has never
    /// fired so any positive interval/cooldown check passes on the first eligible tick.
    /// </summary>
    private static TimeSpan Elapsed(
        DateTime now,
        IReadOnlyDictionary<TriggerKey, DateTime> lastRun,
        TriggerKey key)
    {
        return lastRun.TryGetValue(key, out DateTime last) ? now - last : TimeSpan.MaxValue;
    }
}
