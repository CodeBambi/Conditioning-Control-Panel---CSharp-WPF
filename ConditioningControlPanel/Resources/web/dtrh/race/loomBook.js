/* ============================================================================
 * race/loomBook.js - THE RACE'S OWN SPIRAL BOOK.
 *
 * The owner's law (2026-08-25, quoted in arcademy/engine/loomWash.js): "on ALL
 * the games the spirals should be generated with the Loom." Racing Thoughts drew
 * seven stock gifs out of assets/bubbles/effects/spirals/ and nothing else. This
 * file is where a race spiral comes from now: schema-v2 Loom params, minted off
 * the RUN'S OWN SEED, wearing THE ROOM'S OWN COLOURS.
 *
 *   createLoomBook({ seed }) -> { draw({room, word}), reseed(seed), count, seed }
 *
 * No DOM, no WebGL, no window - race/loomSpiralFx.js is what DRAWS one of
 * these; this file only decides what a spiral IS. It reads its palettes out of
 * race/rooms.js, which imports three.js, so a node caller needs the same
 * `three` resolve hook race/smoke/slope-check.mjs installs (the smoke does).
 *
 * ---------------------------------------------------------------------------
 * THE PALETTE IS THE ROOM'S, AND IT IS READ, NOT WRITTEN TWICE.
 * `paletteFor(roomId)` takes the threads straight off race/rooms.js: the kerb
 * `edge`, the prop tint, the marquee `banner` and any `propAlt`, deduped and in
 * that order, over a ground made from the room's own `fog`. So a spiral in the
 * Tea Garden is cream/rose/sage over its green haze and the Fool's Casino is
 * gold over crimson-black - they can never be the same picture, and adding a
 * ninth room needs no edit here. A room whose colours collapse to one (they do
 * not today) is topped up with the house pink so a band always has something
 * to band against.
 *
 * THE CHARACTER TABLE (`ROOM_CHARACTER`) is the one hand-tuned half: which of
 * the six Loom styles a room is allowed, how hot its glow runs, whether the
 * field breathes or goes liquid, and how fast it spins. Two styles per room,
 * both of them hypnotic reads at wash alpha - `ribbon` and `petal` only where
 * a room already looks like that (the Undertow's kelp, the Toybox's confetti).
 *
 * CONSTRAINED, NOT RANDOM. loomField's own `randomParams2` rolls the whole
 * knob board and can hand back a twelve-arm strobe. The book is narrower on
 * purpose: 2-6 arms, 1.5-4 turns, a duty that keeps the dark as wide as the
 * light, `hueCycles: 0` always (the ROOM owns the colour, so nothing may rotate
 * it away), and a second counter-rotating layer only a third of the time.
 *
 * THE ROAD SAYS ITS OWN WORD. When a draw is handed the phrase the voice just
 * said and it fits the Loom's 12-character mantra, the spiral wears it as its
 * centrepiece - so the picture says what the voice said. No word, and the
 * centre is a plain dot or nothing.
 *
 * DETERMINISM (the same promise race/spine.js makes). Every draw is
 * `makeRng(seed ^ room ^ n)` - the run seed, the room, and how many spirals
 * this run has already drawn. Replay a seed and the same rooms deal the same
 * weaves in the same order. The WORD is not in the seed on purpose: a bubble
 * wearing a different phrase must not shift the whole book under it.
 *
 * THE ID is 'loom:' + FNV-1a over a stable stringify of the normalized params,
 * the same shape the Arcademy's ledger uses, so the same params always name the
 * same spiral. race/smoke/loom-spiral-check.mjs holds it.
 * ==========================================================================*/

import { normalizeParams2, defaultParams2 } from '../shared/loomField.js';
import { ROOMS, roomById } from './rooms.js';
import { makeRng } from './consts.js';

/** The house pink, for a room too monochrome to band against itself. */
const HOUSE_PINK = '#ff69b4';
/** A gif the floor can paint when WebGL is gone (loomSpiralFx hands this back). */
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const snap = (v, step) => Math.round(v / step) * step;
const hex6 = (n) => '#' + ((n >>> 0) & 0xffffff).toString(16).padStart(6, '0');

/** Mix a hex toward black by `k` (0 = untouched, 1 = black). The ground only. */
function darken(h, k) {
  const n = parseInt(String(h).replace('#', ''), 16) || 0;
  const f = (v) => Math.max(0, Math.min(255, Math.round(v * (1 - k))));
  return '#' + [f((n >> 16) & 255), f((n >> 8) & 255), f(n & 255)]
    .map((v) => v.toString(16).padStart(2, '0')).join('');
}

/**
 * The hand-tuned half, per room. `styles` are the Loom styles this room may
 * weave in; `glow`/`pulse`/`wobble` are [min,max] ranges (a 0 max means the
 * room never does that at all); `speed` is the Loom's 1-5 spin table.
 */
export const ROOM_CHARACTER = Object.freeze({
  // nothing here fights you: a slow open weave, a soft halo, no distortion
  teagarden:  { styles: ['log', 'arch'],       glow: [0.20, 0.45], pulse: [0.04, 0.09], wobble: [0, 0],       speed: [2, 3], arms: [2, 5] },
  // the floor bounces: petals and a fat pulse, the confetti room's own bounce
  toybox:     { styles: ['petal', 'arch'],     glow: [0.35, 0.70], pulse: [0.08, 0.14], wobble: [0, 0],       speed: [3, 4], arms: [3, 6] },
  // the wheel always pays: gold growth, the hottest glow, the fastest spin
  casino:     { styles: ['golden', 'log'],     glow: [0.40, 0.75], pulse: [0.06, 0.12], wobble: [0, 0],       speed: [3, 5], arms: [2, 5] },
  // the lane drifts: the two liquid styles, and the only room that really wobbles
  undertow:   { styles: ['ribbon', 'tunnel'],  glow: [0.25, 0.50], pulse: [0.06, 0.11], wobble: [0.10, 0.20], speed: [2, 3], arms: [2, 4] },
  // the picture flips: the bore, a cold glint, a little liquid
  mirrors:    { styles: ['tunnel', 'log'],     glow: [0.30, 0.60], pulse: [0.04, 0.08], wobble: [0.06, 0.13], speed: [3, 4], arms: [3, 6] },
  // the spiral pins itself here: the deepest glow and the widest breath in the game
  chapel:     { styles: ['log', 'golden'],     glow: [0.45, 0.80], pulse: [0.09, 0.15], wobble: [0, 0],       speed: [2, 4], arms: [2, 5] },
  // the only pink left is the treats: a dim, tight, joyless weave
  greyward:   { styles: ['arch', 'log'],       glow: [0.12, 0.32], pulse: [0.03, 0.07], wobble: [0, 0],       speed: [2, 3], arms: [3, 6] },
  // the run remembers: gold leaf, a heavy pulse, a touch of shimmer
  coronation: { styles: ['golden', 'petal'],   glow: [0.40, 0.80], pulse: [0.08, 0.14], wobble: [0.04, 0.10], speed: [3, 5], arms: [2, 5] },
});
/** What a room the table never named (or no room at all) weaves. */
export const DEFAULT_CHARACTER = Object.freeze({
  styles: ['log', 'arch'], glow: [0.25, 0.55], pulse: [0.05, 0.10], wobble: [0, 0], speed: [2, 4], arms: [2, 5],
});

/** The character row for a room id, never null. */
export function characterFor(roomId) {
  return ROOM_CHARACTER[String(roomId || '')] || DEFAULT_CHARACTER;
}

/**
 * THE ROOM'S OWN COLOURS, as a Loom palette: `{ threads, ground, outer }`.
 * Threads come off race/rooms.js in kerb -> prop -> marquee -> alt order,
 * deduped; the ground is the room's haze, and `outer` is that haze taken most
 * of the way to black so a radial ground has somewhere to fall off to.
 */
export function paletteFor(roomId) {
  const room = roomById(roomId) || null;
  const c = (room && room.colors) || null;
  const raw = c
    ? [c.edge, c.prop, c.banner, ...(Array.isArray(room.propAlt) ? room.propAlt : [])]
    : [];
  const threads = [];
  for (const n of raw) {
    const h = hex6(n);
    if (!threads.includes(h)) threads.push(h);
  }
  if (threads.length < 2) threads.push(HOUSE_PINK);
  const fog = c ? hex6(c.fog) : '#12061a';
  return { threads, ground: darken(fog, 0.35), outer: darken(fog, 0.8) };
}

/** Every room has one: race/smoke/loom-spiral-check.mjs walks this list. */
export const BOOK_ROOMS = ROOMS.map((r) => r.id);

const pickOf = (r, arr) => arr[Math.min(arr.length - 1, Math.floor(r() * arr.length))];
const between = (r, [lo, hi]) => lo + r() * (hi - lo);
const intBetween = (r, [lo, hi]) => lo + Math.floor(r() * (hi - lo + 1));

/** The Loom's mantra field is 12 characters; a longer phrase is not a centrepiece. */
export const MANTRA_MAX = 12;
/** Can the road's phrase ride the middle of the spiral? Lowercase, house style. */
export function mantraOf(word) {
  const s = String(word || '').trim().toLowerCase().replace(/\s+/g, ' ');
  return s && s.length <= MANTRA_MAX ? s : '';
}

/**
 * ONE SPIRAL, woven for a room off a die.
 * @param {Function} rng  a 0..1 stream (race/consts.js makeRng); REQUIRED for
 *   determinism - a missing die falls back to Math.random and is for rigs only.
 * @param {Object} [o] { room?: roomId, word?: the road's phrase }
 * @returns normalized schema-v2 params (plain JSON, loomField-ready)
 */
export function bookParams(rng, o = {}) {
  const r = typeof rng === 'function' ? rng : Math.random;
  const ch = characterFor(o.room);
  const pal = paletteFor(o.room);
  const d = defaultParams2();

  // THE WEAVE. Two threads is the read the owner asked for ("two-colour bands
  // from the room palette"); a third joins one draw in four where the room has
  // one, so a long run in one room does not settle into a single picture.
  const threads = pal.threads.slice();
  const nThreads = threads.length > 2 && r() < 0.25 ? 3 : 2;
  // rotate the room's own order rather than shuffling it: the kerb colour leads
  // most spirals, which is what makes a room's book read as one room's book
  const lead = Math.floor(r() * threads.length);
  const rolled = threads.slice(lead).concat(threads.slice(0, lead));

  d.format = 'square';
  d.speed = intBetween(r, ch.speed);
  d.bg = { kind: 'radial', color: pal.ground, outer: pal.outer };
  d.layer.arms = intBetween(r, ch.arms);
  d.layer.turns = clamp(snap(1.5 + r() * 2.5, 0.25), 0.5, 6);
  d.layer.duty = clamp(snap(0.35 + r() * 0.25, 0.05), 0.2, 0.8);
  d.layer.style = pickOf(r, ch.styles);
  d.layer.direction = r() < 0.5 ? 1 : -1;
  d.layer.colors = rolled.slice(0, nThreads);
  d.layer.bandMode = r() < 0.4 ? 'gradient' : 'hard';
  d.layer.speedMul = 1;

  // THE SECOND WEAVE, a third of the time, always turning the other way and
  // always in the room's remaining colour: a counter-rotation reads as depth,
  // two full palettes read as noise.
  d.layer2.enabled = r() < 0.34;
  d.layer2.arms = intBetween(r, [2, 5]);
  d.layer2.turns = clamp(snap(0.75 + r() * 1.75, 0.25), 0.5, 6);
  d.layer2.style = d.layer.style;
  d.layer2.direction = -d.layer.direction;
  d.layer2.colors = [rolled[(nThreads) % rolled.length] || rolled[0]];
  d.layer2.duty = clamp(snap(0.3 + r() * 0.2, 0.05), 0.2, 0.8);

  d.glow = between(r, ch.glow);
  d.pulse = { amp: between(r, ch.pulse), cycles: 1 + (r() < 0.3 ? 1 : 0) };
  d.wobble = ch.wobble[1] > 0
    ? { amp: between(r, ch.wobble), freq: intBetween(r, [2, 4]), cycles: 1 }
    : { amp: 0, freq: 2, cycles: 1 };
  // NEVER. The room owns the colour; a hue rotation would walk the spiral out
  // of the room it was woven for, halfway through its own loop.
  d.hueCycles = 0;

  // THE CENTRE. The road's own phrase where it fits, else a quiet dot, else
  // nothing at all - the field is the effect and the middle is not a logo.
  const mantra = mantraOf(o.word);
  if (mantra) {
    d.centerpiece = { kind: 'mantra', color: rolled[0], sizeFrac: 0.22, text: mantra, flashCycles: 1 + (r() < 0.5 ? 1 : 0) };
  } else if (r() < 0.3) {
    d.centerpiece = { kind: 'dot', color: rolled[0], sizeFrac: 0.08 + r() * 0.05, text: '', flashCycles: 0 };
  }
  return normalizeParams2(d);
}

/* ----------------------------------------------------------------------------
 * THE ID (the Arcademy's shape, so a spiral has one name everywhere)
 * -------------------------------------------------------------------------- */

/** Deterministic stringify: keys sorted at every depth, arrays in order. */
export function stableStringify(v) {
  if (v === null || v === undefined) return 'null';
  const t = typeof v;
  if (t === 'number') return Number.isFinite(v) ? String(v) : 'null';
  if (t === 'boolean') return v ? 'true' : 'false';
  if (t === 'string') return JSON.stringify(v);
  if (Array.isArray(v)) return '[' + v.map(stableStringify).join(',') + ']';
  if (t === 'object') {
    return '{' + Object.keys(v).sort().map((k) => JSON.stringify(k) + ':' + stableStringify(v[k])).join(',') + '}';
  }
  return 'null';
}

/** FNV-1a 32-bit over a string, 8 lowercase hex chars. */
function fnv1aHex(s) {
  let h = 2166136261 >>> 0;
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  return ('0000000' + ((h >>> 0).toString(16))).slice(-8);
}
/** The same params always name the same spiral: 'loom:xxxxxxxx'. */
export function loomId(params) { return 'loom:' + fnv1aHex(stableStringify(normalizeParams2(params))); }
export const LOOM_ID_RE = /^loom:[0-9a-f]{8}$/;

/** The room's name folded into 32 bits, so a room's stream is its own. */
function roomSeed(roomId) {
  let h = 2166136261 >>> 0;
  const s = String(roomId || 'none');
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  return h >>> 0;
}

/**
 * THE BOOK for one run. `draw` is what engine/loomSpirals.js calls (through the
 * `setLoomBook` seam) every time a spiral pop needs a picture.
 *
 * @param {Object} o { seed: the run seed, room?: () => roomId, word?: () => phrase }
 * @returns {{ draw: Function, reseed: Function, count: () => number, seed: () => number }}
 */
export function createLoomBook({ seed = 1, room = null, word = null } = {}) {
  let runSeed = seed >>> 0;
  let n = 0;
  /**
   * One spiral. `o.room` / `o.word` win over the run's own live getters, so a
   * smoke can ask for a named room without a world under it.
   * @returns {{ loom: true, params: Object, id: string, room: string, word: string }}
   */
  function draw(o = {}) {
    const roomId = o.room != null ? o.room : (typeof room === 'function' ? room() : '');
    const phrase = o.word != null ? o.word : (typeof word === 'function' ? word() : '');
    const rng = makeRng((runSeed ^ roomSeed(roomId) ^ Math.imul(n + 1, 0x9e3779b9)) >>> 0);
    n++;
    const params = bookParams(rng, { room: roomId, word: phrase });
    return { loom: true, params, id: loomId(params), room: String(roomId || ''), word: mantraOf(phrase) };
  }
  function reseed(s) { runSeed = (s >>> 0) || 1; n = 0; }
  return { draw, reseed, count: () => n, seed: () => runSeed };
}

export default { createLoomBook, bookParams, paletteFor, characterFor, loomId, BOOK_ROOMS };

// self-check: node race/smoke/loom-spiral-check.mjs (the node half walks every room).
