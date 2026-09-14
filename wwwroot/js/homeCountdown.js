/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
// homeCountdown.js — the live countdown on the top line of the hero.
//
// The date is NOT in this script: it is read from the data-countdown
// attribute of the element itself (ISO format, YYYY-MM-DD). Changing it is
// then one value in the markup rather than an edit to a JavaScript file — and
// resx stays the only place the wording lives.
//
// The element is marked hidden in the HTML and is shown only once there is a
// real value. A missing, invalid or past date renders nothing at all — no empty
// separator on the page, and no "-12 days to go".
(function () {
    "use strict";

    // ── The top bar height → --home-topbar ─────────────────────────
    // The hero is "one screen minus the navigation", so that the pass sits
    // exactly on the bottom edge. The real height of the top bar varies,
    // though: 72px on a desktop, about 106px on a phone where the navigation
    // wraps. The fixed 64px in the CSS left the pass some 50px below the fold —
    // just far enough that nobody could see it was there.
    // The CSS value stays as a fallback for when the script does not load.
    // The measuring and its listeners live in ConfApp.syncTopbarVar
    // (common.js); by default it writes on <html>.
    //
    // [T-23] The returned function is kept: the three places below that used
    // to measure for themselves, before this moved into common.js, went on
    // calling syncTopbarHeight — a name that no longer existed. One of them,
    // on "load", threw a ReferenceError on every visit to the home page.
    var syncTopbarHeight = window.ConfApp.syncTopbarVar("--home-topbar");

    // ── The entrance animation is switched ON, not off ──────────────
    // The keyframes start at opacity: 0. If the animation clock never starts —
    // the page was loaded in a background tab, the document is throttled, the
    // first frame arrives later than the 0.08–0.58s delays — the hero is left
    // with no heading, no paragraph, no logos and no pass. That is, empty.
    //
    // So in the CSS the animation hangs off .hero-ready, which is added from
    // here. The base state is visible: without this script the page simply has
    // no entrance animation.
    //
    // requestAnimationFrame rather than straight away: the class has to be
    // added in a frame where the clock is already running, or we are back to
    // the same problem.
    function armHeroIntro() {
        var hero = document.querySelector(".index-hero");
        if (!hero || hero.classList.contains("hero-ready")) return;
        requestAnimationFrame(function () {
            requestAnimationFrame(function () { hero.classList.add("hero-ready"); });
        });
    }

    armHeroIntro();
    window.addEventListener("load", armHeroIntro);

    var rt;
    window.addEventListener("resize", function () {
        clearTimeout(rt);
        rt = setTimeout(syncTopbarHeight, 150);
    }, { passive: true });

    if (typeof ResizeObserver === "function") {
        var tb = document.querySelector(".topbar");
        if (tb) { try { new ResizeObserver(syncTopbarHeight).observe(tb); } catch (e) { /* an older browser */ } }
    }

    function initCountdown() {
        var el = document.querySelector(".hero-countdown");
        if (!el) return false;

        var raw = el.getAttribute("data-countdown");
        if (!raw) return true;

        // Time zones: the date is read as local midnight, not UTC. Passed to
        // new Date(), "2026-10-29" is taken as UTC, which in Bulgaria is a day
        // out in the late evening.
        var parts = raw.split("-");
        if (parts.length !== 3) return true;

        var target = new Date(+parts[0], +parts[1] - 1, +parts[2]);
        if (isNaN(target.getTime())) return true;

        // The conference lasts more than a day. Without an end date the badge
        // would say "today" on the 29th only and disappear on the 30th and
        // 31st, while the event was still running. The attribute is optional: a
        // missing one means a single-day event.
        var endRaw = el.getAttribute("data-countdown-end");
        var endTarget = target;
        if (endRaw) {
            var ep = endRaw.split("-");
            if (ep.length === 3) {
                var parsed = new Date(+ep[0], +ep[1] - 1, +ep[2]);
                if (!isNaN(parsed.getTime()) && parsed >= target) endTarget = parsed;
            }
        }

        // The wording comes from the markup, through data-* attributes: resx
        // stays the only place language lives, and this file is cacheable and
        // identical for both languages.
        var labelToday = el.getAttribute("data-label-today") || "";
        var labelSuffix = el.getAttribute("data-label-suffix") || "";
        // "1 дни" is wrong in Bulgarian; it has to be "1 ден". English has the
        // same problem with "1 days". The singular form is optional: without it
        // the plural is used, as before.
        var labelSuffixOne = el.getAttribute("data-label-suffix-one") || labelSuffix;

        function render() {
            var now = new Date();
            // Counted in whole days from TODAY's midnight rather than from the
            // current hour, or the number would change in the middle of the
            // day.
            var today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
            var days = Math.round((target - today) / 86400000);
            var daysAfterEnd = Math.round((today - endTarget) / 86400000);

            var num = el.querySelector("b");
            var txt = el.querySelector(".hero-countdown-text");

            // The event is over and the badge is removed. The one case where
            // nothing at all is shown.
            if (daysAfterEnd > 0) {
                el.hidden = true;
                el.classList.remove("is-live", "is-today");
                return false;
            }

            if (days <= 0) {
                // Any day of the conference itself: the word, no number.
                if (num) num.hidden = true;
                if (txt) txt.textContent = labelToday;
                el.hidden = false;
                el.classList.add("is-live", "is-today");
                return true;   // keep checking: tomorrow it may be over
            }

            if (num) { num.hidden = false; num.textContent = days; }
            if (txt) txt.textContent = (days === 1 ? labelSuffixOne : labelSuffix);
            el.hidden = false;
            el.classList.add("is-live");
            el.classList.remove("is-today");
            return true;
        }

        if (!render()) return true;

        // Recomputed once an hour: the page can be left open across midnight.
        // Anything more often is pointless for a counter in whole days.
        setInterval(render, 3600000);
        return true;
    }

    // The script is meant for the end of <body>, but does not rely on it: if
    // it is loaded earlier — in <head>, with defer, or dynamically — the
    // element does not exist yet and querySelector returns null. So it tries
    // again on DOMContentLoaded, on load, and a few times at short intervals,
    // rather than dying quietly at the first attempt.
    function boot() {
        if (initCountdown()) return;

        var tries = 0;
        var timer = setInterval(function () {
            tries++;
            if (initCountdown() || tries > 20) clearInterval(timer);
        }, 100);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", boot);
    } else {
        boot();
    }
    window.addEventListener("load", function () { initCountdown(); syncTopbarHeight(); });
})();
