#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Smoke test for Cleanerer: builds the solution with warnings-as-errors and
    runs the test suite. Exits non-zero (and prints a clear FAIL) if either step fails.
#>

$ErrorActionPreference = 'Stop'

# Ensure `dotnet` is resolvable regardless of the calling shell's current PATH.
$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path', 'User')

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host "==> dotnet build Cleanerer.sln -warnaserror" -ForegroundColor Cyan
dotnet build Cleanerer.sln -warnaserror --nologo
$buildExit = $LASTEXITCODE

if ($buildExit -ne 0) {
    Write-Host ""
    Write-Host "FAIL: build failed (exit code $buildExit)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "==> dotnet test tests/Cleanerer.Tests" -ForegroundColor Cyan
dotnet test tests/Cleanerer.Tests --nologo
$testExit = $LASTEXITCODE

if ($testExit -ne 0) {
    Write-Host ""
    Write-Host "FAIL: tests failed (exit code $testExit)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "PASS: build and tests succeeded" -ForegroundColor Green
exit 0
