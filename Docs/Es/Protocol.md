# Protocolo de red de Golether (versión 1)

## Transporte

TCP, el puerto del anfitrión es **47800** por defecto. Cada conexión:

1. TLS 1.3 (TLS 1.2 en Windows 10) con certificados en ambos lados. La validación de la cadena y del nombre está
   desactivada: el participante comprueba el `PeerId` del certificado del anfitrión con la invitación, el anfitrión
   calcula el `PeerId` del participante.
2. El preámbulo del cliente, 6 bytes: `47 4C 54 48` (`GLTH`), versión `01`, propósito (`01` Control, `02` MediaData,
   `03` Relay, `04` RelayShared).
3. La respuesta del anfitrión, 1 byte: `01` significa aceptado.

`PeerId` = SHA-256 en hexadecimal en minúsculas del SubjectPublicKeyInfo DER de la clave del dispositivo (ECDSA P-256).

## Control

Tramas: 4 bytes de longitud (big-endian) y JSON UTF-8, como máximo 256 KiB. El tipo lo da el campo `$t`; los nombres de
los campos van en camelCase. A es el anfitrión, P un participante.

| `$t` | Dirección | Campos | Significado |
|---|---|---|---|
| `hello` | P → A | `version`, `displayName`, `inviteToken`, `gallery` | el primer mensaje |
| `pending` | A → P | — | esperando la decisión del anfitrión |
| `welcome` | A → P | `sessionName`, `participants[]`, `playback`, `media`, `mediaDataStreams`, `relay`, `relayOverSession`, `relayShared`, `reconnectTicket` | el participante ha sido admitido; `relay` son el puerto y las credenciales del TURN del anfitrión, creados para una sesión; `relayOverSession` significa que el anfitrión acepta TURN sobre flujos `Relay` y no hace falta un puerto aparte; `relayShared` significa que también los acepta todos en un flujo `RelayShared`; `reconnectTicket` es el billete para volver a esta sesión sin una invitación nueva |
| `rejected` | A → P | `reason`, `message` | rechazo, la conexión se cierra |
| `ping` | P → A | `clientSendTime` | una sonda de reloj (t0, µs) |
| `pong` | A → P | `clientSendTime`, `hostReceiveTime`, `hostSendTime` | t0, t1, t2 |
| `request` | P → A | `request: {kind, position}` | una intención: `0` reproducir, `1` pausar, `2` saltar, `3` reproducir sin esperar a los que no están listos |
| `state` | A → todos | `state: PlaybackState` | el estado con autoridad |
| `status` | P → A | `status: ParticipantStatus` | una vez por segundo; el anfitrión sustituye `peerId` por el verificado |
| `participants` | A → todos | `participants[]`, `statuses[]` | una vez por segundo y en los cambios |
| `media` | A → todos | `media: MediaDescriptor` | un archivo nuevo |
| `rtc` | cualquiera | `peer`, `kind`, `payload` | señalización WebRTC; el anfitrión la reenvía poniendo el remitente verificado |
| `moderate` | A → P | `microphone`, `camera` | el anfitrión apaga los dispositivos de un participante (solo apagar) |
| `chat` | P → A → todos | `id`, `kind` (`0` texto, `1` reacción), `text`, `sender` | un mensaje del chat o una reacción; el anfitrión rellena `sender` con el identificador verificado, el valor enviado se descarta |
| `draw` | P → A → todos | `strokeId`, `phase` (`0` inicio, `1` continuación, `2` fin), `points[]` (`x`, `y` como fracciones de la imagen), `sender` | un trazo del lápiz sobre el vídeo; el anfitrión rellena `sender` con el identificador verificado |
| `avatar` | P → A → todos | `image` (PNG, base64; vacío restablece), `sender` | la imagen de un participante, mostrada con la cámara apagada; el anfitrión rellena `sender`; un participante nuevo recibe del anfitrión los avatares ya elegidos |
| `gallery` | A → P | `catalogId`, `title`, `total`, `offset`, `items[]` | una página de una carpeta mostrada; un `catalogId` vacío termina la muestra |
| `show` | P → A → todos | `catalogId`, `index`, `version`, `sender` | el elemento en la pantalla de todos; el anfitrión numera la decisión y rellena `sender` |
| `thumb` | P → A → P | `catalogId`, `index`, `image` (JPEG, base64) | la petición de una miniatura (sin `image`) y la respuesta |
| `bye` | cualquiera | `reason` | salida; `reason` siempre va vacío y el receptor lo ignora |

`PlaybackState`: `state` (`0` en pausa, `1` reproduciendo), `position` (`hh:mm:ss.fffffff`), `referenceTime` (µs según
el reloj del anfitrión), `rate`, `version`, `origin`, `cause` (`0` inicial, `1` reproducir, `2` pausa, `3` salto,
`4` esperando a los participantes, `5` todos están listos, `6` la película terminó, `7` reproducir sin esperar a los que
no están listos).

Todo lo que llega por la red se comprueba antes de usarlo:

- **Un estado** lo acepta un participante solo si el modo es conocido, la posición está entre cero y 30 días y `rate` es
  un número finito de 0,5 a 2. Si no, el estado se omite (en `welcome`, se rechaza la entrada).
- **Una intención** con un `kind` desconocido la descarta el anfitrión. La posición pedida se limita a la duración del
  archivo y, mientras esta no se conozca, a los mismos 30 días.
- **El estado de un participante** lo acota el anfitrión antes de cualquier cálculo o difusión: tiempos dentro de
  ±30 días, ping de 0 a 600 000 ms, contadores de bytes no negativos. Un participante acota de la misma forma los estados
  de `participants`.
- **El nombre del archivo** en `media` tiene como máximo 255 caracteres, sin caracteres de control ni separadores de ruta
  (`/`, `\`). La regla es la misma en todos los sistemas: el anfitrión sustituye esos caracteres por `_` antes de enviar,
  y un nombre válido en su sistema (con dos puntos en Linux, por ejemplo) lo aceptan los demás. El nombre solo se
  muestra; nunca sirve como ruta.

Motivos de un rechazo: `0` invitación no válida, `1` rechazado, `2` el anfitrión no respondió, `3` la sesión está llena,
`4` versión incompatible, `5` expulsado por el anfitrión. Un mensaje `rejected` con el motivo `5` también llega en mitad
de una sesión: así expulsa el anfitrión a un participante admitido.

El campo `message` de `rejected` es una nota breve para el registro (el nombre del motivo en inglés); el participante no
lo muestra, sino que formula el motivo en el idioma de su propia interfaz a partir del campo `reason`. El anfitrión envía
`bye` sin `reason` por la misma razón: los textos para personas no viajan por la red. Un participante ignora el campo
aunque un anfitrión de otra compilación lo rellene, y formula él mismo el final de la sesión.

El orden de una conexión: `hello` → (`pending`) → `welcome` | `rejected`. El token de una invitación sirve hasta que
termina su validez y vale para todos los invitados, pero trae como máximo 16 peticiones de entrada; los contactos de
confianza pueden conectarse sin token. Un dispositivo que el anfitrión expulsó o rechazó necesita un token emitido
después: con uno anterior recibe al momento `rejected` con el motivo `5` o `1`, sin preguntar al anfitrión.

### Chat y reacciones

- Texto: como máximo 100 caracteres tras la limpieza. Se quitan los caracteres de control, los sustitutos y los
  caracteres de formato (salvo U+200D), no más de dos saltos de línea seguidos, se quitan los espacios de alrededor.
- Una reacción: una de un conjunto fijo (`👍 😂 😮 😢 ❤️ 🔥 👏`); si no, el mensaje se descarta.
- `id` son hasta 32 dígitos hexadecimales, elegidos por el remitente; el anfitrión no lo toca.
- Frecuencia: como máximo 5 textos y 15 reacciones en 5 segundos por participante (una ventana deslizante en el
  anfitrión).
- El anfitrión envía un mensaje a todos, incluido el remitente: el orden es el mismo para todos. No se guarda nada en
  disco.

### El lápiz sobre el vídeo

- Un trazo es una secuencia de mensajes `draw` con un mismo `strokeId`: un `begin`, cualquier número de `continue` y un
  `end`. Los puntos los reúne el remitente y salen en lotes aproximadamente cada 60 ms, como máximo 128 puntos por
  mensaje.
- `strokeId` son hasta 32 dígitos hexadecimales, elegidos por el remitente; el anfitrión no lo toca.
- Las coordenadas son fracciones de la imagen (`0`–`1`), no píxeles de la ventana: el trazo cae en el mismo lugar del
  fotograma para todos. Los valores fuera de los límites se acotan; `NaN` e infinitos se descartan.
- Una lista de puntos vacía solo se permite en la fase `end`.
- Frecuencia: como máximo 40 mensajes por segundo y participante (una ventana deslizante en el anfitrión); el exceso se
  descarta.
- Un trazo pertenece a quien lo empezó: un fragmento con el mismo `strokeId` de otro participante no lo acepta el
  receptor.
- El receptor mantiene un trazo terminado 0,3 segundos y lo desvanece durante los 1,2 siguientes; un trazo sin terminar
  (el autor se desconectó) desaparece a los 20 segundos. Se dibujan como máximo 24 trazos a la vez. No se guarda nada.

### Avatares

- Un participante envía su avatar en un mensaje `avatar` sin el campo `sender`; el anfitrión pone el identificador
  verificado del remitente y lo envía a todos, incluido el remitente, así el orden y la imagen son los mismos para
  todos.
- `image` es un PNG de como máximo 128 KiB (para que quepa en el límite de 256 KiB por mensaje junto con el base64) y
  como máximo 1024 × 1024 píxeles. Los bytes no son de confianza: se comprueban la firma PNG, el tamaño del archivo y el
  tamaño de la imagen de la cabecera (IHDR); si no, el mensaje se descarta; un archivo pequeño puede declarar una imagen
  que ocupe gigabytes de memoria. Un campo vacío devuelve el avatar al círculo de color con una letra.
- Frecuencia: como máximo 5 mensajes en 10 segundos por participante (una ventana deslizante en el anfitrión); el exceso
  se descarta.
- El anfitrión recuerda el último avatar de cada participante y se los envía a un participante nuevo justo después de
  `welcome`, así el recién llegado ve las imágenes ya elegidas. Un avatar se olvida cuando el participante se va.
- Un avatar se muestra en lugar de la cámara mientras la cámara está apagada. El archivo del avatar elegido se guarda en
  `data/avatars`.

### Volver a una sesión

`reconnectTicket` son 48 dígitos hexadecimales, emitidos al admitir y válidos mientras dura la sesión. El participante
lo envía en el campo `inviteToken` de `hello`; el anfitrión compara el billete con el emitido para ese dispositivo en
tiempo constante y se salta la comprobación de la invitación. La confirmación del anfitrión se pide como en una entrada
normal. Cuando el anfitrión expulsa a un participante, se le retira el billete.

### Una carpeta mostrada

- Solo los participantes que anunciaron `gallery: true` en `hello` reciben `gallery`, `show` y `thumb`: un
  participante más antiguo cortaría la conexión ante un tipo desconocido.
- `catalogId` son 32 dígitos hexadecimales, aleatorios en cada muestra. Un elemento es `fileName`, `length`, `key`,
  `kind` (`0` imagen, `1` vídeo) y `folder`: la subcarpeta relativa a la mostrada, nombres unidos con `/`, como
  mucho 128 caracteres, cada nombre limpiado como un nombre de archivo. `key` son 64 dígitos hexadecimales,
  aleatorios en cada muestra; sus primeros 8 bytes son la clave de archivo de las peticiones MediaData, así que una
  imagen se obtiene por los flujos de medios en piezas comprobadas. Un vídeo nunca se obtiene así.
- Como mucho 2000 elementos, 50 por página; una imagen ocupa como mucho 64 MiB. Una página cuyos números no cuadran o
  con un elemento que rompe una regla se descarta entera.
- `show` de un participante es una intención (`version` y `sender` se ignoran). El anfitrión acepta como mucho 8
  por segundo de un participante, numera su decisión y la envía a todos con el remitente verificado. Un vídeo puesto en
  pantalla se convierte en la película: el anfitrión envía primero `media` y `state`, después `show`. Una imagen
  en pantalla pausa la película.
- Una miniatura es un JPEG de como mucho 320 × 320 píxeles y 96 KiB, que el anfitrión hace una vez. Responde como mucho
  400 peticiones en 10 segundos por participante; lo que no sea un JPEG dentro de los límites llega como respuesta
  vacía.
- Un participante que mira una imagen aparte comunica su índice en `galleryView` de su estado, acotado por el número
  de elementos; los demás muestran quién mira dónde.
- Quien llega recibe las páginas y el `show` actual justo después de `welcome`.

## MediaData

Una petición, 17 bytes: la operación (`01` GetChunk para una pieza, `02` GetHash solo para el hash), el índice de la
pieza (int64 BE), la clave del archivo (uint64 BE, los primeros 8 bytes del identificador rápido). La respuesta a
GetHash llega sin datos.

Una respuesta, 45 bytes y los datos: el estado (`0` OK, `1` fuera del archivo, `2` otro archivo, `3` error de lectura),
el índice (int64 BE), la longitud (int32 BE), el SHA-256 de los datos (32 bytes).

El tamaño de pieza es 256 KiB por defecto (se permiten de 64 KiB a 16 MiB). El identificador rápido = SHA-256(longitud
int64 LE, el primer MiB, el MiB del medio, el último MiB).

## Relay

El flujo lleva TURN (RFC 5766 en tramas TCP) sin cambios: lo mismo que el cliente enviaría al puerto del relé del
anfitrión, con las mismas credenciales de `welcome`. El anfitrión acepta un flujo así solo de un participante admitido y
lo cierra junto con la admisión. La asignación se crea en la dirección del anfitrión a la que llegó la conexión.

En el lado del participante abre los flujos `RelayTunnel`: escucha en un puerto de `127.0.0.1` al que apunta el
`turn-server` de las cámaras y, para cada conexión a él, abre un flujo `Relay` propio por la ruta que tomó la sesión (una
dirección directa o un encuentro a través del servidor de encuentro). Si `welcome` no tiene `relayOverSession` (un
anfitrión de una versión anterior), el participante va directamente al puerto del relé, como antes.

### RelayShared

Un flujo `RelayShared` lleva a la vez todas las conexiones TURN de un participante, para que las cámaras y las voces
necesiten una sola conexión a través de los routers (a través de un servidor de encuentro, un solo encuentro) en lugar
de una docena. Es una secuencia de tramas: el número de canal (4 bytes, big-endian), el tipo (1 byte), la longitud de la
carga (2 bytes, big-endian), la carga (como máximo 16384 bytes).

| Tipo | Lo envía | Significado |
|---|---|---|
| `1` open | participante | abre un canal; sin carga |
| `2` data | ambos | bytes del canal: el mismo TURN en tramas TCP |
| `3` close | ambos | el canal ha terminado; sin carga |

El relé atiende cada canal como un cliente propio. Como máximo hay 16 canales abiertos a la vez; un `open` por encima se
responde con `close`. Los datos para un canal que no existe se descartan; una trama de otro tipo o con una carga más
larga termina todo el flujo. El participante usa este flujo cuando `welcome` tiene `relayShared` y abre uno nuevo cuando
se rompe; sin la marca (un anfitrión de una versión anterior), cada conexión recibe un flujo `Relay` propio. El precio de
compartir es que un paquete perdido retiene todos los canales hasta que se reenvía.

## Intercambio de piezas entre participantes

Funciona sobre el canal de datos WebRTC `golether` (SCTP sobre DTLS; los mensajes solo están disponibles tras comprobar
el certificado del otro lado). Todos los números son big-endian.

| Tipo | Contenido | Significado |
|---|---|---|
| `1` have | clave del archivo (8), número de rangos (2), rangos: inicio (4), longitud (4) | qué piezas hay; cada 2 s |
| `2` request | clave del archivo (8), número de petición (4), índice de la pieza (4) | una petición para enviar una pieza |
| `3` data | número de petición (4), desplazamiento (4), tamaño total (4), datos | una parte de una pieza (de 16 KiB, en orden) |
| `4` miss | número de petición (4) | la pieza no está o el que la da está ocupado (como máximo 2 envíos por participante) |

Una pieza recibida solo se acepta cuando su SHA-256 coincide con el hash del anfitrión (GetHash); si no, la pieza se
pide al anfitrión. Lo mismo ocurre cuando la transferencia se detiene 5 segundos o la pieza no ha llegado entera en
30 segundos: un participante que envía poco a poco no puede retener la película a los demás.

## Invitación

`golether://join/v1/` + base64url(JSON), como máximo 4096 caracteres:

```json
{ "token": "<22 caracteres base64url, 128 bits>", "expires": 1789000000,
  "host": "<PeerId>", "name": "You", "session": "Movie night",
  "endpoints": ["192.168.1.10:47800", "[2001:db8::5]:47800"],
  "meet": "203.0.113.7:47900", "room": "<22 caracteres base64url, 128 bits>" }
```

`token` y `expires` (segundos Unix) van primero: el resto es igual en cada invitación de una sesión, así una invitación
nueva se diferencia desde sus primeros caracteres. El orden de los campos no significa nada para un lector.
`endpoints` contiene de 1 a 16 direcciones.

La misma invitación como enlace web: `https://depthrel.github.io/Golether/join/#` + el mismo base64url. El navegador no
envía al servidor la parte tras `#`; la página la convierte en `golether://join/v1/…` y se la entrega a la aplicación.
La aplicación acepta ambas formas.

Los campos `meet` y `room` son opcionales y van siempre juntos: el servidor de encuentro del anfitrión y su sala en él
(una nueva para cada sesión). Con solo uno de ellos, o con uno mal formado, la invitación se considera dañada. Las
versiones antiguas no leen estos campos. El protocolo del propio servidor se describe en
[RendezvousServer.md](RendezvousServer.md#protocolo-versión-1).

## Código de verificación

Ambos lados calculan el código por su cuenta; nunca viaja por la red.

```
hash     = SHA-256(UTF-8("golether-sas-v1|" + low + "|" + high + "|" + context))
palabras = los 24 bits de hash[0..2] (big-endian), cortados en tres números de 6 bits (0–63), el más alto primero
número   = uint16 little-endian de hash[3..4] módulo 100
```

`low` y `high` son los dos `PeerId` en orden ordinal, así el resultado no depende de quién lo calcula. El contexto de una
entrada es el token que el participante presenta en `hello`: el token de la invitación, o el `reconnectTicket` al
volver. El contexto de un túnel es el `offerId` de su oferta (ver abajo).

Un número de palabra se convierte en palabra solo en la pantalla, mediante el paquete de idioma (`Verification.Word.00`
… `Verification.Word.63`). Cada idioma nombra las mismas 64 cosas en el mismo orden, así que el código es el mismo en
todos los idiomas, y dos personas con distintos idiomas de interfaz ven traducciones de las palabras del otro.

## Paquetes AmneziaWG

`golether-awg:<offer|answer>:v1:<base64url(cuerpo JSON)>.<base64url(firma)>`.
La firma: ECDSA P-256/SHA-256 (IEEE P1363) sobre la cadena ASCII anterior al punto. Los espacios y saltos de línea dentro
de un paquete se ignoran, así que un paquete se puede reenviar con saltos de línea.

El cuerpo de una oferta: `offerId`, `hostName`, `hostCertificate` (DER, base64), `hostPublicKey` (AWG),
`hostEndpoints[]`, `hostAddress`, `assignedAddress`, `s1`, `s2`, `headers[4]`, `ephemeralKey` (SPKI P-256, base64),
`expiresAt`.

El cuerpo de una respuesta: `offerId`, `participantName`, `participantCertificate`, `participantPublicKey`,
`participantEndpoints[]`, `ephemeralKey`.

PSK = base64(SHA-256(ECDH(claves P-256 efímeras))): el secreto común de dos claves de un solo uso, pasado por SHA-256.
Un paquete con el cuerpo incompleto o con una clave que no está en la curva P-256 se considera dañado.

Código de verificación = `VerificationCode(hostPeerId, participantPeerId, offerId)`.
