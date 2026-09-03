/* ============================================================================
 * engine/variants.js — variant pools + the shuffled garnish bag.
 *
 * Ported from intake/render/effects.js (variantWeights / pickVariantIndex /
 * createGarnishBag). Two rules from GROUND-RULES §6:
 *   - every fire-kind owns 3-5 LOOKS, chosen intensity-weighted and never
 *     repeating the last one (so nothing reads as "the same effect again");
 *   - the always-fire rotation is a SHUFFLED BAG, not a per-effect probability
 *     gate (the play-test verdict on gates was "saw one pink wash all run"),
 *     with a force() path for big moments.
 *
 * Pure when a 0..1 `rand` / `rng` is supplied. Node-tested.
 * ==========================================================================*/

import { clamp01 } from '../core/caps.js';

/** Weight vector for n variants at an intensity: a triangular window that
 *  slides subtle -> spectacular, with a floor so every look stays reachable. */
export function variantWeights(n, intensity) {
  const count = Math.max(1, n | 0);
  const c = clamp01(intensity) * (count - 1);
  const w = [];
  for (let k = 0; k < count; k++) {
    w.push(Math.max(0.12, 1 - (Math.abs(k - c) / Math.max(1, count - 1)) * 0.9));
  }
  return w;
}

/** Roll a variant index: intensity-weighted, never repeating lastIndex. */
export function pickVariantIndex(n, lastIndex, intensity, rand = 0.5) {
  const count = Math.max(1, n | 0);
  if (count === 1) return 0;
  const w = variantWeights(count, intensity);
  if (lastIndex >= 0 && lastIndex < count) w[lastIndex] = 0;   // NO-REPEAT-last
  let total = 0;
  for (let k = 0; k < count; k++) total += w[k];
  if (total <= 0) return (lastIndex + 1) % count;
  let roll = clamp01(rand) * total;
  for (let k = 0; k < count; k++) { roll -= w[k]; if (roll <= 0) return k; }
  return count - 1;
}

/**
 * A stateful pool for one fire-kind. Variants are ordered subtle -> spectacular.
 *   pool.pick(intensity)         -> { index, name }
 *   pool.pick(intensity, 'name') -> forces that name (still records no-repeat)
 */
export function createVariantPool(names, rng = Math.random) {
  const list = (names || []).slice();
  let last = -1;
  return {
    names: list,
    get last() { return last; },
    pick(intensity, force) {
      if (!list.length) return { index: -1, name: null };
      if (force != null) {
        const i = list.indexOf(force);
        if (i >= 0) { last = i; return { index: i, name: list[i] }; }
      }
      const idx = pickVariantIndex(list.length, last, intensity, rng());
      last = idx;
      return { index: idx, name: list[idx] };
    },
    reset() { last = -1; },
  };
}

/** The canonical wash / garnish names, gentle -> "ulterior reward". */
export const GARNISH_KINDS = Object.freeze(['pink', 'sublim', 'drain', 'spiral']);

/** Floors for the always-garnish rotation (jackpots are exempt). */
export const GARNISH_MIN_INTENSITY = 0.2;
export const GARNISH_MIN_HEAT = 0.1;

/**
 * The shuffled bag (ALWAYS GARNISH, ALWAYS DIFFERENT), ported verbatim from
 * intake. draw() pops the next name; an empty bag reshuffles with no repeat
 * across the boundary, so 8 straight draws hit all four kinds twice. draw()
 * takes an `avail` subset (the live path drops 'spiral' when no spiral asset
 * resolved) — unavailable names are skipped AND stay consumed. force(names) is
 * the big-moment path. Pure when `rng` is supplied.
 */
export function createGarnishBag(rng = Math.random, kinds = GARNISH_KINDS) {
  const all = kinds.slice();
  let bag = [];
  let last = null;
  const roll = (n) => Math.min(n - 1, Math.floor(clamp01(rng()) * n));
  function refill(avail) {
    bag = avail.slice();
    for (let k = bag.length - 1; k > 0; k--) {
      const j = roll(k + 1);
      const t = bag[k]; bag[k] = bag[j]; bag[j] = t;
    }
    if (bag.length > 1 && bag[bag.length - 1] === last) {
      const t = bag[bag.length - 1]; bag[bag.length - 1] = bag[0]; bag[0] = t;
    }
  }
  function take(name) {
    const idx = bag.indexOf(name);
    if (idx >= 0) bag.splice(idx, 1);
    last = name;
    return name;
  }
  function draw(avail = all) {
    const ok = all.filter((n) => avail.includes(n));
    if (!ok.length) return null;
    for (let guard = 0; guard < 2; guard++) {
      while (bag.length) {
        const n = bag.pop();
        if (ok.includes(n)) { last = n; return n; }
      }
      refill(ok);
    }
    return null;
  }
  function force(names) {
    const ok = all.filter((n) => names.includes(n));
    if (!ok.length) return null;
    if (!bag.length) refill(all);
    const inBag = ok.filter((n) => bag.includes(n));
    let pool = inBag.length ? inBag : ok;
    if (pool.length > 1 && pool.includes(last)) pool = pool.filter((n) => n !== last);
    return take(pool[roll(pool.length)]);
  }
  return { draw, force, take, get last() { return last; } };
}

/* ---- the per-kind variant name tables (3-5 looks each, CSS-built) --------- */
export const VARIANTS = Object.freeze({
  glitch_swap:  Object.freeze(['crossfade', 'rgbsplit', 'vhsroll', 'datamosh']),
  sub_flash:    Object.freeze(['whisper', 'centre', 'scatter', 'stamp']),
  flash_burst:  Object.freeze(['single', 'scatter', 'double', 'hydra']),
  gif_burst:    Object.freeze(['pop', 'fountain', 'ring', 'spill']),
  wash:         Object.freeze(['pink', 'spiral', 'drain', 'sublim']),
  bubble_field: Object.freeze(['drift', 'rise', 'swarm']),
  row_drift:    Object.freeze(['sway', 'slide', 'shear']),
  gif_rain:     Object.freeze(['light', 'steady', 'downpour']),
  ambient_field: Object.freeze(['motes', 'confetti', 'petals', 'embers', 'specks']),
  crt:          Object.freeze(['scanline', 'chroma', 'bloom']),
  stamp:        Object.freeze(['press', 'thud', 'gild']),
});

export default { createVariantPool, createGarnishBag, VARIANTS };
