# Golether-Vermittlungsserver: Administratorhandbuch

Dieses Handbuch liegt im Ordner `docs` jedes Serverpakets und passt für Windows, Linux und macOS gleichermaßen. Wo sich
die Schritte unterscheiden, sind sie für jedes System einzeln angegeben. Dasselbe Handbuch in anderen Sprachen:
[English](ADMIN-GUIDE.md), [Español](ADMIN-GUIDE.es.md), [Français](ADMIN-GUIDE.fr.md), [Português](ADMIN-GUIDE.pt.md),
[Русский](ADMIN-GUIDE.ru.md).

- [1. Was der Server tut](#1-was-der-server-tut)
- [2. Was Sie brauchen](#2-was-sie-brauchen)
- [3. Was im Paket ist](#3-was-im-paket-ist)
- [4. Installation](#4-installation)
- [5. Einstellungen](#5-einstellungen)
- [6. Router und Firewall](#6-router-und-firewall)
- [7. Was Sie den Gastgebern sagen](#7-was-sie-den-gastgebern-sagen)
- [8. Den Dienst verwalten](#8-den-dienst-verwalten)
- [9. Prüfen, ob er funktioniert](#9-prüfen-ob-er-funktioniert)
- [10. Aktualisieren](#10-aktualisieren)
- [11. Entfernen](#11-entfernen)
- [12. Sicherheit](#12-sicherheit)
- [13. Fehlersuche](#13-fehlersuche)

## 1. Was der Server tut

Golether ist ein Programm, um gemeinsam Videos anzusehen. Meist sitzen Gastgeber und Teilnehmer hinter Heimroutern ohne
offene Ports (eine „graue“ IP, das NAT des Anbieters) und können einander nicht direkt erreichen. Der
Vermittlungsserver stellt sie einander vor:

- der Gastgeber hält einen „Raum“ seiner Sitzung auf dem Server;
- ein Teilnehmer, der über den Einladungslink kommt, bittet den Server, ihn mit dem Gastgeber zusammenzubringen;
- der Server nennt beiden die öffentlichen Adressen des jeweils anderen, und sie schlagen selbst eine **direkte**
  Verbindung durch ihre Router;
- auf demselben Port, aber über UDP, beantwortet der Server STUN-Anfragen: So verbinden sich auch Kameras und Sprache
  direkt.

Film, Synchronisation, Chat, Kameras und Sprache gehen **nicht** über den Server; nur kurze Dienstnachrichten beim
Verbindungsaufbau. Die Last ist minimal; ein Server bedient Hunderte gleichzeitiger Sitzungen.

## 2. Was Sie brauchen

| | |
|---|---|
| Netzwerk | eine öffentliche IPv4-Adresse auf dem Rechner oder auf einem Router, an dem sich ein Port weiterleiten lässt |
| Ports | **TCP 47900** und **UDP 47900** von außen (die Nummer ist einstellbar); Port 47980 ist nur lokal |
| Windows | Windows 10 / Windows Server 2016 oder neuer, x64 oder arm64 |
| Linux | x64 oder arm64 mit glibc (Debian 11+, Ubuntu 20.04+, RHEL/Alma/Rocky 8+ und ähnliche) und systemd für den Betrieb als Dienst |
| macOS | macOS 14 oder neuer, Apple Silicon oder Intel |
| Ressourcen | etwa 100 MB Festplatte und 60–100 MB Arbeitsspeicher |

.NET muss nicht installiert werden: Es steckt im Paket.

## 3. Was im Paket ist

| Datei | Zweck |
|---|---|
| `golether-server` (`golether-server.exe` unter Windows) | startet den Server; die einzige Datei in der Wurzel; ein Programm läuft sowohl in einer Konsole als auch als Dienst |
| `bin/` | das Programm selbst, die .NET-Laufzeit und die Bibliotheken; dort muss man nicht hineinsehen |
| `config/appsettings.json` | die Einstellungen: Adresse, Port, Grenzen (siehe [Abschnitt 5](#5-einstellungen)) |
| `config/monitor.json` | die Einstellungen des Monitors selbst (seine Sprache); sie berühren den Server nie |
| `docs/ADMIN-GUIDE.md`, `docs/ADMIN-GUIDE.<code>.md` | dieses Handbuch auf Englisch und auf Deutsch (`de`), Spanisch (`es`), Französisch (`fr`), Portugiesisch (`pt`) und Russisch (`ru`) |
| `docs/README.txt`, `docs/README.<code>.txt` | eine kurze Notiz zu genau diesem Paket, in denselben Sprachen |
| `service/install-service.sh` | Linux und macOS: installiert, aktualisiert und entfernt den Dienst |
| `service/golether-server.service` | Linux: die systemd-Unit (`install-service.sh` installiert sie) |
| `service/org.golether.server.plist` | macOS: der launchd-Job (`install-service.sh` installiert ihn) |
| `golether-server-monitor` (`golether-server-monitor.exe` unter Windows) | startet Golether Server Monitor, neben dem Server; der Monitor selbst liegt in `monitor/`. Der Monitor: der Zustand des Servers und sein Ereignisjournal in einem Fenster, Starten und Stoppen des Dienstes, die Einstellungen (siehe [Abschnitt 9](#9-prüfen-ob-er-funktioniert)) |

```
GoletherServer/
  golether-server(.exe)
  golether-server-monitor(.exe)
  bin/        das Programm und die Bibliotheken
  config/     appsettings.json, monitor.json
  docs/       ADMIN-GUIDE.md, ADMIN-GUIDE.<code>.md, README.txt, README.<code>.txt
  service/    nur Linux und macOS
  monitor/    Golether Server Monitor (die Dateien des Programms)
```

Das Windows-Installationsprogramm legt die Dateien genauso ab; seine eigenen Deinstallationsdateien liegen im Ordner
`uninstall`.

Für Windows gibt es außerdem ein eigenes Installationsprogramm, `GoletherServer-<version>-setup.exe`.

## 4. Installation

Wählen Sie den Weg für Ihr System. In jedem Fall müssen die Ports nach der Installation geöffnet werden
([Abschnitt 6](#6-router-und-firewall)).

### Windows: das Installationsprogramm (empfohlen)

Starten Sie `GoletherServer-<version>-setup.exe` (Administratorrechte sind nötig). Der Assistent spricht Englisch,
Deutsch, Spanisch, Französisch, Portugiesisch und Russisch; sein erstes Fenster bietet die Sprache von Windows an
(Englisch, wenn der Assistent sie nicht spricht), und Sie können eine andere wählen. Das Installationsprogramm:

- legt den Server nach `C:\Program Files\Golether Server`;
- registriert den Dienst **GoletherServer** („Golether rendezvous server“): Er startet mit dem Computer und startet
  5 Sekunden nach einem Fehler neu;
- fügt eine Windows-Firewallregel für das Programm hinzu (TCP und UDP, jeder Port);
- legt Verknüpfungen zu den Einstellungen, diesem Handbuch und der Statusseite unter „Start → Golether Server“ an.

Dienst und Firewallregel lassen sich mit den Häkchen im Schritt „Zusätzliche Aufgaben auswählen“ abschalten.

### Windows: aus dem Zip-Archiv

Entpacken Sie das Archiv, zum Beispiel nach `C:\GoletherServer`, und starten Sie `golether-server.exe` aus der Wurzel
des Ordners: Der Server arbeitet, solange das Konsolenfenster offen ist. Um ihn als Dienst zu betreiben, führen Sie in
PowerShell **als Administrator** in der Wurzel des Serverordners aus:

```powershell
$exe = Join-Path (Get-Location) 'golether-server.exe'
New-Service -Name GoletherServer -DisplayName 'Golether rendezvous server' -BinaryPathName "`"$exe`"" -StartupType Automatic
sc.exe failure GoletherServer reset= 86400 actions= restart/5000/restart/5000/restart/60000
New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program $exe -Action Allow
Start-Service GoletherServer
```

Verschieben Sie den Ordner danach nicht: Der Dienst startet das Programm über seinen vollständigen Pfad.

### Linux: ein systemd-Dienst

```bash
tar -xzf GoletherServer-<version>-linux-x64.tar.gz
cd GoletherServer
sudo ./service/install-service.sh
```

Das Skript kopiert den Server nach `/opt/golether-server`, installiert die Unit `golether-server.service`, aktiviert sie
beim Hochfahren und startet den Dienst. Mit aktiviertem `ufw` öffnet das Skript den Port aus den Einstellungen selbst;
für `firewalld` und `iptables` siehe [Abschnitt 6](#6-router-und-firewall). Der Dienst läuft als temporärer Benutzer
ohne Rechte (`DynamicUser`) mit Schutz des Dateisystems; der Server braucht keine Root-Rechte.

Ohne Dienst: `./golether-server` in der Wurzel des Paketordners (`Strg+C` beendet ihn).

### macOS: ein launchd-Dienst

```bash
tar -xzf GoletherServer-<version>-osx-arm64.tar.gz     # osx-x64 für Intel
cd GoletherServer
sudo ./service/install-service.sh
```

Das Skript kopiert den Server nach `/usr/local/golether-server`, entfernt die Quarantäne heruntergeladener Dateien,
signiert das Programm mit einer lokalen Signatur (macOS auf Apple Silicon verlangt eine), installiert den Job
`/Library/LaunchDaemons/org.golether.server.plist` und startet ihn. Der Server läuft als Benutzer `nobody` und startet
nach einem Fehler neu.

Ohne Dienst: `./golether-server` in der Wurzel des Paketordners. Sagt macOS, dass das Programm nicht geöffnet werden
kann, führen Sie dort `xattr -dr com.apple.quarantine .` und `codesign --force --sign - golether-server bin/golether-server`
aus. Beim ersten Start fragt die Firewall von macOS (wenn sie eingeschaltet ist), ob eingehende Verbindungen erlaubt
werden sollen: Erlauben Sie sie.

## 5. Einstellungen

Alle Einstellungen stehen in `config/appsettings.json` im Serverordner:

| System | Wo die Datei nach der Installation als Dienst liegt |
|---|---|
| Windows | `C:\Program Files\Golether Server\config\appsettings.json` |
| Linux | `/opt/golether-server/config/appsettings.json` |
| macOS | `/usr/local/golether-server/config/appsettings.json` |

Fehlt die Datei (zum Beispiel versehentlich gelöscht), scheitert der Server nicht: Er schreibt sie mit den unten
gezeigten Standardeinstellungen neu und sagt das in seinem Protokoll. Lässt sich der Ordner nicht beschreiben, startet
der Server trotzdem mit denselben Standardwerten. Ein Schlüssel, der in der Datei fehlt, bekommt ebenfalls seinen
Standardwert.

```json
{
  "Rendezvous": {
    "ListenAddress": "0.0.0.0",
    "Port": 47900,
    "EnableStun": true
  },
  "Urls": "http://127.0.0.1:47980"
}
```

| Parameter `Rendezvous:…` | Standard | Was er festlegt |
|---|---|---|
| `ListenAddress` | `0.0.0.0` | die IP-Adresse **dieses Rechners**, auf der gelauscht wird: `0.0.0.0` ist jede IPv4-Schnittstelle, eine bestimmte Adresse nur diese, `::` ist IPv6 und IPv4 |
| `Port` | `47900` | der Port: TCP für Treffen und dieselbe Nummer über UDP für STUN |
| `EnableStun` | `true` | ob STUN über UDP beantwortet wird; ohne verbinden sich Kameras und Sprache aus verschiedenen Netzen schlechter |
| `MaxConnections` | `1024` | gleichzeitig offene Verbindungen |
| `MaxRooms` | `1024` | gleichzeitig laufende Sitzungen |
| `MaxPendingPerRoom` | `16` | Verbindungen, die auf die Antwort eines Gastgebers warten |
| `MaxConnectionsPerMinute` | `120` | neue Verbindungen von einer IP pro Minute |
| `MaxStunPerMinute` | `600` | STUN-Anfragen von einer IP pro Minute |
| `FirstMessageTimeout` | `00:00:10` | wie lange nach einer Verbindung auf die erste Nachricht gewartet wird |
| `AnswerTimeout` | `00:00:20` | wie lange ein Gastgeber für die Antwort auf die Verbindung eines Teilnehmers brauchen darf |
| `IdleTimeout` | `00:01:30` | wie lange ein Gastgeber schweigen darf, bevor sein Raum freigegeben wird |

| Parameter | Standard | Was er festlegt |
|---|---|---|
| `Urls` | `http://127.0.0.1:47980` | die Adresse der Statusseite `/health`; behalten Sie `127.0.0.1`, damit sie nur auf diesem Rechner sichtbar ist |
| `Logging:LogLevel:Default` | `Information` | wie ausführlich das Protokoll ist; `Debug` protokolliert jede Verbindung |
| `Logging:Console:FormatterOptions:TimestampFormat` | `yyyy-MM-dd HH:mm:ss.fff zzz ` | der Stempel am Anfang jeder Protokollzeile in der Konsole und in der Datei: das Datum, die Serverzeit und der Versatz zu UTC (`2026-10-11 16:14:12.250 +07:00`). Unter systemd und in der Windows-Ereignisanzeige setzt das System die Zeit eines Eintrags selbst |

Jeder Parameter lässt sich auch ohne Bearbeiten der Datei setzen, mit einer Umgebungsvariablen (doppelter Unterstrich
statt des Doppelpunkts) oder einem Kommandozeilenargument; sie überschreiben die Datei:

```bash
./golether-server --Rendezvous:Port=3478                      # Argument, jedes System
Rendezvous__Port=3478 ./golether-server                        # Variable, Linux und macOS
```

```powershell
$env:Rendezvous__Port = '3478'; .\golether-server.exe          # Variable, Windows
```

**Starten Sie den Dienst nach dem Bearbeiten von `config/appsettings.json` neu** ([Abschnitt 8](#8-den-dienst-verwalten)).
Ist eine Einstellung falsch, startet der Server nicht und listet die Fehler auf (in der Konsole oder im
Dienstprotokoll).

## 6. Router und Firewall

**Beide** Protokolle müssen am Serverport von außen erreichbar sein.

**Router.** Gehört die öffentliche IP dem Router und steht der Server dahinter, leiten Sie an die lokale Adresse des
Servers weiter:

| Externer Port | Protokoll | Wohin |
|---|---|---|
| 47900 | TCP | `<lokale IP des Servers>:47900` |
| 47900 | UDP | `<lokale IP des Servers>:47900` |

Leiten Sie Port 47980 nicht nach außen weiter. Für Cloud-Server (VPS) öffnen Sie dieselben Ports in der
Sicherheitsgruppe oder Firewall des Anbieterpanels.

**System-Firewall:**

| System | Befehle |
|---|---|
| Windows | das Installationsprogramm tut es; von Hand: `New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program '<Serverordner>\golether-server.exe' -Action Allow` |
| Linux, ufw | `sudo ufw allow 47900/tcp && sudo ufw allow 47900/udp` (mit aktiviertem ufw tut es `install-service.sh`) |
| Linux, firewalld | `sudo firewall-cmd --permanent --add-port=47900/tcp --add-port=47900/udp && sudo firewall-cmd --reload` |
| Linux, iptables | `sudo iptables -A INPUT -p tcp --dport 47900 -j ACCEPT && sudo iptables -A INPUT -p udp --dport 47900 -j ACCEPT` |
| macOS | „Systemeinstellungen → Netzwerk → Firewall → Optionen“: eingehende Verbindungen für `golether-server` erlauben (wenn die Firewall eingeschaltet ist) |

## 7. Was Sie den Gastgebern sagen

Gastgeber wählen in Golether im Schalter „Verbindungsart“ der Karte „Einen Film zeigen“ **„Eigener Server“** und geben
die Adresse des Servers in das erscheinende Feld ein, als `Adresse:Port`. Den Teilnehmern muss man nichts sagen: Die
Adresse erreicht sie in der Einladung.

(Die Stellung „Automatisch“ dieses Schalters sucht den Server des Golether-Projekts selbst, nicht Ihren: Ein Gastgeber,
der Ihren Server nutzen will, muss „Eigener Server“ wählen.)

Geben Sie die Adresse an, unter der der Server **aus dem Internet** gesehen wird, nicht `ListenAddress`:

| Wo der Server steht | Was Gastgeber eingeben |
|---|---|
| Ein VPS oder ein Rechner mit öffentlicher IP auf der Schnittstelle | diese IP (oder ein Domainname): `203.0.113.7:47900` |
| Zu Hause hinter einem Router mit Weiterleitung | die externe IP des Routers, **nicht** `192.168.x.x`: `198.51.100.20:47900` |
| Der Port wurde geändert | der neue Port: `203.0.113.7:3478` |

Ändert sich die externe IP des Routers, besorgen Sie dafür einen dynamischen DNS-Namen und geben Sie diesen an.

## 8. Den Dienst verwalten

| Aktion | Windows (PowerShell als Administrator) | Linux | macOS |
|---|---|---|---|
| Zustand | `Get-Service GoletherServer` | `systemctl status golether-server` | `sudo launchctl print system/org.golether.server` |
| Starten | `Start-Service GoletherServer` | `sudo systemctl start golether-server` | `sudo launchctl kickstart system/org.golether.server` |
| Stoppen | `Stop-Service GoletherServer` | `sudo systemctl stop golether-server` | `sudo launchctl bootout system/org.golether.server` |
| Neu starten | `Restart-Service GoletherServer` | `sudo systemctl restart golether-server` | `sudo launchctl kickstart -k system/org.golether.server` |
| Protokoll | „Ereignisanzeige → Windows-Protokolle → Anwendung“, Quelle `golether-server` | `journalctl -u golether-server -f` | `tail -f /Library/Logs/golether-server.log` |

Nach einem Stopp mit `bootout` unter macOS wird der Dienst mit
`sudo launchctl bootstrap system /Library/LaunchDaemons/org.golether.server.plist` (oder durch einen Neustart) wieder
gestartet.

Dasselbe unter Windows in der klassischen Kommandozeile (`cmd` als Administrator):

| Aktion | Befehl |
|---|---|
| Zustand | `sc query GoletherServer` |
| Starten | `sc start GoletherServer` |
| Stoppen | `sc stop GoletherServer` |

Das Programm `Golether Server Monitor` erledigt all das mit Schaltflächen und zeigt, was der Server tut; siehe
[Abschnitt 9](#9-prüfen-ob-er-funktioniert).

**Der Dienst kommt von selbst zurück.** Er startet mit dem Computer, und das Installationsprogramm hat Windows gesagt,
ihn 5 Sekunden nach einem Fehler neu zu starten. Das Beenden des Prozesses im Task-Manager zählt als Fehler, sodass der
Server eine Minute später wieder läuft. Stoppen Sie ihn stattdessen als Dienst (die Befehle oben oder `services.msc`).

**`golether-server.exe`, aus einer Konsole gestartet, meldet, dass der Port belegt ist.** Meist belegt ihn der Dienst
selbst, der bereits läuft. Eine zweite Kopie ist nicht nötig: Der Dienst tut dieselbe Arbeit. Um den Server in einer
Konsole laufen zu lassen (zum Beispiel, um seine Meldungen zu sehen), stoppen Sie zuerst den Dienst und starten ihn
danach wieder.

## 9. Prüfen, ob er funktioniert

### Golether Server Monitor

Am einfachsten ist der Monitor: „Golether Server Monitor“ im Startmenü unter Windows, `golether-server-monitor` neben
`golether-server` im Serverordner unter Linux (ein Desktop ist nötig) und macOS. Er zeigt:

- **den Zustand** in Worten: läuft als Dienst, läuft von Hand (nicht als Dienst), gestoppt, der Dienst ist nicht
  installiert; wie lange der Server läuft, die Räume, die Verbindungen, die Treffen und die Ablehnungen;
- **das Ereignisjournal** in Spalten: Zeit | IP-Adresse | Ereignis | Details (der Gastgeber, eine Raummarke, warum eine
  Anfrage abgelehnt wurde). Die neuesten Ereignisse stehen oben; das Suchfeld findet eine Adresse, einen Gastgeber,
  einen Raum oder ein Ereignis; die Trenner der Kopfzeile ändern die Breite der Spalten; ein Rechtsklick kopiert die
  markierten Zeilen;
- die Schaltflächen **Starten** und **Stoppen** des Dienstes und **Journal exportieren** in eine Datei;
- den Reiter **Einstellungen**: die Felder von `config/appsettings.json` mit Erklärungen, geprüft nach denselben Regeln
  wie beim Start des Servers. „Speichern und neu starten“ schreibt die Datei und startet einen laufenden Dienst neu.

Die Sprache des Monitors (English, Deutsch, Español, Français, Português oder Русский, jede mit ihrer Flagge) wird in
seinem Fenster gewählt und ändert sich sofort. Sie wird in `config/monitor.json` neben den Einstellungen des Servers
gespeichert (eine eigene Datei des Monitors: Eine Änderung startet den Server nie neu), und jeder Benutzer darf sie
ändern. Beim ersten Start spricht der Monitor die Sprache des Systems (Englisch, wenn er diese nicht spricht).

Starten, Stoppen und Speichern brauchen Administratorrechte: Windows fragt über UAC, Linux über `pkexec`, macOS nach dem
Passwort. Das Ansehen braucht keine besonderen Rechte.

Das Journal wird im Speicher des Servers gehalten (die letzten 5000 Ereignisse) und beginnt bei jedem Start des Servers
leer. Nichts davon wird auf die Festplatte geschrieben, außer Sie drücken **Journal exportieren**: Dann kommt genau
das, was der Monitor zeigt, in die gewählte Datei. Der Monitor liest das Journal über die Statusadresse dieses
Computers; andere Computer können es nicht lesen.

### Von Hand

**Auf dem Rechner selbst**, die Statusseite:

```bash
curl http://127.0.0.1:47980/health                               # Linux, macOS
```

```powershell
Invoke-RestMethod http://127.0.0.1:47980/health                   # Windows
```

```json
{ "status": "ok", "version": "0.3.0", "protocol": 1, "uptimeSeconds": 3600,
  "rooms": 2, "connections": 3, "meetingsRequested": 14, "meetingsIntroduced": 13,
  "refused": 0, "stunAnswered": 211 }
```

| Feld | Bedeutung |
|---|---|
| `rooms` | Sitzungen, die mit diesem Server laufen |
| `connections` | offene Verbindungen |
| `meetingsRequested` / `meetingsIntroduced` | um wie viele Verbindungen Teilnehmer gebeten haben / wie vielen Seiten der Server die Adressen des jeweils anderen weitergegeben hat |
| `refused` | Ablehnungen wegen der Grenzen |
| `stunAnswered` | STUN-Antworten (wächst, solange Sitzungen mit Kameras laufen) |

**Von außen**, von einem anderen Rechner in einem anderen Netz (zum Beispiel ein Laptop am Handy-Hotspot):

```bash
nc -vz 203.0.113.7 47900                                         # Linux, macOS: ist der TCP-Port offen?
```

```powershell
Test-NetConnection 203.0.113.7 -Port 47900                        # Windows: TcpTestSucceeded : True
```

UDP prüft man mit einem STUN-Client, zum Beispiel `stunclient 203.0.113.7 47900` (das Paket `stuntman-client` unter
Linux). Die abschließende Prüfung: Ein Gastgeber trägt den Server ein und erstellt eine Sitzung, und die Ereignisliste
von Golether zeigt „Der Vermittlungsserver ist bereit“.

## 10. Aktualisieren

| System | Wie man aktualisiert |
|---|---|
| Windows | das Installationsprogramm der neuen Version starten: Es stoppt den Dienst, ersetzt die Dateien und startet ihn wieder; `config\appsettings.json` bleibt erhalten |
| Windows, Zip | den Dienst stoppen, alles außer dem Ordner `config` ersetzen, den Dienst starten |
| Linux, macOS | die neue Version entpacken und `sudo ./service/install-service.sh` erneut ausführen; `config/appsettings.json` bleibt erhalten |

Verschiedene Versionen von Server und Anwendung sind kompatibel, solange die Protokollversion dieselbe ist (das Feld
`protocol` von `/health`).

## 11. Entfernen

| System | Wie man entfernt |
|---|---|
| Windows | „Einstellungen → Apps → Golether Server → Deinstallieren“: Dienst und Firewallregel werden entfernt |
| Windows, Zip | `Stop-Service GoletherServer; sc.exe delete GoletherServer; Remove-NetFirewallRule -DisplayName 'Golether rendezvous server'`, dann den Ordner löschen |
| Linux, macOS | `sudo ./service/install-service.sh uninstall` aus dem Paketordner oder aus dem installierten Ordner |

Vergessen Sie nicht, die Portweiterleitung am Router zu schließen.

## 12. Sicherheit

- **Der Server sieht keine Sitzungen.** Alles, was zwischen Gastgeber und Teilnehmern läuft, ist zwischen ihren Geräten
  verschlüsselt; der Server kennt nur IP-Adressen, die öffentlichen Schlüssel der Gastgeber und die Zeit der
  Verbindungen. Der Server kann sich nicht als Gastgeber ausgeben: Der Teilnehmer prüft den Schlüssel des Gastgebers
  aus der Einladung.
- **Ein Raum lässt sich nicht übernehmen:** Er ist an den Geräteschlüssel des Gastgebers gebunden, und die
  Registrierung ist signiert.
- **Minimale Rechte.** Der Dienst unter Linux und macOS braucht keine Root-Rechte (ein temporärer Benutzer /
  `nobody`); unter Windows läuft der Dienst wie die meisten Dienste unter dem Systemkonto.
- **Grenzen** für die Zahl der Verbindungen, Räume und die Rate der Anfragen von einer IP schützen vor Überlastung.
  Senken Sie bei einem Angriff `MaxConnectionsPerMinute` und `MaxStunPerMinute`.
- **Die Seite `/health`** enthält keine Adressen oder Namen; behalten Sie sie trotzdem auf `127.0.0.1`.
- **Das Ereignisjournal** (`/journal`, vom Monitor gelesen) enthält die IP-Adressen der Geräte, die kurzen Kennungen der
  Gastgeber und Raummarken (nicht ihre Namen: Mit einer Marke erreicht niemand den Gastgeber). Es wird nur diesem
  Computer ausgeliefert, welche Adresse die Statusseite auch hat, und lebt nur im Speicher.
- Der Server speichert außer dem Protokoll nichts auf der Festplatte. Nur der Ordner `config` braucht eine Sicherung.
- Aktualisieren Sie den Server zusammen mit der Golether-Anwendung.

## 13. Fehlersuche

| Was Sie sehen | Was zu prüfen ist |
|---|---|
| Der Dienst startet nicht | das Dienstprotokoll ([Abschnitt 8](#8-den-dienst-verwalten)): eine falsche Einstellung, oder der Server kann nicht lauschen, weil ein anderes Programm den Port belegt (`netstat -ano \| findstr 47900` unter Windows, `sudo ss -lntup \| grep 47900` unter Linux, `sudo lsof -i :47900` unter macOS) |
| Der aus einer Konsole gestartete Server meldet, dass der Port belegt ist, und er kommt zurück, nachdem er im Task-Manager beendet wurde | der Dienst läuft bereits und wird nach jedem Fehler neu gestartet: Stoppen Sie ihn als Dienst ([Abschnitt 8](#8-den-dienst-verwalten)) |
| Der Gastgeber sieht, dass der Vermittlungsserver nicht erreichbar ist | der Dienst läuft; `/health` antwortet; der TCP-Port ist weitergeleitet und offen ([Abschnitt 9](#9-prüfen-ob-er-funktioniert), die Prüfung von außen); der Gastgeber hat die externe Adresse bekommen, nicht die lokale |
| Ein Teilnehmer sieht, dass der Gastgeber nicht auf dem Vermittlungsserver ist | die Sitzung läuft noch; die Ereignisliste des Gastgebers hat gemeldet, dass der Server bereit ist; `rooms` in `/health` ist größer als null |
| Die Router haben die direkte Verbindung nicht durchgelassen | der Server hat seinen Teil getan (`meetingsIntroduced` wächst), aber das Netz einer der Seiten erlaubt keine direkten Verbindungen (ein symmetrisches NAT, häufig bei Mobilfunkanbietern). Das kann der Server nicht beheben: Ein AmneziaWG-Tunnel oder ein weitergeleiteter Port auf der Seite des Gastgebers ist nötig |
| Der Film läuft, es gibt keine Kameras | der UDP-Port ist weitergeleitet und offen; `EnableStun` = `true`; `stunAnswered` wächst |
| `refused` wächst schnell | jemand stößt an die Grenzen: viele Teilnehmer hinter einer IP, dann `MaxConnectionsPerMinute` erhöhen; verdächtige Aktivität, dann das Protokoll auf der Ebene `Debug` ansehen |
| macOS: Das Programm lässt sich nicht öffnen | in der Wurzel des Serverordners: `xattr -dr com.apple.quarantine .` und `codesign --force --sign - golether-server bin/golether-server` |
| Linux: `Permission denied` beim Start | in der Wurzel des Serverordners: `chmod +x golether-server bin/golether-server service/install-service.sh` (wenn das Archiv unter Windows entpackt und übertragen wurde) |
