<#
.SYNOPSIS
    Makes the picture of the project page from the screenshot of the README.

.DESCRIPTION
    Docs/images/screenshot.png is the full-size screenshot (several megabytes). The project page
    (Sources/Apps/Golether.Pages) shows a lighter copy of it: at most 1600 pixels wide, JPEG.
    Run this script after replacing the screenshot, then commit both files.

.PARAMETER Width
    The largest width of the copy in pixels. Default: 1600.

.PARAMETER Quality
    The JPEG quality, 1-100. Default: 84.

.EXAMPLE
    ./Scripts/Assets/update-site-screenshot.ps1
#>
[CmdletBinding()]
param(
    [int] $Width = 1600,
    [int] $Quality = 84
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
$source = Join-Path $root 'Docs\images\screenshot.png'
$target = Join-Path $root 'Sources\Apps\Golether.Pages\assets\screenshot.jpg'

if (-not (Test-Path $source)) {
    throw "The screenshot is not found: $source"
}

Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($source)
try {
    $newWidth = [Math]::Min($Width, $image.Width)
    $newHeight = [int][Math]::Round($image.Height * $newWidth / $image.Width)
    $bitmap = New-Object System.Drawing.Bitmap $newWidth, $newHeight
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($image, 0, 0, $newWidth, $newHeight)
        }
        finally {
            $graphics.Dispose()
        }

        $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
        $parameters = New-Object System.Drawing.Imaging.EncoderParameters 1
        $parameters.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality, [long]$Quality)
        $bitmap.Save($target, $codec, $parameters)
    }
    finally {
        $bitmap.Dispose()
    }
}
finally {
    $image.Dispose()
}

Write-Host ("{0} -> {1}" -f $source, $target)
Write-Host ("{0}x{1}, {2:N0} KB" -f $newWidth, $newHeight, ((Get-Item $target).Length / 1KB))
Write-Host 'If the proportions changed, update width and height of the picture in Sources/Apps/Golether.Pages/index.html.'
