<#
.SYNOPSIS
    Renders the Golether application icon: a minimalist TV silhouette whose screen shows a person avatar. With
    -Server, renders the icon of the rendezvous server: the same TV, smaller, with a server rack badge.

.DESCRIPTION
    The master image is drawn at 1024x1024 with the same geometry as Sources/Apps/Golether.UI/Assets/golether.svg
    and downscaled with bicubic filtering. Produces golether.ico (16-256 px, PNG-compressed frames), golether-256.png
    and golether-512.png. Windows only (System.Drawing).

    Geometry (1024 grid):
      antenna   two strokes from (512,232) to (380,96) and (644,96), width 40, round caps
      body      rounded rectangle 112,232 - 912,812, radius 110, amber #F0A860
      screen    rounded rectangle 168,288 - 856,756, radius 64, ink #0C1315
      avatar    head circle centre (512,452) radius 96; shoulders ellipse 312,588 400x360 clipped to the screen; teal #4FB3A5
      feet      strokes (300,812)-(270,900) and (724,812)-(754,900), width 40, round caps

    Server variant (Sources/Apps/Golether.Web.Server/Assets/golether-server.svg): the TV is scaled by 0.78 and moved by
    (8,48); a transparent gap 556,556 - 1020,1020 (radius 116) is cut out of it, and the badge is drawn there:
      frame     rounded rectangle 584,584 - 992,992, radius 88, amber
      inside    rounded rectangle 604,604 - 972,972, radius 72, ink
      units     three rounded rectangles 628 - 948 wide, 96 high at y 624, 740, 856, radius 22, teal
      slot      rounded rectangle 664 - 800, 16 high, 40 below the top of a unit, ink
      light     circle of radius 18 at (890, top + 48), light amber

.PARAMETER OutputDirectory
    The target directory. Default: Sources/Apps/Golether.UI/Assets, or Sources/Apps/Golether.Web.Server/Assets with
    -Server.

.PARAMETER Server
    Renders the icon of the rendezvous server (golether-server.ico, golether-server-256.png, golether-server-512.png).

.EXAMPLE
    ./Scripts/Assets/generate-icon.ps1
    ./Scripts/Assets/generate-icon.ps1 -Server
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [switch] $Server
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults, so paths are resolved here.
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
if (-not $OutputDirectory) {
    $OutputDirectory = if ($Server) { Join-Path $root 'Sources\Apps\Golether.Web.Server\Assets' } else { Join-Path $root 'Sources\Apps\Golether.UI\Assets' }
}
$name = if ($Server) { 'golether-server' } else { 'golether' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function New-Color([string] $hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function New-RoundedRectangle([float] $left, [float] $top, [float] $right, [float] $bottom, [float] $radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $radius
    $path.AddArc($left, $top, $d, $d, 180, 90)
    $path.AddArc($right - $d, $top, $d, $d, 270, 90)
    $path.AddArc($right - $d, $bottom - $d, $d, $d, 0, 90)
    $path.AddArc($left, $bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$size = 1024
$master = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)
if ($Server) {
    # The TV keeps its geometry and colours, smaller and to the upper left, leaving the corner to the badge.
    $g.TranslateTransform(8, 48)
    $g.ScaleTransform(0.78, 0.78)
}

$amber = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 112, 232), (New-Object System.Drawing.PointF 912, 812), (New-Color '#F6BE82'), (New-Color '#E08E3C')
$ink = New-Object System.Drawing.SolidBrush (New-Color '#0C1315')
$teal = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 312, 356), (New-Object System.Drawing.PointF 712, 756), (New-Color '#7FD1C4'), (New-Color '#3C9A8D')

$stroke = New-Object System.Drawing.Pen (New-Color '#EDA35A'), 40
$stroke.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$stroke.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

# Antenna and feet.
$g.DrawLine($stroke, 512, 232, 380, 96)
$g.DrawLine($stroke, 512, 232, 644, 96)
$g.DrawLine($stroke, 300, 800, 270, 900)
$g.DrawLine($stroke, 724, 800, 754, 900)

# Body and screen.
$body = New-RoundedRectangle 112 232 912 812 110
$screen = New-RoundedRectangle 168 288 856 756 64
$g.FillPath($amber, $body)
$g.FillPath($ink, $screen)

# Avatar inside the screen.
$g.SetClip($screen)
$g.FillEllipse($teal, 416, 356, 192, 192)
$g.FillEllipse($teal, 312, 588, 400, 360)
$g.ResetClip()

if ($Server) {
    $g.ResetTransform()

    # A transparent gap separates the badge from the TV at every size and on any background.
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)), (New-RoundedRectangle 556 556 1020 1020 116))
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver

    # The badge repeats the TV: an amber frame around a dark inside, with a server rack of three teal units.
    $frame = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 584, 584), (New-Object System.Drawing.PointF 992, 992), (New-Color '#F6BE82'), (New-Color '#E08E3C')
    $rack = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 628, 624), (New-Object System.Drawing.PointF 948, 952), (New-Color '#7FD1C4'), (New-Color '#3C9A8D')
    $light = New-Object System.Drawing.SolidBrush (New-Color '#F6BE82')
    $g.FillPath($frame, (New-RoundedRectangle 584 584 992 992 88))
    $g.FillPath($ink, (New-RoundedRectangle 604 604 972 972 72))
    foreach ($top in 624, 740, 856) {
        $g.FillPath($rack, (New-RoundedRectangle 628 $top 948 ($top + 96) 22))
        $g.FillPath($ink, (New-RoundedRectangle 664 ($top + 40) 800 ($top + 56) 8))
        $g.FillEllipse($light, 872, $top + 30, 36, 36)
    }
}

$g.Dispose()

function Get-Scaled([System.Drawing.Bitmap] $source, [int] $side) {
    $bitmap = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.DrawImage($source, 0, 0, $side, $side)
    $graphics.Dispose()
    return $bitmap
}

function Get-PngBytes([System.Drawing.Bitmap] $bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $stream.ToArray()
}

foreach ($side in 256, 512) {
    $scaled = Get-Scaled $master $side
    $scaled.Save((Join-Path $OutputDirectory "$name-$side.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $scaled.Dispose()
}

# ICO container with PNG frames.
$sides = 16, 24, 32, 48, 64, 128, 256
$frames = @()
foreach ($side in $sides) {
    $scaled = Get-Scaled $master $side
    $frames += , (Get-PngBytes $scaled)
    $scaled.Dispose()
}

$icoPath = Join-Path $OutputDirectory "$name.ico"
$writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($icoPath))
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sides.Count)
    $offset = 6 + 16 * $sides.Count
    for ($i = 0; $i -lt $sides.Count; $i++) {
        $dimension = if ($sides[$i] -ge 256) { 0 } else { $sides[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame)
    }
}
finally {
    $writer.Dispose()
}

$master.Dispose()
Write-Host "Icon written to $OutputDirectory"
