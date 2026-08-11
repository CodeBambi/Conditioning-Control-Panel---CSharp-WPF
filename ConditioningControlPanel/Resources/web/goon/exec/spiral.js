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
 * NOTHING HERE IS BAKED ANY MORE (2026-08-04). The bed used to draw from six
 * bundled DtRH files (/dtrh/assets/bubbles/effects/spirals/sp*.gif|webp) and,
 * on the shader path, from six hand-authored presets. Both are gone. Every
 * spiral is now ROLLED at run time by exec/spiralGen.js — the Loom's "surprise
 * me" generator, ported and re-tuned — which pre-generates a SESSION'S WORTH
 * (five) at executor build and rotates through them, never the same one twice
 * running. NO CENTERPIECE: the Loom's dot/cross overlays are excluded at the
 * schema level, not filtered out. The DtRH files stay on disk; DtRH owns them
 * and still draws them. Goon simply stopped pointing at them.
 *
 * TWO WAYS TO FILL THAT PANE, AND THEY NOW SHOW THE SAME SPIRAL.
 *   1. WOVEN (default). exec/spiralField.js renders the variant's parameters
 *      live in WebGL at the window's real pixel ratio. The pane gets a <canvas>
 *      child and its CSS magnification/softening (`scale: 1.6`,
 *      `filter: blur(1.1px)`) and its spin keyframe are all cancelled INLINE,
 *      because a live renderer needs none of them: the shader oversizes to the
 *      corners itself and does its own rotation.
 *   2. RASTER (fallback). No WebGL, no canvas, or a lost context -> the pane
 *      falls back to a still PNG of THE SAME VARIANT, baked on a 2D canvas by
 *      spiralGen (a port of the Loom's own no-WebGL renderer) and spun by the
 *      fx.css keyframe at the variant's own solved revolution time. The
 *      fallback is never removed and never degraded; it is what a machine
 *      without a GPU still gets.
 * Path 2 is one property removal away at any moment, and because both paths
 * draw the same roll, a mid-match context loss changes the RENDERER, not the
 * picture.
 *
 * THE STILL IS PAINTED WHEN THE PANE LANDS ON RASTER, NOT BEFORE (2026-08-05).
 * The pane used to carry the baked background-image ALWAYS, woven or not, on
 * the theory that a bed which already holds its own fallback cannot be caught
 * out by a context loss. What that actually bought, on a phone, was a ~1600-fill
 * canvas bake plus a PNG encode plus a multi-megabyte data-URL decode — for a
 * picture sitting underneath an OPAQUE canvas where no player can ever see it —
 * landing on the main thread inside the cue's own first frames, which is
 * precisely the stutter the owner reported (a spiral that mounts and then holds
 * still for a second or two). Now: the bake is WARMED at build during idle time
 * (exec/spiralGen.js#warmSpiralStills), the woven pane skips painting it, and
 * unweave() — the one door every fallback goes through — paints it on the way
 * down. The guarantee is unchanged and the cost moved off the cue.
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
 *     believes it is drawing — TWICE OVER, on two separate pairs of frames,
 *     because one fat gap is a GC/occlusion hitch far more often than it is a
 *     hang and the lock it sets lasts the rest of the pane's life;
 *   · the KILL SWITCH — `shaderSpirals` in ui/prefs.js, default ON, mirrored to
 *     <html data-gg-shader>. Off, weave() never runs and a live weave is dropped
 *     within a second. exec/ never imports ui/, so it arrives as a root attribute
 *     exactly the way data-gg-vskip does;
 *   · and the backing store is capped (MAX_DPR / MAX_BACKING_PX) so a 4K display
 *     at devicePixelRatio 2 cannot ask the driver for an 8K fill every frame.
 * The rAF-stall check is deliberately blind while the page is hidden: not
 * painting is CORRECT there, and a watchdog that fires on correct behaviour gets
 * switched off by the people it protects. It is blind at hot heat too, for a
 * different reason now — the bed no longer STOPS when the armor goes hot (see
 * step(); it changes gear), but hot is the busiest the machine ever gets, so a
 * fat gap in the middle of one is the likeliest honest hitch on the page.
 *
 * NO CROSS-IMPORT: nothing here reaches into dtrh/. dtrh/engine/loomSpirals.js
 * would pull DtRH settings and the player's saved-spiral storage in with it, and
 * goon must never read the player's Loom library. exec/spiralField.js and
 * exec/spiralGen.js are COPIES of the Loom's renderer and its dice, not imports.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

import { createSpiralField, loopMsFor } from './spiralField.js';
import {
  beginSpiralSession, nextSpiral, warmSharedSpirals, RASTER_PX, LITE_RASTER_PX,
} from './spiralGen.js';
import { perfLite } from './perfTier.js';
import { governorBusy } from './loadGovernor.js';

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
/* --- THE LITE TIER'S HALF OF BOTH DIALS (exec/perfTier.js — phones). The
   shader is a per-pixel per-frame cost and a phone GPU pays it twice over: a
   Retina DPR and a 120Hz ProMotion rAF. So the lite tier draws at CSS pixels
   (a screen-blended bed at <=0.70 opacity survives that with no visible loss)
   and at ~30fps — the phase keeps real time either way (see step()), so a
   throttled bed turns at exactly the tempo the full one does, in coarser
   steps. Both read LAZILY, per resize/frame, so the options toggle reaches a
   bed already spinning. ----------------------------------------------------- */
export const LITE_MAX_DPR = 1;
export const LITE_FRAME_MS = 33;   // ~30fps draw cadence on the lite tier
/* …and the deeper step the bed volunteers WHILE A BURST IS ON (2026-08-05,
   exec/loadGovernor.js): ~15fps for the squall's few seconds, because the
   burst's decodes and the shader's fill land in the same frames and the phase
   maths already makes a skipped draw cost pixels, never tempo. Self-restoring
   by the governor's own deadline; the full tier never consults it. */
export const LITE_BURST_FRAME_MS = 67;

/**
 * Is this frame owed a DRAW? Pure, so the startup edge is pinnable in node.
 *
 * The phase advances on every callback either way (see step()), so a skipped
 * frame skips pixels and never tempo. Two rules:
 *   · the full tier never skips — byte-identical to the pre-tier loop;
 *   · a lastDraw of 0 is "this weave has not drawn a LOOP frame yet" and always
 *     draws. weave() zeroes it for exactly this reason: rAF timestamps are a
 *     page-lifetime clock shared with the pane that just died, so a bed that
 *     mounts within 33ms of the last one's final frame would otherwise open by
 *     SKIPPING — a fresh spiral's first visible act being a dropped frame is
 *     the one moment a throttle must not be silently right.
 */
export function dueForDraw(now, lastDraw, lite, frameMs) {
  if (!lite) return true;
  if (!(lastDraw > 0)) return true;
  // `frameMs` is the governor's deeper step while a burst squall is on
  // (LITE_BURST_FRAME_MS); absent/0 means the plain lite cadence.
  return (now - lastDraw) >= (frameMs > 0 ? frameMs : LITE_FRAME_MS);
}

/* --- THE IN-LOOP WATCHDOG. Checked once per HEALTH_CHECK_MS, never per frame:
   gl.getError()/isContextLost() can force a sync round-trip to the driver, which
   is precisely the thing not to do sixty times a second on a machine that may
   already be stalling. RAF_STALL_MS is the gap between frame callbacks that
   stops counting as a hitch and starts counting as a hang. ------------------ */
export const HEALTH_CHECK_MS = 1000;
export const RAF_STALL_MS = 4000;
/** Context losses this pane will re-upgrade through before it stays on raster. */
export const MAX_CONTEXT_RECOVERIES = 2;

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

  // ROLL THE MATCH'S SPIRALS, ONCE, HERE. createSpiral runs exactly once per
  // duel page (boot.js builds one executor), so this is the session boundary —
  // and it is the shared pool, so exec/bubbles.js's popped-spiral flick throws
  // a spiral this match has actually been showing.
  //
  // …AND WARM THEIR STILLS FROM HERE TOO. boot.js#buildApp builds the executor
  // BEFORE the first screen, so this fires with the whole title/lobby/countdown
  // stretch of idle time still ahead of it — every bake is cached long before a
  // cue, or a popped spiral bubble (exec/bubbles.js#popFx -> pickSpiralImage),
  // can ask for one on the main thread. The lite tier bakes SMALLER (see
  // LITE_RASTER_PX): the still is the fallback bed there and the live bed it
  // falls back FROM is already only LITE_MAX_DPR deep. The tier is read once,
  // here, because a pool's bake size is fixed when it is built — a player who
  // flips the perf toggle mid-match moves the shader and the caps, not the five
  // pictures already in hand.
  try {
    beginSpiralSession({ size: perfLite() ? LITE_RASTER_PX : RASTER_PX });
    warmSharedSpirals();
  } catch (_e) { /* the pool builds lazily on first ask anyway */ }

  let paneEl = null;
  let elementIntensity = null;    // null = element not running
  let payloadIntensity = null;    // null = no payload running
  /**
   * THE ONE payload run holding the pane, or null — the swap token. See
   * renderPayload for the whole argument; the short version is that a second
   * spiral arriving mid-run used to share this file's single `payloadIntensity`
   * while keeping its own end timer, so the FIRST timer to expire took the pane
   * down and left the second run's duration as a dead backlog. A run may only put
   * the dial down while it still owns it.
   */
  let payloadRun = null;

  // THE SPIRAL CURRENTLY ON THE PANE — one roll from the session pool, driving
  // BOTH beds: `.params` is what the shader weaves, `.image()` is the baked
  // still underneath it, `.revSec` is the spin the CSS keyframe runs at.
  let variant = null;
  // The variant whose still is actually IN the pane's background-image. Null
  // while the pane is woven and has never needed one — see paintStill().
  let stillOn = null;
  // ensurePane() MINTED the pane on this call (rather than finding one already
  // up). Read one line later by start()/renderPayload(), which must not roll a
  // second variant on top of the one the new pane just rolled for itself.
  let mintedPane = false;

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
  let onWake = null;              // pageshow/focus — the resumes visibilitychange does not cover
  let lastCheck = 0;              // in-loop watchdog: last health poll (rAF timestamp ms)
  let lastDraw = 0;               // lite-tier draw throttle: last frame actually rendered
  // ONE fat frame gap is not a hang (2026-08-05). See healthCheck(): the first
  // oversized gap only makes the pane a SUSPECT and re-seeds the clocks; it takes
  // a second one, measured on a fresh pair of frames, to cost this pane its
  // shader. Cleared by any healthy check, by a resume, and by every teardown.
  let stallSuspect = false;
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
  /**
   * Is the local effect stack HOT? (A canvas inherits no custom property, so the
   * woven bed has to read the armor's attribute itself.) It is called `parked`
   * for its history: it used to stop the render loop dead, the way
   * --gg-deco-play parks every other keyframe. It no longer does — see step().
   */
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
    mintedPane = true;
    stillOn = null;                 // a brand-new node carries no picture yet
    paneEl = document.createElement('div');
    // .gg-deco marks the pane as motion the armor is allowed to know about —
    // but NOT to stop. Both fx.css rules resolve --gg-deco-play against this
    // element, and .gg-spiral re-declares it `running` (the .gg-mon-proj
    // exemption): a spiral whose only content is its rotation is a photograph
    // when parked, not a cheaper spiral. The woven path makes the same call in
    // JS, in step(), because a canvas inherits no custom properties.
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
    const dpr = Math.min(perfLite() ? LITE_MAX_DPR : MAX_DPR,
      (typeof devicePixelRatio === 'number' && devicePixelRatio > 0) ? devicePixelRatio : 1);
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
    // The pane's OWN roll, not a fresh one: the still already underneath this
    // canvas has to be the same spiral, or the swap between the two paths would
    // be a visible cut instead of a change of renderer.
    if (!variant) variant = nextSpiral();
    params = variant.params;
    const gen = ++weaveGen;

    phase = Math.random();      // never start every match on the same frame
    lastTs = 0;
    lastCheck = 0;
    lastDraw = 0;               // this weave has not drawn a LOOP frame — see dueForDraw()
    stallSuspect = false;       // a fresh weave is owed a fresh pair of witnesses
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
    //
    // AND visibilitychange IS NOT THE ONLY DOOR BACK (2026-08-05). A bfcache
    // restore arrives as `pageshow` (persisted), and a WebView2/WPF host that
    // was merely occluded, minimised or behind another window may hand the page
    // back with nothing but a window `focus` — visibilityState says "visible"
    // the whole time it was not being composited. Both are resumes, both carry
    // one huge frame gap, and neither is a hang, so they zero the same clocks
    // and clear the suspicion the corroboration check may have banked.
    if (typeof d.addEventListener === 'function') {
      onVisibility = () => { lastTs = 0; lastCheck = 0; stallSuspect = false; };
      try { d.addEventListener('visibilitychange', onVisibility); } catch (_e) { onVisibility = null; }
    }
    if (typeof addEventListener === 'function') {
      onWake = () => { lastTs = 0; lastCheck = 0; stallSuspect = false; };
      try {
        addEventListener('pageshow', onWake);
        addEventListener('focus', onWake);
      } catch (_e) { onWake = null; }
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
   *
   * THE STALL VERDICT NEEDS TWO WITNESSES (2026-08-05). It used to need one:
   * a single gap over RAF_STALL_MS while the page called itself visible set
   * rasterLocked, and rasterLocked is FOREVER for that pane — nothing but a
   * teardown or a context restore clears it. But a 4-second frame gap is not
   * only ever a driver hang. A major GC, a WPF window that got occluded or
   * dragged between monitors, a laptop coming off a power-state change, an app
   * switch that never fires visibilitychange because the WebView2 host does not
   * report one — every one of those hands the loop one enormous gap and then
   * behaves perfectly, and the player paid for that single hiccup with the
   * shader for the rest of the effect. So the first oversized gap makes the
   * pane a SUSPECT and re-seeds the clocks; only a SECOND oversized gap,
   * measured on a fresh pair of frames a health check later, is a hang. A real
   * stall keeps stalling and loses its shader a second later than it used to; a
   * hitch is forgiven, which is the whole point.
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
    //    while hidden (rAF is suspended by design), and blind at hot heat: the
    //    bed still PAINTS there now (see step()), but hot is also the busiest
    //    the machine ever gets, so a fat gap in the middle of a three-effect
    //    stack is the likeliest legitimate hitch on the page and the least safe
    //    thing to read as a dead driver. A stall that outlives the heat is
    //    caught by the very next check. lastTs 0 means "no pair to measure yet".
    if (lastTs && visible() && !parked() && (now - lastTs) > RAF_STALL_MS) {
      const gap = Math.round(now - lastTs);
      if (!stallSuspect) {
        // FIRST WITNESS. Re-seed and look again rather than locking: lastCheck 0
        // makes step() re-seed the poll clock on the next callback, and step's
        // own `lastTs = now` (three lines after this returns) gives the next
        // check a pair of frames that were both measured AFTER the hitch.
        stallSuspect = true;
        lastCheck = 0;
        warn(`frame callbacks gapped ${gap}ms — watching for a second one before dropping the shader`);
        return '';
      }
      warn(`frame callbacks stalled ${gap}ms twice over — falling back to the raster pool`);
      return 'stall';
    }
    // A clean check clears the suspicion: two oversized gaps have to be
    // CONSECUTIVE to be a hang, or one hitch a minute would eventually convict.
    stallSuspect = false;
    return '';
  }

  function step(ts, gen) {
    if (gen !== weaveGen) return;      // a torn-down weave's last frame
    rafId = 0;
    if (!field || !paneEl) return;
    const now = typeof ts === 'number' ? ts : 0;
    // SEED, DON'T POLL, ON THE FIRST FRAME OF A WEAVE. lastCheck starts at 0 and
    // an rAF timestamp is a page-lifetime clock, so the very first callback used
    // to satisfy `now - 0 >= 1000` and run the driver poll — a synchronous
    // round-trip to the GPU, in the busiest millisecond of the pane's life, when
    // the only thing it can possibly report is a context created moments ago.
    // (It also re-seeds after a visibilitychange, which zeroes lastCheck for the
    // same reason the stall check does.)
    if (!lastCheck) lastCheck = now;
    else if (now - lastCheck >= HEALTH_CHECK_MS) {
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
    // HOT HEAT THROTTLES THIS BED; IT NO LONGER STOPS IT (2026-08-05, owner
    // play-test: "spirals sometimes render as a frozen static image").
    //
    // This block used to be `if (!parked()) { …advance, draw… }` — hold the
    // frame, the same thing --gg-deco-play does to every keyframe in fx.css.
    // Two things were wrong with copying the CSS here. First, it copied a rule
    // that was itself wrong for this pane: a spiral's whole content IS its
    // rotation, so a parked spiral is not a cheaper spiral, it is a photograph
    // (fx.css's .gg-spiral banner has the full argument, and it now carries the
    // .gg-mon-proj exemption to match). Second, "hot" is a NORMAL mid-match
    // state — exec/layers.js calls it at three concurrent effects and
    // core/draft.js keeps Bubbles always on with two rolled elements overlapping
    // — and exec/executor.js syncHeat()s a payload into the count BEFORE
    // renderPayload runs it, so a spiral landing as the third effect was born
    // parked and held one frame for its entire duration.
    //
    // The frame budget the armor is protecting is real, though, so heat is a
    // GEAR, not a switch: the phase advances every callback exactly as before,
    // and hot borrows the lite tier's burst cadence (~15fps) through the very
    // same throttle the phone diet already uses. Reduced fps, never zero.
    const hot = parked();
    const loop = Math.max(1, loopMsFor(params));
    if (lastTs) phase = (phase + (now - lastTs) / loop) % 1;
    // THE LITE THROTTLE. The PHASE above advanced by real elapsed time on
    // every callback, so skipping a draw skips pixels, never tempo — a
    // 30fps bed and a 120fps bed show the same rotation at the same second.
    // While the load governor says a payload squall is on, the bed steps
    // down further still (LITE_BURST_FRAME_MS) and climbs back by itself.
    // A HOT STACK enters the same gate from the other side: it turns the
    // throttle ON for the full tier too (`hot ||`) and asks for the deeper
    // step, so the one dial serves both callers and there is no second cadence
    // to keep in sync. Full tier, idle stack: no gate at all, byte-identical to
    // the pre-tier loop.
    const busy = hot || governorBusy();
    if (dueForDraw(now, lastDraw, hot || perfLite(), busy ? LITE_BURST_FRAME_MS : 0)) {
      lastDraw = now;
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
    const w = {
      cv: canvasEl, f: field, raf: rafId, lost: onLost, resize: onResize,
      vis: onVisibility, wake: onWake,
    };
    weaveGen++;                        // every queued frame for this weave is now stale
    canvasEl = null; field = null; params = null;
    rafId = 0; lastTs = 0; lastCheck = 0; lastDraw = 0; stallSuspect = false;
    onLost = null; onResize = null; onVisibility = null; onWake = null;
    return () => {
      if (w.raf && typeof cancelAnimationFrame === 'function') {
        try { cancelAnimationFrame(w.raf); } catch (_e) { /* ignore */ }
      }
      if (w.resize && typeof removeEventListener === 'function') {
        try { removeEventListener('resize', w.resize); } catch (_e) { /* ignore */ }
      }
      if (w.wake && typeof removeEventListener === 'function') {
        try {
          removeEventListener('pageshow', w.wake);
          removeEventListener('focus', w.wake);
        } catch (_e) { /* ignore */ }
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
   * behind would hand the player a magnified, unsoftened, UNSPUN still — the
   * baked PNG's wedge edges at 2.4x, held perfectly still, which is the one way
   * to make a generated spiral look worse than the GIFs it replaced. Setting
   * each property to '' (not to its CSS value) is what lets the stylesheet
   * answer again — and note the spin's rate and direction ride CUSTOM
   * PROPERTIES (repick), precisely so this shorthand clear cannot eat them.
   */
  function unweave() {
    // PAINT THE STILL FIRST, WHILE THE DEAD CANVAS IS STILL COVERING THE PANE.
    // This is the moment the fallback stops being insurance and becomes the
    // bed, and it is the ONE door every fallback comes through (a lost context,
    // a polled stall, the kill switch, a render that threw). Doing it here
    // instead of on every repick is what keeps the bake off the cue — and doing
    // it BEFORE detachWoven() drops the canvas is what keeps the swap from
    // showing a frame of empty pane. It is normally free: the pool warmed this
    // picture during idle time long before anything went wrong.
    paintStill(variant);
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
   * Put `v`'s baked still into the pane's background-image — the raster bed's
   * whole picture, and the woven bed's parachute.
   *
   * IDEMPOTENT ON THE VARIANT, because it is called from two directions (the
   * deferred bake on a raster pane, and unweave() on the way down from a woven
   * one) and repainting a picture the pane already carries would re-decode a
   * multi-megabyte data URL for no change at all.
   *
   * An UNBAKEABLE variant (no canvas, no 2D context, a toDataURL the host
   * refuses) answers '' and the pane simply keeps whatever it had. Writing
   * `url('')` instead would blank a bed that was working a moment ago.
   */
  function paintStill(v) {
    if (!paneEl || !paneEl.style || !v || stillOn === v) return;
    try {
      const img = v.image();       // warmed at build; cached after the first ask
      if (!img) return;
      paneEl.style.setProperty('background-image', `url("${img}")`);
      stillOn = v;
    } catch (_e) { /* the bed keeps whatever it had */ }
  }

  /**
   * Advance to the next spiral in the session's rotation, and put it on the
   * shader — and, when the pane is (or lands) on the raster bed, put the baked
   * still of the SAME roll underneath it. One roll drives both, which is what
   * keeps the fallback from ever being a different picture if the context dies
   * mid-match.
   *
   * THE WOVEN PANE DOES NOT PAINT ONE AT ALL (2026-08-05, the phone stutter).
   * The bake used to be DEFERRED ONE TICK on the theory that a bed fading in
   * over 450-900ms would swallow it. It does not: one tick lands the work in
   * the pane's FIRST FRAMES, the rAF loop queues behind it, and a ~1600-fill
   * bake plus a PNG encode plus a multi-megabyte data-URL decode is a second
   * of a spiral that is on screen and not turning. And on the woven path the
   * whole thing is invisible anyway — the canvas over it is `alpha: false`.
   * So: the tick's callback bails if the weave took, unweave() paints on the
   * way back down to raster, and a pane that never wove (no WebGL, shaders off,
   * a defence already fired) paints exactly as before, one tick later. The
   * picture itself is normally already warm — see the createSpiral() banner.
   */
  function repick() {
    if (!paneEl || !paneEl.style) return;
    variant = nextSpiral();
    try {
      // Both beds turn at the roll's own solved revolution time. These are
      // CUSTOM PROPERTIES on purpose: unweave() clears the `animation`
      // shorthand to hand the pane back to fx.css, and a shorthand clear would
      // take plain animation-duration/-direction longhands with it.
      paneEl.style.setProperty('--gg-spiral-spin', `${variant.revSec.toFixed(2)}s`);
      paneEl.style.setProperty('--gg-spiral-dir', variant.params.layer.direction === -1 ? 'reverse' : 'normal');
    } catch (_e) { /* a host without inline style support just keeps the CSS default */ }
    const el = paneEl;
    const v = variant;
    soon(() => {
      // A pane that has been torn down (or replaced) in the meantime must not
      // be painted into — its node lingers 900ms on the way out by design.
      if (paneEl !== el || !el.style) return;
      // The weave (ensurePane's, one line after its repick) took: the still
      // would be baked, decoded and uploaded for a layer nobody can see. It is
      // owed, not skipped — unweave() paints it the instant the pane needs it.
      if (field) return;
      paintStill(v);
    }, 0);
    if (field) {
      params = variant.params;
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
    // Cleared BEFORE the call, set by ensurePane only when it actually mints:
    // start()/renderPayload() read it one line after apply() returns, and a
    // stale true from the pane before this one would cost that cue its repick.
    mintedPane = false;
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
      // A new bed gets a new spiral; a re-tune keeps its own. But a pane
      // ensurePane() JUST MINTED has ALREADY rolled one (and woven it) — see
      // the note in renderPayload; rolling again here would be the second roll
      // of the same cue.
      if (fresh) { if (!mintedPane) repick(); sfx('spiral-in'); }
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

    /**
     * Spiral payload: ride the pane up for duration_ms, then hand it back.
     *
     * INSTANT SWAP, NEVER A BACKLOG (2026-08-05, owner: "we just instantly swap whats
     * active for whatever's next immediately, should be easier on the performances").
     * The full argument lives in exec/brainDrain.js renderPayload — this pane has the
     * identical one-scalar/two-clocks shape and therefore had the identical bug — but
     * the SPIRAL has a second, sharper reason to swap rather than stack:
     *
     * ONE PANE, ONE WEAVE, ONE BAKE. A teardown-then-restart between two overlapping
     * spirals would detach the woven canvas, drop the shader context, mount a fresh
     * pane and queue a fresh raster bake, all inside the incoming cue's own fade —
     * which is the exact double-bake shape PR #137 already had to chase off an iPhone
     * once. Keeping the pane and letting `repick()` roll a new spiral onto it is one
     * bake and no context churn, and it is also the nicer picture: the spiral changes
     * without the screen blinking.
     *
     * `mintedPane` still does its job unchanged. On a swap the pane already exists, so
     * apply() mints nothing, so the repick below is the one and only roll of this cue.
     */
    renderPayload(payload, done) {
      const p = payload || {};
      const runMs = Math.max(1000, (p.duration_ms | 0) || 12000);

      const run = { settle: null };

      let finished = false;
      let endTimer = 0;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        try { clearTimeout(endTimer); } catch (_e) { /* ignore */ }
        // Only the owner may put the dial down; a swapped-out run settles into
        // thin air rather than tearing down its successor's pane.
        if (payloadRun === run) {
          payloadRun = null;
          payloadIntensity = null;
          apply();
        }
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };
      run.settle = settle;

      // Ownership moves BEFORE the outgoing run is settled — reversing these two
      // lines is what would tear the pane down and blink.
      const prev = payloadRun;
      payloadRun = run;
      if (prev) { try { prev.settle(false); } catch (e) { warn(`swap settle threw: ${e && e.message}`); } }

      payloadIntensity = Math.max(0.5, clamp01(p.intensity !== undefined ? p.intensity : 0.75));
      apply();
      // ONE ROLL PER CUE. apply() -> ensurePane() already repicks for a pane it
      // MINTED, and that roll is the one weave() wove a first frame of; rolling
      // again here threw that frame away, swapped the shader's params on the
      // spot, and — before the still moved off this path — queued a SECOND
      // 1280px raster bake behind the first, both landing on the main thread
      // inside the cue's own fade. That double bake is the freeze the owner
      // play-tested on an iPhone. A bed that was ALREADY UP still repicks: a
      // payload landing on a running spiral is exactly when the picture should
      // change.
      if (!mintedPane) repick();

      endTimer = soon(() => settle(true), runMs);
      return () => settle(false);
    },
  };
}

export default createSpiral;
