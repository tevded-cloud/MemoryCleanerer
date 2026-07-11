using System.Globalization;

namespace Cleanerer.Services;

/// <summary>
/// Formatting helpers for byte quantities shown in the UI. Pure and side-effect free
/// so the logic can be unit tested without any native calls.
/// </summary>
public static class ByteFormat
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    /// <summary>
    /// Formats a byte count as whole megabytes with thousands separators, e.g.
    /// <c>1,204 MB</c>. Sub-megabyte remainders are truncated and negative inputs are
    /// clamped to zero, so the smallest result is <c>0 MB</c>.
    /// </summary>
    public static string Megabytes(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        long megabytes = bytes / BytesPerMegabyte;
        // Invariant culture keeps the thousands separator a comma regardless of the
        // machine locale, which also makes the formatter deterministic to unit test.
        return megabytes.ToString("N0", CultureInfo.InvariantCulture) + " MB";
    }

    /// <summary>
    /// Formats a byte count with a trailing percent-of-total, e.g. <c>27,817 MB (47%)</c>,
    /// as used for the Memory page's pagefile/virtual/session-stat lines. The percent is
    /// rounded to the nearest whole number (away from zero) and negative percents are
    /// clamped to zero; <paramref name="bytes"/> follows the same rules as
    /// <see cref="Megabytes"/>.
    /// </summary>
    public static string MegabytesWithPercent(long bytes, double percent)
    {
        if (percent < 0)
        {
            percent = 0;
        }

        int roundedPercent = (int)Math.Round(percent, MidpointRounding.AwayFromZero);
        return $"{Megabytes(bytes)} ({roundedPercent}%)";
    }
}
