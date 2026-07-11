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
    /// Kills a single process outright.
    ///
    /// TODO(unit-6b): route through the kill whitelist before this is reachable from any
    /// automatic/rules-driven caller. There is no whitelist yet — today this only fires from
    /// an explicit, user-initiated button click on the Processes page, and callers should keep
    /// it that way until unit 6b lands. Kept internal so only code in this assembly (the
    /// view-model, and later the rules engine) can call it directly.
    /// </summary>
    /// <returns><c>(true, message)</c> on success; <c>(false, message)</c> otherwise. Never throws.</returns>
    internal (bool Ok, string Message) KillProcess(int pid)
    {
        try
        {
            using Process proc = Process.GetProcessById(pid);
            string name = proc.ProcessName;
            proc.Kill();
            return (true, $"Killed {name} ({pid})");
        }
        catch (ArgumentException)
        {
            // GetProcessById throws ArgumentException when the PID is not running.
            return (false, "Process already exited");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
