using System.Globalization;
using System.Windows.Data;

namespace Cleanerer.Converters;

/// <summary>
/// Shows the bound element when the source string is null/empty and collapses it otherwise.
/// Used for the Processes page search box watermark ("Search processes…"), which must
/// disappear as soon as the user types anything. Pass <c>ConverterParameter=Invert</c> to flip
/// the logic (shown when non-empty instead) — used for the status line, which should only be
/// visible once there is a message.
///
/// Types are fully qualified rather than <c>using</c>'d in because <c>UseWindowsForms</c> is
/// enabled on this project (for the tray icon), which makes <c>Visibility</c> ambiguous between
/// <c>System.Windows</c> and <c>System.Windows.Forms</c>.
/// </summary>
public sealed class StringEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isEmpty = value is not string text || string.IsNullOrEmpty(text);
        bool invert = "Invert".Equals(parameter as string, StringComparison.OrdinalIgnoreCase);

        bool visible = invert ? !isEmpty : isEmpty;
        return visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
