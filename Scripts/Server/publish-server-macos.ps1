<#
.SYNOPSIS
    Builds the macOS package of the Golether rendezvous server from Windows.

.DESCRIPTION
    Runs publish-server.sh (next to this script) in Git Bash. The result is the same as on macOS itself:
    artifacts/publish/<runtime>/GoletherServer and the archive GoletherServer-<version>-<runtime>.tar.gz (and .zip
    when the zip tool is available). The executable bits are put into the archive by publish-server.sh, so the
    programs start on macOS although Windows folders keep no such bits. macOS may still refuse a program that came from
    the internet: the administrator guide (section 13) says how to allow it; install-service.sh signs it locally.

    Git for Windows (Git Bash) is needed; the .NET SDK of Windows is used, nothing has to be installed in WSL.

.PARAMETER Runtime
    osx-arm64 (default, Apple silicon) or osx-x64 (Intel Macs).

.PARAMETER Configuration
    The build configuration. Default: Release.

.EXAMPLE
    ./Scripts/Server/publish-server-macos.ps1
    ./Scripts/Server/publish-server-macos.ps1 -Runtime osx-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string] $Runtime = 'osx-arm64',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

# The bash of Git for Windows; the bash.exe of System32 is the one of WSL and has no .NET SDK.
$candidates = @(
    (Join-Path $env:ProgramFiles 'Git\bin\bash.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Git\bin\bash.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Git\bin\bash.exe')
)
$git = Get-Command 'git.exe' -ErrorAction SilentlyContinue
if ($git) {
    $candidates += (Join-Path (Split-Path -Parent (Split-Path -Parent $git.Source)) 'bin\bash.exe')
}

$bash = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $bash) {
    throw 'Git Bash is not found. Install Git for Windows (https://git-scm.com/download/win) and run the script again.'
}

$script = (Join-Path $scriptDir 'publish-server.sh') -replace '\\', '/'
& $bash $script $Runtime $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "publish-server.sh failed with exit code $LASTEXITCODE."
}
