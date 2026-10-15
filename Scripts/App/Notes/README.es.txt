Golether {version} ({runtime})

[win] Inicio: Golether.exe en esta carpeta (puedes crear un acceso directo o anclarlo a la barra de tareas).
[unix] Inicio: ./Golether.sh
[linux] Menú de aplicaciones y enlaces de invitación: ejecuta una vez ./install-desktop-entry.sh.
Todos los datos (claves, base de datos, túneles, componentes) se guardan en la carpeta data junto a la aplicación. Mueve y borra la carpeta de Golether siempre entera.
[win] Vídeo, cámaras y voz: los componentes están incluidos. Si falta alguno, la tarjeta «Componentes» de la pantalla de inicio lo instala.
[win] Túneles AmneziaWG: no hace falta instalar AmneziaWG para Windows. La primera vez que se abre la ventana «Túneles AWG», Golether descarga su propio motor (unos 4 MB, hace falta Internet). Windows pide permiso de administrador al levantar un túnel.
[win] Windows muestra un aviso de SmartScreen mientras la compilación no esté firmada.
[linux] Vídeo: instala el paquete libmpv de tu distribución (libmpv2 / mpv-libs). Cámaras y voz: gstreamer1.0-plugins-base, -good, -bad y gstreamer1.0-nice. Túneles: amneziawg-tools y el módulo amneziawg (o amneziawg-go).
[macos] La compilación no está firmada por Apple: en el primer inicio abre «Ajustes del Sistema → Privacidad y seguridad → Abrir igualmente» o ejecuta: xattr -dr com.apple.quarantine Golether.app
[macos] Vídeo: brew install mpv. Cámaras y voz: brew install gstreamer.
Guía de uso: https://github.com/DepthRel/Golether/blob/main/Docs/Es/UserGuide.md
