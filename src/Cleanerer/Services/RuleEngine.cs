using System;
using System.Collections.Generic;

namespace Cleanerer.Services;

/// <summary>
/// One matched rule paired with the process it targets and the verdict.
/// </summary>
/// <param name="Rule">The rule that matched (the representative one, after dedupe).</param>
/// <param name="Target">The process sample that breached the rule's threshold.</param>
/// <param name="EffectiveAction">The action that will run for this PID after kill-over-trim dedupe.</param>
/// <param name="BlockedByGuard">
/// True when the action must NOT execute because <see cref="ProcessGuard"/> refused it (a kill of a
/// protected process, or a trim of a trim-unsafe one). Surfaced in the UI as a warning; the executor
/// skips it.
/// </param>
public record RuleHit(ProcessRule Rule, ProcessSample Target, RuleAction EffectiveAction, bool BlockedByGuard);

/// <summary>
/// Pure, side-effect-free rule matcher — the <see cref="ScheduleDecider"/> of unit 6b. Given the
/// current process samples, the rules, our own PID, the current time, and per-PID last-trim times,
/// it returns the actions due right now. No sampling, no clock, no killing happens here, so every
/// safety-relevant decision (matching, the whitelist verdict, kill-over-trim dedupe, trim cooldown)
/// is fully unit-testable.
///
/// Dedupe / precedence rules, all decided here and tested:
/// <list type="bullet">
///   <item>At most ONE action per PID.</item>
///   <item>Kill wins over trim when both match the same PID: the user explicitly configured a kill,
///   which is destructive intent, so the trim is skipped as redundant. (This holds even if the kill
///   is guard-blocked — the safe outcome is "do nothing", not "silently downgrade to a trim".)</item>
///   <item>A trim within its per-PID cooldown is suppressed entirely (no hit emitted) to avoid
///   thrashing a process every tick. A guard-blocked trim is still emitted (as a warning) regardless
///   of cooldown, since it never executes.</item>
///   <item>Our own process is excluded from matching outright.</item>
/// </list>
/// </summary>
public static class RuleEngine
{
    /// <summary>
    /// After a successful trim of a PID, further trims of that same PID are suppressed for this long.
    /// Kills need no cooldown — a killed process is gone (and its PID may be reused by something the
    /// guard must re-evaluate from scratch).
    /// </summary>
    public static readonly TimeSpan TrimCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Computes the rule hits due at <paramref name="now"/>.
    /// </summary>
    /// <param name="samples">Current process snapshot (from <see cref="ProcessMonitorService.Sample"/>).</param>
    /// <param name="rules">All configured rules; disabled ones are ignored.</param>
    /// <param name="ownPid">This process's PID, excluded from matching.</param>
    /// <param name="now">Current time (injected; never read from the clock here).</param>
    /// <param name="lastTrimByPid">
    /// When each PID was last trimmed. A missing PID means "never trimmed" so the first trim always
    /// passes the cooldown. Passed in (like <see cref="ScheduleDecider"/>'s last-run map) to keep this
    /// method pure.
    /// </param>
    public static IReadOnlyList<RuleHit> Evaluate(
        IReadOnlyList<ProcessSample> samples,
        IReadOnlyList<ProcessRule> rules,
        int ownPid,
        DateTime now,
        IReadOnlyDictionary<int, DateTime> lastTrimByPid)
    {
        var hits = new List<RuleHit>();

        // Fast out: nothing enabled means nothing to do.
        bool anyEnabled = false;
        foreach (ProcessRule rule in rules)
        {
            if (rule.Enabled)
            {
                anyEnabled = true;
                break;
            }
        }

        if (!anyEnabled)
        {
            return hits;
        }

        foreach (ProcessSample sample in samples)
        {
            // Never let a rule target the app itself. The guard also covers ownPid, but excluding it
            // here keeps it out of the UI warning stream entirely.
            if (sample.Pid == ownPid)
            {
                continue;
            }

            // Find the first matching kill rule and the first matching trim rule for this PID.
            ProcessRule? killRule = null;
            ProcessRule? trimRule = null;

            foreach (ProcessRule rule in rules)
            {
                if (!rule.Enabled || !Matches(rule, sample))
                {
                    continue;
                }

                if (rule.Action == RuleAction.Kill)
                {
                    killRule ??= rule;
                }
                else
                {
                    trimRule ??= rule;
                }
            }

            if (killRule is not null)
            {
                // Kill wins over any trim on the same PID (destructive intent takes precedence).
                bool blocked = ProcessGuard.IsProtected(sample.Name, sample.Pid, ownPid);
                hits.Add(new RuleHit(killRule, sample, RuleAction.Kill, blocked));
                continue;
            }

            if (trimRule is not null)
            {
                if (!ProcessGuard.IsTrimSafe(sample.Name, sample.Pid))
                {
                    // Emit the warning every evaluation — the executor / UI can de-dupe display.
                    hits.Add(new RuleHit(trimRule, sample, RuleAction.Trim, BlockedByGuard: true));
                }
                else if (TrimCooldownElapsed(now, lastTrimByPid, sample.Pid))
                {
                    hits.Add(new RuleHit(trimRule, sample, RuleAction.Trim, BlockedByGuard: false));
                }
                // else: within cooldown → suppressed (no hit).
            }
        }

        return hits;
    }

    /// <summary>
    /// True when <paramref name="sample"/> both matches the rule's name and strictly exceeds its
    /// threshold. The threshold comparison is strict (<c>&gt;</c>): a process sitting at exactly
    /// <see cref="ProcessRule.ThresholdMb"/> does not fire.
    /// </summary>
    private static bool Matches(ProcessRule rule, ProcessSample sample)
    {
        long thresholdBytes = (long)rule.ThresholdMb * 1024 * 1024;
        if (sample.WorkingSetBytes <= thresholdBytes)
        {
            return false;
        }

        string match = rule.MatchName?.Trim() ?? string.Empty;
        if (match.Length == 0 || match == "*")
        {
            return true;
        }

        return string.Equals(
            ProcessNames.Normalize(match),
            ProcessNames.Normalize(sample.Name),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrimCooldownElapsed(
        DateTime now,
        IReadOnlyDictionary<int, DateTime> lastTrimByPid,
        int pid)
    {
        return !lastTrimByPid.TryGetValue(pid, out DateTime last) || (now - last) >= TrimCooldown;
    }
}
