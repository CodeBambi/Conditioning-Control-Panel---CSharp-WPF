/* ============================================================================
 * exec/flashes.js — GoonElement.Flashes (0) + GoonPayloadKind.FlashBurst (0).
 *
 * Full-window images/GIFs drawn from the PLAYER'S OWN pool (exec/media.js). The
 * opponent never sends pictures — a payload only asks for "a burst", and this
 * side decides what that burst is made of. That is the whole receiver-side
 * safety story for this element.
 *
 * THE UNIFORM RENDERER SHAPE (every module in exec/ implements exactly this;
 * executor.js is the only consumer):
 *
 *   create<Name>({ layers, media, audio, logger, phrases }) -> {
 *     name,                                // for logs
 *     start(cue),                          // sustained run on  (cue = {element,intensity,durationMs,elapsedMs})
 *     setIntensity(v),                     // re-tune a RUNNING sustained run; no-op otherwise
 *     stop(),                              // sustained run off; leaves payload runs alone
 *     renderPayload(payload, done) -> cancel   // done(endured:boolean) at most once; cancel() aborts it
 *   }
 *
 * Sustained run and payload runs are independent: a burst can land on top of an
 * already-running flash bed and neither one owns the other's nodes.
 *
 * Node budget: MAX_LIVE <img> elements on screen at once, recycled through a
 * free list, and every acquired media handle is released when its node retires.
 * ==========================================================================*/

const MAX_LIVE = 4;          // concurrent <img> nodes (the recycler's ceiling)
const POOL_MAX = 6;          // retired nodes kept for reuse

/* --- local helpers. Deliberately duplicated across exec/ modules: this tier's
   territory is these files only, and a shared util module is not one of them. */
const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const rand = (a, b) => a + Math.random() * (b - a);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();   // never hold node's loop open
  return t;
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};

/** Cue intensity -> the two dials the eye actually reads. */
export function flashTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    // ~30/min at the floor, ~150/min at the ceiling (calm halves the rate)
    gapMs: Math.round(lerp(2000, 400, i) * (calm ? 2 : 1)),
    holdMs: Math.round(lerp(150, 320, i)),
    fadeMs: calm ? 260 : Math.round(lerp(160, 90, i)),
    opacity: +lerp(0.35, 0.9, i).toFixed(3),
    scale: calm ? 1 : +lerp(1.02, 1.09, i).toFixed(3),
  };
}

export function createFlashes({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const calm = reducedMotion();

  const free = [];          // retired <img> nodes waiting for reuse
  let live = 0;             // nodes currently on screen
  let sustained = null;     // {intensity, timer}
  const payloads = new Set();   // live burst runs (each {timer, wash, done})

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('flash') : null);
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:flashes] ${m}`); };

  function takeNode() {
    const n = free.pop();
    if (n) return n;
    if (typeof document === 'undefined') return null;
    const img = document.createElement('img');
    img.className = 'gg-flash-img';
    img.decoding = 'async';
    img.alt = '';
    return img;
  }

  function retire(node, handle) {
    live = Math.max(0, live - 1);
    try { node.remove(); } catch (_e) { /* already detached */ }
    try { node.removeAttribute('src'); } catch (_e) { /* ignore */ }
    try { if (handle && handle.release) handle.release(); } catch (_e) { /* ignore */ }
    if (free.length < POOL_MAX) free.push(node);
  }

  /** One flash: draw -> preload -> show -> hold -> fade -> retire. */
  function showOne(tune) {
    const host = layer();
    if (!host || live >= MAX_LIVE) return;
    if (!media || typeof media.drawKind !== 'function') return;

    const entry = media.drawKind('image');   // media.js kinds are image|video; GIFs ride as images
    if (!entry) return;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return;

    const node = takeNode();
    if (!node) { try { handle.release(); } catch (_e) { /* ignore */ } return; }

    live++;
    node.style.setProperty('--gg-flash-op', String(tune.opacity));
    node.style.setProperty('--gg-flash-fade', `${tune.fadeMs}ms`);
    node.style.setProperty('--gg-flash-scale', String(tune.scale));
    node.classList.remove('is-on');

    let settled = false;
    const show = () => {
      if (settled) return;
      settled = true;
      const h = layer();
      if (!h) { retire(node, handle); return; }
      h.appendChild(node);
      // one frame with .is-on absent so the opacity transition actually runs
      soon(() => node.classList.add('is-on'), 16);
      soon(() => {
        node.classList.remove('is-on');
        soon(() => retire(node, handle), tune.fadeMs + 40);
      }, tune.holdMs + 16);
    };
    const fail = () => {
      if (settled) return;
      settled = true;
      retire(node, handle);   // a dud entry is just a skipped beat, never a throw
    };

    node.onload = show;
    node.onerror = fail;
    node.src = handle.url;
    // decode() is the pop-in cure when it exists; onload is the fallback path.
    if (typeof node.decode === 'function') {
      try { node.decode().then(show, () => { /* onload/onerror still arbitrate */ }); }
      catch (_e) { /* ignore */ }
    }
    // A source that never fires either event must not pin a node forever.
    soon(fail, 4000);
  }

  function loop(run) {
    if (!run.alive) return;
    const tune = flashTuning(run.intensity, calm);
    showOne(tune);
    if (audio && typeof audio.sfx === 'function') { try { audio.sfx('flash'); } catch (_e) { /* ignore */ } }
    run.timer = soon(() => loop(run), rand(tune.gapMs * 0.7, tune.gapMs * 1.3));
  }

  return {
    name: 'flashes',

    start(cue) {
      const intensity = clamp01(cue && cue.intensity);
      if (sustained) { sustained.intensity = intensity; return; }
      sustained = { alive: true, intensity, timer: 0 };
      loop(sustained);
    },

    setIntensity(v) { if (sustained) sustained.intensity = clamp01(v); },

    stop() {
      if (!sustained) return;
      sustained.alive = false;
      try { clearTimeout(sustained.timer); } catch (_e) { /* ignore */ }
      sustained = null;
    },

    /**
     * FlashBurst: a dense 3-8s squall over whatever the bed is already doing,
     * plus the pink wash so the burst reads as an EVENT and not just "faster".
     */
    renderPayload(payload, done) {
      const p = payload || {};
      const intensity = clamp01(p.intensity !== undefined ? p.intensity : 0.7);
      // The engine clamps duration_ms to >= 1000; a burst is a squall, not a bed.
      const runMs = Math.min(8000, Math.max(3000, (p.duration_ms | 0) || 5000));

      const run = { alive: true, intensity: Math.max(0.55, intensity), timer: 0, endTimer: 0 };
      let wash = null;
      let finished = false;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        run.alive = false;
        try { clearTimeout(run.timer); } catch (_e) { /* ignore */ }
        try { clearTimeout(run.endTimer); } catch (_e) { /* ignore */ }
        if (wash) {
          wash.classList.remove('is-on');
          const w = wash;
          soon(() => { try { w.remove(); } catch (_e) { /* ignore */ } }, 260);
          wash = null;
        }
        payloads.delete(run);
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };

      const host = layer();
      if (host && typeof document !== 'undefined') {
        wash = document.createElement('div');
        wash.className = 'gg-flash-wash';
        wash.style.setProperty('--gg-flash-op', String(lerp(0.2, 0.45, intensity).toFixed(3)));
        host.appendChild(wash);
        soon(() => { if (wash) wash.classList.add('is-on'); }, 16);
      }

      payloads.add(run);
      // A burst runs ~3x tighter than the bed would at the same intensity —
      // that density IS the payload; "the same thing, slightly faster" is not
      // worth a charge to the sender.
      const burstLoop = () => {
        if (!run.alive) return;
        const tune = flashTuning(run.intensity, calm);
        tune.gapMs = Math.round(tune.gapMs * 0.35);
        showOne(tune);
        run.timer = soon(burstLoop, rand(tune.gapMs * 0.7, tune.gapMs * 1.3));
      };
      burstLoop();
      run.endTimer = soon(() => settle(true), runMs);   // ran to completion = endured
      return () => settle(false);                        // interrupted (mercy/stopAll)
    },
  };
}

export default createFlashes;
