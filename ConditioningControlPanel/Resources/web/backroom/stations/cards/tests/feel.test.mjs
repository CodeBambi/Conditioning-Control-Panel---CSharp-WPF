import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readHand } from '../hand.js';
import { TIMING, momentOf, isBloom, aceSlot, bestCard, vortexOf, resultLines, planSteps, fanCard, lampBreath, screenHoldMs } from '../feel.js';
import { MOMENTS } from '../../../shared/hypno/moments.js';

const H = (o) => readHand({ step: 0, stake: 1, active: 0, done: false, result: null, ...o });
const R = (hands, extra = {}) => {
  const returned = hands.reduce((s, h) => s + h.paid, 0), wagered = hands.reduce((s, h) => s + h.bet, 0);
  return { hands, dealerTotal: 17, dealerBlackjack: false, wagered, returned, net: returned - wagered, ...extra };
};
const hand = (cards, o = {}) => ({ cards, bet: 1, done: true, doubled: false, split: false, ...o });

const bj = H({ id: 'h_bj', dealer: ['9d', '7s'], hands: [hand(['Kh', 'As'])], done: true,
  result: R([{ outcome: 'blackjack', bet: 1, paid: 3, total: 21 }]) });
const lose = H({ id: 'h_l', dealer: ['Td', '9s'], hands: [hand(['Th', '8c'])], done: true, result: R([{ outcome: 'lose', bet: 1, paid: 0, total: 18 }], { dealerTotal: 19 }) });
const push = H({ id: 'h_p', dealer: ['Td', '8s'], hands: [hand(['Th', '8c'])], done: true, result: R([{ outcome: 'push', bet: 1, paid: 1, total: 18 }], { dealerTotal: 18 }) });

test('moment ids exist in the kit table and follow result.net', () => {
  for (const id of ['cards.sit', 'cards.bloom', 'cards.win', 'cards.lose', 'cards.push']) assert.ok(MOMENTS[id], id);
  assert.equal(momentOf(bj), 'cards.win'); assert.equal(momentOf(lose), 'cards.lose'); assert.equal(momentOf(push), 'cards.push');
  assert.equal(momentOf(H({ id: 'o', dealer: ['9d'], hands: [hand(['9h', '5c'], { done: false })] })), null);
});

test('bloom: only a paid player blackjack, never a push against a dealer blackjack or a split 21', () => {
  assert.equal(isBloom(bj), true); assert.equal(aceSlot(bj), 1);
  const bjPush = H({ ...bj, id: 'x', dealer: ['Ad', 'Ks'], result: R([{ outcome: 'push', bet: 1, paid: 1, total: 21 }], { dealerBlackjack: true, dealerTotal: 21 }) });
  assert.equal(isBloom(bjPush), false);
  assert.equal(isBloom(lose), false);
});

test('bestCard: ace highest, first of equals, winning hands only', () => {
  assert.equal(bestCard(bj), 'As');
  const split = H({ id: 's', dealer: ['9d', '8s'], hands: [hand(['8h', 'Kc'], { split: true }), hand(['8d', 'Qs'], { split: true })], done: true,
    result: R([{ outcome: 'lose', bet: 1, paid: 0, total: 18 }, { outcome: 'win', bet: 1, paid: 2, total: 18 }], { dealerTotal: 17 }) });
  assert.equal(bestCard(split), 'Qs', 'the losing hand\'s king does not count');
  assert.equal(bestCard(lose), null);
});

test('chip vortex direction and count', () => {
  assert.deepEqual(vortexOf(bj), { dir: 1, n: 2 });
  assert.deepEqual(vortexOf(lose), { dir: -1, n: 1 });
  assert.equal(vortexOf(push), null);
});

test('every result has a text line; a split adds the net', () => {
  assert.deepEqual(resultLines(bj).map((l) => l.key), ['br_cards_res_blackjack']);
  assert.equal(resultLines(bj)[0].vars.n, 2);
  assert.deepEqual(resultLines(lose)[0].vars, { p: 18, d: 19, n: 1 });
  assert.equal(resultLines(push)[0].key, 'br_cards_res_push');
  const bust = H({ id: 'b', dealer: ['9d', '8s'], hands: [hand(['Th', '6c', '9d'])], done: true, result: R([{ outcome: 'bust', bet: 1, paid: 0, total: 25 }]) });
  assert.equal(resultLines(bust)[0].key, 'br_cards_res_bust');
  const dbust = H({ id: 'db', dealer: ['9d', '8s', '7c'], hands: [hand(['Th', '6c'])], done: true, result: R([{ outcome: 'win', bet: 1, paid: 2, total: 16 }], { dealerTotal: 24 }) });
  assert.equal(resultLines(dbust)[0].key, 'br_cards_res_dealer_bust');
  const split = H({ id: 's', dealer: ['9d', '8s'], hands: [hand(['8h', 'Kc'], { split: true }), hand(['8d', '5s', '6c'], { split: true, bet: 2, doubled: true })], done: true,
    result: R([{ outcome: 'win', bet: 1, paid: 2, total: 18 }, { outcome: 'charlie', bet: 2, paid: 4, total: 19 }]) });
  const lines = resultLines(split);
  assert.deepEqual(lines.map((l) => l.key), ['br_cards_res_win', 'br_cards_res_charlie', 'br_cards_res_net_up']);
  assert.equal(lines[0].prefix.vars.i, 1); assert.equal(lines[2].vars.n, 3);
  for (const l of [...resultLines(bj), ...lines, ...resultLines(lose)]) assert.ok(/^br_cards_/.test(l.key) && !/\u2014/.test(l.fallback));
});

test('planSteps: a fresh deal goes player, dealer up, player, hole, then waits for a decision', () => {
  const next = H({ id: 'h1', dealer: ['9d'], hands: [hand(['Th', '4c'], { done: false })] });
  const steps = planSteps(null, next);
  assert.deepEqual(steps.map((s) => s.op), ['clear', 'card', 'card', 'card', 'card', 'active', 'ready']);
  assert.deepEqual(steps.filter((s) => s.op === 'card').map((s) => [s.owner, s.slot, s.code]), [[0, 0, 'Th'], ['d', 0, '9d'], [0, 1, '4c'], ['d', 1, null]]);
  const hole = steps[4];
  assert.equal(hole.at - steps[1].at, TIMING.dealGapMs * 3);
  assert.equal(steps.at(-1).at, hole.at + TIMING.flyMs + TIMING.flipMs, 'decisions go live once the hole card has landed');
});

test('planSteps: a blackjack blooms the frame the second player card has turned, before the hole card turns', () => {
  const steps = planSteps(null, bj);
  const second = steps.find((s) => s.op === 'card' && s.owner === 0 && s.slot === 1);
  const bloom = steps.find((s) => s.op === 'bloom'), reveal = steps.find((s) => s.op === 'reveal'), settle = steps.at(-1);
  assert.equal(bloom.at, second.at + TIMING.flyMs + TIMING.flipMs);
  assert.equal(reveal.at, bloom.at + TIMING.bjRevealMs); assert.equal(reveal.code, '7s');
  assert.equal(settle.op, 'settle'); assert.ok(settle.at > reveal.at);
  assert.ok(!planSteps(null, lose).some((s) => s.op === 'bloom'));
  for (let i = 1; i < steps.length; i++) assert.ok(steps[i].at >= steps[i - 1].at, 'sorted');
});

test('planSteps: a hit adds one card; a stand that finishes turns the hole and draws for the dealer', () => {
  const shown = H({ id: 'h1', dealer: ['9d'], hands: [hand(['Th', '4c'], { done: false })] });
  const hit = H({ id: 'h1', step: 1, dealer: ['9d'], hands: [hand(['Th', '4c', '3s'], { done: false })] });
  assert.deepEqual(planSteps(shown, hit).map((s) => s.op), ['card', 'ready']);
  const stood = H({ id: 'h1', step: 2, dealer: ['9d', '5s', '4h'], hands: [hand(['Th', '4c', '3s'])], done: true,
    result: R([{ outcome: 'lose', bet: 1, paid: 0, total: 17 }], { dealerTotal: 18 }) });
  const s = planSteps(hit, stood);
  assert.deepEqual(s.map((x) => x.op), ['reveal', 'card', 'settle']);
  assert.deepEqual([s[1].owner, s[1].slot, s[1].code], ['d', 2, '4h']);
});

test('planSteps: a split slides the pair apart, deals each a card and moves the active hand', () => {
  const shown = H({ id: 'h1', dealer: ['6d'], hands: [hand(['8h', '8c'], { done: false })] });
  const next = H({ id: 'h1', step: 1, dealer: ['6d'], active: 0, hands: [hand(['8h', '3s'], { done: false, split: true }), hand(['8c', 'Td'], { done: false, split: true })] });
  const s = planSteps(shown, next);
  assert.deepEqual(s.map((x) => x.op), ['split', 'card', 'card', 'ready']);
  assert.deepEqual(s.filter((x) => x.op === 'card').map((x) => [x.owner, x.slot, x.code]), [[0, 1, '3s'], [1, 1, 'Td']]);
  const stand0 = H({ ...next, step: 2, active: 1, hands: [{ ...next.hands[0], done: true }, next.hands[1]] });
  assert.deepEqual(planSteps(next, stand0).map((x) => x.op), ['active', 'ready']);
});

test('planSteps still (Calm, reduced): the settled state at 0, same order; a bloom keeps its gaps to the reveal and settle', () => {
  const s = planSteps(null, bj, { still: true }), at = (op) => s.find((x) => x.op === op).at;
  assert.ok(s.filter((x) => !['reveal', 'settle'].includes(x.op)).every((x) => x.at === 0));
  assert.deepEqual(s.map((x) => x.op), ['clear', 'card', 'card', 'card', 'card', 'bloom', 'reveal', 'settle']);
  assert.equal(at('reveal'), TIMING.bjRevealMs); assert.ok(at('settle') - at('bloom') >= 360, 'the win wash clears the host wash gap');
  assert.ok(planSteps(null, lose, { still: true }).every((x) => x.at === 0), 'no bloom: everything at 0');
});

test('sit fan: out, face up in a row, back into the shoe inside 4.4 s; still fades in place', () => {
  assert.equal(fanCard(0, 0).visible, false);
  const hold = Array.from({ length: 13 }, (_, i) => fanCard(i, 2200));
  assert.ok(hold.every((c) => c.visible && c.p === 1 && c.flip === 1), 'all thirteen face up at 2.2 s');
  assert.ok(Array.from({ length: 13 }, (_, i) => fanCard(i, TIMING.sitMs)).every((c) => !c.visible), 'all back at 4.4 s');
  assert.ok(fanCard(12, 3900).flip < 1, 'turning face down on the way back');
  assert.equal(fanCard(3, 1200, true).alpha, 1); assert.equal(fanCard(3, TIMING.sitStillMs, true).visible, false);
});

test('the lamp breathes six times a minute and rests while held', () => {
  assert.equal(lampBreath(0), 0.5);
  assert.ok(Math.abs(lampBreath(2500) - 1) < 1e-9); assert.ok(Math.abs(lampBreath(7500)) < 1e-9);
  assert.equal(lampBreath(2500, true), 0.5);
});

test('screenHoldMs: the bloom, the win wash and the losing edges hold the next deal; gates off hold nothing', () => {
  assert.equal(screenHoldMs('cards.bloom', { fired: 2 }), TIMING.bloomMs);
  assert.equal(screenHoldMs('cards.bloom', { fired: 2, still: true }), 2400, 'Calm: the host plays the picture at 60%');
  assert.equal(screenHoldMs('cards.bloom', { fired: 0 }), 0, 'flash off: no picture, no hold');
  assert.equal(screenHoldMs('cards.win', { fired: 1 }), 900, 'the wash is gone at 900 ms');
  assert.equal(screenHoldMs('cards.win', { fired: 0 }), 0);
  assert.equal(screenHoldMs('cards.lose', { tunnel: true }), MOMENTS['cards.lose'].host[0].ms + 150, 'the breath of tunnel, 2600 ms, and its closing post');
  assert.equal(screenHoldMs('cards.lose', { tunnel: false }), 0, 'tunnel gate off: no edges, no hold');
  assert.equal(screenHoldMs('cards.push', { fired: 0, tunnel: true }), 0);
  assert.equal(screenHoldMs('cards.sit', { fired: 0 }), 0);
});
