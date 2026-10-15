#!/usr/bin/env bash
# Writes the note of a package in every language from the templates of a Notes folder (source this file).
# A template is README.<code>.txt: README.en.txt becomes README.txt, the others keep their code. {version} and
# {runtime} are filled in. A line that starts with a tag ([win], [linux], [macos], or [unix] for both Linux and macOS)
# is kept only for that system, without the tag; a line without a tag is for every system. package-notes.ps1 does
# the same for the PowerShell scripts.
# Usage: write_package_notes <notes folder> <target folder> <linux|macos> <version> <runtime>
write_package_notes() {
  local notes="$1" target="$2" platform="$3" version="$4" runtime="$5" template code name
  for template in "$notes"/README.*.txt; do
    code="${template##*/README.}"
    code="${code%.txt}"
    if [ "$code" = en ]; then
      name=README.txt
    else
      name="README.$code.txt"
    fi

    # A checkout on Windows has CRLF line ends; the notes of a Linux or macOS package have LF.
    tr -d '\r' < "$template" | awk -v platform="$platform" -v version="$version" -v runtime="$runtime" '
      {
        line = $0
        if (match(line, /^\[[a-z]+\] /)) {
          tag = substr(line, 2, RLENGTH - 3)
          if (tag != platform && !(tag == "unix" && platform != "win")) {
            next
          }
          line = substr(line, RLENGTH + 1)
        }
        gsub(/[{]version[}]/, version, line)
        gsub(/[{]runtime[}]/, runtime, line)
        print line
      }' > "$target/$name"
  done
}

# Puts the license of Golether (LICENSE.txt) and the notices of its components (THIRD-PARTY-NOTICES.txt) from the root
# of the repository into a package; the GPL asks for both next to the program. Line ends become LF.
# Usage: write_package_licenses <target folder>
write_package_licenses() {
  local target="$1" root name
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  for name in LICENSE.txt THIRD-PARTY-NOTICES.txt; do
    tr -d '\r' < "$root/$name" > "$target/$name"
  done
}
