Golether {version} ({runtime})

[win] Start: Golether.exe in this folder (you can make a shortcut to it or pin it to the taskbar).
[unix] Start: ./Golether.sh
[linux] Application menu and invitation links: run ./install-desktop-entry.sh once.
All data (keys, database, tunnels, components) is kept in the data folder next to the application. Move and delete the Golether folder as a whole.
[win] Video, cameras and voice: the components are included. If one is missing, the "Components" card of the start screen installs it.
[win] AmneziaWG tunnels: AmneziaWG for Windows does not have to be installed. The first time the "AWG tunnels" window opens, Golether downloads its own engine (about 4 MB, the internet is needed). Windows asks for an administrator's permission when a tunnel is brought up.
[win] Windows shows a SmartScreen warning while the build is not signed.
[linux] Video: install the libmpv package of your distribution (libmpv2 / mpv-libs). Cameras and voice: gstreamer1.0-plugins-base, -good, -bad and gstreamer1.0-nice. Tunnels: amneziawg-tools and the amneziawg module (or amneziawg-go).
[macos] The build is not signed by Apple: on the first start open "System Settings → Privacy & Security → Open Anyway", or run: xattr -dr com.apple.quarantine Golether.app
[macos] Video: brew install mpv. Cameras and voice: brew install gstreamer.
User guide: https://github.com/DepthRel/Golether/blob/main/Docs/En/UserGuide.md
