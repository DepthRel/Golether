<#
.SYNOPSIS
    Builds the Windows installer of the Golether rendezvous server with Inno Setup.

.DESCRIPTION
    Steps:
      1. publish-server-win.ps1 prepares artifacts/publish/<runtime>/GoletherServer and the .zip (skip it with
         -SkipPublish);
      2. ISCC compiles Scripts/golether-server.iss into artifacts/publish/GoletherServer-<version>-setup.exe;
      3. the size and the SHA-256 of the installer are printed.

    The installer needs administrator rights: it installs into Program Files, registers the Windows service
    GoletherServer (automatic start, restart after a failure) and allows the program in the Windows firewall. The
    settings file appsettings.json is written only on the first installation, so updates keep it.

.PARAMETER Runtime
    The runtime identifier. Default: win-x64.

.PARAMETER Configuration
    The build configuration. Default: Release.

.PARAMETER SkipPublish
    Uses the folder already prepared in artifacts/publish/<runtime>/GoletherServer.

.EXAMPLE
    ./Scripts/build-server-installer.ps1
    ./Scripts/build-server-installer.ps1 -SkipPublish
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..')).Path
$output = Join-Path $root 'artifacts\publish'
$stage = Join-Path $output "$Runtime\GoletherServer"

if (-not $SkipPublish) {
    & (Join-Path $scriptDir 'publish-server-win.ps1') -Runtime $Runtime -Configuration $Configuration
}

if (-not (Test-Path (Join-Path $stage 'golether-server.exe'))) {
    throw "The server is not prepared: $stage. Run publish-server-win.ps1 first."
}

$server = Join-Path $root 'Sources\Apps\Golether.Web.Server\Golether.Web.Server.csproj'
$version = (& dotnet msbuild $server -nologo -getProperty:Version).Trim()
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
Папка сервера и zip уже готовы в artifacts/publish — ими можно пользоваться и без установщика.
'@
}

Write-Host "==> Inno Setup: $iscc"
& $iscc `
    "/DServerVersion=$version" `
    "/DServerSource=$stage" `
    "/DServerOutputDir=$output" `
    (Join-Path $scriptDir 'golether-server.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$setup = Join-Path $output "GoletherServer-$version-setup.exe"
if (-not (Test-Path $setup)) {
    throw "The installer was not created: $setup"
}

$hash = (Get-FileHash -Algorithm SHA256 $setup).Hash
Write-Host ''
Write-Host "Установщик: $setup"
Write-Host ("Размер: {0:N1} МБ" -f ((Get-Item $setup).Length / 1MB))
Write-Host "SHA-256: $hash"
