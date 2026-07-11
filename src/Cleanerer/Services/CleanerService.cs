using System.Diagnostics;
using System.Runtime.InteropServices;
using Cleanerer.Interop;

namespace Cleanerer.Services;

/// <summary>
/// Outcome of a cleanup action.
/// </summary>
/// <param name="Success">Whether the action completed without a fatal error.</param>
/// <param name="Message">Human-readable summary suitable for the results list.</param>
/// <param name="BytesFreed">
/// Increase in available physical RAM measured across the action (clamped to zero;
/// never negative). Zero for actions that do not reclaim physical memory.
/// </param>
public record CleanResult(bool Success, string Message, long BytesFreed);

/// <summary>
/// Performs the low-level memory / cache cleanup operations. Methods are synchronous;
/// callers wrap the long-running ones in <c>Task.Run</c>. The clipboard method must run
/// on the UI (STA) thread and marshals itself there when needed.
/// </summary>
public class CleanerService
{
    /// <summary>Snapshot of currently-available physical memory, in bytes (0 on failure).</summary>
    private static long AvailablePhysicalBytes()
    {
        var status = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>(),
        };

        return NativeMethods.GlobalMemoryStatusEx(ref status)
            ? (long)status.ullAvailPhys
            : 0L;
    }

    /// <summary>Available-memory delta, clamped so it is never negative.</summary>
    private static long ClampedDelta(long before, long after)
    {
        long delta = after - before;
        return delta < 0 ? 0 : delta;
    }

    /// <summary>
    /// Trims Cleanerer's OWN working set: a full GC pass first (so freed managed memory is
    /// actually releasable), then <c>EmptyWorkingSet</c> on the current process. Pages the app
    /// still needs fault straight back in, so this costs a brief hiccup at most. Called after
    /// startup and whenever the window hides to the tray, keeping a "memory cleaner" from
    /// hogging memory while idle. Never throws.
    /// </summary>
    public static void TrimSelf()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess());
        }
        catch
        {
            // Best effort only.
        }
    }

    /// <summary>
    /// Trims the working set of every accessible process, forcing pages back to the
    /// standby list. Individual failures (access denied, exited process) are counted as
    /// skipped and never throw. Trimming Cleanerer's own process is fine and intentional.
    /// </summary>
    public CleanResult TrimWorkingSets()
    {
        // Best effort: lets us open a few more protected processes. Not fatal if unavailable.
        Privileges.Enable(Privileges.SeDebugPrivilege);

        long before = AvailablePhysicalBytes();

        int trimmed = 0;
        int skipped = 0;

        foreach (Process proc in Process.GetProcesses())
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_SET_QUOTA,
                    false,
                    (uint)proc.Id);

                if (handle == IntPtr.Zero)
                {
                    skipped++;
                    continue;
                }

                if (NativeMethods.EmptyWorkingSet(handle))
                {
                    trimmed++;
                }
                else
                {
                    skipped++;
                }
            }
            catch
            {
                // Never let a single process abort the whole sweep.
                skipped++;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(handle);
                }

                proc.Dispose();
            }
        }

        long freed = ClampedDelta(before, AvailablePhysicalBytes());
        return new CleanResult(true, $"Trimmed {trimmed} processes ({skipped} skipped)", freed);
    }

    /// <summary>
    /// Flushes the system file cache and purges the standby / modified page lists.
    /// Requires elevation: without SeProfileSingleProcessPrivilege (purge) and
    /// SeIncreaseQuotaPrivilege (file cache) it returns <c>Success = false</c> asking the
    /// user to run as administrator. Non-zero NTSTATUS / Win32 errors are surfaced.
    /// </summary>
    public CleanResult ClearSystemCache()
    {
        bool profile = Privileges.Enable(Privileges.SeProfileSingleProcessPrivilege);
        bool quota = Privileges.Enable(Privileges.SeIncreaseQuotaPrivilege);

        if (!profile || !quota)
        {
            return new CleanResult(
                false,
                "Could not obtain the privileges required to purge system caches. Run Cleanerer as administrator.",
                0);
        }

        long before = AvailablePhysicalBytes();
        var problems = new List<string>();

        // Flush the system file cache (needs SeIncreaseQuotaPrivilege).
        if (!NativeMethods.SetSystemFileCacheSize(UIntPtr.MaxValue, UIntPtr.MaxValue, 0))
        {
            problems.Add($"file cache flush failed (Win32 {Marshal.GetLastWin32Error()})");
        }

        // Purge the memory lists (needs SeProfileSingleProcessPrivilege).
        RunMemoryCommand(NativeMethods.MemoryFlushModifiedList, "flush modified list", problems);
        RunMemoryCommand(NativeMethods.MemoryPurgeStandbyList, "purge standby list", problems);
        RunMemoryCommand(NativeMethods.MemoryPurgeLowPriorityStandbyList, "purge low-priority standby list", problems);

        long freed = ClampedDelta(before, AvailablePhysicalBytes());

        if (problems.Count > 0)
        {
            return new CleanResult(false, "System cache partially cleared: " + string.Join("; ", problems), freed);
        }

        return new CleanResult(true, "System caches cleared", freed);
    }

    private static void RunMemoryCommand(int command, string label, List<string> problems)
    {
        int commandValue = command; // NtSetSystemInformation takes the command buffer by ref.
        int status = NativeMethods.NtSetSystemInformation(
            NativeMethods.SystemMemoryListInformation,
            ref commandValue,
            sizeof(int));

        if (status != 0)
        {
            problems.Add($"{label} failed (NTSTATUS 0x{status:X8})");
        }
    }

    /// <summary>
    /// Clears the Windows clipboard. <see cref="System.Windows.Clipboard"/> requires an STA
    /// thread, so this marshals onto the UI dispatcher when called from a background thread.
    /// A locked clipboard (<c>CLIPBRD_E_CANT_OPEN</c>, surfaced as <see cref="ExternalException"/>)
    /// is reported gracefully rather than thrown.
    /// </summary>
    public CleanResult ClearClipboard()
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(ClearClipboardCore);
        }

        return ClearClipboardCore();
    }

    private static CleanResult ClearClipboardCore()
    {
        try
        {
            System.Windows.Clipboard.Clear();
            return new CleanResult(true, "Clipboard cleared", 0);
        }
        catch (ExternalException)
        {
            // CLIPBRD_E_CANT_OPEN: another process is holding the clipboard open.
            return new CleanResult(false, "Clipboard is in use by another application", 0);
        }
    }

    /// <summary>
    /// Restarts Windows Explorer. Kills every "explorer" process (ignoring individual
    /// failures), then waits briefly.
    ///
    /// Elevation caveat: Cleanerer runs elevated, so a plain <c>Process.Start</c> would
    /// launch Explorer at high integrity — undesirable for the shell. In practice Winlogon
    /// automatically relaunches the shell at the user's normal integrity level after it is
    /// killed, so we only start Explorer ourselves as a fallback if none has reappeared.
    /// </summary>
    public CleanResult RestartExplorer()
    {
        int killed = 0;

        foreach (Process proc in Process.GetProcessesByName("explorer"))
        {
            try
            {
                proc.Kill();
                killed++;
            }
            catch
            {
                // Access denied / already exited — ignore and continue.
            }
            finally
            {
                proc.Dispose();
            }
        }

        // Give Winlogon a moment to auto-restart the shell at normal integrity.
        Thread.Sleep(1500);

        bool explorerRunning = false;
        foreach (Process proc in Process.GetProcessesByName("explorer"))
        {
            explorerRunning = true;
            proc.Dispose();
        }

        if (!explorerRunning)
        {
            try
            {
                // Fallback only: this inherits Cleanerer's elevated token (see caveat above).
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                return new CleanResult(false, $"Explorer stopped, but relaunch failed: {ex.Message}", 0);
            }
        }

        string message = killed > 0
            ? $"Explorer restarted ({killed} instance{(killed == 1 ? string.Empty : "s")})"
            : "No Explorer process was running";

        return new CleanResult(true, message, 0);
    }
}
