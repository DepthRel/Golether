# Golether: privacidad

Golether no tiene cuentas, ni publicidad, ni telemetría: no recoge estadísticas sobre usted ni sobre lo que ve. La
película, las voces, las cámaras, el chat y los dibujos van directamente entre los ordenadores de los participantes,
cifrados, y ningún servidor del proyecto los ve.

Esta página enumera todo lo que Golether guarda y cada dirección a la que se conecta.

## Lo que se queda en su ordenador

Todo lo siguiente está en la carpeta `data` de la aplicación (vea «Dónde se guardan los datos» en la
[guía de uso](UserGuide.md)) y nunca sale del ordenador por sí solo:

- la clave del dispositivo: demuestra a los demás que es el mismo dispositivo;
- los contactos: el nombre y la huella de la clave de cada dispositivo cuyo código comparó, si confía en él, su última
  dirección de red, sus notas y cuándo se vio por primera y por última vez;
- los ajustes, los túneles AmneziaWG con sus claves, los componentes descargados;
- el registro de la aplicación en `data/logs`, guardado 7 días. Contiene conexiones, errores y direcciones de red de
  los participantes, nunca el contenido de una sesión.

Los mensajes del chat y los dibujos no se guardan en ninguna parte: al cerrar la sesión, desaparecen. Un informe de
diagnóstico se guarda solo cuando usted lo pide; contiene el registro y los ajustes, así que revíselo antes de
enviarlo a alguien.

## Lo que reciben los demás participantes

Los participantes de su sesión ven su nombre, su avatar, su dirección de red (una conexión directa la necesita) y la
huella de la clave de su dispositivo; con la cámara, el micrófono o el chat activados, también reciben esos. El
anfitrión sirve la película a los participantes, que pueden pasarse partes de ella entre sí. El código de
verificación se compara de viva voz y no se envía a ninguna parte.

Cuando el anfitrión muestra una carpeta, los participantes reciben copias de sus imágenes (solo en memoria) y ven los
nombres de los archivos y de las subcarpetas. Con **No mostrar el lugar de la toma, la cámara ni los comentarios de las
imágenes** activado (por defecto), las copias pierden los bloques EXIF, XMP e IPTC, los comentarios y las imágenes que
algunos teléfonos añaden tras una foto; solo se conserva la orientación. Los originales del disco del anfitrión nunca
cambian. Los vídeos de la carpeta se sirven como una película.

## A dónde se conecta Golether

| A dónde | Cuándo | Qué sabe la otra parte |
|---|---|---|
| El servidor de encuentro del proyecto (su dirección viene de `config.json` de la página del proyecto) | cuando un anfitrión abre una sesión a través del servidor y cuando un participante se une a esa sesión | las direcciones de red del anfitrión y del participante, la huella de la clave del anfitrión, la etiqueta aleatoria de la sala; nunca el contenido de la sesión |
| La página del proyecto `depthrel.github.io` (GitHub Pages) | `updates.json` al iniciar (la versión más reciente; la comprobación se puede desactivar en los ajustes); `config.json` cuando un anfitrión abre una sesión con el servidor de encuentro elegido automáticamente | su dirección IP, como en cualquier página web; se aplica la declaración de privacidad de GitHub |
| GitHub y `gstreamer.freedesktop.org` | en Windows, solo cuando instala un componente que falta con el botón **Instalar** | su dirección IP |
| Los servidores STUN públicos de Google (`stun.l.google.com`) y Cloudflare (`stun.cloudflare.com`) | solo al configurar un túnel AmneziaWG: le dicen al ordenador su dirección exterior | su dirección IP y su puerto; se aplican sus propias políticas de privacidad |
| Su router (UPnP, NAT-PMP) | cuando el anfitrión pide al router que abra un puerto | nada fuera de su red local |

Una sesión también funciona sin el servidor de encuentro: elija la conexión directa o su propio servidor en la
ventana de invitación («Servidor de encuentro» en la [guía de uso](UserGuide.md)).

## La página del proyecto

La página del proyecto `depthrel.github.io/Golether` cuenta sus visitas con Umami Cloud. Umami no usa cookies y, según
su propia descripción, no guarda direcciones IP: registra la dirección de la página (sin la parte tras `#` y `?`), la
página de la que viene, el navegador, el sistema, el tipo de dispositivo, el tamaño de la pantalla, el idioma y el
país, que deduce de su dirección. También se cuenta la pulsación de un botón de descarga: qué paquete (Windows, .deb,
archivo, macOS) y desde qué lugar de la página. Un navegador que pide no ser rastreado (Do Not Track) no se cuenta. La
página de invitación (`/join/`) no tiene ningún contador, y la aplicación misma no envía nada a Umami.

## El servidor de encuentro público

El servidor de encuentro del proyecto solo presenta las dos partes entre sí; después hablan directamente y el
servidor ya no participa. Mantiene la lista de salas abiertas en memoria mientras están abiertas. Su registro de
eventos (conexiones, salas, fallos, con direcciones de red) también vive solo en memoria, guarda un número limitado
de los últimos eventos y solo lo ve el administrador en la propia máquina del servidor. El registro del sistema del
servidor anota cuándo un anfitrión ocupa o deja una sala, con la huella corta de su clave y su dirección de red;
esas líneas permanecen mientras el sistema del servidor conserve su registro. Nada se vende ni se pasa a nadie.

Cualquiera puede tener su propio servidor de encuentro: [Servidor de encuentro](RendezvousServer.md).

## Preguntas

Las preguntas sobre sus datos, los problemas y las ideas se dirigen al responsable del proyecto:
project.golether@gmail.com. El botón **Informar de un problema** (en la ventana «Acerca de Golether» y en la ventana
de un error) abre en su programa de correo una carta con la versión de Golether, el sistema y el idioma ya rellenados.
No se envía nada hasta que usted mismo envíe la carta; llega al responsable a través de Gmail.
