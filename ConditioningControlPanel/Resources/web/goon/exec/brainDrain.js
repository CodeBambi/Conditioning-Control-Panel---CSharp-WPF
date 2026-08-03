/* ============================================================================
 * exec/brainDrain.js — GoonElement.BrainDrain (6) + GoonPayloadKind.BrainDrain (6).
 *
 * The heavy. A full-window veil on #gg-fx-drain: a backdrop-blur pane plus a
 * tinted vignette that pulses slowly. Intensity moves two dials — blur radius
 * and veil opacity — and nothing else, because "more blur" is the whole idea.
 *
 * SAFETY, and it is not negotiable: this layer is z30 and the mercy button is
 * z60 with `isolation:isolate` (goon.css's DO-NOT-TOUCH block). The veil can
 * never cover the way out, and nothing in this file may raise a z-index or reach
 * outside its own layer to "cover everything" — the layer IS the cover.
 *
 * COST: backdrop-filter is the most expensive thing this page can draw. Exactly
 * ONE blur pane exists at a time — the payload does not stack a second one, it
 * raises the running one and puts it back afterwards — and the element and the
 * payload share it through a small dial stack.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};

/** Cue intensity -> the two dials. Calm keeps the veil, drops the pulse + blur. */
export function drainTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    blurPx: +(calm ? lerp(0, 4, i) : lerp(1.5, 11, i)).toFixed(2),
    opacity: +lerp(0.18, 0.72, i).toFixed(3),
    fadeMs: calm ? 1400 : 900,
  };
}

export function createBrainDrain({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:braindrain] ${m}`); };
  const calm = reducedMotion();

  let blurEl = null;
  let tintEl = null;
  let elementIntensity = null;   // null = element not running
  let payloadIntensity = null;   // null = no payload running

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('drain') : null);

  function ensureNodes() {
    if (blurEl && blurEl.isConnected) return true;
    const host = layer();
    if (!host || typeof document === 'undefined') return false;
    blurEl = document.createElement('div');
    blurEl.className = 'gg-drain-blur';
    tintEl = document.createElement('div');
    tintEl.className = calm ? 'gg-drain-tint' : 'gg-drain-tint gg-deco';
    host.appendChild(blurEl);
    host.appendChild(tintEl);
    return true;
  }

  function teardown() {
    for (const el of [blurEl, tintEl]) {
      if (!el) continue;
      el.classList.remove('is-on');
      soon(() => { try { el.remove(); } catch (_e) { /* ignore */ } }, 1000);
    }
    blurEl = null;
    tintEl = null;
  }

  /** The payload outranks the element; whichever is louder wins the dials. */
  function apply() {
    const want = (payloadIntensity === null && elementIntensity === null)
      ? null
      : Math.max(payloadIntensity === null ? 0 : payloadIntensity, elementIntensity === null ? 0 : elementIntensity);

    if (want === null) { teardown(); return; }
    if (!ensureNodes()) return;

    const tune = drainTuning(want, calm);
    blurEl.style.setProperty('--gg-drain-blur', `${tune.blurPx}px`);
    blurEl.style.setProperty('--gg-drain-fade', `${tune.fadeMs}ms`);
    tintEl.style.setProperty('--gg-drain-op', String(tune.opacity));
    tintEl.style.setProperty('--gg-drain-fade', `${tune.fadeMs}ms`);
    soon(() => {
      if (blurEl) blurEl.classList.add('is-on');
      if (tintEl) tintEl.classList.add('is-on');
    }, 16);
  }

  return {
    name: 'brainDrain',

    start(cue) {
      elementIntensity = clamp01(cue && cue.intensity);
      apply();
    },

    setIntensity(v) {
      if (elementIntensity === null) return;
      elementIntensity = clamp01(v);
      apply();
    },

    stop() {
      elementIntensity = null;
      apply();
    },

    /** The once-per-match heavy: ride the veil up for duration_ms, then release. */
    renderPayload(payload, done) {
      const p = payload || {};
      const runMs = Math.max(1000, (p.duration_ms | 0) || 45000);
      payloadIntensity = Math.max(0.55, clamp01(p.intensity !== undefined ? p.intensity : 0.8));
      apply();

      let finished = false;
      let endTimer = 0;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        try { clearTimeout(endTimer); } catch (_e) { /* ignore */ }
        payloadIntensity = null;
        apply();
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };
      endTimer = soon(() => settle(true), runMs);
      return () => settle(false);
    },
  };
}

export default createBrainDrain;
