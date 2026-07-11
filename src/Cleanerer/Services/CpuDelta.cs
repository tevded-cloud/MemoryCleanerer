namespace Cleanerer.Services;

/// <summary>
/// Pure CPU-percent delta math used by <see cref="ProcessMonitorService"/>. Extracted on its
/// own so the arithmetic (and its edge cases) can be unit tested without touching
/// <see cref="System.Diagnostics.Process"/> at all.
/// </summary>
public static class CpuDelta
{
    /// <summary>
    /// Computes the CPU usage percentage for a process between two samples of its
    /// cumulative <c>TotalProcessorTime</c>, normalized across all logical processors.
    /// </summary>
    /// <param name="previousCpuTime">
    /// Cumulative processor time observed at the previous sample, or <c>null</c> if this is
    /// the first time the process has been seen (no delta can be computed yet).
    /// </param>
    /// <param name="currentCpuTime">Cumulative processor time observed at this sample.</param>
    /// <param name="previousSampledAt">
    /// Wall-clock time of the previous sample, or <c>null</c> to match
    /// <paramref name="previousCpuTime"/> being <c>null</c>.
    /// </param>
    /// <param name="currentSampledAt">Wall-clock time of this sample.</param>
    /// <param name="processorCount">Number of logical processors (<see cref="Environment.ProcessorCount"/>).</param>
    /// <returns>
    /// A percentage clamped to <c>[0, 100]</c>. Returns <c>0</c> for the first sample of a
    /// process (no previous data), when no wall-clock time has elapsed, or when
    /// <paramref name="processorCount"/> is not positive.
    /// </returns>
    public static double Percent(
        TimeSpan? previousCpuTime,
        TimeSpan currentCpuTime,
        DateTime? previousSampledAt,
        DateTime currentSampledAt,
        int processorCount)
    {
        if (previousCpuTime is null || previousSampledAt is null || processorCount <= 0)
        {
            return 0;
        }

        double elapsedMs = (currentSampledAt - previousSampledAt.Value).TotalMilliseconds;
        if (elapsedMs <= 0)
        {
            return 0;
        }

        double cpuMs = (currentCpuTime - previousCpuTime.Value).TotalMilliseconds;
        double percent = cpuMs / (elapsedMs * processorCount) * 100.0;

        if (percent < 0)
        {
            return 0;
        }

        if (percent > 100)
        {
            return 100;
        }

        return percent;
    }
}
