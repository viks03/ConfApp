/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
/* lecturerModal.js — the lecturer portrait shown large.

   One object, two pages: the home page (homeLecturers.js) and Lecturers
   (scriptLecturers.js). Each of them used to carry its own copy of
   buildModal / openModal / closeModal and the Escape handler, and the comment
   in homeLecturers.js claimed the classes were the same "so that it is one and
   the same object, not a lookalike". The classes were the same; the code was
   not, and the copies had already drifted apart:

     - the magnifier: <line x1="16" y1="16"> against <line x1="16.5" y1="16.5">.
       Kept: 16,16 — the handle then touches the circle (11 + 7/sqrt(2) ≈ 15.95).
     - the heading id: #lc-modal-name against #hl-modal-name. Kept:
       #lc-modal-name (the two are never on one page).
     - the biography: only the home page showed it. It stays that way —
       .lc-modal-bio has a rule in indexStyle.css only, while Lecturers loads
       lecturersStyle.css. So bio is the caller's choice.

   The markup is built on the first open rather than in Razor: otherwise every
   card on the page would carry a hidden copy of its own photograph.

   Depends on common.js (ConfApp.lockScroll) and is loaded after it. */

(function (window, document) {
    'use strict';

    var ConfApp = window.ConfApp || (window.ConfApp = {});

    var backdrop = null;
    var lastFocused = null;

    var ZOOM_SVG =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" ' +
        'stroke-linecap="round" stroke-linejoin="round">' +
        '<circle cx="11" cy="11" r="7"></circle>' +
        '<line x1="16" y1="16" x2="21" y2="21"></line>' +
        '<line x1="11" y1="8" x2="11" y2="14"></line>' +
        '<line x1="8" y1="11" x2="14" y2="11"></line>' +
        '</svg>';

    var ARROW_SVG =
        ' <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round">' +
        '<line x1="5" y1="12" x2="19" y2="12"></line>' +
        '<polyline points="12 5 19 12 12 19"></polyline>' +
        '</svg>';

    /* The magnifier badge is added by the script, so that it is not left
       sitting in the markup promising a modal that will never open if the
       script fails to load. */
    ConfApp.addZoomIcon = function (avatar) {
        if (!avatar || avatar.querySelector('.lc-zoom')) return;
        var zoom = document.createElement('span');
        zoom.className = 'lc-zoom';
        zoom.setAttribute('aria-hidden', 'true');
        zoom.innerHTML = ZOOM_SVG;
        avatar.appendChild(zoom);
    };

    function buildModal(closeLabel) {
        backdrop = document.createElement('div');
        backdrop.className = 'lc-modal-backdrop';
        backdrop.innerHTML =
            '<div class="lc-modal" role="dialog" aria-modal="true" aria-labelledby="lc-modal-name">' +
                '<i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>' +
                '<div class="lc-modal-photo">' +
                    '<span class="lc-modal-index"></span>' +
                    '<img alt="">' +
                '</div>' +
                '<button type="button" class="lc-modal-close">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="6" y1="6" x2="18" y2="18"></line><line x1="18" y1="6" x2="6" y2="18"></line></svg>' +
                '</button>' +
                '<div class="lc-modal-body">' +
                    '<h3 id="lc-modal-name"></h3>' +
                    '<p class="lc-modal-role"></p>' +
                    '<p class="lc-modal-org"></p>' +
                    '<p class="lc-modal-bio"></p>' +
                '</div>' +
            '</div>';

        var close = backdrop.querySelector('.lc-modal-close');
        close.setAttribute('aria-label', closeLabel || '');
        document.body.appendChild(backdrop);

        close.addEventListener('click', ConfApp.closeLecturerModal);
        // A click outside the card closes it.
        backdrop.addEventListener('click', function (e) {
            if (e.target === backdrop) ConfApp.closeLecturerModal();
        });
        return backdrop;
    }

    /* The fields of data: index, name, role, org, bio, url, moreLabel,
       closeLabel, imgSrc, imgAlt. Empty ones are hidden rather than left as a
       gap in the card — role, org and bio are all nullable in
       LecturerModel. */
    ConfApp.openLecturerModal = function (data) {
        var d = data || {};
        if (!backdrop) buildModal(d.closeLabel);
        else if (d.closeLabel) {
            backdrop.querySelector('.lc-modal-close').setAttribute('aria-label', d.closeLabel);
        }

        var modalImg = backdrop.querySelector('.lc-modal-photo img');
        modalImg.src = d.imgSrc || '';
        modalImg.alt = d.imgAlt || '';

        backdrop.querySelector('.lc-modal-index').textContent = d.index || '';
        backdrop.querySelector('#lc-modal-name').textContent = d.name || '';

        [['.lc-modal-role', d.role], ['.lc-modal-org', d.org], ['.lc-modal-bio', d.bio]]
            .forEach(function (pair) {
                var el = backdrop.querySelector(pair[0]);
                var value = pair[1] || '';
                el.textContent = value;
                el.style.display = value ? '' : 'none';
            });

        // The link to the profile goes INSIDE the card: the modal intercepts
        // the click on the portrait, so without it reaching the profile would
        // take a close and a second click.
        var existing = backdrop.querySelector('.lc-modal-link');
        if (existing) existing.remove();

        if (d.url) {
            var link = document.createElement('a');
            link.className = 'lc-modal-link';
            link.href = d.url;
            link.target = '_blank';
            link.rel = 'noopener';
            link.textContent = d.moreLabel || '';
            link.insertAdjacentHTML('beforeend', ARROW_SVG);
            backdrop.querySelector('.lc-modal-body').appendChild(link);
        }

        lastFocused = document.activeElement;
        backdrop.classList.add('is-open');
        ConfApp.lockScroll(true);
        backdrop.querySelector('.lc-modal-close').focus();
    };

    ConfApp.closeLecturerModal = function () {
        if (!backdrop || !backdrop.classList.contains('is-open')) return;
        backdrop.classList.remove('is-open');
        ConfApp.lockScroll(false);
        if (lastFocused && typeof lastFocused.focus === 'function') lastFocused.focus();
    };

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && backdrop && backdrop.classList.contains('is-open')) {
            ConfApp.closeLecturerModal();
        }
    });

})(window, document);
