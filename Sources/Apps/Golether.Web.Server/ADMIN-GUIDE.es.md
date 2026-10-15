# Servidor de encuentro de Golether: guía del administrador

Esta guía está en la carpeta `docs` de cada paquete del servidor y sirve igual para Windows, Linux y macOS. Donde los
pasos difieren, se dan para cada sistema por separado. La misma guía en otros idiomas: [English](ADMIN-GUIDE.md),
[Deutsch](ADMIN-GUIDE.de.md), [Français](ADMIN-GUIDE.fr.md), [Português](ADMIN-GUIDE.pt.md), [Русский](ADMIN-GUIDE.ru.md).

- [1. Qué hace el servidor](#1-qué-hace-el-servidor)
- [2. Qué necesitas](#2-qué-necesitas)
- [3. Qué hay en el paquete](#3-qué-hay-en-el-paquete)
- [4. Instalación](#4-instalación)
- [5. Ajustes](#5-ajustes)
- [6. Router y firewall](#6-router-y-firewall)
- [7. Qué decir a los anfitriones](#7-qué-decir-a-los-anfitriones)
- [8. Gestionar el servicio](#8-gestionar-el-servicio)
- [9. Comprobar que funciona](#9-comprobar-que-funciona)
- [10. Actualizar](#10-actualizar)
- [11. Desinstalar](#11-desinstalar)
- [12. Seguridad](#12-seguridad)
- [13. Solución de problemas](#13-solución-de-problemas)

## 1. Qué hace el servidor

Golether es un programa para ver vídeos juntos. Normalmente el anfitrión y los participantes están detrás de routers
domésticos sin puertos abiertos (una IP «gris», el NAT del proveedor) y no pueden alcanzarse directamente. El servidor de
encuentro los presenta:

- el anfitrión mantiene una «sala» de su sesión en el servidor;
- un participante, que llega por el enlace de invitación, pide al servidor que lo junte con el anfitrión;
- el servidor dice a ambos las direcciones públicas del otro, y ellos abren por sí mismos una conexión **directa** a
  través de sus routers;
- en el mismo puerto, pero por UDP, el servidor responde a peticiones STUN: así también se conectan directamente las
  cámaras y la voz.

La película, la sincronización, el chat, las cámaras y la voz **no** pasan por el servidor; solo pasan mensajes de
servicio breves al conectar. La carga es mínima; un servidor atiende cientos de sesiones simultáneas.

## 2. Qué necesitas

| | |
|---|---|
| Red | una dirección IPv4 pública en la máquina, o en un router donde se pueda redirigir un puerto |
| Puertos | **TCP 47900** y **UDP 47900** desde fuera (el número es configurable); el puerto 47980 es solo local |
| Windows | Windows 10 / Windows Server 2016 o posterior, x64 o arm64 |
| Linux | x64 o arm64 con glibc (Debian 11+, Ubuntu 20.04+, RHEL/Alma/Rocky 8+ y similares) y systemd para ejecutarlo como servicio |
| macOS | macOS 14 o posterior, Apple silicon o Intel |
| Recursos | unos 100 MB de disco y 60–100 MB de memoria |

No hace falta instalar .NET: va dentro del paquete.

## 3. Qué hay en el paquete

| Archivo | Para qué |
|---|---|
| `golether-server` (`golether-server.exe` en Windows) | inicia el servidor; el único archivo de la raíz; un mismo programa funciona en una consola y como servicio |
| `bin/` | el propio programa, el runtime de .NET y las bibliotecas; no hace falta mirar ahí |
| `config/appsettings.json` | los ajustes: dirección, puerto, límites (ver la [sección 5](#5-ajustes)) |
| `config/monitor.json` | los ajustes del propio monitor (su idioma); nunca tocan el servidor |
| `docs/ADMIN-GUIDE.md`, `docs/ADMIN-GUIDE.<code>.md` | esta guía en inglés y en alemán (`de`), español (`es`), francés (`fr`), portugués (`pt`) y ruso (`ru`) |
| `docs/README.txt`, `docs/README.<code>.txt` | una nota breve para este paquete concreto, en los mismos idiomas |
| `service/install-service.sh` | Linux y macOS: instala, actualiza y quita el servicio |
| `service/golether-server.service` | Linux: la unidad de systemd (la instala `install-service.sh`) |
| `service/org.golether.server.plist` | macOS: la tarea de launchd (la instala `install-service.sh`) |
| `golether-server-monitor` (`golether-server-monitor.exe` en Windows) | inicia Golether Server Monitor, junto al servidor; el monitor en sí está en `monitor/`. El monitor: el estado del servidor y su registro de eventos en una ventana, iniciar y detener el servicio, los ajustes (ver la [sección 9](#9-comprobar-que-funciona)) |

```
GoletherServer/
  golether-server(.exe)
  golether-server-monitor(.exe)
  bin/        el programa y las bibliotecas
  config/     appsettings.json, monitor.json
  docs/       ADMIN-GUIDE.md, ADMIN-GUIDE.<code>.md, README.txt, README.<code>.txt
  service/    solo Linux y macOS
  monitor/    Golether Server Monitor (los archivos del programa)
```

El instalador de Windows coloca los archivos de la misma forma; sus propios archivos de desinstalación están en la
carpeta `uninstall`.

Para Windows hay además un instalador aparte, `GoletherServer-<version>-setup.exe`.

## 4. Instalación

Elige el camino de tu sistema. En todos los casos hay que abrir los puertos después de instalar
([sección 6](#6-router-y-firewall)).

### Windows: el instalador (recomendado)

Ejecuta `GoletherServer-<version>-setup.exe` (hacen falta permisos de administrador). El asistente habla inglés, alemán,
español, francés, portugués y ruso; su primera ventana ofrece el idioma de Windows (el inglés si el asistente no lo
habla), y puedes elegir otro. El instalador:

- pone el servidor en `C:\Program Files\Golether Server`;
- registra el servicio **GoletherServer** («Golether rendezvous server»): arranca con el equipo y se reinicia 5 segundos
  después de un fallo;
- añade una regla del firewall de Windows para el programa (TCP y UDP, cualquier puerto);
- añade accesos directos a los ajustes, a esta guía y a la página de estado en «Inicio → Golether Server».

El servicio y la regla del firewall se pueden desactivar con las casillas del paso «Seleccione las tareas adicionales».

### Windows: desde el archivo zip

Descomprime el archivo, por ejemplo en `C:\GoletherServer`, y ejecuta `golether-server.exe` desde la raíz de la carpeta:
el servidor funciona mientras la ventana de la consola esté abierta. Para ejecutarlo como servicio, ejecuta en
PowerShell **como administrador** desde la raíz de la carpeta del servidor:

```powershell
$exe = Join-Path (Get-Location) 'golether-server.exe'
New-Service -Name GoletherServer -DisplayName 'Golether rendezvous server' -BinaryPathName "`"$exe`"" -StartupType Automatic
sc.exe failure GoletherServer reset= 86400 actions= restart/5000/restart/5000/restart/60000
New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program $exe -Action Allow
Start-Service GoletherServer
```

No muevas la carpeta después: el servicio inicia el programa por su ruta completa.

### Linux: un servicio systemd

```bash
tar -xzf GoletherServer-<version>-linux-x64.tar.gz
cd GoletherServer
sudo ./service/install-service.sh
```

El script copia el servidor a `/opt/golether-server`, instala la unidad `golether-server.service`, la activa al arrancar
e inicia el servicio. Con `ufw` activado, el script abre él mismo el puerto de los ajustes; para `firewalld` e
`iptables` ver la [sección 6](#6-router-y-firewall). El servicio se ejecuta como un usuario temporal sin privilegios
(`DynamicUser`) con protección del sistema de archivos; el servidor no necesita permisos de root.

Sin servicio: `./golether-server` en la raíz de la carpeta del paquete (`Ctrl+C` lo detiene).

### macOS: un servicio launchd

```bash
tar -xzf GoletherServer-<version>-osx-arm64.tar.gz     # osx-x64 para Intel
cd GoletherServer
sudo ./service/install-service.sh
```

El script copia el servidor a `/usr/local/golether-server`, quita la cuarentena de los archivos descargados, firma el
programa con una firma local (macOS en Apple silicon la exige), instala la tarea
`/Library/LaunchDaemons/org.golether.server.plist` y la inicia. El servidor se ejecuta como el usuario `nobody` y se
reinicia tras un fallo.

Sin servicio: `./golether-server` en la raíz de la carpeta del paquete. Si macOS dice que el programa no se puede abrir,
ejecuta allí `xattr -dr com.apple.quarantine .` y `codesign --force --sign - golether-server bin/golether-server`. En el
primer inicio el firewall de macOS (si está activado) pregunta si permitir conexiones entrantes: permítelas.

## 5. Ajustes

Todos los ajustes están en `config/appsettings.json` dentro de la carpeta del servidor:

| Sistema | Dónde está el archivo tras instalarlo como servicio |
|---|---|
| Windows | `C:\Program Files\Golether Server\config\appsettings.json` |
| Linux | `/opt/golether-server/config/appsettings.json` |
| macOS | `/usr/local/golether-server/config/appsettings.json` |

Si falta el archivo (borrado por error, por ejemplo), el servidor no falla: lo vuelve a escribir con los ajustes por
defecto que se ven abajo y lo indica en su registro. Si no se puede escribir en la carpeta, el servidor se inicia igual
con esos mismos valores por defecto. Una clave que falte en el archivo también toma su valor por defecto.

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

| Parámetro `Rendezvous:…` | Por defecto | Qué define |
|---|---|---|
| `ListenAddress` | `0.0.0.0` | la dirección IP **de esta máquina** en la que escuchar: `0.0.0.0` es cualquier interfaz IPv4, una dirección concreta solo esa, `::` es IPv6 e IPv4 |
| `Port` | `47900` | el puerto: TCP para los encuentros y el mismo número por UDP para STUN |
| `EnableStun` | `true` | si responder a STUN por UDP; sin ello las cámaras y la voz de redes distintas se conectan peor |
| `MaxConnections` | `1024` | conexiones abiertas a la vez |
| `MaxRooms` | `1024` | sesiones en marcha a la vez |
| `MaxPendingPerRoom` | `16` | conexiones que esperan la respuesta de un anfitrión |
| `MaxConnectionsPerMinute` | `120` | conexiones nuevas desde una IP por minuto |
| `MaxStunPerMinute` | `600` | peticiones STUN desde una IP por minuto |
| `FirstMessageTimeout` | `00:00:10` | cuánto esperar el primer mensaje tras una conexión |
| `AnswerTimeout` | `00:00:20` | cuánto puede tardar un anfitrión en responder a la conexión de un participante |
| `IdleTimeout` | `00:01:30` | cuánto puede estar en silencio un anfitrión antes de que se libere su sala |

| Parámetro | Por defecto | Qué define |
|---|---|---|
| `Urls` | `http://127.0.0.1:47980` | la dirección de la página de estado `/health`; deja `127.0.0.1` para que solo se vea en esta máquina |
| `Logging:LogLevel:Default` | `Information` | el nivel de detalle del registro; `Debug` registra cada conexión |
| `Logging:Console:FormatterOptions:TimestampFormat` | `yyyy-MM-dd HH:mm:ss.fff zzz ` | la marca al comienzo de cada línea del registro en la consola y en el archivo: la fecha, la hora del servidor y la diferencia con UTC (`2026-10-11 16:14:12.250 +07:00`). Con systemd y en el Visor de eventos de Windows la hora de una entrada la pone el propio sistema |

Cualquier parámetro también se puede definir sin editar el archivo, con una variable de entorno (un doble guion bajo en
lugar de los dos puntos) o un argumento de línea de comandos; tienen prioridad sobre el archivo:

```bash
./golether-server --Rendezvous:Port=3478                      # argumento, cualquier sistema
Rendezvous__Port=3478 ./golether-server                        # variable, Linux y macOS
```

```powershell
$env:Rendezvous__Port = '3478'; .\golether-server.exe          # variable, Windows
```

**Reinicia el servicio después de editar `config/appsettings.json`** ([sección 8](#8-gestionar-el-servicio)). Si un
ajuste es incorrecto, el servidor no se inicia y enumera los errores (en la consola o en el registro del servicio).

## 6. Router y firewall

**Ambos** protocolos deben ser accesibles desde fuera en el puerto del servidor.

**Router.** Si la IP pública pertenece al router y el servidor está detrás de él, redirige a la dirección local del
servidor:

| Puerto externo | Protocolo | Hacia dónde |
|---|---|---|
| 47900 | TCP | `<IP local del servidor>:47900` |
| 47900 | UDP | `<IP local del servidor>:47900` |

No redirijas el puerto 47980 hacia fuera. En servidores en la nube (VPS) abre los mismos puertos en el grupo de
seguridad o en el firewall del panel del proveedor.

**Firewall del sistema:**

| Sistema | Comandos |
|---|---|
| Windows | lo hace el instalador; a mano: `New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program '<carpeta del servidor>\golether-server.exe' -Action Allow` |
| Linux, ufw | `sudo ufw allow 47900/tcp && sudo ufw allow 47900/udp` (con ufw activado, lo hace `install-service.sh`) |
| Linux, firewalld | `sudo firewall-cmd --permanent --add-port=47900/tcp --add-port=47900/udp && sudo firewall-cmd --reload` |
| Linux, iptables | `sudo iptables -A INPUT -p tcp --dport 47900 -j ACCEPT && sudo iptables -A INPUT -p udp --dport 47900 -j ACCEPT` |
| macOS | «Ajustes del Sistema → Red → Firewall → Opciones»: permitir conexiones entrantes para `golether-server` (si el firewall está activado) |

## 7. Qué decir a los anfitriones

Los anfitriones eligen **«Mi propio servidor»** en el selector «Modo de conexión» de la tarjeta «Mostrar una película»
de Golether y escriben la dirección del servidor en el campo que aparece, como `dirección:puerto`. A los participantes
no hay que decirles nada: la dirección les llega en la invitación.

(La posición «Automáticamente» de ese selector busca el servidor del propio proyecto Golether, no el tuyo: un anfitrión
que quiera usar tu servidor tiene que elegir «Mi propio servidor».)

Da la dirección con la que se ve el servidor **desde Internet**, no `ListenAddress`:

| Dónde está el servidor | Qué escriben los anfitriones |
|---|---|
| Un VPS o una máquina con IP pública en la interfaz | esa IP (o un nombre de dominio): `203.0.113.7:47900` |
| En casa detrás de un router con redirección | la IP externa del router, **no** `192.168.x.x`: `198.51.100.20:47900` |
| Se cambió el puerto | el puerto nuevo: `203.0.113.7:3478` |

Si la IP externa del router cambia, consigue un nombre de DNS dinámico para ella y da ese.

## 8. Gestionar el servicio

| Acción | Windows (PowerShell como administrador) | Linux | macOS |
|---|---|---|---|
| Estado | `Get-Service GoletherServer` | `systemctl status golether-server` | `sudo launchctl print system/org.golether.server` |
| Iniciar | `Start-Service GoletherServer` | `sudo systemctl start golether-server` | `sudo launchctl kickstart system/org.golether.server` |
| Detener | `Stop-Service GoletherServer` | `sudo systemctl stop golether-server` | `sudo launchctl bootout system/org.golether.server` |
| Reiniciar | `Restart-Service GoletherServer` | `sudo systemctl restart golether-server` | `sudo launchctl kickstart -k system/org.golether.server` |
| Registro | «Visor de eventos → Registros de Windows → Aplicación», origen `golether-server` | `journalctl -u golether-server -f` | `tail -f /Library/Logs/golether-server.log` |

Tras detenerlo con `bootout` en macOS, el servicio se vuelve a iniciar con
`sudo launchctl bootstrap system /Library/LaunchDaemons/org.golether.server.plist` (o reiniciando).

Lo mismo en Windows desde la línea de comandos clásica (`cmd` como administrador):

| Acción | Comando |
|---|---|
| Estado | `sc query GoletherServer` |
| Iniciar | `sc start GoletherServer` |
| Detener | `sc stop GoletherServer` |

El programa `Golether Server Monitor` hace todo esto con botones y muestra lo que hace el servidor; ver la
[sección 9](#9-comprobar-que-funciona).

**El servicio vuelve solo.** Arranca con el equipo, y el instalador le dijo a Windows que lo reiniciara 5 segundos
después de un fallo. Terminar el proceso en el Administrador de tareas cuenta como un fallo, así que un minuto después el
servidor vuelve a estar en marcha. Detenlo como servicio (los comandos de arriba o `services.msc`).

**`golether-server.exe` iniciado desde una consola dice que el puerto está ocupado.** Normalmente lo ocupa el propio
servicio, que ya está en marcha. No hace falta una segunda copia: el servicio hace el mismo trabajo. Para ejecutar el
servidor en una consola (para ver sus mensajes, por ejemplo), detén primero el servicio y vuelve a iniciarlo después.

## 9. Comprobar que funciona

### Golether Server Monitor

Lo más sencillo es el monitor: «Golether Server Monitor» en el menú Inicio en Windows, `golether-server-monitor` junto a
`golether-server` en la carpeta del servidor en Linux (hace falta un escritorio) y macOS. Muestra:

- **el estado** con palabras: en marcha como servicio, en marcha a mano (no como servicio), detenido, el servicio no está
  instalado; cuánto tiempo lleva en marcha el servidor, las salas, las conexiones, los encuentros y los rechazos;
- **el registro de eventos** en columnas: hora | dirección IP | evento | detalles (el anfitrión, una etiqueta de la sala,
  por qué se rechazó una petición). Los eventos más nuevos van arriba; el cuadro de búsqueda encuentra una dirección, un
  anfitrión, una sala o un evento; los separadores de la cabecera cambian el ancho de las columnas; un clic derecho copia
  las filas seleccionadas;
- los botones **Iniciar** y **Detener** del servicio y **Exportar el registro** a un archivo;
- la pestaña **Ajustes**: los campos de `config/appsettings.json` con explicaciones, comprobados con las mismas reglas
  que al iniciarse el servidor. «Guardar y reiniciar» escribe el archivo y reinicia un servicio en marcha.

El idioma del monitor (English, Deutsch, Español, Français, Português o Русский, cada uno con su bandera) se elige en su
ventana y cambia al momento. Se recuerda en `config/monitor.json` junto a los ajustes del servidor (un archivo propio del
monitor: cambiarlo nunca reinicia el servidor), y cualquier usuario puede cambiarlo. En el primer inicio el monitor habla
el idioma del sistema (el inglés si no habla ese).

Iniciar, detener y guardar necesitan permisos de administrador: Windows los pide con UAC, Linux con `pkexec`, macOS pide
la contraseña. Para ver no hacen falta permisos especiales.

El registro se guarda en la memoria del servidor (los últimos 5000 eventos) y empieza vacío en cada inicio del servidor.
Nada se escribe en disco salvo que pulses **Exportar el registro**: entonces exactamente lo que muestra el monitor va al
archivo que elijas. El monitor lee el registro por la dirección de estado de este equipo; otros equipos no pueden
leerlo.

### A mano

**En la propia máquina**, la página de estado:

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

| Campo | Significado |
|---|---|
| `rooms` | sesiones en marcha con este servidor |
| `connections` | conexiones abiertas |
| `meetingsRequested` / `meetingsIntroduced` | cuántas conexiones pidieron los participantes / a cuántos lados pasó el servidor las direcciones del otro |
| `refused` | rechazos por los límites |
| `stunAnswered` | respuestas STUN (crece mientras hay sesiones con cámaras) |

**Desde fuera**, desde otra máquina de otra red (por ejemplo, un portátil con el móvil compartiendo Internet):

```bash
nc -vz 203.0.113.7 47900                                         # Linux, macOS: ¿está abierto el puerto TCP?
```

```powershell
Test-NetConnection 203.0.113.7 -Port 47900                        # Windows: TcpTestSucceeded : True
```

UDP se comprueba con un cliente STUN, por ejemplo `stunclient 203.0.113.7 47900` (el paquete `stuntman-client` en
Linux). La comprobación final: un anfitrión indica el servidor y crea una sesión, y la lista de eventos de Golether
muestra «El servidor de encuentro está listo».

## 10. Actualizar

| Sistema | Cómo actualizar |
|---|---|
| Windows | ejecuta el instalador de la versión nueva: detiene el servicio, sustituye los archivos y lo vuelve a iniciar; `config\appsettings.json` se conserva |
| Windows, zip | detén el servicio, sustituye todo salvo la carpeta `config`, inicia el servicio |
| Linux, macOS | descomprime la versión nueva y vuelve a ejecutar `sudo ./service/install-service.sh`; `config/appsettings.json` se conserva |

Distintas versiones del servidor y de la aplicación son compatibles mientras la versión del protocolo sea la misma (el
campo `protocol` de `/health`).

## 11. Desinstalar

| Sistema | Cómo desinstalar |
|---|---|
| Windows | «Configuración → Aplicaciones → Golether Server → Desinstalar»: se quitan el servicio y la regla del firewall |
| Windows, zip | `Stop-Service GoletherServer; sc.exe delete GoletherServer; Remove-NetFirewallRule -DisplayName 'Golether rendezvous server'`, después borra la carpeta |
| Linux, macOS | `sudo ./service/install-service.sh uninstall` desde la carpeta del paquete o desde la carpeta instalada |

No olvides cerrar la redirección de puertos en el router.

## 12. Seguridad

- **El servidor no ve las sesiones.** Todo lo que viaja entre el anfitrión y los participantes va cifrado entre sus
  dispositivos; el servidor solo conoce direcciones IP, las claves públicas de los anfitriones y la hora de las
  conexiones. El servidor no puede hacerse pasar por un anfitrión: el participante comprueba la clave del anfitrión de
  la invitación.
- **Una sala no se puede usurpar:** está ligada a la clave del dispositivo del anfitrión, y el registro va firmado.
- **Permisos mínimos.** El servicio en Linux y macOS no necesita permisos de root (un usuario temporal / `nobody`); en
  Windows el servicio se ejecuta con la cuenta del sistema, como la mayoría de los servicios.
- **Los límites** del número de conexiones, de salas y de la frecuencia de peticiones desde una IP protegen contra la
  sobrecarga. Ante un ataque, baja `MaxConnectionsPerMinute` y `MaxStunPerMinute`.
- **La página `/health`** no contiene direcciones ni nombres; aun así, mantenla en `127.0.0.1`.
- **El registro de eventos** (`/journal`, que lee el monitor) contiene las direcciones IP de los dispositivos, los
  identificadores cortos de los anfitriones y etiquetas de las salas (no sus nombres: una etiqueta no permite a nadie
  llegar al anfitrión). Solo se responde a este equipo, sea cual sea la dirección de la página de estado, y vive solo en
  memoria.
- El servidor no guarda nada en disco salvo el registro. Solo la carpeta `config` necesita copia de seguridad.
- Actualiza el servidor junto con la aplicación Golether.

## 13. Solución de problemas

| Lo que ves | Qué comprobar |
|---|---|
| El servicio no se inicia | el registro del servicio ([sección 8](#8-gestionar-el-servicio)): un ajuste incorrecto, o el servidor no puede escuchar porque otro programa ocupa el puerto (`netstat -ano \| findstr 47900` en Windows, `sudo ss -lntup \| grep 47900` en Linux, `sudo lsof -i :47900` en macOS) |
| El servidor iniciado desde una consola dice que el puerto está ocupado, y vuelve después de terminarlo en el Administrador de tareas | el servicio ya está en marcha y se reinicia tras cada fallo: detenlo como servicio ([sección 8](#8-gestionar-el-servicio)) |
| El anfitrión ve que no se llega al servidor de encuentro | el servicio está en marcha; `/health` responde; el puerto TCP está redirigido y abierto ([sección 9](#9-comprobar-que-funciona), la comprobación desde fuera); al anfitrión se le dio la dirección externa, no la local |
| Un participante ve que el anfitrión no está en el servidor de encuentro | la sesión sigue en marcha; la lista de eventos del anfitrión dijo que el servidor estaba listo; `rooms` en `/health` es mayor que cero |
| Los routers no dejaron pasar la conexión directa | el servidor hizo su parte (`meetingsIntroduced` crece), pero la red de uno de los lados no permite conexiones directas (un NAT simétrico, habitual en operadores móviles). El servidor no puede arreglarlo: hace falta un túnel AmneziaWG o un puerto redirigido en el lado del anfitrión |
| La película va, no hay cámaras | el puerto UDP está redirigido y abierto; `EnableStun` = `true`; `stunAnswered` crece |
| `refused` crece rápido | alguien choca con los límites: muchos participantes detrás de una IP, así que sube `MaxConnectionsPerMinute`; actividad sospechosa, así que mira el registro en el nivel `Debug` |
| macOS: el programa no se puede abrir | en la raíz de la carpeta del servidor: `xattr -dr com.apple.quarantine .` y `codesign --force --sign - golether-server bin/golether-server` |
| Linux: `Permission denied` al iniciar | en la raíz de la carpeta del servidor: `chmod +x golether-server bin/golether-server service/install-service.sh` (si el archivo se descomprimió en Windows y se copió) |
