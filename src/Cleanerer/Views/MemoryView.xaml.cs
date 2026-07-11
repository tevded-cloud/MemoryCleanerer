using Cleanerer.ViewModels;

namespace Cleanerer.Views;

/// <summary>
/// The Memory page: live usage gauge, session stats, one-click cleanup task buttons and a
/// results list.
/// </summary>
public partial class MemoryView : System.Windows.Controls.UserControl
{
    public MemoryView()
    {
        InitializeComponent();
        var viewModel = new MemoryViewModel();
        DataContext = viewModel;

        // Navigation swaps this view out (MainViewModel news up a fresh one each time), so stop
        // the poll timer and unsubscribe from the scheduler when we leave to avoid leaks.
        Unloaded += (_, _) => viewModel.Detach();
    }
}
