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
  if (ms === Infinity) return 'Untimed';
  const left = Math.max(0, ms);
  const total = Math.floor(left / 1000);
  const min = Math.floor(total / 60);
  const sec = total % 60;
  if (left < 10000) return `${min}:${String(sec).padStart(2, '0')}.${Math.floor((left % 1000) / 100)}`;
  return `${min}:${String(sec).padStart(2, '0')}`;
}

export function createClock({ perSideMs = DEFAULT_MS, onTick, onFlag, now = () => Date.now() } = {}) {
  const untimed = perSideMs === 0;
  const initial = untimed ? Infinity : perSideMs;
  const left = { w: initial, b: initial };
  let active = null;
  let since = 0;
  let timer = null;
  let flagged = null;

  function drain() {
    if (!active || untimed) return;
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
    return { w: left.w, b: left.b, total: perSideMs, active, ...(untimed ? { untimed: true } : {}) };
  }

  function tick() {
    drain();
    if (onTick) onTick(snapshot());
  }

  function start(side) {
    if (flagged) return;
    drain();
    if (flagged) return;
    active = side;
    since = now();
    if (untimed) { if (onTick) onTick(snapshot()); return; }
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
    /**
     * Give time back, for a ply that was taken back. It is the only way time
     * ever goes up, and a side that has already flagged keeps its flag: the
     * game is over by then and nothing is owed.
     */
    credit(side, ms) {
      if (untimed || flagged || !(ms > 0)) return;
      drain();
      left[side] = Math.min(perSideMs, left[side] + ms);
    },
    /** Only for tests: charge a side without waiting in real time. */
    debit(side, ms) {
      if (untimed) return;
      drain();
      left[side] = Math.max(0, left[side] - ms);
      if (left[side] === 0 && !flagged) { flagged = side; const s = side; stop(); if (onFlag) onFlag(s); }
    },
    /** Restore a local saved clock. Starting the seat begins counting again. */
    restore(saved) {
      stop();
      flagged = null;
      for (const side of ['w', 'b']) {
        const value = saved?.[side];
        left[side] = untimed ? Infinity : (Number.isFinite(value) ? Math.min(perSideMs, Math.max(0, value)) : perSideMs);
      }
    },
    reset() {
      stop();
      left.w = initial; left.b = initial; flagged = null;
    },
  };
}
