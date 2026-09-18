# Golether

Открытое (GPL-3.0-or-later) кроссплатформенное десктопное приложение для совместного просмотра видео.
Один участник показывает файл в исходном качестве, у остальных (до 5 человек вместе с ведущим) видео идёт
синхронно. Пауза, перемотка и старт работают у всех участников и в обе стороны. Архитектура рассчитана на пинг больше 1000 мс.
Канал защищён закреплёнными ключами устройств, каждого участника одобряет ведущий. Если прямой путь
заблокирован, между участниками поднимается туннель AmneziaWG без собственного сервера.

Платформы: Windows 10+, Linux (X11/Wayland), macOS 14+. Стек: .NET 10, Avalonia 12, libmpv, SQLite, EF Core, FluentMigrator.

## Быстрый старт

```bash
dotnet build Golether.slnx
dotnet test --solution Golether.slnx
dotnet run --project Sources/Apps/Golether.UI
```

Для видео нужна libmpv: см. [Native/README.md](Native/README.md). Без неё приложение работает в режиме
синхронизации без изображения.

В VS Code профили запуска лежат в `.vscode/launch.json`: «Golether: ведущий», «Golether: участник» и их
комбинация. Каждый профиль собирает только свой проект с зависимостями.

## Документация

| Документ | Для кого |
|---|---|
| [Docs/UserGuide.md](Docs/UserGuide.md) | пользователям: как показать фильм, пригласить, поднять туннель |
| [Docs/Architecture.md](Docs/Architecture.md) | проектная документация: модули, синхронизация, безопасность, план этапов |
| [Docs/Protocol.md](Docs/Protocol.md) | сетевой протокол и форматы пакетов |
| [Docs/Development.md](Docs/Development.md) | разработчикам: сборка, тесты, отладка, упаковка |
| [AGENTS.md](AGENTS.md) | правила кода |

## Упаковка

```powershell
./Scripts/publish-win.ps1            # artifacts/publish/Golether-<версия>-win-x64.zip
./Scripts/build-installer.ps1        # тот же архив плюс установщик для Windows (нужен Inno Setup 6+)
```

```bash
./Scripts/publish-linux.sh           # .tar.gz (+ .zip, если установлен zip)
./Scripts/publish-macos.sh           # Golether.app с ad-hoc подписью
```

## Лицензия

GPL-3.0-or-later. Все зависимости совместимы с GPL.
