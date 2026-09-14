/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
// homeLecturers.js — the rotation of the four lecturer cards on the home
// page.
//
// It used to be an inline script in Index.cshtml that changed ONE card every
// 5 seconds. With more lecturers that means the same card changes every 20
// seconds, and the eye catches only a flicker somewhere in the row — it does
// not read as "the list is longer than four".
//
// All FOUR change now, but not at once: a wave with 160ms between neighbouring
// cards. Changing them simultaneously makes the whole section blink and nobody
// knows where to look; a wave reads like a departure board, where the movement
// has a direction and a beginning. A long pause follows, so that there is time
// to read who is on the card.
//
// The data comes from <script type="application/json" id="lecturers-data">
// rather than from JavaScript generated into the markup: the server emits
// plain data and the logic lives in a cacheable file.
(function () {
    "use strict";

    var STAGGER = 160;    // between neighbouring cards in the wave
    var CYCLE = 9000;     // from the start of one wave to the start of the next
    var OUT_MS = 340;     // must match the transition on .rotating-out
    var IN_MS = 460;      // must match the cardFlipIn keyframes

    // ── The portrait, shown large ─────────────────────────────────────
    // The modal lives in lecturerModal.js — one object for this page and for
    // Lecturers. There used to be a second copy of the same code here, which
    // had already drifted from the original (the magnifier, the heading id).
    function openModal(card, grid) {
        if (!card) return;

        var txt = function (sel) {
            var el = card.querySelector(sel);
            return el ? el.textContent.trim() : "";
        };
        var img = card.querySelector(".avatar-lg img");

        window.ConfApp.openLecturerModal({
            index:      txt(".lc-index"),
            name:       txt("h4"),
            role:       txt(".role"),
            org:        txt(".org"),
            bio:        txt(".bio"),
            url:        card.dataset.url,
            moreLabel:  txt(".lc-more") || "Профил",
            closeLabel: grid.getAttribute("data-close-label") || "Затвори",
            imgSrc:     img ? img.getAttribute("src") : "",
            imgAlt:     img ? (img.getAttribute("alt") || "") : ""
        });
    }

    function init() {
        var grid = document.getElementById("lecturers-grid");
        if (!grid) return false;

        var dataEl = document.getElementById("lecturers-data");
        if (!dataEl) return true;

        var all;
        try {
            all = JSON.parse(dataEl.textContent || "[]");
        } catch (e) {
            return true; // invalid JSON: the cards stay as the server rendered them
        }
        if (!all || !all.length) return true;

        var isBg = (grid.getAttribute("data-locale") || "").toLowerCase() === "bg";

        // Delegated clicks, because the contents of the cards are replaced.
        //
        // The portrait and the card are TWO different targets: clicking the
        // portrait opens the photograph large (the same modal as on the
        // Lecturers page), clicking anywhere else on the card goes to the
        // profile. Without stopPropagation one click would do both.
        grid.addEventListener("click", function (e) {
            var avatar = e.target.closest(".avatar-lg");
            if (avatar) {
                e.preventDefault();
                e.stopPropagation();
                openModal(avatar.closest(".faculty-card"), grid);
                return;
            }
            var card = e.target.closest(".faculty-card");
            if (card && card.dataset.url) window.open(card.dataset.url, "_blank", "noopener");
        });

        // The portrait is a <div> — a <button> nested inside a clickable card
        // is a bad idea — so the keyboard support is added here.
        grid.addEventListener("keydown", function (e) {
            if (e.key !== "Enter" && e.key !== " ") return;
            var avatar = e.target.closest(".avatar-lg");
            if (!avatar) return;
            e.preventDefault();
            e.stopPropagation();
            openModal(avatar.closest(".faculty-card"), grid);
        });

        var cards = Array.prototype.slice.call(grid.querySelectorAll(".faculty-card"));
        if (!cards.length) return true;

        // The magnifier badge comes from ConfApp.addZoomIcon
        // (lecturerModal.js). The drawing here used
        // <line x1="16.5" y1="16.5"> while Lecturers used 16,16; the shared one
        // is 16,16.
        cards.forEach(function (card) {
            window.ConfApp.addZoomIcon(card.querySelector(".avatar-lg"));
        });

        // With fewer than five lecturers there is nothing to rotate: the four
        // cards already show everybody, and a rotation would only repeat the
        // same people.
        if (all.length <= cards.length) return true;

        function pick(l, a, b) { return isBg ? (l[a] || l[b]) : l[b]; }

        // The first N of the array are the ones the server has already
        // rendered.
        var shown = [];
        for (var i = 0; i < cards.length; i++) shown.push(i);

        var queue = [];
        var qi = 0;

        function refillQueue() {
            queue = [];
            for (var i = 0; i < all.length; i++) queue.push(i);
            // Fisher-Yates. Math.random() - 0.5 inside sort() is not a uniform
            // shuffle and in some browsers produces nearly the same order every
            // time.
            for (var j = queue.length - 1; j > 0; j--) {
                var k = Math.floor(Math.random() * (j + 1));
                var t = queue[j]; queue[j] = queue[k]; queue[k] = t;
            }
            qi = 0;
        }

        refillQueue();

        // The next lecturer who is NOT on screen at that moment; otherwise the
        // same person can appear in two cards at once.
        function nextIndex() {
            for (var tries = 0; tries < all.length * 2; tries++) {
                if (qi >= queue.length) refillQueue();
                var idx = queue[qi++];
                if (shown.indexOf(idx) === -1) return idx;
            }
            return null;
        }

        function swap(card, slot) {
            var idx = nextIndex();
            if (idx === null) return;
            var l = all[idx];

            card.classList.add("rotating-out");

            setTimeout(function () {
                var img = card.querySelector(".avatar-lg img");
                var name = pick(l, "nameBg", "nameEn") || "";
                if (img) { img.src = l.avatar; img.alt = name; }

                var h = card.querySelector("h4");
                var role = card.querySelector(".role");
                var org = card.querySelector(".org");
                var bio = card.querySelector(".bio");
                if (h) h.textContent = name;
                if (role) role.textContent = pick(l, "roleBg", "roleEn") || "";
                if (org) org.textContent = pick(l, "orgBg", "orgEn") || "";
                if (bio) bio.textContent = pick(l, "bioBg", "bioEn") || "";

                card.dataset.url = l.url || "";
                card.style.cursor = l.url ? "pointer" : "default";
                shown[slot] = idx;

                card.classList.remove("rotating-out");
                card.classList.add("rotating-in");
                setTimeout(function () { card.classList.remove("rotating-in"); }, IN_MS);
            }, OUT_MS);
        }

        function wave() {
            // In a background tab browsers throttle the timers, the waves pile
            // up, and everything changes at once when the tab comes back.
            if (document.hidden) return;
            cards.forEach(function (card, i) {
                setTimeout(function () { swap(card, i); }, i * STAGGER);
            });
        }

        setInterval(wave, CYCLE);
        return true;
    }

    // If the script is loaded before the grid exists (in head, with defer, or
    // dynamically) it tries again rather than dying quietly.
    function boot() {
        if (init()) return;
        var tries = 0;
        var t = setInterval(function () {
            tries++;
            if (init() || tries > 20) clearInterval(t);
        }, 100);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", boot);
    } else {
        boot();
    }
})();
