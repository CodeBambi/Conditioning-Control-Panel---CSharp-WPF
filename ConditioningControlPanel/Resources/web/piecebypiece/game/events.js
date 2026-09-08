/* ============================================================================
 * game/events.js - the one bus the board and the effects layer share.
 *
 * Deliberately tiny and synchronous. A listener that throws is logged and
 * skipped so one bad handler can never stall a move or a frame.
 * ==========================================================================*/

export function createBus() {
  const map = new Map(); // type -> Set<fn>

  return {
    on(type, fn) {
      if (typeof fn !== 'function') return () => {};
      if (!map.has(type)) map.set(type, new Set());
      map.get(type).add(fn);
      return () => this.off(type, fn);
    },
    off(type, fn) {
      const set = map.get(type);
      if (set) set.delete(fn);
    },
    emit(type, payload) {
      const set = map.get(type);
      if (!set || set.size === 0) return;
      for (const fn of [...set]) {
        try { fn(payload, type); } catch (err) { console.warn('[pbp] listener failed for ' + type, err); }
      }
    },
  };
}
