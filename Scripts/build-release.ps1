<#
.SYNOPSIS
    Builds every package of a release that is made on this machine, one after another, and lists them with checksums.

.DESCRIPTION
    Runs the build scripts of the categories in this order:
      1. AppWindows      App/build-installer.ps1             Golether-<version>-win-x64.zip and -setup.exe
      2. AppLinux        App/publish-linux.ps1               Golether-<version>-linux-x64.tar.gz (inside WSL)
      3. ServerWindows   Server/build-server-installer.ps1   GoletherServer-<version>-win-x64.zip and -setup.exe
      4. ServerLinux     Server/publish-server-linux.ps1     GoletherServer-<version>-linux-x64.tar.gz (Git Bash)
      5. ServerMacOS     Server/publish-server-macos.ps1     GoletherServer-<version>-osx-arm64.tar.gz (Git Bash)

    Every step runs in a PowerShell process of its own, so one script cannot leave a changed folder or variable to the
    next. The first failed step stops the bundle (see -ContinueOnError). At the end the packages of the version found
    in artifacts/publish are listed with their size and SHA-256, and the checksums are written to
    artifacts/publish/SHA256SUMS-<version>.txt, ready to be attached to the release.

    The macOS packages of the application are built by the "Packages" workflow on GitHub (.github/workflows/packages.yml):
    they need macOS itself.

    What the machine needs: Inno Setup 6+ (the installers), WSL (the Linux package of the application) and Git for
    Windows (the Linux and macOS packages of the server). Each step checks its own tools and says what is missing.

.PARAMETER Skip
    The steps to leave out, by name: AppWindows, AppLinux, ServerWindows, ServerLinux, ServerMacOS.

.PARAMETER ContinueOnError
    Runs the remaining steps after a failed one; the bundle still ends with an error when any step failed.

.EXAMPLE
    ./Scripts/build-release.ps1
    ./Scripts/build-release.ps1 -Skip AppLinux
    ./Scripts/build-release.ps1 -ContinueOnError
#>
[CmdletBinding()]
param(
    [string[]] $Skip = @(),
    [switch] $ContinueOnError
)

$ErrorActionPreference = 'Stop'

$stepNames = 'AppWindows', 'AppLinux', 'ServerWindows', 'ServerLinux', 'ServerMacOS'

# "powershell -File" passes "A,B" as one string, a PowerShell prompt as a list: both are read the same way.
$Skip = @($Skip | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($name in $Skip) {
    if ($stepNames -notcontains $name) {
        throw "Unknown step '$name'. The steps are: $($stepNames -join ', ')."
    }
}

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..')).Path
$output = Join-Path $root 'artifacts\publish'

# Every package carries the version of Directory.Build.props; the UI project reads it like the build scripts do.
$ui = Join-Path $root 'Sources\Apps\Golether.UI\Golether.UI.csproj'
$version = (& dotnet msbuild $ui -nologo -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) {
    throw 'Cannot read the version from the project.'
}

$steps = @(
    @{ Name = 'AppWindows'; Script = 'App\build-installer.ps1' },
    @{ Name = 'AppLinux'; Script = 'App\publish-linux.ps1' },
    @{ Name = 'ServerWindows'; Script = 'Server\build-server-installer.ps1' },
    @{ Name = 'ServerLinux'; Script = 'Server\publish-server-linux.ps1' },
    @{ Name = 'ServerMacOS'; Script = 'Server\publish-server-macos.ps1' }
)

# The steps run in the same PowerShell the bundle runs in (Windows PowerShell or PowerShell 7).
$shell = (Get-Process -Id $PID).Path
$results = @()
$stopped = $false

Write-Host "==> Golether $version release bundle"
foreach ($step in $steps) {
    if ($stopped) {
        $results += [pscustomobject]@{ Step = $step.Name; Result = 'not run'; Time = '' }
        continue
    }

    if ($Skip -contains $step.Name) {
        $results += [pscustomobject]@{ Step = $step.Name; Result = 'skipped'; Time = '' }
        continue
    }

    Write-Host ''
    Write-Host "==> $($step.Name): $($step.Script)"
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptDir $step.Script)
    $code = $LASTEXITCODE
    $watch.Stop()

    $time = '{0:mm\:ss}' -f $watch.Elapsed
    $result = if ($code -eq 0) { 'done' } else { "failed (exit code $code)" }
    $results += [pscustomobject]@{ Step = $step.Name; Result = $result; Time = $time }

    if ($code -ne 0 -and -not $ContinueOnError) {
        $stopped = $true
    }
}

Write-Host ''
Write-Host '==> Steps'
$results | Format-Table -AutoSize | Out-String | Write-Host

# The packages of this version: the archives and the installers in the root of artifacts/publish.
$packages = @(Get-ChildItem $output -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like "*-$version-*" -and
        ($_.Name.EndsWith('.zip') -or $_.Name.EndsWith('.tar.gz') -or $_.Name.EndsWith('.exe'))
    } | Sort-Object Name)

if ($packages.Count -gt 0) {
    $sums = foreach ($package in $packages) {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName).Hash.ToLowerInvariant()
        [pscustomobject]@{ Package = $package.Name; Size = '{0:N1} MB' -f ($package.Length / 1MB); Sha256 = $hash }
    }

    Write-Host "==> Packages in $output"
    $sums | Format-Table -AutoSize | Out-String -Width 400 | Write-Host

    # The usual sha256sum format, so "sha256sum -c" checks a download.
    $sumsFile = Join-Path $output "SHA256SUMS-$version.txt"
    $lines = foreach ($sum in $sums) {
        "$($sum.Sha256)  $($sum.Package)"
    }
    [System.IO.File]::WriteAllText($sumsFile, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Checksums: $sumsFile"
}
else {
    Write-Warning "No packages of version $version were found in $output."
}

$failed = @($results | Where-Object { $_.Result -like 'failed*' })
if ($failed.Count -gt 0) {
    throw "The release bundle is incomplete: $(($failed | ForEach-Object { $_.Step }) -join ', ') failed."
}

Write-Host "The packages of Golether $version made on this machine are ready. The macOS packages of the application come from the Packages workflow on GitHub."
