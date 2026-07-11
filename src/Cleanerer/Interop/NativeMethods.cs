using System.Runtime.InteropServices;

namespace Cleanerer.Interop;

/// <summary>
/// Raw P/Invoke declarations for the Win32 / NT APIs used by the cleaner.
///
/// Struct layouts here are load-bearing: a wrong field type or ordering makes the
/// native calls fail silently (or corrupt memory), so every field mirrors the exact
/// Windows SDK definition. <c>SIZE_T</c> maps to <see cref="UIntPtr"/>, <c>DWORD</c>
/// to <see cref="uint"/>, <c>LARGE_INTEGER</c>-style 64-bit values to <see cref="ulong"/>.
///
/// Kept <c>internal</c> so the P/Invoke surface is not part of the public API.
/// </summary>
internal static class NativeMethods
{
    // ---- Process access rights (OpenProcess) ----
    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint PROCESS_SET_QUOTA = 0x0100;

    // ---- Token access rights (OpenProcessToken) ----
    internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const uint TOKEN_QUERY = 0x0008;

    // ---- Privilege attributes ----
    internal const uint SE_PRIVILEGE_ENABLED = 0x0002;

    // ---- Win32 error codes ----
    internal const int ERROR_NOT_ALL_ASSIGNED = 1300;

    // ---- NtSetSystemInformation: SYSTEM_INFORMATION_CLASS ----
    internal const int SystemMemoryListInformation = 80;

    // ---- SYSTEM_MEMORY_LIST_COMMAND values ----
    internal const int MemoryFlushModifiedList = 3;
    internal const int MemoryPurgeStandbyList = 4;
    internal const int MemoryPurgeLowPriorityStandbyList = 5;

    // ============================================================
    // Structures
    // ============================================================

    /// <summary>MEMORYSTATUSEX — physical/virtual/pagefile totals and availability.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        /// <summary>Must be set to sizeof(MEMORYSTATUSEX) before the call.</summary>
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>
    /// PERFORMANCE_INFORMATION — all SIZE_T fields are pointer-sized (UIntPtr) and are
    /// expressed in <see cref="PageSize"/> pages, not bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PERFORMANCE_INFORMATION
    {
        /// <summary>Must be set to sizeof(PERFORMANCE_INFORMATION) before the call.</summary>
        public uint cb;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    /// <summary>LUID — locally-unique identifier for a privilege.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary>
    /// TOKEN_PRIVILEGES specialised to a single privilege (PrivilegeCount == 1).
    /// The Windows type is a variable-length array; a fixed single-entry layout is
    /// the standard, safe way to enable exactly one privilege.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    // ============================================================
    // kernel32
    // ============================================================

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// SetSystemFileCacheSize. Passing <see cref="UIntPtr.MaxValue"/> (i.e. (SIZE_T)-1)
    /// for both min and max flushes the system file cache. Requires SeIncreaseQuotaPrivilege.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSystemFileCacheSize(
        UIntPtr minimumFileCacheSize,
        UIntPtr maximumFileCacheSize,
        int flags);

    // ============================================================
    // psapi
    // ============================================================

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPerformanceInfo(
        ref PERFORMANCE_INFORMATION pPerformanceInformation,
        uint cb);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr hProcess);

    // ============================================================
    // ntdll
    // ============================================================

    /// <summary>
    /// NtSetSystemInformation. For SystemMemoryListInformation the "information" buffer is a
    /// single 4-byte SYSTEM_MEMORY_LIST_COMMAND passed by ref with length 4. Returns an
    /// NTSTATUS (0 == STATUS_SUCCESS; negative values are failures).
    /// </summary>
    [DllImport("ntdll.dll")]
    internal static extern int NtSetSystemInformation(
        int systemInformationClass,
        ref int systemInformation,
        int systemInformationLength);

    // ============================================================
    // advapi32 (token / privilege APIs)
    // ============================================================

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeValue(
        [MarshalAs(UnmanagedType.LPWStr)] string? lpSystemName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpName,
        out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    // ============================================================
    // user32
    // ============================================================

    /// <summary>
    /// Destroys a GDI icon handle. Required after <c>Bitmap.GetHicon()</c> /
    /// <c>Icon.FromHandle</c>: neither of those managed APIs frees the underlying HICON, so the
    /// raw handle must be destroyed explicitly to avoid leaking GDI objects (the tray icon is
    /// rebuilt only once per process, but leaked handles are still worth avoiding).
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    // ============================================================
    // dwmapi
    // ============================================================

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE — the DwmSetWindowAttribute attribute id used to
    /// request rounded corners on a custom-chrome (WindowChrome) window on Windows 11.</summary>
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>DWMWCP_ROUND — one of the DWM_WINDOW_CORNER_PREFERENCE enum values.</summary>
    internal const int DWMWCP_ROUND = 2;

    /// <summary>
    /// DwmSetWindowAttribute. Unsupported attributes (e.g. DWMWA_WINDOW_CORNER_PREFERENCE on
    /// Windows 10, which predates it) return a failure HRESULT rather than throwing, but the
    /// call site still wraps this in try/catch in case dwmapi.dll itself is unavailable.
    /// </summary>
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
