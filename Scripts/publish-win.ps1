<#
.SYNOPSIS
    Publishes Golether as a self-contained Release build for Windows and packs it into a .zip archive.

.DESCRIPTION
    Layout of the archive:
      Golether/
        Golether.exe          the launcher with the Golether icon (the .NET host pointing at app\Golether.dll)
        app/                  the application with native libraries in app/native
        tools/dbmigrator/     the console database migrator
        golether.install      marks the installation root: all data (keys, database, components) is kept in Golether/data
        README.txt            first start notes

    The video (libmpv) and camera (GStreamer) components are downloaded, verified and included automatically.

.PARAMETER Runtime
    The runtime identifier. Default: win-x64.

.PARAMETER Configuration
    The build configuration. Default: Release.

.PARAMETER Output
    The output root. Default: <repository>/artifacts/publish.

.PARAMETER SkipComponents
    Does not download the video (libmpv) and camera (GStreamer) components into the package. The application then
    offers to install them on first start.

.EXAMPLE
    ./Scripts/publish-win.ps1
    ./Scripts/publish-win.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [string] $Output,
    [switch] $SkipComponents
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults, so paths are resolved here.
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..')).Path
if (-not $Output) {
    $Output = Join-Path $root 'artifacts\publish'
}

if ($Runtime -notmatch '^win-(x64|x86|arm64)$') {
    throw "Unsupported runtime '$Runtime'. Use win-x64, win-x86 or win-arm64."
}

$ui = Join-Path $root 'Sources\Apps\Golether.UI\Golether.UI.csproj'
$migrator = Join-Path $root 'Sources\Database\Golether.Dbs.SQLite.DbMigrator\Golether.Dbs.SQLite.DbMigrator.csproj'
$version = (& dotnet msbuild $ui -nologo -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) {
    throw 'Cannot read the version from the project.'
}

$stage = Join-Path $Output "$Runtime\Golether"
if (Test-Path $stage) {
    $stageFull = (Resolve-Path $stage).Path.TrimEnd('\') + '\'
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($stageFull, [StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count -gt 0) {
        throw "Golether is running from $stage (process $($running[0].Id)). Close it and publish again, or use -Output."
    }

    # The data folder holds the keys and the database of a tried package: it is kept, everything else is rebuilt.
    Get-ChildItem -Force $stage | Where-Object { $_.Name -ne 'data' } | Remove-Item -Recurse -Force
}

function Publish-Project([string] $project, [string] $target, [string[]] $extra = @()) {
    Write-Host "==> $(Split-Path -Leaf $project) ($Runtime, $Configuration) -> $target"
    & dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $target -nologo `
        -p:DebugType=none -p:PublishReadyToRun=false @extra
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing '$project' failed with exit code $LASTEXITCODE."
    }
}

if (-not $SkipComponents) {
    # Downloads the pinned packages (verified by SHA-256) into Native\<runtime>; the UI project copies them to app\native.
    $native = Join-Path $root "Native\$Runtime"
    $cache = Join-Path $root 'artifacts\cache\components'
    Write-Host "==> native components ($Runtime) -> $native"
    & dotnet run --project (Join-Path $root 'Sources\Tools\Golether.Tools.Components\Golether.Tools.Components.csproj') -c $Configuration -- fetch --rid $Runtime --output $native --cache $cache
    if ($LASTEXITCODE -ne 0) {
        throw "Preparing native components failed with exit code $LASTEXITCODE."
    }
}

Publish-Project $ui (Join-Path $stage 'app') @("-p:GoletherRootLauncherPath=$(Join-Path $stage 'Golether.exe')")
Publish-Project $migrator (Join-Path $stage 'tools\dbmigrator')

if (-not (Test-Path (Join-Path $stage 'Golether.exe'))) {
    throw 'The root launcher Golether.exe was not created.'
}
Set-Content -Path (Join-Path $stage 'golether.install') -Encoding ascii `
    -Value 'Golether installation root: all data (keys, database, tunnels, components) is stored in the data folder here.'

$hasMpv = Test-Path (Join-Path $stage 'app\native\libmpv-2.dll')
$readme = @(
    "Golether $version ($Runtime)",
    '',
    'Запуск: Golether.exe в этой папке (на него можно сделать ярлык или закрепить на панели задач).',
    'Все данные (ключи, база, туннели, компоненты) хранятся в папке data рядом с приложением. Переносите и удаляйте папку Golether целиком.',
    $(if ($hasMpv) { 'libmpv включена в пакет.' } else { 'libmpv не включена: положите libmpv-2.dll в app\native, иначе видео не будет показано (синхронизация работает).' }),
    'Туннели AmneziaWG требуют установленного AmneziaWG для Windows; при подъёме туннеля Windows спросит разрешение администратора.',
    'Windows покажет предупреждение SmartScreen, пока сборка не подписана.'
)
[System.IO.File]::WriteAllLines((Join-Path $stage 'README.txt'), $readme, (New-Object System.Text.UTF8Encoding $true))

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$package = Join-Path (Resolve-Path $Output).Path "Golether-$version-$Runtime.zip"
if (Test-Path $package) {
    [System.IO.File]::Delete($package)
}

$zip = [System.IO.Compression.ZipFile]::Open($package, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $stageRoot = (Resolve-Path $stage).Path
    foreach ($file in Get-ChildItem $stageRoot -Recurse -File) {
        $relative = $file.FullName.Substring($stageRoot.Length).TrimStart([char[]]@('\', '/'))
        if ($relative.Split([char[]]@('\', '/'))[0] -eq 'data') {
            continue
        }

        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, 'Golether/' + $relative.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Package: $package"
if (-not $hasMpv) {
    Write-Warning "libmpv-2.dll was not found in Native\${Runtime}: the package plays without video."
}
