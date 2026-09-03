/* ============================================================================
 * core/rng.js — the Arcademy's one seeded random source.
 *
 * Ported from intake/core/engine.js (mulberry32) + intake/core/contracts.js
 * (hash01). Two exports are law (BUILD-CONTRACT §2):
 *   makeRng(seedStr) -> () => 0..1     deterministic stream from a STRING seed
 *   hash01(str)      -> 0..1           deterministic hash of a string
 *
 * Everything that rolls in the Arcademy (timetable, engine variant pools,
 * garnish bag, set-piece gates, reward schedule) runs on one of these. Nothing
 * in the Arcademy calls Math.random() on a path that must replay — that was the
 * one determinism hole in Intake (`pickKind`) and we do not inherit it.
 * ==========================================================================*/

/** Deterministic 0..1 hash of a string (FNV-1a, verbatim from intake contracts.js). */
export function hash01(str) {
  let h = 2166136261 >>> 0;
  const s = String(str);
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  return (h >>> 0) / 4294967295;
}

/** mulberry32 over a 32-bit integer seed. */
function mulberry32(seedInt) {
  let s = seedInt >>> 0;
  return function () {
    s = (s + 0x6D2B79F5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * makeRng('2026-08-19|deja_vu') -> () => 0..1
 * The string is hashed to an int seed, so any seed shape (date, game key,
 * `${utcDateSeed}|${gameKey}|${tier}`) works and the same string always
 * replays the same stream.
 */
export function makeRng(seedStr) {
  const seedInt = Math.floor(hash01(seedStr == null ? '' : String(seedStr)) * 0xFFFFFFFF);
  return mulberry32(seedInt);
}

/**
 * A tag-namespaced roll stream over one seed: roll('burst') advances only the
 * 'burst' namespace, so adding a new roll never shifts an existing sequence
 * (the fix reward.js applies to its kind rolls). Additive helper.
 *
 * Each tag owns a mulberry32 stream seeded from hash01(seed|tag). The first
 * version hashed `seed|tag|n` per call and the trailing counter byte barely
 * avalanches through FNV-1a, so consecutive rolls of one tag clustered (~0.4%
 * near-equal pairs; the decks worked around it with per-tag mulberry32 - this
 * IS that fix, in core). Same contract: tags are independent, replay is exact.
 */
export function makeTaggedRoll(seedStr) {
  const seed = seedStr == null ? '' : String(seedStr);
  const streams = new Map();
  return function roll(tag) {
    const t = String(tag == null ? '' : tag);
    let s = streams.get(t);
    if (!s) {
      s = mulberry32(Math.floor(hash01(seed + '|' + t) * 0xFFFFFFFF));
      streams.set(t, s);
    }
    return s();
  };
}

/** Pick one entry with a supplied 0..1 roll. Returns undefined for an empty list. */
export function pick(list, rand) {
  if (!list || !list.length) return undefined;
  const r = rand == null ? 0 : (rand < 0 ? 0 : rand > 0.999999 ? 0.999999 : rand);
  return list[Math.floor(r * list.length)];
}

/** In-place Fisher-Yates on a COPY, driven by a seeded rng. */
export function shuffled(list, rng) {
  const a = (list || []).slice();
  const r = typeof rng === 'function' ? rng : Math.random;
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.min(i, Math.floor(r() * (i + 1)));
    const t = a[i]; a[i] = a[j]; a[j] = t;
  }
  return a;
}

export default makeRng;
