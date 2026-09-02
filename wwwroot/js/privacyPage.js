// ── Privacy Policy page — TOC generation, scroll progress, back-to-top, print.
//    The TOC is built entirely from whatever H2s exist in the content at
//    runtime — nothing here is hardcoded to specific section titles, so it
//    stays correct no matter what gets edited in the admin panel's Quill editor.
(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        var content    = document.getElementById('privacyContent');
        var tocWrapper = document.getElementById('privacyTocWrapper');
        var tocNav     = document.getElementById('privacyTocNav');

        // ── Estimated reading time — based on the actual word count, so it's
        //    correct no matter how long the admin's edit turns out to be. ──
        var readingTimeEl = document.getElementById('privacyReadingTime');
        if (readingTimeEl && content) {
            var words = (content.textContent || '').trim().split(/\s+/).filter(Boolean).length;
            var minutes = Math.max(1, Math.round(words / 200)); // ~200 wpm average
            var template = readingTimeEl.getAttribute('data-template') || '{0} min read';
            readingTimeEl.textContent = template.replace('{0}', minutes);
        }

        // ── Copy-link affordance on each heading — hover reveals a small link
        //    icon; clicking copies a direct #anchor URL to that section. Useful
        //    for a legal doc people reference specific clauses of. ───────────
        function attachCopyLink(heading, id) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'privacy-copy-link';
            btn.setAttribute('aria-label', 'Copy link to this section');
            btn.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"></path><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"></path></svg>';
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                var url = window.location.origin + window.location.pathname + '#' + id;
                (navigator.clipboard ? navigator.clipboard.writeText(url) : Promise.reject())
                    .then(function () {
                        btn.classList.add('is-copied');
                        setTimeout(function () { btn.classList.remove('is-copied'); }, 1600);
                    })
                    .catch(function () { /* clipboard unavailable — the # link still works by hand */ });
            });
            heading.appendChild(btn);
        }

        // ── Build the TOC ────────────────────────────────────────────────
        var tocLinks = []; // { headingId, el }
        if (content && tocNav) {
            var headings = content.querySelectorAll('h2');

            if (headings.length === 0) {
                if (tocWrapper) tocWrapper.style.display = 'none';
            } else {
                var usedSlugs = {};

                function slugify(text, fallbackIndex) {
                    var base = text.trim().toLowerCase()
                        .replace(/[^\w\s-]/g, '')
                        .replace(/\s+/g, '-')
                        .substring(0, 60);
                    var slug = base || ('section-' + fallbackIndex);
                    var unique = slug;
                    var n = 2;
                    while (usedSlugs[unique]) { unique = slug + '-' + (n++); }
                    usedSlugs[unique] = true;
                    return unique;
                }

                headings.forEach(function (h, i) {
                    var id = slugify(h.textContent, i);
                    h.id = id;
                    attachCopyLink(h, id);

                    var link = document.createElement('a');
                    link.href = '#' + id;
                    link.textContent = h.textContent.trim();
                    link.addEventListener('click', function (e) {
                        e.preventDefault();
                        h.scrollIntoView({ behavior: 'smooth', block: 'start' });
                        history.replaceState(null, '', '#' + id);
                        // Collapse the mobile <details> after jumping, so the
                        // admin's newly-clicked destination is what's on screen
                        // instead of the still-open TOC covering it.
                        var details = tocWrapper ? tocWrapper.querySelector('details') : null;
                        if (details && window.innerWidth < 960) details.open = false;
                    });
                    tocNav.appendChild(link);
                    tocLinks.push({ id: id, link: link, el: h });
                });

                // ── Scrollspy — highlight whichever section is currently in view ──
                if ('IntersectionObserver' in window) {
                    var observer = new IntersectionObserver(function (entries) {
                        entries.forEach(function (entry) {
                            var match = tocLinks.find(function (t) { return t.el === entry.target; });
                            if (match) match.link.classList.toggle('is-active', entry.isIntersecting);
                        });
                    }, { rootMargin: '-15% 0px -70% 0px' });
                    headings.forEach(function (h) { observer.observe(h); });
                }
            }
        }

        // ── Scroll progress bar ──────────────────────────────────────────
        var progressBar = document.getElementById('privacyProgressBar');
        function updateProgress() {
            if (!progressBar) return;
            var scrollable = document.documentElement.scrollHeight - window.innerHeight;
            var pct = scrollable > 0 ? Math.min(100, (window.scrollY / scrollable) * 100) : 0;
            progressBar.style.width = pct + '%';
        }

        // ── Back to top ───────────────────────────────────────────────────
        var backToTop = document.getElementById('privacyBackToTop');
        function updateBackToTop() {
            if (!backToTop) return;
            backToTop.classList.toggle('is-visible', window.scrollY > 500);
        }
        if (backToTop) {
            backToTop.addEventListener('click', function () {
                window.scrollTo({ top: 0, behavior: 'smooth' });
            });
        }

        window.addEventListener('scroll', function () {
            updateProgress();
            updateBackToTop();
        }, { passive: true });
        updateProgress();
        updateBackToTop();

        // ── Print ─────────────────────────────────────────────────────────
        var printBtn = document.getElementById('privacyPrintBtn');
        if (printBtn) printBtn.addEventListener('click', function () { window.print(); });

        // ── Deep-link support — if the URL already has a #hash on load
        //    (e.g. someone bookmarked a specific section), scroll to it once
        //    heading IDs exist. ────────────────────────────────────────────
        if (window.location.hash) {
            var target = document.getElementById(window.location.hash.slice(1));
            if (target) setTimeout(function () { target.scrollIntoView({ block: 'start' }); }, 50);
        }
    });
})();