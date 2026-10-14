// The invitation page: reads the invitation after the "#", shows whom it is from and hands it to the application as
// golether://join/v1/<invitation>. Nothing is sent anywhere. What the invitation says (the names) is shown as plain
// text only: it was written by whoever made the link.
(function () {
  'use strict';

  var texts = {
    ru: {
      'page.title': 'Приглашение Golether',
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

  var english = { 'page.title': document.title, 'invited.copied': 'The key was copied' };
  var nodes = document.querySelectorAll('[data-t]');
  nodes.forEach(function (node) { english[node.getAttribute('data-t')] = node.textContent; });
  texts.en = english;
  var language = 'en';
  var copied = false;

  function text(key) {
    return (texts[language] && texts[language][key]) || english[key] || '';
  }

  function apply(next) {
    language = texts[next] ? next : 'en';
    document.documentElement.lang = language;
    document.title = text('page.title');
    nodes.forEach(function (node) { node.textContent = text(node.getAttribute('data-t')); });
    if (copied) { document.getElementById('copy').textContent = text('invited.copied'); }
    document.querySelectorAll('.lang button').forEach(function (button) {
      button.setAttribute('aria-pressed', button.getAttribute('data-lang') === language ? 'true' : 'false');
    });
  }

  function remembered() {
    try { return localStorage.getItem('golether.language'); } catch (e) { return null; }
  }

  document.querySelectorAll('.lang button').forEach(function (button) {
    button.addEventListener('click', function () {
      var chosen = button.getAttribute('data-lang');
      try { localStorage.setItem('golether.language', chosen); } catch (e) { /* the choice lasts for this visit */ }
      apply(chosen);
    });
  });

  apply(remembered() || ((navigator.language || 'en').slice(0, 2).toLowerCase()));

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
