---
name: golether-build-test-release
description: Use when building, testing, running, packaging or releasing Golether, its rendezvous server or the project page, or when a Golether test fails unexpectedly.
---

# Golether build, test and release

## Quick reference

```bash
dotnet build Golether.slnx                                   # 0 warnings expected
dotnet test --solution Golether.slnx                         # xUnit v3 on Microsoft.Testing.Platform
dotnet test --project Tests/Golether.Sync.Tests              # one project (add --no-build after a build)
dotnet run --project Sources/Apps/Golether.UI
dotnet run --project Sources/Apps/Golether.Web.Server        # rendezvous server, 127.0.0.1:47900
```

| Need | Command / place |
|---|---|
| Native libmpv/GStreamer for a Windows debug run | `dotnet run --project Sources/Tools/Golether.Tools.Components -- fetch --output Native/win-x64 --cache artifacts/cache/components` |
| Two instances locally | VS Code profile "Golether: host + participant", invite `127.0.0.1:47800` |
| Window screenshots from headless tests | `GOLETHER_TEST_SCREENSHOTS=<folder>` |
| Windows app zip / installer | `Scripts/App/publish-win.ps1`, `Scripts/App/build-installer.ps1` (Inno Setup 6+) |
| Whole release from Windows | `Scripts/build-release.ps1` (app installer, Linux app via WSL, server installer, Linux/macOS server via Git Bash, SHA256SUMS) |
| Linux / macOS app | `Scripts/App/publish-linux.sh`, `publish-macos.sh`, or `publish-linux.ps1` (WSL); macOS app on GitHub only (Packages workflow) |
| Server packages | `Scripts/Server/publish-server-win.ps1`, `build-server-installer.ps1`, `publish-server.sh <rid>` |
| Site locally | any static server in `Sources/Apps/Golether.Pages` (CSP blocks `file://`) |

## Test facts

- Tests run with Russian texts (`Tests/Shared/UseRussianTexts.cs`); another language via `Texts.Scope(new Localizer(...))`.
- `MediaTrackTests.RealPlayer_ListsAndSelectsTracks` (real libmpv) may fail in the full parallel run and pass alone:
  rerun that project before debugging.
- GStreamer/libmpv tests skip when the component is missing; one AWG test is skipped without
  `GOLETHER_TEST_AWG_PACKAGES`.

## Release

1. Bump `<Version>` in `Directory.Build.props`.
2. Tag `git tag X.Y.Z; git push origin X.Y.Z` → Packages workflow attaches the macOS app archives (it builds nothing else).
3. Run `Scripts/build-release.ps1` locally, add its packages and `SHA256SUMS-<version>.txt` to the release, update
   `Sources/Apps/Golether.Pages/updates.json`.

## Environment gotchas (Windows dev box)

- No Python; Node, Perl, PowerShell 5.1 exist. Use Node scripts for bulk text edits.
- `core.autocrlf=true`: `.cs/.md/.json` are CRLF, `*.sh` LF. Multi-line `sed`/`perl` patterns miss CRLF files.
- Long bash heredocs and single quotes inside `node -e '...'` break; write the script to a file instead.
