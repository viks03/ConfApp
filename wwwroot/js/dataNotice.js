// ── Data-use notice (cookie banner) — FUNCTIONAL ────────────────────────────
// Persists the visitor's actual choice in a real cookie (not localStorage —
// consent needs to be readable server-side too, e.g. if a future script tag
// ever needs to be gated at render time rather than purely client-side).
//
// EU/GDPR-relevant decisions baked in here:
//   - The cookie that REMEMBERS the choice is itself exempt from needing its
//     own consent (it's "strictly necessary" to avoid re-prompting every
//     page load) — so it's written unconditionally on Accept AND Reject.
//   - 6-month expiry, matching CNIL's specific guidance (many implementations
//     use up to 12 months; 6 sits on the more conservative, defensible end).
//   - A version stamp is included — bump DN_CONSENT_VERSION below whenever
//     the cookie policy or category list changes materially, and every
//     existing visitor will be re-prompted automatically (their old cookie's
//     version no longer matches, so it's treated as if consent was never given).
(function () {
    'use strict';

    var DN_COOKIE_NAME = 'dn_prefs';
    var DN_CONSENT_VERSION = 1; // keep this in sync with the server-side check in _Layout.cshtml
    var DN_CONSENT_MAX_AGE_DAYS = 182; // ~6 months

    // ── Tiny cookie helpers — no framework needed for something this small ──
    function dnSetCookie(name, value, days) {
        var maxAge = days * 24 * 60 * 60;
        var secure = window.location.protocol === 'https:' ? '; Secure' : '';
        document.cookie = name + '=' + encodeURIComponent(value) +
            '; path=/; max-age=' + maxAge + '; SameSite=Lax' + secure;
    }
    function dnGetCookie(name) {
        var pattern = new RegExp('(?:^|; )' + name.replace(/([.$?*|{}()[\]\\/+^])/g, '\\$1') + '=([^;]*)');
        var match = document.cookie.match(pattern);
        return match ? decodeURIComponent(match[1]) : null;
    }

    function dnReadConsent() {
        var raw = dnGetCookie(DN_COOKIE_NAME);
        if (!raw) return null;
        try {
            var parsed = JSON.parse(raw);
            if (parsed.v !== DN_CONSENT_VERSION) return null; // policy changed since — re-prompt
            if (!parsed.categories || typeof parsed.categories !== 'object') return null;
            return parsed;
        } catch (e) {
            return null; // malformed/tampered cookie — treat as no consent given
        }
    }

    function dnWriteConsent(categoriesMap) {
        dnSetCookie(DN_COOKIE_NAME, JSON.stringify({
            v: DN_CONSENT_VERSION,
            ts: Date.now(),
            categories: categoriesMap
        }), DN_CONSENT_MAX_AGE_DAYS);
    }

    document.addEventListener('DOMContentLoaded', function () {
        var banner        = document.getElementById('dnBanner');
        var relaunchBtn    = document.getElementById('dnRelaunchBtn');
        var modalOverlay  = document.getElementById('dnModalOverlay');
        var modalCloseBtn = document.getElementById('dnModalCloseBtn');

        var manageBtn = document.getElementById('dnManageBtn');
        var rejectBtn = document.getElementById('dnRejectBtn');
        var acceptBtn = document.getElementById('dnAcceptBtn');

        var modalRejectBtn = document.getElementById('dnModalRejectBtn');
        var modalSaveBtn   = document.getElementById('dnModalSaveBtn');
        var modalAcceptBtn = document.getElementById('dnModalAcceptBtn');

        var toggles = document.querySelectorAll('.dn-toggle[data-category]');

        // ── Read whatever category states are on screen right now (after any
        //    Accept-All/Reject-All/manual toggle), plus every locked category
        //    (Necessary etc.) as always-true, for a complete audit record. ──
        function currentCategoriesMap() {
            var map = {};
            toggles.forEach(function (t) {
                map[t.getAttribute('data-category')] = t.classList.contains('is-on');
            });
            document.querySelectorAll('.dn-toggle.is-locked').forEach(function (t) {
                var cat = t.closest('.dn-category');
                var key = cat ? cat.getAttribute('data-key') : null;
                if (key) map[key] = true;
            });
            return map;
        }

        // ── Seed the modal's toggles from a previously-saved choice, so
        //    reopening Preferences shows what was actually saved, not the
        //    server-rendered defaults every time. ─────────────
        function applyConsentToToggles(categoriesMap) {
            toggles.forEach(function (t) {
                var key = t.getAttribute('data-category');
                var hasSaved = categoriesMap && Object.prototype.hasOwnProperty.call(categoriesMap, key);
                var on = hasSaved ? !!categoriesMap[key] : t.classList.contains('is-on');
                t.classList.toggle('is-on', on);
                t.setAttribute('aria-checked', on ? 'true' : 'false');
            });
        }

        function hideBanner() {
            if (!banner) return;
            banner.classList.add('is-hidden');
            if (relaunchBtn) relaunchBtn.classList.add('is-visible');
        }

        // ── On load: the server already decided whether to render the banner
        //    hidden (see _Layout.cshtml — avoids any flash-of-banner for
        //    returning visitors). Here we just seed the modal's toggles to
        //    match whatever was actually saved, so it's correct if opened. ──
        var savedConsent = dnReadConsent();
        if (savedConsent) applyConsentToToggles(savedConsent.categories);

        // ── Body scroll lock ─────────────────────────────────────────────
        //    Plain `body { overflow: hidden }` looks like it should work,
        //    but iOS Safari specifically has a long-documented bug where it
        //    does NOT reliably block touch-drag scrolling of the page
        //    behind a modal. Pinning the body to position:fixed at its
        //    current scroll offset is the standard, robust fix — the page
        //    literally can't move because it's taken out of the normal
        //    document flow, and closing restores the exact scroll position.
        var dnScrollLockY = 0;
        function lockBodyScroll() {
            dnScrollLockY = window.scrollY || window.pageYOffset || 0;
            document.body.style.position = 'fixed';
            document.body.style.top = (-dnScrollLockY) + 'px';
            document.body.style.left = '0';
            document.body.style.right = '0';
            document.body.style.width = '100%';
        }
        function unlockBodyScroll() {
            document.body.style.position = '';
            document.body.style.top = '';
            document.body.style.left = '';
            document.body.style.right = '';
            document.body.style.width = '';
            // behavior: 'instant' is required here — the site sets a global
            // scroll-behavior: smooth on <html> (mainStyle.css), and without
            // overriding it, restoring the saved position visibly animates
            // from 0 back down to it instead of landing there immediately.
            window.scrollTo({ top: dnScrollLockY, left: 0, behavior: 'instant' });
        }

        function openModal() {
            // Guards against a rapid double-trigger (e.g. a fast double-tap
            // on the relaunch button) re-running lockBodyScroll() while
            // already locked — at that point window.scrollY reads 0 (the
            // page is pinned via position:fixed), which would overwrite the
            // real saved position with 0 and restore to the wrong spot.
            if (!modalOverlay || modalOverlay.classList.contains('is-open')) return;
            modalOverlay.classList.add('is-open');
            if (banner) banner.classList.add('dn-banner-suppressed');
            lockBodyScroll();
            // Guarantees the border-beam is always actually moving, not just
            // visually present — see the restartBeamAnimations comment below
            // for why display:none → flex (exactly what .is-open just did)
            // can leave an @property-driven animation "stuck" on a static
            // frame in some WebKit versions, every time the modal reopens.
            restartBeamAnimations();
        }
        function closeModal() {
            if (!modalOverlay || !modalOverlay.classList.contains('is-open')) return;
            modalOverlay.classList.remove('is-open');
            if (banner) banner.classList.remove('dn-banner-suppressed');
            unlockBodyScroll();
        }

        // Lets any OTHER page (e.g. the dedicated /Cookies page) reopen this
        // same global modal without duplicating the open/close logic.
        window.openCookiePreferencesModal = openModal;

        function setAllToggles(on) {
            toggles.forEach(function (t) {
                t.classList.toggle('is-on', on);
                t.setAttribute('aria-checked', on ? 'true' : 'false');
            });
        }

        // ── Toggle switches ─────────────────────────────────────────────
        toggles.forEach(function (t) {
            t.addEventListener('click', function () {
                var isOn = t.classList.contains('is-on');
                t.classList.toggle('is-on', !isOn);
                t.setAttribute('aria-checked', (!isOn).toString());
            });
        });

        // ── Banner actions ──────────────────────────────────────────────
        if (manageBtn) manageBtn.addEventListener('click', openModal);

        if (rejectBtn) rejectBtn.addEventListener('click', function () {
            setAllToggles(false);
            dnWriteConsent(currentCategoriesMap());
            hideBanner();
        });

        if (acceptBtn) acceptBtn.addEventListener('click', function () {
            setAllToggles(true);
            dnWriteConsent(currentCategoriesMap());
            hideBanner();
        });

        // ── Relaunch — reopen preferences after the banner's been dismissed,
        //    required so a visitor can change their mind later. ────────────
        if (relaunchBtn) relaunchBtn.addEventListener('click', openModal);

        // ── Modal chrome ─────────────────────────────────────────────────
        if (modalCloseBtn) modalCloseBtn.addEventListener('click', closeModal);
        if (modalOverlay) {
            modalOverlay.addEventListener('click', function (e) {
                if (e.target === modalOverlay) closeModal();
            });
        }
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && modalOverlay && modalOverlay.classList.contains('is-open')) closeModal();
        });

        // ── Modal actions ────────────────────────────────────────────────
        if (modalRejectBtn) modalRejectBtn.addEventListener('click', function () {
            setAllToggles(false);
            dnWriteConsent(currentCategoriesMap());
            closeModal();
            hideBanner();
        });

        if (modalSaveBtn) modalSaveBtn.addEventListener('click', function () {
            dnWriteConsent(currentCategoriesMap());
            closeModal();
            hideBanner();
        });

        if (modalAcceptBtn) modalAcceptBtn.addEventListener('click', function () {
            setAllToggles(true);
            dnWriteConsent(currentCategoriesMap());
            closeModal();
            hideBanner();
        });

        // ── Restart the border-beam/aurora/icon animations after the tab or
        //    app is backgrounded and resumed — some WebKit versions freeze
        //    them mid-cycle instead of resuming (see the .dn-anim-reset
        //    comment in dataNotice.css). Toggling the class for one frame
        //    forces every affected animation to restart from its keyframes.
        function restartBeamAnimations() {
            var root = document.documentElement;
            root.classList.add('dn-anim-reset');
            void root.offsetHeight; // force reflow so the class actually takes effect
            root.classList.remove('dn-anim-reset');
        }
        document.addEventListener('visibilitychange', function () {
            if (!document.hidden) restartBeamAnimations();
        });
        window.addEventListener('pageshow', function (e) {
            if (e.persisted) restartBeamAnimations();
        });
    });
})();