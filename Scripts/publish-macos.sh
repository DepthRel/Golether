#!/usr/bin/env bash
# Publishes Golether for macOS 14+ (self-contained, Golether.app with an ad-hoc signature) and packs it.
# Usage: ./Scripts/publish-macos.sh [runtime=osx-arm64] [configuration=Release] [output=artifacts/publish]
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$script_dir/publish-common.sh" "${1:-osx-arm64}" "${2:-Release}" "${3:-$(cd "$script_dir/.." && pwd)/artifacts/publish}"
