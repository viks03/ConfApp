// globalEffects.js — JS-ът на новия слой: прогрес лентата
// (#scroll-progress), мекото курсорно петно и щитът за банерите.
// Няма canvas и няма render loop за самия фон — фонът е изцяло CSS;
// JS-ът само записва няколко променливи, които CSS-ът превръща в
// transform/mask.
//
// Съзнателни решения:
// - Лентата НЕ следва скрола 1:1. Целта (реалната позиция) се чете
//   passive от scroll събитието, а показваната стойност догонва
//   целта експоненциално (lerp по кадър) — същият подход като
//   mouse парallax-а в auroraGlow.js. Затова движението е плавно
//   изтичане, а не стъпаловидно прещракване на всяко събитие,
//   особено при wheel/trackpad, който праща порции по 100px.
// - rAF цикълът работи САМО докато има разлика между целта и
//   показваната стойност; щом се изравнят, спира сам (без вечен
//   loop на празни обороти).
// - Записва се само --gfx-progress (0..1) на <html>. Ширината е
//   transform: scaleX() в CSS-а — композиторска операция, без
//   layout/paint на кадър.
// - Къси страници: под прага лентата получава hidden и изчезва от
//   рендера, вместо да стои празна. Прагът (не 0) пази от 1-2px
//   разлики от мобилния адрес бар.
// - Не е SPA — състоянието се преизчислява при всяко зареждане,
//   нищо не се persist-ва.
// - Курсорът: само при pointer: fine (на touch устройства listener-ът дори
//   не се закача). Фонът НЕ се мести — движи се само мекото accent
//   петно (.afx-cursor), чиято позиция се записва в --gfx-cx/--gfx-cy
//   и догонва курсора експоненциално (както в auroraGlow.js), затова
//   се усеща като течно, не като залепен за мишката кръг.
// - Нищо тук не докосва .reveal, топбара, мобилното меню или футъра.

(function () {
    "use strict";

    var MIN_SCROLLABLE = 40;  // px — под това страницата се смята за къса
    var EASE = 0.14;          // колко от разликата се изминава на кадър
    var EPSILON = 0.0004;     // под тази разлика смятаме, че е пристигнала

    // Банерите и hero секциите носят СОБСТВЕНИ ефекти (растер, tint,
    // scanlines, canvas) — ambient слоят се изрязва над тях, за да няма
    // двоен ефект. Бъдеща страница се включва САМО с атрибут
    // data-gfx-shield върху секцията — без промяна тук. Класовете
    // по-долу са само за вече съществуващите страници.
    // БЪГ ФИКС: списъкът покриваше само три от осемте банера. Всяка страница
    // си кръщава банера с префикса на страницата (.lc- за лектори, .ic- за
    // ICBI, .sc- за програма, .at- за участие), затова слоят минаваше над тях
    // и се виждаше над снимката — най-ясно на телефон, където банерът заема
    // почти целия екран.
    //
    // Затова: изричен списък на всички ПЛЮС общото правило [class$="-banner"],
    // което хваща и бъдещи страници, кръстени по същата конвенция. Нова
    // страница вече не иска промяна тук.
    var SHIELD_SELECTOR = "[data-gfx-shield], [class$='-banner'], [class*='-banner '], " +
                          ".fq-banner, .tv-banner, .lc-banner, .ic-banner, " +
                          ".sc-banner, .at-banner, .cf-banner, .hero";
    var SHIELD_FADE = 110;    // px мек преход под банера

    var reduceMotion = window.matchMedia &&
        window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    // Изричен opt-out за страница/инструмент, който не го иска:
    // <html data-gfx="off"> — CSS-ът го скрива, тук спирам и цялата
    // работа, за да няма излишни listener-и.
    if (document.documentElement.getAttribute("data-gfx") === "off") return;

    var bar = null;
    var target = 0;
    var shown = 0;
    var rafId = null;

    // Курсорното петно: цел и показвана позиция, в px.
    // 0.075 (вместо 0.12) — петното изостава повече от курсора,
    // затова се чете като част от фона, не като следващ мишката кръг.
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

    // Колко от видимия viewport отгоре се заема от банер в момента.
    // Щитът важи, докато банерът е ВИДИМ отгоре — не само когато е
    // излязъл над ръба: при скрол 0 банерът стои точно под sticky
    // топбара (rect.top > 0), а точно тогава двойният ефект би се
    // получил най-видимо.
    function updateShield() {
        var list = resolveShields();
        var viewport = window.innerHeight || 0;
        var edge = 0;

        for (var i = 0; i < list.length; i++) {
            var rect = list[i].getBoundingClientRect();
            if (rect.height === 0) continue;                  // скрит елемент
            if (rect.bottom <= 0 || rect.top >= viewport) continue; // извън екрана

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

        // ПРОМЯНА: координатите се пишат и при намалено движение.
        // Досега те движеха само петното, затова спирането им беше вярно.
        // Сега от тях зависи ЧЕРТЕЖЪТ на живите присети (lantern, prism,
        // sonar, caustic, spine) — без тях фонът замръзва в резервната си
        // позиция. Самото петно и анимациите пак спират, това е в CSS-а.
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
                // Първото движение: петното се появява вече на позиция, а не
                // долита от ъгъла на екрана.
                pointerSeen = true;
                pointerX = pointerTargetX;
                pointerY = pointerTargetY;
                document.documentElement.style.setProperty("--gfx-cursor", "1");
            }

            start();
        }, { passive: true });

        // Курсорът излиза от страницата — петното изгасва плавно
        // (opacity transition в CSS-а), а не застива на ръба.
        document.addEventListener("mouseleave", function () {
            document.documentElement.style.setProperty("--gfx-cursor", "0");
        }, { passive: true });

        document.addEventListener("mouseenter", function () {
            if (pointerSeen) {
                document.documentElement.style.setProperty("--gfx-cursor", "1");
            }
        }, { passive: true });
    }

    // Съдържанието може да промени височината си след първия кадър
    // (шрифтове, изображения, отворен FAQ акордеон, .reveal преходи) —
    // ResizeObserver преизчислява прага без polling.
    if (typeof ResizeObserver === "function") {
        try {
            new ResizeObserver(onScroll).observe(document.documentElement);
        } catch (e) {
            /* стар браузър — scroll/resize listener-ите са достатъчни */
        }
    }

    function init() {
        shields = null;      // преоткриване след пълно зареждане на DOM-а
        lastShield = -1;
        updateShield();

        if (measure()) {
            shown = target; // първоначалното състояние не се анимира
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
