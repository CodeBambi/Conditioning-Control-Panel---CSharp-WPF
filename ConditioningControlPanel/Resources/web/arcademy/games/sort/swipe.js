/* ============================================================================
 * games/sort/swipe.js - THE HAND. Pointer physics and the keyboard, and they
 * are the SAME gesture: a key IS a swipe.
 *
 * THE NUMBERS (pitch section 6, and nobody may re-type them elsewhere):
 *   stack        three cards at 1.00 / 0.95 / 0.90; the second springs up 200ms
 *   tilt         dx / width * 14deg, capped at 18deg
 *   stamp        opacity = |dx| / threshold, so the word arrives WITH the lean
 *   threshold    the larger of 80px and 28% of the card's width
 *   fling        |vx| > 0.5 px/ms commits under threshold - a flick counts
 *   rubber band  under both, the card springs home in 260ms
 *   commit       flies to 1.4x the viewport in 280ms, then shrinks into its
 *                wall slot over 120ms
 *   no rewind    a card that left the stack is gone. There is no undo verb in
 *                this room and there is deliberately no way to add one.
 *
 * THE MATH IS EXPORTED SEPARATELY FROM THE HANDLERS. `thresholdFor`, `tiltFor`,
 * `stampAlpha` and `decide` are pure and the suite asserts them directly; the
 * pointer plumbing around them only ever calls those four.
 *
 * VELOCITY IS SAMPLED, NOT INTEGRATED. We keep the last two moves and divide;
 * a pointer that stalls for 120ms and then jumps must not read as a fling, so
 * a sample older than STALE_MS is dropped and the velocity is zero.
 *
 * REDUCED MOTION plays the same game with none of the travel: the card fades in
 * FADE_MS rather than flying, and the keys are the primary input (they always
 * were - the pointer is the flourish).
 * ==========================================================================*/

export const SWIPE = Object.freeze({
  /** The three live cards, front to back. */
  STACK_SCALES: Object.freeze([1.00, 0.95, 0.90]),
  /** The second card springing to the front. */
  SPRING_MS: 200,
  TILT_DEG: 14,
  TILT_CAP: 18,
  THRESH_PX: 80,
  THRESH_FRAC: 0.28,
  /** px per ms. */
  FLING_VX: 0.5,
  BAND_MS: 260,
  FLY_MS: 280,
  SHRINK_MS: 120,
  /** Reduced motion: one fade, no travel. */
  FADE_MS: 120,
  /** A pointer sample older than this cannot contribute to velocity. */
  STALE_MS: 120,
  /** The travel a committed card makes, as a multiple of the viewport width. */
  FLY_VIEWPORTS: 1.4,
  /** A key press plays a fling at this velocity, so the feel is identical. */
  KEY_VX: 1.2,
});

function num(v, dflt) { const n = Number(v); return Number.isFinite(n) ? n : dflt; }
function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }
export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }

/** The commit distance for a card of this width: 28% of it, never under 80px. */
export function thresholdFor(width) {
  const w = Math.max(0, num(width, 0));
  return Math.max(SWIPE.THRESH_PX, w * SWIPE.THRESH_FRAC);
}

/** The lean, in degrees, capped so a hard drag never spins the card. */
export function tiltFor(dx, width) {
  const w = Math.max(1, num(width, 1));
  return clamp((num(dx, 0) / w) * SWIPE.TILT_DEG, -SWIPE.TILT_CAP, SWIPE.TILT_CAP);
}

/** The stamp fades in with the lean and is full at the threshold. */
export function stampAlpha(dx, threshold) {
  const th = Math.max(1, num(threshold, 1));
  return clamp01(Math.abs(num(dx, 0)) / th);
}

/** Which stamp the lean is showing, or '' while the card is still square on. */
export function stampSide(dx, threshold) {
  const d = num(dx, 0);
  if (Math.abs(d) < Math.max(2, num(threshold, 1) * 0.04)) return '';
  return d > 0 ? 'right' : 'left';
}

/** Scale for stack position 0 (top), 1, 2. Anything deeper is invisible. */
export function scaleForDepth(depth) {
  const i = Math.max(0, Math.round(num(depth, 0)));
  return i < SWIPE.STACK_SCALES.length ? SWIPE.STACK_SCALES[i] : SWIPE.STACK_SCALES[SWIPE.STACK_SCALES.length - 1];
}

/**
 * Does this release commit, and which way?
 * @param {{dx:number, vx:number, threshold:number}} o
 * @returns {{commit:boolean, dir:string, reason:'threshold'|'fling'|'band'}}
 */
export function decide({ dx, vx, threshold } = {}) {
  const d = num(dx, 0);
  const v = num(vx, 0);
  const th = Math.max(1, num(threshold, 1));
  if (Math.abs(d) >= th) return { commit: true, dir: d > 0 ? 'right' : 'left', reason: 'threshold' };
  /* A FLING IS A COMMIT, AND ITS DIRECTION IS THE FLING'S, NOT THE OFFSET'S.
   * A flick that ends 12px left of centre but is travelling right at 0.9px/ms
   * is a right swipe: the hand said so. Only a fling with no direction at all
   * (a perfectly vertical flick) falls through to the band. */
  if (Math.abs(v) > SWIPE.FLING_VX && v !== 0) {
    return { commit: true, dir: v > 0 ? 'right' : 'left', reason: 'fling' };
  }
  return { commit: false, dir: '', reason: 'band' };
}

/** The travel a committed card makes, in px. */
export function flyDistance(viewportWidth) {
  return Math.max(400, num(viewportWidth, 1280) * SWIPE.FLY_VIEWPORTS);
}

/**
 * A velocity sampler. Feed it (x, t) on every move; ask it for px/ms.
 * A gap longer than STALE_MS drops the history: a stall is not a fling.
 */
export function createVelocity() {
  let lastX = null; let lastT = null; let vx = 0;
  return {
    reset() { lastX = null; lastT = null; vx = 0; },
    sample(x, t) {
      const nx = num(x, 0); const nt = num(t, 0);
      if (lastT == null) { lastX = nx; lastT = nt; vx = 0; return 0; }
      const dt = nt - lastT;
      if (dt <= 0) { lastX = nx; return vx; }
      if (dt > SWIPE.STALE_MS) { vx = 0; lastX = nx; lastT = nt; return 0; }
      vx = (nx - lastX) / dt;
      lastX = nx; lastT = nt;
      return vx;
    },
    get value() { return vx; },
  };
}

/* ============================================================================
 * THE BINDING. One gesture source over the stack element; every moment is
 * reported UP and nothing is decided here except the physics above.
 *
 * createSwipe({ el, widthOf, viewportOf, reduced, now, onGrab, onDrag,
 *               onRelease, onCommit })
 *   el          the stack node (the ONE listener - a card is never a target)
 *   widthOf()   the top card's width in px, read live (the stage resizes)
 *   viewportOf() the stage width, for the fly distance
 *   onGrab()    the hand landed
 *   onDrag({dx, dy, tilt, alpha, side})   every move, already resolved
 *   onRelease({commit, dir, reason, dx, vx})  every release, before the beat
 *   onCommit({dir, vx, reason})           only when it committed
 * The handle also exposes `key(dir)` so a keybind plays the identical fling,
 * and `enabled(bool)` so a frozen class cannot be dragged.
 * ==========================================================================*/
export function createSwipe(o = {}) {
  const el = o.el || null;
  const reduced = !!o.reduced;
  const widthOf = typeof o.widthOf === 'function' ? o.widthOf : () => 320;
  const viewportOf = typeof o.viewportOf === 'function' ? o.viewportOf : () => 1280;
  const nowFn = typeof o.now === 'function' ? o.now : () => Date.now();
  const emit = (name, arg) => {
    const fn = o[name];
    if (typeof fn !== 'function') return;
    try { fn(arg); } catch (e) { /* a gesture must never be the thing that throws */ }
  };
  const vel = createVelocity();
  let grab = null;
  let on = true;
  let bound = false;

  function samePointer(e) {
    if (!grab) return false;
    if (e && e.pointerId != null && grab.id != null) return e.pointerId === grab.id;
    return true;
  }

  function onDown(e) {
    if (!on || !el || grab || !e) return;
    grab = {
      id: e.pointerId == null ? null : e.pointerId,
      x: num(e.clientX, 0), y: num(e.clientY, 0),
      dx: 0, dy: 0, captured: false, moved: false,
    };
    vel.reset();
    vel.sample(grab.x, nowFn());
    try {
      if (typeof el.setPointerCapture === 'function' && grab.id != null) {
        el.setPointerCapture(grab.id);
        grab.captured = true;
      }
    } catch (err) { grab.captured = false; }
    emit('onGrab', { x: grab.x, y: grab.y });
  }

  function onMove(e) {
    if (!on || !grab || !samePointer(e)) return;
    const x = num(e.clientX, grab.x);
    const y = num(e.clientY, grab.y);
    grab.dx = x - grab.x;
    grab.dy = y - grab.y;
    grab.moved = true;
    vel.sample(x, nowFn());
    const width = Math.max(1, num(widthOf(), 320));
    const th = thresholdFor(width);
    emit('onDrag', {
      dx: grab.dx, dy: grab.dy,
      tilt: tiltFor(grab.dx, width),
      alpha: stampAlpha(grab.dx, th),
      side: stampSide(grab.dx, th),
      threshold: th,
    });
  }

  function finish(e, cancelled) {
    if (!grab) return;
    const g = grab;
    grab = null;
    if (el && g.captured && g.id != null) {
      try { if (typeof el.releasePointerCapture === 'function') el.releasePointerCapture(g.id); }
      catch (err) { /* the pointer is already gone */ }
    }
    const width = Math.max(1, num(widthOf(), 320));
    const th = thresholdFor(width);
    const vx = cancelled ? 0 : vel.value;
    const d = cancelled ? { commit: false, dir: '', reason: 'band' } : decide({ dx: g.dx, vx, threshold: th });
    vel.reset();
    emit('onRelease', { commit: d.commit, dir: d.dir, reason: d.reason, dx: g.dx, vx, threshold: th });
    if (d.commit) emit('onCommit', { dir: d.dir, vx, reason: d.reason, dx: g.dx });
  }

  function onUp(e) { if (on && grab && samePointer(e)) finish(e, false); }
  function onCancel(e) { if (grab && samePointer(e)) finish(e, true); }
  function onLeave(e) { if (grab && !grab.captured) onCancel(e); }

  function bind() {
    if (bound || !el || typeof el.addEventListener !== 'function') return;
    bound = true;
    el.addEventListener('pointerdown', onDown);
    el.addEventListener('pointermove', onMove);
    el.addEventListener('pointerup', onUp);
    el.addEventListener('pointercancel', onCancel);
    el.addEventListener('lostpointercapture', onCancel);
    el.addEventListener('pointerleave', onLeave);
  }
  function unbind() {
    if (!bound || !el || typeof el.removeEventListener !== 'function') return;
    bound = false;
    el.removeEventListener('pointerdown', onDown);
    el.removeEventListener('pointermove', onMove);
    el.removeEventListener('pointerup', onUp);
    el.removeEventListener('pointercancel', onCancel);
    el.removeEventListener('lostpointercapture', onCancel);
    el.removeEventListener('pointerleave', onLeave);
  }

  bind();

  return {
    /** A KEY IS A SWIPE. Same commit path, same reported fling velocity. */
    key(dir) {
      if (!on) return false;
      const d = dir === 'left' ? 'left' : dir === 'right' ? 'right' : '';
      if (!d) return false;
      if (grab) finish(null, true);        // a key mid-drag cancels the drag first
      const vx = d === 'right' ? SWIPE.KEY_VX : -SWIPE.KEY_VX;
      emit('onRelease', { commit: true, dir: d, reason: 'key', dx: 0, vx, threshold: thresholdFor(widthOf()) });
      emit('onCommit', { dir: d, vx, reason: 'key', dx: 0 });
      return true;
    },
    /** Frozen classes cannot be dragged; an in-flight grab is cancelled. */
    enabled(v) {
      const want = v !== false;
      if (want === on) return on;
      on = want;
      if (!on && grab) finish(null, true);
      return on;
    },
    get dragging() { return !!grab; },
    /** The travel a commit makes right now, in px (reduced motion: none). */
    flyPx() { return reduced ? 0 : flyDistance(viewportOf()); },
    diagnostics() {
      return {
        bound, enabled: on, dragging: !!grab, reduced,
        threshold: thresholdFor(widthOf()),
        width: num(widthOf(), 0),
      };
    },
    destroy() { if (grab) finish(null, true); unbind(); },
  };
}

export default { SWIPE, createSwipe, decide, thresholdFor, tiltFor, stampAlpha };
