/* ============================================================================
 * engine/loom/seeded.js - THE SEEDED LOOM: the Arcademy's own "surprise me".
 *
 * Vendored-from provenance: a FAITHFUL FORK of dtrh/shared/loomField.js
 * randomParams2 @ 2026-08-25, with every Math.random() swapped for an injected
 * rng (core/rng.js makeRng stream). Same branch structure, same knob ranges,
 * same normalizeParams2 exit - only the dice are ours. Call-count parity
 * ACROSS seeds is not promised (branches consume different roll counts, as the
 * original does); what is promised is Law V: the same seed always weaves the
 * same spiral. loomField.js itself is the vendored original - do not hand-edit
 * it, and fork changes land HERE.
 *
 * THE ID is the ledger's word for a generated spiral: 'loom:' + an 8-hex FNV-1a
 * over a STABLE stringify of the normalized params (keys sorted, numbers
 * canonical). Same params -> same id, forever - Instant Recall quizzes on it
 * ("which spiral did you just see"), so the hash may never depend on key
 * insertion order or platform.
 *
 * THE PALETTE LEAN. The dtrh dice roll over the full LOOM_SWATCHES; the
 * Arcademy leans house: a violet -> rose arc (the school's pink/lavender
 * accent pair, #FF69B4 / #B8A6E8 anchored) with an occasional off-arc teal
 * thread (~1 in 6) so the pool does not flatten into one wash of pink. Grounds
 * stay near-black plum like the default - this is the hypno spiral, and it
 * lives UNDER the game at wash alpha, so restraint is the whole taste rule.
 * ==========================================================================*/

import { normalizeParams2, defaultParams2, LOOM_STYLES } from './loomField.js';

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const snap = (v, step) => Math.round(v / step) * step;

/** The house arc: violet -> rose, ordered for no reason a player can see. */
export const ARCADEMY_SWATCHES = Object.freeze([
  '#ff69b4', '#e56cc0', '#ff8fcf', '#c76bff', '#8a5cff', '#b8a6e8', '#ff5aa0',
]);
/** The off-arc accent thread (occasional, never a whole palette). */
const OFF_ARC_TEAL = '#00e5ff';
/** Dark grounds the arc reads well over (index 0 = loomField's own default). */
const GROUNDS = Object.freeze(['#14060f', '#120a1e', '#0d0716', '#160812']);

/** seeded pickFrom - the dtrh helper with the die injected. */
function pickFrom(r, arr) { return arr[Math.floor(r() * arr.length)]; }

/** seeded threads() - a Fisher-Yates instead of dtrh's sort(() => random-0.5)
 *  (that idiom is engine-dependent even with a seeded die; the shuffle is not). */
function threads(r, count, palette) {
  const pool = palette.slice();
  for (let i = pool.length - 1; i > 0; i--) {
    const j = Math.min(i, Math.floor(r() * (i + 1)));
    const t = pool[i]; pool[i] = pool[j]; pool[j] = t;
  }
  return pool.slice(0, count);
}

/**
 * A seeded, tasteful creation - randomParams2 with the dice handed in.
 * @param {Function} rng  a 0..1 stream (core/rng.js makeRng); REQUIRED for
 *   determinism - a missing rng falls back to Math.random and is only for rigs.
 * @param {Object} [opts]
 *   palette?: string[]   thread swatches (default: the house arc + rare teal)
 *   centerpiece?: bool   false = never mint a centerpiece (the WASH path sets
 *     this so the live shader layer and the composeFrame thumbnails agree -
 *     the wash renderer draws no centerpiece)
 * @returns normalized v2 params (normalizeParams2'd, plain JSON-able object)
 */
export function seededParams2(rng, opts = {}) {
  const r = typeof rng === 'function' ? rng : Math.random;
  let palette = Array.isArray(opts.palette) && opts.palette.length
    ? opts.palette.slice() : ARCADEMY_SWATCHES.slice();
  // the off-arc thread: ~1 in 6 palettes carry the teal at all
  if (!Array.isArray(opts.palette) && r() < 0.17) palette.push(OFF_ARC_TEAL);

  const d = defaultParams2();
  d.layer.arms = 1 + Math.floor(r() * 8);
  d.layer.turns = clamp(snap(0.5 + r() * 3.5, 0.25), 0.5, 6);
  d.layer.duty = clamp(snap(0.3 + r() * 0.4, 0.05), 0.2, 0.8);
  d.layer.style = pickFrom(r, LOOM_STYLES);
  d.layer.direction = r() < 0.5 ? 1 : -1;
  d.layer.colors = threads(r, 1 + Math.floor(r() * 3), palette);
  d.layer.bandMode = r() < 0.35 ? 'gradient' : 'hard';
  d.layer2.enabled = r() < 0.35;
  d.layer2.arms = 2 + Math.floor(r() * 5);
  d.layer2.turns = clamp(snap(0.5 + r() * 2.5, 0.25), 0.5, 6);
  d.layer2.colors = threads(r, 1, palette);
  d.layer2.direction = -d.layer.direction;
  d.speed = 2 + Math.floor(r() * 3);
  d.bg.color = pickFrom(r, GROUNDS);
  d.glow = r() < 0.4 ? 0.3 + r() * 0.5 : 0;
  if (r() < 0.35) d.pulse = { amp: 0.05 + r() * 0.12, cycles: 1 + Math.floor(r() * 2) };
  if (r() < 0.3) d.wobble = { amp: 0.08 + r() * 0.15, freq: 2 + Math.floor(r() * 3), cycles: 1 };
  if (r() < 0.2) d.hueCycles = 1;
  if (opts.centerpiece !== false && r() < 0.25) {
    d.centerpiece.kind = pickFrom(r, ['dot', 'star', 'cross', 'x']);
    d.centerpiece.color = pickFrom(r, palette);
  }
  return normalizeParams2(d);
}

/* ----------------------------------------------------------------------------
 * THE STABLE ID
 * -------------------------------------------------------------------------- */

/** Deterministic stringify: keys sorted at every depth, numbers via String()
 *  (params are normalized snaps of small decimals - stable in every JS engine),
 *  no whitespace. Arrays keep order (order is meaning for colors). */
export function stableStringify(v) {
  if (v === null || v === undefined) return 'null';
  const t = typeof v;
  if (t === 'number') return Number.isFinite(v) ? String(v) : 'null';
  if (t === 'boolean') return v ? 'true' : 'false';
  if (t === 'string') return JSON.stringify(v);
  if (Array.isArray(v)) return '[' + v.map(stableStringify).join(',') + ']';
  if (t === 'object') {
    const keys = Object.keys(v).sort();
    return '{' + keys.map((k) => JSON.stringify(k) + ':' + stableStringify(v[k])).join(',') + '}';
  }
  return 'null';
}

/** FNV-1a 32-bit over a string, as 8 lowercase hex chars. */
function fnv1aHex(s) {
  let h = 2166136261 >>> 0;
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  h = h >>> 0;
  return ('0000000' + h.toString(16)).slice(-8);
}

/**
 * The ledger id for a set of loom params: 'loom:xxxxxxxx'. Params are
 * re-normalized first, so any two objects that WEAVE the same spiral share the
 * id even if one arrived denormalized. Determinism is the contract - same
 * params, same id, in every session on every platform.
 */
export function loomId(params) {
  return 'loom:' + fnv1aHex(stableStringify(normalizeParams2(params)));
}

/** Is this string a generated-loom ledger id? */
export const LOOM_ID_RE = /^loom:[0-9a-f]{8}$/;

export default { seededParams2, loomId, stableStringify, ARCADEMY_SWATCHES, LOOM_ID_RE };
