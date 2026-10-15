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
