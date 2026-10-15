Golether {version} ({runtime})

[win] Запуск: Golether.exe в этой папке (на него можно сделать ярлык или закрепить на панели задач).
[unix] Запуск: ./Golether.sh
[linux] Меню приложений и ссылки-приглашения: один раз запустите ./install-desktop-entry.sh.
Все данные (ключи, база, туннели, компоненты) хранятся в папке data рядом с приложением. Переносите и удаляйте папку Golether целиком.
[win] Видео, камеры и голос: компоненты включены в пакет. Если какого-то нет, его установит карточка «Компоненты» на стартовом экране.
[win] Туннели AmneziaWG: устанавливать AmneziaWG для Windows не нужно. При первом открытии окна «Туннели AWG» Golether сам скачает свой движок (около 4 МБ, нужен интернет). При подъёме туннеля Windows спросит разрешение администратора.
[win] Windows покажет предупреждение SmartScreen, пока сборка не подписана.
[linux] Видео: установите пакет libmpv вашего дистрибутива (libmpv2 / mpv-libs). Камеры и голос: gstreamer1.0-plugins-base, -good, -bad и gstreamer1.0-nice. Туннели: amneziawg-tools и модуль amneziawg (или amneziawg-go).
[macos] Сборка не подписана Apple: при первом запуске откройте «Системные настройки → Конфиденциальность и безопасность → Всё равно открыть» или выполните: xattr -dr com.apple.quarantine Golether.app
[macos] Видео: brew install mpv. Камеры и голос: brew install gstreamer.
Руководство: https://github.com/DepthRel/Golether/blob/main/Docs/Ru/UserGuide.md
