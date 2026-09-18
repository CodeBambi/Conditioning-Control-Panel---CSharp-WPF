import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readState, cardOf, deliveryKeyOf, classify, createCounter, LEX, isRt } from '../cards.js';
import { createMockServer, CATALOG_V1 } from '../mock-server.js';

const wait = (ms = 0) => new Promise((r) => setTimeout(r, ms));
let n = 0;
const mint = () => `counteridem${String(++n).padStart(8, '0')}`;

/** A counter on the mock, with a recording spReadout and chime. `hold` parks requests until released. */
function rig(opts = {}, { readout = true } = {}) {
  const server = createMockServer(opts);
  const calls = [], sent = [], held = [];
  let hold = false;
  const request = (op, body, idem) => {
    sent.push({ op, body, idem });
    const go = () => server.handle(op, body, idem);
    if (!hold) return go();
    return new Promise((res) => held.push(() => res(go())));
  };
  const counter = createCounter({
    request, sp: () => server.user.sp, mint, now: () => 1789400000000, onChange: () => calls.push('paint'),
    chime: () => calls.push('chime'),
    spReadout: readout ? { set: (v) => calls.push(['set', v]), owe: () => calls.push('owe'), thud: () => calls.push('thud'), target: () => null } : undefined,
  });
  return { server, counter, calls, sent, held, hold: (v) => { hold = v; }, release: () => { while (held.length) held.shift()(); } };
}
const face = (c, id) => c.view().find((r) => r.id === id);

test('every card face derives from the row and the balance', () => {
  const row = { id: 'x', priceSp: 40, sale: 'on', owned: null, needs: null };
  assert.deepEqual(cardOf(row, 40), { face: 'buy' });
  assert.deepEqual(cardOf(row, 12), { face: 'short', short: 28 });
  assert.deepEqual(cardOf({ ...row, needs: 'discord' }, 999), { face: 'discord' });
  assert.deepEqual(cardOf({ ...row, sale: 'soon', needs: 'discord' }, 999), { face: 'soon' });
  assert.deepEqual(cardOf({ ...row, sale: 'soon', owned: { at: 1, paidSp: 40 } }, 0), { face: 'owned' }, 'owned wins over soon');
  assert.equal(deliveryKeyOf({ id: 'high_roller', owned: { at: 1 } }, { high_roller: { status: 'not_in_guild' } }), 'br_counter_delivery_not_in_guild');
  assert.equal(deliveryKeyOf({ id: 'high_roller', owned: null }, { high_roller: { status: 'pending' } }), null);
  assert.equal(deliveryKeyOf({ id: 'high_roller', owned: { at: 1 } }, { high_roller: { status: 'weird' } }), null);
  assert.ok(isRt('rt_bundle_2') && !isRt('high_roller'));
});

test('state reads in order, with fallbacks for every prize name and blurb', async () => {
  const s = createMockServer({ on: 'high_roller,jackpot_remix', owned: { rt_demo: { at: 5, paidSp: 20 } } });
  const body = (await s.handle('state')).body;
  const st = readState({ ...body, catalog: [...body.catalog].reverse() });
  assert.deepEqual(st.catalog.map((r) => r.id), CATALOG_V1.map((r) => r.id));
  assert.deepEqual(st.catalog.map((r) => r.sale), ['on', 'soon', 'on', 'soon', 'soon', 'soon', 'soon', 'soon']);
  assert.deepEqual(st.grants, ['rt.original.00']);
  for (const r of st.catalog) { assert.ok(LEX[r.nameKey] && LEX[r.blurbKey], r.id); }
  assert.equal(readState({ ok: false, reason: 'closed' }), null);
  assert.equal(readState({ ok: true }), null);
});

test('mixed faces: owned, soon, link Discord first, short by N, buy', async () => {
  const { server, counter } = rig({ sp: 20, on: '*', discordId: null, owned: { jackpot_remix: { at: 1, paidSp: 15 } } });
  server.setOn('jackpot_remix,rt_demo,high_roller,flashes_v2');
  await counter.open();
  assert.equal(counter.phase, 'ready');
  const faces = Object.fromEntries(counter.view().map((r) => [r.id, r.face]));
  assert.deepEqual(faces, { jackpot_remix: 'owned', rt_demo: 'buy', high_roller: 'discord', flashes_v2: 'short', bubbles_v2: 'soon',
    rt_bundle_1: 'soon', rt_bundle_2: 'soon', rt_bundle_3: 'soon' });
  assert.equal(face(counter, 'flashes_v2').short, 10);
  assert.equal(face(counter, 'rt_demo').noteKey, 'br_prize_rt_note', 'the server row carries the RT note');
  assert.equal(face(counter, 'high_roller').noteKey, null);
  assert.equal(counter.ask('flashes_v2'), false, 'short opens no confirm');
  assert.equal(counter.ask('bubbles_v2'), false, 'soon opens no confirm');
  assert.equal(counter.ask('high_roller'), false, 'discord opens no confirm');
});

test('a failed state is closed; the door (200 open:false) is closed', async () => {
  const a = rig({ open: false });
  assert.equal(await a.counter.open(), false);
  assert.equal(a.counter.phase, 'closed');
  assert.equal(classify({ ok: true, status: 200, body: { ok: true, open: false } }).kind, 'closed');
  assert.equal(readState({ ok: true, open: false, catalog: [] }), null);
  const b = rig();
  b.server.fail('state', 'timeout');
  await b.counter.open();
  assert.equal(b.counter.phase, 'closed');
});

test('confirm shows balance after; cancel drops it; success flips to Owned, chip set/null/thud and one chime', async () => {
  const { server, counter, calls, sent } = rig({ sp: 100, on: '*' });
  await counter.open();
  assert.ok(counter.ask('jackpot_remix'));
  assert.deepEqual(face(counter, 'jackpot_remix').confirm.after, 85);
  assert.ok(counter.cancel() && !counter.confirm);
  counter.ask('jackpot_remix');
  const idem = counter.confirm.idem;
  calls.length = 0;
  assert.equal(await counter.buy(), 'ok');
  const buy = sent.find((x) => x.op === 'buy');
  assert.deepEqual(buy.body, { prizeId: 'jackpot_remix', catalogVersion: 1 });
  assert.equal(buy.idem, idem);
  assert.deepEqual(calls.filter((c) => c !== 'paint'), [['set', 85], ['set', null], 'thud', 'chime']);
  assert.equal(face(counter, 'jackpot_remix').face, 'owned');
  assert.ok(face(counter, 'jackpot_remix').flip);
  assert.equal(server.user.sp, 85);
  assert.deepEqual(counter.state.grants, ['fx.jackpot_remix']);
  assert.equal(counter.confirm, null);
});

test('without a room chip the success still chimes and never throws', async () => {
  const { counter, calls } = rig({ sp: 100, on: '*' }, { readout: false });
  await counter.open();
  counter.ask('rt_demo');
  assert.equal(await counter.buy(), 'ok');
  assert.deepEqual(calls.filter((c) => c !== 'paint'), ['chime']);
});

test('busy and a lost reply keep the confirm open with retry, and the retry reuses the idem', async () => {
  const { server, counter, sent, calls } = rig({ sp: 10, on: '*' });
  await counter.open();
  counter.ask('flashes_v2');
  assert.equal(face(counter, 'flashes_v2').face, 'short');
  counter.cancel();
  server.setSp(500);
  counter.ask('flashes_v2');
  const idem = counter.confirm.idem;
  server.fail('buy', 'busy');
  assert.equal(await counter.buy(), 'retry');
  assert.ok(counter.confirm.retry && !counter.confirm.pending && counter.confirm.idem === idem);
  server.fail('buy', 'too_fast');
  assert.equal(await counter.buy(), 'retry');
  assert.ok(counter.confirm.retry && counter.confirm.idem === idem, 'too_fast keeps the confirm too');
  server.fail('buy', 'timeout', 1, { apply: true });   // settled on the server, the reply lost
  assert.equal(await counter.buy(), 'retry');
  assert.equal(server.user.sp, 470);   // 500 less the effect row's 30
  calls.length = 0;
  assert.equal(await counter.buy(), 'ok', 'the same idem replays the receipt');
  assert.deepEqual(sent.filter((x) => x.op === 'buy').map((x) => x.idem), [idem, idem, idem, idem]);
  assert.equal(server.user.sp, 470, 'charged once');
  assert.deepEqual(calls.filter((c) => c !== 'paint'), [['set', 470], ['set', null], 'thud', 'chime']);
});

test('a second press while pending sends nothing', async () => {
  const r = rig({ sp: 100, on: '*' });
  await r.counter.open();
  r.counter.ask('rt_demo');
  r.hold(true);
  const p = r.counter.buy();
  await wait();
  assert.equal(await r.counter.buy(), null);
  assert.equal(r.counter.cancel(), false, 'a pending confirm cannot be cancelled');
  assert.equal(r.counter.ask('jackpot_remix'), false);
  r.release();
  assert.equal(await p, 'ok');
  assert.equal(r.sent.filter((x) => x.op === 'buy').length, 1);
});

for (const [reason, setup, expect] of [
  ['insufficient', (s) => s.setSp(10), (c) => assert.equal(face(c, 'high_roller').face, 'short')],
  ['owned', (s) => { s.user.counter.owned.high_roller = { at: 1, paidSp: 40 }; }, (c) => assert.equal(face(c, 'high_roller').face, 'owned')],
  ['unavailable', (s) => s.setOn('jackpot_remix'), (c) => assert.equal(face(c, 'high_roller').face, 'soon')],
  ['discord_required', (s) => s.linkDiscord(null), (c) => assert.equal(face(c, 'high_roller').face, 'discord')],
]) {
  test(`refusal ${reason}: the confirm closes, the card repaints from a fresh state, nothing charged`, async () => {
    const { server, counter, calls, sent } = rig({ sp: 100, on: '*' });
    await counter.open();
    assert.ok(counter.ask('high_roller'));
    setup(server);
    const before = server.user.sp;
    calls.length = 0;
    assert.equal(await counter.buy(), 'repaint');
    assert.equal(counter.confirm, null);
    expect(counter);
    assert.equal(server.user.sp, before);
    assert.ok(!calls.includes('chime') && !calls.some((c) => Array.isArray(c)), 'no chip, no chime');
    assert.equal(sent.at(-1).op, 'state');
  });
}

test('catalog_changed repaints the new price and asks again with a new idem, never buying on its own', async () => {
  const { server, counter, sent } = rig({ sp: 1000, on: '*' });
  await counter.open();
  counter.ask('bubbles_v2');
  const first = counter.confirm.idem;
  server.reprice('bubbles_v2', 300);
  assert.equal(await counter.buy(), 'catalog');
  assert.equal(sent.filter((x) => x.op === 'buy').length, 1, 'no automatic second buy');
  const c = counter.confirm;
  assert.ok(c && c.prizeId === 'bubbles_v2' && c.priceSp === 300 && c.catalogVersion === 2 && c.asked && c.idem !== first);
  assert.equal(face(counter, 'bubbles_v2').confirm.after, 700);
  assert.equal(server.user.sp, 1000);
  assert.equal(await counter.buy(), 'ok');
  assert.deepEqual(sent.at(-2).body, { prizeId: 'bubbles_v2', catalogVersion: 2 });
  assert.equal(server.user.sp, 700);

  server.reprice('flashes_v2', 5000);
  counter.ask('flashes_v2');
  assert.equal(await counter.buy(), 'catalog');
  assert.equal(counter.confirm, null, 'no longer affordable: the confirm closes');
  assert.equal(face(counter, 'flashes_v2').face, 'short');
});

test('the refresh after a success re-asks an open confirm at a new price, never buying', async () => {
  const r = rig({ sp: 1000, on: '*' });
  await r.counter.open();
  r.counter.ask('jackpot_remix');
  r.hold(true);
  const p = r.counter.buy();
  await wait();
  r.release();
  assert.equal(await p, 'ok');
  await wait();
  assert.equal(r.held.length, 1, 'the follow-up state read is parked');
  assert.ok(r.counter.ask('bubbles_v2'));
  const idem = r.counter.confirm.idem;
  r.server.reprice('bubbles_v2', 300);
  r.release();
  await wait(5);
  const c = r.counter.confirm;
  assert.ok(c.asked && c.priceSp === 300 && c.catalogVersion === 2 && c.idem !== idem);
  assert.equal(face(r.counter, 'bubbles_v2').confirm.after, 685);
  assert.equal(r.sent.filter((x) => x.op === 'buy').length, 1);
});

test('closed during a buy (403) closes the counter', async () => {
  const { server, counter } = rig({ sp: 100, on: '*' });
  await counter.open();
  counter.ask('rt_demo');
  server.fail('buy', 'closed');
  assert.equal(await counter.buy(), 'closed');
  assert.equal(counter.phase, 'closed');
});

test('High Roller success carries the delivery line; state refresh follows its status', async () => {
  const { server, counter } = rig({ sp: 100, on: '*', delivery: 'pending' });
  await counter.open();
  counter.ask('high_roller');
  assert.equal(await counter.buy(), 'ok');
  assert.equal(face(counter, 'high_roller').deliveryKey, 'br_counter_delivery_pending');
  server.setDelivery('granted');
  await counter.open();
  assert.equal(face(counter, 'high_roller').deliveryKey, 'br_counter_delivery_granted');
  assert.equal(face(counter, 'jackpot_remix').deliveryKey, null);
});

test('Back during an in-flight buy: the late reply is dropped, no chip or chime, the next open shows it owned', async () => {
  const r = rig({ sp: 100, on: '*' });
  await r.counter.open();
  r.counter.ask('rt_demo');
  r.hold(true);
  const p = r.counter.buy();
  await wait();
  r.counter.close();
  r.calls.length = 0;
  r.release();
  assert.equal(await p, 'gone');
  assert.deepEqual(r.calls, [], 'nothing painted, set, thudded or chimed after close');
  assert.equal(r.server.user.sp, 80, 'the buy settled on the server');
  r.hold(false);
  await r.counter.open();
  assert.equal(face(r.counter, 'rt_demo').face, 'owned');
  assert.equal(face(r.counter, 'rt_demo').flip, false);
});

test('classify maps host shapes', () => {
  assert.equal(classify({ ok: true, status: 403, body: { ok: false, reason: 'closed' } }).kind, 'closed');
  assert.equal(classify({ ok: false, status: 0, reason: 'timeout' }).kind, 'retry');
  assert.equal(classify(null).kind, 'retry');
  assert.equal(classify({ ok: true, status: 200, body: { ok: false, reason: 'busy' } }).kind, 'retry');
  assert.equal(classify({ ok: true, status: 200, body: { ok: false, reason: 'idem_mismatch' } }).kind, 'refresh');
  assert.equal(classify({ ok: true, status: 200, body: { ok: false, reason: 'catalog_changed' } }).kind, 'catalog');
});

test('THE NUDGE: after the first power-up, a balance short of the other one points at the wheel', async () => {
  // 45 SP: buys flashes_v2 at 30, leaves 15, which cannot reach bubbles_v2 at 30.
  const { counter } = rig({ sp: 45, on: '*' });
  await counter.open();
  assert.equal(counter.nudge, null, 'nothing before a buy');
  counter.ask('flashes_v2');
  assert.equal(await counter.buy(), 'ok');
  assert.equal(counter.nudge && counter.nudge.id, 'bubbles_v2');
});

test('THE NUDGE stays quiet when they can still afford the other one, or own both', async () => {
  const rich = rig({ sp: 500, on: '*' });
  await rich.counter.open();
  rich.counter.ask('flashes_v2');
  assert.equal(await rich.counter.buy(), 'ok');
  assert.equal(rich.counter.nudge, null, '470 left covers the other row');

  // The owner flagged this one: at 30 a row and 60 in hand, 30 left is NOT below 30. By design.
  const exact = rig({ sp: 60, on: '*' });
  await exact.counter.open();
  exact.counter.ask('bubbles_v2');
  assert.equal(await exact.counter.buy(), 'ok');
  assert.equal(exact.counter.nudge, null, 'exactly enough is not short');

  const both = rig({ sp: 45, on: '*', owned: { bubbles_v2: { at: 1, paidSp: 30 } } });
  await both.counter.open();
  both.counter.ask('flashes_v2');
  assert.equal(await both.counter.buy(), 'ok');
  assert.equal(both.counter.nudge, null, 'owning both leaves nothing to be short of');
});

test('THE NUDGE is for power-ups only, and only for the FIRST of them', async () => {
  const other = rig({ sp: 20, on: '*' });
  await other.counter.open();
  other.counter.ask('rt_demo');                       // not a power-up row
  assert.equal(await other.counter.buy(), 'ok');
  assert.equal(other.counter.nudge, null);

  // The second power-up: nothing left to point at, so no line even though the balance is tiny.
  const second = rig({ sp: 35, on: '*', owned: { flashes_v2: { at: 1, paidSp: 30 } } });
  await second.counter.open();
  second.counter.ask('bubbles_v2');
  assert.equal(await second.counter.buy(), 'ok');
  assert.equal(second.counter.nudge, null);
});
