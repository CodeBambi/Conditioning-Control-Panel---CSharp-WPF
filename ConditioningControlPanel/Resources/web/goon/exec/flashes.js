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
 * per beat. Hard cap of MAX_LIVE on screen at once — a dense wave skips rather
 * than floods, exactly like payloadFx's MAX_FLASH.
 *
 * THE HYDRA (dtrh/engine/bubbles.js buildFlashClip + flashBurst): a flash is
 * CLICKABLE. Click one and it pops out of existence and TWO MORE hatch in its
 * place, scattered around where it stood. Children count against MAX_LIVE, and
 * the headroom is measured against the population BEFORE the dismissal frees the
 * clicked one's slot — so a click can only ever hold or shrink the field, never
 * grow it past the cap, and AT the cap a click is a plain dismissal. Unlike DtRH
 * there is no generation cap: MAX_LIVE is the only thing bounding the split, and
 * children taper slightly in size so a deep chain reads as a chain.
 *
 * POPPING IS COSMETIC. Exactly like the bubble field: it scores nothing, ends
 * nothing, and changes NO receipt semantics. A FlashBurst payload still runs its
 * full duration_ms and then done(true) if it was never interrupted, whether you
 * clicked every flash or none of them.
 *
 * POINTER RULES. #gg-fx and its sub-layers are pointer-events:none. `.gg-flash`
 * on its own stays click-through (exec/bubbles.js mints plain .gg-flash nodes for
 * its pop effect and those must NOT eat clicks); only the `.gg-flash--hydra`
 * modifier this module adds opts back in. fx.css drops that opt-in whenever
 * #gg-stage is occupied — the same `body:has(#gg-stage > *)` rule the bubbles
 * use — so a flash can never swallow a click meant for a lock card or a video
 * attention check.
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

export const MAX_LIVE = 20;          // concurrent <img> nodes, hydra children included
export const HYDRA_CHILDREN = 2;     // what one click hatches (clamped to the cap)
export const BASE_HOLD_MS = 5000;    // on-screen time a flash is centred on
const HOLD_SPREAD_MS = 400;          // ± this across the intensity range (4600..5400)
const POP_OUT_MS = 220;              // the dismissed flash's quick pop-out (fx.css ggFlashPop)
const HATCH_MS = [70, 210];          // children hatch staggered, never on one frame

/* The scatter box, in vw/vh — payloadFx's numbers. Children are placed radially
   off their parent inside the same box. */
const X_MIN = 14, X_SPAN = 72;
const Y_MIN = 16, Y_SPAN = 64;
/* Mirrors --gg-flash-size's default in fx.css (38vmin + 15%). Only children ever
   write the property; a gen-0 flash lets the stylesheet own its size. */
const BASE_SIZE_VMIN = 43.7;

/* --- local helpers. Deliberately duplicated across exec/ modules: this tier's
   territory is these files only, and a shared util module is not one of them. */
const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const rand = (a, b) => a + Math.random() * (b - a);
const box = (v, min, span) => (v < min ? min : (v > min + span ? min + span : v));
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();   // never hold node's loop open
  return t;
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};

/** Cue intensity -> the dials the eye actually reads (DtRH's scale()/scaleD()).
 *
 *  CADENCE, retuned for a 5s lifetime + a cap of 20. What the eye reads is the
 *  STANDING population, which settles at roughly count * holdMs / gapMs:
 *      i=0    ~1 flash every 2.4s  ->  ~2 on screen
 *      i=0.5  ~2 flashes every 1.7s -> ~6 on screen
 *      i=1    ~2 flashes every 0.95s -> ~11 on screen
 *  So intensity still spans a visible 6x of density while the bed alone never
 *  pins the cap — the headroom above ~11 is what a FlashBurst (and the hydra)
 *  spend, which is exactly where the cap should bite. */
export function flashTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    gapMs: Math.round(lerp(2400, 950, i) * (calm ? 2 : 1)),
    count: Math.max(1, Math.round(lerp(1, 2, i))),          // flashes per beat
    holdMs: Math.round(lerp(BASE_HOLD_MS - HOLD_SPREAD_MS, BASE_HOLD_MS + HOLD_SPREAD_MS, i) * (calm ? 1.15 : 1)),
    opacity: +lerp(0.55, 1, i).toFixed(3),
  };
}

export function createFlashes({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const calm = reducedMotion();

  const live = new Set();       // {node, handle, x, y, gen, tune, popped, kill} on screen
  let sustained = null;         // {alive, intensity, timer}

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('flash') : null);
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:flashes] ${m}`); };
  const sfx = (id) => { if (audio && typeof audio.sfx === 'function') { try { audio.sfx(id); } catch (_e) { /* ignore */ } } };

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

  /** Where a flash lands. `near` (a parent's vw/vh) makes it a hydra child:
   *  pushed radially off the parent so the split reads as a split, then boxed
   *  back into the scatter area so nothing hatches off-screen. */
  function place(nearX, nearY) {
    if (typeof nearX !== 'number' || typeof nearY !== 'number') {
      return { x: X_MIN + Math.random() * X_SPAN, y: Y_MIN + Math.random() * Y_SPAN };
    }
    const a = rand(0, Math.PI * 2);
    const d = rand(11, 21);
    return {
      x: box(nearX + Math.cos(a) * d, X_MIN, X_SPAN),
      y: box(nearY + Math.sin(a) * d * 0.8, Y_MIN, Y_SPAN),
    };
  }

  /**
   * One scattered flash: draw -> place -> animate -> retire.
   * `opts` = {gen, nearX, nearY} when this one hatched from a click.
   * THE ONE PLACE the cap is enforced — every spawn path lands here.
   */
  function showOne(tune, opts) {
    prune();
    const host = layer();
    if (!host || typeof document === 'undefined') return;
    if (live.size >= MAX_LIVE) return;
    if (!media || typeof media.drawKind !== 'function') return;

    const entry = media.drawKind('image');   // media.js kinds are image|video; GIFs ride as images
    if (!entry) return;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return;

    const gen = Math.max(0, (opts && opts.gen) | 0);
    const pos = place(opts && opts.nearX, opts && opts.nearY);

    const img = document.createElement('img');
    // --hydra is the pointer opt-in (fx.css). bubbles.js's pop flashes are plain
    // .gg-flash on purpose and stay click-through.
    img.className = gen > 0 ? 'gg-flash gg-flash--hydra gg-flash--hatch' : 'gg-flash gg-flash--hydra';
    img.decoding = 'async';
    img.alt = '';
    img.style.setProperty('--gg-flash-dur', `${tune.holdMs}ms`);
    img.style.setProperty('--gg-flash-op', String(tune.opacity));
    // scatter across the window like the WPF floating flashes (never dead-centre)
    img.style.setProperty('left', `${pos.x.toFixed(1)}vw`);
    img.style.setProperty('top', `${pos.y.toFixed(1)}vh`);
    img.style.setProperty('--gg-flash-rot', `${(Math.random() * 16 - 8).toFixed(1)}deg`);
    // Children taper a little so a long hydra chain reads as descendants rather
    // than 20 identical posters. Size is a CSS var, NOT a transform — the tilt and
    // the fade animation own transform and must not be fought over.
    if (gen > 0) {
      const factor = Math.max(0.78, 1 - 0.09 * gen);
      img.style.setProperty('--gg-flash-size', `${(BASE_SIZE_VMIN * factor).toFixed(1)}vmin`);
    }

    const rec = { node: img, handle, x: pos.x, y: pos.y, gen, tune, popped: false, kill: null };
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
    rec.kill = kill;

    // Fires for whichever animation is running — the timed fade, or the pop-out
    // that replaces it on a click.
    img.addEventListener('animationend', kill, { once: true });
    img.onerror = kill;                       // a dud entry is a skipped beat, never a throw
    img.addEventListener('pointerdown', (e) => hydra(rec, e));
    img.src = handle.url;
    host.appendChild(img);
    // Safety net: a throttled/hidden tab (and prefers-reduced-motion, which has no
    // animation at all) may never deliver animationend, and a leaked node is a
    // leak for the rest of the match.
    soon(kill, tune.holdMs + 600);
  }

  /** Pop it out and free its slot. The pop-out animation replaces the fade. */
  function dismiss(rec) {
    if (rec.popped) return;
    rec.popped = true;
    live.delete(rec);                       // the slot is free the instant it is clicked
    try { rec.node.classList.add('is-popped'); } catch (_e) { /* ignore */ }
    soon(rec.kill, POP_OUT_MS + 260);       // animationend normally beats this
  }

  /** Click one, two hatch. Cosmetic: it touches no receipt and no payload run. */
  function hydra(rec, e) {
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    // Left button only: a right-click belongs to the page, not the field.
    if (e && e.button != null && e.button !== 0) return;
    if (rec.popped) return;

    // Headroom measured BEFORE the dismissal frees this one's slot: kids <= room
    // means the post-click population is at most (live.size - 1 + room) =
    // MAX_LIVE - 1. At the cap room is 0 and the click is a plain dismissal.
    const room = Math.max(0, MAX_LIVE - live.size);
    dismiss(rec);
    sfx('flash-pop');

    const kids = Math.min(HYDRA_CHILDREN, room);
    for (let k = 0; k < kids; k++) {
      const opts = { gen: rec.gen + 1, nearX: rec.x, nearY: rec.y };
      // A live sustained run re-tunes the children to the CURRENT intensity;
      // otherwise they inherit whatever tune spawned their parent.
      const tune = sustained ? flashTuning(sustained.intensity, calm) : rec.tune;
      soon(() => showOne(tune, opts), HATCH_MS[k] != null ? HATCH_MS[k] : 70 + k * 140);
    }
  }

  /** One beat: `count` flashes, staggered so they land as a scatter not a stack.
   *  The stagger re-checks `run.alive`, so stop()/cancel() means stop spawning —
   *  a half-landed beat can no longer trail nodes in behind a stopped element. */
  function beat(tune, run) {
    showOne(tune);
    // A fixed stagger, not a fraction of holdMs: at a 5s lifetime a proportional
    // stagger would drift the second flash of a beat most of a second late.
    for (let i = 1; i < tune.count; i++) {
      soon(() => { if (!run || run.alive) showOne(tune); }, 150 * i + Math.round(rand(0, 120)));
    }
  }

  function loop(run) {
    if (!run.alive) return;
    const tune = flashTuning(run.intensity, calm);
    beat(tune, run);
    sfx('flash');
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
      // Airborne flashes finish their fade — each retires on its own timer, and
      // each one stays clickable until it does.
    },

    /**
     * FlashBurst: a dense 3-8s squall over whatever the bed is already doing.
     * Basically just flashes, faster and more of them — the density IS the
     * payload, and MAX_LIVE is what keeps that density honest. At burst cadence
     * the standing population runs at roughly the cap, which is the point: the
     * squall is the only thing that fills the screen.
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
        tune.gapMs = Math.round(tune.gapMs * 0.5);
        tune.count = Math.min(3, tune.count + 1);
        beat(tune, run);
        run.timer = soon(burstLoop, rand(tune.gapMs * 0.7, tune.gapMs * 1.3));
      };
      burstLoop();
      run.endTimer = soon(() => settle(true), runMs);   // ran to completion = endured
      return () => settle(false);                        // interrupted (mercy/stopAll)
    },
  };
}

export default createFlashes;
