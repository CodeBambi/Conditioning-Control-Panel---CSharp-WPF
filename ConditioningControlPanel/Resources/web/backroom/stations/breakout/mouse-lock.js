/* The mouse under pointer lock (owner, 2026-09-22: "my number 1 frustration now is when my mouse exits the browser window
 * and the paddle stops responding"). Locked, the browser hands the game RELATIVE movement with no window edge, so the
 * paddle keeps following however far the hand travels. Same shape as the room's look lock (room/scene.js): a mouse
 * click takes the mouse, Esc frees it, a refused lock leaves absolute steering as it was, and Chromium's ~1 s refusal
 * after an Esc exit is a cooldown, never counted. Fingers and pens never lock. Pure enough to test with a fake document. */

export const LOCK_REFUSALS = 4;
export const LOCK_COOLDOWN_MS = 1500;
/** Paddle travel per mouse count while locked, on top of the field's own scale. 1 = the paddle follows the hand exactly. */
export const LOCK_GAIN = 1;
export const STORE_KEY = 'bo.mouselock.v1';
/**
 * The most one mouse report may move the paddle, in CSS px (ccp-bugs #1337: "the paddle occasionally jumps in the
 * direction it is travelling"). Chromium and WebView2 on Windows hand a locked page the odd spurious movementX burst
 * (the hidden cursor's re-centre warp, hundreds of px in one report), and `input.x` is the paddle target, so one bad
 * report teleported the paddle. Real numbers for the cap: a flat-out sweep across a 1920 px screen in 120 ms is about
 * 16 px/ms, which is 16 px per report on a 1000 Hz mouse and 128 px on a 125 Hz office mouse (the slowest common
 * rate). 160 sits above every one of those, so normal play never touches it; only a burst no hand can make is cut.
 * The cap applies per RAW report (the coalesced list), never to a frame's sum, so a long frame on a heavy wall
 * (pendulums) still carries all of the hand's real travel.
 */
export const LOCK_MAX_STEP = 160;

/** The paddle's next x from a locked mouse move: current x plus the movement in field px, held inside the walls. */
export function lockedSteer(x, movementX, fieldPerCss, half, width) {
  const dx = (Number(movementX) || 0) * (Number(fieldPerCss) || 1) * LOCK_GAIN;
  return Math.max(half, Math.min(width - half, x + dx));
}

/** One raw report's movement, held to +-cap. Garbage is a no-move. */
export function clampStep(movementX, cap = LOCK_MAX_STEP) {
  const v = Number(movementX);
  if (!Number.isFinite(v)) return 0;
  return Math.max(-cap, Math.min(cap, v));
}

/**
 * A pointermove's movement as the sum of its raw reports, each clamped. The browser folds every report since the last
 * frame into one event whose movementX is their sum; `getCoalescedEvents()` hands the reports back. When that list is
 * missing, empty (a synthetic event) or does not add up to the event's own movementX (some builds report 0 per
 * coalesced entry), the event itself is the one report.
 */
export function lockedMovement(e, cap = LOCK_MAX_STEP) {
  const whole = Number(e?.movementX) || 0;
  let list = null;
  try { list = typeof e?.getCoalescedEvents === 'function' ? e.getCoalescedEvents() : null; } catch (err) { list = null; }
  if (list && list.length) {
    let raw = 0, sum = 0;
    for (const c of list) { const v = Number(c?.movementX) || 0; raw += v; sum += clampStep(v, cap); }
    if (Math.abs(raw - whole) <= 1) return sum;
  }
  return clampStep(whole, cap);
}

/** The saved preference: on unless the player switched it off. */
export function readEnabled(store) {
  try { return store?.get?.(STORE_KEY) !== '0'; } catch (e) { return true; }
}

/**
 * createMouseLock({ canvas, doc, now, onLost }) -> { request(e), release(), movement(e), setEnabled(on), locked, enabled, refused, dispose() }
 * `onLost()` fires when a held lock goes away by itself (Esc, a tab switch): the station pauses on it so the pointer
 * that just came back has a card to click. A lock the station lets go of on purpose (`release()`, the switch going
 * off, `dispose()`) is NOT lost and never fires it: the ending releases the mouse for its card, and a pause on that
 * release paused the ending on every frame (owner, 2026-09-22: "if I unpause it repauses immediately").
 */
export function createMouseLock({ canvas, doc, now = () => (globalThis.performance ? performance.now() : Date.now()), onLost = () => {}, enabled = true } = {}) {
  let locked = false, refused = false, errors = 0, unlockedAt = -1e9, on = !!enabled, disposed = false, letting = false, fresh = false;
  const has = () => !!canvas && !!doc && typeof canvas.requestPointerLock === 'function';
  const failed = () => { if (now() - unlockedAt < LOCK_COOLDOWN_MS) return; if (++errors >= LOCK_REFUSALS) refused = true; };
  const change = () => {
    const isNow = !!doc && doc.pointerLockElement === canvas;
    const meant = letting; letting = false;                 // one release answers one change, however it went
    if (locked && !isNow) { unlockedAt = now(); locked = false; if (!meant) { try { onLost(); } catch (e) { /* the station's problem */ } } return; }
    if (isNow) { errors = 0; if (!locked) fresh = true; }   // a lock just taken: its first move is the cursor's jump, not the hand
    locked = isNow;
  };
  const error = () => failed();
  if (doc && typeof doc.addEventListener === 'function') { doc.addEventListener('pointerlockchange', change); doc.addEventListener('pointerlockerror', error); }
  return {
    /** A mouse press while playing takes the mouse. Anything else, or a lock switched off or refused, leaves it alone. */
    request(e) {
      if (disposed || !on || refused || locked || !has()) return false;
      if (e && e.pointerType && e.pointerType !== 'mouse') return false;
      try {
        const p = canvas.requestPointerLock();
        if (p && typeof p.catch === 'function') p.catch(() => failed());
        return true;
      } catch (err) { failed(); return false; }
    },
    /**
     * Pointer capture for a press, safe under the lock. Chromium (and WebView2) THROWS InvalidStateError from
     * setPointerCapture while the document holds a pointer lock, and an unguarded call there ended the press handler
     * before it could launch: every click after the first one that took the mouse did nothing, which only showed where
     * there is no auto launch (the wall 8 finale, tester report 2026-09-28). A locked pointer needs no capture anyway.
     */
    capture(e) {
      if (!canvas || typeof canvas.setPointerCapture !== 'function' || (doc && doc.pointerLockElement === canvas)) return false;
      try { canvas.setPointerCapture(e.pointerId); return true; } catch (err) { return false; }
    },
    /** Lets the mouse go on purpose: the change it causes is meant, not lost, so `onLost` stays quiet. */
    release() {
      if (!doc || doc.pointerLockElement !== canvas) return;
      letting = true;
      try { doc.exitPointerLock(); } catch (err) { letting = false; }
    },
    /**
     * A locked pointermove's movement in CSS px, safe to steer by (ccp-bugs #1337). The first move after the lock is
     * taken is dropped: browsers can report the whole jump of the cursor into lock as that one move. Every later move
     * is its raw reports, each clamped to LOCK_MAX_STEP (lockedMovement).
     */
    movement(e) {
      if (fresh) { fresh = false; return 0; }
      return lockedMovement(e);
    },
    setEnabled(next) { on = !!next; if (!on) this.release(); },
    get locked() { return locked; },
    get enabled() { return on; },
    get refused() { return refused; },
    dispose() {
      disposed = true; this.release();
      if (doc && typeof doc.removeEventListener === 'function') { doc.removeEventListener('pointerlockchange', change); doc.removeEventListener('pointerlockerror', error); }
    },
  };
}
