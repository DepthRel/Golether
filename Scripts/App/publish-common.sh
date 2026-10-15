#!/usr/bin/env bash
# Shared publish logic for publish-linux.sh and publish-macos.sh.
# Publishes Golether (application and database migrator) as self-contained Release builds and packs them into
# Golether-<version>-<runtime>.tar.gz and, when the zip tool is available, .zip.
# Native libraries from <repository>/Native/<runtime>/ are included automatically, LICENSE.txt and
# THIRD-PARTY-NOTICES.txt from the root of the repository too.
# Usage: publish-common.sh <runtime> [configuration=Release] [output=artifacts/publish]
set -euo pipefail

runtime="${1:?runtime identifier required}"
configuration="${2:-Release}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$script_dir/../.." && pwd)"
output="${3:-$root/artifacts/publish}"

case "$runtime" in
  linux-x64|linux-arm64|osx-x64|osx-arm64) ;;
  *) echo "Unsupported runtime '$runtime'." >&2; exit 2 ;;
esac

ui="$root/Sources/Apps/Golether.UI/Golether.UI.csproj"
migrator="$root/Sources/Database/Golether.Dbs.SQLite.DbMigrator/Golether.Dbs.SQLite.DbMigrator.csproj"
version="$(dotnet msbuild "$ui" -nologo -getProperty:Version | tr -d '[:space:]')"
stage="$output/$runtime/Golether"
rm -rf "$stage"
mkdir -p "$stage"

publish() {
  echo "==> $(basename "$1") ($runtime, $configuration) -> $2"
  dotnet publish "$1" -c "$configuration" -r "$runtime" --self-contained true -o "$2" -nologo -p:DebugType=none
}

publish "$ui" "$stage/app"
publish "$migrator" "$stage/tools/dbmigrator"
chmod +x "$stage/app/Golether" "$stage/tools/dbmigrator/Golether.Dbs.SQLite.DbMigrator"
cp "$root/Sources/Apps/Golether.UI/Assets/golether-512.png" "$stage/golether.png"

echo "Golether installation root: all data (keys, database, tunnels, components) is stored in the data folder here." > "$stage/golether.install"

cat > "$stage/Golether.sh" <<'LAUNCHER'
#!/bin/bash
# Starts Golether from any location.
cd "$(dirname "$0")/app" && exec ./Golether "$@"
LAUNCHER
chmod +x "$stage/Golether.sh"

case "$runtime" in
  linux-*)
    cat > "$stage/install-desktop-entry.sh" <<'DESKTOP'
#!/bin/bash
# Registers Golether in the application menu of the current user (paths are absolute: run again after moving).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$HOME/.local/share/applications"
cat > "$HOME/.local/share/applications/golether.desktop" <<ENTRY
[Desktop Entry]
Type=Application
Name=Golether
Comment=Watch a video together, in sync
Comment[de]=Videos gemeinsam und synchron ansehen
Comment[es]=Ver un vídeo juntos y sincronizados
Comment[fr]=Regarder une vidéo ensemble, en synchro
Comment[pt]=Ver um vídeo em conjunto e em sincronia
Comment[ru]=Синхронный совместный просмотр видео
Exec="$here/app/Golether" %u
Path=$here/app
Icon=$here/golether.png
Terminal=false
Categories=AudioVideo;Video;Network;
MimeType=x-scheme-handler/golether;
ENTRY
# Invitation links (golether://join/...) clicked in a browser open Golether.
if command -v update-desktop-database >/dev/null; then
  update-desktop-database "$HOME/.local/share/applications" || true
fi
if command -v xdg-mime >/dev/null; then
  xdg-mime default golether.desktop x-scheme-handler/golether || true
fi
echo "Golether added to the application menu; invitation links open it."
DESKTOP
    chmod +x "$stage/install-desktop-entry.sh"
    platform=linux
    ;;
  osx-*)
    bundle="$stage/Golether.app"
    mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
    cp -R "$stage/app/." "$bundle/Contents/MacOS/"
    cp "$stage/golether.png" "$bundle/Contents/Resources/golether.png"
    if command -v sips >/dev/null && command -v iconutil >/dev/null; then
      iconset="$(mktemp -d)/golether.iconset"
      mkdir -p "$iconset"
      for size in 16 32 64 128 256 512; do
        sips -z $size $size "$stage/golether.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
      done
      iconutil -c icns "$iconset" -o "$bundle/Contents/Resources/golether.icns"
    fi
    cat > "$bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Golether</string>
  <key>CFBundleDisplayName</key><string>Golether</string>
  <key>CFBundleIdentifier</key><string>org.golether.app</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleExecutable</key><string>Golether</string>
  <key>CFBundleIconFile</key><string>golether.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>CFBundleURLTypes</key>
  <array>
    <dict>
      <key>CFBundleURLName</key><string>org.golether.invite</string>
      <key>CFBundleTypeRole</key><string>Viewer</string>
      <key>CFBundleURLSchemes</key><array><string>golether</string></array>
    </dict>
  </array>
  <key>NSCameraUsageDescription</key><string>Golether shows your camera to the participants of the session.</string>
  <key>NSMicrophoneUsageDescription</key><string>Golether sends your voice to the participants of the session.</string>
</dict>
</plist>
PLIST
    rm -rf "$stage/app"
    cat > "$stage/Golether.sh" <<'LAUNCHER'
#!/bin/bash
# Starts Golether from any location.
exec "$(dirname "$0")/Golether.app/Contents/MacOS/Golether" "$@"
LAUNCHER
    chmod +x "$stage/Golether.sh"
    if command -v codesign >/dev/null; then
      # Ad-hoc signature: required for arm64 binaries; not a Developer ID signature.
      codesign --force --deep --sign - "$bundle" || echo "warning: ad-hoc signing failed" >&2
    fi
    platform=macos
    ;;
esac

# The note of the package in every language of the application, from the templates in Notes.
# shellcheck source=../Shared/package-notes.sh
source "$script_dir/../Shared/package-notes.sh"
write_package_notes "$script_dir/Notes" "$stage" "$platform" "$version" "$runtime"
write_package_licenses "$stage"

package_base="$(cd "$output" && pwd)/Golether-$version-$runtime"
tar -czf "$package_base.tar.gz" -C "$(dirname "$stage")" --exclude="Golether/data" Golether
echo "Package: $package_base.tar.gz"
if command -v zip >/dev/null; then
  rm -f "$package_base.zip"
  (cd "$(dirname "$stage")" && zip -qry "$package_base.zip" Golether -x "Golether/data/*")
  echo "Package: $package_base.zip"
fi

# The installer of Debian, Ubuntu and their relatives, where dpkg-deb is at hand (any of them, WSL included).
if [ "$platform" = linux ]; then
  if command -v dpkg-deb >/dev/null; then
    bash "$script_dir/build-deb.sh" "$runtime" "$stage" "$version" "$output"
  else
    echo "dpkg-deb is missing: no .deb package (it comes with Debian and Ubuntu, WSL included)." >&2
  fi
fi
