import { createRenderBudget } from '../room/render-budget.js';

/** Presentation policy only. Never changes motion preferences or game state. */
export function createQuality(device = {}, storage = null) {
  const conservative = createRenderBudget(device).mobile;
  let mode = 'auto', reduced = conservative, elapsed = 0, slow = 0, stable = 0;
  const listeners = new Set();
  try { const saved = storage?.getItem('br.quality.v1'); if (['auto','full','performance'].includes(saved)) mode = saved; } catch {}
  const notify = () => { for (const fn of listeners) fn(api); };
  const api = {
    get mode() { return mode; },
    get performance() { return mode === 'performance' || (mode === 'auto' && reduced); },
    setMode(value) {
      if (!['auto','full','performance'].includes(value)) return;
      mode = value; reduced = conservative; elapsed = slow = stable = 0;
      try { storage?.setItem('br.quality.v1', mode); } catch {}
      notify();
    },
    subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
    sample(ms, target, canRecover = false) {
      if (mode !== 'auto' || !Number.isFinite(ms) || ms <= 0 || ms > 250) return;
      elapsed += ms; if (ms > target * 1.45) slow += ms;
      if (elapsed < 5000) return;
      if (slow / elapsed > .3) { stable = 0; if (!reduced) { reduced = true; notify(); } }
      else if (slow / elapsed < .05) { stable += elapsed; if (reduced && !conservative && stable >= 30000 && canRecover) { reduced = false; stable = 0; notify(); } }
      else stable = 0;
      elapsed = slow = 0;
    },
    resetSamples() { elapsed = slow = stable = 0; },
  };
  return api;
}
let storage = null;
try { storage = globalThis.localStorage; } catch {}
export const quality = createQuality(globalThis.navigator || {}, storage);
if (typeof window !== 'undefined') window.__backroomQuality = quality;
