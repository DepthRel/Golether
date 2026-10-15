// The languages of the project pages and the language picker both pages share. English is written in the HTML
// itself, so a page reads fine without scripts; the other languages are tables in the script of each page.
// A new language is one more entry in `list` (with its flag in assets/flags/<code>.svg), one more table in
// site.js and join.js and, once its documentation exists, one more folder in `docs`.
(function () {
  'use strict';

  // Scripts run: the styles may show what needs them (the picker, the steps that appear one by one).
  document.documentElement.classList.add('js');

  var storageKey = 'golether.language';

  // The languages in the order they are offered: English first, then by their own names.
  var list = [
    { code: 'en', name: 'English' },
    { code: 'de', name: 'Deutsch' },
    { code: 'es', name: 'Español' },
    { code: 'fr', name: 'Français' },
    { code: 'pt', name: 'Português' },
    { code: 'ru', name: 'Русский' }
  ];

  // The folder of the documentation in each language (Docs/<folder> in the repository).
  var docs = { en: 'En', de: 'De', es: 'Es', fr: 'Fr', pt: 'Pt', ru: 'Ru' };
  var docsRoot = 'https://github.com/DepthRel/Golether/blob/main/Docs/';

  function find(code) {
    for (var i = 0; i < list.length; i++) {
      if (list[i].code === code) { return list[i]; }
    }
    return null;
  }

  function remembered() {
    try { return localStorage.getItem(storageKey); } catch (e) { return null; }
  }

  function remember(code) {
    try { localStorage.setItem(storageKey, code); } catch (e) { /* the choice lasts for this visit */ }
  }

  // The language a visitor chose before, otherwise the first language of the browser the pages speak, otherwise
  // English: the same rule the application follows with the language of the system.
  function detect() {
    var chosen = remembered();
    if (chosen && find(chosen)) { return chosen; }
    var wanted = (navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || 'en']);
    for (var i = 0; i < wanted.length; i++) {
      var code = String(wanted[i] || '').slice(0, 2).toLowerCase();
      if (find(code)) { return code; }
    }
    return 'en';
  }

  // The address of a document of the documentation in a language; a language without its own set gets English.
  function docUrl(code, file) {
    return docsRoot + (docs[code] || docs.en) + '/' + file;
  }

  function flag(root, code) {
    var image = document.createElement('img');
    image.className = 'flag';
    image.src = root + 'assets/flags/' + code + '.svg';
    image.alt = '';
    image.width = 21;
    image.height = 14;
    return image;
  }

  // Turns <div class="lang" data-root="…"> into a button with the flag of the current language and a list of all
  // of them. The list follows the usual keyboard rules: arrows move, Enter or Space choose, Escape closes.
  function mountPicker(container, onChange) {
    var root = container.getAttribute('data-root') || '';
    container.textContent = '';

    var button = document.createElement('button');
    button.type = 'button';
    button.className = 'lang-button';
    button.setAttribute('aria-haspopup', 'listbox');
    button.setAttribute('aria-expanded', 'false');
    var current = flag(root, 'en');
    var currentCode = document.createElement('span');
    currentCode.className = 'lang-code';
    var chevron = document.createElement('span');
    chevron.className = 'lang-chevron';
    chevron.setAttribute('aria-hidden', 'true');
    button.appendChild(current);
    button.appendChild(currentCode);
    button.appendChild(chevron);

    var menu = document.createElement('ul');
    menu.className = 'lang-menu';
    menu.setAttribute('role', 'listbox');
    menu.tabIndex = -1;
    menu.hidden = true;

    var options = list.map(function (language) {
      var item = document.createElement('li');
      item.setAttribute('role', 'option');
      item.setAttribute('data-lang', language.code);
      item.setAttribute('lang', language.code);
      item.tabIndex = -1;
      var name = document.createElement('span');
      name.textContent = language.name;
      var code = document.createElement('span');
      code.className = 'lang-code';
      code.textContent = language.code.toUpperCase();
      item.appendChild(flag(root, language.code));
      item.appendChild(name);
      item.appendChild(code);
      menu.appendChild(item);
      return item;
    });

    container.appendChild(button);
    container.appendChild(menu);

    function open() {
      menu.hidden = false;
      container.classList.add('open');
      button.setAttribute('aria-expanded', 'true');
      var selected = menu.querySelector('[aria-selected="true"]') || options[0];
      selected.focus();
    }

    function close(focusButton) {
      if (menu.hidden) { return; }
      menu.hidden = true;
      container.classList.remove('open');
      button.setAttribute('aria-expanded', 'false');
      if (focusButton) { button.focus(); }
    }

    function choose(item) {
      var code = item.getAttribute('data-lang');
      remember(code);
      close(true);
      onChange(code);
    }

    button.addEventListener('click', function () {
      if (menu.hidden) { open(); } else { close(false); }
    });
    button.addEventListener('keydown', function (event) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') { event.preventDefault(); open(); }
    });
    options.forEach(function (item, index) {
      item.addEventListener('click', function () { choose(item); });
      item.addEventListener('keydown', function (event) {
        var next = null;
        if (event.key === 'ArrowDown') { next = options[(index + 1) % options.length]; }
        else if (event.key === 'ArrowUp') { next = options[(index - 1 + options.length) % options.length]; }
        else if (event.key === 'Home') { next = options[0]; }
        else if (event.key === 'End') { next = options[options.length - 1]; }
        else if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); choose(item); return; }
        else if (event.key === 'Escape') { event.preventDefault(); close(true); return; }
        else if (event.key === 'Tab') { close(false); return; }
        if (next) { event.preventDefault(); next.focus(); }
      });
    });
    document.addEventListener('click', function (event) {
      if (!container.contains(event.target)) { close(false); }
    });

    // Shows the language in use on the button and in the list; the label is the one of the container, which the
    // page words in the language in use.
    return function show(code) {
      var language = find(code) || list[0];
      var label = container.getAttribute('aria-label') || 'Language';
      menu.setAttribute('aria-label', label);
      current.src = root + 'assets/flags/' + language.code + '.svg';
      currentCode.textContent = language.code.toUpperCase();
      button.setAttribute('aria-label', label + ': ' + language.name);
      button.title = language.name;
      options.forEach(function (item) {
        item.setAttribute('aria-selected', item.getAttribute('data-lang') === language.code ? 'true' : 'false');
      });
    };
  }

  window.GoletherLanguages = { list: list, find: find, detect: detect, docUrl: docUrl, mountPicker: mountPicker };
})();
