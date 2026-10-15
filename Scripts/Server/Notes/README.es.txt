Servidor de encuentro de Golether {version} ({runtime})

La guía completa del administrador (Windows, Linux, macOS): ADMIN-GUIDE.es.md junto a este archivo.

[win] En resumen:
[unix] En resumen (los comandos se ejecutan desde la raíz de la carpeta del servidor):
[win]   - Servicio de Windows: el instalador GoletherServer-<versión>-setup.exe o los comandos de la sección 4 de la guía.
[win]   - Sin servicio: ejecuta golether-server.exe en la raíz de la carpeta del servidor (Ctrl+C lo detiene).
[win]   - Golether Server Monitor (golether-server-monitor.exe junto a golether-server.exe): el estado del servidor, el registro de eventos, iniciar y detener el servicio, exportar el registro, los ajustes.
[win]   - Ajustes: config\appsettings.json; reinicia el servicio después de cambiarlos.
[win]   - Carpetas: bin - el programa y sus bibliotecas, config - los ajustes, docs - la documentación, monitor - el monitor.
[linux]   - Servicio systemd: sudo ./service/install-service.sh (instala en /opt/golether-server y lo activa al arrancar; una actualización es el mismo comando, los ajustes se conservan).
[linux]   - Desinstalación: sudo ./service/install-service.sh uninstall
[linux]   - Registro: journalctl -u golether-server -f
[macos]   - Servicio launchd: sudo ./service/install-service.sh (instala en /usr/local/golether-server y lo inicia al arrancar; una actualización es el mismo comando, los ajustes se conservan).
[macos]   - Desinstalación: sudo ./service/install-service.sh uninstall
[macos]   - Registro: tail -f /Library/Logs/golether-server.log
[macos]   - Si macOS no deja abrir el programa: en la carpeta del servidor ejecuta xattr -dr com.apple.quarantine . y codesign --force --sign - golether-server bin/golether-server
[unix]   - Sin servicio: ./golether-server (Ctrl+C lo detiene).
[unix]   - Golether Server Monitor (./golether-server-monitor, necesita un escritorio): el estado, el registro de eventos, iniciar y detener, los ajustes.
[unix]   - Ajustes: config/appsettings.json; reinicia el servicio después de cambiarlos.
[unix]   - Carpetas: bin - el programa y sus bibliotecas, config - los ajustes, docs - la documentación, service - la instalación del servicio, monitor - el monitor.
[win]   - Abre TCP y UDP 47900 hacia el exterior. Comprobación en esta máquina: http://127.0.0.1:47980/health
[unix]   - Abre TCP y UDP 47900 hacia el exterior. Comprobación en esta máquina: curl http://127.0.0.1:47980/health
  - Da a los anfitriones la dirección pública del servidor y el puerto, por ejemplo 203.0.113.7:47900.
