using Cleanerer.ViewModels;

namespace Cleanerer.Views;

/// <summary>
/// Interaction logic for RulesPanel.xaml — the reusable "Automatic rules" card, hosted on the
/// Options page. Owns its own view-model and detaches it (scheduler event + timers) on Unload.
/// </summary>
public partial class RulesPanel : System.Windows.Controls.UserControl
{
    private readonly RulesPanelViewModel _viewModel = new();

    public RulesPanel()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Unloaded += (_, _) => _viewModel.Detach();
    }
}
