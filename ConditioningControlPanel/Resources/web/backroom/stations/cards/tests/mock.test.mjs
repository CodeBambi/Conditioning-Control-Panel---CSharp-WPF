import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createMockServer } from '../mock-server.js';
import { readHand, readState, classify, owedFor } from '../hand.js';

let seq = 0;
const idem = () => 'mockidem' + String(++seq).padStart(10, '0');
const post = async (m, op, body) => (await m.handle(op, { idem: idem(), ...body })).body;

test('state carries the 10.13.E fields', async () => {
  const m = createMockServer({ sp: 40 });
  const s = (await m.handle('state')).body;
  for (const k of ['ok', 'sp', 'open', 'hand', 'legal', 'hint', 'autoStandAt', 'rules', 'floorMs']) assert.ok(k in s, k);
  assert.deepEqual(readState(s).rules.stakes, [1, 2]);
});

test('a scripted deal opens a hand, a stand settles it against the dealer', async () => {
  let t = 1000;
  const m = createMockServer({ sp: 40, now: () => t });
  m.script('Th', '9d', '8c', '8s');
  const d = await post(m, 'deal', { stake: 2 });
  assert.equal(d.ok, true); assert.equal(d.sp, 38); assert.equal(d.cost, 2);
  const h = readHand(d.hand);
  assert.deepEqual([h.hands[0].cards, h.dealer, h.done], [['Th', '8c'], ['9d'], false]);
  assert.deepEqual(d.legal, ['hit', 'stand', 'double']);
  const st = await post(m, 'stand', { handId: h.id, step: h.step });
  assert.equal(st.hand.done, true); assert.equal(st.hand.result.hands[0].outcome, 'win');
  assert.equal(st.returned, 4); assert.equal(st.sp, 42); assert.equal(owedFor(st), 4);
  t += 1000;
  const fast = await post(m, 'deal', { stake: 1 });
  assert.equal(classify({ ok: true, body: fast }).kind, 'wait'); assert.equal(fast.retryInMs, 4000);
});

test('blackjack on the deal settles at once and pays 2:1', async () => {
  const m = createMockServer({ sp: 10 });
  m.script('As', '9d', 'Kh', '7c');
  const d = await post(m, 'deal', { stake: 1 });
  assert.equal(d.hand.done, true); assert.deepEqual(d.hand.dealer, ['9d', '7c']);
  assert.equal(d.hand.result.hands[0].outcome, 'blackjack'); assert.equal(d.returned, 3); assert.equal(d.sp, 12);
});

test('split, double after split, stale and illegal refusals carry the hand', async () => {
  const m = createMockServer({ sp: 20 });
  m.script('8h', '6d', '8c', 'Ts', '3s', 'Td', '9c');
  const d = await post(m, 'deal', { stake: 1 });
  assert.ok(d.legal.includes('split'));
  const sp = await post(m, 'split', { handId: d.hand.id, step: 0 });
  assert.deepEqual(sp.hand.hands.map((x) => x.cards), [['8h', '3s'], ['8c', 'Td']]); assert.equal(sp.cost, 1);
  const stale = await post(m, 'hit', { handId: d.hand.id, step: 0 });
  assert.equal(stale.reason, 'stale'); assert.equal(stale.hand.step, 1);
  const dbl = await post(m, 'double', { handId: d.hand.id, step: 1 });
  assert.equal(dbl.hand.hands[0].doubled, true); assert.equal(dbl.hand.active, 1);
  const ill = await post(m, 'split', { handId: d.hand.id, step: 2 });
  assert.equal(ill.reason, 'illegal'); assert.ok(ill.hand && Array.isArray(ill.legal));
});

test('receipts replay byte for byte; faults, the door and auto-stand', async () => {
  const m = createMockServer({ sp: 20 });
  m.script('9h', '6d', '7c', 'Ts', '2c');
  const id = idem();
  const a = (await m.handle('deal', { idem: id, stake: 1 })).body, b = (await m.handle('deal', { idem: id, stake: 1 })).body;
  assert.deepEqual(a, b);
  assert.equal((await post(m, 'deal', { stake: 1 })).reason, 'hand_open');
  m.fail('hit', 'busy', 1);
  assert.equal((await m.handle('hit', { idem: idem(), handId: a.hand.id, step: 0 })).body.reason, 'busy');
  m.fail('hit', 'timeout', 1, { apply: true });
  assert.deepEqual(await m.handle('hit', { idem: idem(), handId: a.hand.id, step: 0 }), { ok: false, status: 0, reason: 'timeout' });
  m.age();
  const auto = await post(m, 'hit', { handId: a.hand.id, step: 1 });
  assert.equal(auto.reason, 'auto_stood'); assert.equal(auto.hand.done, true);
  assert.equal((await post(m, 'stand', { handId: a.hand.id, step: 1 })).reason, 'no_hand');
  assert.equal((await post(m, 'deal', { stake: 3 })).reason, 'bad_request');
  m.setOpen(false);
  assert.equal((await m.handle('state')).status, 403);
});

test('insufficient SP refuses the deal', async () => {
  const m = createMockServer({ sp: 1, floorMs: 0 });
  assert.equal((await post(m, 'deal', { stake: 2 })).reason, 'insufficient');
});
