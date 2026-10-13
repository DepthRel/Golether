#!/usr/bin/env bash
# Builds the Linux package of Golether inside WSL from a repository that lives on the Windows side.
# Called by Scripts/publish-linux.ps1; can also be run by hand: wsl bash Scripts/publish-linux-in-wsl.sh [runtime]
#
# Why not simply run publish-linux.sh on /mnt/c: Windows folders keep no executable bits, so the archive would hold
# files Linux refuses to start. The sources are therefore copied into the WSL file system, built there, and only
# the finished archive comes back to artifacts/publish.
# The .NET SDK is installed into ~/.dotnet with the official script when it is missing (no sudo, nothing system-wide).
set -euo pipefail

runtime="${1:-linux-x64}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_root="$(cd "$script_dir/.." && pwd)"
work="$HOME/golether-build"
dotnet_dir="$HOME/.dotnet"

for tool in git tar curl; do
  command -v "$tool" >/dev/null || { echo "'$tool' is missing in WSL: sudo apt install $tool" >&2; exit 2; }
done

if ! "$dotnet_dir/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "==> .NET SDK 10 -> $dotnet_dir"
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$dotnet_dir" --no-path
fi
export DOTNET_ROOT="$dotnet_dir"
export PATH="$dotnet_dir:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# Everything git knows about or would add (tracked and new files, nothing ignored), exactly as it is on disk now.
echo "==> sources: $source_root -> $work/src"
rm -rf "$work/src"
mkdir -p "$work/src"
(cd "$source_root" && git -c safe.directory='*' -c core.quotepath=off ls-files -z --cached --others --exclude-standard \
  | tar --null --ignore-failed-read -T - -cf -) | tar -xf - -C "$work/src"

# The scripts must be LF and executable whatever the Windows checkout did to them.
find "$work/src/Scripts" -name '*.sh' -exec sed -i 's/\r$//' {} + -exec chmod +x {} +

bash "$work/src/Scripts/publish-linux.sh" "$runtime" Release "$work/out"

mkdir -p "$source_root/artifacts/publish"
cp -f "$work/out"/Golether-*-"$runtime".tar.gz "$source_root/artifacts/publish/"
echo
echo "Ready:"
ls -1 "$source_root/artifacts/publish"/Golether-*-"$runtime".tar.gz
