# Builds the real Windows installer (MSI) for the website.
# Output: dist\MemoryCleanerer-Setup-v<version>.msi  plus a .sha256 file.
$ErrorActionPreference = "Stop"
$env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')

$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo "src\Cleanerer"
$publish = Join-Path $proj "bin\Release\net8.0-windows\win-x64\publish"
$dist = Join-Path $repo "dist"
$installerDir = Join-Path $repo "installer"
$iconPath = Join-Path $proj "Assets\app.ico"

# Version comes from the csproj so the MSI name and ProductVersion never go stale.
$csproj = [xml](Get-Content (Join-Path $proj "Cleanerer.csproj"))
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = "0.1.0" }

Write-Host "==> dotnet tool restore (WiX CLI)"
Push-Location $repo
try {
    dotnet tool restore --tool-manifest (Join-Path $repo ".config\dotnet-tools.json") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed" }

    # Extensions are cached under .wix/ (repo-local, gitignored) so a fresh checkout needs this
    # too; re-adding an already-cached extension is a harmless no-op.
    dotnet tool run wix -- extension add WixToolset.UI.wixext/5.0.2 WixToolset.Util.wixext/5.0.2 | Out-Null
}
finally {
    Pop-Location
}

Write-Host "==> dotnet publish (Release, win-x64, single exe)"
dotnet publish $proj -c Release -r win-x64 /p:PublishSingleExe=true --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

New-Item -ItemType Directory -Path $dist -Force | Out-Null
$msiName = "MemoryCleanerer-Setup-v$version.msi"
$msiPath = Join-Path $dist $msiName

Write-Host "==> wix build"
Push-Location $repo
try {
    dotnet tool run wix -- build (Join-Path $installerDir "Product.wxs") `
        -arch x64 `
        -ext WixToolset.UI.wixext/5.0.2 `
        -ext WixToolset.Util.wixext/5.0.2 `
        -d "Version=$version" `
        -d "PublishDir=$publish" `
        -d "IconPath=$iconPath" `
        -bindpath $installerDir `
        -culture en-us `
        -o $msiPath
    if ($LASTEXITCODE -ne 0) { throw "wix build failed" }
}
finally {
    Pop-Location
}

$hash = (Get-FileHash $msiPath -Algorithm SHA256).Hash
"$hash  $msiName" | Out-File "$msiPath.sha256" -Encoding ascii

$sizeMb = [math]::Round((Get-Item $msiPath).Length / 1MB, 1)
Write-Host ""
Write-Host "BUILT:  $msiPath ($sizeMb MB)"
Write-Host "SHA256: $hash"
