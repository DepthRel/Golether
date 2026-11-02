# Inicio del emulador de Android

Cómo preparar un equipo con Windows para ejecutar el cliente Android de Golether en un emulador. No hace falta Android
Studio. Los scripts dejan el dispositivo en estado de funcionamiento por sí mismos: el teclado, los botones de
navegación y la aceleración gráfica funcionan justo después de la instalación.

## Inicio rápido

Se necesita Windows 10 u 11 (64 bits), la virtualización activada en la configuración de BIOS/UEFI y unos 10 GB de
espacio libre. Ejecute los scripts de `Scripts/Android` en este orden; basta con un doble clic:

| Paso | Script | Permisos de administrador | Qué hace |
|---|---|---|---|
| 1 | `enable-windows-features.bat` | sí, los pide | Activa la Plataforma del hipervisor de Windows. Después **reinicie Windows** |
| 2 | `install-android-tools.bat` | sí, los pide | Instala el workload .NET Android, el JDK, el Android SDK y el emulador: unos 6 GB, las descargas se comprueban con SHA-256 |
| 3 | `create-emulator.bat` | no | Crea el dispositivo `Golether-API36`, lo inicia y deja un acceso directo en el escritorio |

Después el emulador se inicia con el acceso directo **Android Emulator (Golether-API36)** o con `start-emulator.bat`. Arranca desde cero cada vez: una instantánea para el inicio rápido ocuparía 4 GB y se reescribiría en cada salida.
Tras una salida fallida, el emulador pregunta si debe enviar un informe de error y espera la respuesta con el
dispositivo apagado; cierra esa ventana.

Desde la línea de comandos el dispositivo se apaga limpiamente con `adb shell reboot -p`; `adb emu kill` cuenta como una salida fallida. El dispositivo solo tiene cámara trasera: para probar el cambio entre dos cámaras, inicie el emulador una vez con `-camera-front emulated`.

Los pasos 2 y 3 se pueden repetir: lo instalado se queda, y un dispositivo existente conserva sus datos.

## Qué va a dónde

| Qué | Dónde |
|---|---|
| Workload .NET Android | La carpeta de .NET, `C:\Program Files\dotnet\packs`: un workload siempre va ahí |
| Microsoft OpenJDK 21 | `C:\Program Files\AndroidTools\jdk-21` |
| Android SDK, emulador, imagen del sistema | `C:\Program Files\AndroidTools\sdk` |
| Dispositivos virtuales | `%USERPROFILE%\.android\avd`: el emulador escribe ahí mientras funciona, por eso no pueden estar en Program Files |

`JAVA_HOME` y `ANDROID_HOME` se definen para todos los usuarios. Reinicie los terminales y Visual Studio para verlas.

Dos variables cambian las carpetas: `ANDROID_TOOLS_ROOT` para las herramientas y `ANDROID_AVD_HOME` para los
dispositivos. Con `ANDROID_TOOLS_ROOT` no hacen falta permisos de administrador (si el workload está instalado), pero
entonces `JAVA_HOME` y `ANDROID_HOME` se definen a mano.

## Los ajustes del dispositivo

`avdmanager` crea un dispositivo incómodo para trabajar, por eso `create-emulator.bat` lo cambia:

| Ajuste | Por defecto | Puesto por el script | Por qué |
|---|---|---|---|
| `hw.keyboard` | `no` | `yes` | Si no, el teclado del equipo no escribe nada |
| `hw.gpu.enabled` | `no` | `yes` | Si no, la imagen la dibuja el procesador: el sistema va lento y el vídeo no tiene OpenGL ES |
| `hw.gpu.mode` | `auto` | `auto` | La tarjeta gráfica cuando es posible; en un equipo sin ella dibuja el procesador |
| `hw.ramSize` | `2G` | `4G` | 2 GB son poco para Android 16 con Google APIs |
| Navegación | gestos | tres botones | Con gestos no hay botones en la pantalla, y con el ratón los gestos son incómodos |

Los primeros ajustes son líneas de `config.ini` del dispositivo. La navegación se define en el sistema en marcha y se
queda allí:

```powershell
adb shell cmd overlay enable-exclusive --category com.android.internal.systemui.navbar.threebutton
```

## Trabajar con el emulador

- Junto con el emulador se inicia una ventana de consola minimizada. Si se cierra, se cierra el emulador.
- Desde el emulador, el equipo está en `10.0.2.2`.
- Mientras el teclado físico está activado, el de pantalla no aparece: es normal.
- El emulador no muestra la decodificación de vídeo por hardware real, ni el calor ni la velocidad de un teléfono: para
  eso hace falta un teléfono real conectado por USB con la depuración USB activada.
- `sdkmanager` y `adb` no están en `PATH`. Están en `C:\Program Files\AndroidTools\sdk`, en `cmdline-tools\latest\bin` y
  `platform-tools`. `sdkmanager` escribe en Program Files, así que necesita un terminal como administrador.

## Compilar y ejecutar el cliente

Los proyectos de Android no forman parte de la compilación de `Golether.slnx`: debe funcionar sin el workload de
Android. Se compilan aparte y necesitan los componentes nativos en `Native/android-*` (véase `Native/README.md`):

```powershell
dotnet build Sources/Apps/Golether.Mobile.Android -t:Install
```

Con `-p:GoletherDeviceChecks=true` el paquete incluye además comprobaciones de lo que solo se puede demostrar en un
dispositivo: datos, claves, TLS, libmpv, GStreamer. Se inician con un extra de la actividad e informan en logcat con la
etiqueta `GOLETHER-CHECK`:

```powershell
adb shell am start -n depthrel.golether/depthrel.golether.MainActivity --ez golether.checks true --ez golether.conference true
adb logcat -s GOLETHER-CHECK
```

## Problemas

| Qué se ve | Qué significa |
|---|---|
| `sdkmanager` no se reconoce | No está en `PATH`: use la ruta completa o `install-android-tools.bat` |
| Acceso denegado mientras `sdkmanager` instala | El terminal no se ha ejecutado como administrador |
| `WARNING: The SDK Manager CLI tool (sdkmanager) is deprecated` | Una advertencia, no un error |
| `Error: Could not load devices from ...\devices.xml` de `avdmanager` | Inofensivo: la imagen no tiene ese archivo, el dispositivo se crea |
| El teclado no escribe nada o el sistema va lento | El dispositivo se creó a mano: ejecute `create-emulator.bat`, conserva los datos y corrige los ajustes |
| El emulador va muy lento, se saltó el paso 1 | Ejecute `enable-windows-features.bat` y reinicie Windows. Compruébelo con `emulator -accel-check` |
| `enable-windows-features.bat` falla | La virtualización está desactivada en la configuración de BIOS/UEFI |
