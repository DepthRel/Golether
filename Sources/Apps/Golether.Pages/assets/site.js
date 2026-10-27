// The project page: the texts of every language, the language picker and the download buttons. English is written
// in the HTML itself, so the page reads fine without scripts; the other languages are tables here. The list of
// languages, the picker and the links to the documentation live in languages.js, shared with the invitation page.
(function () {
  'use strict';

  var languages = window.GoletherLanguages;

  var texts = {
    de: {
      'page.title': 'Golether: Filme gemeinsam und synchron ansehen',
      'nav.language': 'Sprache',
      'nav.join': 'So treten Sie bei',
      'nav.host': 'So laden Sie ein',
      'nav.download': 'Herunterladen',
      'hero.title': 'Filme <em>gemeinsam</em> ansehen,<br>wo immer Sie sind',
      'hero.lead': 'Für alle dasselbe Bild, selbst Tausende Kilometer voneinander entfernt.<br>Das Lachen und die Stimmen Ihrer Liebsten ganz nah.',
      'hero.download': 'Golether herunterladen',
      'hero.how': 'Ich habe eine Einladung',
      'hero.free': 'Kostenlos und quelloffen · Windows, Linux, macOS',
      'hero.shot': 'Das Golether-Fenster: ein Film im Player, die Teilnehmer und der Chat rechts',
      'hero.sync': 'synchron',
      'join.eyebrow': 'Sie wurden eingeladen',
      'join.title': 'Vier Schritte bis zum Film',
      'join.sub': 'Sie brauchen nur Golether auf Ihrem Computer und den Link, den Ihnen jemand geschickt hat.',
      'join.1.title': 'Auf den Link klicken',
      'join.1.text': 'Der Browser fragt, ob Golether geöffnet werden soll. Stimmen Sie zu.',
      'join.2.title': 'Bestätigen',
      'join.2.text': 'Golether zeigt den Namen der Sitzung und wer Sie einlädt. Drücken Sie „Beitreten“.',
      'join.3.title': 'Code vergleichen',
      'join.3.text': 'Sie und der Gastgeber sehen dieselben drei Wörter und eine Zahl. Sagen Sie sie einander laut.',
      'join.4.title': 'Genießen',
      'join.4.text': 'Der Gastgeber lässt Sie herein, und der Film startet für alle im selben Moment.',
      'join.note': '<b>Der Link hat die App nicht geöffnet?</b> Bitten Sie den Gastgeber um den <b>Schlüssel</b>: Kopieren Sie ihn, öffnen Sie Golether, fügen Sie ihn in das Feld „Einladung“ ein und drücken Sie „Verbinden“. Ein Link gilt 15 Minuten, ein Schlüssel bis zu einer Stunde; mit einer Einladung kann die ganze Runde beitreten.',
      'code.1': 'Ahorn',
      'code.2': 'Fluss',
      'code.3': 'Stein',
      'host.eyebrow': 'Sie laden ein',
      'host.title': 'Zeigen Sie Ihren Freunden einen Film',
      'host.sub': 'Der Film bleibt auf Ihrem Computer und geht direkt zu Ihren Gästen.<br>Bis zu fünf Personen schauen gemeinsam.',
      'host.1.title': 'Film auswählen',
      'host.1.text': 'Drücken Sie „Sitzung erstellen“ und wählen Sie eine Videodatei. Nichts wird auf einen Server hochgeladen.',
      'host.2.title': 'Einladung schicken',
      'host.2.text': 'Drücken Sie „Einladen“, kopieren Sie den Link und posten Sie ihn in Ihren Gruppenchat. Alle treten mit demselben Link bei.',
      'host.3.title': 'Gäste einlassen',
      'host.3.text': 'Vergleichen Sie den Code mit Ihrem Gast per Sprache, lassen Sie ihn herein und drücken Sie auf Abspielen.',
      'why.eyebrow': 'Warum Golether',
      'why.title': 'Wie auf demselben Sofa',
      'why.sub': 'Alles für einen gemeinsamen Abend passt in ein Fenster.',
      'why.1.title': 'Perfekt synchron',
      'why.1.text': 'Abspielen, Pause und Spulen wirken für alle zugleich, egal wer die Taste drückt.',
      'why.2.title': 'Originalqualität',
      'why.2.text': 'Der Film wird nicht zu einem verschwommenen Stream umkodiert: Alle sehen die Datei, wie sie ist.',
      'why.3.title': 'Gesichter und Stimmen',
      'why.3.text': 'Webcams, Sprache, Chat und Reaktionen über dem Film. Sie können sogar direkt aufs Bild zeichnen, und alle sehen es.',
      'why.4.title': 'Privat',
      'why.4.text': 'Die Verbindung ist verschlüsselt und läuft direkt zwischen Ihnen. Niemand kommt ohne Zustimmung des Gastgebers herein.',
      'why.5.title': 'Eigene Untertitel und Tonspur',
      'why.5.text': 'Jeder wählt Untertitel und Tonspur selbst, unabhängig von den anderen.',
      'why.6.title': 'Für immer kostenlos',
      'why.6.text': 'Offener Quellcode. Keine Konten, keine Werbung, keine Abos.',
      'why.7.badge': 'Neu',
      'why.7.title': 'Fotos und Videos von der Reise',
      'why.7.text': 'Zeigen Sie einen ganzen Ordner samt Unterordnern: Alle sehen einen Streifen kleiner Bilder und können jedes Foto oder Video auf den Bildschirm holen oder sich ein Bild allein ansehen und heranzoomen. Handyfotos im HEIC-Format gehen auch, und Aufnahmeort, Kamera und Kommentare bleiben bei Ihnen.',
      'dl.eyebrow': 'Herunterladen',
      'dl.title': 'Golether holen',
      'dl.sub': 'Dieselbe Version für alle, die gemeinsam schauen.<br>Updates werden in der App angeboten.',
      'dl.windows': 'Ein Installationsprogramm für Windows 10 und neuer. Einladungslinks öffnen die App von selbst.',
      'dl.linux': 'Ein Paket für Ubuntu, Debian und verwandte Systeme: Das System installiert die Komponenten für Video und Kameras gleich mit.',
      'dl.windows.other': 'oder das portable .zip-Archiv',
      'dl.linux.other': 'oder das Archiv .tar.gz für andere Distributionen',
      'dl.macos.other': 'oder für Macs mit Intel-Prozessor',
      'dl.macos': 'Ein Archiv für Macs mit Apple Silicon, macOS 14 und neuer.',
      'dl.button': 'Herunterladen',
      'dl.version': 'Version ',
      'dl.for': 'Herunterladen für ',
      'foot.license': 'Golether ist freie Software unter der Lizenz GPL-3.0-or-later.',
      'foot.source': 'Quellcode',
      'foot.guide': 'Handbuch',
      'foot.privacy': 'Datenschutz',
      'foot.releases': 'Alle Versionen'
    },
    es: {
      'page.title': 'Golether: ved películas juntos y sincronizados',
      'nav.language': 'Idioma',
      'nav.join': 'Cómo unirse',
      'nav.host': 'Cómo invitar',
      'nav.download': 'Descargar',
      'hero.title': 'Ved películas <em>juntos</em><br>estéis donde estéis',
      'hero.lead': 'El mismo fotograma para todos, aunque os separen miles de kilómetros.<br>Las risas y las voces de los tuyos, justo a tu lado.',
      'hero.download': 'Descargar Golether',
      'hero.how': 'Tengo una invitación',
      'hero.free': 'Gratis y de código abierto · Windows, Linux, macOS',
      'hero.shot': 'La ventana de Golether: una película en el reproductor, los participantes y el chat a la derecha',
      'hero.sync': 'sincronizados',
      'join.eyebrow': 'Te han invitado',
      'join.title': 'Cuatro pasos hasta la película',
      'join.sub': 'Solo necesitas Golether en tu ordenador y el enlace que te ha enviado tu amigo.',
      'join.1.title': 'Haz clic en el enlace',
      'join.1.text': 'El navegador pregunta si quieres abrir Golether. Di que sí.',
      'join.2.title': 'Confirma',
      'join.2.text': 'Golether muestra el nombre de la sesión y quién te invita. Pulsa «Unirse».',
      'join.3.title': 'Compara el código',
      'join.3.text': 'Tú y el anfitrión veis las mismas tres palabras y un número. Decídoslas en voz alta.',
      'join.4.title': 'Disfruta',
      'join.4.text': 'El anfitrión te deja entrar y la película empieza para todos en el mismo momento.',
      'join.note': '<b>¿El enlace no abrió la aplicación?</b> Pide al anfitrión la <b>clave</b>: cópiala, abre Golether, pégala en el campo «Invitación» y pulsa «Conectar». Un enlace vale 15 minutos y una clave hasta una hora; todo el grupo puede unirse con una sola invitación.',
      'code.1': 'arce',
      'code.2': 'río',
      'code.3': 'piedra',
      'host.eyebrow': 'Tú invitas',
      'host.title': 'Muestra una película a tus amigos',
      'host.sub': 'La película se queda en tu ordenador y va directamente a tus invitados.<br>Hasta cinco personas ven juntas.',
      'host.1.title': 'Elige la película',
      'host.1.text': 'Pulsa «Crear una sesión» y elige un archivo de vídeo. No se sube nada a ningún servidor.',
      'host.2.title': 'Envía la invitación',
      'host.2.text': 'Pulsa «Invitar», copia el enlace y publícalo en tu chat de grupo. Todos se unen con el mismo enlace.',
      'host.3.title': 'Déjalos entrar',
      'host.3.text': 'Compara el código con tu invitado de viva voz, déjalo entrar y dale a reproducir.',
      'why.eyebrow': 'Por qué Golether',
      'why.title': 'Como en el mismo sofá',
      'why.sub': 'Todo lo necesario para una noche juntos cabe en una ventana.',
      'why.1.title': 'Perfectamente sincronizado',
      'why.1.text': 'Reproducir, pausar y rebobinar funcionan para todos a la vez, pulse quien pulse el botón.',
      'why.2.title': 'Calidad original',
      'why.2.text': 'La película no se recodifica en una emisión borrosa: todos ven el archivo tal como es.',
      'why.3.title': 'Caras y voces',
      'why.3.text': 'Cámaras, voz, chat y reacciones sobre la película. Incluso puedes dibujar sobre la imagen, y todos lo ven.',
      'why.4.title': 'Privado',
      'why.4.text': 'La conexión está cifrada y va directamente entre vosotros. Nadie entra sin la aprobación del anfitrión.',
      'why.5.title': 'Tus propios subtítulos y audio',
      'why.5.text': 'Cada uno elige los subtítulos y la pista de audio por su cuenta, sin depender de los demás.',
      'why.6.title': 'Gratis para siempre',
      'why.6.text': 'Código abierto. Sin cuentas, sin anuncios, sin suscripciones.',
      'why.7.badge': 'Nuevo',
      'why.7.title': 'Fotos y vídeos del viaje',
      'why.7.text': 'Muestra una carpeta entera, con sus subcarpetas: todos ven una tira de miniaturas y pueden poner en pantalla cualquier foto o vídeo, o mirar una por su cuenta y ampliarla. Las fotos HEIC del móvil también sirven, y el lugar, la cámara y los comentarios se quedan contigo.',
      'dl.eyebrow': 'Descargar',
      'dl.title': 'Consigue Golether',
      'dl.sub': 'La misma versión para todos los que ven juntos.<br>Las actualizaciones se ofrecen dentro de la aplicación.',
      'dl.windows': 'Un instalador para Windows 10 y posteriores. Los enlaces de invitación abren la aplicación solos.',
      'dl.linux': 'Un paquete para Ubuntu, Debian y derivados: el sistema instala con él los componentes de vídeo y cámaras.',
      'dl.windows.other': 'o el archivo portátil .zip',
      'dl.linux.other': 'o el archivo .tar.gz para otras distribuciones',
      'dl.macos.other': 'o para Mac con procesador Intel',
      'dl.macos': 'Un archivo para Mac con Apple silicon, macOS 14 y posteriores.',
      'dl.button': 'Descargar',
      'dl.version': 'versión ',
      'dl.for': 'Descargar para ',
      'foot.license': 'Golether es software libre bajo la licencia GPL-3.0-or-later.',
      'foot.source': 'Código fuente',
      'foot.guide': 'Guía de uso',
      'foot.privacy': 'Privacidad',
      'foot.releases': 'Todas las versiones'
    },
    fr: {
      'page.title': 'Golether : regarder des films ensemble, en synchro',
      'nav.language': 'Langue',
      'nav.join': 'Comment rejoindre',
      'nav.host': 'Comment inviter',
      'nav.download': 'Télécharger',
      'hero.title': 'Regardez des films <em>ensemble</em><br>où que vous soyez',
      'hero.lead': 'La même image pour tous, même à des milliers de kilomètres.<br>Les rires et les voix de vos proches, tout près de vous.',
      'hero.download': 'Télécharger Golether',
      'hero.how': 'J’ai une invitation',
      'hero.free': 'Gratuit et open source · Windows, Linux, macOS',
      'hero.shot': 'La fenêtre de Golether : un film dans le lecteur, les participants et la discussion à droite',
      'hero.sync': 'en synchro',
      'join.eyebrow': 'Vous êtes invité',
      'join.title': 'Quatre étapes jusqu’au film',
      'join.sub': 'Il vous faut seulement Golether sur votre ordinateur et le lien qu’un ami vous a envoyé.',
      'join.1.title': 'Cliquez sur le lien',
      'join.1.text': 'Le navigateur demande s’il faut ouvrir Golether. Acceptez.',
      'join.2.title': 'Confirmez',
      'join.2.text': 'Golether affiche le nom de la séance et qui vous invite. Appuyez sur « Rejoindre ».',
      'join.3.title': 'Comparez le code',
      'join.3.text': 'Vous et l’hôte voyez les trois mêmes mots et un nombre. Dites-les-vous à voix haute.',
      'join.4.title': 'Profitez',
      'join.4.text': 'L’hôte vous fait entrer, et le film démarre pour tout le monde au même moment.',
      'join.note': '<b>Le lien n’a pas ouvert l’application ?</b> Demandez plutôt la <b>clé</b> à l’hôte : copiez-la, ouvrez Golether, collez-la dans le champ « Invitation » et appuyez sur « Se connecter ». Un lien est valable 15 minutes, une clé jusqu’à une heure ; toute la bande peut rejoindre avec une seule invitation.',
      'code.1': 'érable',
      'code.2': 'rivière',
      'code.3': 'pierre',
      'host.eyebrow': 'Vous invitez',
      'host.title': 'Montrez un film à vos amis',
      'host.sub': 'Le film reste sur votre ordinateur et va directement à vos invités.<br>Jusqu’à cinq personnes regardent ensemble.',
      'host.1.title': 'Choisissez le film',
      'host.1.text': 'Appuyez sur « Créer une séance » et choisissez un fichier vidéo. Rien n’est envoyé sur un serveur.',
      'host.2.title': 'Envoyez l’invitation',
      'host.2.text': 'Appuyez sur « Inviter », copiez le lien et publiez-le dans votre discussion de groupe. Tout le monde rejoint avec le même lien.',
      'host.3.title': 'Faites-les entrer',
      'host.3.text': 'Comparez le code avec votre invité à voix haute, faites-le entrer et lancez la lecture.',
      'why.eyebrow': 'Pourquoi Golether',
      'why.title': 'Comme sur le même canapé',
      'why.sub': 'Tout ce qu’il faut pour une soirée ensemble tient dans une fenêtre.',
      'why.1.title': 'Parfaitement synchronisé',
      'why.1.text': 'Lecture, pause et retour en arrière valent pour tout le monde à la fois, quel que soit celui qui appuie.',
      'why.2.title': 'Qualité d’origine',
      'why.2.text': 'Le film n’est pas réencodé en un flux flou : chacun regarde le fichier tel qu’il est.',
      'why.3.title': 'Visages et voix',
      'why.3.text': 'Webcams, voix, discussion et réactions par-dessus le film. Vous pouvez même dessiner sur l’image, et tout le monde le voit.',
      'why.4.title': 'Privé',
      'why.4.text': 'La connexion est chiffrée et passe directement entre vous. Personne n’entre sans l’accord de l’hôte.',
      'why.5.title': 'Vos sous-titres et votre audio',
      'why.5.text': 'Chacun choisit ses sous-titres et sa piste audio, indépendamment des autres.',
      'why.6.title': 'Gratuit pour de bon',
      'why.6.text': 'Code ouvert. Pas de compte, pas de publicité, pas d’abonnement.',
      'why.7.badge': 'Nouveau',
      'why.7.title': 'Photos et vidéos du voyage',
      'why.7.text': 'Montrez un dossier entier, sous-dossiers compris : chacun voit un bandeau de miniatures et peut mettre n’importe quelle photo ou vidéo à l’écran, ou en regarder une à part et zoomer. Les photos HEIC du téléphone passent aussi, et le lieu, l’appareil et les commentaires restent chez vous.',
      'dl.eyebrow': 'Télécharger',
      'dl.title': 'Obtenir Golether',
      'dl.sub': 'La même version pour tous ceux qui regardent ensemble.<br>Les mises à jour sont proposées dans l’application.',
      'dl.windows': 'Un programme d’installation pour Windows 10 et plus récent. Les liens d’invitation ouvrent l’application tout seuls.',
      'dl.linux': 'Un paquet pour Ubuntu, Debian et leurs dérivées : le système installe avec lui les composants vidéo et caméra.',
      'dl.windows.other': 'ou l’archive portable .zip',
      'dl.linux.other': 'ou l’archive .tar.gz pour les autres distributions',
      'dl.macos.other': 'ou pour les Mac à processeur Intel',
      'dl.macos': 'Une archive pour les Mac avec puce Apple, macOS 14 et plus récent.',
      'dl.button': 'Télécharger',
      'dl.version': 'version ',
      'dl.for': 'Télécharger pour ',
      'foot.license': 'Golether est un logiciel libre sous licence GPL-3.0-or-later.',
      'foot.source': 'Code source',
      'foot.guide': 'Guide d’utilisation',
      'foot.privacy': 'Confidentialité',
      'foot.releases': 'Toutes les versions'
    },
    pt: {
      'page.title': 'Golether: vejam filmes juntos, em sincronia',
      'nav.language': 'Idioma',
      'nav.join': 'Como entrar',
      'nav.host': 'Como convidar',
      'nav.download': 'Transferir',
      'hero.title': 'Vejam filmes <em>juntos</em><br>estejam onde estiverem',
      'hero.lead': 'A mesma imagem para todos, mesmo a milhares de quilómetros de distância.<br>As gargalhadas e as vozes dos seus, mesmo ao lado.',
      'hero.download': 'Transferir o Golether',
      'hero.how': 'Tenho um convite',
      'hero.free': 'Gratuito e de código aberto · Windows, Linux, macOS',
      'hero.shot': 'A janela do Golether: um filme no leitor, os participantes e a conversa à direita',
      'hero.sync': 'em sincronia',
      'join.eyebrow': 'Foi convidado',
      'join.title': 'Quatro passos até ao filme',
      'join.sub': 'Só precisa do Golether no computador e da ligação que um amigo lhe enviou.',
      'join.1.title': 'Clique na ligação',
      'join.1.text': 'O navegador pergunta se quer abrir o Golether. Diga que sim.',
      'join.2.title': 'Confirme',
      'join.2.text': 'O Golether mostra o nome da sessão e quem o convida. Prima “Entrar”.',
      'join.3.title': 'Compare o código',
      'join.3.text': 'Você e o anfitrião veem as mesmas três palavras e um número. Digam-nos um ao outro em voz alta.',
      'join.4.title': 'Divirta-se',
      'join.4.text': 'O anfitrião deixa-o entrar e o filme começa para todos no mesmo momento.',
      'join.note': '<b>A ligação não abriu a aplicação?</b> Peça antes a <b>chave</b> ao anfitrião: copie-a, abra o Golether, cole-a no campo “Convite” e prima “Ligar”. Uma ligação é válida durante 15 minutos e uma chave até uma hora; todo o grupo pode entrar com um só convite.',
      'code.1': 'bordo',
      'code.2': 'rio',
      'code.3': 'pedra',
      'host.eyebrow': 'Você convida',
      'host.title': 'Mostre um filme aos seus amigos',
      'host.sub': 'O filme fica no seu computador e vai diretamente para os seus convidados.<br>Até cinco pessoas veem juntas.',
      'host.1.title': 'Escolha o filme',
      'host.1.text': 'Prima “Criar uma sessão” e escolha um ficheiro de vídeo. Nada é enviado para nenhum servidor.',
      'host.2.title': 'Envie o convite',
      'host.2.text': 'Prima “Convidar”, copie a ligação e publique-a na conversa do grupo. Todos entram com a mesma ligação.',
      'host.3.title': 'Deixe-os entrar',
      'host.3.text': 'Compare o código com o seu convidado de viva voz, deixe-o entrar e carregue em reproduzir.',
      'why.eyebrow': 'Porquê o Golether',
      'why.title': 'Como no mesmo sofá',
      'why.sub': 'Tudo para uma noite em conjunto cabe numa janela.',
      'why.1.title': 'Perfeitamente sincronizado',
      'why.1.text': 'Reproduzir, pausar e recuar funcionam para todos ao mesmo tempo, seja quem for que carregue no botão.',
      'why.2.title': 'Qualidade original',
      'why.2.text': 'O filme não é recodificado numa transmissão desfocada: todos veem o ficheiro tal como é.',
      'why.3.title': 'Caras e vozes',
      'why.3.text': 'Câmaras, voz, conversa e reações por cima do filme. Pode até desenhar sobre a imagem, e todos veem.',
      'why.4.title': 'Privado',
      'why.4.text': 'A ligação é cifrada e vai diretamente entre vocês. Ninguém entra sem a aprovação do anfitrião.',
      'why.5.title': 'As suas legendas e o seu áudio',
      'why.5.text': 'Cada um escolhe as legendas e a faixa de áudio por si, independentemente dos outros.',
      'why.6.title': 'Gratuito para sempre',
      'why.6.text': 'Código aberto. Sem contas, sem anúncios, sem subscrições.',
      'why.7.badge': 'Novo',
      'why.7.title': 'Fotografias e vídeos da viagem',
      'why.7.text': 'Mostre uma pasta inteira, com as subpastas: todos veem uma faixa de miniaturas e podem pôr qualquer foto ou vídeo no ecrã, ou ver uma à parte e ampliá-la. As fotos HEIC do telemóvel também servem, e o local, a câmara e os comentários ficam consigo.',
      'dl.eyebrow': 'Transferir',
      'dl.title': 'Obtenha o Golether',
      'dl.sub': 'A mesma versão para todos os que veem juntos.<br>As atualizações são propostas dentro da aplicação.',
      'dl.windows': 'Um instalador para Windows 10 e mais recente. As ligações de convite abrem a aplicação sozinhas.',
      'dl.linux': 'Um pacote para Ubuntu, Debian e derivados: o sistema instala com ele os componentes de vídeo e câmaras.',
      'dl.windows.other': 'ou o arquivo portátil .zip',
      'dl.linux.other': 'ou o arquivo .tar.gz para outras distribuições',
      'dl.macos.other': 'ou para Mac com processador Intel',
      'dl.macos': 'Um arquivo para Mac com Apple silicon, macOS 14 e mais recente.',
      'dl.button': 'Transferir',
      'dl.version': 'versão ',
      'dl.for': 'Transferir para ',
      'foot.license': 'O Golether é software livre sob a licença GPL-3.0-or-later.',
      'foot.source': 'Código-fonte',
      'foot.guide': 'Guia do utilizador',
      'foot.privacy': 'Privacidade',
      'foot.releases': 'Todas as versões'
    },
    ru: {
      'page.title': 'Golether: смотрите кино вместе, синхронно',
      'nav.language': 'Язык',
      'nav.join': 'Как войти',
      'nav.host': 'Как пригласить',
      'nav.download': 'Скачать',
      'hero.title': 'Смотрите кино <em>вместе</em><br>где бы вы ни были',
      'hero.lead': 'Один кадр на всех, даже если между вами тысячи километров.<br>Смех и голоса близких — рядом.',
      'hero.download': 'Скачать Golether',
      'hero.how': 'У меня есть приглашение',
      'hero.free': 'Бесплатно, с открытым кодом · Windows, Linux, macOS',
      'hero.shot': 'Окно Golether: фильм в плеере, участники и чат справа',
      'hero.sync': 'синхронно',
      'join.eyebrow': 'Вас пригласили',
      'join.title': 'Четыре шага до фильма',
      'join.sub': 'Нужен только Golether на компьютере и ссылка, которую прислал друг.',
      'join.1.title': 'Нажмите на ссылку',
      'join.1.text': 'Браузер спросит, открыть ли Golether. Соглашайтесь.',
      'join.2.title': 'Подтвердите вход',
      'join.2.text': 'Golether покажет название сеанса и того, кто вас зовёт. Нажмите «Войти».',
      'join.3.title': 'Сверьте код',
      'join.3.text': 'У вас и у ведущего на экране одни и те же три слова и число. Назовите их друг другу вслух.',
      'join.4.title': 'Наслаждайтесь',
      'join.4.text': 'Ведущий впускает вас, и фильм начинается у всех в один момент.',
      'join.note': '<b>Ссылка не открыла приложение?</b> Попросите у ведущего <b>ключ</b>: скопируйте его, откройте Golether, вставьте в поле «Приглашение» и нажмите «Подключиться». Ссылка действует 15 минут, ключ — до часа; по одному приглашению может войти вся компания.',
      'code.1': 'клён',
      'code.2': 'река',
      'code.3': 'камень',
      'host.eyebrow': 'Вы приглашаете',
      'host.title': 'Покажите фильм друзьям',
      'host.sub': 'Фильм остаётся на вашем компьютере и идёт к гостям напрямую.<br>Вместе смотрят до пяти человек.',
      'host.1.title': 'Выберите фильм',
      'host.1.text': 'Нажмите «Создать сеанс» и выберите видеофайл. На серверы ничего не загружается.',
      'host.2.title': 'Отправьте приглашение',
      'host.2.text': 'Нажмите «Пригласить», скопируйте ссылку и отправьте её в общий чат. По одной ссылке войдут все.',
      'host.3.title': 'Впустите гостей',
      'host.3.text': 'Сверьте код с гостем голосом, впустите его и нажмите «Пуск».',
      'why.eyebrow': 'Почему Golether',
      'why.title': 'Как на одном диване',
      'why.sub': 'Всё для вечера вместе помещается в одном окне.',
      'why.1.title': 'Кадр в кадр',
      'why.1.text': 'Пуск, пауза и перемотка работают сразу у всех, кто бы ни нажал кнопку.',
      'why.2.title': 'Исходное качество',
      'why.2.text': 'Фильм не пережимается в мутную трансляцию: все смотрят файл таким, какой он есть.',
      'why.3.title': 'Лица и голоса',
      'why.3.text': 'Камеры, голос, чат и реакции поверх фильма. А ещё можно рисовать прямо на кадре, и это видят все.',
      'why.4.title': 'Только свои',
      'why.4.text': 'Соединение зашифровано и идёт напрямую между вами. Без одобрения ведущего никто не войдёт.',
      'why.5.title': 'Свои субтитры и озвучка',
      'why.5.text': 'Субтитры и звуковую дорожку каждый выбирает сам, независимо от остальных.',
      'why.6.title': 'Бесплатно навсегда',
      'why.6.text': 'Открытый исходный код. Без регистрации, рекламы и подписок.',
      'why.7.badge': 'Новое',
      'why.7.title': 'Фотографии и видео из поездки',
      'why.7.text': 'Покажите целую папку вместе с вложенными: у всех лента миниатюр, любой может вывести на экран фото или видео, а может рассмотреть снимок отдельно и приблизить. Фото с телефона в HEIC тоже подходят, а место съёмки, камера и комментарии остаются у вас.',
      'dl.eyebrow': 'Скачать',
      'dl.title': 'Установите Golether',
      'dl.sub': 'У всех, кто смотрит вместе, должна быть одна версия.<br>Об обновлениях приложение сообщит само.',
      'dl.windows': 'Установщик для Windows 10 и новее. Ссылки-приглашения открывают приложение сами.',
      'dl.linux': 'Пакет для Ubuntu, Debian и родственных систем: вместе с ним система поставит компоненты для видео и камер.',
      'dl.windows.other': 'или переносной архив .zip',
      'dl.linux.other': 'или архив .tar.gz для других дистрибутивов',
      'dl.macos.other': 'или для Mac с процессором Intel',
      'dl.macos': 'Архив для Mac на Apple silicon, macOS 14 и новее.',
      'dl.button': 'Скачать',
      'dl.version': 'версия ',
      'dl.for': 'Скачать для ',
      'foot.license': 'Golether — свободная программа под лицензией GPL-3.0-or-later.',
      'foot.source': 'Исходный код',
      'foot.guide': 'Руководство',
      'foot.privacy': 'Конфиденциальность',
      'foot.releases': 'Все выпуски'
    }
  };

  // What the HTML says is the English table.
  var english = { 'page.title': document.title, 'nav.language': 'Language', 'dl.version': 'version ', 'dl.for': 'Download for ' };
  var nodes = document.querySelectorAll('[data-t]');
  nodes.forEach(function (node) { english[node.getAttribute('data-t')] = node.innerHTML; });
  var alts = document.querySelectorAll('[data-t-alt]');
  alts.forEach(function (node) { english[node.getAttribute('data-t-alt')] = node.getAttribute('alt'); });
  texts.en = english;

  var language = 'en';
  var release = null;
  var names = { windows: 'Windows', linux: 'Linux', macos: 'macOS' };

  function text(key) {
    return (texts[language] && texts[language][key]) || english[key] || '';
  }

  function detectSystem() {
    var agent = navigator.userAgent || '';
    if (/Android|iPhone|iPad|iPod/i.test(agent)) { return null; }
    if (/Windows/i.test(agent)) { return 'windows'; }
    if (/Macintosh|Mac OS X/i.test(agent)) { return 'macos'; }
    if (/Linux|X11/i.test(agent)) { return 'linux'; }
    return null;
  }

  // Only https addresses of a release are put on a button; anything else in the file is ignored.
  function safeUrl(value) {
    return typeof value === 'string' && /^https:\/\/[^\s"'<>]+$/.test(value) ? value : null;
  }

  function showDownloads() {
    var mine = detectSystem();
    document.querySelectorAll('.download').forEach(function (card) {
      var system = card.getAttribute('data-os');
      var entry = release && release[system];
      var version = entry && typeof entry.version === 'string' && /^[0-9][0-9A-Za-z.\-]{0,30}$/.test(entry.version) ? entry.version : null;
      card.querySelector('.meta').textContent = version ? text('dl.version') + version : '';
      // Each link names the field of updates.json it takes: url (the archive or the installer) or deb.
      card.querySelectorAll('a[data-field]').forEach(function (link) {
        var url = entry && safeUrl(entry[link.getAttribute('data-field')]);
        if (url) { link.setAttribute('href', url); }
      });
      card.classList.toggle('mine', system === mine);
    });

    var hero = document.getElementById('hero-download');
    var entry = mine && release && release[mine];
    if (mine) {
      hero.querySelector('span').textContent = text('dl.for') + names[mine];
      // On Linux the package of the system comes first, as on the card.
      var deb = entry && safeUrl(entry.deb);
      var url = deb || (entry && safeUrl(entry.url));
      hero.setAttribute('href', url || '#download');
      // A click is counted as a download (Umami) only when the button leads to a package, not to the list below.
      if (url) {
        hero.setAttribute('data-umami-event', 'download');
        hero.setAttribute('data-umami-event-package', mine === 'linux' ? (deb ? 'linux-deb' : 'linux-archive') : mine);
        hero.setAttribute('data-umami-event-place', 'hero');
      }
    }
  }

  var picker = document.querySelector('.lang');
  var showPicker = picker && languages ? languages.mountPicker(picker, switchTo) : function () { };

  function apply(next) {
    language = texts[next] ? next : 'en';
    document.documentElement.lang = language;
    document.title = text('page.title');
    nodes.forEach(function (node) { node.innerHTML = text(node.getAttribute('data-t')); });
    alts.forEach(function (node) { node.setAttribute('alt', text(node.getAttribute('data-t-alt'))); });
    if (picker) { picker.setAttribute('aria-label', text('nav.language')); }
    if (languages) {
      document.getElementById('guide-link').setAttribute('href', languages.docUrl(language, 'UserGuide.md'));
      document.getElementById('privacy-link').setAttribute('href', languages.docUrl(language, 'Privacy.md'));
    }
    showPicker(language);
    showDownloads();
  }

  // A chosen language fades the page out and back in, so the switch reads as one change rather than a flicker.
  function switchTo(next) {
    var body = document.body;
    if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      apply(next);
      return;
    }
    body.classList.add('switching');
    window.setTimeout(function () {
      apply(next);
      body.classList.remove('switching');
    }, 160);
  }

  apply(languages ? languages.detect() : 'en');

  // The same file the application reads to learn about new versions.
  fetch('updates.json', { cache: 'no-cache' })
    .then(function (response) { return response.ok ? response.json() : null; })
    .then(function (json) { if (json && typeof json === 'object') { release = json; showDownloads(); } })
    .catch(function () { /* the buttons keep leading to the releases page */ });

  // The steps appear as the reader gets to them.
  var steps = document.querySelectorAll('.steps li');
  if ('IntersectionObserver' in window) {
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) { entry.target.classList.add('seen'); observer.unobserve(entry.target); }
      });
    }, { threshold: 0.2 });
    steps.forEach(function (step, index) {
      step.style.transitionDelay = (index % 4) * 90 + 'ms';
      observer.observe(step);
    });
  } else {
    steps.forEach(function (step) { step.classList.add('seen'); });
  }
})();
