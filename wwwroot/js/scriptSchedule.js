document.addEventListener("DOMContentLoaded", () => {
    const dayButtons = document.querySelectorAll('.day-btn');
    const tabPanes = document.querySelectorAll('.tab-pane');
    const toggleContainer = document.querySelector('.day-toggles');
    const indicator = document.querySelector('.day-toggle-indicator');

    // --- Плъзгащ се индикатор под активния таб ---
    // Смята позицията/ширината на текущо активния бутон спрямо контейнера
    // и ги задава на индикатора инлайн — CSS transition-ът (виж
    // .day-toggle-indicator в scheduleStyle.css) прави самото плъзгане.
    function moveIndicatorTo(button) {
        if (!toggleContainer || !indicator || !button) return;
        const containerRect = toggleContainer.getBoundingClientRect();
        const btnRect = button.getBoundingClientRect();
        indicator.style.left = (btnRect.left - containerRect.left) + "px";
        indicator.style.width = btnRect.width + "px";
    }

    function getActiveButton() {
        return document.querySelector('.day-btn.active') || dayButtons[0];
    }

    dayButtons.forEach(button => {
        button.addEventListener('click', () => {
            // 1. Премахваме 'active' класа от всички бутони
            dayButtons.forEach(btn => btn.classList.remove('active'));
            // 2. Добавяме 'active' клас на кликнатия бутон
            button.classList.add('active');
            // 3. Вземаме ID-то на таба, който трябва да се покаже
            const targetId = button.getAttribute('data-target');
            // 4. Скриваме всички разписания (табове)
            tabPanes.forEach(pane => pane.classList.remove('active'));
            // 5. Показваме само това с търсеното ID
            const targetPane = document.getElementById(targetId);
            if (targetPane) targetPane.classList.add('active');
            // 6. Плъзгаме индикатора към новия активен бутон
            moveIndicatorTo(button);
            // 7. Преизчисляваме кои описания реално преливат в новия
            //    видим ден (скритите табове дават scrollHeight = 0)
            refreshReadMore();
        });
    });

    // Първоначална позиция на индикатора при зареждане (под активния по
    // подразбиране таб). Малко изчакване, защото шрифтовете може да не
    // са се заредили още в първия момент, което би дало грешна ширина.
    moveIndicatorTo(getActiveButton());
    window.addEventListener('load', () => moveIndicatorTo(getActiveButton()));

    // При resize (завъртане на телефон, преоразмеряване на прозорец) —
    // ширините на бутоните може да са различни, преизчисляваме без анимация.
    let resizeTimeout;
    window.addEventListener('resize', () => {
        clearTimeout(resizeTimeout);
        resizeTimeout = setTimeout(() => {
            if (indicator) indicator.style.transition = 'none';
            moveIndicatorTo(getActiveButton());
            if (indicator) {
                // връщаме transition-а на следващия кадър, за да не се
                // анимира самото преизчисляване при resize
                requestAnimationFrame(() => { indicator.style.transition = ''; });
            }
        }, 150);
    });

    // --- "Прочети повече" за дълги описания ---
    // .session-desc е орязано до 3 реда с CSS -webkit-line-clamp (виж
    // scheduleStyle.css) — дава перфектно многоточие точно там, където
    // текстът реално свършва, за всяко съдържание и всяка ширина на
    // екрана. Тъй като line-clamp не се анимира, по време на самия клик
    // временно превключваме на max-height (animatable) и след
    // transitionend се връщаме към чист line-clamp режим (или към
    // напълно разгънато състояние). ВАЖНО: карти вътре в неактивен
    // .tab-pane имат scrollHeight/clientHeight = 0 (display: none),
    // затова refreshReadMore() се вика и при всяка смяна на деня.
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
        // Отменяме недовършена предишна анимация на този елемент, ако има.
        if (desc._readMoreTransitionHandler) {
            desc.removeEventListener('transitionend', desc._readMoreTransitionHandler);
            desc._readMoreTransitionHandler = null;
        }

        if (expand) {
            // Докато сме все още в line-clamp режим, clientHeight = точно
            // видимата 3-редова височина — записваме я, ще ни трябва при
            // затваряне (веднъж разгънато, line-clamp вече го няма).
            const collapsedHeight = desc.clientHeight;
            desc.dataset.collapsedHeight = collapsedHeight;
            const fullHeight = desc.scrollHeight;

            // 1) Сменяме механизма (line-clamp → max-height), но със
            //    СЪЩАТА видима височина — визуално нищо не помръдва все
            //    още. Насилваме reflow (четене на offsetHeight), за да не
            //    "изяде" браузърът тази стъпка и следващата в едно.
            desc.classList.add('is-animating');
            desc.style.maxHeight = collapsedHeight + 'px';
            void desc.offsetHeight;

            // 2) На следващия кадър сменяме target-а към пълната височина
            //    — тук CSS transition-ът реално "хваща" промяната.
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
                desc.style.maxHeight = ''; // обратно към чист line-clamp режим
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
            if (btn.getAttribute('data-expanded') === 'true') return; // не пипаме отворените
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

    // --- Стрелка за скрол (само десктоп — CSS я скрива на мобилен) ---
    // position: fixed към viewport-а гарантира вярна позиция навсякъде;
    // тук просто я скриваме, щом потребителят вече е скролнал надолу.
    const scrollArrow = document.querySelector('.scroll-down-arrow');
    if (scrollArrow) {
        const toggleArrowVisibility = () => {
            scrollArrow.classList.toggle('is-hidden', window.scrollY > 60);
        };
        toggleArrowVisibility();
        window.addEventListener('scroll', toggleArrowVisibility, { passive: true });

        // Гарантиран smooth scroll при клик — не разчитаме само на CSS
        // scroll-behavior (глобално зададено, но това е допълнителна
        // сигурност хем на телефон, хем на лаптоп).
        scrollArrow.addEventListener('click', (e) => {
            const targetId = scrollArrow.getAttribute('href');
            const targetEl = targetId ? document.querySelector(targetId) : null;
            if (targetEl) {
                e.preventDefault();
                targetEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
    }
});