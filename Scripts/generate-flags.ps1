<#
.SYNOPSIS
  Draws the flags shown in the language selector (Sources/Apps/Golether.UI/Assets/Flags/<language code>.png).
.DESCRIPTION
  A language gets its flag by code: to offer a new language add Languages/<code>.json to Golether.Localization and a
  Flags/<code>.png here (a language without a flag is shown with its name only). Run this script to redraw ru and en.
#>
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'Sources/Apps/Golether.UI/Assets/Flags'
New-Item -ItemType Directory -Force $out | Out-Null

$width = 120
$height = 80

function New-Flag([scriptblock] $draw, [string] $name) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    & $draw $g
    $g.Dispose()
    $bitmap.Save((Join-Path $out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

function Get-Brush([string] $hex) {
    New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex))
}

# Russia: white, blue and red horizontal stripes.
New-Flag {
    param($g)
    $third = $height / 3
    $g.FillRectangle((Get-Brush '#FFFFFF'), 0, 0, $width, $third)
    $g.FillRectangle((Get-Brush '#0039A6'), 0, $third, $width, $third)
    $g.FillRectangle((Get-Brush '#D52B1E'), 0, 2 * $third, $width, $third)
} 'ru'

# USA: thirteen stripes and a blue canton with fifty stars.
New-Flag {
    param($g)
    $stripe = $height / 13
    for ($i = 0; $i -lt 13; $i++) {
        $brush = if ($i % 2 -eq 0) { Get-Brush '#B22234' } else { Get-Brush '#FFFFFF' }
        $g.FillRectangle($brush, 0, [float]($i * $stripe), $width, [float]($stripe + 0.5))
    }

    $cantonWidth = $width * 0.4
    $cantonHeight = $stripe * 7
    $g.FillRectangle((Get-Brush '#3C3B6E'), 0, 0, $cantonWidth, $cantonHeight)
    $star = Get-Brush '#FFFFFF'
    for ($row = 0; $row -lt 9; $row++) {
        $count = if ($row % 2 -eq 0) { 6 } else { 5 }
        $shift = if ($row % 2 -eq 0) { 1 } else { 2 }
        for ($col = 0; $col -lt $count; $col++) {
            $x = $cantonWidth * (($col * 2 + $shift) / 12)
            $y = $cantonHeight * (($row + 1) / 10)
            $points = for ($k = 0; $k -lt 10; $k++) {
                $radius = if ($k % 2 -eq 0) { 3.2 } else { 1.3 }
                $angle = [Math]::PI * $k / 5 - [Math]::PI / 2
                New-Object System.Drawing.PointF ([float]($x + $radius * [Math]::Cos($angle))), ([float]($y + $radius * [Math]::Sin($angle)))
            }
            $g.FillPolygon($star, [System.Drawing.PointF[]]$points)
        }
    }
} 'en'
