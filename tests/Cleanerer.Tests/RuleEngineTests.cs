using System;
using System.Collections.Generic;
using System.Linq;
using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Covers <see cref="RuleEngine.Evaluate"/>, the pure rule matcher: name/wildcard matching, the strict
/// threshold boundary, disabled rules, kill-over-trim dedupe on a single PID, guard-blocked kills and
/// trims, the per-PID trim cooldown, and own-PID exclusion.
/// </summary>
public class RuleEngineTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Local);
    private const int OwnPid = 4242;

    private const long Mb = 1024 * 1024;

    private static ProcessSample Sample(int pid, string name, int workingSetMb)
        => new(pid, name, workingSetMb * Mb, workingSetMb * Mb, 0.0);

    private static ProcessRule Rule(
        string match = "*",
        int thresholdMb = 100,
        RuleAction action = RuleAction.Trim,
        bool enabled = true)
        => new() { MatchName = match, ThresholdMb = thresholdMb, Action = action, Enabled = enabled };

    private static IReadOnlyList<RuleHit> Evaluate(
        IReadOnlyList<ProcessSample> samples,
        IReadOnlyList<ProcessRule> rules,
        IReadOnlyDictionary<int, DateTime>? lastTrim = null)
        => RuleEngine.Evaluate(samples, rules, OwnPid, Now, lastTrim ?? new Dictionary<int, DateTime>());

    // ---- Matching --------------------------------------------------------------------------

    [Fact]
    public void Wildcard_Matches_AnyProcessOverThreshold()
    {
        var hits = Evaluate(
            new[] { Sample(10, "chrome", 500) },
            new[] { Rule(match: "*", thresholdMb: 100) });

        RuleHit hit = Assert.Single(hits);
        Assert.Equal(10, hit.Target.Pid);
        Assert.Equal(RuleAction.Trim, hit.EffectiveAction);
        Assert.False(hit.BlockedByGuard);
    }

    [Fact]
    public void EmptyMatchName_BehavesAsWildcard()
    {
        var hits = Evaluate(
            new[] { Sample(10, "chrome", 500) },
            new[] { Rule(match: "", thresholdMb: 100) });

        Assert.Single(hits);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("CHROME")]
    [InlineData("chrome.exe")]
    [InlineData(" Chrome.EXE ")]
    public void ExactMatch_IsCaseAndExeInsensitive(string match)
    {
        var hits = Evaluate(
            new[] { Sample(10, "chrome", 500) },
            new[] { Rule(match: match, thresholdMb: 100) });

        Assert.Single(hits);
    }

    [Fact]
    public void ExactMatch_DoesNotMatchDifferentProcess()
    {
        var hits = Evaluate(
            new[] { Sample(10, "firefox", 500) },
            new[] { Rule(match: "chrome", thresholdMb: 100) });

        Assert.Empty(hits);
    }

    // ---- Threshold boundary ----------------------------------------------------------------

    [Fact]
    public void Threshold_ExactlyAtLimit_DoesNotFire()
    {
        // Strict ">": a process sitting at exactly ThresholdMb does not match.
        var hits = Evaluate(
            new[] { Sample(10, "chrome", 100) },
            new[] { Rule(match: "chrome", thresholdMb: 100) });

        Assert.Empty(hits);
    }

    [Fact]
    public void Threshold_OneMbOver_Fires()
    {
        var hits = Evaluate(
            new[] { Sample(10, "chrome", 101) },
            new[] { Rule(match: "chrome", thresholdMb: 100) });

        Assert.Single(hits);
    }

    // ---- Disabled --------------------------------------------------------------------------

    [Fact]
    public void DisabledRule_NeverFires()
    {
        var hits = Evaluate(
            new[] { Sample(10, "chrome", 5000) },
            new[] { Rule(match: "chrome", thresholdMb: 1, enabled: false) });

        Assert.Empty(hits);
    }

    // ---- Kill-vs-trim dedupe ---------------------------------------------------------------

    [Fact]
    public void KillAndTrim_SamePid_KillWins_TrimSkipped()
    {
        var hits = Evaluate(
            new[] { Sample(10, "chrome", 5000) },
            new[]
            {
                Rule(match: "chrome", thresholdMb: 100, action: RuleAction.Trim),
                Rule(match: "chrome", thresholdMb: 100, action: RuleAction.Kill),
            });

        RuleHit hit = Assert.Single(hits);
        Assert.Equal(RuleAction.Kill, hit.EffectiveAction);
        Assert.False(hit.BlockedByGuard);
    }

    [Fact]
    public void OneActionPerPid_EvenWithMultipleTrimRules()
    {
        var hits = Evaluate(
            new[] { Sample(10, "chrome", 5000) },
            new[]
            {
                Rule(match: "*", thresholdMb: 100, action: RuleAction.Trim),
                Rule(match: "chrome", thresholdMb: 200, action: RuleAction.Trim),
            });

        Assert.Single(hits);
    }

    // ---- Guard blocking --------------------------------------------------------------------

    [Fact]
    public void KillRule_OnProtectedProcess_IsBlockedNotExecuted()
    {
        var hits = Evaluate(
            new[] { Sample(500, "lsass", 5000) },
            new[] { Rule(match: "lsass", thresholdMb: 100, action: RuleAction.Kill) });

        RuleHit hit = Assert.Single(hits);
        Assert.Equal(RuleAction.Kill, hit.EffectiveAction);
        Assert.True(hit.BlockedByGuard);
    }

    [Fact]
    public void WildcardKill_SweepsUpProtectedProcess_AsBlocked()
    {
        var hits = Evaluate(
            new[]
            {
                Sample(500, "lsass", 5000),
                Sample(600, "chrome", 5000),
            },
            new[] { Rule(match: "*", thresholdMb: 100, action: RuleAction.Kill) });

        Assert.Equal(2, hits.Count);
        Assert.True(hits.Single(h => h.Target.Pid == 500).BlockedByGuard);
        Assert.False(hits.Single(h => h.Target.Pid == 600).BlockedByGuard);
    }

    [Fact]
    public void TrimRule_OnTrimUnsafeProcess_IsBlocked()
    {
        var hits = Evaluate(
            new[] { Sample(700, "memory compression", 5000) },
            new[] { Rule(match: "*", thresholdMb: 100, action: RuleAction.Trim) });

        RuleHit hit = Assert.Single(hits);
        Assert.Equal(RuleAction.Trim, hit.EffectiveAction);
        Assert.True(hit.BlockedByGuard);
    }

    // ---- Trim cooldown ---------------------------------------------------------------------

    [Fact]
    public void Trim_WithinCooldown_IsSuppressed()
    {
        var lastTrim = new Dictionary<int, DateTime> { [10] = Now.AddMinutes(-4) };

        var hits = Evaluate(
            new[] { Sample(10, "chrome", 5000) },
            new[] { Rule(match: "chrome", thresholdMb: 100) },
            lastTrim);

        Assert.Empty(hits);
    }

    [Fact]
    public void Trim_AfterCooldown_Fires()
    {
        var lastTrim = new Dictionary<int, DateTime> { [10] = Now.AddMinutes(-5) };

        var hits = Evaluate(
            new[] { Sample(10, "chrome", 5000) },
            new[] { Rule(match: "chrome", thresholdMb: 100) },
            lastTrim);

        Assert.Single(hits);
    }

    [Fact]
    public void Kill_HasNoCooldown_EvenWithRecentTrimHistory()
    {
        // A recent trim of this PID must not suppress a kill (different lane; killed process is gone).
        var lastTrim = new Dictionary<int, DateTime> { [10] = Now.AddSeconds(-1) };

        var hits = Evaluate(
            new[] { Sample(10, "chrome", 5000) },
            new[] { Rule(match: "chrome", thresholdMb: 100, action: RuleAction.Kill) },
            lastTrim);

        Assert.Single(hits);
    }

    // ---- Own PID ---------------------------------------------------------------------------

    [Fact]
    public void OwnProcess_IsExcludedFromMatching()
    {
        var hits = Evaluate(
            new[] { Sample(OwnPid, "cleanerer", 5000) },
            new[] { Rule(match: "*", thresholdMb: 100, action: RuleAction.Kill) });

        Assert.Empty(hits);
    }

    // ---- No enabled rules ------------------------------------------------------------------

    [Fact]
    public void NoEnabledRules_ReturnsEmpty()
    {
        var hits = Evaluate(
            new[] { Sample(10, "chrome", 5000) },
            Array.Empty<ProcessRule>());

        Assert.Empty(hits);
    }
}
