using System;

namespace Cleanerer.Services;

/// <summary>What an <see cref="ProcessRule"/> does to a process that breaches its threshold.</summary>
public enum RuleAction
{
    /// <summary>Empty the working set (reversible; the OS pages memory back on demand).</summary>
    Trim,

    /// <summary>Terminate the process outright (gated by <see cref="ProcessGuard"/>).</summary>
    Kill,
}

/// <summary>
/// One user-configured auto-management rule: "when a process named <see cref="MatchName"/> uses more
/// than <see cref="ThresholdMb"/> MB of working set, <see cref="Action"/> it." Persisted as part of
/// <c>rules.json</c> by <see cref="RulesService"/>. A record so value equality comes for free (used
/// by the persistence round-trip test) and rows can be rebuilt with <c>with</c> expressions.
/// </summary>
public record ProcessRule
{
    /// <summary>Stable identity, so a rule survives edits/reordering. Defaults to a fresh GUID.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Process name to match. <c>"*"</c> or empty means "any process". Otherwise matched against the
    /// normalized process name (see <see cref="ProcessNames.Normalize"/>), so ".exe" and casing do
    /// not matter.
    /// </summary>
    public string MatchName { get; init; } = string.Empty;

    /// <summary>Working-set threshold in megabytes; the rule fires when usage is strictly greater.</summary>
    public int ThresholdMb { get; init; } = 1024;

    /// <summary>Trim or Kill. Kill is gated by the whitelist; a blocked kill is surfaced, not run.</summary>
    public RuleAction Action { get; init; } = RuleAction.Trim;

    /// <summary>Disabled rules are skipped entirely by <see cref="RuleEngine.Evaluate"/>.</summary>
    public bool Enabled { get; init; } = true;
}
