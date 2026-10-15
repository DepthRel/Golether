Golether {version} ({runtime})

[win] Lancement : Golether.exe dans ce dossier (vous pouvez créer un raccourci ou l'épingler à la barre des tâches).
[unix] Lancement : ./Golether.sh
[linux] Menu des applications et liens d'invitation : lancez une fois ./install-desktop-entry.sh.
Toutes les données (clés, base de données, tunnels, composants) sont dans le dossier data à côté de l'application. Déplacez et supprimez le dossier Golether toujours en entier.
[win] Vidéo, caméras et voix : les composants sont inclus. S'il en manque un, la carte « Composants » de l'écran d'accueil l'installe.
[win] Tunnels AmneziaWG : inutile d'installer AmneziaWG pour Windows. À la première ouverture de la fenêtre « Tunnels AWG », Golether télécharge son propre moteur (environ 4 Mo, Internet nécessaire). Windows demande l'autorisation d'un administrateur quand un tunnel monte.
[win] Windows affiche un avertissement SmartScreen tant que la version n'est pas signée.
[linux] Vidéo : installez le paquet libmpv de votre distribution (libmpv2 / mpv-libs). Caméras et voix : gstreamer1.0-plugins-base, -good, -bad et gstreamer1.0-nice. Tunnels : amneziawg-tools et le module amneziawg (ou amneziawg-go).
[macos] La version n'est pas signée par Apple : au premier lancement, ouvrez « Réglages Système → Confidentialité et sécurité → Ouvrir quand même », ou lancez : xattr -dr com.apple.quarantine Golether.app
[macos] Vidéo : brew install mpv. Caméras et voix : brew install gstreamer.
Guide d'utilisation : https://github.com/DepthRel/Golether/blob/main/Docs/Fr/UserGuide.md
