// adminPanelChangelog.js — точката „има ново" до таба.
//
// Съхранението е в localStorage, не в базата: това е предпочитание на
// БРАУЗЪРА, не на акаунта. Ако беше в базата, отваряне на панела от друг
// компютър би скрило известието и на първия.
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
        catch (e) { return; }   // частен режим — просто няма известие

        dot.hidden = (seen === latest);

        // Гасне при ОТВАРЯНЕ на таба, не при зареждане на панела: иначе
        // известието изчезва, без някой да е прочел какво е новото.
        var btn = document.querySelector('.admin-tab[data-target="tab-changelog"]');
        if (btn) {
            btn.addEventListener('click', function () {
                dot.hidden = true;
                try { localStorage.setItem(KEY, latest); } catch (e) { /* няма как */ }
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
