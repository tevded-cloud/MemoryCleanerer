using System.Diagnostics;
using Cleanerer.Interop;

namespace Cleanerer.Services;

/// <summary>
/// Point-in-time reading of one running process, as produced by
/// <see cref="ProcessMonitorService.Sample"/>.
/// </summary>
/// <param name="Pid">Process id.</param>
/// <param name="Name">Process (image) name, without the ".exe" suffix.</param>
/// <param name="WorkingSetBytes">Current working set size, in bytes.</param>
/// <param name="PrivateBytes">Current private (committed) memory size, in bytes.</param>
/// <param name="CpuPercent">
/// CPU usage percent (0-100) since the previous <see cref="ProcessMonitorService.Sample"/>
/// call, normalized across all logical processors. Zero the first time a process is seen.
/// </param>
public record ProcessSample(int Pid, string Name, long WorkingSetBytes, long PrivateBytes, double CpuPercent);

/// <summary>
/// Snapshot provider for the live process list. <see cref="Sample"/> is the single source of
/// truth other consumers (the Processes page view-model today, the unit-6b rules engine later)
/// should read from — it is designed to be called repeatedly (e.g. every 2 seconds) and keeps
/// just enough state (previous per-PID CPU time) to compute <see cref="ProcessSample.CpuPercent"/>.
///
/// Not thread-safe: call <see cref="Sample"/> from a single caller at a time (the view-model
/// guards against overlapping ticks).
/// </summary>
public class ProcessMonitorService
{
    /// <summary>Previous cumulative CPU time and wall-clock time observed per PID.</summary>
    private readonly Dictionary<int, (TimeSpan CpuTime, DateTime At)> _previous = new();

    /// <summary>
    /// Takes a fresh snapshot of every running process. Individual processes that throw while
    /// being read (exited between enumeration and read, or access denied) are skipped rather
    /// than aborting the whole sweep; this method never throws.
    /// </summary>
    public IReadOnlyList<ProcessSample> Sample()
    {
        DateTime now = DateTime.UtcNow;
        var samples = new List<ProcessSample>();
        var seenPids = new HashSet<int>();

        foreach (Process proc in Process.GetProcesses())
        {
            try
            {
                int pid = proc.Id;
                seenPids.Add(pid);

                string name;
                long workingSet;
                long privateBytes;
                TimeSpan cpuTime;

                try
                {
                    name = proc.ProcessName;
                    workingSet = proc.WorkingSet64;
                    privateBytes = proc.PrivateMemorySize64;
                    cpuTime = proc.TotalProcessorTime;
                }
                catch
                {
                    // Exited or access denied between GetProcesses() and reading its properties.
                    continue;
                }

                (TimeSpan CpuTime, DateTime At)? previous = _previous.TryGetValue(pid, out var prev)
                    ? prev
                    : null;

                double cpuPercent = CpuDelta.Percent(
                    previous?.CpuTime,
                    cpuTime,
                    previous?.At,
                    now,
                    Environment.ProcessorCount);

                _previous[pid] = (cpuTime, now);

                samples.Add(new ProcessSample(pid, name, workingSet, privateBytes, cpuPercent));
            }
            catch
            {
                // Never let a single process abort the whole sweep.
            }
            finally
            {
                proc.Dispose();
            }
        }

        // Prune PIDs that disappeared so the dictionary does not grow unbounded and so a
        // reused PID does not inherit a stale CPU-time baseline from an unrelated process.
        List<int> stalePids = _previous.Keys.Where(pid => !seenPids.Contains(pid)).ToList();
        foreach (int pid in stalePids)
        {
            _previous.Remove(pid);
        }

        return samples;
    }

    /// <summary>
    /// Trims the working set of a single process (see <see cref="CleanerService.TrimWorkingSets"/>
    /// for the same mechanism applied to every process at once).
    /// </summary>
    /// <returns>
    /// <c>(true, message)</c> on success; <c>(false, message)</c> if the process could not be
    /// opened (access denied / already exited) or the trim call itself failed. Never throws.
    /// </returns>
    public (bool Ok, string Message) TrimProcess(int pid)
    {
        // Best effort: lets us open a few more protected processes. Not fatal if unavailable.
        Privileges.Enable(Privileges.SeDebugPrivilege);

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_SET_QUOTA,
                false,
                (uint)pid);

            if (handle == IntPtr.Zero)
            {
                return (false, "Could not open process (access denied or already exited)");
            }

            return NativeMethods.EmptyWorkingSet(handle)
                ? (true, "Working set trimmed")
                : (false, "Trim failed");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }

    /// <summary>
    /// Whitelist-checked kill for automatic/rules-driven callers. Defense in depth, in this order:
    /// <list type="number">
    ///   <item>Re-check the <paramref name="sampledName"/> the caller matched on against
    ///   <see cref="ProcessGuard.IsProtected"/> (the sample may be seconds old).</item>
    ///   <item>Re-resolve the LIVE name for <paramref name="pid"/> via
    ///   <see cref="Process.GetProcessById"/> — the process list may have changed and the PID may now
    ///   belong to a different, possibly protected, process — and check THAT name too.</item>
    ///   <item>Only then kill, using the very handle we just verified (no third lookup to race).</item>
    /// </list>
    /// </summary>
    /// <returns><c>(true, message)</c> on success; <c>(false, reason)</c> if blocked or it failed. Never throws.</returns>
    public (bool Ok, string Message) KillProcessChecked(int pid, string? sampledName)
    {
        int ownPid = Environment.ProcessId;

        if (ProcessGuard.IsProtected(sampledName, pid, ownPid))
        {
            return (false, $"Blocked kill of protected process {Describe(sampledName, pid)}");
        }

        try
        {
            using Process proc = Process.GetProcessById(pid);
            string liveName = proc.ProcessName;

            // The PID may have been reused since the sample; the live name is the authoritative one.
            if (ProcessGuard.IsProtected(liveName, pid, ownPid))
            {
                return (false, $"Blocked kill of protected process {Describe(liveName, pid)}");
            }

            proc.Kill();
            return (true, $"Killed {liveName} ({pid})");
        }
        catch (ArgumentException)
        {
            return (false, "Process already exited");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Whitelist-checked trim for automatic/rules-driven callers. Like <see cref="KillProcessChecked"/>
    /// it re-checks both the sampled name and the freshly re-resolved live name against
    /// <see cref="ProcessGuard.IsTrimSafe"/> before delegating to <see cref="TrimProcess"/>.
    /// </summary>
    /// <returns><c>(true, message)</c> on success; <c>(false, reason)</c> if blocked or it failed. Never throws.</returns>
    public (bool Ok, string Message) TrimProcessChecked(int pid, string? sampledName)
    {
        if (!ProcessGuard.IsTrimSafe(sampledName, pid))
        {
            return (false, $"Blocked trim of {Describe(sampledName, pid)}");
        }

        string liveName;
        try
        {
            using Process proc = Process.GetProcessById(pid);
            liveName = proc.ProcessName;
        }
        catch (ArgumentException)
        {
            return (false, "Process already exited");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

        if (!ProcessGuard.IsTrimSafe(liveName, pid))
        {
            return (false, $"Blocked trim of {Describe(liveName, pid)}");
        }

        return TrimProcess(pid);
    }

    private static string Describe(string? name, int pid)
    {
        return string.IsNullOrWhiteSpace(name) ? $"(PID {pid})" : $"{name} (PID {pid})";
    }
}
