<#
.SYNOPSIS
  Draws the flags shown in the language selector (Sources/Apps/Golether.UI/Assets/Flags/<language code>.png).
.DESCRIPTION
  A language gets its flag by code: to offer a new language add Languages/<code>.json to Golether.Localization and a
  Flags/<code>.png here (a language without a flag is shown with its name only). Run this script to redraw every flag:
  en, de, fr, es, pt and ru. The monitor of the rendezvous server shows the same files.
#>
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
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

function Get-Pen([string] $hex, [float] $thickness) {
    New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml($hex)), $thickness
}

# A heraldic shield: a flat top and sides and a round bottom.
function New-Shield([float] $left, [float] $top, [float] $shieldWidth, [float] $sideHeight) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddLine($left, $top, $left + $shieldWidth, $top)
    $path.AddLine($left + $shieldWidth, $top, $left + $shieldWidth, $top + $sideHeight)
    $path.AddArc($left, $top + $sideHeight - $shieldWidth / 2, $shieldWidth, $shieldWidth, 0, 180)
    $path.CloseFigure()
    $path
}

# Germany: black, red and gold horizontal stripes.
New-Flag {
    param($g)
    $third = $height / 3
    $g.FillRectangle((Get-Brush '#000000'), 0, 0, $width, $third)
    $g.FillRectangle((Get-Brush '#DD0000'), 0, $third, $width, $third)
    $g.FillRectangle((Get-Brush '#FFCE00'), 0, 2 * $third, $width, $third)
} 'de'

# France: blue, white and red vertical stripes.
New-Flag {
    param($g)
    $third = $width / 3
    $g.FillRectangle((Get-Brush '#0055A4'), 0, 0, $third, $height)
    $g.FillRectangle((Get-Brush '#FFFFFF'), $third, 0, $third, $height)
    $g.FillRectangle((Get-Brush '#EF4135'), 2 * $third, 0, $third, $height)
} 'fr'

# Spain: red, yellow (twice as wide) and red, with the coat of arms drawn as a crowned shield near the hoist.
New-Flag {
    param($g)
    $quarter = $height / 4
    $g.FillRectangle((Get-Brush '#AA151B'), 0, 0, $width, $height)
    $g.FillRectangle((Get-Brush '#F1BF00'), 0, $quarter, $width, 2 * $quarter)
    $shield = New-Shield 30 29 16 13
    $g.FillPath((Get-Brush '#AD1519'), $shield)
    $g.FillRectangle((Get-Brush '#F1BF00'), 38, 29, 8, 7)
    $g.FillRectangle((Get-Brush '#F1BF00'), 30, 36, 8, 6)
    $g.DrawPath((Get-Pen '#7A0A0E' 1.2), $shield)
    $g.FillRectangle((Get-Brush '#C8B100'), 29, 25, 18, 3)
    $g.FillRectangle((Get-Brush '#C8B100'), 25, 30, 3, 18)
    $g.FillRectangle((Get-Brush '#C8B100'), 48, 30, 3, 18)
} 'es'

# Portugal: green at the hoist, red in the fly, and the armillary sphere with the shield on the border between them.
New-Flag {
    param($g)
    $border = $width * 2 / 5
    $middle = $height / 2
    $g.FillRectangle((Get-Brush '#006600'), 0, 0, $border, $height)
    $g.FillRectangle((Get-Brush '#FF0000'), $border, 0, $width - $border, $height)
    $radius = 17
    $g.DrawEllipse((Get-Pen '#FFE900' 3), $border - $radius, $middle - $radius, 2 * $radius, 2 * $radius)
    $g.DrawEllipse((Get-Pen '#FFE900' 2), $border - $radius, $middle - 6, 2 * $radius, 12)
    $g.FillPath((Get-Brush '#FF0000'), (New-Shield ($border - 9) ($middle - 11) 18 13))
    $g.FillPath((Get-Brush '#FFFFFF'), (New-Shield ($border - 6) ($middle - 8) 12 9))
    $blue = Get-Brush '#003399'
    foreach ($spot in @(@(0, -4), @(-3, 0), @(0, 0), @(3, 0), @(0, 4))) {
        $g.FillEllipse($blue, [float]($border + $spot[0] - 1.3), [float]($middle + $spot[1] - 1.3), [float]2.6, [float]2.6)
    }
} 'pt'
