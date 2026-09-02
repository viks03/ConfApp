/* ═══════════════════════════════════════════════════════════════════════
   validation.js — клиентска валидация, Blockchain Education 2026

   ПРЕНАПИСАН. Какво беше сбъркано в предишната версия:

   1. Скриптът се закачаше САМО на `#registrationForm`. `Profile.cshtml`
      го зареждаше, но неговата форма е `#profileForm` — тоест на профила
      валидацията не правеше абсолютно нищо. `SubmitDocuments` пък изобщо
      нямаше клиентска валидация.

   2. `showFeedback` търсеше `.checkbox-container` — клас, който вече не
      съществува след редизайна. `closest()` връщаше null, следващият ред
      хвърляше TypeError и целият submit handler умираше по средата, СЛЕД
      `e.preventDefault()`. Резултат: бутонът не правеше нищо.

   3. Съобщенията бяха общи ("This field is required") за всички полета.
      Сега всяко поле има собствен текст, подаден от страницата през
      `window.ValidationMessages` (идва от resx, затова е преведен).

   Регистрира се само за полетата, които реално съществуват на текущата
   страница/фаза — една и съща логика обслужва трите форми.
   ═══════════════════════════════════════════════════════════════════════ */

(function () {
    'use strict';

    var M = window.ValidationMessages || {};

    function msg(key, fallback) {
        return M[key] || fallback;
    }

    var RX = {
        latinName: /^[A-Za-z\u00C0-\u024F\s\-']+$/,
        email:     /^[^\s@]+@[^\s@]+\.[^\s@]+$/,
        phone:     /^\+?[\d\s\-()]{8,20}$/,
        digits:    /^\d+$/,
        url:       /^https?:\/\/.+\..+/,
        // приема и "media.bg", и "https://media.bg"
        domain:    /^(https?:\/\/)?[^\s.]+\.[^\s]{2,}$/
    };

    var MAX_PAPER_BYTES = 10 * 1024 * 1024;   // доклад — 10 MB
    var MAX_PROOF_BYTES = 3 * 1024 * 1024;    // документ за верификация — 3 MB

    // ── Показване на грешка под конкретно поле ───────────────────────────
    function fieldShell(field) {
        if (field.type === 'checkbox') {
            return field.closest('.auth-check, .pf-check, label') || field.parentElement;
        }
        return field;
    }

    function setFieldState(field, ok, message) {
        var shell = fieldShell(field);
        if (!shell || !shell.parentNode) return;

        var slot = shell.nextElementSibling;
        var isSlot = slot && (
            slot.classList.contains('error-message') ||
            slot.classList.contains('field-error') ||
            slot.classList.contains('err-msg') ||
            slot.classList.contains('auth-field-err')
        );

        if (!isSlot) {
            slot = document.createElement('span');
            slot.className = 'error-message';
            shell.parentNode.insertBefore(slot, shell.nextSibling);
        }

        if (ok) {
            shell.classList.remove('is-invalid');
            slot.textContent = '';
            slot.hidden = true;
        } else {
            shell.classList.add('is-invalid');
            slot.textContent = message;
            slot.hidden = false;
        }
    }

    // ── Live проверка за зает имейл (само в регистрацията) ───────────────
    var emailTaken = false;
    var emailTakenMsg = '';

    // ── Валидатор на едно поле ───────────────────────────────────────────
    function validate(field, rule) {
        if (!field) return true;            // полето не е на тази фаза/страница
        if (field.disabled) return true;    // заключено поле не се валидира

        // БЪГ ФИКС: многофазовата регистрация пренася попълненото от предишните
        // фази като <input type="hidden">. Те не бива да се валидират тук:
        //   • за radio група (Input.PartForm) hidden input НИКОГА не е .checked,
        //     затова на фаза 3 излизаше "Моля, изберете форма на участие",
        //     макар потребителят да я е избрал на фаза 2;
        //   • дори да мине, грешка върху невидимо поле е неоправима от
        //     потребителя — няма какво да поправи на екрана.
        // Стойностите им са валидирани на своята фаза и сървърът ги проверява
        // отново при финалния submit.
        if (field.type === 'hidden') return true;

        var val = (field.value || '').trim();
        var ok = true;
        var text = '';

        switch (rule.type) {
            case 'name':
                if (!val)                         { ok = false; text = msg(rule.msgKey + '_required', 'Полето е задължително.'); }
                else if (val.length < 2)          { ok = false; text = msg(rule.msgKey + '_short', 'Твърде кратко.'); }
                else if (!RX.latinName.test(val)) { ok = false; text = msg(rule.msgKey + '_latin', 'Използвайте само латински букви.'); }
                break;

            case 'age':
                if (!val)                      { ok = false; text = msg('age_required', 'Полето е задължително.'); }
                else if (!RX.digits.test(val)) { ok = false; text = msg('age_digits', 'Въведете само цифри.'); }
                else {
                    var n = parseInt(val, 10);
                    if (n < 18 || n > 100)     { ok = false; text = msg('age_range', 'Възрастта трябва да е между 18 и 100 години.'); }
                }
                break;

            case 'email':
                if (!val)                     { ok = false; text = msg('email_required', 'Полето е задължително.'); }
                else if (!RX.email.test(val)) { ok = false; text = msg('email_invalid', 'Проверете имейл адреса.'); }
                else if (emailTaken)          { ok = false; text = emailTakenMsg || msg('email_taken', 'Този имейл вече е регистриран.'); }
                break;

            case 'phone':
                if (!val)                     { ok = false; text = msg('phone_required', 'Полето е задължително.'); }
                else if (!RX.phone.test(val)) { ok = false; text = msg('phone_invalid', 'Проверете телефонния номер.'); }
                break;

            case 'url':
                // Незадължително поле — празното е валидно.
                // Сървърът НЕ изисква схема (добавя https:// сам, виж
                // NormalizeWebsite в SubmitDocuments.cshtml.cs), затова и тук
                // приемаме "media.bg". Отхвърляме само нещо, което изобщо не
                // прилича на домейн — иначе блокирахме подаването заради поле,
                // което дори не е задължително.
                if (val && !RX.domain.test(val)) { ok = false; text = msg('url_invalid', 'Проверете адреса.'); }
                break;

            case 'text':
                if (!val && !rule.optional)   { ok = false; text = msg(rule.msgKey, 'Полето е задължително.'); }
                break;

            case 'choice':
                var scope = field.form || document;
                // Само истински radio-та — hidden носител на същото име не е избор.
                var group = scope.querySelectorAll('input[type="radio"][name="' + field.name + '"]');
                if (!group.length) break;    // няма radio група на тази фаза
                var picked = Array.prototype.some.call(group, function (r) { return r.checked; });
                if (!picked)                 { ok = false; text = msg(rule.msgKey, 'Моля, направете избор.'); }
                break;

            case 'checkbox':
                if (!field.checked)          { ok = false; text = msg(rule.msgKey, 'Съгласието е задължително.'); }
                break;

            case 'file':
                if (field.files && field.files.length) {
                    var f = field.files[0];
                    var ext = (f.name.split('.').pop() || '').toLowerCase();
                    if (rule.exts && rule.exts.indexOf(ext) === -1) {
                        ok = false; text = msg('file_type', 'Неразрешен тип файл.');
                    } else if (rule.maxBytes && f.size > rule.maxBytes) {
                        ok = false;
                        text = msg('file_too_large', 'Файлът е твърде голям ({0} MB).')
                                 .replace('{0}', (f.size / 1024 / 1024).toFixed(1));
                    }
                } else if (rule.required) {
                    ok = false; text = msg(rule.msgKey, 'Моля, изберете файл.');
                }
                break;
        }

        setFieldState(field, ok, text);
        return ok;
    }

    function wireEmailCheck(field) {
        if (!field) return;

        field.addEventListener('blur', function () {
            var email = (field.value || '').trim();
            if (!email || email.indexOf('@') === -1) return;

            fetch('?handler=CheckEmail&email=' + encodeURIComponent(email) + '&t=' + Date.now())
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    emailTaken = !data.isAvailable;
                    emailTakenMsg = data.message || '';
                    validate(field, { type: 'email' });
                })
                .catch(function () {
                    // Мрежов проблем не бива да заключва формата — сървърът
                    // пак ще откаже зает имейл при самия submit.
                    emailTaken = false;
                });
        });

        field.addEventListener('input', function () { emailTaken = false; });
    }

    // ── Свързване на форма ───────────────────────────────────────────────
    function wire(form, rules) {
        if (!form) return;

        form.setAttribute('novalidate', 'novalidate');

        var live = [];
        Object.keys(rules).forEach(function (sel) {
            var field = form.querySelector(sel);
            if (!field) return;

            var rule = rules[sel];
            live.push({ field: field, rule: rule });

            var evt = (field.type === 'checkbox' || field.type === 'radio' || field.type === 'file')
                ? 'change' : 'input';

            field.addEventListener(evt, function () { validate(field, rule); });
            field.addEventListener('blur', function () { validate(field, rule); });
        });

        // Кой бутон е предизвикал submit-а. `e.submitter` не е навсякъде наличен,
        // затова пазим и последния натиснат бутон.
        var lastSubmitter = null;
        form.addEventListener('click', function (e) {
            var btn = e.target.closest('button, input[type="submit"], input[type="image"]');
            if (btn && form.contains(btn)) lastSubmitter = btn;
        }, true);

        form.addEventListener('submit', function (e) {
            // БЪГ ФИКС: бутонът "Назад" носи formnovalidate, но това изключва само
            // БРАУЗЪРНАТА валидация — този слушател продължаваше да се изпълнява,
            // намираше невалидно поле и правеше preventDefault(). Резултат:
            // "Назад" не работеше. Навигация назад не бива да валидира нищо.
            var submitter = e.submitter || lastSubmitter;
            if (submitter && (
                    submitter.hasAttribute('formnovalidate') ||
                    /handler=Back/i.test(submitter.getAttribute('formaction') || '')
                )) {
                return;
            }

            var allOk = true;

            live.forEach(function (item) {
                if (!validate(item.field, item.rule)) allOk = false;
            });

            if (allOk) return;   // пуска submit-а нормално

            e.preventDefault();

            var firstBad = form.querySelector('.is-invalid');
            if (firstBad) {
                var focusable = firstBad.matches('input, select, textarea')
                    ? firstBad
                    : firstBad.querySelector('input, select, textarea');
                if (focusable) focusable.focus();
                firstBad.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        });
    }

    // ── Стартиране ───────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', function () {

        // ── Регистрация ──────────────────────────────────────────────────
        var regForm = document.getElementById('registrationForm');
        if (regForm) {
            wire(regForm, {
                '#Input_FirstName':     { type: 'name',  msgKey: 'first_name' },
                '#Input_LastName':      { type: 'name',  msgKey: 'last_name' },
                '#Input_Age':           { type: 'age' },
                '#Input_AcademicTitle': { type: 'text',  msgKey: 'title_required' },
                '#Input_Email':         { type: 'email' },
                '#Input_Phone':         { type: 'phone' },
                '#Input_Workplace':     { type: 'text',  msgKey: 'workplace_required' },
                'input[name="Input.PartForm"]': { type: 'choice', msgKey: 'partform_required' },
                '#terms-accept':        { type: 'checkbox', msgKey: 'gdpr_required' },
                '#Input_UploadedFile':  { type: 'file', exts: ['pdf', 'doc', 'docx'], maxBytes: MAX_PAPER_BYTES }
            });

            wireEmailCheck(document.getElementById('Input_Email'));
        }

        // ── Профил (предишната версия изобщо не стигаше дотук) ───────────
        var profForm = document.getElementById('profileForm');
        if (profForm) {
            wire(profForm, {
                '#Input_FirstName':     { type: 'name',  msgKey: 'first_name' },
                '#Input_LastName':      { type: 'name',  msgKey: 'last_name' },
                '#Input_Age':           { type: 'age' },
                '#Input_AcademicTitle': { type: 'text',  msgKey: 'title_required' },
                '#Input_Phone':         { type: 'phone' },
                '#Input_Workplace':     { type: 'text',  msgKey: 'workplace_required' },
                '#Input_UploadedFile':  { type: 'file', exts: ['pdf', 'doc', 'docx'], maxBytes: MAX_PAPER_BYTES }
            });
        }

        // ── Подаване на документи ────────────────────────────────────────
        var sdForm = document.querySelector('.sd-form');
        if (sdForm) {
            var isStudent = !!sdForm.querySelector('#student-doc1');

            if (isStudent) {
                wire(sdForm, {
                    '#StudentInput_University': { type: 'text', msgKey: 'university_required' },
                    '#StudentInput_Specialty':  { type: 'text', msgKey: 'specialty_required' },
                    '#StudentInput_StudentId':  { type: 'text', msgKey: 'studentid_required' },
                    'input[name="StudentInput.StudyYear"]': { type: 'choice', msgKey: 'year_required' },
                    '#student-doc1': { type: 'file', required: true, msgKey: 'proof_required',
                                       exts: ['jpg', 'jpeg', 'png'], maxBytes: MAX_PROOF_BYTES }
                });
            } else {
                wire(sdForm, {
                    '#JournalistInput_MediaOutlet':  { type: 'text', msgKey: 'media_required' },
                    '#JournalistInput_Position':     { type: 'text', msgKey: 'position_required' },
                    '#JournalistInput_MediaWebsite': { type: 'url' },
                    '#journ-doc1': { type: 'file', required: true, msgKey: 'proof_required',
                                     exts: ['jpg', 'jpeg', 'png'], maxBytes: MAX_PROOF_BYTES }
                });
            }
        }
    });
})();
