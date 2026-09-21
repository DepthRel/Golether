<p align="center">
  <img src="Docs/images/banner.svg" alt="Golether: Let's go together" width="100%">
</p>

<p align="center">
  <b>Watch movies together, in perfect sync, face to face.</b><br>
  One person plays the file. Everyone else sees the same frame at the same moment, with webcams and voice on the side.
</p>

<p align="center">
  <img alt="License: GPL-3.0-or-later" src="https://img.shields.io/badge/license-GPL--3.0--or--later-F0A860">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="Platforms" src="https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-4FB3A5">
</p>

<p align="center">
  <img src="Docs/images/screenshot.png" alt="The Golether window: a movie in the player, participants' webcams and the chat on the right" width="90%">
</p>

## Let's go together

**Golether** = **GO** + **LET** + **HER**, hiding in the phrase "**LET**'s **GO** toget**HER**". That is the whole idea:
press play once and everybody goes along.

It is a free, open-source, **cross-platform** desktop app (Windows 10+, Linux on X11/Wayland, macOS 14+).
The host shows a video file in its original quality, up to five people watch together, and nobody has to upload,
transcode or install a server.

## Features

- **Truly synchronous playback.** Play, pause and seek work for everyone, in both directions. The protocol is
  designed for pings above 1000 ms, and "start" waits until every participant has the first seconds buffered.
- **Your own audio track and subtitles.** Each participant picks their own: one watches the original, another the
  dub, a third reads subtitles. You can also plug in your own `.srt`/`.ass` file or a separate audio track.
- **Reactions.** Send an emoji that floats over the video for everyone, with your name on it.
- **Draw on the screen.** Grab the pen and circle a detail: everyone sees the line as you draw it, in your colour.
  It fades after two seconds and stays attached to the picture, not to the window.
- **Webcams and voice** in one window: a tile for each person, echo cancellation, per-person volume, and a chat.
- **AmneziaWG tunnel, built in.** When the direct path is blocked, raise a tunnel between participants right from the
  app: no server of your own, no separate VPN client to set up.
- **Secure by default.** Device keys are pinned, the host admits every participant, and both sides compare a
  verification code aloud before anyone is let in. Invitations are single-use.
- **Instant start.** Open a video with "Open with → Golether", and a session is created for it. UPnP / NAT-PMP can
  open the port on your router automatically.
- **Resume where you stopped**, and a UI in English and Russian (a new language is one JSON file).

## Built with

| Area | Technology |
|---|---|
| Language and runtime | C# 14, .NET 10 |
| UI | Avalonia 12, MVVM ([CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)) |
| Video player | [libmpv](https://mpv.io/) (FFmpeg decoding), embedded through P/Invoke |
| Webcams and voice | GStreamer `webrtcbin` (VP8 / Opus, echo cancellation) |
| Networking | TLS transport, UPnP / NAT-PMP port mapping, [AmneziaWG](https://docs.amnezia.org/) tunnels |
| Security | Pinned device keys, TLS, X25519 keys for tunnels (NSec) |
| Storage | SQLite, EF Core, FluentMigrator |
| Tests | xUnit v3, NSubstitute |

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
