/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
const menuToggle = document.querySelector(".menu-toggle");
const mobileNavOverlay = document.getElementById("mobile-nav-overlay");
const mobileNavPanel = document.querySelector(".mobile-nav-panel");
const desktopNav = document.querySelector(".nav");

// --- The full-screen mobile menu ---
// It replaces the old .nav dropdown behaviour entirely. menuToggle now drives
// #mobile-nav-overlay, which slides in from the left, instead of showing and
// hiding .nav inline. The hamburger itself gets the .is-open class — that is
// what turns it into an "X" (see mainStyle.css).
if (menuToggle && mobileNavOverlay) {
  // The real height of the top bar varies between breakpoints — below 640px
  // it becomes a two-row grid and is taller — so it is measured rather than
  // guessed, or the panel overlaps it.
  // The measurement comes from ConfApp.topbarHeight (common.js), the same
  // .topbar the three CSS variables above use. Here the value goes into an
  // inline padding rather than into a variable, so only the reading is
  // shared.
  const syncPanelOffset = () => {
    if (mobileNavPanel) {
      mobileNavPanel.style.paddingTop = window.ConfApp.topbarHeight() + "px";
    }
  };

  // Kept in step with the CSS breakpoint for the narrow tier, because the
  // elaborate overlay — the particles, the promo slider — is only visible
  // there. On the middle and wide tiers body.mobile-nav-open simply shows the
  // .nav dropdown instead (see the CSS), and the particles and the autoplay
  // would be burning cycles on something hidden by CSS.
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

  // A click on any link inside the menu closes it. That matters for
  // same-page anchors; for an ordinary navigation the browser reloads the page
  // anyway.
  mobileNavOverlay.querySelectorAll("a").forEach((link) => {
    link.addEventListener("click", closeMobileNav);
  });

  // The middle tier reuses desktopNav (.nav) as a dropdown, so its links
  // close the menu on click too, exactly as the overlay's do.
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

  // If the window becomes wide enough for the desktop navigation — a tablet
  // rotated, say — the overlay is closed rather than left hanging open behind
  // the .nav that is now showing. If it stays mobile, only the top bar height
  // is measured again, since that changes between breakpoints.
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
        // We have crossed from the narrow tier into the middle one with the
        // menu open — a phone rotated, say. The CSS now shows the dropdown
        // instead of the overlay, so the particles and the autoplay are
        // invisible and are stopped.
        if (window.mobileNavParticles) window.mobileNavParticles.stop();
        if (window.mobileNavPromoSlider) window.mobileNavPromoSlider.stop();
      }
    }
  });
}

// --- The decorative particles in the strip of the mobile menu ---
// A small, self-contained particle module for #mobile-nav-particles alone —
// the narrow decorative column inside the overlay, not a background for the
// whole page. It runs only while the menu is actually open (see the start()
// and stop() calls above) and does nothing at all while it is closed.
// The same sprite-blit optimisation as on the Schedule page: a pre-rendered
// glow drawn with drawImage, rather than a createRadialGradient per frame.
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

  // A jittered grid rather than plain random x/y: it guarantees even coverage
  // of the whole strip from the first frame. With a moderate number of points,
  // pure randomness easily clusters them in one place and leaves holes
  // elsewhere. The free movement in step() takes over from there, and the
  // mutual repulsion keeps them evenly spread while they move — not only at
  // the start.
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

  // isInitial is true for the first spawn, where each point starts at a
  // random phase of its life cycle so that they do not all pulse in unison;
  // false on respawn after a fade-out, where the cycle starts at 0.
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
      // Real, free movement with a bounce off the edges rather than an
      // anchored "breathing". Clustering is prevented not by constraining the
      // movement but by the gentle mutual repulsion below.
      p.x += p.vx;
      p.y += p.vy;

      // A little random turbulence each frame, so the paths look organic
      // instead of a perfectly mechanical bounce along a straight line.
      p.vx += (Math.random() - 0.5) * 0.045;
      p.vy += (Math.random() - 0.5) * 0.045;
      const maxV = 1.1;
      p.vx = Math.max(-maxV, Math.min(maxV, p.vx));
      p.vy = Math.max(-maxV, Math.min(maxV, p.vy));

      if (p.x < p.r || p.x > width - p.r) p.vx *= -1;
      if (p.y < p.r || p.y > height - p.r) p.vy *= -1;
      p.x = Math.max(p.r, Math.min(width - p.r, p.x));
      p.y = Math.max(p.r, Math.min(height - p.r, p.y));

      // The life cycle: fade in, hold, fade out, respawn somewhere else.
      // That is what gives the impression of some points settling while others
      // appear, rather than a fixed set sitting there.
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

    // The key difference from the earlier versions: the particles move
    // entirely freely, but two that come too close push each other apart
    // gently. That is what stops anything piling up in one place while
    // somewhere else is left empty — self-organising even coverage, without
    // pinning anything to a grid.
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
    if (rafId === null) return; // the menu is closed: nothing to recompute
    clearTimeout(resizeTimeout);
    resizeTimeout = setTimeout(resize, 150);
  }, { passive: true });

  window.mobileNavParticles = { start: start, stop: stop };
})();

// --- The promo slider in the mobile menu ---
// The indicator is one fixed track (.progress-track) inside which only the
// thumb resizes and moves, according to the current slide and how many there
// are — rather than N separate dots, which would overflow or become
// microscopic once an administrator adds ten promos.
// Dragging uses Pointer Events, which unify mouse, finger and stylus in one
// API and make separate touch and mouse handlers unnecessary.
(function () {
  const slider = document.getElementById("mobile-nav-promo-slider");
  const track = document.getElementById("mobile-nav-promo-track");
  // progressTrack and progressThumb are not rendered at all when there is
  // only one promo (see _Layout.cshtml): an indicator for a single slide means
  // nothing. They are therefore optional here — the rest of the slider, the
  // title marquee and so on, has to work without them.
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
  const AUTOPLAY_MS = 6750; // 1.5x the original 4500ms: there was not enough
                            // time to read a slide before it moved on

  function updateProgress() {
    if (!progressTrack || !progressThumb) return;
    const segment = 100 / slides.length;
    progressThumb.style.width = segment + "%";
    progressThumb.style.left = (segment * currentIndex) + "%";
  }

  // Clicking the track itself, not only the thumb, jumps to the slide whose
  // share of the track was clicked. Only when the indicator exists at all,
  // which means two or more promos.
  if (progressTrack) {
    progressTrack.addEventListener("click", (e) => {
      const rect = progressTrack.getBoundingClientRect();
      const ratio = (e.clientX - rect.left) / rect.width;
      goTo(Math.floor(ratio * slides.length));
      restartAutoplay();
    });
  }

  function goTo(index, animate) {
    // It wraps around: after the last slide it returns to the first, rather
    // than the autoplay running into a wall.
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

  // Dragging is meaningless with zero or one slide, and the listeners would
  // only get in the way of the click and the navigation.
  if (slides.length > 1) {
    slider.addEventListener("pointerdown", (e) => {
      isDragging = true;
      hasDragged = false;
      dragStartX = e.clientX;
      baseTranslate = -currentIndex * sliderWidth;
      track.style.transition = "none";
      stopAutoplay(); // do not fight the user while they are dragging
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
      // hasDragged is held briefly, so that releasing a finger at the end of
      // a drag does not open the link underneath it.
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

  // Measures the REAL overflow of each title (scrollWidth against the visible
  // width), so that only the titles that genuinely do not fit get the marquee
  // animation and the short ones stay still. The distance is computed per
  // slide rather than guessed as a fixed percentage: exactly as far as it
  // takes to reveal the whole text.
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
  // start and stop control the autoplay timer ONLY. They are called from
  // openMobileNav() and closeMobileNav(), so that nothing ticks in the
  // background while the menu is closed — the same discipline as the particle
  // module above.
  window.mobileNavPromoSlider = { refresh: refresh, start: startAutoplay, stop: stopAutoplay };
})();

// The footer tagline marquee (see .footer-brand-tagline in mainStyle.css).
// The same mechanism as measureTitleOverflow() above for the promo slider: it
// measures the REAL overflow (scrollWidth against the visible width) rather
// than guessing from a breakpoint. Only a tagline that genuinely does not fit
// on one line gets the sliding animation; a short one stays still.
// Called once on load and on every resize — the viewport width decides whether
// "SHAPES THE FUTURE OF FINANCE EDUCATION" fits or not.
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

// The same mechanism as [data-reveal] on travel and faq, but with .reveal,
// .is-visible, a different threshold and NO arming — everything is observed,
// not only what is below the fold. Hence the arguments.
window.ConfApp.revealOnScroll({
  selector: ".reveal",
  visibleClass: "is-visible",
  threshold: 0.18,
  rootMargin: "0px",
  arm: false
});

window.addEventListener("load", () => {
  document.body.classList.add("hero-ready");
});

// --- The "bit rain" in the footer intro card ---
// A small, self-contained canvas module. It draws 0s and 1s — and the
// occasional hex character — flying from right to left in .footer-intro-art,
// inside .footer-intro-art-frame, the right-hand part of the large glass card.
// The movement is horizontal: each bit appears at the right edge and travels
// left, towards the text.
//
// The blur towards the left is REAL, not merely a CSS fade: the sharp
// characters are drawn into an offscreen buffer, and that buffer is then
// composited onto the visible canvas in several vertical bands (BAND_COUNT),
// each with its own ctx.filter = "blur(…)" — 0px at the right, increasing
// leftwards. The mask-image in mainStyle.css adds the final disappearance.
// Two depth layers — near and far bits, differing in size, speed and
// brightness — plus a slight vertical wobble and an occasional pulse keep the
// movement alive rather than mechanically straight.
// Started and stopped by an IntersectionObserver: the footer is usually below
// the fold and there is no point ticking until somebody has scrolled to it
// (the same discipline as the mobile-nav particles above).
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

  // ── Цветовете идват от темата ─────────────────────────────────────
  // Знаците се рисуваха в закован червен и в закован rgba(244, 242, 236)
  // — стойността на --text при тъмната тема. Нито един от двата не
  // следваше темата: при Кварц дъждът валеше червено върху крем, а
  // неакцентните знаци бяха крем върху крем, тоест невидими.
  //
  // Токените се четат ВЕДНЪЖ тук. Темата се сглобява на сървъра
  // (ThemeProvider изписва <style> при рендиране на страницата) и стига до
  // браузъра само с ново зареждане на документа — няма смяна в движение,
  // която да се следи. А това се рисува шейсет пъти в секунда.
  //
  // Четенето минава през временен елемент, а не през getPropertyValue:
  // потребителските свойства не се разгръщат, така че --accent би се
  // върнало като текст — веднъж "#6f9fd0", друг път "color-mix(…)".
  //
  // Изчисленият цвят идва в два различни записа и двата се срещат тук:
  //
  //   rgb(111, 159, 208)              — при шестнайсетичен токен, 0–255
  //   color(srgb 0.593 0.729 0.867)   — при color-mix, 0–1
  //
  // Вторият е записът на --accent-light във всяка тема. Разчетен като
  // 0–255 той дава почти черно, тоест пулсът потъмнява вместо да
  // изсветлява — затова записът се разпознава, а не се предполага.
  function parseColor(got) {
    // Името на цветовото пространство се маха преди търсенето на числа:
    // "display-p3" носи цифра, която иначе би минала за компонента.
    const isColorFn = got.indexOf("color(") === 0;
    const body = isColorFn ? got.slice(6).replace(/^[a-z0-9-]+/i, "") : got;
    const parts = body.match(/-?[\d.]+(?:e-?\d+)?/gi);
    if (!parts || parts.length < 3) return null;
    const scale = isColorFn ? 255 : 1;
    const rgb = [];
    for (let i = 0; i < 3; i++) {
      const v = Number(parts[i]);
      if (!isFinite(v)) return null;
      rgb.push(Math.max(0, Math.min(255, Math.round(v * scale))));
    }
    return rgb;
  }

  function readRgb(expr, fallback) {
    try {
      const probe = document.createElement("span");
      probe.style.cssText =
        "position:absolute;left:-9999px;top:0;visibility:hidden;pointer-events:none";
      probe.style.color = expr;
      document.documentElement.appendChild(probe);
      const got = getComputedStyle(probe).color;
      probe.remove();
      // Резервните стойности са точно старите заковани цветове, така че при
      // неуспех се рисува както досега, вместо да не се рисува нищо.
      return parseColor(got) || fallback;
    } catch (err) {
      return fallback;
    }
  }

  const ACCENT = readRgb("var(--accent)", [255, 70, 70]);
  const TEXT = readRgb("var(--text)", [244, 242, 236]);

  // Пулсът избледняваше червеното към оранжево — зелената съставка растеше
  // от 70 до 160, останалите две стояха. Върхът вече е --accent-light:
  // светлото стъпало на самата тема. Не се пресмята примес тук, защото
  // всяка тема сама решава как изглежда нейният светъл нюанс; тема без
  // свой го получава от :root в mainStyle.css.
  //
  // Резервната стойност е точно старият връх rgb(255, 160, 70), така че
  // при неуспешно четене пулсът избледнява както досега.
  const ACCENT_PULSE = readRgb("var(--accent-light)", [255, 160, 70]);

  let width = 0;
  let height = 0;
  let dpr = 1;
  let drops = [];
  let rafId = null;

  // The offscreen buffer: the sharp characters are drawn here every frame,
  // and the visible canvas only composites blurred copies of it in bands (see
  // compositeWithBlur below). It is kept in CSS pixel coordinates; the
  // device-pixel-ratio scaling applies to the visible canvas alone, at
  // drawImage time.
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

  // Mostly 0 and 1, with a small chance of a hex character (A–F): a hint of a
  // real hash rather than plain binary rain. A little more interesting to look
  // at without breaking the impression of bits.
  function pickChar() {
    return Math.random() < 0.8
      ? CHARS_BIN[Math.random() < 0.5 ? 0 : 1]
      : CHARS_HEX[Math.floor(Math.random() * CHARS_HEX.length)];
  }

  // isInitial is true for the first seed only: the bits are scattered across
  // the full width at once, or the card looks empty for a few seconds while
  // the first of them travel in from the right edge. On respawn they always
  // enter from the right, outside the canvas.
  //
  // depth (0 = far, 1 = near) is a cheap depth trick: the near bits are
  // larger, faster and brighter; the far ones smaller, slower and dimmer. They
  // are drawn far to near (see drawDrops) so that nothing visually jumps in
  // front of something it should be behind.
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
      // A rare random pulse: a brief brighter, larger flash, as though a
      // packet of data were passing through the stream. pulseBoost decays
      // slowly back to 0 each frame (see drawDrops).
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

      // The character flips from time to time on its way left: a slight
      // digital flicker rather than one static symbol the whole way.
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

    // A separate drawing pass ordered by depth, far ones first, so that the
    // near bits never appear behind the far ones. A cheap depth trick that
    // leaves the order of the drops array alone — that order must not be
    // shuffled, because of the respawn above.
    const order = drops.slice().sort((a, b) => a.depth - b.depth);
    for (let i = 0; i < order.length; i++) {
      const d = order[i];
      const y = d.baseY + Math.sin(d.wobblePhase) * d.wobbleAmp;
      const alpha = Math.min(1, d.baseAlpha + d.pulseBoost * 0.55);
      const size = d.fontSize + d.pulseBoost * 2.5;

      bufferCtx.font = `${size.toFixed(1)}px "Courier New", monospace`;
      if (d.isAccent) {
        // Смесването е три умножения на знак — същата аритметика, която
        // тук и досега се правеше на всеки кадър. Скъпото беше четенето на
        // токена и то вече е отвън.
        const t = d.pulseBoost;
        const r = Math.round(ACCENT[0] + (ACCENT_PULSE[0] - ACCENT[0]) * t);
        const g = Math.round(ACCENT[1] + (ACCENT_PULSE[1] - ACCENT[1]) * t);
        const b = Math.round(ACCENT[2] + (ACCENT_PULSE[2] - ACCENT[2]) * t);
        bufferCtx.fillStyle = `rgba(${r}, ${g}, ${b}, ${alpha})`;
      } else {
        bufferCtx.fillStyle =
          `rgba(${TEXT[0]}, ${TEXT[1]}, ${TEXT[2]}, ${alpha * 0.85})`;
      }
      bufferCtx.fillText(d.char, d.x, y);
    }
  }

  // Composites the buffer onto the visible canvas in BAND_COUNT vertical
  // bands: band 0 is the rightmost and sharpest (blur 0px), the last is the
  // leftmost (blur ≈ MAX_BLUR_PX). Each band reads from the WHOLE buffer
  // rather than from a clipped region, so that the pixels just outside it can
  // bleed into the blur — without that, hard seams show between the bands.
  function compositeWithBlur() {
    ctx.clearRect(0, 0, width, height);
    const bandWidth = width / BAND_COUNT;

    for (let i = 0; i < BAND_COUNT; i++) {
      const bandRight = width - i * bandWidth;
      const bandLeft = bandRight - bandWidth;
      const t = i / (BAND_COUNT - 1); // 0 at the right → 1 at the left
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
    if (rafId === null) return; // nothing ticks while it is off screen — see the observer below
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