using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleanerer.Services;

namespace Cleanerer.ViewModels;

/// <summary>
/// View-model for the "Automatic rules" panel (hosted on the Options page): owns the editable
/// rule list, persists every change through <see cref="RulesService"/>, and surfaces the
/// scheduler's rule-action reports ("Auto-trim ...", "⚠ blocked ...") as a transient status line.
/// </summary>
public partial class RulesPanelViewModel : ObservableObject
{
    private readonly RulesService _rulesService;
    private readonly SchedulerService _scheduler;
    private readonly DispatcherTimer _statusClearTimer;
    private bool _loadingRules;

    /// <summary>The editable auto-management rules.</summary>
    public ObservableCollection<RuleRowViewModel> Rules { get; } = new();

    /// <summary>Appends a new default rule (any process, &gt; 1024 MB, Trim) and persists.</summary>
    public IRelayCommand AddRuleCommand { get; }

    /// <summary>Most recent rule-action report from the scheduler, or null once cleared.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>True when <see cref="StatusMessage"/> describes a blocked/failed action.</summary>
    [ObservableProperty]
    private bool _statusIsError;

    public RulesPanelViewModel()
        : this(RulesService.Instance, SchedulerService.Instance)
    {
    }

    public RulesPanelViewModel(RulesService rulesService, SchedulerService scheduler)
    {
        _rulesService = rulesService;
        _scheduler = scheduler;

        AddRuleCommand = new RelayCommand(AddRule);

        LoadRules();
        _scheduler.RuleActionReported += OnRuleActionReported;

        _statusClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusClearTimer.Tick += (_, _) =>
        {
            StatusMessage = null;
            _statusClearTimer.Stop();
        };
    }

    /// <summary>Unhooks the scheduler event and timers; called by the view on Unload.</summary>
    public void Detach()
    {
        _statusClearTimer.Stop();
        _scheduler.RuleActionReported -= OnRuleActionReported;
    }

    private void LoadRules()
    {
        _loadingRules = true;
        try
        {
            Rules.Clear();
            foreach (ProcessRule rule in _rulesService.Current)
            {
                Rules.Add(new RuleRowViewModel(rule, PersistRules, DeleteRule));
            }
        }
        finally
        {
            _loadingRules = false;
        }
    }

    private void AddRule()
    {
        // Enabled with a wildcard match is valid, but the default is a Trim (reversible) so a
        // fresh, unconfigured rule can never kill anything before the user names a target.
        var rule = new ProcessRule { MatchName = string.Empty, ThresholdMb = 1024, Action = RuleAction.Trim, Enabled = true };
        Rules.Add(new RuleRowViewModel(rule, PersistRules, DeleteRule));
        PersistRules();
    }

    private void DeleteRule(RuleRowViewModel row)
    {
        Rules.Remove(row);
        PersistRules();
    }

    private void PersistRules()
    {
        if (_loadingRules)
        {
            return;
        }

        _rulesService.Save(Rules.Select(r => r.ToRule()).ToList());
    }

    private void OnRuleActionReported(string message)
    {
        StatusMessage = message;
        StatusIsError = message.Contains("blocked") || message.Contains("failed");

        _statusClearTimer.Stop();
        _statusClearTimer.Start();
    }
}
