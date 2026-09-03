/* ============================================================================
 * colorFlash.js — the COLOUR PICK FLASH.
 *
 * When the player answers one of the Calibration colour-preference questions
 * (core/palette.js), the whole screen briefly washes with a deep, saturated
 * version of the colour they just chose: ~1s, fading in and out, peaking near
 * 40% — the app's "Pink filter" look, tinted to their pick instead of pink.
 *
 * IT MATCHES effects.js's PINK WASH DELIBERATELY: same radial-gradient tint on
 * a fullscreen div, same `mix-blend-mode: screen`, same WAAPI in/hold/out
 * envelope, same pointer-events:none. The only differences are the hue (theirs,
 * not the theme accent), the shorter life, and that this layer is NOT part of
 * the garnish rotation — it is a direct response to one specific answer.
 *
 * PARENTED TO <body>, NOT THE STAGE. render/beats.js does `stage.innerHTML = ''`
 * at the top of every render, so anything on the stage dies the instant the
 * player answers — which is exactly when this fires. Same reason effects.js
 * hosts its garnish/ambient/burst layers on <body> (see bodyHost()/reattach()).
 *
 * z-index 8: above the stage (2), hud (3), aside (4), the garnish/burst layers
 * (5), the interstitial shell overlay (6) and the glitch blackout (7); below the
 * loader (10) so a flash can never paint over a boot/teardown screen.
 *
 * REDUCED MOTION: heavily softened, never skipped outright — a third of the
 * peak alpha over a shorter, gentler envelope, so the answer still lands
 * visually without a bright pulse.
 *
 * CAPS: the peak alpha runs through clampIntensity(), so the user's
 * masterIntensity governs it like every other visual (invariant #2).
 *
 * IMPORT-SIDE-EFFECT FREE: no DOM access at module load. Never throws.
 * ==========================================================================*/

import { clampIntensity, clamp01 } from '../core/contracts.js';

/** Peak opacity of the wash (pre-caps). The brief: "roughly 40%". */
export const FLASH_PEAK_ALPHA = 0.40;
/** Total life, fade-in + hold + fade-out. The brief: "~1 second total". */
export const FLASH_DURATION_MS = 1000;
/** Reduced-motion softening: a third of the peak over a shorter, flatter life. */
export const FLASH_REDUCED_ALPHA_SCALE = 0.35;
export const FLASH_REDUCED_DURATION_MS = 700;

const STYLE_ID = 'ixcf-styles';
const CSS = `
.ixcf-root{position:fixed;inset:0;z-index:8;pointer-events:none;overflow:hidden;}
.ixcf-wash{position:absolute;inset:0;opacity:0;mix-blend-mode:screen;will-change:opacity;}`;

function ensureStyles() {
  if (typeof document === 'undefined') return;
  if (document.getElementById(STYLE_ID)) return;
  try {
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) { /* non-fatal */ }
}

/** Live probe, called at flash time (never at import) — matches beats.js. */
function prefersReducedMotion() {
  try {
    return typeof window !== 'undefined' && !!window.matchMedia
      && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch (_e) { return false; }
}

/* One layer for the page; rebuilt on demand if something tore it out. */
let root = null;

function ensureRoot() {
  if (typeof document === 'undefined' || !document.body) return null;
  if (root && root.parentNode) return root;
  ensureStyles();
  try {
    root = document.createElement('div');
    root.className = 'ixcf-root';
    root.setAttribute('aria-hidden', 'true');
    document.body.appendChild(root);
  } catch (_e) { root = null; }
  return root;
}

function removeNode(el) {
  try { if (el && el.parentNode) el.parentNode.removeChild(el); } catch (_e) {}
}

/**
 * Wash the screen with `hex`. Non-blocking, self-cleaning, never throws.
 * @param {string} hex   the DEEPENED pick (core/palette.js deepen()).
 * @param {Object=} opts { caps } — user caps (DEFAULT_CAPS shape).
 * @returns {boolean} true if a wash was mounted.
 */
export function flashColor(hex, opts) {
  const o = opts || {};
  if (typeof document === 'undefined' || !hex) return false;
  const host = ensureRoot();
  if (!host) return false;
  const reduced = prefersReducedMotion();
  const peak = clamp01(clampIntensity(
    FLASH_PEAK_ALPHA * (reduced ? FLASH_REDUCED_ALPHA_SCALE : 1), o.caps));
  if (peak <= 0.001) return false;
  const durMs = reduced ? FLASH_REDUCED_DURATION_MS : FLASH_DURATION_MS;

  try {
    const el = document.createElement('div');
    el.className = 'ixcf-wash';
    // Same shape as effects.js's pink wash: a soft radial bloom off-centre so
    // the tint has depth instead of reading as a flat colour card.
    el.style.background = `radial-gradient(circle at 50% 45%,${hex} 0%,${hex} 45%,rgba(0,0,0,0) 140%)`;
    host.appendChild(el);

    const supportsAnim = typeof Element !== 'undefined' && typeof Element.prototype.animate === 'function';
    if (supportsAnim) {
      // fade IN to the peak by ~35% of the life, brief hold, gentle fade OUT —
      // no hard cut in either direction.
      const a = el.animate(
        [{ opacity: 0 }, { opacity: peak, offset: 0.35 }, { opacity: peak, offset: 0.55 }, { opacity: 0 }],
        { duration: durMs, easing: 'ease-in-out' });
      a.onfinish = () => removeNode(el);
      setTimeout(() => removeNode(el), durMs + 250); // safety net if onfinish never lands
    } else {
      // No WAAPI: a CSS transition still gives a real fade, never a hard cut.
      el.style.transition = `opacity ${Math.round(durMs * 0.35)}ms ease-in-out`;
      setTimeout(() => { try { el.style.opacity = String(peak); } catch (_e) {} }, 16);
      setTimeout(() => { try { el.style.opacity = '0'; } catch (_e) {} }, Math.round(durMs * 0.55));
      setTimeout(() => removeNode(el), durMs + 250);
    }
    return true;
  } catch (_e) { return false; }
}

/** Tear the layer out (run teardown / host dispose). Never throws. */
export function disposeColorFlash() {
  removeNode(root);
  root = null;
}
