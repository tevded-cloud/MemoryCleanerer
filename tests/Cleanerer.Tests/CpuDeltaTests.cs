using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Covers <see cref="CpuDelta.Percent"/>, the pure CPU-percent delta math behind
/// <see cref="ProcessMonitorService.Sample"/>. No <see cref="System.Diagnostics.Process"/>
/// calls are involved, so this is exercised directly with synthetic timestamps.
/// </summary>
public class CpuDeltaTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstSample_NullPreviousCpuTime_ReturnsZero()
    {
        double percent = CpuDelta.Percent(
            previousCpuTime: null,
            currentCpuTime: TimeSpan.FromSeconds(5),
            previousSampledAt: null,
            currentSampledAt: BaseTime,
            processorCount: 4);

        Assert.Equal(0, percent);
    }

    [Fact]
    public void FirstSample_NullPreviousSampledAt_ReturnsZero()
    {
        // Defensive: previousCpuTime present but previousSampledAt missing should still be
        // treated as "no previous data" rather than throwing on the null-forgiving dereference.
        double percent = CpuDelta.Percent(
            previousCpuTime: TimeSpan.Zero,
            currentCpuTime: TimeSpan.FromSeconds(5),
            previousSampledAt: null,
            currentSampledAt: BaseTime,
            processorCount: 4);

        Assert.Equal(0, percent);
    }

    [Fact]
    public void ZeroElapsedWallClock_ReturnsZero()
    {
        // Same timestamp for both samples (e.g. a caller retrying immediately) must not divide
        // by zero.
        double percent = CpuDelta.Percent(
            previousCpuTime: TimeSpan.FromSeconds(1),
            currentCpuTime: TimeSpan.FromSeconds(2),
            previousSampledAt: BaseTime,
            currentSampledAt: BaseTime,
            processorCount: 4);

        Assert.Equal(0, percent);
    }

    [Fact]
    public void NegativeElapsedWallClock_ReturnsZero()
    {
        // Clock went "backwards" relative to the previous sample; guard rather than return a
        // nonsensical negative or huge percentage.
        double percent = CpuDelta.Percent(
            previousCpuTime: TimeSpan.FromSeconds(1),
            currentCpuTime: TimeSpan.FromSeconds(2),
            previousSampledAt: BaseTime,
            currentSampledAt: BaseTime.AddSeconds(-1),
            processorCount: 4);

        Assert.Equal(0, percent);
    }

    [Fact]
    public void ZeroOrNegativeProcessorCount_ReturnsZero()
    {
        double percent = CpuDelta.Percent(
            previousCpuTime: TimeSpan.Zero,
            currentCpuTime: TimeSpan.FromSeconds(1),
            previousSampledAt: BaseTime,
            currentSampledAt: BaseTime.AddSeconds(1),
            processorCount: 0);

        Assert.Equal(0, percent);
    }

    [Fact]
    public void SingleCore_FullSecondOfCpuOverOneSecond_Is100Percent()
    {
        double percent = CpuDelta.Percent(
            previousCpuTime: TimeSpan.Zero,
            currentCpuTime: TimeSpan.FromSeconds(1),
            previousSampledAt: BaseTime,
            currentSampledAt: BaseTime.AddSeconds(1),
            processorCount: 1);

        Assert.Equal(100, percent);
    }

    [Fact]
    public void QuadCore_OneSecondOfCpuOverOneSecond_Is25Percent()
    {
        // One full core-second of work spread across a 4-core machine in one wall-clock
        // second is 25% of total capacity.
        double percent = CpuDelta.Percent(
            previousCpuTime: TimeSpan.Zero,
            currentCpuTime: TimeSpan.FromSeconds(1),
            previousSampledAt: BaseTime,
            currentSampledAt: BaseTime.AddSeconds(1),
            processorCount: 4);

        Assert.Equal(25, percent, precision: 3);
    }

    [Fact]
    public void MultiCoreBurst_ClampsToOneHundred()
    {
        // 2 seconds of CPU time over 1 wall-clock second on a single-core box (e.g. a process
        // with multiple threads briefly oversubscribing) would compute to 200% uncapped.
        double percent = CpuDelta.Percent(
            previousCpuTime: TimeSpan.Zero,
            currentCpuTime: TimeSpan.FromSeconds(2),
            previousSampledAt: BaseTime,
            currentSampledAt: BaseTime.AddSeconds(1),
            processorCount: 1);

        Assert.Equal(100, percent);
    }

    [Fact]
    public void NoAdditionalCpuTime_ReturnsZero()
    {
        double percent = CpuDelta.Percent(
            previousCpuTime: TimeSpan.FromSeconds(3),
            currentCpuTime: TimeSpan.FromSeconds(3),
            previousSampledAt: BaseTime,
            currentSampledAt: BaseTime.AddSeconds(2),
            processorCount: 4);

        Assert.Equal(0, percent);
    }

    [Fact]
    public void PartialSecondElapsed_ComputesProportionalPercent()
    {
        // 100ms of CPU time over a 200ms wall-clock window on a single core is 50%.
        double percent = CpuDelta.Percent(
            previousCpuTime: TimeSpan.Zero,
            currentCpuTime: TimeSpan.FromMilliseconds(100),
            previousSampledAt: BaseTime,
            currentSampledAt: BaseTime.AddMilliseconds(200),
            processorCount: 1);

        Assert.Equal(50, percent, precision: 3);
    }
}
