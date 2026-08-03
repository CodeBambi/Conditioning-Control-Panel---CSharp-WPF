/* ============================================================================
 * exec/videos.js — GoonElement.Videos (1) + GoonPayloadKind.Video (3).
 *
 * TWO SHAPES, ONE POOL. Both draw clips from the PLAYER'S OWN library
 * (exec/media.js — the opponent sends a tag reference, never a file), but they
 * are different objects on screen and always have been meant to be:
 *
 *   ELEMENT (the shared ramp's mandatory video) — FULLSCREEN on #gg-stage (z20,
 *   under the HUD, under mercy, over the screens). One clip chained after
 *   another for as long as the ramp keeps it up. Unchanged.
 *
 *   PAYLOAD (the opponent throws the VHS) — FLOATING MINI WINDOWS on
 *   #gg-fx-vwin (z30). Small, glitch-in, drifting, DRAGGABLE, wheel-resizable,
 *   and dismissed with a click. NEVER on #gg-stage: the stage is full-bleed and
 *   only click-through while :empty (ui/screens.css), so a husk left on it is a
 *   full-screen click shield — the exact bug clearForRecap exists to sweep up.
 *   A window on an fx layer cannot do that, and layers.stopAll() takes it with
 *   the rest of the tier.
 *
 * THE WINDOW, in the owner's words: "when they start they should have a random
 * glitch animation btw, also add some glowing borders with a red dot on the top
 * right (kinda like the live red light)". So: one of GLITCH_VARIANTS CRT-style
 * spawn-ins picked per window, a pink/violet glow ring, and a recording dot that
 * pulses while it plays.
 *
 * THREE ELEMENTS, THREE ANIMATIONS — and that is not styling, it is the
 * ANIMATION SHORTHAND TRAP: `animation` is a shorthand, so a one-shot and a loop
 * declared on ONE element cancel each other. The wrapper takes the drag offset
 * (inline transform, NO animation of its own, so the inline write is
 * authoritative); the drift loop rides .gg-vwin-drift; the one-shot glitch-in
 * rides .gg-vwin-inner; the pulse rides .gg-vwin-dot; the glow breathes on
 * .gg-vwin-inner::after. Nobody shares.
 *
 * PHYSICAL INTERACTION is exec/flashes.js's, ported rather than reinvented — it
 * already solved the three things that make this kind of node feel broken:
 *   · 6px of slop decides CLICK vs DRAG, and it is decided on pointerUP, so one
 *     press can still become either;
 *   · a grabbed window leaves the keyframe world (.is-grabbed kills the drift in
 *     fx.css) because animations outrank inline styles — a live drift keyframe
 *     would drag the node back to its anchor every frame. The drift offset is
 *     FOLDED into the drag offset first, so killing it moves nothing;
 *   · the z-lift is a re-append (fx.css rule 1: nothing here raises z-index),
 *     and a re-append RELEASES pointer capture — so we re-take it and swallow
 *     exactly one stale lostpointercapture.
 *
 * AUDIO: only the NEWEST window speaks, at volumeFor(intensity). When it goes,
 * the next-newest takes over. Four soundtracks at once is mush. Chromium's
 * autoplay policy can still refuse a play() with sound before the page has had a
 * gesture; that is HANDLED, not dropped — we retry muted, so the picture never
 * silently fails to appear. The one thing we never do is give up on the visual.
 *
 * RECEIPTS, mirrored from what the fullscreen payload did before:
 *   video ended · 60s cap · player dismissed it · displaced by a 5th window
 *                                              -> done(true)  "survived", +1 charge
 *   executor cancel (mercy / stopAll) · nothing playable in the library
 *                                              -> done(false) "completed", no charge
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

const MAX_SOURCE_TRIES = 3;   // a broken entry costs one redraw, not the run

/* --- the window dials. Exported because the self-test asserts against these
   numbers rather than re-typing them. ------------------------------------- */
export const MAX_WINDOWS = 4;          // live floating windows; a 5th evicts the oldest
export const WINDOW_CAP_MS = 60000;    // nothing floats longer than this, ever
export const DRAG_SLOP_PX = 6;         // <= this much travel and the press was a CLICK
export const WHEEL_STEP = 1.08;        // size multiplier per wheel notch
export const SIZE_MIN_FACTOR = 0.5;    // wheel clamp, relative to the window's base width
export const SIZE_MAX_FACTOR = 2.5;
export const GLITCH_VARIANTS = 4;      // .gg-vwin-in--1 .. --4 in fx.css
/* The bottom gutter MERCY owns. Same 96px ui/options.js clips off the drawer
   (MERCY_CLEARANCE_PX) — the guaranteed way out is never covered, by anything. */
export const MERCY_KEEPOUT_PX = 96;

const HOLD_WATCHDOG_MS = 30000;  // nothing may be "held" longer than this
const SETTLE_FADE_MS = 240;      // the window's fade-out before the node is torn out
const SPAWN_TRIES = 60;          // placement attempts before the safe-corner fallback

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const num = (v, dflt) => (typeof v === 'number' && v === v ? v : dflt);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
const nowMs = () => {
  try { if (typeof performance === 'object' && performance && typeof performance.now === 'function') return performance.now(); }
  catch (_e) { /* fall through */ }
  return Date.now();
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};
const vpW = () => ((typeof window !== 'undefined' && window && window.innerWidth > 0) ? window.innerWidth : 1280);
const vpH = () => ((typeof window !== 'undefined' && window && window.innerHeight > 0) ? window.innerHeight : 720);

/** Cue intensity -> playback volume. Modest on purpose: the HUD must stay audible. */
export function volumeFor(intensity) {
  return +lerp(0.12, 0.55, clamp01(intensity)).toFixed(3);
}

/**
 * THE AUDIO INVARIANT, as a pure function: given `count` windows in spawn order,
 * exactly the newest (last) one is unmuted, and only when there is one at all.
 * Kept separate from the DOM so it can be asserted without a browser.
 */
export function unmutedFlags(count) {
  const n = Math.max(0, count | 0);
  const out = new Array(n);
  for (let i = 0; i < n; i++) out[i] = (i === n - 1);
  return out;
}

/**
 * How long ONE window may float: never past WINDOW_CAP_MS, never past what the
 * payload asked for, never under a second. The clip ending beats all of it.
 */
export function windowCapMs(durationMs) {
  return Math.min(WINDOW_CAP_MS, Math.max(1000, (durationMs | 0) || 30000));
}

/** A window's born width in px — phone-screen-ish, and never more than a slab. */
export function baseWindowWidth(viewportW) {
  const w = num(viewportW, 0) > 0 ? viewportW : 1280;
  return Math.round(Math.min(400, Math.max(220, w * 0.22)));
}

/**
 * The two rectangles a window may not be BORN on top of (it may drift, slowly,
 * anywhere — the drift is biased upward and away from the gutter, see
 * driftVars()):
 *   mercy    the bottom-centre gutter. Never covered, at any heat, in any phase.
 *   monitor  the opponent's CRT column, top-right (ui/hud.css .gg-rightcol is
 *            --gg-mon-w = clamp(210px, 22vw, 280px) wide).
 */
export function keepOutRects(viewportW, viewportH) {
  const W = num(viewportW, 0) > 0 ? viewportW : 1280;
  const H = num(viewportH, 0) > 0 ? viewportH : 720;
  const monW = Math.min(280, Math.max(210, W * 0.22)) + 48;   // + the column's gap/margin
  return {
    mercy: { x0: W * 0.28, y0: Math.max(0, H - MERCY_KEEPOUT_PX), x1: W * 0.72, y1: H },
    monitor: { x0: Math.max(0, W - monW), y0: 0, x1: W, y1: H * 0.72 },
  };
}

const hits = (x, y, w, h, r) => !(x + w <= r.x0 || x >= r.x1 || y + h <= r.y0 || y >= r.y1);

/**
 * Where a new window lands: random, but off the mercy gutter and off the
 * opponent's monitor. Rejection sampling with a deterministic fallback (the
 * top-left corner clears both keep-outs by construction) — a window that could
 * not be placed still has to appear.
 * @param {(n?:number)=>number} [rnd] injectable RNG so the self-test can sweep it
 */
export function placeWindow(viewportW, viewportH, w, h, rnd) {
  const R = typeof rnd === 'function' ? rnd : Math.random;
  const W = num(viewportW, 0) > 0 ? viewportW : 1280;
  const H = num(viewportH, 0) > 0 ? viewportH : 720;
  const ww = Math.min(num(w, 320), W * 0.9);
  const hh = Math.min(num(h, 180), H * 0.9);
  const pad = 8;
  const spanX = Math.max(0, W - ww - pad * 2);
  const spanY = Math.max(0, H - hh - pad * 2);
  const zones = keepOutRects(W, H);
  for (let i = 0; i < SPAWN_TRIES; i++) {
    const x = pad + R() * spanX;
    const y = pad + R() * spanY;
    if (hits(x, y, ww, hh, zones.mercy) || hits(x, y, ww, hh, zones.monitor)) continue;
    return { x: Math.round(x), y: Math.round(y) };
  }
  return { x: pad, y: pad };
}

/** Drift dials for one window. dy is biased UP: the gutter is downstairs. */
function driftVars(R) {
  const rnd = typeof R === 'function' ? R : Math.random;
  const sign = rnd() < 0.5 ? -1 : 1;
  return {
    dx: Math.round(sign * (18 + rnd() * 28)),
    dy: -Math.round(14 + rnd() * 26),
    durS: +(18 + rnd() * 16).toFixed(1),
  };
}

/** Which spawn-in plays. 1..GLITCH_VARIANTS so four spawns do not look cloned. */
export function glitchVariant(rnd) {
  const R = typeof rnd === 'function' ? rnd : Math.random;
  return 1 + Math.min(GLITCH_VARIANTS - 1, Math.floor(R() * GLITCH_VARIANTS));
}

/** translate() out of a computed matrix, so folding the drift needs no library. */
function matrixXY(str) {
  const s = String(str || '');
  if (!s || s === 'none') return null;
  const m = /matrix\(\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*\)/.exec(s);
  if (m) return { x: parseFloat(m[5]) || 0, y: parseFloat(m[6]) || 0 };
  const m3 = /matrix3d\(([^)]+)\)/.exec(s);
  if (m3) {
    const p = m3[1].split(',').map((v) => parseFloat(v) || 0);
    if (p.length >= 14) return { x: p[12], y: p[13] };
  }
  return null;
}

export function createVideos({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:videos] ${m}`); };
  const info = (m) => { if (log && log.info) log.info(`[gg:videos] ${m}`); };
  const calm = reducedMotion();

  let sustained = null;               // the chained element run (fullscreen, stage)
  const runs = new Set();             // every live element run
  const wins = [];                    // floating windows, OLDEST FIRST
  let drag = null;                    // the ONE live press, if any

  const stage = () => (layers && typeof layers.get === 'function' ? layers.get('stage') : null);
  const winLayer = () => (layers && typeof layers.get === 'function' ? layers.get('vwin') : null);

  /* =========================================================================
   * THE ELEMENT RUN — fullscreen on #gg-stage. This is the ramp's mandatory
   * video and it is deliberately the boring one: mount, chain, tear down.
   * ======================================================================= */
  function createRun({ intensity, onDone }) {
    const run = {
      alive: true, intensity: clamp01(intensity),
      wrap: null, video: null, handle: null, tries: 0, finished: false,
    };
    runs.add(run);

    const settle = (endured) => {
      if (run.finished) return;
      run.finished = true;
      run.alive = false;
      teardown();
      runs.delete(run);
      if (typeof onDone === 'function') { try { onDone(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
    };

    function releaseSource() {
      try { if (run.handle && run.handle.release) run.handle.release(); } catch (_e) { /* ignore */ }
      run.handle = null;
    }

    function teardown() {
      const v = run.video;
      if (v) {
        try { v.pause(); } catch (_e) { /* ignore */ }
        try { v.removeAttribute('src'); v.load(); } catch (_e) { /* ignore */ }
        v.onended = null; v.onerror = null;
      }
      releaseSource();
      const w = run.wrap;
      if (w) {
        w.classList.remove('is-on');
        soon(() => { try { w.remove(); } catch (_e) { /* ignore */ } }, 280);
      }
      run.wrap = null;
      run.video = null;
    }

    function mount() {
      if (typeof document === 'undefined') return false;
      const host = stage();
      if (!host) return false;
      const wrap = document.createElement('div');
      wrap.className = 'gg-vid-wrap';
      const video = document.createElement('video');
      video.className = 'gg-vid';
      video.playsInline = true;
      video.autoplay = true;
      video.controls = false;
      video.preload = 'auto';
      video.volume = volumeFor(run.intensity);
      wrap.appendChild(video);
      host.appendChild(wrap);
      soon(() => wrap.classList.add('is-on'), 16);
      run.wrap = wrap;
      run.video = video;
      return true;
    }

    /** Draw the next clip. Returns false when the pool has nothing playable. */
    function next() {
      if (!run.alive) return false;
      if (!run.video && !mount()) return false;
      if (!media || typeof media.drawKind !== 'function') return false;

      const entry = media.drawKind('video');
      if (!entry) return false;
      const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
      if (!handle || !handle.url) return false;

      releaseSource();
      run.handle = handle;

      const v = run.video;
      v.loop = false;               // the element CHAINS: one clip after another
      v.onerror = () => {
        run.tries++;
        if (run.tries >= MAX_SOURCE_TRIES) { settle(false); return; }
        warn(`source failed (${run.tries}/${MAX_SOURCE_TRIES}), redrawing`);
        if (!next()) settle(false);
      };
      v.onended = () => { if (run.alive && !next()) settle(false); };
      try { v.src = handle.url; } catch (_e) { return false; }
      play(v);
      return true;
    }

    run.settle = settle;
    /**
     * The pool has no playable video. That is the RECEIVER'S library gap, not a
     * failure of the sender's payload, so the render is a silent no-op.
     */
    run.begin = () => {
      if (!next()) { settle(false); return false; }
      return true;
    };
    run.retune = (v) => {
      run.intensity = clamp01(v);
      if (run.video) { try { run.video.volume = volumeFor(run.intensity); } catch (_e) { /* ignore */ } }
    };
    return run;
  }

  /** play(), with the autoplay-policy fallback: keep the PICTURE, drop the sound. */
  function play(v) {
    if (!v || typeof v.play !== 'function') return;
    let p = null;
    try { p = v.play(); } catch (_e) { return; }
    if (p && typeof p.catch === 'function') {
      p.catch(() => {
        try { v.muted = true; const q = v.play(); if (q && q.catch) q.catch(() => { /* give up quietly */ }); }
        catch (_e) { /* ignore */ }
        info('autoplay refused with sound — playing muted');
      });
    }
  }

  /* =========================================================================
   * THE FLOATING WINDOWS — the payload. Everything below this line lives on
   * #gg-fx-vwin and never touches #gg-stage.
   * ======================================================================= */

  /** Only the newest window speaks. Re-applied on every spawn and every settle. */
  function applyAudio() {
    const flags = unmutedFlags(wins.length);
    for (let i = 0; i < wins.length; i++) {
      const v = wins[i].video;
      if (!v) continue;
      try { v.muted = !flags[i]; } catch (_e) { /* ignore */ }
      try { v.volume = volumeFor(wins[i].intensity); } catch (_e) { /* ignore */ }
    }
  }

  /** The one place a window's transform is written: drag offset in screen px. */
  function paint(rec) {
    if (!rec || !rec.node) return;
    try { rec.node.style.setProperty('transform', `translate(${rec.dx.toFixed(1)}px, ${rec.dy.toFixed(1)}px)`); }
    catch (_e) { /* ignore */ }
  }

  function drop(rec) {
    const i = wins.indexOf(rec);
    if (i >= 0) wins.splice(i, 1);
  }

  function settleWindow(rec, endured) {
    if (!rec || rec.finished) return;
    rec.finished = true;
    try { clearTimeout(rec.endTimer); } catch (_e) { /* ignore */ }
    if (drag && drag.rec === rec) forgetDrag();
    drop(rec);

    const v = rec.video;
    if (v) {
      try { v.pause(); } catch (_e) { /* ignore */ }
      try { v.removeAttribute('src'); v.load(); } catch (_e) { /* ignore */ }
      v.onended = null; v.onerror = null; v.onloadedmetadata = null;
    }
    try { if (rec.handle && rec.handle.release) rec.handle.release(); } catch (_e) { /* ignore */ }
    rec.handle = null;
    rec.video = null;

    const node = rec.node;
    if (node) {
      try { node.classList.remove('is-on'); } catch (_e) { /* ignore */ }
      soon(() => { try { node.remove(); } catch (_e) { /* ignore */ } }, SETTLE_FADE_MS);
    }
    rec.node = null;
    applyAudio();
    if (typeof rec.onDone === 'function') {
      try { rec.onDone(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); }
    }
  }

  /* ------------------------------------------------------------ the press */

  const ptX = (e) => num(e && e.clientX, 0);
  const ptY = (e) => num(e && e.clientY, 0);
  const idMatch = (d, e) => !(d.pointerId != null && e && e.pointerId != null && e.pointerId !== d.pointerId);

  function listen(d, on) {
    const n = d.rec && d.rec.node;
    if (!n || typeof n.addEventListener !== 'function') return;
    const io = on ? 'addEventListener' : 'removeEventListener';
    try {
      n[io]('pointermove', onPointerMove);
      n[io]('pointerup', onPointerUp);
      n[io]('pointercancel', onPointerCancel);
      n[io]('lostpointercapture', onPointerCancel);
    } catch (_e) { /* ignore */ }
  }

  /** Tear the bookkeeping down without touching the window. */
  function forgetDrag() {
    if (!drag) return;
    listen(drag, false);
    try { clearTimeout(drag.watchdog); } catch (_e) { /* ignore */ }
    drag = null;
  }

  function onPointerDown(rec, e) {
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    if (e && e.button != null && e.button !== 0) return;   // right-click belongs to the page
    if (rec.finished || !rec.node || !rec.node.isConnected) return;

    if (drag) {
      // A second finger is IGNORED — but a drag whose node was torn out from
      // under us must never block the field forever.
      if (drag.rec && drag.rec.node && drag.rec.node.isConnected) return;
      forgetDrag();
    }

    const x = ptX(e), y = ptY(e);
    drag = {
      rec,
      pointerId: (e && e.pointerId != null) ? e.pointerId : null,
      x0: x, y0: y, baseDx: rec.dx, baseDy: rec.dy,
      grabbed: false, watchdog: 0, staleCapture: false,
    };
    listen(drag, true);
    drag.watchdog = soon(() => { if (drag) endDrag('watchdog'); }, HOLD_WATCHDOG_MS);
    try {
      if (typeof rec.node.setPointerCapture === 'function' && e && e.pointerId != null) {
        rec.node.setPointerCapture(e.pointerId);
      }
    } catch (_e) { /* capture is a nicety; the node listeners still work without it */ }
  }

  /**
   * Past the slop: the window LEAVES the keyframe world. The live drift offset
   * is folded into the drag offset FIRST, so `.is-grabbed`'s `animation: none`
   * (fx.css) freezes it exactly where it was instead of snapping it home.
   */
  function beginGrab(d) {
    const rec = d.rec;
    d.grabbed = true;
    rec.held = true;
    const fold = readDrift(rec);
    if (fold) {
      rec.dx += fold.x; rec.dy += fold.y;
      d.baseDx += fold.x; d.baseDy += fold.y;
    }
    try { rec.node.classList.add('is-grabbed'); } catch (_e) { /* ignore */ }
    lift(d);
    paint(rec);
  }

  /** The drift element's CURRENT translate, or null on a host without layout. */
  function readDrift(rec) {
    if (!rec || !rec.drift) return null;
    try {
      if (typeof getComputedStyle !== 'function') return null;
      return matrixXY(getComputedStyle(rec.drift).transform);
    } catch (_e) { return null; }
  }

  /**
   * Float the grabbed window over its siblings by DOM ORDER — fx.css rule 1
   * forbids raising z-index in this tier, and last-painted wins inside one layer.
   * THE CATCH (flashes.js's, verbatim): re-inserting a node counts as a removal,
   * and a removal implicitly RELEASES pointer capture. So we re-take it in the
   * same breath and swallow exactly ONE stale release notice.
   */
  function lift(d) {
    const rec = d.rec;
    try {
      const host = rec.node.parentNode;
      const kids = host && host.childNodes;
      if (!host || typeof host.appendChild !== 'function') return;
      if (kids && kids.length && kids[kids.length - 1] === rec.node) return;
      host.appendChild(rec.node);
      if (d.pointerId != null && typeof rec.node.setPointerCapture === 'function') {
        d.staleCapture = true;
        rec.node.setPointerCapture(d.pointerId);
      }
    } catch (_e) { /* ignore */ }
  }

  function onPointerMove(e) {
    const d = drag;
    if (!d || !idMatch(d, e)) return;
    // A stale release notice is dispatched BEFORE the next pointer event, so a
    // move means lift()'s re-capture went through and the licence has expired.
    d.staleCapture = false;
    const x = ptX(e), y = ptY(e);
    if (!d.grabbed) {
      if (Math.hypot(x - d.x0, y - d.y0) <= DRAG_SLOP_PX) return;   // still a click
      beginGrab(d);
    }
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    d.rec.dx = d.baseDx + (x - d.x0);
    d.rec.dy = d.baseDy + (y - d.y0);
    paint(d.rec);
  }

  /**
   * THE ONE EXIT. `why` is 'up' (released), 'cancel', 'stop' or 'watchdog'.
   * Only 'up' can dismiss; every other reason is a gentle drop, which is what
   * "no stuck held windows" means.
   */
  function endDrag(why) {
    const d = drag;
    if (!d) return;
    drag = null;
    listen(d, false);
    try { clearTimeout(d.watchdog); } catch (_e) { /* ignore */ }

    const rec = d.rec;
    if (!rec || !rec.node) return;

    if (!d.grabbed) {
      // Never passed the slop: this press was a CLICK, and a click dismisses.
      // The player watched it until they closed it, so that is ENDURED.
      if (why === 'up' && rec.node.isConnected) settleWindow(rec, true);
      return;
    }

    rec.held = false;
    try { rec.node.classList.remove('is-grabbed'); } catch (_e) { /* ignore */ }
    // The drift restarts from wherever it was dropped (its offset is already in
    // rec.dx/dy), so letting go moves nothing either.
    paint(rec);
  }

  function onPointerUp(e) {
    const d = drag;
    if (!d || !idMatch(d, e)) return;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    endDrag('up');
  }

  /** pointercancel / lostpointercapture: the hand is empty and the window stays
   *  exactly where it was, un-dismissed. The one exception is the release lift()
   *  causes on purpose — paperwork, swallowed once. */
  function onPointerCancel(e) {
    const d = drag;
    if (!d || !idMatch(d, e)) return;
    if (d.staleCapture && e && e.type === 'lostpointercapture') {
      d.staleCapture = false;
      const n = d.rec && d.rec.node;
      try { if (n && typeof n.hasPointerCapture === 'function' && n.hasPointerCapture(d.pointerId)) return; }
      catch (_e) { /* fall through and try to re-take it */ }
      try { if (n && d.pointerId != null && typeof n.setPointerCapture === 'function') { n.setPointerCapture(d.pointerId); return; } }
      catch (_e) { /* genuinely gone: drop it */ }
    }
    endDrag('cancel');
  }

  /**
   * Wheel over a window resizes it through --gg-vwin-w — a WIDTH var, never a
   * stacked transform scale, which would fight the drag offset. No press needed:
   * the pointer being over the window is the whole gesture.
   */
  function onWheel(rec, e) {
    if (!rec || rec.finished || !rec.node) return;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();   // the page never scrolls
    if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    const dy = num(e && e.deltaY, 0);
    if (!dy) return;
    const notches = Math.max(-3, Math.min(3, Math.round(dy / 100) || (dy > 0 ? 1 : -1)));
    const lo = rec.baseW * SIZE_MIN_FACTOR;
    const hi = rec.baseW * SIZE_MAX_FACTOR;
    const want = rec.widthPx * Math.pow(WHEEL_STEP, -notches);   // wheel UP (deltaY<0) grows
    const next = want < lo ? lo : (want > hi ? hi : want);
    if (Math.abs(next - rec.widthPx) < 0.01) return;
    rec.widthPx = next;
    try { rec.node.style.setProperty('--gg-vwin-w', `${next.toFixed(0)}px`); } catch (_e) { /* ignore */ }
  }

  /* ------------------------------------------------------------ the window */

  /** Build + mount one window. Returns the record, or null if it cannot exist. */
  function openWindow(intensity, capMs, onDone) {
    if (typeof document === 'undefined') return null;
    const host = winLayer();
    if (!host) return null;
    if (!media || typeof media.drawKind !== 'function') return null;

    const entry = media.drawKind('video');
    if (!entry) return null;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return null;

    // At the cap the OLDEST settles first — it has been watched the longest, so
    // it is endured, not interrupted.
    while (wins.length >= MAX_WINDOWS) settleWindow(wins[0], true);

    const baseW = baseWindowWidth(vpW());
    const pos = placeWindow(vpW(), vpH(), baseW, baseW * 9 / 16);
    const drift = driftVars();

    const node = document.createElement('div');
    node.className = 'gg-vwin';
    node.style.setProperty('left', `${pos.x}px`);
    node.style.setProperty('top', `${pos.y}px`);
    node.style.setProperty('--gg-vwin-w', `${baseW}px`);

    const driftEl = document.createElement('div');
    driftEl.className = calm ? 'gg-vwin-drift' : 'gg-vwin-drift gg-deco';
    driftEl.style.setProperty('--gg-vwin-dx', `${drift.dx}px`);
    driftEl.style.setProperty('--gg-vwin-dy', `${drift.dy}px`);
    driftEl.style.setProperty('--gg-vwin-drift-dur', `${drift.durS}s`);

    const inner = document.createElement('div');
    // ONE one-shot glitch-in, on its OWN element: the drift loop and the dot
    // pulse are shorthand `animation` declarations too, and two of those on one
    // element cancel each other (see the banner).
    inner.className = `gg-vwin-inner gg-vwin-in--${glitchVariant()}`;

    const video = document.createElement('video');
    video.className = 'gg-vwin-vid';
    video.playsInline = true;
    video.autoplay = true;
    video.controls = false;
    video.preload = 'auto';
    video.loop = false;               // it ends when the CLIP ends
    video.muted = true;               // applyAudio() hands the mic to the newest
    video.volume = volumeFor(intensity);

    const dot = document.createElement('span');
    dot.className = calm ? 'gg-vwin-dot' : 'gg-vwin-dot gg-deco';

    inner.appendChild(video);
    inner.appendChild(dot);
    driftEl.appendChild(inner);
    node.appendChild(driftEl);

    const rec = {
      node, drift: driftEl, video, handle, onDone,
      intensity: clamp01(intensity),
      dx: 0, dy: 0, held: false, finished: false,
      baseW, widthPx: baseW, tries: 0, endTimer: 0, bornAt: nowMs(),
    };

    video.onerror = () => {
      rec.tries++;
      if (rec.tries >= MAX_SOURCE_TRIES) { settleWindow(rec, false); return; }
      warn(`window source failed (${rec.tries}/${MAX_SOURCE_TRIES}), redrawing`);
      if (!reSource(rec)) settleWindow(rec, false);
    };
    video.onended = () => { if (!rec.finished) settleWindow(rec, true); };
    // The window wears the CLIP's own shape once the browser knows it.
    video.onloadedmetadata = () => {
      const w = num(video.videoWidth, 0), h = num(video.videoHeight, 0);
      if (w > 0 && h > 0) { try { node.style.setProperty('--gg-vwin-ar', `${w} / ${h}`); } catch (_e) { /* ignore */ } }
    };
    node.addEventListener('pointerdown', (e) => onPointerDown(rec, e));
    try { node.addEventListener('wheel', (e) => onWheel(rec, e), { passive: false }); }
    catch (_e) { try { node.addEventListener('wheel', (e) => onWheel(rec, e)); } catch (_e2) { /* ignore */ } }

    try { video.src = handle.url; } catch (_e) { /* onerror will redraw */ }
    host.appendChild(node);
    wins.push(rec);
    soon(() => { if (!rec.finished && rec.node) rec.node.classList.add('is-on'); }, 16);
    applyAudio();
    play(video);
    rec.endTimer = soon(() => settleWindow(rec, true), capMs);
    return rec;
  }

  /** Redraw a source into a window whose entry turned out to be a dud. */
  function reSource(rec) {
    if (!rec || rec.finished || !rec.video) return false;
    if (!media || typeof media.drawKind !== 'function') return false;
    const entry = media.drawKind('video');
    if (!entry) return false;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return false;
    try { if (rec.handle && rec.handle.release) rec.handle.release(); } catch (_e) { /* ignore */ }
    rec.handle = handle;
    try { rec.video.src = handle.url; } catch (_e) { return false; }
    play(rec.video);
    return true;
  }

  return {
    name: 'videos',

    start(cue) {
      const intensity = clamp01(cue && cue.intensity);
      if (sustained && sustained.alive) { sustained.retune(intensity); return; }
      sustained = createRun({ intensity, onDone: null });
      sustained.begin();
    },

    setIntensity(v) { if (sustained && sustained.alive) sustained.retune(v); },

    stop() {
      // The element run only. Payload windows are independent (uniform shape) —
      // executor.stopAll() cancels those through their own cancel fn.
      if (!sustained) return;
      sustained.settle(false);   // no receipt path on the element run (onDone is null)
      sustained = null;
    },

    /**
     * ONE floating window per payload. It dies on whichever comes first: the
     * clip ending, the duration cap (never more than WINDOW_CAP_MS), a click, or
     * a fifth window displacing it — all of those are ENDURED. Only the cancel
     * fn (mercy / stopAll) and an empty library are not.
     */
    renderPayload(payload, done) {
      const p = payload || {};
      const capMs = windowCapMs(p.duration_ms);
      const intensity = p.intensity !== undefined ? p.intensity : 0.6;
      const rec = openWindow(intensity, capMs, done);
      if (!rec) {
        // Nothing playable in the receiver's library: a silent no-op, receipted
        // completed (endured=false — nothing was endured, so no charge).
        if (typeof done === 'function') { try { done(false); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
        return () => {};
      }
      return () => settleWindow(rec, false);
    },

    /** Test/diagnostic seam: how many windows are floating right now. */
    windowCount: () => wins.length,
  };
}

export default createVideos;
