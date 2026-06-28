# PhotoQuickSelector app icon generator
#
# Source design: src/PhotoQuickSelector.App/Assets/AppIcon.svg
#   (Concept A + gold star, star offset -8 = "an1")
# Renders the same geometry (200x200 coordinate space) with GDI+ at every size,
# producing the packaging PNG set and a multi-resolution .ico.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools/generate-app-icon.ps1
#
# NOTE: The source of truth is this script + AppIcon.svg. To change the PNG/.ico,
#       edit the geometry in both and re-run.

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$assets = Join-Path $PSScriptRoot '..\src\PhotoQuickSelector.App\Assets'
$assets = [System.IO.Path]::GetFullPath($assets)

$charcoal = [System.Drawing.Color]::FromArgb(255, 0x2A, 0x2A, 0x2A)
$blue     = [System.Drawing.Color]::FromArgb(255, 0x34, 0x78, 0xF6)
$white    = [System.Drawing.Color]::FromArgb(255, 0xFF, 0xFF, 0xFF)
$gold     = [System.Drawing.Color]::FromArgb(255, 0xFF, 0xD7, 0x00)

function Add-RoundedRect($path, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = $r * 2
    $path.AddArc($x,            $y,            $d, $d, 180, 90)
    $path.AddArc($x + $w - $d,  $y,            $d, $d, 270, 90)
    $path.AddArc($x + $w - $d,  $y + $h - $d,  $d, $d,   0, 90)
    $path.AddArc($x,            $y + $h - $d,  $d, $d,  90, 90)
    $path.CloseFigure()
}

function New-Point([float]$x, [float]$y) { New-Object System.Drawing.PointF($x, $y) }

# Draw the icon in a 200x200 space, placed at (offsetX,offsetY) with width = side.
function Draw-Icon($g, [float]$offsetX, [float]$offsetY, [float]$side) {
    $state = $g.Save()
    $g.TranslateTransform($offsetX, $offsetY)
    $s = $side / 200.0
    $g.ScaleTransform($s, $s)

    $bCharcoal = New-Object System.Drawing.SolidBrush($charcoal)
    $bBlue  = New-Object System.Drawing.SolidBrush($blue)
    $bWhite = New-Object System.Drawing.SolidBrush($white)
    $bGold  = New-Object System.Drawing.SolidBrush($gold)

    # Charcoal tile (Dark theme)
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRect $tile 0 0 200 200 44
    $g.FillPath($bCharcoal, $tile)

    # Photo frame (white)
    $frame = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRect $frame 47 53 106 83 10
    $g.FillPath($bWhite, $frame)

    # Sun
    $g.FillEllipse($bGold, 65, 68, 24, 24)

    # Mountain
    $mtn = [System.Drawing.PointF[]]@(
        (New-Point 47 127), (New-Point 87 93), (New-Point 113 117),
        (New-Point 137 97), (New-Point 153 127)
    )
    $g.FillPolygon($bBlue, $mtn)

    # Star (an1 = -8px up)
    $star = [System.Drawing.PointF[]]@(
        (New-Point 146 98),     (New-Point 154.2 120.7), (New-Point 178.3 121.5),
        (New-Point 159.3 136.3),(New-Point 166.0 159.5), (New-Point 146 146),
        (New-Point 126.0 159.5),(New-Point 132.7 136.3), (New-Point 113.7 121.5),
        (New-Point 137.8 120.7)
    )
    $g.FillPolygon($bGold, $star)

    $tile.Dispose(); $frame.Dispose()
    $bCharcoal.Dispose(); $bBlue.Dispose(); $bWhite.Dispose(); $bGold.Dispose()
    $g.Restore($state)
}

# Create a transparent W x H bitmap with the icon (width = side) centered.
function New-IconBitmap([int]$w, [int]$h, [float]$side) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Icon $g (($w - $side) / 2.0) (($h - $side) / 2.0) $side
    $g.Dispose()
    return $bmp
}

function Save-Png([int]$w, [int]$h, [float]$side, [string]$name) {
    $bmp = New-IconBitmap $w $h $side
    $path = Join-Path $assets $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $name  ($w x $h)"
}

Write-Host "PNG -> $assets"
# Square (icon fills the whole canvas)
Save-Png  50  50  50 'StoreLogo.png'
Save-Png  88  88  88 'Square44x44Logo.scale-200.png'
Save-Png  24  24  24 'Square44x44Logo.targetsize-24_altform-unplated.png'
Save-Png  48  48  48 'Square44x44Logo.targetsize-48_altform-lightunplated.png'
Save-Png 300 300 300 'Square150x150Logo.scale-200.png'
Save-Png  48  48  48 'LockScreenLogo.scale-200.png'
# Wide / splash (icon centered, transparent sides)
Save-Png  620 300 240 'Wide310x150Logo.scale-200.png'
Save-Png 1240 600 320 'SplashScreen.scale-200.png'

# ---- .ico (PNG-embedded, multi-resolution) ----
Write-Host "ICO -> AppIcon.ico"
$icoSizes = @(16, 24, 32, 48, 64, 128, 256)
$pngBlobs = @()
foreach ($sz in $icoSizes) {
    $bmp = New-IconBitmap $sz $sz $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs += ,($ms.ToArray())
    $bmp.Dispose(); $ms.Dispose()
}

$icoPath = Join-Path $assets 'AppIcon.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
# ICONDIR
$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type = icon
$bw.Write([UInt16]$icoSizes.Count)   # count
# image data offset = 6 (header) + 16 * imageCount
$offset = 6 + 16 * $icoSizes.Count
for ($i = 0; $i -lt $icoSizes.Count; $i++) {
    $sz = $icoSizes[$i]
    $blob = $pngBlobs[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }   # 256 is represented as 0
    $bw.Write([byte]$dim)            # width
    $bw.Write([byte]$dim)            # height
    $bw.Write([byte]0)               # color count
    $bw.Write([byte]0)               # reserved
    $bw.Write([UInt16]1)             # planes
    $bw.Write([UInt16]32)            # bit count
    $bw.Write([UInt32]$blob.Length)  # bytes in res
    $bw.Write([UInt32]$offset)       # image offset
    $offset += $blob.Length
}
foreach ($blob in $pngBlobs) { $bw.Write($blob) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Host "Done."
