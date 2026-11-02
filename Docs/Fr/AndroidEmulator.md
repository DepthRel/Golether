# Démarrage de l'émulateur Android

Comment préparer un ordinateur Windows pour exécuter le client Android de Golether dans un émulateur. Android Studio
n'est pas nécessaire. Les scripts mettent eux-mêmes l'appareil en état de marche : le clavier, les boutons de navigation
et l'accélération graphique fonctionnent dès la fin de l'installation.

## Démarrage rapide

Il faut Windows 10 ou 11 (64 bits), la virtualisation activée dans les réglages BIOS/UEFI et environ 10 Go d'espace
libre. Lancez les scripts de `Scripts/Android` dans cet ordre ; un double clic suffit :

| Étape | Script | Droits d'administrateur | Ce qu'il fait |
|---|---|---|---|
| 1 | `enable-windows-features.bat` | oui, il les demande | Active la plateforme de l'hyperviseur Windows. Ensuite, **redémarrez Windows** |
| 2 | `install-android-tools.bat` | oui, il les demande | Installe le workload .NET Android, le JDK, l'Android SDK et l'émulateur : environ 6 Go, les téléchargements sont vérifiés par SHA-256 |
| 3 | `create-emulator.bat` | non | Crée l'appareil `Golether-API36`, le démarre et place un raccourci sur le bureau |

Ensuite, l'émulateur démarre avec le raccourci **Android Emulator (Golether-API36)** ou avec `start-emulator.bat`. Il démarre à froid à chaque fois : un instantané pour le démarrage rapide occuperait 4 Go, réécrits à chaque sortie.
Après une sortie ratée, l'émulateur demande s'il doit envoyer un rapport d'incident et attend la réponse avec
l'appareil éteint ; fermez cette fenêtre.

En ligne de commande, l'appareil s'éteint proprement avec `adb shell reboot -p` ; `adb emu kill` compte comme une sortie ratée. L'appareil n'a qu'une caméra arrière : pour essayer le passage d'une caméra à l'autre, démarrez l'émulateur une fois avec `-camera-front emulated`.

Les étapes 2 et 3 peuvent être relancées : ce qui est installé reste en place et un appareil existant garde ses données.

## Où va quoi

| Quoi | Où |
|---|---|
| Workload .NET Android | Le dossier de .NET, `C:\Program Files\dotnet\packs` : un workload y va toujours |
| Microsoft OpenJDK 21 | `C:\Program Files\AndroidTools\jdk-21` |
| Android SDK, émulateur, image système | `C:\Program Files\AndroidTools\sdk` |
| Appareils virtuels | `%USERPROFILE%\.android\avd` : l'émulateur y écrit pendant son fonctionnement, ils ne peuvent donc pas être dans Program Files |

`JAVA_HOME` et `ANDROID_HOME` sont définies pour tous les utilisateurs. Redémarrez les terminaux et Visual Studio pour
les voir.

Deux variables changent les dossiers : `ANDROID_TOOLS_ROOT` pour les outils et `ANDROID_AVD_HOME` pour les appareils.
Avec `ANDROID_TOOLS_ROOT`, les droits d'administrateur ne sont pas nécessaires (si le workload est installé), mais
`JAVA_HOME` et `ANDROID_HOME` sont alors à définir vous-même.

## Les réglages de l'appareil

`avdmanager` crée un appareil peu pratique, c'est pourquoi `create-emulator.bat` le modifie :

| Réglage | Par défaut | Mis par le script | Pourquoi |
|---|---|---|---|
| `hw.keyboard` | `no` | `yes` | Sinon, le clavier de l'ordinateur ne saisit rien |
| `hw.gpu.enabled` | `no` | `yes` | Sinon, le processeur dessine l'image : le système rame et la vidéo n'a pas OpenGL ES |
| `hw.gpu.mode` | `auto` | `auto` | La carte graphique si possible ; sur un ordinateur sans carte, le processeur dessine |
| `hw.ramSize` | `2G` | `4G` | 2 Go, c'est trop peu pour Android 16 avec Google APIs |
| Navigation | gestes | trois boutons | Avec les gestes, il n'y a pas de boutons à l'écran, et les gestes sont peu pratiques à la souris |

Les premiers réglages sont des lignes de `config.ini` de l'appareil. La navigation se règle dans le système en marche et
y reste :

```powershell
adb shell cmd overlay enable-exclusive --category com.android.internal.systemui.navbar.threebutton
```

## Travailler avec l'émulateur

- Une fenêtre de console réduite démarre avec l'émulateur. La fermer ferme l'émulateur.
- Depuis l'émulateur, l'ordinateur est joignable à l'adresse `10.0.2.2`.
- Tant que le clavier physique est activé, le clavier à l'écran n'apparaît pas : c'est normal.
- L'émulateur ne montre ni le vrai décodage vidéo matériel, ni la chauffe, ni la vitesse d'un téléphone : pour cela, il
  faut un vrai téléphone relié en USB, avec le débogage USB activé.
- `sdkmanager` et `adb` ne sont pas dans le `PATH`. Ils se trouvent dans `C:\Program Files\AndroidTools\sdk`, dans
  `cmdline-tools\latest\bin` et `platform-tools`. `sdkmanager` écrit dans Program Files et demande donc un terminal
  lancé en administrateur.

## Compiler et lancer le client

Les projets Android ne font pas partie de la compilation de `Golether.slnx` : elle doit fonctionner sans le workload
Android. Ils se compilent à part et ont besoin des composants natifs dans `Native/android-*` (voir `Native/README.md`) :

```powershell
dotnet build Sources/Apps/Golether.Mobile.Android -t:Install
```

Avec `-p:GoletherDeviceChecks=true`, le paquet contient aussi des vérifications de ce qui ne peut se prouver que sur un
appareil : données, clés, TLS, libmpv, GStreamer. Elles se lancent par un extra de l'activité et répondent dans logcat
avec l'étiquette `GOLETHER-CHECK` :

```powershell
adb shell am start -n depthrel.golether/depthrel.golether.MainActivity --ez golether.checks true --ez golether.conference true
adb logcat -s GOLETHER-CHECK
```

## Problèmes

| Ce que vous voyez | Ce que cela signifie |
|---|---|
| `sdkmanager` n'est pas reconnu | Il n'est pas dans le `PATH` : utilisez le chemin complet ou `install-android-tools.bat` |
| Accès refusé pendant que `sdkmanager` installe | Le terminal n'a pas été lancé en administrateur |
| `WARNING: The SDK Manager CLI tool (sdkmanager) is deprecated` | Un avertissement, pas une erreur |
| `Error: Could not load devices from ...\devices.xml` de `avdmanager` | Sans danger : l'image n'a pas ce fichier, l'appareil est créé |
| Le clavier ne saisit rien ou le système rame | L'appareil a été créé à la main : lancez `create-emulator.bat`, il garde les données et corrige les réglages |
| L'émulateur est très lent, l'étape 1 a été sautée | Lancez `enable-windows-features.bat` et redémarrez Windows. Vérifiez avec `emulator -accel-check` |
| `enable-windows-features.bat` échoue | La virtualisation est désactivée dans les réglages BIOS/UEFI |
