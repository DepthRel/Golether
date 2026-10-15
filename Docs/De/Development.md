# Golether entwickeln

## Umgebung

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- VS Code mit C# Dev Kit und der Avalonia-Erweiterung, oder Visual Studio / Rider.
- Die nativen Komponenten (libmpv, GStreamer) bereitet das Werkzeug `golether-components` vor, siehe
  [Native/README.md](../../Native/README.md).
- Windows PowerShell 5.1 oder PowerShell 7 für `Scripts/*/*.ps1`, bash für `Scripts/*/*.sh`. Die Skripte sind nach dem
  gruppiert, was sie bauen: `Scripts/App`, `Scripts/Server`, `Scripts/Assets` (siehe `Scripts/README.md`).

## Build und Tests

```bash
dotnet build Golether.slnx
dotnet test --solution Golether.slnx                                    # alle Tests
dotnet test --project Tests/Golether.Sync.Tests/Golether.Sync.Tests.csproj  # ein Projekt
```

Die Variable `GOLETHER_TEST_SCREENSHOTS` (ein Ordner) speichert Bilder des Hauptfensters aus den Headless-Tests für eine
Sichtprüfung, ohne Fenster auf dem Bildschirm zu öffnen.

Die Variable `GOLETHER_TEST_GST_LOG` (ein Dateipfad) schaltet das ausführliche GStreamer-Protokoll in den
Konferenztests ein.

Die Variable `GOLETHER_TEST_AWG_PACKAGES` (ein Ordner mit Dateien `amneziawg-<arch>-<version>.msi` aus den
Amnezia-Releases) schaltet die Prüfung ein, die die Tunnel-Engine aus einem echten Paket entpackt. Ohne sie wird dieser
Test übersprungen, sodass ein gewöhnlicher Lauf keinen Download braucht. Das Paket lässt sich von der Adresse in
`ComponentCatalog` herunterladen; der Dateiname kommt auch von dort.

Die Tests sind xUnit v3 auf Microsoft.Testing.Platform (eingeschaltet in `global.json`). Die gemeinsamen Testpakete
werden in `Directory.Build.props` für jedes `*.Tests`-Projekt referenziert.

| Projekt | Was es prüft |
|---|---|
| Golether.Components.Tests | der Katalog, Hinweise für Linux/macOS, PE-Importe, das Installationsprogramm (Prüfsumme, Größe, Cache), Wiederverwendung von GStreamer |
| Golether.Conventions.Tests | die Regeln aus AGENTS.md, die Analysatoren nicht prüfen können, an den mit Roslyn gelesenen Quellen: ein Typ pro Datei, private verschachtelte Typen, XML-Kommentare, Aufzählungen in Enums-Ordnern, logische Operatoren am Zeilenende, die Reihenfolge der usings, Zeit nur über TimeProvider, das Schema nur über Migrationen, Projekte in ihren Projektmappenordnern |
| Golether.Media.Conference.GStreamer.Tests | zwei WebRTC-Knoten über Loopback auf Testquellen, Prüfung der Signale, Verbindungsstatistik und der Wechsel zum Spar-Kamerastrom; ohne GStreamer übersprungen |
| Golether.Core.Tests | PeerId, PlaybackState, DriftCorrector, Adressen, Datenpfade (einschließlich einer Installation „für alle Benutzer“) |
| Golether.Localization.Tests | Sprachpakete: Lesen, der Katalog, Wahl einer Sprache nach Kultur, der englische Rückfall, dieselben Schlüssel und Platzhalter in jeder Sprache, die 64 verschiedenen Wörter des Prüfcodes und eine Flagge in jeder Sprache, jeder angefragte Schlüssel existiert, kein russischer Text im Code |
| Golether.Security.Tests | Signaturen, der Schlüsselspeicher, Einladungen (Schlüssel und Weblink, Gültigkeit, die gemeinsame Einladung und die Grenze der Anfragen), die versiegelte Serveradresse, der Prüfcode, der Einlass |
| Golether.Transports.Tests | Rahmen, mTLS über Loopback, Fixierung des Schlüssels, NAT-PMP und UPnP mit einem falschen Router, Nachrichten und Signatur des Vermittlungsservers, TCP hole punching über Loopback |
| Golether.Sync.Tests | Uhren bei einer RTT von 1240 ms, Nachrichten, Authority, Follower |
| Golether.Media.Streaming.Tests | Übertragung von Stücken, der Cache, der Stream für den Player, Hashes ohne Daten, Austausch von Stücken zwischen Teilnehmern und die Prüfung gegen Hashes |
| Golether.Media.Player.Tests | der simulierte Player, die Registrierung `golether://`, Mausbelegungen, Lautstärke, Tonspuren und Untertitel, geladene Bereiche eines echten libmpv (ohne es übersprungen) |
| Golether.Tunnels.AmneziaWG.Tests | Schlüssel, Parameter, `.conf`, Pakete, der Controller |
| Golether.Core.Data.Tests | Migrationen, EF-Core-Speicher, der Migrator |
| Golether.Session.Tests | Gastgeber und Teilnehmer als Ganzes: Einlass, die Datei, der gemeinsame Start, Pause, die gemeinsame Einladung, Entfernen eines Teilnehmers und Ablehnung, eine abgelaufene Einladung |
| Golether.UI.Tests | der Tunnelablauf auf zwei Datenbanken, View-Models, Lautstärke (die eigene und die jedes Teilnehmers), Ausschalten von Geräten, Chat und Reaktionen, Spuren und Untertitel, Fortsetzen an der Stelle des Aufhörens, der Diagnosebericht, Updates, Wahl und Speicherung der Sprache, Texte auf Englisch, Beitritt per Link, die einzige Instanz, Suche des Vermittlungsservers, Entfernen eines Teilnehmers |
| Golether.Web.Server.Tests | der Vermittlungsserver: Einstellungen, Grenzen, Räume und Treffen, STUN, ein ganzes Treffen bis zu einem mTLS-Stream über die durchgeschlagene Verbindung, Ablehnungen (Signatur, ein fremder Raum, ein leerer Raum, ein schweigender Gastgeber) |
| Golether.Web.Server.UI.Tests | der Server-Monitor: das Einstellungsformular und seine Datei, das Journal und seine Suche, die Dienststeuerung von Windows, Linux und macOS, das Fenster in Avalonia Headless und die Flaggen seiner Sprachauswahl |
| Golether.UI.Headless.Tests | das Hauptfenster in Avalonia Headless: das Menü einer Teilnehmerkachel, die Reiter Chat und Ereignisse, das Lautstärke-Popup, Klicks auf das Video, die flüssige Zeitleiste mit der Ladeanzeige, Reaktionen über dem Video, die Liste der Sprachen und das Ändern jedes Textes im laufenden Betrieb, die Verbindungsart und der Block „Erweitert“ des Startbildschirms, die Fenster für Einladung und Einstellungen |

Die Tests (außer `Golether.Localization.Tests`) laufen auf Russisch: `Tests/Shared/UseRussianTexts.cs` schaltet das
russische Paket für den ganzen Lauf ein, weil die Erwartungen gegen die russischen Texte geschrieben sind. Prüfungen
einer anderen Sprache nehmen einen eigenen `Localizer` und schalten ihn über `Texts.Scope(...)` für einen Arbeitsfluss
ein, sodass parallele Tests einander nicht stören.

## Eine Sprache hinzufügen

1. Kopieren Sie `Sources/Core/Golether.Localization/Languages/en.json` nach `<code>.json` (ein zweibuchstabiger Code
   nach ISO 639-1), füllen Sie `Language.Code` und `Language.Name` (der Name der Sprache in dieser Sprache) aus und
   übersetzen Sie die Werte. Schlüssel und die Platzhalter `{0}`, `{1}` ändern sich nicht; eine Übersetzung, die einen
   Schlüssel oder einen Platzhalter verloren hat, lässt `Golether.Localization.Tests` scheitern.
   Die 64 Wörter des Prüfcodes (`Verification.Word.00` … `Verification.Word.63`) sind in jeder Sprache Übersetzungen
   derselben Dinge: Zwei Menschen mit verschiedenen Oberflächensprachen vergleichen Wörter derselben Bedeutung. Halten
   Sie sie kurz, leicht laut auszusprechen und voneinander verschieden.
2. Legen Sie eine Flagge in `Sources/Apps/Golether.UI/Assets/Flags/<code>.png` ab (120×80, Seitenverhältnis 3:2):
   Zeichnen Sie sie in `Scripts/Assets/generate-flags.ps1`, das jede Flagge zeichnet (`en`, `de`, `fr`, `es`, `pt`,
   `ru`). Der Server-Monitor zeigt dieselben Dateien. Ein Test prüft, dass jede Sprache ihre Flagge hat.
3. Das ist alles für Anwendung und Monitor: Das Paket wird automatisch übernommen, die Sprache erscheint in den Listen
   und wird beim ersten Start nach der Systemsprache gewählt.

Die Sprache der Anwendung ist nicht alles, was ein Mensch liest. Gesondert übersetzt werden:

- die Projektseite: ein weiterer Eintrag in der `list` von `Sources/Apps/Golether.Pages/assets/languages.js`, ihre
  Flagge in `assets/flags/<code>.svg` und eine weitere Tabelle in `assets/site.js` und `assets/join.js`;
- die Installationsassistenten: `[Languages]`, `[Messages]` und `[CustomMessages]` in `Scripts/App/golether.iss` und
  `Scripts/Server/golether-server.iss` (Inno Setup bringt die Standardmeldungen jeder verwendeten Sprache mit);
- die Notizen der Pakete: eine weitere Vorlage in `Scripts/App/Notes` und `Scripts/Server/Notes`
  (`README.<code>.txt`, siehe `Scripts/README.md`);
- die Dokumentation (siehe unten).

Ein neuer Text: Fügen Sie den Schlüssel in **jede** Datei `Languages/*.json` ein und holen Sie ihn mit
`Texts.Get("Key")` im Code oder `{loc:Loc Key}` in XAML. Im Code dürfen keine Zeichenketten in menschlicher Sprache
stehen (der Test `NoRussianTextIsLeftInTheCode`).

## Dokumentation

Die Dokumente liegen nach Sprachen: `Docs/En` ist der Hauptsatz, und `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt` und
`Docs/Ru` enthalten denselben Satz auf Deutsch, Spanisch, Französisch, Portugiesisch und Russisch. Die Regeln:

- die Menge der Dateien und ihre Namen sind in jeder Sprache gleich; eine Änderung der Bedeutung geht sofort in alle
  Sprachen;
- Links von außen (das README) führen nach `Docs/En`; die Projektseite öffnet das Handbuch in der dort gewählten Sprache
  und das englische, wenn es keine solche Übersetzung gibt (die Tabelle `docs` in `assets/languages.js`);
- Bilder sind gemeinsam und liegen in `Docs/images`;
- eine neue Sprache ist ein neuer Ordner `Docs/<Code>` mit demselben Satz plus eine weitere Zeile in der Tabelle `docs`
  der Seite.

Das Administratorhandbuch des Servers liegt neben dem Server, weil es Teil seiner Pakete ist:
`Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.md` ist das englische und `ADMIN-GUIDE.<code>.md` die anderen (`de`, `es`,
`fr`, `pt`, `ru`). Dieselben Regeln gelten: ein Inhalt in jeder Sprache; eine weitere Sprache ist ein weiteres
`ADMIN-GUIDE.<code>.md`, das das Projekt selbst übernimmt.

## Start aus VS Code

`.vscode/launch.json`:

| Profil | Was es tut |
|---|---|
| Golether: host | baut `Golether.UI` mit seinen Abhängigkeiten und startet es mit den Daten in `.vscode/dev/host/data` |
| Golether: participant | dasselbe mit den Daten in `.vscode/dev/guest/data` |
| Golether: host + participant | zwei unabhängige Geräte auf einem Computer; laden Sie über die Adresse `127.0.0.1` ein. Die Regel „eine einzige Instanz“ stört sie nicht: Sie gilt innerhalb eines Datenordners |
| Rendezvous server | baut und startet `Golether.Web.Server` auf `127.0.0.1:47900`; wählen Sie beim Gastgeber im Schalter „Verbindungsart“ „Eigener Server“ und geben Sie diese Adresse ein ([RendezvousServer.md](RendezvousServer.md)) |
| DbMigrator | baut und startet den Migrator mit Argumenten aus einer Eingabeaufforderung |

Die Build-Aufgaben (`.vscode/tasks.json`) rufen `dotnet build <csproj>` auf: Gebaut wird nur das gewählte Projekt und
das, wovon es abhängt. Ein vollständiger Build der Solution ist eine eigene Aufgabe, `build: solution`.

Umgebungsvariablen:

| Variable | Zweck |
|---|---|
| `GOLETHER_DATA_DIR` | der Datenordner (Datenbank, Schlüssel, Tunnel) statt `data` neben dem Programm; mit ihr werden die Daten früher Builds nicht verschoben |
| `GOLETHER_LIBMPV` | ein expliziter Pfad zu libmpv |

## Datenbank

Das Schema entsteht nur durch FluentMigrator-Migrationen. Um das Schema zu ändern:

1. Fügen Sie `Scripts/000N_Name.Up.sql` und `000N_Name.Down.sql` zu `Golether.Core.Data.Migrations.SQLite` hinzu.
2. Fügen Sie eine Klasse `Versions/M000N_Name.cs` mit `[Migration(N, "...")]` hinzu, abgeleitet von `ScriptMigration`.
3. Aktualisieren Sie die Entitäten und `GoletherDbContext` (EF Core bildet das Modell nur auf die Tabellen ab).

Die Anwendung wendet Migrationen beim Start an. Von Hand:

```bash
dotnet run --project Sources/Database/Golether.Dbs.SQLite.DbMigrator -- --database path/golether.db [--list | --down 0]
```

## Code-Regeln

Siehe [AGENTS.md](../../AGENTS.md) (auf Englisch): ein öffentlicher Typ pro Datei, ein XML-Kommentar an jedem Member
(CS1591 ist ein Fehler), geschweifte Klammern um jeden Block, Leerzeilen zwischen den logischen Schritten einer Methode,
logische Operatoren am Zeilenende, Aufzählungen in `Enums`-Ordnern, `using`-Direktiven in den Blöcken System,
Bibliotheken, Golether, nur GPL-kompatible Abhängigkeiten, Zeit über `TimeProvider`/`IMonotonicClock`,
nicht vertrauenswürdige Eingaben werden immer geprüft.

## Pakete

| Skript | Ergebnis |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/Golether-<version>-win-x64.zip` |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `.tar.gz` und `.zip` (wenn `zip` vorhanden ist), `install-desktop-entry.sh`, `.deb` (wo `dpkg-deb` vorhanden ist: Debian, Ubuntu, WSL) |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `Golether.app` (Info.plist, Symbol, Ad-hoc-Signatur) in einem Archiv |
| `Scripts/App/publish-linux.ps1 [-Runtime linux-x64]` | dasselbe Linux-Archiv, unter Windows in WSL gebaut (siehe unten) |
| `Scripts/Assets/update-site-screenshot.ps1` | das Bild der Projektseite aus `Docs/images/screenshot.png` |
| `Scripts/Assets/generate-installer-art.ps1` | die Bilder des Installationsassistenten (eine dunkle Fläche mit dem Logo und das Logo für die Kopfzeile) in `artifacts/installer-art`; beide Build-Skripte der Installationsprogramme rufen es selbst auf |
| `Scripts/Assets/generate-icon.ps1` | `golether.ico`, PNG 256/512 aus der Geometrie von `golether.svg` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` (das Archiv baut derselbe Lauf) | `artifacts/publish/Golether-<version>-setup.exe` (braucht Inno Setup 6 oder neuer; geprüft mit 7.1) und einen fertigen Eintrag für die Update-Datei |
| `Scripts/build-release.ps1 [-Skip <Schritte>] [-ContinueOnError]` | alles, was für ein Release unter Windows gebaut wird, nacheinander, und `artifacts/publish/SHA256SUMS-<version>.txt` (siehe unten) |

Der Build ist eigenständig; der Zielrechner braucht kein .NET. Der Inhalt von `Native/<RID>/` kommt nach `app/native`.
Die Wurzel des Windows-Pakets enthält `Golether.exe` mit dem Anwendungssymbol: Es ist derselbe .NET-Host (das Target
`GoletherRootLauncher` in `Golether.UI.csproj`, die SDK-Aufgabe `CreateAppHost`), nur mit dem relativen Pfad
`app\Golether.dll`. So startet das Paket per Doppelklick von überall, und `Golether.exe` kann eine Verknüpfung bekommen
oder an die Taskleiste angeheftet werden. Die Linux- und macOS-Pakete werden auf dem Zielsystem gebaut: Nur dort
überleben die Ausführungsrechte im Archiv.

So baut man sie ohne einen Linux-Rechner oder einen Mac zur Hand:

- **Linux unter Windows.** `./Scripts/App/publish-linux.ps1` führt den Build in WSL aus: Die Quellen werden in das
  Linux-Dateisystem kopiert und dort gebaut, und das fertige `Golether-<version>-linux-x64.tar.gz` kommt zurück nach
  `artifacts/publish`. Der erste Lauf installiert das .NET SDK in `~/.dotnet` der Distribution (mit dem offiziellen
  Skript, ohne `sudo`).
- **Das ganze Release unter Windows.** `./Scripts/build-release.ps1` führt `App/build-installer.ps1`,
  `App/publish-linux.ps1`, `Server/build-server-installer.ps1`, `Server/publish-server-linux.ps1` und
  `Server/publish-server-macos.ps1` nacheinander aus, jedes in einem eigenen PowerShell-Prozess. Der erste Fehler hält
  es an; `-ContinueOnError` führt den Rest aus, `-Skip AppLinux,ServerMacOS` lässt Schritte weg. Am Ende listet es die
  Pakete der Version mit Größe und SHA-256 auf und schreibt `artifacts/publish/SHA256SUMS-<version>.txt` für das
  Release. Der Rechner braucht Inno Setup, WSL und Git für Windows; jeder Schritt sagt, was fehlt.
- **macOS auf GitHub.** Der Workflow **Packages** (`.github/workflows/packages.yml`) baut nur die macOS-Pakete der
  Anwendung, für Apple Silicon (`osx-arm64`) und Intel (`osx-x64`): Sie brauchen `sips`, `iconutil` und
  `codesign`, die es nur unter macOS gibt. Ein Lauf von Hand (Actions → Packages → Run workflow) liefert die Archive
  als Artefakte des Laufs. Ein Lauf durch ein Versions-Tag (`git tag 0.3.0; git push origin 0.3.0`) hängt sie außerdem
  an das Release dieses Tags; gibt es kein Release, wird ein Entwurf erstellt. Der Rest des Releases kommt aus
  `build-release.ps1` und wird zum selben Release hinzugefügt; der Workflow ersetzt nur die Dateien mit den Namen
  seiner eigenen Archive.

## Netzwerk debuggen

- Zwei Instanzen auf einem Rechner: das Profil „Golether: host + participant“, mit der Adresse `127.0.0.1:47800` in der
  Einladung.
- Großer Ping und Verluste: unter Windows [clumsy](https://jagt.github.io/clumsy/), unter Linux
  `tc qdisc add dev lo root netem delay 600ms loss 2%`.
- Ohne libmpv wird der simulierte Player verwendet: Die Position folgt der Uhr, sodass sich die Synchronisation ohne
  Video prüfen lässt.
