using Cleanerer.ViewModels;

namespace Cleanerer.Views;

/// <summary>
/// The Processes page: a live, sortable/searchable process list with per-row trim and kill
/// actions. The auto-management rules engine (unit 6b) is a separate consumer of
/// <see cref="Services.ProcessMonitorService"/> and is not wired up here.
/// </summary>
public partial class ProcessesView : System.Windows.Controls.UserControl
{
    public ProcessesView()
    {
        InitializeComponent();
        var viewModel = new ProcessesViewModel();
        DataContext = viewModel;

        // Navigation swaps this view out (MainViewModel news up a fresh one each time), so stop
        // the poll timer when we leave to avoid leaking a dangling DispatcherTimer.
        Unloaded += (_, _) => viewModel.Detach();
    }
}
