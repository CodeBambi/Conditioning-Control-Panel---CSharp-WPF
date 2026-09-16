// node --test backroom/stations/roulette/tests/ : bets, the client cover-all check, Law I, retries, against mock-server.js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  MAX_CHIPS, spotKind, coversOf, coversAll, addChip, removeChip, chipTotal, betsOf, chipsOf, checkLayout, adoptTape, owed, shownSp,
  cursorOf, readOutcome, classify, spinBody, mintId, IDEM_RE, rowOf, colorName,
} from '../tape.js';
import { createMockServer } from '../mock-server.js';

const server0 = createMockServer();
const { WHEEL, ROSE, SPOTS } = server0;

test('the five bet kinds by spot id, covers from state.rose', () => {
  assert.equal(spotKind('s0'), 'straight'); assert.equal(spotKind('s36'), 'straight'); assert.equal(spotKind('s37'), null);
  assert.equal(spotKind('rose'), 'color'); assert.equal(spotKind('plum'), 'color');
  for (const r of ['sip', 'sink', 'deep']) assert.equal(spotKind(r), 'row');
  assert.equal(spotKind('red'), null);
  assert.deepEqual(coversOf('s17', ROSE), [17]);
  assert.equal(coversOf('rose', ROSE).length, 18); assert.equal(coversOf('plum', ROSE).length, 18);
  assert.ok(!coversOf('plum', ROSE).some((n) => ROSE.includes(n)) && !coversOf('plum', ROSE).includes(0));
  assert.deepEqual(coversOf('sink', ROSE), Array.from({ length: 12 }, (_, i) => 13 + i));
  assert.equal(rowOf(0), null); assert.equal(rowOf(12), 'sip'); assert.equal(rowOf(25), 'deep');
  assert.equal(colorName(0, ROSE), 'zero'); assert.equal(colorName(32, ROSE), 'rose'); assert.equal(colorName(33, ROSE), 'plum');
});

test('chips: whole SP, at most 3 a spin in all, unknown spots refused', () => {
  let c = {};
  for (const s of ['s17', 's17', 'rose']) { const r = addChip(c, s, { spots: SPOTS }); assert.ok(r.ok); c = r.chips; }
  assert.equal(chipTotal(c), MAX_CHIPS);
  const over = addChip(c, 'sip', { spots: SPOTS });
  assert.equal(over.ok, false); assert.equal(over.why, 'stake_cap'); assert.equal(chipTotal(over.chips), 3);
  assert.equal(addChip({}, 'nope', { spots: SPOTS }).why, 'unknown_spot');
  c = removeChip(c, 's17'); assert.deepEqual(c, { s17: 1, rose: 1 });
  c = removeChip(c, 's17'); assert.deepEqual(c, { rose: 1 });
  assert.deepEqual(betsOf({ deep: 1, s3: 1, rose: 1 }, SPOTS), [{ spot: 's3', amt: 1 }, { spot: 'rose', amt: 1 }, { spot: 'deep', amt: 1 }]);
  assert.deepEqual(chipsOf([{ spot: 'rose', amt: 2 }]), { rose: 2 });
});

test('the client cover-all check disables Spin with the server word; near covers pass', () => {
  assert.equal(checkLayout({}, { rose: ROSE }).why, 'empty');
  assert.equal(checkLayout({ rose: 1, plum: 1 }, { rose: ROSE }).why, 'covers_all');
  assert.equal(checkLayout({ sip: 1, sink: 1, deep: 1 }, { rose: ROSE }).why, 'covers_all');
  assert.ok(checkLayout({ rose: 1, sip: 1, sink: 1 }, { rose: ROSE }).ok, 'a colour plus two rows covers 30 of 36 and is allowed');
  assert.ok(!coversAll([{ spot: 's0' }, { spot: 'rose' }], ROSE));
  const poor = checkLayout({ rose: 2 }, { rose: ROSE, count: 5, sp: 9 });
  assert.equal(poor.why, 'insufficient'); assert.equal(poor.cost, 10);
  assert.ok(checkLayout({ rose: 2 }, { rose: ROSE, count: 5, sp: 10 }).ok);
});

test('the check agrees with the mock server on every legal-sized outside layout', async () => {
  const outside = ['rose', 'plum', 'sip', 'sink', 'deep'];
  const sets = [];
  for (let m = 1; m < 32; m++) { const s = outside.filter((_, i) => m & (1 << i)); if (s.length <= 3) sets.push(s); }
  for (const s of sets) {
    const srv = createMockServer({ sp: 50, floorMs: 0 });
    const chips = Object.fromEntries(s.map((x) => [x, 1]));
    const r = await srv.handle('spin', { idem: mintId(), count: 1, bets: betsOf(chips, SPOTS) });
    const c = checkLayout(chips, { rose: ROSE });
    assert.equal(c.why === 'covers_all', r.body.reason === 'bad_layout' && r.body.why === 'covers_all', s.join('+'));
  }
});

test('Law I: the chip owes what the tape has not played; the cursor rides the next spin', async () => {
  const srv = createMockServer({ sp: 30, floorMs: 0 });
  srv.script({ pocket: 17, wake: false }, { pocket: 0 }, { pocket: 17, wake: true });
  const idem = mintId();
  assert.ok(IDEM_RE.test(idem));
  const body = spinBody({ idem, count: 3, chips: { s17: 1, sink: 1 }, spots: SPOTS, tape: null });
  assert.equal(body.cursor, undefined);
  const res = await srv.handle('spin', body, idem);
  const a = classify(res);
  assert.equal(a.kind, 'ok');
  const tape = adoptTape(a.body.tape);
  assert.deepEqual(tape.outcomes.map((o) => o.pay), [36 + 3, 0, (36 + 3) * 2]);
  assert.equal(a.body.sp, 30 - 6 + 39 + 78);
  assert.equal(owed(tape), 117); assert.equal(shownSp(a.body.sp, tape), 24, 'the stake leaves at once, nothing lands early');
  tape.played = 1; assert.equal(shownSp(a.body.sp, tape), 63);
  assert.deepEqual(cursorOf(tape), { tapeId: tape.id, played: 1 });
  const again = spinBody({ idem: mintId(), count: 1, chips: { rose: 1 }, spots: SPOTS, tape });
  assert.deepEqual(again.cursor, { tapeId: tape.id, played: 1 });
  const unplayed = classify(await srv.handle('spin', again));
  assert.equal(unplayed.kind, 'tape', 'two spins still unwatched: adopt, nothing bought');
  assert.equal(unplayed.tape.played, 1); assert.deepEqual(unplayed.tape.bets, [{ spot: 's17', amt: 1 }, { spot: 'sink', amt: 1 }]);
});

test('a read outcome: hits, the straight, the wheel index; pay stays the server\'s', () => {
  const bets = [{ spot: 's17', amt: 1 }, { spot: 'plum', amt: 1 }, { spot: 'sink', amt: 1 }];
  const r = readOutcome({ i: 2, pocket: 17, wake: true, pay: 999 }, bets, { rose: ROSE, wheel: WHEEL });
  assert.deepEqual(r.hits, ['s17', 'plum', 'sink']); assert.equal(r.straight, true); assert.equal(r.pay, 999);
  assert.equal(r.index, WHEEL.indexOf(17)); assert.equal(r.color, 'plum'); assert.equal(r.row, 'sink');
  const z = readOutcome({ i: 0, pocket: 0, wake: false, pay: 0 }, bets, { rose: ROSE, wheel: WHEEL });
  assert.deepEqual(z.hits, []); assert.equal(z.index, 0); assert.equal(z.color, 'zero');
});

test('classify: the same idem on busy and a lost reply, a short floor waited, refusals as text', async () => {
  assert.equal(classify({ ok: true, status: 403, body: { ok: false, reason: 'closed' } }).kind, 'closed');
  assert.deepEqual(classify({ ok: true, status: 200, body: { ok: false, reason: 'busy' } }, 1), { kind: 'retry', waitMs: 650, reason: 'busy' });
  assert.equal(classify({ ok: false, reason: 'timeout' }, 2).kind, 'retry');
  assert.equal(classify({ ok: false, reason: 'timeout' }, 3).kind, 'refused', 'three sends and it is a refusal');
  assert.equal(classify({ ok: true, body: { ok: false, reason: 'too_fast', retryInMs: 1200 } }).waitMs, 1260);
  assert.equal(classify({ ok: true, body: { ok: false, reason: 'too_fast', retryInMs: 30000 } }).kind, 'refused');
  const bl = classify({ ok: true, body: { ok: false, reason: 'bad_layout', why: 'covers_all' } });
  assert.equal(bl.kind, 'refused'); assert.equal(bl.why, 'covers_all');

  const srv = createMockServer({ sp: 20, floorMs: 0 });
  srv.fail('spin', 'busy', 1); srv.fail('spin', 'timeout', 1, { apply: true });
  const idem = mintId(), body = spinBody({ idem, count: 2, chips: { rose: 1 }, spots: SPOTS });
  let tries = 0, a;
  do { tries++; a = classify(await srv.handle('spin', body, idem), tries); } while (a.kind === 'retry');
  assert.equal(a.kind, 'ok'); assert.equal(tries, 3);
  assert.equal(new Set(srv.log.filter((l) => l.op === 'spin').map((l) => l.body.idem)).size, 1, 'one idem for the whole intent');
  assert.equal(srv.user.n, 2, 'the lost reply settled once; the retry replayed its receipt');
});

test('mock server: floors, insufficient, door, cursor forward only', async () => {
  let now = 1000;
  const srv = createMockServer({ sp: 5, floorMs: 6000, now: () => now });
  const spin = (chips, count) => srv.handle('spin', spinBody({ idem: mintId(), count, chips, spots: SPOTS }));
  assert.equal((await spin({ rose: 3 }, 2)).body.reason, 'insufficient');
  const ok = await spin({ rose: 1 }, 2);
  assert.ok(ok.body.ok);
  await srv.handle('cursor', { tapeId: ok.body.tape.id, played: 2 });
  await srv.handle('cursor', { tapeId: ok.body.tape.id, played: 1 });
  assert.equal(srv.user.tape.played, 2);
  const fast = await spin({ rose: 1 }, 1);
  assert.equal(fast.body.reason, 'too_fast'); assert.equal(fast.body.retryInMs, 12000);
  now += 12000;
  assert.ok((await spin({ rose: 1 }, 1)).body.ok);
  srv.setOpen(false);
  const closed = await spin({ rose: 1 }, 1);
  assert.equal(closed.status, 403);
  const state = (await srv.handle('state')).body;
  assert.equal(state.open, false); assert.equal(state.wheel.length, 37); assert.equal(state.spots.length, 42);
});
