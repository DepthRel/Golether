<#
.SYNOPSIS
    Builds the Linux package of Golether from Windows, inside WSL.

.DESCRIPTION
    Runs Scripts/App/publish-linux-in-wsl.sh in a WSL distribution: the sources are copied into the Linux file system
    (so the archive keeps its executable bits), built there, and the finished archive lands in
    artifacts/publish/Golether-<version>-<runtime>.tar.gz next to the Windows packages.

    The first run installs the .NET SDK into ~/.dotnet of the distribution (official install script, no sudo) and
    takes a few minutes; later runs are faster.

    A macOS package cannot be built this way: it needs macOS itself. Use the "Packages" workflow on GitHub
    (.github/workflows/packages.yml).

.PARAMETER Runtime
    The runtime identifier: linux-x64 (default) or linux-arm64.

.PARAMETER Distribution
    The WSL distribution to build in. Default: the default distribution of WSL.

.EXAMPLE
    ./Scripts/App/publish-linux.ps1
    ./Scripts/App/publish-linux.ps1 -Runtime linux-arm64 -Distribution Ubuntu
#>
[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'linux-arm64')]
    [string] $Runtime = 'linux-x64',
    [string] $Distribution
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

if (-not (Get-Command 'wsl.exe' -ErrorAction SilentlyContinue)) {
    throw 'WSL is not found. Install it with "wsl --install" (a restart is needed) and run the script again.'
}

# A Windows path becomes /mnt/<drive>/...: the drive letter in lower case, the slashes turned around.
$windowsPath = Join-Path $scriptDir 'publish-linux-in-wsl.sh'
$linuxPath = '/mnt/' + $windowsPath.Substring(0, 1).ToLowerInvariant() + ($windowsPath.Substring(2) -replace '\\', '/')

$arguments = @()
if ($Distribution) {
    $arguments += @('-d', $Distribution)
}
$arguments += @('--', 'bash', $linuxPath, $Runtime)

& wsl.exe @arguments
if ($LASTEXITCODE -ne 0) {
    throw "The build in WSL failed with exit code $LASTEXITCODE."
}
