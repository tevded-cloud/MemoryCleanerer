using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Covers <see cref="ByteFormat.Megabytes"/>, the pure formatter used for freed-memory
/// figures in the results list. Native cleanup paths are not exercised here — they cannot
/// run reliably in a non-elevated CI environment.
/// </summary>
public class ByteFormatTests
{
    [Fact]
    public void Zero_FormatsAsZeroMegabytes()
    {
        Assert.Equal("0 MB", ByteFormat.Megabytes(0));
    }

    [Fact]
    public void SubMegabyte_TruncatesToZero()
    {
        // 500 KB is below 1 MB and truncates down.
        Assert.Equal("0 MB", ByteFormat.Megabytes(500 * 1024));
    }

    [Fact]
    public void ExactlyOneMegabyte_FormatsAsOne()
    {
        Assert.Equal("1 MB", ByteFormat.Megabytes(1024L * 1024L));
    }

    [Fact]
    public void ThousandsSeparator_IsApplied()
    {
        // 1,204 MB — the canonical example from the results list.
        Assert.Equal("1,204 MB", ByteFormat.Megabytes(1204L * 1024L * 1024L));
    }

    [Fact]
    public void GigabyteScale_FormatsAsMegabytesWithSeparators()
    {
        // 8 GB == 8,192 MB.
        Assert.Equal("8,192 MB", ByteFormat.Megabytes(8L * 1024L * 1024L * 1024L));
    }

    [Fact]
    public void SubMegabyteRemainder_IsTruncatedNotRounded()
    {
        // 1.9 MB truncates to 1 MB (no rounding up).
        long oneAndAHalfMb = (1024L * 1024L) + (900L * 1024L);
        Assert.Equal("1 MB", ByteFormat.Megabytes(oneAndAHalfMb));
    }

    [Fact]
    public void NegativeInput_IsClampedToZero()
    {
        Assert.Equal("0 MB", ByteFormat.Megabytes(-5_000_000));
    }

    [Fact]
    public void MegabytesWithPercent_ZeroBytesAndPercent_FormatsBothAsZero()
    {
        Assert.Equal("0 MB (0%)", ByteFormat.MegabytesWithPercent(0, 0));
    }

    [Fact]
    public void MegabytesWithPercent_CanonicalExample_MatchesExpectedString()
    {
        // 27,817 MB (47%) — the canonical example from the Memory page pagefile line.
        long bytes = 27_817L * 1024L * 1024L;
        Assert.Equal("27,817 MB (47%)", ByteFormat.MegabytesWithPercent(bytes, 47.0));
    }

    [Fact]
    public void MegabytesWithPercent_NegativePercent_IsClampedToZero()
    {
        Assert.Equal("0 MB (0%)", ByteFormat.MegabytesWithPercent(0, -12.5));
    }

    [Fact]
    public void MegabytesWithPercent_RoundsToNearestWholePercent()
    {
        Assert.Equal("0 MB (47%)", ByteFormat.MegabytesWithPercent(0, 46.5));
        Assert.Equal("0 MB (46%)", ByteFormat.MegabytesWithPercent(0, 46.4));
    }

    [Fact]
    public void MegabytesWithPercent_NegativeBytes_AreClampedToZeroLikeMegabytes()
    {
        Assert.Equal("0 MB (10%)", ByteFormat.MegabytesWithPercent(-1024, 10));
    }
}
