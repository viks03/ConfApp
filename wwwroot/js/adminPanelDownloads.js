// adminPanelDownloads.js — качване и премахване на файлове за изтегляне.
(function () {
    'use strict';

    function init() {
        var root = document.getElementById('tab-downloads');
        if (!root) return;

        // Делегиране: редовете се пренарисуват след качване.
        root.addEventListener('change', async function (e) {
            var input = e.target.closest('.dl-file');
            if (!input || !input.files || !input.files.length) return;

            var row = input.closest('.dl-row');
            var key = row.dataset.dlKey;
            var file = input.files[0];

            // Формата се строи на ръка, защото buildFormData не работи с файл.
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
                    // Презареждане: редът мени и бутоните си (Качи → Замени),
                    // и състоянието. По-честно е от частично пренарисуване,
                    // което лесно се разминава със сървъра.
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
