# Serveur de rendez-vous de Golether : guide de l'administrateur

Ce guide se trouve dans le dossier `docs` de chaque paquet du serveur et convient aussi bien à Windows qu'à Linux et
macOS. Là où les étapes diffèrent, elles sont données pour chaque système séparément. Le même guide dans d'autres
langues : [English](ADMIN-GUIDE.md), [Deutsch](ADMIN-GUIDE.de.md), [Español](ADMIN-GUIDE.es.md),
[Português](ADMIN-GUIDE.pt.md), [Русский](ADMIN-GUIDE.ru.md).

- [1. Ce que fait le serveur](#1-ce-que-fait-le-serveur)
- [2. Ce qu'il vous faut](#2-ce-quil-vous-faut)
- [3. Ce que contient le paquet](#3-ce-que-contient-le-paquet)
- [4. Installation](#4-installation)
- [5. Réglages](#5-réglages)
- [6. Routeur et pare-feu](#6-routeur-et-pare-feu)
- [7. Ce qu'il faut dire aux hôtes](#7-ce-quil-faut-dire-aux-hôtes)
- [8. Gérer le service](#8-gérer-le-service)
- [9. Vérifier que tout fonctionne](#9-vérifier-que-tout-fonctionne)
- [10. Mise à jour](#10-mise-à-jour)
- [11. Désinstallation](#11-désinstallation)
- [12. Sécurité](#12-sécurité)
- [13. Dépannage](#13-dépannage)

## 1. Ce que fait le serveur

Golether est un programme pour regarder des vidéos ensemble. En général, l'hôte et les participants sont derrière des
routeurs domestiques sans port ouvert (une IP « grise », le NAT du fournisseur) et ne peuvent pas se joindre directement.
Le serveur de rendez-vous les présente l'un à l'autre :

- l'hôte garde une « salle » de sa séance sur le serveur ;
- un participant, arrivé par le lien d'invitation, demande au serveur de le mettre en relation avec l'hôte ;
- le serveur indique à chacun l'adresse publique de l'autre, et ils percent eux-mêmes une connexion **directe** à travers
  leurs routeurs ;
- sur le même port, mais en UDP, le serveur répond aux requêtes STUN : c'est ainsi que les caméras et la voix se
  connectent elles aussi directement.

Le film, la synchronisation, la discussion, les caméras et la voix ne passent **pas** par le serveur ; seuls de courts
messages de service passent au moment de la connexion. La charge est minimale ; un serveur sert des centaines de séances
simultanées.

## 2. Ce qu'il vous faut

| | |
|---|---|
| Réseau | une adresse IPv4 publique sur la machine, ou sur un routeur où l'on peut rediriger un port |
| Ports | **TCP 47900** et **UDP 47900** depuis l'extérieur (le numéro est configurable) ; le port 47980 est local uniquement |
| Windows | Windows 10 / Windows Server 2016 ou plus récent, x64 ou arm64 |
| Linux | x64 ou arm64 avec glibc (Debian 11+, Ubuntu 20.04+, RHEL/Alma/Rocky 8+ et similaires) et systemd pour tourner comme service |
| macOS | macOS 14 ou plus récent, puce Apple ou Intel |
| Ressources | environ 100 Mo de disque et 60–100 Mo de mémoire |

.NET n'a pas besoin d'être installé : il est dans le paquet.

## 3. Ce que contient le paquet

| Fichier | Rôle |
|---|---|
| `golether-server` (`golether-server.exe` sous Windows) | démarre le serveur ; le seul fichier à la racine ; un même programme tourne en console et comme service |
| `bin/` | le programme lui-même, le runtime .NET et les bibliothèques ; inutile d'y regarder |
| `config/appsettings.json` | les réglages : adresse, port, limites (voir la [section 5](#5-réglages)) |
| `config/monitor.json` | les réglages du moniteur lui-même (sa langue) ; ils ne touchent jamais au serveur |
| `docs/ADMIN-GUIDE.md`, `docs/ADMIN-GUIDE.<code>.md` | ce guide en anglais et en allemand (`de`), espagnol (`es`), français (`fr`), portugais (`pt`) et russe (`ru`) |
| `docs/README.txt`, `docs/README.<code>.txt` | une courte note pour ce paquet précis, dans les mêmes langues |
| `service/install-service.sh` | Linux et macOS : installe, met à jour et retire le service |
| `service/golether-server.service` | Linux : l'unité systemd (installée par `install-service.sh`) |
| `service/org.golether.server.plist` | macOS : la tâche launchd (installée par `install-service.sh`) |
| `golether-server-monitor` (`golether-server-monitor.exe` sous Windows) | démarre Golether Server Monitor, à côté du serveur ; le moniteur lui-même est dans `monitor/`. Le moniteur : l'état du serveur et son journal des événements dans une fenêtre, le démarrage et l'arrêt du service, les réglages (voir la [section 9](#9-vérifier-que-tout-fonctionne)) |

```
GoletherServer/
  golether-server(.exe)
  golether-server-monitor(.exe)
  bin/        le programme et les bibliothèques
  config/     appsettings.json, monitor.json
  docs/       ADMIN-GUIDE.md, ADMIN-GUIDE.<code>.md, README.txt, README.<code>.txt
  service/    Linux et macOS uniquement
  monitor/    Golether Server Monitor (les fichiers du programme)
```

Le programme d'installation Windows dispose les fichiers de la même façon ; ses propres fichiers de désinstallation sont
dans le dossier `uninstall`.

Pour Windows, il existe aussi un programme d'installation séparé, `GoletherServer-<version>-setup.exe`.

## 4. Installation

Choisissez la méthode pour votre système. Dans tous les cas, il faut ouvrir les ports après l'installation
([section 6](#6-routeur-et-pare-feu)).

### Windows : le programme d'installation (recommandé)

Lancez `GoletherServer-<version>-setup.exe` (des droits d'administrateur sont nécessaires). L'assistant parle anglais,
allemand, espagnol, français, portugais et russe ; sa première fenêtre propose la langue de Windows (l'anglais si
l'assistant ne la parle pas), et vous pouvez en choisir une autre. Le programme d'installation :

- place le serveur dans `C:\Program Files\Golether Server` ;
- enregistre le service **GoletherServer** (« Golether rendezvous server ») : il démarre avec l'ordinateur et redémarre
  5 secondes après une panne ;
- ajoute une règle du pare-feu Windows pour le programme (TCP et UDP, tout port) ;
- ajoute des raccourcis vers les réglages, ce guide et la page d'état dans « Démarrer → Golether Server ».

Le service et la règle du pare-feu peuvent être désactivés avec les cases de l'étape « Tâches supplémentaires ».

### Windows : depuis l'archive zip

Décompressez l'archive, par exemple dans `C:\GoletherServer`, et lancez `golether-server.exe` depuis la racine du
dossier : le serveur fonctionne tant que la fenêtre de la console est ouverte. Pour le faire tourner comme service,
exécutez dans PowerShell **en administrateur**, depuis la racine du dossier du serveur :

```powershell
$exe = Join-Path (Get-Location) 'golether-server.exe'
New-Service -Name GoletherServer -DisplayName 'Golether rendezvous server' -BinaryPathName "`"$exe`"" -StartupType Automatic
sc.exe failure GoletherServer reset= 86400 actions= restart/5000/restart/5000/restart/60000
New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program $exe -Action Allow
Start-Service GoletherServer
```

Ne déplacez plus le dossier ensuite : le service lance le programme par son chemin complet.

### Linux : un service systemd

```bash
tar -xzf GoletherServer-<version>-linux-x64.tar.gz
cd GoletherServer
sudo ./service/install-service.sh
```

Le script copie le serveur dans `/opt/golether-server`, installe l'unité `golether-server.service`, l'active au
démarrage et lance le service. Avec `ufw` activé, le script ouvre lui-même le port des réglages ; pour `firewalld` et
`iptables`, voir la [section 6](#6-routeur-et-pare-feu). Le service tourne sous un utilisateur temporaire sans
privilèges (`DynamicUser`) avec une protection du système de fichiers ; le serveur n'a pas besoin des droits root.

Sans service : `./golether-server` à la racine du dossier du paquet (`Ctrl+C` l'arrête).

### macOS : un service launchd

```bash
tar -xzf GoletherServer-<version>-osx-arm64.tar.gz     # osx-x64 pour Intel
cd GoletherServer
sudo ./service/install-service.sh
```

Le script copie le serveur dans `/usr/local/golether-server`, retire la quarantaine des fichiers téléchargés, signe le
programme avec une signature locale (macOS sur puce Apple en exige une), installe la tâche
`/Library/LaunchDaemons/org.golether.server.plist` et la démarre. Le serveur tourne sous l'utilisateur `nobody` et
redémarre après une panne.

Sans service : `./golether-server` à la racine du dossier du paquet. Si macOS indique que le programme ne peut pas être
ouvert, lancez là `xattr -dr com.apple.quarantine .` et `codesign --force --sign - golether-server bin/golether-server`.
Au premier lancement, le pare-feu de macOS (s'il est activé) demande s'il faut autoriser les connexions entrantes :
autorisez-les.

## 5. Réglages

Tous les réglages sont dans `config/appsettings.json` à l'intérieur du dossier du serveur :

| Système | Où se trouve le fichier après l'installation comme service |
|---|---|
| Windows | `C:\Program Files\Golether Server\config\appsettings.json` |
| Linux | `/opt/golether-server/config/appsettings.json` |
| macOS | `/usr/local/golether-server/config/appsettings.json` |

Si le fichier manque (supprimé par erreur, par exemple), le serveur ne s'arrête pas : il le réécrit avec les réglages par
défaut ci-dessous et le signale dans son journal. Si le dossier n'est pas accessible en écriture, le serveur démarre
quand même avec ces mêmes valeurs par défaut. Une clé absente du fichier prend elle aussi sa valeur par défaut.

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

| Paramètre `Rendezvous:…` | Par défaut | Ce qu'il définit |
|---|---|---|
| `ListenAddress` | `0.0.0.0` | l'adresse IP **de cette machine** sur laquelle écouter : `0.0.0.0` est toute interface IPv4, une adresse précise seulement celle-là, `::` est IPv6 et IPv4 |
| `Port` | `47900` | le port : TCP pour les rencontres et le même numéro en UDP pour le STUN |
| `EnableStun` | `true` | s'il faut répondre au STUN en UDP ; sans lui, les caméras et la voix de réseaux différents se connectent moins bien |
| `MaxConnections` | `1024` | connexions ouvertes en même temps |
| `MaxRooms` | `1024` | séances en cours en même temps |
| `MaxPendingPerRoom` | `16` | connexions en attente de la réponse d'un hôte |
| `MaxConnectionsPerMinute` | `120` | nouvelles connexions depuis une IP par minute |
| `MaxStunPerMinute` | `600` | requêtes STUN depuis une IP par minute |
| `FirstMessageTimeout` | `00:00:10` | combien de temps attendre le premier message après une connexion |
| `AnswerTimeout` | `00:00:20` | combien de temps un hôte peut mettre à répondre à la connexion d'un participant |
| `IdleTimeout` | `00:01:30` | combien de temps un hôte peut rester silencieux avant que sa salle soit libérée |

| Paramètre | Par défaut | Ce qu'il définit |
|---|---|---|
| `Urls` | `http://127.0.0.1:47980` | l'adresse de la page d'état `/health` ; gardez `127.0.0.1` pour qu'elle ne soit visible que sur cette machine |
| `Logging:LogLevel:Default` | `Information` | le niveau de détail du journal ; `Debug` consigne chaque connexion |
| `Logging:Console:FormatterOptions:TimestampFormat` | `yyyy-MM-dd HH:mm:ss.fff zzz ` | l'horodatage au début de chaque ligne du journal dans la console et dans le fichier : la date, l'heure du serveur et le décalage par rapport à UTC (`2026-10-11 16:14:12.250 +07:00`). Sous systemd et dans l'Observateur d'événements Windows, l'heure d'une entrée est fixée par le système lui-même |

Tout paramètre peut aussi être défini sans modifier le fichier, par une variable d'environnement (un double soulignement
à la place des deux-points) ou un argument de ligne de commande ; ils l'emportent sur le fichier :

```bash
./golether-server --Rendezvous:Port=3478                      # argument, tout système
Rendezvous__Port=3478 ./golether-server                        # variable, Linux et macOS
```

```powershell
$env:Rendezvous__Port = '3478'; .\golether-server.exe          # variable, Windows
```

**Redémarrez le service après avoir modifié `config/appsettings.json`** ([section 8](#8-gérer-le-service)). Si un réglage
est incorrect, le serveur ne démarre pas et liste les erreurs (dans la console ou dans le journal du service).

## 6. Routeur et pare-feu

**Les deux** protocoles doivent être joignables depuis l'extérieur sur le port du serveur.

**Routeur.** Si l'IP publique appartient au routeur et que le serveur est derrière lui, redirigez vers l'adresse locale
du serveur :

| Port externe | Protocole | Vers |
|---|---|---|
| 47900 | TCP | `<IP locale du serveur>:47900` |
| 47900 | UDP | `<IP locale du serveur>:47900` |

Ne redirigez pas le port 47980 vers l'extérieur. Pour les serveurs dans le cloud (VPS), ouvrez les mêmes ports dans le
groupe de sécurité ou le pare-feu du panneau du fournisseur.

**Pare-feu du système :**

| Système | Commandes |
|---|---|
| Windows | le programme d'installation s'en charge ; à la main : `New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program '<dossier du serveur>\golether-server.exe' -Action Allow` |
| Linux, ufw | `sudo ufw allow 47900/tcp && sudo ufw allow 47900/udp` (avec ufw activé, `install-service.sh` s'en charge) |
| Linux, firewalld | `sudo firewall-cmd --permanent --add-port=47900/tcp --add-port=47900/udp && sudo firewall-cmd --reload` |
| Linux, iptables | `sudo iptables -A INPUT -p tcp --dport 47900 -j ACCEPT && sudo iptables -A INPUT -p udp --dport 47900 -j ACCEPT` |
| macOS | « Réglages Système → Réseau → Coupe-feu → Options » : autoriser les connexions entrantes pour `golether-server` (si le pare-feu est activé) |

## 7. Ce qu'il faut dire aux hôtes

Les hôtes choisissent **« Mon propre serveur »** dans le sélecteur « Mode de connexion » de la carte « Montrer un film »
de Golether et saisissent l'adresse du serveur dans le champ qui apparaît, sous la forme `adresse:port`. Il n'y a rien à
dire aux participants : l'adresse leur parvient dans l'invitation.

(La position « Automatiquement » de ce sélecteur cherche le serveur du projet Golether lui-même, pas le vôtre : un hôte
qui veut utiliser votre serveur doit choisir « Mon propre serveur ».)

Donnez l'adresse sous laquelle le serveur est vu **depuis Internet**, pas `ListenAddress` :

| Où se trouve le serveur | Ce que saisissent les hôtes |
|---|---|
| Un VPS ou une machine avec une IP publique sur l'interface | cette IP (ou un nom de domaine) : `203.0.113.7:47900` |
| À la maison derrière un routeur avec redirection | l'IP externe du routeur, **pas** `192.168.x.x` : `198.51.100.20:47900` |
| Le port a été changé | le nouveau port : `203.0.113.7:3478` |

Si l'IP externe du routeur change, obtenez pour elle un nom DNS dynamique et donnez celui-ci.

## 8. Gérer le service

| Action | Windows (PowerShell en administrateur) | Linux | macOS |
|---|---|---|---|
| État | `Get-Service GoletherServer` | `systemctl status golether-server` | `sudo launchctl print system/org.golether.server` |
| Démarrer | `Start-Service GoletherServer` | `sudo systemctl start golether-server` | `sudo launchctl kickstart system/org.golether.server` |
| Arrêter | `Stop-Service GoletherServer` | `sudo systemctl stop golether-server` | `sudo launchctl bootout system/org.golether.server` |
| Redémarrer | `Restart-Service GoletherServer` | `sudo systemctl restart golether-server` | `sudo launchctl kickstart -k system/org.golether.server` |
| Journal | « Observateur d'événements → Journaux Windows → Application », source `golether-server` | `journalctl -u golether-server -f` | `tail -f /Library/Logs/golether-server.log` |

Après un arrêt par `bootout` sous macOS, le service se relance avec
`sudo launchctl bootstrap system /Library/LaunchDaemons/org.golether.server.plist` (ou par un redémarrage).

La même chose sous Windows depuis la ligne de commande classique (`cmd` en administrateur) :

| Action | Commande |
|---|---|
| État | `sc query GoletherServer` |
| Démarrer | `sc start GoletherServer` |
| Arrêter | `sc stop GoletherServer` |

Le programme `Golether Server Monitor` fait tout cela avec des boutons et montre ce que fait le serveur ; voir la
[section 9](#9-vérifier-que-tout-fonctionne).

**Le service revient tout seul.** Il démarre avec l'ordinateur, et le programme d'installation a demandé à Windows de le
redémarrer 5 secondes après une panne. Terminer le processus dans le Gestionnaire des tâches compte comme une panne, donc
une minute plus tard le serveur tourne à nouveau. Arrêtez-le plutôt comme service (les commandes ci-dessus ou
`services.msc`).

**`golether-server.exe` lancé depuis une console indique que le port est occupé.** En général, il est occupé par le
service lui-même, qui tourne déjà. Une deuxième copie n'est pas nécessaire : le service fait le même travail. Pour faire
tourner le serveur dans une console (pour voir ses messages, par exemple), arrêtez d'abord le service et relancez-le
ensuite.

## 9. Vérifier que tout fonctionne

### Golether Server Monitor

Le plus simple est le moniteur : « Golether Server Monitor » dans le menu Démarrer sous Windows,
`golether-server-monitor` à côté de `golether-server` dans le dossier du serveur sous Linux (un bureau est nécessaire) et
macOS. Il affiche :

- **l'état** en mots : en marche comme service, en marche à la main (pas comme service), arrêté, le service n'est pas
  installé ; depuis combien de temps le serveur tourne, les salles, les connexions, les rencontres et les refus ;
- **le journal des événements** en colonnes : heure | adresse IP | événement | détails (l'hôte, une étiquette de la
  salle, pourquoi une requête a été refusée). Les événements les plus récents sont en haut ; la zone de recherche trouve
  une adresse, un hôte, une salle ou un événement ; les séparateurs de l'en-tête changent la largeur des colonnes ; un
  clic droit copie les lignes sélectionnées ;
- les boutons **Démarrer** et **Arrêter** du service et **Exporter le journal** vers un fichier ;
- l'onglet **Réglages** : les champs de `config/appsettings.json` avec des explications, vérifiés selon les mêmes règles
  qu'au démarrage du serveur. « Enregistrer et redémarrer » écrit le fichier et redémarre un service en cours.

La langue du moniteur (English, Deutsch, Español, Français, Português ou Русский, chacune avec son drapeau) se choisit
dans sa fenêtre et change aussitôt. Elle est retenue dans `config/monitor.json` à côté des réglages du serveur (un
fichier propre au moniteur : le modifier ne redémarre jamais le serveur), et chaque utilisateur peut la changer. Au
premier lancement, le moniteur parle la langue du système (l'anglais s'il ne la parle pas).

Démarrer, arrêter et enregistrer demandent des droits d'administrateur : Windows les demande via l'UAC, Linux via
`pkexec`, macOS demande le mot de passe. Consulter ne demande aucun droit particulier.

Le journal est gardé dans la mémoire du serveur (les 5000 derniers événements) et commence vide à chaque démarrage du
serveur. Rien n'en est écrit sur disque sauf si vous appuyez sur **Exporter le journal** : alors exactement ce que montre
le moniteur va dans le fichier que vous choisissez. Le moniteur lit le journal par l'adresse d'état de cet ordinateur ;
les autres ordinateurs ne peuvent pas le lire.

### À la main

**Sur la machine elle-même**, la page d'état :

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

| Champ | Signification |
|---|---|
| `rooms` | séances en cours avec ce serveur |
| `connections` | connexions ouvertes |
| `meetingsRequested` / `meetingsIntroduced` | combien de connexions les participants ont demandé / à combien de côtés le serveur a transmis l'adresse de l'autre |
| `refused` | refus dus aux limites |
| `stunAnswered` | réponses STUN (augmente tant que des séances avec caméras tournent) |

**De l'extérieur**, depuis une autre machine sur un autre réseau (par exemple un portable en partage de connexion
mobile) :

```bash
nc -vz 203.0.113.7 47900                                         # Linux, macOS : le port TCP est-il ouvert ?
```

```powershell
Test-NetConnection 203.0.113.7 -Port 47900                        # Windows : TcpTestSucceeded : True
```

L'UDP se vérifie avec un client STUN, par exemple `stunclient 203.0.113.7 47900` (le paquet `stuntman-client` sous
Linux). La vérification finale : un hôte indique le serveur et crée une séance, et le fil des événements de Golether
affiche « Le serveur de rendez-vous est prêt ».

## 10. Mise à jour

| Système | Comment mettre à jour |
|---|---|
| Windows | lancez le programme d'installation de la nouvelle version : il arrête le service, remplace les fichiers et le redémarre ; `config\appsettings.json` est conservé |
| Windows, zip | arrêtez le service, remplacez tout sauf le dossier `config`, démarrez le service |
| Linux, macOS | décompressez la nouvelle version et relancez `sudo ./service/install-service.sh` ; `config/appsettings.json` est conservé |

Des versions différentes du serveur et de l'application sont compatibles tant que la version du protocole est la même
(le champ `protocol` de `/health`).

## 11. Désinstallation

| Système | Comment désinstaller |
|---|---|
| Windows | « Paramètres → Applications → Golether Server → Désinstaller » : le service et la règle du pare-feu sont retirés |
| Windows, zip | `Stop-Service GoletherServer; sc.exe delete GoletherServer; Remove-NetFirewallRule -DisplayName 'Golether rendezvous server'`, puis supprimez le dossier |
| Linux, macOS | `sudo ./service/install-service.sh uninstall` depuis le dossier du paquet ou depuis le dossier installé |

N'oubliez pas de fermer la redirection de port sur le routeur.

## 12. Sécurité

- **Le serveur ne voit pas les séances.** Tout ce qui circule entre l'hôte et les participants est chiffré entre leurs
  appareils ; le serveur ne connaît que des adresses IP, les clés publiques des hôtes et l'heure des connexions. Le
  serveur ne peut pas se faire passer pour un hôte : le participant vérifie la clé de l'hôte depuis l'invitation.
- **Une salle ne peut pas être usurpée :** elle est liée à la clé de l'appareil de l'hôte, et l'inscription est signée.
- **Droits minimaux.** Le service sous Linux et macOS n'a pas besoin des droits root (un utilisateur temporaire /
  `nobody`) ; sous Windows, le service tourne sous le compte système, comme la plupart des services.
- **Les limites** sur le nombre de connexions, de salles et la fréquence des requêtes depuis une IP protègent contre la
  surcharge. En cas d'attaque, baissez `MaxConnectionsPerMinute` et `MaxStunPerMinute`.
- **La page `/health`** ne contient ni adresses ni noms ; gardez-la quand même sur `127.0.0.1`.
- **Le journal des événements** (`/journal`, lu par le moniteur) contient les adresses IP des appareils, les identifiants
  courts des hôtes et des étiquettes des salles (pas leurs noms : une étiquette ne permet à personne d'atteindre
  l'hôte). Il n'est servi qu'à cet ordinateur, quelle que soit l'adresse de la page d'état, et ne vit qu'en mémoire.
- Le serveur ne stocke rien sur disque à part le journal. Seul le dossier `config` a besoin d'une sauvegarde.
- Mettez le serveur à jour en même temps que l'application Golether.

## 13. Dépannage

| Ce que vous voyez | Ce qu'il faut vérifier |
|---|---|
| Le service ne démarre pas | le journal du service ([section 8](#8-gérer-le-service)) : un réglage incorrect, ou le serveur ne peut pas écouter parce que le port est occupé par un autre programme (`netstat -ano \| findstr 47900` sous Windows, `sudo ss -lntup \| grep 47900` sous Linux, `sudo lsof -i :47900` sous macOS) |
| Le serveur lancé depuis une console indique que le port est occupé, et il revient après avoir été terminé dans le Gestionnaire des tâches | le service tourne déjà et redémarre après chaque panne : arrêtez-le comme service ([section 8](#8-gérer-le-service)) |
| L'hôte voit que le serveur de rendez-vous est injoignable | le service tourne ; `/health` répond ; le port TCP est redirigé et ouvert ([section 9](#9-vérifier-que-tout-fonctionne), la vérification de l'extérieur) ; l'hôte a reçu l'adresse externe, pas l'adresse locale |
| Un participant voit que l'hôte n'est pas sur le serveur de rendez-vous | la séance est toujours en cours ; le fil des événements de l'hôte a indiqué que le serveur était prêt ; `rooms` dans `/health` est supérieur à zéro |
| Les routeurs n'ont pas laissé passer la connexion directe | le serveur a fait sa part (`meetingsIntroduced` augmente), mais le réseau de l'un des côtés n'autorise pas les connexions directes (un NAT symétrique, fréquent chez les opérateurs mobiles). Le serveur ne peut rien y faire : il faut un tunnel AmneziaWG ou un port redirigé du côté de l'hôte |
| Le film passe, il n'y a pas de caméras | le port UDP est redirigé et ouvert ; `EnableStun` = `true` ; `stunAnswered` augmente |
| `refused` augmente vite | quelqu'un se heurte aux limites : beaucoup de participants derrière une IP, alors augmentez `MaxConnectionsPerMinute` ; une activité suspecte, alors regardez le journal au niveau `Debug` |
| macOS : le programme ne peut pas être ouvert | à la racine du dossier du serveur : `xattr -dr com.apple.quarantine .` et `codesign --force --sign - golether-server bin/golether-server` |
| Linux : `Permission denied` au démarrage | à la racine du dossier du serveur : `chmod +x golether-server bin/golether-server service/install-service.sh` (si l'archive a été décompressée sous Windows puis copiée) |
