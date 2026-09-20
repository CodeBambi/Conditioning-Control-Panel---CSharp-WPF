/* win.test.mjs - THE REWARD PASS at the Daily Daze wheel (CONTRACT 10.22, lane BR2-rw-wheel).
 *
 * What is under test is the WIRING, not the spine: shared/win/tests/ already pins winPlan, the bank engine
 * and the ladder note for note. What this file pins is the wheel's side of the retrofit -
 *
 *   - the wheel asks for a rung through `houseTier` and gets its own `tierOf` back untouched (rule 3);
 *   - THE PRIZE MOMENT is a SECOND ask, folded in by Brake 2, never a second party;
 *   - `recipe` sizes itself off `plan.spent` (what was paid out) and not off `tierOf` (what was asked for);
 *   - THE BANK's count and value ladder are the shared engine's, not a second copy;
 *   - Calm keeps the flight and loses the decoration; reduced motion keeps the CUE and loses the flight;
 *   - the adapter in bank.js plays the engine's events in the order Law X asks for, on a stubbed board.
 */

import test from 'node:test';
import assert from 'node:assert/strict';

import { recipe, tierOf, prizeTier, PRIZE_TIER, winTokens, tickValues, FEEL } from '../feel.js';
import { houseTier, MOMENT_TIER } from '../../../shared/win/tier.js';
import { sitPlan, mergePlans, afterParty, freshSit } from '../../../shared/win/plan.js';
import { ladderPlan, LADDER } from '../../../shared/win/ladder.js';
import * as sharedBank from '../../../shared/win/bank.js';

/** A settled receipt, exactly the shape readResult hands land(). */
const R = (o = {}) => ({ pay: 0, total: 0, snoozed: false, jackpotWon: false, reward: null, sliceId: 's', day: 'd', ...o });
const GIFT = R({ reward: { kind: 'decoration', decorationId: 'ivy', fallback: false } });
const DOUBLE = R({ reward: { kind: 'double' } });
const CALM = { still: true, lite: true };

/** The station's own two asks, folded by Brake 2 - planFor() in station.js, kept in step by hand here. */
const planFor = (r, sit = freshSit(), ctx = {}) => mergePlans(
  sitPlan(houseTier({ station: 'wheel', tier: tierOf(r) }), sit, ctx),
  prizeTier(r) ? sitPlan(houseTier({ station: 'wheel', tier: prizeTier(r) }), sit, ctx) : null);

/* ---------------------------------------------------------------- the rung */

test('the wheel hands its own rung in whole: houseTier never raises it by the pay (rule 3)', () => {
  // A Snooze pays a carry and is still not a win; a gift pays nothing and is still a 2.
  assert.equal(planFor(R({ snoozed: true, total: 2 })).spent, 0);
  assert.equal(houseTier({ station: 'wheel', tier: tierOf(R({ pay: 2 })), pay: 900 }), 1, 'a pay never raises the wheel');
  assert.deepEqual([2, 8, 40, 550].map(pay => houseTier({ station: 'wheel', tier: tierOf(R({ pay, jackpotWon: pay === 550 })) })), [1, 2, 3, 4]);
});

test('THE PRIZE MOMENT is a second ask, merged to the higher (Brake 2), never the sum', () => {
  assert.equal(prizeTier(GIFT), PRIZE_TIER);
  assert.equal(prizeTier(DOUBLE), PRIZE_TIER);
  assert.equal(prizeTier(R({ reward: { kind: 'decoration', decorationId: 'ivy', fallback: true } })), 0,
    'a fallback decoration is a PAY, not a prize: the collection was already complete');
  assert.equal(prizeTier(R({ reward: { kind: 'nothing' } })), 0);
  assert.equal(prizeTier(R({ pay: 40 })), 0);
  // tierOf still calls a gift a 2 - shared/win/tier.js pins that table against this one and it must not move.
  assert.equal(tierOf(GIFT), 2);
  assert.equal(MOMENT_TIER.wheel.gift, 2);
  const plan = planFor(GIFT);
  assert.equal(plan.spent, 3, 'the arrival of a thing is a big beat');
  assert.equal(plan.sparkle, 7, 'and it is the rung THE SPARKLE BURST enters at');
  assert.ok(plan.glow > 0 && !plan.reveal, 'a prize is not a declared hero: the pot still is');
});

test('a prize and a pay on one frame are ONE party, and it is the bigger', () => {
  const both = mergePlans(sitPlan(1, freshSit(), {}), sitPlan(PRIZE_TIER, freshSit(), {}));
  assert.equal(both.spent, PRIZE_TIER);
  assert.equal(both.bank, sharedBank.winTokens(PRIZE_TIER), 'the tokens are the higher plans, never both plans');
});

/* --------------------------------------------------------------- the plan */

test('the recipe sizes off what was PAID OUT, not off what was asked for', () => {
  const pot = R({ pay: 550, total: 550, jackpotWon: true });
  const first = planFor(pot);
  assert.ok(first.reveal && recipe(pot, { plan: first }).sound === 'reveal');
  // Law IX: the hero plays once a sit-down. The second pot of a sit-down is a very good tier 3.
  const sit = afterParty(freshSit(), first);
  const second = planFor(pot, sit);
  assert.equal(second.spent, 3);
  assert.equal(second.why, 'capped');
  const rec = recipe(pot, { plan: second });
  assert.equal(rec.sound, 'thud', 'the cue shrinks with the rung without this file knowing why');
  assert.ok(!rec.reveal && !rec.gold, 'and so does THE REVEAL and the gold');
  assert.equal(rec.partyMs, second.partyMs);
});

test('with no plan the recipe falls back to its own rung, exactly as before (dev.html, the checks)', () => {
  const pot = R({ pay: 550, jackpotWon: true });
  assert.deepEqual(
    [recipe(pot).sound, recipe(pot).reveal, recipe(pot).sparks, recipe(pot).partyMs],
    ['reveal', true, true, FEEL.PARTY_MS[4]]);
});

test('Law VI and Brake 8: Calm keeps the flight, reduced motion keeps the CUE', () => {
  const big = R({ pay: 60, total: 60 });
  const calm = planFor(big, freshSit(), CALM);
  assert.ok(calm.bank >= 3 && calm.bank <= 4, 'a value that just changes is a Law XII break at every motion level');
  assert.deepEqual([calm.shower, calm.sparkle, calm.reveal], [0, 0, false], 'Calm strips the decoration');
  assert.ok(calm.glow > 0 && calm.partyMs > 0, 'a warm cut is not travel');
  const off = planFor(big, freshSit(), { reduced: true });
  assert.deepEqual([off.bank, off.shower, off.sparkle, off.glow, off.partyMs], [0, 0, 0, 0, 0]);
  assert.ok(off.ladder > 0, 'the ladder survives reduced motion: the cue still plays (Brake 9)');
});

test('the announcement is skipped, not sent with a 0 (10.22.B)', () => {
  // What station.js guards on: a paid result with a shower, and nothing else.
  const announced = r => { const p = planFor(r); return r.pay > 0 && p.shower > 0; };
  assert.equal(announced(R({ pay: 2, total: 2 })), false, 'a small win is a close-up event (Law IX)');
  assert.equal(announced(R({ pay: 20, total: 20 })), true);
  assert.equal(announced(R({ snoozed: true, total: 2 })), false, 'the room is not told about a doze');
  assert.equal(announced(R({ reward: { kind: 'nothing' } })), false, 'nor about Head Empty');
  assert.equal(announced(GIFT), false, 'a gift pays no SP, so there is nothing to shower');
  assert.equal(announced(R({ pay: 550, total: 550, jackpotWon: true })), true);
  assert.equal(planFor(R({ pay: 60, total: 60 }), freshSit(), CALM).shower, 0, 'and never under Calm');
});

/* ------------------------------------------------------- the shared engine */

test('THE BANK count and the value ladder are the shared engine, not a second copy', () => {
  assert.equal(winTokens, sharedBank.winTokens);
  assert.equal(tickValues, sharedBank.tickValues);
  assert.deepEqual([1, 2, 3, 4].map(t => winTokens(t, false)), [3, 4, 5, 7]);
  assert.deepEqual([1, 2, 3, 4].map(t => winTokens(t, true)), [3, 4, 4, 4]);
});

test('THE CHIME LADDER is the shared one: the cap and the 6 Hz floor come off LADDER', () => {
  assert.equal(FEEL.LADDER_CAP, LADDER.CAP);
  assert.equal(FEEL.STROBE_MIN_MS, LADDER.STROBE_MIN_MS);
  const big = planFor(R({ pay: 60, total: 60 }));
  const steps = ladderPlan(big.spent, big.partyMs, big.octave < 0);
  assert.equal(steps[0].at, 0, 'step 0 is the landing note win() already played');
  assert.ok(steps.length > 1, 'a big landing RISES: the flat cue by tier is gone');
  for (let i = 1; i < steps.length; i++) assert.ok(steps[i].at - steps[i - 1].at >= LADDER.STROBE_MIN_MS);
  assert.ok(steps.length <= LADDER.CAP);
  const small = planFor(R({ pay: 2, total: 2 }));
  assert.equal(ladderPlan(small.spent, small.partyMs).length, 1, 'a small win gets the landing note and nothing after it');
});

/* ----------------------------------------------- the adapter over the engine */

/** The smallest board bank.js can run on: a layer, an element factory and a frame pump we drive by hand. */
function board() {
  const made = [];
  const layer = { getBoundingClientRect: () => ({ left: 0, top: 0, width: 400, height: 300 }), append: () => {} };
  const doc = { createElement: () => { const el = { style: {}, gone: false, remove() { this.gone = true; } }; made.push(el); return el; } };
  let queued = [];
  const prev = { document: globalThis.document, raf: globalThis.requestAnimationFrame, caf: globalThis.cancelAnimationFrame };
  globalThis.document = doc;
  globalThis.requestAnimationFrame = fn => { queued.push(fn); return queued.length; };
  globalThis.cancelAnimationFrame = () => { queued = []; };
  return {
    made, layer,
    pump(at) { const q = queued; queued = []; for (const fn of q) fn(at); },
    restore() { globalThis.document = prev.document; globalThis.requestAnimationFrame = prev.raf; globalThis.cancelAnimationFrame = prev.caf; },
  };
}

test('the adapter plays the engine in Law X order: ticks on the landings, the mini-thud at the END of the count', async () => {
  const b = board();
  try {
    const { createBank } = await import('../bank.js');
    const log = [];
    const bank = createBank({ layer: b.layer, reduced: false, now: () => 0,
      onTick: (v, quiet) => log.push(['tick', v, quiet]), onLand: () => log.push(['land']), onDone: () => log.push(['done']) });
    const n = 5, rollupMs = 2000;
    const mode = bank.start({ n, fromValue: 100, toValue: 160, rollupMs, from: () => ({ x: 10, y: 10 }), to: () => ({ x: 300, y: 20 }) });
    assert.equal(mode, 'flying');
    assert.equal(b.made.length, n, 'one element a token, the wheels own class');

    b.pump(0);
    assert.deepEqual(log, [], 'nothing has landed on the first frame, so the readout says nothing');
    b.pump(sharedBank.bankLandMs(0));
    assert.equal(log.length, 1, 'the first token LANDED, so the readout ticked');
    assert.deepEqual([log[0][0], log[0][2]], ['tick', false]);
    b.pump(sharedBank.bankFlightMs(n));
    assert.equal(log.filter(e => e[0] === 'tick').length, n, 'every landing ticked once');
    assert.equal(log.filter(e => e[0] === 'land').length, 0, 'and the mini-thud is HELD: the readout is still counting');
    assert.ok(b.made.every(el => el.gone), 'the tokens are down and off the layer');

    b.pump(rollupMs);
    assert.deepEqual(log.map(e => e[0]).slice(-2), ['land', 'done'], 'the mini-thud and the teardown close the beat');
    assert.equal(log.filter(e => e[0] === 'tick').at(-1)[1], 160, 'and it lands on the settled value, never a rung of the ladder');
    assert.ok(log.some(e => e[0] === 'tick' && e[2] === true), 'the tail ticks are quiet: no second gesture, no token cue');
    assert.equal(bank.busy, false);
  } finally { b.restore(); }
});

test('Law VI: reduced motion takes the STATE and never a token; skip() leaves quietly', async () => {
  const b = board();
  try {
    const { createBank } = await import('../bank.js');
    const off = [];
    const state = createBank({ layer: b.layer, reduced: () => true, now: () => 0,
      onTick: (v, quiet) => off.push(['tick', v, quiet]), onLand: () => off.push(['land']), onDone: () => off.push(['done']) });
    assert.equal(state.start({ n: 7, fromValue: 100, toValue: 160, from: () => null, to: () => null }), 'state');
    assert.deepEqual(off, [['tick', 160, true], ['land'], ['done']]);
    assert.equal(b.made.length, 0, 'no tokens were ever made');

    const log = [];
    const bank = createBank({ layer: b.layer, reduced: false, now: () => 0,
      onTick: (v, quiet) => log.push(['tick', v, quiet]), onLand: () => log.push(['land']), onDone: () => log.push(['done']) });
    bank.start({ n: 4, fromValue: 100, toValue: 160, rollupMs: 2000, from: () => ({ x: 0, y: 0 }), to: () => ({ x: 9, y: 9 }) });
    b.pump(sharedBank.bankLandMs(0));
    bank.skip();
    assert.deepEqual(log.at(-1), ['done']);
    assert.equal(log.filter(e => e[0] === 'land').length, 0, 'Back, suspend and close settle without the mini-thud');
    assert.equal(log.at(-2)[1], 160, 'straight to the settled value, never a faster version of the travel');
    assert.equal(bank.busy, false);
    bank.skip();
    assert.equal(log.filter(e => e[0] === 'done').length, 1, 'a second skip fires nothing');
  } finally { b.restore(); }
});
