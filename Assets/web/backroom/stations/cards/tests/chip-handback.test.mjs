import test from 'node:test';
import assert from 'node:assert/strict';
import { createBankRun } from '../../../shared/win/bank.js';
import { shownSp, owedFor } from '../hand.js';

/* (c) tester, 2026-09-18: the chip read 0 after a win until the player stood up. The settle pins the chip to
 * `before` (server minus owed: 0 when the stake was the whole balance) and hands the number back through THE BANK.
 * These pin the two halves the station's frame guard relies on: every run ends by handing the settled value
 * back, and the rule's number (shownSp) is never below zero and equals the server once nothing is owed. */

const pay = (o) => createBankRun({ kind: 'pay', n: 3, fromValue: 0, toValue: 6, startMs: 0, ...o });
const ticks = (evs) => evs.filter((e) => e.type === 'tick').map((e) => e.value);

test('a pay from 0 (an all-in stake) ends on the settled balance with a done, on every exit', () => {
  const quiet = pay().skip();
  assert.deepEqual(ticks(quiet), [6], 'Back/suspend: the last tick is the real balance, not 0');
  assert.equal(quiet.at(-1).type, 'done', 'done hands the chip back to the rule');
  const landed = pay().skip({ land: true });
  assert.deepEqual(ticks(landed), [6]); assert.equal(landed.at(-1).type, 'done');
  const reduced = pay({ reduced: true }).step(0);
  assert.deepEqual(ticks(reduced.events), [6]); assert.equal(reduced.done, true);
  assert.equal(reduced.events.at(-1).type, 'done', 'the STATE path hands the chip back too');
});

test('a run cut mid-flight after a merge lands on the newer total, and a finished run answers nothing', () => {
  const r = pay();
  r.step(1);
  assert.equal(r.merge(9), 'merged');
  const out = r.skip();
  assert.deepEqual(ticks(out), [9], 'a second win re-aims the settled value');
  assert.equal(out.at(-1).type, 'done');
  assert.deepEqual(r.skip(), [], 'no second hand-back: the chip is already the room\'s');
});

test('the rule: the shown number is never below zero, and equals the server balance once nothing is owed', () => {
  assert.equal(shownSp(6, 6), 0, 'an all-in stake reads 0 while the win is still owed');
  assert.equal(shownSp(6, 0), 6, 'the settle clears owed: the real balance');
  assert.equal(shownSp(3, 9), 0, 'never negative');
  assert.equal(shownSp('6', undefined), 6);
  const hand = { id: 'h1', step: 2, stake: 3, dealer: ['Kh', '7s'], active: 0, done: true,
    hands: [{ cards: ['As', 'Kd'], bet: 3, done: true }], result: { net: 3, returned: 6, hands: [{ outcome: 'win', net: 3, returned: 6 }] } };
  assert.equal(owedFor({ ok: true, sp: 6, spBefore: 3, cost: 3, hand }), 6, 'an all-in win: the whole return is owed until the settle frame');
  assert.equal(owedFor({ ok: true, sp: 6, hand }), 6, 'a reply without spBefore/cost still owes no more than the balance moved');
  assert.equal(owedFor({ ok: true, sp: 6, hand: { ...hand, done: false, result: undefined } }), 0, 'an open hand owes nothing');
});
