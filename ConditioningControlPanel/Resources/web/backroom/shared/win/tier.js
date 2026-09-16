/* tier.js - WHAT A WIN IS WORTH, once, for all four stations (lane BR2-spine, CONTRACT 10.22).
 *
 * Law IX sizes the party to the win: 0 nothing, 1 small (a chime), 2 bigger (two notes and a jolt),
 * 3 big (THE THUD), 4 the hero (THE REVEAL). Every station already decides that for itself and keeps
 * deciding it: the slot's `tierOf` reads its paylines, the wheel's reads the day's slice, the roulette
 * and the cards read a moment id. Nothing here replaces those. This file NORMALISES what they produce
 * so a tier 3 at the roulette buys exactly the party a tier 3 at the slot buys, and so `plan.js` has one
 * number to spend against.
 *
 * PURE: no DOM, no three, no audio, no timers, so node:test holds it (the same rule stations/slot/feel.js
 * lives by).
 *
 * Law I: a tier is a picture of a number the server already settled. Nothing here mints, weights or
 * re-draws a pay. `pay` is whatever the tape said and the ladder below only reads it.
 *
 * WHERE THE LADDER COMES FROM (derived, not invented - every row is in a station file today):
 *   slot      stations/slot/feel.js LINE_TIER + tierOf: the payline ids, emi3 the jackpot at 4.
 *   wheel     stations/wheel/feel.js tierOf: jackpotWon 4, double/decoration 2, snooze 0, then the
 *             pay steps. Its landMoment rows are that same table by name.
 *   roulette  stations/roulette/feel.js CALLOUTS: a beat's callout `tier` is the station's own sizing,
 *             small / big / hero. `land.full` (a straight-up hit on a wake) is its hero.
 *   cards     stations/cards/feel.js CALLOUTS: the same three sizes; `cards.sweep` is its hero.
 *
 * TRAP: the callout sizes have three rungs, not five, so the roulette and the cards land on 1, 3 and 4
 * and never hand out a bare 2 of their own. That is deliberate - it is what those two files say today.
 * A pay big enough raises them (see `houseTier` rule 4); a station's OWN numeric tier is never raised.
 */

/** The five rungs of Law IX. */
export const TIER = Object.freeze({ NONE: 0, SMALL: 1, GOOD: 2, BIG: 3, HERO: 4 });
export const TIER_NAMES = Object.freeze(['none', 'small', 'good', 'big', 'hero']);

/** The four fixtures that pay. `counter` is a drain, never a win, so it is not here (10.17). */
export const STATIONS = Object.freeze(['slot', 'wheel', 'roulette', 'cards']);

/**
 * THE PAY LADDER, `[atLeast, tier]` highest first. The 40 / 10 / 1 steps are the slot's own thresholds
 * (`stations/slot/feel.js` tierOf) and the wheel's within one step; 400 is `emi3`, the slot jackpot's pay,
 * and it is here for the two stations that have no jackpot of their own - at the roulette and the cards a
 * paid result that size IS the hero. The slot and the wheel never reach it: they hand their own tier in.
 */
export const PAY_STEPS = Object.freeze([[400, 4], [40, 3], [10, 2], [1, 1]]);

/** Anything -> an integer rung 0..4. A missing, negative or daft value is nothing (Brake 9's floor: a tier
 *  that cannot be read is never a party). */
export function normTier(x) {
  const n = Math.floor(Number(x));
  if (!Number.isFinite(n) || n <= 0) return TIER.NONE;
  return Math.min(TIER.HERO, n);
}

/** The rung's name, for a log line or a css hook. */
export const tierName = x => TIER_NAMES[normTier(x)];

/** A settled pay -> its rung on THE PAY LADDER. A pay of 0 or less is nothing, never a small win. */
export function tierFromPay(pay) {
  const p = Math.floor(Number(pay) || 0);
  for (const [at, tier] of PAY_STEPS) if (p >= at) return tier;
  return TIER.NONE;
}

/**
 * EVERY MOMENT ID THE FOUR STATIONS PRODUCE, by station, as a rung. The keys are the literal strings the
 * station files return today (`landMoment`, `landBeat`, `settleMoment`, and the slot's payline `line`),
 * so a caller can pass what it already has without translating.
 *
 * Unknown ids are not an error: `tierOfMoment` returns null and `houseTier` falls through to the pay.
 */
export const MOMENT_TIER = Object.freeze({
  // stations/slot/feel.js LINE_TIER + tierOf. The lines that pay nothing are named so a no-pay spin
  // reads as a deliberate 0 rather than an unknown id.
  slot: Object.freeze({
    none: 0, melt: 0, hold: 0,
    spiral2: 1, sub2: 1, gif3: 1,
    spiral3: 2, sub3: 2,
    gif3same: 3,
    emi3: 4,
  }),
  // stations/wheel/feel.js landMoment, each row at the rung its own tierOf gives that result.
  // `grab`, `coast` and `nearMiss` are pre-landing beats and pay nothing, so they are 0.
  wheel: Object.freeze({
    snooze: 0, empty: 0, grab: 0, coast: 0, nearMiss: 0,
    small: 1,
    mid: 2, double: 2, gift: 2,
    big: 3,
    jackpot: 4,
  }),
  // stations/roulette/feel.js: landMoment's three ids and landBeat's five, sized by that file's CALLOUTS.
  roulette: Object.freeze({
    'roulette.land.miss': 0, 'roulette.land.win': 1, 'roulette.land.big': 3,
    'land.miss': 0, near: 0,
    'land.win': 1, streak: 1,
    'land.straight': 3, 'land.wake': 3,
    'land.full': 4,
  }),
  // stations/cards/feel.js settleMoment + isBloom, sized by that file's CALLOUTS.
  cards: Object.freeze({
    'cards.lose': 0, 'cards.push': 0,
    'cards.win': 1, 'cards.dealer_bust': 1,
    'cards.bloom': 3, 'cards.streak': 3,
    'cards.sweep': 4,
  }),
});

/** A station's moment id -> its rung, or null when the id is not one of that station's own. */
export function tierOfMoment(station, moment) {
  const table = MOMENT_TIER[station];
  if (!table || typeof moment !== 'string') return null;
  return Object.prototype.hasOwnProperty.call(table, moment) ? table[moment] : null;
}

/**
 * THE ONE ENTRY POINT. Give it whatever the station already has; it gives back a rung 0..4.
 *
 *   houseTier({ station, tier, moment, pay, jackpot, none })
 *     station  'slot' | 'wheel' | 'roulette' | 'cards' - which table `moment` is read against
 *     tier     the station's OWN tierOf, when it has one (the slot, the wheel). AUTHORITATIVE.
 *     moment   the station's moment / beat / settle id (the roulette, the cards, the wheel's rows)
 *     pay      the settled SP this result paid, from the tape. Never a guess.
 *     jackpot  the result took a declared jackpot (slot `emi3`, wheel `jackpotWon`)
 *     none     the caller already knows this is not a win (a miss, a push, a wound-down wheel)
 *
 * THE ORDER, and why:
 *   1. `jackpot` wins outright. A halved jackpot is still the jackpot (slot tierOf says so in as many words).
 *   2. `none` is nothing, whatever else is on the object. A push pays, and is still not a win.
 *   3. `tier` is the station's own recipe and is taken as read. The slot calls `spiral2` a 1 even when it
 *      pays 60; that is the slot's business and this file does not second-guess it.
 *   4. a known `moment` is the rung, RAISED by the pay when the pay says more - and only when the moment
 *      is already above 0. This is the rung the roulette and the cards ride: three callout sizes plus the
 *      honest number. A 0 moment stays 0: a miss is a miss.
 *   5. otherwise the pay alone.
 */
export function houseTier(input) {
  const o = input || {};
  if (o.jackpot === true) return TIER.HERO;
  if (o.none === true) return TIER.NONE;
  if (o.tier !== undefined && o.tier !== null) return normTier(o.tier);
  const m = tierOfMoment(o.station, o.moment);
  if (m !== null) return m > 0 ? Math.max(m, tierFromPay(o.pay)) : TIER.NONE;
  return tierFromPay(o.pay);
}
