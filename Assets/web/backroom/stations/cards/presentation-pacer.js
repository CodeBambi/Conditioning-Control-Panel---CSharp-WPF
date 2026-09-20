import { createRenderBudget } from '../../room/render-budget.js';

/** Pace presentation only. Gameplay queues keep their wall-clock deadlines. */
export function createPresentationPacer(device = {}, policy = null) {
  const fps = createRenderBudget(device).mobile ? 30 : 60;
  let interval = 1000 / fps;
  let next = null;
  return {
    get fps() { return policy?.performance ? 30 : fps; },
    due(now) {
      if (!Number.isFinite(now)) return false;
      const desired = 1000 / (policy?.performance ? 30 : fps);
      if (desired !== interval) { interval = desired; next = null; }
      if (next === null) { next = now + interval; return true; }
      // Tolerate floating point rounding, without drawing early on ordinary RAFs.
      if (now + .001 < next) return false;
      // Skip missed slots after a stall. Never replay a burst of stale paints.
      next += (Math.floor(Math.max(0, now - next) / interval + .000001) + 1) * interval;
      return true;
    },
    reset() { next = null; },
  };
}
