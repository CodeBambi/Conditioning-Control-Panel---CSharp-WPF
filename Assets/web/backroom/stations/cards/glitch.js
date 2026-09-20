/* ============================================================================
 * stations/cards/glitch.js - THE SLIP (owner decision, 2026-09-17). Pure.
 *
 * About one hand in fifty, one card already face up on the felt slips: its rank
 * and suit glyph tear for a moment and settle as a DIFFERENT card. Both
 * renderers resolve a card's picture from its code (`deck.keyFor`), so the GIF
 * turns over with the face and nothing extra has to be dealt.
 *
 * PRESENTATION ONLY, and the rule is load-bearing. The server owns the hand
 * (hand.js: "Never the rules: legal moves, the hint, totals and pays all come
 * from the server"), so a slip is a COSTUME over a code the server settled.
 * It is applied at paint time and never written back to the card, so the felt
 * total, the legal moves and the pay keep reading the server's own codes. A
 * client that could really change a card would be a client that could cheat.
 *
 * Because the total is drawn from the true codes, a slip that changed a card's
 * VALUE would print a face that disagrees with the total under it. So the draw
 * prefers a replacement of the same blackjack value that still moves the
 * picture - the ten family, T J Q K, which share a value but not a deck slot -
 * and only falls back to a free rank when the family cannot serve. Pass
 * `free: false` to forbid the fallback and keep the felt provably honest.
 * ==========================================================================*/

import { validCode, cardValue } from './hand.js';
import { fnv1a, valueIndex } from '../../shared/hypno/media.js';

/** One hand in fifty, rolled per hand (never a counter: a streak of fifty quiet hands is allowed). */
export const SLIP_ODDS = 1 / 50;
/** The tear: glyphs jitter and the face crosses over at the midpoint. */
export const SLIP_MS = 420;

const RANKS = Object.freeze(['A', '2', '3', '4', '5', '6', '7', '8', '9', 'T', 'J', 'Q', 'K']);
const SUITS = Object.freeze(['s', 'h', 'd', 'c']);

/** A hand-seeded stream, so a hand always slips the same way and the game's own rng is never touched. */
function stream(seed) {
  let h = fnv1a(String(seed)) || 1;
  return () => { h ^= h << 13; h >>>= 0; h ^= h >> 17; h ^= h << 5; h >>>= 0; return h / 4294967296; };
}

/** Codes that share `code`'s blackjack value but not its picture (deck slot). Empty for A and 2..9. */
export function twins(code) {
  if (!validCode(code)) return [];
  const v = cardValue(code), slot = valueIndex(code[0]);
  const out = [];
  for (const r of RANKS) {
    if (cardValue(r + 's') !== v || valueIndex(r) === slot) continue;
    for (const s of SUITS) out.push(r + s);
  }
  return out;
}

/** Any code that is not `code`. */
function others(code) {
  const out = [];
  for (const r of RANKS) for (const s of SUITS) { const c = r + s; if (c !== code) out.push(c); }
  return out;
}

/**
 * Roll a hand's slip. `cards` is [{ id, code }] - only face-up, real codes can slip.
 * Returns { id, from, to } or null. `odds` and `free` are for the tests and the owner's dial.
 */
export function planSlip(seed, cards, { odds = SLIP_ODDS, free = true } = {}) {
  const pool = (Array.isArray(cards) ? cards : []).filter((c) => c && validCode(c.code));
  if (!pool.length) return null;
  const rnd = stream(seed);
  if (rnd() >= odds) return null;
  const victim = pool[Math.min(pool.length - 1, Math.floor(rnd() * pool.length))];
  const kin = twins(victim.code);
  const from = kin.length ? kin : (free ? others(victim.code) : []);
  if (!from.length) return null;
  return { id: victim.id, from: victim.code, to: from[Math.min(from.length - 1, Math.floor(rnd() * from.length))] };
}

/** 0 before the tear, 1 once it has settled. */
export function slipPhase(age) {
  if (!(age >= 0)) return 0;
  return age >= SLIP_MS ? 1 : age / SLIP_MS;
}

/**
 * What a card should WEAR this frame. `slip` is planSlip's answer (or null), `t0` when the tear began.
 * The face crosses at the midpoint so the glyphs tear as the old card and settle as the new one.
 */
export function wornCode(card, code, slip, t0, now) {
  if (!slip || !card || card.id !== slip.id) return code;
  return slipPhase(now - t0) < 0.5 ? code : slip.to;
}

/**
 * The tear itself: how hard the rank and suit glyphs are thrown this frame, 0 at both ends and
 * hardest at the crossover. `k` is the station's strength (0.5 on Calm or reduced motion), and a
 * still frame never tears - it simply shows the card it settled on.
 */
export function slipShake(card, slip, t0, now, k = 1, still = false) {
  if (!slip || !card || card.id !== slip.id || still) return 0;
  const p = slipPhase(now - t0);
  return p <= 0 || p >= 1 ? 0 : Math.sin(p * Math.PI) ** 2 * Math.max(0, Math.min(1, k));
}

/** Per-glyph offset in px at a given shake, deterministic in the frame so the two glyphs tear apart. */
export const shakeOffset = (shake, i, now) => shake * 3.2 * Math.sin((now / 26) + i * 2.1);
