/* ============================================================================
 * exec/flashes.js — GoonElement.Flashes (0) + GoonPayloadKind.FlashBurst (0).
 *
 * Images/GIFs drawn from the PLAYER'S OWN pool (exec/media.js). The opponent
 * never sends pictures — a payload only asks for "a burst", and this side decides
 * what that burst is made of. That is the whole receiver-side safety story for
 * this element.
 *
 * THE DtRH FLASH, PORTED (game/payloadFx.js flash(), .sf-pfx-flash in
 * dtrh/styles.css): flashes are SCATTERED, not full-window. Each one is a capped
 * <img> dropped at a random spot (14+72vw / 16+64vh), rotated a few degrees,
 * running one timed fade animation and then gone. Strength scales how many land
 * per beat and how long each holds. Hard cap of MAX_LIVE on screen at once — a
 * dense wave skips rather than floods, exactly like payloadFx's MAX_FLASH.
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
 * ==========================================================================*/

const MAX_LIVE = 6;          // concurrent <img> nodes (payloadFx's MAX_FLASH)

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

/** Cue intensity -> the dials the eye actually reads (DtRH's scale()/scaleD()). */
export function flashTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    // beats: ~1 every 2.2s at the floor, ~1 every 0.65s at the ceiling
    gapMs: Math.round(lerp(2200, 650, i) * (calm ? 2 : 1)),
    count: Math.max(1, Math.round(lerp(1, 2, i))),          // flashes per beat
    holdMs: Math.round(lerp(900, 1800, i) * (calm ? 1.3 : 1)),
    opacity: +lerp(0.55, 1, i).toFixed(3),
  };
}

export function createFlashes({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const calm = reducedMotion();

  const live = new Set();       // {node, handle} currently on screen
  let sustained = null;         // {alive, intensity, timer}

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('flash') : null);
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:flashes] ${m}`); };

  /** Drop bookkeeping for nodes the layer already tore out from under us
   *  (layers.stopAll() empties the DOM without going through kill()). */
  function prune() {
    for (const rec of Array.from(live)) {
      if (!rec.node || !rec.node.isConnected) {
        live.delete(rec);
        try { if (rec.handle && rec.handle.release) rec.handle.release(); } catch (_e) { /* ignore */ }
      }
    }
  }

  /** One scattered flash: draw -> place -> animate -> retire. */
  function showOne(tune) {
    prune();
    const host = layer();
    if (!host || typeof document === 'undefined') return;
    if (live.size >= MAX_LIVE) return;
    if (!media || typeof media.drawKind !== 'function') return;

    const entry = media.drawKind('image');   // media.js kinds are image|video; GIFs ride as images
    if (!entry) return;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return;

    const img = document.createElement('img');
    img.className = 'gg-flash';
    img.decoding = 'async';
    img.alt = '';
    img.style.setProperty('--gg-flash-dur', `${tune.holdMs}ms`);
    img.style.setProperty('--gg-flash-op', String(tune.opacity));
    // scatter across the window like the WPF floating flashes (never dead-centre)
    img.style.setProperty('left', `${(14 + Math.random() * 72).toFixed(1)}vw`);
    img.style.setProperty('top', `${(16 + Math.random() * 64).toFixed(1)}vh`);
    img.style.setProperty('--gg-flash-rot', `${(Math.random() * 16 - 8).toFixed(1)}deg`);

    const rec = { node: img, handle };
    live.add(rec);

    let killed = false;
    const kill = () => {
      if (killed) return;
      killed = true;
      live.delete(rec);
      try { img.remove(); } catch (_e) { /* already detached */ }
      try { img.removeAttribute('src'); } catch (_e) { /* ignore */ }
      try { if (handle && handle.release) handle.release(); } catch (_e) { /* ignore */ }
    };

    img.addEventListener('animationend', kill, { once: true });
    img.onerror = kill;                       // a dud entry is a skipped beat, never a throw
    img.src = handle.url;
    host.appendChild(img);
    // Safety net: a throttled/hidden tab may never deliver animationend, and a
    // leaked node is a leak for the rest of the match.
    soon(kill, tune.holdMs + 600);
  }

  /** One beat: `count` flashes, staggered so they land as a scatter not a stack. */
  function beat(tune) {
    showOne(tune);
    for (let i = 1; i < tune.count; i++) soon(() => showOne(tune), Math.round(tune.holdMs * 0.32 * i));
  }

  function loop(run) {
    if (!run.alive) return;
    const tune = flashTuning(run.intensity, calm);
    beat(tune);
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
      // Airborne flashes finish their fade — each retires on its own timer.
    },

    /**
     * FlashBurst: a dense 3-8s squall over whatever the bed is already doing.
     * Basically just flashes, faster and more of them — the density IS the
     * payload, and MAX_LIVE is what keeps that density honest.
     */
    renderPayload(payload, done) {
      const p = payload || {};
      const intensity = clamp01(p.intensity !== undefined ? p.intensity : 0.7);
      // The engine clamps duration_ms to >= 1000; a burst is a squall, not a bed.
      const runMs = Math.min(8000, Math.max(3000, (p.duration_ms | 0) || 5000));

      const run = { alive: true, intensity: Math.max(0.55, intensity), timer: 0, endTimer: 0 };
      let finished = false;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        run.alive = false;
        try { clearTimeout(run.timer); } catch (_e) { /* ignore */ }
        try { clearTimeout(run.endTimer); } catch (_e) { /* ignore */ }
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };

      const burstLoop = () => {
        if (!run.alive) return;
        const tune = flashTuning(run.intensity, calm);
        tune.gapMs = Math.round(tune.gapMs * 0.4);
        tune.count = Math.min(3, tune.count + 1);
        beat(tune);
        run.timer = soon(burstLoop, rand(tune.gapMs * 0.7, tune.gapMs * 1.3));
      };
      burstLoop();
      run.endTimer = soon(() => settle(true), runMs);   // ran to completion = endured
      return () => settle(false);                        // interrupted (mercy/stopAll)
    },
  };
}

export default createFlashes;
