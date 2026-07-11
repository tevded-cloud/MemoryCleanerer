using System;
using System.IO;
using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Covers <see cref="SettingsService"/> persistence: a full round-trip of every field, and the
/// two "never throw" fallbacks (missing file, corrupt JSON). Each test runs against a throwaway
/// temp directory and stubs the autostart reconcile so the real registry is never touched.
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "Cleanerer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private SettingsService NewService(Action<bool>? applyAutostart = null)
        => new SettingsService(_dir, applyAutostart ?? (_ => { }));

    private static AppSettings NonDefaultSettings() => new()
    {
        StartWithWindows = true,
        RunInBackground = false,
        TrimIntervalEnabled = true,
        TrimIntervalMinutes = 12,
        CacheIntervalEnabled = true,
        CacheIntervalMinutes = 33,
        TrimThresholdEnabled = true,
        TrimThresholdPercent = 77,
        CacheThresholdEnabled = true,
        CacheThresholdPercent = 66,
    };

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        AppSettings original = NonDefaultSettings();

        NewService().Save(original);
        AppSettings reloaded = NewService().Load();

        // Records give value equality, so this compares all ten fields at once.
        Assert.Equal(original, reloaded);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        AppSettings loaded = NewService().Load();

        Assert.Equal(new AppSettings(), loaded);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not valid json ]]");

        AppSettings loaded = NewService().Load();

        Assert.Equal(new AppSettings(), loaded);
    }

    [Fact]
    public void Constructor_LoadsCurrentFromDisk()
    {
        NewService().Save(NonDefaultSettings());

        SettingsService fresh = NewService();

        Assert.Equal(NonDefaultSettings(), fresh.Current);
    }

    [Fact]
    public void Save_RaisesSettingsChangedWithNewValues()
    {
        SettingsService service = NewService();
        AppSettings? received = null;
        service.SettingsChanged += (_, s) => received = s;

        AppSettings toSave = NonDefaultSettings();
        service.Save(toSave);

        Assert.Equal(toSave, received);
        Assert.Equal(toSave, service.Current);
    }

    [Fact]
    public void Save_ReconcilesAutostartWithStartWithWindows()
    {
        bool? applied = null;
        SettingsService service = NewService(applyAutostart: enabled => applied = enabled);

        service.Save(new AppSettings { StartWithWindows = true });

        Assert.True(applied);
    }
}
