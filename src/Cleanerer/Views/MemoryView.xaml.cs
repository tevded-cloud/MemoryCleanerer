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
        DataContext = new MemoryViewModel();
    }
}
