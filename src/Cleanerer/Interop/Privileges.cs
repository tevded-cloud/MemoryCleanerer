using System.Runtime.InteropServices;

namespace Cleanerer.Interop;

/// <summary>
/// Enables Windows security privileges on the current process token.
/// </summary>
public static class Privileges
{
    /// <summary>Required to open and trim the working sets of other processes.</summary>
    public const string SeDebugPrivilege = "SeDebugPrivilege";

    /// <summary>Required to purge the standby / modified page lists via NtSetSystemInformation.</summary>
    public const string SeProfileSingleProcessPrivilege = "SeProfileSingleProcessPrivilege";

    /// <summary>Required to flush the system file cache via SetSystemFileCacheSize.</summary>
    public const string SeIncreaseQuotaPrivilege = "SeIncreaseQuotaPrivilege";

    /// <summary>
    /// Enables <paramref name="privilegeName"/> on the current process token.
    /// </summary>
    /// <returns>
    /// <c>true</c> only if the privilege was genuinely enabled. AdjustTokenPrivileges
    /// returns success even when the token does not hold the requested privilege; the
    /// authoritative signal is <c>GetLastWin32Error() != ERROR_NOT_ALL_ASSIGNED (1300)</c>,
    /// which is checked here. A non-elevated process therefore gets <c>false</c>.
    /// </returns>
    public static bool Enable(string privilegeName)
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                out IntPtr token))
        {
            return false;
        }

        try
        {
            if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out NativeMethods.LUID luid))
            {
                return false;
            }

            var tp = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeMethods.SE_PRIVILEGE_ENABLED,
            };

            bool ok = NativeMethods.AdjustTokenPrivileges(
                token,
                disableAllPrivileges: false,
                ref tp,
                (uint)Marshal.SizeOf<NativeMethods.TOKEN_PRIVILEGES>(),
                IntPtr.Zero,
                IntPtr.Zero);

            // Must read the error immediately after the SetLastError call.
            int lastError = Marshal.GetLastWin32Error();

            return ok && lastError != NativeMethods.ERROR_NOT_ALL_ASSIGNED;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }
}
