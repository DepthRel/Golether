<#
.SYNOPSIS
    Publishes the Golether rendezvous server as a self-contained Release build for Windows and packs it into a .zip.

.DESCRIPTION
    Layout of the folder and the archive:
      GoletherServer/
        golether-server.exe   the launcher with the server icon (console program and Windows service in one)
        bin/                  the program, the .NET runtime and the libraries
        config/               appsettings.json: listening address, port, limits
        docs/                 ADMIN-GUIDE.md and ADMIN-GUIDE.ru.md (the administrator guide for every system), README.txt and README.ru.txt
        golether-server-monitor.exe  the launcher of Golether Server Monitor (the state, the journal of events, the
                              start and stop of the service and the settings in a window)
        monitor/              the monitor itself

    The project makes the layout (target GoletherServerPackageLayout); this script adds the monitor, README.txt and the
    archive.

    The folder is artifacts/publish/<runtime>/GoletherServer, the archive
    artifacts/publish/GoletherServer-<version>-<runtime>.zip. build-server-installer.ps1 makes the installer from the
    same folder.

.PARAMETER Runtime
    The runtime identifier. Default: win-x64.

.PARAMETER Configuration
    The build configuration. Default: Release.

.PARAMETER Output
    The output root. Default: <repository>/artifacts/publish.

.EXAMPLE
    ./Scripts/Server/publish-server-win.ps1
    ./Scripts/Server/publish-server-win.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [string] $Output
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults, so paths are resolved here.
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
if (-not $Output) {
    $Output = Join-Path $root 'artifacts\publish'
}

if ($Runtime -notmatch '^win-(x64|x86|arm64)$') {
    throw "Unsupported runtime '$Runtime'. Use win-x64, win-x86 or win-arm64."
}

$server = Join-Path $root 'Sources\Apps\Golether.Web.Server\Golether.Web.Server.csproj'
$version = (& dotnet msbuild $server -nologo -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) {
    throw 'Cannot read the version from the project.'
}

$stage = Join-Path $Output "$Runtime\GoletherServer"
if (Test-Path $stage) {
    $stageFull = (Resolve-Path $stage).Path.TrimEnd('\') + '\'
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($stageFull, [StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count -gt 0) {
        throw "The server is running from $stage (process $($running[0].Id)). Stop it and publish again, or use -Output."
    }

    Remove-Item -Recurse -Force $stage
}

Write-Host "==> $(Split-Path -Leaf $server) ($Runtime, $Configuration) -> $stage"
& dotnet publish $server -c $Configuration -r $Runtime --self-contained true -o (Join-Path $stage 'bin') -nologo `
    "-p:GoletherServerPackageRoot=$stage" `
    -p:DebugType=none -p:PublishReadyToRun=false
if ($LASTEXITCODE -ne 0) {
    throw "Publishing '$server' failed with exit code $LASTEXITCODE."
}

# The monitor lies in monitor/ of the server folder and finds the settings in ../config.
$monitor = Join-Path $root 'Sources\Apps\Golether.Web.Server.UI\Golether.Web.Server.UI.csproj'
Write-Host "==> $(Split-Path -Leaf $monitor) ($Runtime, $Configuration) -> $(Join-Path $stage 'monitor')"
& dotnet publish $monitor -c $Configuration -r $Runtime --self-contained true -o (Join-Path $stage 'monitor') -nologo `
    -p:DebugType=none -p:PublishReadyToRun=false "-p:GoletherMonitorLauncherPath=$(Join-Path $stage 'golether-server-monitor.exe')"
if ($LASTEXITCODE -ne 0) {
    throw "Publishing '$monitor' failed with exit code $LASTEXITCODE."
}

foreach ($expected in 'golether-server.exe', 'bin\golether-server.dll', 'config\appsettings.json', 'docs\ADMIN-GUIDE.md', 'docs\ADMIN-GUIDE.ru.md', 'monitor\golether-server-monitor.exe', 'golether-server-monitor.exe', 'config\monitor.json') {
    if (-not (Test-Path (Join-Path $stage $expected))) {
        throw "The package layout is incomplete: $expected is missing."
    }
}

# The note of the package in two languages: README.txt in English (the default one) and README.ru.txt in Russian.
# The installer shows the one of the chosen language after the installation.
$readmeEnglish = @(
    "Golether rendezvous server $version ($Runtime)",
    '',
    'The full administrator guide (Windows, Linux, macOS): ADMIN-GUIDE.md next to this file.',
    '',
    'In short:',
    '  - Windows service: the installer GoletherServer-<version>-setup.exe or the commands of section 4 of the guide.',
    '  - Without a service: run golether-server.exe in the root of the server folder (Ctrl+C stops it).',
    '  - Golether Server Monitor (golether-server-monitor.exe next to golether-server.exe): the state of the server, the journal of events,',
    '    starting and stopping the service, exporting the journal, the settings.',
    '  - Settings: config\appsettings.json; restart the service after changing them.',
    '  - Folders: bin - the program and its libraries, config - the settings, docs - the documentation, monitor - the monitor.',
    '  - Open TCP and UDP 47900 to the outside. Check on this machine: http://127.0.0.1:47980/health',
    '  - Give the hosts the public address of the server and the port, for example 203.0.113.7:47900.'
)
[System.IO.File]::WriteAllLines((Join-Path $stage 'docs\README.txt'), $readmeEnglish, (New-Object System.Text.UTF8Encoding $true))

$readme = @(
    "Сервер рукопожатий Golether $version ($Runtime)",
    '',
    'Полное руководство администратора (Windows, Linux, macOS): ADMIN-GUIDE.ru.md рядом с этим файлом.',
    '',
    'Коротко:',
    '  - Служба Windows: установщик GoletherServer-<версия>-setup.exe или команды из раздела 4 руководства.',
    '  - Без службы: запустите golether-server.exe в корне папки сервера (остановка — Ctrl+C).',
    '  - Golether Server Monitor (golether-server-monitor.exe рядом с golether-server.exe): состояние сервера, журнал событий,',
    '    запуск и остановка службы, экспорт журнала, настройки.',
    '  - Настройки: config\appsettings.json; после правки перезапустите службу.',
    '  - Папки: bin — программа и библиотеки, config — настройки, docs — документация, monitor — монитор.',
    '  - Снаружи откройте TCP и UDP 47900. Проверка на этой машине: http://127.0.0.1:47980/health',
    '  - Ведущим сообщите внешний адрес сервера и порт, например 203.0.113.7:47900.'
)
[System.IO.File]::WriteAllLines((Join-Path $stage 'docs\README.ru.txt'), $readme, (New-Object System.Text.UTF8Encoding $true))

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$package = Join-Path (Resolve-Path $Output).Path "GoletherServer-$version-$Runtime.zip"
if (Test-Path $package) {
    [System.IO.File]::Delete($package)
}

$zip = [System.IO.Compression.ZipFile]::Open($package, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $stageRoot = (Resolve-Path $stage).Path
    foreach ($file in Get-ChildItem $stageRoot -Recurse -File) {
        $relative = $file.FullName.Substring($stageRoot.Length).TrimStart([char[]]@('\', '/'))
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, 'GoletherServer/' + $relative.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Folder:  $stage"
Write-Host "Package: $package"
