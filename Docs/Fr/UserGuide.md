# Golether : guide d'utilisation

## Ce qu'il vous faut

- Windows 10 ou plus récent, Linux ou macOS 14+.
- Le paquet Windows contient déjà les composants pour la vidéo et les caméras. S'il en manque un, appuyez sur
  **Installer** dans la carte « Composants » de l'écran d'accueil : l'application le télécharge depuis le site officiel,
  vérifie la somme de contrôle et l'installe. Aucun droit d'administrateur n'est nécessaire. Sous Linux et macOS, la
  carte affiche une commande prête pour le terminal : copiez-la avec le bouton **Copier**.
- Une vidéo s'ouvre tout de suite : « Ouvrir avec → Golether » crée une séance et montre ce fichier.
- Les tunnels ont besoin d'AmneziaWG. Sous Windows, il n'y a rien à installer à part : à la première ouverture de la
  fenêtre des tunnels, Golether télécharge son propre moteur (environ 4 Mo). Sous Linux et macOS, il vous faut
  `amneziawg-tools` avec le module `amneziawg` / `amneziawg-go`. Golether lui-même n'a jamais besoin de tourner en
  administrateur : le système demande l'autorisation quand un tunnel monte.

## Langue de l'interface

Golether parle anglais, allemand, français, espagnol, portugais et russe. Au premier lancement, la langue suit le
système d'exploitation : un système dans l'une de ces langues la reçoit, quelle que soit la région (allemand de Suisse,
français du Canada, portugais du Brésil, etc.), et toute autre langue reçoit l'anglais. Le choix est retenu, et les
lancements suivants l'utilisent à la place de la langue du système.

Pour changer de langue, ouvrez **Réglages** (le bouton en haut à droite), section **Réglages généraux** : la liste
montre le drapeau et le nom de chaque langue. Après **Appliquer**, tous les textes changent aussitôt, sans redémarrage,
et le choix est enregistré. Les messages d'erreur utilisent la même langue. Les motifs d'un refus envoyés par l'hôte
s'affichent dans votre langue, même si l'hôte en utilise une autre.

Les mots du code de vérification s'affichent eux aussi dans votre langue. Chaque langue nomme les mêmes 64 choses : si
l'hôte utilise une autre langue, ses mots sont des traductions des vôtres. *hibou · phare · vent · 47* sur un écran est
*owl · lighthouse · wind · 47* sur l'autre. Comparez le sens des mots et le nombre.

## Montrer un film (vous êtes l'hôte)

1. Saisissez votre nom et celui de la séance, puis appuyez sur **Créer une séance**. Le port de votre ordinateur (47800
   par défaut) et la demande au routeur se trouvent sous **Avancé** : ne les changez que si le port est occupé ou a été
   redirigé à la main.
2. Appuyez sur **Montrer un fichier** et choisissez une vidéo. Le fichier reste chez vous : les participants le reçoivent
   morceau par morceau pendant le visionnage et ne le gardent qu'en mémoire.
3. Appuyez sur **Inviter**, copiez le **lien** et envoyez-le à vos invités comme vous voulez. Un clic dessus ouvre
   Golether sur l'ordinateur de l'invité et lui demande s'il veut rejoindre. Le lien est valable 15 minutes. Si le lien
   n'ouvre pas l'application chez un invité, envoyez la **clé** depuis la même fenêtre : elle se colle à la main dans
   Golether et reste valable 15 minutes ou une heure.
   Une invitation suffit pour toute la bande : publiez le lien dans votre discussion de groupe, et chacun peut rejoindre
   avec jusqu'à son expiration. C'est quand même vous qui faites entrer chaque invité. La durée de validité ne limite
   que le moment de l'entrée : un invité qui est entré reste dans la séance après l'expiration de l'invitation.
   Avec « Ouvrir le port sur le routeur » activé, Golether demande au routeur d'ouvrir le port (UPnP ou NAT-PMP), ajoute
   l'adresse publique à l'invitation et indique le résultat dans le fil des événements. Si le routeur n'y arrive pas ou
   si l'adresse de votre fournisseur n'est pas publique, ajoutez une adresse publique avec un port redirigé à la main, ou
   l'adresse dans un tunnel AWG (fenêtre d'invitation, **Avancé**).
   Le plus simple est le serveur de rendez-vous. Le sélecteur **Mode de connexion** de l'écran d'accueil est réglé sur
   **Automatiquement** par défaut : Golether trouve tout seul le serveur du projet, il figure dans l'invitation, et les
   invités d'autres réseaux se connectent sans port ouvert d'aucun côté ; voir [plus bas](#serveur-de-rendez-vous).
4. Quand un participant se connecte, une fenêtre avec le code de vérification apparaît. Lisez le code à voix haute. Si
   le participant voit le même code, appuyez sur **Les codes correspondent, faire entrer**.
5. Lecture, pause et déplacements agissent chez tous les participants.
6. Les caméras et la voix démarrent toutes seules quand vous entrez dans une séance : la vignette de chaque participant
   à droite montre sa caméra, et les voix des autres sont mixées en un seul flux. La caméra et le micro se choisissent
   dans **Réglages**, section **Caméra et micro** (sinon, les choix par défaut du système sont utilisés). Une caméra ou
   un micro coupé est réellement libéré.

## Rejoindre (vous êtes participant)

1. Cliquez sur le lien d'invitation : le navigateur propose d'ouvrir Golether, et Golether demande si vous voulez
   rejoindre la séance (en montrant son nom et celui de l'hôte). Si vous êtes déjà dans une autre séance, il demande si
   vous voulez changer. Au lieu de cliquer, vous pouvez coller la clé ou le lien lui-même dans le champ **Invitation**
   et appuyer sur **Se connecter**.
2. Un code de vérification apparaît. Comparez-le à voix haute avec l'hôte. Si les codes diffèrent, appuyez sur
   **Terminer** : la connexion a peut-être été falsifiée.
3. Si vous avez déjà le même fichier, appuyez sur **Ma copie du fichier** et choisissez-le : la lecture se fera depuis
   votre disque.

## Commandes

| Action | Bouton | Touche |
|---|---|---|
| Lecture / pause pour tout le monde | ▶ / ❚❚ | Espace |
| Reculer / avancer de 10 s | ◀◀ / ▶▶ | ← / → |
| Se déplacer | le curseur (appliqué au relâchement) | — |
| Plein écran | ⛶ | F11, Échap |
| Lecture / pause d'un clic | un clic sur la vidéo | — |
| Plein écran d'un clic | un double-clic sur la zone vidéo (fonctionne aussi avant le choix d'un film) | — |
| Votre propre volume | l'icône du haut-parleur à droite : survolez pour une échelle 0–100 %, un clic coupe et rétablit, la molette le change | — |
| Micro et caméra | « Micro activé » / « Caméra allumée » sous les vignettes des participants | — |
| Son et sous-titres | l'icône « CC » de la barre du lecteur : chacun a ses propres pistes | — |
| Réaction pour tout le monde | l'icône de smiley de la barre du lecteur | — |
| Dessiner sur la vidéo | l'icône du stylo de la barre du lecteur | — |
| Discussion et événements | les onglets **DISCUSSION** et **ÉVÉNEMENTS** en bas à droite ; un clic sur l'onglet ouvert ou sur la flèche masque le panneau | — |
| Envoyer un message | le champ en bas du panneau de discussion | Entrée |
| Volume d'un autre participant | clic droit sur sa vignette → curseur | — |
| La caméra d'un participant dans sa propre fenêtre | clic droit sur sa vignette → **Ouvrir dans une fenêtre séparée** | — |

Dans une fenêtre étroite, les boutons « Réglages », « Inviter » et « Terminer » se replient dans le bouton **⋯** en haut
à droite.

Le volume et la coupure du son n'agissent que chez vous : les autres ne remarquent rien. Une échelle à 0 coupe le son,
et un nouveau clic sur le haut-parleur rétablit le volume précédent.

### Plein écran

Le curseur au-dessus de la vidéo disparaît après trois secondes d'immobilité et revient au premier mouvement, dans tous
les modes de fenêtre. Au-dessus des vignettes des participants et de la discussion, il reste toujours visible.

En plein écran, la barre d'état en bas est masquée et la vidéo occupe toute la hauteur. La barre du haut et la barre du
lecteur se trouvent par-dessus la vidéo et s'effacent trois secondes après que le curseur les a quittées ; l'image ne
change pas de taille. Amenez le curseur au bord supérieur ou inférieur de l'écran, et les barres reviennent et restent
tant que le curseur est dessus. Rien ne se masque tant qu'un menu d'une barre est ouvert, qu'on fait glisser le curseur
ou que le stylo est actif.

Si vous voulez les barres en permanence, activez **Toujours afficher la barre de lecture** dans **Réglages**, section
**Affichage** : en plein écran, les deux barres restent en place et la vidéo s'ajuste entre elles.

### Largeur du panneau des participants et ordre des vignettes

Dans **Réglages**, section **Affichage**, activez **Disposition réglable** : une fine limite apparaît entre la vidéo et
les participants. Faites-la glisser pour élargir le panneau des participants ; les vignettes des caméras grandissent
avec lui. Un double-clic sur la limite rétablit la largeur d'origine.

Avec trois participants ou moins, les vignettes forment une colonne. Avec plus de participants et un panneau étiré à
plus de trois fois sa largeur d'origine, les vignettes forment deux colonnes, pour faire tenir plus de monde à l'écran.

Avec le même réglage, on peut échanger les vignettes : faites-en glisser une sur une autre. Cet ordre n'est visible que
par vous et dure jusqu'à la fermeture de l'application ; les nouveaux participants vont à la fin. Désactiver le réglage
rétablit la largeur et l'ordre d'origine et retire la limite.

### La caméra d'un participant dans une fenêtre séparée

Clic droit sur la vignette d'un participant → **Ouvrir dans une fenêtre séparée**. La fenêtre montre uniquement cette
caméra ; vous pouvez l'agrandir ou la déplacer sur un autre écran. Fermer la fenêtre n'a aucun effet ; quand le
participant s'en va, elle se ferme toute seule.

### Vos propres pistes audio et sous-titres

L'icône **CC** de la barre du lecteur ouvre la liste des pistes audio et des sous-titres du film. Le choix n'agit que
chez vous : l'un regarde la version originale, un autre le doublage, un troisième lit des sous-titres. La langue
choisie est retenue et appliquée au film suivant qui a une telle piste. **Sous-titres depuis un fichier…** ajoute un
fichier `.srt` ou `.ass` de votre ordinateur, **Son depuis un fichier…** une piste audio séparée (votre propre doublage,
par exemple). Vous seul voyez et entendez l'un ou l'autre.

### Discussion et réactions

Il y a deux onglets en bas à droite : **DISCUSSION** et **ÉVÉNEMENTS**. Un clic sur l'onglet ouvert (ou sur la flèche à
droite) masque complètement le panneau et laisse la place aux vignettes des participants. Tant que la discussion est
masquée, son onglet affiche un compteur de nouveaux messages. Les messages passent par le même canal protégé que le
reste de la séance et ne sont enregistrés nulle part : à la fermeture de la séance, ils disparaissent.

L'icône de smiley de la barre du lecteur envoie une réaction : pendant quelques secondes, elle flotte au-dessus de la
vidéo chez tous les participants, avec le nom de l'expéditeur. Les réactions sont disponibles dès que vous entrez dans
une séance, avant même le choix d'un film. La couche des réactions laisse passer les clics : la pause d'un clic sur la
vidéo continue de fonctionner.

### Le stylo : entourer quelque chose directement sur la vidéo

L'icône du **stylo** de la barre du lecteur active le dessin. Le bouton reste enfoncé et le curseur au-dessus de la
vidéo devient un stylo. Maintenez le bouton gauche de la souris et déplacez-la : le trait suit exactement le curseur,
et chaque participant le voit au même moment. Quand vous relâchez le bouton, le trait reste deux secondes puis
s'estompe. Chaque participant a sa propre couleur, la même que sa vignette, pour qu'on sache qui dessine. Un nouveau
clic sur l'icône relâche le bouton et le curseur redevient normal.

Les traits ne sont enregistrés nulle part et sont attachés à l'image, pas à la fenêtre : si votre fenêtre est plus large
et celle de l'autre personne plus étroite, l'endroit entouré reste la même partie de l'image. Tant que le stylo est
désactivé, les clics sur la vidéo fonctionnent comme d'habitude : pause et plein écran.

### Attendre que tout le monde soit prêt

Quand vous appuyez sur lecture, Golether vérifie d'abord que chacun a ouvert le fichier et chargé son début. Tant que
quelqu'un n'est pas prêt, la barre du lecteur indique qui est attendu (« En attente de : Marina… ») et le bouton devient
« Annuler le départ ». L'hôte n'est pas obligé d'attendre : **Démarrer sans eux** lance le film tout de suite, et ceux
qui sont en retard rattrapent d'eux-mêmes. Si un participant n'est toujours pas prêt au bout de 45 secondes, la séance
commence quand même.

### Reprendre là où vous vous êtes arrêté

Golether retient où vous vous êtes arrêté dans chaque film (sur cet ordinateur, dans le dossier `data`). Quand l'hôte
rouvre le même fichier, une barre apparaît au-dessus de la barre de lecture avec la position où vous vous êtes arrêté et
le bouton **Reprendre pour tout le monde**. Un film vu jusqu'au bout est oublié.

### Ce que montre la barre de lecture

Le curseur ne bouge que lorsque l'image bouge vraiment : pendant le compte à rebours avant le départ et pendant que le
lecteur se déplace ou attend des données, il reste immobile.

Le remplissage clair de la barre de lecture correspond à ce qui est déjà chargé et accessible sans attendre. Pour l'hôte
et pour quiconque regarde sa propre copie du fichier, toute la barre est éclairée. Les cercles avec des lettres sont les
participants : vous voyez où en est chacun.

### La connexion a été coupée

Si la connexion avec l'hôte est perdue, Golether se reconnecte tout seul : jusqu'à huit tentatives avec des pauses de
plus en plus longues. Aucune nouvelle invitation n'est nécessaire : à l'entrée, vous recevez un ticket valable pour
cette séance. L'hôte confirme quand même le retour, comme pour une entrée ordinaire. Pendant les tentatives, une barre
en haut affiche le numéro de la tentative et le bouton **Quitter**.

### Hôte : couper le micro ou la caméra d'un participant

Clic droit sur la vignette d'un participant, puis **Couper le micro** ou **Couper la caméra**. L'hôte ne peut que
couper : seul le participant peut rallumer l'appareil, et Golether le lui dit. Les vignettes montrent qui a le micro ou
la caméra coupé. Votre propre vignette n'a pas de menu : pour vous-même, utilisez les boutons sous les vignettes.

### Hôte : retirer un participant

Le même menu (clic droit sur la vignette d'un participant) propose **Retirer de la séance**. Golether demande
confirmation, puis le participant quitte aussitôt la séance et voit un message indiquant que l'hôte l'a retiré. Ce
n'est pas un bannissement : il peut revenir, mais seulement avec une nouvelle invitation de votre part ; ni les
invitations précédentes ni la reconnexion automatique ne le laissent entrer. Les invitations précédentes restent
valables pour vos autres invités : c'est la personne qui est retirée, pas le lien. Ouvrez **Inviter**, appuyez sur
**Nouvelle invitation** et envoyez le nouveau lien à cette personne en privé. La même règle s'applique à quelqu'un que
vous avez refusé dans la fenêtre du code de vérification.

### Départ, fin du film et barre d'espace

Quand le film se termine, il s'arrête sur la dernière image chez tout le monde et le bouton de lecture devient **Revoir**
(une flèche circulaire) : un appui ou la barre d'espace relance le film depuis le début pour tout le monde.

Les boutons du lecteur ne prennent pas le focus du clavier, donc la barre d'espace signifie toujours lecture/pause, même
après un clic sur d'autres boutons.

Après l'appui sur lecture, il y a un court compte à rebours : ainsi tous les participants démarrent ensemble, même avec
un ping élevé.

## Ce que montrent les vignettes

- **Cadre vert** : le participant parle en ce moment (vous aussi quand vous parlez). Avec le micro coupé, il n'y a pas
  de cadre.
- **Ping** : le temps de réponse avec l'hôte. Vert sous 400 ms, jaune jusqu'à 1,5 s, rouge au-delà.
- **Écart** : de combien le participant est en avance (+) ou en retard (−) sur la position commune. Un petit écart est
  compensé par un changement de vitesse imperceptible, un grand par un déplacement.
- **Tampon** : combien de secondes de vidéo sont déjà chargées d'avance. Si quelqu'un prend beaucoup de retard et attend
  des données, l'hôte met tout le monde en pause automatiquement et reprend quand tous sont prêts.

Quand plusieurs personnes regardent, les participants se passent directement les parties déjà chargées du film, ainsi
l'hôte n'a pas à les donner à chacun. Chaque partie est vérifiée avec la somme de contrôle de l'hôte. Combien a été reçu
et donné s'affiche dans la barre d'état en bas de la fenêtre.

## Si quelque chose ne va pas

**⋯ → Rapport de diagnostic…** enregistre un fichier texte dans `data/logs` (le bouton **⋯** apparaît en haut à droite
quand la fenêtre est trop étroite pour les boutons de la barre du haut) : la version, l'état des composants, des
informations sur la séance (sans le nom du film) et les dernières lignes du journal. Les clés, jetons et mots de passe
sont retirés du rapport, donc le fichier peut être envoyé à la personne qui vous aide.

Le rapport contient une ligne pour chaque connexion avec un participant : si elle est vérifiée, si le canal de données
est ouvert et combien d'images sont sorties et entrées (`vidéo 120↑/0↓`). Elles montrent tout de suite où le lien
casse : la connexion ne s'est pas établie, elle s'est établie à vide, ou la caméra ne donne rien.

### Personne ne peut se connecter : le pare-feu

Windows considère une nouvelle connexion réseau, un adaptateur de tunnel surtout, comme un réseau public et rejette les
connexions entrantes. Les participants ne peuvent alors pas entrer, même si les ordinateurs se voient parfaitement.
Golether le vérifie au début d'une séance et, s'il n'y a pas de règle, affiche en haut la barre **« Le pare-feu ne
laisse pas entrer les participants »** avec le bouton **Autoriser**. Le bouton demande une seule fois la confirmation
d'un administrateur et ajoute une règle pour le programme ; aucun service permanent n'apparaît, et il n'est jamais
nécessaire de recommencer.

La même chose à la main : « Pare-feu Windows Defender » → « Autoriser une application via le pare-feu » → trouvez
Golether et cochez à la fois le réseau privé et le réseau public.

## Mises à jour

Au lancement, Golether demande à la [page du projet](https://depthrel.github.io/Golether/) si une nouvelle version est
sortie. Si oui, la barre « Une nouvelle version est disponible » apparaît en haut avec le bouton **Ouvrir la page de
téléchargement** : votre navigateur ouvre la page avec les liens pour votre système. Rien n'est téléchargé ni installé
tout seul : c'est vous qui décidez. La case **Ignorer cette version**, cochée avant de fermer la barre, désactive le
rappel pour cette version ; Golether vous signalera la suivante. Une mise à jour ne touche pas aux données personnelles.

Dans **Réglages**, section **Mises à jour**, vous pouvez désactiver la vérification ou indiquer votre propre source
(l'adresse d'un fichier avec la liste des versions, `https://…`) ; ce n'est utile que pour vos propres versions.

## Serveur de rendez-vous

Si vous et vos participants avez des adresses « grises » et qu'un port ne peut pas être redirigé, le serveur de
rendez-vous vous aide : un petit programme sur n'importe quelle machine avec une IP publique (la vôtre ou celle d'un
ami). Il ne fait que présenter vos ordinateurs l'un à l'autre ; la connexion entre eux est percée en direct. Le film,
les caméras et les voix ne passent pas par le serveur, et il ne peut pas les voir.

1. Quelqu'un installe le serveur en suivant le
   [guide de l'administrateur](../../Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.fr.md) (Windows, Linux ou macOS ; le
   même fichier se trouve dans chaque paquet du serveur).
2. L'hôte choisit le **mode de connexion** sur l'écran d'accueil et crée une séance :
   - **Automatiquement** (par défaut) : Golether apprend l'adresse du serveur du projet depuis la page du projet. Si la
     page est inaccessible, l'adresse de la dernière fois est utilisée ; s'il n'y en a pas, la séance est créée sans
     serveur et le fil des événements le signale ;
   - **En direct** : sans serveur ; les invités se connectent uniquement aux adresses de votre ordinateur ;
   - **Mon propre serveur** : un champ apparaît pour l'adresse du serveur, par exemple `203.0.113.7:47900`.

   Quand le serveur a répondu, le fil des événements affiche « Le serveur de rendez-vous est prêt ».
3. Un participant n'a rien à configurer : l'adresse du serveur arrive dans l'invitation. Si les adresses directes ne
   répondent pas en 4 secondes, Golether rejoint l'hôte par le serveur tout seul.

Cela ne fonctionne pas quand au moins un côté est derrière un NAT symétrique (fréquent chez les opérateurs mobiles et
dans les réseaux d'entreprise) : Golether indique alors que les routeurs n'ont pas laissé passer la connexion directe,
et il reste un tunnel AWG ou un port redirigé.

## Tunnel AmneziaWG

Un tunnel se monte avant de se connecter : **Réglages → Tunnels AmneziaWG → Configurer les tunnels**. Pendant une séance,
ce bouton n'est pas disponible.

Un tunnel est nécessaire quand une connexion directe ne passe pas ou est bloquée. Aucun serveur à vous n'est requis, et
sous Windows **AmneziaWG n'a pas besoin d'être installé à part** : Golether télécharge son propre moteur et monte le
tunnel dans sa propre instance. Si vous utilisez déjà AmneziaWG avec vos propres adresses, il n'est pas touché : ce sont
des adaptateurs réseau différents.

À la première ouverture de la fenêtre des tunnels, le moteur se télécharge avec le bouton **Installer** directement dans
cette fenêtre (environ 4 Mo, sans droits d'administrateur). Tant qu'il n'est pas là, les boutons « Monter le tunnel » ne
sont pas disponibles.

**Hôte :**
1. Dans la fenêtre des tunnels : **Je suis l'hôte → Créer une offre**, envoyez le texte au participant.
2. Collez la réponse du participant et appuyez sur **Accepter la réponse**. Comparez le code à voix haute.
3. **Monter le tunnel** (Windows affiche une demande d'autorisation, macOS et Linux demandent le mot de passe) ou
   **Exporter le .conf** pour AmneziaVPN.
4. Ajoutez à l'invitation l'adresse de l'hôte dans le tunnel, par exemple `10.77.41.1:47800` (fenêtre d'invitation,
   **Avancé**).

**Participant :**
1. Dans la fenêtre des tunnels : **Je suis participant**, collez l'offre, **Accepter l'offre**.
2. Envoyez la réponse à l'hôte, comparez le code, **Monter le tunnel**.

**Un tunnel vit tant que Golether est ouvert.** À la fermeture, il descend automatiquement ; le système demande une fois
l'autorisation d'un administrateur. Si vous refusez, le tunnel reste, et Golether propose de le retirer au lancement
suivant. Pour le retirer plus tôt, utilisez **Descendre le tunnel** dans la même fenêtre. Les tunnels qui n'ont pas été
montés par Golether ne sont pas touchés.

Golether trouve l'adresse publique tout seul : quand une offre est créée et quand une réponse est acceptée, il demande à
des serveurs STUN publics à quoi ressemble son port de l'extérieur et met l'adresse dans le paquet. Ensuite, les deux
côtés « frappent » l'un chez l'autre pour que les routeurs laissent passer la réponse. C'est pourquoi rediriger un port
à la main est généralement inutile.

Cela ne fonctionne pas quand le routeur donne à chaque destination son propre port (NAT symétrique) ou que le
fournisseur vous garde derrière une adresse partagée (CGNAT), des deux côtés à la fois. Il faut alors à l'un des côtés
une IP publique ou un port redirigé. Les fichiers `.conf` contiennent des clés : ne les publiez pas.

## Où sont gardées les données

Golether est autonome : tout ce qu'il enregistre (la clé de l'appareil, la base des contacts et des tunnels, les
configurations `.conf`, les composants téléchargés, les réglages) se trouve dans le dossier `data` à côté du programme.
Une archive décompressée n'écrit rien dans le profil de l'utilisateur ni dans le registre ; le programme d'installation
Windows n'ajoute au registre que l'entrée de désinstallation et le gestionnaire des liens d'invitation. Par conséquent :

- décompressez Golether là où vous avez le droit d'écrire : dans « Documents », sur le bureau, sur une clé USB. Une copie
  décompressée dans `Program Files` ne démarre pas et vous demande de déplacer le dossier ;
- le programme d'installation Windows demande pour qui installer. **Seulement pour moi** (par défaut) installe dans
  `%LOCALAPPDATA%\Programs\Golether`, sans droits d'administrateur, et garde les données dans `data` à l'intérieur de
  l'installation. **Pour tout le monde** installe dans `C:\Program Files\Golether`, demande une fois des droits
  d'administrateur pendant l'installation et garde les données de chaque utilisateur dans son profil :
  `%LOCALAPPDATA%\Golether`. Le programme d'installation apprend aussi au système à ouvrir les liens d'invitation avec
  Golether. Dans une archive décompressée, les liens ne s'ouvrent pas tout seuls : utilisez la clé (sous Linux,
  `install-desktop-entry.sh` active les liens) ;
- déplacez, copiez et supprimez le dossier `Golether` en entier : toutes les données voyagent avec lui ;
- pour mettre à jour une copie décompressée, décompressez la nouvelle version et déplacez-y le dossier `data` de
  l'ancienne ;
- si les données se trouvaient auparavant dans `%LOCALAPPDATA%\Golether` (premières versions), elles sont déplacées dans
  `data` automatiquement au premier lancement ;
- sous Windows, la clé de l'appareil et les secrets des tunnels sont en plus chiffrés pour votre compte Windows. Sur un
  autre ordinateur ou sous un autre utilisateur, ils ne peuvent pas être lus : le programme le dit et, une fois le dossier
  `data\identity` supprimé, crée une nouvelle clé ;
- `data\identity\device.key` est la clé de l'appareil. Si elle est supprimée, l'empreinte de l'appareil change et vos
  contacts doivent de nouveau comparer le code.

## Questions fréquentes

- **« L'hôte est injoignable ».** Vérifiez que le port est ouvert dans le pare-feu de l'hôte, ou utilisez un tunnel.
- **« L'invitation n'est pas valide ou a expiré ».** La durée de validité de l'invitation est écoulée (un lien vit
  15 minutes, une clé jusqu'à une heure) : demandez-en une nouvelle à l'hôte.
- **« L'hôte vous a retiré » ou « L'hôte a refusé la demande ».** Les invitations précédentes ne vous laissent plus
  entrer ; vous pouvez rejoindre avec une invitation créée par l'hôte après cela.
- **macOS n'ouvre pas l'application.** La version n'est pas signée par Apple : « Réglages Système → Confidentialité et
  sécurité → Ouvrir quand même ».
- **Un autre participant a une connexion faible.** Sa vignette reçoit la mention « éco » : votre caméra lui parvient en
  plus basse résolution (320×180) jusqu'à ce que la connexion se rétablisse. Le son ne se dégrade pas.
- **Pas de caméras.** Vérifiez que le composant « Caméras et voix » est installé (écran d'accueil) et que Windows permet
  aux applications d'utiliser la caméra et le micro (« Paramètres → Confidentialité »). Une vignette noire signifie
  généralement un cache d'objectif fermé.
- **L'autre personne s'entend elle-même.** L'annulation d'écho agit sur le son que joue Golether ; un casque supprime
  complètement le problème.
