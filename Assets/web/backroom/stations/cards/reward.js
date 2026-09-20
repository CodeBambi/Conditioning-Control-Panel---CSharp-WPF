/* ============================================================================
 * stations/cards/reward.js - what a settled hand is WORTH at the card table and
 * what its party may SPEND, pure (lane BR2-rw-cards, CONTRACT 10.22).
 *
 * The table had no party at all: station.js took spReadout.set and .owe and
 * never .thud(), never flew a token, and read its cue off a flat map by moment
 * id. This file is the thin reading between the table's own settled result and
 * the shared spine - it decides NOTHING the spine decides:
 *
 *   shared/win/tier.js    what the rung is (settleMoment + the settled pay)
 *   shared/win/plan.js    what the rung may spend (Law IX, Brakes 2, 3, 5, 8)
 *   shared/win/ladder.js  the notes and the octave
 *
 * PURE: no DOM, no three, no audio, no timers, so node:test holds it, the same
 * rule feel.js and hand.js live by. Every number below is the server's: `net`
 * is `hand.result.net` off the reply, never a pay this page worked out (Law I).
 *
 *   Law I     the rung is a picture of a settled result. A hand that is not
 *             done has no rung and no party.
 *   Law IX    the table's own sizes ARE its callout sizes (feel.CALLOUTS):
 *             win / dealer_bust small, bloom / streak big, sweep the hero. A
 *             pay big enough raises them and nothing else does.
 *   Law X     the landing cue is ONE note - the rest of the ladder is the
 *             rollup's climb, not a second party (`climbSteps`).
 *   Brake 2   `joinParty` folds a settle that lands inside a live bloom into
 *             the higher plan. Parties never stack; the bank still flies,
 *             because the pay is not a ceremony (Law XII).
 *   Brake 5   at this table the focus state is the bloom's own fullscreen
 *             picture: a settle under it is `melted` - quiet, an octave down,
 *             never invisible.
 * ==========================================================================*/

import { houseTier } from '../../shared/win/tier.js';
import { mergePlans } from '../../shared/win/plan.js';
import { ladderSemis, ladderPlan } from '../../shared/win/ladder.js';
import { settleMoment } from './feel.js';

/** The station's row in shared/win/tier.js MOMENT_TIER. */
export const STATION = 'cards';
/** The bloom's own beat: a paid player blackjack names itself before the hand settles. */
export const BLOOM = 'cards.bloom';

/** What the server settled this hand at, in SP. 0 for a loss, a push and a hand still open (Law I). */
export const netOf = hand => (hand && hand.done && hand.result ? Math.max(0, Math.round(Number(hand.result.net) || 0)) : 0);

/**
 * The rung of the settle beat: the table's own size (feel.settleMoment -> the CALLOUTS sizes, through
 * MOMENT_TIER) raised by the settled pay. `streak` is wins in a row INCLUDING this hand (feel.streakAfter),
 * exactly what station.js already keeps.
 *   lose, push        0   a miss is a miss, whatever it returned
 *   win, dealer_bust  1   a chime (Law IX: a small win never gets confetti)
 *   streak            3   THE THUD
 *   sweep             4   the table's hero, and the only rung that reaches THE REVEAL
 */
export function settleTier(hand, streak = 0) {
  const id = settleMoment(hand, streak);
  if (!id) return 0;
  return houseTier({ station: STATION, moment: id, pay: netOf(hand) });
}

/**
 * The bloom's rung: a paid blackjack is BIG (3), never the hero. The hero of this table is `cards.sweep` -
 * every hand of a split won - and that is what feel.js CALLOUTS has said since the winning flow landed
 * (bloom 'big', sweep 'hero'), which is where shared/win/tier.js MOMENT_TIER derived its own row from.
 *
 * TRAP, if a later owner wants the blackjack to be the table's REVEAL instead: it is this one line
 * (`houseTier({ station: STATION, tier: TIER.HERO })` - rule 3, a numeric tier is authoritative and needs no
 * shared file touched), but CALLOUTS['cards.bloom'].tier and MOMENT_TIER.cards must move with it or the two
 * tables disagree, shared/win/tests/tier.test.mjs pins them against each other, and the bloom starts burning
 * the once-a-sit-down hero a sweep is owed.
 */
export const bloomTier = () => houseTier({ station: STATION, moment: BLOOM });

/* ----------------------------------------------------------------------------
 * THE CUES - the kit's own names, chosen by what the plan actually SPENT
 * -------------------------------------------------------------------------- */

/** shared/sound/kit.js `win` tiers by `plan.spent`. Index 0 never plays a win: nothing was won. */
export const CUE_TIER = Object.freeze([null, 'small', 'mid', 'big', 'hero']);

/**
 * THE CHIME LADDER's root at this table: the streak is the climb. The first win of a run is the root note
 * and every win after it starts a semitone higher, capped at the ladder's seven and an octave down while
 * melted - the slot's `ladderSemis` over the cards' own `streak` counter. `streak` counts THIS hand, so it
 * is one-based: the 1 of the first win is the root, not a step above it.
 */
export const ladderRoot = (streak, melted = false) => ladderSemis(Math.max(0, (streak | 0) - 1), !!melted);

/**
 * The settle's cue, by moment and plan. A pay is THE WIN by the rung the brakes LEFT (a fourth small win of
 * a sit-down is a chime however the hand was dressed), a loss is THE SETTLE (soft, never a fail) and a push
 * is a sigh. A bust is a beat with nothing in it, so it has no cue either.
 * -> [name, opts] for `kit.play(...)`, or null.
 */
export function settleCue(id, plan, streak = 0) {
  if (id === 'cards.lose') return ['settle', {}];
  if (id === 'cards.push') return ['sigh', { level: 0.6 }];
  const tier = CUE_TIER[plan ? plan.spent : 0];
  if (!tier) return null;
  return ['win', { tier, semis: ladderRoot(streak, !!(plan && plan.octave < 0)) }];
}

/**
 * The notes AFTER the landing cue, across the party. Step 0 is the cue `settleCue` already played (Law X:
 * one gesture, one beat), so only what follows it is handed to the kit; an empty list is a rung that has
 * nothing to climb (tier 1, a melt, reduced motion's `partyMs` of 0).
 */
export function climbSteps(plan) {
  if (!plan || !(plan.spent > 0)) return [];
  return ladderPlan(plan.spent, plan.partyMs, plan.octave < 0).slice(1);
}

/* ----------------------------------------------------------------------------
 * BRAKE 2 - one hero moment per beat
 * -------------------------------------------------------------------------- */

/**
 * A new beat against the party still running. `running` is `{ plan, until }` (the last party and the frame
 * it stops owning the station), `now` the frame this beat lands on.
 *
 * -> { plan, ceremony, merged }
 *   plan       the HIGHER of the two (shared/win/plan.js mergePlans: never the sum, a tie keeps the runner)
 *   ceremony   may this beat throw its own party? False when the live one was already as big or bigger -
 *              the callout, the shower, the sparkle, the glow and the ladder all sit this one out. THE BANK
 *              is not a ceremony and ignores this: a pay must be seen to move (Law XII).
 *   merged     a live party was there to merge with, for the feel log
 */
export function joinParty(running, plan, now = 0) {
  const live = running && running.plan && Number(running.until) > Number(now) ? running.plan : null;
  const winner = mergePlans(live, plan) || plan;
  return { plan: winner, ceremony: winner === plan, merged: !!live };
}

/** How long this beat owns the station: its own party, never shorter than the winning flow's own hold. */
export const partyHoldMs = (plan, floorMs = 0) => Math.max(Math.max(0, floorMs | 0), plan ? plan.partyMs : 0);

/* ----------------------------------------------------------------------------
 * THE CALLOUT AND THE SHOWER - the two things a plan may shrink
 * -------------------------------------------------------------------------- */

/**
 * The callout's size for a plan. THE REVEAL (the hero callout: 14vh, a rim and a short shake) is the
 * declared hero move and plays once a sit-down (`plan.reveal`, Law IX); a second sweep, a sweep under Calm
 * and a sweep in a trance all name themselves at `big` instead. Anything the brakes wore below the callout's
 * own size comes down with it, so a worn-out win never shouts.
 */
export function calloutTier(co, plan) {
  if (!co) return null;
  const spent = plan ? plan.spent : 0;
  if (co.tier === 'hero') return plan && plan.reveal ? 'hero' : 'big';
  if (co.tier === 'big' && spent > 0 && spent < 3) return 'small';
  return co.tier;
}

/** CONTRACT 10.22.B: the room is told once, on the frame the pay is revealed, and only when the plan bought
 *  a shower. `plan.shower` of 0 must never reach `ctx.revealedWin` - room/coin-shower.js clamps 1..4 and
 *  would rain anyway (Law IX: a small win does not show from across the room). */
export const showsRoom = (plan, amount) => !!(plan && plan.shower > 0 && Number(amount) > 0);
