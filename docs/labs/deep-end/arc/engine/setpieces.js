/* ============================================================================
 * engine/setpieces.js — the set-piece director.
 *
 * One descriptor shape replaces the ~8 hand-rolled probability blocks Intake
 * grew (GROUND-RULES §6 / MINED-INTAKE §5.9-11):
 *
 *   { key, eligiblePhases, perBeatChance, oncePerRunGate, forceTail,
 *     maxPerPhase?, minGap?, cosmetic?, run? }
 *
 *   eligiblePhases   array of phase keys/indices, or a predicate(phase) -> bool,
 *                    or omitted (= every phase)
 *   perBeatChance    0..1 chance per beat() while eligible
 *   oncePerRunGate   true = at most one fire per run
 *   forceTail        true = if it never fired, FORCE it on the tail beat
 *   maxPerPhase      anti-clump: max fires within one phase (default Infinity)
 *   minGap           anti-clump: min beats between fires of THIS key (default 0)
 *   cosmetic         true = opts out of the gateUsedThisBeat mutual exclusion
 *   run              optional callback invoked when it fires (also reported)
 *
 * GATE: at most ONE non-cosmetic set-piece owns a beat (`gateUsedThisBeat`).
 * All rolls come off the seeded rng — nothing here touches Math.random.
 * ==========================================================================*/

import { clamp01 } from '../core/caps.js';

export function createSetpieceDirector({ rng = Math.random, log = () => {} } = {}) {
  const specs = new Map();
  const state = new Map();     // key -> { firedRun, firedPhase, lastBeat }
  let beatIndex = 0;
  let phase = 0;               // current phase key (index or string)
  let tailArmed = false;

  const st = (key) => {
    let s = state.get(key);
    if (!s) { s = { firedRun: 0, firedPhase: 0, lastBeat: -Infinity }; state.set(key, s); }
    return s;
  };

  function register(descriptor) {
    if (!descriptor || !descriptor.key) return null;
    const d = {
      key: String(descriptor.key),
      eligiblePhases: descriptor.eligiblePhases == null ? null : descriptor.eligiblePhases,
      perBeatChance: clamp01(descriptor.perBeatChance == null ? 0 : descriptor.perBeatChance),
      oncePerRunGate: !!descriptor.oncePerRunGate,
      forceTail: !!descriptor.forceTail,
      maxPerPhase: Number.isFinite(descriptor.maxPerPhase) ? descriptor.maxPerPhase : Infinity,
      minGap: Number.isFinite(descriptor.minGap) ? Math.max(0, descriptor.minGap) : 0,
      cosmetic: !!descriptor.cosmetic,
      run: typeof descriptor.run === 'function' ? descriptor.run : null,
    };
    specs.set(d.key, d);
    st(d.key);
    return {
      key: d.key,
      unregister() { specs.delete(d.key); state.delete(d.key); },
      /** Fire it by hand (big moments / debriefed twists). Honours nothing but disposal. */
      force(ctx) { return fireOne(d, ctx, 'forced'); },
    };
  }

  function eligible(d) {
    if (d.eligiblePhases == null) return true;
    if (typeof d.eligiblePhases === 'function') { try { return !!d.eligiblePhases(phase); } catch { return false; } }
    if (Array.isArray(d.eligiblePhases)) return d.eligiblePhases.includes(phase);
    return d.eligiblePhases === phase;
  }

  function blocked(d) {
    const s = st(d.key);
    if (d.oncePerRunGate && s.firedRun > 0) return 'once-per-run';
    if (s.firedPhase >= d.maxPerPhase) return 'max-per-phase';
    if (beatIndex - s.lastBeat < d.minGap) return 'min-gap';
    return null;
  }

  function fireOne(d, ctx, why) {
    const s = st(d.key);
    s.firedRun += 1;
    s.firedPhase += 1;
    s.lastBeat = beatIndex;
    let result;
    if (d.run) { try { result = d.run(ctx || {}); } catch (e) { log('setpiece ' + d.key + ' threw: ' + (e && e.message)); } }
    return { key: d.key, why, beat: beatIndex, phase, result };
  }

  /**
   * setPhase(next) — resets per-phase counters when the phase actually changes.
   * Phases can be indices (0..3, matching curves.PHASE_BANDS) or string keys.
   */
  function setPhase(next) {
    if (next === phase) return phase;
    phase = next;
    for (const s of state.values()) s.firedPhase = 0;
    return phase;
  }

  /** Arm the tail so the NEXT beat honours forceTail (the class's last beat). */
  function armTail(on = true) { tailArmed = !!on; }

  /**
   * beat(ctx) — advance the beat clock and resolve set-pieces.
   * Returns { beat, phase, fired:[{key,why,...}], gateUsed }.
   * At most one non-cosmetic set-piece fires (gateUsedThisBeat); cosmetics may
   * still ride along. forceTail wins over chance when the tail is armed.
   */
  function beat(ctx) {
    beatIndex += 1;
    let gateUsed = false;
    const fired = [];
    const order = [...specs.values()];

    // forced tail pass first: anything with forceTail that never fired this run
    if (tailArmed) {
      for (const d of order) {
        if (!d.forceTail) continue;
        if (st(d.key).firedRun > 0) continue;
        if (!d.cosmetic && gateUsed) continue;
        fired.push(fireOne(d, ctx, 'force-tail'));
        if (!d.cosmetic) gateUsed = true;
      }
    }

    for (const d of order) {
      if (d.perBeatChance <= 0) continue;
      if (!d.cosmetic && gateUsed) continue;
      if (!eligible(d)) continue;
      if (blocked(d)) continue;
      if (clamp01(rng()) >= d.perBeatChance) continue;
      fired.push(fireOne(d, ctx, 'chance'));
      if (!d.cosmetic) gateUsed = true;
    }
    return { beat: beatIndex, phase, fired, gateUsed };
  }

  function reset() {
    state.clear();
    for (const d of specs.values()) st(d.key);
    beatIndex = 0;
    tailArmed = false;
  }

  return {
    register, setPhase, armTail, beat, reset,
    get beatIndex() { return beatIndex; },
    get phase() { return phase; },
    stats() {
      const out = {};
      for (const [k, s] of state) out[k] = { firedRun: s.firedRun, firedPhase: s.firedPhase, lastBeat: s.lastBeat };
      return out;
    },
  };
}

export default createSetpieceDirector;
