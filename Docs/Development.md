# Разработка Golether

## Окружение

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- VS Code с C# Dev Kit и расширением Avalonia, либо Visual Studio / Rider.
- Нативные компоненты (libmpv, GStreamer) готовит утилита `golether-components`, см. [Native/README.md](../Native/README.md).
- Windows PowerShell 5.1 или PowerShell 7 для `Scripts/*.ps1`, bash для `Scripts/*.sh`.

## Сборка и тесты

```bash
dotnet build Golether.slnx
dotnet test --solution Golether.slnx                                    # все тесты
dotnet test --project Tests/Golether.Sync.Tests/Golether.Sync.Tests.csproj  # один проект
```

Переменная `GOLETHER_TEST_SCREENSHOTS` (папка) сохраняет кадры главного окна из headless-тестов для визуальной проверки
без запуска окон на экране.

Переменная `GOLETHER_TEST_GST_LOG` (путь к файлу) включает подробный журнал GStreamer в тестах конференции.

Переменная `GOLETHER_TEST_AWG_PACKAGES` (папка с файлами `amneziawg-<arch>-<версия>.msi` из релизов Amnezia)
включает проверку распаковки движка туннеля на настоящем пакете. Без неё этот тест пропускается, чтобы обычный
прогон не требовал загрузки. Скачать пакет можно по адресу из `ComponentCatalog`, имя файла берётся оттуда же.

Тесты — xUnit v3 на Microsoft.Testing.Platform (включено в `global.json`). Общие пакеты тестов подключаются в
`Directory.Build.props` для всех проектов `*.Tests`.

| Проект | Что проверяет |
|---|---|
| Golether.Components.Tests | каталог, подсказки для Linux/macOS, импорты PE, установщик (сумма, размер, кэш), переиспользование GStreamer |
| Golether.Media.Conference.GStreamer.Tests | два узла WebRTC через loopback на тестовых источниках, проверка сигналов, статистика соединения и переключение на экономный поток камеры; пропускаются без GStreamer |
| Golether.Core.Tests | PeerId, PlaybackState, DriftCorrector, адреса, пути данных |
| Golether.Security.Tests | подписи, хранилище ключа, приглашения, код сверки, допуск |
| Golether.Transports.Tests | кадры, mTLS через loopback, закрепление ключа, NAT-PMP и UPnP с поддельным роутером |
| Golether.Sync.Tests | часы при RTT 1240 мс, сообщения, authority, follower |
| Golether.Media.Streaming.Tests | передача кусков, кэш, поток для плеера, хэши без данных, обмен кусками между участниками и проверка по хэшам |
| Golether.Media.Player.Tests | плеер-симулятор, реестр `golether://`, привязки мыши, громкость, звуковые дорожки и субтитры, загруженные участки настоящего libmpv (пропускаются без него) |
| Golether.Tunnels.AmneziaWG.Tests | ключи, параметры, `.conf`, пакеты, контроллер |
| Golether.Core.Data.Tests | миграции, хранилища EF Core, мигратор |
| Golether.Session.Tests | ведущий и участник целиком: допуск, файл, совместный старт, пауза |
| Golether.UI.Tests | процесс туннелей на двух базах, модели представлений, громкость (своя и каждого участника), выключение устройств, чат и реакции, дорожки и субтитры, продолжение с места остановки, отчёт для диагностики, обновления |
| Golether.UI.Headless.Tests | главное окно в Avalonia Headless: меню плитки участника, вкладки чата и событий, всплывающая громкость, щелчки по видео, плавная шкала с индикатором загруженного, реакции поверх видео |

## Запуск из VS Code

`.vscode/launch.json`:

| Профиль | Что делает |
|---|---|
| Golether: ведущий | собирает `Golether.UI` с зависимостями и запускает с данными в `.vscode/dev/host/data` |
| Golether: участник | то же с данными в `.vscode/dev/guest/data` |
| Golether: ведущий + участник | два независимых устройства на одном компьютере; приглашайте по адресу `127.0.0.1` |
| DbMigrator | собирает и запускает мигратор с аргументами из запроса |

Задачи сборки (`.vscode/tasks.json`) вызывают `dotnet build <csproj>`: собирается только выбранный проект и
то, от чего он зависит. Полная сборка решения — отдельная задача `build: solution`.

Переменные окружения:

| Переменная | Назначение |
|---|---|
| `GOLETHER_DATA_DIR` | папка данных (БД, ключи, туннели) вместо `data` рядом с программой; при ней данные ранних сборок не переносятся |
| `GOLETHER_LIBMPV` | явный путь к libmpv |

## База данных

Схема создаётся только миграциями FluentMigrator. Чтобы изменить схему:

1. Добавьте `Scripts/000N_Name.Up.sql` и `000N_Name.Down.sql` в `Golether.Core.Data.Migrations.SQLite`.
2. Добавьте класс `Versions/M000N_Name.cs` с `[Migration(N, "...")]`, унаследованный от `ScriptMigration`.
3. Обновите сущности и `GoletherDbContext` (EF Core только сопоставляет модель с таблицами).

Приложение применяет миграции при запуске. Вручную:

```bash
dotnet run --project Sources/Database/Golether.Dbs.SQLite.DbMigrator -- --database path/golether.db [--list | --down 0]
```

## Правила кода

См. [AGENTS.md](../AGENTS.md): XML-комментарии у каждого члена (CS1591 — ошибка), только GPL-совместимые
зависимости, время через `TimeProvider`/`IMonotonicClock`, недоверенные входные данные всегда проверяются.

## Упаковка

| Скрипт | Результат |
|---|---|
| `Scripts/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/Golether-<версия>-win-x64.zip` |
| `Scripts/publish-linux.sh [linux-x64] [Release] [out]` | `.tar.gz` и `.zip` (если есть `zip`), `install-desktop-entry.sh` |
| `Scripts/publish-macos.sh [osx-arm64] ...` | `Golether.app` (Info.plist, иконка, ad-hoc подпись) в архиве |
| `Scripts/generate-icon.ps1` | `golether.ico`, PNG 256/512 из геометрии `golether.svg` |
| `Scripts/build-installer.ps1 [-SkipPublish]` (архив собирается тем же запуском) | `artifacts/publish/Golether-<версия>-setup.exe` (нужен Inno Setup 6 или новее; проверено на 7.1) и готовая запись для файла обновлений |

Сборка самодостаточная (self-contained), .NET на целевой машине не нужен. Содержимое `Native/<RID>/` попадает в
`app/native`. В корне Windows-пакета лежит `Golether.exe` с иконкой приложения: это тот же хост .NET (цель
`GoletherRootLauncher` в `Golether.UI.csproj`, задача SDK `CreateAppHost`), только с относительным путём
`app\Golether.dll`. Поэтому пакет запускается двойным щелчком из любого места, а на `Golether.exe` можно сделать ярлык
или закрепить его на панели задач. Linux- и macOS-пакеты лучше собирать на целевой ОС: только там в архиве сохранятся права на исполнение.

## Отладка сети

- Два экземпляра на одной машине: профиль «ведущий + участник», в приглашении адрес `127.0.0.1:47800`.
- Большой пинг и потери: Windows — [clumsy](https://jagt.github.io/clumsy/), Linux —
  `tc qdisc add dev lo root netem delay 600ms loss 2%`.
- Без libmpv используется плеер-симулятор: позиция идёт по часам, поэтому синхронизацию можно проверять и без видео.
