const faqItems = document.querySelectorAll(".faq-item");
const menuToggle = document.querySelector(".menu-toggle");
const mobileNavOverlay = document.getElementById("mobile-nav-overlay");
const mobileNavPanel = document.querySelector(".mobile-nav-panel");
const topbarEl = document.querySelector(".topbar");
const desktopNav = document.querySelector(".nav");

const closeFaqItem = (item) => {
  const button = item.querySelector(".faq-question");
  const answer = item.querySelector(".faq-answer");

  item.classList.remove("is-open");
  button.setAttribute("aria-expanded", "false");
  answer.style.maxHeight = "0px";
};

const openFaqItem = (item) => {
  const button = item.querySelector(".faq-question");
  const answer = item.querySelector(".faq-answer");

  item.classList.add("is-open");
  button.setAttribute("aria-expanded", "true");
  answer.style.maxHeight = `${answer.scrollHeight}px`;
};

faqItems.forEach((item, index) => {
  const button = item.querySelector(".faq-question");
  const answer = item.querySelector(".faq-answer");

  button.setAttribute("aria-expanded", "false");
  answer.style.maxHeight = "0px";
  item.classList.remove("is-open");
  answer.id = `faq-answer-${index + 1}`;
  button.setAttribute("aria-controls", answer.id);

  button.addEventListener("click", () => {
    const isOpen = item.classList.contains("is-open");

    faqItems.forEach((faqItem) => {
      closeFaqItem(faqItem);
    });

    if (!isOpen) {
      openFaqItem(item);
    }
  });
});

// --- Full-screen мобилно меню ---
// Заменя старото .nav dropdown поведение изцяло. menuToggle вече
// контролира #mobile-nav-overlay (плъзгане отляво надясно) вместо да
// показва/скрива .nav инлайн. Хамбургерът получава .is-open клас
// (не само .nav) — оттам идва завъртането му в "X" (виж mainStyle.css).
if (menuToggle && mobileNavOverlay) {
  // Реалната височина на topbar-а варира между breakpoint-ите (под
  // 640px минава на 2-редов grid и е по-висок) — мерим я реално вместо
  // да гадаем фиксирана стойност, за да не се застъпва панелът с нея.
  const syncPanelOffset = () => {
    if (mobileNavPanel && topbarEl) {
      mobileNavPanel.style.paddingTop = topbarEl.offsetHeight + "px";
    }
  };

  // Синхронизирано с CSS breakpoint-а за тесния tier — елаборираният
  // overlay (частици, промо слайдър) реално се вижда само тук.
  // На средния/широкия tier body.mobile-nav-open просто показва
  // .nav dropdown-а вместо него (виж CSS-а) — частиците/автоплеят
  // биха хабили ресурси за нещо, което е CSS-скрито и невидимо.
  const NARROW_TIER_MAX = 640;

  const openMobileNav = () => {
    syncPanelOffset();
    mobileNavOverlay.classList.add("is-open");
    mobileNavOverlay.setAttribute("aria-hidden", "false");
    menuToggle.classList.add("is-open");
    menuToggle.setAttribute("aria-expanded", "true");
    if (menuToggle.dataset.labelClose) menuToggle.setAttribute("aria-label", menuToggle.dataset.labelClose);
    document.body.classList.add("mobile-nav-open");
    if (window.innerWidth <= NARROW_TIER_MAX) {
      if (window.mobileNavParticles) window.mobileNavParticles.start();
      if (window.mobileNavPromoSlider) {
        window.mobileNavPromoSlider.refresh();
        window.mobileNavPromoSlider.start();
      }
    }
  };

  const closeMobileNav = () => {
    mobileNavOverlay.classList.remove("is-open");
    mobileNavOverlay.setAttribute("aria-hidden", "true");
    menuToggle.classList.remove("is-open");
    menuToggle.setAttribute("aria-expanded", "false");
    if (menuToggle.dataset.labelOpen) menuToggle.setAttribute("aria-label", menuToggle.dataset.labelOpen);
    document.body.classList.remove("mobile-nav-open");
    if (window.mobileNavParticles) window.mobileNavParticles.stop();
    if (window.mobileNavPromoSlider) window.mobileNavPromoSlider.stop();
  };

  menuToggle.addEventListener("click", () => {
    if (mobileNavOverlay.classList.contains("is-open")) {
      closeMobileNav();
    } else {
      openMobileNav();
    }
  });

  // Клик върху който и да е линк вътре в менюто го затваря — важно
  // при same-page anchor линкове; при обикновена навигация браузърът
  // и без друго ще презареди страницата.
  mobileNavOverlay.querySelectorAll("a").forEach((link) => {
    link.addEventListener("click", closeMobileNav);
  });

  // Средният tier преизползва desktopNav (.nav) като dropdown — линковете
  // му също затварят менюто при клик, по аналогия с overlay-я по-горе.
  if (desktopNav) {
    desktopNav.querySelectorAll("a").forEach((link) => {
      link.addEventListener("click", closeMobileNav);
    });
  }

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && mobileNavOverlay.classList.contains("is-open")) {
      closeMobileNav();
    }
  });

  // Ако прозорецът стане достатъчно широк за десктоп навигацията
  // (напр. завъртане на таблет), затваряме overlay-я вместо да виси
  // отворен зад вече показаната десктоп .nav. Ако си остава мобилен,
  // само преизмерваме topbar височината (тя се сменя между breakpoint-и).
  window.addEventListener("resize", () => {
    if (window.innerWidth > 1240 && mobileNavOverlay.classList.contains("is-open")) {
      closeMobileNav();
      return;
    }
    if (mobileNavOverlay.classList.contains("is-open")) {
      var inNarrowTier = window.innerWidth <= NARROW_TIER_MAX;
      if (inNarrowTier) {
        syncPanelOffset();
        if (window.mobileNavParticles) window.mobileNavParticles.start();
        if (window.mobileNavPromoSlider) window.mobileNavPromoSlider.start();
      } else {
        // Преминахме от тесния в средния tier, докато менюто е отворено
        // (напр. завъртане на телефона) — CSS вече показва dropdown-а
        // вместо overlay-я; частиците/автоплеят вече са невидими, спираме ги.
        if (window.mobileNavParticles) window.mobileNavParticles.stop();
        if (window.mobileNavPromoSlider) window.mobileNavPromoSlider.stop();
      }
    }
  });
}

// --- Декоративни частици в тясната лента на мобилното меню ---
// Съвсем отделен, лек particle модул — само за #mobile-nav-particles
// (тясна декоративна колона вътре в overlay-я), не за фона на цялата
// страница. Работи само докато менюто реално е отворено (виж
// start()/stop() извикванията по-горе) — нула работа, докато е
// затворено. Същата sprite-blit оптимизация (pre-rendered glow +
// drawImage, не createRadialGradient на всеки кадър) като на Schedule
// страницата.
(function () {
  const canvas = document.getElementById("mobile-nav-particles");
  const decor = document.querySelector(".mobile-nav-decor");
  if (!canvas || !decor) return;

  const ctx = canvas.getContext("2d");
  if (!ctx) return;

  if (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
    return;
  }

  const SPRITE_SIZE = 40;
  const spriteCanvas = document.createElement("canvas");
  spriteCanvas.width = SPRITE_SIZE;
  spriteCanvas.height = SPRITE_SIZE;
  const spriteCtx = spriteCanvas.getContext("2d");
  (function buildSprite() {
    const c = SPRITE_SIZE / 2;
    const grad = spriteCtx.createRadialGradient(c, c, 0, c, c, c);
    grad.addColorStop(0, "rgba(255,235,225,1)");
    grad.addColorStop(0.3, "rgba(255,77,77,0.85)");
    grad.addColorStop(0.72, "rgba(255,54,54,0.28)");
    grad.addColorStop(1, "rgba(255,54,54,0)");
    spriteCtx.fillStyle = grad;
    spriteCtx.beginPath();
    spriteCtx.arc(c, c, c, 0, Math.PI * 2);
    spriteCtx.fill();
  })();

  let width = 0;
  let height = 0;
  let dpr = 1;
  let particles = [];
  let rafId = null;

  function resize() {
    const rect = decor.getBoundingClientRect();
    width = rect.width || 120;
    height = rect.height || window.innerHeight;
    dpr = Math.min(window.devicePixelRatio || 1, 1.75);

    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);
    canvas.style.width = width + "px";
    canvas.style.height = height + "px";
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

    seed();
  }

  // Jittered grid вместо чист Math.random() x/y — гарантира равномерно
  // покритие на цялата лента от самото начало (иначе чиста случайност
  // при умерен брой точки лесно дава клъстери на едно място и празни
  // "дупки" другаде). particles.js-стил свободно движение (виж step())
  // поема нататък — mutual repulsion пази точките равномерно
  // разпределени и докато се движат, не само в началото.
  function seed() {
    particles = [];
    const targetCount = Math.max(30, Math.min(Math.round((width * height) / 950), 90));
    const aspect = width / Math.max(height, 1);
    const cols = Math.max(2, Math.round(Math.sqrt(targetCount * aspect)));
    const rows = Math.max(2, Math.ceil(targetCount / cols));
    const cellW = width / cols;
    const cellH = height / rows;

    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < cols; c++) {
        const jitterX = (Math.random() - 0.5) * cellW * 0.75;
        const jitterY = (Math.random() - 0.5) * cellH * 0.75;
        const x = Math.min(width, Math.max(0, c * cellW + cellW / 2 + jitterX));
        const y = Math.min(height, Math.max(0, r * cellH + cellH / 2 + jitterY));
        particles.push(makeParticle(x, y, true));
      }
    }
  }

  // isInitial=true при първоначален spawn (стартира на случайна фаза от
  // жизнения си цикъл, за да не пулсират всички точки синхронно) —
  // false при прераждане след fade-out (винаги стартира от 0).
  function makeParticle(x, y, isInitial) {
    const state = isInitial
      ? (Math.random() < 0.25 ? "in" : (Math.random() < 0.85 ? "hold" : "out"))
      : "in";
    return {
      x: x,
      y: y,
      vx: (Math.random() - 0.5) * 0.8,
      vy: (Math.random() - 0.5) * 0.8,
      r: 0.7 + Math.random() * 1.3,
      alpha: isInitial ? Math.random() : 0,
      targetAlpha: 0.8 + Math.random() * 0.2,
      state: state,
      holdTimer: Math.random() * 160,
      fadeSpeed: 0.008 + Math.random() * 0.012
    };
  }

  function step() {
    ctx.clearRect(0, 0, width, height);

    for (let i = 0; i < particles.length; i++) {
      const p = particles[i];
      // particles.js-стил: реално, свободно движение с отскок от
      // ръбовете — не закотвено "дишане". Клъстерите се предотвратяват
      // не чрез ограничаване на движението, а чрез лекото взаимно
      // отблъскване по-долу.
      p.x += p.vx;
      p.y += p.vy;

      // Лека случайна "турбуленция" всеки кадър — пътищата стават
      // по-органични, не перфектно механично отскачане по права линия.
      p.vx += (Math.random() - 0.5) * 0.045;
      p.vy += (Math.random() - 0.5) * 0.045;
      const maxV = 1.1;
      p.vx = Math.max(-maxV, Math.min(maxV, p.vx));
      p.vy = Math.max(-maxV, Math.min(maxV, p.vy));

      if (p.x < p.r || p.x > width - p.r) p.vx *= -1;
      if (p.y < p.r || p.y > height - p.r) p.vy *= -1;
      p.x = Math.max(p.r, Math.min(width - p.r, p.x));
      p.y = Math.max(p.r, Math.min(height - p.r, p.y));

      // Жизнен цикъл: fade in → задържане → fade out → прераждане на
      // случайно ново място. Точно това дава усещането "едни точки
      // утихват, други се появяват", вместо статичен фиксиран набор.
      if (p.state === "in") {
        p.alpha += p.fadeSpeed;
        if (p.alpha >= p.targetAlpha) {
          p.alpha = p.targetAlpha;
          p.state = "hold";
          p.holdTimer = 90 + Math.random() * 220;
        }
      } else if (p.state === "hold") {
        p.holdTimer -= 1;
        if (p.holdTimer <= 0) {
          p.state = "out";
        }
      } else {
        p.alpha -= p.fadeSpeed;
        if (p.alpha <= 0) {
          p.alpha = 0;
          p.x = p.r + Math.random() * (width - p.r * 2);
          p.y = p.r + Math.random() * (height - p.r * 2);
          p.vx = (Math.random() - 0.5) * 0.8;
          p.vy = (Math.random() - 0.5) * 0.8;
          p.r = 0.7 + Math.random() * 1.3;
          p.targetAlpha = 0.8 + Math.random() * 0.2;
          p.fadeSpeed = 0.008 + Math.random() * 0.012;
          p.state = "in";
        }
      }
    }

    // Ключовата разлика спрямо предишните версии: частиците се движат
    // напълно свободно (истинско particles.js усещане), но щом две се
    // доближат прекалено, леко се отблъскват една от друга. Точно
    // това не позволява на нищо да се "струпа" на едно място, докато
    // някъде другаде остане празно — самоорганизиращо се равномерно
    // покритие, без изкуствено закотвяне към решетка.
    const MIN_DIST = 24;
    for (let a = 0; a < particles.length; a++) {
      for (let b = a + 1; b < particles.length; b++) {
        const pa = particles[a];
        const pb = particles[b];
        const dx = pa.x - pb.x;
        const dy = pa.y - pb.y;
        const d = Math.sqrt(dx * dx + dy * dy);
        if (d > 0.01 && d < MIN_DIST) {
          const push = ((MIN_DIST - d) / MIN_DIST) * 0.6;
          const nx = dx / d;
          const ny = dy / d;
          pa.x += nx * push;
          pa.y += ny * push;
          pb.x -= nx * push;
          pb.y -= ny * push;
        }
      }
    }

    ctx.strokeStyle = "rgba(255,54,54,0.55)";
    ctx.lineWidth = 1;
    for (let a = 0; a < particles.length; a++) {
      for (let b = a + 1; b < particles.length; b++) {
        const pa = particles[a];
        const pb = particles[b];
        const dx = pa.x - pb.x;
        const dy = pa.y - pb.y;
        const d = Math.sqrt(dx * dx + dy * dy);
        if (d < 68) {
          const lifeAlpha = Math.min(pa.alpha, pb.alpha);
          if (lifeAlpha < 0.02) continue;
          ctx.globalAlpha = (1 - d / 68) * 0.7 * lifeAlpha;
          ctx.beginPath();
          ctx.moveTo(pa.x, pa.y);
          ctx.lineTo(pb.x, pb.y);
          ctx.stroke();
        }
      }
    }
    ctx.globalAlpha = 1;

    for (let i = 0; i < particles.length; i++) {
      const p = particles[i];
      if (p.alpha < 0.02) continue;
      const size = p.r * 3.4;
      const half = size / 2;
      ctx.globalAlpha = p.alpha;
      ctx.drawImage(
        spriteCanvas,
        Math.round(p.x - half),
        Math.round(p.y - half),
        Math.round(size),
        Math.round(size)
      );
    }
    ctx.globalAlpha = 1;

    rafId = requestAnimationFrame(step);
  }

  function start() {
    if (rafId !== null) return;
    resize();
    rafId = requestAnimationFrame(step);
  }

  function stop() {
    if (rafId !== null) {
      cancelAnimationFrame(rafId);
      rafId = null;
    }
  }

  let resizeTimeout;
  window.addEventListener("resize", () => {
    if (rafId === null) return; // менюто не е отворено — няма смисъл да преизчисляваме
    clearTimeout(resizeTimeout);
    resizeTimeout = setTimeout(resize, 150);
  }, { passive: true });

  window.mobileNavParticles = { start: start, stop: stop };
})();

// --- Промо слайдър в мобилното меню ---
// "Infinity-proof" индикатор: фиксирана лента (.progress-track), в
// която само thumb сегментът се преоразмерява/премества спрямо
// текущия слайд и общия им брой — вместо N отделни dots, които биха
// преляли или станали микроскопични с 10 промота от admin панела.
// Влачене на самия слайдер чрез Pointer Events (унифицира мишка/
// пръст/писалка в едно API, не са нужни отделни touch/mouse handler-и).
(function () {
  const slider = document.getElementById("mobile-nav-promo-slider");
  const track = document.getElementById("mobile-nav-promo-track");
  // progressTrack/progressThumb вече не се рендират изобщо, когато има
  // само 1 промо (виж _Layout.cshtml) — индикаторът е безсмислен с 1
  // слайд. Затова тук са опционални: слайдърът (заглавие marquee и т.н.)
  // трябва да работи и без тях, не само с 2+ промота.
  const progressTrack = document.getElementById("mobile-nav-promo-progress-track");
  const progressThumb = document.getElementById("mobile-nav-promo-progress-thumb");
  if (!slider || !track) return;

  const slides = Array.from(track.querySelectorAll(".mobile-nav-promo-slide"));
  if (slides.length === 0) return;

  let currentIndex = 0;
  let sliderWidth = slider.clientWidth;
  let isDragging = false;
  let hasDragged = false;
  let dragStartX = 0;
  let baseTranslate = 0;
  let dragResetTimeout;
  let autoplayTimer = null;
  const AUTOPLAY_MS = 6750; // беше 4500ms — точно 1.5x по-бавно

  function updateProgress() {
    if (!progressTrack || !progressThumb) return;
    const segment = 100 / slides.length;
    progressThumb.style.width = segment + "%";
    progressThumb.style.left = (segment * currentIndex) + "%";
  }

  // Кликваш directно върху лентата (не само thumb-а) — скача на
  // пропорционалния слайд спрямо позицията на клика по X. Само ако
  // индикаторът реално съществува (2+ промота).
  if (progressTrack) {
    progressTrack.addEventListener("click", (e) => {
      const rect = progressTrack.getBoundingClientRect();
      const ratio = (e.clientX - rect.left) / rect.width;
      goTo(Math.floor(ratio * slides.length));
      restartAutoplay();
    });
  }

  function goTo(index, animate) {
    // Кръгово завъртане — след последния слайд се връща на първия,
    // вместо auto-play-ят да опре в стена.
    const total = slides.length;
    currentIndex = ((index % total) + total) % total;
    track.style.transition = animate === false ? "none" : "transform 0.35s cubic-bezier(0.22, 1, 0.36, 1)";
    track.style.transform = "translateX(" + (-currentIndex * sliderWidth) + "px)";
    updateProgress();
  }

  function startAutoplay() {
    if (slides.length < 2 || autoplayTimer !== null) return;
    autoplayTimer = setInterval(() => goTo(currentIndex + 1), AUTOPLAY_MS);
  }

  function stopAutoplay() {
    if (autoplayTimer !== null) {
      clearInterval(autoplayTimer);
      autoplayTimer = null;
    }
  }

  function restartAutoplay() {
    stopAutoplay();
    startAutoplay();
  }

  // Влаченето е ирелевантно с 0-1 слайд — не пречи на клика/навигацията.
  if (slides.length > 1) {
    slider.addEventListener("pointerdown", (e) => {
      isDragging = true;
      hasDragged = false;
      dragStartX = e.clientX;
      baseTranslate = -currentIndex * sliderWidth;
      track.style.transition = "none";
      stopAutoplay(); // не се боричкаме с потребителя, докато той влачи
      slider.setPointerCapture(e.pointerId);
    });

    slider.addEventListener("pointermove", (e) => {
      if (!isDragging) return;
      const dx = e.clientX - dragStartX;
      if (Math.abs(dx) > 8) hasDragged = true;
      track.style.transform = "translateX(" + (baseTranslate + dx) + "px)";
    });

    const endDrag = (e) => {
      if (!isDragging) return;
      isDragging = false;
      const dx = e.clientX - dragStartX;
      if (Math.abs(dx) > sliderWidth * 0.18) {
        goTo(currentIndex + (dx < 0 ? 1 : -1));
      } else {
        goTo(currentIndex);
      }
      restartAutoplay();
      // Кратко задържане на hasDragged, за да не отвори линка веднага
      // след пускане на пръста (иначе влаченето би задействало навигация).
      clearTimeout(dragResetTimeout);
      dragResetTimeout = setTimeout(() => { hasDragged = false; }, 80);
    };
    slider.addEventListener("pointerup", endDrag);
    slider.addEventListener("pointercancel", endDrag);

    slides.forEach((slide) => {
      slide.addEventListener("click", (e) => {
        if (hasDragged) e.preventDefault();
      });
    });
  }

  function refresh() {
    sliderWidth = slider.clientWidth || sliderWidth;
    goTo(currentIndex, false);
    measureTitleOverflow();
  }

  // Мери РЕАЛНОТО прeливане на всяко заглавие (scrollWidth спрямо
  // видимата ширина) — само наистина непоместващите се заглавия
  // получават marquee анимация, късите просто си стоят статични.
  // Дистанцията се пресмята индивидуално за всеки слайд, не е гадаене
  // с фиксиран процент — точно толкова, колкото е нужно да се разкрие
  // напълно текста.
  function measureTitleOverflow() {
    slides.forEach((slide) => {
      const titleEl = slide.querySelector(".mobile-nav-promo-title");
      const innerEl = slide.querySelector(".mobile-nav-promo-title-inner");
      if (!titleEl || !innerEl) return;

      const overflow = innerEl.scrollWidth - titleEl.clientWidth;
      if (overflow > 2) {
        innerEl.style.setProperty("--marquee-distance", "-" + overflow + "px");
        titleEl.classList.add("is-overflowing");
      } else {
        innerEl.style.removeProperty("--marquee-distance");
        titleEl.classList.remove("is-overflowing");
      }
    });
  }

  window.addEventListener("resize", () => {
    if (slider.getBoundingClientRect().width > 0) refresh();
  }, { passive: true });

  updateProgress();
  // start/stop контролират ЕДИНСТВЕНО autoplay таймера — вика се от
  // openMobileNav()/closeMobileNav(), за да не тиктака фоново, докато
  // менюто е затворено (същата дисциплина като particles модула).
  window.mobileNavPromoSlider = { refresh: refresh, start: startAutoplay, stop: stopAutoplay };
})();

// Footer tagline marquee (виж .footer-brand-tagline в mainStyle.css) —
// огледален механизъм на measureTitleOverflow() по-горе за promo
// slider-а: мери РЕАЛНОТО преливане (scrollWidth спрямо видимата
// ширина), не гадае с фиксиран breakpoint. Само tagline-ове, които
// реално не се събират на един ред, получават плъзгащата анимация —
// къси стоят статични. Извиква се веднъж при зареждане и на всеки
// resize (viewport ширината решава дали "SHAPES THE FUTURE OF FINANCE
// EDUCATION" се събира или не).
function measureFooterTaglineOverflow() {
  const taglineEl = document.querySelector(".footer-brand-tagline");
  const innerEl = document.querySelector(".footer-brand-tagline-inner");
  if (!taglineEl || !innerEl) return;

  const overflow = innerEl.scrollWidth - taglineEl.clientWidth;
  if (overflow > 2) {
    innerEl.style.setProperty("--marquee-distance", "-" + overflow + "px");
    taglineEl.classList.add("is-overflowing");
  } else {
    innerEl.style.removeProperty("--marquee-distance");
    taglineEl.classList.remove("is-overflowing");
  }
}

measureFooterTaglineOverflow();
window.addEventListener("resize", measureFooterTaglineOverflow, { passive: true });

const revealObserver = new IntersectionObserver((entries) => {
  entries.forEach((entry) => {
    if (entry.isIntersecting) {
      entry.target.classList.add("is-visible");
      revealObserver.unobserve(entry.target);
    }
  });
}, { threshold: 0.18 });

document.querySelectorAll(".reveal").forEach((element) => {
  revealObserver.observe(element);
});

window.addEventListener("load", () => {
  document.body.classList.add("hero-ready");
});

window.addEventListener("resize", () => {
  faqItems.forEach((item) => {
    if (item.classList.contains("is-open")) {
      item.querySelector(".faq-answer").style.maxHeight = `${item.querySelector(".faq-answer").scrollHeight}px`;
    }
  });
});

// --- "Дъжд от битове" във footer intro картата ---
// Съвсем отделен, лек canvas модул — рисува 0/1 (и по някой hex знак)
// знаци, летящи отдясно наляво в .footer-intro-art (вътре в
// .footer-intro-art-frame, дясната част на голямата стъклена карта).
// Движението е хоризонтално: всеки бит се появява от десния край и се
// движи наляво, към текста.
//
// Размазването наляво е РЕАЛНО, не само CSS fade: рисуваме острите
// знаци в offscreen буфер, после композираме буфера върху видимия
// canvas на няколко ленти (BAND_COUNT), всяка със собствен
// ctx.filter = "blur(...)" — 0px най-вдясно, все по-силно наляво. CSS
// mask-image в mainStyle.css (по-дълъг fade опашка сега — виж заявка
// за "удължи ефекта наляво") добавя финалното изчезване. Два "depth"
// слоя (близки/далечни битове — различен размер/скорост/яркост) плюс
// лек синусоидален "wobble" по вертикала и случаен pulse блясък правят
// движението по-живо, не механично право. Стартира/спира през
// IntersectionObserver — footer-ът обикновено е под сгъвката, няма
// смисъл да тиктака, докато потребителят не е скролнал до него (същата
// дисциплина като mobile-nav-particles модула по-горе).
(function () {
  const canvas = document.querySelector(".footer-intro-art");
  if (!canvas) return;

  const ctx = canvas.getContext("2d");
  if (!ctx) return;

  if (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
    return;
  }

  const CHARS_BIN = ["0", "1"];
  const CHARS_HEX = ["A", "B", "C", "D", "E", "F"];
  const BAND_COUNT = 7;
  const MAX_BLUR_PX = 9;

  let width = 0;
  let height = 0;
  let dpr = 1;
  let drops = [];
  let rafId = null;

  // Offscreen буфер — тук се рисуват острите знаци всеки кадър; видимият
  // canvas само композира размазани копия на този буфер на ленти (виж
  // compositeWithBlur() по-долу). Държи се в CSS px координати (dpr
  // мащабирането е само върху видимия canvas при рисуване с drawImage).
  const buffer = document.createElement("canvas");
  const bufferCtx = buffer.getContext("2d");

  function resize() {
    const rect = canvas.getBoundingClientRect();
    width = rect.width || 1;
    height = rect.height || 1;
    dpr = Math.min(window.devicePixelRatio || 1, 1.75);

    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

    buffer.width = Math.round(width * dpr);
    buffer.height = Math.round(height * dpr);
    bufferCtx.setTransform(dpr, 0, 0, dpr, 0, 0);

    seed();
  }

  // Предимно 0/1, но с малък шанс за hex знак (A–F) — намек за реални
  // blockchain хешове, не чисто "binary rain", леко по-интересно на
  // окото без да къса усещането за "битове".
  function pickChar() {
    return Math.random() < 0.8
      ? CHARS_BIN[Math.random() < 0.5 ? 0 : 1]
      : CHARS_HEX[Math.floor(Math.random() * CHARS_HEX.length)];
  }

  // isInitial=true само при първия seed — разпръсва битовете през
  // цялата ширина веднага (иначе картата изглежда празна няколко
  // секунди, докато първите битове дойдат от десния край). При
  // прераждане (isInitial=false) винаги влиза отдясно, извън canvas-а.
  //
  // depth (0=далечен/1=близък) е евтин трик за дълбочина: близките
  // битове са по-едри, по-бързи и по-ярки; далечните — по-дребни, бавни
  // и приглушени. Рисуват се далечни→близки (виж drawDrops), за да не
  // "прескачат" визуално.
  function makeDrop(isInitial) {
    const isNear = Math.random() < 0.45;
    return {
      x: isInitial ? Math.random() * (width + 40) - 20 : width + 10 + Math.random() * 30,
      baseY: Math.random() * height,
      wobblePhase: Math.random() * Math.PI * 2,
      wobbleSpeed: 0.02 + Math.random() * 0.035,
      wobbleAmp: 2 + Math.random() * (isNear ? 6 : 3),
      depth: isNear ? 1 : 0,
      fontSize: isNear ? 14 + Math.random() * 2.5 : 10 + Math.random() * 2,
      speed: (isNear ? 1.15 : 0.6) + Math.random() * (isNear ? 1 : 0.5),
      char: pickChar(),
      baseAlpha: (isNear ? 0.4 : 0.2) + Math.random() * (isNear ? 0.42 : 0.26),
      isAccent: Math.random() < 0.8,
      flipTimer: 45 + Math.random() * 150,
      // Случаен, рядък "pulse" — кратко по-ярко/по-едро проблясване,
      // сякаш пакет данни минава през потока. pulseBoost изтлява бавно
      // обратно към 0 всеки кадър (виж drawDrops).
      pulseTimer: 140 + Math.random() * 320,
      pulseBoost: 0
    };
  }

  function seed() {
    const count = Math.max(24, Math.min(Math.round((width * height) / 560), 80));
    drops = [];
    for (let i = 0; i < count; i++) {
      drops.push(makeDrop(true));
    }
  }

  function drawDrops() {
    bufferCtx.clearRect(0, 0, width, height);
    bufferCtx.textAlign = "center";
    bufferCtx.textBaseline = "middle";

    for (let i = 0; i < drops.length; i++) {
      const d = drops[i];
      d.x -= d.speed;
      d.wobblePhase += d.wobbleSpeed;

      // Знакът се "прещраква" от време на време по пътя наляво — лек
      // digital flicker, не статичен символ през целия път.
      d.flipTimer -= 1;
      if (d.flipTimer <= 0) {
        d.char = pickChar();
        d.flipTimer = 45 + Math.random() * 150;
      }

      d.pulseTimer -= 1;
      if (d.pulseTimer <= 0) {
        d.pulseBoost = 1;
        d.pulseTimer = 160 + Math.random() * 340;
      } else if (d.pulseBoost > 0) {
        d.pulseBoost = Math.max(0, d.pulseBoost - 0.045);
      }

      if (d.x < -20) {
        drops[i] = makeDrop(false);
      }
    }

    // Отделен рисуващ проход, подреден по depth (далечните първо) — за
    // да не "изскачат" близките зад далечните; евтин depth trick без
    // да пипаме реда в самия drops масив (там редът не бива да се
    // разбърква заради respawn-а по-горе).
    const order = drops.slice().sort((a, b) => a.depth - b.depth);
    for (let i = 0; i < order.length; i++) {
      const d = order[i];
      const y = d.baseY + Math.sin(d.wobblePhase) * d.wobbleAmp;
      const alpha = Math.min(1, d.baseAlpha + d.pulseBoost * 0.55);
      const size = d.fontSize + d.pulseBoost * 2.5;

      bufferCtx.font = `${size.toFixed(1)}px "Courier New", monospace`;
      bufferCtx.fillStyle = d.isAccent
        ? `rgba(255, ${Math.round(70 + d.pulseBoost * 90)}, 70, ${alpha})`
        : `rgba(244, 242, 236, ${alpha * 0.85})`;
      bufferCtx.fillText(d.char, d.x, y);
    }
  }

  // Композира буфера на BAND_COUNT вертикални ленти върху видимия
  // canvas — лента 0 е най-вдясно (blur 0px, най-остра), последната е
  // най-вляво (blur ≈ MAX_BLUR_PX). Всяка лента чете от ЦЕЛИЯ буфер
  // (не изрязан регион), за да могат съседните пиксели извън лентата
  // да "изтекат" в размазването — без това щяха да се виждат твърди
  // шевове между лентите.
  function compositeWithBlur() {
    ctx.clearRect(0, 0, width, height);
    const bandWidth = width / BAND_COUNT;

    for (let i = 0; i < BAND_COUNT; i++) {
      const bandRight = width - i * bandWidth;
      const bandLeft = bandRight - bandWidth;
      const t = i / (BAND_COUNT - 1); // 0 вдясно → 1 вляво
      const blurPx = Math.pow(t, 1.4) * MAX_BLUR_PX;

      ctx.save();
      ctx.beginPath();
      ctx.rect(Math.max(0, bandLeft) - 0.5, 0, bandWidth + 1, height);
      ctx.clip();
      ctx.filter = blurPx > 0.05 ? `blur(${blurPx.toFixed(2)}px)` : "none";
      ctx.drawImage(buffer, 0, 0, width, height);
      ctx.restore();
    }
    ctx.filter = "none";
  }

  function step() {
    drawDrops();
    compositeWithBlur();
    rafId = requestAnimationFrame(step);
  }

  function start() {
    if (rafId !== null) return;
    resize();
    rafId = requestAnimationFrame(step);
  }

  function stop() {
    if (rafId !== null) {
      cancelAnimationFrame(rafId);
      rafId = null;
    }
  }

  let resizeTimeout;
  window.addEventListener("resize", () => {
    if (rafId === null) return; // не тиктака, докато не е видим — виж observer-а по-долу
    clearTimeout(resizeTimeout);
    resizeTimeout = setTimeout(resize, 150);
  }, { passive: true });

  const footerArtVisibility = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        start();
      } else {
        stop();
      }
    });
  }, { threshold: 0.01 });

  footerArtVisibility.observe(canvas);
})();