using System;
using System.Collections.Generic;

namespace Cleanerer.Services;

/// <summary>
/// THE KILL WHITELIST. Pure, exhaustively tested, and the single gate every automatic/rules-driven
/// kill must pass through. Killing a core Windows process (lsass, csrss, wininit, ...) crashes or
/// bricks the user's session, so the default answer here is "protected": anything unknown, empty,
/// or on the list is refused. Trimming is far gentler (reversible), so it has its own, much smaller
/// deny-list via <see cref="IsTrimSafe"/>.
///
/// All name comparison goes through <see cref="ProcessNames.Normalize"/> so the guard and the rule
/// matcher can never disagree about what a name means.
/// </summary>
public static class ProcessGuard
{
    /// <summary>
    /// Processes that must NEVER be killed. Normalized (no ".exe"), matched case-insensitively.
    /// Notably ABSENT: explorer (the app deliberately restarts it), chrome, spoolsv — killing those
    /// is at worst inconvenient, not fatal.
    /// </summary>
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kernel / session pseudo-processes.
        "idle", "system", "registry", "memory compression", "secure system",
        // Local Security Authority (killing lsass = instant logoff / BSOD) and its isolated twin.
        "lsass", "lsaiso",
        // Session / logon / service infrastructure.
        "csrss", "smss", "wininit", "winlogon", "services", "svchost",
        // Shell / desktop composition and input plumbing.
        "dwm", "fontdrvhost", "sihost", "ctfmon", "conhost",
        // Audio, virtualization, user-mode driver host.
        "audiodg", "vmmem", "wudfhost",
        // Belt-and-braces: never let a rule turn the app on itself (ownPid also covers this).
        // Both names covered: pre-rebrand exe and the current "Memory Cleanerer" one.
        "cleanerer", "memorycleanerer",
    };

    /// <summary>
    /// The small set of processes whose working set must NOT be trimmed either: trimming these is
    /// useless (compression / kernel-managed) or pointless-and-risky. Everything else is trim-safe —
    /// trimming is reversible and the OS simply pages memory back in on demand.
    /// </summary>
    private static readonly HashSet<string> TrimUnsafeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "registry", "memory compression", "secure system", "lsaiso",
    };

    /// <summary>
    /// Returns true when <paramref name="processName"/> / <paramref name="pid"/> must never be killed.
    /// Protected when: PID &lt;= 4 (Idle=0, System=4, and any low system PID), the PID is our own
    /// process (<paramref name="ownPid"/>), the name is unknown (null/empty/whitespace), or the
    /// normalized name is in the protected set.
    /// </summary>
    public static bool IsProtected(string? processName, int pid, int ownPid)
    {
        if (pid <= 4)
        {
            return true;
        }

        if (pid == ownPid)
        {
            return true;
        }

        string normalized = ProcessNames.Normalize(processName);
        if (normalized.Length == 0)
        {
            // Unknown name → refuse. We only ever kill something we can positively identify.
            return true;
        }

        return ProtectedNames.Contains(normalized);
    }

    /// <summary>
    /// Name-only protected check for the UI (rule-row warning): does this (possibly hand-typed)
    /// name resolve to a protected process? Empty / wildcard names are NOT flagged — they are not a
    /// specific protected process, and the per-target guard in <see cref="IsProtected"/> is what
    /// actually blocks execution.
    /// </summary>
    public static bool IsProtectedName(string? name)
    {
        string normalized = ProcessNames.Normalize(name);
        return normalized.Length > 0 && ProtectedNames.Contains(normalized);
    }

    /// <summary>
    /// Returns true when it is safe to trim <paramref name="name"/> / <paramref name="pid"/>. Unlike
    /// <see cref="IsProtected"/> this is permissive: only PID &lt;= 4, the tiny <see cref="TrimUnsafeNames"/>
    /// set, and unknown names are refused. (An unknown name is refused so a wildcard rule cannot trim
    /// something the app cannot even identify.)
    /// </summary>
    public static bool IsTrimSafe(string? name, int pid)
    {
        if (pid <= 4)
        {
            return false;
        }

        string normalized = ProcessNames.Normalize(name);
        if (normalized.Length == 0)
        {
            return false;
        }

        return !TrimUnsafeNames.Contains(normalized);
    }
}
