# Golether: development rules

Golether is a free (GPL-3.0-or-later) cross-platform desktop application for watching a video together, in sync, with
webcams and voice. Design documentation: [Docs/En/Architecture.md](Docs/En/Architecture.md).

## General rules

- The latest .NET and C# (currently .NET 10, `LangVersion=latest`); shared settings live in `Directory.Build.props`,
  package versions centrally in `Directory.Packages.props`.
- Only free dependencies compatible with GPL-3.0-or-later. Do not add packages without a need.
- Domain logic does not depend on the UI, hosting or infrastructure. Interfaces, dependency injection, small services.
- External commands, paths, configurations and everything that came over the network are untrusted data: validate
  them and fail with a clear diagnostic.
- Security-related operations are explicit, bounded and verifiable.
- Time comes from `TimeProvider` or `IMonotonicClock`, so tests are deterministic.
- Critical logic is covered by unit and integration tests; external dependencies (processes, files, network, database,
  player) are replaced with stubs.

## Code style

- **One type per file.** Every class, struct, enum, record, interface or delegate that is not `private` lives in a file
  of its own named after it. Only `private` nested types may share a file with their owner.
- **Enumerations live in `Enums` folders.** An enum shared by several projects belongs to the shared core:
  `Golether.Core.Data/Enums`, or `Golether.Core/Enums` when `Golether.Core` itself or a project that does not reference
  `Golether.Core.Data` needs it. An enum that only one project needs (a way of showing something in the UI, a code of
  a native library) lives in the `Enums` folder at the root of that project, with the narrowest access that works as
  is: `internal` when nothing public exposes it, otherwise `public`. Access is never bent around the enum. A `private`
  nested enum stays inside its owner.
- **Comments.** Every type, method, property, field, constructor and data structure carries a short but meaningful
  `///` comment: what it is for, not how it is written. Private members and tests included; a missing comment on a
  public member is a build error (CS1591).
- **Design.** Follow SOLID, KISS, DRY and YAGNI: small classes with one responsibility, dependencies through
  interfaces, no copy-paste, no code for imagined needs.
- **Readable methods.** Separate the logical steps of a method with blank lines (checks, preparation, the work, the
  result); never write a whole method as one dense block.
- **Naming.**
  - Constants of any access, local ones too, are `UPPER_SNAKE_CASE`: `const int MAX_PAYLOAD = 1200;`.
  - Private and protected fields, `static readonly` ones too, are `_camelCase`: `_retryDelay`.
  - Public fields, types, methods, properties, events and enum values are PascalCase.
  - Locals and parameters of a method or constructor are `camelCase`, without an underscore.
- **Braces.** Every construct gets a block in braces, even a single line:

  ```csharp
  if (ready)
  {
      return true;
  }
  ```

  never `if (ready) return true;` or a body on the next line without braces.
- **Indentation.** Four spaces, never tabs (two spaces in XML, JSON and YAML files, see `.editorconfig`).
- **Logical operators** stay at the end of a line; a continuation line never starts with `&&` or `||`. This applies to
  LINQ lambdas and to any other expression:

  ```csharp
  var ready = statuses.Where(s => s.IsReady &&
      s.Buffered >= threshold);
  ```
- File-scoped namespaces.
- **`using` directives.** No unused ones. They go in three blocks, one right after another without blank lines:
  `System` and `System.*`, then the libraries (`Avalonia`, `Microsoft`, `NSubstitute`, …), then the solution
  (`Golether.*`); each block is in alphabetical order. `using static` and aliases come after the blocks:

  ```csharp
  using System.Globalization;
  using Microsoft.Extensions.Logging;
  using Golether.Core.Enums;
  using Golether.Localization;
  ```

The rules are checked, not only written down. The build fails on a missing XML comment of a public member (CS1591),
on braces, block namespaces, naming and unused usings (analyzers of `.editorconfig`, run by `EnforceCodeStyleInBuild`).
`Tests/Golether.Conventions.Tests` parses the sources with Roslyn and checks the rest: one type per file, XML comments
of every member, enums in `Enums`, logical operators, the order of usings, time only through `TimeProvider`, the schema
only through migrations and projects in their solution folders.

## Structure

```
Sources/
  Apps/          applications (Golether.UI, the rendezvous server Golether.Web.Server, its monitor
                 Golether.Web.Server.UI and their shared contracts Golether.Web.Server.Contracts,
                 the project page Golether.Pages)
  Components/    native components: catalog, checksums, installation
  Core/          core, data and migrations, localization
  Database/      console database migrator
  Media/         player (libmpv), file serving, cameras and voice
  Security/      device keys, invitations, admission, verification code
  Session/       host and participant sessions
  Sync/          protocol and playback synchronization
  Tools/         the golether-components utility for package builds
  Transports/    network transports
  Tunnels/       AmneziaWG tunnels
Tests/           test projects (<Project>.Tests)
Docs/            documentation by language: En (the main set), De, Es, Fr, Pt, Ru (the same set translated), images
Scripts/         packaging: App (the application), Server (the server and its monitor), Assets (pictures),
                 Shared (helpers of the others); build-release.ps1 builds a whole release; see Scripts/README.md
```

A new project is added to `Golether.slnx` in the solution folder that repeats its path on disk.

## Localization

- Everything a user reads (labels, error and exception messages, session events, reports) lives in
  `Sources/Core/Golether.Localization/Languages/<code>.json` and is taken by key: `Texts.Get("Key")` /
  `Texts.Format("Key", …)` in code, `{loc:Loc Key}` in XAML. There are no human-language strings in code or markup and
  no "if the language is …" branches: a language is the chosen pack (a strategy), not a condition.
- A key is added to every language file at once; English is the default language. A key that depends on an enumeration
  is built from the name of the value (`"Session.Reject." + reason`), and a test checks every value.
- The languages are English, German, Spanish, French, Portuguese and Russian. The language is chosen once from the
  language of the OS (English when there is no such pack), kept in the `ui.language` setting and changed by the user
  on the fly. Reasons and codes travel over the network, not texts: the reader words them in their own language. The
  words of the verification code are translations of the same 64 things in every language.
- Every language has a flag (`Scripts/Assets/generate-flags.ps1`), a table on the project page
  (`Sources/Apps/Golether.Pages/assets/*.js`), messages in both setup wizards (`Scripts/*/*.iss`) and package notes
  (`Scripts/*/Notes/README.<code>.txt`).
- Details and adding a language: [Docs/En/Development.md](Docs/En/Development.md).
- The documentation is kept in every language at once: the set of files in `Docs/En`, `Docs/De`, `Docs/Es`,
  `Docs/Fr`, `Docs/Pt` and `Docs/Ru` is the same, and a change of meaning goes into all of them, together with
  `ADMIN-GUIDE.<code>.md` of the server. Links from outside lead to `Docs/En`.

## Databases

SQLite, the schema only through FluentMigrator migrations (SQL scripts in
`Golether.Core.Data.Migrations.SQLite/Scripts`), data access through EF Core. `EnsureCreated` and EF Core migrations
are not used.
