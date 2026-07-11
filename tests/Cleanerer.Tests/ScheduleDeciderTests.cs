using System;
using System.Collections.Generic;
using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Covers <see cref="ScheduleDecider"/>, the pure automatic-cleanup decision logic: interval
/// due/not-due math, threshold firing, the 5-minute threshold cooldown, interval+threshold dedupe
/// to a single action, disabled settings never firing, and the settings clamp helpers.
/// </summary>
public class ScheduleDeciderTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Local);

    private static ScheduleDecider.TriggerKey Key(CleanupKind kind, TriggerReason reason) => new(kind, reason);

    private static Dictionary<ScheduleDecider.TriggerKey, DateTime> LastRun(
        params (CleanupKind kind, TriggerReason reason, DateTime when)[] entries)
    {
        var map = new Dictionary<ScheduleDecider.TriggerKey, DateTime>();
        foreach (var (kind, reason, when) in entries)
        {
            map[Key(kind, reason)] = when;
        }
        return map;
    }

    // ---- Intervals -------------------------------------------------------------------------

    [Fact]
    public void Interval_NotDue_WhenLessThanIntervalElapsed()
    {
        var settings = new AppSettings { TrimIntervalEnabled = true, TrimIntervalMinutes = 5 };
        var lastRun = LastRun((CleanupKind.Trim, TriggerReason.Interval, Now.AddMinutes(-4)));

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 10, lastRun);

        Assert.Empty(actions);
    }

    [Fact]
    public void Interval_Due_WhenIntervalElapsed()
    {
        var settings = new AppSettings { TrimIntervalEnabled = true, TrimIntervalMinutes = 5 };
        var lastRun = LastRun((CleanupKind.Trim, TriggerReason.Interval, Now.AddMinutes(-5)));

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 10, lastRun);

        ScheduledAction action = Assert.Single(actions);
        Assert.Equal(CleanupKind.Trim, action.Kind);
        Assert.Equal(new[] { TriggerReason.Interval }, action.Reasons);
    }

    [Fact]
    public void Interval_Due_OnFirstTick_WhenNeverRun()
    {
        var settings = new AppSettings { CacheIntervalEnabled = true, CacheIntervalMinutes = 30 };

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 10, LastRun());

        ScheduledAction action = Assert.Single(actions);
        Assert.Equal(CleanupKind.ClearCache, action.Kind);
    }

    // ---- Thresholds ------------------------------------------------------------------------

    [Fact]
    public void Threshold_Fires_WhenLoadAtOrAboveThreshold()
    {
        var settings = new AppSettings { TrimThresholdEnabled = true, TrimThresholdPercent = 80 };

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 80, LastRun());

        ScheduledAction action = Assert.Single(actions);
        Assert.Equal(CleanupKind.Trim, action.Kind);
        Assert.Equal(new[] { TriggerReason.Threshold }, action.Reasons);
    }

    [Fact]
    public void Threshold_DoesNotFire_WhenLoadBelowThreshold()
    {
        var settings = new AppSettings { TrimThresholdEnabled = true, TrimThresholdPercent = 80 };

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 79, LastRun());

        Assert.Empty(actions);
    }

    [Fact]
    public void Threshold_Cooldown_BlocksRefireWithinFiveMinutes()
    {
        var settings = new AppSettings { TrimThresholdEnabled = true, TrimThresholdPercent = 80 };
        var lastRun = LastRun((CleanupKind.Trim, TriggerReason.Threshold, Now.AddMinutes(-4)));

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 95, lastRun);

        Assert.Empty(actions);
    }

    [Fact]
    public void Threshold_Refires_AfterCooldownElapses()
    {
        var settings = new AppSettings { TrimThresholdEnabled = true, TrimThresholdPercent = 80 };
        var lastRun = LastRun((CleanupKind.Trim, TriggerReason.Threshold, Now.AddMinutes(-5)));

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 95, lastRun);

        Assert.Single(actions);
    }

    // ---- Dedupe ----------------------------------------------------------------------------

    [Fact]
    public void IntervalAndThreshold_SameTick_DedupeToSingleTrimWithBothReasons()
    {
        var settings = new AppSettings
        {
            TrimIntervalEnabled = true,
            TrimIntervalMinutes = 5,
            TrimThresholdEnabled = true,
            TrimThresholdPercent = 80,
        };
        // Interval due (last ran 6 min ago), threshold breached and never cooled down.
        var lastRun = LastRun((CleanupKind.Trim, TriggerReason.Interval, Now.AddMinutes(-6)));

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 90, lastRun);

        ScheduledAction action = Assert.Single(actions);
        Assert.Equal(CleanupKind.Trim, action.Kind);
        Assert.Equal(2, action.Reasons.Count);
        Assert.Contains(TriggerReason.Interval, action.Reasons);
        Assert.Contains(TriggerReason.Threshold, action.Reasons);
    }

    [Fact]
    public void TrimAndCache_BothDue_ProduceTwoDistinctActions()
    {
        var settings = new AppSettings
        {
            TrimThresholdEnabled = true,
            TrimThresholdPercent = 80,
            CacheThresholdEnabled = true,
            CacheThresholdPercent = 80,
        };

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 85, LastRun());

        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, a => a.Kind == CleanupKind.Trim);
        Assert.Contains(actions, a => a.Kind == CleanupKind.ClearCache);
    }

    // ---- Disabled --------------------------------------------------------------------------

    [Fact]
    public void AllDisabled_NeverFires_EvenAtFullLoad()
    {
        var settings = new AppSettings(); // every feature off by default

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 100, LastRun());

        Assert.Empty(actions);
    }

    [Fact]
    public void DisabledInterval_DoesNotFire_EvenWhenOverdue()
    {
        var settings = new AppSettings { TrimIntervalEnabled = false, TrimIntervalMinutes = 1 };
        var lastRun = LastRun((CleanupKind.Trim, TriggerReason.Interval, Now.AddHours(-2)));

        var actions = ScheduleDecider.Decide(Now, settings, loadPercent: 10, lastRun);

        Assert.Empty(actions);
    }

    // ---- Clamp helpers ---------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(1440, 1440)]
    [InlineData(9999, 1440)]
    [InlineData(-3, 1)]
    public void ClampMinutes_ConstrainsToOneThrough1440(int input, int expected)
    {
        Assert.Equal(expected, AppSettings.ClampMinutes(input));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(49, 50)]
    [InlineData(50, 50)]
    [InlineData(80, 80)]
    [InlineData(99, 99)]
    [InlineData(100, 99)]
    [InlineData(250, 99)]
    public void ClampPercent_ConstrainsTo50Through99(int input, int expected)
    {
        Assert.Equal(expected, AppSettings.ClampPercent(input));
    }
}
