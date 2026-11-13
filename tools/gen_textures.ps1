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

# Light fills so the build material (stuff) tints them; dark outlines for readability.
$fill = [System.Drawing.Color]::FromArgb(255, 235, 230, 220)
$shade = [System.Drawing.Color]::FromArgb(255, 190, 182, 170)
$outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 60, 50, 40)), 4
$outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$fillBrush = New-Object System.Drawing.SolidBrush $fill
$shadeBrush = New-Object System.Drawing.SolidBrush $shade
function PF($x, $y) { New-Object System.Drawing.PointF $x, $y }

# --- Idol: the god's flat featureless triangle on a plinth ---
$bmp, $g = New-Canvas 128
$g.FillRectangle($shadeBrush, 30, 92, 68, 26); $g.DrawRectangle($outline, 30, 92, 68, 26)
$tri = [System.Drawing.PointF[]]@((PF 44 14), (PF 44 92), (PF 92 66))
$g.FillPolygon($fillBrush, $tri); $g.DrawPolygon($outline, $tri)
$g.Dispose(); Save $bmp "Textures/Things/AGM/Idol.png"; $bmp.Dispose()

# --- Incense burner: a bowl on three legs, with smoke ---
$bmp, $g = New-Canvas 128
foreach ($x in 40, 64, 88) { $g.DrawLine($outline, 64, 78, $x, 116) }
$g.FillEllipse($fillBrush, 30, 58, 68, 30); $g.DrawEllipse($outline, 30, 58, 68, 30)
$smoke = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 120, 120, 120)), 4
$g.DrawBezier($smoke, (PF 56 58), (PF 46 40), (PF 70 30), (PF 58 10))
$g.DrawBezier($smoke, (PF 72 58), (PF 82 44), (PF 62 34), (PF 76 16))
$g.Dispose(); Save $bmp "Textures/Things/AGM/IncenseBurner.png"; $bmp.Dispose()

# --- Offering bowl: a wide shallow bowl on a short stand ---
$bmp, $g = New-Canvas 128
$g.FillRectangle($shadeBrush, 52, 88, 24, 22); $g.DrawRectangle($outline, 52, 88, 24, 22)
$g.FillPie($fillBrush, 16, 40, 96, 64, 0, 180); $g.DrawArc($outline, 16, 40, 96, 64, 0, 180)
$g.FillEllipse($shadeBrush, 16, 58, 96, 28); $g.DrawEllipse($outline, 16, 58, 96, 28)
$g.Dispose(); Save $bmp "Textures/Things/AGM/OfferingBowl.png"; $bmp.Dispose()

# --- Pylon: a tall obelisk marked with the god's triangle (128x256) ---
$bmp = New-Object System.Drawing.Bitmap 128, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias; $g.Clear([System.Drawing.Color]::Transparent)
$obelisk = [System.Drawing.PointF[]]@((PF 64 8), (PF 88 40), (PF 84 236), (PF 44 236), (PF 40 40))
$g.FillPolygon($fillBrush, $obelisk); $g.DrawPolygon($outline, $obelisk)
$mark = [System.Drawing.PointF[]]@((PF 54 80), (PF 54 136), (PF 82 118))
$markPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 200, 150, 50)), 5
$g.DrawPolygon($markPen, $mark)
$g.FillRectangle($shadeBrush, 30, 230, 68, 22); $g.DrawRectangle($outline, 30, 230, 68, 22)
$g.Dispose(); Save $bmp "Textures/Things/AGM/Pylon.png"; $bmp.Dispose()
