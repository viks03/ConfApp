// particlesBg.js — оригиналната "галактика" particles концепция
// (точки с мека радиална светлина + свързващи линии по краищата на
// екрана), върната обратно вместо hex rain/circuit traces версиите.
//
// НОВ ФИКС в тази версия: "гличаво/рестартира се при скрол на мобилен".
// Причина: мобилните браузъри (особено Safari) динамично показват/
// скриват адрес бара по време на скрол, което реално сменя
// window.innerHeight — това гърми обикновен "resize" event, ВЪПРЕКИ
// че никой истински resize/завъртане не се е случило. Старият код
// правеше пълен rebuild на цялата частична система (нови позиции,
// нови ленти) при ВСЕКИ resize event — оттам грозното "рестартиране"
// на всеки скрол.
//
// Фиксът: пазим последната позната ШИРИНА. Пълен rebuild (нови точки,
// нови ленти) става само ако ШИРИНАТА реално се е сменила (истинско
// завъртане на телефона или преоразмеряване на десктоп прозорец).
// Ако само височината се е сменила (адрес бар се крие/показва по време
// на скрол) — просто преоразмеряваме canvas буфера, БЕЗ да пипаме
// нито една съществуваща точка. Нула видим "рестарт".
//
// Останалата, вече доказана performance основа си остава: position:
// fixed + GPU layer promotion (viewport-размер canvas, transform:
// translateZ(0) в CSS), pre-rendered "glow" спрайт вместо
// createRadialGradient на всеки кадър, mobile/desktop конфигурации,
// frame-rate throttle, pause при скрит таб, prefers-reduced-motion.
//
// Няма външни зависимости — чист canvas + requestAnimationFrame.

(function () {
    "use strict";

    var canvas = document.getElementById("particles-bg");
    if (!canvas) return;

    var ctx = canvas.getContext("2d");
    if (!ctx) return;

    if (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        return;
    }

    var hasFinePointer = window.matchMedia && window.matchMedia("(pointer: fine)").matches;

    var accentRed = getComputedStyle(document.documentElement).getPropertyValue("--accent-red").trim() || "#8b0000";
    function hexToRgb(hex) {
        var m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
        return m ? (parseInt(m[1], 16) + "," + parseInt(m[2], 16) + "," + parseInt(m[3], 16)) : "139,0,0";
    }
    var accentRgb = hexToRgb(accentRed);
    var dotRgb = hexToRgb("#ff4d4d");

    // ---- Pre-rendered glow спрайт (веднъж, не всеки кадър) ----
    var SPRITE_SIZE = 48;
    var spriteCanvas = document.createElement("canvas");
    spriteCanvas.width = SPRITE_SIZE;
    spriteCanvas.height = SPRITE_SIZE;
    var spriteCtx = spriteCanvas.getContext("2d");
    (function buildSprite() {
        var c = SPRITE_SIZE / 2;
        var grad = spriteCtx.createRadialGradient(c, c, 0, c, c, c);
        grad.addColorStop(0, "rgba(255,235,225,1)");
        grad.addColorStop(0.28, "rgba(" + dotRgb + ",0.85)");
        grad.addColorStop(0.7, "rgba(" + accentRgb + ",0.28)");
        grad.addColorStop(1, "rgba(" + accentRgb + ",0)");
        spriteCtx.fillStyle = grad;
        spriteCtx.beginPath();
        spriteCtx.arc(c, c, c, 0, Math.PI * 2);
        spriteCtx.fill();
    })();

    var width = 0;
    var height = 0;
    var dpr = 1;

    var COMPACT_BREAKPOINT = 760;
    var isCompact = false;
    var cfg = null;

    function buildConfig() {
        if (isCompact) {
            return {
                linkDistance: 65,
                density: 2600,       // много по-плътно от преди (беше 5200)
                maxPerLane: 48,      // таван почти двойно по-висок (беше 26)
                linkAlphaBase: 0.5,
                twinkleFloor: 0.85,  // по-рядко потъмняват — по-константно наситено
                fadePower: 0.45,     // изяжда по-малко от лентата навътре — остава наситено по-дълго
                speedMul: 0.9,
                sizeMul: 1.35,       // по-едри точки — на телефон се гледа отблизо, дребните бяха незабележими
                dprCap: 1.75,
                targetFps: 30
            };
        }
        return {
            linkDistance: 100,
            density: 5000,
            maxPerLane: 90,
            linkAlphaBase: 0.4,
            twinkleFloor: 0.72,
            fadePower: 0.68,
            speedMul: 1.2,
            sizeMul: 1,
            dprCap: 2,
            targetFps: 60
        };
    }

    var leftLane = null;
    var rightLane = null;

    var particles = [];
    var mouse = { x: -9999, y: -9999, active: false };
    var time = 0;

    var MOUSE_EFFECT_DISTANCE = 130;
    var REPULSE_STRENGTH = 0.8;
    var FADE_SKIP_THRESHOLD = 0.03;

    function measureLanes() {
        if (isCompact) {
            var band = Math.max(70, Math.min(width * 0.24, 165));
            leftLane = { min: 0, max: band };
            rightLane = { min: width - band, max: width };
            return;
        }

        var content = document.querySelector(".schedule-container") || document.querySelector("main");
        var contentLeft = width * 0.5;
        var contentRight = width * 0.5;
        if (content) {
            var rect = content.getBoundingClientRect();
            if (rect.width > 0) {
                contentLeft = rect.left;
                contentRight = rect.right;
            }
        }

        var leftBand = contentLeft * 0.93;
        var rightBand = (width - contentRight) * 0.93;

        leftLane = leftBand >= 60 ? { min: 0, max: leftBand } : null;
        rightLane = rightBand >= 60 ? { min: width - rightBand, max: width } : null;
    }

    function laneParticleCount(lane) {
        if (!lane) return 0;
        var area = (lane.max - lane.min) * height;
        var count = Math.round(area / cfg.density);
        return Math.max(10, Math.min(count, cfg.maxPerLane));
    }

    // Пълен rebuild — извиква се САМО при истинска промяна на ширината
    // (виж onWindowResize по-долу), не при всяка мобилна address-bar
    // флуктуация на височината.
    function rebuild() {
        width = window.innerWidth;
        height = window.innerHeight;
        isCompact = width <= COMPACT_BREAKPOINT;
        dpr = Math.min(window.devicePixelRatio || 1, 2);

        if (isCompact) {
            // На този етап ефектът е ИЗКЛЮЧЕН на мобилни устройства —
            // ще имплементираме нещо отделно за тях по-късно. Спираме
            // цикъла, чистим всичко и излизаме — нула рисуване, нула
            // работа на всеки кадър на телефон.
            stopLoop();
            particles = [];
            applyCanvasSize();
            ctx.clearRect(0, 0, width, height);
            return;
        }

        cfg = buildConfig();
        dpr = Math.min(window.devicePixelRatio || 1, cfg.dprCap);

        applyCanvasSize();
        measureLanes();
        seedParticles();
        startLoop();
    }

    // Само преоразмерява canvas буфера, БЕЗ да пипа съществуващите
    // частици — за случая "само височината се е сменила" (мобилен
    // адрес бар по време на скрол).
    function resizeCanvasOnly() {
        height = window.innerHeight;
        applyCanvasSize();
    }

    function applyCanvasSize() {
        canvas.width = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);
        canvas.style.width = width + "px";
        canvas.style.height = height + "px";
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    function makeParticle(lane, isLeft) {
        var bandWidth = lane.max - lane.min;
        var biasedT = Math.pow(Math.random(), 1.6);
        var x = isLeft ? (lane.min + biasedT * bandWidth) : (lane.max - biasedT * bandWidth);

        var isNear = Math.random() < 0.32;
        var speedBase = (isNear ? 1.35 : 0.75) * cfg.speedMul;

        return {
            x: x,
            y: Math.random() * height,
            vx: (Math.random() - 0.5) * speedBase,
            vy: (Math.random() - 0.5) * speedBase,
            r: (isNear ? (2.3 + Math.random() * 1.7) : (1.2 + Math.random() * 1.1)) * cfg.sizeMul,
            baseAlpha: isNear ? 1 : 0.68,
            xMin: lane.min,
            xMax: lane.max,
            edgeX: isLeft ? 0 : width,
            bandWidth: bandWidth,
            twinklePhase: Math.random() * Math.PI * 2,
            twinkleSpeed: 0.015 + Math.random() * 0.04,
            fade: 1,
            glow: 0
        };
    }

    function seedParticles() {
        particles = [];
        var i;
        if (leftLane) for (i = 0; i < laneParticleCount(leftLane); i++) particles.push(makeParticle(leftLane, true));
        if (rightLane) for (i = 0; i < laneParticleCount(rightLane); i++) particles.push(makeParticle(rightLane, false));
    }

    function smoothstep(t) {
        return t * t * (3 - 2 * t);
    }

    function stepFrame() {
        time += 1;
        ctx.clearRect(0, 0, width, height);

        var i, p;
        for (i = 0; i < particles.length; i++) {
            p = particles[i];

            p.x += p.vx;
            p.y += p.vy;

            if (p.x <= p.xMin || p.x >= p.xMax) p.vx *= -1;
            if (p.y <= 0 || p.y >= height) p.vy *= -1;
            p.x = Math.max(p.xMin, Math.min(p.xMax, p.x));
            p.y = Math.max(0, Math.min(height, p.y));

            var distFromEdge = Math.abs(p.x - p.edgeX);
            var edgeT = 1 - Math.min(distFromEdge / p.bandWidth, 1);
            p.fade = Math.pow(smoothstep(edgeT), cfg.fadePower);

            p.glow = 0;
            if (hasFinePointer && mouse.active) {
                var dx = p.x - mouse.x;
                var dy = p.y - mouse.y;
                var dist = Math.sqrt(dx * dx + dy * dy);
                if (dist < MOUSE_EFFECT_DISTANCE && dist > 0.01) {
                    var proximity = 1 - dist / MOUSE_EFFECT_DISTANCE;
                    p.glow = proximity;
                    var force = proximity * REPULSE_STRENGTH;
                    p.x += (dx / dist) * force;
                    p.y += (dy / dist) * force;
                    p.x = Math.max(p.xMin, Math.min(p.xMax, p.x));
                }
            }
        }

        var a, b, pa, pb;
        for (a = 0; a < particles.length; a++) {
            pa = particles[a];
            if (pa.fade < FADE_SKIP_THRESHOLD) continue;
            for (b = a + 1; b < particles.length; b++) {
                pb = particles[b];
                if (pa.xMin !== pb.xMin) continue;
                if (pb.fade < FADE_SKIP_THRESHOLD) continue;
                var ddx = pa.x - pb.x;
                var ddy = pa.y - pb.y;
                var d = Math.sqrt(ddx * ddx + ddy * ddy);
                if (d < cfg.linkDistance) {
                    var linkFade = (pa.fade + pb.fade) * 0.5;
                    var alpha = (1 - d / cfg.linkDistance) * cfg.linkAlphaBase * linkFade;
                    if (alpha < 0.015) continue;
                    ctx.strokeStyle = "rgba(" + accentRgb + "," + alpha.toFixed(3) + ")";
                    ctx.lineWidth = 1;
                    ctx.beginPath();
                    ctx.moveTo(pa.x, pa.y);
                    ctx.lineTo(pb.x, pb.y);
                    ctx.stroke();
                }
            }
        }

        if (hasFinePointer && mouse.active) {
            for (i = 0; i < particles.length; i++) {
                p = particles[i];
                if (p.glow > 0) {
                    var mAlpha = p.glow * 0.55 * Math.max(p.fade, 0.3);
                    ctx.strokeStyle = "rgba(" + dotRgb + "," + mAlpha.toFixed(3) + ")";
                    ctx.lineWidth = 1;
                    ctx.beginPath();
                    ctx.moveTo(mouse.x, mouse.y);
                    ctx.lineTo(p.x, p.y);
                    ctx.stroke();
                }
            }
        }

        for (i = 0; i < particles.length; i++) {
            p = particles[i];
            if (p.fade < FADE_SKIP_THRESHOLD && p.glow === 0) continue;

            var twinkle = cfg.twinkleFloor + (1 - cfg.twinkleFloor) * Math.sin(time * p.twinkleSpeed + p.twinklePhase);
            var alpha = Math.min(1, p.fade * twinkle * p.baseAlpha + p.glow * 0.5);
            if (alpha < 0.02) continue;

            var size = (p.r + p.glow * 1.4) * 3.2;
            var half = size / 2;
            ctx.globalAlpha = alpha;
            ctx.drawImage(
                spriteCanvas,
                Math.round(p.x - half),
                Math.round(p.y - half),
                Math.round(size),
                Math.round(size)
            );
        }
        ctx.globalAlpha = 1;
    }

    // --- Контрол на цикъла: guard срещу паралелни rAF вериги + таван
    //     на честотата на кадрите на мобилен (30fps вместо 60). ---
    var rafId = null;
    var lastFrameTime = 0;

    function loop(now) {
        rafId = requestAnimationFrame(loop);

        var targetInterval = 1000 / (cfg ? cfg.targetFps : 60);
        if (now - lastFrameTime < targetInterval) return;
        lastFrameTime = now;

        stepFrame();
    }

    function startLoop() {
        if (rafId !== null) return;
        lastFrameTime = 0;
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
        } else if (!isCompact) {
            startLoop();
        }
    });

    // --- Resize handling с защита срещу мобилния "address bar" бъг ---
    var lastKnownWidth = window.innerWidth;
    var resizeTimeout;
    window.addEventListener("resize", function () {
        var currentWidth = window.innerWidth;

        if (currentWidth === lastKnownWidth) {
            // Само височината се е сменила — почти сигурно мобилен адрес
            // бар, който се крие/показва по време на скрол, не истински
            // resize. Само преоразмеряваме canvas буфера, БЕЗ да
            // пресъздаваме нито една точка — нула видим "рестарт".
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(resizeCanvasOnly, 100);
            return;
        }

        // Реална промяна на ширината (завъртане на телефона, реално
        // преоразмеряване на десктоп прозорец) — тук е ОК да
        // пресъздадем всичко, защото лентите/броят точки зависят от
        // ширината.
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
        document.addEventListener("mouseleave", function () {
            mouse.active = false;
        }, { passive: true });
    }

    rebuild();
    window.addEventListener("load", function () {
        setTimeout(rebuild, 200);
    });
})();