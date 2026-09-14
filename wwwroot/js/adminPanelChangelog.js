/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
// adminPanelChangelog.js — the "there is something new" dot next to the tab.
//
// The state is kept in localStorage rather than in the database: this is a
// property of the BROWSER, not of the account. Stored server-side, opening the
// panel on another computer would clear the notice on the first one too.
(function () {
    'use strict';

    var KEY = 'confapp.changelog.seen';

    function init() {
        var tab = document.getElementById('tab-changelog');
        var dot = document.getElementById('cl-dot');
        if (!tab || !dot) return;

        var latest = tab.getAttribute('data-cl-latest') || '';
        if (!latest) return;

        var seen;
        try { seen = localStorage.getItem(KEY); }
        catch (e) { return; }   // a private window: no notice, no error

        dot.hidden = (seen === latest);

        // Cleared when the tab is OPENED, not when the panel loads: otherwise
        // the notice disappears without anyone having read what is new.
        var btn = document.querySelector('.admin-tab[data-target="tab-changelog"]');
        if (btn) {
            btn.addEventListener('click', function () {
                dot.hidden = true;
                try { localStorage.setItem(KEY, latest); } catch (e) { /* nothing to be done */ }
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
