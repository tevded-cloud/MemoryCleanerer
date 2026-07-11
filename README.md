# Cleanerer

A free, GameTev-styled memory cleaner for Windows. Live memory gauge, working-set
trimming, standby-cache purge, process monitoring, and automatic rules — the
features other "RAM cleaner" tools put behind a paywall are just here, free, forever.

*(screenshot placeholder — add a shot of the Memory tab here)*

## Build

```powershell
dotnet build
```

## Test

```powershell
scripts/smoke.ps1
```

Builds the solution with warnings-as-errors and runs the test suite (161 tests).

## Publish a single exe

```powershell
dotnet publish src/Cleanerer -c Release -r win-x64 /p:PublishSingleExe=true
```

This produces a self-contained, single-file `Cleanerer.exe` under
`src/Cleanerer/bin/Release/net8.0-windows/win-x64/publish/`. The build is
unaffected unless `PublishSingleExe=true` is passed explicitly — normal Debug
and Release builds don't produce a single file.

## Requirements

- Windows 10/11, x64
- Administrator privileges (only needed for the standby-cache purge; everything
  else works unelevated)

## Honesty note

Memory cleaning is mostly placebo on modern Windows: the OS already manages
RAM better than a button click can, working sets get paged back in on demand,
and the standby cache would be evicted automatically under pressure anyway.
Cleanerer doesn't pretend otherwise — it does exactly what it says on each
button, no more, and charges nothing for any of it.
