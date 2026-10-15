# Développer Golether

## Environnement

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- VS Code avec C# Dev Kit et l'extension Avalonia, ou Visual Studio / Rider.
- Les composants natifs (libmpv, GStreamer) sont préparés par l'utilitaire `golether-components`, voir
  [Native/README.md](../../Native/README.md).
- Windows PowerShell 5.1 ou PowerShell 7 pour `Scripts/*/*.ps1`, bash pour `Scripts/*/*.sh`. Les scripts sont regroupés
  selon ce qu'ils construisent : `Scripts/App`, `Scripts/Server`, `Scripts/Assets` (voir `Scripts/README.md`).

## Construction et tests

```bash
dotnet build Golether.slnx
dotnet test --solution Golether.slnx                                    # tous les tests
dotnet test --project Tests/Golether.Sync.Tests/Golether.Sync.Tests.csproj  # un projet
```

La variable `GOLETHER_TEST_SCREENSHOTS` (un dossier) enregistre des images de la fenêtre principale depuis les tests
headless, pour un contrôle visuel sans ouvrir de fenêtres à l'écran.

La variable `GOLETHER_TEST_GST_LOG` (un chemin de fichier) active le journal détaillé de GStreamer dans les tests de
conférence.

La variable `GOLETHER_TEST_AWG_PACKAGES` (un dossier avec des fichiers `amneziawg-<arch>-<version>.msi` des versions
d'Amnezia) active la vérification qui décompresse le moteur de tunnel depuis un vrai paquet. Sans elle, ce test est
ignoré, donc une exécution ordinaire ne demande aucun téléchargement. Le paquet peut être téléchargé à l'adresse de
`ComponentCatalog` ; le nom du fichier vient aussi de là.

Les tests sont en xUnit v3 sur Microsoft.Testing.Platform (activé dans `global.json`). Les paquets de test communs sont
référencés dans `Directory.Build.props` pour chaque projet `*.Tests`.

| Projet | Ce qu'il vérifie |
|---|---|
| Golether.Components.Tests | le catalogue, les indications pour Linux/macOS, les imports PE, l'installeur (somme, taille, cache), la réutilisation de GStreamer |
| Golether.Conventions.Tests | les règles d'AGENTS.md que les analyseurs ne peuvent pas vérifier, sur les sources lues avec Roslyn : un type par fichier, des types imbriqués privés, des commentaires XML, les énumérations dans des dossiers Enums, les opérateurs logiques en fin de ligne, l'ordre des using, le temps seulement via TimeProvider, le schéma seulement via les migrations, les projets dans leurs dossiers de solution |
| Golether.Media.Conference.GStreamer.Tests | deux nœuds WebRTC en loopback sur des sources de test, la validation des signaux, les statistiques de connexion et le passage au flux économique de la caméra ; ignorés sans GStreamer |
| Golether.Core.Tests | PeerId, PlaybackState, DriftCorrector, adresses, chemins des données (y compris une installation « pour tous les utilisateurs ») |
| Golether.Localization.Tests | paquets de langue : lecture, le catalogue, le choix d'une langue selon la culture, le repli sur l'anglais, les mêmes clés et emplacements dans chaque langue, les 64 mots distincts du code de vérification et un drapeau pour chaque langue, chaque clé demandée existe, pas de texte russe dans le code |
| Golether.Security.Tests | signatures, le magasin de clés, invitations (clé et lien web, durée de validité, l'invitation partagée et la limite de demandes), l'adresse scellée du serveur, le code de vérification, l'admission |
| Golether.Transports.Tests | trames, mTLS en loopback, épinglage de la clé, NAT-PMP et UPnP avec un faux routeur, messages et signature du serveur de rendez-vous, TCP hole punching en loopback |
| Golether.Sync.Tests | horloges avec une RTT de 1240 ms, messages, authority, follower |
| Golether.Media.Streaming.Tests | transfert de morceaux, le cache, le flux pour le lecteur, hachages sans données, échange de morceaux entre participants et vérification avec les hachages |
| Golether.Media.Player.Tests | le lecteur simulé, l'enregistrement `golether://`, les liaisons de la souris, le volume, les pistes audio et les sous-titres, les plages chargées d'un vrai libmpv (ignorés sans lui) |
| Golether.Tunnels.AmneziaWG.Tests | clés, paramètres, `.conf`, paquets, le contrôleur |
| Golether.Core.Data.Tests | migrations, magasins EF Core, le migrateur |
| Golether.Session.Tests | l'hôte et un participant dans leur ensemble : admission, le fichier, le départ commun, la pause, l'invitation partagée, le retrait d'un participant et le refus, une invitation expirée |
| Golether.UI.Tests | le processus des tunnels sur deux bases, les modèles de vue, le volume (le sien et celui de chaque participant), la coupure des appareils, la discussion et les réactions, les pistes et sous-titres, la reprise là où on s'est arrêté, le rapport de diagnostic, les mises à jour, le choix et l'enregistrement de la langue, les textes en anglais, rejoindre par lien, l'instance unique, la recherche du serveur de rendez-vous, le retrait d'un participant |
| Golether.Web.Server.Tests | le serveur de rendez-vous : réglages, limites, salles et rencontres, STUN, une rencontre complète jusqu'à un flux mTLS sur la connexion percée, refus (signature, salle d'autrui, salle vide, hôte silencieux) |
| Golether.Web.Server.UI.Tests | le moniteur du serveur : le formulaire des réglages et son fichier, le journal et sa recherche, la gestion du service sous Windows, Linux et macOS, la fenêtre en Avalonia Headless et les drapeaux de son sélecteur de langue |
| Golether.UI.Headless.Tests | la fenêtre principale en Avalonia Headless : le menu d'une vignette de participant, les onglets discussion et événements, la fenêtre de volume, les clics sur la vidéo, la barre de lecture fluide avec l'indicateur de chargement, les réactions sur la vidéo, la liste des langues et le changement de tous les textes à la volée, le mode de connexion et le bloc « Avancé » de l'écran d'accueil, les fenêtres d'invitation et de réglages |

Les tests (sauf `Golether.Localization.Tests`) tournent en russe : `Tests/Shared/UseRussianTexts.cs` active le paquet
russe pour toute l'exécution, parce que les attentes sont écrites d'après les textes russes. Les vérifications d'une
autre langue prennent leur propre `Localizer` et l'activent pour un flux de travail via `Texts.Scope(...)`, donc les
tests parallèles ne se gênent pas.

## Ajouter une langue

1. Copiez `Sources/Core/Golether.Localization/Languages/en.json` en `<code>.json` (un code ISO 639-1 à deux lettres),
   remplissez `Language.Code` et `Language.Name` (le nom de la langue dans cette langue) et traduisez les valeurs. Les
   clés et les emplacements `{0}`, `{1}` ne changent pas ; une traduction qui a perdu une clé ou un emplacement fait
   échouer `Golether.Localization.Tests`.
   Les 64 mots du code de vérification (`Verification.Word.00` … `Verification.Word.63`) sont des traductions des mêmes
   choses dans chaque langue : deux personnes avec des langues d'interface différentes comparent des mots de même sens.
   Gardez-les courts, faciles à dire à voix haute et distincts les uns des autres.
2. Placez un drapeau dans `Sources/Apps/Golether.UI/Assets/Flags/<code>.png` (120×80, rapport 3:2) : dessinez-le dans
   `Scripts/Assets/generate-flags.ps1`, qui dessine tous les drapeaux (`en`, `de`, `fr`, `es`, `pt`, `ru`). Le moniteur
   du serveur affiche les mêmes fichiers. Un test vérifie que chaque langue a son drapeau.
3. C'est tout pour l'application et le moniteur : le paquet est pris en compte automatiquement, la langue apparaît dans
   les listes et est choisie selon la langue du système au premier lancement.

La langue de l'application n'est pas tout ce que lit une personne. Sont traduits à part :

- la page du projet : une entrée de plus dans la `list` de `Sources/Apps/Golether.Pages/assets/languages.js`, son
  drapeau dans `assets/flags/<code>.svg` et une table de plus dans `assets/site.js` et `assets/join.js` ;
- les assistants d'installation : `[Languages]`, `[Messages]` et `[CustomMessages]` dans `Scripts/App/golether.iss` et
  `Scripts/Server/golether-server.iss` (Inno Setup fournit les messages standard de chaque langue utilisée) ;
- les notes des paquets : un modèle de plus dans `Scripts/App/Notes` et `Scripts/Server/Notes` (`README.<code>.txt`, voir
  `Scripts/README.md`) ;
- la documentation (voir plus bas).

Un nouveau texte : ajoutez la clé à **chaque** fichier `Languages/*.json` et prenez-la avec `Texts.Get("Key")` dans le
code ou `{loc:Loc Key}` en XAML. Il ne doit y avoir aucune chaîne en langue humaine dans le code (le test
`NoRussianTextIsLeftInTheCode`).

## Documentation

Les documents sont rangés par langue : `Docs/En` est l'ensemble principal, et `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt`
et `Docs/Ru` contiennent le même ensemble en allemand, espagnol, français, portugais et russe. Les règles :

- l'ensemble des fichiers et leurs noms sont les mêmes dans chaque langue ; un changement de sens va dans toutes les
  langues à la fois ;
- les liens de l'extérieur (le README) mènent à `Docs/En` ; la page du projet ouvre le guide dans la langue choisie sur
  elle, et l'anglais quand cette traduction n'existe pas (la table `docs` de `assets/languages.js`) ;
- les images sont communes et se trouvent dans `Docs/images` ;
- une nouvelle langue est un nouveau dossier `Docs/<Code>` avec le même ensemble, plus une ligne dans la table `docs` de
  la page.

Le guide de l'administrateur du serveur se trouve à côté du serveur, car il fait partie de ses paquets :
`Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.md` est l'anglais et `ADMIN-GUIDE.<code>.md` les autres (`de`, `es`, `fr`,
`pt`, `ru`). Les mêmes règles s'appliquent : un même contenu dans chaque langue ; une autre langue est un
`ADMIN-GUIDE.<code>.md` de plus, que le projet prend en compte tout seul.

## Lancer depuis VS Code

`.vscode/launch.json` :

| Profil | Ce qu'il fait |
|---|---|
| Golether: host | construit `Golether.UI` avec ses dépendances et le lance avec les données dans `.vscode/dev/host/data` |
| Golether: participant | la même chose avec les données dans `.vscode/dev/guest/data` |
| Golether: host + participant | deux appareils indépendants sur un ordinateur ; invitez avec l'adresse `127.0.0.1`. La règle « instance unique » ne les gêne pas : elle s'applique à l'intérieur d'un dossier de données |
| Rendezvous server | construit et lance `Golether.Web.Server` sur `127.0.0.1:47900` ; choisissez « Mon propre serveur » dans le sélecteur « Mode de connexion » de l'hôte et saisissez cette adresse ([RendezvousServer.md](RendezvousServer.md)) |
| DbMigrator | construit et lance le migrateur avec des arguments demandés dans une invite |

Les tâches de construction (`.vscode/tasks.json`) appellent `dotnet build <csproj>` : seul le projet choisi et ce dont il
dépend sont construits. Une construction complète de la solution est une tâche à part, `build: solution`.

Variables d'environnement :

| Variable | Rôle |
|---|---|
| `GOLETHER_DATA_DIR` | le dossier des données (base, clés, tunnels) au lieu de `data` à côté du programme ; avec elle, les données des premières versions ne sont pas déplacées |
| `GOLETHER_LIBMPV` | un chemin explicite vers libmpv |

## Base de données

Le schéma n'est créé que par les migrations FluentMigrator. Pour changer le schéma :

1. Ajoutez `Scripts/000N_Name.Up.sql` et `000N_Name.Down.sql` à `Golether.Core.Data.Migrations.SQLite`.
2. Ajoutez une classe `Versions/M000N_Name.cs` avec `[Migration(N, "...")]`, dérivée de `ScriptMigration`.
3. Mettez à jour les entités et `GoletherDbContext` (EF Core ne fait que faire correspondre le modèle aux tables).

L'application applique les migrations au lancement. À la main :

```bash
dotnet run --project Sources/Database/Golether.Dbs.SQLite.DbMigrator -- --database path/golether.db [--list | --down 0]
```

## Règles du code

Voir [AGENTS.md](../../AGENTS.md) (en anglais) : un type public par fichier, un commentaire XML sur chaque membre (CS1591
est une erreur), des accolades autour de chaque bloc, des lignes vides entre les étapes logiques d'une méthode, les
opérateurs logiques en fin de ligne, les énumérations dans des dossiers `Enums`, les directives `using` en blocs System,
bibliothèques, Golether, uniquement des dépendances compatibles avec la GPL, le temps via
`TimeProvider`/`IMonotonicClock`, les entrées non fiables toujours validées.

## Paquets

| Script | Résultat |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/Golether-<version>-win-x64.zip` |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `.tar.gz` et `.zip` (si `zip` est disponible), `install-desktop-entry.sh` |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `Golether.app` (Info.plist, icône, signature ad hoc) dans une archive |
| `Scripts/App/publish-linux.ps1 [-Runtime linux-x64]` | la même archive Linux, construite depuis Windows dans WSL (voir plus bas) |
| `Scripts/Assets/update-site-screenshot.ps1` | l'image de la page du projet à partir de `Docs/images/screenshot.png` |
| `Scripts/Assets/generate-installer-art.ps1` | les images de l'assistant d'installation (un panneau sombre avec le logo et le logo de l'en-tête) dans `artifacts/installer-art` ; les deux scripts de construction des programmes d'installation l'appellent eux-mêmes |
| `Scripts/Assets/generate-icon.ps1` | `golether.ico`, PNG 256/512 à partir de la géométrie de `golether.svg` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` (l'archive est construite par la même exécution) | `artifacts/publish/Golether-<version>-setup.exe` (nécessite Inno Setup 6 ou plus récent ; vérifié avec 7.1) et une entrée prête pour le fichier des mises à jour |
| `Scripts/build-release.ps1 [-Skip <étapes>] [-ContinueOnError]` | tout ce qu'une version construit sous Windows, l'un après l'autre, et `artifacts/publish/SHA256SUMS-<version>.txt` (voir plus bas) |

La construction est autonome ; la machine cible n'a pas besoin de .NET. Le contenu de `Native/<RID>/` va dans
`app/native`. La racine du paquet Windows contient `Golether.exe` avec l'icône de l'application : c'est le même hôte .NET
(la cible `GoletherRootLauncher` de `Golether.UI.csproj`, la tâche du SDK `CreateAppHost`), seulement avec le chemin
relatif `app\Golether.dll`. Ainsi le paquet démarre d'un double-clic depuis n'importe où, et `Golether.exe` peut recevoir
un raccourci ou être épinglé à la barre des tâches. Les paquets Linux et macOS sont construits sur le système cible :
c'est seulement là que les droits d'exécution survivent dans l'archive.

Comment les construire sans machine Linux ni Mac sous la main :

- **Linux depuis Windows.** `./Scripts/App/publish-linux.ps1` lance la construction dans WSL : les sources sont copiées
  dans le système de fichiers Linux et construites là-bas, et le `Golether-<version>-linux-x64.tar.gz` terminé revient
  dans `artifacts/publish`. La première exécution installe le .NET SDK dans `~/.dotnet` de la distribution (avec le
  script officiel, sans `sudo`).
- **Toute la version depuis Windows.** `./Scripts/build-release.ps1` lance `App/build-installer.ps1`,
  `App/publish-linux.ps1`, `Server/build-server-installer.ps1`, `Server/publish-server-linux.ps1` et
  `Server/publish-server-macos.ps1` l'un après l'autre, chacun dans son propre processus PowerShell. Le premier échec
  l'arrête ; `-ContinueOnError` lance le reste, `-Skip AppLinux,ServerMacOS` saute des étapes. À la fin, il liste les
  paquets de la version avec leur taille et leur SHA-256 et écrit `artifacts/publish/SHA256SUMS-<version>.txt` pour la
  version. La machine a besoin d'Inno Setup, de WSL et de Git pour Windows ; chaque étape dit ce qui manque.
- **macOS sur GitHub.** Le workflow **Packages** (`.github/workflows/packages.yml`) ne construit que les paquets macOS
  de l'application, pour puce Apple (`osx-arm64`) et Intel (`osx-x64`) : ils ont besoin de `sips`, `iconutil` et
  `codesign`, qui n'existent que sous macOS. Une exécution manuelle (Actions → Packages → Run workflow) donne les
  archives comme artefacts de l'exécution. Une exécution par une étiquette de version
  (`git tag 0.3.0; git push origin 0.3.0`) les joint aussi à la version de cette étiquette ; s'il n'y a pas de version,
  un brouillon est créé. Le reste de la version vient de `build-release.ps1` et est ajouté à la même version ; le
  workflow ne remplace que les fichiers portant les noms de ses propres archives.

## Déboguer le réseau

- Deux instances sur une machine : le profil « Golether: host + participant », avec l'adresse `127.0.0.1:47800` dans
  l'invitation.
- Ping élevé et pertes : sous Windows [clumsy](https://jagt.github.io/clumsy/), sous Linux
  `tc qdisc add dev lo root netem delay 600ms loss 2%`.
- Sans libmpv, le lecteur simulé est utilisé : la position suit l'horloge, donc la synchronisation peut être vérifiée
  sans vidéo.
