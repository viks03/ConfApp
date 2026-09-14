/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
// adminPanelDownloads.js — uploading and removing the downloadable files.
(function () {
    'use strict';

    function init() {
        var root = document.getElementById('tab-downloads');
        if (!root) return;

        // Delegated, because the rows are redrawn after every upload.
        root.addEventListener('change', async function (e) {
            var input = e.target.closest('.dl-file');
            if (!input || !input.files || !input.files.length) return;

            var row = input.closest('.dl-row');
            var key = row.dataset.dlKey;
            var file = input.files[0];

            // The form data is built by hand: the shared buildFormData helper
            // does not handle a file input.
            var fd = new FormData();
            fd.append('fileKey', key);
            fd.append('file', file);

            var token = document.querySelector('input[name="__RequestVerificationToken"]');
            if (token) fd.append('__RequestVerificationToken', token.value);

            row.classList.add('is-busy');
            try {
                var res = await fetch('?handler=UploadDownload', {
                    method: 'POST',
                    body: fd,
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });
                var d = await res.json();

                if (d.success) {
                    showToast(d.message, 'success');
                    // A full reload: the row changes both its buttons (Upload →
                    // Replace) and its state. More honest than a partial redraw,
                    // which easily drifts away from what the server stored.
                    setTimeout(function () { location.reload(); }, 600);
                } else {
                    showToast('Грешка: ' + d.message, 'error');
                    input.value = '';
                }
            } catch (err) {
                showToast('Сървърна грешка при качване.', 'error');
                input.value = '';
            } finally {
                row.classList.remove('is-busy');
            }
        });

        root.addEventListener('click', async function (e) {
            var btn = e.target.closest('.dl-remove');
            if (!btn) return;

            var row = btn.closest('.dl-row');
            if (!confirm('Файлът ще бъде премахнат от сайта. Продължавате ли?')) return;

            btn.disabled = true;
            try {
                var d = await postJson('?handler=RemoveDownload',
                                       buildFormData({ fileKey: row.dataset.dlKey }));
                if (d.success) {
                    showToast(d.message, 'success');
                    setTimeout(function () { location.reload(); }, 600);
                } else {
                    showToast('Грешка: ' + d.message, 'error');
                    btn.disabled = false;
                }
            } catch (err) {
                showToast('Сървърна грешка.', 'error');
                btn.disabled = false;
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
