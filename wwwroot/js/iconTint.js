/* ─────────────────────────────────────────────────────────────────────
   ConferenceApp · Blockchain Education 2026
   Author: Viktor Georgiev
   ───────────────────────────────────────────────────────────────────── */
// iconTint.js — paints the PNG icons in the current accent colour.
//
// The problem it solves:
// The icons used to be coloured with a filter chain:
//   filter: brightness(0) … sepia(72%) hue-rotate(343deg) …
// which produces a FIXED colour. A CSS filter takes transformations, not
// colours, so it cannot read var(--accent) — and the icons stayed red whatever
// theme was active.
//
// The solution:
// A mask takes a colour directly. Its difficulty is that it wants the path of
// the image in the CSS, while the path lives in the HTML, in the src of the
// <img>. So this script moves that src into a mask on the wrapping box, hides
// the image, and paints the box with background: var(--accent).
//
// It works ONLY because the icons are WHITE SILHOUETTES: the mask reads the
// transparency, so the shape is preserved exactly.
(function () {
    'use strict';

    var SELECTORS = [
        '.icon-box img',
        '.at-row-icon img',
        '.at-extra-icon img',
        '.at-pay-icon img'
    ].join(', ');

    function tint(img) {
        var box = img.parentElement;
        if (!box || box.dataset.iconTinted === '1') return;

        var src = img.getAttribute('src');
        if (!src) return;

        // The logos are left alone: they carry their own brand colours.
        if (/\/logos\/(UNWE|ICBI|NABI|mainLogo|Error)/i.test(src)) return;

        var url = 'url("' + src.replace(/"/g, '\\"') + '")';

        box.style.setProperty('-webkit-mask-image', url);
        box.style.setProperty('mask-image', url);
        box.style.setProperty('-webkit-mask-repeat', 'no-repeat');
        box.style.setProperty('mask-repeat', 'no-repeat');
        box.style.setProperty('-webkit-mask-position', 'center');
        box.style.setProperty('mask-position', 'center');
        box.style.setProperty('-webkit-mask-size', 'contain');
        box.style.setProperty('mask-size', 'contain');
        box.style.setProperty('background-color', 'var(--accent)');

        // The image is now the shape of the mask and is not displayed itself.
        // visibility rather than display: none, so that the layout does not
        // shift.
        img.style.visibility = 'hidden';

        box.dataset.iconTinted = '1';
    }

    function run() {
        // Without mask support nothing is touched and the icons keep the
        // filter from the CSS. An out-of-date colour beats invisible icons.
        var ok = CSS && CSS.supports &&
                 (CSS.supports('mask-image', 'url(a.png)') ||
                  CSS.supports('-webkit-mask-image', 'url(a.png)'));
        if (!ok) return;

        document.querySelectorAll(SELECTORS).forEach(tint);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', run);
    } else {
        run();
    }
})();
