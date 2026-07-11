# Builds the distributable zip for the website.
# Output: dist\Cleanerer-v<version>-win-x64.zip  (exe + README) plus a .sha256 file.
$ErrorActionPreference = "Stop"
$env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')

$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo "src\Cleanerer"
$publish = Join-Path $proj "bin\Release\net8.0-windows\win-x64\publish"
$dist = Join-Path $repo "dist"

# Version comes from the csproj so the zip name never goes stale.
$csproj = [xml](Get-Content (Join-Path $proj "Cleanerer.csproj"))
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = "0.1.0" }

Write-Host "==> dotnet publish (Release, win-x64, single exe)"
dotnet publish $proj -c Release -r win-x64 /p:PublishSingleExe=true --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$stage = Join-Path $env:TEMP "cleanerer-package"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Copy-Item (Join-Path $publish "Cleanerer.exe") $stage

# Short user-facing readme inside the zip (not the repo README).
@"
Cleanerer v$version
===================

A free Windows memory utility by TevDed.
Live memory gauge - working-set trimming - standby-cache purge -
process monitoring and automatic rules. All features free, forever.

Requirements: Windows 10/11, 64-bit. No install needed - just run Cleanerer.exe.
It asks for administrator rights because purging the standby cache requires them.

100% private to your system: no network connections, no telemetry, nothing collected.
"@ | Out-File (Join-Path $stage "README.txt") -Encoding utf8

New-Item -ItemType Directory -Path $dist -Force | Out-Null
$zipName = "Cleanerer-v$version-win-x64.zip"
$zipPath = Join-Path $dist $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
"$hash  $zipName" | Out-File "$zipPath.sha256" -Encoding ascii

Remove-Item $stage -Recurse -Force

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "PACKAGED: $zipPath ($sizeMb MB)"
Write-Host "SHA256:   $hash"
