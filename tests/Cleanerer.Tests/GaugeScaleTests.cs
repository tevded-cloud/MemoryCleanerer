using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Covers <see cref="GaugeScale.Classify"/>, the pure percent-to-color-band threshold logic
/// behind the Memory page's usage gauge.
/// </summary>
public class GaugeScaleTests
{
    [Theory]
    [InlineData(0, GaugeLevel.Low)]
    [InlineData(59, GaugeLevel.Low)]
    [InlineData(60, GaugeLevel.Mid)]
    [InlineData(84, GaugeLevel.Mid)]
    [InlineData(85, GaugeLevel.Mid)]
    [InlineData(100, GaugeLevel.High)]
    public void Classify_ReturnsExpectedBand(int loadPercent, GaugeLevel expected)
    {
        Assert.Equal(expected, GaugeScale.Classify(loadPercent));
    }

    [Fact]
    public void JustAboveHighThreshold_IsHigh()
    {
        Assert.Equal(GaugeLevel.High, GaugeScale.Classify(86));
    }

    [Fact]
    public void JustBelowMidThreshold_IsLow()
    {
        Assert.Equal(GaugeLevel.Low, GaugeScale.Classify(59));
    }
}
