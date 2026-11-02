# Android-Emulator starten

So bereiten Sie einen Windows-Computer darauf vor, den Android-Client von Golether im Emulator auszuführen. Android
Studio wird nicht benötigt. Die Skripte bringen das Gerät selbst in einen funktionsfähigen Zustand: Tastatur,
Navigationstasten und Grafikbeschleunigung funktionieren direkt nach der Installation.

## Schnellstart

Sie brauchen Windows 10 oder 11 (64 Bit), eingeschaltete Virtualisierung in den BIOS/UEFI-Einstellungen und etwa 10 GB
freien Speicherplatz. Starten Sie die Skripte aus `Scripts/Android` in dieser Reihenfolge; ein Doppelklick genügt:

| Schritt | Skript | Administratorrechte | Was es tut |
|---|---|---|---|
| 1 | `enable-windows-features.bat` | ja, es fragt selbst | Schaltet die Windows-Hypervisor-Plattform ein. Danach **Windows neu starten** |
| 2 | `install-android-tools.bat` | ja, es fragt selbst | Installiert den .NET-Android-Workload, das JDK, das Android SDK und den Emulator: etwa 6 GB, Downloads werden per SHA-256 geprüft |
| 3 | `create-emulator.bat` | nein | Legt das Gerät `Golether-API36` an, startet es und legt eine Verknüpfung auf dem Desktop ab |

Danach startet der Emulator über die Verknüpfung **Android Emulator (Golether-API36)** oder mit `start-emulator.bat`. Er bootet jedes Mal neu: ein Snapshot für den Schnellstart würde 4 GB belegen und bei jedem Beenden neu geschrieben.
Nach einem fehlerhaften Beenden fragt der Emulator, ob er einen Absturzbericht senden soll, und wartet mit
ausgeschaltetem Gerät auf die Antwort; schließen Sie dieses Fenster.

Über die Befehlszeile fährt das Gerät mit `adb shell reboot -p` sauber herunter; `adb emu kill` gilt als fehlgeschlagenes Beenden. Das Gerät hat nur eine Rückkamera: Um den Wechsel zwischen zwei Kameras zu prüfen, starten Sie den Emulator einmal mit `-camera-front emulated`.

Die Schritte 2 und 3 lassen sich wiederholen: Installiertes bleibt, ein vorhandenes Gerät behält seine Daten.

## Was wohin kommt

| Was | Wohin |
|---|---|
| .NET-Android-Workload | Der Ordner von .NET, `C:\Program Files\dotnet\packs`: ein Workload liegt immer dort |
| Microsoft OpenJDK 21 | `C:\Program Files\AndroidTools\jdk-21` |
| Android SDK, Emulator, Systemabbild | `C:\Program Files\AndroidTools\sdk` |
| Virtuelle Geräte | `%USERPROFILE%\.android\avd`: Der Emulator schreibt dort während des Betriebs, deshalb können sie nicht in Program Files liegen |

`JAVA_HOME` und `ANDROID_HOME` werden für alle Benutzer gesetzt. Starten Sie Terminals und Visual Studio neu, damit sie
sichtbar werden.

Zwei Variablen ändern die Ordner: `ANDROID_TOOLS_ROOT` für die Werkzeuge und `ANDROID_AVD_HOME` für die Geräte. Mit
`ANDROID_TOOLS_ROOT` sind keine Administratorrechte nötig (wenn der Workload installiert ist), `JAVA_HOME` und
`ANDROID_HOME` setzen Sie dann aber selbst.

## Die Einstellungen des Geräts

`avdmanager` legt ein Gerät an, mit dem sich schlecht arbeiten lässt, deshalb ändert `create-emulator.bat` es:

| Einstellung | Standard | Vom Skript gesetzt | Warum |
|---|---|---|---|
| `hw.keyboard` | `no` | `yes` | Sonst gibt die Tastatur des Computers nichts ein |
| `hw.gpu.enabled` | `no` | `yes` | Sonst zeichnet der Prozessor das Bild: Das System ruckelt, und Video hat kein OpenGL ES |
| `hw.gpu.mode` | `auto` | `auto` | Die Grafikkarte, wenn möglich, auf einem Computer ohne sie zeichnet der Prozessor |
| `hw.ramSize` | `2G` | `4G` | 2 GB sind für Android 16 mit Google APIs zu wenig |
| Navigation | Gesten | drei Tasten | Bei Gesten gibt es keine Tasten auf dem Bildschirm, und mit der Maus sind Gesten unbequem |

Die ersten Einstellungen sind Zeilen in der `config.ini` des Geräts. Die Navigation wird im laufenden System gesetzt und
bleibt dort erhalten:

```powershell
adb shell cmd overlay enable-exclusive --category com.android.internal.systemui.navbar.threebutton
```

## Arbeiten mit dem Emulator

- Zusammen mit dem Emulator startet ein minimiertes Konsolenfenster. Wird es geschlossen, schließt sich der Emulator.
- Der Computer ist aus dem Emulator unter `10.0.2.2` erreichbar.
- Solange die physische Tastatur eingeschaltet ist, erscheint die Bildschirmtastatur nicht: Das ist normal.
- Der Emulator zeigt weder die echte Hardware-Dekodierung von Video noch die Wärme und die Geschwindigkeit eines
  Telefons: Dafür braucht man ein echtes Telefon, das per USB mit eingeschaltetem USB-Debugging verbunden ist.
- `sdkmanager` und `adb` stehen nicht im `PATH`. Sie liegen in `C:\Program Files\AndroidTools\sdk`, in
  `cmdline-tools\latest\bin` und `platform-tools`. `sdkmanager` schreibt nach Program Files und braucht daher ein
  Terminal mit Administratorrechten.

## Den Client bauen und starten

Die Android-Projekte gehören nicht zum Build von `Golether.slnx`: Er muss ohne den Android-Workload funktionieren. Sie
werden einzeln gebaut und brauchen die nativen Komponenten in `Native/android-*` (siehe `Native/README.md`):

```powershell
dotnet build Sources/Apps/Golether.Mobile.Android -t:Install
```

Mit `-p:GoletherDeviceChecks=true` enthält das Paket zusätzlich Prüfungen dessen, was sich nur auf einem Gerät beweisen
lässt: Daten, Schlüssel, TLS, libmpv, GStreamer. Sie werden über ein Extra der Activity gestartet und melden sich in
logcat mit dem Tag `GOLETHER-CHECK`:

```powershell
adb shell am start -n depthrel.golether/depthrel.golether.MainActivity --ez golether.checks true --ez golether.conference true
adb logcat -s GOLETHER-CHECK
```

## Probleme

| Was Sie sehen | Was es bedeutet |
|---|---|
| `sdkmanager` wird nicht erkannt | Es steht nicht im `PATH`: Verwenden Sie den vollen Pfad oder `install-android-tools.bat` |
| Zugriff verweigert, während `sdkmanager` installiert | Das Terminal läuft nicht mit Administratorrechten |
| `WARNING: The SDK Manager CLI tool (sdkmanager) is deprecated` | Eine Warnung, kein Fehler |
| `Error: Could not load devices from ...\devices.xml` von `avdmanager` | Harmlos: Das Abbild hat diese Datei nicht, das Gerät wird angelegt |
| Die Tastatur gibt nichts ein oder das System ruckelt | Das Gerät wurde von Hand angelegt: Starten Sie `create-emulator.bat`, es behält die Daten und korrigiert die Einstellungen |
| Der Emulator ist sehr langsam, Schritt 1 wurde ausgelassen | Starten Sie `enable-windows-features.bat` und dann Windows neu. Prüfen mit `emulator -accel-check` |
| `enable-windows-features.bat` schlägt fehl | Die Virtualisierung ist in den BIOS/UEFI-Einstellungen ausgeschaltet |
