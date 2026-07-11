using System;

namespace Cleanerer.Services;

/// <summary>
/// Persisted user preferences for Cleanerer. Serialized to
/// <c>%AppData%\Cleanerer\settings.json</c> by <see cref="SettingsService"/>.
///
/// A record with <c>init</c> setters is used so value equality comes for free (handy for the
/// settings round-trip tests) while callers still build new instances with the <c>with</c>
/// expression. All four automatic-cleanup features default to <c>false</c>: the original app
/// shipped some of these on, but opt-in is the safer, less surprising default.
/// </summary>
public record AppSettings
{
    /// <summary>Register Cleanerer in the HKCU Run key so it launches at logon.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>
    /// Keep running (minimized to the tray) when the window is closed. Maps to the original
    /// app's inverted "Do not run in background" option. Defaults to <c>true</c>.
    /// </summary>
    public bool RunInBackground { get; init; } = true;

    /// <summary>Trim every process's working set every <see cref="TrimIntervalMinutes"/> minutes.</summary>
    public bool TrimIntervalEnabled { get; init; }

    /// <summary>Interval, in minutes, for the periodic working-set trim. Clamped 1-1440.</summary>
    public int TrimIntervalMinutes { get; init; } = 5;

    /// <summary>Flush system caches every <see cref="CacheIntervalMinutes"/> minutes.</summary>
    public bool CacheIntervalEnabled { get; init; }

    /// <summary>Interval, in minutes, for the periodic cache clear. Clamped 1-1440.</summary>
    public int CacheIntervalMinutes { get; init; } = 5;

    /// <summary>Trim working sets when memory load reaches <see cref="TrimThresholdPercent"/>.</summary>
    public bool TrimThresholdEnabled { get; init; }

    /// <summary>Memory-load percentage that triggers an automatic trim. Clamped 50-99.</summary>
    public int TrimThresholdPercent { get; init; } = 80;

    /// <summary>Clear caches when memory load reaches <see cref="CacheThresholdPercent"/>.</summary>
    public bool CacheThresholdEnabled { get; init; }

    /// <summary>Memory-load percentage that triggers an automatic cache clear. Clamped 50-99.</summary>
    public int CacheThresholdPercent { get; init; } = 80;

    /// <summary>Smallest / largest sensible interval, in minutes (1 minute .. 24 hours).</summary>
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 1440;

    /// <summary>Threshold percentages below 50 would fire almost constantly; 100 could never fire.</summary>
    public const int MinThresholdPercent = 50;
    public const int MaxThresholdPercent = 99;

    /// <summary>Clamps an interval-minutes value into the accepted range without throwing.</summary>
    public static int ClampMinutes(int value) => Math.Clamp(value, MinIntervalMinutes, MaxIntervalMinutes);

    /// <summary>Clamps a threshold-percent value into the accepted range without throwing.</summary>
    public static int ClampPercent(int value) => Math.Clamp(value, MinThresholdPercent, MaxThresholdPercent);
}
