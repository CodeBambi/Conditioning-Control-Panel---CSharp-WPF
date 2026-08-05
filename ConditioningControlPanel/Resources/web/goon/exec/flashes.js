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
 * GRAB, FLING, WHEEL (the owner's ask). A flash is also a physical object: press
 * and move past DRAG_SLOP_PX and you are HOLDING it — it follows the pointer,
 * lifts (scale + shadow), its fade is suspended and its lifetime clock PAUSES.
 * Let go and the pointer's last ~110ms of travel becomes momentum: it glides
 * under friction, bounces off the viewport inset, and comes to rest — or, thrown
 * hard enough, sails off-screen and dies there. Wheel while holding resizes it
 * through --gg-flash-size (never a stacked transform scale, which would fight the
 * tilt and the drag offset). A press that never passes the slop is still a CLICK,
 * so the hydra lives on pointerUP, not pointerdown — that is the one behaviour
 * change here, and it is what lets drag and click share the same press.
 *
 * ONE POINTER AT A TIME. A second finger landing while a drag is live is ignored
 * outright (no pop, no second grab) rather than half-driven. Every exit path —
 * pointerup, pointercancel, lostpointercapture, a node torn out from under us,
 * stop(), a 30s watchdog — runs the same endDrag(), so there is no such thing as
 * a stuck held flash.
 *
 * A GRABBED FLASH LEAVES THE KEYFRAME WORLD. Once held it wears
 * .gg-flash--grabbed (animation: none) and JS owns its transform, its opacity and
 * its retirement: it expires on a timer into .gg-flash--expire, an opacity-only
 * fade that cannot yank the node back to its CSS anchor (animations outrank
 * inline styles in the cascade, so any exit keyframe that touched transform would
 * teleport a dragged flash home before dying).
 *
 * POPPING (AND DRAGGING) IS COSMETIC. Exactly like the bubble field: it scores
 * nothing, ends nothing, and changes NO receipt semantics. A FlashBurst payload
 * still runs its full duration_ms and then done(true) if it was never
 * interrupted, whether you clicked every flash, flung them all, or neither. A
 * held flash still counts against MAX_LIVE — holding does not buy headroom.
 *
 * POINTER RULES. #gg-fx and its sub-layers are pointer-events:none. `.gg-flash`
 * on its own stays click-through (exec/bubbles.js mints plain .gg-flash nodes for
 * its pop effect and those must NOT eat clicks); only the `.gg-flash--hydra`
 * modifier this module adds opts back in. fx.css drops that opt-in whenever
 * #gg-stage is occupied — the same `body:has(#gg-stage > *)` rule the bubbles
 * use — so a flash can never swallow a click meant for a lock card or a video
 * attention check. That rule guards the DRAG path too: no pointerdown means no
 * drag can ever start over the stage, and a drag already in flight when the stage
 * fills loses its capture and drops gently.
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
 * PINCH (2026-08-04). A phone has no wheel, so the resize above was a desktop-only
 * affordance and an opponent's GIF arrived on a phone at whatever size it was
 * born at. TWO fingers on one flash resize it — through the same
 * --gg-flash-size var, for the same reason the wheel uses it (transform is
 * already spoken for by the tilt, the lift and the drag offset). The arithmetic
 * is exec/pinch.js's, pure and self-tested; the rest is pointer bookkeeping.
 * ONE POINTER AT A TIME still holds everywhere else: a second finger on a
 * DIFFERENT flash is ignored exactly as it always was, and a mouse never pinches.
 *
 * Sustained run and payload runs are independent: a burst can land on top of an
 * already-running flash bed and neither one owns the other's nodes.
 * ==========================================================================*/

import {
  PINCH_MIN_FACTOR, PINCH_MAX_FACTOR, PINCH_SLOP_PX, PINCH_KEEP_PX,
  pinchEligible, pinchDistance, pinchMidpoint, pinchStarted, pinchStep,
  viewportCap, pxToVmin, safeInsets,
} from './pinch.js';
import { perfLite } from './perfTier.js';
import { isAnimatedMedia } from './media.js';
import { governorHold } from './loadGovernor.js';

export const MAX_LIVE = 20;          // concurrent <img> nodes, hydra children included
/* The LITE tier's cap (exec/perfTier.js — phones). Half the field: each flash is
 * a ~44vmin GPU layer and twenty of them is most of what an iPhone was choking
 * on. Read LAZILY at every spawn (liveCap in the factory), never captured at
 * build, so the options toggle reaches a burst already in flight — a tightened
 * cap simply stops admitting nodes and the live ones age out on their own. */
export const MAX_LIVE_LITE = 10;
/* THE ANIMATION BUDGET (lite only — 2026-08-05, second mobile pass). The node
 * cap above treats a static JPEG and an animated GIF as the same spend, and
 * they are not even close: an <img> playing a GIF is a CPU decode loop that
 * runs for the flash's whole life, and a burst of peer media is USUALLY GIFs —
 * ten at once is exactly the "everything overloads" moment the owner
 * play-tested. So on lite at most this many flashes may be PLAYING an animated
 * file at once (isAnimatedMedia decides what counts, generously). Overflow
 * flashes still land — frozen on their first frame via a one-frame canvas bake
 * (`animation-play-state` cannot pause a GIF; only not-being-a-GIF can) — so a
 * burst keeps its density while paying for three decode loops, not ten. A host
 * that cannot bake (no Image, no 2d context) SKIPS the overflow flash instead:
 * a skipped beat is the cap's own precedent, and rendering the animation
 * anyway would be the budget lying. */
export const ANIM_LIVE_LITE = 3;
/* The frozen frame's longest side, in backing pixels. A flash tops out at
 * ~44vmin (<=430px CSS on any phone this tier targets), so 512 is already
 * oversampled — decoding a 4K GIF's first frame into a 4K canvas would spend
 * the very memory the freeze is here to save. */
export const FROZEN_MAX_PX = 512;
export const HYDRA_CHILDREN = 2;     // what one click hatches (clamped to the cap)
export const BASE_HOLD_MS = 5000;    // on-screen time a flash is centred on
const HOLD_SPREAD_MS = 400;          // ± this across the intensity range (4600..5400)
const POP_OUT_MS = 220;              // the dismissed flash's quick pop-out (fx.css ggFlashPop)
const HATCH_MS = [70, 210];          // children hatch staggered, never on one frame

/* P2P media transfer: how many of a burst's flashes may be the SENDER's own files.
 * MIRRORS net/mediaChannel.js XFER_TAGS_MAX and is kept local on purpose — exec/
 * never imports net/, so the render tier stays independent of the wire tier. */
export const XFER_TAGS_MAX = 3;

/* BURST DENSITY, PER TIER (renderPayload). The full tier's burst halves the
 * beat gap AND adds a flash per beat — density by tempo and by node count at
 * once. On LITE only the tempo survives: the gap tightens less (0.72 vs 0.5)
 * and the per-beat count stays the bed's own, because on a phone every extra
 * node is another decode landing inside the squall's own frames. The burst
 * still reads as a burst — beats arrive ~40% faster than the bed's — but the
 * standing population climbs at roughly half the full-tier rate, which is the
 * churn the play-test was drowning in. */
export const BURST_GAP_SCALE = 0.5;
export const BURST_GAP_SCALE_LITE = 0.72;

/** The `xfer:` tags off a payload, cleaned and capped. [] for every other payload. */
function xferTags(payload) {
  const tags = (payload && Array.isArray(payload.tags)) ? payload.tags : null;
  if (!tags) return [];
  const out = [];
  for (const t of tags) {
    if (typeof t !== 'string' || !t.startsWith('xfer:')) continue;
    out.push(t);
    if (out.length >= XFER_TAGS_MAX) break;
  }
  return out;
}

/* The scatter box, in vw/vh — payloadFx's numbers. Children are placed radially
   off their parent inside the same box. */
const X_MIN = 14, X_SPAN = 72;
const Y_MIN = 16, Y_SPAN = 64;
/* Mirrors --gg-flash-size's default in fx.css (38vmin + 15%). Only children (and
   the wheel) ever write the property; an untouched gen-0 flash lets the
   stylesheet own its size. */
const BASE_SIZE_VMIN = 43.7;

/* --- GRAB / FLING / WHEEL dials. Exported because the self-test asserts against
   these numbers rather than re-typing them. -------------------------------- */
export const DRAG_SLOP_PX = 6;          // <= this much travel and the press was a CLICK
export const VELOCITY_WINDOW_MS = 110;  // tail of pointer travel that becomes momentum
export const FLING_FRICTION = 0.94;     // per 60fps frame
export const FLING_BOUNCE = 0.6;        // velocity kept when it kisses the viewport inset
export const WHEEL_STEP = 1.08;         // size multiplier per wheel notch
export const SIZE_MIN_FACTOR = 0.5;     // wheel clamp, relative to the flash's OWN base size
export const SIZE_MAX_FACTOR = 2.5;
export const HELD_RESUME_MIN_MS = 2000; // a released flash always gets this long to be watched

const HOLD_SCALE = 1.06;        // the lift while held (transform, not --gg-flash-size)
const FRAME_MS = 16.7;          // the frame the friction/velocity numbers are quoted in
const FLING_REST = 0.35;        // px/frame: below this it has landed
const FLING_MIN = 0.6;          // px/frame: below this a release is a drop, not a throw
const FLING_MAX = 90;           // px/frame: nothing leaves the hand faster than this
const FLING_ESCAPE = 48;        // px/frame: past this it stops bouncing and leaves
const BOUNCE_INSET = 0.07;      // the wall the centre bounces off, as a fraction of w/h
const EXPIRE_MS = 420;          // .gg-flash--expire, the JS-owned fade a touched flash dies on
const HOLD_WATCHDOG_MS = 30000; // nothing may be "held" longer than this, ever

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
const nowMs = () => {
  try { if (typeof performance === 'object' && performance && typeof performance.now === 'function') return performance.now(); }
  catch (_e) { /* fall through */ }
  return Date.now();
};
/* Viewport in px. Falls back to a sane desktop box so the physics never divides
   by zero on a host without a window (the self-test's stub has both). */
const vpW = () => ((typeof window !== 'undefined' && window && window.innerWidth > 0) ? window.innerWidth : 1280);
const vpH = () => ((typeof window !== 'undefined' && window && window.innerHeight > 0) ? window.innerHeight : 720);
const num = (v, dflt) => (typeof v === 'number' && v === v ? v : dflt);
/* Per-generation taper (children read it as a RATIO off their parent's actual
   size, so a wheel-grown flash grows its whole lineage). */
const genFactor = (g) => Math.max(0.78, 1 - 0.09 * Math.max(0, g | 0));

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

  const live = new Set();       // every flash on screen (see showOne for the record shape)
  const flying = new Set();     // the subset currently gliding on a fling
  let sustained = null;         // {alive, intensity, timer}
  let drag = null;              // the ONE live press, if any (see onPointerDown)
  let pinch = null;             // …and the second finger on it, if any (see beginPinch)
  let stepId = 0;               // the shared fling stepper's handle
  let stepRaf = false;          // ...and whether it is an rAF handle or a timeout
  let stepLast = 0;

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('flash') : null);
  /** THE cap, resolved fresh per ask — the device tier owns the difference. */
  const liveCap = () => (perfLite() ? MAX_LIVE_LITE : MAX_LIVE);
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:flashes] ${m}`); };
  const sfx = (id) => { if (audio && typeof audio.sfx === 'function') { try { audio.sfx(id); } catch (_e) { /* ignore */ } } };

  /** Drop bookkeeping for nodes the layer already tore out from under us
   *  (layers.stopAll() empties the DOM without going through kill()). */
  function prune() {
    for (const rec of Array.from(live)) {
      if (!rec.node || !rec.node.isConnected) {
        live.delete(rec);
        flying.delete(rec);
        if (drag && drag.rec === rec) forgetDrag();
        try { if (rec.handle && rec.handle.release) rec.handle.release(); } catch (_e) { /* ignore */ }
      }
    }
  }

  /* =========================================================================
   * THE PHYSICS LAYER — one transform per touched flash, one stepper for all.
   *
   * A flash's position is (anchor in vw/vh, written to left/top once at spawn)
   * PLUS (dx, dy) in px, which only ever lives in the inline transform. That
   * split is deliberate: the anchor keeps the scatter maths in the units
   * payloadFx uses, while the drag/fling runs on the compositor property, and
   * neither one has to know about the other.
   * ======================================================================= */

  /** The one place a touched flash's transform is written. Owns the tilt (so it
   *  cannot be fought over) and the hold lift; SIZE is never in here — that is
   *  --gg-flash-size's job, per fx.css. */
  function paint(rec) {
    if (!rec || !rec.node) return;
    const s = rec.held ? HOLD_SCALE : 1;
    try {
      // Two translates rather than one calc(): the first is the -50% centring
      // every flash rule uses, the second is the drag offset in screen px. Both
      // land before rotate/scale, so the tilt and the lift still pivot on the
      // node's own centre exactly like the keyframes did.
      rec.node.style.setProperty('transform',
        `translate(-50%, -50%) translate(${rec.dx.toFixed(1)}px, ${rec.dy.toFixed(1)}px)`
        + ` rotate(${rec.rot}deg) scale(${s})`);
    } catch (_e) { /* ignore */ }
  }

  /** Current visual centre, back in the vw/vh the scatter box speaks. Hydra
   *  children hatch off THIS, so a flash you dragged across the screen splits
   *  where you dropped it and not where it spawned. */
  const curX = (rec) => rec.x + (rec.dx / vpW()) * 100;
  const curY = (rec) => rec.y + (rec.dy / vpH()) * 100;

  function unschedule() {
    if (!stepId) return;
    try {
      if (stepRaf && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(stepId);
      else clearTimeout(stepId);
    } catch (_e) { /* ignore */ }
    stepId = 0;
  }

  /** rAF where there is one; an unref'd 16ms timer where there is not (node, and
   *  any host that throttles us out of rAF entirely). Same stepper either way. */
  function schedule() {
    if (stepId || !flying.size) return;
    if (typeof requestAnimationFrame === 'function') {
      stepRaf = true;
      stepId = requestAnimationFrame(() => { stepId = 0; step(); });
    } else {
      stepRaf = false;
      stepId = soon(() => { stepId = 0; step(); }, 16);
    }
  }

  /** ONE stepper for every flying flash. Frame-rate independent: the friction
   *  and velocity numbers are quoted per 60fps frame and scaled by real dt. */
  function step() {
    const t = nowMs();
    const frames = Math.max(0.5, Math.min(3, (t - stepLast) / FRAME_MS));
    stepLast = t;
    const W = vpW(), H = vpH();
    const decay = Math.pow(FLING_FRICTION, frames);
    const minX = BOUNCE_INSET * W, maxX = W - minX;
    const minY = BOUNCE_INSET * H, maxY = H - minY;

    for (const rec of Array.from(flying)) {
      const f = rec.fly;
      if (!f || rec.held || rec.popped || !rec.node || !rec.node.isConnected) { flying.delete(rec); continue; }

      rec.dx += f.vx * frames;
      rec.dy += f.vy * frames;
      f.vx *= decay;
      f.vy *= decay;

      const cx = rec.x / 100 * W + rec.dx;
      const cy = rec.y / 100 * H + rec.dy;

      if (f.escape) {
        // Thrown hard: it does not bounce, it leaves — and dies out there rather
        // than sitting invisible on the wrong side of the viewport holding a slot.
        if (cx < -0.25 * W || cx > 1.25 * W || cy < -0.25 * H || cy > 1.25 * H) {
          flying.delete(rec);
          rec.kill();
          continue;
        }
      } else {
        if (cx < minX) { rec.dx += (minX - cx); f.vx = Math.abs(f.vx) * FLING_BOUNCE; }
        else if (cx > maxX) { rec.dx -= (cx - maxX); f.vx = -Math.abs(f.vx) * FLING_BOUNCE; }
        if (cy < minY) { rec.dy += (minY - cy); f.vy = Math.abs(f.vy) * FLING_BOUNCE; }
        else if (cy > maxY) { rec.dy -= (cy - maxY); f.vy = -Math.abs(f.vy) * FLING_BOUNCE; }
      }

      paint(rec);
      if (Math.abs(f.vx) + Math.abs(f.vy) < FLING_REST) { rec.fly = null; flying.delete(rec); }
    }

    if (flying.size) schedule(); else unschedule();
  }

  /** Everything a touched flash's lifetime timer does. A grabbed flash has no
   *  CSS animation left to end on, so this is its ONLY way out (short of a pop). */
  function expire(rec) {
    if (!rec || rec.popped || !rec.node) return;
    try { rec.node.classList.add('gg-flash--expire'); } catch (_e) { /* ignore */ }
    soon(rec.kill, EXPIRE_MS + 140);        // animationend normally beats this
  }

  /* =========================================================================
   * THE PRESS. pointerdown opens a MAYBE: past the slop it is a grab, inside it
   * it is the hydra click, and either way it ends in exactly one endDrag().
   * ======================================================================= */

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
      // THE SECOND FINGER'S HALF, bound with the first one's and torn down with
      // it — two bookkeeping lifetimes that must agree is how a listener leaks.
      // Both no-op unless a pinch is live, and the handlers above already ignore
      // a foreign pointerId, so the two sets never fight over one event.
      n[io]('pointermove', onPinchMove);
      n[io]('pointerup', onPinchEnd);
      n[io]('pointercancel', onPinchEnd);
      n[io]('lostpointercapture', onPinchEnd);
    } catch (_e) { /* ignore */ }
    // Wheel is NOT a pointer event, so capture does not redirect it: bind it as
    // wide as the host allows, and fall back to the node (which is under the
    // cursor by construction while it is being dragged).
    if (on) {
      d.wheelHost = (typeof window !== 'undefined' && window && typeof window.addEventListener === 'function') ? window : n;
    }
    const w = d.wheelHost;
    if (!w) return;
    try { w[io]('wheel', onWheel, { passive: false }); }
    catch (_e) { try { w[io]('wheel', onWheel); } catch (_e2) { /* ignore */ } }
  }

  /** Tear the bookkeeping down without touching the flash — used when the node
   *  itself has already gone (layers.stopAll, kill). */
  function forgetDrag() {
    if (!drag) return;
    forgetPinch();                  // a pinch cannot outlive the press it grew from
    listen(drag, false);
    try { clearTimeout(drag.watchdog); } catch (_e) { /* ignore */ }
    drag = null;
  }

  /** Drop the second finger. Releases its capture and leaves the flash's SIZE
   *  alone — a pinch that ends keeps what it zoomed to, exactly like the wheel. */
  function forgetPinch() {
    const p = pinch;
    if (!p) return;
    pinch = null;
    const n = p.rec && p.rec.node;
    try {
      if (n && p.idB != null && typeof n.releasePointerCapture === 'function'
          && (typeof n.hasPointerCapture !== 'function' || n.hasPointerCapture(p.idB))) {
        n.releasePointerCapture(p.idB);
      }
    } catch (_e) { /* ignore */ }
    if (drag) drag.pinching = false;
  }

  function onPointerDown(rec, e) {
    // Own the gesture: no native image-drag ghost, no text selection, no scroll.
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    if (e && e.button != null && e.button !== 0) return;   // right-click belongs to the page
    if (rec.popped || !rec.node || !rec.node.isConnected) return;

    if (drag) {
      // A second finger on THIS flash is a PINCH (touch only — see beginPinch);
      // on any other flash it is IGNORED (no pop, no second grab) exactly as it
      // always was. A drag whose node was torn out from under us must never
      // block the field forever.
      if (drag.rec && drag.rec.node && drag.rec.node.isConnected) {
        if (!pinch && drag.rec === rec) beginPinch(rec, e);
        return;
      }
      forgetDrag();
    }

    const x = ptX(e), y = ptY(e);
    drag = {
      rec,
      pointerId: (e && e.pointerId != null) ? e.pointerId : null,
      pointerType: (e && e.pointerType) || '',
      x0: x, y0: y, lastX: x, lastY: y, baseDx: rec.dx, baseDy: rec.dy,
      grabbed: false, wheeled: false, watchdog: 0, wheelHost: null,
      pinching: false, pinched: false,
      samples: [{ t: nowMs(), x, y }],
    };
    listen(drag, true);
    drag.watchdog = soon(() => { if (drag) endDrag('watchdog'); }, HOLD_WATCHDOG_MS);
    try {
      if (typeof rec.node.setPointerCapture === 'function' && e && e.pointerId != null) {
        rec.node.setPointerCapture(e.pointerId);
      }
    } catch (_e) { /* capture is a nicety; the node listeners still work without it */ }
  }

  /** Past the slop: freeze the fade, pause the clock, and hand transform to JS. */
  function beginGrab(d) {
    const rec = d.rec;
    d.grabbed = true;
    rec.held = true;
    try { clearTimeout(rec.safety); } catch (_e) { /* ignore */ }
    try { clearTimeout(rec.expTimer); } catch (_e) { /* ignore */ }
    rec.expTimer = 0;
    rec.remainMs = Math.max(0, rec.bornAt + rec.holdMs - nowMs());   // the clock stops here
    rec.fly = null;
    flying.delete(rec);
    try { rec.node.classList.add('gg-flash--grabbed', 'is-held'); } catch (_e) { /* ignore */ }
    lift(d);
    paint(rec);
  }

  /** Float the held flash over its siblings by DOM ORDER — fx.css rule 1 forbids
   *  raising z-index in this tier, and last-painted wins inside one layer anyway.
   *
   *  THE CATCH: re-inserting a node counts as a removal, and a removal implicitly
   *  RELEASES pointer capture. Left alone, every grab would hand itself a
   *  lostpointercapture on the next move and drop the flash instantly. So we
   *  re-take the capture in the same breath and swallow exactly ONE stale
   *  release notice (see onPointerCancel) — and skip the whole dance when the
   *  flash is already the top sibling, which it usually is. */
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
    // A stale release notice is dispatched BEFORE the next pointer event, so if
    // we are getting a move instead, lift()'s re-capture went through cleanly and
    // the swallow-one licence has expired.
    d.staleCapture = false;
    const x = ptX(e), y = ptY(e);
    const t = nowMs();
    d.lastX = x; d.lastY = y;
    d.samples.push({ t, x, y });
    while (d.samples.length > 2 && t - d.samples[0].t > VELOCITY_WINDOW_MS) d.samples.shift();

    if (d.pinching && pinch) {
      // This finger is half of a pinch now: it drives the gesture, not the drag.
      // (The flash still travels — with the MIDPOINT, inside applyPinch.)
      if (e && typeof e.preventDefault === 'function') e.preventDefault();
      pinch.ax = x; pinch.ay = y;
      applyPinch();
      return;
    }
    if (!d.grabbed) {
      if (Math.hypot(x - d.x0, y - d.y0) <= DRAG_SLOP_PX) return;   // still a click
      beginGrab(d);
    }
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    d.rec.dx = d.baseDx + (x - d.x0);
    d.rec.dy = d.baseDy + (y - d.y0);
    paint(d.rec);
  }

  /* ---------------------------------------------------------------- the pinch
   * Two fingers on ONE flash resize it. Touch only, so the desktop gesture set
   * (click, drag, fling, wheel) is bit-for-bit what it was.
   * ------------------------------------------------------------------------ */

  /** The second finger landed. A refusal simply leaves the press alone. */
  function beginPinch(rec, e) {
    const d = drag;
    if (!d || d.rec !== rec || rec.popped || !rec.node) return;
    const idB = (e && e.pointerId != null) ? e.pointerId : null;
    if (!pinchEligible(d.pointerType, e && e.pointerType, d.pointerId, idB)) return;
    // A pinch GRABS first: .gg-flash--grabbed stops the keyframe fade, the
    // lifetime clock pauses, any fling is cancelled and JS owns the transform —
    // only then is (anchor + dx/dy) the flash's actual place on the screen.
    if (!d.grabbed) beginGrab(d);
    const bx = ptX(e), by = ptY(e);
    const mid = pinchMidpoint(d.lastX, d.lastY, bx, by);
    pinch = {
      rec,
      idB,
      ax: d.lastX, ay: d.lastY, bx, by,
      startDist: pinchDistance(d.lastX, d.lastY, bx, by),
      startSize: rec.sizeVmin,
      midX: mid.x, midY: mid.y,
      insets: safeInsets(vpW(), vpH()),
      live: false,
    };
    d.pinching = true;
    d.pinched = true;               // the release neither pops nor throws it
    d.wheeled = true;               // …for the same reason a wheel-resize does not pop
    try {
      if (idB != null && typeof rec.node.setPointerCapture === 'function') rec.node.setPointerCapture(idB);
    } catch (_e) { /* capture is a nicety; the node listeners still work without it */ }
  }

  function onPinchMove(e) {
    const p = pinch;
    if (!p || !e || e.pointerId == null || e.pointerId !== p.idB) return;
    if (typeof e.preventDefault === 'function') e.preventDefault();
    p.bx = ptX(e); p.by = ptY(e);
    applyPinch();
  }

  /** Either finger up/cancelled ends the WHOLE gesture. This is the second
   *  finger's half; the first one's is onPointerUp/onPointerCancel -> endDrag. */
  function onPinchEnd(e) {
    const p = pinch;
    if (!p || !e || e.pointerId == null || e.pointerId !== p.idB) return;
    forgetPinch();
    // The flash stays in hand: the remaining finger is a plain drag again, from
    // wherever it is now (so the flash does not leap), and it can no longer pop.
    const d = drag;
    if (d) {
      d.x0 = d.lastX; d.y0 = d.lastY; d.baseDx = d.rec.dx; d.baseDy = d.rec.dy;
      d.samples = [{ t: nowMs(), x: d.lastX, y: d.lastY }];   // a pinch is not momentum
    }
  }

  /** ONE step of the gesture. The arithmetic is exec/pinch.js's; this reads the
   *  record, writes the size var and repaints. */
  function applyPinch() {
    const p = pinch;
    if (!p) return;
    const rec = p.rec;
    if (!rec || rec.popped || !rec.node) { forgetPinch(); return; }
    const dist = pinchDistance(p.ax, p.ay, p.bx, p.by);
    if (!p.live) {
      // Two fingers resting is not a gesture. Keep the midpoint fresh so the pan
      // does not jump the moment it becomes one.
      if (!pinchStarted(p.startDist, dist, PINCH_SLOP_PX)) {
        const m = pinchMidpoint(p.ax, p.ay, p.bx, p.by);
        p.midX = m.x; p.midY = m.y;
        return;
      }
      p.live = true;
    }
    const W = vpW(), H = vpH();
    const vmin = Math.min(W, H) / 100;              // px in one vmin: the size unit's scale
    const out = pinchStep(
      {
        startDist: p.startDist, startSize: p.startSize, base: rec.baseSizeVmin, size: rec.sizeVmin,
        // A flash is anchored on its CENTRE (every rule translates it -50%,-50%).
        anchorX: rec.x / 100 * W + rec.dx, anchorY: rec.y / 100 * H + rec.dy,
        midX: p.midX, midY: p.midY,
      },
      { ax: p.ax, ay: p.ay, bx: p.bx, by: p.by },
      {
        vw: W, vh: H, insets: p.insets, aspect: 1, centred: true, pxPerUnit: vmin,
        keep: PINCH_KEEP_PX,
        cap: pxToVmin(viewportCap(W, H, p.insets, 1), W, H),
        minFactor: PINCH_MIN_FACTOR, maxFactor: PINCH_MAX_FACTOR,
      },
    );
    p.midX = out.midX; p.midY = out.midY;
    rec.sizeVmin = out.size;
    rec.dx = out.x - (rec.x / 100 * W);
    rec.dy = out.y - (rec.y / 100 * H);
    try { rec.node.style.setProperty('--gg-flash-size', `${out.size.toFixed(1)}vmin`); } catch (_e) { /* ignore */ }
    paint(rec);
  }

  /** The tail of the gesture, in px/frame. Anything older than the window is not
   *  momentum — it is where your hand HAD been, and letting it count is what
   *  makes a flick after a slow drag fly across the room. */
  function velocity(d) {
    const t = nowMs();
    const s = d.samples.filter((p) => t - p.t <= VELOCITY_WINDOW_MS);
    if (s.length < 2) return { vx: 0, vy: 0 };
    const a = s[0], b = s[s.length - 1];
    const dt = Math.max(8, b.t - a.t);
    const cap = (v) => (v > FLING_MAX ? FLING_MAX : (v < -FLING_MAX ? -FLING_MAX : v));
    return { vx: cap((b.x - a.x) / dt * FRAME_MS), vy: cap((b.y - a.y) / dt * FRAME_MS) };
  }

  /**
   * THE ONE EXIT. `why` is 'up' (released), 'cancel' (pointercancel / lost
   * capture), 'stop' or 'watchdog'. Only 'up' can pop or throw; every other
   * reason is a gentle drop, which is exactly what "no stuck held flashes" means.
   */
  function endDrag(why) {
    const d = drag;
    if (!d) return;
    forgetPinch();                  // whichever finger left, the gesture is over
    drag = null;
    listen(d, false);
    try { clearTimeout(d.watchdog); } catch (_e) { /* ignore */ }

    const rec = d.rec;
    if (!rec || !rec.node) return;

    if (!d.grabbed) {
      // Never passed the slop: this press was a CLICK. (A wheel-resize counts as
      // using the flash, not clicking it, so it eats the pop.)
      if (why === 'up' && !d.wheeled && rec.node.isConnected) hydra(rec, d.up || null);
      return;
    }

    rec.held = false;
    try { rec.node.classList.remove('is-held'); } catch (_e) { /* ignore */ }
    if (!rec.node.isConnected || rec.popped) return;

    // The clock restarts — and a release always buys a couple of seconds, so a
    // flash you just flung is never snatched away mid-glide.
    rec.remainMs = Math.max(rec.remainMs, HELD_RESUME_MIN_MS);
    rec.bornAt = nowMs();
    rec.holdMs = rec.remainMs;
    rec.expTimer = soon(() => expire(rec), rec.remainMs);

    let vx = 0, vy = 0;
    // A PINCH IS NOT A THROW. Two fingers leaving a flash at speed is a gesture
    // ending, not momentum handed over — and a zoomed-in flash sailing off the
    // screen because you let go quickly is the opposite of what was asked for.
    if (why === 'up' && !d.pinched) { const v = velocity(d); vx = v.vx; vy = v.vy; }
    const speed = Math.hypot(vx, vy);
    if (speed >= FLING_MIN) {
      rec.fly = { vx, vy, escape: speed > FLING_ESCAPE };
      flying.add(rec);
      stepLast = nowMs();
      schedule();
    }
    paint(rec);
  }

  function onPointerUp(e) {
    const d = drag;
    if (!d || !idMatch(d, e)) return;
    d.up = e;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    endDrag('up');
  }

  /** pointercancel (the OS took the gesture) and lostpointercapture (the node
   *  went away under us) are the same story: the hand is empty and the flash
   *  stays exactly where it was, un-popped and un-thrown. The one exception is
   *  the release lift() causes on purpose — that one is a paperwork event, not a
   *  lost gesture, and it is swallowed once. */
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

  /** Wheel while pressing = resize, through the size VAR. It persists for the
   *  rest of that flash's life and its hydra children taper off the NEW size. */
  function onWheel(e) {
    const d = drag;
    if (!d || !d.rec || d.rec.popped) return;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();   // the page never scrolls
    if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    const dy = num(e && e.deltaY, 0);
    if (!dy) return;
    // Line/page deltaModes report single digits; a rounded 0 is still one notch.
    const notches = Math.max(-3, Math.min(3, Math.round(dy / 100) || (dy > 0 ? 1 : -1)));
    const rec = d.rec;
    const lo = rec.baseSizeVmin * SIZE_MIN_FACTOR;
    const hi = rec.baseSizeVmin * SIZE_MAX_FACTOR;
    const want = rec.sizeVmin * Math.pow(WHEEL_STEP, -notches);   // wheel UP (deltaY<0) grows
    const next = want < lo ? lo : (want > hi ? hi : want);
    if (Math.abs(next - rec.sizeVmin) < 0.01) { d.wheeled = true; return; }
    rec.sizeVmin = next;
    d.wheeled = true;
    try { rec.node.style.setProperty('--gg-flash-size', `${next.toFixed(1)}vmin`); } catch (_e) { /* ignore */ }
  }

  /* =========================================================================
   * THE ANIMATION BUDGET's moving parts (lite only — see ANIM_LIVE_LITE).
   * ======================================================================= */

  /** Live flashes currently PLAYING an animated file. Counted off the live
   *  set, not tracked as a counter, so a node the layer tore out from under us
   *  (prune) can never leave a phantom charge against the budget. */
  function countAnimPlaying() {
    let k = 0;
    for (const rec of live) { if (rec.animPlaying) k++; }
    return k;
  }

  /** A canvas ready to wear frame 0, or null when this host cannot freeze one
   *  (no 2d context, no Image constructor) — the caller then SKIPS the flash. */
  function frozenCanvas() {
    if (typeof Image !== 'function') return null;
    let cv = null;
    try { cv = document.createElement('canvas'); } catch (_e) { return null; }
    if (!cv || typeof cv.getContext !== 'function') return null;
    let ctx = null;
    try { ctx = cv.getContext('2d'); } catch (_e) { return null; }
    if (!ctx || typeof ctx.drawImage !== 'function') return null;
    return cv;
  }

  /**
   * Decode ONE frame of `url` into the canvas. The probe <img> never enters
   * the DOM, so the browser decodes the first frame for the draw and nothing
   * ever asks it to advance — that asymmetry is the entire saving. Drawing a
   * cross-origin frame TAINTS the canvas, which is fine: nothing here reads
   * pixels back, it only shows them. Every failure lands on rec.kill(), the
   * same road a dud <img> src takes (a skipped flash, never a throw).
   */
  function paintFrameZero(cv, url, rec) {
    let probe = null;
    try { probe = new Image(); } catch (_e) { rec.kill(); return; }
    probe.onload = () => {
      if (rec.popped || !cv.isConnected) return;   // it died while decoding
      const w = (probe.naturalWidth | 0) || (probe.width | 0);
      const h = (probe.naturalHeight | 0) || (probe.height | 0);
      if (!w || !h) { rec.kill(); return; }
      const k = Math.min(1, FROZEN_MAX_PX / Math.max(w, h));
      try {
        cv.width = Math.max(1, Math.round(w * k));
        cv.height = Math.max(1, Math.round(h * k));
        cv.getContext('2d').drawImage(probe, 0, 0, cv.width, cv.height);
      } catch (_e) { rec.kill(); return; }
      // Let the decoder drop the animation it was holding for the probe.
      try { probe.src = ''; } catch (_e) { /* ignore */ }
    };
    probe.onerror = () => rec.kill();
    try { probe.decoding = 'async'; } catch (_e) { /* ignore */ }
    try { probe.src = url; } catch (_e) { rec.kill(); }
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
   * `opts` = {gen, nearX, nearY, size} when this one hatched from a click; the
   * parent hands down an absolute size, already tapered off whatever IT was.
   * `run` is the burst/sustained run this flash belongs to, and it is how a
   * PAYLOAD burst spends its `xfer:` tags — see takeTag.
   * THE ONE PLACE the cap is enforced — every spawn path lands here.
   */
  function showOne(tune, opts, run) {
    prune();
    const host = layer();
    if (!host || typeof document === 'undefined') return;
    if (live.size >= liveCap()) return;
    if (!media || typeof media.drawKind !== 'function') return;

    // media.js kinds are image|video; GIFs ride as images. A PEER run spends
    // its exact xfer: tags first, then keeps drawing from whatever else the
    // opponent has transferred this match (media.drawReceived) — the local
    // library is the last resort, not the rest of the burst.
    const drawWith = takeTag(run);
    const wantPeer = !!(run && run.peer);
    let entry = (drawWith && typeof media.drawFor === 'function')
      ? media.drawFor('image', drawWith)
      : null;
    if (wantPeer && (!entry || entry.provenance !== 'peer') && typeof media.drawReceived === 'function') {
      entry = media.drawReceived('image') || entry;
    }
    if (!entry) entry = media.drawKind('image');
    if (!entry) return;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return;

    const gen = Math.max(0, (opts && opts.gen) | 0);
    const pos = place(opts && opts.nearX, opts && opts.nearY);
    // Children are handed an ABSOLUTE size by their parent (already tapered off
    // whatever the parent had grown to); a gen-0 flash is always born at base.
    const sizeVmin = (opts && num(opts.size, 0) > 0) ? opts.size : BASE_SIZE_VMIN;
    const rot = (Math.random() * 16 - 8).toFixed(1);

    // THE ANIMATION BUDGET (lite only — see ANIM_LIVE_LITE at the top). The
    // sniff runs only under lite, so the full tier does not even classify:
    // its spawn path is byte-identical to the pre-budget one. Over budget, the
    // flash lands FROZEN (a frame-0 canvas instead of an <img>); a host that
    // cannot freeze skips it, which is what the cap already does when full.
    const animated = perfLite() && isAnimatedMedia(entry);
    const mustFreeze = animated && countAnimPlaying() >= ANIM_LIVE_LITE;

    let img;
    if (mustFreeze) {
      img = frozenCanvas();
      if (!img) {
        try { if (handle.release) handle.release(); } catch (_e) { /* ignore */ }
        return;                                    // a skipped beat, never a flood
      }
    } else {
      img = document.createElement('img');
      img.decoding = 'async';
      img.alt = '';
    }
    // --hydra is the pointer opt-in (fx.css). bubbles.js's pop flashes are plain
    // .gg-flash on purpose and stay click-through.
    img.className = gen > 0 ? 'gg-flash gg-flash--hydra gg-flash--hatch' : 'gg-flash gg-flash--hydra';
    img.style.setProperty('--gg-flash-dur', `${tune.holdMs}ms`);
    img.style.setProperty('--gg-flash-op', String(tune.opacity));
    // scatter across the window like the WPF floating flashes (never dead-centre)
    img.style.setProperty('left', `${pos.x.toFixed(1)}vw`);
    img.style.setProperty('top', `${pos.y.toFixed(1)}vh`);
    img.style.setProperty('--gg-flash-rot', `${rot}deg`);
    // Children taper a little so a long hydra chain reads as descendants rather
    // than 20 identical posters. Size is a CSS var, NOT a transform — the tilt,
    // the fade animation and the drag offset own transform between them and must
    // not be fought over. The wheel writes this same var while a flash is held.
    if (gen > 0) img.style.setProperty('--gg-flash-size', `${sizeVmin.toFixed(1)}vmin`);
    img.draggable = false;                    // the browser's own image-drag is not ours

    const rec = {
      node: img, handle, x: pos.x, y: pos.y, gen, tune, popped: false, kill: null,
      peer: wantPeer,                         // hydra children keep drawing peer media
      rot,                                    // tilt, re-applied by every paint()
      dx: 0, dy: 0,                           // drag/fling offset in px off the anchor
      sizeVmin, baseSizeVmin: sizeVmin,       // current vs. born size (the wheel clamp)
      held: false, fly: null,                 // in hand / gliding on a fling
      bornAt: nowMs(), holdMs: tune.holdMs, remainMs: tune.holdMs,
      safety: 0, expTimer: 0,
      // Charged against ANIM_LIVE_LITE while this rec is live. A frozen flash
      // is animated MEDIA but not an animated NODE, so it charges nothing.
      animPlaying: animated && !mustFreeze,
    };
    live.add(rec);

    let killed = false;
    const kill = () => {
      if (killed) return;
      killed = true;
      live.delete(rec);
      flying.delete(rec);
      try { clearTimeout(rec.safety); } catch (_e) { /* ignore */ }
      try { clearTimeout(rec.expTimer); } catch (_e) { /* ignore */ }
      // If it dies in someone's hand, the hand is empty now — not stuck.
      if (drag && drag.rec === rec) forgetDrag();
      try { img.remove(); } catch (_e) { /* already detached */ }
      try { img.removeAttribute('src'); } catch (_e) { /* ignore */ }
      try { if (handle && handle.release) handle.release(); } catch (_e) { /* ignore */ }
    };
    rec.kill = kill;

    // Fires for whichever animation is running — the timed fade, or the pop-out
    // that replaces it on a click.
    img.addEventListener('animationend', kill, { once: true });
    // The press: a click (hydra, on pointerUP) or a grab, decided by the slop.
    img.addEventListener('pointerdown', (e) => onPointerDown(rec, e));
    if (mustFreeze) {
      // The canvas cannot error and carries no src; the PROBE inside
      // paintFrameZero owns both, and its failures land on the same kill.
      host.appendChild(img);
      paintFrameZero(img, handle.url, rec);
    } else {
      img.onerror = kill;                     // a dud entry is a skipped beat, never a throw
      img.src = handle.url;
      host.appendChild(img);
    }
    // Safety net: a throttled/hidden tab (and prefers-reduced-motion, which has no
    // animation at all) may never deliver animationend, and a leaked node is a
    // leak for the rest of the match. The FIRST grab cancels this and hands the
    // whole lifetime over to the pausable JS clock (beginGrab/endDrag/expire).
    rec.safety = soon(kill, tune.holdMs + 600);
  }

  /**
   * Spend ONE of the burst's transferred artifacts, or return null for "draw from
   * our own library" (the overwhelmingly common answer).
   *
   * A burst shows dozens of images; the first XFER_TAGS_MAX of them may be the
   * SENDER's, which is what gives the "that is THEIR picture" moment without
   * spending queue depth on 30 files. Each tag is consumed exactly ONCE — the
   * queue is shallow and a repeated file reads as a bug, not as emphasis.
   *
   * Only a PAYLOAD run ever carries tags: the sustained bed's run has none, hatched
   * children pass no run at all, and both therefore behave exactly as they did.
   */
  function takeTag(run) {
    if (!run || !Array.isArray(run.tags) || !run.tags.length) return null;
    return { tags: [run.tags.shift()] };
  }

  /** Pop it out and free its slot. The pop-out animation replaces the fade. */
  function dismiss(rec) {
    if (rec.popped) return;
    rec.popped = true;
    live.delete(rec);                       // the slot is free the instant it is clicked
    flying.delete(rec);
    try { clearTimeout(rec.expTimer); } catch (_e) { /* ignore */ }
    // A flash that has been dragged pops with an OPACITY-ONLY keyframe
    // (.gg-flash--grabbed.is-popped in fx.css): animations outrank inline styles,
    // so a pop that animated transform would snap it home before it died.
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
    // the cap - 1. At the cap room is 0 and the click is a plain dismissal.
    // liveCap(), not MAX_LIVE: the hydra spends the same headroom the tier set.
    const room = Math.max(0, liveCap() - live.size);
    dismiss(rec);
    sfx('flash-pop');

    const kids = Math.min(HYDRA_CHILDREN, room);
    // Children hatch off where the parent IS (drag included) and taper off the
    // size the parent HAD (wheel included), not off the anchor and the base.
    const nearX = curX(rec), nearY = curY(rec);
    const size = rec.sizeVmin * (genFactor(rec.gen + 1) / genFactor(rec.gen));
    for (let k = 0; k < kids; k++) {
      const opts = { gen: rec.gen + 1, nearX, nearY, size };
      // A live sustained run re-tunes the children to the CURRENT intensity;
      // otherwise they inherit whatever tune spawned their parent.
      const tune = sustained ? flashTuning(sustained.intensity, calm) : rec.tune;
      // A child of a peer flash is still the opponent's moment: pass a tagless
      // peer run so it draws received media too (takeTag on it answers null).
      const kidRun = rec.peer ? { peer: true } : undefined;
      soon(() => showOne(tune, opts, kidRun), HATCH_MS[k] != null ? HATCH_MS[k] : 70 + k * 140);
    }
  }

  /** One beat: `count` flashes, staggered so they land as a scatter not a stack.
   *  The stagger re-checks `run.alive`, so stop()/cancel() means stop spawning —
   *  a half-landed beat can no longer trail nodes in behind a stopped element. */
  function beat(tune, run) {
    showOne(tune, null, run);
    // A fixed stagger, not a fraction of holdMs: at a 5s lifetime a proportional
    // stagger would drift the second flash of a beat most of a second late.
    for (let i = 1; i < tune.count; i++) {
      soon(() => { if (!run || run.alive) showOne(tune, null, run); }, 150 * i + Math.round(rand(0, 120)));
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
      // Teardown of the PHYSICS is unconditional (stop() also arrives via
      // executor.stopAll(), which is about to empty the DOM): the hand lets go
      // gently, every glider lands where it is, and the shared stepper stops.
      // Nothing here kills a node — airborne flashes still finish their own life.
      if (drag) endDrag('stop');
      for (const rec of Array.from(flying)) { rec.fly = null; flying.delete(rec); }
      unschedule();

      if (!sustained) return;
      sustained.alive = false;
      try { clearTimeout(sustained.timer); } catch (_e) { /* ignore */ }
      sustained = null;
      // Airborne flashes finish their fade — each retires on its own timer, and
      // each one stays clickable (and draggable) until it does.
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

      const run = {
        alive: true, intensity: Math.max(0.55, intensity), timer: 0, endTimer: 0,
        // Up to XFER_TAGS_MAX artifacts the sender transferred, consumed one per
        // flash by takeTag and then gone. Empty for every payload that carries no
        // tags, which is every payload before this feature and every payload on a
        // relayed connection.
        tags: xferTags(payload),
        // PEER run: after `tags` is spent, showOne keeps drawing from the
        // received store instead of the local library (null in solo, where
        // nothing was ever received — the fallback is unchanged there).
        peer: true,
      };
      // Tell the ambient renderers (spiral bed, drain wash) a squall is on:
      // on lite they volunteer frames for its duration. Held unconditionally,
      // read behind perfLite() — see exec/loadGovernor.js for why both.
      const releaseGovernor = governorHold(runMs);

      let finished = false;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        run.alive = false;
        try { releaseGovernor(); } catch (_e) { /* ignore */ }
        try { clearTimeout(run.timer); } catch (_e) { /* ignore */ }
        try { clearTimeout(run.endTimer); } catch (_e) { /* ignore */ }
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };

      const burstLoop = () => {
        if (!run.alive) return;
        const tune = flashTuning(run.intensity, calm);
        // Tier split — see BURST_GAP_SCALE/_LITE: on lite the burst is a burst
        // by TEMPO only. Read per beat, so the mid-match toggle bites.
        if (perfLite()) {
          tune.gapMs = Math.round(tune.gapMs * BURST_GAP_SCALE_LITE);
        } else {
          tune.gapMs = Math.round(tune.gapMs * BURST_GAP_SCALE);
          tune.count = Math.min(3, tune.count + 1);
        }
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
