<#
.SYNOPSIS
    Draws the pictures of the Windows installers: the side panel of the welcome page and the small picture in the
    header of the other pages, for Golether and for the rendezvous server.

.DESCRIPTION
    The installer wizard is light, the Golether logo is made for a dark ground. So the side panel is a piece of the
    application itself: the ink gradient of its theme, the amber glow of the splash screen, the logo and the word
    mark on it. The header of the other pages is white; there the logo stands alone, and its amber frame carries it.

    Files (BMP, which every version of Inno Setup reads), drawn at 2.5 times the size the wizard shows at 100%:
      golether-wizard.bmp         410 x 785   side panel, application
      golether-wizard-small.bmp   138 x 145   header picture, application
      server-wizard.bmp           410 x 785   side panel, rendezvous server
      server-wizard-small.bmp     138 x 145   header picture, rendezvous server

    build-installer.ps1 and build-server-installer.ps1 call this script before compiling. Windows only (System.Drawing).

.PARAMETER OutputDirectory
    The target directory. Default: artifacts/installer-art.

.EXAMPLE
    ./Scripts/Assets/generate-installer-art.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'artifacts\installer-art'
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

Add-Type -AssemblyName System.Drawing

# The palette of Sources/Apps/Golether.UI/Styles/Theme.axaml.
$ink = [System.Drawing.ColorTranslator]::FromHtml('#0A0F11')
$panel = [System.Drawing.ColorTranslator]::FromHtml('#162226')
$text = [System.Drawing.ColorTranslator]::FromHtml('#DDE7E5')
$muted = [System.Drawing.ColorTranslator]::FromHtml('#7F9491')
$accent = [System.Drawing.ColorTranslator]::FromHtml('#F0A860')
$secure = [System.Drawing.ColorTranslator]::FromHtml('#4FB3A5')

function New-Canvas([int] $width, [int] $height) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return $bitmap, $graphics
}

# Draws a text centred at x; returns nothing. Two-colour words are drawn as two pieces measured together.
function Write-Centered($graphics, [string[]] $parts, [System.Drawing.Color[]] $colors, $font, [single] $centerX, [single] $top) {
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $widths = @($parts | ForEach-Object { $graphics.MeasureString($_, $font, 10000, $format).Width })
    $x = $centerX - (($widths | Measure-Object -Sum).Sum / 2)
    for ($i = 0; $i -lt $parts.Length; $i++) {
        $brush = New-Object System.Drawing.SolidBrush $colors[$i]
        $graphics.DrawString($parts[$i], $font, $brush, $x, $top, $format)
        $brush.Dispose()
        $x += $widths[$i]
    }
}

function Save-SidePanel([string] $logoPath, [string] $label, [System.Drawing.Color] $labelColor, [string] $target) {
    $width = 410; $height = 785
    $bitmap, $graphics = New-Canvas $width $height
    try {
        # The ground of the application: ink turning into the panel colour towards the lower right corner.
        $bounds = New-Object System.Drawing.Rectangle 0, 0, $width, $height
        $ground = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bounds, $ink, $panel, 55.0
        $graphics.FillRectangle($ground, $bounds)
        $ground.Dispose()

        # The glow of the projector lamp behind the logo.
        $logoSize = 250
        $logoLeft = ($width - $logoSize) / 2
        $logoTop = 190
        $glowSize = 620
        $glowBounds = New-Object System.Drawing.RectangleF (($width - $glowSize) / 2), ($logoTop + ($logoSize - $glowSize) / 2), $glowSize, $glowSize
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddEllipse($glowBounds)
        $glow = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
        $glow.CenterColor = [System.Drawing.Color]::FromArgb(64, $accent)
        $glow.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $accent))
        $graphics.FillEllipse($glow, $glowBounds)
        $glow.Dispose(); $path.Dispose()

        $logo = [System.Drawing.Image]::FromFile($logoPath)
        try {
            $graphics.DrawImage($logo, [single]$logoLeft, [single]$logoTop, [single]$logoSize, [single]$logoSize)
        }
        finally {
            $logo.Dispose()
        }

        # The word mark of the title bar: "gole" in the text colour, "ther" in amber.
        $wordFont = New-Object System.Drawing.Font 'Segoe UI', 46, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        Write-Centered $graphics @('gole', 'ther') @($text, $accent) $wordFont ($width / 2) ($logoTop + $logoSize + 26)
        $wordFont.Dispose()

        $labelFont = New-Object System.Drawing.Font 'Consolas', 17, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        Write-Centered $graphics @($label) @($labelColor) $labelFont ($width / 2) ($logoTop + $logoSize + 96)
        $labelFont.Dispose()

        # A thin amber line where the panel meets the light page.
        $edge = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(160, $accent))
        $graphics.FillRectangle($edge, $width - 3, 0, 3, $height)
        $edge.Dispose()

        $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $graphics.Dispose(); $bitmap.Dispose()
    }
}

function Save-HeaderPicture([string] $logoPath, [string] $target) {
    $width = 138; $height = 145
    $bitmap, $graphics = New-Canvas $width $height
    try {
        $graphics.Clear([System.Drawing.Color]::White)
        $logo = [System.Drawing.Image]::FromFile($logoPath)
        try {
            $size = 126
            $graphics.DrawImage($logo, [single](($width - $size) / 2), [single](($height - $size) / 2), [single]$size, [single]$size)
        }
        finally {
            $logo.Dispose()
        }

        $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $graphics.Dispose(); $bitmap.Dispose()
    }
}

$appLogo = Join-Path $root 'Sources\Apps\Golether.UI\Assets\golether-512.png'
$serverLogo = Join-Path $root 'Sources\Apps\Golether.Web.Server\Assets\golether-server-512.png'
foreach ($file in $appLogo, $serverLogo) {
    if (-not (Test-Path $file)) {
        throw "The logo is not found: $file. Run Scripts/Assets/generate-icon.ps1 first."
    }
}

Save-SidePanel $appLogo "LET'S GO TOGETHER" $muted (Join-Path $OutputDirectory 'golether-wizard.bmp')
Save-HeaderPicture $appLogo (Join-Path $OutputDirectory 'golether-wizard-small.bmp')
Save-SidePanel $serverLogo 'RENDEZVOUS SERVER' $secure (Join-Path $OutputDirectory 'server-wizard.bmp')
Save-HeaderPicture $serverLogo (Join-Path $OutputDirectory 'server-wizard-small.bmp')

Write-Host "Installer pictures: $OutputDirectory"
