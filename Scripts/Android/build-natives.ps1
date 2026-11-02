<#
.SYNOPSIS
    Builds the native components of the Android client in Docker: libmpv (LGPL, no GPL parts) and GStreamer.

.DESCRIPTION
    Builds the image of Natives/Dockerfile and runs Natives/build-natives.sh in it. The sources and intermediate
    files stay in the Docker volume "golether-android-natives" (several GB; remove it with
    "docker volume rm golether-android-natives" when no rebuild is planned), the result goes to
    Native/android-arm64, Native/android-arm, Native/android-x64 and Native/android of the repository:

        Native/android-<abi>/libmpv.so, libav*.so, libsw*.so, libc++_shared.so ...   the video component
        Native/android-<abi>/gstreamer/libgstreamer_android.so                     cameras and voice
        Native/android/gstreamer-java/                                             the Java glue of GStreamer
        Native/LIBMPV-BUILDINFO.txt, Native/GSTREAMER-BUILDINFO.txt                versions and licence proof

    The build runs once per version of the components; the folders are ignored by git.

.PARAMETER Archs
    The mpv-android architectures to build: arm64, armv7l, x86_64.
#>
param(
    [string[]] $Archs = @('arm64', 'armv7l', 'x86_64')
)

# Docker writes its progress to stderr; Windows PowerShell would turn that into errors, so exit codes are checked instead.
$ErrorActionPreference = 'Continue'
$repository = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$context = Join-Path $PSScriptRoot 'Natives'
$output = Join-Path $repository 'Native'
$image = 'golether-android-natives'

& docker version --format '{{.Server.Version}}' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker is not running. Start Docker Desktop and run the script again.'
}

& docker build -t $image $context
if ($LASTEXITCODE -ne 0) {
    throw "The image $image could not be built."
}

New-Item -ItemType Directory -Force $output | Out-Null
& docker run --rm `
    -e "ARCHS=$($Archs -join ' ')" `
    -v "golether-android-natives:/work" `
    -v "${output}:/out" `
    -v "$(Join-Path $context 'build-natives.sh'):/build-natives.sh:ro" `
    $image bash /build-natives.sh
if ($LASTEXITCODE -ne 0) {
    throw 'The native components were not built; see the output above.'
}

Get-ChildItem $output -Directory -Filter 'android*' | ForEach-Object {
    $size = (Get-ChildItem $_.FullName -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
    '{0,-16} {1,8:N1} MB' -f $_.Name, $size
}
