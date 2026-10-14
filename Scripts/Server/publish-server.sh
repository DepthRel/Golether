#!/usr/bin/env bash
# Publishes the Golether rendezvous server (self-contained Release build) for Linux or macOS and packs it into
# GoletherServer-<version>-<runtime>.tar.gz and, when the zip tool is available, .zip.
# Layout (the project makes the first four, see target GoletherServerPackageLayout):
#   GoletherServer/golether-server    the launcher
#   GoletherServer/bin/               the program, the .NET runtime and the libraries
#   GoletherServer/config/            appsettings.json
#   GoletherServer/docs/              ADMIN-GUIDE.md and README.txt in English, ADMIN-GUIDE.ru.md and README.ru.txt in Russian
#   GoletherServer/service/           install-service.sh, which installs, updates or removes the service: systemd on
#                                     Linux (golether-server.service), launchd on macOS (org.golether.server.plist)
#   GoletherServer/golether-server-monitor  the launcher of Golether Server Monitor (the state, the journal of
#                                     events, the start and stop of the service and the settings in a window)
#   GoletherServer/monitor/           the monitor itself
# Usage: ./Scripts/Server/publish-server.sh [runtime=linux-x64] [configuration=Release] [output=artifacts/publish]
set -euo pipefail

runtime="${1:-linux-x64}"
configuration="${2:-Release}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$script_dir/../.." && pwd)"
output="${3:-$root/artifacts/publish}"

case "$runtime" in
  linux-x64|linux-arm64|osx-x64|osx-arm64) ;;
  *) echo "Unsupported runtime '$runtime'." >&2; exit 2 ;;
esac

server="$root/Sources/Apps/Golether.Web.Server/Golether.Web.Server.csproj"
version="$(dotnet msbuild "$server" -nologo -getProperty:Version | tr -d '[:space:]')"
stage="$output/$runtime/GoletherServer"
rm -rf "$stage"
mkdir -p "$stage/service"
# dotnet on Windows (Git Bash) needs a Windows path inside a property value; elsewhere the path stays as it is.
stage_native="$stage"
if command -v cygpath >/dev/null; then
  stage_native="$(cygpath -w "$stage")"
fi

echo "==> $(basename "$server") ($runtime, $configuration) -> $stage"
dotnet publish "$server" -c "$configuration" -r "$runtime" --self-contained true -o "$stage/bin" -nologo \
  -p:DebugType=none "-p:GoletherServerPackageRoot=$stage_native"
# The monitor lies in monitor/ of the server folder and finds the settings in ../config.
monitor="$root/Sources/Apps/Golether.Web.Server.UI/Golether.Web.Server.UI.csproj"
echo "==> $(basename "$monitor") ($runtime, $configuration) -> $stage/monitor"
dotnet publish "$monitor" -c "$configuration" -r "$runtime" --self-contained true -o "$stage/monitor" -nologo -p:DebugType=none \
  "-p:GoletherMonitorLauncherPath=$stage_native/golether-server-monitor"

for expected in golether-server bin/golether-server config/appsettings.json docs/ADMIN-GUIDE.md docs/ADMIN-GUIDE.ru.md monitor/golether-server-monitor golether-server-monitor config/monitor.json; do
  if [ ! -f "$stage/$expected" ]; then
    echo "The package layout is incomplete: $expected is missing." >&2
    exit 1
  fi
done

case "$runtime" in
  linux-*)
    cat > "$stage/service/golether-server.service" <<'UNIT'
[Unit]
Description=Golether rendezvous server
Documentation=file:///opt/golether-server/docs/ADMIN-GUIDE.md
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=/opt/golether-server/golether-server
WorkingDirectory=/opt/golether-server
Restart=on-failure
RestartSec=5
DynamicUser=yes
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes

[Install]
WantedBy=multi-user.target
UNIT

    cat > "$stage/service/install-service.sh" <<'INSTALL'
#!/bin/bash
# Installs or updates the rendezvous server in /opt/golether-server as the systemd service golether-server; an
# existing config/appsettings.json and config/monitor.json are kept. "uninstall" removes the service and the folder.
# Usage: sudo ./service/install-service.sh [uninstall]. See docs/ADMIN-GUIDE.md.
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root: sudo $0 $*" >&2
  exit 1
fi

package="$(cd "$(dirname "$0")/.." && pwd)"
target=/opt/golether-server
unit=/etc/systemd/system/golether-server.service
port="$(sed -n 's/.*"Port": *\([0-9]*\).*/\1/p' "$target/config/appsettings.json" "$package/config/appsettings.json" 2>/dev/null | head -n 1 || true)"
port="${port:-47900}"
ufw_active=no
if command -v ufw >/dev/null && ufw status | grep -q "Status: active"; then
  ufw_active=yes
fi

if [ "${1:-}" = "uninstall" ]; then
  systemctl disable --now golether-server 2>/dev/null || true
  rm -f "$unit"
  systemctl daemon-reload
  if [ "$ufw_active" = yes ]; then
    ufw delete allow "$port/tcp" || true
    ufw delete allow "$port/udp" || true
  fi
  rm -rf "$target"
  echo "The rendezvous server was removed. Close the port $port on the router if you forwarded it."
  exit 0
fi

systemctl stop golether-server 2>/dev/null || true
mkdir -p "$target"
if [ "$package" != "$target" ]; then
  # The settings of the server and of the monitor survive an update.
  for kept in appsettings.json monitor.json; do
    if [ -f "$target/config/$kept" ]; then
      cp "$target/config/$kept" "$target/$kept.keep"
    fi
  done

  cp -R "$package/." "$target/"
  for kept in appsettings.json monitor.json; do
    if [ -f "$target/$kept.keep" ]; then
      mv -f "$target/$kept.keep" "$target/config/$kept"
    fi
  done
fi

# The monitor runs without root: it may change its own settings (the language), not those of the server.
chmod 666 "$target/config/monitor.json" 2>/dev/null || true

chmod +x "$target/golether-server" "$target/bin/golether-server" "$target/service/install-service.sh" "$target/monitor/golether-server-monitor" "$target/golether-server-monitor"
install -m 644 "$target/service/golether-server.service" "$unit"
systemctl daemon-reload
systemctl enable --now golether-server
if [ "$ufw_active" = yes ]; then
  ufw allow "$port/tcp"
  ufw allow "$port/udp"
fi

echo "Installed into $target and started. Settings: $target/config/appsettings.json (then: sudo systemctl restart golether-server)."
echo "Log: journalctl -u golether-server -f   Health: curl http://127.0.0.1:47980/health   Guide: $target/docs/ADMIN-GUIDE.md"
INSTALL
    platform_notes_en=(
      "  - systemd service: sudo ./service/install-service.sh (installs into /opt/golether-server and enables it at boot; an update is the same command, the settings are kept)."
      "  - Removal: sudo ./service/install-service.sh uninstall"
      "  - Log: journalctl -u golether-server -f"
    )
    platform_notes=(
      "  - Служба systemd: sudo ./service/install-service.sh (ставит в /opt/golether-server, включает автозапуск; обновление — то же, настройки сохраняются)."
      "  - Удаление: sudo ./service/install-service.sh uninstall"
      "  - Журнал: journalctl -u golether-server -f"
    )
    ;;
  osx-*)
    cat > "$stage/service/org.golether.server.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>org.golether.server</string>
  <key>ProgramArguments</key>
  <array>
    <string>/usr/local/golether-server/golether-server</string>
  </array>
  <key>WorkingDirectory</key><string>/usr/local/golether-server</string>
  <key>UserName</key><string>nobody</string>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key>
  <dict>
    <key>SuccessfulExit</key><false/>
  </dict>
  <key>ThrottleInterval</key><integer>5</integer>
  <key>StandardOutPath</key><string>/Library/Logs/golether-server.log</string>
  <key>StandardErrorPath</key><string>/Library/Logs/golether-server.log</string>
</dict>
</plist>
PLIST

    cat > "$stage/service/install-service.sh" <<'INSTALL'
#!/bin/bash
# Installs or updates the rendezvous server in /usr/local/golether-server as the launchd daemon org.golether.server;
# an existing config/appsettings.json and config/monitor.json are kept. "uninstall" removes the daemon and the folder.
# Usage: sudo ./service/install-service.sh [uninstall]. See docs/ADMIN-GUIDE.md.
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root: sudo $0 $*" >&2
  exit 1
fi

package="$(cd "$(dirname "$0")/.." && pwd)"
target=/usr/local/golether-server
label=org.golether.server
plist="/Library/LaunchDaemons/$label.plist"
log=/Library/Logs/golether-server.log

if [ "${1:-}" = "uninstall" ]; then
  launchctl bootout "system/$label" 2>/dev/null || true
  rm -f "$plist"
  rm -rf "$target"
  echo "The rendezvous server was removed (the log $log is kept). Close the port on the router if you forwarded it."
  exit 0
fi

launchctl bootout "system/$label" 2>/dev/null || true
mkdir -p "$target"
if [ "$package" != "$target" ]; then
  # The settings of the server and of the monitor survive an update.
  for kept in appsettings.json monitor.json; do
    if [ -f "$target/config/$kept" ]; then
      cp "$target/config/$kept" "$target/$kept.keep"
    fi
  done

  cp -R "$package/." "$target/"
  for kept in appsettings.json monitor.json; do
    if [ -f "$target/$kept.keep" ]; then
      mv -f "$target/$kept.keep" "$target/config/$kept"
    fi
  done
fi

# The monitor runs without root: it may change its own settings (the language), not those of the server.
chmod 666 "$target/config/monitor.json" 2>/dev/null || true

chmod +x "$target/golether-server" "$target/bin/golether-server" "$target/service/install-service.sh" "$target/monitor/golether-server-monitor" "$target/golether-server-monitor"
# Downloaded files are quarantined, and Apple Silicon runs only signed programs: a local ad-hoc signature is enough.
xattr -dr com.apple.quarantine "$target" 2>/dev/null || true
for program in "$target/golether-server" "$target/bin/golether-server" "$target/golether-server-monitor" "$target/monitor/golether-server-monitor"; do
  codesign --force --sign - "$program" 2>/dev/null || echo "warning: the ad-hoc signature of $program failed" >&2
done
touch "$log"
chown nobody "$log"
install -m 644 -o root -g wheel "$target/service/$label.plist" "$plist"
launchctl bootstrap system "$plist"

echo "Installed into $target and started. Settings: $target/config/appsettings.json (then: sudo launchctl kickstart -k system/$label)."
echo "Log: tail -f $log   Health: curl http://127.0.0.1:47980/health   Guide: $target/docs/ADMIN-GUIDE.md"
INSTALL
    if command -v codesign >/dev/null; then
      # Ad-hoc signatures for a build made on a Mac; install-service.sh signs builds made elsewhere.
      for program in "$stage/golether-server" "$stage/bin/golether-server"; do
        codesign --force --sign - "$program" || echo "warning: ad-hoc signing of $program failed" >&2
      done
    fi
    platform_notes_en=(
      "  - launchd service: sudo ./service/install-service.sh (installs into /usr/local/golether-server and starts it at boot; an update is the same command, the settings are kept)."
      "  - Removal: sudo ./service/install-service.sh uninstall"
      "  - Log: tail -f /Library/Logs/golether-server.log"
      "  - If macOS refuses to open the program: in the server folder run xattr -dr com.apple.quarantine . and codesign --force --sign - golether-server bin/golether-server"
    )
    platform_notes=(
      "  - Служба launchd: sudo ./service/install-service.sh (ставит в /usr/local/golether-server, запускает при загрузке; обновление — то же, настройки сохраняются)."
      "  - Удаление: sudo ./service/install-service.sh uninstall"
      "  - Журнал: tail -f /Library/Logs/golether-server.log"
      "  - Если macOS не даёт открыть программу: в папке сервера xattr -dr com.apple.quarantine . и codesign --force --sign - golether-server bin/golether-server"
    )
    ;;
esac

executables=(golether-server golether-server-monitor bin/golether-server bin/createdump service/install-service.sh monitor/golether-server-monitor monitor/createdump)
for name in "${executables[@]}"; do
  if [ -f "$stage/$name" ]; then
    chmod +x "$stage/$name"
  fi
done

# The note of the package in two languages: README.txt in English (the default one) and README.ru.txt in Russian.
{
  echo "Golether rendezvous server $version ($runtime)"
  echo
  echo "The full administrator guide (Windows, Linux, macOS): ADMIN-GUIDE.md next to this file."
  echo
  echo "In short (the commands are run from the root of the server folder):"
  printf '%s\n' "${platform_notes_en[@]}"
  echo "  - Without a service: ./golether-server (Ctrl+C stops it)."
  echo "  - Golether Server Monitor (./golether-server-monitor, needs a desktop): the state, the journal of events, start and stop, the settings."
  echo "  - Settings: config/appsettings.json; restart the service after changing them."
  echo "  - Folders: bin - the program and its libraries, config - the settings, docs - the documentation, service - installing the service, monitor - the monitor."
  echo "  - Open TCP and UDP 47900 to the outside. Check on this machine: curl http://127.0.0.1:47980/health"
  echo "  - Give the hosts the public address of the server and the port, for example 203.0.113.7:47900."
} > "$stage/docs/README.txt"

{
  echo "Сервер рукопожатий Golether $version ($runtime)"
  echo
  echo "Полное руководство администратора (Windows, Linux, macOS): ADMIN-GUIDE.ru.md рядом с этим файлом."
  echo
  echo "Коротко (команды — из корня папки сервера):"
  printf '%s\n' "${platform_notes[@]}"
  echo "  - Без службы: ./golether-server (остановка — Ctrl+C)."
  echo "  - Golether Server Monitor (./golether-server-monitor, нужен рабочий стол): состояние, журнал событий, запуск и остановка, настройки."
  echo "  - Настройки: config/appsettings.json; после правки перезапустите службу."
  echo "  - Папки: bin — программа и библиотеки, config — настройки, docs — документация, service — установка службы, monitor — монитор."
  echo "  - Снаружи откройте TCP и UDP 47900. Проверка на этой машине: curl http://127.0.0.1:47980/health"
  echo "  - Ведущим сообщите внешний адрес сервера и порт, например 203.0.113.7:47900."
} > "$stage/docs/README.ru.txt"

package_base="$(cd "$output" && pwd)/GoletherServer-$version-$runtime"
if tar --version 2>/dev/null | grep -q "GNU tar"; then
  # A file system without Unix permissions (a build on Windows) loses the executable bit, so GNU tar sets it
  # explicitly: the programs and scripts get 755, everything else keeps its mode, and the owner is root.
  archive="$package_base.tar"
  rm -f "$archive" "$archive.gz"
  excludes=()
  for name in "${executables[@]}"; do
    excludes+=("--exclude=GoletherServer/$name")
  done
  (cd "$(dirname "$stage")" && tar -cf "$archive" --owner=0 --group=0 --numeric-owner "${excludes[@]}" GoletherServer)
  for name in "${executables[@]}"; do
    if [ -f "$stage/$name" ]; then
      (cd "$(dirname "$stage")" && tar -rf "$archive" --owner=0 --group=0 --numeric-owner --mode=755 "GoletherServer/$name")
    fi
  done
  gzip -f "$archive"
else
  tar -czf "$package_base.tar.gz" -C "$(dirname "$stage")" GoletherServer
fi

echo "Folder:  $stage"
echo "Package: $package_base.tar.gz"
if command -v zip >/dev/null; then
  rm -f "$package_base.zip"
  (cd "$(dirname "$stage")" && zip -qry "$package_base.zip" GoletherServer)
  echo "Package: $package_base.zip"
fi
