<#
.SYNOPSIS
    Builds the Windows installer of Golether with Inno Setup and prints the entry for the update manifest.

.DESCRIPTION
    Steps:
      1. publish-win.ps1 prepares artifacts/publish/win-x64/Golether (skip it with -SkipPublish);
      2. ISCC compiles Scripts/golether.iss into artifacts/publish/Golether-<version>-setup.exe;
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
    ./Scripts/build-installer.ps1
    ./Scripts/build-installer.ps1 -SkipPublish -UpdateUrlPrefix https://example.org/golether/
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
$root = (Resolve-Path (Join-Path $scriptDir '..')).Path
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

$iscc = (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    # Любая установленная версия Inno Setup (6, 7 и новее), включая установку в профиль пользователя.
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
Inno Setup не найден. Установите его (бесплатно, https://jrsoftware.org/isdl.php)
или добавьте ISCC.exe в PATH, затем запустите скрипт снова.
Пакет уже готов в artifacts/publish/<runtime>/Golether — им можно пользоваться и без установщика.
'@
}

# The pictures of the wizard (the logo instead of the stock box) are drawn right before compiling.
$art = Join-Path $root 'artifacts\installer-art'
& (Join-Path $scriptDir 'generate-installer-art.ps1') -OutputDirectory $art

Write-Host "==> Inno Setup: $iscc"
& $iscc `
    "/DGoletherVersion=$version" `
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
Write-Host "Установщик: $setup"
Write-Host ("Размер: {0:N1} МБ" -f ($size / 1MB))
Write-Host "SHA-256: $hash"
Write-Host ''
if (-not $UpdateUrlPrefix) {
    # Without another address the installer is expected among the assets of the GitHub release of this version.
    $UpdateUrlPrefix = "https://github.com/DepthRel/Golether/releases/download/$version/"
}

Write-Host 'Запись для Sources/Apps/Golether.Pages/updates.json (страница проекта и проверка обновлений читают этот файл):'
$entry = [ordered]@{
    windows = [ordered]@{
        version = $version
        notes   = "Что нового в $version"
        url     = ($UpdateUrlPrefix.TrimEnd('/') + '/' + (Split-Path -Leaf $setup))
        sha256  = $hash
        size    = $size
        page    = 'https://depthrel.github.io/Golether/#download'
    }
}
$entry | ConvertTo-Json -Depth 4
