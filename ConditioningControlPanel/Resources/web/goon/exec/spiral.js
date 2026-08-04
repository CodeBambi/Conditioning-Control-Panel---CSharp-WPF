/* ============================================================================
 * exec/spiral.js — GoonElement.Spiral (8) + GoonPayloadKind.Spiral (7).
 *
 * The DtRH spiral, straight across: ONE persistent full-window pane on
 * #gg-fx-spiral, screen-blended and spinning slowly (.gg-spiral in fx.css is the
 * port of DtRH's .sf-pfx-spiral). Intensity moves exactly one dial — opacity,
 * 0.25..0.70, the same band payloadFx.showSpiral uses — because "more spiral"
 * is the whole idea.
 *
 * ONE ELEMENT, ALWAYS. The element and the payload share the pane through a dial
 * stack (whoever wants it louder wins); a payload landing on a running bed does
 * not add a second spinning cover pass.
 *
 * TWO WAYS TO FILL THAT PANE, AND THE GOOD ONE IS PREFERRED.
 *   1. WOVEN (default). exec/spiralField.js renders the Loom's own spiral field
 *      live in WebGL at the window's real pixel ratio — no magnification, no
 *      palette, no dither. The pane gets a <canvas> child and its CSS
 *      magnification/softening (`scale: 1.6`, `filter: blur(1.1px)`) and its
 *      spin keyframe are all cancelled INLINE, because a live renderer needs
 *      none of them: the shader oversizes to the corners itself and does its own
 *      rotation. See that module's banner for why the rasters were grainy.
 *   2. RASTER (fallback). No WebGL, no canvas, or a lost context -> the pane
 *      keeps the bundled sp*.gif pool exactly as it always worked, blur and
 *      overscan included. The fallback is never removed and never degraded; it
 *      is what a machine without a GPU still gets.
 * The pane always carries a pool background-image either way, so path 2 is one
 * property removal away at any moment.
 *
 * THE FREEZE DEFENCE (2026-08-04). This pane is the only GPU context the duel
 * owns, and it went in hours before a session froze VISUALLY while its JS kept
 * running — a compositor/GPU stall, no crash, no dump. Nothing here proves the
 * shader caused it, but a shader is the cheapest thing to be able to drop, so it
 * is now droppable four different ways and every one of them lands on path 2:
 *   · `webglcontextlost` — preventDefault, swap to raster THAT INSTANT, and keep
 *     the dead canvas around (shrunk to 1x1) purely as an antenna for
 *     `webglcontextrestored`, which may re-upgrade at most MAX_CONTEXT_RECOVERIES
 *     times before the pane stays on raster for good;
 *   · a WATCHDOG inside the render loop, once a second, never per frame: a lost
 *     context (polled, because an event needs an event loop that a stall is
 *     busy eating) or rAF callbacks more than RAF_STALL_MS apart while the pane
 *     believes it is drawing;
 *   · the KILL SWITCH — `shaderSpirals` in ui/prefs.js, default ON, mirrored to
 *     <html data-gg-shader>. Off, weave() never runs and a live weave is dropped
 *     within a second. exec/ never imports ui/, so it arrives as a root attribute
 *     exactly the way data-gg-vskip does;
 *   · and the backing store is capped (MAX_DPR / MAX_BACKING_PX) so a 4K display
 *     at devicePixelRatio 2 cannot ask the driver for an 8K fill every frame.
 * The rAF-stall check is deliberately blind while the page is hidden or the heat
 * armor has parked decoration: not painting is CORRECT in both, and a watchdog
 * that fires on correct behaviour gets switched off by the people it protects.
 *
 * ASSETS: the pool is the DtRH bundle under /dtrh/assets/bubbles/effects/spirals/
 * (same ccp.game origin — Resources/web is the host root, so /dtrh/... resolves
 * from /goon/). Deliberately NOT imported from dtrh/engine/loomSpirals.js: that
 * module pulls DtRH settings/Loom storage in with it. The pick is three lines.
 * exec/spiralField.js obeys the same rule — it is a COPY of the Loom's shader,
 * not an import, and it never touches the player's saved-spiral library.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

import { createSpiralField, pickSpiralParams, loopMsFor } from './spiralField.js';

/**
 * The bundled spiral pool — the FALLBACK bed (DtRH ships these; goon reads them
 * off the same host) and the source for the bubble-pop flick, which is a brief
 * transient where magnification never gets the chance to read as grain.
 *
 * sp7.gif IS DELIBERATELY NOT IN HERE. Measured, these are 360x360..720x720 with
 * palettes as thin as 8 and 16 colours, and a fullscreen cover plus
 * `.gg-spiral { scale: 1.6 }` magnifies them 4.3x (sp2/sp4) to 8.5x (sp7) — so
 * sp7 is the worst case by a wide margin. The rest are softened enough by
 * `.gg-spiral { filter: blur(1.1px) }` in fx.css, which is why that blur must
 * STAY for this path even though the woven path cancels it. The FILE stays on
 * disk — DtRH owns it and still uses it.
 */
export const SPIRAL_DIR = '/dtrh/assets/bubbles/effects/spirals/';
export const SPIRAL_POOL = Object.freeze([
  'sp1.gif', 'sp2.webp', 'sp3.gif', 'sp4.webp', 'sp5.gif', 'sp6.gif',
]);
/** The one still that ships outside the pool — the floor if the pool is ever empty. */
export const SPIRAL_FALLBACK = '/dtrh/assets/bubbles/effects/spiral.png';

/* --- THE SHADER KILL SWITCH. The pref is ui/prefs.js's (`shaderSpirals`,
   default TRUE); it reaches this tier as a ROOT ATTRIBUTE, because exec/ does not
   import ui/ and a renderer built once at startup must still see a toggle flipped
   mid-match. ABSENT MEANS ON — the attribute only exists once a prefs store has
   been built, and a page that never made one (a self-test, the import sweep) must
   still get the good path. Only the exact off value switches it off. ---------- */
export const SHADER_ATTR = 'data-gg-shader';   // on <html>, written by ui/prefs.js
export const SHADER_OFF = 'off';               // …anything else, or absent, is ON

/* --- THE BACKING-STORE CAP. A screen-blended bed at <=0.70 opacity behind a live
   match does not need retina detail, and the fill cost is per-pixel per-frame:
   this is the single biggest lever on how hard the shader leans on the GPU.
   TWO caps, because either alone has a hole. The DPR cap alone still lets a 4K
   window at DPR 1 through at 8.3MP; the pixel budget alone still lets a small
   window run at DPR 3. MAX_BACKING_PX is one 4K frame, which is the most any
   display in front of this game can actually show. ------------------------- */
export const MAX_DPR = 1.5;
export const MAX_BACKING_PX = 3840 * 2160;

/* --- THE IN-LOOP WATCHDOG. Checked once per HEALTH_CHECK_MS, never per frame:
   gl.getError()/isContextLost() can force a sync round-trip to the driver, which
   is precisely the thing not to do sixty times a second on a machine that may
   already be stalling. RAF_STALL_MS is the gap between frame callbacks that
   stops counting as a hitch and starts counting as a hang. ------------------ */
export const HEALTH_CHECK_MS = 1000;
export const RAF_STALL_MS = 4000;
/** Context losses this pane will re-upgrade through before it stays on raster. */
export const MAX_CONTEXT_RECOVERIES = 2;

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
  const sfx = (id) => { try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ } };
  const calm = reducedMotion();

  let paneEl = null;
  let elementIntensity = null;    // null = element not running
  let payloadIntensity = null;    // null = no payload running

  // --- the woven path's state (all null on the raster fallback) -------------
  let canvasEl = null;
  let field = null;
  let params = null;
  let rafId = 0;
  let phase = 0;
  let lastTs = 0;
  let onLost = null;
  let onResize = null;
  let onVisibility = null;
  let lastCheck = 0;              // in-loop watchdog: last health poll (rAF timestamp ms)
  // A defence fired and this pane stays on raster until it is replaced. Cleared
  // only by teardown (a new pane earns a fresh try) or by a context RESTORE
  // inside the recovery budget — never by a repick, or a machine whose driver is
  // in trouble would be handed the shader back every few seconds.
  let rasterLocked = false;
  // A context-lost canvas, shrunk to 1x1 and kept ONLY to hear
  // `webglcontextrestored`. It is out of the DOM and holds no drawing buffer, so
  // it is an antenna, not a leak — and teardown drops it either way.
  let lostCanvas = null;
  let onRestored = null;
  let recoveries = 0;             // context-loss re-upgrades spent on THIS pane
  // Bumped on every weave. A frame callback queued by a weave that has since
  // been torn down carries a stale token and returns without re-arming itself —
  // without this, the pane's own fade-out could hand its rAF chain to the NEXT
  // pane and leave two render loops driving one canvas.
  let weaveGen = 0;

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('spiral') : null);
  const doc = () => (typeof document !== 'undefined' ? document : null);
  const hasRaf = () => typeof requestAnimationFrame === 'function' && typeof cancelAnimationFrame === 'function';
  /** The heat armor parks decoration through CSS; a canvas has to check itself. */
  const parked = () => {
    const d = doc();
    try { return !!(d && d.documentElement && d.documentElement.getAttribute('data-gg-fx') === 'hot'); }
    catch (_e) { return false; }
  };
  /** The kill switch, read FRESH every time — a cached copy is a dead toggle. */
  const shadersOn = () => {
    const d = doc();
    try { return !(d && d.documentElement && d.documentElement.getAttribute(SHADER_ATTR) === SHADER_OFF); }
    catch (_e) { return true; }
  };
  /** A hidden page is not painting ON PURPOSE; the stall watchdog must ignore it. */
  const visible = () => {
    const d = doc();
    try { return !d || !d.visibilityState || d.visibilityState === 'visible'; }
    catch (_e) { return true; }
  };

  function ensurePane() {
    if (paneEl && paneEl.isConnected) return true;
    const host = layer();
    if (!host || typeof document === 'undefined') return false;
    paneEl = document.createElement('div');
    // .gg-deco so the heat armor can park the spin when the stack goes hot.
    // (The woven path cancels the keyframe and parks itself — see step().)
    paneEl.className = calm ? 'gg-spiral' : 'gg-spiral gg-deco';
    repick();
    host.appendChild(paneEl);
    weave();          // AFTER the append: the canvas needs a laid-out parent to size against.
    return true;
  }

  /**
   * Backing-store size for the pane — CAPPED TWICE (see MAX_DPR / MAX_BACKING_PX).
   *
   * The fill is per-pixel per-frame, so this number IS the GPU cost of the bed.
   * The ratio cap keeps a HiDPI laptop from doubling that for detail nobody can
   * resolve through a screen blend at 0.7; the pixel budget keeps a 4K (or wider)
   * window from asking for a frame no display can show, whatever the ratio says.
   * Aspect is preserved when the budget bites — a squashed spiral is worse than a
   * slightly soft one.
   */
  function backingSize() {
    const w = (paneEl && paneEl.clientWidth) || (typeof innerWidth === 'number' ? innerWidth : 0);
    const h = (paneEl && paneEl.clientHeight) || (typeof innerHeight === 'number' ? innerHeight : 0);
    const dpr = Math.min(MAX_DPR, (typeof devicePixelRatio === 'number' && devicePixelRatio > 0) ? devicePixelRatio : 1);
    let bw = Math.max(1, Math.round(w * dpr));
    let bh = Math.max(1, Math.round(h * dpr));
    const total = bw * bh;
    if (total > MAX_BACKING_PX) {
      const k = Math.sqrt(MAX_BACKING_PX / total);
      bw = Math.max(1, Math.round(bw * k));
      bh = Math.max(1, Math.round(bh * k));
    }
    return { w: bw, h: bh };
  }

  function sizeCanvas() {
    if (!canvasEl) return false;
    const { w, h } = backingSize();
    if (canvasEl.width === w && canvasEl.height === h) return false;
    canvasEl.width = w;
    canvasEl.height = h;
    return true;
  }

  /**
   * Try to upgrade the pane to the live Loom field. Every failure here is silent
   * and lands on the raster bed, which is already mounted and already correct.
   */
  function weave() {
    if (field || !paneEl) return;
    // The two ways the pane is entitled to stay on raster: the player switched
    // shaders off, or a defence already fired on this pane.
    if (rasterLocked || !shadersOn()) return;
    const d = doc();
    if (!d || typeof d.createElement !== 'function') return;

    let cv = null;
    try { cv = d.createElement('canvas'); } catch (_e) { return; }
    if (!cv || typeof cv.getContext !== 'function' || !cv.style) return;

    canvasEl = cv;
    sizeCanvas();
    const f = createSpiralField(cv);
    if (!f) { canvasEl = null; return; }
    field = f;
    params = pickSpiralParams();
    const gen = ++weaveGen;

    phase = Math.random();      // never start every match on the same frame
    lastTs = 0;
    lastCheck = 0;
    // THE FIRST FRAME GOES IN BEFORE THE APPEND. The context is `alpha: false`,
    // so an un-drawn canvas is OPAQUE BLACK: appending it and drawing afterwards
    // blinks the bed out for a frame. It matters most on the re-upgrade after a
    // context loss, where the raster bed is already up and visibly correct.
    draw();
    if (!field) { canvasEl = null; return; }   // that first draw threw and unwove us

    // fx.css is not ours to edit and must keep serving the raster fallback, so
    // the woven pane opts out of the raster-only treatments INLINE:
    //   scale 1.6  -> the shader oversizes to the corners itself, and scaling a
    //                 canvas up 1.6x would throw away the resolution we came for
    //   blur 1.1px -> that blur exists to hide dither; there is none here
    //   animation  -> the shader owns the rotation (and its loop is seamless)
    try {
      cv.style.setProperty('position', 'absolute');
      cv.style.setProperty('inset', '0');
      cv.style.setProperty('width', '100%');
      cv.style.setProperty('height', '100%');
      cv.style.setProperty('display', 'block');
      paneEl.style.setProperty('scale', '1');
      paneEl.style.setProperty('filter', 'none');
      paneEl.style.setProperty('animation', 'none');
      paneEl.appendChild(cv);
    } catch (_e) { unweave(); return; }

    // A GPU reset kills the context without killing the page. preventDefault is
    // what makes the loss RECOVERABLE (without it the browser never fires
    // `webglcontextrestored`), and the swap to raster happens on the spot — the
    // player must not sit in front of a frozen last frame waiting for a restore
    // that may never come.
    if (typeof cv.addEventListener === 'function') {
      onLost = (e) => {
        try { e.preventDefault(); } catch (_e2) { /* ignore */ }
        warn('webgl context lost — falling back to the raster pool');
        loseWoven();
      };
      try { cv.addEventListener('webglcontextlost', onLost); } catch (_e) { /* ignore */ }
    }
    if (typeof addEventListener === 'function') {
      onResize = () => { if (sizeCanvas() && field && params) { try { field.render(params, phase); } catch (_e) { /* ignore */ } } };
      try { addEventListener('resize', onResize); } catch (_e) { onResize = null; }
    }
    // Coming back from hidden is NOT a stall, but it does look exactly like one
    // from inside the loop: rAF is suspended while the page is hidden, so the
    // first frame after a resume carries a gap of however long the player was
    // away. Forget the last timestamp on every visibility change and the
    // watchdog simply starts measuring again from the next frame pair.
    if (typeof d.addEventListener === 'function') {
      onVisibility = () => { lastTs = 0; lastCheck = 0; };
      try { d.addEventListener('visibilitychange', onVisibility); } catch (_e) { onVisibility = null; }
    }

    // Reduced motion gets ONE still frame — and unlike a GIF, which ignores the
    // preference entirely, this actually honours it.
    if (!calm && hasRaf()) rafId = requestAnimationFrame((ts) => step(ts, gen));
  }

  function draw() {
    if (!field || !params) return;
    try { field.render(params, phase); }
    catch (e) { warn(`field render threw: ${e && e.message}`); unweave(); }
  }

  /**
   * The in-loop watchdog. ONCE A SECOND, never per frame — asking the driver
   * whether it is still there can cost a synchronous round-trip, and a machine
   * that is already stalling is the last one to ask sixty times a second.
   *
   * Returns '' while the pane is well, or the reason it must go back to raster
   * ('off' | 'lost' | 'stall'). `now` is the rAF timestamp, i.e.
   * performance.now()'s clock, so the frame gap it measures is the real one and
   * not a timer's opinion of it.
   */
  function healthCheck(now) {
    // 1. the player switched shaders off mid-match — the escape hatch has to
    //    reach a bed that is already running, not just the next one.
    if (!shadersOn()) { warn('shader spirals switched off — handing the pane back to the raster bed'); return 'off'; }
    // 2. the context went away without the event reaching us. An event needs an
    //    event loop, which is exactly what a compositor stall is busy eating.
    if (field && typeof field.lost === 'function' && field.lost()) {
      warn('webgl context lost (polled) — falling back to the raster pool');
      return 'lost';
    }
    // 3. frames are not arriving while this pane believes it is drawing. Blind
    //    while hidden (rAF is suspended by design) and while the heat armor has
    //    parked decoration; lastTs 0 means "no pair to measure yet".
    if (lastTs && visible() && !parked() && (now - lastTs) > RAF_STALL_MS) {
      warn(`frame callbacks stalled ${Math.round(now - lastTs)}ms — falling back to the raster pool`);
      return 'stall';
    }
    return '';
  }

  function step(ts, gen) {
    if (gen !== weaveGen) return;      // a torn-down weave's last frame
    rafId = 0;
    if (!field || !paneEl) return;
    const now = typeof ts === 'number' ? ts : 0;
    if (now - lastCheck >= HEALTH_CHECK_MS) {
      lastCheck = now;
      const ill = healthCheck(now);
      if (ill) {
        // 'off' is the player's own choice and must be reversible: leave the
        // pane unlocked so switching shaders back on re-weaves at the next cue.
        if (ill !== 'off') rasterLocked = true;
        if (ill === 'lost') loseWoven(); else unweave();
        return;
      }
    }
    // Parked at data-gg-fx="hot": hold the frame instead of advancing it, the
    // same thing --gg-deco-play does to every keyframe in fx.css.
    if (!parked()) {
      const loop = Math.max(1, loopMsFor(params));
      if (lastTs) phase = (phase + (now - lastTs) / loop) % 1;
      draw();
    }
    lastTs = now;
    if (hasRaf()) rafId = requestAnimationFrame((t) => step(t, gen));
  }

  /**
   * Lift the live weave OUT of module state and hand back its disposer. The
   * caller decides when the GPU resources actually go — teardown holds them
   * until the pane has finished fading, because releasing them early would
   * swap a crisp spiral for a magnified raster one mid-fade, in full view.
   */
  function detachWoven() {
    const w = { cv: canvasEl, f: field, raf: rafId, lost: onLost, resize: onResize, vis: onVisibility };
    weaveGen++;                        // every queued frame for this weave is now stale
    canvasEl = null; field = null; params = null;
    rafId = 0; lastTs = 0; lastCheck = 0; onLost = null; onResize = null; onVisibility = null;
    return () => {
      if (w.raf && typeof cancelAnimationFrame === 'function') {
        try { cancelAnimationFrame(w.raf); } catch (_e) { /* ignore */ }
      }
      if (w.resize && typeof removeEventListener === 'function') {
        try { removeEventListener('resize', w.resize); } catch (_e) { /* ignore */ }
      }
      if (w.vis) {
        const d = doc();
        if (d && typeof d.removeEventListener === 'function') {
          try { d.removeEventListener('visibilitychange', w.vis); } catch (_e) { /* ignore */ }
        }
      }
      if (w.cv && w.lost && typeof w.cv.removeEventListener === 'function') {
        try { w.cv.removeEventListener('webglcontextlost', w.lost); } catch (_e) { /* ignore */ }
      }
      // dispose() force-loses the context. Panes come and go all match and a
      // page only gets a handful of live WebGL contexts, so this is not optional.
      if (w.f) { try { w.f.dispose(); } catch (_e) { /* ignore */ } }
      if (w.cv) { try { w.cv.remove(); } catch (_e) { /* ignore */ } }
    };
  }

  /**
   * Drop the weave NOW and give the surviving pane back to the raster bed.
   *
   * REMOVING THE THREE INLINE PROPERTIES IS THE WHOLE SWAP, and it has to remove
   * ALL THREE: fx.css's `.gg-spiral` carries `scale: 1.6`, `filter: blur(1.1px)`
   * and the spin keyframe as one treatment for one bed. Leaving `filter: none`
   * behind would hand the player a magnified, unblurred, unspun raster — the
   * dither this pane exists to hide, at 4-8x, held still. Setting each property
   * to '' (not to its CSS value) is what lets the stylesheet answer again.
   */
  function unweave() {
    detachWoven()();
    // Hand the raster treatments back exactly as fx.css declares them.
    if (paneEl && paneEl.style) {
      try {
        paneEl.style.setProperty('scale', '');
        paneEl.style.setProperty('filter', '');
        paneEl.style.setProperty('animation', '');
      } catch (_e) { /* ignore */ }
    }
  }

  /** Stop listening to a context-lost canvas, and let the node go. */
  function releaseLostCanvas() {
    if (lostCanvas && onRestored && typeof lostCanvas.removeEventListener === 'function') {
      try { lostCanvas.removeEventListener('webglcontextrestored', onRestored); } catch (_e) { /* ignore */ }
    }
    lostCanvas = null;
    onRestored = null;
  }

  /**
   * The context died under us (event or poll): swap to raster on the spot, then
   * keep the dead canvas ONLY as an antenna for `webglcontextrestored`.
   *
   * It is shrunk to 1x1 first. A fullscreen backing store is megabytes, the
   * re-upgrade builds a brand new canvas anyway, and a detached full-size canvas
   * held across a GPU reset is how a defence turns into a leak. The budget
   * (MAX_CONTEXT_RECOVERIES) is what stops a driver that resets every few
   * seconds from being handed the shader back forever.
   */
  function loseWoven() {
    const cv = canvasEl;
    rasterLocked = true;               // nothing re-weaves until a restore says so
    unweave();
    releaseLostCanvas();
    if (!cv || recoveries >= MAX_CONTEXT_RECOVERIES || typeof cv.addEventListener !== 'function') return;
    try { cv.width = 1; cv.height = 1; } catch (_e) { /* ignore */ }
    onRestored = () => {
      releaseLostCanvas();
      recoveries++;
      rasterLocked = false;
      // Lazily, and only if the pane is still up: a restore that arrives after
      // the bed has gone must not resurrect one.
      if (paneEl && !field && shadersOn()) weave();
    };
    lostCanvas = cv;
    try { cv.addEventListener('webglcontextrestored', onRestored); }
    catch (_e) { lostCanvas = null; onRestored = null; }
  }

  /**
   * Draw a different spiral. BOTH beds are repicked every time: the woven one
   * takes a new preset, and the raster background-image is refreshed too so the
   * fallback underneath is never stale if the context dies mid-match.
   */
  function repick() {
    if (!paneEl || !paneEl.style) return;
    try { paneEl.style.setProperty('background-image', `url('${pickSpiralUrl()}')`); }
    catch (_e) { /* a host without inline style support just keeps the CSS default */ }
    if (field) {
      params = pickSpiralParams();
      draw();
      return;
    }
    // A cue is also where the kill switch coming back ON takes effect. Swapping
    // raster -> shader in the middle of a running bed would read as a glitch; a
    // new spiral is the moment the bed is expected to change anyway. A pane that
    // a DEFENCE dropped (rasterLocked) is never re-woven here — only a context
    // restore, inside its budget, may do that.
    // isConnected, because ensurePane() repicks BEFORE it mounts the pane and a
    // canvas sized against an unlaid-out parent measures nothing; that first
    // weave is ensurePane's own, one line later.
    if (paneEl.isConnected && !rasterLocked && shadersOn()) weave();
  }

  function teardown() {
    if (!paneEl) return;
    const el = paneEl;
    // The canvas KEEPS ITS LAST FRAME on screen for the whole fade — see
    // detachWoven(). Releasing it here instead would uncover the raster
    // background underneath and the bed would visibly jump from crisp to
    // magnified for 900ms on the way out. Detaching from module state right away
    // also means a pane that comes back inside those 900ms weaves a fresh one.
    const dispose = detachWoven();
    // The defences are per-PANE. A machine that lost a context or stalled during
    // one bed gets a clean try at the next one — the alternative is one bad
    // moment costing the player the good spiral for the rest of the session.
    releaseLostCanvas();
    rasterLocked = false;
    recoveries = 0;
    el.classList.remove('is-on');
    soon(() => {
      dispose();
      try { el.remove(); } catch (_e) { /* ignore */ }
    }, 900);
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
      if (fresh) { repick(); sfx('spiral-in'); }   // a new bed gets a new spiral; a re-tune keeps its own
    },

    setIntensity(v) {
      if (elementIntensity === null) return;
      elementIntensity = clamp01(v);
      apply();
    },

    stop() {
      elementIntensity = null;
      apply();
      sfx('spiral-out');
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
