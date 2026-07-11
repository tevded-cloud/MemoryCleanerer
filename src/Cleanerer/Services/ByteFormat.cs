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
}
