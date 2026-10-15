<#
.SYNOPSIS
    Builds the Windows installer of Golether with Inno Setup and prints the entry for the update manifest.

.DESCRIPTION
    Steps:
      1. publish-win.ps1 prepares artifacts/publish/win-x64/Golether (skip it with -SkipPublish);
      2. ISCC compiles Scripts/App/golether.iss into artifacts/publish/Golether-<version>-setup.exe;
      3. the SHA-256 of the installer and a ready JSON entry for the update manifest are printed.

    The installer asks whom to install for. "Only for me" (the default) needs no administrator rights and goes to
    %LOCALAPPDATA%\Programs\Golether with all data in the data folder inside the installation. "For all users" goes
    to C:\Program Files\Golether, and every user keeps the data in %LOCALAPPDATA%\Golether. Either way an update never
    touches keys, settings or contacts. The installer also registers the golether:// links of invitations.

.PARAMETER Runtime
    The runtime identifier. Default: win-x64.

.PARAMETER Configuration
    The build configuration. Default: Release.

.PARAMETER SkipPublish
    Uses the package already prepared in artifacts/publish/<runtime>/Golether.

.PARAMETER UpdateUrlPrefix
    The address the installer will be published under; used for the printed manifest entry.

.EXAMPLE
    ./Scripts/App/build-installer.ps1
    ./Scripts/App/build-installer.ps1 -SkipPublish -UpdateUrlPrefix https://example.org/golether/
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [switch] $SkipPublish,
    [string] $UpdateUrlPrefix = ''
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
$output = Join-Path $root 'artifacts\publish'
$stage = Join-Path $output "$Runtime\Golether"

if (-not $SkipPublish) {
    & (Join-Path $scriptDir 'publish-win.ps1') -Runtime $Runtime -Configuration $Configuration
}

if (-not (Test-Path (Join-Path $stage 'Golether.exe'))) {
    throw "The package is not prepared: $stage. Run publish-win.ps1 first."
}

$ui = Join-Path $root 'Sources\Apps\Golether.UI\Golether.UI.csproj'
$version = (& dotnet msbuild $ui -nologo -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) {
    throw 'Cannot read the version from the project.'
}

$projectUrl = (& dotnet msbuild $ui -nologo -getProperty:PackageProjectUrl).Trim()
if ($LASTEXITCODE -ne 0 -or -not $projectUrl) {
    throw 'Cannot read the address of the project page (PackageProjectUrl) from the project.'
}

$iscc = (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    # Any installed version of Inno Setup (6, 7 and newer), including an installation into the user profile.
    $roots = @("${env:ProgramFiles(x86)}", "$env:ProgramFiles", "$env:LOCALAPPDATA\Programs") | Where-Object { $_ -and (Test-Path $_) }
    $iscc = $roots |
        ForEach-Object { Get-ChildItem -Path $_ -Filter "Inno Setup*" -Directory -ErrorAction SilentlyContinue } |
        ForEach-Object { Join-Path $_.FullName "ISCC.exe" } |
        Where-Object { Test-Path $_ } |
        Sort-Object -Descending |
        Select-Object -First 1
}

if (-not $iscc) {
    throw @'
Inno Setup was not found. Install it (free, https://jrsoftware.org/isdl.php)
or add ISCC.exe to PATH, then run the script again.
The package is ready in artifacts/publish/<runtime>/Golether: it can be used without the installer.
'@
}

# The pictures of the wizard (the logo instead of the stock box) are drawn right before compiling.
$art = Join-Path $root 'artifacts\installer-art'
& (Join-Path $scriptDir '..\Assets\generate-installer-art.ps1') -OutputDirectory $art

Write-Host "==> Inno Setup: $iscc"
& $iscc `
    "/DGoletherVersion=$version" `
    "/DGoletherUrl=$projectUrl" `
    "/DGoletherSource=$stage" `
    "/DGoletherOutputDir=$output" `
    "/DInstallerArt=$art" `
    (Join-Path $scriptDir 'golether.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$setup = Join-Path $output "Golether-$version-setup.exe"
if (-not (Test-Path $setup)) {
    throw "The installer was not created: $setup"
}

$hash = (Get-FileHash -Algorithm SHA256 $setup).Hash
$size = (Get-Item $setup).Length
Write-Host ''
Write-Host "Installer: $setup"
Write-Host ("Size: {0:N1} MB" -f ($size / 1MB))
Write-Host "SHA-256: $hash"
Write-Host ''
if (-not $UpdateUrlPrefix) {
    # Without another address the installer is expected among the assets of the GitHub release of this version.
    $UpdateUrlPrefix = "https://github.com/DepthRel/Golether/releases/download/$version/"
}

Write-Host 'The entry for Sources/Apps/Golether.Pages/updates.json (the project page and the update check read this file):'
$entry = [ordered]@{
    windows = [ordered]@{
        version = $version
        notes   = "What is new in $version"
        url     = ($UpdateUrlPrefix.TrimEnd('/') + '/' + (Split-Path -Leaf $setup))
        sha256  = $hash
        size    = $size
        page    = 'https://depthrel.github.io/Golether/#download'
    }
}
$entry | ConvertTo-Json -Depth 4
