<#
.SYNOPSIS
  Generates placeholder textures for the mod (Windows PowerShell 5.1, uses System.Drawing).
  Re-run after changing shapes:  powershell -ExecutionPolicy Bypass -File tools/gen_textures.ps1
#>
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot

function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Save([System.Drawing.Bitmap]$bmp, [string]$relPath) {
    $path = Join-Path $root $relPath
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "wrote $relPath"
}

# --- The cursor triangle scratched into the dirt (Awakening trance) ---
$bmp, $g = New-Canvas 128
$rng = New-Object System.Random 7
# Same proportions as the Hand of God cursor: tip top-left, flat featureless triangle.
$pts = @(
    (New-Object System.Drawing.PointF 26, 14),
    (New-Object System.Drawing.PointF 26, 110),
    (New-Object System.Drawing.PointF 92, 80)
)
# Several slightly offset passes so it looks hand-drawn with a stick.
for ($pass = 0; $pass -lt 4; $pass++) {
    $alpha = 150 - $pass * 25
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb($alpha, 52, 38, 28)), (5 - $pass)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $jittered = foreach ($p in $pts) { New-Object System.Drawing.PointF ($p.X + $rng.Next(-3, 4)), ($p.Y + $rng.Next(-3, 4)) }
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]$jittered)
    $pen.Dispose()
}
$g.Dispose()
Save $bmp "Textures/Things/AGM/SignInDirt.png"
$bmp.Dispose()
