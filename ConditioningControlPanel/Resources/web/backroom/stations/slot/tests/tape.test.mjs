// node --test ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/
import test from 'node:test';
import assert from 'node:assert/strict';
import { createTape, shownSpOf, stopsFor, normalize, defaultTapeCount, affordableTapeCount, TAPE_MAX } from '../tape.js';
import { createMockServer } from '../mock-server.js';

function rig(opts = {}) {
  let t = 0;
  const server = createMockServer({ now: () => t, ...opts });
  const sleeps = [], melts = [];
  const tape = createTape({
    request: (op, body, idem) => server.handle(op, body, idem),
    sleep: async ms => { sleeps.push(ms); t += ms; },
    onMelt: left => melts.push(left),
  });
  return { server, tape, sleeps, melts, advance: ms => { t += ms; } };
}

/** Press and land until `n` outcomes have played; returns them. */
async function play(r, n) {
  const out = [];
  for (let i = 0; i < n; i++) {
    const p = await r.tape.press();
    assert.equal(p.kind, 'play', `press ${i} refused: ${p.reason}`);
    assert.ok(r.tape.land(p.outcome));
    out.push(p.outcome);
    r.advance(1000);
  }
  return out;
}

const tapeCalls = r => r.server.log.filter(x => x.op === 'tape');

test('shownSp is the server balance minus the pays still on the tape', () => {
  const tape = { played: 1, outcomes: [{ pay: 40 }, { pay: 3 }, { pay: 0 }, { pay: 10 }] };
  assert.equal(shownSpOf(100, tape), 87);
  assert.equal(shownSpOf(100, tape, { played: 0, outcomes: [{ pay: 5 }] }), 82);
  assert.equal(shownSpOf(100, null), 100);
  assert.deepEqual(stopsFor([['a', 'b'], ['b', 'a'], ['x']], ['b', 'b', 'q']), [1, 0, 0]);
  assert.equal(normalize({ ok: false, reason: 'timeout' }).reason, 'timeout');
  assert.equal(normalize({ ok: true, body: { ok: false, reason: 'too_fast' } }).reason, 'too_fast');
  assert.equal(normalize(null).ok, false);
});

test('a bought tape ticks the readout per landing and ends on the server balance', async () => {
  const r = rig({ sp: 57 });
  assert.equal((await r.tape.open()).ok, true);
  const first = await r.tape.press();
  assert.equal(first.kind, 'play');
  const s = r.tape.snapshot();
  assert.equal(tapeCalls(r)[0].body.count, 10);
  assert.equal(s.shownSp, 57 - 10, 'wins have not landed yet');
  let expected = 47;
  r.tape.land(first.outcome);
  expected += first.outcome.pay;
  assert.equal(r.tape.snapshot().shownSp, expected);
  const rest = await play(r, s.onTape - 1);
  for (const o of rest) expected += o.pay;
  assert.equal(r.tape.snapshot().shownSp, expected);
  assert.equal(r.tape.snapshot().shownSp, r.server.user.sp);
  assert.equal(tapeCalls(r).length, 1, 'free spins and re-spins came off the same tape');
});

test('open resumes an unplayed tape without buying', async () => {
  const r = rig({ sp: 30 });
  await r.tape.open();
  await play(r, 4);
  const before = r.server.user.sp;
  // Leave mid-tape: the host flushes the cursor, the page reopens later.
  await r.server.handle('cursor', r.tape.cursor());
  r.tape.abort();
  const again = createTape({ request: (op, b, i) => r.server.handle(op, b, i), sleep: async () => {} });
  await again.open();
  assert.equal(again.snapshot().onTape, r.server.user.tape.outcomes.length - 4);
  const p = await again.press();
  assert.equal(p.outcome, again.next());
  assert.equal(p.outcome.i, 4, 'picks up at the cursor');
  assert.equal(tapeCalls(r).length, 1);
  assert.equal(r.server.user.sp, before);
});

test('tape_unplayed from the server is adopted and played, not re-bought', async () => {
  const r = rig({ sp: 30 });
  // A second page opens before any tape exists, then the first page buys and plays two.
  const stale = createTape({ request: (op, b, i) => r.server.handle(op, b, i), sleep: async () => {} });
  await stale.open();
  await r.tape.open();
  await play(r, 2);
  await r.server.handle('cursor', r.tape.cursor());
  const p = await stale.press();
  assert.equal(r.server.log.at(-1).op, 'tape');
  assert.equal(p.kind, 'play');
  assert.equal(p.outcome.i, 2, 'resumes at the stored cursor');
  assert.equal(tapeCalls(r).length, 2, 'the refused buy debited nothing');
  assert.equal(stale.snapshot().shownSp, r.tape.snapshot().shownSp);
});

test('a lost reply retries with the same idem and debits once', async () => {
  const r = rig({ sp: 20 });
  await r.tape.open();
  r.server.fail('tape', 'timeout', 1, { apply: true });
  const p = await r.tape.press();
  assert.equal(p.kind, 'play');
  const calls = tapeCalls(r);
  assert.equal(calls.length, 2);
  assert.equal(calls[0].idem, calls[1].idem);
  const receipt = r.server.user.tape.outcomes.reduce((t, o) => t + o.pay, 0);
  assert.equal(r.server.user.sp, 20 - 4 + receipt, 'debited once (20 SP buys the 4-spin default)');
  assert.equal(r.tape.snapshot().shownSp, 16);
});

test('a press after retries ran out reuses the same intent; a new intent mints a new idem', async () => {
  const r = rig({ sp: 20 });
  await r.tape.open();
  r.server.fail('tape', 'offline', 4);
  assert.deepEqual(await r.tape.press(), { kind: 'refused', reason: 'offline' });
  const p = await r.tape.press();
  assert.equal(p.kind, 'play');
  const ids = new Set(tapeCalls(r).map(c => c.idem));
  assert.equal(ids.size, 1);
  await play(r, r.tape.snapshot().onTape - 1 + 1);
  r.advance(60000);
  await r.tape.press();
  assert.equal(new Set(tapeCalls(r).map(c => c.idem)).size, 2);
});

test('too_fast waits out retryInMs silently on the same idem, cursor folded in', async () => {
  const r = rig({ sp: 40, floorMs: 800 });
  await r.tape.open();
  await play(r, 1);
  while (r.tape.snapshot().onTape) { const p = await r.tape.press(); r.tape.land(p.outcome); }
  const done = r.tape.cursor();   // no time passed since the buy: the floor refuses the next one
  assert.equal((await r.tape.press()).kind, 'play');
  assert.ok(r.sleeps[0] > 0, 'slept on retryInMs');
  const calls = tapeCalls(r);
  assert.equal(calls.at(-1).idem, calls.at(-2).idem);
  assert.deepEqual(calls.at(-1).body.cursor, done);
});

test('a freeze mid-tape plays first, costs 2, and never skips a tape outcome', async () => {
  const r = rig({ sp: 50 });
  await r.tape.open();
  const head = await play(r, 3);
  const mainId = r.tape.cursor().tapeId;
  const stored = r.server.user.tape.outcomes.map(o => o.i);
  const spBefore = r.server.user.sp;
  assert.equal(r.tape.toggleHold(0), 0);
  assert.equal(r.tape.toggleHold(1), 1, 'one column: lighting another moves the hold');
  const f = await r.tape.press();
  assert.equal(f.outcome.kind, 'freeze');
  assert.equal(f.outcome.symbols[1], head[2].symbols[1], 'held column keeps what was showing');
  const buy = tapeCalls(r).at(-1);
  assert.deepEqual([buy.body.freeze, buy.body.count, buy.body.cursor], [{ col: 1 }, 1, { tapeId: mainId, played: 3 }]);
  assert.equal(r.tape.snapshot().hold, null, 'the hold is spent on one spin');
  // The freeze and anything it expanded into drain before the tape resumes.
  const side = [f.outcome];
  r.tape.land(f.outcome);
  let p = await r.tape.press();
  while (p.from === 'side') {
    side.push(p.outcome); r.tape.land(p.outcome); p = await r.tape.press();
  }
  assert.equal(r.server.user.sp, spBefore - 2 + side.reduce((s, o) => s + o.pay, 0));
  assert.equal(p.outcome.i, 3);
  assert.equal(r.tape.cursor().tapeId, mainId);
  const tail = [];
  for (;;) {
    tail.push(p.outcome); r.tape.land(p.outcome);
    if (!r.tape.snapshot().onTape) break;
    p = await r.tape.press();
  }
  assert.deepEqual([...head, ...tail].map(o => o.i), stored);
  assert.equal(tapeCalls(r).length, 2);
  assert.equal(r.tape.snapshot().shownSp, r.server.user.sp);
});

test('insufficient: nothing sent below 1 SP, a freeze below its cost is refused quietly', async () => {
  const r = rig({ sp: 0 });
  await r.tape.open();
  assert.deepEqual(await r.tape.press(), { kind: 'refused', reason: 'insufficient' });
  assert.equal(tapeCalls(r).length, 0);
  const r2 = rig({ sp: 1 });
  await r2.tape.open(); r2.tape.toggleHold(0);
  assert.deepEqual(await r2.tape.press(), { kind: 'refused', reason: 'insufficient' });
  r2.tape.clearHold();
  const p = await r2.tape.press();
  assert.equal(tapeCalls(r2)[0].body.count, 1, '1 SP affords a 1-spin tape');
  assert.equal(p.kind, 'play');
});

test('melt is carried over from the server on open and reported when it changes', async () => {
  const r = rig({ sp: 20, melt: 2 });
  await r.tape.open();
  assert.equal(r.tape.snapshot().melt, 2);
  assert.deepEqual(r.melts, [2]);
  r.server.script(['gif0', 'sub1', 'spiral0'], ['gif1', 'melt', 'spiral2'], ['gif0', 'gif0', 'gif0']);
  const [a, b, c] = await play(r, 3);
  assert.equal(a.meltLeft, 1);
  assert.equal(b.line, 'melt');
  assert.deepEqual([c.halved, c.pay], [true, 20]);
  assert.deepEqual(r.melts, [2, 1, 3, 2]);
});

test('free spins play from the tape and abort drops a press in flight', async () => {
  const r = rig({ sp: 20 });
  await r.tape.open();
  r.server.script(['spiral0', 'spiral1', 'spiral2']);
  const [win] = await play(r, 1);
  assert.deepEqual([win.line, win.freeLeft], ['spiral3', 3]);
  assert.equal(r.tape.next().kind, 'free');
  assert.equal(r.tape.snapshot().free, 3);
  await play(r, r.tape.snapshot().onTape);
  assert.equal(tapeCalls(r).length, 1);
  r.server.fail('tape', 'too_fast', 1, { body: { retryInMs: 500 } });
  const inflight = r.tape.press();
  r.tape.abort();
  assert.deepEqual(await inflight, { kind: 'aborted' });
});

test('defaultTapeCount: floor(sp / 5) held to 1..10, and the clamp never offers more than the balance', () => {
  const sps = [0, 1, 4, 5, 16, 49, 50, 500];
  assert.deepEqual(sps.map(defaultTapeCount), [1, 1, 1, 1, 3, 9, 10, 10]);
  assert.deepEqual(sps.map(sp => affordableTapeCount(defaultTapeCount(sp), sp)), [0, 1, 1, 1, 3, 9, 10, 10]);
  assert.deepEqual(sps.map(sp => affordableTapeCount(TAPE_MAX, sp)), [0, 1, 4, 5, 16, 20, 20, 20], 'a manual pick is held to the balance and the server max');
  assert.equal(defaultTapeCount(NaN), 1);
  assert.equal(affordableTapeCount(12, 7.9), 7);
});

test('the tape follows the balance until the player picks a count, and any pick stays affordable', async () => {
  const r = rig({ sp: 16 });
  await r.tape.open();
  assert.equal(r.tape.snapshot().tapeCount, 3);
  r.tape.setServerSp(49);
  assert.equal(r.tape.snapshot().tapeCount, 9, 'a balance change moves the default');
  r.tape.setServerSp(16);
  const p = await r.tape.press();
  assert.equal(p.kind, 'play');
  assert.equal(tapeCalls(r)[0].body.count, 3);
  assert.equal(r.server.user.sp, 13 + r.server.user.tape.outcomes.reduce((t, o) => t + o.pay, 0));

  const r2 = rig({ sp: 16 });
  await r2.tape.open();
  assert.equal(r2.tape.pickCount(12), 12);
  r2.tape.setServerSp(500);
  assert.equal(r2.tape.snapshot().tapeCount, 12, 'a pick is kept when the balance moves');
  assert.equal(r2.tape.pickCount(99), TAPE_MAX);
  r2.tape.setServerSp(16);
  assert.equal(r2.tape.snapshot().tapeCount, 16, 'never above what the balance affords');
  await r2.tape.press();
  assert.equal(tapeCalls(r2)[0].body.count, 16);
  r2.tape.setServerSp(0);
  assert.equal(r2.tape.pickCount(null), 0, 'back to the default, and 0 SP affords nothing');
  await r2.tape.open();
  assert.equal(r2.tape.snapshot().tapePicked, null, 'a new sit-down forgets the pick');
});
