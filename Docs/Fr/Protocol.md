# Protocole réseau de Golether (version 1)

## Transport

TCP, le port de l'hôte est **47800** par défaut. Chaque connexion :

1. TLS 1.3 (TLS 1.2 sous Windows 10) avec des certificats des deux côtés. La validation de la chaîne et du nom est
   désactivée : le participant vérifie le `PeerId` du certificat de l'hôte avec l'invitation, l'hôte calcule le `PeerId`
   du participant.
2. Le préambule du client, 6 octets : `47 4C 54 48` (`GLTH`), version `01`, rôle (`01` Control, `02` MediaData,
   `03` Relay, `04` RelayShared).
3. La réponse de l'hôte, 1 octet : `01` signifie accepté.

`PeerId` = SHA-256 en hexadécimal minuscule du SubjectPublicKeyInfo DER de la clé de l'appareil (ECDSA P-256).

## Control

Trames : 4 octets de longueur (big-endian) et du JSON UTF-8, au plus 256 Kio. Le type est donné par le champ `$t` ; les
noms de champs sont en camelCase. H est l'hôte, P un participant.

| `$t` | Sens | Champs | Signification |
|---|---|---|---|
| `hello` | P → H | `version`, `displayName`, `inviteToken` | le premier message |
| `pending` | H → P | — | en attente de la décision de l'hôte |
| `welcome` | H → P | `sessionName`, `participants[]`, `playback`, `media`, `mediaDataStreams`, `relay`, `relayOverSession`, `relayShared`, `reconnectTicket` | le participant est admis ; `relay` sont le port et les identifiants du TURN de l'hôte, créés pour une séance ; `relayOverSession` signifie que l'hôte accepte le TURN sur des flux `Relay` et qu'aucun port séparé n'est nécessaire ; `relayShared` signifie qu'il les accepte aussi tous dans un flux `RelayShared` ; `reconnectTicket` est le ticket pour revenir dans cette séance sans nouvelle invitation |
| `rejected` | H → P | `reason`, `message` | refus, la connexion est fermée |
| `ping` | P → H | `clientSendTime` | une sonde d'horloge (t0, µs) |
| `pong` | H → P | `clientSendTime`, `hostReceiveTime`, `hostSendTime` | t0, t1, t2 |
| `request` | P → H | `request: {kind, position}` | une intention : `0` lecture, `1` pause, `2` déplacement, `3` lecture sans attendre ceux qui ne sont pas prêts |
| `state` | H → tous | `state: PlaybackState` | l'état de référence |
| `status` | P → H | `status: ParticipantStatus` | une fois par seconde ; l'hôte remplace `peerId` par celui vérifié |
| `participants` | H → tous | `participants[]`, `statuses[]` | une fois par seconde et à chaque changement |
| `media` | H → tous | `media: MediaDescriptor` | un nouveau fichier |
| `rtc` | tous | `peer`, `kind`, `payload` | signalisation WebRTC ; l'hôte la relaie en mettant l'expéditeur vérifié |
| `moderate` | H → P | `microphone`, `camera` | l'hôte coupe les appareils d'un participant (seulement couper) |
| `chat` | P → H → tous | `id`, `kind` (`0` texte, `1` réaction), `text`, `sender` | un message de discussion ou une réaction ; l'hôte remplit `sender` avec l'identifiant vérifié, la valeur envoyée est ignorée |
| `draw` | P → H → tous | `strokeId`, `phase` (`0` début, `1` suite, `2` fin), `points[]` (`x`, `y` en fractions de l'image), `sender` | un trait de stylo sur la vidéo ; l'hôte remplit `sender` avec l'identifiant vérifié |
| `avatar` | P → H → tous | `image` (PNG, base64 ; vide réinitialise), `sender` | l'image d'un participant, affichée quand la caméra est coupée ; l'hôte remplit `sender` ; un nouveau participant reçoit de l'hôte les avatars déjà choisis |
| `bye` | tous | `reason` | départ ; `reason` est toujours vide et le destinataire l'ignore |

`PlaybackState` : `state` (`0` en pause, `1` en lecture), `position` (`hh:mm:ss.fffffff`), `referenceTime` (µs selon
l'horloge de l'hôte), `rate`, `version`, `origin`, `cause` (`0` initial, `1` lecture, `2` pause, `3` déplacement,
`4` attente des participants, `5` tout le monde est prêt, `6` le film est terminé, `7` lecture sans attendre ceux qui ne
sont pas prêts).

Tout ce qui est arrivé par le réseau est vérifié avant usage :

- **Un état** n'est accepté par un participant que si le mode est connu, que la position est entre zéro et 30 jours et
  que `rate` est un nombre fini de 0,5 à 2. Sinon l'état est ignoré (dans `welcome`, l'entrée est refusée).
- **Une intention** avec un `kind` inconnu est rejetée par l'hôte. La position demandée est ramenée à la durée du fichier
  et, tant que celle-ci est inconnue, aux mêmes 30 jours.
- **Le statut d'un participant** est borné par l'hôte avant tout calcul ou diffusion : des temps dans ±30 jours, un ping
  de 0 à 600 000 ms, des compteurs d'octets non négatifs. Un participant borne de la même façon les statuts de
  `participants`.
- **Le nom du fichier** dans `media` fait au plus 255 caractères, sans caractères de contrôle ni séparateurs de chemin
  (`/`, `\`). La règle est la même sur tous les systèmes : l'hôte remplace ces caractères par `_` avant l'envoi, et un
  nom valide sur son système (avec deux-points sous Linux, par exemple) est accepté par les autres. Le nom n'est
  qu'affiché ; il ne sert jamais de chemin.

Motifs d'un refus : `0` invitation non valide, `1` refusé, `2` l'hôte n'a pas répondu, `3` la séance est complète,
`4` version incompatible, `5` retiré par l'hôte. Un message `rejected` avec le motif `5` arrive aussi au milieu d'une
séance : c'est ainsi que l'hôte retire un participant admis.

Le champ `message` de `rejected` est une courte note pour le journal (le nom du motif en anglais) ; le participant ne
l'affiche pas, mais formule le motif dans la langue de sa propre interface à partir du champ `reason`. L'hôte envoie
`bye` sans `reason` pour la même raison : les textes destinés aux personnes ne circulent pas sur le réseau. Un
participant ignore le champ même quand un hôte d'une autre version le remplit, et formule lui-même la fin de la séance.

L'ordre d'une connexion : `hello` → (`pending`) → `welcome` | `rejected`. Le jeton d'une invitation fonctionne jusqu'à la
fin de sa durée de validité et sert à tous les invités, mais apporte au plus 16 demandes d'entrée ; les contacts de
confiance peuvent se connecter sans jeton. Un appareil que l'hôte a retiré ou refusé a besoin d'un jeton émis plus
tard : pour un jeton antérieur, il reçoit aussitôt `rejected` avec le motif `5` ou `1`, sans qu'on demande à l'hôte.

### Discussion et réactions

- Texte : au plus 100 caractères après nettoyage. Les caractères de contrôle, les substituts et les caractères de mise en
  forme (sauf U+200D) sont retirés, pas plus de deux sauts de ligne d'affilée, les espaces autour sont retirés.
- Une réaction : l'une d'un ensemble fixe (`👍 😂 😮 😢 ❤️ 🔥 👏`), sinon le message est rejeté.
- `id` fait jusqu'à 32 chiffres hexadécimaux, choisis par l'expéditeur ; l'hôte n'y touche pas.
- Fréquence : au plus 5 textes et 15 réactions en 5 secondes par participant (une fenêtre glissante chez l'hôte).
- L'hôte envoie un message à tous, l'expéditeur compris : l'ordre est le même pour tous. Rien n'est stocké sur disque.

### Le stylo sur la vidéo

- Un trait est une suite de messages `draw` avec un même `strokeId` : un `begin`, un nombre quelconque de `continue` et
  un `end`. Les points sont rassemblés par l'expéditeur et partent par lots environ toutes les 60 ms, au plus 128 points
  par message.
- `strokeId` fait jusqu'à 32 chiffres hexadécimaux, choisis par l'expéditeur ; l'hôte n'y touche pas.
- Les coordonnées sont des fractions de l'image (`0`–`1`), pas des pixels de la fenêtre : le trait tombe au même endroit
  de l'image pour tout le monde. Les valeurs hors limites sont ramenées dans les bornes ; `NaN` et les infinis sont
  rejetés.
- Une liste de points vide n'est autorisée que pour la phase `end`.
- Fréquence : au plus 40 messages par seconde et par participant (une fenêtre glissante chez l'hôte) ; l'excédent est
  rejeté.
- Un trait appartient à celui qui l'a commencé : un morceau avec le même `strokeId` venant d'un autre participant n'est
  pas accepté par le destinataire.
- Le destinataire garde un trait terminé 0,3 seconde et l'estompe pendant les 1,2 suivantes ; un trait inachevé (l'auteur
  s'est déconnecté) disparaît au bout de 20 secondes. Au plus 24 traits sont dessinés à la fois. Rien n'est stocké.

### Avatars

- Un participant envoie son avatar dans un message `avatar` sans le champ `sender` ; l'hôte met l'identifiant vérifié de
  l'expéditeur et l'envoie à tous, l'expéditeur compris, pour que l'ordre et l'image soient les mêmes pour tous.
- `image` est un PNG d'au plus 128 Kio (pour tenir dans la limite de 256 Kio par message avec le base64) et d'au plus
  1024 × 1024 pixels. Les octets ne sont pas fiables : la signature PNG, la taille du fichier et la taille de l'image de
  l'en-tête (IHDR) sont vérifiées, sinon le message est rejeté ; un petit fichier peut déclarer une image qui occuperait
  des gigaoctets de mémoire. Un champ vide ramène l'avatar au cercle coloré avec une lettre.
- Fréquence : au plus 5 messages en 10 secondes par participant (une fenêtre glissante chez l'hôte) ; l'excédent est
  rejeté.
- L'hôte retient le dernier avatar de chaque participant et les envoie à un nouveau participant juste après `welcome`,
  pour que le nouveau venu voie les images déjà choisies. Un avatar est oublié quand le participant part.
- Un avatar s'affiche à la place de la caméra tant que la caméra est coupée. Le fichier de l'avatar choisi est gardé dans
  `data/avatars`.

### Revenir dans une séance

`reconnectTicket` fait 48 chiffres hexadécimaux, émis à l'admission et valable tant que la séance dure. Le participant
l'envoie dans le champ `inviteToken` de `hello` ; l'hôte compare le ticket à celui émis pour cet appareil en temps
constant et saute la vérification de l'invitation. La confirmation de l'hôte est demandée comme pour une entrée
ordinaire. Quand l'hôte retire un participant, le ticket lui est retiré.

## MediaData

Une requête, 17 octets : l'opération (`01` GetChunk pour un morceau, `02` GetHash pour le hachage seul), l'indice du
morceau (int64 BE), la clé du fichier (uint64 BE, les 8 premiers octets de l'identifiant rapide). La réponse à GetHash
arrive sans données.

Une réponse, 45 octets et les données : le statut (`0` OK, `1` hors du fichier, `2` un autre fichier, `3` erreur de
lecture), l'indice (int64 BE), la longueur (int32 BE), le SHA-256 des données (32 octets).

La taille des morceaux est de 256 Kio par défaut (64 Kio – 16 Mio sont autorisés). L'identifiant rapide =
SHA-256(longueur int64 LE, le premier Mio, le Mio du milieu, le dernier Mio).

## Relay

Le flux transporte du TURN (RFC 5766 en tramage TCP) tel quel : la même chose que ce que le client enverrait au port du
relais de l'hôte, avec les mêmes identifiants de `welcome`. L'hôte n'accepte un tel flux que d'un participant admis et le
ferme avec l'admission. L'allocation est créée sur l'adresse de l'hôte atteinte par la connexion.

Côté participant, les flux sont ouverts par `RelayTunnel` : il écoute sur un port de `127.0.0.1` vers lequel pointe le
`turn-server` des caméras et, pour chaque connexion vers lui, ouvre son propre flux `Relay` par le chemin pris par la
séance (une adresse directe ou une rencontre via le serveur de rendez-vous). Si `welcome` n'a pas `relayOverSession` (un
hôte d'une version antérieure), le participant va directement au port du relais, comme avant.

### RelayShared

Un flux `RelayShared` transporte à la fois toutes les connexions TURN d'un participant, pour que les caméras et les voix
n'aient besoin que d'une connexion à travers les routeurs (via un serveur de rendez-vous, une seule rencontre) au lieu
d'une douzaine. C'est une suite de trames : le numéro de canal (4 octets, big-endian), le type (1 octet), la longueur de
la charge (2 octets, big-endian), la charge (au plus 16384 octets).

| Type | Envoyé par | Signification |
|---|---|---|
| `1` open | participant | ouvre un canal ; pas de charge |
| `2` data | les deux | octets du canal : le même TURN en tramage TCP |
| `3` close | les deux | le canal est terminé ; pas de charge |

Le relais sert chaque canal comme un client à part. Au plus 16 canaux sont ouverts à la fois ; un `open` au-delà reçoit
`close` en réponse. Les données pour un canal inexistant sont rejetées ; une trame d'un autre type ou avec une charge
plus longue termine tout le flux. Le participant utilise ce flux quand `welcome` a `relayShared` et en ouvre un nouveau
quand il s'est rompu ; sans l'indicateur (un hôte d'une version antérieure), chaque connexion reçoit son propre flux
`Relay`. Le prix du partage est qu'un paquet perdu retient tous les canaux jusqu'à ce qu'il soit renvoyé.

## Échange de morceaux entre participants

Il passe par le canal de données WebRTC `golether` (SCTP sur DTLS ; les messages ne sont disponibles qu'après la
vérification du certificat de l'autre côté). Tous les nombres sont en big-endian.

| Type | Contenu | Signification |
|---|---|---|
| `1` have | clé du fichier (8), nombre de plages (2), plages : début (4), longueur (4) | quels morceaux sont là ; toutes les 2 s |
| `2` request | clé du fichier (8), numéro de requête (4), indice du morceau (4) | une demande d'envoi d'un morceau |
| `3` data | numéro de requête (4), décalage (4), taille totale (4), données | une partie d'un morceau (de 16 Kio chacune, dans l'ordre) |
| `4` miss | numéro de requête (4) | le morceau n'est pas là ou le donneur est occupé (au plus 2 envois par participant) |

Un morceau reçu n'est accepté que si son SHA-256 correspond au hachage de l'hôte (GetHash) ; sinon le morceau est demandé
à l'hôte. Il en va de même quand le transfert se bloque 5 secondes ou que le morceau n'est pas arrivé entier en
30 secondes : un participant qui envoie peu à la fois ne peut pas retenir le film pour les autres.

## Invitation

`golether://join/v1/` + base64url(JSON), au plus 4096 caractères :

```json
{ "token": "<22 caractères base64url, 128 bits>", "expires": 1789000000,
  "host": "<PeerId>", "name": "You", "session": "Movie night",
  "endpoints": ["192.168.1.10:47800", "[2001:db8::5]:47800"],
  "meet": "203.0.113.7:47900", "room": "<22 caractères base64url, 128 bits>" }
```

`token` et `expires` (secondes Unix) sont écrits en premier : le reste est identique dans chaque invitation d'une séance,
donc une nouvelle invitation se distingue dès ses premiers caractères. L'ordre des champs ne signifie rien pour un
lecteur. `endpoints` contient de 1 à 16 adresses.

La même invitation sous forme de lien web : `https://depthrel.github.io/Golether/join/#` + le même base64url. Le
navigateur n'envoie pas au serveur la partie après `#` ; la page la transforme en `golether://join/v1/…` et la transmet
à l'application. L'application accepte les deux formes.

Les champs `meet` et `room` sont facultatifs et vont toujours ensemble : le serveur de rendez-vous de l'hôte et sa salle
sur ce serveur (une nouvelle pour chaque séance). Avec un seul des deux, ou avec un champ mal formé, l'invitation est
considérée comme endommagée. Les anciennes versions ne lisent pas ces champs. Le protocole du serveur lui-même est
décrit dans [RendezvousServer.md](RendezvousServer.md#protocole-version-1).

## Code de vérification

Les deux côtés calculent le code chacun de leur côté ; il ne circule jamais sur le réseau.

```
hash   = SHA-256(UTF-8("golether-sas-v1|" + low + "|" + high + "|" + context))
mots   = les 24 bits de hash[0..2] (big-endian), découpés en trois nombres de 6 bits (0–63), le plus fort d'abord
nombre = uint16 little-endian de hash[3..4] modulo 100
```

`low` et `high` sont les deux `PeerId` dans l'ordre ordinal, donc le résultat ne dépend pas de qui le calcule. Le
contexte d'une entrée est le jeton que le participant présente dans `hello` : le jeton de l'invitation, ou le
`reconnectTicket` au retour. Le contexte d'un tunnel est l'`offerId` de son offre (voir plus bas).

Un numéro de mot ne devient un mot qu'à l'écran, par le paquet de langue (`Verification.Word.00` …
`Verification.Word.63`). Chaque langue nomme les mêmes 64 choses dans le même ordre, donc le code est le même dans toutes
les langues, et deux personnes avec des langues d'interface différentes voient des traductions des mots de l'autre.

## Paquets AmneziaWG

`golether-awg:<offer|answer>:v1:<base64url(corps JSON)>.<base64url(signature)>`.
La signature : ECDSA P-256/SHA-256 (IEEE P1363) sur la chaîne ASCII avant le point. Les espaces et sauts de ligne à
l'intérieur d'un paquet sont ignorés, donc un paquet peut être transféré avec des retours à la ligne.

Le corps d'une offre : `offerId`, `hostName`, `hostCertificate` (DER, base64), `hostPublicKey` (AWG), `hostEndpoints[]`,
`hostAddress`, `assignedAddress`, `s1`, `s2`, `headers[4]`, `ephemeralKey` (SPKI P-256, base64), `expiresAt`.

Le corps d'une réponse : `offerId`, `participantName`, `participantCertificate`, `participantPublicKey`,
`participantEndpoints[]`, `ephemeralKey`.

PSK = base64(SHA-256(ECDH(clés P-256 éphémères))) : le secret commun de deux clés à usage unique, passé par SHA-256. Un
paquet au corps incomplet ou avec une clé qui n'est pas sur la courbe P-256 est considéré comme endommagé.

Code de vérification = `VerificationCode(hostPeerId, participantPeerId, offerId)`.
