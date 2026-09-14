/* ============================================================================
 * rng.js - seeded randomness for the Remix Room.
 *
 * Everything the engine rolls (spiral arms, glitch band positions, shuffle
 * permutations, surprise-me picks) comes from here, so a remix code always
 * replays the same dice. Pure: no DOM, no globals, node-testable.
 *
 * mulberry32 is a 32-bit state PRNG: fast, tiny, and good enough that two
 * nearby seeds do not produce visibly related streams.
 * ==========================================================================*/

/** Mix a seed with a short label into a fresh uint32, so one project seed can
 *  fan out into independent streams ('spiral', 'glitch:blockId', ...). */
export function mixSeed(seed, label = '') {
  let h = (seed >>> 0) ^ 0x9e3779b9;
  for (let i = 0; i < label.length; i++) {
    h = Math.imul(h ^ label.charCodeAt(i), 0x85ebca6b);
    h = (h << 13) | (h >>> 19);
  }
  h = Math.imul(h ^ (h >>> 16), 0x2545f491);
  return (h ^ (h >>> 15)) >>> 0;
}

/** mulberry32. Returns a function producing floats in [0,1). */
export function makeRng(seed) {
  let a = (seed >>> 0) || 1;
  const next = () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  return next;
}

/** A small helper object around one stream. All methods are deterministic. */
export function rngFrom(seed, label = '') {
  const next = makeRng(mixSeed(seed, label));
  return {
    next,
    /** float in [lo, hi) */
    range: (lo, hi) => lo + next() * (hi - lo),
    /** integer in [lo, hi] inclusive */
    int: (lo, hi) => lo + Math.floor(next() * (hi - lo + 1)),
    /** one item from a non-empty array */
    pick: (arr) => arr[Math.floor(next() * arr.length) % arr.length],
    /** true with probability p */
    chance: (p) => next() < p,
    /** a new array, Fisher-Yates, original untouched */
    shuffle: (arr) => {
      const out = arr.slice();
      for (let i = out.length - 1; i > 0; i--) {
        const j = Math.floor(next() * (i + 1));
        const t = out[i]; out[i] = out[j]; out[j] = t;
      }
      return out;
    },
    /** k distinct indices from 0..n-1, sorted ascending */
    sample: (n, k) => {
      const idx = [];
      for (let i = 0; i < n; i++) idx.push(i);
      for (let i = idx.length - 1; i > 0; i--) {
        const j = Math.floor(next() * (i + 1));
        const t = idx[i]; idx[i] = idx[j]; idx[j] = t;
      }
      return idx.slice(0, Math.max(0, Math.min(k, n))).sort((a, b) => a - b);
    },
  };
}

/* Stateless helpers: same seed + same index always gives the same value.
 * Used inside per-frame effect code where keeping a stream alive is awkward. */

/** Deterministic float in [0,1) from a seed and any number of integer coords. */
export function hash01(seed, ...coords) {
  let h = seed >>> 0;
  for (let i = 0; i < coords.length; i++) {
    h = Math.imul(h ^ (coords[i] | 0), 0x27d4eb2d);
    h = (h << 15) | (h >>> 17);
  }
  h = Math.imul(h ^ (h >>> 13), 0x85ebca6b);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/** Smooth 1D value noise in [0,1), period-free, cheap. */
export function noise1(seed, x) {
  const i = Math.floor(x);
  const f = x - i;
  const u = f * f * (3 - 2 * f);
  const a = hash01(seed, i);
  const b = hash01(seed, i + 1);
  return a + (b - a) * u;
}

/** Sum of two octaves of noise1, still in [0,1). */
export function fbm1(seed, x) {
  return noise1(seed, x) * 0.65 + noise1(seed ^ 0x5bf03635, x * 2.17) * 0.35;
}
