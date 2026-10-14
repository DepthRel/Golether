# Developing Golether

## Environment

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- VS Code with C# Dev Kit and the Avalonia extension, or Visual Studio / Rider.
- The native components (libmpv, GStreamer) are prepared by the `golether-components` utility, see
  [Native/README.md](../../Native/README.md).
- Windows PowerShell 5.1 or PowerShell 7 for `Scripts/*/*.ps1`, bash for `Scripts/*/*.sh`. The scripts are grouped
  by what they build: `Scripts/App`, `Scripts/Server`, `Scripts/Assets` (see `Scripts/README.md`).

## Build and tests

```bash
dotnet build Golether.slnx
dotnet test --solution Golether.slnx                                    # all tests
dotnet test --project Tests/Golether.Sync.Tests/Golether.Sync.Tests.csproj  # one project
```

The `GOLETHER_TEST_SCREENSHOTS` variable (a folder) saves frames of the main window from the headless tests for a
visual check without opening windows on the screen.

The `GOLETHER_TEST_GST_LOG` variable (a file path) switches on the detailed GStreamer log in the conference tests.

The `GOLETHER_TEST_AWG_PACKAGES` variable (a folder with `amneziawg-<arch>-<version>.msi` files from the Amnezia
releases) switches on the check that unpacks the tunnel engine from a real package. Without it that test is skipped,
so an ordinary run needs no download. The package can be downloaded from the address in `ComponentCatalog`; the file
name comes from there too.

The tests are xUnit v3 on Microsoft.Testing.Platform (switched on in `global.json`). The common test packages are
referenced in `Directory.Build.props` for every `*.Tests` project.

| Project | What it checks |
|---|---|
| Golether.Components.Tests | the catalog, hints for Linux/macOS, PE imports, the installer (checksum, size, cache), reuse of GStreamer |
| Golether.Media.Conference.GStreamer.Tests | two WebRTC nodes over loopback on test sources, signal validation, connection statistics and the switch to the economy camera stream; skipped without GStreamer |
| Golether.Core.Tests | PeerId, PlaybackState, DriftCorrector, addresses, data paths (including an installation "for all users") |
| Golether.Localization.Tests | language packs: reading, the catalog, choosing a language by culture, the English fallback, the same keys and placeholders in every language, every requested key exists, no Russian text in the code |
| Golether.Security.Tests | signatures, the key store, invitations (key and web link, lifetime, the shared invitation and the limit of requests), the sealed server address, the verification code, admission |
| Golether.Transports.Tests | frames, mTLS over loopback, key pinning, NAT-PMP and UPnP with a fake router, messages and signature of the rendezvous server, TCP hole punching over loopback |
| Golether.Sync.Tests | clocks at an RTT of 1240 ms, messages, authority, follower |
| Golether.Media.Streaming.Tests | piece transfer, the cache, the stream for the player, hashes without data, exchanging pieces between participants and the check against hashes |
| Golether.Media.Player.Tests | the simulated player, the `golether://` registry, mouse bindings, volume, audio tracks and subtitles, loaded ranges of a real libmpv (skipped without it) |
| Golether.Tunnels.AmneziaWG.Tests | keys, parameters, `.conf`, packages, the controller |
| Golether.Core.Data.Tests | migrations, EF Core stores, the migrator |
| Golether.Session.Tests | the host and a participant as a whole: admission, the file, the joint start, pause, the shared invitation, removal of a participant and refusal, an expired invitation |
| Golether.UI.Tests | the tunnel workflow on two databases, view models, volume (your own and every participant's), switching devices off, chat and reactions, tracks and subtitles, continuing where one stopped, the diagnostic report, updates, choosing and storing the language, texts in English, joining by link, the single instance, lookup of the rendezvous server, removal of a participant |
| Golether.Web.Server.Tests | the rendezvous server: settings, limits, rooms and meetings, STUN, a whole meeting up to an mTLS stream over the punched connection, refusals (signature, somebody else's room, an empty room, a silent host) |
| Golether.UI.Headless.Tests | the main window in Avalonia Headless: the menu of a participant tile, the chat and event tabs, the volume popup, clicks on the video, the smooth timeline with the loaded indicator, reactions over the video, the list of languages and changing every text on the fly, the connection method and the "Advanced" block of the start screen, the invitation and settings windows |

The tests (except `Golether.Localization.Tests`) run in Russian: `Tests/Shared/UseRussianTexts.cs` switches the
Russian pack on for the whole run, because the expectations are written against the Russian texts. Checks of another
language take their own `Localizer` and switch it on for one flow of work through `Texts.Scope(...)`, so parallel
tests do not disturb each other.

## Adding a language

1. Copy `Sources/Core/Golether.Localization/Languages/en.json` to `<code>.json` (a two-letter ISO 639-1 code), fill
   in `Language.Code` and `Language.Name` (the name of the language in that language) and translate the values. Keys
   and the placeholders `{0}`, `{1}` do not change; a translation that lost a key or a placeholder fails
   `Golether.Localization.Tests`.
2. Put a flag into `Sources/Apps/Golether.UI/Assets/Flags/<code>.png` (60×40 or larger, a 3:2 ratio). Without a flag
   the language is shown by its name alone. `Scripts/Assets/generate-flags.ps1` draws the `ru` and `en` flags.
3. That is all: the pack is picked up automatically, the language appears in the list and is chosen by the system
   language on the first start.

The language of the application is not everything a person reads. Translated separately are: the project page (the
tables in `Sources/Apps/Golether.Pages/assets/site.js` and `join.js`), the setup wizards (`[Languages]`,
`[Messages]` and `[CustomMessages]` in `Scripts/App/golether.iss` and `Scripts/Server/golether-server.iss`) and the
documentation (see below).

A new text: add the key to **every** `Languages/*.json` file and take it with `Texts.Get("Key")` in code or
`{loc:Loc Key}` in XAML. There must be no human-language strings in the code (the `NoRussianTextIsLeftInTheCode`
test).

## Documentation

The documents are kept by language: `Docs/En` is the main set, `Docs/Ru` the same set in Russian. The rules:

- the set of files and their names are the same in every language; a change of meaning goes into all languages at
  once;
- links from outside (the README, the project page) lead to `Docs/En`; the project page opens the guide in the
  language chosen on it, and the English one when there is no such translation (the `docs` table in
  `assets/site.js`);
- pictures are shared and live in `Docs/images`;
- a new language is a new folder `Docs/<Code>` with the same set, plus one more line in the `docs` table of the page.

The administrator guide of the server lives next to the server, because it is part of its packages:
`Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.md` is the English one and `ADMIN-GUIDE.ru.md` the Russian one. The
same rules apply: one content in every language; another language is `ADMIN-GUIDE.<code>.md`, which the project
picks up by itself.

## Running from VS Code

`.vscode/launch.json` (the profile names are in Russian):

| Profile | What it does |
|---|---|
| Golether: ведущий (host) | builds `Golether.UI` with its dependencies and starts it with the data in `.vscode/dev/host/data` |
| Golether: участник (participant) | the same with the data in `.vscode/dev/guest/data` |
| Golether: ведущий + участник (host + participant) | two independent devices on one computer; invite by the address `127.0.0.1`. The "single instance" rule does not get in their way: it applies within one data folder |
| Сервер рукопожатий (rendezvous server) | builds and starts `Golether.Web.Server` on `127.0.0.1:47900`; choose "My own server" in the "Connection method" switch of the host and enter that address ([RendezvousServer.md](RendezvousServer.md)) |
| DbMigrator | builds and starts the migrator with arguments from a prompt |

The build tasks (`.vscode/tasks.json`) call `dotnet build <csproj>`: only the chosen project and what it depends on
are built. A full build of the solution is a separate task, `build: solution`.

Environment variables:

| Variable | Purpose |
|---|---|
| `GOLETHER_DATA_DIR` | the data folder (database, keys, tunnels) instead of `data` next to the program; with it the data of early builds is not moved |
| `GOLETHER_LIBMPV` | an explicit path to libmpv |

## Database

The schema is created only by FluentMigrator migrations. To change the schema:

1. Add `Scripts/000N_Name.Up.sql` and `000N_Name.Down.sql` to `Golether.Core.Data.Migrations.SQLite`.
2. Add a class `Versions/M000N_Name.cs` with `[Migration(N, "...")]`, derived from `ScriptMigration`.
3. Update the entities and `GoletherDbContext` (EF Core only maps the model to the tables).

The application applies migrations at start. By hand:

```bash
dotnet run --project Sources/Database/Golether.Dbs.SQLite.DbMigrator -- --database path/golether.db [--list | --down 0]
```

## Code rules

See [AGENTS.md](../../AGENTS.md) (in Russian): XML comments on every member (CS1591 is an error), only GPL-compatible
dependencies, time through `TimeProvider`/`IMonotonicClock`, untrusted input is always validated.

## Packaging

| Script | Result |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/Golether-<version>-win-x64.zip` |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `.tar.gz` and `.zip` (when `zip` is available), `install-desktop-entry.sh` |
| `Scripts/App/publish-macos.sh [osx-arm64] ...` | `Golether.app` (Info.plist, icon, ad-hoc signature) in an archive |
| `Scripts/App/publish-linux.ps1 [-Runtime linux-x64]` | the same Linux archive, built from Windows inside WSL (see below) |
| `Scripts/Assets/update-site-screenshot.ps1` | the picture of the project page from `Docs/images/screenshot.png` |
| `Scripts/Assets/generate-installer-art.ps1` | the pictures of the setup wizard (a dark panel with the logo and the logo for the header) in `artifacts/installer-art`; both installer build scripts call it themselves |
| `Scripts/Assets/generate-icon.ps1` | `golether.ico`, PNG 256/512 from the geometry of `golether.svg` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` (the archive is built by the same run) | `artifacts/publish/Golether-<version>-setup.exe` (needs Inno Setup 6 or newer; checked on 7.1) and a ready entry for the update file |

The build is self-contained; the target machine needs no .NET. The content of `Native/<RID>/` goes to `app/native`.
The root of the Windows package holds `Golether.exe` with the application icon: it is the same .NET host (the
`GoletherRootLauncher` target in `Golether.UI.csproj`, the SDK task `CreateAppHost`), only with the relative path
`app\Golether.dll`. So the package starts with a double click from anywhere, and `Golether.exe` can get a shortcut or
be pinned to the taskbar. The Linux and macOS packages are built on the target OS: only there do the executable
permissions survive in the archive.

How to build them without a Linux machine or a Mac at hand:

- **Linux from Windows.** `./Scripts/App/publish-linux.ps1` runs the build in WSL: the sources are copied into the Linux
  file system and built there, and the finished `Golether-<version>-linux-x64.tar.gz` comes back to
  `artifacts/publish`. The first run installs the .NET SDK into `~/.dotnet` of the distribution (with the official
  script, no `sudo`).
- **macOS and Linux on GitHub.** The **Packages** workflow (`.github/workflows/packages.yml`) builds both packages on
  GitHub runners: the macOS package needs `sips`, `iconutil` and `codesign`, which exist nowhere but on macOS. A run
  by hand (Actions → Packages → Run workflow) gives the archives as artifacts of the run. A run by a version tag
  (`git tag 0.3.0; git push origin 0.3.0`) also attaches them to the release of that tag; when there is no release,
  a draft is created. The Windows installer is built locally (`build-installer.ps1`) and added to the same release.

## Debugging the network

- Two instances on one machine: the "ведущий + участник" profile, with the address `127.0.0.1:47800` in the
  invitation.
- A large ping and losses: on Windows [clumsy](https://jagt.github.io/clumsy/), on Linux
  `tc qdisc add dev lo root netem delay 600ms loss 2%`.
- Without libmpv the simulated player is used: the position follows the clock, so the synchronization can be checked
  without video.
