// siteAmbient.js — "liquid" fluid background, mouse-reactive.
// Three things make this read as FLUID rather than "a few soft circles
// drifting" (the previous version):
//   1. Trail persistence — each frame partially fades the previous frame
//      instead of hard-clearing it (a low-alpha dark overlay), so motion
//      smears into soft trailing streaks, like ink/liquid, not blobs that
//      instantly reset position every repaint.
//   2. Screen blend mode where blobs overlap — colors genuinely MERGE and
//      brighten at intersections instead of just stacking translucent
//      circles, which is what actually reads as "liquid light" rather
//      than "several pngs on top of each other."
//   3. Real mouse attraction — blobs bend their organic drift path TOWARD
//      the cursor (gently, with inertia), so the whole field visibly
//      responds to the visitor instead of just ambient movement + a
//      separate, disconnected spotlight overlay.
//
// Performance foundation unchanged from before: fixed position + GPU
// layer (iOS scroll-jank fix), pre-rendered sprites, visibility pause,
// prefers-reduced-motion respected, resize handling.

(function () {
    "use strict";

    var canvas = document.getElementById("site-particles-bg");
    if (!canvas) return;

    var ctx = canvas.getContext("2d");
    if (!ctx) return;

    var reduceMotion = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    var hasFinePointer = window.matchMedia && window.matchMedia("(pointer: fine)").matches;

    var root = getComputedStyle(document.documentElement);
    var accentHex = root.getPropertyValue("--accent").trim() || "#FF3636";

    function hexToRgb(hex) {
        var m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
        return m ? (parseInt(m[1], 16) + "," + parseInt(m[2], 16) + "," + parseInt(m[3], 16)) : "255,54,54";
    }
    var accentRgb = hexToRgb(accentHex);
    var BLOB_COLORS = [accentRgb, accentRgb, "255,150,110"];

    var width = 0, height = 0, dpr = 1;
    var DPR_CAP = 1.6; // trail-fade fillRect every frame is pricier than a plain clearRect — cap a bit tighter

    // ---- Pre-rendered gradient sprites (once, not per frame) ----
    var SPRITE_SIZE = 900;
    function buildBlobSprite(rgb) {
        var c = document.createElement("canvas");
        c.width = SPRITE_SIZE;
        c.height = SPRITE_SIZE;
        var sctx = c.getContext("2d");
        var r = SPRITE_SIZE / 2;
        var grad = sctx.createRadialGradient(r, r, 0, r, r, r);
        grad.addColorStop(0, "rgba(" + rgb + ",0.30)");
        grad.addColorStop(0.45, "rgba(" + rgb + ",0.14)");
        grad.addColorStop(1, "rgba(" + rgb + ",0)");
        sctx.fillStyle = grad;
        sctx.beginPath();
        sctx.arc(r, r, r, 0, Math.PI * 2);
        sctx.fill();
        return c;
    }
    var blobSprites = BLOB_COLORS.map(buildBlobSprite);

    var blobs = [];
    var mouse = { x: -9999, y: -9999, active: false };
    var mouseSmoothed = { x: -9999, y: -9999 };

    function buildBlobs() {
        var isCompact = width <= 760;
        var baseSize = isCompact ? Math.min(width, height) * 0.9 : Math.min(width, height) * 0.62;

        blobs = [
            { sprite: blobSprites[0], size: baseSize * 1.15, baseX: width * 0.18, baseY: height * 0.28, ampX: width * 0.14, ampY: height * 0.1, freqX: 0.00011, freqY: 0.00016, phase: 0, x: width * 0.18, y: height * 0.28 },
            { sprite: blobSprites[1], size: baseSize * 0.95, baseX: width * 0.82, baseY: height * 0.62, ampX: width * 0.12, ampY: height * 0.13, freqX: 0.00014, freqY: 0.00010, phase: 2.1, x: width * 0.82, y: height * 0.62 },
            { sprite: blobSprites[2], size: baseSize * 0.8,  baseX: width * 0.5,  baseY: height * 0.85, ampX: width * 0.16, ampY: height * 0.08, freqX: 0.00009, freqY: 0.00013, phase: 4.3, x: width * 0.5,  y: height * 0.85 }
        ];
    }

    function applyCanvasSize() {
        canvas.width = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);
        canvas.style.width = width + "px";
        canvas.style.height = height + "px";
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    function rebuild() {
        width = window.innerWidth;
        height = window.innerHeight;
        dpr = Math.min(window.devicePixelRatio || 1, DPR_CAP);
        applyCanvasSize();
        buildBlobs();
    }

    function resizeCanvasOnly() {
        height = window.innerHeight;
        applyCanvasSize();
        buildBlobs();
    }

    function renderFrame(t) {
        // clearRect, not a repeated low-alpha fill — the latter mathematically
        // converges toward fully opaque over many frames (each frame's alpha
        // compositing pushes it closer to 1.0), which would eventually hide
        // the page's own background gradient entirely. The elastic easing on
        // blob movement below already gives real fluid inertia/lag without
        // needing an actual pixel-persistence trail.
        ctx.clearRect(0, 0, width, height);

        // Mouse position eases toward its real target (inertia) — a
        // literal snap-to-cursor would feel mechanical, not fluid.
        if (hasFinePointer && mouse.active) {
            mouseSmoothed.x += (mouse.x - mouseSmoothed.x) * 0.03;
            mouseSmoothed.y += (mouse.y - mouseSmoothed.y) * 0.03;
        }

        ctx.globalCompositeOperation = "screen";
        for (var i = 0; i < blobs.length; i++) {
            var b = blobs[i];
            var driftX = b.baseX + Math.sin(t * b.freqX + b.phase) * b.ampX;
            var driftY = b.baseY + Math.cos(t * b.freqY + b.phase) * b.ampY;

            var targetX = driftX;
            var targetY = driftY;
            if (hasFinePointer && mouse.active) {
                // Gentle pull toward the cursor — blended with the blob's
                // own organic drift, never fully overriding it, so the
                // field still feels alive even while responding to you.
                targetX = driftX + (mouseSmoothed.x - driftX) * 0.18;
                targetY = driftY + (mouseSmoothed.y - driftY) * 0.18;
            }
            b.x += (targetX - b.x) * 0.045;
            b.y += (targetY - b.y) * 0.045;

            var half = b.size / 2;
            ctx.drawImage(b.sprite, Math.round(b.x - half), Math.round(b.y - half), Math.round(b.size), Math.round(b.size));
        }
        ctx.globalCompositeOperation = "source-over";
    }

    var rafId = null;
    var startTime = null;

    function loop(now) {
        rafId = requestAnimationFrame(loop);
        if (startTime === null) startTime = now;
        renderFrame(now - startTime);
    }

    function startLoop() {
        if (rafId !== null || reduceMotion) return;
        rafId = requestAnimationFrame(loop);
    }

    function stopLoop() {
        if (rafId !== null) {
            cancelAnimationFrame(rafId);
            rafId = null;
        }
    }

    document.addEventListener("visibilitychange", function () {
        if (document.hidden) stopLoop();
        else startLoop();
    });

    var lastKnownWidth = window.innerWidth;
    var resizeTimeout;
    window.addEventListener("resize", function () {
        var currentWidth = window.innerWidth;
        if (currentWidth === lastKnownWidth) {
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(resizeCanvasOnly, 100);
            return;
        }
        lastKnownWidth = currentWidth;
        clearTimeout(resizeTimeout);
        resizeTimeout = setTimeout(rebuild, 150);
    }, { passive: true });

    if (hasFinePointer) {
        window.addEventListener("pointermove", function (e) {
            mouse.x = e.clientX;
            mouse.y = e.clientY;
            mouse.active = true;
        }, { passive: true });
        window.addEventListener("pointerout", function () {
            mouse.active = false;
        }, { passive: true });
    }

    rebuild();
    mouseSmoothed.x = width / 2;
    mouseSmoothed.y = height / 2;

    if (reduceMotion) {
        renderFrame(0);
    } else if (!document.hidden) {
        startLoop();
    }
})();