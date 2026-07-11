using System;
using Microsoft.Win32;

namespace Cleanerer.Services;

/// <summary>
/// Reads and reconciles the "launch at logon" entry under the per-user Run key
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>).
///
/// Registry access is deliberately isolated here and kept thin so the rest of the app (and the
/// unit tests) never touch the real registry. Every method swallows registry failures rather
/// than throwing — a missing autostart entry must never crash settings persistence.
///
/// Elevation note: Cleanerer runs elevated, so these HKCU writes land in the hive of whichever
/// account launched the elevated process (the admin user). On a single-user machine — the
/// expected deployment — that is the same person, so the entry works as intended.
/// </summary>
public class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Memory Cleanerer";

    /// <summary>Pre-rebrand entry name; removed whenever the entry is reconciled.</summary>
    private const string LegacyValueName = "Cleanerer";

    /// <summary>True if the Run entry exists and points at the current executable.</summary>
    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string existing
                   && string.Equals(existing, QuotedExePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adds or removes the Run entry so it matches <paramref name="enabled"/>. The value is the
    /// quoted path to the current executable (<see cref="Environment.ProcessPath"/>).
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            // Rebrand migration: drop the old "Cleanerer" value so the app can't double-start.
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

            if (enabled)
            {
                key.SetValue(ValueName, QuotedExePath(), RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best effort: a locked or inaccessible registry must not break saving settings.
        }
    }

    /// <summary>Path to the running executable, quoted to survive spaces in the install path.</summary>
    private static string QuotedExePath()
    {
        string path = Environment.ProcessPath ?? string.Empty;
        return "\"" + path + "\"";
    }
}
