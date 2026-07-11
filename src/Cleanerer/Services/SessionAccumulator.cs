namespace Cleanerer.Services;

/// <summary>
/// Pure running-statistics accumulator for a stream of "used bytes" samples (one per
/// <see cref="MemoryInfoService.Read"/> call). Holds no native handles and makes no
/// P/Invoke calls, so it is fully unit testable on its own.
/// </summary>
public class SessionAccumulator
{
    private long _count;
    private double _sum;

    /// <summary>Number of samples added so far.</summary>
    public long Count => _count;

    /// <summary>Smallest value added so far (0 before the first sample).</summary>
    public long MinUsedBytes { get; private set; }

    /// <summary>Largest value added so far (0 before the first sample).</summary>
    public long MaxUsedBytes { get; private set; }

    /// <summary>
    /// Running average of every value added so far (0 before the first sample). The sum is
    /// kept as a <see cref="double"/> so a long-running session accumulating many
    /// multi-gigabyte samples cannot overflow the way a running <see cref="long"/> sum could.
    /// </summary>
    public long AvgUsedBytes => _count == 0 ? 0 : (long)(_sum / _count);

    /// <summary>Folds one more sample into the running min/max/average.</summary>
    public void Add(long usedBytes)
    {
        if (_count == 0)
        {
            MinUsedBytes = usedBytes;
            MaxUsedBytes = usedBytes;
        }
        else
        {
            if (usedBytes < MinUsedBytes)
            {
                MinUsedBytes = usedBytes;
            }

            if (usedBytes > MaxUsedBytes)
            {
                MaxUsedBytes = usedBytes;
            }
        }

        _sum += usedBytes;
        _count++;
    }
}
