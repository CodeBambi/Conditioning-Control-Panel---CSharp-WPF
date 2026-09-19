import test from 'node:test';
import assert from 'node:assert/strict';
import { createFreePlay, dealRow, counterShown, DEMO_ROWS, DEMO_KIND } from '../freeplay.js';
import { createTape } from '../tape.js';
import { lineFor } from '../mock-server.js';
import { tierOf, recipe, flowPlan, playsWithoutPress } from '../feel.js';

const STRIPS = [
  ['gif0', 'spiral0', 'sub0', 'gif1', 'emi', 'spiral1', 'gif2', 'sub1', 'spiral2', 'gif3', 'sub2', 'sub3', 'melt'],
  ['sub1', 'gif2', 'spiral1', 'melt', 'gif0', 'sub3', 'emi', 'spiral2', 'gif3', 'sub0', 'spiral0', 'gif1', 'sub2'],
  ['spiral2', 'gif3', 'sub2', 'gif1', 'spiral0', 'emi', 'sub0', 'gif0', 'melt', 'sub3', 'gif2', 'spiral1', 'sub1'],
];
function seeded(seed) {
  let a = seed >>> 0;
  return () => { a = (a + 0x6d2b79f5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}

test('a demo spin makes no host or server op, moves no money and never enters the tape', async () => {
  const calls = [];
  const paid = { kind: 'plain', symbols: ['gif0', 'sub1', 'spiral3'], pay: 0, meltLeft: 0, freeLeft: 0, jarN: 2, fx: [] };
  const tape = createTape({ mint: () => 'c'.repeat(32), request: async (op, body) => {
    calls.push(op);
    if (op === 'state') return { body: { sp: 3, strips: STRIPS, table: { stake: 1, jar: { size: 30, free: 5 } }, jar: 2, tape: { id: 't1', played: 0, outcomes: [paid] } } };
    return { body: { sp: 2, tape: { id: 't2', played: 0, outcomes: [paid] } } };
  } });
  assert.equal((await tape.open()).ok, true);
  const before = tape.snapshot(), opsBefore = calls.length;
  const free = createFreePlay({ random: seeded(7) });
  for (let n = 0; n < 40; n++) {
    const step = free.press({ strips: before.strips, melt: before.melt });
    assert.equal(step.kind, 'play');
    assert.equal(step.demo, true);
    assert.equal(step.held, null);
    const o = step.outcome;
    assert.equal(o.pay, 0);
    assert.equal(o.kind, DEMO_KIND);
    assert.equal(o.freeLeft, 0);
    assert.equal('jarN' in o, false);
    assert.equal(tierOf(o), 0, 'a demo row is never a paying tier');
    assert.equal(recipe(o).tokens, false, 'THE BANK never flies for a demo row');
    assert.equal(playsWithoutPress(o.kind), false);
    assert.equal(tape.land(o), false, 'the tape refuses an outcome that is not its own');
  }
  assert.equal(calls.length, opsBefore, 'no request left for the server during forty demo spins');
  const after = tape.snapshot();
  assert.deepEqual([after.sp, after.shownSp, after.melt, after.free, after.jar, after.onTape], [before.sp, before.shownSp, before.melt, before.free, before.jar, before.onTape]);
  assert.equal(after.last, before.last);
});

test('every demo row yields an effects plan and reads as the line it claims, never a tape event', () => {
  const rnd = seeded(11);
  for (const row of DEMO_ROWS) {
    for (let n = 0; n < 25; n++) {
      const symbols = dealRow(row.line, STRIPS, rnd);
      assert.ok(symbols, `${row.line} deals off the strips`);
      assert.equal(lineFor(symbols), row.line, `${row.line}: ${symbols.join(',')}`);
      assert.ok(!symbols.includes('emi') && !symbols.includes('melt'));
      const o = { i: n, kind: DEMO_KIND, symbols, line: row.line, pay: 0, fx: [...row.fx], stops: [0, 0, 0] };
      const plan = flowPlan(o);
      assert.ok(plan.fx.length >= 1, `${row.line} fires host fx`);
      assert.ok(plan.unlockMs >= 0);
      if (row.line !== 'none') { assert.ok(plan.callouts.length >= 1); assert.equal(plan.unlockMs, 2000); }
      else assert.deepEqual(plan.fx.map(f => f.id), ['fx.gif_burst']);
    }
  }
  assert.ok(!DEMO_ROWS.some(r => ['emi3', 'emi2', 'melt'].includes(r.line)));
  const free = createFreePlay({ random: seeded(3) });
  const lines = new Set();
  for (let n = 0; n < 200; n++) lines.add(free.press({ strips: STRIPS }).outcome.line);
  assert.ok(lines.has('spiral3') && lines.has('spiral2') && lines.has('sub2'), [...lines].join(','));
  assert.equal(free.count(), 200);
});

test('the dealer refuses rather than invents a row the strips cannot show', () => {
  const free = createFreePlay({ random: seeded(5) });
  assert.equal(free.press({ strips: null }).kind, 'refused');
  assert.equal(free.press({ strips: [['emi'], ['emi'], ['emi']] }).kind, 'refused');
  assert.equal(dealRow('emi3', STRIPS), null);
  assert.equal(dealRow('melt', STRIPS), null);
});

test('the spins-left counter hides while a hold plays and returns on the idle screen', () => {
  assert.equal(counterShown({ pace: 'spin', unlockAt: 0, now: 100 }), false);
  assert.equal(counterShown({ pace: 'reveal', unlockAt: 0, now: 100 }), false);
  assert.equal(counterShown({ pace: 'idle', unlockAt: 2400, now: 1000 }), false, 'idle marked but the landing hold still runs');
  assert.equal(counterShown({ pace: 'idle', unlockAt: 2400, now: 2400 }), true);
  assert.equal(counterShown({ pace: 'idle', unlockAt: 0, now: 5000 }), true);
  assert.equal(counterShown(), true);
});
