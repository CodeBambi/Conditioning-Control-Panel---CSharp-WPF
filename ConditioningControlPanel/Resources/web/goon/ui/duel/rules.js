/* ============================================================================
 * ui/duel/rules.js - the pure rules of a game night duel. No DOM, no clock.
 *
 * A duel is a thrown GAME CARD: both players play the same seeded Deep End
 * board for the duel length, each reports its own final board, and both sides
 * compute the same winner from the same two numbers.
 * ==========================================================================*/

import { saltSeed } from '../../core/rng.js';
import { DUEL_LENGTHS_SEC } from '../../core/contracts.js';

/** Points added to the winner's match score. Tie = nothing. */
export const DUEL_WIN_BONUS = 100;
/** The "incoming game" card before the board. */
export const DUEL_INTRO_MS = 2000;
/** How long we wait for their final score after ours. A silent peer counts as 0. */
export const DUEL_REPORT_GRACE_MS = 6000;
/** Game cards start dropping from this many finished matches (i.e. the second match). */
export const DUEL_MIN_FINISHED = 1;
/**
 * Drop weight, as an arsenal `cost` (ui/drops.js weights 1/cost^1.6). 3.2 puts the
 * card at about 4.5% of drops, so a 12 minute match with a steady pop rate sees
 * one to three. Only one card is ever held at a time.
 */
export const GAME_CARD_COST = 3.2;
export const DEFAULT_DUEL_LEN_SEC = DUEL_LENGTHS_SEC[0];

/** Offset that keeps duel seeds clear of the draft's per-element salts (codes 0..8). */
const DUEL_SALT_BASE = 0x44554500;

/** Same seed on both machines: the match seed salted by the duel index. */
export function duelSeed(matchSeed, idx) {
  let s;
  try { s = BigInt(matchSeed || 0); } catch (_e) { s = 0n; }
  return saltSeed(s, DUEL_SALT_BASE + Math.max(0, idx | 0));
}

/**
 * Who won, from the two reported boards. Highest tile first, then score.
 * Symmetric by construction: outcome(a,b) is always the mirror of outcome(b,a),
 * which is what lets both sides agree without a third message.
 * @param {{tile:number, score:number}} mine
 * @param {{tile:number, score:number}|null} theirs null = never reported = 0
 * @returns {'win'|'lose'|'tie'}
 */
export function duelOutcome(mine, theirs) {
  const a = { tile: num(mine && mine.tile), score: num(mine && mine.score) };
  const b = { tile: num(theirs && theirs.tile), score: num(theirs && theirs.score) };
  if (a.tile !== b.tile) return a.tile > b.tile ? 'win' : 'lose';
  if (a.score !== b.score) return a.score > b.score ? 'win' : 'lose';
  return 'tie';
}

/** Bonus this side adds to its own score for an outcome. */
export function bonusFor(outcome) {
  return outcome === 'win' ? DUEL_WIN_BONUS : 0;
}

/**
 * May a game card drop right now? Peer speaks night, this is at least the
 * player's second match, no duel is running and none is already held.
 */
export function cardEligible({ peerNight, finished, inDuel, held }) {
  return !!peerNight && (finished | 0) >= DUEL_MIN_FINISHED && !inDuel && (held | 0) <= 0;
}

/** The length both sides play: the host's cfg when it arrived, else the frame, else the default. */
export function pickLength(v) {
  const n = Number(v) | 0;
  return DUEL_LENGTHS_SEC.includes(n) ? n : DEFAULT_DUEL_LEN_SEC;
}

/** Tier -> the number on the tile (1 -> 2, 11 -> 2048). */
export function tileValue(tier) {
  const t = tier | 0;
  return t > 0 ? Math.pow(2, t) : 0;
}

function num(v) {
  const n = Number(v);
  return Number.isFinite(n) && n > 0 ? Math.trunc(n) : 0;
}
