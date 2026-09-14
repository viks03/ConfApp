/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
/* faq.js — the accordion, the search box and the reveal on scroll.
   The script only adds behaviour: without it the page is still visible and
   the questions still readable, with the default-open ones open. */
(function () {
    var list = document.querySelector("[data-faq-list]");
    if (!list) return;

    var items = Array.prototype.slice.call(list.querySelectorAll(".fq-item"));

    /* ── Accordion ────────────────────────────────────────── */
    items.forEach(function (item) {
        var btn = item.querySelector(".fq-q");
        if (!btn) return;
        btn.addEventListener("click", function () {
            var open = item.classList.toggle("is-open");
            btn.setAttribute("aria-expanded", open ? "true" : "false");
        });
    });

    /* ── Expand / collapse all ────────────────────────────── */
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

    /* ── Search ───────────────────────────────────────────── */
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
        // Enter must not submit anything: the search box is not in a form,
        // and the browser would reload the page.
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

    /* ── Reveal on scroll ─────────────────────────────────── */
    window.ConfApp.revealOnScroll();
})();
