---
name: golether-architecture
description: Use when working anywhere in the Golether repository and you need to know which module owns a behaviour, how a session, sync, file serving, rendezvous, tunnels or security work, or which design constraints a change must respect.
---

# Golether architecture

## Overview

Golether is a GPL-3.0-or-later .NET 10 / Avalonia 12 desktop app for watching one video file together, in sync,
with webcams and voice. The host serves the original file; up to 5 people watch; no media ever passes a server.
Full design: `Docs/En/Architecture.md`, wire formats: `Docs/En/Protocol.md`.

## Module map (`Sources/`)

| Folder | Owns |
|---|---|
| `Core/Golether.Core` | `PeerId`, `PlaybackState`, `DriftCorrector`, `PeerEndpoint`, `AppDataPaths`, `IMonotonicClock` |
| `Core/Golether.Localization` | language packs (`Languages/<code>.json`), `Localizer`, `Texts` |
| `Core/Golether.Core.Data*` | EF Core stores + FluentMigrator SQL migrations (SQLite) |
| `Security` | device key (ECDSA P-256), invitations + `InviteRegistry`, `VerificationCode`, admission, `EndpointSeal` |
| `Transports.*` | mTLS with pinned keys (`Tls`), UPnP/NAT-PMP (`PortMapping`), host TURN relay (`Relay`), hole punching (`Rendezvous`) |
| `Sync/Golether.Sync` | session messages, clock estimation, `PlaybackAuthority` (host) / `PlaybackFollower` |
| `Media.Streaming` / `Media.Player` / `Media.Conference*` | 256 KiB chunk serving + peer sharing / libmpv / GStreamer `webrtcbin` |
| `Tunnels.AmneziaWG` | AWG keys, signed offer/answer packages, CLI control |
| `Session` | `HostSession`, `ParticipantSession` |
| `Apps/Golether.UI` | Avalonia MVVM app, composition root `AppServices` |
| `Apps/Golether.Web.Server(.UI/.Contracts)` | rendezvous + STUN server, its monitor window, shared contracts |
| `Apps/Golether.Pages` | static GitHub Pages site: landing, `join/` link page, `updates.json`, `config.json` |

Dependencies point inwards; Core depends on nothing app-level.

## Ideas a change must keep

- **Time defines state:** `Position + (now − ReferenceTime) × Rate`; the host decides (versions, last request wins).
  Interface commands are intentions; mpv events are never echoed back.
- **Trust nothing from the network or disk:** sizes, formats, rates are bounded; senders are replaced by the
  authenticated `PeerId`; only codes/reasons travel, never user-facing text (a `bye` reason is ignored).
- **Security:** participant pins the host key from the invitation; host admits everyone after a spoken 3-word +
  number code (word numbers are language-neutral, words are localized). Invitations ≤ 1 h, ≤ 16 uses.
- **Servers never see sessions:** rendezvous only introduces sides; cameras use DTLS-SRTP; the host relay rides
  session streams.
- **Portable data:** everything in `data/` next to the program (`golether.install` marker), `GOLETHER_DATA_DIR` for dev.

The general rules (time, schema, texts, dependencies) are in `AGENTS.md`.

## Common mistakes

- Changing `VerificationCode.Compute` or message shapes: breaks compatibility with older peers.
- Trusting `Sender`/`Peer` fields from a message; reading wall-clock time directly.
