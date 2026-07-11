namespace Cleanerer.Services;

/// <summary>Which color band the usage gauge should render in.</summary>
public enum GaugeLevel
{
    Low,
    Mid,
    High,
}

/// <summary>
/// Pure threshold logic for the memory usage gauge color. Kept separate from any WPF
/// brush lookup (see <c>Cleanerer.Converters.GaugeLevelToBrushConverter</c>) so it is
/// unit testable without loading resource dictionaries.
/// </summary>
public static class GaugeScale
{
    /// <summary>
    /// Classifies a memory-load percentage into a color band: below 60% is
    /// <see cref="GaugeLevel.Low"/> (green to accent), 60-85% inclusive is
    /// <see cref="GaugeLevel.Mid"/> (yellow), and above 85% is <see cref="GaugeLevel.High"/>
    /// (red).
    /// </summary>
    public static GaugeLevel Classify(int loadPercent)
    {
        if (loadPercent > 85)
        {
            return GaugeLevel.High;
        }

        if (loadPercent >= 60)
        {
            return GaugeLevel.Mid;
        }

        return GaugeLevel.Low;
    }
}
