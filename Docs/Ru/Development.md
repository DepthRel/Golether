# Разработка Golether

## Окружение

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- VS Code с C# Dev Kit и расширением Avalonia, либо Visual Studio / Rider.
- Нативные компоненты (libmpv, GStreamer) готовит утилита `golether-components`, см. [Native/README.md](../../Native/README.md).
- Windows PowerShell 5.1 или PowerShell 7 для `Scripts/*/*.ps1`, bash для `Scripts/*/*.sh`. Скрипты разложены по
  тому, что собирают: `Scripts/App`, `Scripts/Server`, `Scripts/Assets` (см. `Scripts/README.md`).

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
| Golether.Conventions.Tests | правила AGENTS.md, которые не могут проверить анализаторы, по исходникам, разобранным Roslyn: один тип на файл, приватные вложенные типы, XML-комментарии, перечисления в папках Enums, логические операторы в конце строки, порядок using, время только через TimeProvider, схема только через миграции, проекты в своих папках решения |
| Golether.Media.Conference.GStreamer.Tests | два узла WebRTC через loopback на тестовых источниках, проверка сигналов, статистика соединения и переключение на экономный поток камеры; пропускаются без GStreamer |
| Golether.Core.Tests | PeerId, PlaybackState, DriftCorrector, адреса, пути данных (в том числе установка «для всех пользователей») |
| Golether.Localization.Tests | языковые пакеты: чтение, каталог, выбор языка по культуре, запасной английский, одинаковые ключи и подстановки во всех языках, 64 разных слова кода сверки и флаг у каждого языка, каждый запрошенный ключ существует, в коде нет русского текста |
| Golether.Security.Tests | подписи, хранилище ключа, приглашения (ключ и веб-ссылка, срок, общее приглашение и предел запросов), запечатанный адрес сервера, код сверки, допуск |
| Golether.Transports.Tests | кадры, mTLS через loopback, закрепление ключа, NAT-PMP и UPnP с поддельным роутером, сообщения и подпись сервера рукопожатий, пробивание TCP через loopback |
| Golether.Sync.Tests | часы при RTT 1240 мс, сообщения, authority, follower |
| Golether.Media.Streaming.Tests | передача кусков, кэш, поток для плеера, хэши без данных, обмен кусками между участниками и проверка по хэшам |
| Golether.Media.Player.Tests | плеер-симулятор, реестр `golether://`, привязки мыши, громкость, звуковые дорожки и субтитры, загруженные участки настоящего libmpv (пропускаются без него) |
| Golether.Tunnels.AmneziaWG.Tests | ключи, параметры, `.conf`, пакеты, контроллер |
| Golether.Core.Data.Tests | миграции, хранилища EF Core, мигратор |
| Golether.Session.Tests | ведущий и участник целиком: допуск, файл, совместный старт, пауза, общее приглашение, исключение участника и отказ, истёкшее приглашение |
| Golether.UI.Tests | процесс туннелей на двух базах, модели представлений, громкость (своя и каждого участника), выключение устройств, чат и реакции, дорожки и субтитры, продолжение с места остановки, отчёт для диагностики, обновления, выбор и хранение языка, тексты на английском, вход по ссылке, один экземпляр, поиск сервера рукопожатий, исключение участника |
| Golether.Web.Server.Tests | сервер рукопожатий: настройки, лимиты, комнаты и встречи, STUN, встреча целиком до mTLS-потока по пробитому соединению, отказы (подпись, чужая комната, пустая комната, молчащий ведущий) |
| Golether.Web.Server.UI.Tests | монитор сервера: форма настроек и её файл, журнал и поиск по нему, управление службой в Windows, Linux и macOS, окно в Avalonia Headless и флаги его выбора языка |
| Golether.UI.Headless.Tests | главное окно в Avalonia Headless: меню плитки участника, вкладки чата и событий, всплывающая громкость, щелчки по видео, плавная шкала с индикатором загруженного, реакции поверх видео, список языков и смена всех надписей на лету, метод соединения и блок «Дополнительно» на стартовом экране, окна приглашения и настроек |

Тесты (кроме `Golether.Localization.Tests`) работают на русском: `Tests/Shared/UseRussianTexts.cs` включает русский пакет
для всего прогона, потому что ожидания написаны по русским текстам. Проверки другого языка берут свой `Localizer`
и включают его для одного потока работы через `Texts.Scope(...)`, поэтому параллельные тесты друг другу не мешают.

## Добавить язык

1. Скопируйте `Sources/Core/Golether.Localization/Languages/en.json` в `<код>.json` (двухбуквенный код ISO 639-1),
   заполните `Language.Code` и `Language.Name` (название языка на нём самом) и переведите значения. Ключи и
   подстановки `{0}`, `{1}` не меняются; перевод, в котором потерялся ключ или подстановка, роняет
   `Golether.Localization.Tests`.
   64 слова кода сверки (`Verification.Word.00` … `Verification.Word.63`) — переводы одних и тех же предметов во всех
   языках: два человека с разными языками интерфейса сверяют слова одного смысла. Слова должны быть короткими, легко
   произносимыми и непохожими друг на друга.
2. Положите флаг в `Sources/Apps/Golether.UI/Assets/Flags/<код>.png` (120×80, соотношение 3:2): нарисуйте его в
   `Scripts/Assets/generate-flags.ps1`, который рисует все флаги (`en`, `de`, `fr`, `es`, `pt`, `ru`). Монитор
   сервера показывает те же файлы. Тест проверяет, что флаг есть у каждого языка.
3. Для приложения и монитора на этом всё: пакет подхватывается автоматически, язык появляется в списках и выбирается
   по языку системы на первом запуске.

Язык приложения — это ещё не всё, что читает человек. Отдельно переводятся:

- страница проекта: ещё одна запись в списке `list` файла `Sources/Apps/Golether.Pages/assets/languages.js`, флаг в
  `assets/flags/<код>.svg` и ещё одна таблица в `assets/site.js` и `assets/join.js`;
- мастера установки: `[Languages]`, `[Messages]` и `[CustomMessages]` в `Scripts/App/golether.iss` и
  `Scripts/Server/golether-server.iss` (стандартные сообщения каждого языка есть в самом Inno Setup);
- памятки пакетов: ещё один шаблон в `Scripts/App/Notes` и `Scripts/Server/Notes` (`README.<код>.txt`, см.
  `Scripts/README.md`);
- документация (см. ниже).

Новый текст: добавьте ключ в **каждый** файл `Languages/*.json` и берите его через `Texts.Get("Ключ")` в коде или
`{loc:Loc Ключ}` в XAML. Строк на человеческом языке в коде быть не должно (тест `NoRussianTextIsLeftInTheCode`).

## Документация

Документы лежат по языкам: `Docs/En` — основной набор, а `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt` и `Docs/Ru` —
тот же набор по-немецки, по-испански, по-французски, по-португальски и по-русски. Правила:

- набор файлов и их имена одинаковы во всех языках; правка смысла вносится во все языки сразу;
- ссылки снаружи (README) ведут в `Docs/En`; страница проекта открывает руководство на языке, выбранном на ней,
  а если такого перевода нет — английское (таблица `docs` в `assets/languages.js`);
- картинки общие и лежат в `Docs/images`;
- новый язык — новая папка `Docs/<Код>` с тем же набором и ещё одна строка в таблице `docs` страницы.

Руководство администратора сервера лежит рядом с сервером, потому что входит в его пакеты:
`Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.md` — английское, `ADMIN-GUIDE.<код>.md` — остальные (`de`, `es`, `fr`,
`pt`, `ru`). Правила те же: одно содержание на всех языках; ещё один язык — это ещё один `ADMIN-GUIDE.<код>.md`, проект
подхватит его сам.

## Запуск из VS Code

`.vscode/launch.json`:

| Профиль | Что делает |
|---|---|
| Golether: host | собирает `Golether.UI` с зависимостями и запускает с данными в `.vscode/dev/host/data` |
| Golether: participant | то же с данными в `.vscode/dev/guest/data` |
| Golether: host + participant | два независимых устройства на одном компьютере; приглашайте по адресу `127.0.0.1`. Правило «один экземпляр» им не мешает: оно действует в пределах одной папки данных |
| Rendezvous server | собирает и запускает `Golether.Web.Server` на `127.0.0.1:47900`; выберите у ведущего «Свой сервер» в переключателе «Метод соединения» и впишите этот адрес ([RendezvousServer.md](RendezvousServer.md)) |
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

См. [AGENTS.md](../../AGENTS.md) (на английском): один публичный тип на файл, XML-комментарий у каждого члена (CS1591 —
ошибка), фигурные скобки вокруг каждого блока, пустые строки между логическими шагами метода, логические операторы в
конце строки, перечисления в папках `Enums`, директивы `using` блоками System, библиотеки, Golether, только
GPL-совместимые зависимости, время через `TimeProvider`/`IMonotonicClock`, недоверенные входные
данные всегда проверяются.

## Упаковка

| Скрипт | Результат |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/Golether-<версия>-win-x64.zip` |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `.tar.gz` и `.zip` (если есть `zip`), `install-desktop-entry.sh`, `.deb` (где есть `dpkg-deb`: Debian, Ubuntu, WSL) |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `Golether.app` (Info.plist, иконка, ad-hoc подпись) в архиве |
| `Scripts/App/publish-linux.ps1 [-Runtime linux-x64]` | тот же Linux-архив, собранный с Windows внутри WSL (см. ниже) |
| `Scripts/Assets/update-site-screenshot.ps1` | картинка страницы проекта из `Docs/images/screenshot.png` |
| `Scripts/Assets/generate-installer-art.ps1` | картинки мастера установки (тёмная панель с логотипом и логотип для шапки) в `artifacts/installer-art`; оба скрипта сборки установщиков вызывают его сами |
| `Scripts/Assets/generate-icon.ps1` | `golether.ico`, PNG 256/512 из геометрии `golether.svg` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` (архив собирается тем же запуском) | `artifacts/publish/Golether-<версия>-setup.exe` (нужен Inno Setup 6 или новее; проверено на 7.1) и готовая запись для файла обновлений |
| `Scripts/build-release.ps1 [-Skip <шаги>] [-ContinueOnError]` | всё, что для выпуска собирается на Windows, по очереди, и `artifacts/publish/SHA256SUMS-<версия>.txt` (см. ниже) |

Сборка самодостаточная (self-contained), .NET на целевой машине не нужен. Содержимое `Native/<RID>/` попадает в
`app/native`. В корне Windows-пакета лежит `Golether.exe` с иконкой приложения: это тот же хост .NET (цель
`GoletherRootLauncher` в `Golether.UI.csproj`, задача SDK `CreateAppHost`), только с относительным путём
`app\Golether.dll`. Поэтому пакет запускается двойным щелчком из любого места, а на `Golether.exe` можно сделать ярлык
или закрепить его на панели задач. Linux- и macOS-пакеты собираются на целевой ОС: только там в архиве сохранятся права на исполнение.

Как собрать их, не имея под рукой Linux и Mac:

- **Linux с Windows.** `./Scripts/App/publish-linux.ps1` запускает сборку в WSL: исходники копируются в файловую систему
  Linux, там же собираются, а готовый `Golether-<версия>-linux-x64.tar.gz` возвращается в `artifacts/publish`. При
  первом запуске в `~/.dotnet` дистрибутива ставится .NET SDK (официальным скриптом, без `sudo`).
- **Весь выпуск с Windows.** `./Scripts/build-release.ps1` по очереди запускает `App/build-installer.ps1`,
  `App/publish-linux.ps1`, `Server/build-server-installer.ps1`, `Server/publish-server-linux.ps1` и
  `Server/publish-server-macos.ps1`, каждый в собственном процессе PowerShell. Первая ошибка останавливает сборку;
  `-ContinueOnError` доводит остальные шаги, `-Skip AppLinux,ServerMacOS` пропускает шаги. В конце скрипт выводит
  пакеты версии с размером и SHA-256 и пишет `artifacts/publish/SHA256SUMS-<версия>.txt` для выпуска. На машине нужны
  Inno Setup, WSL и Git для Windows; каждый шаг сам говорит, чего не хватает.
- **macOS на GitHub.** Workflow **Packages** (`.github/workflows/packages.yml`) собирает только macOS-пакеты
  приложения, для Apple silicon (`osx-arm64`) и Intel (`osx-x64`): им нужны `sips`, `iconutil` и `codesign`,
  которых нет нигде, кроме macOS. Запуск вручную (Actions → Packages → Run workflow) даёт архивы как артефакты запуска.
  Запуск по тегу версии (`git tag 0.3.0; git push origin 0.3.0`) ещё и прикладывает их к выпуску этого тега; если
  выпуска нет, создаётся черновик. Остальное для выпуска даёт `build-release.ps1`, и это добавляется в тот же выпуск;
  workflow заменяет только файлы с именами своих архивов.

## Отладка сети

- Два экземпляра на одной машине: профиль «Golether: host + participant», в приглашении адрес `127.0.0.1:47800`.
- Большой пинг и потери: Windows — [clumsy](https://jagt.github.io/clumsy/), Linux —
  `tc qdisc add dev lo root netem delay 600ms loss 2%`.
- Без libmpv используется плеер-симулятор: позиция идёт по часам, поэтому синхронизацию можно проверять и без видео.
