#!/usr/bin/env bash
# Publishes Golether for Linux (self-contained) and packs it.
# Usage: ./Scripts/publish-linux.sh [runtime=linux-x64] [configuration=Release] [output=artifacts/publish]
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$script_dir/publish-common.sh" "${1:-linux-x64}" "${2:-Release}" "${3:-$(cd "$script_dir/.." && pwd)/artifacts/publish}"
