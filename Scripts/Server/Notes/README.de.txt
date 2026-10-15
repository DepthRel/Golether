Golether-Vermittlungsserver {version} ({runtime})

Das vollständige Administratorhandbuch (Windows, Linux, macOS): ADMIN-GUIDE.de.md neben dieser Datei.

[win] Kurz gesagt:
[unix] Kurz gesagt (die Befehle werden im Hauptordner des Servers ausgeführt):
[win]   - Windows-Dienst: das Installationsprogramm GoletherServer-<Version>-setup.exe oder die Befehle aus Abschnitt 4 des Handbuchs.
[win]   - Ohne Dienst: Starten Sie golether-server.exe im Hauptordner des Servers (Strg+C beendet ihn).
[win]   - Golether Server Monitor (golether-server-monitor.exe neben golether-server.exe): Zustand des Servers, Ereignisjournal, Starten und Stoppen des Dienstes, Export des Journals, Einstellungen.
[win]   - Einstellungen: config\appsettings.json; starten Sie den Dienst nach einer Änderung neu.
[win]   - Ordner: bin – Programm und Bibliotheken, config – Einstellungen, docs – Dokumentation, monitor – der Monitor.
[linux]   - systemd-Dienst: sudo ./service/install-service.sh (installiert nach /opt/golether-server und aktiviert den Autostart; ein Update ist derselbe Befehl, die Einstellungen bleiben erhalten).
[linux]   - Entfernen: sudo ./service/install-service.sh uninstall
[linux]   - Protokoll: journalctl -u golether-server -f
[macos]   - launchd-Dienst: sudo ./service/install-service.sh (installiert nach /usr/local/golether-server und startet beim Hochfahren; ein Update ist derselbe Befehl, die Einstellungen bleiben erhalten).
[macos]   - Entfernen: sudo ./service/install-service.sh uninstall
[macos]   - Protokoll: tail -f /Library/Logs/golether-server.log
[macos]   - Wenn macOS das Programm nicht öffnet: im Serverordner xattr -dr com.apple.quarantine . und codesign --force --sign - golether-server bin/golether-server ausführen
[unix]   - Ohne Dienst: ./golether-server (Strg+C beendet ihn).
[unix]   - Golether Server Monitor (./golether-server-monitor, benötigt einen Desktop): Zustand, Ereignisjournal, Starten und Stoppen, Einstellungen.
[unix]   - Einstellungen: config/appsettings.json; starten Sie den Dienst nach einer Änderung neu.
[unix]   - Ordner: bin – Programm und Bibliotheken, config – Einstellungen, docs – Dokumentation, service – Installation des Dienstes, monitor – der Monitor.
[win]   - Öffnen Sie TCP und UDP 47900 nach außen. Prüfung auf diesem Rechner: http://127.0.0.1:47980/health
[unix]   - Öffnen Sie TCP und UDP 47900 nach außen. Prüfung auf diesem Rechner: curl http://127.0.0.1:47980/health
  - Teilen Sie den Gastgebern die öffentliche Adresse des Servers und den Port mit, zum Beispiel 203.0.113.7:47900.
