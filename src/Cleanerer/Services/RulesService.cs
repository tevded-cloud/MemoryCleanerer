using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Cleanerer.Services;

/// <summary>
/// Loads and saves the auto-management rules as <c>rules.json</c>, a sibling of <c>settings.json</c>
/// under <c>%AppData%\Cleanerer</c>. Mirrors <see cref="SettingsService"/>: a process-wide singleton
/// (<see cref="Instance"/>) is the shared source of truth (the Processes page edits it, the scheduler
/// reads it), <see cref="Load"/> never throws (a missing or corrupt file yields an empty list), and
/// <see cref="Save"/> raises <see cref="RulesChanged"/> after persisting.
/// </summary>
public class RulesService
{
    /// <summary>Shared instance used by the running app. Tests construct their own.</summary>
    public static RulesService Instance { get; } = new RulesService();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    /// <summary>The most recently loaded/saved rules. Never null.</summary>
    public IReadOnlyList<ProcessRule> Current { get; private set; }

    /// <summary>Raised after rules are persisted, carrying the new list.</summary>
    public event EventHandler<IReadOnlyList<ProcessRule>>? RulesChanged;

    /// <param name="baseDirectory">
    /// Directory that holds <c>rules.json</c>. Defaults to <c>%AppData%\Cleanerer</c>; tests pass a
    /// temp path.
    /// </param>
    public RulesService(string? baseDirectory = null)
    {
        baseDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cleanerer");
        _filePath = Path.Combine(baseDirectory, "rules.json");

        Current = Load();
    }

    /// <summary>
    /// Reads rules from disk. A missing file, unreadable file, or corrupt/partial JSON all fall back
    /// to an empty list — this never throws.
    /// </summary>
    public IReadOnlyList<ProcessRule> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new List<ProcessRule>();
            }

            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<ProcessRule>>(json) ?? new List<ProcessRule>();
        }
        catch
        {
            // Corrupt JSON, IO error, permissions — an empty rule set keeps the app safe (no
            // automatic actions) rather than acting on garbage.
            return new List<ProcessRule>();
        }
    }

    /// <summary>
    /// Persists <paramref name="rules"/> (creating the directory if needed), updates
    /// <see cref="Current"/>, and raises <see cref="RulesChanged"/>. A failed write is swallowed but
    /// the in-memory <see cref="Current"/> and the event still reflect the requested values.
    /// </summary>
    public void Save(IReadOnlyList<ProcessRule> rules)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(rules, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Disk full / permissions: keep the requested rules live in memory anyway.
        }

        Current = rules;
        RulesChanged?.Invoke(this, rules);
    }
}
