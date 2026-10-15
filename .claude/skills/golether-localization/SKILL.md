---
name: golether-localization
description: Use when adding or changing any text a Golether user, administrator or site visitor reads, adding a language, or editing documentation in Docs/, ADMIN-GUIDE, package notes, installers or the project page.
---

# Golether localization and documentation

## Overview

The rules are in `AGENTS.md` (Localization); it is already in context. This skill is the map of every surface a
language touches and of what guards it. Portuguese is European (Portugal flag). The first start picks the pack by the
two-letter code of the OS UI culture.

## Where texts live

| Surface | Files | Guard |
|---|---|---|
| App + server monitor | `Sources/Core/Golether.Localization/Languages/<code>.json` (flat, keys in ordinal order) | `Golether.Localization.Tests`: same keys, same `{n}` placeholders, keys used in code/XAML exist, no Cyrillic in code |
| Verification words | `Verification.Word.00`…`63` in every pack: the same 64 things, translated, distinct | `Language_HasDistinctVerificationWords` |
| Flags | `Sources/Apps/Golether.UI/Assets/Flags/<code>.png` via `Scripts/Assets/generate-flags.ps1` (monitor links them) | `Language_HasAFlag` |
| Project page | `Golether.Pages/assets/languages.js` (`list`, `docs`: the site picks the Docs folder there), `assets/flags/<code>.svg`, tables in `site.js` + `join.js` | keys must equal the English `data-t` keys |
| Installers | `[Languages]`, `[Messages]`, `[CustomMessages]` in `Scripts/App/golether.iss`, `Scripts/Server/golether-server.iss` | same message count per language |
| Package READMEs | `Scripts/App/Notes`, `Scripts/Server/Notes` `README.<code>.txt`, tags `[win] [linux] [macos] [unix]` | — |
| Docs | `Docs/<En|De|Es|Fr|Pt|Ru>/` same 5 files; `ADMIN-GUIDE[.<code>].md` next to the server | manual |

After changing texts: `dotnet test --project Tests/Golether.Localization.Tests`.

## Common mistakes

- Updating only en/ru: the other four packs fail the key-parity test.
- Inline JS/CSS on the site: CSP forbids it; everything is a file in `assets/`.
- Tests expect Russian texts (`Tests/Shared/UseRussianTexts.cs`); compare with `Texts.Get(...)`, not literals.
