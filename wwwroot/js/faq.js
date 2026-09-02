/* faq.js — акордеон, търсене и появяване при скрол.
   Скриптът само добавя поведение: без него страницата е видима,
   а въпросите остават четими (отворените по подразбиране са отворени). */
(function () {
    var list = document.querySelector("[data-faq-list]");
    if (!list) return;

    var items = Array.prototype.slice.call(list.querySelectorAll(".fq-item"));

    /* ── Акордеон ─────────────────────────────────────────── */
    items.forEach(function (item) {
        var btn = item.querySelector(".fq-q");
        if (!btn) return;
        btn.addEventListener("click", function () {
            var open = item.classList.toggle("is-open");
            btn.setAttribute("aria-expanded", open ? "true" : "false");
        });
    });

    /* ── Разгъни / свий всички ────────────────────────────── */
    var toggleAll = document.querySelector("[data-faq-toggle-all]");
    if (toggleAll) {
        var allOpen = false;
        toggleAll.addEventListener("click", function () {
            allOpen = !allOpen;
            items.forEach(function (item) {
                if (item.hidden) return;
                item.classList.toggle("is-open", allOpen);
                var b = item.querySelector(".fq-q");
                if (b) b.setAttribute("aria-expanded", allOpen ? "true" : "false");
            });
            toggleAll.textContent = allOpen
                ? toggleAll.getAttribute("data-label-collapse")
                : toggleAll.getAttribute("data-label-expand");
        });
    }

    /* ── Търсене ──────────────────────────────────────────── */
    var input = document.querySelector("[data-faq-search]");
    var clear = document.querySelector("[data-faq-clear]");
    var count = document.querySelector("[data-faq-count]");
    var empty = document.querySelector("[data-faq-empty]");
    var term = document.querySelector("[data-faq-term]");
    var total = items.length;

    function filter() {
        var q = (input ? input.value : "").trim().toLowerCase();
        var shown = 0;

        items.forEach(function (item) {
            var hay = (item.textContent || "").toLowerCase();
            var match = !q || hay.indexOf(q) !== -1;
            item.hidden = !match;
            if (match) shown++;
        });

        if (clear) clear.hidden = !q;
        if (empty) empty.hidden = shown !== 0;
        if (term) term.textContent = q;
        if (count) {
            count.textContent = q
                ? shown + " " + count.getAttribute("data-of") + " " + total
                : total + " " + count.getAttribute("data-unit");
        }
    }

    if (input) {
        input.addEventListener("input", filter);
        // Enter не бива да праща форма — страницата не е форма.
        input.addEventListener("keydown", function (e) {
            if (e.key === "Enter") e.preventDefault();
        });
    }
    if (clear) {
        clear.addEventListener("click", function () {
            if (input) { input.value = ""; input.focus(); }
            filter();
        });
    }
    var showAll = document.querySelector("[data-faq-showall]");
    if (showAll) {
        showAll.addEventListener("click", function () {
            if (input) { input.value = ""; input.focus(); }
            filter();
        });
    }
    filter();

    /* ── Появяване при скрол ──────────────────────────────── */
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
})();
