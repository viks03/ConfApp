// siteParticles.js — споделен, site-wide "network pulse" фон.
//
// Пренаписан визуален език върху същата, доказана инженерна основа:
// вместо генеричен "galaxy" starfield, ефектът сега прилича на мрежа
// от свързани възли, по която от време на време пътуват малки светли
// "пакети" от един възел към друг — визуална метафора, която реално
// пасва на конференция за blockchain, вместо произволни премигващи
// точки. Няколко от възлите са леко по-големи "хъбове" и периодично
// пускат разширяващ се "ping" пръстен около себе си (block-confirmed
// усещане), с раздалечени фази, за да не пулсират синхронно.
//
// ПОПРАВЕНО: старата версия четеше --accent-red (#8b0000, тъмно бордо)
// — токън, който вече не съществува след обединяването на Index
// палитрата с истинската на сайта. Сега чете --accent (#FF3636),
// истинския брандинг цвят навсякъде другаде в сайта.
//
// Генерализирана версия на доказаната particlesBg.js архитектура от
// Schedule страницата — не е обвързана с конкретна страница, измерва
// РЕАЛНАТА позиция на .section-inner конвенцията (max-width: 1180px,
// центрирано), която използва цялата страница, вместо hardcode-нат
// клас като .schedule-container. Може да се включи от коя да е
// страница, която ползва тази конвенция — Index сега, други по-късно.
//
// Едно измерение при зареждане + при resize е достатъчно — всички
// .section-inner елементи на страницата резолват до СЪЩАТА центрирана
// ширина на дадена viewport ширина, затова измерваме само един (не
// .hero-inner, който нарочно не следва конвенцията — виж :not() по-долу).
//
// Performance основа — непроменена спрямо доказаната версия:
// - position: fixed + GPU layer promotion (виж CSS-а) — iOS Safari
//   fixed-position scroll jank фикс
// - Pre-rendered "glow" спрайт (createRadialGradient САМО веднъж, не
//   на всеки кадър/точка) + drawImage за самото рисуване
// - Edge lanes с fade към центъра — възлите съществуват само в
//   тесните странични ленти, не по цялата ширина, за да не пречат на
//   четимостта на съдържанието в центъра
// - Нула изкуствен frame-rate таван — requestAnimationFrame следва
//   естествената честота на екрана (60/90/120Hz)
// - ctx.strokeStyle/lineWidth се задават ВЕДНЪЖ преди O(n²) веригата
//   от свързващи линии, не на всяка итерация — само globalAlpha се
//   сменя (число), вместо препарсване на нов rgba() низ всеки път
// - Mouse interaction (отблъскване + свързващи линии) — само desktop
//   (pointer: fine), нула допълнителна логика на телефон
// - Resize handling с защита срещу мобилния "address bar" бъг (само
//   ширината тригерва пълен rebuild, не всяка височинна флуктуация)
// - Pause при скрит таб, prefers-reduced-motion се уважава

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
    var dotRgb = "255,110,110"; // lighter derivative of --accent, for the mouse-proximity highlight

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

    // Втори, по-малък спрайт специално за пътуващите "пакети" — по-ярко,
    // по-плътно ядро, за да се разчита ясно докато пресича мрежата бързо.
    var PULSE_SPRITE_SIZE = 22;
    var pulseSpriteCanvas = document.createElement("canvas");
    pulseSpriteCanvas.width = PULSE_SPRITE_SIZE;
    pulseSpriteCanvas.height = PULSE_SPRITE_SIZE;
    var pulseSpriteCtx = pulseSpriteCanvas.getContext("2d");
    (function buildPulseSprite() {
        var c = PULSE_SPRITE_SIZE / 2;
        var grad = pulseSpriteCtx.createRadialGradient(c, c, 0, c, c, c);
        grad.addColorStop(0, "rgba(255,255,255,1)");
        grad.addColorStop(0.35, "rgba(" + dotRgb + ",0.95)");
        grad.addColorStop(1, "rgba(" + accentRgb + ",0)");
        pulseSpriteCtx.fillStyle = grad;
        pulseSpriteCtx.beginPath();
        pulseSpriteCtx.arc(c, c, c, 0, Math.PI * 2);
        pulseSpriteCtx.fill();
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
                density: 1600,       // малко по-рехаво от преди — пулсовете/ping пръстените носят интереса вместо суров брой точки
                maxPerLane: 110,
                linkAlphaBase: 0.56,
                twinkleFloor: 0.82,
                fadePower: 0.4,
                speedMul: 0.85,
                dprCap: 1.75,
                hubChance: 0.16,
                pulseChance: 0.0022
            };
        }
        return {
            linkDistance: 105,
            density: 3700,
            maxPerLane: 95,
            linkAlphaBase: 0.5,
            twinkleFloor: 0.72,
            fadePower: 0.58,
            speedMul: 1.1,
            dprCap: 2,
            hubChance: 0.14,
            pulseChance: 0.0016
        };
    }

    var leftLane = null;
    var rightLane = null;

    var particles = [];
    var pulses = [];
    var MAX_PULSES = 36;
    var PULSE_SPEED = 0.016; // ~ дял от пътя, изминат за един кадър

    var mouse = { x: -9999, y: -9999, active: false };
    var time = 0;

    var MOUSE_EFFECT_DISTANCE = 130;
    var REPULSE_STRENGTH = 0.8;
    var FADE_SKIP_THRESHOLD = 0.03;

    // Измерваме първия .section-inner, който НЕ е .hero-inner — hero-то
    // нарочно не следва центриращата конвенция (собствен width: 100%),
    // затова би дал грешни измервания. Всеки друг .section-inner на
    // страницата резолва до СЪЩАТА центрирана ширина на дадена
    // viewport ширина, така че един измерен е достатъчен за всички.
    function measureLanes() {
        if (isCompact) {
            var band = Math.max(85, Math.min(width * 0.3, 195));
            leftLane = { min: 0, max: band };
            rightLane = { min: width - band, max: width };
            return;
        }

        var content = document.querySelector(".section-inner:not(.hero-inner)") || document.querySelector("main");
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
        return Math.max(8, Math.min(count, cfg.maxPerLane));
    }

    function rebuild() {
        width = window.innerWidth;
        height = window.innerHeight;
        isCompact = width <= COMPACT_BREAKPOINT;
        cfg = buildConfig();
        dpr = Math.min(window.devicePixelRatio || 1, cfg.dprCap);

        applyCanvasSize();
        measureLanes();
        seedParticles();
        pulses = [];
    }

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
        var isHub = Math.random() < cfg.hubChance;

        return {
            x: x,
            y: Math.random() * height,
            vx: (Math.random() - 0.5) * 0.8 * cfg.speedMul,
            vy: (Math.random() - 0.5) * 0.8 * cfg.speedMul,
            r: isHub ? (1.5 + Math.random() * 1.1) : (0.7 + Math.random() * 1.0),
            baseAlpha: isNear ? 1 : 0.68,
            xMin: lane.min,
            xMax: lane.max,
            edgeX: isLeft ? 0 : width,
            bandWidth: bandWidth,
            twinklePhase: Math.random() * Math.PI * 2,
            twinkleSpeed: 0.015 + Math.random() * 0.04,
            isHub: isHub,
            ringPhase: Math.random(),
            ringSpeed: 0.0028 + Math.random() * 0.0016,
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

        // ── Connecting lines + occasional traveling "data packet" spawn ──
        var a, b, pa, pb;
        ctx.strokeStyle = "rgb(" + accentRgb + ")";
        ctx.lineWidth = 1;
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
                    ctx.globalAlpha = alpha;
                    ctx.beginPath();
                    ctx.moveTo(pa.x, pa.y);
                    ctx.lineTo(pb.x, pb.y);
                    ctx.stroke();

                    if (pulses.length < MAX_PULSES && linkFade > 0.4 && Math.random() < cfg.pulseChance) {
                        pulses.push({
                            ax: pa.x, ay: pa.y,
                            bx: pb.x, by: pb.y,
                            t: 0,
                            fade: linkFade
                        });
                    }
                }
            }
        }

        if (hasFinePointer && mouse.active) {
            ctx.strokeStyle = "rgb(" + dotRgb + ")";
            for (i = 0; i < particles.length; i++) {
                p = particles[i];
                if (p.glow > 0) {
                    var mAlpha = p.glow * 0.55 * Math.max(p.fade, 0.3);
                    ctx.globalAlpha = mAlpha;
                    ctx.beginPath();
                    ctx.moveTo(mouse.x, mouse.y);
                    ctx.lineTo(p.x, p.y);
                    ctx.stroke();
                }
            }
        }

        // ── Hub "ping" rings — a slow, staggered expanding ring from each
        //    hub node, like a confirmed block rippling outward. ──────────
        ctx.lineWidth = 1;
        for (i = 0; i < particles.length; i++) {
            p = particles[i];
            if (!p.isHub || p.fade < FADE_SKIP_THRESHOLD) continue;
            var cycle = (time * p.ringSpeed + p.ringPhase) % 1;
            if (cycle >= 0.55) continue; // off for the rest of the cycle — avoids constant visual noise
            var ringT = cycle / 0.55;
            var ringR = 3 + ringT * 20;
            var ringAlpha = (1 - ringT) * 0.32 * p.fade;
            if (ringAlpha < 0.02) continue;
            ctx.globalAlpha = ringAlpha;
            ctx.strokeStyle = "rgb(" + accentRgb + ")";
            ctx.beginPath();
            ctx.arc(p.x, p.y, ringR, 0, Math.PI * 2);
            ctx.stroke();
        }

        // ── Nodes themselves ──────────────────────────────────────────
        for (i = 0; i < particles.length; i++) {
            p = particles[i];
            if (p.fade < FADE_SKIP_THRESHOLD && p.glow === 0) continue;

            var twinkle = cfg.twinkleFloor + (1 - cfg.twinkleFloor) * Math.sin(time * p.twinkleSpeed + p.twinklePhase);
            var alpha = Math.min(1, p.fade * twinkle * p.baseAlpha + p.glow * 0.5);
            if (alpha < 0.02) continue;

            var size = (p.r + p.glow * 1.4) * 3.4;
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

        // ── Traveling data packets ───────────────────────────────────
        for (i = pulses.length - 1; i >= 0; i--) {
            var pu = pulses[i];
            pu.t += PULSE_SPEED;
            if (pu.t >= 1) { pulses.splice(i, 1); continue; }
            var px = pu.ax + (pu.bx - pu.ax) * pu.t;
            var py = pu.ay + (pu.by - pu.ay) * pu.t;
            var pulseAlpha = Math.sin(pu.t * Math.PI) * pu.fade; // ease in, ease out over the traverse
            if (pulseAlpha < 0.03) continue;
            var pSize = PULSE_SPRITE_SIZE * 0.55;
            var pHalf = pSize / 2;
            ctx.globalAlpha = pulseAlpha;
            ctx.drawImage(
                pulseSpriteCanvas,
                Math.round(px - pHalf),
                Math.round(py - pHalf),
                Math.round(pSize),
                Math.round(pSize)
            );
        }

        ctx.globalAlpha = 1;
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
            // Само височината се е сменила — мобилен адрес бар, който
            // се крие/показва по време на скрол, не истински resize.
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
        document.addEventListener("mouseleave", function () {
            mouse.active = false;
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