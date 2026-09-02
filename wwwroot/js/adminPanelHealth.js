/* ══════════════════════════════════════════════════════════════════════
   adminPanelHealth.js — таб „Health Check"

   Зарежда се СЛЕД adminPanel.js. Не пипа нищо от него.
   Единственото, което очаква отвън: секцията #tab-health да е в DOM-а
   и 'tab-health' да е добавен във VALID_TABS вътре в adminPanel.js.

   Сървърен договор — виж INTEGRATION.md.
   ══════════════════════════════════════════════════════════════════════ */

(function () {
  'use strict';

  var SECTION_ID = 'tab-health';

  /* Осемте услуги — редът тук е редът на екрана.
     Съвпада един към един със списъка в брифа.                          */
  var SERVICES = [
    { key: 'database',   name: 'База данни' },
    { key: 'smtp',       name: 'SMTP (Office 365)' },
    { key: 'stripe',     name: 'Stripe' },
    { key: 'go28',       name: 'Go28 (крипто)' },
    { key: 'emailQueue', name: 'Фонова опашка за имейли' },
    { key: 'disk',       name: 'Дисково пространство' },
    { key: 'backups',    name: 'Резервни копия' },
    { key: 'templates',  name: 'Имейл темплейти' }
  ];

  var STATUS_LABEL = {
    ok:           'Работи',
    warn:         'Внимание',
    fail:         'Проблем',
    unconfigured: 'Не е настроена',
    checking:     'Проверява се',
    unknown:      'Неизвестно'
  };

  var ICONS = {
    ok:           '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="4 12.5 9.5 18 20 6"></polyline></svg>',
    warn:         '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3 22 20H2Z"></path><line x1="12" y1="9.5" x2="12" y2="14"></line><line x1="12" y1="16.8" x2="12" y2="16.9"></line></svg>',
    fail:         '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="5" x2="19" y2="19"></line><line x1="19" y1="5" x2="5" y2="19"></line></svg>',
    unconfigured: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"></circle><line x1="12" y1="7.5" x2="12" y2="13"></line><line x1="12" y1="16.4" x2="12" y2="16.5"></line></svg>',
    checking:     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M21 12a9 9 0 1 1-3.2-6.9"></path></svg>',
    unknown:      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><line x1="5" y1="12" x2="19" y2="12"></line></svg>',
    refresh:      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20.5 12a8.5 8.5 0 1 1-2.6-6.1"></path><polyline points="20.5 4 20.5 9.2 15.3 9.2"></polyline></svg>'
  };

  /* Праг, над който отговорът се смята за бавен и се оцветява. Сървърът
     може да върне собствено предупреждение — това е само за числото.    */
  var SLOW_MS = 1500;

  var state = {
    loaded: false,
    inFlight: 0,
    results: {},                 // key → нормализиран резултат
    lastRunAt: null,
    mode: 'parallel'             // 'parallel' | 'bulk'
  };

  var el = {};                   // кеширани възли

  /* ── помощни ──────────────────────────────────────────────────────── */

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function timeText(iso) {
    if (!iso) return '—';
    var d = new Date(iso);
    if (isNaN(d)) return esc(iso);
    return d.toLocaleTimeString('bg-BG', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  function msText(ms) {
    if (ms == null || isNaN(ms)) return null;
    return ms < 1000 ? Math.round(ms) + ' ms' : (ms / 1000).toFixed(1) + ' s';
  }

  function normalize(key, raw) {
    var s = (raw && raw.status ? String(raw.status) : 'unknown').toLowerCase();
    if (!STATUS_LABEL[s]) s = 'unknown';
    return {
      key: key,
      name: (raw && raw.name) || labelFor(key),
      status: s,
      message: (raw && raw.message) || '',
      hint: (raw && raw.hint) || '',
      responseMs: raw && raw.responseMs != null ? Number(raw.responseMs) : null,
      checkedAt: (raw && raw.checkedAt) || new Date().toISOString(),
      details: (raw && Array.isArray(raw.details)) ? raw.details : []
    };
  }

  function labelFor(key) {
    for (var i = 0; i < SERVICES.length; i++) if (SERVICES[i].key === key) return SERVICES[i].name;
    return key;
  }

  /* ── скеле ────────────────────────────────────────────────────────── */

  function buildCard(svc) {
    var card = document.createElement('article');
    card.className = 'hc-card';
    card.dataset.service = svc.key;
    card.dataset.status = 'checking';
    card.setAttribute('aria-busy', 'true');
    card.innerHTML =
      '<div class="hc-card-head">' +
        '<div>' +
          '<span class="hc-card-name">' + esc(svc.name) + '</span>' +
          '<span class="hc-card-key">' + esc(svc.key) + '</span>' +
        '</div>' +
        '<span class="hc-status" data-status="checking">' + ICONS.checking + '<span>' + STATUS_LABEL.checking + '</span></span>' +
      '</div>' +
      '<p class="hc-card-message">Проверката тече…</p>' +
      '<div class="hc-card-facts" hidden></div>' +
      '<div class="hc-card-foot">' +
        '<span class="hc-checked-at">—</span>' +
        '<button type="button" class="hc-refresh-btn" data-service="' + esc(svc.key) + '" ' +
                'aria-label="Провери отново: ' + esc(svc.name) + '">' + ICONS.refresh + '<span>Провери</span></button>' +
      '</div>';
    return card;
  }

  function renderCard(res) {
    var card = el.grid.querySelector('.hc-card[data-service="' + res.key + '"]');
    if (!card) return;

    card.dataset.status = res.status;
    card.setAttribute('aria-busy', res.status === 'checking' ? 'true' : 'false');

    var badge = card.querySelector('.hc-status');
    badge.dataset.status = res.status;
    badge.innerHTML = (ICONS[res.status] || ICONS.unknown) + '<span>' + STATUS_LABEL[res.status] + '</span>';

    card.querySelector('.hc-card-message').textContent =
      res.message || (res.status === 'ok' ? 'Услугата отговаря нормално.' : 'Няма съобщение от сървъра.');

    /* Подсказка — само когато сървърът е дал такава. */
    var hint = card.querySelector('.hc-card-hint');
    if (res.hint) {
      if (!hint) {
        hint = document.createElement('p');
        hint.className = 'hc-card-hint';
        card.insertBefore(hint, card.querySelector('.hc-card-facts'));
      }
      hint.textContent = res.hint;
    } else if (hint) {
      hint.remove();
    }

    /* Числа: време за отговор + каквото сървърът е добавил в details. */
    var facts = card.querySelector('.hc-card-facts');
    var parts = [];
    var ms = msText(res.responseMs);
    if (ms) {
      parts.push('<span class="hc-fact"><span class="hc-fact-label">Отговор</span>' +
                 '<span class="hc-fact-value' + (res.responseMs > SLOW_MS ? ' is-slow' : '') + '">' + esc(ms) + '</span></span>');
    }
    (res.details || []).forEach(function (d) {
      if (!d || d.value == null) return;
      parts.push('<span class="hc-fact"><span class="hc-fact-label">' + esc(d.label || '') + '</span>' +
                 '<span class="hc-fact-value">' + esc(d.value) + '</span></span>');
    });
    facts.innerHTML = parts.join('');
    facts.hidden = parts.length === 0;

    card.querySelector('.hc-checked-at').textContent = timeText(res.checkedAt);
    var btn = card.querySelector('.hc-refresh-btn');
    btn.disabled = res.status === 'checking';
  }

  function setCardChecking(key) {
    var card = el.grid.querySelector('.hc-card[data-service="' + key + '"]');
    if (!card) return;
    card.dataset.status = 'checking';
    card.setAttribute('aria-busy', 'true');
    var badge = card.querySelector('.hc-status');
    badge.dataset.status = 'checking';
    badge.innerHTML = ICONS.checking + '<span>' + STATUS_LABEL.checking + '</span>';
    card.querySelector('.hc-card-message').textContent = 'Проверката тече…';
    card.querySelector('.hc-refresh-btn').disabled = true;
  }

  /* ── обобщение ────────────────────────────────────────────────────── */

  function renderSummary() {
    var counts = { ok: 0, warn: 0, fail: 0 };
    var total = SERVICES.length;
    var done = 0;

    SERVICES.forEach(function (s) {
      var r = state.results[s.key];
      if (!r || r.status === 'checking') return;
      done++;
      if (r.status === 'ok') counts.ok++;
      else if (r.status === 'warn') counts.warn++;
      else counts.fail++;                       // fail + unconfigured + unknown
    });

    var cls, title, sub, mark;
    if (state.inFlight > 0) {
      cls = 'is-loading'; mark = 'checking';
      title = 'Проверката тече';
      sub = done + ' от ' + total + ' услуги са проверени.';
    } else if (counts.fail > 0) {
      cls = 'is-fail'; mark = 'fail';
      title = counts.fail === 1 ? 'Една услуга има проблем' : counts.fail + ' услуги имат проблем';
      sub = 'Отвори картите с червен ръб — там е причината.';
    } else if (counts.warn > 0) {
      cls = 'is-warn'; mark = 'warn';
      title = counts.warn === 1 ? 'Една услуга иска внимание' : counts.warn + ' услуги искат внимание';
      sub = 'Всичко работи, но нещо не е както обикновено.';
    } else {
      cls = 'is-ok'; mark = 'ok';
      title = 'Всичко работи';
      sub = 'И осемте услуги отговарят нормално.';
    }

    el.summary.className = 'hc-summary ' + cls;
    el.summary.querySelector('.hc-summary-mark').innerHTML = ICONS[mark];
    el.summary.querySelector('.hc-summary-title').textContent = title;
    el.summary.querySelector('.hc-summary-sub').textContent = sub;

    el.summary.querySelector('.hc-tally-item.is-ok .hc-tally-num').textContent = counts.ok;
    el.summary.querySelector('.hc-tally-item.is-warn .hc-tally-num').textContent = counts.warn;
    el.summary.querySelector('.hc-tally-item.is-fail .hc-tally-num').textContent = counts.fail;

    el.timestamp.textContent = state.lastRunAt ? timeText(state.lastRunAt) : '—';
    el.refreshAll.disabled = state.inFlight > 0;
  }

  /* ── мрежа ────────────────────────────────────────────────────────── */

  function endpoint(serviceKey) {
    var base = el.section.dataset.hcEndpoint || '?handler=HealthCheck';
    if (!serviceKey) return base;
    return base + (base.indexOf('?') === -1 ? '?' : '&') + 'service=' + encodeURIComponent(serviceKey);
  }

  function fetchJson(url) {
    return fetch(url, {
      headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
      cache: 'no-store',
      credentials: 'same-origin'
    }).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status + ' ' + r.statusText);
      return r.json();
    });
  }

  function showFatal(err) {
    el.fatal.classList.add('show');
    el.fatal.querySelector('.hc-fatal-body').innerHTML =
      'Самата проверка не успя да стигне до сървъра. Това значи, че или приложението е спряло, ' +
      'или маршрутът <code>' + esc(endpoint()) + '</code> още не съществува.<br>' +
      'Техническа причина: <code>' + esc(err && err.message ? err.message : String(err)) + '</code>';
  }

  function checkOne(key) {
    setCardChecking(key);
    state.inFlight++;
    renderSummary();

    return fetchJson(endpoint(key))
      .then(function (data) {
        var raw = data && data.services
          ? (Array.isArray(data.services) ? data.services.filter(function (s) { return s.key === key; })[0] : data.services[key])
          : data;
        state.results[key] = normalize(key, raw);
      })
      .catch(function (err) {
        state.results[key] = normalize(key, {
          status: 'fail',
          message: 'Проверката не успя да се изпълни.',
          hint: 'Техническа причина: ' + (err && err.message ? err.message : String(err))
        });
      })
      .then(function () {
        renderCard(state.results[key]);
        state.inFlight--;
        state.lastRunAt = new Date().toISOString();
        renderSummary();
      });
  }

  function checkAllParallel() {
    el.fatal.classList.remove('show');
    var failures = 0;
    return Promise.all(SERVICES.map(function (s) {
      return checkOne(s.key).then(function () {
        if (state.results[s.key] && /не успя да се изпълни/.test(state.results[s.key].message)) failures++;
      });
    })).then(function () {
      /* Всичките осем се провалиха по един и същ начин → сървърът мълчи. */
      if (failures === SERVICES.length) showFatal(new Error('нито една проверка не върна отговор'));
    });
  }

  function checkAllBulk() {
    el.fatal.classList.remove('show');
    SERVICES.forEach(function (s) { setCardChecking(s.key); });
    state.inFlight = 1;
    renderSummary();

    return fetchJson(endpoint())
      .then(function (data) {
        var list = data && data.services;
        var byKey = {};
        if (Array.isArray(list)) list.forEach(function (s) { byKey[s.key] = s; });
        else if (list) byKey = list;

        SERVICES.forEach(function (s) {
          state.results[s.key] = normalize(s.key, byKey[s.key] || {
            status: 'unknown',
            message: 'Сървърът не върна резултат за тази услуга.'
          });
          renderCard(state.results[s.key]);
        });
        state.lastRunAt = (data && data.checkedAt) || new Date().toISOString();
      })
      .catch(function (err) {
        SERVICES.forEach(function (s) {
          state.results[s.key] = normalize(s.key, { status: 'unknown', message: 'Няма отговор от сървъра.' });
          renderCard(state.results[s.key]);
        });
        showFatal(err);
      })
      .then(function () {
        state.inFlight = 0;
        renderSummary();
      });
  }

  function runAll() {
    return state.mode === 'bulk' ? checkAllBulk() : checkAllParallel();
  }

  /* ── копиране като текст ──────────────────────────────────────────── */

  function asPlainText() {
    var lines = [];
    lines.push('HEALTH CHECK — Blockchain Education 2026');
    lines.push('Проверено: ' + (state.lastRunAt ? new Date(state.lastRunAt).toLocaleString('bg-BG') : '—'));
    lines.push('Адрес: ' + location.origin + location.pathname);
    lines.push('');
    SERVICES.forEach(function (s) {
      var r = state.results[s.key];
      if (!r) { lines.push('[?] ' + s.name + ' — не е проверена'); return; }
      var mark = { ok: 'OK  ', warn: 'WARN', fail: 'FAIL', unconfigured: 'CFG ', checking: '... ', unknown: '?   ' }[r.status];
      var head = '[' + mark + '] ' + r.name;
      var ms = msText(r.responseMs);
      if (ms) head += '  (' + ms + ')';
      lines.push(head);
      if (r.message) lines.push('        ' + r.message);
      if (r.hint) lines.push('        → ' + r.hint);
      (r.details || []).forEach(function (d) {
        if (d && d.value != null) lines.push('        ' + (d.label || '') + ': ' + d.value);
      });
    });
    return lines.join('\n');
  }

  function copyReport() {
    var text = asPlainText();
    var done = function () {
      if (window.showToast) window.showToast('Резултатът е копиран като текст.', 'success');
    };
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(done, fallback);
    } else {
      fallback();
    }
    function fallback() {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand('copy'); done(); }
      catch (e) { if (window.showToast) window.showToast('Копирането не стана — маркирай текста ръчно.', 'error'); }
      document.body.removeChild(ta);
    }
  }

  /* ── старт ────────────────────────────────────────────────────────── */

  function init() {
    var section = document.getElementById(SECTION_ID);
    if (!section) return;

    el.section    = section;
    el.grid       = section.querySelector('#hc-grid');
    el.summary    = section.querySelector('#hc-summary');
    el.fatal      = section.querySelector('#hc-fatal');
    el.timestamp  = section.querySelector('#hc-timestamp');
    el.refreshAll = section.querySelector('#hc-refresh-all');
    el.copyBtn    = section.querySelector('#hc-copy');

    if (!el.grid || !el.summary) return;
    if (section.dataset.hcMode === 'bulk') state.mode = 'bulk';

    /* Скелето се строи веднага — табът никога не е празен и мълчалив. */
    el.grid.innerHTML = '';
    SERVICES.forEach(function (s) { el.grid.appendChild(buildCard(s)); });

    el.grid.addEventListener('click', function (e) {
      var btn = e.target.closest('.hc-refresh-btn');
      if (!btn || btn.disabled) return;
      checkOne(btn.dataset.service);
    });

    if (el.refreshAll) el.refreshAll.addEventListener('click', function () { runAll(); });
    if (el.copyBtn) el.copyBtn.addEventListener('click', copyReport);

    /* Проверката тръгва при отваряне на таба, не при зареждане на панела. */
    function maybeRun() {
      if (state.loaded) return;
      if (!section.classList.contains('active')) return;
      state.loaded = true;
      runAll();
    }

    var trigger = document.querySelector('.admin-tab[data-target="' + SECTION_ID + '"]');
    if (trigger) trigger.addEventListener('click', function () { setTimeout(maybeRun, 0); });

    /* Ако табът е бил активен от предишната сесия (localStorage). */
    setTimeout(maybeRun, 0);

    /* Ръчен достъп отвън, ако някога потрябва. */
    window.adminHealthCheck = { run: runAll, one: checkOne, text: asPlainText };
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();

/* ══════════════════════════════════════════════════════════════════════
   Допълнение извън Health Check: падащите менюта на докосване.

   В оригинала .dropdown-content се показва само на :hover. На телефон
   :hover „залепва" след тап и менюто остава отворено, докато не се
   докосне друго място. Затова в CSS-а :hover е ограничен до устройства
   с истински показалец, а тук се грижим за докосването чрез клас .open.

   На мишка нищо не се променя — този код не се закача при hover.
   ══════════════════════════════════════════════════════════════════════ */

(function () {
  'use strict';

  var canHover = window.matchMedia && window.matchMedia('(hover: hover) and (pointer: fine)').matches;
  if (canHover) return;

  function closeAll(except) {
    document.querySelectorAll('.dropdown.open').forEach(function (d) {
      if (d !== except) d.classList.remove('open');
    });
  }

  document.addEventListener('click', function (e) {
    var trigger = e.target.closest('.dropdown > .action-btn, .dropdown > button');
    if (trigger) {
      var dd = trigger.closest('.dropdown');
      var wasOpen = dd.classList.contains('open');
      closeAll(dd);
      dd.classList.toggle('open', !wasOpen);
      e.preventDefault();
      return;
    }
    /* Клик върху елемент от менюто го затваря; клик встрани — също. */
    closeAll(null);
  });
})();
