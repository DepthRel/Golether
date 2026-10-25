# Golether: guía de uso

## Qué necesitas

- Windows 10 o posterior, Linux o macOS 14+.
- El paquete de Windows ya contiene los componentes de vídeo y cámaras. Si falta alguno, pulsa **Instalar** en la
  tarjeta «Componentes» de la pantalla de inicio: la aplicación lo descarga del sitio oficial, comprueba la suma de
  verificación y lo instala. No hacen falta permisos de administrador. En Linux y macOS la tarjeta muestra un comando
  listo para la terminal: cópialo con el botón **Copiar**.
- Un vídeo se puede abrir enseguida: «Abrir con → Golether» crea una sesión y muestra ese archivo.
- Los túneles necesitan AmneziaWG. En Windows no hay que instalar nada aparte: la primera vez que abres la ventana de
  túneles, Golether descarga su propio motor (unos 4 MB). En Linux y macOS necesitas `amneziawg-tools` junto con el
  módulo `amneziawg` / `amneziawg-go`. Golether nunca tiene que ejecutarse como administrador: el sistema pide permiso
  cuando se levanta un túnel.

## Idioma de la interfaz

Golether habla inglés, alemán, francés, español, portugués y ruso. En el primer inicio el idioma sigue al sistema
operativo: un sistema en uno de esos idiomas lo recibe, sea cual sea la región (alemán de Suiza, francés de Canadá,
portugués de Brasil, etc.), y cualquier otro idioma recibe el inglés. La elección se recuerda, y los inicios siguientes
la usan en lugar del idioma del sistema.

Para cambiar el idioma, abre **Ajustes** (el botón de arriba a la derecha), sección **Ajustes generales**: la lista
muestra la bandera y el nombre de cada idioma. Tras **Aplicar** todos los textos cambian al momento, no hace falta
reiniciar, y la elección se guarda. Los mensajes de error usan el mismo idioma. Los motivos de un rechazo que envía el
anfitrión se muestran en tu idioma aunque el anfitrión use otro.

Las palabras del código de verificación también se muestran en tu idioma. Cada idioma nombra las mismas 64 cosas, así
que cuando el anfitrión usa otro idioma, sus palabras son traducciones de las tuyas: *búho · faro · viento · 47* en una
pantalla es *owl · lighthouse · wind · 47* en la otra. Compara el significado de las palabras y el número.

## Mostrar una película (eres el anfitrión)

1. Escribe tu nombre y el nombre de la sesión y pulsa **Crear una sesión**. El puerto de tu equipo (47800 por defecto)
   y la petición al router están en **Avanzado**: cámbialos solo si el puerto está ocupado o lo redirigiste a mano.
2. Pulsa **Mostrar un archivo** y elige un vídeo. El archivo se queda contigo: los participantes lo reciben pieza a
   pieza mientras ven y lo guardan solo en memoria.
3. Pulsa **Invitar**, copia el **enlace** y envíaselo a tus invitados como prefieras. Un clic en él abre Golether en el
   equipo del invitado y pregunta si quiere unirse. El enlace vale 15 minutos. Si a un invitado el enlace no le abre la
   aplicación, envíale la **clave** desde la misma ventana: se pega en Golether a mano y vale 15 minutos o una hora.
   Una invitación sirve para todo el grupo: publica el enlace en tu chat de grupo y todos podrán unirse con él hasta que
   caduque. Aun así, a cada invitado lo dejas entrar tú. La validez solo limita el momento de entrar: un invitado que
   entró se queda en la sesión aunque la invitación haya caducado.
   Con «Abrir el puerto en el router» activado, Golether pide al router que abra el puerto (UPnP o NAT-PMP), añade la
   dirección pública a la invitación e informa del resultado en la lista de eventos. Si el router no puede hacerlo o la
   dirección de tu proveedor no es pública, añade una dirección pública con un puerto redirigido a mano, o la dirección
   dentro de un túnel AWG (ventana de invitación, **Avanzado**).
   Lo más sencillo es el servidor de encuentro. El selector **Modo de conexión** de la pantalla de inicio está en
   **Automáticamente** por defecto: Golether encuentra solo el servidor del proyecto, va en la invitación, y los
   invitados de otras redes se conectan sin puertos abiertos en ningún lado; consulta [más abajo](#servidor-de-encuentro).
4. Cuando un participante se conecta, aparece una ventana con el código de verificación. Lee el código en voz alta. Si
   el participante ve el mismo código, pulsa **Los códigos coinciden, dejar entrar**.
5. Reproducir, pausar y saltar actúan para todos los participantes.
6. Las cámaras y la voz se inician solas al entrar en una sesión: el recuadro de cada participante a la derecha muestra
   su cámara, y las voces de los demás se mezclan en un solo flujo. La cámara y el micrófono se eligen en **Ajustes**,
   sección **Cámara y micrófono** (si no, se usan los predeterminados del sistema). Una cámara o un micrófono apagados se
   liberan de verdad.

## Unirse (eres participante)

1. Haz clic en el enlace de invitación: el navegador ofrece abrir Golether, y Golether pregunta si quieres unirte a la
   sesión (con su nombre y el del anfitrión). Si ya estás en otra sesión, pregunta si quieres cambiar. En lugar de hacer
   clic, puedes pegar la clave o el propio enlace en el campo **Invitación** y pulsar **Conectar**.
2. Aparece un código de verificación. Compáralo de viva voz con el anfitrión. Si los códigos no coinciden, pulsa
   **Terminar**: la conexión podría estar manipulada.
3. Si ya tienes el mismo archivo, pulsa **Mi copia del archivo** y elígelo: la reproducción irá desde tu disco.

## Controles

| Acción | Botón | Tecla |
|---|---|---|
| Reproducir / pausar para todos | ▶ / ❚❚ | Espacio |
| Retroceder / avanzar 10 s | ◀◀ / ▶▶ | ← / → |
| Saltar | el control deslizante (se aplica al soltarlo) | — |
| Pantalla completa | ⛶ | F11, Esc |
| Reproducir / pausar con un clic | un clic en el vídeo | — |
| Pantalla completa con un clic | un doble clic en la zona del vídeo (funciona también antes de elegir una película) | — |
| Tu propio volumen | el icono del altavoz a la derecha: al pasar por encima aparece una escala de 0–100 %, un clic silencia y reactiva, la rueda lo cambia | — |
| Micrófono y cámara | «Micrófono encendido» / «Cámara encendida» bajo los recuadros de los participantes | — |
| Sonido y subtítulos | el icono «CC» de la barra del reproductor: cada uno tiene sus propias pistas | — |
| Reacción para todos | el icono de la carita en la barra del reproductor | — |
| Dibujar sobre el vídeo | el icono del lápiz en la barra del reproductor | — |
| Chat y eventos | las pestañas **CHAT** y **EVENTOS** abajo a la derecha; un clic en la pestaña abierta o en la flecha oculta el panel | — |
| Enviar un mensaje | el campo de abajo del panel del chat | Intro |
| Volumen de otro participante | clic derecho en su recuadro → control deslizante | — |
| La cámara de un participante en su propia ventana | clic derecho en su recuadro → **Abrir en una ventana aparte** | — |

En una ventana estrecha, los botones «Ajustes», «Invitar» y «Terminar» se recogen en el botón **⋯** de arriba a la
derecha.

El volumen y el silencio actúan solo en tu lado: los demás no notan nada. Una escala en 0 silencia el sonido, y otro
clic en el altavoz devuelve el volumen anterior.

### Pantalla completa

El cursor sobre el vídeo desaparece tras tres segundos de reposo y vuelve con el primer movimiento, en cualquier modo de
ventana. Sobre los recuadros de los participantes y el chat siempre es visible.

En pantalla completa la barra de estado de abajo se oculta y el vídeo ocupa toda la altura. La barra superior y la
barra del reproductor quedan sobre el vídeo y se retiran tres segundos después de que el cursor salga de ellas; la
imagen no cambia de tamaño. Lleva el cursor al borde superior o inferior de la pantalla y las barras vuelven y se
quedan mientras el cursor esté sobre ellas. No se oculta nada mientras haya un menú de una barra abierto, se arrastre
el control deslizante o el lápiz esté activo.

Si quieres las barras siempre, activa **Mostrar siempre la línea de tiempo** en **Ajustes**, sección **Pantalla**: en
pantalla completa ambas barras se quedan en su sitio y el vídeo se ajusta entre ellas.

### Ancho del panel de participantes y orden de los recuadros

En **Ajustes**, sección **Pantalla**, activa **Diseño ajustable**: aparece un límite fino entre el vídeo y los
participantes. Arrástralo para ensanchar el panel de participantes; los recuadros de las cámaras crecen con él. Un
doble clic en el límite restaura el ancho original.

Con tres participantes o menos, los recuadros forman una columna. Con más participantes y un panel estirado a más del
triple de su ancho original, los recuadros forman dos columnas, para que quepa más gente en la pantalla.

Con el mismo ajuste se pueden intercambiar los recuadros: arrastra uno sobre otro. El orden solo lo ves tú y dura hasta
que se cierra la aplicación; los participantes nuevos van al final. Desactivar el ajuste restaura el ancho y el orden
originales y quita el límite.

### La cámara de un participante en una ventana aparte

Clic derecho en el recuadro de un participante → **Abrir en una ventana aparte**. La ventana muestra solo esa cámara;
puedes agrandarla o llevarla a otra pantalla. Cerrar la ventana no afecta a nada; cuando el participante se va, se
cierra sola.

### Tus propias pistas de audio y subtítulos

El icono **CC** de la barra del reproductor abre la lista de pistas de audio y subtítulos de la película. La elección
actúa solo en tu lado: uno ve la versión original, otro el doblaje, un tercero lee subtítulos. El idioma elegido se
recuerda y se aplica a la siguiente película que tenga una pista así. **Subtítulos desde un archivo…** añade un archivo
`.srt` o `.ass` de tu equipo, **Sonido desde un archivo…** una pista de audio aparte (tu propio doblaje, por ejemplo).
Solo tú ves y oyes cualquiera de los dos.

### Chat y reacciones

Abajo a la derecha hay dos pestañas: **CHAT** y **EVENTOS**. Un clic en la pestaña abierta (o en la flecha de la
derecha) oculta el panel entero y deja el sitio a los recuadros de los participantes. Mientras el chat está oculto, su
pestaña muestra un contador de mensajes nuevos. Los mensajes viajan por el mismo canal protegido que el resto de la
sesión y no se guardan en ningún sitio: al cerrar la sesión, desaparecen.

Mientras el chat no se ve, en la esquina de su pestaña aparece además una «i» naranja en un círculo, y mientras todo
el panel está oculto, una franja naranja corta recorre despacio el borde del botón de la flecha. La pestaña
**EVENTOS** nunca llama la atención. Un mensaje de otro participante se anuncia con un breve golpe de claqueta de
cine; tus propios mensajes y las reacciones no suenan. El sonido se desactiva en **Ajustes**, sección **Sonidos**.

El icono de la carita de la barra del reproductor envía una reacción: durante unos segundos flota sobre el vídeo para
todos los participantes, con el nombre de quien la envía. Las reacciones están disponibles desde que entras en una
sesión, antes de elegir una película. La capa de las reacciones deja pasar los clics: la pausa con un clic en el vídeo
sigue funcionando.

### El lápiz: rodear algo directamente en el vídeo

El icono del **lápiz** de la barra del reproductor activa el dibujo. El botón se queda pulsado y el cursor sobre el
vídeo se convierte en un lápiz. Mantén pulsado el botón izquierdo del ratón y muévelo: la línea sigue al cursor con
exactitud, y todos los participantes la ven en el mismo momento. Al soltar el botón, la línea se queda dos segundos y
se desvanece. Cada participante tiene su propio color, el mismo que su recuadro, así que se sabe quién dibuja. Otro
clic en el icono suelta el botón y el cursor vuelve a ser el normal.

Las líneas no se guardan en ningún sitio y van unidas a la imagen, no a la ventana: si tu ventana es más ancha y la de
la otra persona más estrecha, el lugar rodeado sigue siendo la misma parte del fotograma. Mientras el lápiz está
apagado, los clics en el vídeo funcionan como siempre: pausa y pantalla completa.

### Esperar a que todos estén listos

Cuando pulsas reproducir, Golether comprueba primero que todos tienen el archivo abierto y su comienzo cargado.
Mientras alguien no esté listo, la barra del reproductor muestra a quién se espera («Esperando a: Marina…») y el botón
se convierte en «Cancelar el inicio». El anfitrión no tiene por qué esperar: **Empezar sin ellos** inicia la película al
momento, y quienes vayan por detrás se ponen al día solos. Si un participante sigue sin estar listo pasados
45 segundos, la proyección empieza de todos modos.

### Seguir donde lo dejaste

Golether recuerda dónde dejaste cada película (en este equipo, en la carpeta `data`). Cuando el anfitrión vuelve a abrir
el mismo archivo, aparece sobre la línea de tiempo una barra con la posición en la que te quedaste y el botón
**Continuar para todos**. Una película vista hasta el final se olvida.

### Qué muestra la línea de tiempo

El control deslizante solo se mueve cuando la imagen se mueve de verdad: durante la cuenta atrás antes de empezar y
mientras el reproductor salta o espera datos, se queda quieto.

El relleno claro de la línea de tiempo es lo que ya está cargado y a lo que se puede saltar sin esperar. Para el
anfitrión y para quien ve su propia copia del archivo, toda la línea de tiempo está iluminada. Los círculos con letras
son los participantes: puedes ver dónde está cada uno.

### Se cortó la conexión

Si se pierde la conexión con el anfitrión, Golether vuelve a conectar solo: hasta ocho intentos con pausas cada vez
más largas. No hace falta una invitación nueva: al entrar recibes un billete válido para esta sesión. Aun así, el
anfitrión confirma el regreso, como en una entrada normal. Mientras duran los intentos, una barra arriba muestra el
número del intento y el botón **Salir**.

### Anfitrión: apagar el micrófono o la cámara de un participante

Clic derecho en el recuadro de un participante y elige **Apagar el micrófono** o **Apagar la cámara**. El anfitrión
solo puede apagar: solo el participante puede volver a encender el dispositivo, y Golether se lo dice. Los recuadros
muestran a quién se le ha apagado el micrófono o la cámara. Tu propio recuadro no tiene menú: para ti, usa los botones
de debajo de los recuadros.

### Anfitrión: expulsar a un participante

El mismo menú (clic derecho en el recuadro de un participante) tiene **Expulsar de la sesión**. Golether pide
confirmación, y entonces el participante sale de la sesión al momento y ve un mensaje de que el anfitrión lo expulsó.
No es un bloqueo: puede volver, pero solo con una invitación nueva tuya; ni las invitaciones anteriores ni la
reconexión automática lo dejan entrar. Las invitaciones anteriores siguen funcionando para tus otros invitados: se
expulsa a la persona, no el enlace. Abre **Invitar**, pulsa **Nueva invitación** y envía el enlace nuevo a esa persona
en privado. La misma regla vale para alguien a quien rechazaste en la ventana del código de verificación.

### Inicio, final de la película y la barra espaciadora

Cuando la película termina, se detiene en el último fotograma para todos y el botón de reproducir se convierte en **Ver
otra vez** (una flecha circular): pulsarlo o la barra espaciadora empieza la película desde el principio para todos.

Los botones del reproductor no toman el foco del teclado, así que la barra espaciadora siempre significa
reproducir/pausar, incluso después de hacer clic en otros botones.

Tras pulsar reproducir hay una breve cuenta atrás: así todos los participantes empiezan juntos, incluso con un ping alto.

## Qué muestran los recuadros

- **Marco verde**: el participante está hablando en este momento (también tú cuando hablas). Con el micrófono apagado no
  hay marco.
- **Ping**: el tiempo de respuesta con el anfitrión. Verde por debajo de 400 ms, amarillo hasta 1,5 s, rojo por encima.
- **Desfase**: cuánto va el participante por delante (+) o por detrás (−) de la posición común. Un desfase pequeño se
  corrige con un cambio de velocidad imperceptible, uno grande con un salto.
- **Búfer**: cuántos segundos de vídeo están ya cargados por delante. Si alguien se queda muy atrás y espera datos, el
  anfitrión pausa a todos automáticamente y sigue cuando todos están listos.

Cuando ven varias personas, los participantes se pasan directamente las partes ya cargadas de la película, así el
anfitrión no tiene que dárselas a cada uno. Cada parte se comprueba con la suma de verificación del anfitrión. Cuánto se
recibió y se dio se muestra en la barra de estado de la parte inferior de la ventana.

## Si algo salió mal

**⋯ → Informe de diagnóstico…** guarda un archivo de texto en `data/logs` (el botón **⋯** aparece arriba a la derecha
cuando la ventana es tan estrecha que los botones de la barra superior ya no caben): la versión, el estado de los
componentes, información sobre la sesión (sin el nombre de la película) y las últimas líneas del registro. Las claves,
los tokens y las contraseñas se quitan del informe, así que el archivo se puede enviar a quien te ayude.

Con cualquier ancho de la ventana, el mismo informe se guarda desde **Ajustes → Acerca de Golether → Informe de
diagnóstico…**. Al lado, **Informar de un problema** abre en su programa de correo una carta a los desarrolladores
(project.golether@gmail.com) con la versión y el sistema ya rellenados; la ventana de cualquier error ofrece lo mismo.
Adjunte el informe a la carta: no se envía nada hasta que usted la envíe.

El informe tiene una línea por cada conexión con un participante: si está verificada, si el canal de datos está
abierto y cuántos fotogramas salieron y entraron (`vídeo 120↑/0↓`). Muestran enseguida dónde se rompe el enlace: la
conexión no se estableció, se estableció vacía o la cámara no da nada.

### Nadie puede conectarse: el firewall

Windows trata una conexión de red nueva, sobre todo un adaptador de túnel, como red pública y descarta las conexiones
entrantes. Entonces los participantes no pueden entrar aunque los equipos se vean perfectamente. Golether lo comprueba al
empezar una proyección y, si no hay regla, muestra arriba la barra **«El firewall no deja entrar a los participantes»**
con el botón **Permitir**. El botón pide una vez la confirmación de un administrador y añade una regla para el
programa; no aparece ningún servicio permanente, y nunca hay que repetirlo.

Lo mismo a mano: «Firewall de Windows Defender» → «Permitir una aplicación a través del firewall» → busca Golether y
marca tanto la red privada como la pública.

## Actualizaciones

Al iniciarse, Golether pregunta a la [página del proyecto](https://depthrel.github.io/Golether/) si ha salido una
versión nueva. Si es así, arriba aparece la barra «Hay una versión nueva disponible» con el botón **Abrir la página de
descarga**: tu navegador abre la página con los enlaces para tu sistema. No se descarga ni se instala nada solo: decides
tú. La casilla **Ignorar esta versión**, marcada antes de cerrar la barra, desactiva el aviso de esa versión; Golether te
avisará de la siguiente. Una actualización no toca los datos personales.

En **Ajustes**, sección **Actualizaciones**, puedes desactivar la comprobación o indicar tu propio origen (la dirección
de un archivo con la lista de versiones, `https://…`); solo hace falta para compilaciones propias.

## Servidor de encuentro

Si tú y tus participantes tenéis direcciones «grises» y no se puede redirigir un puerto, ayuda el servidor de encuentro:
un programa pequeño en cualquier máquina con IP pública (tuya o de un amigo). Solo presenta vuestros equipos; la
conexión entre ellos se abre directamente. La película, las cámaras y las voces no pasan por el servidor, y él no puede
verlas.

1. Alguien instala el servidor siguiendo la
   [guía del administrador](../../Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.es.md) (Windows, Linux o macOS; el
   mismo archivo está en cada paquete del servidor).
2. El anfitrión elige el **modo de conexión** en la pantalla de inicio y crea una sesión:
   - **Automáticamente** (por defecto): Golether obtiene la dirección del servidor del proyecto desde la página del
     proyecto. Si la página no está disponible, se usa la dirección de la última vez; si no hay ninguna, la sesión se
     crea sin servidor y la lista de eventos lo indica;
   - **Directamente**: sin servidor; los invitados se conectan solo a las direcciones de tu equipo;
   - **Mi propio servidor**: aparece un campo para la dirección del servidor, por ejemplo `203.0.113.7:47900`.

   Cuando el servidor ha respondido, la lista de eventos muestra «El servidor de encuentro está listo».
3. Un participante no tiene que configurar nada: la dirección del servidor llega en la invitación. Si las direcciones
   directas no responden en 4 segundos, Golether se encuentra con el anfitrión a través del servidor por sí solo.

No funciona cuando al menos un lado está detrás de un NAT simétrico (habitual en operadores móviles y redes de oficina):
Golether informa entonces de que los routers no dejaron pasar la conexión directa, y queda un túnel AWG o un puerto
redirigido.

## Túnel AmneziaWG

Un túnel se levanta antes de conectar: **Ajustes → Túneles AmneziaWG → Configurar túneles**. Durante una sesión este
botón no está disponible.

Un túnel hace falta cuando una conexión directa no pasa o está bloqueada. No hace falta un servidor propio, y en
Windows **no hay que instalar AmneziaWG aparte**: Golether descarga su propio motor y levanta el túnel en su propia
instancia. Si ya usas AmneziaWG con tus propias direcciones, no se ve afectado: son adaptadores de red distintos.

La primera vez que se abre la ventana de túneles, el motor se descarga con el botón **Instalar** en esa misma ventana
(unos 4 MB, sin permisos de administrador). Hasta que esté, los botones «Levantar el túnel» no están disponibles.

**Anfitrión:**
1. En la ventana de túneles: **Soy el anfitrión → Crear una oferta**, envía el texto al participante.
2. Pega la respuesta del participante y pulsa **Aceptar la respuesta**. Comparad el código de viva voz.
3. **Levantar el túnel** (Windows muestra una solicitud de permiso, macOS y Linux piden la contraseña) o **Exportar
   .conf** para AmneziaVPN.
4. Añade la dirección del anfitrión dentro del túnel a la invitación, por ejemplo `10.77.41.1:47800` (ventana de
   invitación, **Avanzado**).

**Participante:**
1. En la ventana de túneles: **Soy participante**, pega la oferta, **Aceptar la oferta**.
2. Envía la respuesta al anfitrión, comparad el código, **Levantar el túnel**.

**Un túnel vive mientras Golether está abierto.** Al salir se baja automáticamente; el sistema pide una vez el permiso
de un administrador. Si lo rechazas, el túnel se queda, y Golether ofrece quitarlo en el siguiente inicio. Para quitarlo
antes, usa **Bajar el túnel** en la misma ventana. Los túneles que no levantó Golether no se tocan.

Golether encuentra la dirección pública solo: al crear una oferta y al aceptar una respuesta, pregunta a servidores STUN
públicos cómo se ve su puerto desde fuera y pone la dirección en el paquete. Después ambos lados «llaman» al otro para
que los routers dejen pasar la respuesta. Por eso suele ser innecesario redirigir un puerto a mano.

No funciona cuando el router da a cada destino su propio puerto (NAT simétrico) o el proveedor te mantiene detrás de una
dirección compartida (CGNAT), en ambos lados a la vez. Entonces un lado necesita una IP pública o un puerto redirigido.
Los archivos `.conf` contienen claves: no los publiques.

## Dónde se guardan los datos

Golether es autónomo: todo lo que guarda (la clave del dispositivo, la base de datos de contactos y túneles, las
configuraciones `.conf`, los componentes descargados, los ajustes) está en la carpeta `data` junto al programa. Un
archivo descomprimido no escribe nada en el perfil del usuario ni en el registro; el instalador de Windows solo añade al
registro la entrada de desinstalación y el manejador de los enlaces de invitación. Por eso:

- descomprime Golether donde tengas permiso de escritura: en «Documentos», en el escritorio, en una memoria USB. Una
  copia descomprimida en `Program Files` no se inicia y te pide mover la carpeta;
- el instalador de Windows pregunta para quién instalar. **Solo para mí** (por defecto) instala en
  `%LOCALAPPDATA%\Programs\Golether`, no necesita permisos de administrador y guarda los datos en `data` dentro de la
  instalación. **Para todos** instala en `C:\Program Files\Golether`, necesita permisos de administrador una vez durante
  la instalación y guarda los datos de cada usuario en su perfil: `%LOCALAPPDATA%\Golether`. El instalador también enseña
  al sistema a abrir los enlaces de invitación con Golether. En un archivo descomprimido los enlaces no se abren solos:
  usa la clave (en Linux `install-desktop-entry.sh` activa los enlaces);
- el paquete para Ubuntu y Debian (`.deb`, se instala con `sudo apt install ./Golether-<version>-linux-x64.deb`) trae
  consigo los componentes de vídeo y cámaras, añade Golether al menú, hace que el sistema abra los enlaces de
  invitación y guarda los datos de cada usuario en su perfil: `~/.local/share/Golether`. Se quita con
  `sudo apt remove golether`;
- mueve, copia y borra la carpeta `Golether` entera: todos los datos viajan con ella;
- para actualizar una copia descomprimida, descomprime la versión nueva y mueve a ella la carpeta `data` de la antigua;
- si los datos estaban antes en `%LOCALAPPDATA%\Golether` (compilaciones antiguas), se mueven a `data` automáticamente en
  el primer inicio;
- en Windows la clave del dispositivo y los secretos de los túneles se cifran además para tu cuenta de Windows. En otro
  equipo o con otro usuario no se pueden leer: el programa lo dice, y tras borrar la carpeta `data\identity` crea una
  clave nueva;
- `data\identity\device.key` es la clave del dispositivo. Si se borra, la huella del dispositivo cambia y tus contactos
  tienen que volver a comparar el código.

## Preguntas frecuentes

- **«No se pudo llegar al anfitrión».** Comprueba que el puerto está abierto en el firewall del anfitrión, o usa un
  túnel.
- **«La invitación no es válida o ha caducado».** La validez de la invitación terminó (un enlace vive 15 minutos, una
  clave hasta una hora): pide una nueva al anfitrión.
- **«El anfitrión te expulsó» o «El anfitrión rechazó la solicitud».** Las invitaciones anteriores ya no te dejan
  entrar; puedes unirte con una invitación que el anfitrión cree después.
- **macOS no abre la aplicación.** La compilación no está firmada por Apple: «Ajustes del Sistema → Privacidad y
  seguridad → Abrir igualmente».
- **Otro participante tiene una conexión débil.** Su recuadro recibe la marca «eco»: tu cámara le llega con menos
  resolución (320×180) hasta que la conexión se recupera. El sonido no empeora.
- **No hay cámaras.** Comprueba que el componente «Cámaras y voz» está instalado (pantalla de inicio) y que Windows
  permite a las aplicaciones usar la cámara y el micrófono («Configuración → Privacidad»). Un recuadro negro suele
  significar una tapa de objetivo cerrada.
- **La otra persona se oye a sí misma.** La cancelación de eco funciona para el sonido que reproduce Golether; unos
  auriculares eliminan el problema por completo.
