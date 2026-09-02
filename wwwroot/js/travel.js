/* travel.js — появяване при скрол + активна секция в лентата.
   Скриптът само добавя ефект: ако не се зареди, страницата е напълно
   видима и използваема. */
(function () {
    var reveals = document.querySelectorAll("[data-reveal]");
    if ("IntersectionObserver" in window && reveals.length) {
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) {
                if (e.isIntersecting) {
                    e.target.classList.add("is-in");
                    io.unobserve(e.target);
                }
            });
        }, { rootMargin: "0px 0px -8% 0px", threshold: 0.02 });

        Array.prototype.forEach.call(reveals, function (n, i) {
            // Скриваме само това, което е под сгъвката.
            if (n.getBoundingClientRect().top > window.innerHeight * 0.92) {
                n.style.transitionDelay = (i % 3) * 80 + "ms";
                n.classList.add("is-armed");
                io.observe(n);
            }
        });
    }

    // Активната връзка се смята от позицията на скрола — най-долната
    // секция, чието начало е минало линията под sticky лентата.
    var links = Array.prototype.slice.call(document.querySelectorAll("[data-nav]"));
    if (!links.length) return;

    // Чете --navbar-height от :root (същата CSS променлива, ползвана в
    // travelStyle.css за sticky позицията), с fallback 64px.
    function navbarHeight() {
        var v = getComputedStyle(document.documentElement).getPropertyValue("--navbar-height");
        var n = parseFloat(v);
        return isNaN(n) ? 64 : n;
    }

    var raf = 0;
    function sync() {
        raf = 0;
        // 140 беше калибрирано за старата позиция (tv-nav на top:0).
        // Сега tv-nav каца под главния navbar, затова прагът се измества
        // надолу с толкова, колкото е неговата височина.
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
