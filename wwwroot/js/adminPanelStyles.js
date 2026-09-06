// adminPanelStyles.js — таб „Визуални стилове".
//
// Отделен файл от adminPanel.js по нареждане в брифа: интеграцията се
// проверява с един <script> и се маха с един <script>. Ползва три неща
// от adminPanel.js, които вече са глобални: postJson(), buildFormData(),
// showToast() и openModal()/closeModal(). Ако някой ден тези станат
// модулни, тук трябва да се внесат — друга зависимост няма.
//
// ВАЖНО: табът НЕ работи, докато 'tab-styles' не влезе във VALID_TABS в
// adminPanel.js. Виж STYLES_TAB_ANSWERS.md, §6.
//
// Какво прави скриптът:
//   1. филтрира редовете (всички / ръчни / с custom CSS);
//   2. отваря редактора за един ред и връща стойностите в него;
//   3. поддържа ЖИВ преглед на тъмна плочка, докато се влачат плъзгачите
//      (нищо не се записва, докато не се натисне „Запиши");
//   4. проверява custom CSS-а КЛИЕНТСКИ, преди да го изпрати — същите
//      правила, които сървърът прилага повторно (клиентската проверка е
//      удобство, не защита);
//   5. записва, връща към резервната стойност и изключва всички custom
//      CSS наведнъж (аварийният изход).

(function () {
    'use strict';

    // Осемте валидни фона. Държат се в CSS-а на сайта; тук са само за
    // да не се изпрати непознат низ от развален DOM.
    // ВАЖНО: този списък трябва да съвпада с allowed в Index.cshtml.cs и с
    // правилата в globalEffects.css. При разминаване редакторът мълчаливо
    // пада на 'grid' — точно това се беше случило с изтрития 'beam'.
    var VALID_BG = ['grid', 'paper', 'hatch', 'glow', 'contour', 'fiber', 'lantern', 'prism', 'sonar', 'caustic', 'spine', 'ascent', 'off'];

    // Базовите стойности от globalEffects.css. Прегледът е нормализиран
    // спрямо тях: --ga-ink 1 значи „както е в CSS-а".
    var BASE = { intensity: 1, ink: 0.05, glow: 0.13, cursor: 0.05, grid: 48, step: 8, bar: 2 };

    // Мащаб на прегледа: 34px решетка в плочката отговаря на 48px на сайта.
    var PREVIEW_SCALE = 34 / BASE.grid;

    var root, modal, preview, previewArt, previewLabel, form;

    function q(id) { return document.getElementById(id); }

    // ── 1. Филтри ────────────────────────────────────────────────
    function initFilters() {
        var buttons = root.querySelectorAll('.gfx-filter-btn');
        if (!buttons.length) return;

        buttons.forEach(function (btn) {
            btn.addEventListener('click', function () {
                buttons.forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                applyFilter(btn.dataset.gfxFilter || 'all');
            });
        });
    }

    function applyFilter(mode) {
        root.querySelectorAll('.gfx-row').forEach(function (row) {
            var manual = row.dataset.gfxManual === 'true';
            var css = row.dataset.gfxHasCss === 'true';
            var show = mode === 'all' || (mode === 'manual' && manual) || (mode === 'css' && css);
            row.hidden = !show;
        });

        // Група без видими редове се скрива цялата — иначе остава
        // заглавие с празнина под него.
        root.querySelectorAll('.gfx-group').forEach(function (group) {
            var visible = group.querySelectorAll('.gfx-row:not([hidden])').length;
            group.hidden = visible === 0;
        });
    }

    // ── 2. Редакторът ────────────────────────────────────────────
    // Миниатюрите на осемте вградени фона са в adminPanelStyles.css. Тези на
    // собствените не могат да са там — параметрите им се въвеждат по-късно.
    // Затова правилата се искат от сървъра: СЪЩИЯ код, който рисува и сайта.
    var customCssCache = {};

    function paintCustomArt(artEl) {
        var layers = artEl.getAttribute('data-gfx-layers');
        if (!layers) return;

        var key = layers;
        if (customCssCache[key]) { applyCustom(artEl, customCssCache[key]); return; }

        fetch('?handler=CustomBackgroundCss&slug=preview&layersJson=' + encodeURIComponent(layers),
              { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                if (!d || !d.success) return;
                customCssCache[key] = d.css || '';
                applyCustom(artEl, customCssCache[key]);
            })
            .catch(function () { /* мрежова грешка — плочката остава празна */ });
    }

    // Правилата идват със селектор за истинския слой; тук се прилагат
    // директно върху двата <span> на конкретната плочка.
    function applyCustom(artEl, css) {
        var a = artEl.querySelector('.gfx-art-a');
        var b = artEl.querySelector('.gfx-art-b');
        var mA = css.match(/\.afx-a\{([^}]*)\}/);
        var mB = css.match(/\.afx-b\{([^}]*)\}/);
        if (a) a.style.cssText = mA ? mA[1] : '';
        if (b) b.style.cssText = mB ? mB[1] : '';
    }

    // ── Превключватели за телефон ────────────────────────────────────────
    function initMobileToggles() {
        // Глобалният
        var g = q('gfx-global-mobile');
        if (g) {
            g.addEventListener('change', async function () {
                g.disabled = true;
                try {
                    var r = await postJson('?handler=SetGlobalMobile',
                                           buildFormData({ allowed: g.checked }));
                    if (r.success) {
                        showToast(r.message, 'success');
                        // Редовете стават приглушени, за да е ясно, че
                        // настройката им е без значение, докато е спряно.
                        root.classList.toggle('gfx-mobile-off', !g.checked);
                    } else {
                        g.checked = !g.checked;   // връщаме, ако не е записано
                        showToast('Грешка: ' + r.message, 'error');
                    }
                } catch (e) {
                    g.checked = !g.checked;
                    showToast('Сървърна грешка.', 'error');
                } finally { g.disabled = false; }
            });

            root.classList.toggle('gfx-mobile-off', !g.checked);
        }

        // По ред. Делегиране, за да работи и след пренарисуване на реда.
        root.addEventListener('change', async function (e) {
            var box = e.target.closest('.gfx-row-mobile-input');
            if (!box) return;

            var row = box.closest('.gfx-row');
            if (!row) return;

            box.disabled = true;
            try {
                var r = await postJson('?handler=TogglePageMobile', buildFormData({
                    pageKey: row.dataset.gfxKey,
                    enabled: box.checked
                }));
                if (r.success) {
                    row.dataset.gfxMobile = box.checked ? 'true' : 'false';
                    // Редът вече има запис — бутонът за връщане се отключва.
                    row.dataset.gfxManual = 'true';
                    paintRow(row);
                } else {
                    box.checked = !box.checked;
                    showToast('Грешка: ' + r.message, 'error');
                }
            } catch (err) {
                box.checked = !box.checked;
                showToast('Сървърна грешка.', 'error');
            } finally { box.disabled = false; }
        });
    }

    function initEditor() {
        modal = q('gfx-editor-modal');
        if (!modal) return;

        preview = modal.querySelector('.gfx-preview');
        previewArt = modal.querySelector('.gfx-preview .gfx-art');
        previewLabel = modal.querySelector('.gfx-preview-label');
        form = modal.querySelector('#gfx-editor-form');

        // Избор на фон
        modal.querySelectorAll('.gfx-choice input').forEach(function (input) {
            input.addEventListener('change', function () {
                modal.querySelectorAll('.gfx-choice').forEach(function (c) { c.classList.remove('is-selected'); });
                input.closest('.gfx-choice').classList.add('is-selected');
                syncPreview();
                syncMotionWarning();
            });
        });

        modal.querySelectorAll('input[name="gfxMotion"], input[name="gfxMotionSpeed"]')
             .forEach(function (input) {
                 input.addEventListener('change', function () {
                     syncMotionWarning();
                     syncPreview();
                 });
             });

        // Плъзгачи: всяка промяна маха състоянието „наследено".
        modal.querySelectorAll('.gfx-slider-row input[type="range"]').forEach(function (input) {
            input.addEventListener('input', function () {
                setInherited(input, false);
                syncPreview();
            });
        });

        // „Върни към стойността от CSS-а" — полето отново става null.
        modal.querySelectorAll('.gfx-inherit').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var input = q(btn.dataset.gfxFor);
                if (!input) return;
                input.value = input.dataset.gfxBase;
                setInherited(input, true);
                syncPreview();
            });
        });

        // Курсорното петно в прегледа
        if (preview) {
            preview.addEventListener('pointermove', function (e) {
                var r = preview.getBoundingClientRect();
                preview.style.setProperty('--ga-cx', Math.round(e.clientX - r.left) + 'px');
                preview.style.setProperty('--ga-cy', Math.round(e.clientY - r.top) + 'px');
            });
        }

        q('gfx-css-check').addEventListener('click', function () {
            var report = checkCss(q('gfx-css-input').value);
            renderReport(report);
        });

        q('gfx-editor-save').addEventListener('click', save);

        // Отваряне от ред
        root.addEventListener('click', function (e) {
            var edit = e.target.closest('.gfx-edit-btn');
            if (edit) { openEditor(edit.closest('.gfx-row')); return; }

            var reset = e.target.closest('.gfx-reset-btn');
            if (reset) { resetRow(reset.closest('.gfx-row')); return; }
        });

        var panic = q('gfx-disable-all-css');
        if (panic) panic.addEventListener('click', disableAllCss);
    }

    function setInherited(input, inherited) {
        input.dataset.gfxInherited = inherited ? 'true' : 'false';
        var row = input.closest('.gfx-slider-row');
        if (row) row.classList.toggle('is-inherited', inherited);
        writeOutput(input);
    }

    function writeOutput(input) {
        var out = input.closest('.gfx-slider-row').querySelector('output');
        if (!out) return;
        var inherited = input.dataset.gfxInherited === 'true';
        var unit = input.dataset.gfxUnit || '';
        out.textContent = inherited ? 'по CSS' : (input.value + unit);
    }

    function fieldValue(input) {
        // null (наследено) се изпраща като празен низ — сървърът пише NULL.
        return input.dataset.gfxInherited === 'true' ? '' : input.value;
    }

    function openEditor(row) {
        if (!row || !modal) return;
        var d = row.dataset;

        q('gfx-page-key').value = d.gfxKey || '';
        var title = modal.querySelector('.modal-header h3');
        if (title) title.textContent = (d.gfxName || d.gfxKey || '') + ' — фон';

        var bg = VALID_BG.indexOf(d.gfxBg) >= 0 ? d.gfxBg : 'grid';
        modal.querySelectorAll('.gfx-choice').forEach(function (choice) {
            var input = choice.querySelector('input');
            var on = input.value === bg;
            input.checked = on;
            choice.classList.toggle('is-selected', on);
        });

        setField('gfx-intensity', d.gfxIntensity);
        setField('gfx-ink', d.gfxInk);
        setField('gfx-glow', d.gfxGlow);
        setField('gfx-cursor', d.gfxCursor);
        setField('gfx-grid', d.gfxGrid);
        setField('gfx-step', d.gfxStep);
        setField('gfx-bar', d.gfxBar);

        var mr = modal.querySelector('input[name="gfxMotion"][value="' + (d.gfxMotion || '') + '"]');
        if (mr) mr.checked = true;

        var sr = modal.querySelector('input[name="gfxMotionSpeed"][value="' + (d.gfxMotionSpeed || '') + '"]');
        if (sr) sr.checked = true;

        var mob = q('gfx-show-mobile');
        if (mob) mob.checked = d.gfxMobile === 'true';

        syncMotionWarning();

        q('gfx-css-input').value = d.gfxCss || '';
        q('gfx-css-enabled').checked = d.gfxCssEnabled === 'true';
        renderReport(null);

        // Разширените и CSS панелите се отварят затворени всеки път —
        // иначе админът вижда първо --gfx-ink, а трябва да види фона.
        modal.querySelectorAll('.gfx-fold').forEach(function (f) { f.open = false; });

        // Миниатюрите на собствените фонове се рисуват при отваряне, не при
        // зареждане на страницата: иначе всеки собствен фон би правил заявка,
        // дори ако администраторът никога не отвори редактора.
        modal.querySelectorAll('.gfx-art[data-gfx-layers]').forEach(paintCustomArt);

        syncPreview();
        openModal('gfx-editor-modal');
    }

    function setField(id, raw) {
        var input = q(id);
        if (!input) return;
        var empty = raw === undefined || raw === null || raw === '';
        input.value = empty ? input.dataset.gfxBase : raw;
        setInherited(input, empty);
    }

    // ── 3. Живият преглед ────────────────────────────────────────
    // Нормализира стойностите спрямо базовите от globalEffects.css и ги
    // пише като --ga-* на плочката. Тук не се записва нищо.
    function syncPreview() {
        if (!previewArt) return;

        var bg = selectedBg();

        if (bg.indexOf('custom:') === 0) {
            // Големият преглед взима параметрите от плочката на избрания фон.
            var chosen = modal.querySelector('.gfx-choice input:checked');
            var art = chosen ? chosen.parentElement.querySelector('.gfx-art') : null;
            var layers = art ? art.getAttribute('data-gfx-layers') : null;

            previewArt.dataset.bg = 'custom-preview';
            if (layers) {
                previewArt.setAttribute('data-gfx-layers', layers);
                paintCustomArt(previewArt);
            }
            if (previewLabel) {
                var nameEl = chosen ? chosen.parentElement.querySelector('.gfx-choice-name') : null;
                previewLabel.textContent = nameEl ? nameEl.textContent : bg;
            }
        } else {
            previewArt.dataset.bg = bg;
            previewArt.removeAttribute('data-gfx-layers');
            // Изчистваме инлайн стиловете от предишен собствен фон, иначе
            // остават върху вградения и двата се наслагват.
            var pa = previewArt.querySelector('.gfx-art-a');
            var pb = previewArt.querySelector('.gfx-art-b');
            if (pa) pa.style.cssText = '';
            if (pb) pb.style.cssText = '';
            if (previewLabel) previewLabel.textContent = bg === 'off' ? 'без фон' : bg;
        }

        var intensity = numOr('gfx-intensity', BASE.intensity);
        var ink = numOr('gfx-ink', BASE.ink);
        var glow = numOr('gfx-glow', BASE.glow);
        var cursor = numOr('gfx-cursor', BASE.cursor);
        var grid = numOr('gfx-grid', BASE.grid);
        var step = numOr('gfx-step', BASE.step);

        previewArt.style.setProperty('--ga-intensity', intensity);
        previewArt.style.setProperty('--ga-ink', (ink / BASE.ink).toFixed(3));
        previewArt.style.setProperty('--ga-glow', (glow / BASE.glow).toFixed(3));
        previewArt.style.setProperty('--ga-grid', Math.round(grid * PREVIEW_SCALE) + 'px');
        previewArt.style.setProperty('--ga-step', Math.max(2, Math.round(step * PREVIEW_SCALE)) + 'px');
        if (preview) preview.style.setProperty('--ga-cursor', cursor);

        // Движението се показва и в плочката — иначе изборът се вижда чак
        // след запис, на реалната страница.
        var mSel = modal.querySelector('input[name="gfxMotion"]:checked');
        var mVal = mSel ? mSel.value : '';
        if (mVal) previewArt.dataset.motion = mVal;
        else previewArt.removeAttribute('data-motion');
    }

    // Линейните присети са статични нарочно: тънка линия в движение трепти
    // на екран с висока плътност. Предупреждението излиза САМО при рискова
    // комбинация — постоянното се научава да се пренебрегва.
    // ── Кой присет какво движение понася ────────────────────────────────
    // Изведено от УСТРОЙСТВОТО на всеки мотив, не от вкус:
    //
    //  ТЪНКИ ЛИНИИ (grid, paper, barcode, braille, plumb, fiber)
    //    Линия от 1px в движение трепти при субпикселни позиции — физика
    //    на растеризацията, не бъг. Безопасно е само „breathe": мени се
    //    плътността, геометрията стои.
    //
    //  ПОВТАРЯЩ СЕ МОТИВ БЕЗ ЦЕНТЪР (dust, hatch, masonry)
    //    Плъзгане и плаване работят — рисунъкът е еднакъв навсякъде,
    //    затова изместването не открива празнина. Въртенето обаче
    //    открива, че мотивът има ос.
    //
    //  ЦЕНТРИРАН МОТИВ (contour, orbit)
    //    Раздуване е естественото: пръстените растат от своя център.
    //    Плъзгането измества центъра извън екрана.
    //
    //  МЕКО ПОЛЕ БЕЗ ГЕОМЕТРИЯ (glow, chalk)
    //    Понася всичко — няма ръб, който да издаде повторение.
    var MOTION_FIT = {
        grid:    ['breathe'],
        paper:   ['breathe'],
        hatch:   ['breathe', 'drift', 'slide'],
        fiber:   ['breathe', 'slide'],
        contour: ['breathe', 'swell', 'drift'],
        glow:    ['breathe', 'drift', 'slide', 'swell', 'turn']
    };

    // Живите присети се рисуват спрямо курсора и скрола. Движението мести
    // целия слой и връзката с ръката се къса — затова изборът е заключен.
    // Единственото, което ги засяга, е „спряно".
    var LIVE_PRESETS = ['lantern', 'prism', 'sonar', 'caustic', 'spine', 'ascent'];

    var MOTION_REASON = {
        grid:    'решетка от тънки линии — движение трепти',
        paper:   'фини линии — движение трепти',
        hatch:   'щриховане — въртенето открива ъгъла му',
        fiber:   'нишки — понасят плъзгане по оста си, не напречно',
        contour: 'пръстени с център — раздуването е естественото им движение',
        glow:    'меко поле без ръб — понася всичко'
    };

    function syncMotionWarning() {
        var warn = q('gfx-motion-warn');
        if (!warn || !modal) return;

        var bg = selectedBg();
        var m = (modal.querySelector('input[name="gfxMotion"]:checked') || {}).value || '';

        var opts = modal.querySelectorAll('input[name="gfxMotion"]');
        var live = LIVE_PRESETS.indexOf(bg) !== -1;

        // Живите присети: всичко освен „по присет" и „спряно" се заключва.
        opts.forEach(function (o) {
            var lock = live && o.value !== '' && o.value !== 'off';
            o.disabled = lock;
            o.parentElement.classList.toggle('is-locked', lock);
            if (lock && o.checked) {
                var d = modal.querySelector('input[name="gfxMotion"][value=""]');
                if (d) d.checked = true;
            }
        });

        if (live) {
            warn.innerHTML = '<b>' + escapeHtml(bg) + '</b> — този фон се рисува ' +
                'спрямо мишката и скрола. Движението му идва от ръката, затова ' +
                'изборът на анимация е заключен. „Спряно" работи.';
            warn.hidden = false;
            return;
        }

        // „по присет" и „спряно" никога не са рискови.
        if (m === '' || m === 'off' || bg === 'off') { warn.hidden = true; return; }

        var fit = MOTION_FIT[bg];
        // Присет извън таблицата (напр. собствен фон) — не гадаем.
        if (!fit) { warn.hidden = true; return; }

        if (fit.indexOf(m) !== -1) { warn.hidden = true; return; }

        var names = { drift: 'плаване', slide: 'плъзгане', swell: 'раздуване',
                      breathe: 'дишане', turn: 'въртене' };
        var ok = fit.map(function (x) { return names[x] || x; }).join(', ');

        warn.innerHTML = '<b>' + escapeHtml(bg) + '</b> — ' +
            escapeHtml(MOTION_REASON[bg] || '') + '.<br>' +
            'Работи добре с: <b>' + escapeHtml(ok) + '</b>.';
        warn.hidden = false;
    }

    function selectedBg() {
        var checked = modal.querySelector('.gfx-choice input:checked');
        return checked ? checked.value : 'grid';
    }

    function numOr(id, fallback) {
        var input = q(id);
        if (!input) return fallback;
        var v = parseFloat(input.value);
        return isNaN(v) ? fallback : v;
    }

    // ── 4. Проверка на custom CSS (клиентска) ────────────────────
    // Правилата са СЪЩИТЕ като на сървъра; тази проверка е за да не
    // чака админът кръг до сървъра, не е защита. Сървърът прилага
    // всичко отново и той е последната дума.
    function checkCss(css) {
        var errors = [];
        var warnings = [];
        var text = (css || '').trim();

        if (!text) return { level: 'empty', errors: errors, warnings: warnings };

        if (text.length > 4000) errors.push('Над 4000 знака.');
        if (/[<>]/.test(text)) errors.push('Знаците < и > не се приемат — с тях се излиза от &lt;style&gt;.');
        if (/@import/i.test(text)) errors.push('@import не се приема — външен CSS не се зарежда.');
        if (/expression\s*\(/i.test(text)) errors.push('expression() не се приема.');
        if (/javascript\s*:/i.test(text)) errors.push('javascript: не се приема.');
        if (/url\s*\(\s*(?!['"]?data:)/i.test(text)) errors.push('url() се приема само с data: (без външни адреси).');

        var open = (text.match(/{/g) || []).length;
        var close = (text.match(/}/g) || []).length;
        if (open !== close) errors.push('Неравен брой скоби { } — правилото е незавършено.');

        // Обхват: всеки селектор трябва да е ВЪТРЕ в слоя.
        var selectors = text.split('}').map(function (block) {
            var i = block.indexOf('{');
            return i < 0 ? '' : block.slice(0, i).trim();
        }).filter(Boolean);

        selectors.forEach(function (sel) {
            sel.split(',').forEach(function (one) {
                var s = one.trim();
                if (!s) return;
                if (/^(html|body|main|header|footer|\.topbar|\*)/i.test(s)) {
                    errors.push('Селекторът „' + s + '" излиза извън фона. Позволено е само #ambient-fx и .afx-*.');
                } else if (!/^(#ambient-fx|\.afx-)/.test(s)) {
                    warnings.push('Селекторът „' + s + '" ще бъде ограничен автоматично до #ambient-fx.');
                }
            });
        });

        if (/position\s*:\s*fixed/i.test(text)) warnings.push('position: fixed вътре в слоя обикновено не прави каквото се очаква.');
        if (/z-index\s*:\s*([2-9]|[1-9][0-9])/i.test(text)) warnings.push('z-index над 1 може да изкара фона над топбара.');
        if (/filter\s*:\s*blur\((\d{2,})/i.test(text)) warnings.push('Голям blur е скъп на телефон.');

        return {
            level: errors.length ? 'bad' : (warnings.length ? 'warn' : 'ok'),
            errors: errors,
            warnings: warnings
        };
    }

    function renderReport(report) {
        var box = q('gfx-css-report');
        if (!box) return;

        if (!report || report.level === 'empty') {
            box.hidden = true;
            box.className = 'gfx-css-report';
            box.innerHTML = '';
            return;
        }

        var items = report.errors.concat(report.warnings);
        var head = report.level === 'ok'
            ? 'Правилата са в обхвата на фона.'
            : (report.level === 'warn' ? 'Ще се запише, но има бележки:' : 'Няма да се запише:');

        box.className = 'gfx-css-report is-' + (report.level === 'ok' ? 'ok' : (report.level === 'warn' ? 'warn' : 'bad'));
        box.innerHTML = '<b>' + head + '</b>' +
            (items.length ? '<ul><li>' + items.map(escapeHtml).join('</li><li>') + '</li></ul>' : '');
        box.hidden = false;
    }

    function escapeHtml(s) {
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    // ── 5. Запис ─────────────────────────────────────────────────
    async function save() {
        var key = q('gfx-page-key').value;
        if (!key) return;

        var css = q('gfx-css-input').value;
        var report = checkCss(css);
        if (report.level === 'bad') {
            renderReport(report);
            showToast('Custom CSS-ът не е записан — вижте бележките.', 'error');
            return;
        }

        var btn = q('gfx-editor-save');
        var original = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = 'Записва…';

        try {
            var result = await postJson('?handler=SavePageStyle', buildFormData({
                pageKey: key,
                background: selectedBg(),
                intensity: fieldValue(q('gfx-intensity')),
                ink: fieldValue(q('gfx-ink')),
                glow: fieldValue(q('gfx-glow')),
                cursorAlpha: fieldValue(q('gfx-cursor')),
                gridStep: fieldValue(q('gfx-grid')),
                paperStep: fieldValue(q('gfx-step')),
                barHeight: fieldValue(q('gfx-bar')),
                customCss: css,
                customCssEnabled: q('gfx-css-enabled').checked,
                motion: (modal.querySelector('input[name="gfxMotion"]:checked') || {}).value || '',
                motionSpeed: (modal.querySelector('input[name="gfxMotionSpeed"]:checked') || {}).value || '',
                showOnMobile: q('gfx-show-mobile').checked
            }));

            if (result.success) {
                showToast('Записано.', 'success');
                updateRow(key, result);
                closeModal();
            } else {
                showToast('Грешка: ' + result.message, 'error');
                if (result.report) renderReport(result.report);
            }
        } catch (e) {
            showToast('Сървърна грешка. Опитайте отново.', 'error');
        } finally {
            btn.disabled = false;
            btn.innerHTML = original;
        }
    }

    // Редът се пренарежда от отговора на сървъра, не от полетата на
    // формата: сървърът може да е орязал custom CSS-а или да е отхвърлил
    // стойност извън диапазона, и редът трябва да показва записаното.
    function updateRow(key, result) {
        var row = root.querySelector('.gfx-row[data-gfx-key="' + key + '"]');
        if (!row) return;

        var s = result.setting || {};
        row.dataset.gfxBg = s.background || 'grid';
        row.dataset.gfxIntensity = s.intensity == null ? '' : s.intensity;
        row.dataset.gfxInk = s.ink == null ? '' : s.ink;
        row.dataset.gfxGlow = s.glow == null ? '' : s.glow;
        row.dataset.gfxCursor = s.cursorAlpha == null ? '' : s.cursorAlpha;
        row.dataset.gfxGrid = s.gridStep == null ? '' : s.gridStep;
        row.dataset.gfxStep = s.paperStep == null ? '' : s.paperStep;
        row.dataset.gfxBar = s.barHeight == null ? '' : s.barHeight;
        row.dataset.gfxCss = s.customCss || '';
        row.dataset.gfxCssEnabled = s.customCssEnabled ? 'true' : 'false';
        row.dataset.gfxManual = 'true';
        row.dataset.gfxHasCss = (s.customCss && s.customCss.trim()) ? 'true' : 'false';

        paintRow(row);
    }

    // Рисува видимата част на реда от data-* атрибутите — един източник
    // на истина, вместо два пътя (сървърен рендер и клиентски update).
    function paintRow(row) {
        var d = row.dataset;

        var art = row.querySelector('.gfx-art');
        if (art) {
            art.dataset.bg = VALID_BG.indexOf(d.gfxBg) >= 0 ? d.gfxBg : 'unknown';
            art.style.setProperty('--ga-intensity', d.gfxIntensity || 1);
        }

        var meta = row.querySelector('.gfx-row-meta');
        if (!meta) return;

        var tags = [];
        tags.push(d.gfxManual === 'true'
            ? '<span class="gfx-tag is-manual">ръчно</span>'
            : '<span class="gfx-tag is-default">резервно</span>');

        tags.push('<span class="gfx-tag">фон <span class="gfx-tag-value">' + escapeHtml(d.gfxBg || 'grid') + '</span></span>');
        tags.push('<span class="gfx-tag">сила <span class="gfx-tag-value">' +
            (d.gfxIntensity ? escapeHtml(d.gfxIntensity) : 'по CSS') + '</span></span>');

        if (d.gfxHasCss === 'true') {
            tags.push(d.gfxCssEnabled === 'true'
                ? '<span class="gfx-tag is-css">custom CSS</span>'
                : '<span class="gfx-tag is-css-off">custom CSS (изкл.)</span>');
        }

        meta.innerHTML = tags.join('');

        var resetBtn = row.querySelector('.gfx-reset-btn');
        if (resetBtn) resetBtn.disabled = d.gfxManual !== 'true';
    }

    async function resetRow(row) {
        if (!row) return;
        var key = row.dataset.gfxKey;
        if (!key) return;

        row.classList.add('is-saving');
        try {
            var result = await postJson('?handler=ResetPageStyle', buildFormData({ pageKey: key }));
            if (result.success) {
                showToast('Върнато към резервната стойност.', 'success');
                row.dataset.gfxBg = result.fallbackBackground || 'grid';
                ['gfxIntensity', 'gfxInk', 'gfxGlow', 'gfxCursor', 'gfxGrid', 'gfxStep', 'gfxBar', 'gfxCss'].forEach(function (k) {
                    row.dataset[k] = '';
                });
                row.dataset.gfxCssEnabled = 'false';
                row.dataset.gfxManual = 'false';
                row.dataset.gfxHasCss = 'false';
                paintRow(row);
            } else {
                showToast('Грешка: ' + result.message, 'error');
            }
        } catch (e) {
            showToast('Сървърна грешка. Опитайте отново.', 'error');
        } finally {
            row.classList.remove('is-saving');
        }
    }

    // Аварийният изход: изключва custom CSS-а на ВСИЧКИ страници с едно
    // действие. Съдържанието се пази — само не се излъчва.
    async function disableAllCss() {
        var btn = q('gfx-disable-all-css');
        var original = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = 'Изключва…';

        try {
            var result = await postJson('?handler=DisableAllCustomCss', buildFormData({}));
            if (result.success) {
                showToast('Custom CSS е изключен на всички страници.', 'success');
                root.querySelectorAll('.gfx-row').forEach(function (row) {
                    if (row.dataset.gfxHasCss === 'true') {
                        row.dataset.gfxCssEnabled = 'false';
                        paintRow(row);
                    }
                });
            } else {
                showToast('Грешка: ' + result.message, 'error');
            }
        } catch (e) {
            showToast('Сървърна грешка. Опитайте отново.', 'error');
        } finally {
            btn.disabled = false;
            btn.innerHTML = original;
        }
    }

    function init() {
        root = q('tab-styles');
        if (!root) return;

        root.querySelectorAll('.gfx-row').forEach(paintRow);
        initFilters();
        initEditor();
        initMobileToggles();
        applyFilter('all');
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();

// ─────────────────────────────────────────────────────────────────
// Собствени фонове — параметри, не CSS.
//
// Защо параметри: нов фон, написан като CSS от админ поле, е същият
// риск като custom CSS-а, само че постоянен и на всяка страница, която
// го ползва. Осемте вградени фона са комбинация от шест рисунъка и
// четири движения — това се описва с осем числа и два избора, а
// сървърът сглобява правилото. Админът не пише синтаксис, значи не
// може да го счупи, а диапазоните на плъзгачите пазят от фон, който
// спори с текста.
//
// Прегледът тук се генерира клиентски със същата функция, с която
// сървърът трябва да сглоби правилото (виж STYLES_TAB_ANSWERS.md, §5) —
// затова каквото се вижда в панела, това се излъчва.
(function () {
    'use strict';

    var LAYERS = ['a', 'b'];
    var modal, art, styleTag;

    function q(id) { return document.getElementById(id); }

    function readLayer(id) {
        return {
            kind: q('gfx-c-' + id + '-kind').value,
            color: q('gfx-c-' + id + '-color').value,
            spacing: parseInt(q('gfx-c-' + id + '-spacing').value, 10),
            thickness: parseInt(q('gfx-c-' + id + '-thickness').value, 10),
            angle: parseInt(q('gfx-c-' + id + '-angle').value, 10),
            alpha: parseFloat(q('gfx-c-' + id + '-alpha').value),
            motion: q('gfx-c-' + id + '-motion').value,
            duration: parseInt(q('gfx-c-' + id + '-duration').value, 10)
        };
    }

    function rgba(color, alpha) {
        return color === 'accent'
            ? 'rgba(255, 54, 54, ' + alpha + ')'
            : 'rgba(255, 255, 255, ' + alpha + ')';
    }

    // Един слой → едно CSS правило. Същата таблица трябва да е и на
    // сървъра; ако се разминат, панелът лъже.
    // layerCss() беше ТУК. Премахната нарочно: същата логика съществуваше
    // и в C# (CustomBackgroundCss.cs), а две реализации на едно и също нещо
    // се разминават мълчаливо — прегледът показва едно, сайтът рисува друго.
    // Сега правилата идват от сървъра, от същия код, който храни и сайта.


    // Таймерът трябва да е в ТОЗИ модул: двата IIFE-а са отделни
    // обхвати и променлива от първия не се вижда във втория.
    var previewTimer = null;

    function currentMode() {
        var r = modal && modal.querySelector('input[name="gfxCustomMode"]:checked');
        return r ? r.value : 'params';
    }

    function syncPreview() {
        if (!art) return;
        if (!styleTag) {
            styleTag = document.createElement('style');
            styleTag.id = 'gfx-custom-preview-style';
            document.head.appendChild(styleTag);
        }

        var mode = currentMode();
        var payload = mode === 'css'
            ? ''
            : JSON.stringify({ a: readLayer('a'), b: readLayer('b') });
        var rawCss = mode === 'css' ? (q('gfx-custom-css').value || '') : '';

        // Забавяне: влаченето на плъзгач праща десетки събития в секунда.
        // Без него всяко движение би било отделна заявка.
        clearTimeout(previewTimer);
        previewTimer = setTimeout(function () {
            var url = '?handler=CustomBackgroundCss&slug=preview' +
                      '&mode=' + encodeURIComponent(mode) +
                      '&layersJson=' + encodeURIComponent(payload) +
                      '&rawCss=' + encodeURIComponent(rawCss);

            fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (r) { return r.json(); })
                .then(function (d) {
                    if (!d || !d.success) return;
                    // Правилата идват със селектор за истинския слой; тук се
                    // пренасочват към плочката на прегледа.
                    styleTag.textContent = String(d.css || '')
                        .split('#ambient-fx[data-gfx-bg="custom:preview"] .afx-a')
                        .join('#gfx-custom-art .gfx-art-a')
                        .split('#ambient-fx[data-gfx-bg="custom:preview"] .afx-b')
                        .join('#gfx-custom-art .gfx-art-b');
                })
                .catch(function () { /* мрежова грешка — прегледът остава както е */ });
        }, 120);

        LAYERS.forEach(function (id) {
            ['spacing', 'thickness', 'angle', 'alpha'].forEach(function (f) {
                var input = q('gfx-c-' + id + '-' + f);
                var out = input.closest('.gfx-slider-row').querySelector('output');
                if (out) out.textContent = input.value + (input.dataset.gfxUnit || '');
            });
        });
    }

    function slugify(text) {
        return String(text).toLowerCase()
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '')
            .slice(0, 40);
    }

    function openEditor(row) {
        var data = row ? JSON.parse(row.dataset.gfxCustom || '{}') : {};
        q('gfx-custom-slug').value = data.slug || '';
        q('gfx-custom-name').value = data.name || '';
        q('gfx-custom-key').value = data.slug || '';

        var mode = data.mode === 'css' ? 'css' : 'params';
        var radio = modal.querySelector('input[name="gfxCustomMode"][value="' + mode + '"]');
        if (radio) radio.checked = true;
        q('gfx-custom-css').value = data.rawCss || '';
        q('gfx-custom-css-pane').hidden = mode !== 'css';
        q('gfx-custom-params-pane').hidden = mode === 'css';
        var rep = q('gfx-custom-css-report');
        if (rep) { rep.hidden = true; rep.innerHTML = ''; }

        LAYERS.forEach(function (id) {
            var l = (data.layers && data.layers[id]) || (id === 'a'
                ? { kind: 'lines', color: 'ink', spacing: 48, thickness: 1, angle: 45, alpha: 0.05, motion: 'slide', duration: 150 }
                : { kind: 'none', color: 'accent', spacing: 120, thickness: 1, angle: 45, alpha: 0.03, motion: 'none', duration: 200 });

            q('gfx-c-' + id + '-kind').value = l.kind;
            q('gfx-c-' + id + '-color').value = l.color;
            q('gfx-c-' + id + '-spacing').value = l.spacing;
            q('gfx-c-' + id + '-thickness').value = l.thickness;
            q('gfx-c-' + id + '-angle').value = l.angle;
            q('gfx-c-' + id + '-alpha').value = l.alpha;
            q('gfx-c-' + id + '-motion').value = l.motion;
            q('gfx-c-' + id + '-duration').value = l.duration;
        });

        syncPreview();
        openModal('gfx-custom-modal');
    }

    async function save() {
        var name = q('gfx-custom-name').value.trim();
        if (!name) { showToast('Името е задължително.', 'error'); return; }

        var slug = slugify(q('gfx-custom-key').value || name);
        if (!slug) { showToast('Ключът трябва да съдържа поне една латинска буква или цифра.', 'error'); return; }

        var btn = q('gfx-custom-save');
        var original = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = 'Записва…';

        try {
            var result = await postJson('?handler=SaveCustomBackground', buildFormData({
                originalSlug: q('gfx-custom-slug').value,
                slug: slug,
                name: name,
                layersJson: JSON.stringify({ a: readLayer('a'), b: readLayer('b') }),
                mode: currentMode(),
                rawCss: currentMode() === 'css' ? (q('gfx-custom-css').value || '') : ''
            }));

            if (result.success) {
                showToast('Фонът е записан. Презаредете страницата, за да се появи в списъка.', 'success');
                closeModal();
            } else {
                // Отчетът от сървъра се показва в същата кутия като клиентския.
                var box = q('gfx-custom-css-report');
                if (result.report && box) {
                    var msgs = (result.report.errors || []).concat(result.report.warnings || []);
                    box.innerHTML = msgs.map(function (m) {
                        return '<div>' + escapeHtml(m) + '</div>';
                    }).join('');
                    box.hidden = msgs.length === 0;
                }
                showToast('Грешка: ' + result.message, 'error');
            }
        } catch (e) {
            showToast('Сървърна грешка. Опитайте отново.', 'error');
        } finally {
            btn.disabled = false;
            btn.innerHTML = original;
        }
    }

    async function remove(row) {
        var slug = row.dataset.gfxCustomSlug;
        if (!slug) return;

        row.classList.add('is-saving');
        try {
            var result = await postJson('?handler=DeleteCustomBackground', buildFormData({ slug: slug }));
            if (result.success) {
                // Страниците, които са го ползвали, падат на резервната
                // стойност — сървърът връща колко са, за да не остане
                // админът с усещането, че нищо не се е случило.
                showToast(result.affected
                    ? 'Изтрит. ' + result.affected + ' страници минаха на резервния фон.'
                    : 'Изтрит.', 'success');
                row.remove();
            } else {
                showToast('Грешка: ' + result.message, 'error');
                row.classList.remove('is-saving');
            }
        } catch (e) {
            showToast('Сървърна грешка. Опитайте отново.', 'error');
            row.classList.remove('is-saving');
        }
    }

    function init() {
        modal = q('gfx-custom-modal');
        var list = q('gfx-custom-list');
        if (!modal || !list) return;

        art = q('gfx-custom-art');

        modal.querySelectorAll('.gfx-c-field').forEach(function (field) {
            field.addEventListener('input', syncPreview);
            field.addEventListener('change', syncPreview);
        });

        var preview = modal.querySelector('.gfx-preview');
        if (preview) {
            preview.addEventListener('pointermove', function (e) {
                var r = preview.getBoundingClientRect();
                preview.style.setProperty('--ga-cx', Math.round(e.clientX - r.left) + 'px');
                preview.style.setProperty('--ga-cy', Math.round(e.clientY - r.top) + 'px');
            });
        }

        // Превключване между двата режима — паната се сменят, прегледът
        // се преизчислява от новия източник.
        modal.querySelectorAll('input[name="gfxCustomMode"]').forEach(function (r) {
            r.addEventListener('change', function () {
                var css = currentMode() === 'css';
                q('gfx-custom-css-pane').hidden = !css;
                q('gfx-custom-params-pane').hidden = css;
                syncPreview();
            });
        });

        var cssBox = q('gfx-custom-css');
        if (cssBox) { cssBox.addEventListener('input', syncPreview); }

        q('gfx-custom-new').addEventListener('click', function () { openEditor(null); });
        q('gfx-custom-save').addEventListener('click', save);

        list.addEventListener('click', function (e) {
            var edit = e.target.closest('.gfx-custom-edit-btn');
            if (edit) { openEditor(edit.closest('.gfx-row')); return; }

            var del = e.target.closest('.gfx-custom-delete-btn');
            if (del) { remove(del.closest('.gfx-row')); }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
