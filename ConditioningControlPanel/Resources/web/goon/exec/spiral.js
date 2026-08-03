/* ============================================================================
 * exec/spiral.js — GoonElement.Spiral (8) + GoonPayloadKind.Spiral (7).
 *
 * The DtRH spiral, straight across: ONE persistent full-window pane on
 * #gg-fx-spiral whose background is drawn from the bundled spiral pool, covered,
 * screen-blended and spinning slowly (.gg-spiral in fx.css is the port of DtRH's
 * .sf-pfx-spiral). Intensity moves exactly one dial — opacity, 0.25..0.70, the
 * same band payloadFx.showSpiral uses — because "more spiral" is the whole idea.
 *
 * ONE ELEMENT, ALWAYS. The element and the payload share the pane through a dial
 * stack (whoever wants it louder wins); a payload landing on a running bed does
 * not add a second spinning cover pass.
 *
 * ASSETS: the pool is the DtRH bundle under /dtrh/assets/bubbles/effects/spirals/
 * (same ccp.game origin — Resources/web is the host root, so /dtrh/... resolves
 * from /goon/). Deliberately NOT imported from dtrh/engine/loomSpirals.js: that
 * module pulls DtRH settings/Loom storage in with it. The pick is three lines.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

/** The bundled spiral pool (DtRH ships these; goon reads them off the same host). */
export const SPIRAL_DIR = '/dtrh/assets/bubbles/effects/spirals/';
export const SPIRAL_POOL = Object.freeze([
  'sp1.gif', 'sp2.webp', 'sp3.gif', 'sp4.webp', 'sp5.gif', 'sp6.gif', 'sp7.gif',
]);
/** The one still that ships outside the pool — the floor if the pool is ever empty. */
export const SPIRAL_FALLBACK = '/dtrh/assets/bubbles/effects/spiral.png';

/** A random spiral URL. Shared with exec/bubbles.js (a popped spiral bubble flicks one). */
export function pickSpiralUrl() {
  if (!SPIRAL_POOL.length) return SPIRAL_FALLBACK;
  return SPIRAL_DIR + SPIRAL_POOL[(Math.random() * SPIRAL_POOL.length) | 0];
}

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

/** Cue intensity -> the one dial. The band is DtRH's scaleD(0.25, 0.70, strength). */
export function spiralTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    opacity: +lerp(0.25, 0.70, i).toFixed(3),
    fadeMs: calm ? 900 : 450,
  };
}

export function createSpiral({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:spiral] ${m}`); };
  const calm = reducedMotion();

  let paneEl = null;
  let elementIntensity = null;    // null = element not running
  let payloadIntensity = null;    // null = no payload running

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('spiral') : null);

  function ensurePane() {
    if (paneEl && paneEl.isConnected) return true;
    const host = layer();
    if (!host || typeof document === 'undefined') return false;
    paneEl = document.createElement('div');
    // .gg-deco so the heat armor can park the spin when the stack goes hot.
    paneEl.className = calm ? 'gg-spiral' : 'gg-spiral gg-deco';
    repick();
    host.appendChild(paneEl);
    return true;
  }

  /** Swap the image (CSS still owns cover/blend/spin — only the source varies). */
  function repick() {
    if (!paneEl || !paneEl.style) return;
    try { paneEl.style.setProperty('background-image', `url('${pickSpiralUrl()}')`); }
    catch (_e) { /* a host without inline style support just keeps the CSS default */ }
  }

  function teardown() {
    if (!paneEl) return;
    const el = paneEl;
    el.classList.remove('is-on');
    soon(() => { try { el.remove(); } catch (_e) { /* ignore */ } }, 900);
    paneEl = null;
  }

  /** The payload outranks the element; whichever is louder wins the dial. */
  function apply() {
    const want = (payloadIntensity === null && elementIntensity === null)
      ? null
      : Math.max(payloadIntensity === null ? 0 : payloadIntensity, elementIntensity === null ? 0 : elementIntensity);

    if (want === null) { teardown(); return; }
    if (!ensurePane()) return;

    const tune = spiralTuning(want, calm);
    paneEl.style.setProperty('--gg-spiral-op', String(tune.opacity));
    paneEl.style.setProperty('--gg-spiral-fade', `${tune.fadeMs}ms`);
    soon(() => { if (paneEl) paneEl.classList.add('is-on'); }, 16);
  }

  return {
    name: 'spiral',

    start(cue) {
      const fresh = elementIntensity === null;
      elementIntensity = clamp01(cue && cue.intensity);
      apply();
      if (fresh) repick();   // a new bed gets a new spiral; a re-tune keeps its own
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

    /** Spiral payload: ride the pane up for duration_ms, then hand it back. */
    renderPayload(payload, done) {
      const p = payload || {};
      const runMs = Math.max(1000, (p.duration_ms | 0) || 12000);
      payloadIntensity = Math.max(0.5, clamp01(p.intensity !== undefined ? p.intensity : 0.75));
      apply();
      repick();

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

export default createSpiral;
