// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
(function () {
  var lang = 'ar';
  try { lang = localStorage.getItem('lang') || 'ar'; } catch (e) {}
  function load(path) { return fetch(path).then(function (r) { return r.json(); }); }
  function apply(t) {
    // العربية تغيّر النص واتجاهه فقط، ولا يُعكس التخطيط.
    document.documentElement.lang = lang;
    document.querySelectorAll('[data-i]').forEach(function (el) {
      el.textContent = t[el.dataset.i] || '';
      el.dir = lang === 'ar' ? 'rtl' : 'ltr';
    });
  }
  function setLinks(cfg, t) {
    var ul = document.getElementById('links');
    ul.innerHTML = '';
    ['repo', 'issues', 'changelog', 'license', 'privacy', 'buyMeACoffee'].forEach(function (k) {
      if (!cfg[k] || cfg[k].indexOf('http') !== 0) return;
      var li = document.createElement('li'), a = document.createElement('a');
      a.href = cfg[k]; a.rel = 'noopener'; a.textContent = t['l_' + k] || k;
      li.appendChild(a); ul.appendChild(li);
    });
    var w = document.getElementById('wallets');
    w.innerHTML = '';
    (cfg.wallets || []).forEach(function (x) {
      var li = document.createElement('li'), b = document.createElement('button');
      li.textContent = x.name + ': ' + x.value;
      b.textContent = t.copy || 'copy';
      b.onclick = function () { try { navigator.clipboard.writeText(String(x.value)); } catch (e) {} };
      li.appendChild(b); w.appendChild(li);
    });
  }
  function run() {
    Promise.all([load('i18n/' + lang + '.json'), load('shared-links.json').catch(function () { return {}; })])
      .then(function (r) { apply(r[0]); setLinks(r[1], r[0]); });
  }
  document.querySelectorAll('[data-l]').forEach(function (b) {
    b.onclick = function () { lang = b.dataset.l; try { localStorage.setItem('lang', lang); } catch (e) {} run(); };
  });
  run();
})();