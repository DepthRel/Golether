Golether rendezvous server {version} ({runtime})

The full administrator guide (Windows, Linux, macOS): ADMIN-GUIDE.md next to this file.

[win] In short:
[unix] In short (the commands are run from the root of the server folder):
[win]   - Windows service: the installer GoletherServer-<version>-setup.exe or the commands of section 4 of the guide.
[win]   - Without a service: run golether-server.exe in the root of the server folder (Ctrl+C stops it).
[win]   - Golether Server Monitor (golether-server-monitor.exe next to golether-server.exe): the state of the server, the journal of events, starting and stopping the service, exporting the journal, the settings.
[win]   - Settings: config\appsettings.json; restart the service after changing them.
[win]   - Folders: bin - the program and its libraries, config - the settings, docs - the documentation, monitor - the monitor.
[linux]   - systemd service: sudo ./service/install-service.sh (installs into /opt/golether-server and enables it at boot; an update is the same command, the settings are kept).
[linux]   - Removal: sudo ./service/install-service.sh uninstall
[linux]   - Log: journalctl -u golether-server -f
[macos]   - launchd service: sudo ./service/install-service.sh (installs into /usr/local/golether-server and starts it at boot; an update is the same command, the settings are kept).
[macos]   - Removal: sudo ./service/install-service.sh uninstall
[macos]   - Log: tail -f /Library/Logs/golether-server.log
[macos]   - If macOS refuses to open the program: in the server folder run xattr -dr com.apple.quarantine . and codesign --force --sign - golether-server bin/golether-server
[unix]   - Without a service: ./golether-server (Ctrl+C stops it).
[unix]   - Golether Server Monitor (./golether-server-monitor, needs a desktop): the state, the journal of events, start and stop, the settings.
[unix]   - Settings: config/appsettings.json; restart the service after changing them.
[unix]   - Folders: bin - the program and its libraries, config - the settings, docs - the documentation, service - installing the service, monitor - the monitor.
[win]   - Open TCP and UDP 47900 to the outside. Check on this machine: http://127.0.0.1:47980/health
[unix]   - Open TCP and UDP 47900 to the outside. Check on this machine: curl http://127.0.0.1:47980/health
  - Give the hosts the public address of the server and the port, for example 203.0.113.7:47900.
