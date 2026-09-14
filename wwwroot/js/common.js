/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
/* common.js — what the per-page scripts share.

   Loaded by _Layout.cshtml BEFORE all the others, so it is available
   everywhere: on the public pages and in the admin panel, which uses the same
   layout.

   What lives here is what used to be copied between files:

     ConfApp.escapeHtml(v)              five implementations, three different
                                        sets of characters
     ConfApp.revealOnScroll(opts)       three copies (travel, faq, scriptMain)
     ConfApp.bindScrollArrow()          two copies (scriptSchedule,
                                        scriptLecturers)
     ConfApp.topbarHeight()             four places measured .topbar
     ConfApp.syncTopbarVar(name, el)    three of them wrote a CSS variable
     ConfApp.lockScroll(on)             four copies, three mechanisms

   Nothing here depends on the markup of any one page: every function checks
   first whether there is anything to work on. */

(function (window, document) {
    'use strict';

    var ConfApp = window.ConfApp || (window.ConfApp = {});

    /* ── Escaping HTML ───────────────────────────────────────────────────
       Through textContent rather than a chain of .replace() calls, so that no
       character can be missed. The five old implementations escaped
       `& < > "`, `& < > " '`, `& < >`, `& <` and everything respectively —
       and had already started to disagree with one another. This one is a
       superset of all of them. */
    ConfApp.escapeHtml = function (value) {
        var d = document.createElement('div');
        d.textContent = value == null ? '' : String(value);
        return d.innerHTML;
    };

    /* ── Reveal on scroll ────────────────────────────────────────────────
       Two modes, because that is what the three copies did:

         arm: true  (the default) — only the elements below the fold are armed,
                    with a staggered delay. What travel.js and faq.js did, to
                    the letter.
         arm: false — everything is observed, with no delay and no .is-armed.
                    What scriptMain.js does for .reveal.

       Without IntersectionObserver nothing is hidden at all: the page stays
       visible rather than blank. */
    ConfApp.revealOnScroll = function (opts) {
        var o = opts || {};
        var selector     = o.selector || '[data-reveal]';
        var visibleClass = o.visibleClass || 'is-in';
        var threshold    = (o.threshold == null) ? 0.02 : o.threshold;
        var rootMargin   = (o.rootMargin == null) ? '0px 0px -8% 0px' : o.rootMargin;
        var arm          = o.arm !== false;

        var nodes = document.querySelectorAll(selector);
        if (!('IntersectionObserver' in window) || !nodes.length) return null;

        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) {
                if (e.isIntersecting) {
                    e.target.classList.add(visibleClass);
                    io.unobserve(e.target);
                }
            });
        }, { rootMargin: rootMargin, threshold: threshold });

        Array.prototype.forEach.call(nodes, function (n, i) {
            if (!arm) { io.observe(n); return; }
            // Only what is below the fold is hidden: arming something
            // already on screen would make it fade in for no reason.
            if (n.getBoundingClientRect().top > window.innerHeight * 0.92) {
                n.style.transitionDelay = (i % 3) * 80 + 'ms';
                n.classList.add('is-armed');
                io.observe(n);
            }
        });

        return io;
    };

    /* ── The scroll-down arrow ───────────────────────────────────────────
       Hides itself once the visitor has scrolled, and scrolls smoothly on
       click rather than relying on CSS scroll-behavior alone. */
    ConfApp.bindScrollArrow = function (selector) {
        var arrow = document.querySelector(selector || '.scroll-down-arrow');
        if (!arrow) return;

        var toggle = function () {
            arrow.classList.toggle('is-hidden', window.scrollY > 60);
        };
        toggle();
        window.addEventListener('scroll', toggle, { passive: true });

        arrow.addEventListener('click', function (e) {
            var targetId = arrow.getAttribute('href');
            var targetEl = targetId ? document.querySelector(targetId) : null;
            if (targetEl) {
                e.preventDefault();
                targetEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
    };

    /* ── The height of the top bar ───────────────────────────────────────
       The real height changes between breakpoints (72px on a desktop, about
       106px on a phone, where the navigation wraps onto a second line) and
       also without a resize — when the mobile menu reflows, or after the font
       loads. So it is measured rather than guessed; the value in the CSS
       stays as a fallback for when the script does not load. */
    ConfApp.topbarHeight = function () {
        var topbar = document.querySelector('.topbar');
        if (!topbar) return 0;
        return Math.round(topbar.getBoundingClientRect().height);
    };

    /* Writes that height into a CSS variable and keeps it up to date.
       `target` is the element written to, <html> by default. */
    ConfApp.syncTopbarVar = function (name, target) {
        var host = target || document.documentElement;
        if (!host) return;

        var sync = function () {
            var h = ConfApp.topbarHeight();
            if (h > 0) host.style.setProperty(name, h + 'px');
        };

        sync();
        window.addEventListener('load', sync);
        window.addEventListener('orientationchange', sync);

        var topbar = document.querySelector('.topbar');
        if (typeof ResizeObserver === 'function' && topbar) {
            try { new ResizeObserver(sync).observe(topbar); } catch (e) { /* an older browser */ }
        }

        return sync;
    };

    /* ── Locking the scroll behind a modal ───────────────────────────────
       One mechanism for the four places that each did it their own way:

       1. position: fixed rather than overflow: hidden. iOS Safari has a long
          documented bug where overflow: hidden does NOT reliably stop the page
          behind the modal from being dragged with a finger. Taking body out of
          the normal flow stops it literally, and the position is restored
          exactly on unlock. Only dataNotice.js knew this; the other three
          implementations of the same task did not.

       2. Compensating for the scrollbar width. Without it the page jumps to
          the right at the moment the modal opens. Only the two lecturer modals
          knew this; dataNotice did not.

       3. A counter rather than a boolean. `lockScroll(false)` used to unlock
          unconditionally: close the portrait modal while the bug-report modal
          is open, and the page behind it scrolls again.

       behavior: 'instant' on restore is required — the site sets a global
       scroll-behavior: smooth on <html> (mainStyle.css), and without it the
       restore is visibly animated from the top of the page down. */
    var lockCount = 0;
    var lockedY = 0;

    ConfApp.lockScroll = function (on) {
        var body = document.body;
        if (!body) return;

        if (on) {
            lockCount++;
            if (lockCount > 1) return;      // already locked by somebody else

            lockedY = window.scrollY || window.pageYOffset || 0;
            var sbw = window.innerWidth - document.documentElement.clientWidth;

            body.style.position = 'fixed';
            body.style.top   = (-lockedY) + 'px';
            body.style.left  = '0';
            body.style.right = '0';
            body.style.width = '100%';
            if (sbw > 0) body.style.paddingRight = sbw + 'px';
            return;
        }

        if (lockCount === 0) return;
        lockCount--;
        if (lockCount > 0) return;          // another modal still holds the lock

        body.style.position = '';
        body.style.top = '';
        body.style.left = '';
        body.style.right = '';
        body.style.width = '';
        body.style.paddingRight = '';
        window.scrollTo({ top: lockedY, left: 0, behavior: 'instant' });
    };

})(window, document);
