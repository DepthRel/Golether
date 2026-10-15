// The invitation page: reads the invitation after the "#", shows whom it is from and hands it to the application as
// golether://join/v1/<invitation>. Nothing is sent anywhere. What the invitation says (the names) is shown as plain
// text only: it was written by whoever made the link. The language picker lives in languages.js.
(function () {
  'use strict';

  var languages = window.GoletherLanguages;

  var texts = {
    de: {
      'page.title': 'Golether-Einladung',
      'nav.language': 'Sprache',
      'invited.title': 'Sie sind zum gemeinsamen Schauen eingeladen',
      'invited.text': 'Öffnen Sie die Einladung in Golether. Vor dem Beitritt fragt die App noch einmal nach.',
      'invited.session': 'Sitzung',
      'invited.host': 'Gastgeber',
      'invited.open': 'In Golether öffnen',
      'invited.download': 'Ich habe Golether noch nicht',
      'invited.fallback': 'Nichts passiert? Kopieren Sie den Schlüssel und fügen Sie ihn in das Feld „Einladung“ von Golether ein.',
      'invited.copy': 'Schlüssel kopieren',
      'invited.copied': 'Der Schlüssel wurde kopiert',
      'invited.step1': 'Stimmen Sie zu, wenn der Browser fragt, ob Golether geöffnet werden soll.',
      'invited.step2': 'Drücken Sie in der App „Beitreten“.',
      'invited.step3': 'Vergleichen Sie den Code mit dem Gastgeber per Sprache.',
      'expired.title': 'Diese Einladung ist abgelaufen',
      'expired.text': 'Einladungen gelten absichtlich nur kurz. Bitten Sie den Gastgeber um eine neue.',
      'broken.title': 'Hier ist keine Einladung',
      'broken.text': 'Der Link ist unvollständig. Kopieren Sie ihn noch einmal vollständig oder bitten Sie den Gastgeber um einen neuen.',
      'common.about': 'Was ist Golether?'
    },
    es: {
      'page.title': 'Invitación de Golether',
      'nav.language': 'Idioma',
      'invited.title': 'Te invitan a ver juntos',
      'invited.text': 'Abre la invitación en Golether. La aplicación te preguntará una vez más antes de unirte.',
      'invited.session': 'Sesión',
      'invited.host': 'Anfitrión',
      'invited.open': 'Abrir en Golether',
      'invited.download': 'Todavía no tengo Golether',
      'invited.fallback': '¿No ha pasado nada? Copia la clave y pégala en el campo «Invitación» de Golether.',
      'invited.copy': 'Copiar la clave',
      'invited.copied': 'Se copió la clave',
      'invited.step1': 'Acepta abrir Golether cuando el navegador lo pregunte.',
      'invited.step2': 'Pulsa «Unirse» en la aplicación.',
      'invited.step3': 'Compara el código con el anfitrión de viva voz.',
      'expired.title': 'Esta invitación ha caducado',
      'expired.text': 'Las invitaciones duran poco a propósito. Pide al anfitrión que te envíe una nueva.',
      'broken.title': 'Aquí no hay ninguna invitación',
      'broken.text': 'El enlace está incompleto. Cópialo de nuevo entero o pide uno nuevo al anfitrión.',
      'common.about': '¿Qué es Golether?'
    },
    fr: {
      'page.title': 'Invitation Golether',
      'nav.language': 'Langue',
      'invited.title': 'Vous êtes invité à regarder ensemble',
      'invited.text': 'Ouvrez l’invitation dans Golether. L’application vous redemandera avant de rejoindre.',
      'invited.session': 'Séance',
      'invited.host': 'Hôte',
      'invited.open': 'Ouvrir dans Golether',
      'invited.download': 'Je n’ai pas encore Golether',
      'invited.fallback': 'Rien ne s’est passé ? Copiez la clé et collez-la dans le champ « Invitation » de Golether.',
      'invited.copy': 'Copier la clé',
      'invited.copied': 'La clé a été copiée',
      'invited.step1': 'Acceptez d’ouvrir Golether quand le navigateur le demande.',
      'invited.step2': 'Appuyez sur « Rejoindre » dans l’application.',
      'invited.step3': 'Comparez le code avec l’hôte à voix haute.',
      'expired.title': 'Cette invitation a expiré',
      'expired.text': 'Les invitations durent peu de temps, exprès. Demandez-en une nouvelle à l’hôte.',
      'broken.title': 'Il n’y a pas d’invitation ici',
      'broken.text': 'Le lien est incomplet. Copiez-le de nouveau en entier ou demandez-en un nouveau à l’hôte.',
      'common.about': 'Qu’est-ce que Golether ?'
    },
    pt: {
      'page.title': 'Convite do Golether',
      'nav.language': 'Idioma',
      'invited.title': 'Foi convidado para ver em conjunto',
      'invited.text': 'Abra o convite no Golether. A aplicação volta a perguntar antes de entrar.',
      'invited.session': 'Sessão',
      'invited.host': 'Anfitrião',
      'invited.open': 'Abrir no Golether',
      'invited.download': 'Ainda não tenho o Golether',
      'invited.fallback': 'Não aconteceu nada? Copie a chave e cole-a no campo “Convite” do Golether.',
      'invited.copy': 'Copiar a chave',
      'invited.copied': 'A chave foi copiada',
      'invited.step1': 'Aceite abrir o Golether quando o navegador perguntar.',
      'invited.step2': 'Prima “Entrar” na aplicação.',
      'invited.step3': 'Compare o código com o anfitrião de viva voz.',
      'expired.title': 'Este convite expirou',
      'expired.text': 'Os convites duram pouco tempo de propósito. Peça ao anfitrião que lhe envie um novo.',
      'broken.title': 'Aqui não há nenhum convite',
      'broken.text': 'A ligação está incompleta. Copie-a de novo por inteiro ou peça uma nova ao anfitrião.',
      'common.about': 'O que é o Golether?'
    },
    ru: {
      'page.title': 'Приглашение Golether',
      'nav.language': 'Язык',
      'invited.title': 'Вас приглашают смотреть вместе',
      'invited.text': 'Откройте приглашение в Golether. Перед входом приложение спросит вас ещё раз.',
      'invited.session': 'Сеанс',
      'invited.host': 'Ведущий',
      'invited.open': 'Открыть в Golether',
      'invited.download': 'У меня ещё нет Golether',
      'invited.fallback': 'Ничего не произошло? Скопируйте ключ и вставьте его в поле «Приглашение» в Golether.',
      'invited.copy': 'Скопировать ключ',
      'invited.copied': 'Ключ скопирован',
      'invited.step1': 'Согласитесь открыть Golether, когда браузер спросит.',
      'invited.step2': 'Нажмите «Войти» в приложении.',
      'invited.step3': 'Сверьте код с ведущим голосом.',
      'expired.title': 'Срок приглашения истёк',
      'expired.text': 'Приглашения живут недолго намеренно. Попросите ведущего прислать новое.',
      'broken.title': 'Здесь нет приглашения',
      'broken.text': 'Ссылка неполная. Скопируйте её ещё раз целиком или попросите у ведущего новую.',
      'common.about': 'Что такое Golether?'
    }
  };

  var english = { 'page.title': document.title, 'nav.language': 'Language', 'invited.copied': 'The key was copied' };
  var nodes = document.querySelectorAll('[data-t]');
  nodes.forEach(function (node) { english[node.getAttribute('data-t')] = node.textContent; });
  texts.en = english;
  var language = 'en';
  var copied = false;

  function text(key) {
    return (texts[language] && texts[language][key]) || english[key] || '';
  }

  var picker = document.querySelector('.lang');
  var showPicker = picker && languages ? languages.mountPicker(picker, apply) : function () { };

  function apply(next) {
    language = texts[next] ? next : 'en';
    document.documentElement.lang = language;
    document.title = text('page.title');
    nodes.forEach(function (node) { node.textContent = text(node.getAttribute('data-t')); });
    if (copied) { document.getElementById('copy').textContent = text('invited.copied'); }
    if (picker) { picker.setAttribute('aria-label', text('nav.language')); }
    showPicker(language);
  }

  apply(languages ? languages.detect() : 'en');

  // The invitation: base64url of a small JSON document, at most 4096 characters with the prefix (Invite.MaxLinkLength).
  function read(payload) {
    if (!/^[A-Za-z0-9_-]{40,4000}$/.test(payload)) { return null; }
    try {
      var base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
      var binary = atob(base64 + '==='.slice((base64.length + 3) % 4));
      var bytes = new Uint8Array(binary.length);
      for (var i = 0; i < binary.length; i++) { bytes[i] = binary.charCodeAt(i); }
      var invite = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
      return invite && typeof invite === 'object' && typeof invite.token === 'string' && typeof invite.expires === 'number'
        ? invite
        : null;
    } catch (e) {
      return null;
    }
  }

  function name(value) {
    return typeof value === 'string' && value.trim() ? value.trim().slice(0, 64) : '—';
  }

  function show(id) {
    ['invited', 'expired', 'broken'].forEach(function (other) {
      document.getElementById(other).hidden = other !== id;
    });
  }

  var payload = (location.hash || '').slice(1);
  var invite = read(payload);

  // A second invitation opened in this very tab changes only what follows the "#", and the browser does not load
  // the page again by itself: without this the page would go on handing out the first invitation.
  window.addEventListener('hashchange', function () {
    if ((location.hash || '').length > 1) { location.reload(); }
  });

  // The invitation leaves the address bar and the history entry of this page: it is needed in memory only.
  if (payload && history.replaceState) {
    try { history.replaceState(null, '', location.pathname); } catch (e) { /* the address stays as it was */ }
  }

  if (!invite) {
    show('broken');
    return;
  }

  if (invite.expires * 1000 <= Date.now()) {
    show('expired');
    return;
  }

  var key = 'golether://join/v1/' + payload;
  document.getElementById('session').textContent = name(invite.session);
  document.getElementById('host').textContent = name(invite.name);
  document.getElementById('open').setAttribute('href', key);
  document.getElementById('copy').addEventListener('click', function () {
    var button = this;
    var done = function () { copied = true; button.textContent = text('invited.copied'); };
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(key).then(done, function () { window.prompt('', key); });
    } else {
      window.prompt('', key);
    }
  });
  show('invited');

  // One attempt right away: most browsers ask "Open Golether?" by themselves. The button stays for the rest.
  window.setTimeout(function () { window.location.href = key; }, 400);
})();
