/* ============================================================================
 * exec/brainDrain.js — GoonElement.BrainDrain (6) + GoonPayloadKind.BrainDrain (6).
 *
 * The heavy, ported from DtRH's payloadFx.showBraindrain + .sf-pfx-drain: ONE
 * full-window veil on #gg-fx-drain that dims to near-black, blurs everything
 * behind it, and carries a faint random image from the player's own pool washed
 * over the top under a dark luminosity blend — so it reads as DRAINED, not as a
 * slideshow. Intensity moves one dial, the veil's opacity, across DtRH's exact
 * 0.35..0.62 band.
 *
 * SAFETY, and it is not negotiable: this layer is z30 and the mercy button is
 * z60 with `isolation:isolate` (goon.css's DO-NOT-TOUCH block). The veil can
 * never cover the way out, and nothing in this file may raise a z-index or reach
 * outside its own layer to "cover everything" — the layer IS the cover.
 *
 * COST: backdrop-filter is the most expensive thing this page can draw. Exactly
 * ONE pane exists at a time — the payload does not stack a second one, it raises
 * the running one and puts it back afterwards — and the element and the payload
 * share it through a small dial stack.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

const WASH_MIN_MS = 7000;    // how long one washed image holds before a re-pick
const WASH_MAX_MS = 11000;

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const rand = (a, b) => a + Math.random() * (b - a);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};

/** Cue intensity -> the veil dial. The band is DtRH's scaleD(0.35, 0.62, strength). */
export function drainTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    opacity: +lerp(0.35, 0.62, i).toFixed(3),
    fadeMs: calm ? 1400 : 900,
  };
}

export function createBrainDrain({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:braindrain] ${m}`); };
  const calm = reducedMotion();

  let veilEl = null;
  let washHandle = null;         // media handle behind the current wash image
  let washTimer = 0;             // the slow re-pick
  let elementIntensity = null;   // null = element not running
  let payloadIntensity = null;   // null = no payload running

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('drain') : null);

  function releaseWash() {
    try { if (washHandle && washHandle.release) washHandle.release(); } catch (_e) { /* ignore */ }
    washHandle = null;
  }

  /** Wash one image from the player's pool over the veil (no pool = plain dim). */
  function repickWash() {
    if (!veilEl || !media || typeof media.drawKind !== 'function') return;
    const entry = media.drawKind('image');
    if (!entry) return;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return;
    releaseWash();
    washHandle = handle;
    try { veilEl.style.setProperty('background-image', `url("${handle.url}")`); }
    catch (_e) { /* a host without inline style support just keeps the flat dim */ }
  }

  function scheduleWash() {
    try { clearTimeout(washTimer); } catch (_e) { /* ignore */ }
    washTimer = soon(() => {
      if (!veilEl) return;
      repickWash();
      scheduleWash();
    }, Math.round(rand(WASH_MIN_MS, WASH_MAX_MS)));
  }

  function ensureNodes() {
    if (veilEl && veilEl.isConnected) return true;
    const host = layer();
    if (!host || typeof document === 'undefined') return false;
    veilEl = document.createElement('div');
    veilEl.className = 'gg-drain';
    host.appendChild(veilEl);
    repickWash();
    scheduleWash();
    return true;
  }

  function teardown() {
    try { clearTimeout(washTimer); } catch (_e) { /* ignore */ }
    washTimer = 0;
    if (!veilEl) return;
    const el = veilEl;
    el.classList.remove('is-on');
    soon(() => { try { el.remove(); } catch (_e) { /* ignore */ } }, 1000);
    veilEl = null;
    releaseWash();
  }

  /** The payload outranks the element; whichever is louder wins the dial. */
  function apply() {
    const want = (payloadIntensity === null && elementIntensity === null)
      ? null
      : Math.max(payloadIntensity === null ? 0 : payloadIntensity, elementIntensity === null ? 0 : elementIntensity);

    if (want === null) { teardown(); return; }
    if (!ensureNodes()) return;

    const tune = drainTuning(want, calm);
    veilEl.style.setProperty('--gg-drain-op', String(tune.opacity));
    veilEl.style.setProperty('--gg-drain-fade', `${tune.fadeMs}ms`);
    soon(() => { if (veilEl) veilEl.classList.add('is-on'); }, 16);
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
      repickWash();

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
