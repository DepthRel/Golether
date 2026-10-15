Golether {version} ({runtime})

[win] Start: Golether.exe in diesem Ordner (Sie können eine Verknüpfung dazu anlegen oder es an die Taskleiste anheften).
[unix] Start: ./Golether.sh
[linux] Anwendungsmenü und Einladungslinks: Führen Sie einmal ./install-desktop-entry.sh aus.
Alle Daten (Schlüssel, Datenbank, Tunnel, Komponenten) liegen im Ordner data neben der Anwendung. Verschieben und löschen Sie den Golether-Ordner immer als Ganzes.
[win] Video, Kameras und Sprache: Die Komponenten sind enthalten. Fehlt eine, installiert sie die Karte „Komponenten“ auf dem Startbildschirm.
[win] AmneziaWG-Tunnel: AmneziaWG für Windows muss nicht installiert werden. Beim ersten Öffnen des Fensters „AWG-Tunnel“ lädt Golether seine eigene Engine herunter (etwa 4 MB, Internet nötig). Beim Aufbau eines Tunnels fragt Windows nach Administratorrechten.
[win] Windows zeigt eine SmartScreen-Warnung, solange der Build nicht signiert ist.
[linux] Video: Installieren Sie das libmpv-Paket Ihrer Distribution (libmpv2 / mpv-libs). Kameras und Sprache: gstreamer1.0-plugins-base, -good, -bad und gstreamer1.0-nice. Tunnel: amneziawg-tools und das Modul amneziawg (oder amneziawg-go).
[macos] Der Build ist nicht von Apple signiert: Öffnen Sie beim ersten Start „Systemeinstellungen → Datenschutz & Sicherheit → Dennoch öffnen“ oder führen Sie aus: xattr -dr com.apple.quarantine Golether.app
[macos] Video: brew install mpv. Kameras und Sprache: brew install gstreamer.
Handbuch: https://github.com/DepthRel/Golether/blob/main/Docs/De/UserGuide.md
