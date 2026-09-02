document.addEventListener("DOMContentLoaded", () => {
    const filterButtons = document.querySelectorAll('.filter-btn');
    const speakerCards = document.querySelectorAll('.speaker-card');

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
                const cardCategories = card.getAttribute('data-category').split(' ');

                // Ако сме натиснали "All Speakers" или картата съдържа търсената категория
                if (filterValue === 'all' || cardCategories.includes(filterValue)) {
                    card.classList.remove('hide'); // Показваме
                } else {
                    card.classList.add('hide'); // Скриваме
                }
            });
        });
    });
});