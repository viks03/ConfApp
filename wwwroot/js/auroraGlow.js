// auroraGlow.js — споделен, site-wide фонов ефект: течна, светеща
// "aurora" композиция, изградена от няколко меки, замъглени петна
// светлина в акцентния цвят, които бавно и органично дрейфват из
// viewport-а.
//
// Защо изглежда различно от предните два опита: тук изобщо няма
// дискретни обекти (възли, линии, шестоъгълници) — само светлина.
// Всяко петно е pre-rendered "glow" спрайт (createRadialGradient
// САМО веднъж при зареждане, не всеки кадър), нарисуван всеки кадър
// през globalCompositeOperation = 'lighter' — където две петна се
// препокриват, светлината се СЪБИРА (не просто рисува отгоре), затова
// пресечните зони светят по-богато и плътно, вместо да изглеждат като
// два отделни кръга. Точно тази техника дава "течно стъкло" усещането
// вместо плоски CSS blur петна.
//
// Движението е сбор от два синусоида на различна честота/скорост на
// петно (layered motion) — органичен дрейф, който практически никога
// не се усеща като повтарящ се цикъл, но остава изцяло детерминиран
// (без физика/random walk), затова е гарантирано гладко. Всяко петно
// освен това бавно "диша" (лек пулс в радиуса).
//
// Много деликатен mouse parallax (само desktop, pointer: fine) —
// цялата композиция леко се измества според позицията на курсора,
// с експоненциално сглаждане (lerp към целта всеки кадър, никога
// директен скок), затова усещането е плавно "living", не отзивчиво/
// дразнещо. По-близките (по-големи/по-ярки) петна се движат малко
// повече от по-далечните — лек depth ефект.
//
// Технически основа — идентична на доказаната от particlesBg.js/
// chainFlow.js:
// - position: fixed canvas + GPU layer promotion (виж CSS-а)
// - Resize handling с защита срещу мобилния "address bar" бъг
// - Pause при скрит tab, prefers-reduced-motion се уважава
// - DPR-aware canvas sizing с таван за по-слаби устройства

(function () {
    "use strict";

    var canvas = document.getElementById("site-particles-bg");
    if (!canvas) return;

    var ctx = canvas.getContext("2d");
    if (!ctx) return;

    if (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        return;
    }

    var hasFinePointer = window.matchMedia && window.matchMedia("(pointer: fine)").matches;

    var accentColor = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#FF3636";
    function hexToRgb(hex) {
        var m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
        return m ? (parseInt(m[1], 16) + "," + parseInt(m[2], 16) + "," + parseInt(m[3], 16)) : "255,54,54";
    }
    var accentRgb = hexToRgb(accentColor);

    // ---- Pre-rendered soft glow спрайт (веднъж, не всеки кадър) ----
    var SPRITE_SIZE = 512;
    var spriteCanvas = document.createElement("canvas");
    spriteCanvas.width = SPRITE_SIZE;
    spriteCanvas.height = SPRITE_SIZE;
    var spriteCtx = spriteCanvas.getContext("2d");
    (function buildSprite() {
        var c = SPRITE_SIZE / 2;
        var grad = spriteCtx.createRadialGradient(c, c, 0, c, c, c);
        grad.addColorStop(0, "rgba(" + accentRgb + ",1)");
        grad.addColorStop(0.35, "rgba(" + accentRgb + ",0.55)");
        grad.addColorStop(0.7, "rgba(" + accentRgb + ",0.16)");
        grad.addColorStop(1, "rgba(" + accentRgb + ",0)");
        spriteCtx.fillStyle = grad;
        spriteCtx.fillRect(0, 0, SPRITE_SIZE, SPRITE_SIZE);
    })();

    // ---- Много фина, статична "film grain" текстура, за да не бандира
    //      градиентът върху голямо тъмно платно — стандартен трик за
    //      кинематографично усещане на dark UI, почти незабележим
    //      сам по себе си. Pre-rendered веднъж като tile pattern. ----
    var GRAIN_TILE = 96;
    var grainCanvas = document.createElement("canvas");
    grainCanvas.width = GRAIN_TILE;
    grainCanvas.height = GRAIN_TILE;
    var grainCtx = grainCanvas.getContext("2d");
    (function buildGrain() {
        var imgData = grainCtx.createImageData(GRAIN_TILE, GRAIN_TILE);
        var d = imgData.data;
        for (var i = 0; i < d.length; i += 4) {
            var v = Math.random() * 255;
            d[i] = v; d[i + 1] = v; d[i + 2] = v;
            d[i + 3] = Math.random() * 14; // много ниска, произволна плътност на пиксел
        }
        grainCtx.putImageData(imgData, 0, 0);
    })();
    var grainPattern = null; // построява се след ctx е наличен (виж rebuild)

    var width = 0;
    var height = 0;
    var dpr = 1;

    var COMPACT_BREAKPOINT = 760;
    var isCompact = false;

    // Базови позиции (като дял от viewport-а) + собствена честота/фаза/
    // амплитуда на всяко петно — комбинация от "голям, мек, бавен
    // ambient пласт" и "по-малък, по-ярък, по-жив accent пласт" за
    // усещане за дълбочина.
    var BLOB_DEFS = [
        { ox: 0.10, oy: 0.16, r: 340, alpha: 0.11, depth: 0.6, f1: 0.00033, f2: 0.00019, a1: 130, a2: 70, breathe: 0.00042 },
        { ox: 0.90, oy: 0.52, r: 300, alpha: 0.10, depth: 0.7, f1: 0.00027, f2: 0.00021, a1: 120, a2: 85, breathe: 0.00036 },
        { ox: 0.28, oy: 0.88, r: 270, alpha: 0.09, depth: 0.55, f1: 0.00030, f2: 0.00017, a1: 110, a2: 75, breathe: 0.00039 },
        { ox: 0.72, oy: 0.12, r: 165, alpha: 0.16, depth: 1.1, f1: 0.00046, f2: 0.00028, a1: 85, a2: 55, breathe: 0.00055 },
        { ox: 0.18, oy: 0.60, r: 145, alpha: 0.15, depth: 1.2, f1: 0.00040, f2: 0.00025, a1: 75, a2: 50, breathe: 0.00050 }
    ];
    var blobs = [];

    var mouseTargetX = 0, mouseTargetY = 0;
    var mouseX = 0, mouseY = 0;
    var mouseActive = false;
    var time = 0;

    function buildBlobs() {
        blobs = [];
        var i, def;
        for (i = 0; i < BLOB_DEFS.length; i++) {
            def = BLOB_DEFS[i];
            blobs.push({
                baseX: def.ox * width,
                baseY: def.oy * height,
                r: isCompact ? def.r * 0.72 : def.r,
                alpha: def.alpha,
                depth: def.depth,
                f1: def.f1, f2: def.f2,
                a1: def.a1, a2: def.a2,
                breathe: def.breathe,
                phase: Math.random() * Math.PI * 2,
                phase2: Math.random() * Math.PI * 2,
                breathePhase: Math.random() * Math.PI * 2
            });
        }
    }

    function rebuild() {
        width = window.innerWidth;
        height = window.innerHeight;
        isCompact = width <= COMPACT_BREAKPOINT;
        dpr = Math.min(window.devicePixelRatio || 1, isCompact ? 1.75 : 2);

        applyCanvasSize();
        buildBlobs();

        if (!grainPattern) {
            grainPattern = ctx.createPattern(grainCanvas, "repeat");
        }

        mouseX = mouseTargetX = width / 2;
        mouseY = mouseTargetY = height / 2;
    }

    function resizeCanvasOnly() {
        height = window.innerHeight;
        applyCanvasSize();
        buildBlobs();
    }

    function applyCanvasSize() {
        canvas.width = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);
        canvas.style.width = width + "px";
        canvas.style.height = height + "px";
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    function stepFrame() {
        time += 16.7; // ~фиксирана крачка спрямо реално време, независима от refresh rate-а
        ctx.clearRect(0, 0, width, height);

        // Плавно "следване" на курсора — експоненциално сглаждане, не
        // директен скок, затова паралаксът винаги се усеща като течен.
        if (hasFinePointer && mouseActive) {
            mouseX += (mouseTargetX - mouseX) * 0.025;
            mouseY += (mouseTargetY - mouseY) * 0.025;
        }
        var parX = (mouseX - width / 2) * 0.03;
        var parY = (mouseY - height / 2) * 0.03;

        ctx.globalCompositeOperation = "lighter";

        var i, b, x, y, r, size;
        for (i = 0; i < blobs.length; i++) {
            b = blobs[i];

            x = b.baseX
                + b.a1 * Math.sin(time * b.f1 + b.phase)
                + b.a2 * Math.sin(time * b.f2 * 1.7 + b.phase2)
                + parX * b.depth;
            y = b.baseY
                + b.a1 * Math.cos(time * b.f1 * 0.8 + b.phase)
                + b.a2 * Math.cos(time * b.f2 * 1.4 + b.phase2)
                + parY * b.depth;

            r = b.r * (1 + Math.sin(time * b.breathe + b.breathePhase) * 0.12);
            size = r * 2;

            ctx.globalAlpha = b.alpha;
            ctx.drawImage(spriteCanvas, x - r, y - r, size, size);
        }

        ctx.globalCompositeOperation = "source-over";
        ctx.globalAlpha = 1;

        if (grainPattern) {
            ctx.fillStyle = grainPattern;
            ctx.fillRect(0, 0, width, height);
        }
    }

    var rafId = null;

    function loop() {
        rafId = requestAnimationFrame(loop);
        stepFrame();
    }

    function startLoop() {
        if (rafId !== null) return;
        rafId = requestAnimationFrame(loop);
    }

    function stopLoop() {
        if (rafId !== null) {
            cancelAnimationFrame(rafId);
            rafId = null;
        }
    }

    document.addEventListener("visibilitychange", function () {
        if (document.hidden) {
            stopLoop();
        } else {
            startLoop();
        }
    });

    var lastKnownWidth = window.innerWidth;
    var resizeTimeout;
    window.addEventListener("resize", function () {
        var currentWidth = window.innerWidth;

        if (currentWidth === lastKnownWidth) {
            // Само височината се е сменила — мобилен адрес бар.
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
            mouseTargetX = e.clientX;
            mouseTargetY = e.clientY;
            mouseActive = true;
        }, { passive: true });
        window.addEventListener("pointerout", function () {
            mouseActive = false;
        }, { passive: true });
        document.addEventListener("mouseleave", function () {
            mouseActive = false;
        }, { passive: true });
    }

    rebuild();
    window.addEventListener("load", function () {
        setTimeout(rebuild, 200);
    });

    if (!document.hidden) {
        startLoop();
    }
})();
