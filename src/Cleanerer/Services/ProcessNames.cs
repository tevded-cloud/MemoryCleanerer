using System;

namespace Cleanerer.Services;

/// <summary>
/// Shared process-name normalization used by both the kill whitelist (<see cref="ProcessGuard"/>)
/// and the rules matcher (<see cref="RuleEngine"/>). Keeping a single canonical form here is the
/// whole safety argument: the guard and the matcher must agree, character for character, on what
/// "lsass" means, or a rule could match a process the guard fails to recognize as protected.
/// </summary>
public static class ProcessNames
{
    /// <summary>
    /// Canonicalizes a process name for comparison:
    /// <list type="bullet">
    ///   <item>null / empty / whitespace-only → <see cref="string.Empty"/> (the caller decides
    ///   what "unknown" means; the guard treats it as protected, the matcher as no-match).</item>
    ///   <item>surrounding whitespace is trimmed.</item>
    ///   <item>a single trailing <c>.exe</c> (case-insensitive) is stripped — <see cref="System.Diagnostics.Process.ProcessName"/>
    ///   never carries it, but a rule or hand-typed name might. Only ONE suffix is stripped, so
    ///   <c>"lsass.exe.exe"</c> normalizes to <c>"lsass.exe"</c> (which is NOT <c>"lsass"</c>) and is
    ///   therefore not a protected match — a real process literally named that is not lsass.</item>
    /// </list>
    /// The result is returned with original casing; all comparisons against it MUST use
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> (the guard's set does exactly this).
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string trimmed = name.Trim();

        if (trimmed.Length > 4 && trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 4);
        }

        return trimmed;
    }
}
