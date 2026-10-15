# Desarrollo de Golether

## Entorno

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- VS Code con C# Dev Kit y la extensión de Avalonia, o Visual Studio / Rider.
- Los componentes nativos (libmpv, GStreamer) los prepara la utilidad `golether-components`, ver
  [Native/README.md](../../Native/README.md).
- Windows PowerShell 5.1 o PowerShell 7 para `Scripts/*/*.ps1`, bash para `Scripts/*/*.sh`. Los scripts se agrupan según
  lo que compilan: `Scripts/App`, `Scripts/Server`, `Scripts/Assets` (ver `Scripts/README.md`).

## Compilación y pruebas

```bash
dotnet build Golether.slnx
dotnet test --solution Golether.slnx                                    # todas las pruebas
dotnet test --project Tests/Golether.Sync.Tests/Golether.Sync.Tests.csproj  # un proyecto
```

La variable `GOLETHER_TEST_SCREENSHOTS` (una carpeta) guarda fotogramas de la ventana principal de las pruebas headless
para una comprobación visual sin abrir ventanas en la pantalla.

La variable `GOLETHER_TEST_GST_LOG` (una ruta de archivo) activa el registro detallado de GStreamer en las pruebas de
conferencia.

La variable `GOLETHER_TEST_AWG_PACKAGES` (una carpeta con archivos `amneziawg-<arch>-<version>.msi` de las versiones de
Amnezia) activa la comprobación que descomprime el motor de túneles de un paquete real. Sin ella esa prueba se omite, así
que una ejecución normal no necesita descargas. El paquete se puede descargar de la dirección de `ComponentCatalog`; el
nombre del archivo también sale de ahí.

Las pruebas son xUnit v3 sobre Microsoft.Testing.Platform (activado en `global.json`). Los paquetes comunes de pruebas se
referencian en `Directory.Build.props` para cada proyecto `*.Tests`.

| Proyecto | Qué comprueba |
|---|---|
| Golether.Components.Tests | el catálogo, las pistas para Linux/macOS, las importaciones PE, el instalador (suma, tamaño, caché), la reutilización de GStreamer |
| Golether.Conventions.Tests | las reglas de AGENTS.md que los analizadores no pueden comprobar, sobre las fuentes leídas con Roslyn: un tipo por archivo, tipos anidados privados, comentarios XML, enumeraciones en carpetas Enums, operadores lógicos al final de la línea, el orden de los using, el tiempo solo mediante TimeProvider, el esquema solo mediante migraciones, proyectos en sus carpetas de la solución |
| Golether.Media.Conference.GStreamer.Tests | dos nodos WebRTC por loopback con fuentes de prueba, validación de señales, estadísticas de conexión y el cambio al flujo económico de la cámara; se omiten sin GStreamer |
| Golether.Core.Tests | PeerId, PlaybackState, DriftCorrector, direcciones, rutas de datos (incluida una instalación «para todos los usuarios») |
| Golether.Localization.Tests | paquetes de idioma: lectura, el catálogo, elección del idioma por cultura, el inglés de respaldo, las mismas claves y marcadores en cada idioma, las 64 palabras distintas del código de verificación y una bandera en cada idioma, cada clave pedida existe, no hay texto ruso en el código |
| Golether.Security.Tests | firmas, el almacén de claves, invitaciones (clave y enlace web, validez, la invitación compartida y el límite de peticiones), la dirección sellada del servidor, el código de verificación, la admisión |
| Golether.Transports.Tests | tramas, mTLS por loopback, fijación de la clave, NAT-PMP y UPnP con un router falso, mensajes y firma del servidor de encuentro, TCP hole punching por loopback |
| Golether.Sync.Tests | relojes con una RTT de 1240 ms, mensajes, authority, follower |
| Golether.Media.Streaming.Tests | transferencia de piezas, la caché, el flujo para el reproductor, hashes sin datos, intercambio de piezas entre participantes y la comprobación con hashes |
| Golether.Media.Player.Tests | el reproductor simulado, el registro `golether://`, asignaciones del ratón, volumen, pistas de audio y subtítulos, rangos cargados de un libmpv real (se omiten sin él) |
| Golether.Tunnels.AmneziaWG.Tests | claves, parámetros, `.conf`, paquetes, el controlador |
| Golether.Core.Data.Tests | migraciones, almacenes de EF Core, el migrador |
| Golether.Session.Tests | el anfitrión y un participante en conjunto: admisión, el archivo, el inicio conjunto, la pausa, la invitación compartida, expulsión de un participante y rechazo, una invitación caducada |
| Golether.UI.Tests | el flujo de túneles en dos bases de datos, modelos de vista, volumen (el propio y el de cada participante), apagado de dispositivos, chat y reacciones, pistas y subtítulos, seguir donde se dejó, el informe de diagnóstico, actualizaciones, elegir y guardar el idioma, textos en inglés, unirse por enlace, una sola instancia, búsqueda del servidor de encuentro, expulsión de un participante |
| Golether.Web.Server.Tests | el servidor de encuentro: ajustes, límites, salas y encuentros, STUN, un encuentro completo hasta un flujo mTLS por la conexión perforada, rechazos (firma, sala ajena, sala vacía, anfitrión en silencio) |
| Golether.Web.Server.UI.Tests | el monitor del servidor: el formulario de ajustes y su archivo, el registro y su búsqueda, la gestión del servicio en Windows, Linux y macOS, la ventana en Avalonia Headless y las banderas de su selector de idioma |
| Golether.UI.Headless.Tests | la ventana principal en Avalonia Headless: el menú del recuadro de un participante, las pestañas de chat y eventos, el control emergente de volumen, los clics en el vídeo, la línea de tiempo fluida con el indicador de carga, las reacciones sobre el vídeo, la lista de idiomas y el cambio de todos los textos al momento, el modo de conexión y el bloque «Avanzado» de la pantalla de inicio, las ventanas de invitación y de ajustes |

Las pruebas (salvo `Golether.Localization.Tests`) se ejecutan en ruso: `Tests/Shared/UseRussianTexts.cs` activa el
paquete ruso para toda la ejecución, porque las expectativas están escritas con los textos rusos. Las comprobaciones de
otro idioma toman su propio `Localizer` y lo activan para un flujo de trabajo mediante `Texts.Scope(...)`, así las
pruebas en paralelo no se molestan entre sí.

## Añadir un idioma

1. Copia `Sources/Core/Golether.Localization/Languages/en.json` en `<code>.json` (un código ISO 639-1 de dos letras),
   rellena `Language.Code` y `Language.Name` (el nombre del idioma en ese idioma) y traduce los valores. Las claves y
   los marcadores `{0}`, `{1}` no cambian; una traducción que haya perdido una clave o un marcador hace fallar
   `Golether.Localization.Tests`.
   Las 64 palabras del código de verificación (`Verification.Word.00` … `Verification.Word.63`) son traducciones de las
   mismas cosas en cada idioma: dos personas con distintos idiomas de interfaz comparan palabras del mismo significado.
   Que sean cortas, fáciles de decir en voz alta y distintas entre sí.
2. Pon una bandera en `Sources/Apps/Golether.UI/Assets/Flags/<code>.png` (120×80, proporción 3:2): dibújala en
   `Scripts/Assets/generate-flags.ps1`, que dibuja todas las banderas (`en`, `de`, `fr`, `es`, `pt`, `ru`). El monitor
   del servidor muestra los mismos archivos. Una prueba comprueba que cada idioma tiene su bandera.
3. Eso es todo para la aplicación y el monitor: el paquete se recoge automáticamente, el idioma aparece en las listas y
   se elige según el idioma del sistema en el primer inicio.

El idioma de la aplicación no es todo lo que lee una persona. Se traducen aparte:

- la página del proyecto: una entrada más en la `list` de `Sources/Apps/Golether.Pages/assets/languages.js`, su bandera
  en `assets/flags/<code>.svg` y una tabla más en `assets/site.js` y `assets/join.js`;
- los asistentes de instalación: `[Languages]`, `[Messages]` y `[CustomMessages]` en `Scripts/App/golether.iss` y
  `Scripts/Server/golether-server.iss` (Inno Setup trae los mensajes estándar de cada idioma usado);
- las notas de los paquetes: una plantilla más en `Scripts/App/Notes` y `Scripts/Server/Notes` (`README.<code>.txt`,
  ver `Scripts/README.md`);
- la documentación (ver abajo).

Un texto nuevo: añade la clave a **cada** archivo `Languages/*.json` y tómala con `Texts.Get("Key")` en el código o
`{loc:Loc Key}` en XAML. No debe haber cadenas en lenguaje humano en el código (la prueba
`NoRussianTextIsLeftInTheCode`).

## Documentación

Los documentos se guardan por idioma: `Docs/En` es el conjunto principal, y `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt` y
`Docs/Ru` contienen el mismo conjunto en alemán, español, francés, portugués y ruso. Las reglas:

- el conjunto de archivos y sus nombres son iguales en cada idioma; un cambio de significado entra en todos los idiomas a
  la vez;
- los enlaces desde fuera (el README) llevan a `Docs/En`; la página del proyecto abre la guía en el idioma elegido en
  ella, y la inglesa cuando no hay esa traducción (la tabla `docs` de `assets/languages.js`);
- las imágenes son comunes y están en `Docs/images`;
- un idioma nuevo es una carpeta nueva `Docs/<Code>` con el mismo conjunto, más una línea en la tabla `docs` de la
  página.

La guía del administrador del servidor está junto al servidor, porque forma parte de sus paquetes:
`Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.md` es la inglesa y `ADMIN-GUIDE.<code>.md` las demás (`de`, `es`, `fr`,
`pt`, `ru`). Se aplican las mismas reglas: un contenido en cada idioma; otro idioma es un `ADMIN-GUIDE.<code>.md` más,
que el proyecto recoge solo.

## Ejecutar desde VS Code

`.vscode/launch.json`:

| Perfil | Qué hace |
|---|---|
| Golether: host | compila `Golether.UI` con sus dependencias y lo inicia con los datos en `.vscode/dev/host/data` |
| Golether: participant | lo mismo con los datos en `.vscode/dev/guest/data` |
| Golether: host + participant | dos dispositivos independientes en un equipo; invita con la dirección `127.0.0.1`. La regla de «una sola instancia» no les estorba: se aplica dentro de una carpeta de datos |
| Rendezvous server | compila e inicia `Golether.Web.Server` en `127.0.0.1:47900`; elige «Mi propio servidor» en el selector «Modo de conexión» del anfitrión y escribe esa dirección ([RendezvousServer.md](RendezvousServer.md)) |
| DbMigrator | compila e inicia el migrador con argumentos pedidos en un aviso |

Las tareas de compilación (`.vscode/tasks.json`) llaman a `dotnet build <csproj>`: solo se compila el proyecto elegido y
aquello de lo que depende. Una compilación completa de la solución es una tarea aparte, `build: solution`.

Variables de entorno:

| Variable | Propósito |
|---|---|
| `GOLETHER_DATA_DIR` | la carpeta de datos (base de datos, claves, túneles) en lugar de `data` junto al programa; con ella no se mueven los datos de compilaciones antiguas |
| `GOLETHER_LIBMPV` | una ruta explícita a libmpv |

## Base de datos

El esquema lo crean solo las migraciones de FluentMigrator. Para cambiar el esquema:

1. Añade `Scripts/000N_Name.Up.sql` y `000N_Name.Down.sql` a `Golether.Core.Data.Migrations.SQLite`.
2. Añade una clase `Versions/M000N_Name.cs` con `[Migration(N, "...")]`, derivada de `ScriptMigration`.
3. Actualiza las entidades y `GoletherDbContext` (EF Core solo asigna el modelo a las tablas).

La aplicación aplica las migraciones al iniciarse. A mano:

```bash
dotnet run --project Sources/Database/Golether.Dbs.SQLite.DbMigrator -- --database path/golether.db [--list | --down 0]
```

## Reglas del código

Ver [AGENTS.md](../../AGENTS.md) (en inglés): un tipo público por archivo, un comentario XML en cada miembro (CS1591 es un
error), llaves alrededor de cada bloque, líneas en blanco entre los pasos lógicos de un método, operadores lógicos al
final de la línea, las enumeraciones en carpetas `Enums`, las directivas `using` en los bloques System, bibliotecas,
Golether, solo dependencias compatibles con la GPL, el tiempo mediante `TimeProvider`/`IMonotonicClock`, las
entradas no fiables siempre se validan.

## Paquetes

| Script | Resultado |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/Golether-<version>-win-x64.zip` |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `.tar.gz` y `.zip` (si está `zip`), `install-desktop-entry.sh` |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `Golether.app` (Info.plist, icono, firma ad-hoc) en un archivo |
| `Scripts/App/publish-linux.ps1 [-Runtime linux-x64]` | el mismo archivo de Linux, compilado desde Windows en WSL (ver abajo) |
| `Scripts/Assets/update-site-screenshot.ps1` | la imagen de la página del proyecto a partir de `Docs/images/screenshot.png` |
| `Scripts/Assets/generate-installer-art.ps1` | las imágenes del asistente de instalación (un panel oscuro con el logotipo y el logotipo de la cabecera) en `artifacts/installer-art`; los dos scripts de compilación de instaladores lo llaman solos |
| `Scripts/Assets/generate-icon.ps1` | `golether.ico`, PNG 256/512 a partir de la geometría de `golether.svg` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` (el archivo lo compila la misma ejecución) | `artifacts/publish/Golether-<version>-setup.exe` (necesita Inno Setup 6 o posterior; comprobado con 7.1) y una entrada lista para el archivo de actualizaciones |
| `Scripts/build-release.ps1 [-Skip <pasos>] [-ContinueOnError]` | todo lo de una versión que se compila en Windows, uno tras otro, y `artifacts/publish/SHA256SUMS-<version>.txt` (ver abajo) |

La compilación es autónoma; la máquina de destino no necesita .NET. El contenido de `Native/<RID>/` va a `app/native`.
La raíz del paquete de Windows contiene `Golether.exe` con el icono de la aplicación: es el mismo host .NET (el target
`GoletherRootLauncher` de `Golether.UI.csproj`, la tarea del SDK `CreateAppHost`), solo que con la ruta relativa
`app\Golether.dll`. Así el paquete se inicia con un doble clic desde cualquier sitio, y `Golether.exe` puede tener un
acceso directo o anclarse a la barra de tareas. Los paquetes de Linux y macOS se compilan en el sistema de destino: solo
ahí sobreviven en el archivo los permisos de ejecución.

Cómo compilarlos sin tener a mano una máquina Linux o un Mac:

- **Linux desde Windows.** `./Scripts/App/publish-linux.ps1` ejecuta la compilación en WSL: el código fuente se copia al
  sistema de archivos de Linux y se compila allí, y el `Golether-<version>-linux-x64.tar.gz` terminado vuelve a
  `artifacts/publish`. La primera ejecución instala el .NET SDK en `~/.dotnet` de la distribución (con el script
  oficial, sin `sudo`).
- **Toda la versión desde Windows.** `./Scripts/build-release.ps1` ejecuta `App/build-installer.ps1`,
  `App/publish-linux.ps1`, `Server/build-server-installer.ps1`, `Server/publish-server-linux.ps1` y
  `Server/publish-server-macos.ps1` uno tras otro, cada uno en su propio proceso de PowerShell. El primer fallo lo
  detiene; `-ContinueOnError` ejecuta el resto, `-Skip AppLinux,ServerMacOS` omite pasos. Al final enumera los
  paquetes de la versión con su tamaño y su SHA-256 y escribe `artifacts/publish/SHA256SUMS-<version>.txt` para la
  versión. La máquina necesita Inno Setup, WSL y Git para Windows; cada paso indica lo que falta.
- **macOS en GitHub.** El flujo **Packages** (`.github/workflows/packages.yml`) compila solo los paquetes de macOS de
  la aplicación, para Apple silicon (`osx-arm64`) e Intel (`osx-x64`): necesitan `sips`, `iconutil` y
  `codesign`, que solo existen en macOS. Una ejecución a mano (Actions → Packages → Run workflow) da los archivos como
  artefactos de la ejecución. Una ejecución por una etiqueta de versión (`git tag 0.3.0; git push origin 0.3.0`)
  además los adjunta a la versión de esa etiqueta; si no hay versión, se crea un borrador. El resto de la versión sale
  de `build-release.ps1` y se añade a la misma versión; el flujo solo sustituye los archivos con los nombres de sus
  propios archivos.

## Depurar la red

- Dos instancias en una máquina: el perfil «Golether: host + participant», con la dirección `127.0.0.1:47800` en la
  invitación.
- Ping alto y pérdidas: en Windows [clumsy](https://jagt.github.io/clumsy/), en Linux
  `tc qdisc add dev lo root netem delay 600ms loss 2%`.
- Sin libmpv se usa el reproductor simulado: la posición sigue al reloj, así que la sincronización se puede comprobar sin
  vídeo.
