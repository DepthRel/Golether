Сервер рукопожатий Golether {version} ({runtime})

Полное руководство администратора (Windows, Linux, macOS): ADMIN-GUIDE.ru.md рядом с этим файлом.

[win] Коротко:
[unix] Коротко (команды — из корня папки сервера):
[win]   - Служба Windows: установщик GoletherServer-<версия>-setup.exe или команды из раздела 4 руководства.
[win]   - Без службы: запустите golether-server.exe в корне папки сервера (остановка — Ctrl+C).
[win]   - Golether Server Monitor (golether-server-monitor.exe рядом с golether-server.exe): состояние сервера, журнал событий, запуск и остановка службы, экспорт журнала, настройки.
[win]   - Настройки: config\appsettings.json; после правки перезапустите службу.
[win]   - Папки: bin — программа и библиотеки, config — настройки, docs — документация, monitor — монитор.
[linux]   - Служба systemd: sudo ./service/install-service.sh (ставит в /opt/golether-server, включает автозапуск; обновление — то же, настройки сохраняются).
[linux]   - Удаление: sudo ./service/install-service.sh uninstall
[linux]   - Журнал: journalctl -u golether-server -f
[macos]   - Служба launchd: sudo ./service/install-service.sh (ставит в /usr/local/golether-server, запускает при загрузке; обновление — то же, настройки сохраняются).
[macos]   - Удаление: sudo ./service/install-service.sh uninstall
[macos]   - Журнал: tail -f /Library/Logs/golether-server.log
[macos]   - Если macOS не даёт открыть программу: в папке сервера xattr -dr com.apple.quarantine . и codesign --force --sign - golether-server bin/golether-server
[unix]   - Без службы: ./golether-server (остановка — Ctrl+C).
[unix]   - Golether Server Monitor (./golether-server-monitor, нужен рабочий стол): состояние, журнал событий, запуск и остановка, настройки.
[unix]   - Настройки: config/appsettings.json; после правки перезапустите службу.
[unix]   - Папки: bin — программа и библиотеки, config — настройки, docs — документация, service — установка службы, monitor — монитор.
[win]   - Снаружи откройте TCP и UDP 47900. Проверка на этой машине: http://127.0.0.1:47980/health
[unix]   - Снаружи откройте TCP и UDP 47900. Проверка на этой машине: curl http://127.0.0.1:47980/health
  - Ведущим сообщите внешний адрес сервера и порт, например 203.0.113.7:47900.
