// homeLecturers.js — ротацията на четирите карти с лектори на началната
// страница.
//
// Беше inline скрипт в Index.cshtml, който въртеше ПО ЕДНА карта на всеки
// 5 секунди. При повече лектори това означава, че една и съща карта се
// сменя през 20 секунди, а окото хваща само отделно потрепване някъде в
// реда — не се чете като „списъкът е по-дълъг от четири".
//
// Сега се сменят И ЧЕТИРИТЕ, но НЕ едновременно: вълна със 160ms между
// съседните карти. Едновременната смяна кара цялата секция да мигне и
// човек не знае накъде да гледа; вълната се чете като табло на летище —
// движението има посока и начало. След вълната следва дълга пауза, за да
// има време да се прочете кой е на картата.
//
// Данните идват от <script type="application/json" id="lecturers-data">,
// не от вграден в разметката JS — така сървърният JSON е обикновени
// данни, а логиката е в кешируем файл.
(function () {
    "use strict";

    var STAGGER = 160;    // между съседните карти във вълната
    var CYCLE = 9000;     // от начало на вълна до начало на следващата
    var OUT_MS = 340;     // трябва да съвпада с transition на .rotating-out
    var IN_MS = 460;      // трябва да съвпада с cardFlipIn

    // ── Снимката в едър план ──────────────────────────────────────────
    // Разметката на плочата се гради при първото отваряне, а не в
    // Razor-а: иначе всяка карта носи скрито копие на снимката си.
    // Класовете са същите като на страницата „Лектори" (.lc-modal-*),
    // за да е един и същият обект, а не негово подобие.
    var backdrop = null;
    var lastFocused = null;

    function buildModal(closeLabel) {
        backdrop = document.createElement("div");
        backdrop.className = "lc-modal-backdrop";
        backdrop.innerHTML =
            '<div class="lc-modal" role="dialog" aria-modal="true" aria-labelledby="hl-modal-name">' +
                '<i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>' +
                '<div class="lc-modal-photo">' +
                    '<span class="lc-modal-index"></span>' +
                    '<img alt="">' +
                '</div>' +
                '<button type="button" class="lc-modal-close">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="6" y1="6" x2="18" y2="18"></line><line x1="18" y1="6" x2="6" y2="18"></line></svg>' +
                '</button>' +
                '<div class="lc-modal-body">' +
                    '<h3 id="hl-modal-name"></h3>' +
                    '<p class="lc-modal-role"></p>' +
                    '<p class="lc-modal-org"></p>' +
                    '<p class="lc-modal-bio"></p>' +
                '</div>' +
            '</div>';

        var close = backdrop.querySelector(".lc-modal-close");
        close.setAttribute("aria-label", closeLabel);
        document.body.appendChild(backdrop);

        close.addEventListener("click", closeModal);
        backdrop.addEventListener("click", function (e) { if (e.target === backdrop) closeModal(); });
        return backdrop;
    }

    function openModal(card, grid) {
        if (!card) return;
        if (!backdrop) buildModal(grid.getAttribute("data-close-label") || "Затвори");

        var txt = function (sel) {
            var el = card.querySelector(sel);
            return el ? el.textContent.trim() : "";
        };

        var img = card.querySelector(".avatar-lg img");
        var modalImg = backdrop.querySelector(".lc-modal-photo img");
        modalImg.src = img ? img.getAttribute("src") : "";
        modalImg.alt = img ? (img.getAttribute("alt") || "") : "";

        backdrop.querySelector(".lc-modal-index").textContent = txt(".lc-index");
        backdrop.querySelector("#hl-modal-name").textContent = txt("h4");

        // Роля, организация и биография може да липсват (полетата са
        // nullable) — тогава редът се скрива, вместо да оставя дупка.
        [[".lc-modal-role", ".role"], [".lc-modal-org", ".org"], [".lc-modal-bio", ".bio"]].forEach(function (pair) {
            var el = backdrop.querySelector(pair[0]);
            var value = txt(pair[1]);
            el.textContent = value;
            el.style.display = value ? "" : "none";
        });

        // Пътят до профила се слага В плочата: модалът прехваща клика
        // върху портрета, иначе профилът иска затваряне и втори клик.
        var existing = backdrop.querySelector(".lc-modal-link");
        if (existing) existing.remove();

        var url = card.dataset.url;
        if (url) {
            var link = document.createElement("a");
            link.className = "lc-modal-link";
            link.href = url;
            link.target = "_blank";
            link.rel = "noopener";
            link.innerHTML = (txt(".lc-more") || "Профил") +
                ' <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>';
            backdrop.querySelector(".lc-modal-body").appendChild(link);
        }

        lastFocused = document.activeElement;
        backdrop.classList.add("is-open");
        lockScroll(true);
        backdrop.querySelector(".lc-modal-close").focus();
    }

    function closeModal() {
        if (!backdrop) return;
        backdrop.classList.remove("is-open");
        lockScroll(false);
        if (lastFocused && typeof lastFocused.focus === "function") lastFocused.focus();
    }

    // Без компенсация за скролбара страницата подскача при отваряне.
    function lockScroll(on) {
        var sbw = window.innerWidth - document.documentElement.clientWidth;
        if (on) {
            document.body.style.paddingRight = sbw > 0 ? sbw + "px" : "";
            document.body.style.overflow = "hidden";
        } else {
            document.body.style.paddingRight = "";
            document.body.style.overflow = "";
        }
    }

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && backdrop && backdrop.classList.contains("is-open")) closeModal();
    });

    function init() {
        var grid = document.getElementById("lecturers-grid");
        if (!grid) return false;

        var dataEl = document.getElementById("lecturers-data");
        if (!dataEl) return true;

        var all;
        try {
            all = JSON.parse(dataEl.textContent || "[]");
        } catch (e) {
            return true; // невалиден JSON — картите остават както са рендирани
        }
        if (!all || !all.length) return true;

        var isBg = (grid.getAttribute("data-locale") || "").toLowerCase() === "bg";

        // Делегирани кликове, защото съдържанието на картите се сменя.
        //
        // Портретът и картата са ДВА различни таргета: клик върху
        // портрета отваря снимката в едър план (същата плоча като на
        // страницата „Лектори"), клик другаде по картата води към
        // профила. Без stopPropagation един клик би направил и двете.
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

        // Портретът е <div> (вложен <button> в кликаема карта е лоша
        // идея), затова клавиатурната поддръжка се дава тук.
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

        // Знакът за увеличение се добавя от JS, за да не стои мъртъв в
        // разметката, ако скриптът не се зареди.
        cards.forEach(function (card) {
            var avatar = card.querySelector(".avatar-lg");
            if (!avatar || avatar.querySelector(".lc-zoom")) return;
            var zoom = document.createElement("span");
            zoom.className = "lc-zoom";
            zoom.setAttribute("aria-hidden", "true");
            zoom.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"></circle><line x1="16.5" y1="16.5" x2="21" y2="21"></line><line x1="11" y1="8" x2="11" y2="14"></line><line x1="8" y1="11" x2="14" y2="11"></line></svg>';
            avatar.appendChild(zoom);
        });

        // Под пет лектора няма какво да се върти — четирите карти вече
        // показват всичко. Ротация тогава само би повтаряла същите хора.
        if (all.length <= cards.length) return true;

        function pick(l, a, b) { return isBg ? (l[a] || l[b]) : l[b]; }

        // Първите N от масива са тези, които сървърът вече е рендирал.
        var shown = [];
        for (var i = 0; i < cards.length; i++) shown.push(i);

        var queue = [];
        var qi = 0;

        function refillQueue() {
            queue = [];
            for (var i = 0; i < all.length; i++) queue.push(i);
            // Фишер-Йейтс — Math.random()-0.5 в sort() не дава равномерно
            // разбъркване и в част от браузърите подрежда почти същото.
            for (var j = queue.length - 1; j > 0; j--) {
                var k = Math.floor(Math.random() * (j + 1));
                var t = queue[j]; queue[j] = queue[k]; queue[k] = t;
            }
            qi = 0;
        }

        refillQueue();

        // Следващ лектор, който НЕ е на екрана в момента — иначе един и
        // същи човек може да се появи в две карти едновременно.
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
            // Скрит таб: browser-ите тротлят таймерите, вълната се
            // натрупва и при връщане всичко се сменя наведнъж.
            if (document.hidden) return;
            cards.forEach(function (card, i) {
                setTimeout(function () { swap(card, i); }, i * STAGGER);
            });
        }

        setInterval(wave, CYCLE);
        return true;
    }

    // Ако скриптът бъде зареден преди решетката да съществува (head,
    // defer, динамично включване), опитваме отново, вместо да умрем тихо.
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
