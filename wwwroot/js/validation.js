/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
/* ═══════════════════════════════════════════════════════════════════════
   validation.js — the client-side validation for the three forms that have
   one: registration, the profile, and the verification documents.

   It is a convenience, never a defence: every rule here is enforced again on
   the server, and the page works with the script disabled.

   Three things it was rewritten to fix:

   1. It bound to `#registrationForm` ONLY. Profile.cshtml loaded it, but its
      form is `#profileForm`, so on the profile the validation did nothing at
      all — and SubmitDocuments had no client-side validation whatsoever.

   2. `showFeedback` looked for `.checkbox-container`, a class that no longer
      existed after the redesign. `closest()` returned null, the next line
      threw a TypeError, and the whole submit handler died halfway through —
      AFTER `e.preventDefault()`. The result: the button did nothing.

   3. The messages were generic ("This field is required") for every field.
      Each field now has its own text, handed to the script by the page
      through `window.ValidationMessages`, which comes from resx and is
      therefore translated.

   It binds only to the fields that actually exist on the current page or
   phase, so one set of rules serves all three forms.
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
        // accepts both "media.bg" and "https://media.bg"
        domain:    /^(https?:\/\/)?[^\s.]+\.[^\s]{2,}$/
    };

    // The same two limits the server enforces: 10 MB for a paper (Register and
    // Profile), 3 MB for a verification document.
    var MAX_PAPER_BYTES = 10 * 1024 * 1024;
    var MAX_PROOF_BYTES = 3 * 1024 * 1024;

    // ── Showing an error under one field ─────────────────────────────────
    // The message goes into the element after the field, creating one if the
    // page does not already provide it — the three forms name that slot
    // differently, which is why four class names are recognised.
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

    // ── The live "is this address taken" check (registration only) ───────
    var emailTaken = false;
    var emailTakenMsg = '';

    // ── Validating one field ─────────────────────────────────────────────
    function validate(field, rule) {
        if (!field) return true;            // not on this page or phase
        if (field.disabled) return true;    // a locked field is not the user's to fix

        // The multi-phase registration carries the earlier phases' answers
        // forward as <input type="hidden">. Those must not be validated here:
        //   - for a radio group (Input.PartForm) a hidden input is NEVER
        //     .checked, so phase 3 complained that no participation form had
        //     been chosen, although the user had chosen one in phase 2;
        //   - and even when it is right, an error on an invisible field is one
        //     the user cannot act on — there is nothing on screen to fix.
        // Their values were validated in their own phase, and the server checks
        // them again on the final submit.
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
                // An optional field: empty is valid.
                // The server does not require a scheme either — it adds
                // https:// itself, see NormalizeWebsite in
                // SubmitDocuments.cshtml.cs — so "media.bg" is accepted here
                // too. Only something that does not look like a domain at all
                // is rejected; anything stricter blocked the whole form over a
                // field that is not even required.
                if (val && !RX.domain.test(val)) { ok = false; text = msg('url_invalid', 'Проверете адреса.'); }
                break;

            case 'text':
                if (!val && !rule.optional)   { ok = false; text = msg(rule.msgKey, 'Полето е задължително.'); }
                break;

            case 'choice':
                var scope = field.form || document;
                // Real radios only: a hidden input carrying the same name is not
                // a choice.
                var group = scope.querySelectorAll('input[type="radio"][name="' + field.name + '"]');
                if (!group.length) break;    // no radio group on this phase
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
                    // A network problem must not lock the form: the server
                    // refuses a taken address at submit time anyway.
                    emailTaken = false;
                });
        });

        field.addEventListener('input', function () { emailTaken = false; });
    }

    // ── Binding one form ─────────────────────────────────────────────────
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

        // Which button caused the submit. `e.submitter` is not available in
        // every browser, so the last button pressed is remembered as well.
        var lastSubmitter = null;
        form.addEventListener('click', function (e) {
            var btn = e.target.closest('button, input[type="submit"], input[type="image"]');
            if (btn && form.contains(btn)) lastSubmitter = btn;
        }, true);

        form.addEventListener('submit', function (e) {
            // The "Back" button carries formnovalidate, but that switches off
            // the BROWSER's validation only — this listener still ran, found an
            // invalid field and called preventDefault(). The result: "Back" did
            // nothing. Going backwards must validate nothing.
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

            if (allOk) return;   // let the submit through

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

    // ── Start-up: bind whichever of the three forms is on this page ──────
    document.addEventListener('DOMContentLoaded', function () {

        // ── Registration ─────────────────────────────────────────────────
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

        // ── The profile (which the previous version never reached) ───────
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

        // ── Verification documents ───────────────────────────────────────
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
