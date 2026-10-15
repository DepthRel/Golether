# Golether : confidentialité

Golether n'a ni comptes, ni publicité, ni télémétrie : il ne collecte aucune statistique sur vous ni sur ce que vous
regardez. Le film, les voix, les caméras, le chat et les dessins passent directement entre les ordinateurs des
participants, chiffrés, et aucun serveur du projet ne les voit.

Cette page liste tout ce que Golether conserve et chaque adresse qu'il contacte.

## Ce qui reste sur votre ordinateur

Tout ce qui suit se trouve dans le dossier `data` de l'application (voir « Où sont gardées les données » dans le
[guide d'utilisation](UserGuide.md)) et ne quitte jamais l'ordinateur de lui-même :

- la clé de l'appareil : elle prouve aux autres qu'il s'agit du même appareil ;
- les contacts : le nom et l'empreinte de clé de chaque appareil dont vous avez comparé le code, si vous lui faites
  confiance, sa dernière adresse réseau, vos notes et quand il a été vu pour la première et la dernière fois ;
- les réglages, les tunnels AmneziaWG avec leurs clés, les composants téléchargés ;
- le journal de l'application dans `data/logs`, conservé 7 jours. Il contient les connexions, les erreurs et les
  adresses réseau des participants, jamais le contenu d'une séance.

Les messages du chat et les dessins ne sont conservés nulle part : une fois la séance fermée, ils disparaissent. Un
rapport de diagnostic n'est enregistré que si vous le demandez ; il contient le journal et les réglages, alors
regardez-le avant de l'envoyer à quelqu'un.

## Ce que reçoivent les autres participants

Les participants de votre séance voient votre nom, votre avatar, votre adresse réseau (une connexion directe en a
besoin) et l'empreinte de la clé de votre appareil ; avec la caméra, le micro ou le chat activés, ils les reçoivent
aussi. L'hôte sert le film aux participants, qui peuvent s'en transmettre des morceaux entre eux. Le code de
vérification se compare de vive voix et n'est envoyé nulle part.

## Où Golether se connecte

| Où | Quand | Ce que l'autre côté apprend |
|---|---|---|
| Le serveur de rendez-vous du projet (son adresse vient de `config.json` de la page du projet) | quand un hôte ouvre une séance par le serveur et quand un participant rejoint une telle séance | les adresses réseau de l'hôte et du participant, l'empreinte de la clé de l'hôte, l'étiquette aléatoire de la salle ; jamais le contenu de la séance |
| La page du projet `depthrel.github.io` (GitHub Pages) | `updates.json` au démarrage (la version la plus récente ; la vérification peut être désactivée dans les réglages) ; `config.json` quand un hôte ouvre une séance avec le serveur de rendez-vous choisi automatiquement | votre adresse IP, comme pour toute page web ; la déclaration de confidentialité de GitHub s'applique |
| GitHub et `gstreamer.freedesktop.org` | sous Windows, seulement quand vous installez un composant manquant avec le bouton **Installer** | votre adresse IP |
| Les serveurs STUN publics de Google (`stun.l.google.com`) et de Cloudflare (`stun.cloudflare.com`) | seulement lors de la mise en place d'un tunnel AmneziaWG : ils indiquent à l'ordinateur son adresse vue de l'extérieur | votre adresse IP et votre port ; leurs propres politiques de confidentialité s'appliquent |
| Votre routeur (UPnP, NAT-PMP) | quand l'hôte demande au routeur d'ouvrir un port | rien au-delà de votre réseau local |

Une séance peut aussi se passer du serveur de rendez-vous : choisissez la connexion directe ou votre propre serveur
dans la fenêtre d'invitation (« Serveur de rendez-vous » dans le [guide d'utilisation](UserGuide.md)).

## La page du projet

La page du projet `depthrel.github.io/Golether` compte ses visites avec Umami Cloud. Umami n'utilise pas de cookies
et, selon sa propre description, ne garde aucune adresse IP : il enregistre l'adresse de la page (sans la partie après
`#` et `?`), la page d'où vous venez, le navigateur, le système, le type d'appareil, la taille de l'écran, la langue
et le pays, qu'il déduit de votre adresse. Un clic sur un bouton de téléchargement est compté aussi : quel paquet
(Windows, .deb, archive, macOS) et depuis quel endroit de la page. Un navigateur qui demande à ne pas être suivi (Do
Not Track) n'est pas compté. La page d'invitation (`/join/`) n'a aucun compteur, et l'application elle-même n'envoie
rien à Umami.

## Le serveur de rendez-vous public

Le serveur de rendez-vous du projet ne fait que présenter les deux côtés l'un à l'autre ; ensuite ils parlent
directement et le serveur n'intervient plus. Il garde la liste des salles ouvertes en mémoire tant qu'elles sont
ouvertes. Son journal des événements (connexions, salles, échecs, avec les adresses réseau) ne vit lui aussi qu'en
mémoire, garde un nombre limité des derniers événements et n'est visible que de l'administrateur sur la machine du
serveur elle-même. Le journal système du serveur note quand un hôte occupe ou quitte une salle, avec l'empreinte
courte de sa clé et son adresse réseau ; ces lignes restent aussi longtemps que le système du serveur conserve son
journal. Rien n'est vendu ni transmis à qui que ce soit.

Chacun peut faire tourner son propre serveur de rendez-vous : [Serveur de rendez-vous](RendezvousServer.md).

## Questions

Les questions sur vos données, les problèmes et les idées s'adressent au responsable du projet :
project.golether@gmail.com. Le bouton **Signaler un problème** (dans la fenêtre « À propos de Golether » et dans la
fenêtre d'une erreur) ouvre dans votre messagerie une lettre où la version de Golether, le système et la langue sont
déjà remplis. Rien n'est envoyé tant que vous n'envoyez pas la lettre vous-même ; elle parvient au responsable par
Gmail.
