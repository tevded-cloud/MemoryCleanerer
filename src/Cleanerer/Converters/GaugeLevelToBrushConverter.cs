using System.Globalization;
using System.Windows.Data;
using Cleanerer.Services;

namespace Cleanerer.Converters;

/// <summary>
/// Maps a <see cref="GaugeLevel"/> to the matching gradient brush defined in
/// <c>Themes/GameTev.xaml</c> (<c>Brush.Gauge.Low</c> / <c>.Mid</c> / <c>.High</c>).
/// The percent-to-level threshold logic itself lives in the pure, unit-tested
/// <see cref="GaugeScale"/> class; this converter only does the resource-key lookup,
/// which needs the WPF resource system and so is exercised visually rather than in tests.
///
/// Types are fully qualified rather than <c>using</c>'d in because <c>UseWindowsForms</c>
/// is enabled on this project (for a future tray icon), which makes <c>Application</c> and
/// <c>Brush</c>/<c>Brushes</c> ambiguous between <c>System.Windows(.Forms)</c> and
/// <c>System.Windows.Media</c>.
/// </summary>
public sealed class GaugeLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value switch
        {
            GaugeLevel.Mid => "Brush.Gauge.Mid",
            GaugeLevel.High => "Brush.Gauge.High",
            _ => "Brush.Gauge.Low",
        };

        return System.Windows.Application.Current.TryFindResource(key) as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
