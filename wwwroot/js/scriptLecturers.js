/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
// initLecturers() is called through the guard at the end of the file and NOT
// directly on DOMContentLoaded: if the script is loaded after that event has
// already fired (defer, async, a dynamic include, the tag moved later), the
// listener never runs and the filters stop working with no error in the
// console at all.
function initLecturers() {
    const filterButtons = document.querySelectorAll('.filter-btn');
    const speakerCards = document.querySelectorAll('.speaker-card');

    // --- The top bar height → --lc-rail-top ---
    // The filter rail is sticky and has to sit EXACTLY under the fixed top
    // bar. The real height of that bar changes between breakpoints (at ≤640px
    // the navigation wraps onto a second line, see mainStyle.css), and changes
    // again when a phone is rotated or a translated label is longer. A fixed
    // constant in the CSS leaves either a gap or a hidden first row of the
    // grid, so it is measured here. The CSS value stays as a fallback.
    // The measuring and its listeners live in ConfApp.syncTopbarVar
    // (common.js); what is left here is the variable name and where to write
    // it.
    const lecturersPage = document.querySelector('.lecturers-page');
    if (lecturersPage) window.ConfApp.syncTopbarVar('--lc-rail-top', lecturersPage);

    // --- The count above the grid, and the empty state ---
    // A filter can match nothing — a category with no rows in the database,
    // say. Without this the visitor is left looking at an empty space, unable
    // to tell whether the page is broken or the category is simply empty.
    const countEl = document.querySelector('.lc-count b');
    const emptyEl = document.querySelector('.lc-empty');

    function refreshCount() {
        const visible = [...speakerCards].filter(c => !c.classList.contains('hide')).length;
        if (countEl) countEl.textContent = visible;
        if (emptyEl) emptyEl.classList.toggle('is-visible', visible === 0);
    }

    filterButtons.forEach(button => {
        button.addEventListener('click', () => {

            // 1. Move the 'active' class onto the button just pressed.
            filterButtons.forEach(btn => btn.classList.remove('active'));
            button.classList.add('active');

            // 2. The filter that was pressed, for example "academic".
            const filterValue = button.getAttribute('data-filter');

            // 3. Walk the cards.
            speakerCards.forEach(card => {
                // The categories of this card.
                const cardCategories = (card.getAttribute('data-category') || '').split(' ');

                // "All Speakers", or a card that carries the chosen category.
                if (filterValue === 'all' || cardCategories.includes(filterValue)) {
                    card.classList.remove('hide');
                } else {
                    card.classList.add('hide');
                }
            });

            // 4. Refresh the count and the empty state.
            refreshCount();

            // 5. On a phone the rail scrolls sideways, so the button just
            //    pressed is brought into view — otherwise the active filter can
            //    end up off screen.
            const menu = button.parentElement;
            if (menu && menu.scrollWidth > menu.clientWidth) {
                const target = button.offsetLeft - (menu.clientWidth - button.offsetWidth) / 2;
                menu.scrollTo({ left: Math.max(0, target), behavior: 'smooth' });
            }
        });
    });

    refreshCount();

    // --- The portrait, shown large ---
    // Clicking the portrait opens a square card with the photograph in colour.
    // The whole card is an <a> to ProfileUrl, so the handler calls both
    // preventDefault and stopPropagation — otherwise one click would open the
    // modal and the profile at the same time.
    //
    // The modal itself lives in lecturerModal.js: one object for this page and
    // for the home page, instead of two copies that had already drifted
    // apart.
    const grid = document.querySelector('.speakers-grid');
    const closeLabel = (grid && grid.getAttribute('data-close-label')) || 'Close';

    function openModal(card) {
        const txt = (sel) => {
            const el = card.querySelector(sel);
            return el ? el.textContent.trim() : '';
        };
        const img = card.querySelector('.avatar img');

        // No biography is passed: .lc-modal-bio has a rule in indexStyle.css
        // only, and this page loads lecturersStyle.css.
        window.ConfApp.openLecturerModal({
            index:      txt('.lc-index'),
            name:       txt('h4'),
            role:       txt('.role'),
            org:        txt('.org'),
            // The link to the profile, only if the card really is an <a> with
            // an href.
            url:        card.tagName === 'A' ? card.getAttribute('href') : null,
            moreLabel:  txt('.lc-more') || 'Profile',
            closeLabel: closeLabel,
            imgSrc:     img ? img.getAttribute('src') : '',
            imgAlt:     img ? (img.getAttribute('alt') || '') : ''
        });
    }

    speakerCards.forEach(card => {
        const avatar = card.querySelector('.avatar');
        if (!avatar) return;

        window.ConfApp.addZoomIcon(avatar);

        avatar.addEventListener('click', (e) => {
            // The card is an <a>: without these two the click would open the
            // profile as well.
            e.preventDefault();
            e.stopPropagation();
            openModal(card);
        });

        // The portrait is a <div> inside an <a> — a nested <button> would be
        // invalid HTML — so the role and the keyboard support are added here.
        avatar.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                e.stopPropagation();
                openModal(card);
            }
        });
    });

    // --- The scroll-down arrow --- see ConfApp.bindScrollArrow in
    // common.js.
    window.ConfApp.bindScrollArrow();
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initLecturers);
} else {
    initLecturers();
}
