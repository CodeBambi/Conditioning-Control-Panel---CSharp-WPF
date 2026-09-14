import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  cardValue, rankLabel, suitOf, totalOf, readHand, readState, legalOf, defaultStake, controls, classify, createIntent, mayRetry,
  moveBody, owedFor, shownSp, readHintPref, writeHintPref, HINT_KEY, RETRY,
} from '../hand.js';

const open = { id: 'h_1_x', step: 0, stake: 1, dealer: ['9d'], hands: [{ cards: ['Th', '4c'], bet: 1, done: false, doubled: false, split: false, total: 14, soft: false }],
  active: 0, done: false, result: null };
const settled = { id: 'h_2_y', step: 1, stake: 2, dealer: ['9d', '8s'], hands: [{ cards: ['Th', '9c'], bet: 2, done: true, doubled: false, split: false }],
  active: 0, done: true, result: { hands: [{ outcome: 'win', bet: 2, paid: 4, total: 19 }], dealerTotal: 17, dealerBlackjack: false, wagered: 2, returned: 4, net: 2 } };

test('card codes read as ranks, values and suits', () => {
  assert.equal(cardValue('As'), 1); assert.equal(cardValue('Td'), 10); assert.equal(cardValue('Kc'), 10); assert.equal(cardValue('7h'), 7);
  assert.equal(cardValue('1x'), 0);
  assert.equal(rankLabel('Td'), '10'); assert.equal(rankLabel('Qh'), 'Q');
  assert.deepEqual(suitOf('Qh'), { glyph: '♥', red: true }); assert.equal(suitOf('Zz'), null);
  assert.deepEqual(totalOf(['As', '6d']), { total: 17, soft: true });
  assert.deepEqual(totalOf(['As', '6d', '9c']), { total: 16, soft: false });
});

test('readHand accepts publicHand and refuses anything else', () => {
  const h = readHand(open);
  assert.equal(h.id, 'h_1_x'); assert.equal(h.hands[0].cards.length, 2); assert.equal(h.done, false); assert.equal(h.result, null);
  assert.equal(readHand(settled).result.net, 2);
  assert.equal(readHand(null), null);
  assert.equal(readHand({ ...open, dealer: ['xx'] }), null);
  assert.equal(readHand({ ...open, hands: [] }), null);
  assert.equal(readHand({ ...settled, result: null }), null, 'a done hand needs its result');
  assert.equal(readHand({ ...open, active: 7 }).active, 0);
});

test('readState defaults a bare body to an empty table on stakes 1 and 2', () => {
  const s = readState({ ok: true, sp: 40, hand: open, legal: ['hit', 'stand', 'fly'], hint: 'stand', floorMs: 8000, rules: { stakes: [1, 2] } });
  assert.deepEqual(s.legal, ['hit', 'stand']);
  assert.equal(s.hint, 'stand'); assert.equal(s.hand.id, 'h_1_x');
  const e = readState({});
  assert.equal(e.hand, null); assert.deepEqual(e.rules.stakes, [1, 2]); assert.equal(e.floorMs, 8000);
  assert.deepEqual(legalOf('hit'), []);
});

test('the bet chip starts on 1 below 30 SP, else the largest stake the balance covers', () => {
  assert.equal(defaultStake(29), 1);
  assert.equal(defaultStake(30), 2);
  assert.equal(defaultStake(0), 1);
  assert.equal(defaultStake(500, [1, 2]), 2);
});

test('controls: moves from legal only while a hand is open, deal only when the table is free', () => {
  const base = { phase: 'play', legal: ['hit', 'stand', 'double'], sp: 40, stake: 2, now: 1000 };
  let c = controls({ ...base, hand: readHand(open) });
  assert.deepEqual(c.moves, { hit: true, stand: true, double: true, split: false });
  assert.equal(c.deal, false); assert.equal(c.dealWhy, 'open'); assert.equal(c.sit, false); assert.equal(c.bet, false);
  c = controls({ ...base, hand: readHand(settled) });
  assert.equal(c.deal, true); assert.equal(c.sit, true); assert.equal(c.moves.hit, false);
  assert.equal(controls({ ...base, hand: null, sp: 1 }).dealWhy, 'sp');
  assert.equal(controls({ ...base, hand: null, dealReadyAt: 5000 }).dealWhy, 'floor');
  assert.equal(controls({ ...base, hand: null, screenUntil: 1001 }).dealWhy, 'screen', 'a running fullscreen moment holds the deal');
  assert.equal(controls({ ...base, hand: null, screenUntil: 1001, dealReadyAt: 5000, sp: 1 }).dealWhy, 'screen', 'the moment reads first');
  assert.equal(controls({ ...base, hand: null, screenUntil: 1000 }).deal, true, 'and lets go the frame it ends');
  assert.equal(controls({ ...base, hand: null, screenUntil: 1001 }).sit, true, 'sitting back down opens no decision');
  assert.equal(controls({ ...base, hand: null, animating: true }).dealWhy, 'busy');
  assert.equal(controls({ ...base, hand: null, animating: true, screenUntil: 1001 }).dealWhy, 'screen', 'a bloom mid-deal reads as the moment');
  assert.equal(controls({ ...base, hand: null, busy: true, screenUntil: 1001 }).deal, false);
  assert.equal(controls({ ...base, hand: readHand(open), busy: true }).moves.hit, false);
  assert.equal(controls({ ...base, phase: 'sit', hand: null }).deal, false);
});

test('classify: retries keep the idem, stale adopts, closed and too_fast', () => {
  assert.equal(classify({ ok: true, status: 200, body: { ok: true } }).kind, 'ok');
  assert.equal(classify({ ok: true, status: 403, body: { ok: false, reason: 'closed' } }).kind, 'closed');
  assert.deepEqual([classify({ ok: true, status: 200, body: { ok: false, reason: 'busy' } }).kind, classify({ ok: false, reason: 'timeout' }).kind], ['retry', 'retry']);
  const fast = classify({ ok: true, status: 200, body: { ok: false, reason: 'too_fast', retryInMs: 4200 } });
  assert.equal(fast.kind, 'wait'); assert.equal(fast.waitMs, 4200);
  assert.equal(classify({ ok: true, body: { ok: false, reason: 'too_fast', retryInMs: 999999 } }).waitMs, RETRY.fastCapMs);
  for (const r of ['stale', 'illegal', 'hand_open', 'auto_stood']) assert.equal(classify({ ok: true, body: { ok: false, reason: r } }).kind, 'adopt', r);
  assert.deepEqual(['no_hand', 'bad_request', 'bad_op'].map((r) => classify({ ok: true, body: { ok: false, reason: r } }).kind), ['refresh', 'refresh', 'closed']);
  assert.equal(classify({ ok: true, body: { ok: false, reason: 'insufficient', sp: 0 } }).kind, 'insufficient');
  assert.equal(classify({ ok: false, reason: 'offline' }).kind, 'failed');
  assert.equal(classify(null).kind, 'failed');
});

test('an intent mints its idem once and counts its retries', () => {
  let n = 0;
  const it = createIntent('hit', moveBody(readHand(open)), () => 'idem' + String(++n).padStart(12, '0'));
  assert.deepEqual(it.body, { handId: 'h_1_x', step: 0, idem: 'idem000000000001' });
  const busy = classify({ ok: true, body: { ok: false, reason: 'busy' } });
  assert.equal(mayRetry(it, busy), true); assert.equal(mayRetry(it, busy), true); assert.equal(mayRetry(it, busy), true);
  assert.equal(mayRetry(it, busy), false, 'three retries, then give up');
  assert.equal(it.idem, 'idem000000000001'); assert.equal(n, 1);
  assert.equal(mayRetry(it, classify({ ok: true, body: { ok: false, reason: 'stale' } })), false);
});

test('LAW I: a settled return is owed until it shows, never beyond what was credited', () => {
  assert.equal(owedFor({ ok: true, sp: 42, spBefore: 40, cost: 2, returned: 4, hand: settled }), 4);
  assert.equal(owedFor({ ok: true, sp: 38, spBefore: 40, cost: 2, returned: 0, hand: open }), 0);
  assert.equal(owedFor({ ok: true, sp: 99999, spBefore: 99998, cost: 2, returned: 4, capped: true, hand: settled }), 3, 'capped');
  assert.equal(owedFor({ ok: true, sp: 45, spBefore: 40, cost: 2, returned: 7, hand: settled }), 4, 'an auto-stood return shows at once');
  assert.equal(shownSp(42, 4), 38); assert.equal(shownSp(2, 4), 0);
});

test('the hint preference is off by default and survives a throwing storage', () => {
  const mem = new Map(), store = { getItem: (k) => (mem.has(k) ? mem.get(k) : null), setItem: (k, v) => mem.set(k, v), removeItem: (k) => mem.delete(k) };
  assert.equal(readHintPref(store), false);
  writeHintPref(store, true); assert.equal(mem.get(HINT_KEY), '1'); assert.equal(readHintPref(store), true);
  writeHintPref(store, false); assert.equal(readHintPref(store), false);
  const bad = { getItem() { throw new Error('denied'); }, setItem() { throw new Error('denied'); } };
  assert.equal(readHintPref(bad), false); assert.doesNotThrow(() => writeHintPref(bad, true)); assert.equal(readHintPref(undefined), false);
});
