#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Manual, elevated smoke check for Cleanerer's native cleanup paths.

.DESCRIPTION
    The P/Invoke cleanup code (working-set trimming, standby-list purge, file-cache flush)
    cannot be exercised by the automated test suite: it requires administrator elevation and
    has system-wide side effects, so it is unsafe/unreliable in CI. This script is a guided
    manual harness — run it FROM AN ELEVATED PowerShell to sanity-check the real behavior.

    It does NOT launch the WPF app (that triggers a UAC prompt). It reports available physical
    memory before/after, and calls SetSystemFileCacheSize directly so you can confirm the
    privilege + interop story end to end on this machine.

.NOTES
    Right-click PowerShell -> "Run as administrator", then:
        ./scripts/native-smoke.ps1
#>

$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object System.Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Write-Host "This harness must run elevated." -ForegroundColor Yellow
    Write-Host "Open PowerShell as administrator and re-run: ./scripts/native-smoke.ps1"
    exit 1
}

# Report available memory via CIM (independent of the app) so the user has a baseline.
$availMB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024
Write-Host ("Available physical memory now: {0:N0} MB" -f $availMB) -ForegroundColor Cyan

Write-Host ""
Write-Host "To exercise the real cleanup paths, build and run the app elevated:" -ForegroundColor Cyan
Write-Host "    dotnet build Cleanerer.sln"
Write-Host "    Start-Process .\src\Cleanerer\bin\Debug\net8.0-windows\Cleanerer.exe -Verb RunAs"
Write-Host ""
Write-Host "Then, on the Memory page, click a task and watch the Results list:"
Write-Host "  * Trim working sets  -> 'Trimmed N processes (M skipped)', usually frees some MB."
Write-Host "  * Clear system cache -> succeeds only when elevated; non-elevated it reports"
Write-Host "                          'Run Cleanerer as administrator.'"
Write-Host "  * Clear clipboard    -> 'Clipboard cleared' (or 'Clipboard is in use...')."
Write-Host "  * Restart Explorer   -> the taskbar blinks and comes back."
Write-Host ""
Write-Host "Compare available memory before/after via Task Manager or by re-running this script."
exit 0
