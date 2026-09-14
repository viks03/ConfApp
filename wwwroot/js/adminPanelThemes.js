/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
// adminPanelThemes.js — activating, uploading and deleting themes.
(function () {
    'use strict';

    function report(box, title, items, kind) {
        if (!box) return;
        if (!items || !items.length) { box.hidden = true; return; }
        box.className = 'gfx-report is-' + (kind || 'warn');
        box.innerHTML = '<b>' + title + '</b>' +
            items.map(function (m) {
                return '<div>' + window.ConfApp.escapeHtml(m) + '</div>';
            }).join('');
        box.hidden = false;
    }

    function init() {
        var root = document.getElementById('tab-themes');
        if (!root) return;
        var box = document.getElementById('th-report');

        // ── Activating ────────────────────────────────────────────────
        root.addEventListener('click', async function (e) {
            var btn = e.target.closest('.th-activate');
            if (btn) {
                var row = btn.closest('.th-row');
                btn.disabled = true;
                try {
                    var d = await postJson('?handler=ActivateTheme',
                                           buildFormData({ themeKey: row.dataset.thKey || '' }));
                    if (d.success) {
                        showToast(d.message, 'success');
                        // A full reload: a theme changes the look of the panel
                        // ITSELF as well, and a partial redraw drifts away from
                        // that easily.
                        setTimeout(function () { location.reload(); }, 500);
                    } else {
                        showToast('Грешка: ' + d.message, 'error');
                        btn.disabled = false;
                    }
                } catch (err) {
                    showToast('Сървърна грешка.', 'error');
                    btn.disabled = false;
                }
                return;
            }

            // ── Deleting ──────────────────────────────────────────────
            var del = e.target.closest('.th-delete');
            if (del) {
                var r = del.closest('.th-row');
                if (!confirm('Темата ще бъде изтрита. Продължавате ли?')) return;
                del.disabled = true;
                try {
                    var res = await postJson('?handler=DeleteTheme',
                                             buildFormData({ themeKey: r.dataset.thKey }));
                    if (res.success) {
                        showToast(res.message, 'success');
                        setTimeout(function () { location.reload(); }, 500);
                    } else {
                        showToast('Грешка: ' + res.message, 'error');
                        del.disabled = false;
                    }
                } catch (err2) {
                    showToast('Сървърна грешка.', 'error');
                    del.disabled = false;
                }
            }
        });

        // ── Uploading ─────────────────────────────────────────────────
        var file = document.getElementById('th-file');
        if (file) {
            file.addEventListener('change', async function () {
                if (!file.files || !file.files.length) return;

                var fd = new FormData();
                fd.append('file', file.files[0]);
                var token = document.querySelector('input[name="__RequestVerificationToken"]');
                if (token) fd.append('__RequestVerificationToken', token.value);

                if (box) box.hidden = true;
                try {
                    var res = await fetch('?handler=UploadTheme', {
                        method: 'POST', body: fd,
                        headers: { 'X-Requested-With': 'XMLHttpRequest' }
                    });
                    var d = await res.json();

                    if (d.success) {
                        showToast(d.message, 'success');
                        // The warnings matter on SUCCESS too: a theme missing
                        // some of the tokens is valid, but somebody should be
                        // told which ones fell back to their defaults.
                        report(box, 'Бележки:', d.warnings, 'warn');
                        setTimeout(function () { location.reload(); }, 1200);
                    } else {
                        showToast(d.message || 'Темата не е приета.', 'error');
                        report(box, 'Темата не е приета:',
                               (d.errors || []).concat(d.warnings || []), 'bad');
                        file.value = '';
                    }
                } catch (err) {
                    showToast('Сървърна грешка при качване.', 'error');
                    file.value = '';
                }
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
