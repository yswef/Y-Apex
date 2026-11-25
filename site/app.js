// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
(function () {
  'use strict';

  var languageKey = 'y-apex-docs-language';
  var supportedLanguages = ['ar', 'en'];
  var documentRoot = document.documentElement;
  var toggle = document.getElementById('language-toggle');
  var skipLink = document.getElementById('skip-link');
  var brandLink = document.querySelector('.brand');

  function readLanguage() {
    try {
      var stored = localStorage.getItem(languageKey);
      return supportedLanguages.indexOf(stored) >= 0 ? stored : 'ar';
    } catch (error) {
      return 'ar';
    }
  }

  function applyLanguage(language, persist) {
    if (supportedLanguages.indexOf(language) < 0) return;

    document.querySelectorAll('[data-locale-content]').forEach(function (panel) {
      panel.hidden = panel.dataset.localeContent !== language;
    });

    documentRoot.lang = language;
    documentRoot.dir = language === 'ar' ? 'rtl' : 'ltr';

    var targetLanguage = language === 'ar' ? 'en' : 'ar';
    toggle.textContent = targetLanguage === 'ar' ? 'العربية' : 'English';
    toggle.setAttribute('aria-label', targetLanguage === 'ar' ? 'التبديل إلى العربية' : 'Switch language to English');
    skipLink.textContent = language === 'ar' ? 'تخطي إلى المحتوى' : 'Skip to content';
    skipLink.href = '#' + language + '-overview';
    brandLink.href = '#' + language + '-overview';
    brandLink.setAttribute('aria-label', language === 'ar' ? 'Y-Apex — الصفحة الرئيسية' : 'Y-Apex — home page');

    if (persist) {
      try { localStorage.setItem(languageKey, language); } catch (error) {}
    }
  }

  function isSafeHttps(value) {
    try {
      var url = new URL(value);
      return url.protocol === 'https:' && url.hostname.length > 0 && !url.username && !url.password;
    } catch (error) {
      return false;
    }
  }

  function applySharedLinks(config) {
    if (!config || typeof config !== 'object') return;
    document.querySelectorAll('[data-shared-link]').forEach(function (anchor) {
      var key = anchor.dataset.sharedLink;
      if (typeof config[key] === 'string' && isSafeHttps(config[key])) {
        anchor.href = config[key];
      }
    });
  }

  toggle.addEventListener('click', function () {
    applyLanguage(documentRoot.lang === 'ar' ? 'en' : 'ar', true);
  });

  applyLanguage(readLanguage(), false);
  fetch('shared-links.json', { cache: 'no-store' })
    .then(function (response) {
      if (!response.ok) throw new Error('Shared links are not available.');
      return response.json();
    })
    .then(applySharedLinks)
    .catch(function () {});
})();
