import test from 'node:test';
import assert from 'node:assert/strict';
import { BANK, winTokens, spendTokens, bankFlightMs, bankLandMs, tokenQ,
         tickValues, rollupAt, rollupTicks, tokenAt, createBankRun } from '../bank.js';
import { CFX } from '../../../../arcademy/shell/counterfx.js';
import { FEEL, tickValues as slotTicks, rollupTicks as slotRollup, bankFlightMs as slotFlight,
         bankLandMs as slotLand, winTokens as slotWinTokens } from '../../../stations/slot/feel.js';
import { winTokens as wheelWinTokens } from '../../../stations/wheel/feel.js';

const run = (o) => createBankRun({ kind: 'pay', n: 3, fromValue: 0, toValue: 30, startMs: 0, ...o });
const ticks = evs => evs.filter(e => e.type === 'tick').map(e => e.value);
const types = evs => evs.map(e => e.type);

test('THE BANK keeps the House Book numbers, straight off counterfx', () => {
  assert.equal(BANK.FLY_MS, 560);
  assert.equal(BANK.STAGGER_MS, 70);
  assert.deepEqual([BANK.MIN, BANK.MAX, BANK.MAX_LITE], [3, 7, 4]);
  assert.equal(BANK.FLY_MS, CFX.BANK_FLY_MS);
  assert.equal(BANK.STAGGER_MS, CFX.BANK_STAGGER_MS);
  assert.ok(Object.isFrozen(BANK));
});

test('it is the same engine the two stations run: counts, flight and ticks all agree', () => {
  for (const t of [0, 1, 2, 3, 4]) {
    for (const lite of [false, true]) {
      assert.equal(winTokens(t, lite), slotWinTokens(t, lite), `slot tier ${t} lite ${lite}`);
      assert.equal(winTokens(t, lite), wheelWinTokens(t, lite), `wheel tier ${t} lite ${lite}`);
    }
  }
  assert.equal(BANK.FLY_MS, FEEL.BANK_FLY_MS);
  for (const n of [1, 3, 4, 7]) {
    assert.equal(bankFlightMs(n), slotFlight(n));
    assert.deepEqual(tickValues(0, 97, n), slotTicks(0, 97, n));
    for (const ms of [0, 700, 2000, 6000]) assert.deepEqual(rollupTicks(0, 97, n, ms), slotRollup(0, 97, n, ms));
  }
  for (const i of [0, 1, 6]) assert.equal(bankLandMs(i), slotLand(i));
});

test('Brake 8: 3 to 7 tokens, 4 on a lite board, and a spend counts by its price', () => {
  assert.deepEqual([0, 1, 2, 3, 4].map(t => winTokens(t)), [3, 3, 4, 5, 7]);
  for (const t of [0, 1, 2, 3, 4]) assert.ok(winTokens(t, true) <= BANK.MAX_LITE);
  assert.deepEqual([10, 60, 200, 600, 1000].map(c => spendTokens(c)), [3, 4, 5, 6, 7]);
  assert.ok(spendTokens(1000, true) <= BANK.MAX_LITE);
});

test('Law I: the ladder always ENDS on the tape number, never on a rounding of it', () => {
  for (const n of [1, 3, 4, 7]) {
    for (const to of [1, 7, 97, 550, 12345]) {
      const v = tickValues(0, to, n);
      assert.equal(v.length, n);
      assert.equal(v[v.length - 1], to, `n ${n} to ${to}`);
    }
  }
  assert.equal(rollupAt(0, 550, 1), 550);
  assert.equal(rollupAt(0, 550, 9), 550);
  assert.equal(rollupAt(0, 550, -1), 0);
});

test('the path: three lanes, a bow above the line, and nothing drawn before it leaves', () => {
  const from = { x: 0, y: 0 }, to = { x: 200, y: 100 };
  const waiting = tokenAt(2, 0, from, to);
  assert.equal(waiting.state, 'waiting');
  assert.equal(waiting.opacity, 0);
  const mid = tokenAt(0, BANK.FLY_MS / 2, from, to);
  assert.equal(mid.state, 'flying');
  assert.ok(mid.y < 0, 'the arc bows above the straight line');
  assert.ok(mid.scale < 1 && mid.scale > 0.6);
  const down = tokenAt(0, BANK.FLY_MS, from, to);
  assert.deepEqual([down.state, down.x, down.y], ['landed', 200, 100]);
  // The reversed spend is the same arc run the other way: the engine is told nothing about which is which.
  const back = tokenAt(0, BANK.FLY_MS, to, from);
  assert.deepEqual([back.x, back.y], [0, 0]);
  assert.equal(tokenQ(2, 2 * BANK.STAGGER_MS), 0);
});

test('Law X: a PAY ticks the readout as each token LANDS, never before', () => {
  const r = run();
  assert.equal(r.flight, 700);
  assert.deepEqual(r.values, [10, 20, 30]);
  assert.deepEqual(ticks(r.step(0).events), []);
  assert.deepEqual(ticks(r.step(559).events), [], 'not one SP before the first landing');
  assert.deepEqual(ticks(r.step(560).events), [10]);
  assert.deepEqual(ticks(r.step(630).events), [20]);
  const last = r.step(700);
  assert.deepEqual(ticks(last.events), [30]);
  assert.deepEqual(types(last.events), ['tick', 'land', 'done']);
  assert.equal(last.events.find(e => e.type === 'land').counting, false, 'the mini-thud rides the last landing');
  assert.equal(r.done, true);
  assert.equal(r.shown, 30);
});

test('the reversed spend: the readout ticks DOWN as each token LEAVES', () => {
  const r = createBankRun({ kind: 'spend', n: 3, fromValue: 100, toValue: 70, startMs: 0 });
  assert.deepEqual(ticks(r.step(0).events), [90], 'the first token is away, the readout felt it');
  assert.deepEqual(ticks(r.step(70).events), [80]);
  assert.deepEqual(ticks(r.step(140).events), [70]);
  assert.equal(r.shown, 70);
  const end = r.step(700);
  assert.deepEqual(types(end.events), ['land', 'done']);
  assert.equal(r.done, true);
});

test('the rollup tail: the mini-thud waits for the END of the count, not the end of the flight', () => {
  const r = run({ rollupMs: 2000 });
  assert.equal(r.total, 2000);
  r.step(560); r.step(630);
  const landed = r.step(700);
  const land = landed.events.find(e => e.type === 'land');
  assert.equal(land.counting, true, 'the tokens are down and the readout is still counting');
  assert.ok(r.shown < 30, 'it has not reached the tape number yet');
  const mid = r.step(1350);
  assert.ok(ticks(mid.events).length === 1 && r.shown > 17 && r.shown < 30);
  assert.equal(mid.events[0].tail, true, 'a tail tick is a count, not a gesture');
  const end = r.step(2000);
  assert.equal(r.shown, 30);
  assert.equal(end.events.find(e => e.type === 'land').counting, false);
  assert.equal(r.done, true);
});

test('Law VI: skip() puts the readout on the SETTLED value at once, not on the last rung it ticked', () => {
  const r = run({ rollupMs: 6000 });
  r.step(700);
  assert.ok(r.shown < 30);
  const evs = r.skip({ land: true });
  assert.deepEqual(types(evs), ['tick', 'land', 'done']);
  assert.equal(evs[0].value, 30);
  assert.equal(evs[1].counting, false, 'a lever press takes the whole settled state, thud and all');
  assert.equal(r.shown, 30);
  assert.equal(r.done, true);
  assert.deepEqual(r.skip({ land: true }), [], 'a second skip is a no-op');
  assert.deepEqual(r.step(9999).events, []);
});

test('Law VI: Back and suspend leave quietly - the value, no thud', () => {
  const r = run({ rollupMs: 6000 });
  r.step(700);
  const evs = r.skip();
  assert.deepEqual(types(evs), ['tick', 'done']);
  assert.equal(evs[0].value, 30);
});

test('Law VI: reduced motion takes no tokens and the final value, with the cue still playing', () => {
  const r = run({ reduced: true, rollupMs: 6000 });
  assert.equal(r.mode, 'state');
  const out = r.step(0);
  assert.deepEqual(out.tokens, [], 'no travel at all');
  assert.deepEqual(types(out.events), ['tick', 'land', 'done']);
  assert.equal(out.events[0].value, 30);
  assert.equal(out.events[1].counting, false, 'the cue still plays, on the settled frame');
  assert.equal(r.done, true);
});

test('Brake 2: a pay landing inside a pay MERGES - the tokens down keep what they ticked', () => {
  const r = run();
  r.step(560);
  assert.equal(r.shown, 10);
  assert.equal(r.merge(90), 'merged');
  assert.equal(r.settled, 90);
  assert.deepEqual(r.values, [10, 50, 90], 'only the tokens still in the air are re-aimed');
  assert.deepEqual(ticks(r.step(630).events), [50]);
  assert.deepEqual(ticks(r.step(700).events), [90]);
  assert.equal(r.shown, 90);
});

test('Brake 2: a merge never stacks a second beat, and a spend is never merged into', () => {
  const r = run();
  r.step(560);
  const before = r.step(561).events.length;
  r.merge(90);
  assert.equal(before, 0, 'the merge itself fires nothing');
  const s = createBankRun({ kind: 'spend', n: 3, fromValue: 100, toValue: 70 });
  assert.equal(s.merge(50), null);
  const d = run();
  d.skip();
  assert.equal(d.merge(90), null, 'a settled run is closed to merges');
});

test('step() is edge-triggered: twice on one frame fires once, a dropped frame loses nothing', () => {
  const a = run();
  a.step(560);
  assert.deepEqual(a.step(560).events, [], 'the same frame twice is a no-op');
  const b = run();
  const all = b.step(700);             // every token landed inside one dropped frame
  assert.deepEqual(ticks(all.events), [10, 20, 30], 'every tick still fired, in order');
  assert.equal(b.shown, 30);
  assert.equal(b.done, true);
});

test('the clock is the callers: startMs shifts the whole run and nothing else', () => {
  const r = run({ startMs: 100000 });
  assert.deepEqual(ticks(r.step(100000).events), []);
  assert.deepEqual(ticks(r.step(100560).events), [10]);
  assert.deepEqual(ticks(r.step(100700).events), [20, 30]);
});

test('nonsense in, a settled run out - createBankRun never throws', () => {
  const r = createBankRun();
  assert.equal(r.kind, 'pay');
  assert.equal(r.n, 1);
  assert.equal(r.settled, 0);
  const out = r.step(9999);
  assert.equal(out.done, true);
  assert.equal(r.shown, 0);
  assert.equal(createBankRun({ n: -4 }).n, 1);
  assert.equal(createBankRun({ n: NaN, toValue: 'x' }).settled, 0);
});

/* step() hands back a FRAME ({ shown, tokens, events, ... }); only skip() hands back a bare event array.
 * Every station bank feeds its own play() an array, so a bank that plays the frame itself throws
 * "events is not iterable" and takes the whole settle down with it - which is what the card and the
 * roulette tables did on their reduced-motion path (Law VI, mode 'state') until 2026-09-19. */
test('a station bank plays step().events, never the frame step() returned', async () => {
  const { readFile } = await import('node:fs/promises');
  const { fileURLToPath } = await import('node:url');
  const { resolve, dirname } = await import('node:path');
  const stations = resolve(dirname(fileURLToPath(import.meta.url)), '../../../stations');
  const offenders = [];
  for (const id of ['cards', 'roulette', 'slot', 'wheel']) {
    const src = await readFile(resolve(stations, id, 'bank.js'), 'utf8');
    for (const raw of src.split('\n')) {
      const line = raw.trim();
      if (line.includes('play(') && line.includes('.step(') && !/\.step\([^)]*\)\s*\.events/.test(line)) offenders.push(id + ': ' + line);
    }
  }
  assert.deepEqual(offenders, []);
});
