using System.Runtime.InteropServices;
using Cleanerer.Interop;

namespace Cleanerer.Services;

/// <summary>
/// Point-in-time reading of system memory usage.
/// </summary>
/// <param name="UsedBytes">Physical RAM in use (Total - Available), clamped to zero.</param>
/// <param name="TotalBytes">Total installed physical RAM.</param>
/// <param name="AvailableBytes">Currently available physical RAM.</param>
/// <param name="LoadPercent">Windows' own "memory load" percentage (0-100).</param>
/// <param name="PageFileUsedBytes">Committed page file bytes in use, clamped to zero.</param>
/// <param name="PageFileTotalBytes">Total page file size across all page files.</param>
/// <param name="VirtualUsedBytes">Used virtual address space for this process, clamped to zero.</param>
/// <param name="VirtualTotalBytes">Total virtual address space for this process.</param>
/// <param name="SystemCacheBytes">System (file) cache working set, in bytes.</param>
public record MemorySnapshot(
    long UsedBytes,
    long TotalBytes,
    long AvailableBytes,
    int LoadPercent,
    long PageFileUsedBytes,
    long PageFileTotalBytes,
    long VirtualUsedBytes,
    long VirtualTotalBytes,
    long SystemCacheBytes);

/// <summary>
/// Min / max / average physical memory used, and the matching percent-of-total figures,
/// accumulated across every <see cref="MemoryInfoService.Read"/> call since app start.
/// </summary>
public record SessionStats(
    long MinUsedBytes,
    long MaxUsedBytes,
    long AvgUsedBytes,
    double MinPercent,
    double MaxPercent,
    double AvgPercent);

/// <summary>
/// Reads live memory usage via <see cref="NativeMethods.GlobalMemoryStatusEx"/> and
/// <see cref="NativeMethods.GetPerformanceInfo"/>, and tracks running session statistics.
/// One instance is expected to live for the app's lifetime (created once by the view-model
/// that polls it) so <see cref="SessionStats"/> reflects the whole session.
/// </summary>
public class MemoryInfoService
{
    private readonly SessionAccumulator _accumulator = new();
    private long _lastTotalBytes;

    /// <summary>
    /// Takes a fresh snapshot of memory usage and folds the physical-used figure into the
    /// running session statistics. Never throws: a failed native call leaves the
    /// corresponding fields at zero rather than aborting the whole snapshot.
    /// </summary>
    public MemorySnapshot Read()
    {
        var status = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>(),
        };

        long totalPhys = 0;
        long availPhys = 0;
        long totalPageFile = 0;
        long availPageFile = 0;
        long totalVirtual = 0;
        long availVirtual = 0;
        int loadPercent = 0;

        if (NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            totalPhys = (long)status.ullTotalPhys;
            availPhys = (long)status.ullAvailPhys;
            totalPageFile = (long)status.ullTotalPageFile;
            availPageFile = (long)status.ullAvailPageFile;
            totalVirtual = (long)status.ullTotalVirtual;
            availVirtual = (long)status.ullAvailVirtual;
            loadPercent = (int)status.dwMemoryLoad;
        }

        long systemCacheBytes = 0;
        var perf = new NativeMethods.PERFORMANCE_INFORMATION
        {
            cb = (uint)Marshal.SizeOf<NativeMethods.PERFORMANCE_INFORMATION>(),
        };

        if (NativeMethods.GetPerformanceInfo(ref perf, perf.cb))
        {
            // SystemCache is expressed in pages; multiply by PageSize to get bytes.
            systemCacheBytes = (long)perf.SystemCache.ToUInt64() * (long)perf.PageSize.ToUInt64();
        }

        long usedBytes = ClampToZero(totalPhys - availPhys);

        _lastTotalBytes = totalPhys;
        _accumulator.Add(usedBytes);

        return new MemorySnapshot(
            UsedBytes: usedBytes,
            TotalBytes: totalPhys,
            AvailableBytes: availPhys,
            LoadPercent: loadPercent,
            PageFileUsedBytes: ClampToZero(totalPageFile - availPageFile),
            PageFileTotalBytes: totalPageFile,
            VirtualUsedBytes: ClampToZero(totalVirtual - availVirtual),
            VirtualTotalBytes: totalVirtual,
            SystemCacheBytes: systemCacheBytes);
    }

    /// <summary>
    /// Min/max/average physical memory used across every <see cref="Read"/> call so far
    /// this session, plus the matching percent-of-total figures (using the most recently
    /// observed total, which does not change at runtime).
    /// </summary>
    public SessionStats SessionStats
    {
        get
        {
            long total = _lastTotalBytes;
            return new SessionStats(
                _accumulator.MinUsedBytes,
                _accumulator.MaxUsedBytes,
                _accumulator.AvgUsedBytes,
                PercentOf(_accumulator.MinUsedBytes, total),
                PercentOf(_accumulator.MaxUsedBytes, total),
                PercentOf(_accumulator.AvgUsedBytes, total));
        }
    }

    private static long ClampToZero(long value) => value < 0 ? 0 : value;

    private static double PercentOf(long value, long total) => total <= 0 ? 0 : value * 100.0 / total;
}
