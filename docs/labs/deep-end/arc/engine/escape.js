/* ============================================================================
 * engine/escape.js — the escape guard (Intake invariant, ported verbatim).
 *
 * Any distraction that BLOCKS input must guarantee an exit: once the player has
 * spent ESCAPE_EFFORT interactions or ESCAPE_MS of sustained effort on it, the
 * effect calls forceComplete and lets go. Friction, never lockout.
 *
 * The engine wires one of these around every clickable/blocking effect it
 * renders (clickable flash_burst, clickable bubble_field, any game-declared
 * blocking surface via engine.escapeGuard()).
 * ==========================================================================*/

export const ESCAPE_EFFORT = 6;      // interactions
export const ESCAPE_MS = 5000;       // ms of sustained effort

/**
 * createEscapeGuard({ onComplete, effort, ms, timers })
 *   guard.note()      count one interaction (click/tap/keypress against it)
 *   guard.arm()       start the clock (call when the blocking effect appears)
 *   guard.cancel()    the effect completed normally
 *   guard.tripped     whether forceComplete already fired
 */
export function createEscapeGuard({ onComplete, effort = ESCAPE_EFFORT, ms = ESCAPE_MS, timers } = {}) {
  let count = 0;
  let timer = 0;
  let tripped = false;
  let armed = false;

  function trip(why) {
    if (tripped) return;
    tripped = true;
    if (timer && timers) timers.cancel(timer);
    timer = 0;
    try { if (onComplete) onComplete(why); } catch { /* never throw out of a guard */ }
  }
  function arm() {
    if (armed || tripped) return;
    armed = true;
    if (timers) timer = timers.after(ms, () => trip('timeout'));
  }
  function note() {
    if (tripped) return;
    count += 1;
    if (count >= effort) trip('effort');
  }
  function cancel() {
    armed = false;
    if (timer && timers) timers.cancel(timer);
    timer = 0;
  }
  return {
    arm, note, cancel, trip,
    get tripped() { return tripped; },
    get count() { return count; },
    get effort() { return effort; },
    get ms() { return ms; },
  };
}

export default createEscapeGuard;
