/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
// initSchedule() is called through the guard at the end of the file and NOT
// directly on DOMContentLoaded: if the script is loaded after that event has
// already fired (defer, async, a dynamic include, the tag moved later), the
// listener never runs and the whole file is dead — the day tabs, the indicator
// and "read more" all stop working, with no error in the console.
function initSchedule() {
    // --- The top bar height → --sc-rail-top ---
    // The rail of days is sticky and has to sit EXACTLY under the fixed top
    // bar. The real height of that bar changes between breakpoints (at ≤640px
    // it becomes a grid with different spacing, see mainStyle.css), and again
    // when a phone is rotated or a translated label is longer. A fixed constant
    // in the CSS leaves either a gap or a hidden row, so it is measured here.
    // The CSS value stays as a fallback for when the script does not load.
    // The measuring and its listeners live in ConfApp.syncTopbarVar
    // (common.js); what is left here is the variable name and where to write
    // it.
    const schedulePage = document.querySelector('.schedule-page');
    if (schedulePage) window.ConfApp.syncTopbarVar('--sc-rail-top', schedulePage);

    const dayButtons = document.querySelectorAll('.day-btn');
    const tabPanes = document.querySelectorAll('.tab-pane');
    const toggleContainer = document.querySelector('.day-toggles');
    const indicator = document.querySelector('.day-toggle-indicator');

    // --- The sliding indicator under the active tab ---
    // It measures the position and width of the active button relative to the
    // container and writes them onto the indicator inline; the CSS transition
    // (see .day-toggle-indicator in scheduleStyle.css) does the sliding.
    function moveIndicatorTo(button) {
        if (!toggleContainer || !indicator || !button) return;
        const containerRect = toggleContainer.getBoundingClientRect();
        const btnRect = button.getBoundingClientRect();
        // scrollLeft: on a phone the rail scrolls sideways, so the position is
        // measured against the CONTENT of the container rather than its visible
        // part — otherwise the indicator lags behind as the rail is scrolled.
        indicator.style.left = (btnRect.left - containerRect.left + toggleContainer.scrollLeft) + "px";
        indicator.style.width = btnRect.width + "px";
    }

    function getActiveButton() {
        return document.querySelector('.day-btn.active') || dayButtons[0];
    }

    dayButtons.forEach(button => {
        button.addEventListener('click', () => {
            // 1. Clear 'active' from every button.
            dayButtons.forEach(btn => btn.classList.remove('active'));
            // 2. Mark the button that was clicked.
            button.classList.add('active');
            // 3. The id of the pane to show.
            const targetId = button.getAttribute('data-target');
            // 4. Hide every pane.
            tabPanes.forEach(pane => pane.classList.remove('active'));
            // 5. Show the one asked for.
            const targetPane = document.getElementById(targetId);
            if (targetPane) targetPane.classList.add('active');
            // 6. Slide the indicator to the new button.
            moveIndicatorTo(button);
            // 7. Recompute which descriptions actually overflow in the newly
            //    visible day: a hidden pane reports scrollHeight = 0.
            refreshReadMore();
        });
    });

    // The initial position, under the tab that starts active. It waits a
    // moment because the fonts may not have loaded yet, which would give the
    // wrong width.
    moveIndicatorTo(getActiveButton());
    window.addEventListener('load', () => moveIndicatorTo(getActiveButton()));

    // Scrolling the rail sideways on a phone moves the buttons too, and the
    // indicator is positioned absolutely inside the same container, so it has
    // to be recomputed or it falls behind.
    if (toggleContainer) {
        toggleContainer.addEventListener('scroll', () => {
            moveIndicatorTo(getActiveButton());
        }, { passive: true });
    }

    // On a resize — a phone rotated, a window dragged — the button widths can
    // change, so it is recomputed without animating.
    let resizeTimeout;
    window.addEventListener('resize', () => {
        clearTimeout(resizeTimeout);
        resizeTimeout = setTimeout(() => {
            syncRailOffset();
            if (indicator) indicator.style.transition = 'none';
            moveIndicatorTo(getActiveButton());
            if (indicator) {
                // The transition is restored on the next frame, so that the
                // recalculation itself is not animated.
                requestAnimationFrame(() => { indicator.style.transition = ''; });
            }
        }, 150);
    });

    // --- "Read more" for long descriptions ---
    // .session-desc is clamped to 3 lines with CSS -webkit-line-clamp (see
    // scheduleStyle.css), which puts the ellipsis exactly where the text really
    // runs out, for any content and any screen width. line-clamp cannot be
    // animated, so during the click itself the mechanism is switched to
    // max-height, which can be, and after transitionend it goes back to plain
    // line-clamp — or to fully expanded.
    //
    // Cards inside an inactive .tab-pane report scrollHeight and clientHeight
    // as 0, because the pane is display: none. That is why refreshReadMore() is
    // also called on every change of day.
    const readMorePairs = [];
    document.querySelectorAll('.session-desc').forEach(desc => {
        const btn = desc.nextElementSibling;
        if (btn && btn.classList.contains('read-more-btn')) {
            readMorePairs.push({ desc, btn });
            btn.addEventListener('click', () => {
                const expanded = btn.getAttribute('data-expanded') === 'true';
                setDescExpanded(desc, btn, !expanded);
            });
        }
    });

    function setDescExpanded(desc, btn, expand) {
        // Cancel an unfinished animation on this element, if there is one.
        if (desc._readMoreTransitionHandler) {
            desc.removeEventListener('transitionend', desc._readMoreTransitionHandler);
            desc._readMoreTransitionHandler = null;
        }

        if (expand) {
            // While still in line-clamp mode, clientHeight is exactly the
            // visible three-line height. It is recorded here because it is
            // needed on collapse — once expanded, the clamp is gone.
            const collapsedHeight = desc.clientHeight;
            desc.dataset.collapsedHeight = collapsedHeight;
            const fullHeight = desc.scrollHeight;

            // 1) Switch the mechanism (line-clamp → max-height) at the SAME
            //    visible height, so nothing moves on screen yet. Reading
            //    offsetHeight forces a reflow, or the browser would collapse
            //    this step and the next into one.
            desc.classList.add('is-animating');
            desc.style.maxHeight = collapsedHeight + 'px';
            void desc.offsetHeight;

            // 2) On the next frame the target becomes the full height — and
            //    that is the change the CSS transition actually animates.
            requestAnimationFrame(() => {
                desc.style.maxHeight = fullHeight + 'px';
            });

            const onEnd = (e) => {
                if (e.propertyName !== 'max-height') return;
                desc.removeEventListener('transitionend', onEnd);
                desc._readMoreTransitionHandler = null;
                desc.classList.remove('is-animating');
                desc.classList.add('is-expanded');
                desc.style.maxHeight = '';
            };
            desc._readMoreTransitionHandler = onEnd;
            desc.addEventListener('transitionend', onEnd);
        } else {
            const collapsedHeight = parseFloat(desc.dataset.collapsedHeight) || 0;
            const fullHeight = desc.scrollHeight;

            desc.classList.remove('is-expanded');
            desc.classList.add('is-animating');
            desc.style.maxHeight = fullHeight + 'px';
            void desc.offsetHeight;

            requestAnimationFrame(() => {
                desc.style.maxHeight = collapsedHeight + 'px';
            });

            const onEnd = (e) => {
                if (e.propertyName !== 'max-height') return;
                desc.removeEventListener('transitionend', onEnd);
                desc._readMoreTransitionHandler = null;
                desc.classList.remove('is-animating');
                desc.style.maxHeight = ''; // back to plain line-clamp mode
            };
            desc._readMoreTransitionHandler = onEnd;
            desc.addEventListener('transitionend', onEnd);
        }

        btn.setAttribute('data-expanded', String(expand));
        const label = btn.querySelector('.read-more-label');
        if (label) label.textContent = expand ? btn.dataset.labelLess : btn.dataset.labelMore;
    }

    function refreshReadMore() {
        readMorePairs.forEach(({ desc, btn }) => {
            if (btn.getAttribute('data-expanded') === 'true') return; // leave the expanded ones alone
            const truncated = desc.scrollHeight > desc.clientHeight + 1;
            btn.classList.toggle('is-visible', truncated);
        });
    }

    refreshReadMore();
    window.addEventListener('load', refreshReadMore);

    let readMoreResizeTimeout;
    window.addEventListener('resize', () => {
        clearTimeout(readMoreResizeTimeout);
        readMoreResizeTimeout = setTimeout(refreshReadMore, 150);
    });

    // --- The scroll-down arrow ---
    // It is part of the banner now (position: absolute) rather than a fixed
    // element floating over the content. The behaviour is the same and is
    // shared with the Lecturers page — see ConfApp.bindScrollArrow in
    // common.js.
    window.ConfApp.bindScrollArrow();
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSchedule);
} else {
    initSchedule();
}
