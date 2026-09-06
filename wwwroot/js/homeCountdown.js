// homeCountdown.js — живото броене в горния ред на hero-то.
//
// Датата НЕ е в скрипта: чете се от data-countdown атрибута на самия
// елемент (ISO формат, YYYY-MM-DD). Така смяната ѝ е една стойност в
// разметката, а не редакция на JS файл — и .resx-ът остава единственото
// място за текстовете.
//
// Елементът стои с hidden в HTML-а и се показва само след като има
// реална стойност. Ако датата липсва, е невалидна или вече е минала,
// нищо не се рендира — на страницата не остава празен разделител,
// нито „остават -12 дни".
(function () {
    "use strict";

    // ── Височината на топбара → --home-topbar ──────────────────────
    // hero-то е „един екран минус навигацията", за да стои пропускът
    // точно на долния ръб. Реалната височина на топбара обаче се мени:
    // 72px на десктоп, но около 106px на телефон, където навигацията се
    // пренася. Фиксираните 64px в CSS-а оставяха пропуска около 50px под
    // сгъвката — точно толкова, че да не се вижда, че го има.
    // Стойността в CSS-а остава като fallback, ако скриптът не се зареди.
    function syncTopbarHeight() {
        var topbar = document.querySelector(".topbar");
        if (!topbar) return;
        var h = Math.round(topbar.getBoundingClientRect().height);
        if (h > 0) document.documentElement.style.setProperty("--home-topbar", h + "px");
    }

    syncTopbarHeight();
    window.addEventListener("load", syncTopbarHeight);
    window.addEventListener("orientationchange", syncTopbarHeight);

    // ── Входната анимация се ВКЛЮЧВА, не се изключва ────────────────
    // Keyframes-ите тръгват от opacity: 0. Ако анимационният часовник не
    // тръгне (страницата е заредена в скрит таб, документът е
    // throttle-нат, първият кадър закъснее над закъсненията от 0.08–0.58s),
    // hero-то остава без заглавие, абзац, лога и талон — тоест празно.
    //
    // Затова в CSS-а анимацията виси на .hero-ready и се слага оттук.
    // Базовото състояние е видимо: ако този скрипт не се зареди,
    // страницата просто няма входна анимация.
    //
    // rAF, не веднага: класът трябва да се сложи в кадър, в който
    // часовникът вече тече, иначе се връщаме на същия проблем.
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
        if (tb) { try { new ResizeObserver(syncTopbarHeight).observe(tb); } catch (e) { /* стар браузър */ } }
    }

    function initCountdown() {
        var el = document.querySelector(".hero-countdown");
        if (!el) return false;

        var raw = el.getAttribute("data-countdown");
        if (!raw) return true;

        // Часовият пояс: датата се чете като локална полунощ, не UTC —
        // "2026-10-29" в new Date() се приема за UTC и в България би
        // дало ден разлика в късните часове.
        var parts = raw.split("-");
        if (parts.length !== 3) return true;

        var target = new Date(+parts[0], +parts[1] - 1, +parts[2]);
        if (isNaN(target.getTime())) return true;

        // Конференцията трае повече от един ден. Без крайната дата
        // талонът щеше да каже "днес" само на 29-ти, а на 30-ти и 31-ви
        // да изчезне — докато събитието още тече. Атрибутът е незадължителен:
        // ако липсва, се приема еднодневно събитие.
        var endRaw = el.getAttribute("data-countdown-end");
        var endTarget = target;
        if (endRaw) {
            var ep = endRaw.split("-");
            if (ep.length === 3) {
                var parsed = new Date(+ep[0], +ep[1] - 1, +ep[2]);
                if (!isNaN(parsed.getTime()) && parsed >= target) endTarget = parsed;
            }
        }

        // Текстовете идват от разметката (data-* атрибути), не оттук —
        // .resx остава единственото място за език, а JS файлът е кешируем
        // и еднакъв за двата езика.
        var labelToday = el.getAttribute("data-label-today") || "";
        var labelSuffix = el.getAttribute("data-label-suffix") || "";
        // На български "1 дни" е грешно — трябва "1 ден". Английският има
        // същия проблем ("1 days"). Формата за единствено число е
        // незадължителна: без нея се ползва множествената, както досега.
        var labelSuffixOne = el.getAttribute("data-label-suffix-one") || labelSuffix;

        function render() {
            var now = new Date();
            // Броим в цели дни от ДНЕШНАТА полунощ, не от текущия час —
            // иначе числото се сменя по средата на деня.
            var today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
            var days = Math.round((target - today) / 86400000);
            var daysAfterEnd = Math.round((today - endTarget) / 86400000);

            var num = el.querySelector("b");
            var txt = el.querySelector(".hero-countdown-text");

            // Събитието е свършило — талонът се маха. Единственият случай,
            // в който нищо не се показва.
            if (daysAfterEnd > 0) {
                el.hidden = true;
                el.classList.remove("is-live", "is-today");
                return false;
            }

            if (days <= 0) {
                // Който и да е от дните на конференцията: без число.
                if (num) num.hidden = true;
                if (txt) txt.textContent = labelToday;
                el.hidden = false;
                el.classList.add("is-live", "is-today");
                return true;   // продължаваме да проверяваме — утре може да е свършила
            }

            if (num) { num.hidden = false; num.textContent = days; }
            if (txt) txt.textContent = (days === 1 ? labelSuffixOne : labelSuffix);
            el.hidden = false;
            el.classList.add("is-live");
            el.classList.remove("is-today");
            return true;
        }

        if (!render()) return true;

        // Преизчисляване веднъж в час — страницата може да стои отворена
        // през полунощ. По-често е излишно за брояч в цели дни.
        setInterval(render, 3600000);
        return true;
    }

    // Скриптът е предвиден за края на <body> (@section Scripts), но не
    // разчита на това: ако бъде зареден по-рано — в <head>, през defer
    // или динамично — елементът още не съществува и querySelector връща
    // null. Затова опитваме отново на DOMContentLoaded, на load и няколко
    // пъти на кратки интервали, вместо да умрем тихо при първия опит.
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
