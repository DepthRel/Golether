# Golether: Datenschutz

Golether hat keine Konten, keine Werbung und keine Telemetrie: Es sammelt keine Statistiken über Sie oder darüber, was
Sie ansehen. Der Film, die Stimmen, die Kameras, der Chat und die Zeichnungen gehen verschlüsselt direkt zwischen den
Rechnern der Teilnehmer, und kein Server des Projekts sieht sie.

Diese Seite nennt alles, was Golether speichert, und jede Adresse, die es kontaktiert.

## Was auf Ihrem Rechner bleibt

Alles Folgende liegt im Ordner `data` der Anwendung (siehe „Wo die Daten liegen“ im [Handbuch](UserGuide.md)) und
verlässt den Rechner nie von selbst:

- der Geräteschlüssel: Er beweist den anderen, dass es dasselbe Gerät ist;
- die Kontakte: Name und Schlüssel-Fingerabdruck jedes Geräts, dessen Code Sie verglichen haben, ob Sie ihm vertrauen,
  seine letzte Netzwerkadresse, Ihre Notizen und wann es zuerst und zuletzt gesehen wurde;
- die Einstellungen, die AmneziaWG-Tunnel mit ihren Schlüsseln, die heruntergeladenen Komponenten;
- das Protokoll der Anwendung in `data/logs`, 7 Tage aufbewahrt. Es enthält Verbindungen, Fehler und
  Netzwerkadressen der Teilnehmer, nie den Inhalt einer Sitzung.

Chatnachrichten und Zeichnungen werden nirgends gespeichert: Wenn die Sitzung geschlossen ist, sind sie weg. Ein
Diagnosebericht wird nur gespeichert, wenn Sie ihn anfordern; er enthält das Protokoll und die Einstellungen, also
sehen Sie ihn an, bevor Sie ihn jemandem schicken.

## Was die anderen Teilnehmer erhalten

Die Teilnehmer Ihrer Sitzung sehen Ihren Namen, Ihren Avatar, Ihre Netzwerkadresse (eine direkte Verbindung braucht
sie) und den Fingerabdruck Ihres Geräteschlüssels; mit eingeschalteter Kamera, Mikrofon oder Chat erhalten sie auch
diese. Der Gastgeber liefert den Film an die Teilnehmer, die Teile davon untereinander weitergeben können. Der
Prüfcode wird mündlich verglichen und nirgendwohin gesendet.

## Wohin Golether sich verbindet

| Wohin | Wann | Was die Gegenseite erfährt |
|---|---|---|
| Der Vermittlungsserver des Projekts (seine Adresse kommt aus `config.json` der Projektseite) | wenn ein Gastgeber eine Sitzung über den Server öffnet und wenn ein Teilnehmer einer solchen Sitzung beitritt | die Netzwerkadressen von Gastgeber und Teilnehmer, den Fingerabdruck des Gastgeberschlüssels, das zufällige Kennzeichen des Raums; nie den Inhalt der Sitzung |
| Die Projektseite `depthrel.github.io` (GitHub Pages) | `updates.json` beim Start (die neueste Version; die Prüfung lässt sich in den Einstellungen abschalten); `config.json`, wenn ein Gastgeber eine Sitzung mit automatisch gewähltem Vermittlungsserver öffnet | Ihre IP-Adresse, wie bei jeder Webseite; es gilt die Datenschutzerklärung von GitHub |
| GitHub und `gstreamer.freedesktop.org` | unter Windows nur, wenn Sie eine fehlende Komponente mit der Schaltfläche **Installieren** einrichten | Ihre IP-Adresse |
| Die öffentlichen STUN-Server von Google (`stun.l.google.com`) und Cloudflare (`stun.cloudflare.com`) | nur beim Einrichten eines AmneziaWG-Tunnels: Sie nennen dem Rechner seine Adresse nach außen | Ihre IP-Adresse und Ihren Port; es gelten ihre eigenen Datenschutzerklärungen |
| Ihr Router (UPnP, NAT-PMP) | wenn der Gastgeber den Router bittet, einen Port zu öffnen | nichts außerhalb Ihres lokalen Netzes |

Eine Sitzung geht auch ohne Vermittlungsserver: Wählen Sie im Einladungsfenster die direkte Verbindung oder Ihren
eigenen Server („Vermittlungsserver“ im [Handbuch](UserGuide.md)).

## Die Projektseite

Die Projektseite `depthrel.github.io/Golether` zählt ihre Besuche mit Umami Cloud. Umami verwendet keine Cookies und
speichert nach eigener Beschreibung keine IP-Adressen: Es erfasst die Adresse der Seite (ohne den Teil nach `#` und
`?`), die Seite, von der Sie kamen, den Browser, das System, die Art des Geräts, die Bildschirmgröße, die Sprache und
das Land, das es aus Ihrer Adresse ableitet. Auch ein Klick auf eine Download-Schaltfläche wird gezählt: welches Paket
(Windows, .deb, Archiv, macOS) und von welcher Stelle der Seite. Ein Browser, der darum bittet, nicht verfolgt zu
werden (Do Not Track), wird nicht gezählt. Die Einladungsseite (`/join/`) hat gar keinen Zähler, und die Anwendung
selbst sendet nichts an Umami.

## Der öffentliche Vermittlungsserver

Der Vermittlungsserver des Projekts stellt die beiden Seiten nur einander vor; danach sprechen sie direkt, und der
Server ist nicht mehr beteiligt. Die Liste der offenen Räume hält er im Speicher, solange sie offen sind. Sein
Ereignisprotokoll (Verbindungen, Räume, Fehler, mit Netzwerkadressen) lebt ebenfalls nur im Speicher, enthält eine
begrenzte Zahl der letzten Ereignisse und ist nur für den Administrator auf dem Serverrechner selbst sichtbar. Das
Systemprotokoll des Servers verzeichnet, wann ein Gastgeber einen Raum belegt oder verlässt, mit dem kurzen
Fingerabdruck seines Schlüssels und seiner Netzwerkadresse; diese Zeilen bleiben so lange, wie das System des Servers
sein Protokoll aufbewahrt. Nichts wird verkauft oder an jemanden weitergegeben.

Jeder kann einen eigenen Vermittlungsserver betreiben: [Vermittlungsserver](RendezvousServer.md).

## Fragen

Fragen zu Ihren Daten, Probleme und Ideen gehen an den Betreuer: project.golether@gmail.com. Die Schaltfläche
**Problem melden** (im Fenster „Über Golether“ und im Fenster eines Fehlers) öffnet in Ihrem Mailprogramm einen Brief,
in dem die Version von Golether, das System und die Sprache schon eingetragen sind. Nichts wird gesendet, bevor Sie
den Brief selbst abschicken; er erreicht den Betreuer über Gmail.
