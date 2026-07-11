using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Covers <see cref="SessionAccumulator"/>, the pure running-statistics accumulator behind
/// the Memory page's Avg/Max/Min session stats. No native calls are involved.
/// </summary>
public class SessionAccumulatorTests
{
    [Fact]
    public void EmptyState_AllStatsAreZero()
    {
        var accumulator = new SessionAccumulator();

        Assert.Equal(0, accumulator.Count);
        Assert.Equal(0, accumulator.MinUsedBytes);
        Assert.Equal(0, accumulator.MaxUsedBytes);
        Assert.Equal(0, accumulator.AvgUsedBytes);
    }

    [Fact]
    public void SingleAdd_MinMaxAvgAllEqualTheSample()
    {
        var accumulator = new SessionAccumulator();
        accumulator.Add(1000);

        Assert.Equal(1, accumulator.Count);
        Assert.Equal(1000, accumulator.MinUsedBytes);
        Assert.Equal(1000, accumulator.MaxUsedBytes);
        Assert.Equal(1000, accumulator.AvgUsedBytes);
    }

    [Fact]
    public void SequenceOfAdds_TracksMinMaxAndAverage()
    {
        var accumulator = new SessionAccumulator();
        foreach (long sample in new long[] { 100, 500, 300, 900, 200 })
        {
            accumulator.Add(sample);
        }

        Assert.Equal(5, accumulator.Count);
        Assert.Equal(100, accumulator.MinUsedBytes);
        Assert.Equal(900, accumulator.MaxUsedBytes);
        // (100 + 500 + 300 + 900 + 200) / 5 = 400
        Assert.Equal(400, accumulator.AvgUsedBytes);
    }

    [Fact]
    public void DescendingThenAscending_StillFindsCorrectMinMax()
    {
        var accumulator = new SessionAccumulator();
        foreach (long sample in new long[] { 900, 700, 500, 300, 100, 300, 500, 700 })
        {
            accumulator.Add(sample);
        }

        Assert.Equal(100, accumulator.MinUsedBytes);
        Assert.Equal(900, accumulator.MaxUsedBytes);
    }

    [Fact]
    public void LargeValues_DoNotOverflow()
    {
        // 64 GB, repeated many times: a naive long sum would still fit here, but this
        // guards the double-sum-based average against regressions toward a narrower type.
        const long sixtyFourGigabytes = 64L * 1024L * 1024L * 1024L;
        var accumulator = new SessionAccumulator();

        for (int i = 0; i < 100_000; i++)
        {
            accumulator.Add(sixtyFourGigabytes);
        }

        Assert.Equal(100_000, accumulator.Count);
        Assert.Equal(sixtyFourGigabytes, accumulator.MinUsedBytes);
        Assert.Equal(sixtyFourGigabytes, accumulator.MaxUsedBytes);
        Assert.Equal(sixtyFourGigabytes, accumulator.AvgUsedBytes);
    }

    [Fact]
    public void ZeroSample_IsHandledLikeAnyOtherValue()
    {
        var accumulator = new SessionAccumulator();
        accumulator.Add(0);
        accumulator.Add(1000);

        Assert.Equal(0, accumulator.MinUsedBytes);
        Assert.Equal(1000, accumulator.MaxUsedBytes);
        Assert.Equal(500, accumulator.AvgUsedBytes);
    }
}
