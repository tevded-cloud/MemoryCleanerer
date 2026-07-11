using CommunityToolkit.Mvvm.ComponentModel;
using Cleanerer.Services;

namespace Cleanerer.ViewModels;

/// <summary>
/// View-model for the Options page. Mirrors <see cref="AppSettings"/> as bindable properties and
/// persists on every change (there is no Apply button): each setter calls
/// <see cref="SettingsService.Save"/>, which also reconciles autostart and notifies the scheduler.
///
/// Numeric setters clamp into the accepted range in-place (minutes 1-1440, percent 50-99) so an
/// out-of-range entry is silently corrected rather than throwing or persisting a bad value.
/// The bool properties use the toolkit's generator; the numeric ones are hand-written so the
/// clamp and the forced-revert-on-clamp behaviour live in the setter.
/// </summary>
public partial class OptionsViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    // True only while priming from stored settings, so LoadFrom does not trigger a save per field.
    private bool _loading;

    public OptionsViewModel() : this(SettingsService.Instance)
    {
    }

    public OptionsViewModel(SettingsService settings)
    {
        _settings = settings;
        LoadFrom(settings.Current);
    }

    // ---- Startup ---------------------------------------------------------------------------

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _runInBackground;

    // ---- Automatic cleanup toggles ---------------------------------------------------------

    [ObservableProperty]
    private bool _trimIntervalEnabled;

    [ObservableProperty]
    private bool _cacheIntervalEnabled;

    [ObservableProperty]
    private bool _trimThresholdEnabled;

    [ObservableProperty]
    private bool _cacheThresholdEnabled;

    partial void OnStartWithWindowsChanged(bool value) => SaveIfLoaded();
    partial void OnRunInBackgroundChanged(bool value) => SaveIfLoaded();
    partial void OnTrimIntervalEnabledChanged(bool value) => SaveIfLoaded();
    partial void OnCacheIntervalEnabledChanged(bool value) => SaveIfLoaded();
    partial void OnTrimThresholdEnabledChanged(bool value) => SaveIfLoaded();
    partial void OnCacheThresholdEnabledChanged(bool value) => SaveIfLoaded();

    // ---- Automatic cleanup numeric values (clamped) ----------------------------------------

    private int _trimIntervalMinutes;
    public int TrimIntervalMinutes
    {
        get => _trimIntervalMinutes;
        set => SetClampedMinutes(ref _trimIntervalMinutes, value);
    }

    private int _cacheIntervalMinutes;
    public int CacheIntervalMinutes
    {
        get => _cacheIntervalMinutes;
        set => SetClampedMinutes(ref _cacheIntervalMinutes, value);
    }

    private int _trimThresholdPercent;
    public int TrimThresholdPercent
    {
        get => _trimThresholdPercent;
        set => SetClampedPercent(ref _trimThresholdPercent, value);
    }

    private int _cacheThresholdPercent;
    public int CacheThresholdPercent
    {
        get => _cacheThresholdPercent;
        set => SetClampedPercent(ref _cacheThresholdPercent, value);
    }

    private void SetClampedMinutes(ref int field, int value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => SetClamped(ref field, AppSettings.ClampMinutes(value), value, propertyName);

    private void SetClampedPercent(ref int field, int value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => SetClamped(ref field, AppSettings.ClampPercent(value), value, propertyName);

    private void SetClamped(ref int field, int clamped, int requested, string? propertyName)
    {
        if (SetProperty(ref field, clamped, propertyName))
        {
            SaveIfLoaded();
        }
        else if (clamped != requested)
        {
            // The raw entry clamped back to the current value (e.g. typing 9999 when already at the
            // max): raise anyway so a two-way TextBox reverts its text to the clamped value.
            OnPropertyChanged(propertyName);
        }
    }

    // ---- Persistence -----------------------------------------------------------------------

    private void LoadFrom(AppSettings settings)
    {
        _loading = true;
        try
        {
            StartWithWindows = settings.StartWithWindows;
            RunInBackground = settings.RunInBackground;
            TrimIntervalEnabled = settings.TrimIntervalEnabled;
            TrimIntervalMinutes = settings.TrimIntervalMinutes;
            CacheIntervalEnabled = settings.CacheIntervalEnabled;
            CacheIntervalMinutes = settings.CacheIntervalMinutes;
            TrimThresholdEnabled = settings.TrimThresholdEnabled;
            TrimThresholdPercent = settings.TrimThresholdPercent;
            CacheThresholdEnabled = settings.CacheThresholdEnabled;
            CacheThresholdPercent = settings.CacheThresholdPercent;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveIfLoaded()
    {
        if (_loading)
        {
            return;
        }

        _settings.Save(new AppSettings
        {
            StartWithWindows = StartWithWindows,
            RunInBackground = RunInBackground,
            TrimIntervalEnabled = TrimIntervalEnabled,
            TrimIntervalMinutes = TrimIntervalMinutes,
            CacheIntervalEnabled = CacheIntervalEnabled,
            CacheIntervalMinutes = CacheIntervalMinutes,
            TrimThresholdEnabled = TrimThresholdEnabled,
            TrimThresholdPercent = TrimThresholdPercent,
            CacheThresholdEnabled = CacheThresholdEnabled,
            CacheThresholdPercent = CacheThresholdPercent,
        });
    }
}
