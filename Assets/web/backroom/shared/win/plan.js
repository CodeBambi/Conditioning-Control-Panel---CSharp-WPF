/* plan.js - WHAT A WIN IS ALLOWED TO SPEND (lane BR2-spine, CONTRACT 10.22).
 *
 * THE ONE PLACE Law IX and Brakes 2, 3 and 5 are enforced. Today every station re-derives its own restraint
 * and three of the four get it wrong by omission: the roulette's SP number simply changes (Law XII, broken),
 * the cards have no party at all, and the wheel carries a half copy of the slot's. From here on a station
 * asks for a rung and OBEYS the plan it gets back. It never re-decides whether it has earned a reveal.
 *
 * PURE: no DOM, no three, no audio, no timers. It returns a frozen budget; the station spends it.
 *
 *   Law IX    size the party to the win. small = a chime; bigger = two notes and a jolt; big = THE THUD;
 *             biggest = THE REVEAL. A small win NEVER gets sparkle, a shower or a reveal.
 *   Law XII   the bank is the one thing a win almost always keeps: value must be seen to move. Only a
 *             reduced-motion sit-down takes the number without the flight.
 *   Law XIII  every plan names an EMI pose. She reacts, she does not narrate.
 *   Brake 2   one hero moment per beat: `mergePlans` folds an overlap into the HIGHER plan, never the sum.
 *   Brake 3   repetition shrinks the party: the first three get the fanfare, then a rung down, and from the
 *             fortieth it is a thud and the tokens.
 *   Brake 5   never during focus states: melted drops the rung to 1, kills every garnish and drops the
 *             ladder an octave. The value still moves - a melted win is quiet, not invisible.
 *   Brake 8   lite boards drop the particles and cap the tokens at 4. The sound still carries the beat.
 *   Brake 9   `partyMs` is the only thing that costs time; every value is text underneath it.
 *
 * TRAP: `reduced` and `still` are NOT the same flag, and the stations already treat them apart. `reduced` is
 * reduced motion - the settled STATE, no travel at all (Law VI). `still` is Calm / Motion off - the
 * decoration goes (sparkle, shower, reveal) but the bank still flies, because a value that just changes is a
 * Law XII break at every motion level.
 */

import { normTier, TIER } from './tier.js';
import { winTokens, BANK } from './bank.js';
import { ladderSteps, LADDER } from './ladder.js';

export const PARTY = Object.freeze({
  /** THE PARTY's length by rung, in ms: the slot's ROLLUP_MS ladder (10.15 A3), which is also how long the
   *  chime ladder has to climb and how long the readout counts. The jackpot's 6 s is the declared hero. */
  MS: Object.freeze([0, 500, 1200, 2000, 6000]),
  /** THE SPARKLE BURST's spark count by rung. counterfx's sparkBurst clamps 5..9; 0 means do not fire it.
   *  Tiers 1 and 2 get none - it accompanies a big event and is never the event itself. */
  SPARKS: Object.freeze([0, 0, 0, 7, 9]),
  /** The room-side coin shower's tier by rung, for `ctx.revealedWin(amount, tier, text)` -> the fixture's
   *  payout node. room/coin-shower.js turns 1..4 into 7 / 16 / 32 / 64 coins. 0 means no shower: a small win
   *  is a close-up event and does not show from across the room. */
  SHOWER: Object.freeze([0, 0, 2, 3, 4]),
  /** THE GLOW, warm cut, in fast out slow. Given to anything that paid, at every rung above nothing. */
  GLOW_MS: 480,
  /** Brake 3's two gates: the first three of a rung get the fanfare, the fortieth is a thud and the tokens. */
  FANFARE_TIMES: 3,
  THUD_ONLY_FROM: 40,
  /** Law IX: the top tier plays once per sit-down. A second jackpot is a very good tier 3. */
  HEROES_PER_SIT: 1,
});

/** THE MASCOT GLANCE by rung (Law XIII). Pose keys are the shared face atlas's, the ones both the slot's and
 *  the wheel's `POSES` list. A station may override with its own landPose; it may not skip the glance. */
export const POSE_BY_TIER = Object.freeze(['idle0_0', 'hearts', 'hearts', 'spirals', 'jackpot']);
export const MELT_POSE = 'melt';

const clampSparks = n => (n > 0 ? Math.max(5, Math.min(9, Math.round(n))) : 0);
const frozen = p => Object.freeze(p);

/**
 * winPlan(tier, ctx) -> a FROZEN budget. The station does exactly this and no more.
 *
 * ctx (every field optional, every default the generous one):
 *   reduced        reduced motion. Law VI: the settled state, never a faster travel.
 *   lite           a board that asked for less (Brake 8): 4 tokens, no particles.
 *   still          Calm / Motion off: the decoration goes, the bank stays.
 *   melted         a focus state - a trance, a melt, a halved spin (Brake 5).
 *   seen           how many earlier wins of THIS RUNG already celebrated this sit-down (Brake 3).
 *   heroesThisSit  how many hero reveals have already played this sit-down (Law IX, Brake 2).
 *
 * The plan:
 *   tier      0..4   the rung that was asked for
 *   spent     0..4   the rung actually paid out, after every brake above. Everything else is sized off this.
 *   bank      0 | 3..7  tokens to fly (Law XII). 0 ONLY under reduced motion: take the value, keep the cue.
 *   shower    0..4   the room-side coin shower's tier for ctx.revealedWin. 0 = none.
 *   ladder    0..7   notes on THE CHIME LADDER, the landing cue included. 1 = the landing note alone.
 *   octave    0|-12  what the whole ladder is transposed by (Brake 5).
 *   sparkle   0|5..9 sparks for counterfx sparkBurst. 0 = do not fire it.
 *   reveal    bool   THE REVEAL, the 620 ms declared hero, is allowed on this beat.
 *   glow      0|480  ms of THE GLOW (counterfx warmGlow). 0 while melted (Brake 5: no ceremonies) and
 *                    under reduced motion; Calm keeps it, a warm cut is not travel.
 *   emi       str    the pose for THE MASCOT GLANCE.
 *   partyMs   0..6000  how long this beat owns the station: the rollup, the ladder's climb, the hold before
 *                      the next decision may open. 0 under reduced motion - nothing to wait for.
 *   why       null | 'none' | 'melted' | 'reduced' | 'repeat' | 'thud' | 'capped'
 *                      what shrank it, for the feel log and the tests. null = nothing did.
 */
export function winPlan(tier, ctx) {
  const c = ctx || {};
  const reduced = !!c.reduced, lite = !!c.lite, still = !!c.still || reduced, melted = !!c.melted;
  const seen = Math.max(0, Math.floor(Number(c.seen) || 0));
  const heroes = Math.max(0, Math.floor(Number(c.heroesThisSit) || 0));

  const asked = normTier(tier);
  let spent = asked;
  let why = null;

  // Nothing was won. There is no party to size: the station's own failure recipe (a muted thud, THE SHIVER,
  // a wince - Brake 6) owns this beat, and it is not a win plan's business.
  if (spent === TIER.NONE) return frozen({ tier: asked, spent: 0, bank: 0, shower: 0, ladder: 0, octave: 0,
    sparkle: 0, reveal: false, glow: 0, emi: melted ? MELT_POSE : POSE_BY_TIER[0], partyMs: 0, why: 'none' });

  // Law IX, Brake 2: the hero plays once a sit-down. A second jackpot is a very good big win.
  if (spent === TIER.HERO && heroes >= PARTY.HEROES_PER_SIT) { spent = TIER.BIG; why = 'capped'; }

  // Brake 3: repetition shrinks the party. The hero is exempt - it is capped by the count above, not worn
  // down by it - so this only ever touches the rungs that can repeat.
  if (spent < TIER.HERO) {
    if (seen >= PARTY.THUD_ONLY_FROM - 1) { spent = TIER.SMALL; why = 'thud'; }
    else if (seen >= PARTY.FANFARE_TIMES) { spent = Math.max(TIER.SMALL, spent - 1); why = why || 'repeat'; }
  }

  // Brake 5: never during focus states. Melted drops to the quietest rung that still moves the value.
  if (melted) { spent = TIER.SMALL; why = 'melted'; }

  const ladder = ladderSteps(spent, melted);
  const octave = melted ? LADDER.OCTAVE : 0;
  const emi = melted ? MELT_POSE : POSE_BY_TIER[spent];

  // Law VI: reduced motion takes the STATE. No tokens, no shower, no sparks, no reveal, no travel and no
  // wait - and the cue still plays, which is why the ladder survives this branch intact (Brake 9).
  if (reduced) {
    return frozen({ tier: asked, spent, bank: 0, shower: 0, ladder, octave, sparkle: 0, reveal: false,
      glow: 0, emi, partyMs: 0, why: why || 'reduced' });
  }

  // Law XII: the value moves at every other motion level. Brake 8 caps the tokens on a lite board.
  const bank = Math.min(winTokens(spent, lite), lite ? BANK.MAX_LITE : BANK.MAX);
  // Calm strips what is only decoration; melt strips it too (Brake 5).
  const quiet = still || melted;
  const shower = quiet ? 0 : lite ? Math.min(2, PARTY.SHOWER[spent]) : PARTY.SHOWER[spent];
  const sparkle = quiet || lite ? 0 : clampSparks(PARTY.SPARKS[spent]);
  const reveal = !quiet && spent === TIER.HERO;

  return frozen({ tier: asked, spent, bank, shower, ladder, octave, sparkle, reveal,
    glow: melted ? 0 : PARTY.GLOW_MS, emi, partyMs: PARTY.MS[spent], why });
}

/**
 * Brake 2, one hero moment per beat: two parties that land on the same frame MERGE into the higher one, they
 * never stack. The jar spilling on the spin that also won a line, a wheel gift on a paying slice, a cards
 * sweep that is also a streak - one plan comes out, and it is the bigger.
 * Ties keep the first, so the beat that owns the frame keeps its own EMI pose and its own `why`.
 */
export function mergePlans(a, b) {
  if (!a) return b || null;
  if (!b) return a;
  return b.spent > a.spent ? b : a;
}

/* ----------------------------------------------------------------------------
 * THE SIT-DOWN LEDGER - Brake 3's memory, so a station keeps no counters of its own
 * -------------------------------------------------------------------------- */

/** A fresh ledger, one per sit-down. `seen` is per rung; `heroes` is the whole sit-down's. */
export const freshSit = () => Object.freeze({ seen: Object.freeze([0, 0, 0, 0, 0]), heroes: 0 });

/** winPlan with `seen` and `heroesThisSit` read off the ledger. The station passes the rest of the ctx. */
export function sitPlan(tier, sit, ctx) {
  const s = sit || freshSit();
  const t = normTier(tier);
  return winPlan(t, { ...(ctx || {}), seen: s.seen[t] || 0, heroesThisSit: s.heroes || 0 });
}

/** The ledger AFTER a plan was played. Immutable: it returns a new one, it never writes to the old.
 *  A plan that spent nothing is not a celebration and does not count toward Brake 3. */
export function afterParty(sit, plan) {
  const s = sit || freshSit();
  if (!plan || !(plan.spent > 0)) return s;
  const seen = s.seen.slice();
  const t = normTier(plan.tier);
  seen[t] = (seen[t] || 0) + 1;
  return Object.freeze({ seen: Object.freeze(seen), heroes: s.heroes + (plan.reveal ? 1 : 0) });
}
