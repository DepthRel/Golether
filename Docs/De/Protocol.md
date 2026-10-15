# Netzwerkprotokoll von Golether (Version 1)

## Transport

TCP, der Port des Gastgebers ist standardmäßig **47800**. Jede Verbindung:

1. TLS 1.3 (TLS 1.2 unter Windows 10) mit Zertifikaten auf beiden Seiten. Die Prüfung von Kette und Namen ist aus: Der
   Teilnehmer prüft die `PeerId` des Gastgeberzertifikats gegen die Einladung, der Gastgeber berechnet die `PeerId` des
   Teilnehmers.
2. Die Präambel des Clients, 6 Bytes: `47 4C 54 48` (`GLTH`), Version `01`, Zweck (`01` Control, `02` MediaData,
   `03` Relay, `04` RelayShared).
3. Die Antwort des Gastgebers, 1 Byte: `01` bedeutet angenommen.

`PeerId` = hexadezimales SHA-256 in Kleinbuchstaben der DER-SubjectPublicKeyInfo des Geräteschlüssels (ECDSA P-256).

## Control

Rahmen: 4 Bytes Länge (Big-Endian) und UTF-8-JSON, höchstens 256 KiB. Der Typ steht im Feld `$t`; Feldnamen sind in
camelCase. G ist der Gastgeber, T ein Teilnehmer.

| `$t` | Richtung | Felder | Bedeutung |
|---|---|---|---|
| `hello` | T → G | `version`, `displayName`, `inviteToken` | die erste Nachricht |
| `pending` | G → T | — | Warten auf die Entscheidung des Gastgebers |
| `welcome` | G → T | `sessionName`, `participants[]`, `playback`, `media`, `mediaDataStreams`, `relay`, `relayOverSession`, `relayShared`, `reconnectTicket` | der Teilnehmer ist eingelassen; `relay` sind Port und Zugangsdaten des TURN des Gastgebers, für eine Sitzung erzeugt; `relayOverSession` bedeutet, dass der Gastgeber TURN über `Relay`-Streams annimmt und kein eigener Port nötig ist; `relayShared` bedeutet, dass er sie auch alle in einem Stream `RelayShared` annimmt; `reconnectTicket` ist das Ticket für die Rückkehr in diese Sitzung ohne neue Einladung |
| `rejected` | G → T | `reason`, `message` | Ablehnung, die Verbindung wird geschlossen |
| `ping` | T → G | `clientSendTime` | eine Uhrenprobe (t0, µs) |
| `pong` | G → T | `clientSendTime`, `hostReceiveTime`, `hostSendTime` | t0, t1, t2 |
| `request` | T → G | `request: {kind, position}` | eine Absicht: `0` abspielen, `1` Pause, `2` springen, `3` abspielen, ohne auf Nicht-Bereite zu warten |
| `state` | G → alle | `state: PlaybackState` | der maßgebliche Zustand |
| `status` | T → G | `status: ParticipantStatus` | einmal pro Sekunde; der Gastgeber ersetzt `peerId` durch die geprüfte |
| `participants` | G → alle | `participants[]`, `statuses[]` | einmal pro Sekunde und bei Änderungen |
| `media` | G → alle | `media: MediaDescriptor` | eine neue Datei |
| `rtc` | beliebig | `peer`, `kind`, `payload` | WebRTC-Signalisierung; der Gastgeber leitet sie weiter und setzt den geprüften Absender ein |
| `moderate` | G → T | `microphone`, `camera` | der Gastgeber schaltet Geräte eines Teilnehmers aus (nur ausschalten) |
| `chat` | T → G → alle | `id`, `kind` (`0` Text, `1` Reaktion), `text`, `sender` | eine Chatnachricht oder eine Reaktion; der Gastgeber füllt `sender` mit der geprüften Kennung, der gesendete Wert wird verworfen |
| `draw` | T → G → alle | `strokeId`, `phase` (`0` Beginn, `1` Fortsetzung, `2` Ende), `points[]` (`x`, `y` als Anteile des Bildes), `sender` | ein Stiftstrich über dem Video; der Gastgeber füllt `sender` mit der geprüften Kennung |
| `avatar` | T → G → alle | `image` (PNG, base64; leer setzt zurück), `sender` | das Bild eines Teilnehmers, gezeigt bei ausgeschalteter Kamera; der Gastgeber füllt `sender`; ein neuer Teilnehmer bekommt die bereits gewählten Avatare vom Gastgeber |
| `bye` | beliebig | `reason` | Verlassen; `reason` ist immer leer und wird vom Empfänger ignoriert |

`PlaybackState`: `state` (`0` pausiert, `1` spielt), `position` (`hh:mm:ss.fffffff`), `referenceTime` (µs nach der Uhr
des Gastgebers), `rate`, `version`, `origin`, `cause` (`0` Anfang, `1` abspielen, `2` Pause, `3` springen, `4` Warten
auf Teilnehmer, `5` alle sind bereit, `6` der Film ist zu Ende, `7` abspielen, ohne auf Nicht-Bereite zu warten).

Alles, was über das Netz kam, wird vor der Verwendung geprüft:

- **Einen Zustand** nimmt ein Teilnehmer nur an, wenn der Modus bekannt ist, die Position zwischen null und 30 Tagen
  liegt und `rate` eine endliche Zahl von 0,5 bis 2 ist. Sonst wird der Zustand übersprungen (in `welcome` wird der
  Eintritt verweigert).
- **Eine Absicht** mit unbekanntem `kind` verwirft der Gastgeber. Die angefragte Position wird auf die Dauer der Datei
  begrenzt und, solange diese unbekannt ist, auf dieselben 30 Tage.
- **Der Status eines Teilnehmers** wird vom Gastgeber vor jeder Berechnung oder Weitergabe begrenzt: Zeiten innerhalb
  von ±30 Tagen, Ping von 0 bis 600.000 ms, Bytezähler nicht unter null. Ein Teilnehmer begrenzt die Status aus
  `participants` genauso.
- **Der Dateiname** in `media` hat höchstens 255 Zeichen, ohne Steuerzeichen und ohne Pfadtrenner (`/`, `\`). Die Regel
  ist auf jedem System gleich: Der Gastgeber ersetzt solche Zeichen vor dem Senden durch `_`, und ein Name, der auf
  seinem System gültig ist (zum Beispiel mit Doppelpunkt unter Linux), wird von den anderen angenommen. Der Name wird
  nur angezeigt; er dient nie als Pfad.

Gründe einer Ablehnung: `0` ungültige Einladung, `1` abgelehnt, `2` der Gastgeber hat nicht geantwortet, `3` die Sitzung
ist voll, `4` inkompatible Version, `5` vom Gastgeber entfernt. Eine `rejected`-Nachricht mit Grund `5` kommt auch
mitten in einer Sitzung: So entfernt der Gastgeber einen eingelassenen Teilnehmer.

Das Feld `message` von `rejected` ist eine kurze Notiz für das Protokoll (der Name des Grundes auf Englisch); der
Teilnehmer zeigt es nicht an, sondern formuliert den Grund aus dem Feld `reason` in der Sprache seiner eigenen
Oberfläche. Der Gastgeber schickt `bye` aus demselben Grund ohne `reason`: Texte für Menschen gehen nicht über das Netz.
Ein Teilnehmer ignoriert das Feld, auch wenn ein Gastgeber eines anderen Builds es füllt, und formuliert das Ende der
Sitzung selbst.

Die Reihenfolge einer Verbindung: `hello` → (`pending`) → `welcome` | `rejected`. Das Token einer Einladung gilt bis zum
Ende ihrer Gültigkeit und dient allen Eingeladenen, bringt aber höchstens 16 Beitrittsanfragen; vertraute Kontakte
dürfen sich ohne Token verbinden. Ein Gerät, das der Gastgeber entfernt oder abgelehnt hat, braucht ein später
ausgegebenes Token: Für ein früheres bekommt es sofort `rejected` mit Grund `5` oder `1`, ohne dass der Gastgeber gefragt
wird.

### Chat und Reaktionen

- Text: höchstens 100 Zeichen nach der Bereinigung. Steuerzeichen, Surrogate und Formatierungszeichen (außer U+200D)
  werden entfernt, nicht mehr als zwei Zeilenumbrüche hintereinander, umgebende Leerzeichen werden entfernt.
- Eine Reaktion: eine aus einer festen Menge (`👍 😂 😮 😢 ❤️ 🔥 👏`), sonst wird die Nachricht verworfen.
- `id` sind bis zu 32 hexadezimale Ziffern, vom Absender gewählt; der Gastgeber berührt sie nicht.
- Rate: höchstens 5 Texte und 15 Reaktionen in 5 Sekunden pro Teilnehmer (ein gleitendes Fenster beim Gastgeber).
- Der Gastgeber schickt eine Nachricht an alle, den Absender eingeschlossen: Die Reihenfolge ist für alle gleich. Nichts
  wird auf der Festplatte gespeichert.

### Der Stift über dem Video

- Ein Strich ist eine Folge von `draw`-Nachrichten mit einer `strokeId`: ein `begin`, beliebig viele `continue` und ein
  `end`. Punkte sammelt der Absender und schickt sie etwa alle 60 ms in Paketen, höchstens 128 Punkte pro Nachricht.
- `strokeId` sind bis zu 32 hexadezimale Ziffern, vom Absender gewählt; der Gastgeber berührt sie nicht.
- Koordinaten sind Anteile des Bildes (`0`–`1`), keine Fensterpixel: Der Strich landet bei allen auf derselben Stelle
  des Bildes. Werte außerhalb der Grenzen werden begrenzt; `NaN` und Unendlichkeiten werden verworfen.
- Eine leere Punktliste ist nur in der Phase `end` erlaubt.
- Rate: höchstens 40 Nachrichten pro Sekunde und Teilnehmer (ein gleitendes Fenster beim Gastgeber); der Überschuss wird
  verworfen.
- Ein Strich gehört dem, der ihn begonnen hat: Ein Stück mit derselben `strokeId` von einem anderen Teilnehmer nimmt der
  Empfänger nicht an.
- Der Empfänger hält einen beendeten Strich 0,3 Sekunden und blendet ihn in den nächsten 1,2 aus; ein unvollendeter
  Strich (der Autor hat die Verbindung verloren) verschwindet nach 20 Sekunden. Höchstens 24 Striche werden gleichzeitig
  gezeichnet. Nichts wird gespeichert.

### Avatare

- Ein Teilnehmer schickt seinen Avatar in einer `avatar`-Nachricht ohne das Feld `sender`; der Gastgeber setzt die
  geprüfte Kennung des Absenders ein und schickt sie an alle, den Absender eingeschlossen, sodass Reihenfolge und Bild
  für alle gleich sind.
- `image` ist ein PNG von höchstens 128 KiB (damit es zusammen mit base64 in die Grenze von 256 KiB pro Nachricht passt)
  und höchstens 1024 × 1024 Pixeln. Die Bytes sind nicht vertrauenswürdig: Die PNG-Signatur, die Dateigröße und die
  Bildgröße aus dem Header (IHDR) werden geprüft, sonst wird die Nachricht verworfen; eine kleine Datei kann ein Bild im
  Wert von Gigabytes an Speicher ankündigen. Ein leeres Feld setzt den Avatar auf den farbigen Kreis mit einem Buchstaben
  zurück.
- Rate: höchstens 5 Nachrichten in 10 Sekunden pro Teilnehmer (ein gleitendes Fenster beim Gastgeber); der Überschuss
  wird verworfen.
- Der Gastgeber merkt sich den letzten Avatar jedes Teilnehmers und schickt sie einem neuen Teilnehmer direkt nach
  `welcome`, sodass der Neue die bereits gewählten Bilder sieht. Ein Avatar wird vergessen, wenn der Teilnehmer die
  Sitzung verlässt.
- Ein Avatar wird bei ausgeschalteter Kamera an Stelle der Kamera gezeigt. Die Datei des gewählten Avatars liegt in
  `data/avatars`.

### Rückkehr in eine Sitzung

`reconnectTicket` sind 48 hexadezimale Ziffern, beim Einlass ausgegeben und gültig, solange die Sitzung läuft. Der
Teilnehmer schickt es im Feld `inviteToken` von `hello`; der Gastgeber vergleicht das Ticket in konstanter Zeit mit dem
für dieses Gerät ausgegebenen und überspringt die Prüfung der Einladung. Die Bestätigung des Gastgebers wird wie bei
einem gewöhnlichen Eintritt eingeholt. Entfernt der Gastgeber einen Teilnehmer, wird das Ticket entzogen.

## MediaData

Eine Anfrage, 17 Bytes: die Operation (`01` GetChunk für ein Stück, `02` GetHash nur für den Hash), der Index des Stücks
(int64 BE), der Dateischlüssel (uint64 BE, die ersten 8 Bytes der schnellen Kennung). Die Antwort auf GetHash kommt ohne
Daten.

Eine Antwort, 45 Bytes und die Daten: der Status (`0` OK, `1` außerhalb der Datei, `2` eine andere Datei,
`3` Lesefehler), der Index (int64 BE), die Länge (int32 BE), das SHA-256 der Daten (32 Bytes).

Die Stückgröße ist standardmäßig 256 KiB (zulässig sind 64 KiB – 16 MiB). Die schnelle Kennung = SHA-256(Länge int64
LE, das erste MiB, das mittlere MiB, das letzte MiB).

## Relay

Der Stream trägt TURN (RFC 5766 in TCP-Rahmung) unverändert: dasselbe, was der Client an den Relay-Port des Gastgebers
schicken würde, mit denselben Zugangsdaten aus `welcome`. Der Gastgeber nimmt einen solchen Stream nur von einem
eingelassenen Teilnehmer an und schließt ihn zusammen mit dem Einlass. Die Zuweisung wird auf der Adresse des
Gastgebers angelegt, die die Verbindung erreicht hat.

Auf der Seite des Teilnehmers öffnet `RelayTunnel` die Streams: Er lauscht auf einem Port von `127.0.0.1`, auf den der
`turn-server` der Kameras zeigt, und öffnet für jede Verbindung dorthin einen eigenen `Relay`-Stream auf dem Weg, den
die Sitzung genommen hat (eine direkte Adresse oder ein Treffen über den Vermittlungsserver). Hat `welcome` kein
`relayOverSession` (ein Gastgeber einer früheren Version), geht der Teilnehmer wie früher direkt zum Relay-Port.

### RelayShared

Ein `RelayShared`-Stream trägt alle TURN-Verbindungen eines Teilnehmers zugleich, damit Kameras und Stimmen eine
Verbindung durch die Router brauchen (über einen Vermittlungsserver ein Treffen) statt eines Dutzends. Er ist eine Folge
von Rahmen: die Kanalnummer (4 Bytes, Big-Endian), die Art (1 Byte), die Länge der Nutzlast (2 Bytes, Big-Endian), die
Nutzlast (höchstens 16384 Bytes).

| Art | Gesendet von | Bedeutung |
|---|---|---|
| `1` open | Teilnehmer | eröffnet einen Kanal; keine Nutzlast |
| `2` data | beide | Bytes des Kanals: dasselbe TURN in TCP-Rahmung |
| `3` close | beide | der Kanal ist zu Ende; keine Nutzlast |

Das Relay bedient jeden Kanal als eigenen Client. Höchstens 16 Kanäle sind gleichzeitig offen; ein `open` darüber hinaus
wird mit `close` beantwortet. Daten für einen nicht vorhandenen Kanal werden verworfen; ein Rahmen anderer Art oder mit
längerer Nutzlast beendet den ganzen Stream. Der Teilnehmer verwendet diesen Stream, wenn `welcome` `relayShared` hat,
und öffnet einen neuen, wenn er abgebrochen ist; ohne das Flag (ein Gastgeber einer früheren Version) bekommt jede
Verbindung einen eigenen `Relay`-Stream. Der Preis des Teilens: Ein verlorenes Paket hält alle Kanäle auf, bis es erneut
gesendet ist.

## Austausch von Stücken zwischen Teilnehmern

Er läuft über den WebRTC-Datenkanal `golether` (SCTP über DTLS; Nachrichten sind erst verfügbar, nachdem das Zertifikat
der Gegenseite geprüft wurde). Alle Zahlen sind Big-Endian.

| Typ | Inhalt | Bedeutung |
|---|---|---|
| `1` have | Dateischlüssel (8), Anzahl der Bereiche (2), Bereiche: Anfang (4), Länge (4) | welche Stücke vorhanden sind; alle 2 s |
| `2` request | Dateischlüssel (8), Anfragenummer (4), Stückindex (4) | eine Bitte, ein Stück zu senden |
| `3` data | Anfragenummer (4), Versatz (4), volle Größe (4), Daten | ein Teil eines Stücks (je 16 KiB, der Reihe nach) |
| `4` miss | Anfragenummer (4) | das Stück ist nicht da oder der Geber ist beschäftigt (höchstens 2 Uploads pro Teilnehmer) |

Ein empfangenes Stück wird nur angenommen, wenn sein SHA-256 zum Hash des Gastgebers (GetHash) passt; sonst wird das
Stück beim Gastgeber angefragt. Dasselbe geschieht, wenn die Übertragung 5 Sekunden stockt oder das Stück nicht
innerhalb von 30 Sekunden vollständig angekommen ist: Ein Teilnehmer, der wenig auf einmal schickt, kann den Film für
die anderen nicht aufhalten.

## Einladung

`golether://join/v1/` + base64url(JSON), höchstens 4096 Zeichen:

```json
{ "token": "<22 base64url-Zeichen, 128 Bit>", "expires": 1789000000,
  "host": "<PeerId>", "name": "You", "session": "Movie night",
  "endpoints": ["192.168.1.10:47800", "[2001:db8::5]:47800"],
  "meet": "203.0.113.7:47900", "room": "<22 base64url-Zeichen, 128 Bit>" }
```

`token` und `expires` (Unix-Sekunden) stehen zuerst: Der Rest ist in jeder Einladung einer Sitzung gleich, so
unterscheidet sich eine neue Einladung ab den ersten Zeichen. Die Reihenfolge der Felder bedeutet einem Leser nichts.
`endpoints` enthält 1 bis 16 Adressen.

Dieselbe Einladung als Weblink: `https://depthrel.github.io/Golether/join/#` + dasselbe base64url. Der Browser schickt
den Teil nach `#` nicht an den Server; die Seite macht daraus `golether://join/v1/…` und übergibt es der Anwendung. Die
Anwendung nimmt beide Schreibweisen an.

Die Felder `meet` und `room` sind optional und kommen nur zusammen: der Vermittlungsserver des Gastgebers und sein Raum
dort (ein neuer für jede Sitzung). Mit nur einem davon oder mit einem fehlerhaften gilt die Einladung als beschädigt.
Alte Versionen lesen diese Felder nicht. Das Protokoll des Servers selbst ist in
[RendezvousServer.md](RendezvousServer.md#protokoll-version-1) beschrieben.

## Prüfcode

Beide Seiten berechnen den Code selbst; er geht nie über das Netz.

```
hash   = SHA-256(UTF-8("golether-sas-v1|" + low + "|" + high + "|" + context))
Wörter = die 24 Bit von hash[0..2] (Big-Endian), in drei Zahlen zu 6 Bit (0–63) geschnitten, die höchste zuerst
Zahl   = uint16 Little-Endian aus hash[3..4] modulo 100
```

`low` und `high` sind die beiden `PeerId` in ordinaler Reihenfolge, sodass das Ergebnis nicht davon abhängt, wer
rechnet. Der Kontext eines Eintritts ist das Token, das der Teilnehmer in `hello` vorlegt: das Einladungstoken oder
beim Zurückkommen das `reconnectTicket`. Der Kontext eines Tunnels ist die `offerId` seines Angebots (siehe unten).

Eine Wortnummer wird erst auf dem Bildschirm zu einem Wort, durch das Sprachpaket (`Verification.Word.00` …
`Verification.Word.63`). Jede Sprache benennt dieselben 64 Dinge in derselben Reihenfolge, daher ist der Code in jeder
Sprache derselbe, und zwei Menschen mit verschiedenen Oberflächensprachen sehen Übersetzungen der Wörter des anderen.

## AmneziaWG-Pakete

`golether-awg:<offer|answer>:v1:<base64url(JSON-Körper)>.<base64url(Signatur)>`.
Die Signatur: ECDSA P-256/SHA-256 (IEEE P1363) über die ASCII-Zeichenkette vor dem Punkt. Leerzeichen und
Zeilenumbrüche innerhalb eines Pakets werden ignoriert, sodass ein Paket mit Zeilenumbrüchen weitergeleitet werden kann.

Der Körper eines Angebots: `offerId`, `hostName`, `hostCertificate` (DER, base64), `hostPublicKey` (AWG),
`hostEndpoints[]`, `hostAddress`, `assignedAddress`, `s1`, `s2`, `headers[4]`, `ephemeralKey` (SPKI P-256, base64),
`expiresAt`.

Der Körper einer Antwort: `offerId`, `participantName`, `participantCertificate`, `participantPublicKey`,
`participantEndpoints[]`, `ephemeralKey`.

PSK = base64(SHA-256(ECDH(ephemere P-256-Schlüssel))): das gemeinsame Geheimnis zweier Einmalschlüssel, durch SHA-256
geschickt. Ein Paket mit unvollständigem Körper oder mit einem Schlüssel, der nicht auf der Kurve P-256 liegt, gilt als
beschädigt.

Prüfcode = `VerificationCode(hostPeerId, participantPeerId, offerId)`.
