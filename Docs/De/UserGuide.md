# Golether: Benutzerhandbuch

## Was Sie brauchen

- Windows 10 oder neuer, Linux oder macOS 14+.
- Das Windows-Paket enthält die Komponenten für Video und Kameras bereits. Fehlt eine, drücken Sie **Installieren**
  auf der Karte „Komponenten“ des Startbildschirms: Die Anwendung lädt sie von der offiziellen Seite herunter, prüft
  die Prüfsumme und installiert sie. Administratorrechte sind nicht nötig. Unter Linux und macOS zeigt die Karte einen
  fertigen Befehl für das Terminal: Kopieren Sie ihn mit der Schaltfläche **Kopieren**.
- Ein Video lässt sich sofort öffnen: „Öffnen mit → Golether“ erstellt eine Sitzung und zeigt diese Datei.
- Tunnel benötigen AmneziaWG. Unter Windows muss nichts separat installiert werden: Beim ersten Öffnen des
  Tunnelfensters lädt Golether seine eigene Engine herunter (etwa 4 MB). Unter Linux und macOS brauchen Sie
  `amneziawg-tools` zusammen mit dem Modul `amneziawg` / `amneziawg-go`. Golether selbst muss nie als Administrator
  laufen: Das System fragt nach der Erlaubnis, wenn ein Tunnel aufgebaut wird.

## Sprache der Oberfläche

Golether spricht Englisch, Deutsch, Französisch, Spanisch, Portugiesisch und Russisch. Beim ersten Start richtet sich
die Sprache nach dem Betriebssystem: Ein System in einer dieser Sprachen bekommt sie, unabhängig von der Region
(Schweizerdeutsch, kanadisches Französisch, brasilianisches Portugiesisch und so weiter), jede andere Sprache bekommt
Englisch. Die Wahl wird gespeichert, und spätere Starts verwenden sie statt der Systemsprache.

Um die Sprache zu ändern, öffnen Sie **Einstellungen** (die Schaltfläche oben rechts), Abschnitt **Allgemeine
Einstellungen**: Die Liste zeigt die Flagge und den Namen jeder Sprache. Nach **Übernehmen** ändern sich alle Texte
sofort, ein Neustart ist nicht nötig, und die Wahl wird gespeichert. Fehlermeldungen verwenden dieselbe Sprache. Die
Gründe einer Ablehnung, die der Gastgeber schickt, werden in Ihrer Sprache angezeigt, auch wenn der Gastgeber eine
andere verwendet.

Auch die Wörter des Prüfcodes werden in Ihrer Sprache angezeigt. Jede Sprache benennt dieselben 64 Dinge; verwendet
der Gastgeber eine andere Sprache, sind seine Wörter Übersetzungen Ihrer Wörter: *Eule · Leuchtturm · Wind · 47* auf
dem einen Bildschirm ist *owl · lighthouse · wind · 47* auf dem anderen. Vergleichen Sie die Bedeutung der Wörter und
die Zahl.

## Einen Film zeigen (Sie sind der Gastgeber)

1. Geben Sie Ihren Namen und den Namen der Sitzung ein und drücken Sie **Sitzung erstellen**. Der Port Ihres Computers
   (standardmäßig 47800) und die Anfrage an den Router liegen unter **Erweitert**: Ändern Sie sie nur, wenn der Port
   belegt ist oder von Hand weitergeleitet wurde.
2. Drücken Sie **Zeigen** → **Datei** und wählen Sie ein Video. Die Datei bleibt bei Ihnen: Die Teilnehmer erhalten sie
   Stück für Stück während des Ansehens und behalten sie nur im Arbeitsspeicher.
3. Drücken Sie **Einladen**, kopieren Sie den **Link** und schicken Sie ihn Ihren Gästen auf beliebigem Weg. Ein Klick
   darauf öffnet Golether auf dem Computer des Gastes und fragt, ob er beitreten möchte. Der Link gilt 15 Minuten.
   Öffnet der Link die Anwendung bei einem Gast nicht, schicken Sie den **Schlüssel** aus demselben Fenster: Er wird
   von Hand in Golether eingefügt und gilt 15 Minuten oder eine Stunde.
   Eine Einladung reicht für die ganze Runde: Posten Sie den Link in Ihren Gruppenchat, und alle können damit
   beitreten, bis er abläuft. Jeden Gast lassen Sie trotzdem selbst herein. Die Gültigkeit begrenzt nur den Moment des
   Beitritts: Ein Gast, der drin ist, bleibt in der Sitzung, auch wenn die Einladung abgelaufen ist.
   Mit eingeschaltetem „Port am Router öffnen“ bittet Golether den Router, den Port zu öffnen (UPnP oder NAT-PMP),
   fügt die öffentliche Adresse der Einladung hinzu und meldet das Ergebnis in der Ereignisliste. Kann der Router das
   nicht oder ist die Adresse Ihres Anbieters nicht öffentlich, fügen Sie eine öffentliche Adresse mit einem von Hand
   weitergeleiteten Port oder die Adresse in einem AWG-Tunnel hinzu (Einladungsfenster, **Erweitert**).
   Am einfachsten ist der Vermittlungsserver. Der Schalter **Verbindungsart** auf dem Startbildschirm steht
   standardmäßig auf **Automatisch**: Golether findet den Server des Projekts selbst, er kommt in die Einladung, und
   Gäste aus anderen Netzen verbinden sich ohne offene Ports auf beiden Seiten; siehe [unten](#vermittlungsserver).
4. Wenn sich ein Teilnehmer verbindet, erscheint ein Fenster mit dem Prüfcode. Lesen Sie den Code laut vor. Sieht der
   Teilnehmer denselben Code, drücken Sie **Die Codes stimmen überein, einlassen**.
5. Abspielen, Pause und Spulen wirken bei allen Teilnehmern.
6. Kameras und Sprache starten von selbst, wenn Sie einer Sitzung beitreten: Die Kachel jedes Teilnehmers rechts zeigt
   seine Kamera, und die Stimmen der anderen werden zu einem Strom gemischt. Kamera und Mikrofon wählen Sie in
   **Einstellungen**, Abschnitt **Kamera und Mikrofon** (sonst werden die Systemstandards verwendet). Eine
   ausgeschaltete Kamera oder ein ausgeschaltetes Mikrofon wird wirklich freigegeben.

## Beitreten (Sie sind Teilnehmer)

1. Klicken Sie auf den Einladungslink: Der Browser bietet an, Golether zu öffnen, und Golether fragt, ob Sie der
   Sitzung beitreten möchten (mit ihrem Namen und dem Namen des Gastgebers). Sind Sie bereits in einer anderen Sitzung,
   fragt es, ob Sie wechseln möchten. Statt zu klicken, können Sie den Schlüssel oder den Link selbst in das Feld
   **Einladung** einfügen und **Verbinden** drücken.
2. Ein Prüfcode erscheint. Vergleichen Sie ihn per Sprache mit dem Gastgeber. Unterscheiden sich die Codes, drücken Sie
   **Beenden**: Die Verbindung könnte manipuliert worden sein.
3. Haben Sie dieselbe Datei bereits, drücken Sie **Meine Kopie der Datei** und wählen Sie sie: Die Wiedergabe läuft
   dann von Ihrer Festplatte.

## Bedienung

| Aktion | Schaltfläche | Taste |
|---|---|---|
| Abspielen / Pause für alle | ▶ / ❚❚ | Leertaste |
| 10 s zurück / vor | ◀◀ / ▶▶ | ← / → |
| Springen | der Schieberegler (wirkt beim Loslassen) | — |
| Vollbild | ⛶ | F11, Esc |
| Abspielen / Pause per Klick | ein Klick auf das Video | — |
| Vollbild per Klick | ein Doppelklick auf die Videofläche (funktioniert auch, bevor ein Film gewählt ist) | — |
| Ihre eigene Lautstärke | das Lautsprechersymbol rechts: Darüberfahren zeigt eine Skala 0–100 %, Klick schaltet stumm und laut, Mausrad ändert | — |
| Mikrofon und Kamera | „Mikrofon an“ / „Kamera an“ unter den Teilnehmerkacheln | — |
| Ton und Untertitel | das Symbol „CC“ in der Player-Leiste: Jeder hat seine eigenen Spuren | — |
| Reaktion für alle | das Smiley-Symbol in der Player-Leiste | — |
| Über das Video zeichnen | das Stift-Symbol in der Player-Leiste | — |
| Chat und Ereignisse | die Reiter **CHAT** und **EREIGNISSE** unten rechts; ein Klick auf den offenen Reiter oder den Pfeil blendet die Leiste aus | — |
| Nachricht senden | das Feld unten in der Chat-Leiste | Enter |
| Lautstärke eines anderen Teilnehmers | Rechtsklick auf seine Kachel → Schieberegler | — |
| Kamera eines Teilnehmers im eigenen Fenster | Rechtsklick auf seine Kachel → **In einem eigenen Fenster öffnen** | — |

In einem schmalen Fenster wandern die Schaltflächen „Einstellungen“, „Einladen“ und „Beenden“ in die Schaltfläche
**⋯** oben rechts.

Lautstärke und Stummschaltung wirken nur bei Ihnen: Die anderen merken nichts. Eine Skala auf 0 schaltet den Ton stumm,
und ein weiterer Klick auf den Lautsprecher bringt die vorige Lautstärke zurück.

### Vollbild

Der Mauszeiger über dem Video verschwindet nach drei Sekunden Ruhe und kommt bei der ersten Bewegung zurück, in jedem
Fenstermodus. Über den Teilnehmerkacheln und dem Chat ist er immer sichtbar.

Im Vollbild ist die Statusleiste unten ausgeblendet, und das Video nimmt die ganze Höhe ein. Die obere Leiste und die
Player-Leiste liegen über dem Video und gleiten drei Sekunden, nachdem der Zeiger sie verlassen hat, weg; das Bild
ändert seine Größe nicht. Bewegen Sie den Zeiger an den oberen oder unteren Bildschirmrand, und die Leisten gleiten
zurück und bleiben, solange der Zeiger auf ihnen ist. Nichts wird ausgeblendet, solange ein Menü einer Leiste offen
ist, der Schieberegler gezogen wird oder der Stift aktiv ist.

Möchten Sie die Leisten immer sehen, schalten Sie **Zeitleiste immer anzeigen** in **Einstellungen**, Abschnitt
**Anzeige**, ein: Im Vollbild bleiben beide Leisten an ihrem Platz, und das Video wird zwischen sie eingepasst.

### Breite der Teilnehmerleiste und Reihenfolge der Kacheln

Schalten Sie in **Einstellungen**, Abschnitt **Anzeige**, **Anpassbares Layout** ein: Zwischen Video und Teilnehmern
erscheint eine schmale Grenze. Ziehen Sie sie, um die Teilnehmerleiste zu verbreitern; die Kamerakacheln wachsen mit.
Ein Doppelklick auf die Grenze stellt die ursprüngliche Breite wieder her.

Bei drei Teilnehmern oder weniger bilden die Kacheln eine Spalte. Bei mehr Teilnehmern und einer Leiste, die auf mehr
als das Dreifache ihrer ursprünglichen Breite gezogen ist, bilden die Kacheln zwei Spalten, sodass mehr Menschen auf
den Bildschirm passen.

Mit derselben Einstellung lassen sich die Kacheln tauschen: Ziehen Sie eine auf eine andere. Die Reihenfolge sehen nur
Sie, und sie gilt, bis die Anwendung geschlossen wird; neue Teilnehmer kommen ans Ende. Das Ausschalten der Einstellung
stellt die ursprüngliche Breite und Reihenfolge wieder her und entfernt die Grenze.

### Die Kamera eines Teilnehmers in einem eigenen Fenster

Rechtsklick auf die Kachel eines Teilnehmers → **In einem eigenen Fenster öffnen**. Das Fenster zeigt nur diese
Kamera; Sie können es vergrößern oder auf einen anderen Bildschirm schieben. Das Schließen des Fensters beeinflusst
nichts; verlässt der Teilnehmer die Sitzung, schließt es sich von selbst.

### Eigene Tonspuren und Untertitel

Das Symbol **CC** in der Player-Leiste öffnet die Liste der Tonspuren und Untertitel des Films. Die Wahl wirkt nur bei
Ihnen: Einer sieht das Original, ein anderer die Synchronfassung, ein Dritter liest Untertitel. Die gewählte Sprache
wird gespeichert und beim nächsten Film mit einer solchen Spur angewendet. **Untertitel aus einer Datei…** fügt eine
`.srt`- oder `.ass`-Datei von Ihrem Computer hinzu, **Ton aus einer Datei…** eine separate Tonspur (zum Beispiel Ihre
eigene Synchronisation). Beides sehen und hören nur Sie.

### Chat und Reaktionen

Unten rechts gibt es zwei Reiter: **CHAT** und **EREIGNISSE**. Ein Klick auf den offenen Reiter (oder auf den Pfeil
rechts) blendet die Leiste ganz aus und überlässt den Platz den Teilnehmerkacheln. Solange der Chat ausgeblendet ist,
zeigt sein Reiter einen Zähler neuer Nachrichten. Nachrichten laufen über denselben geschützten Kanal wie der Rest der
Sitzung und werden nirgends gespeichert: Wird die Sitzung geschlossen, sind sie weg.

Solange der Chat nicht zu sehen ist, steht außerdem ein orangefarbenes „i“ im Kreis an der Ecke seines Reiters, und
solange die ganze Leiste ausgeblendet ist, läuft ein kurzer orangefarbener Streifen langsam um die Pfeiltaste. Der
Reiter **EREIGNISSE** verlangt nie Aufmerksamkeit. Eine Nachricht eines anderen Teilnehmers kündigt ein kurzer Schlag
einer Filmklappe an; eigene Nachrichten und Reaktionen bleiben stumm. Der Ton wird in **Einstellungen**, Abschnitt
**Töne**, ausgeschaltet.

Das Smiley-Symbol in der Player-Leiste schickt eine Reaktion: Einige Sekunden lang schwebt sie bei allen Teilnehmern
über dem Video, mit dem Namen des Absenders. Reaktionen sind ab dem Beitritt zur Sitzung verfügbar, noch bevor ein Film
gewählt ist. Die Ebene mit den Reaktionen lässt Klicks durch: Pause per Klick auf das Video funktioniert weiter.

### Der Stift: etwas direkt auf dem Video einkreisen

Das Symbol **Stift** in der Player-Leiste schaltet das Zeichnen ein. Die Schaltfläche bleibt gedrückt, und der Zeiger
über dem Video wird zum Stift. Halten Sie die linke Maustaste und bewegen Sie die Maus: Die Linie folgt dem Zeiger
genau, und jeder Teilnehmer sieht sie im selben Moment. Lassen Sie die Taste los, bleibt die Linie zwei Sekunden und
verblasst. Jeder Teilnehmer hat seine eigene Farbe, dieselbe wie seine Kachel, sodass klar ist, wer zeichnet. Ein
weiterer Klick auf das Symbol löst die Schaltfläche, und der Zeiger ist wieder normal.

Linien werden nirgends gespeichert und hängen am Bild, nicht am Fenster: Ist Ihr Fenster breiter und das der anderen
Person schmaler, ist die eingekreiste Stelle trotzdem derselbe Teil des Bildes. Solange der Stift aus ist, wirken
Klicks auf das Video wie gewohnt: Pause und Vollbild.

### Fotos und Videos eines Ordners zeigen (Gastgeber)

**Zeigen** → **Ordner mit Bildern** (Ctrl+Shift+O) öffnet ein Fenster, das zeigt, was in einem Ordner liegt: seine
Unterordner, seine Bilder mit kleinen Vorschauen und seine Videos. Öffnen Sie den gewünschten Ordner und drücken Sie
**Zeigen**. Darunter zählt das Fenster, was hinausgeht (Bilder, Videos, die Größe der Bilder) und was ausgelassen
wird: RAW-Dateien von Kameras und Bilder über 64 MB. Dazu gehören zwei Schalter:

- **Mit Unterordnern**: Die Dateien jedes Unterordners folgen denen des Ordners selbst, Ordner für Ordner, und der
  Streifen nennt einen Unterordner bei seinem ersten Element.
- **Aufnahmeort, Kamera und Kommentare der Bilder nicht zeigen** (standardmäßig an): Die Kopien, die die Teilnehmer
  erhalten, verlieren den Ort, die Kamera, den Autor, die Kommentare und die zusätzlichen Bilder, die manche Telefone
  an ein Foto anhängen. Ihre eigenen Dateien werden nicht verändert. Die Ausrichtung bleibt, das Foto steht also
  weiterhin aufrecht.

Im Fenster funktionieren **Zurück** und **Vorwärts** wie in einem Dateimanager, auch mit Alt+← und Alt+→, der
Rücktaste und den Seitentasten der Maus.

Höchstens 2000 Dateien werden auf einmal gezeigt. Ein einzelnes Bild, gewählt mit **Zeigen** → **Datei**, wird allein
gezeigt: Nichts anderes aus seinem Ordner geht hinaus, und seine Metadaten bleiben bei Ihnen.

Solange ein Ordner gezeigt wird, liegt unter dem Bild ein Streifen kleiner Bilder. Ein Klick auf ein Element bringt es
auf den Bildschirm aller, wie ein Sprung im Film: Jeder Teilnehmer kann das, der Gastgeber entscheidet. ← und → gehen
zum vorherigen und zum nächsten Element; die Schaltfläche mit vier Quadraten öffnet alle Elemente als Raster nur auf
Ihrem Bildschirm (Esc schließt es). Unter jedem Element stehen die Marken derer, die es ansehen. Das Mausrad blättert
den Streifen seitwärts.

- **Bilder** zeichnet Golether selbst. Das Mausrad oder die Tasten + und − vergrößern ein Bild um den Zeiger, Ziehen
  verschiebt das vergrößerte Bild, 0 zeigt es wieder ganz. Das ändert nur Ihren Bildschirm, und die Linien des Stifts
  bleiben an ihren Stellen des Bildes. HEIC-, AVIF- und TIFF-Fotos werden auf dem Rechner des Gastgebers von der
  Videokomponente in JPEG umgewandelt, die Teilnehmer brauchen dafür nichts.
- **Nur für mich öffnen** (Rechtsklick auf ein Element) zeigt ein Bild nur auf Ihrem Bildschirm. Eine Zeile oben sagt,
  was alle anderen sehen, und bietet **Zurück zu allen** und **Allen zeigen** an; die anderen sehen auf Ihrer Kachel
  die Marke „ALLEIN“. Der Stift ist währenddessen aus.
- **Videos** des Ordners laufen wie ein Film: Auf den Bildschirm gebracht, wird ein Video zum Film aller, mit seiner
  Zeitleiste, seinem Ton und seinen Untertiteln, und der Streifen bleibt. Sein kleines Bild ist ein Bild aus dem Video.

Der Film pausiert, sobald ein Bild auf den Bildschirm kommt, und wartet unter den Bildern; die Schaltflächen für Ton
und Untertitel sind inaktiv, solange ein Bild gezeigt wird. Ein Video des Ordners auf dem Bildschirm ersetzt den Film.
**Bilder schließen** (Gastgeber) beendet das Zeigen für alle, und der Film kehrt an die Stelle zurück, an der er
unterbrochen wurde. Auch die Wahl eines Films mit **Zeigen** → **Datei** beendet es.

### Warten, bis alle bereit sind

Wenn Sie auf Abspielen drücken, prüft Golether zuerst, ob alle die Datei geöffnet und ihren Anfang geladen haben.
Solange jemand nicht bereit ist, zeigt die Player-Leiste, auf wen gewartet wird („Warten auf: Marina…“), und die
Schaltfläche wird zu „Start abbrechen“. Der Gastgeber muss nicht warten: **Ohne sie starten** startet den Film sofort,
und wer zurückliegt, holt von selbst auf. Ist ein Teilnehmer nach 45 Sekunden immer noch nicht bereit, beginnt die
Vorstellung trotzdem.

### Dort weitermachen, wo Sie aufgehört haben

Golether merkt sich, wo Sie bei jedem Film aufgehört haben (auf diesem Computer, im Ordner `data`). Öffnet der
Gastgeber dieselbe Datei erneut, erscheint über der Zeitleiste eine Leiste mit der Stelle, an der Sie aufgehört haben,
und der Schaltfläche **Für alle fortsetzen**. Ein bis zum Ende gesehener Film wird vergessen.

### Was die Zeitleiste zeigt

Der Schieberegler bewegt sich nur, wenn sich das Bild wirklich bewegt: Während des Countdowns vor dem Start und während
der Player springt oder auf Daten wartet, steht er still.

Die helle Füllung auf der Zeitleiste ist das, was bereits geladen ist und ohne Warten angesprungen werden kann. Für den
Gastgeber und für alle, die ihre eigene Kopie der Datei ansehen, ist die ganze Zeitleiste hell. Die Kreise mit
Buchstaben sind die Teilnehmer: Sie sehen, wo jeder gerade ist.

### Die Verbindung ist abgebrochen

Geht die Verbindung zum Gastgeber verloren, verbindet sich Golether von selbst neu: bis zu acht Versuche mit wachsenden
Pausen. Eine neue Einladung ist nicht nötig: Beim Einlass erhalten Sie ein Ticket, das für diese Sitzung gilt. Der
Gastgeber bestätigt die Rückkehr trotzdem, wie bei einem gewöhnlichen Beitritt. Während der Versuche zeigt eine Leiste
oben die Nummer des Versuchs und die Schaltfläche **Verlassen**.

### Gastgeber: Mikrofon oder Kamera eines Teilnehmers ausschalten

Rechtsklick auf die Kachel eines Teilnehmers und **Mikrofon ausschalten** oder **Kamera ausschalten** wählen. Der
Gastgeber kann nur ausschalten: Nur der Teilnehmer kann das Gerät wieder einschalten, und Golether sagt ihm das. Die
Kacheln zeigen, wessen Mikrofon oder Kamera aus ist. Ihre eigene Kachel hat kein Menü: Verwenden Sie für sich selbst die
Schaltflächen unter den Kacheln.

### Gastgeber: einen Teilnehmer entfernen

Dasselbe Menü (Rechtsklick auf die Kachel eines Teilnehmers) enthält **Aus der Sitzung entfernen**. Golether bittet um
Bestätigung, dann verlässt der Teilnehmer die Sitzung sofort und sieht eine Meldung, dass der Gastgeber ihn entfernt
hat. Das ist keine Sperre: Er kann zurückkommen, aber nur mit einer neuen Einladung von Ihnen; weder die früheren
Einladungen noch die automatische Wiederverbindung lassen ihn herein. Die früheren Einladungen funktionieren für Ihre
anderen Gäste weiter: Entfernt wird die Person, nicht der Link. Öffnen Sie **Einladen**, drücken Sie **Neue Einladung**
und schicken Sie den neuen Link dieser Person privat. Dieselbe Regel gilt für jemanden, den Sie im Fenster mit dem
Prüfcode abgelehnt haben.

### Start, Filmende und die Leertaste

Wenn der Film endet, hält er bei allen auf dem letzten Bild an, und die Abspieltaste wird zu **Noch einmal ansehen**
(ein Kreispfeil): Ein Druck oder die Leertaste startet den Film für alle von vorn.

Die Player-Schaltflächen übernehmen nicht den Tastaturfokus, daher bedeutet die Leertaste immer Abspielen/Pause, auch
nach einem Klick auf andere Schaltflächen.

Nach dem Druck auf Abspielen gibt es einen kurzen Countdown: So starten alle Teilnehmer gemeinsam, auch bei großem Ping.

## Was die Kacheln zeigen

- **Grüner Rahmen**: Der Teilnehmer spricht gerade (auch Sie selbst, wenn Sie sprechen). Bei ausgeschaltetem Mikrofon
  gibt es keinen Rahmen.
- **Ping**: die Antwortzeit zum Gastgeber. Grün unter 400 ms, gelb bis 1,5 s, rot darüber.
- **Abweichung**: wie weit der Teilnehmer der gemeinsamen Position voraus (+) oder hinterher (−) ist. Eine kleine
  Abweichung wird durch eine unmerkliche Änderung der Geschwindigkeit ausgeglichen, eine große durch einen Sprung.
- **Puffer**: wie viele Sekunden Video bereits im Voraus geladen sind. Fällt jemand weit zurück und wartet auf Daten,
  pausiert der Gastgeber automatisch alle und macht weiter, wenn alle bereit sind.

Wenn mehrere Leute schauen, geben sich die Teilnehmer die bereits geladenen Teile des Films direkt weiter, sodass der
Gastgeber sie nicht jedem geben muss. Jedes Teil wird mit der Prüfsumme des Gastgebers geprüft. Wie viel empfangen und
gegeben wurde, zeigt die Statusleiste unten im Fenster.

## Wenn etwas schiefging

**⋯ → Diagnosebericht…** speichert eine Textdatei in `data/logs` (die Schaltfläche **⋯** erscheint oben rechts, wenn
das Fenster so schmal ist, dass die Schaltflächen der oberen Leiste nicht mehr hineinpassen): die Version, der Zustand
der Komponenten, Informationen über die Sitzung (ohne den Namen des Films) und die letzten Zeilen des Protokolls.
Schlüssel, Token und Passwörter werden aus dem Bericht entfernt, sodass die Datei an jeden geschickt werden kann, der
Ihnen hilft.

Bei jeder Fensterbreite wird derselbe Bericht über **Einstellungen → Über Golether → Diagnosebericht…** gespeichert.
Daneben öffnet **Problem melden** in Ihrem Mailprogramm einen Brief an die Entwickler (project.golether@gmail.com),
Version und System sind schon eingetragen; das Fenster jedes Fehlers bietet dasselbe an. Hängen Sie den Bericht an den
Brief: Nichts wird gesendet, bevor Sie ihn selbst abschicken.

Der Bericht hat eine Zeile für jede Verbindung mit einem Teilnehmer: ob sie geprüft ist, ob der Datenkanal offen ist
und wie viele Bilder hinaus- und hereingegangen sind (`Video 120↑/0↓`). So sieht man sofort, wo die Verbindung bricht:
Sie ist nicht zustande gekommen, sie kam leer zustande, oder die Kamera liefert nichts.

### Niemand kann sich verbinden: die Firewall

Windows behandelt eine neue Netzwerkverbindung, vor allem einen Tunneladapter, als öffentliches Netzwerk und verwirft
eingehende Verbindungen. Teilnehmer kommen dann nicht herein, selbst wenn sich die Computer bestens sehen. Golether
prüft das beim Start einer Vorstellung und zeigt, wenn es keine Regel gibt, oben die Leiste **„Die Firewall lässt keine
Teilnehmer herein“** mit der Schaltfläche **Zulassen**. Die Schaltfläche fragt einmal nach der Bestätigung eines
Administrators und fügt eine Regel für das Programm hinzu; es entsteht kein dauerhafter Dienst, und es muss nie
wiederholt werden.

Dasselbe von Hand: „Windows Defender Firewall“ → „App durch die Firewall zulassen“ → Golether suchen und sowohl das
private als auch das öffentliche Netzwerk ankreuzen.

## Updates

Beim Start fragt Golether die [Projektseite](https://depthrel.github.io/Golether/), ob eine neue Version erschienen ist.
Wenn ja, erscheint oben die Leiste „Eine neue Version ist verfügbar“ mit der Schaltfläche **Downloadseite öffnen**: Ihr
Browser öffnet die Seite mit den Links für Ihr System. Nichts wird von selbst heruntergeladen oder installiert: Sie
entscheiden. Das Häkchen **Diese Version ignorieren**, vor dem Schließen der Leiste gesetzt, schaltet die Erinnerung an
diese Version ab; über die nächste informiert Golether Sie. Persönliche Daten werden von einem Update nicht berührt.

In **Einstellungen**, Abschnitt **Updates**, können Sie die Prüfung abschalten oder eine eigene Quelle angeben (die
Adresse einer Datei mit der Liste der Versionen, `https://…`); das ist nur für eigene Builds nötig.

## Vermittlungsserver

Haben Sie und Ihre Teilnehmer „graue“ Adressen und lässt sich kein Port weiterleiten, hilft der Vermittlungsserver:
ein kleines Programm auf einem beliebigen Rechner mit öffentlicher IP (Ihrem oder dem eines Freundes). Er stellt Ihre
Computer nur einander vor; die Verbindung zwischen ihnen wird direkt durchgeschlagen. Film, Kameras und Stimmen gehen
nicht über den Server, und er kann sie nicht sehen.

1. Jemand richtet den Server nach dem
   [Administratorhandbuch](../../Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.de.md) ein (Windows, Linux oder macOS;
   dieselbe Datei liegt in jedem Serverpaket).
2. Der Gastgeber wählt auf dem Startbildschirm die **Verbindungsart** und erstellt eine Sitzung:
   - **Automatisch** (Standard): Golether erfährt die Adresse des Projektservers von der Projektseite. Ist die Seite
     nicht erreichbar, wird die Adresse vom letzten Mal verwendet; gibt es keine, wird die Sitzung ohne Server erstellt,
     und die Ereignisliste sagt das;
   - **Direkt**: ohne Server; Gäste verbinden sich nur mit den Adressen Ihres Computers;
   - **Eigener Server**: Es erscheint ein Feld für die Serveradresse, zum Beispiel `203.0.113.7:47900`.

   Hat der Server geantwortet, zeigt die Ereignisliste „Der Vermittlungsserver ist bereit“.
3. Ein Teilnehmer muss nichts einrichten: Die Serveradresse kommt mit der Einladung. Antworten die direkten Adressen
   nicht innerhalb von 4 Sekunden, trifft Golether den Gastgeber von selbst über den Server.

Es funktioniert nicht, wenn mindestens eine Seite hinter einem symmetrischen NAT sitzt (häufig bei Mobilfunkanbietern
und in Büronetzen): Golether meldet dann, dass die Router die direkte Verbindung nicht durchgelassen haben, und es
bleiben ein AWG-Tunnel oder ein weitergeleiteter Port.

## AmneziaWG-Tunnel

Ein Tunnel wird vor dem Verbinden aufgebaut: **Einstellungen → AmneziaWG-Tunnel → Tunnel einrichten**. Während einer
Sitzung ist diese Schaltfläche nicht verfügbar.

Ein Tunnel wird gebraucht, wenn eine direkte Verbindung nicht durchkommt oder blockiert ist. Ein eigener Server ist
nicht nötig, und unter Windows **muss AmneziaWG nicht separat installiert werden**: Golether lädt seine eigene Engine
herunter und baut den Tunnel in seiner eigenen Instanz auf. Nutzen Sie AmneziaWG bereits mit eigenen Adressen, wird das
nicht berührt: Es sind verschiedene Netzwerkadapter.

Beim ersten Öffnen des Tunnelfensters wird die Engine mit der Schaltfläche **Installieren** direkt in diesem Fenster
heruntergeladen (etwa 4 MB, keine Administratorrechte nötig). Bis sie da ist, sind die Schaltflächen „Tunnel aufbauen“
nicht verfügbar.

**Gastgeber:**
1. Im Tunnelfenster: **Ich bin der Gastgeber → Angebot erstellen**, den Text an den Teilnehmer schicken.
2. Die Antwort des Teilnehmers einfügen und **Antwort annehmen** drücken. Den Code per Sprache vergleichen.
3. **Tunnel aufbauen** (Windows zeigt eine Berechtigungsabfrage, macOS und Linux fragen nach dem Passwort) oder
   **.conf exportieren** für AmneziaVPN.
4. Die Adresse des Gastgebers im Tunnel zur Einladung hinzufügen, zum Beispiel `10.77.41.1:47800` (Einladungsfenster,
   **Erweitert**).

**Teilnehmer:**
1. Im Tunnelfenster: **Ich bin Teilnehmer**, das Angebot einfügen, **Angebot annehmen**.
2. Die Antwort an den Gastgeber schicken, den Code vergleichen, **Tunnel aufbauen**.

**Ein Tunnel lebt, solange Golether geöffnet ist.** Beim Beenden wird er automatisch abgebaut; das System fragt einmal
nach der Erlaubnis eines Administrators. Lehnen Sie ab, bleibt der Tunnel, und Golether bietet beim nächsten Start an,
ihn zu entfernen. Um ihn früher zu entfernen, verwenden Sie **Tunnel abbauen** im selben Fenster. Tunnel, die nicht von
Golether aufgebaut wurden, bleiben unberührt.

Golether findet die öffentliche Adresse selbst: Beim Erstellen eines Angebots und beim Annehmen einer Antwort fragt es
öffentliche STUN-Server, wie sein Port von außen aussieht, und schreibt die Adresse in das Paket. Dann „klopfen“ beide
Seiten beieinander an, damit die Router die Antwort durchlassen. Deshalb ist das Weiterleiten eines Ports von Hand
meist unnötig.

Es funktioniert nicht, wenn der Router jedem Ziel einen eigenen Port gibt (symmetrisches NAT) oder der Anbieter Sie
hinter einer gemeinsamen Adresse hält (CGNAT), und zwar auf beiden Seiten zugleich. Dann braucht eine Seite eine
öffentliche IP oder einen weitergeleiteten Port. `.conf`-Dateien enthalten Schlüssel: Veröffentlichen Sie sie nicht.

## Wo die Daten liegen

Golether ist eigenständig: Alles, was es speichert (der Geräteschlüssel, die Datenbank der Kontakte und Tunnel,
`.conf`-Konfigurationen, heruntergeladene Komponenten, Einstellungen), liegt im Ordner `data` neben dem Programm. Ein
entpacktes Archiv schreibt nichts in das Benutzerprofil oder die Registry; das Windows-Installationsprogramm trägt in die
Registry nur den Deinstallationseintrag und den Handler für Einladungslinks ein. Daher:

- Entpacken Sie Golether dort, wo Sie Schreibrechte haben: in „Dokumente“, auf den Desktop, auf einen USB-Stick. Eine
  entpackte Kopie in `Program Files` startet nicht und bittet Sie, den Ordner zu verschieben;
- das Windows-Installationsprogramm fragt, für wen installiert werden soll. **Nur für mich** (Standard) installiert nach
  `%LOCALAPPDATA%\Programs\Golether`, braucht keine Administratorrechte und behält die Daten in `data` innerhalb der
  Installation. **Für alle** installiert nach `C:\Program Files\Golether`, braucht während der Installation einmal
  Administratorrechte und legt die Daten jedes Benutzers in seinem Profil ab: `%LOCALAPPDATA%\Golether`. Das
  Installationsprogramm bringt dem System außerdem bei, Einladungslinks mit Golether zu öffnen. In einem entpackten
  Archiv öffnen sich Links nicht von selbst: Verwenden Sie den Schlüssel (unter Linux schaltet
  `install-desktop-entry.sh` die Links ein);
- das Paket für Ubuntu und Debian (`.deb`, installiert mit `sudo apt install ./Golether-<version>-linux-x64.deb`)
  bringt die Komponenten für Video und Kameras mit, trägt Golether ins Menü ein, lässt das System Einladungslinks mit
  Golether öffnen und hält die Daten jedes Benutzers in seinem Profil: `~/.local/share/Golether`. Entfernt wird es mit
  `sudo apt remove golether`;
- verschieben, kopieren und löschen Sie den Ordner `Golether` als Ganzes: Alle Daten reisen mit;
- um eine entpackte Kopie zu aktualisieren, entpacken Sie die neue Version und verschieben den Ordner `data` aus der
  alten hinein;
- lagen die Daten früher in `%LOCALAPPDATA%\Golether` (frühe Builds), werden sie beim ersten Start automatisch nach
  `data` verschoben;
- unter Windows werden der Geräteschlüssel und die Tunnelgeheimnisse zusätzlich für Ihr Windows-Konto verschlüsselt. Auf
  einem anderen Computer oder unter einem anderen Benutzer lassen sie sich nicht lesen: Das Programm sagt das, und nach
  dem Löschen des Ordners `data\identity` erstellt es einen neuen Schlüssel;
- solange ein Ordner gezeigt wird, liegen in `data\gallery` die JPEG-Dateien, die der Gastgeber aus seinen HEIC-,
  AVIF- und TIFF-Fotos und aus Bildern seiner Videos macht. Der Ordner wird geleert, wenn das Zeigen endet, oder beim
  nächsten Start, wenn das Programm währenddessen geschlossen wurde;
- `data\identity\device.key` ist der Geräteschlüssel. Wird er gelöscht, ändert sich der Fingerabdruck des Geräts, und
  Ihre Kontakte müssen den Code erneut vergleichen.

## Häufige Fragen

- **„Der Gastgeber war nicht erreichbar“.** Prüfen Sie, ob der Port in der Firewall des Gastgebers offen ist, oder
  verwenden Sie einen Tunnel.
- **„Die Einladung ist ungültig oder abgelaufen“.** Die Gültigkeit der Einladung ist vorbei (ein Link lebt 15 Minuten,
  ein Schlüssel bis zu einer Stunde): Bitten Sie den Gastgeber um eine neue.
- **„Der Gastgeber hat Sie entfernt“ oder „Der Gastgeber hat die Anfrage abgelehnt“.** Die früheren Einladungen lassen
  Sie nicht mehr herein; Sie können mit einer Einladung beitreten, die der Gastgeber danach erstellt.
- **macOS öffnet die Anwendung nicht.** Der Build ist nicht von Apple signiert: „Systemeinstellungen → Datenschutz &
  Sicherheit → Dennoch öffnen“.
- **Ein anderer Teilnehmer hat eine schwache Verbindung.** Seine Kachel bekommt die Markierung „spar“: Ihre Kamera geht
  in geringerer Auflösung (320×180) an ihn, bis sich die Verbindung erholt. Der Ton wird nicht schlechter.
- **Keine Kameras.** Prüfen Sie, ob die Komponente „Kameras und Sprache“ installiert ist (Startbildschirm) und ob Windows
  Anwendungen Kamera und Mikrofon erlaubt („Einstellungen → Datenschutz“). Eine schwarze Kachel bedeutet meist eine
  geschlossene Objektivabdeckung.
- **Die andere Person hört sich selbst.** Die Echounterdrückung wirkt für den Ton, den Golether abspielt; Kopfhörer
  beseitigen das Problem vollständig.
