using Cleanerer.ViewModels;

namespace Cleanerer.Views;

/// <summary>
/// Options page: startup and automatic-cleanup settings. Owns its own
/// <see cref="OptionsViewModel"/>, which reads and writes the shared settings service.
/// </summary>
public partial class OptionsView : System.Windows.Controls.UserControl
{
    public OptionsView()
    {
        InitializeComponent();
        DataContext = new OptionsViewModel();
    }
}
