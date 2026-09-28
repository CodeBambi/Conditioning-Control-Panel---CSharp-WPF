export const duration = 35;
export const beats = Object.freeze([0, 2, 7, 11, 15, 19, 24, 28, 32]);
export function beatAt(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) return -1;
  return beats.findLastIndex(start => seconds >= start);
}
export function createClock(onBeat, onFinish) {
  let start = null, current = -1, running = false;
  return {
    start(now) { start = now; current = -1; running = true; },
    stop() { running = false; },
    tick(now) {
      if (!running) return null;
      const time = Math.max(0, Math.min(duration, (now - start) / 1000));
      const next = beatAt(time);
      if (next !== current) { current = next; onBeat(next); }
      if (time >= duration) { running = false; onFinish(); }
      return time;
    },
    get running() { return running; }
  };
}
