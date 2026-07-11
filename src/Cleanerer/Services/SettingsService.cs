using System;
using System.IO;
using System.Text.Json;

namespace Cleanerer.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under <c>%AppData%\Cleanerer</c>, caches the
/// current values, and raises <see cref="SettingsChanged"/> whenever they are persisted so the
/// scheduler (and, later, the tray) can react without polling.
///
/// A process-wide singleton (<see cref="Instance"/>) is shared across view-models, matching the
/// app's "no DI container" style. The constructor stays public with overridable hooks so tests
/// can point it at a temp directory and stub out the (registry-touching) autostart reconcile.
/// </summary>
public class SettingsService
{
    /// <summary>Shared instance used by the running app. Tests construct their own.</summary>
    public static SettingsService Instance { get; } = new SettingsService();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Action<bool> _applyAutostart;

    /// <summary>The most recently loaded/saved settings. Never null.</summary>
    public AppSettings Current { get; private set; }

    /// <summary>Raised after settings are persisted, carrying the new values.</summary>
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <param name="baseDirectory">
    /// Directory that holds <c>settings.json</c>. Defaults to <c>%AppData%\Cleanerer</c>; tests
    /// pass a temp path.
    /// </param>
    /// <param name="applyAutostart">
    /// Invoked on <see cref="Save"/> to reconcile the logon entry. Defaults to the real
    /// <see cref="AutostartService"/>; tests pass a no-op so they never touch the registry.
    /// </param>
    public SettingsService(string? baseDirectory = null, Action<bool>? applyAutostart = null)
    {
        baseDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cleanerer");
        _filePath = Path.Combine(baseDirectory, "settings.json");
        _applyAutostart = applyAutostart ?? (enabled => new AutostartService().SetEnabled(enabled));

        Current = Load();
    }

    /// <summary>
    /// Reads settings from disk. A missing file, unreadable file, or corrupt/partial JSON all
    /// fall back to <see cref="AppSettings"/> defaults — this never throws.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // Corrupt JSON, IO error, permissions — defaults keep the app usable.
            return new AppSettings();
        }
    }

    /// <summary>
    /// Persists <paramref name="settings"/> (creating the directory if needed), reconciles the
    /// autostart entry, updates <see cref="Current"/>, and raises <see cref="SettingsChanged"/>.
    /// A failed write is swallowed but the in-memory <see cref="Current"/> and the event still
    /// reflect the requested values.
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Disk full / permissions: keep the requested settings live in memory anyway.
        }

        _applyAutostart(settings.StartWithWindows);

        Current = settings;
        SettingsChanged?.Invoke(this, settings);
    }
}
