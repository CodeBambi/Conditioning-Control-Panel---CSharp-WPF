/* ============================================================================
 * games/instant-recall/spirals.js - THE SPIRAL SET, and why a decoy is another
 * spiral rather than a filtered copy of the one you saw.
 *
 * The owner asked for "which spiral did you just see - three generated one,
 * similar to that one". This file is the honest reading of that, and the reason
 * it is a SET of real assets is measurable, not aesthetic:
 *
 *   Five of the seven bundled spirals are mirror- and/or rotation-symmetric
 *   (sp1/sp7 monochrome concentric rings, sp5/sp6 pink heart tunnels, sp3 a
 *   colour kaleidoscope). So `scaleX(-1)` and any `rotate()` render an option
 *   PIXEL-IDENTICAL to the truth, which breaks input honesty outright: two
 *   buttons, one answer. `hue-rotate` is a no-op on the two monochrome sets and
 *   `invert` on rings is a phase swap of the same loop. A recolour is honest for
 *   the coloured assets but makes "the naturally-coloured one" the tell after two
 *   classes - and recolouring the EMITTED 150vmax wash with a filter is a
 *   full-screen GPU pass per frame (arcademy CLAUDE.md trap 36).
 *
 * The one scheme that is honest for EVERY asset and carries no meta-tell: the
 * decoys are three other spirals the engine could equally have shown, drawn
 * KIN-FIRST so the near-misses are look-alikes (rings vs rings, hearts vs hearts,
 * Loom vs Loom) rather than obviously-other pictures. Recall, not elimination.
 *
 * THE GENERATED LOOM (2026-08-25, the owner's "all games weave" directive).
 * The class spiral can now be WOVEN live (engine/loom/), and its ledger id is
 * 'loom:xxxxxxxx' - a params hash, not a url. The doctrine above holds
 * unchanged and is worth restating for this family: the decoys for a woven
 * spiral are THREE GENUINELY DIFFERENT seeded weaves from the same generator
 * (`loomDecoyParams`) - real spirals the engine could equally have shown -
 * never a recolour/mirror of the truth (the generator's palette lean makes
 * every weave a look-alike already, which is exactly kin-first). Option
 * thumbnails render ONE frame of the params at ~96px (`loomThumbDataUrl`),
 * square, unspun - the same "asset AS SHIPPED" posture the gif previews keep.
 *
 * ---------------------------------------------------------------------------
 * LAWS. `buildSpiralSet` / `spiralDecoys` / `kinOf` / `loomRowsOf` /
 * `loomDecoyParams` are PURE and SEEDED (Law V) - no clock, no DOM, no
 * Math.random, no ledger. `preloadSpirals` and `loomThumbDataUrl` touch the
 * DOM (an Image / a canvas) and are best-effort: never awaited, failure is
 * invisible (null thumb = the option keeps its backing colour). This module
 * imports `../../core/rng.js` and the engine's VENDORED loom pair
 * (`engine/loom/seeded.js`, `engine/loom/loomField.js`) and nothing else - in
 * particular never `vigil.js`, so the RING SIZE is the caller's
 * (`PLAYTEST.SPIRAL_RING[tier]`) and the plan file stays the only owner of the
 * tier dials.
 * ==========================================================================*/

import { makeRng, makeTaggedRoll, shuffled } from '../../core/rng.js';
import { seededParams2, loomId, LOOM_ID_RE } from '../../engine/loom/seeded.js';
import { createFieldRenderer, composeFrame, drawFallbackFrame, normalizeParams2 } from '../../engine/loom/loomField.js';

/** Which bundled files LOOK like each other. Data, filed by eye (all seven
 *  viewed 2026-08-23): sp1/sp7 monochrome ring tunnels, sp2 a green/magenta
 *  ring tunnel, sp5/sp6 pink heart tunnels, sp3 a colour kaleidoscope, sp4 a
 *  rainbow pinwheel (arms, not rings - it keeps the kaleidoscope company). */
export const SPIRAL_KIN = Object.freeze({
  'sp1.gif': 'rings',
  'sp7.gif': 'rings',
  'sp2.webp': 'rings',
  'sp5.gif': 'hearts',
  'sp6.gif': 'hearts',
  'sp3.gif': 'kaleido',
  'sp4.webp': 'kaleido',
});

const LOOM_RE = /^https:\/\/ccp\.spirals\/loom_/;
const DEFAULT_RING = 2;
const DECOY_COUNT = 3;
const SET_MAX = 4;

/** The file name at the end of a url, query and hash stripped. */
function basenameOf(url) {
  const s = String(url == null ? '' : url);
  const cut = s.split(/[?#]/)[0];
  const bits = cut.split('/');
  return bits[bits.length - 1] || '';
}

/**
 * The look-alike family of a spiral url OR ledger id.
 * A player's own Loom spirals are their own kin - they share a generator, so
 * they look like each other and like nothing bundled. A GENERATED spiral's
 * 'loom:xxxxxxxx' id (engine/loom/seeded.js) is the same family for the same
 * reason: one generator, one look.
 * @param {string} url  a spiral url, or a 'loom:' ledger id
 * @returns {'rings'|'hearts'|'kaleido'|'loom'|'other'}
 */
export function kinOf(url) {
  const s = String(url == null ? '' : url);
  if (LOOM_RE.test(s) || LOOM_ID_RE.test(s)) return 'loom';
  const kin = SPIRAL_KIN[basenameOf(s)];
  return kin || 'other';
}

/** 0..1, clamped, never NaN - a bad roll must not fall off the end of a list. */
function unit(v) {
  const n = Number(v);
  if (!Number.isFinite(n)) return 0;
  return n < 0 ? 0 : n > 0.999999 ? 0.999999 : n;
}

/** `roll` may be a tagged roll (tag -> 0..1) or a plain rng; both answer to a
 *  call with a tag, so one adapter serves either. */
function streamOf(roll, tag) {
  if (typeof roll !== 'function') return null;
  return function () {
    let v;
    try { v = roll(tag); } catch (e) { v = NaN; }
    return unit(v);
  };
}

/** Normalise `ctx.spiralPool` into usable weighted rows, de-duplicated by url. */
function rowsOf(pool) {
  const out = [];
  const seen = new Set();
  const list = Array.isArray(pool) ? pool : [];
  for (const row of list) {
    if (!row) continue;
    const url = typeof row === 'string' ? row : row.url;
    if (typeof url !== 'string' || !url || seen.has(url)) continue;
    const w = Number(row && row.weight);
    seen.add(url);
    out.push({ url, weight: Number.isFinite(w) && w > 0 ? w : 1 });
  }
  return out;
}

/** One weighted pick over `rows` with a single 0..1 roll. */
function weightedPick(rows, r) {
  let total = 0;
  for (const row of rows) total += row.weight;
  if (!(total > 0)) return rows[0].url;
  let x = unit(r) * total;
  for (const row of rows) { x -= row.weight; if (x < 0) return row.url; }
  return rows[rows.length - 1].url;
}

const EMPTY_SET = Object.freeze({ set: Object.freeze([]), ring: Object.freeze([]), kin: Object.freeze({}) });

/**
 * THE CLASS'S SPIRAL SET: four urls the class may ever show or offer, drawn off
 * the class seed so a retake dives exactly the same water (Law V).
 *
 *   set[0]  the anchor, a WEIGHTED pick over the pool (the same byte-size
 *           weights the shell's own one-url pick walks, so the spiral a class
 *           leans on is the one it would have worn anyway)
 *   set[1..3]  KIN-FIRST: the anchor's look-alikes in seeded order, then
 *           everything else in seeded order, up to four urls in total
 *   ring    the prefix of `set` the emitter actually CYCLES between. The caller
 *           owns its size (tier dial); the rest of the set exists to be a decoy.
 *
 * Consequence worth stating: a tier-1 ring of 2 means the player has genuinely
 * only ever seen two of the four options, and the other two are honest wrong
 * answers rather than things that were never in the room.
 *
 * @param {Object} o { pool: [{url, weight}], seed: string, ringSize?: number }
 * @returns {{set: string[], ring: string[], kin: Object}} frozen
 */
export function buildSpiralSet(o = {}) {
  const rows = rowsOf(o && o.pool);
  if (!rows.length) return EMPTY_SET;

  const roll = makeTaggedRoll(String(o && o.seed == null ? '' : o.seed) + '|ir-spirals');
  const first = weightedPick(rows, roll('first'));
  const anchorKin = kinOf(first);

  const rest = rows.filter((r) => r.url !== first).map((r) => r.url);
  const same = rest.filter((u) => kinOf(u) === anchorKin);
  const other = rest.filter((u) => kinOf(u) !== anchorKin);
  const ordered = shuffled(same, () => roll('kin')).concat(shuffled(other, () => roll('rest')));

  const set = [first];
  for (const u of ordered) {
    if (set.length >= SET_MAX) break;
    set.push(u);
  }

  const wantRing = Number(o && o.ringSize);
  const size = Math.max(1, Math.min(set.length,
    Math.round(Number.isFinite(wantRing) ? wantRing : DEFAULT_RING)));

  const kin = {};
  for (const u of set) kin[u] = kinOf(u);

  return Object.freeze({
    set: Object.freeze(set.slice()),
    ring: Object.freeze(set.slice(0, size)),
    kin: Object.freeze(kin),
  });
}

/**
 * The three wrong options for "which spiral did you see last?" - every OTHER
 * url of the set, look-alikes first, in seeded order. Never the truth, never a
 * duplicate, and short rather than padded when the pool could not fill four.
 *
 * @param {string} truthUrl  what the ledger says actually played
 * @param {string[]|{set:string[]}} set  the class's spiral set
 * @param {Function} [roll]  the caller's seeded roll (tagged or plain)
 * @returns {string[]} length <= 3
 */
export function spiralDecoys(truthUrl, set, roll) {
  const list = Array.isArray(set) ? set : (set && Array.isArray(set.set) ? set.set : []);
  const truth = String(truthUrl == null ? '' : truthUrl);
  const seen = new Set([truth]);
  const rest = [];
  for (const u of list) {
    if (typeof u !== 'string' || !u || seen.has(u)) continue;
    seen.add(u);
    rest.push(u);
  }
  if (!rest.length) return [];

  const truthKin = kinOf(truth);
  const same = rest.filter((u) => kinOf(u) === truthKin);
  const other = rest.filter((u) => kinOf(u) !== truthKin);
  const kinStream = streamOf(roll, 'spiral-decoy-kin');
  const restStream = streamOf(roll, 'spiral-decoy-rest');
  const ordered = (kinStream ? shuffled(same, kinStream) : same.slice())
    .concat(restStream ? shuffled(other, restStream) : other.slice());
  return ordered.slice(0, DECOY_COUNT);
}

/**
 * Warm the decoders for a set of spirals so a preview tile is not a grey box on
 * the first card of the class. Best-effort by construction: no promise, no
 * await, no callback, no failure path - a spiral that never loads simply shows
 * the option's own backing colour, and the class is unaffected.
 * @param {string[]} urls
 * @returns {number} how many requests were actually started
 */
export function preloadSpirals(urls) {
  const list = Array.isArray(urls) ? urls : (urls && Array.isArray(urls.set) ? urls.set : []);
  let n = 0;
  for (const u of list) {
    if (typeof u !== 'string' || !u) continue;
    try {
      if (typeof Image !== 'function') break;          // headless / DOM double
      const img = new Image();
      try { img.decoding = 'async'; } catch (e) { /* not everywhere */ }
      img.src = u;
      n += 1;
    } catch (e) { /* a warm cache is a nicety, never a dependency */ }
  }
  return n;
}

/* ============================================================================
 * THE GENERATED-LOOM HELPERS (2026-08-25)
 * ==========================================================================*/

/**
 * The WOVEN rows of `ctx.spiralPool`, whole. The shell ships a generated loom
 * as `{loom:true, id, params, href, weight}` with deliberately NO `url` key,
 * so the string-normalising `rowsOf` above skips it - THIS is the read that
 * takes it. PURE; de-duplicated by id; a row without params is dropped.
 * @param {Array} pool  raw ctx.spiralPool
 * @returns {{id:string, params:Object, href:?string, weight:number}[]}
 */
export function loomRowsOf(pool) {
  const out = [];
  const seen = new Set();
  const list = Array.isArray(pool) ? pool : [];
  for (const row of list) {
    if (!row || row.loom !== true || !row.params || typeof row.params !== 'object') continue;
    let id;
    try {
      id = (typeof row.id === 'string' && LOOM_ID_RE.test(row.id)) ? row.id : loomId(row.params);
    } catch (e) { continue; }
    if (seen.has(id)) continue;
    seen.add(id);
    const w = Number(row.weight);
    out.push({
      id,
      params: row.params,
      href: typeof row.href === 'string' && row.href ? row.href : null,
      weight: Number.isFinite(w) && w > 0 ? w : 1,
    });
  }
  return out;
}

/**
 * THREE GENUINELY DIFFERENT woven decoys for a generated-spiral question -
 * fresh seeded params from the same generator, never a filtered copy of the
 * truth (the doctrine at the top of this file, applied to the loom family).
 * PURE AND SEEDED: attempt i rides makeRng(seed+'|ir-loom-decoy|'+i), and a
 * hash collision (with the truth or an earlier decoy) skips to the next
 * attempt deterministically - same seed, same decoys, forever.
 * @param {string} seed       the class seed (or any stable per-class string)
 * @param {number} [count]    how many (default 3, capped 6)
 * @param {string} [excludeId]  the truth's 'loom:' id - never dealt back
 * @returns {{id:string, params:Object}[]}
 */
export function loomDecoyParams(seed, count = 3, excludeId = '') {
  const want = Math.max(0, Math.min(6, Math.round(Number(count) || 0)));
  const out = [];
  const seen = new Set([String(excludeId == null ? '' : excludeId)]);
  const base = String(seed == null ? '' : seed) + '|ir-loom-decoy|';
  for (let i = 0; out.length < want && i < want * 8 + 8; i++) {
    let id; let params;
    try {
      params = seededParams2(makeRng(base + i), { centerpiece: false });
      id = loomId(params);
    } catch (e) { break; }
    if (seen.has(id)) continue;
    seen.add(id);
    out.push({ id, params });
  }
  return out;
}

/** The cached thumb renderer ({field}|false once WebGL refused). */
let _thumbField = null;

/**
 * ONE still frame of a woven spiral as a data url, for a quiz option's face -
 * ~96px, square, phase 0: unspun and uncoloured-by-us, exactly the "asset AS
 * SHIPPED" posture the gif previews keep (style.js). Synchronous-ish: one
 * offscreen render, no fetch. Best-effort by construction - `null` on any
 * failure (headless, no canvas, no 2D context) and the option simply keeps
 * its backing colour, same as a gif that never loaded. WebGL is preferred
 * (the exact wash pipeline); the vendored 2D fallback draws when it refuses,
 * which is also what a no-WebGL player's wash gif approximates.
 * @param {Object} params  loomField v2 params (normalized or not)
 * @param {number} [size]  face edge in px (default 96, clamped 32..256)
 * @returns {?string} a PNG data url, or null
 */
export function loomThumbDataUrl(params, size = 96) {
  try {
    if (typeof document === 'undefined' || !document.createElement) return null;
    const q = normalizeParams2(params);
    const s = Math.max(32, Math.min(256, Math.round(Number(size) || 96)));
    const out = document.createElement('canvas');
    out.width = s; out.height = s;
    const ctx2d = out.getContext('2d');
    if (!ctx2d) return null;
    if (_thumbField !== false && !_thumbField) {
      try {
        const c = document.createElement('canvas');
        c.width = s; c.height = s;
        _thumbField = createFieldRenderer(c) || false;
      } catch (e) { _thumbField = false; }
    }
    if (_thumbField) {
      if (_thumbField.canvas.width !== s) { _thumbField.canvas.width = s; _thumbField.canvas.height = s; }
      composeFrame(ctx2d, _thumbField, q, 0, s);
    } else {
      drawFallbackFrame(ctx2d, q, 0, s);
    }
    return out.toDataURL('image/png');
  } catch (e) { return null; }
}

export default {
  SPIRAL_KIN, kinOf, buildSpiralSet, spiralDecoys, preloadSpirals,
  loomRowsOf, loomDecoyParams, loomThumbDataUrl,
};
