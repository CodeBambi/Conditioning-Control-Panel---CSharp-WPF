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

/** The paddle's next x from a locked mouse move: current x plus the movement in field px, held inside the walls. */
export function lockedSteer(x, movementX, fieldPerCss, half, width) {
  const dx = (Number(movementX) || 0) * (Number(fieldPerCss) || 1) * LOCK_GAIN;
  return Math.max(half, Math.min(width - half, x + dx));
}

/** The saved preference: on unless the player switched it off. */
export function readEnabled(store) {
  try { return store?.get?.(STORE_KEY) !== '0'; } catch (e) { return true; }
}

/**
 * createMouseLock({ canvas, doc, now, onLost }) -> { request(e), release(), setEnabled(on), locked, enabled, refused, dispose() }
 * `onLost()` fires when a held lock goes away by itself (Esc, a tab switch): the station pauses on it so the pointer
 * that just came back has a card to click. A lock the station lets go of on purpose (`release()`, the switch going
 * off, `dispose()`) is NOT lost and never fires it: the ending releases the mouse for its card, and a pause on that
 * release paused the ending on every frame (owner, 2026-09-22: "if I unpause it repauses immediately").
 */
export function createMouseLock({ canvas, doc, now = () => (globalThis.performance ? performance.now() : Date.now()), onLost = () => {}, enabled = true } = {}) {
  let locked = false, refused = false, errors = 0, unlockedAt = -1e9, on = !!enabled, disposed = false, letting = false;
  const has = () => !!canvas && !!doc && typeof canvas.requestPointerLock === 'function';
  const failed = () => { if (now() - unlockedAt < LOCK_COOLDOWN_MS) return; if (++errors >= LOCK_REFUSALS) refused = true; };
  const change = () => {
    const isNow = !!doc && doc.pointerLockElement === canvas;
    const meant = letting; letting = false;                 // one release answers one change, however it went
    if (locked && !isNow) { unlockedAt = now(); locked = false; if (!meant) { try { onLost(); } catch (e) { /* the station's problem */ } } return; }
    if (isNow) errors = 0;
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
    /** Lets the mouse go on purpose: the change it causes is meant, not lost, so `onLost` stays quiet. */
    release() {
      if (!doc || doc.pointerLockElement !== canvas) return;
      letting = true;
      try { doc.exitPointerLock(); } catch (err) { letting = false; }
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
