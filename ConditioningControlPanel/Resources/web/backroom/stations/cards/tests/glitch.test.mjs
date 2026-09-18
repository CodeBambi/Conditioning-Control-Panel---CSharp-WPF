/* THE SLIP (glitch.js). Pure: no DOM, no canvas, no clock. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { planSlip, wornCode, slipShake, slipPhase, twins, SLIP_ODDS, SLIP_MS } from '../glitch.js';
import { cardValue } from '../hand.js';

const hand = (...codes) => codes.map((code, i) => ({ id: i + 1, code }));
/** Seeds whose first roll lands under `odds`, so a test can get a slip on demand. */
function seedThatSlips(cards, odds = 1) {
  for (let i = 0; i < 5000; i++) { const s = planSlip('h' + i, cards, { odds }); if (s) return { seed: 'h' + i, slip: s }; }
  throw new Error('no seed slipped');
}

test('the odds are one hand in fifty, rolled per hand', () => {
  assert.equal(SLIP_ODDS, 1 / 50);
  const cards = hand('Ts', '7h');
  let slips = 0;
  for (let i = 0; i < 20000; i++) if (planSlip('hand-' + i, cards)) slips++;
  const rate = slips / 20000;
  assert.ok(rate > 1 / 90 && rate < 1 / 28, 'about 1 in 50, got 1 in ' + Math.round(1 / rate));
});

test('the same hand always slips the same way, and never consumes a shared rng', () => {
  const cards = hand('Ts', '7h', 'Qd');
  const { seed, slip } = seedThatSlips(cards);
  assert.deepEqual(planSlip(seed, cards, { odds: 1 }), slip);
  assert.deepEqual(planSlip(seed, cards, { odds: 1 }), slip);
});

test('a ten-family card slips inside its own value, so the felt total stays honest', () => {
  // T J Q K share a blackjack value but not a deck slot, so the picture moves and the total cannot lie.
  const { slip } = seedThatSlips(hand('Qd'));
  assert.equal(slip.from, 'Qd');
  assert.notEqual(slip.to, 'Qd');
  assert.equal(cardValue(slip.to), cardValue('Qd'));
  assert.notEqual(slip.to[0], 'Q', 'a different rank, so deck.keyFor gives a different picture');
});

test('twins are same-value, different-picture codes; A and the pip cards have none', () => {
  assert.equal(twins('Ts').length, 12);              // J Q K, four suits each
  for (const c of twins('Ts')) assert.equal(cardValue(c), 10);
  assert.deepEqual(twins('7h'), []);
  assert.deepEqual(twins('Ac'), []);
  assert.deepEqual(twins('nope'), []);
});

test('a card with no twin takes a free rank, and free:false refuses instead', () => {
  const { seed, slip } = seedThatSlips(hand('7h'));
  assert.notEqual(slip.to, '7h');
  assert.equal(planSlip(seed, hand('7h'), { odds: 1, free: false }), null);
});

test('only real face-up codes can slip, and an empty felt never does', () => {
  assert.equal(planSlip('x', [], { odds: 1 }), null);
  assert.equal(planSlip('x', [{ id: 1, code: null }], { odds: 1 }), null);
  assert.equal(planSlip('x', [{ id: 1, code: 'zz' }], { odds: 1 }), null);
  assert.equal(planSlip('x', null, { odds: 1 }), null);
  const { slip } = seedThatSlips([{ id: 9, code: 'Ts' }, { id: 10, code: null }]);
  assert.equal(slip.id, 9);
});

test('the face crosses at the midpoint and the card is never left mid-tear', () => {
  const slip = { id: 3, from: 'Ts', to: 'Kh' }, card = { id: 3 };
  assert.equal(wornCode(card, 'Ts', slip, 0, 0), 'Ts');
  assert.equal(wornCode(card, 'Ts', slip, 0, SLIP_MS / 2 - 1), 'Ts');
  assert.equal(wornCode(card, 'Ts', slip, 0, SLIP_MS / 2 + 1), 'Kh');
  assert.equal(wornCode(card, 'Ts', slip, 0, SLIP_MS * 10), 'Kh', 'it settles on the new card for good');
  assert.equal(wornCode({ id: 4 }, 'Ts', slip, 0, SLIP_MS), 'Ts', 'every other card is untouched');
  assert.equal(wornCode(card, 'Ts', null, 0, SLIP_MS), 'Ts', 'no slip, no change');
});

test('the tear is zero at both ends, hardest in the middle, and off on a still frame', () => {
  const slip = { id: 1, from: 'Ts', to: 'Kh' }, card = { id: 1 };
  assert.equal(slipShake(card, slip, 0, 0), 0);
  assert.equal(slipShake(card, slip, 0, SLIP_MS), 0);
  assert.ok(slipShake(card, slip, 0, SLIP_MS / 2) > 0.9);
  assert.equal(slipShake(card, slip, 0, SLIP_MS / 2, 1, true), 0, 'a still frame shows the card, not the tear');
  assert.ok(slipShake(card, slip, 0, SLIP_MS / 2, 0.5) < slipShake(card, slip, 0, SLIP_MS / 2, 1), 'Calm tears less');
  assert.equal(slipShake(card, slip, 0, -50), 0, 'nothing before it begins');
});

test('slipPhase is clamped at both ends', () => {
  assert.equal(slipPhase(-1), 0);
  assert.equal(slipPhase(0), 0);
  assert.equal(slipPhase(SLIP_MS), 1);
  assert.equal(slipPhase(SLIP_MS * 3), 1);
});
