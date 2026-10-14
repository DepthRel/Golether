# Scripts

Every script finds the repository by itself, so it can be started from any folder. The packages go to
`artifacts/publish`.

## App — the Golether application

| Script | Runs on | Makes |
|---|---|---|
| `App/publish-win.ps1` | Windows | `Golether-<version>-win-x64.zip`, native components included |
| `App/build-installer.ps1` | Windows | the same archive plus `Golether-<version>-setup.exe` (Inno Setup) from `App/golether.iss` |
| `App/publish-linux.ps1` | Windows (WSL) | `Golether-<version>-linux-x64.tar.gz`, built inside WSL by `App/publish-linux-in-wsl.sh` |
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
| `Assets/generate-flags.ps1` | the flags of the language selector |
| `Assets/generate-installer-art.ps1` | the pictures of the installers (the build scripts run it) |
| `Assets/update-site-screenshot.ps1` | the picture of the project page from `Docs/images/screenshot.png` |

More: `Docs/En/Development.md` (the application) and `Docs/En/RendezvousServer.md` (the server).
