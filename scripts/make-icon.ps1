# Builds src\Cleanerer\Assets\app.ico (multi-size, PNG-compressed entries) and app-logo.png
# from a large square source image. Usage: .\scripts\make-icon.ps1 <source.png>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$source = $args[0]
if (-not (Test-Path $source)) { throw "source image not found: $source" }

$repo = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $repo "src\Cleanerer\Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

$src = [System.Drawing.Image]::FromFile((Resolve-Path $source))

function Resize([System.Drawing.Image]$img, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($img, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

# In-app logo bitmap (crisp at up to ~48px UI tiles, keep 256 for DPI headroom)
$logo = Resize $src 256
$logo.Save((Join-Path $assets "app-logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()

# Multi-size ICO with PNG-compressed entries (valid since Vista)
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngBlobs = foreach ($s in $sizes) {
    $bmp = Resize $src $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$icoPath = Join-Path $assets "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type: icon
$bw.Write([uint16]$sizes.Count)   # image count

# ICONDIRENTRYs
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width (0 = 256)
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $bw.Write([byte]0)            # palette
    $bw.Write([byte]0)            # reserved
    $bw.Write([uint16]1)          # planes
    $bw.Write([uint16]32)         # bpp
    $bw.Write([uint32]$pngBlobs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngBlobs[$i].Length
}

foreach ($blob in $pngBlobs) { $bw.Write($blob) }
$bw.Flush(); $bw.Close()
$src.Dispose()

Write-Output ("wrote {0} ({1} KB) and app-logo.png" -f $icoPath, [math]::Round((Get-Item $icoPath).Length / 1KB))
