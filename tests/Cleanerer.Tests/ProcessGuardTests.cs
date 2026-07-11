using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Exhaustive coverage of the kill whitelist (<see cref="ProcessGuard"/>) — the safety-critical
/// heart of unit 6b. Every protected name, casing/whitespace/".exe" normalization edge, the PID and
/// own-PID gates, the "unknown = protected" rule, and the trim-safety carve-outs are pinned here so a
/// future edit cannot silently make lsass/csrss killable.
/// </summary>
public class ProcessGuardTests
{
    private const int OwnPid = 4242;
    private const int NormalPid = 9999;

    // ---- Protected names (every entry in the set) ------------------------------------------

    [Theory]
    [InlineData("idle")]
    [InlineData("system")]
    [InlineData("registry")]
    [InlineData("memory compression")]
    [InlineData("secure system")]
    [InlineData("lsass")]
    [InlineData("lsaiso")]
    [InlineData("csrss")]
    [InlineData("smss")]
    [InlineData("wininit")]
    [InlineData("winlogon")]
    [InlineData("services")]
    [InlineData("svchost")]
    [InlineData("dwm")]
    [InlineData("fontdrvhost")]
    [InlineData("sihost")]
    [InlineData("ctfmon")]
    [InlineData("conhost")]
    [InlineData("audiodg")]
    [InlineData("vmmem")]
    [InlineData("wudfhost")]
    [InlineData("cleanerer")]
    [InlineData("memorycleanerer")]
    public void IsProtected_True_ForEveryProtectedName(string name)
    {
        Assert.True(ProcessGuard.IsProtected(name, NormalPid, OwnPid));
    }

    // ---- Casing / whitespace / ".exe" normalization ----------------------------------------

    [Theory]
    [InlineData("LSASS")]
    [InlineData("LsAsS")]
    [InlineData("lsass.exe")]
    [InlineData("LSASS.EXE")]
    [InlineData("  lsass  ")]
    [InlineData(" lsass.exe ")]
    [InlineData("Csrss.Exe")]
    public void IsProtected_True_AcrossCaseWhitespaceAndExe(string name)
    {
        Assert.True(ProcessGuard.IsProtected(name, NormalPid, OwnPid));
    }

    [Fact]
    public void IsProtected_DoubleExe_IsNotProtected()
    {
        // Only ONE ".exe" is stripped: "lsass.exe.exe" -> "lsass.exe", which is not "lsass". A real
        // process literally named that is not the Local Security Authority, so killing it is safe.
        Assert.False(ProcessGuard.IsProtected("lsass.exe.exe", NormalPid, OwnPid));
    }

    [Theory]
    [InlineData("lsass2")]
    [InlineData("notlsass")]
    [InlineData("svchost-helper")]
    public void IsProtected_LookalikeNames_AreNotProtected(string name)
    {
        Assert.False(ProcessGuard.IsProtected(name, NormalPid, OwnPid));
    }

    // ---- Killable everyday processes -------------------------------------------------------

    [Theory]
    [InlineData("explorer")]   // the app deliberately restarts explorer
    [InlineData("explorer.exe")]
    [InlineData("chrome")]
    [InlineData("spoolsv")]
    [InlineData("notepad")]
    public void IsProtected_False_ForKillableProcesses(string name)
    {
        Assert.False(ProcessGuard.IsProtected(name, NormalPid, OwnPid));
    }

    // ---- PID gates -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void IsProtected_True_ForLowPids_EvenWithBenignName(int pid)
    {
        Assert.True(ProcessGuard.IsProtected("chrome", pid, OwnPid));
    }

    [Fact]
    public void IsProtected_True_ForOwnPid_EvenWithBenignName()
    {
        Assert.True(ProcessGuard.IsProtected("chrome", OwnPid, OwnPid));
    }

    // ---- Unknown names ---------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsProtected_True_ForNullEmptyOrWhitespaceName(string? name)
    {
        Assert.True(ProcessGuard.IsProtected(name, NormalPid, OwnPid));
    }

    // ---- IsProtectedName (UI warning helper) -----------------------------------------------

    [Theory]
    [InlineData("lsass")]
    [InlineData("LSASS.exe")]
    [InlineData(" svchost ")]
    public void IsProtectedName_True_ForProtected(string name)
    {
        Assert.True(ProcessGuard.IsProtectedName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("chrome")]
    public void IsProtectedName_False_ForEmptyWildcardOrKillable(string? name)
    {
        Assert.False(ProcessGuard.IsProtectedName(name));
    }

    // ---- Trim safety -----------------------------------------------------------------------

    [Theory]
    [InlineData("chrome")]
    [InlineData("explorer")]
    [InlineData("lsass")]   // trimming lsass is allowed; only killing it is refused
    [InlineData("svchost")]
    public void IsTrimSafe_True_ForOrdinaryAndTrimmableSystemProcesses(string name)
    {
        Assert.True(ProcessGuard.IsTrimSafe(name, NormalPid));
    }

    [Theory]
    [InlineData("registry")]
    [InlineData("memory compression")]
    [InlineData("secure system")]
    [InlineData("lsaiso")]
    public void IsTrimSafe_False_ForTrimUnsafeNames(string name)
    {
        Assert.False(ProcessGuard.IsTrimSafe(name, NormalPid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void IsTrimSafe_False_ForLowPids(int pid)
    {
        Assert.False(ProcessGuard.IsTrimSafe("chrome", pid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsTrimSafe_False_ForUnknownName(string? name)
    {
        Assert.False(ProcessGuard.IsTrimSafe(name, NormalPid));
    }
}
