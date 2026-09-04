// initLecturers() се вика през guard-а в края на файла, а НЕ директно на
// DOMContentLoaded: ако скриптът бъде зареден след като събитието вече е
// минало (defer, async, динамично включване, късно преместване на тага),
// listener-ът никога не се изпълнява и филтрите спират да работят без
// никаква грешка в конзолата.
function initLecturers() {
    const filterButtons = document.querySelectorAll('.filter-btn');
    const speakerCards = document.querySelectorAll('.speaker-card');

    // --- Височината на топбара → --lc-rail-top ---
    // Релсата с филтрите е sticky и трябва да се залепи ТОЧНО под
    // фиксирания топбар. Реалната му височина се мени между
    // breakpoint-ите (при ≤640px навигацията се пренася на втори ред,
    // виж mainStyle.css), а при завъртане на телефон и при по-дълги
    // локализирани етикети тя е още различна. Фиксирана константа в
    // CSS-а оставя или процеп, или прикрит първи ред от решетката,
    // затова тук се измерва. Стойността в CSS-а остава като fallback.
    const lecturersPage = document.querySelector('.lecturers-page');
    const topbarEl = document.querySelector('.topbar');

    function syncRailOffset() {
        if (!lecturersPage || !topbarEl) return;
        const h = Math.round(topbarEl.getBoundingClientRect().height);
        if (h > 0) lecturersPage.style.setProperty('--lc-rail-top', h + 'px');
    }

    syncRailOffset();
    window.addEventListener('load', syncRailOffset);
    window.addEventListener('orientationchange', syncRailOffset);

    if (typeof ResizeObserver === 'function' && topbarEl) {
        try { new ResizeObserver(syncRailOffset).observe(topbarEl); } catch (e) { /* стар браузър */ }
    }

    // --- Броячът над решетката и празният резултат ---
    // Филтърът може да не върне нищо (напр. категория без записи в
    // базата). Без това потребителят вижда празно място и не разбира
    // дали страницата се е счупила, или просто няма лектори.
    const countEl = document.querySelector('.lc-count b');
    const emptyEl = document.querySelector('.lc-empty');

    function refreshCount() {
        const visible = [...speakerCards].filter(c => !c.classList.contains('hide')).length;
        if (countEl) countEl.textContent = visible;
        if (emptyEl) emptyEl.classList.toggle('is-visible', visible === 0);
    }

    filterButtons.forEach(button => {
        button.addEventListener('click', () => {

            // 1. Премахваме 'active' класа от всички бутони и го добавяме на натиснатия
            filterButtons.forEach(btn => btn.classList.remove('active'));
            button.classList.add('active');

            // 2. Вземаме филтъра, който сме натиснали (напр. "academic")
            const filterValue = button.getAttribute('data-filter');

            // 3. Обхождаме всички карти
            speakerCards.forEach(card => {
                // Вземаме категориите на текущата карта
                const cardCategories = (card.getAttribute('data-category') || '').split(' ');

                // Ако сме натиснали "All Speakers" или картата съдържа търсената категория
                if (filterValue === 'all' || cardCategories.includes(filterValue)) {
                    card.classList.remove('hide'); // Показваме
                } else {
                    card.classList.add('hide'); // Скриваме
                }
            });

            // 4. Опресняваме брояча и състоянието „няма резултати"
            refreshCount();

            // 5. На телефон релсата се скролва хоризонтално — плъзгаме
            //    натиснатия таб във видимата част, за да не остане
            //    активният филтър извън екрана.
            const menu = button.parentElement;
            if (menu && menu.scrollWidth > menu.clientWidth) {
                const target = button.offsetLeft - (menu.clientWidth - button.offsetWidth) / 2;
                menu.scrollTo({ left: Math.max(0, target), behavior: 'smooth' });
            }
        });
    });

    refreshCount();

    // --- Снимката в едър план (модал) ---
    // Клик върху портрета отваря квадратна плоча с голямата снимка в
    // цвят. Цялата карта е <a> към ProfileUrl (така е и в оригинала),
    // затова handler-ът прави и preventDefault, и stopPropagation —
    // иначе един клик би отворил и модала, и профила в нов ранд.
    //
    // Разметката на модала се гради тук, при първото отваряне, а не в
    // Razor-а: иначе всеки лектор ще носи скрито копие на снимката си
    // в DOM-а — при 20+ лектори това са 20+ излишни елемента.
    const grid = document.querySelector('.speakers-grid');
    const closeLabel = (grid && grid.getAttribute('data-close-label')) || 'Close';
    let backdrop = null;
    let lastFocused = null;

    function buildModal() {
        backdrop = document.createElement('div');
        backdrop.className = 'lc-modal-backdrop';
        backdrop.innerHTML =
            '<div class="lc-modal" role="dialog" aria-modal="true" aria-labelledby="lc-modal-name">' +
                '<i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>' +
                '<div class="lc-modal-photo">' +
                    '<span class="lc-modal-index"></span>' +
                    '<img alt="">' +
                '</div>' +
                '<button type="button" class="lc-modal-close">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="6" y1="6" x2="18" y2="18"></line><line x1="18" y1="6" x2="6" y2="18"></line></svg>' +
                '</button>' +
                '<div class="lc-modal-body">' +
                    '<h3 id="lc-modal-name"></h3>' +
                    '<p class="lc-modal-role"></p>' +
                    '<p class="lc-modal-org"></p>' +
                '</div>' +
            '</div>';

        backdrop.querySelector('.lc-modal-close').setAttribute('aria-label', closeLabel);
        document.body.appendChild(backdrop);

        backdrop.querySelector('.lc-modal-close').addEventListener('click', closeModal);
        // клик извън плочата затваря
        backdrop.addEventListener('click', (e) => { if (e.target === backdrop) closeModal(); });
        return backdrop;
    }

    function openModal(card) {
        if (!backdrop) buildModal();

        const img = card.querySelector('.avatar img');
        const modalImg = backdrop.querySelector('.lc-modal-photo img');
        modalImg.src = img ? img.getAttribute('src') : '';
        modalImg.alt = img ? (img.getAttribute('alt') || '') : '';

        const txt = (sel) => {
            const el = card.querySelector(sel);
            return el ? el.textContent.trim() : '';
        };

        backdrop.querySelector('.lc-modal-index').textContent = txt('.lc-index');
        backdrop.querySelector('#lc-modal-name').textContent = txt('h4');

        // Ролята и организацията може да са празни (полетата са nullable
        // в LecturerModel) — тогава редът се скрива, вместо да оставя
        // празнина в плочата.
        const roleEl = backdrop.querySelector('.lc-modal-role');
        const orgEl = backdrop.querySelector('.lc-modal-org');
        const role = txt('.role');
        const org = txt('.org');
        roleEl.textContent = role;
        roleEl.style.display = role ? '' : 'none';
        orgEl.textContent = org;
        orgEl.style.display = org ? '' : 'none';

        // Връзката към профила — само ако картата е <a> с href. Слага се
        // в плочата, защото модалът прехваща клика върху портрета: без
        // нея пътят до профила изисква затваряне и втори клик.
        const existing = backdrop.querySelector('.lc-modal-link');
        if (existing) existing.remove();

        const href = card.tagName === 'A' ? card.getAttribute('href') : null;
        const moreLabel = txt('.lc-more');
        if (href) {
            const link = document.createElement('a');
            link.className = 'lc-modal-link';
            link.href = href;
            link.target = '_blank';
            link.rel = 'noopener';
            link.innerHTML = (moreLabel || 'Profile') +
                ' <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>';
            backdrop.querySelector('.lc-modal-body').appendChild(link);
        }

        lastFocused = document.activeElement;
        backdrop.classList.add('is-open');
        lockScroll(true);
        backdrop.querySelector('.lc-modal-close').focus();
    }

    function closeModal() {
        if (!backdrop) return;
        backdrop.classList.remove('is-open');
        lockScroll(false);
        if (lastFocused && typeof lastFocused.focus === 'function') lastFocused.focus();
    }

    // Скрол лок: без компенсация за широчината на скролбара цялата
    // страница подскача вдясно в момента на отваряне.
    function lockScroll(on) {
        const sbw = window.innerWidth - document.documentElement.clientWidth;
        if (on) {
            document.body.style.paddingRight = sbw > 0 ? sbw + 'px' : '';
            document.body.style.overflow = 'hidden';
        } else {
            document.body.style.paddingRight = '';
            document.body.style.overflow = '';
        }
    }

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && backdrop && backdrop.classList.contains('is-open')) closeModal();
    });

    speakerCards.forEach(card => {
        const avatar = card.querySelector('.avatar');
        if (!avatar) return;

        // Знакът за увеличение се добавя от JS, за да не стои в
        // разметката като мъртъв елемент, ако скриптът не се зареди.
        if (!avatar.querySelector('.lc-zoom')) {
            const zoom = document.createElement('span');
            zoom.className = 'lc-zoom';
            zoom.setAttribute('aria-hidden', 'true');
            zoom.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"></circle><line x1="16" y1="16" x2="21" y2="21"></line><line x1="11" y1="8" x2="11" y2="14"></line><line x1="8" y1="11" x2="14" y2="11"></line></svg>';
            avatar.appendChild(zoom);
        }

        avatar.addEventListener('click', (e) => {
            // Картата е <a> — без тези двете кликът би отворил и профила.
            e.preventDefault();
            e.stopPropagation();
            openModal(card);
        });

        // Портретът е <div> вътре в <a> (вложен <button> в <a> е невалиден
        // HTML), затова ролята и клавиатурната поддръжка се дават тук.
        avatar.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                e.stopPropagation();
                openModal(card);
            }
        });
    });

    // --- Стрелка за скрол ---
    // Крие се щом потребителят е скролнал, и прави smooth scroll при
    // клик (не разчитаме само на CSS scroll-behavior).
    const scrollArrow = document.querySelector('.scroll-down-arrow');
    if (scrollArrow) {
        const toggleArrowVisibility = () => {
            scrollArrow.classList.toggle('is-hidden', window.scrollY > 60);
        };
        toggleArrowVisibility();
        window.addEventListener('scroll', toggleArrowVisibility, { passive: true });

        scrollArrow.addEventListener('click', (e) => {
            const targetId = scrollArrow.getAttribute('href');
            const targetEl = targetId ? document.querySelector(targetId) : null;
            if (targetEl) {
                e.preventDefault();
                targetEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
    }
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initLecturers);
} else {
    initLecturers();
}
