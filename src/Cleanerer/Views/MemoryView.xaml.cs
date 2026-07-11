using Cleanerer.ViewModels;

namespace Cleanerer.Views;

/// <summary>
/// The Memory page: one-click cleanup task buttons and a results list. The usage gauge
/// and session stats are added in a later unit.
/// </summary>
public partial class MemoryView : System.Windows.Controls.UserControl
{
    public MemoryView()
    {
        InitializeComponent();
        DataContext = new MemoryViewModel();
    }
}
