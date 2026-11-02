<#
.SYNOPSIS
    Builds the Android client as a Release package and puts the installable .apk into the artifacts.

.DESCRIPTION
    The package holds the native components of every supported processor (arm64, arm, x64), so one file installs on
    any phone. They are taken from Native/android-*: build them once with Scripts/Android/build-natives.ps1.

    The package is signed with the debug key the Android tools keep on this computer. A phone installs it once the
    installation of apps from that source is allowed, and the next builds of the same computer install over it. A
    build of another computer or a store version, signed with another key, does not: the app must be removed first,
    together with its data.

.PARAMETER Configuration
    The build configuration. Default: Release.

.PARAMETER Output
    The output folder. Default: <repository>/artifacts/publish.

.EXAMPLE
    ./Scripts/Android/publish-android.ps1
#>
[CmdletBinding()]
param(
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

$project = Join-Path $root 'Sources\Apps\Golether.Mobile.Android\Golether.Mobile.Android.csproj'
$properties = (& dotnet msbuild $project -nologo -getProperty:Version -getProperty:TargetFramework -getProperty:ApplicationId) | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Cannot read the properties of the project (is the android workload installed? see Docs/En/AndroidEmulator.md): $properties"
}

$read = ($properties | ConvertFrom-Json).Properties
if (-not $read.Version -or -not $read.TargetFramework -or -not $read.ApplicationId) {
    throw "The project did not give its version, target framework and application id: $properties"
}

# The project checks the native components itself and names the script that builds them.
Write-Host "==> $(Split-Path -Leaf $project) ($Configuration)"
& dotnet publish $project -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) {
    throw "Publishing the Android package failed with exit code $LASTEXITCODE."
}

# Next to the signed package the build leaves an unsigned one, which no phone installs.
$signed = Join-Path $root "Sources\Apps\Golether.Mobile.Android\bin\$Configuration\$($read.TargetFramework)\publish\$($read.ApplicationId)-Signed.apk"
if (-not (Test-Path $signed)) {
    throw "The signed package was not found: $signed"
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$package = Join-Path (Resolve-Path $Output).Path "Golether-$($read.Version)-android.apk"
Copy-Item -Path $signed -Destination $package -Force

$size = [Math]::Round((Get-Item $package).Length / 1MB)
Write-Host "Package: $package ($size MB)"
Write-Host 'Signed with the debug key of this computer: for installing by hand, not for a store.'
