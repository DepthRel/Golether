<#
.SYNOPSIS
    Publishes the Golether rendezvous server as a self-contained Release build for Windows and packs it into a .zip.

.DESCRIPTION
    Layout of the folder and the archive:
      GoletherServer/
        golether-server.exe   the launcher with the server icon (console program and Windows service in one)
        bin/                  the program, the .NET runtime and the libraries
        config/               appsettings.json: listening address, port, limits
        docs/                 ADMIN-GUIDE.md and ADMIN-GUIDE.<code>.md (the administrator guide for every system),
                              README.txt and README.<code>.txt (from the templates in Notes)
        golether-server-monitor.exe  the launcher of Golether Server Monitor (the state, the journal of events, the
                              start and stop of the service and the settings in a window)
        monitor/              the monitor itself
        LICENSE.txt           the GPL-3.0 text; THIRD-PARTY-NOTICES.txt lists the components and their licenses

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

$guides = 'docs\ADMIN-GUIDE.md', 'docs\ADMIN-GUIDE.de.md', 'docs\ADMIN-GUIDE.es.md', 'docs\ADMIN-GUIDE.fr.md', 'docs\ADMIN-GUIDE.pt.md', 'docs\ADMIN-GUIDE.ru.md'
foreach ($expected in @('golether-server.exe', 'bin\golether-server.dll', 'config\appsettings.json', 'monitor\golether-server-monitor.exe', 'golether-server-monitor.exe', 'config\monitor.json') + $guides) {
    if (-not (Test-Path (Join-Path $stage $expected))) {
        throw "The package layout is incomplete: $expected is missing."
    }
}

# The note of the package in every language, from the templates in Notes: README.txt in English (the default one)
# and README.<code>.txt for the others. The installer shows the one of the chosen language after the installation.
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'Shared/package-notes.ps1')
Write-PackageNotes -Notes (Join-Path $PSScriptRoot 'Notes') -Target (Join-Path $stage 'docs') -Platform win -Version $version -Runtime $Runtime
Write-PackageLicenses -Target $stage

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
