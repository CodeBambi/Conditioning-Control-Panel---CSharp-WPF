import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createMockServer, CATALOG_V1 } from '../mock-server.js';

const idem = (n) => `mockcounteridem${String(n).padStart(4, '0')}`;
const buy = async (s, prizeId, i, catalogVersion = 1) => (await s.handle('buy', { prizeId, catalogVersion }, idem(i))).body;

test('state has the 10.17.C shape; everything is soon until switched on', async () => {
  const s = createMockServer();
  const b = (await s.handle('state')).body;
  for (const k of ['ok', 'open', 'sp', 'catalogVersion', 'discordLinked', 'prizes', 'catalog', 'delivery']) assert.ok(k in b, k);
  assert.equal(CATALOG_V1.reduce((t, r) => t + r.priceSp, 0), 13935, 'whole shelf');
  assert.ok(b.catalog.every((r) => r.sale === 'soon'));
  s.setOn('*');
  assert.ok((await s.handle('state')).body.catalog.every((r) => r.sale === 'on'));
  const noRole = createMockServer({ on: '*', roleId: false });
  assert.equal((await noRole.handle('state')).body.catalog.find((r) => r.id === 'high_roller').sale, 'soon');
  s.linkDiscord(null);
  const hr = (await s.handle('state')).body.catalog.find((r) => r.id === 'high_roller');
  assert.equal(hr.needs, 'discord');
});

test('buy settles once, receipts replay, grants union', async () => {
  const s = createMockServer({ sp: 5000, on: '*' });
  const a = await buy(s, 'rt_demo', 1);
  assert.deepEqual([a.ok, a.paidSp, a.spBefore, a.sp, a.delivery], [true, 20, 5000, 4980, null]);
  assert.deepEqual(await buy(s, 'rt_demo', 1), a, 'replay');
  assert.equal((await buy(s, 'jackpot_remix', 1)).reason, 'idem_mismatch');
  const b = await buy(s, 'rt_bundle_2', 2);
  assert.equal(b.prizes.grants.filter((g) => g === 'rt.original.00').length, 1);
  assert.equal(b.prizes.revision, 2);
  assert.equal(s.user.counter.netSp, -3620);
});

test('refusal order: bad_input, catalog_changed, unavailable, owned, discord_required, insufficient', async () => {
  const s = createMockServer({ sp: 30, on: 'jackpot_remix,high_roller,flashes_v2', discordId: null });
  assert.equal((await s.handle('buy', { prizeId: 'nope', catalogVersion: 1 }, idem(1))).body.reason, 'bad_input');
  assert.equal((await s.handle('buy', { prizeId: 'rt_demo', catalogVersion: 1 }, 'short')).body.reason, 'bad_input');
  assert.equal((await s.handle('buy', { prizeId: 'rt_demo', catalogVersion: '1' }, idem(2))).body.reason, 'bad_input');
  const cc = await buy(s, 'rt_demo', 3, 9);
  assert.ok(cc.reason === 'catalog_changed' && cc.catalogVersion === 1 && cc.catalog.length === 8);
  assert.equal((await buy(s, 'rt_demo', 4)).reason, 'unavailable');
  assert.ok((await buy(s, 'jackpot_remix', 5)).ok);
  const owned = await buy(s, 'jackpot_remix', 6);
  assert.ok(owned.reason === 'owned' && owned.prizes.grants.includes('fx.jackpot_remix'));
  assert.equal((await buy(s, 'high_roller', 7)).reason, 'discord_required');
  const ins = await buy(s, 'flashes_v2', 8);
  assert.deepEqual([ins.reason, ins.sp], ['insufficient', 15]);
});

test('high_roller records a delivery; reprice bumps the version; faults and the door', async () => {
  const s = createMockServer({ sp: 100, on: '*', delivery: 'not_in_guild' });
  const hr = await buy(s, 'high_roller', 1);
  assert.equal(hr.delivery.status, 'not_in_guild');
  assert.equal((await s.handle('state')).body.delivery.high_roller.status, 'not_in_guild');
  s.reprice('bubbles_v2', 300);
  assert.equal(s.catalogVersion, 2);
  assert.equal((await buy(s, 'bubbles_v2', 2)).reason, 'catalog_changed');
  s.fail('buy', 'busy');
  assert.deepEqual(await s.handle('buy', {}, idem(3)), { ok: true, status: 200, body: { ok: false, reason: 'busy' } });
  s.fail('buy', 'timeout', 1, { apply: true });
  assert.deepEqual(await s.handle('buy', { prizeId: 'rt_demo', catalogVersion: 2 }, idem(4)), { ok: false, status: 0, reason: 'timeout' });
  assert.ok((await buy(s, 'rt_demo', 4, 2)).ok, 'the lost reply was applied; the retry replays');
  s.fail('buy', 'too_fast');
  assert.equal((await s.handle('buy', {}, idem(5))).body.reason, 'too_fast');
  s.setOpen(false);
  assert.deepEqual(await s.handle('state'), { ok: true, status: 200, body: { ok: true, open: false } });
  assert.deepEqual(await s.handle('buy', { prizeId: 'jackpot_remix', catalogVersion: 2 }, idem(6)), { ok: true, status: 403, body: { ok: false, reason: 'closed' } });
});
