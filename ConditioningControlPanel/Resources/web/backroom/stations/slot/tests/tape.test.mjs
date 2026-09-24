// node --test ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/
import test from 'node:test';
import assert from 'node:assert/strict';
import { createTape, shownSpOf, stopsFor, normalize, defaultTapeCount, affordableTapeCount, TAPE_MAX } from '../tape.js';
import { createMockServer, lineFor, WEIGHTS_V7 } from '../mock-server.js';
import { jarPlan, playsWithoutPress, respinKeep } from '../feel.js';

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

test('a freeze mid-tape plays its own outcomes, holds through a re-spin, then the tape resumes at the same cursor', async () => {
  const r = rig({ sp: 50 });
  await r.tape.open();
  // 10 paid rows (pays 3, 2, 0, 40, 3, then nothing), then the freeze: spiral2 on the held spiral1 plus its re-spin (sub2).
  const none = ['gif1', 'spiral1', 'sub2'];
  r.server.script(['gif0', 'gif1', 'gif2'], ['sub0', 'gif1', 'sub1'], ['gif0', 'spiral1', 'sub0'], ['gif0', 'gif0', 'gif0'],
    ['gif3', 'gif1', 'gif2'], none, none, none, none, none, ['spiral0', 'x', 'gif2'], ['sub1', 'x', 'sub2']);
  const first = await r.tape.press();
  assert.equal(r.tape.snapshot().shownSp, 40, 'before: 50 - 10, no win landed');
  r.tape.land(first.outcome); r.advance(1000);
  const head = [first.outcome, ...await play(r, 2)];
  assert.equal(r.tape.snapshot().shownSp, 45);
  const mainId = r.tape.cursor().tapeId, stored = r.server.user.tape.outcomes.map(o => o.i);
  const cursor = r.tape.cursor();
  assert.equal(r.tape.toggleHold(0), 0);
  assert.equal(r.tape.toggleHold(1), 1, 'one column: lighting another moves the hold');
  const f = await r.tape.press();
  const buy = tapeCalls(r).at(-1), receipt = r.server.user.freezes.at(-1);
  assert.deepEqual([buy.body.freeze, buy.body.count, buy.body.cursor], [{ col: 1 }, 1, { tapeId: mainId, played: 3 }]);
  assert.deepEqual([f.from, f.held, f.outcome.kind, f.outcome.line, f.outcome.symbols[1]], ['side', 1, 'freeze', 'spiral2', 'spiral1']);
  assert.equal(receipt.at, 3);
  assert.equal(r.tape.snapshot().hold, null, 'the hold is spent on one buy');
  assert.deepEqual(r.tape.cursor(), cursor, 'the stored tape cursor did not move');
  assert.equal(r.server.user.sp, 50 - 10 + 48 - 2 + 3);
  assert.equal(r.tape.snapshot().shownSp, 43, 'during: the freeze cost shows, its unplayed wins do not');
  assert.deepEqual([r.tape.snapshot().owed, r.tape.snapshot().tapeOwed], [46, 43],
    'owed counts the freeze wins too, tapeOwed only the stored tape (what a reopen shows)');
  r.tape.land(f.outcome);
  assert.equal(r.tape.snapshot().shownSp, 44);
  const re = await r.tape.press();
  assert.deepEqual([re.from, re.held, re.outcome.kind, re.outcome.line, re.outcome.symbols[1]], ['side', 1, 'respin', 'sub2', 'spiral1'], 'a re-spin keeps the hold');
  r.tape.land(re.outcome);
  assert.equal(r.tape.snapshot().shownSp, 46);
  assert.deepEqual(r.tape.cursor(), cursor);
  let p = await r.tape.press();
  assert.deepEqual([p.from, p.held, p.outcome.i], ['main', null, 3], 'the tape resumes where it was');
  const tail = [];
  for (;;) {
    tail.push(p.outcome); r.tape.land(p.outcome);
    if (!r.tape.snapshot().onTape) break;
    p = await r.tape.press();
  }
  assert.deepEqual([...head, ...tail].map(o => o.i), stored);
  assert.equal(tapeCalls(r).length, 2);
  assert.equal(r.tape.snapshot().shownSp, r.server.user.sp, 'after: display meets the ledger');
  assert.equal(r.tape.snapshot().shownSp, 89);
});

test('a freeze with no stored tape answers tape:null and plays on its own', async () => {
  const r = rig({ sp: 5 });
  await r.tape.open();
  r.tape.toggleHold(2);
  const f = await r.tape.press();
  const body = await r.server.handle('tape', tapeCalls(r).at(-1).body, tapeCalls(r).at(-1).idem);
  assert.deepEqual([body.body.tape, body.body.freeze.col, body.body.freeze.held], [null, 2, 'sub2'], 'a replayed receipt');
  assert.deepEqual([f.outcome.kind, f.held, f.outcome.symbols[2], r.tape.cursor()], ['freeze', 2, 'sub2', null]);
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

test('a freeze mid-tape never moves the shown melt, though its outcomes carry the stored tape end melt', async () => {
  const r = rig({ sp: 50 });
  await r.tape.open();
  const none = ['gif1', 'spiral1', 'sub2'];
  // 9 blank paid rows then a melt on the 10th; the freeze (held gif1) lands spiral2 and its re-spin a blank.
  r.server.script(none, none, none, none, none, none, none, none, none, ['gif0', 'melt', 'sub0'],
    ['x', 'spiral0', 'spiral1'], ['x', 'gif0', 'sub1']);
  await play(r, 2);
  assert.equal(r.tape.snapshot().melt, 0);
  r.tape.toggleHold(0);
  const f = await r.tape.press();
  assert.deepEqual([f.from, f.outcome.kind, f.outcome.line, f.outcome.meltLeft], ['side', 'freeze', 'spiral2', 3],
    'the server settled the whole tape first: the freeze sees its end melt');
  r.tape.land(f.outcome);
  assert.equal(r.tape.snapshot().melt, 0, 'a freeze landing leaves the shown melt on the tape cursor');
  const re = await r.tape.press();
  assert.deepEqual([re.from, re.outcome.kind, re.outcome.meltLeft], ['side', 'respin', 3]);
  r.tape.land(re.outcome);
  assert.equal(r.tape.snapshot().melt, 0, 'and so does its re-spin');
  assert.deepEqual(r.melts, [0], 'no melt change was reported through the freeze');
  const rest = await play(r, 8);
  assert.equal(rest.at(-1).line, 'melt');
  assert.equal(r.tape.snapshot().melt, 3, 'the melt arrives when the tape reaches it');
  assert.deepEqual(r.melts, [0, 3]);
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


/* ---------------------------------------------------------------------------------------------------
 * The playbook, Tier B and C (CONTRACT 10.16), against the mock's table v7. The client never weights,
 * mints or invents any of this: every number below is the server's and the page only shows it (Law I).
 * ------------------------------------------------------------------------------------------------ */

test('table v7: the jar block, the emi2 row, the re-spin block and the TOTAL jackpot odds', async () => {
  const r = rig();
  await r.tape.open();
  const s = r.tape.snapshot();
  assert.equal(s.jarSize, 100, '10.16.A, decided 2026-09-14 evening: a jar is a come-back reason');
  assert.equal(s.jarFree, 3);
  assert.equal(s.jackpotOdds, '1 in 6,493', 'published odds are always the TOTAL, both paths counted');
  const emi2 = s.lines.find(l => l.id === 'emi2');
  assert.ok(emi2, 'emi2 joins LINES between emi3 and gif3same');
  assert.equal(emi2.pays, 0, 'the re-spin IS the event, so the line itself pays nothing');
  assert.equal(emi2.respin, 1);
  assert.equal(s.lines.findIndex(l => l.id === 'emi2'), 1);
  // The weights the server lane is re-solving live in one place, and they still add up to the denominator.
  assert.deepEqual({ ...WEIGHTS_V7 }, { emi3: 92, emi2: 8207, gif3same: 6784, sub3: 8479, spiral3: 8479,
                                        gif3: 83746, sub2: 67829, spiral2: 67829, melt: 58900, none: 689655 },
    'the server lane\'s own solved numbers (CCP-Server #176), mirrored');
  assert.equal(WEIGHTS_V7.melt, 58900, 'melt does not scale');
  assert.equal(WEIGHTS_V7.gif3, 83746, 'gif3 is polished alone to land the exact 1.0200 (83,759 -> 83,746)');
  assert.equal(Object.values(WEIGHTS_V7).reduce((a, b) => a + b, 0), 1e6);
  // Published odds are DEN / weight, rounded, exactly as 10.16.A's own table computes them.
  for (const l of s.lines) {
    if (l.id === 'emi3') continue;                      // its published row is the TOTAL, not its own weight
    const w = WEIGHTS_V7[l.id];
    assert.equal(l.odds, `1 in ${Math.round(1e6 / w).toLocaleString('en-US')}`, `${l.id} odds`);
  }
});

test('C1 lineFor: emi2 is read before melt, and only on reels 1 and 2', () => {
  assert.equal(lineFor(['emi', 'emi', 'melt']), 'emi2', 'the chase does NOT start the melt');
  assert.equal(lineFor(['emi', 'emi', 'sub0']), 'emi2');
  assert.equal(lineFor(['emi', 'emi', 'emi']), 'emi3', 'three is still the jackpot');
  assert.equal(lineFor(['emi', 'sub0', 'emi']), 'none', 'the pair is about reels 1 and 2');
  assert.equal(lineFor(['melt', 'emi', 'emi']), 'melt');
  assert.equal(lineFor(['spiral0', 'spiral1', 'spiral2']), 'spiral3', 'nothing else moved');
});

test('C1 an emi2 queues exactly one emi_respin, inline, and it holds reels 1 and 2', async () => {
  const r = rig({ sp: 20 });
  await r.tape.open();
  r.server.script(['emi', 'emi', 'melt']);
  r.server.respin('sub1');
  r.tape.pickCount(1);
  const out = await play(r, 2);
  assert.equal(out[0].line, 'emi2');
  assert.equal(out[0].pay, 0);
  assert.equal(out[0].meltLeft, 0, 'emi, emi, melt reads emi2 and never starts the melt');
  assert.equal(out[1].kind, 'emi_respin');
  assert.equal(playsWithoutPress(out[1].kind), true, 'it plays on its own, with no second lever press');
  assert.deepEqual(respinKeep(out[1].kind), [0, 1]);
  assert.deepEqual(out[1].symbols.slice(0, 2), ['emi', 'emi'], 'reels 1 and 2 stay exactly where they are');
  assert.equal(out[1].symbols[2], 'sub1');
  assert.equal(out[1].line, 'none');
  assert.equal(out[1].halved, false);
  // exactly one: an emi_respin never queues a further re-spin, free spin or a second emi2.
  assert.equal(out.filter(o => o.kind === 'emi_respin').length, 1);
});

test('C1 the re-spin can land the jackpot, and it is never halved even while melted', async () => {
  const r = rig({ sp: 20, melt: 3 });
  await r.tape.open();
  r.server.script(['emi', 'emi', 'sub0']);
  r.server.respin('emi');
  r.tape.pickCount(1);
  const out = await play(r, 2);
  assert.equal(out[0].line, 'emi2');
  assert.equal(out[0].halved, true, 'the pair itself is a plain-band outcome: halved of 0 while melted');
  const hit = out[1];
  assert.equal(hit.kind, 'emi_respin');
  assert.equal(hit.line, 'emi3');
  assert.equal(hit.pay, 400, 'the re-spin\'s jackpot is never halved');
  assert.equal(hit.halved, false);
  assert.ok(hit.fx.includes('fx.jackpot'));
});

test('C1 a freeze never draws emi2 and never re-spins (the seal that keeps rtpFrozen at 1.0200)', async () => {
  const r = rig({ sp: 30 });
  await r.tape.open();
  r.tape.pickCount(1);
  await play(r, 1);
  r.tape.toggleHold(0);
  r.server.script(['emi', 'emi', 'melt']);
  const p = await r.tape.press();
  assert.equal(p.kind, 'play');
  assert.ok(r.tape.land(p.outcome));
  assert.equal(p.outcome.kind, 'freeze');
  assert.notEqual(p.outcome.line, 'emi2', 'a sealed row never reads emi2');
  assert.equal(r.server.user.tape.outcomes.every(o => o.kind !== 'emi_respin'), true);
});

test('B1 the jar: every spiral shown ticks it, and the tape carries jarN after every outcome', async () => {
  const r = rig({ sp: 30, jar: 10 });
  await r.tape.open();
  assert.equal(r.tape.snapshot().jar, 10, 'the stored count arrives with the state');
  r.server.script(['spiral0', 'gif1', 'spiral2'], ['gif0', 'sub1', 'gif2']);
  r.tape.pickCount(2);
  const before = r.tape.snapshot().jar;
  const out = await play(r, 2);
  assert.equal(out[0].jarN, 12, 'two spirals shown, two ticks');
  assert.equal(out[1].jarN, 12, 'a spin with no spiral leaves it alone');
  assert.equal(r.tape.snapshot().jar, 12, 'the readout follows the tape cursor, exactly as melt does');
  const plan = jarPlan(out[0], before, r.tape.snapshot().jarSize);
  assert.deepEqual(plan.reels, [0, 2], 'and the tube ticks on those two reels\' own thuds');
  assert.deepEqual(plan.values, [11, 12]);
});

test('B1 a full jar spills inline: 3 jar spins in draw order, before the next paid spin', async () => {
  const r = rig({ sp: 30, jar: 99 });
  await r.tape.open();
  r.server.script(['spiral0', 'gif1', 'sub0'], ['gif0', 'sub1', 'gif2']);
  r.tape.pickCount(2);
  const before = r.tape.snapshot().jar;
  const out = await play(r, 5);
  assert.equal(out[0].line, 'none', 'one spiral, no line of its own to expand');
  assert.equal(out[0].jarN, 0, '99 + 1 fires at 100 and the remainder stays');
  assert.equal(jarPlan(out[0], before, 100).full, true);
  assert.deepEqual(out.slice(1, 4).map(o => o.kind), ['jar', 'jar', 'jar'], 'three of them, inline');
  assert.equal(out[4].kind, 'paid', 'and only then the next paid spin');
  assert.equal(out[0].freeLeft >= 3, true, 'freeLeft counts the jar spins too');
  assert.equal(r.tape.snapshot().jar, out[4].jarN, 'and the readout follows the tape cursor throughout');
});

test('B1 the jar is sealed from a freeze: its spirals earn nothing and jarN comes back unchanged', async () => {
  const r = rig({ sp: 40, jar: 50 });
  await r.tape.open();
  r.tape.pickCount(1);
  await play(r, 1);
  const at = r.tape.snapshot().jar;
  r.tape.toggleHold(1);
  r.server.script(['spiral0', 'spiral1', 'spiral2']);
  const p = await r.tape.press();
  assert.equal(p.outcome.kind, 'freeze');
  assert.ok(r.tape.land(p.outcome));
  assert.equal(p.outcome.jarN, at, 'a freeze carries back the jar it did not change');
  assert.deepEqual(jarPlan(p.outcome, at, 100).reels, [], 'so the tube does not tick');
  assert.equal(r.tape.snapshot().jar, at);
});

test('B3 the comp: the first buy is 0 SP, playable at 0 SP, and it is spent once', async () => {
  const r = rig({ sp: 0, comp: { id: 'c_mock_welcome', spins: 5 } });
  await r.tape.open();
  const s = r.tape.snapshot();
  assert.deepEqual(s.comp, { id: 'c_mock_welcome', spins: 5 });
  assert.equal(s.compSpent, false);
  assert.equal(s.tapeCount, 0, 'a paid tape is not affordable at 0 SP...');
  const p = await r.tape.press();
  assert.equal(p.kind, 'play', '...but the comp is, which is the whole point of a comp');
  const sent = tapeCalls(r).at(-1);
  assert.equal(sent.body.comp, 'c_mock_welcome');
  assert.equal(sent.body.count, undefined, '10.16.C: with comp, count is absent or exactly 5');
  const receipt = r.server.user;
  assert.equal(receipt.comp, null, 'settle() clears the stored comp');
  assert.equal(r.tape.snapshot().comp, null);
  assert.equal(r.tape.snapshot().compSpent, true);
  assert.equal(r.server.user.tape.outcomes.length >= 5, true, 'five spins, drawn from the NORMAL plain table');
  assert.equal(r.server.user.tape.outcomes.filter(o => o.kind === 'paid').length, 5);
});

test('B3 the comp is halved by the melt like any other spin: a gift, not a cleanse', async () => {
  const r = rig({ sp: 0, melt: 3, comp: true });
  await r.tape.open();
  r.server.script(['gif1', 'gif1', 'gif1']);
  const out = await play(r, 1);
  assert.equal(out[0].line, 'gif3same');
  assert.equal(out[0].halved, true);
  assert.equal(out[0].pay, 20, 'half of 40');
  assert.equal(out[0].meltLeft, 2, 'and it consumed one melt in draw order');
});

test('B3 comp refusals: comp_used falls back to a paid tape, and a refused buy never eats it', async () => {
  const r = rig({ sp: 20, comp: { id: 'c_mock_welcome', spins: 5 } });
  await r.tape.open();
  // The server forgot it (already spent, or an older grant): the page falls back to a paid tape at once.
  r.server.user.comp = { id: 'c_other_one', spins: 5, day: 0 };
  r.tape.pickCount(2);
  const p = await r.tape.press();
  assert.equal(p.kind, 'play');
  const calls = tapeCalls(r);
  assert.equal(calls[0].body.comp, 'c_mock_welcome');
  assert.equal(calls[1].body.count, 2, 'and the paid tape it fell back to');
  assert.notEqual(calls[0].idem, calls[1].idem, 'a fresh intent mints a fresh idem');
  assert.equal(r.tape.snapshot().comp, null);
});

test('B3 the comp waits behind an unplayed tape (finish the tape you have)', async () => {
  const r = rig({ sp: 20 });
  await r.tape.open();
  r.tape.pickCount(2);
  await r.tape.press();                       // a paid tape is bought and left unplayed
  r.server.user.comp = { id: 'c_mock_welcome', spins: 5, day: 0 };
  const r2 = rig();
  // a fresh sit-down on the same server, with the tape still unplayed
  const tape2 = createTape({ request: (op, body, idem) => r.server.handle(op, body, idem), sleep: async () => {} });
  await tape2.open();
  assert.deepEqual(tape2.snapshot().comp, { id: 'c_mock_welcome', spins: 5 });
  const p = await tape2.press();
  assert.equal(p.kind, 'play', 'the stored tape plays first');
  assert.deepEqual(tape2.snapshot().comp, { id: 'c_mock_welcome', spins: 5 }, 'and the comp is still standing');
  assert.ok(r2);
});

test('C1 the re-spin comes immediately after its emi2, even when other spins are already queued', async () => {
  const r = rig({ sp: 30 });
  await r.tape.open();
  // spiral2 queues a re-spin of its own; that re-spin draws the EMI pair, so the emi_respin is queued while
  // three more outcomes are already waiting. The chase is the point: it plays next, not after them.
  r.server.script(['spiral0', 'gif1', 'spiral2'], ['emi', 'emi', 'sub0']);
  r.server.respin('sub1');
  r.tape.pickCount(1);
  const out = await play(r, 3);
  assert.deepEqual(out.map(o => o.kind), ['paid', 'respin', 'emi_respin']);
  assert.equal(out[1].line, 'emi2');
  assert.equal(out[2].symbols[2], 'sub1');
  assert.equal(playsWithoutPress(out[2].kind), true, 'so the page plays it as the second beat of that press');
});

test('table v7: the expansion queue drains free spins first and jar spins behind them', async () => {
  const r = rig({ sp: 30, jar: 98 });
  await r.tape.open();
  // Three spirals: spiral3 pays 10 and wins 3 free spins, and the same row fills the jar (98 + 3).
  r.server.script(['spiral0', 'spiral1', 'spiral2']);
  r.tape.pickCount(1);
  const out = await play(r, 7);
  assert.equal(out[0].line, 'spiral3');
  assert.equal(out[0].jarN, 1, '98 + 3 fires at 100 and leaves 1');
  assert.deepEqual(out.slice(1, 7).map(o => o.kind), ['free', 'free', 'free', 'jar', 'jar', 'jar'],
    '10.16.A: the jar queues behind the line free spins it landed with');
});

test('10.16.A: an emi_respin can spill the jar too (it descends from a plain-band spin)', async () => {
  const r = rig({ sp: 30, jar: 99 });
  await r.tape.open();
  r.server.script(['emi', 'emi', 'sub0']);
  r.server.respin('spiral1');                 // the re-spin shows one spiral, and the jar is one short
  r.tape.pickCount(1);
  const out = await play(r, 5);
  assert.equal(out[0].line, 'emi2');
  assert.equal(out[0].jarN, 99, 'the pair showed no spiral of its own');
  assert.equal(out[1].kind, 'emi_respin');
  assert.equal(out[1].jarN, 0, 'and the re-spin\'s own spiral fires it');
  assert.deepEqual(out.slice(2, 5).map(o => o.kind), ['jar', 'jar', 'jar']);
});

test('10.16.D: under a freeze seal emi2 reads as none, exactly as melt does', async () => {
  const r = rig({ sp: 40 });
  await r.tape.open();
  r.tape.pickCount(1);
  r.server.script(['gif0', 'sub1', 'gif2'], ['emi', 'emi', 'gif0']);
  await play(r, 1);                           // lands gif2 on reel 3, which is what the hold then keeps
  r.tape.toggleHold(2);                       // reel 3 held, so reels 1 and 2 keep the scripted EMI pair
  const p = await r.tape.press();
  assert.equal(p.outcome.kind, 'freeze');
  assert.deepEqual(p.outcome.symbols.slice(0, 2), ['emi', 'emi']);
  assert.equal(p.outcome.line, 'none', 'no chase from a 2 SP freeze');
  assert.equal(p.outcome.pay, 0);
  assert.equal(p.outcome.fx.length, 0);
  assert.ok(r.tape.land(p.outcome));
  assert.equal(r.server.user.freezes.length, 1);
});

test('10.16.A: the receipt carries the jar beside the melt', async () => {
  const r = rig({ sp: 30, jar: 50 });
  await r.tape.open();
  r.server.script(['spiral0', 'gif1', 'sub0']);
  r.tape.pickCount(1);
  await play(r, 1);
  const receipt = r.server.log.filter(x => x.op === 'tape').length;
  assert.equal(receipt, 1);
  const res = await r.server.handle('state');
  assert.equal(res.body.jar, 51, 'and the state route reports the stored count');
  assert.equal(typeof res.body.melt, 'number');
});

test('10.24 onLanded names each server outcome that played, main by tape id and index, a freeze by side', async () => {
  let t = 0;
  const server = createMockServer({ now: () => t, sp: 50 });
  const landed = [];
  const tape = createTape({
    request: (op, body, idem) => server.handle(op, body, idem),
    sleep: async ms => { t += ms; },
    onLanded: l => landed.push(l),
  });
  await tape.open();
  const none = ['gif1', 'spiral1', 'sub2'];
  server.script(none, none, none, none, none, none, none, none, none, none, ['x', 'gif1', 'x'], ['x', 'x', 'x']);
  const a = await tape.press(); tape.land(a.outcome); t += 1000;
  const b = await tape.press(); tape.land(b.outcome); t += 1000;
  const id = tape.cursor().tapeId;
  assert.deepEqual(landed, [{ tapeId: id, i: 0 }, { tapeId: id, i: 1 }]);
  tape.toggleHold(1);
  const f = await tape.press();
  assert.equal(f.from, 'side');
  tape.land(f.outcome);
  assert.deepEqual(landed.at(-1), { side: true, i: 0 });
  assert.equal(tape.land(f.outcome), false, 'an outcome lands once');
  assert.equal(landed.length, 3);
});

test('10.24 a throwing onLanded never stops the landing', async () => {
  let t = 0;
  const server = createMockServer({ now: () => t, sp: 50 });
  const tape = createTape({ request: (op, body, idem) => server.handle(op, body, idem), sleep: async ms => { t += ms; },
    onLanded: () => { throw new Error('listener'); } });
  await tape.open();
  const p = await tape.press();
  assert.equal(tape.land(p.outcome), true);
});
