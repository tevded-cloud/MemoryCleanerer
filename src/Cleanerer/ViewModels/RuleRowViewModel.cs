using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleanerer.Services;

namespace Cleanerer.ViewModels;

/// <summary>
/// One editable row in the Processes page "Automatic rules" card, wrapping a <see cref="ProcessRule"/>.
/// Every edit (toggle, name, threshold, action) calls back into the owning view-model so the whole
/// rule set is re-persisted immediately (the <see cref="SettingsService"/> pattern). The identity
/// (<see cref="Id"/>) is preserved so re-persisting does not churn GUIDs.
/// </summary>
public partial class RuleRowViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private readonly Action<RuleRowViewModel> _onDelete;
    private readonly bool _initialized;

    /// <summary>Stable rule identity, carried through edits.</summary>
    public Guid Id { get; }

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _matchName;

    [ObservableProperty]
    private int _thresholdMb;

    [ObservableProperty]
    private RuleAction _action;

    /// <summary>True when this row is a Kill rule aimed at a protected process name (warning glyph).</summary>
    [ObservableProperty]
    private bool _showProtectedWarning;

    /// <summary>The two selectable actions, bound to the row's action ComboBox.</summary>
    public IReadOnlyList<RuleAction> ActionOptions { get; } = new[] { RuleAction.Trim, RuleAction.Kill };

    /// <summary>Removes this rule from the set (and re-persists).</summary>
    public IRelayCommand DeleteCommand { get; }

    public RuleRowViewModel(ProcessRule rule, Action onChanged, Action<RuleRowViewModel> onDelete)
    {
        Id = rule.Id;
        _matchName = rule.MatchName;
        _thresholdMb = rule.ThresholdMb;
        _action = rule.Action;
        _enabled = rule.Enabled;
        _onChanged = onChanged;
        _onDelete = onDelete;

        DeleteCommand = new RelayCommand(() => _onDelete(this));

        UpdateWarning();
        _initialized = true;
    }

    /// <summary>Snapshots this row back into an immutable <see cref="ProcessRule"/> for persistence.</summary>
    public ProcessRule ToRule() => new()
    {
        Id = Id,
        MatchName = MatchName ?? string.Empty,
        ThresholdMb = ThresholdMb,
        Action = Action,
        Enabled = Enabled,
    };

    partial void OnEnabledChanged(bool value) => NotifyChanged();

    partial void OnMatchNameChanged(string value)
    {
        UpdateWarning();
        NotifyChanged();
    }

    partial void OnThresholdMbChanged(int value) => NotifyChanged();

    partial void OnActionChanged(RuleAction value)
    {
        UpdateWarning();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        // Ignore the property sets that happen inside the constructor before wiring is complete.
        if (_initialized)
        {
            _onChanged();
        }
    }

    private void UpdateWarning()
    {
        ShowProtectedWarning = Action == RuleAction.Kill && ProcessGuard.IsProtectedName(MatchName);
    }
}
