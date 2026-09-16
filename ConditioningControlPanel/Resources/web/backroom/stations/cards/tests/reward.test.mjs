/* reward.test.mjs - the card table's reward reading (lane BR2-rw-cards, CONTRACT 10.22).
 *
 * What is pinned here: the rungs the table's own settled results are worth, the cue and the ladder that come
 * off a plan, Brake 2's merge with a live party, and the two things a plan is allowed to shrink (the callout's
 * size and the room-side shower). Law IX, Brakes 3 and 5 themselves belong to shared/win/plan.js and are held
 * by shared/win/tests - what is checked below is that this station ASKS correctly and OBEYS the answer.
 *
 *   node --test ConditioningControlPanel/Resources/web/backroom/stations/cards/tests/reward.test.mjs
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readHand } from '../hand.js';
import { CALLOUTS, calloutFor, settleMoment, STREAK_FROM } from '../feel.js';
import { MOMENT_TIER } from '../../../shared/win/tier.js';
import { winPlan, sitPlan, freshSit, afterParty, PARTY } from '../../../shared/win/plan.js';
import { LADDER } from '../../../shared/win/ladder.js';
import { settleTier, bloomTier, netOf, settleCue, climbSteps, ladderRoot, joinParty, partyHoldMs,
  calloutTier, showsRoom, CUE_TIER, STATION, BLOOM } from '../reward.js';

const H = (o) => readHand({ step: 0, stake: 1, active: 0, done: false, result: null, ...o });
const R = (hands, extra = {}) => {
  const returned = hands.reduce((s, h) => s + h.paid, 0), wagered = hands.reduce((s, h) => s + h.bet, 0);
  return { hands, dealerTotal: 17, dealerBlackjack: false, wagered, returned, net: returned - wagered, ...extra };
};
const hand = (cards, o = {}) => ({ cards, bet: 1, done: true, doubled: false, split: false, ...o });

const win = H({ id: 'h_w', dealer: ['Td', '7s'], hands: [hand(['Th', '9c'])], done: true,
  result: R([{ outcome: 'win', bet: 1, paid: 2, total: 19 }]) });
const bust = H({ id: 'h_db', dealer: ['Td', '5s', '9h'], hands: [hand(['Th', '8c'])], done: true,
  result: R([{ outcome: 'win', bet: 1, paid: 2, total: 18 }], { dealerTotal: 24 }) });
const bj = H({ id: 'h_bj', dealer: ['9d', '7s'], hands: [hand(['Kh', 'As'])], done: true,
  result: R([{ outcome: 'blackjack', bet: 1, paid: 3, total: 21 }]) });
const sweep = H({ id: 'h_s', dealer: ['9d', '8s'], hands: [hand(['8h', 'Kc'], { split: true }), hand(['8d', 'Qs'], { split: true })], done: true,
  result: R([{ outcome: 'win', bet: 1, paid: 2, total: 18 }, { outcome: 'win', bet: 1, paid: 2, total: 18 }]) });
const lose = H({ id: 'h_l', dealer: ['Td', '9s'], hands: [hand(['Th', '8c'])], done: true,
  result: R([{ outcome: 'lose', bet: 1, paid: 0, total: 18 }], { dealerTotal: 19 }) });
const push = H({ id: 'h_p', dealer: ['Td', '8s'], hands: [hand(['Th', '8c'])], done: true,
  result: R([{ outcome: 'push', bet: 1, paid: 1, total: 18 }], { dealerTotal: 18 }) });
const open = H({ id: 'h_o', dealer: ['9d'], hands: [hand(['9h', '5c'], { done: false })] });

test('netOf is the server\'s settled net and nothing else (Law I)', () => {
  assert.equal(netOf(win), 1);
  assert.equal(netOf(bj), 2);
  assert.equal(netOf(sweep), 2);
  assert.equal(netOf(lose), 0, 'a loss has nothing to fly');
  assert.equal(netOf(push), 0, 'a bet coming back is not a win');
  assert.equal(netOf(open), 0, 'a hand still open has no result to read');
  assert.equal(netOf(null), 0);
});

test('the rungs are the table\'s own callout sizes, through the spine (Law IX)', () => {
  assert.equal(settleTier(win, 1), 1, 'a plain win is a chime');
  assert.equal(settleTier(bust, 1), 1);
  assert.equal(settleTier(sweep, 1), 4, 'a sweep is this table\'s hero');
  assert.equal(settleTier(win, STREAK_FROM), 3, 'the third win in a row is THE THUD');
  assert.equal(bloomTier(), 3, 'a blackjack is big, never the hero: the sweep is the 4');
  assert.equal(settleTier(lose, 0), 0);
  assert.equal(settleTier(push, 0), 0, 'a push pays and is still not a win');
  assert.equal(settleTier(open, 0), 0);
});

test('every settle id the station can produce has a rung in the spine\'s table', () => {
  for (const h of [win, bust, bj, sweep, lose, push]) {
    for (const streak of [0, 1, STREAK_FROM]) {
      const id = settleMoment(h, streak);
      assert.ok(Object.prototype.hasOwnProperty.call(MOMENT_TIER[STATION], id), id + ' is unknown to tier.js');
    }
  }
  assert.ok(Object.prototype.hasOwnProperty.call(MOMENT_TIER[STATION], BLOOM));
});

test('a rung the table cannot reach on its own is never invented: no bare tier 2', () => {
  const rungs = new Set(Object.values(MOMENT_TIER[STATION]));
  assert.equal(rungs.has(2), false, 'the CALLOUTS table has three sizes, not five');
  for (const h of [win, bust, sweep, lose, push]) assert.notEqual(settleTier(h, 1), 2);
});

/* ------------------------------------------------------------------ the cues */

test('the settle cue is chosen by what the plan SPENT, not by the moment id', () => {
  const full = winPlan(4, {});
  assert.deepEqual(settleCue('cards.sweep', full, 1), ['win', { tier: 'hero', semis: 0 }]);
  // Brake 3: the fortieth win of a rung is a thud and the tokens, so the hero cue goes with it.
  const worn = winPlan(3, { seen: 40 });
  assert.equal(worn.spent, 1);
  assert.equal(settleCue('cards.streak', worn, 4)[1].tier, 'small');
  assert.equal(CUE_TIER[0], null, 'nothing won never plays a win');
});

test('a loss settles soft and a push sighs, whatever the plan says (never a fail sound)', () => {
  const none = winPlan(0, {});
  assert.deepEqual(settleCue('cards.lose', none, 0), ['settle', {}]);
  assert.deepEqual(settleCue('cards.push', none, 0), ['sigh', { level: 0.6 }]);
  assert.equal(settleCue('cards.win', none, 0), null, 'a rung of nothing has no win cue to play');
});

test('THE CHIME LADDER: the streak is the root, one-based, capped, an octave down while melted (Brake 5)', () => {
  assert.equal(ladderRoot(0), 0, 'no streak yet: the root');
  assert.equal(ladderRoot(1), 0, 'the first win of a run IS the root');
  assert.equal(ladderRoot(4), 3);
  assert.equal(ladderRoot(99), LADDER.CAP, 'the cap holds however long the run is');
  assert.equal(ladderRoot(1, true), LADDER.OCTAVE);
  assert.equal(ladderRoot(4, true), 3 + LADDER.OCTAVE);
  assert.equal(settleCue('cards.win', winPlan(1, {}), 3)[1].semis, 2, 'the cue carries the same root');
});

test('the climb is what follows the landing cue, never the cue again (Law X)', () => {
  const small = winPlan(1, {});
  assert.deepEqual(climbSteps(small), [], 'a small win is one note and nothing after it');
  const hero = winPlan(4, {});
  const steps = climbSteps(hero);
  assert.equal(steps.length, hero.ladder - 1);
  assert.ok(steps.every((s, i) => s.semis === i + 1), 'the rungs carry on from the landing note');
  assert.ok(steps.every((s, i) => i === 0 || s.at - steps[i - 1].at >= LADDER.STROBE_MIN_MS - 1), 'Brake 7');
  assert.deepEqual(climbSteps(winPlan(4, { reduced: true })), [], 'reduced motion has no rollup to climb over');
  assert.deepEqual(climbSteps(winPlan(3, { melted: true })), [], 'a melt is the landing note alone');
  assert.deepEqual(climbSteps(null), []);
});

/* --------------------------------------------------------- Brake 2, the merge */

test('a settle inside a live bloom merges into it and throws no second party', () => {
  const bloom = winPlan(3, {});
  const running = { plan: bloom, until: 5000 };
  const plain = winPlan(1, {});
  const joined = joinParty(running, plain, 2500);
  assert.equal(joined.plan, bloom, 'the HIGHER plan, never the sum');
  assert.equal(joined.ceremony, false, 'the bloom already owns this frame');
  assert.equal(joined.merged, true);
});

test('a bigger beat takes the frame over, and a party that has run out is not in the way', () => {
  const running = { plan: winPlan(1, {}), until: 5000 };
  const sweepPlan = winPlan(4, {});
  const over = joinParty(running, sweepPlan, 2500);
  assert.equal(over.plan, sweepPlan);
  assert.equal(over.ceremony, true, 'the higher rung takes the beat');
  const late = joinParty(running, winPlan(1, {}), 9000);
  assert.equal(late.ceremony, true, 'the earlier party has ended');
  assert.equal(late.merged, false);
  const first = joinParty(null, winPlan(1, {}), 0);
  assert.equal(first.ceremony, true);
  assert.equal(first.merged, false);
});

test('a tie keeps the party that already owns the beat', () => {
  const running = { plan: winPlan(3, {}), until: 5000 };
  const joined = joinParty(running, winPlan(3, {}), 100);
  assert.equal(joined.plan, running.plan);
  assert.equal(joined.ceremony, false);
});

test('the hold is the longer of the party and the winning flow', () => {
  assert.equal(partyHoldMs(winPlan(1, {}), 2000), 2000, 'a chime still waits out the callout');
  assert.equal(partyHoldMs(winPlan(4, {}), 2000), PARTY.MS[4], 'the declared hero owns the station');
  assert.equal(partyHoldMs(winPlan(4, { reduced: true }), 0), 0, 'reduced motion waits for nothing');
  assert.equal(partyHoldMs(null, 0), 0);
});

/* ------------------------------------------- the callout and the room shower */

test('THE REVEAL is the hero callout and it plays once a sit-down (Law IX, Brake 2)', () => {
  const co = CALLOUTS['cards.sweep'];
  assert.equal(co.tier, 'hero');
  let ledger = freshSit();
  const first = sitPlan(4, ledger, {});
  assert.equal(first.reveal, true);
  assert.equal(calloutTier(co, first), 'hero');
  ledger = afterParty(ledger, first);
  assert.equal(ledger.heroes, 1);
  const second = sitPlan(4, ledger, {});
  assert.equal(second.reveal, false, 'the second sweep of a sitting is a very good tier 3');
  assert.equal(calloutTier(co, second), 'big', 'and it names itself at big');
});

test('a hero under Calm burns no reveal, so the first unhurried sweep still gets one', () => {
  const ledger = freshSit();
  const calm = sitPlan(4, ledger, { still: true });
  assert.equal(calm.reveal, false);
  assert.equal(calloutTier(CALLOUTS['cards.sweep'], calm), 'big');
  assert.equal(afterParty(ledger, calm).heroes, 0);
});

test('a callout never shouts above what the brakes left, and an unknown beat has none', () => {
  const worn = winPlan(3, { seen: 40 });
  assert.equal(calloutTier(CALLOUTS['cards.streak'], worn), 'small', 'a thud-only streak does not say Hot Hand in gold');
  assert.equal(calloutTier(CALLOUTS['cards.streak'], winPlan(3, {})), 'big');
  assert.equal(calloutTier(CALLOUTS['cards.win'], winPlan(1, {})), 'small');
  assert.equal(calloutTier(null, winPlan(4, {})), null);
  assert.equal(calloutFor('cards.win', { bloomed: true }), null, 'a blackjack has already named itself');
});

test('10.22.B: the room is told only when the plan bought a shower, and never about a miss', () => {
  assert.equal(showsRoom(winPlan(1, {}), 1), false, 'a small win is close-up: no shower tier reaches the room');
  assert.equal(winPlan(1, {}).shower, 0);
  assert.equal(showsRoom(winPlan(4, {}), 2), true);
  assert.equal(showsRoom(winPlan(4, { still: true }), 2), false, 'Calm draws no coins');
  assert.equal(showsRoom(winPlan(4, { reduced: true }), 2), false);
  assert.equal(showsRoom(winPlan(3, { melted: true }), 2), false, 'Brake 5');
  assert.equal(showsRoom(winPlan(4, {}), 0), false, 'nothing paid, nothing announced');
  assert.equal(showsRoom(null, 5), false);
});

test('a melted settle is quiet, not invisible: the bank still flies (Law XII, Brake 5)', () => {
  const melted = sitPlan(settleTier(sweep, 1), freshSit(), { melted: true });
  assert.equal(melted.spent, 1);
  assert.ok(melted.bank >= 3, 'the value still moves');
  assert.equal(melted.shower, 0);
  assert.equal(melted.sparkle, 0);
  assert.equal(melted.glow, 0);
  assert.equal(melted.reveal, false);
  assert.equal(melted.octave, LADDER.OCTAVE);
  assert.equal(settleCue('cards.sweep', melted, 2)[1].semis, 1 + LADDER.OCTAVE);
});

test('reduced motion takes the state and keeps the cue (Law VI, Brake 9)', () => {
  const plan = sitPlan(settleTier(sweep, 1), freshSit(), { reduced: true });
  assert.equal(plan.bank, 0, 'no travel at all');
  assert.equal(plan.partyMs, 0, 'nothing to wait for');
  assert.ok(plan.ladder > 0, 'the cue still plays');
  assert.equal(settleCue('cards.sweep', plan, 1)[0], 'win');
});
