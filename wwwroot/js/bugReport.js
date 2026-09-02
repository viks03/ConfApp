// ── Bug Report widget — floating button + modal, present on every page for
//    logged-in admins. Submits to Controllers/BugReportController.cs.
(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        var fab       = document.getElementById('brwFab');
        var overlay   = document.getElementById('brwOverlay');
        var closeBtn  = document.getElementById('brwCloseBtn');
        var cancelBtn = document.getElementById('brwCancelBtn');
        var form      = document.getElementById('brwForm');
        var submitBtn = document.getElementById('brwSubmitBtn');
        var statusMsg = document.getElementById('brwStatusMsg');
        var pageUrlInput   = document.getElementById('brwPageUrl');
        var userAgentInput = document.getElementById('brwUserAgent');

        if (!fab || !overlay || !form) return; // widget not on this page for some reason — bail quietly

        // ── Custom dropdowns (Category / Severity) ──────────────────────────
        // Generic wiring: any .brw-select whose data-hidden-input points at a
        // real <input type="hidden"> gets click-to-open, click-option-to-pick,
        // click-outside-to-close, and Escape-to-close behavior.
        var iconSvgNS = 'http://www.w3.org/2000/svg';

        document.querySelectorAll('.brw-select').forEach(function (root) {
            var trigger = root.querySelector('.brw-select-trigger');
            var menu = root.querySelector('.brw-select-menu');
            var hiddenInput = document.getElementById(root.getAttribute('data-hidden-input'));
            var labelEl = trigger.querySelector('.brw-select-label');
            var iconSlot = trigger.querySelector('.brw-select-icon-slot');

            trigger.addEventListener('click', function (e) {
                e.stopPropagation();
                var isOpen = root.classList.contains('is-open');
                document.querySelectorAll('.brw-select.is-open').forEach(function (r) { r.classList.remove('is-open'); });
                if (!isOpen) root.classList.add('is-open');
            });

            menu.querySelectorAll('.brw-select-option').forEach(function (opt) {
                opt.addEventListener('click', function () {
                    if (hiddenInput) hiddenInput.value = opt.getAttribute('data-value');
                    if (labelEl) labelEl.textContent = opt.getAttribute('data-label') || opt.textContent.trim();

                    // Swap the trigger's icon slot for the selected option's — copies
                    // whatever markup the option used (svg icon, or a colored dot span).
                    var sourceIcon = opt.querySelector('.brw-select-icon-slot');
                    if (iconSlot && sourceIcon) {
                        iconSlot.innerHTML = sourceIcon.innerHTML;
                        // The severity colour class (brw-sev-low/medium/high/critical) lives
                        // on the OPTION BUTTON itself, not on its inner icon-slot span —
                        // read it from `opt`, not from `sourceIcon`.
                        var sevMatch = opt.className.match(/brw-sev-\S+/);
                        iconSlot.className = 'brw-select-icon-slot' + (sevMatch ? ' ' + sevMatch[0] : '');
                    }

                    menu.querySelectorAll('.brw-select-option').forEach(function (o) { o.classList.remove('is-selected'); });
                    opt.classList.add('is-selected');
                    root.classList.remove('is-open');
                });
            });
        });

        document.addEventListener('click', function () {
            document.querySelectorAll('.brw-select.is-open').forEach(function (r) { r.classList.remove('is-open'); });
        });

        // ── Modal open/close ─────────────────────────────────────────────────
        function openModal() {
            // Auto-captured, no admin input needed.
            if (pageUrlInput) pageUrlInput.value = window.location.href;
            if (userAgentInput) userAgentInput.value = navigator.userAgent;
            overlay.classList.add('is-open');
            document.body.style.overflow = 'hidden';
            var titleInput = document.getElementById('brwTitle');
            if (titleInput) setTimeout(function () { titleInput.focus(); }, 60);
        }

        function closeModal() {
            overlay.classList.remove('is-open');
            document.body.style.overflow = '';
            statusMsg.className = 'brw-status-msg';
            statusMsg.innerHTML = '';
        }

        fab.addEventListener('click', openModal);
        closeBtn.addEventListener('click', closeModal);
        cancelBtn.addEventListener('click', closeModal);
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) closeModal(); // click on the dim backdrop, not the panel itself
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && overlay.classList.contains('is-open')) closeModal();
        });

        // ── Submit button state machine: idle → sending (spinner) → success
        //    (checkmark) or back to idle with an error banner ───────────────
        var submitIconSlot = submitBtn.querySelector('.brw-btn-icon-slot');
        var submitLabel = submitBtn.querySelector('.brw-btn-label');
        var idleIconHtml = submitIconSlot.innerHTML;

        function setSubmitState(state) {
            if (state === 'sending') {
                submitBtn.disabled = true;
                submitBtn.classList.remove('is-success');
                submitIconSlot.innerHTML = '<span class="brw-spinner"></span>';
                submitLabel.textContent = 'Sending…';
            } else if (state === 'success') {
                submitBtn.classList.add('is-success');
                submitIconSlot.innerHTML =
                    '<svg class="brw-check-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"></path></svg>';
                submitLabel.textContent = 'Sent!';
            } else {
                submitBtn.disabled = false;
                submitBtn.classList.remove('is-success');
                submitIconSlot.innerHTML = idleIconHtml;
                submitLabel.textContent = 'Submit Report';
            }
        }

        function showStatus(kind, message) {
            var icon = kind === 'success'
                ? '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>'
                : '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>';
            statusMsg.className = 'brw-status-msg is-' + kind;
            statusMsg.innerHTML = icon + '<span>' + message + '</span>';
        }

        form.addEventListener('submit', async function (e) {
            e.preventDefault();

            var title = document.getElementById('brwTitle').value.trim();
            var description = document.getElementById('brwDescription').value.trim();
            if (!title || !description) {
                showStatus('error', 'Please fill in both the title and description.');
                return;
            }

            setSubmitState('sending');
            statusMsg.className = 'brw-status-msg';
            statusMsg.innerHTML = '';

            var MIN_SENDING_MS = 650; // guarantees the spinner is actually seen, even on a fast local response
            var startedAt = Date.now();

            try {
                var formData = new FormData(form);
                var res = await fetch('/api/bug-reports/submit', { method: 'POST', body: formData });
                var data = await res.json().catch(function () { return null; });

                var elapsed = Date.now() - startedAt;
                if (elapsed < MIN_SENDING_MS) {
                    await new Promise(function (resolve) { setTimeout(resolve, MIN_SENDING_MS - elapsed); });
                }

                if (res.ok && data && data.success) {
                    setSubmitState('success');
                    showStatus('success', "Thanks — your report has been logged and the team's been notified.");
                    setTimeout(function () {
                        form.reset();
                        setSubmitState('idle');
                        closeModal();
                    }, 1500);
                } else {
                    setSubmitState('idle');
                    showStatus('error', (data && data.message) || 'Something went wrong submitting the report. Please try again.');
                }
            } catch (err) {
                setSubmitState('idle');
                showStatus('error', 'Network error — the report was not sent. Please try again.');
                console.log('Bug report submit failed:', err);
            }
        });
    });
})();