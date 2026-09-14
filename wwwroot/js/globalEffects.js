/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
// globalEffects.js — the script side of the ambient layer: the scroll
// progress bar (#scroll-progress), the soft cursor glow, and the shield that
// keeps the layer off the banners.
// There is no canvas and no render loop for the background itself — the
// background is entirely CSS; this file only writes a few variables that the
// CSS turns into
// transform/mask.
//
// Deliberate decisions:
// - The bar does NOT follow the scroll one to one. The target (the real
//   position) is read passively from the scroll event, and the displayed value
//   chases it exponentially, one lerp per frame. So the movement is a smooth
//   glide rather than a step on every event — which matters most with a wheel
//   or a trackpad, where each event jumps 100px.
// - The requestAnimationFrame loop runs ONLY while the target and the
//   displayed value differ; once they meet, it stops itself. No idle loop.
// - Only --gfx-progress (0..1) is written, on <html>. The width is a
//   transform: scaleX() in the CSS — a compositor operation, with no layout or
//   paint per frame.
// - Short pages: below the threshold the bar is hidden and leaves the render
//   entirely rather than sitting there empty. The threshold is not 0, to
//   absorb the one or two pixels a mobile address bar adds and removes.
// - This is not a single-page app: the state is recomputed on every load and
//   nothing is persisted.
// - The cursor glow runs only under pointer: fine; on a touch device the
//   listener is never even attached. The background does NOT move — what moves
//   is the soft accent spot (.afx-cursor), whose position is written into
//   --gfx-cx / --gfx-cy and which chases the cursor exponentially, so it reads
//   as something liquid rather than a circle glued to the mouse.
// - Nothing here touches .reveal, the top bar, the mobile menu or the
//   footer.

(function () {
    "use strict";

    var MIN_SCROLLABLE = 40;  // px; below this the page counts as short
    var EASE = 0.14;          // how much of the gap is covered per frame
    var EPSILON = 0.0004;     // below this the value counts as arrived

    // Banners and hero sections carry effects of their OWN (raster, tint,
    // scanlines, canvas), so the ambient layer is cut away above them to avoid
    // stacking two. A future page opts in with a data-gfx-shield attribute on
    // the section and needs no change here; the class names below are for the
    // pages that already exist.
    //
    // The list used to cover three of the eight banners. Each page names its
    // banner with the page's own prefix (.lc- for lecturers, .ic- for ICBI,
    // .sc- for the programme, .at- for attend), so the layer ran over them and
    // was visible on top of the photograph — most obviously on a phone, where
    // the banner fills nearly the whole screen.
    //
    // Hence an explicit list of all of them PLUS the general
    // [class$="-banner"] rule, which also catches future pages named by the
    // same convention.
    var SHIELD_SELECTOR = "[data-gfx-shield], [class$='-banner'], [class*='-banner '], " +
                          ".fq-banner, .tv-banner, .lc-banner, .ic-banner, " +
                          ".sc-banner, .at-banner, .cf-banner, .hero";
    var SHIELD_FADE = 110;    // px of soft transition below the banner

    var reduceMotion = window.matchMedia &&
        window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    // An explicit opt-out for a page or a tool that does not want the layer:
    // <html data-gfx="off">. The CSS hides it; this stops the work as well, so
    // that no listeners are attached at all.
    if (document.documentElement.getAttribute("data-gfx") === "off") return;

    var bar = null;
    var target = 0;
    var shown = 0;
    var rafId = null;

    // The cursor glow: the target and the displayed position, in px.
    // 0.075 rather than 0.12 — the spot lags further behind the cursor, which
    // is what makes it read as part of the background instead of a circle
    // following the mouse.
    var POINTER_EASE = 0.075;
    var hasFinePointer = window.matchMedia &&
        window.matchMedia("(pointer: fine)").matches;
    var pointerTargetX = 0, pointerTargetY = 0;
    var pointerX = 0, pointerY = 0;
    var pointerSeen = false;

    var shields = null;
    var lastShield = -1;

    function resolveShields() {
        if (shields === null) {
            shields = Array.prototype.slice.call(
                document.querySelectorAll(SHIELD_SELECTOR));
        }
        return shields;
    }

    // How much of the top of the viewport a banner currently occupies.
    // The shield applies while the banner is VISIBLE at the top, not only once
    // it has scrolled past the edge: at scroll 0 the banner sits just below the
    // sticky top bar (rect.top > 0), and that is exactly when the doubled
    // effect would be most obvious.
    function updateShield() {
        var list = resolveShields();
        var viewport = window.innerHeight || 0;
        var edge = 0;

        for (var i = 0; i < list.length; i++) {
            var rect = list[i].getBoundingClientRect();
            if (rect.height === 0) continue;                  // a hidden element
            if (rect.bottom <= 0 || rect.top >= viewport) continue; // off screen

            var bottom = rect.bottom > viewport ? viewport : rect.bottom;
            if (bottom > edge) edge = bottom;
        }

        edge = Math.round(edge);
        if (edge === lastShield) return;
        lastShield = edge;

        var rootStyle = document.documentElement.style;
        rootStyle.setProperty("--gfx-shield", edge + "px");
        rootStyle.setProperty("--gfx-shield-fade", (edge > 0 ? SHIELD_FADE : 0) + "px");
    }

    function resolveBar() {
        if (bar === null) {
            bar = document.getElementById("scroll-progress");
        }
        return bar;
    }

    function write(value) {
        document.documentElement.style.setProperty("--gfx-progress", value.toFixed(4));
    }

    function measure() {
        var el = resolveBar();
        if (!el) return false;

        var doc = document.documentElement;
        var scrollable = (doc.scrollHeight || 0) - window.innerHeight;

        if (scrollable <= MIN_SCROLLABLE) {
            el.hidden = true;
            target = shown = 0;
            write(0);
            return false;
        }

        el.hidden = false;

        var offset = window.pageYOffset || doc.scrollTop || 0;
        var ratio = offset / scrollable;
        target = ratio < 0 ? 0 : (ratio > 1 ? 1 : ratio);
        return true;
    }

    function step() {
        var diff = target - shown;
        var busy = false;

        if (Math.abs(diff) < EPSILON) {
            shown = target;
        } else {
            shown += diff * EASE;
            busy = true;
        }
        write(shown);

        // The coordinates are written even under prefers-reduced-motion.
        // They used to move the glow alone, so stopping them was right. The
        // DRAWING of the interactive presets (lantern, prism, sonar, caustic,
        // spine) now depends on them, and without them the background freezes
        // in its fallback position. The glow itself and the animations do still
        // stop — that part is in the CSS.
        if (hasFinePointer && pointerSeen) {
            var dx = pointerTargetX - pointerX;
            var dy = pointerTargetY - pointerY;

            if (Math.abs(dx) < 0.2 && Math.abs(dy) < 0.2) {
                pointerX = pointerTargetX;
                pointerY = pointerTargetY;
            } else {
                pointerX += dx * POINTER_EASE;
                pointerY += dy * POINTER_EASE;
                busy = true;
            }

            var rootStyle = document.documentElement.style;
            rootStyle.setProperty("--gfx-cx", Math.round(pointerX) + "px");
            rootStyle.setProperty("--gfx-cy", Math.round(pointerY) + "px");
        }

        rafId = busy ? window.requestAnimationFrame(step) : null;
    }

    function start() {
        if (rafId === null) {
            rafId = window.requestAnimationFrame(step);
        }
    }

    function onScroll() {
        updateShield();

        if (!measure()) return;

        if (reduceMotion) {
            shown = target;
            write(shown);
            return;
        }

        start();
    }

    window.addEventListener("scroll", onScroll, { passive: true });
    window.addEventListener("resize", onScroll, { passive: true });
    window.addEventListener("orientationchange", onScroll, { passive: true });

    if (hasFinePointer) {
        window.addEventListener("pointermove", function (e) {
            pointerTargetX = e.clientX;
            pointerTargetY = e.clientY;

            if (!pointerSeen) {
                // On the first movement the glow appears already in place
                // rather than flying in from the corner of the screen.
                pointerSeen = true;
                pointerX = pointerTargetX;
                pointerY = pointerTargetY;
                document.documentElement.style.setProperty("--gfx-cursor", "1");
            }

            start();
        }, { passive: true });

        // The cursor leaves the page: the glow fades out through a CSS opacity
        // transition rather than freezing at the edge.
        document.addEventListener("mouseleave", function () {
            document.documentElement.style.setProperty("--gfx-cursor", "0");
        }, { passive: true });

        document.addEventListener("mouseenter", function () {
            if (pointerSeen) {
                document.documentElement.style.setProperty("--gfx-cursor", "1");
            }
        }, { passive: true });
    }

    // The content can change height after the first frame — fonts, images, an
    // opened FAQ accordion, the .reveal transitions — so a ResizeObserver
    // recomputes the threshold without polling for it.
    if (typeof ResizeObserver === "function") {
        try {
            new ResizeObserver(onScroll).observe(document.documentElement);
        } catch (e) {
            /* an older browser: the scroll and resize listeners are enough */
        }
    }

    function init() {
        shields = null;      // rediscovered once the DOM has fully loaded
        lastShield = -1;
        updateShield();

        if (measure()) {
            shown = target; // the initial state is not animated
            write(shown);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }

    window.addEventListener("load", init);
})();
