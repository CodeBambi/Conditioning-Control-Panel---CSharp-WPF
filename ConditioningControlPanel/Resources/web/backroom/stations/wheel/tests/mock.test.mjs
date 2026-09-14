import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createMockServer } from '../mock-server.js';
import { layoutOf, resultIndex, readResult, countdown } from '../wheel.js';

const DAY = 86400000, T0 = Date.parse('2026-09-14T10:00:00Z');
const idem = n => `wheelidem${String(n).padStart(8, '0')}`;
function clocked(opts = {}) { let now = T0; const s = createMockServer({ now: () => now, ...opts }); return { s, set: v => { now = v; }, get now() { return now; } }; }

test('state has the binding shape, and the page can draw it', async () => {
  const { s } = clocked();
  const { ok, status, body } = await s.handle('state');
  assert.ok(ok && status === 200 && body.ok);
  for (const k of ['sp', 'open', 'day', 'spun', 'result', 'snoozeCarry', 'nextResetAt', 'jackpot', 'slices', 'floorMs']) assert.ok(k in body, k);
  assert.equal(body.day, '2026-09-14');
  assert.equal(body.nextResetAt, '2026-09-15T00:00:00.000Z');
  assert.deepEqual(Object.keys(body.jackpot).sort(), ['amount', 'eligible', 'mustHit', 'odds', 'wonToday']);   // mustHit: 10.16.E
  assert.equal(body.floorMs, 3000);
  const L = layoutOf(body.slices);
  assert.equal(L.length, 14);
  assert.equal(body.slices[0].pay, 250, "the jackpot slice's pay is the day's pot");
  assert.match(body.slices[1].odds, /^1 in [\d,]+$/);
  assert.equal(countdown(body.nextResetAt, T0).text, '14:00:00');
});

test('a spin: the result names its slice, pays, and a second spin is already_spun with the stored result', async () => {
  const { s } = clocked({ sp: 57 });
  s.script('glow');
  const a = (await s.handle('spin', { idem: idem(1) })).body;
  assert.ok(a.ok);
  for (const k of ['day', 'sliceId', 'sliceIndex', 'pay', 'snoozeCarryPaid', 'jackpot', 'jackpotFallback', 'snoozed', 'total', 'capped']) assert.ok(k in a.result, k);
  const L = layoutOf((await s.handle('state')).body.slices);
  assert.equal(L[resultIndex(L, a.result)].id, 'glow');
  assert.equal(a.sp, 62);
  const again = (await s.handle('spin', { idem: idem(2) })).body;
  assert.equal(again.reason, 'already_spun');
  assert.deepEqual(again.result, a.result);
  for (const k of ['sp', 'jackpot', 'snoozeCarry', 'nextResetAt']) assert.ok(k in again, k);
  assert.deepEqual((await s.handle('spin', { idem: idem(1) })).body, a, 'a seen idem replays the receipt');
  const st = (await s.handle('state')).body;
  assert.ok(st.spun && st.result.sliceId === 'glow');
});

test('Snooze stacks a carry that the next paying spin collects, across injected days', async () => {
  const c = clocked({ sp: 10 });
  c.s.script('snooze');
  const a = (await c.s.handle('spin', { idem: idem(1) })).body;
  assert.ok(a.result.snoozed && a.result.pay === 0 && a.snoozeCarry === 2 && a.sp === 10);
  c.set(T0 + DAY); c.s.script('snooze');
  assert.equal((await c.s.handle('spin', { idem: idem(2) })).body.snoozeCarry, 4);
  c.set(T0 + 2 * DAY); c.s.script('twinkle');
  const b = (await c.s.handle('spin', { idem: idem(3) })).body;
  assert.equal(readResult(b.result).total, 7);
  assert.ok(b.result.snoozeCarryPaid === 4 && b.snoozeCarry === 0 && b.sp === 17);
});

test('jackpot states: pot growth, a win resets it, a young account or a taken pot pays Dazed and prints never', async () => {
  const c = clocked({ sp: 0 });
  c.s.potAge(12);
  let st = (await c.s.handle('state')).body;
  assert.equal(st.jackpot.amount, 550);
  c.s.script('jackpot');
  const won = (await c.s.handle('spin', { idem: idem(1) })).body;
  assert.ok(won.result.jackpot === true && won.result.pay === 550 && won.jackpot.wonToday);
  st = (await c.s.handle('state')).body;
  assert.equal(st.slices[0].odds, 'never', 'taken today');

  const y = clocked({ eligible: false });
  y.s.script('jackpot');
  st = (await y.s.handle('state')).body;
  assert.ok(st.jackpot.eligible === false && st.slices[0].odds === 'never');
  const fb = (await y.s.handle('spin', { idem: idem(1) })).body.result;
  assert.ok(fb.sliceId === 'dazed' && fb.jackpotFallback && !fb.jackpot && fb.pay === 100);

  const capped = clocked({ sp: 99990 });
  capped.s.script('dazed');
  const cp = (await capped.s.handle('spin', { idem: idem(1) })).body;
  assert.ok(cp.result.capped && cp.sp === 99999 && cp.result.total === 100);
  c.s.potAge(200);
  assert.equal((await c.s.handle('state')).body.jackpot.amount, 1000, 'cap');
});

test('refusals: busy and too_fast are HTTP 200, closed is 403, a bad idem is bad_request, host faults have no body', async () => {
  const { s } = clocked();
  s.fail('spin', 'busy');
  assert.deepEqual(await s.handle('spin', { idem: idem(1) }), { ok: true, status: 200, body: { ok: false, reason: 'busy' } });
  s.fail('spin', 'too_fast', 1, { body: { retryInMs: 900 } });
  assert.equal((await s.handle('spin', { idem: idem(1) })).body.retryInMs, 900);
  assert.equal((await s.handle('spin', { idem: 'short' })).body.reason, 'bad_request');
  s.fail('spin', 'timeout', 1, { apply: true });
  assert.deepEqual(await s.handle('spin', { idem: idem(2) }), { ok: false, status: 0, reason: 'timeout' });
  assert.ok((await s.handle('spin', { idem: idem(2) })).body.ok, 'the lost reply was applied; the retry replays it');
  s.setOpen(false);
  assert.deepEqual(await s.handle('state'), { ok: true, status: 403, body: { ok: false, reason: 'closed' } });
});

test('the day turns at 00:00 UTC on the injected clock', async () => {
  const c = clocked();
  c.set(Date.parse('2026-09-14T23:59:59Z'));
  await c.s.handle('spin', { idem: idem(1) });
  assert.ok((await c.s.handle('state')).body.spun);
  c.set(Date.parse('2026-09-15T00:00:00Z'));
  const st = (await c.s.handle('state')).body;
  assert.ok(!st.spun && st.result === null && st.day === '2026-09-15');
});

/* ------------------------------------ C2 must-hit-by in the mock (10.16.E) */

test('the pot climbs to the cap, and mustHit turns on exactly there', async () => {
  const day = 20000;
  const at = (age) => {
    const m = createMockServer({ now: () => (day + age) * 86400000, potSeedDay: day });
    return m.handle('state').then(r => r.body.jackpot);
  };
  assert.deepEqual(await at(0).then(j => [j.amount, j.mustHit]), [250, false]);
  assert.deepEqual(await at(29).then(j => [j.amount, j.mustHit]), [975, false]);
  assert.deepEqual(await at(30).then(j => [j.amount, j.mustHit]), [1000, true], 'the cap is the must-hit line');
  assert.deepEqual(await at(45).then(j => [j.amount, j.mustHit]), [1000, true], 'and it stays there');
});

test('an eligible spin on the must-hit day takes the pot, and the pot re-seeds', async () => {
  const server = createMockServer({ sp: 0 });
  server.mustHit();
  const before = (await server.handle('state')).body.jackpot;
  assert.equal(before.mustHit, true);
  assert.equal(before.amount, 1000);
  const spin = (await server.handle('spin', { idem: 'mustHitTakesThePot01' })).body;
  assert.equal(spin.result.sliceId, 'jackpot');
  assert.equal(spin.result.jackpot, true);
  assert.equal(spin.result.pay, 1000);
  assert.equal(spin.jackpot.mustHit, false, 'the pot is taken, so nothing must fall any more');
  assert.equal(spin.jackpot.amount, 250, 'tomorrow starts at 250 again');
});

test('a pot already taken today is not must-hit, whatever the day', async () => {
  const server = createMockServer();
  server.mustHit();
  server.wonToday();
  assert.equal((await server.handle('state')).body.jackpot.mustHit, false);
});

test('a young account is never forced: the must-hit day does not mint it a pot', async () => {
  const server = createMockServer({ eligible: false });
  server.mustHit();
  const j = (await server.handle('state')).body.jackpot;
  assert.equal(j.mustHit, true, 'the room fact holds for everyone');
  assert.equal(j.eligible, false);
  const spin = (await server.handle('spin', { idem: 'youngOnTheMustHitDay1' })).body;
  assert.notEqual(spin.result.sliceId, 'jackpot');
  assert.equal((await server.handle('state')).body.jackpot.mustHit, true, 'and the pot is still there for someone else');
});
