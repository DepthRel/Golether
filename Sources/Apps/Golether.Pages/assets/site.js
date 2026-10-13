// The project page: the language switch and the download buttons. English is written in the HTML itself, so the page
// reads fine without scripts; other languages are tables here. A new language is one more table and one more button.
(function () {
  'use strict';

  document.documentElement.classList.add('js');

  var texts = {
    ru: {
      'page.title': 'Golether: смотрите кино вместе, синхронно',
      'nav.join': 'Как войти',
      'nav.host': 'Как пригласить',
      'nav.download': 'Скачать',
      'hero.title': 'Смотрите кино <em>вместе</em>,<br>где бы вы ни были',
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
      'join.note': '<b>Ссылка не открыла приложение?</b> Попросите у ведущего <b>ключ</b>: скопируйте его, откройте Golether, вставьте в поле «Приглашение» и нажмите «Подключиться». Ссылка действует 15 минут, ключ — до часа, и каждое приглашение срабатывает один раз: для второго гостя ведущий делает новое.',
      'host.eyebrow': 'Вы приглашаете',
      'host.title': 'Покажите фильм друзьям',
      'host.sub': 'Фильм остаётся на вашем компьютере и идёт к гостям напрямую.<br>Вместе смотрят до пяти человек.',
      'host.1.title': 'Выберите фильм',
      'host.1.text': 'Нажмите «Создать сеанс» и выберите видеофайл. На серверы ничего не загружается.',
      'host.2.title': 'Отправьте приглашение',
      'host.2.text': 'Нажмите «Пригласить», скопируйте ссылку и отправьте её в любом мессенджере. Одна ссылка — один гость.',
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
      'why.3.text': 'Камеры, голос, чат и реакции, всплывающие прямо поверх фильма.',
      'why.4.title': 'Только свои',
      'why.4.text': 'Соединение зашифровано и идёт напрямую между вами. Без одобрения ведущего никто не войдёт.',
      'why.5.title': 'Субтитры и озвучка — у каждого свои',
      'why.5.text': 'Звуковую дорожку и субтитры каждый выбирает независимо от остальных: вы смотрите оригинал, друг — дубляж, и никто никому не мешает.',
      'why.6.title': 'Бесплатно навсегда',
      'why.6.text': 'Открытый исходный код. Без регистрации, рекламы и подписок.',
      'dl.eyebrow': 'Скачать',
      'dl.title': 'Установите Golether',
      'dl.sub': 'У всех, кто смотрит вместе, должна быть одна версия.<br>Об обновлениях приложение сообщит само.',
      'dl.windows': 'Установщик для Windows 10 и новее. Ссылки-приглашения открывают приложение сами.',
      'dl.linux': 'Архив для 64-разрядного Linux. Распакуйте, один раз запустите install-desktop-entry.sh, затем Golether.sh.',
      'dl.macos': 'Архив для Mac на Apple silicon, macOS 14 и новее.',
      'dl.button': 'Скачать',
      'dl.version': 'версия ',
      'dl.for': 'Скачать для ',
      'foot.license': 'Golether — свободная программа под лицензией GPL-3.0-or-later.',
      'foot.source': 'Исходный код',
      'foot.guide': 'Руководство',
      'foot.releases': 'Все выпуски'
    }
  };

  // What the HTML says is the English table.
  var english = { 'page.title': document.title, 'dl.version': 'version ', 'dl.for': 'Download for ' };
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
      var url = entry && safeUrl(entry.url);
      if (url) { card.querySelector('a').setAttribute('href', url); }
      card.classList.toggle('mine', system === mine);
    });

    var hero = document.getElementById('hero-download');
    var entry = mine && release && release[mine];
    if (mine) {
      hero.querySelector('span').textContent = text('dl.for') + names[mine];
      hero.setAttribute('href', (entry && safeUrl(entry.url)) || '#download');
    }
  }

  function apply(next) {
    language = texts[next] ? next : 'en';
    document.documentElement.lang = language;
    document.title = text('page.title');
    nodes.forEach(function (node) { node.innerHTML = text(node.getAttribute('data-t')); });
    alts.forEach(function (node) { node.setAttribute('alt', text(node.getAttribute('data-t-alt'))); });
    document.querySelectorAll('.lang button').forEach(function (button) {
      button.setAttribute('aria-pressed', button.getAttribute('data-lang') === language ? 'true' : 'false');
    });
    showDownloads();
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
