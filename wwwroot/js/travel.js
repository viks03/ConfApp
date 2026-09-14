/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
/* travel.js — the reveal on scroll and the active section in the rail.
   The script only adds an effect: without it the page is fully visible and
   usable. */
(function () {
    window.ConfApp.revealOnScroll();

    // The active link is worked out from the scroll position: the lowest
    // section whose top has passed the line under the sticky rail.
    var links = Array.prototype.slice.call(document.querySelectorAll("[data-nav]"));
    if (!links.length) return;

    // Reads --navbar-height from :root — the same variable travelStyle.css
    // uses for the sticky position — falling back to 64px.
    function navbarHeight() {
        var v = getComputedStyle(document.documentElement).getPropertyValue("--navbar-height");
        var n = parseFloat(v);
        return isNaN(n) ? 64 : n;
    }

    var raf = 0;
    function sync() {
        raf = 0;
        // The 140 was calibrated for the old position, with tv-nav at top: 0.
        // The rail now sits below the main navbar, so the threshold moves down
        // by exactly that bar's height.
        var threshold = 140 + navbarHeight();
        var active = links[0].getAttribute("data-nav");
        links.forEach(function (l) {
            var s = document.getElementById(l.getAttribute("data-nav"));
            if (s && s.getBoundingClientRect().top <= threshold) active = l.getAttribute("data-nav");
        });
        links.forEach(function (l) {
            l.classList.toggle("is-active", l.getAttribute("data-nav") === active);
        });
    }
    function queue() { if (!raf) raf = requestAnimationFrame(sync); }

    window.addEventListener("scroll", queue, { passive: true });
    window.addEventListener("resize", queue);
    sync();
})();
