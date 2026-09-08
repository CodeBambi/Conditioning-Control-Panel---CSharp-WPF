/* ============================================================================
 * game/clock.js - two clocks, 15 minutes a side, no increment.
 *
 * Ticks four times a second. Running out is a loss, exactly as over the board.
 * The clock measures real elapsed time rather than counting ticks, so a stalled
 * tab or a slow frame cannot hand anyone free seconds.
 * ==========================================================================*/

export const DEFAULT_MS = 15 * 60 * 1000;
export const TICK_MS = 250;

/** "9:07" style, or "0:09.4" under ten seconds. */
export function formatClock(ms) {
  const left = Math.max(0, ms);
  const total = Math.floor(left / 1000);
  const min = Math.floor(total / 60);
  const sec = total % 60;
  if (left < 10000) return `${min}:${String(sec).padStart(2, '0')}.${Math.floor((left % 1000) / 100)}`;
  return `${min}:${String(sec).padStart(2, '0')}`;
}

export function createClock({ perSideMs = DEFAULT_MS, onTick, onFlag, now = () => Date.now() } = {}) {
  const left = { w: perSideMs, b: perSideMs };
  let active = null;
  let since = 0;
  let timer = null;
  let flagged = null;

  function drain() {
    if (!active) return;
    const t = now();
    left[active] = Math.max(0, left[active] - (t - since));
    since = t;
    if (left[active] === 0 && !flagged) {
      flagged = active;
      const side = active;
      stop();
      if (onFlag) onFlag(side);
    }
  }

  function snapshot() {
    return { w: left.w, b: left.b, total: perSideMs, active };
  }

  function tick() {
    drain();
    if (onTick) onTick(snapshot());
  }

  function start(side) {
    if (flagged) return;
    drain();
    active = side;
    since = now();
    if (!timer) { timer = setInterval(tick, TICK_MS); timer.unref?.(); } // unref: node tests never hang on a clock
  }

  function stop() {
    drain();
    active = null;
    if (timer) { clearInterval(timer); timer = null; }
  }

  return {
    start,
    stop,
    /** Hand the move over: charge the mover, run the other side's clock. */
    press(next) { start(next); },
    snapshot,
    remaining(side) { drain(); return left[side]; },
    flagged: () => flagged,
    isRunning: () => active !== null,
    /** Only for tests: charge a side without waiting in real time. */
    debit(side, ms) {
      drain();
      left[side] = Math.max(0, left[side] - ms);
      if (left[side] === 0 && !flagged) { flagged = side; const s = side; stop(); if (onFlag) onFlag(s); }
    },
    reset() {
      stop();
      left.w = perSideMs; left.b = perSideMs; flagged = null;
    },
  };
}
