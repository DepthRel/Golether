#!/usr/bin/env bash
# Builds the Debian/Ubuntu installer of Golether from a published Linux package folder.
# Called by publish-common.sh for linux-* runtimes when dpkg-deb is available; can also be run by hand.
# Usage: build-deb.sh <runtime: linux-x64|linux-arm64> <published Golether folder> <version> <output folder>
#
# Layout of the installed package:
#   /opt/golether/                       the published folder; golether.shared next to golether.install puts the data
#                                        of every user into ~/.local/share/Golether, as the Windows installer does
#   /usr/bin/golether                    the launcher
#   /usr/share/applications/golether.desktop   the menu entry and the handler of golether:// invitation links
#   /usr/share/icons/hicolor/{scalable,512x512,256x256}/apps/golether.{svg,png}   the icon in every size a desktop asks
#   /usr/share/metainfo/io.github.depthrel.Golether.metainfo.xml   what software centres show about the application
#   /usr/share/doc/golether/copyright    where the license and the notices of the components are
# libmpv is a dependency (the video); GStreamer is recommended (cameras and voice), so apt installs both by default.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
assets="$script_dir/../../Sources/Apps/Golether.UI/Assets"

runtime="${1:?runtime identifier required}"
published="${2:?published folder required}"
version="${3:?version required}"
output="${4:?output folder required}"

case "$runtime" in
  linux-x64) architecture=amd64 ;;
  linux-arm64) architecture=arm64 ;;
  *) echo "build-deb.sh: no Debian architecture for $runtime" >&2; exit 2 ;;
esac

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
root="$work/golether_${version}_$architecture"

# The program: the published folder without the parts of an unpacked archive (data of a tried copy, the launcher
# and the menu script, which the package replaces with its own).
mkdir -p "$root/opt/golether"
tar -C "$published" --exclude=./data --exclude=./Golether.sh --exclude=./install-desktop-entry.sh -cf - . \
  | tar -C "$root/opt/golether" -xf -
touch "$root/opt/golether/golether.shared"

mkdir -p "$root/usr/bin"
cat > "$root/usr/bin/golether" <<'LAUNCHER'
#!/bin/sh
# Starts Golether installed from the Debian package.
cd /opt/golether/app && exec ./Golether "$@"
LAUNCHER
chmod 755 "$root/usr/bin/golether"

mkdir -p "$root/usr/share/applications"
cat > "$root/usr/share/applications/golether.desktop" <<'ENTRY'
[Desktop Entry]
Type=Application
Name=Golether
Comment=Watch a video together, in sync
Comment[de]=Videos gemeinsam und synchron ansehen
Comment[es]=Ver un vídeo juntos y sincronizados
Comment[fr]=Regarder une vidéo ensemble, en synchro
Comment[pt]=Ver um vídeo em conjunto e em sincronia
Comment[ru]=Синхронный совместный просмотр видео
Exec=golether %u
Icon=golether
Terminal=false
Categories=AudioVideo;Video;Network;
MimeType=x-scheme-handler/golether;
ENTRY

# The icon of the menu and of the software centres: the vector one for any size, raster ones for desktops that
# want them.
icons="$root/usr/share/icons/hicolor"
mkdir -p "$icons/scalable/apps" "$icons/512x512/apps" "$icons/256x256/apps"
cp "$assets/golether.svg" "$icons/scalable/apps/golether.svg"
cp "$published/golether.png" "$icons/512x512/apps/golether.png"
cp "$assets/golether-256.png" "$icons/256x256/apps/golether.png"

# What a software centre (GNOME Software, the App Center of Ubuntu, Discover) shows about the application.
mkdir -p "$root/usr/share/metainfo"
cat > "$root/usr/share/metainfo/io.github.depthrel.Golether.metainfo.xml" <<METAINFO
<?xml version="1.0" encoding="UTF-8"?>
<component type="desktop-application">
  <id>io.github.depthrel.Golether</id>
  <name>Golether</name>
  <summary>Watch a video together, in sync, with webcams and voice</summary>
  <summary xml:lang="de">Videos gemeinsam und synchron ansehen, mit Kameras und Stimmen</summary>
  <summary xml:lang="es">Ver un vídeo juntos y sincronizados, con cámaras y voz</summary>
  <summary xml:lang="fr">Regarder une vidéo ensemble, en synchro, avec caméras et voix</summary>
  <summary xml:lang="pt">Ver um vídeo em conjunto e em sincronia, com câmaras e voz</summary>
  <summary xml:lang="ru">Совместный просмотр видео в синхроне, с камерами и голосом</summary>
  <metadata_license>CC0-1.0</metadata_license>
  <project_license>GPL-3.0-or-later</project_license>
  <developer id="io.github.depthrel">
    <name>DepthRel</name>
  </developer>
  <description>
    <p>Golether plays one video file for up to five people at once. The host shares the file straight from the
    computer, everyone sees and hears each other, and pause, seeking and speed stay in sync.</p>
    <p>No accounts, no ads, no telemetry: the film, the cameras and the voices go directly between the participants,
    encrypted, and no server of the project ever sees them.</p>
  </description>
  <launchable type="desktop-id">golether.desktop</launchable>
  <icon type="stock">golether</icon>
  <url type="homepage">https://depthrel.github.io/Golether/</url>
  <url type="vcs-browser">https://github.com/DepthRel/Golether</url>
  <categories>
    <category>AudioVideo</category>
    <category>Video</category>
  </categories>
  <content_rating type="oars-1.1">
    <content_attribute id="social-chat">intense</content_attribute>
    <content_attribute id="social-audio">intense</content_attribute>
  </content_rating>
  <releases>
    <release version="$version" date="$(date -u +%Y-%m-%d)" />
  </releases>
</component>
METAINFO

mkdir -p "$root/usr/share/doc/golether"
cat > "$root/usr/share/doc/golether/copyright" <<'COPYRIGHT'
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Golether
Source: https://github.com/DepthRel/Golether

Files: *
Copyright: Golether contributors
License: GPL-3.0-or-later
 The full text is in /opt/golether/LICENSE.txt. The components Golether contains, their licenses and where to get
 their source are listed in /opt/golether/THIRD-PARTY-NOTICES.txt.
COPYRIGHT

# Directories and files are readable by all; only programs (ELF executables, not shared libraries) and the launcher
# may run. A build on a Windows file system marks every file executable, so the bits are set here, not kept.
find "$root" -type d -exec chmod 755 {} +
find "$root" -type f -exec chmod 644 {} +
while IFS= read -r -d '' file; do
  if [ "$(head -c 4 "$file" | od -An -tx1 | tr -d ' ')" = 7f454c46 ] && [[ "$file" != *.so* ]]; then
    chmod 755 "$file"
  fi
done < <(find "$root/opt" -type f -print0)
chmod 755 "$root/usr/bin/golether"

installed_size=$(du -sk --exclude=DEBIAN "$root" | cut -f1)
mkdir -p "$root/DEBIAN"
cat > "$root/DEBIAN/control" <<CONTROL
Package: golether
Version: $version
Architecture: $architecture
Maintainer: Golether project <project.golether@gmail.com>
Installed-Size: $installed_size
Depends: libmpv2 | libmpv1, libicu74 | libicu76 | libicu72 | libicu70, libssl3t64 | libssl3, libfontconfig1, libx11-6, libice6, libsm6
Recommends: gstreamer1.0-plugins-base, gstreamer1.0-plugins-good, gstreamer1.0-plugins-bad, gstreamer1.0-nice, gstreamer1.0-pulseaudio
Section: video
Priority: optional
Homepage: https://depthrel.github.io/Golether/
Description: Watch a video together, in sync, with webcams and voice
 Golether plays one video file for up to five people at once. The host shares the file straight from the computer,
 everyone sees and hears each other, and pause, seeking and speed stay in sync. No server of the project ever sees
 the film, the cameras or the voices.
CONTROL

mkdir -p "$output"
package="$(cd "$output" && pwd)/Golether-$version-$runtime.deb"
dpkg-deb --root-owner-group --build "$root" "$package" >/dev/null
echo "Package: $package"
