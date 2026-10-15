# Scripts

Every script finds the repository by itself, so it can be started from any folder. The packages go to
`artifacts/publish`.

## The release bundle

`build-release.ps1` (Windows) builds everything of a release that is made on this machine, one script after another:
`App/build-installer.ps1`, `App/publish-linux.ps1`, `Server/build-server-installer.ps1`,
`Server/publish-server-linux.ps1` and `Server/publish-server-macos.ps1`. Each runs in a PowerShell process of its own;
the first failure stops the bundle (`-ContinueOnError` runs the rest, `-Skip AppLinux,ServerMacOS` leaves steps out).
At the end it lists the packages of the version with their size and SHA-256 and writes
`artifacts/publish/SHA256SUMS-<version>.txt`. The macOS packages of the application are not part of it.

The usual way is GitHub: the **Packages** workflow (`.github/workflows/packages.yml`) runs the same scripts on
GitHub runners and builds every package of a release, the macOS application included. A version tag attaches them
and the checksums to a draft release of that tag; a run by hand leaves them as artifacts of the run. Code signing
belongs to its Windows jobs, between the build and the upload of the artifact. The **CI** workflow
(`.github/workflows/ci.yml`) builds the solution and runs the tests on Windows and Linux for every push to `main` and
every pull request.

## App — the Golether application

| Script | Runs on | Makes |
|---|---|---|
| `App/publish-win.ps1` | Windows | `Golether-<version>-win-x64.zip`, native components included |
| `App/build-installer.ps1` | Windows | the same archive plus `Golether-<version>-setup.exe` (Inno Setup) from `App/golether.iss` |
| `App/publish-linux.ps1` | Windows (WSL) | `Golether-<version>-linux-x64.tar.gz` and `.deb` (`App/build-deb.sh`), built inside WSL by `App/publish-linux-in-wsl.sh` |
| `App/publish-linux.sh` | Linux | the Linux archive (`App/publish-common.sh` does the work) |
| `App/publish-macos.sh` | macOS | `Golether.app` in an archive (the same `App/publish-common.sh`) |

## Server — the rendezvous server and its monitor

| Script | Runs on | Makes |
|---|---|---|
| `Server/publish-server-win.ps1` | Windows | `GoletherServer-<version>-win-x64.zip` |
| `Server/build-server-installer.ps1` | Windows | the same archive plus `GoletherServer-<version>-setup.exe` from `Server/golether-server.iss` |
| `Server/publish-server-linux.ps1` | Windows (Git Bash) | `GoletherServer-<version>-linux-x64.tar.gz` (`-Runtime linux-arm64` as well) |
| `Server/publish-server-macos.ps1` | Windows (Git Bash) | `GoletherServer-<version>-osx-arm64.tar.gz` (`-Runtime osx-x64` as well) |
| `Server/publish-server.sh [runtime]` | Linux, macOS, Git Bash | any of the Linux and macOS packages; the two scripts above run it |

## Assets — pictures made from sources

| Script | Makes |
|---|---|
| `Assets/generate-icon.ps1` | the icons of the application (`-Server`: of the server) from their SVG geometry |
| `Assets/generate-flags.ps1` | the flags of the language selectors of the application and the monitor (every language) |
| `Assets/generate-installer-art.ps1` | the pictures of the installers (the build scripts run it) |
| `Assets/update-site-screenshot.ps1` | the picture of the project page from `Docs/images/screenshot.png` |

## Package notes

The `README.txt` of every package comes from templates, one per language: `App/Notes/README.<code>.txt` for the
application and `Server/Notes/README.<code>.txt` for the server. `README.en.txt` becomes `README.txt`, the others keep
their code; `{version}` and `{runtime}` are filled in. A line tagged `[win]`, `[linux]`, `[macos]` or `[unix]` (Linux
and macOS) goes only into the packages of that system. `Shared/package-notes.ps1` and `Shared/package-notes.sh` do the
work for the PowerShell and the bash scripts. A new language is one more template in each folder.

Every package also carries `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` from the root of the repository, next to the
program (`Write-PackageLicenses` and `write_package_licenses` in the same files). The notices list every component that
a package contains, with its license, its source and the full license texts: a new or updated dependency, NuGet or
native, is written there in the same change.

## Shared — helpers of the other scripts

| Script | Used by |
|---|---|
| `Shared/package-notes.ps1` | `App/publish-win.ps1`, `Server/publish-server-win.ps1` (dot-sourced) |
| `Shared/package-notes.sh` | `App/publish-common.sh`, `Server/publish-server.sh` (sourced) |

More: `Docs/En/Development.md` (the application) and `Docs/En/RendezvousServer.md` (the server).
