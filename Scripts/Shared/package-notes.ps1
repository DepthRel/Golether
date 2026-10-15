<#
.SYNOPSIS
  Writes the note of a package in every language from the templates of a Notes folder (dot-source this file).
.DESCRIPTION
  A template is README.<code>.txt: README.en.txt becomes README.txt, the others keep their code. {version} and
  {runtime} are filled in. A line that starts with a tag ([win], [linux], [macos], or [unix] for both Linux and macOS)
  is kept only for that system, without the tag; a line without a tag is for every system. package-notes.sh does the
  same for the bash scripts.
#>

function Write-PackageNotes {
    param(
        [Parameter(Mandatory)] [string] $Notes,
        [Parameter(Mandatory)] [string] $Target,
        [Parameter(Mandatory)] [ValidateSet('win', 'linux', 'macos')] [string] $Platform,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Runtime
    )

    $tags = @{ win = @('win'); linux = @('linux', 'unix'); macos = @('macos', 'unix') }[$Platform]
    $newline = if ($Platform -eq 'win') { "`r`n" } else { "`n" }

    # Windows notes carry a byte order mark, so that every editor of the system reads them as UTF-8.
    $encoding = New-Object System.Text.UTF8Encoding ($Platform -eq 'win')

    foreach ($template in Get-ChildItem $Notes -Filter 'README.*.txt') {
        $code = $template.BaseName.Substring('README.'.Length)
        $lines = foreach ($line in [System.IO.File]::ReadAllLines($template.FullName)) {
            if ($line -match '^\[(\w+)\] (.*)$') {
                if ($tags -contains $Matches[1]) {
                    $Matches[2]
                }
            }
            else {
                $line
            }
        }

        $text = (($lines | ForEach-Object { $_.Replace('{version}', $Version).Replace('{runtime}', $Runtime) }) -join $newline) + $newline
        $name = if ($code -eq 'en') { 'README.txt' } else { "README.$code.txt" }
        [System.IO.File]::WriteAllText((Join-Path $Target $name), $text, $encoding)
    }
}

<#
.SYNOPSIS
  Puts the license of Golether (LICENSE.txt) and the notices of its components (THIRD-PARTY-NOTICES.txt) from the
  root of the repository into a package; the GPL asks for both next to the program.
#>
function Write-PackageLicenses {
    param(
        [Parameter(Mandatory)] [string] $Target
    )

    $root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    foreach ($name in 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt') {
        Copy-Item (Join-Path $root $name) (Join-Path $Target $name) -Force
    }
}

<#
.SYNOPSIS
  Writes the license page of the setup wizards in every language: LICENSE.<code>.txt is the short introduction of
  License/intro.<code>.txt followed by the English text of the GPL, the only legally binding one.
#>
function Write-InstallerLicenses {
    param(
        [Parameter(Mandatory)] [string] $Target
    )

    $root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    $license = [System.IO.File]::ReadAllText((Join-Path $root 'LICENSE.txt'))
    $rule = '-' * 78

    # Inno Setup reads a text page as UTF-8 only when it starts with a byte order mark.
    $encoding = New-Object System.Text.UTF8Encoding $true

    New-Item -ItemType Directory -Force $Target | Out-Null
    foreach ($intro in Get-ChildItem (Join-Path $PSScriptRoot 'License') -Filter 'intro.*.txt') {
        $code = $intro.BaseName.Substring('intro.'.Length)
        $text = [System.IO.File]::ReadAllText($intro.FullName).TrimEnd() + "`n`n$rule`n`n" + $license
        $text = $text.Replace("`r`n", "`n").Replace("`n", "`r`n")
        [System.IO.File]::WriteAllText((Join-Path $Target "LICENSE.$code.txt"), $text, $encoding)
    }
}
