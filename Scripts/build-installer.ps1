<#
.SYNOPSIS
    Builds the Windows installer of Golether with Inno Setup and prints the entry for the update manifest.

.DESCRIPTION
    Steps:
      1. publish-win.ps1 prepares artifacts/publish/win-x64/Golether (skip it with -SkipPublish);
      2. ISCC compiles Scripts/golether.iss into artifacts/publish/Golether-<version>-setup.exe;
      3. the SHA-256 of the installer and a ready JSON entry for the update manifest are printed.

    The installation is per-user (no administrator rights) into %LOCALAPPDATA%\Programs\Golether. All data stays in
    the data folder inside the installation, so an update never touches keys, settings or contacts.

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
    [string] $UpdateUrlPrefix = 'https://example.org/golether/'
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

Write-Host "==> Inno Setup: $iscc"
& $iscc `
    "/DGoletherVersion=$version" `
    "/DGoletherSource=$stage" `
    "/DGoletherOutputDir=$output" `
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
Write-Host 'Запись для файла обновлений (адрес источника задаётся в приложении на стартовом экране):'
$entry = [ordered]@{
    windows = [ordered]@{
        version = $version
        notes   = "Что нового в $version"
        url     = ($UpdateUrlPrefix.TrimEnd('/') + '/' + (Split-Path -Leaf $setup))
        sha256  = $hash
        size    = $size
    }
}
$entry | ConvertTo-Json -Depth 4
