import test from 'node:test';
import assert from 'node:assert/strict';
import TWIST, { gateOrder, boxesFrom, GATE_LEAD_S, GATE_STEP_S, SEAL_FADE_S, LOCK_SHAKE_S } from './keys.js';
import RENDER from './keys-render.js';
import CUES, { arpLength, octaveFor, ARP_MAX } from '../cues/twist-keys.js';
import FX from '../reactions/twist-keys.js';
import { boardById, parseBoard, BOARD_COLS } from '../doors.js';

/* ---------------------------------------------------------------- harness
 * A miniature of game.js's authored wall and its doorCtx: the same brick
 * geometry, the same `at(row, col)`, the same steel-first breakBrick and the
 * same sim-time timer queue. Nothing the twist reads is faked away.
 */
const CELL_W = 48.96, CELL_H = 27.54, GAP = 6, TOP = 26;

function harness(rows) {
  const bricks = [], grid = new Map(), events = [], timers = [];
  let clock = 0, rngCalls = 0, seed = 7;
  for (const cell of parseBoard(rows)) {
    const br = { x: 20 + cell.col * (CELL_W + GAP), y: TOP + cell.row * (CELL_H + GAP), w: CELL_W, h: CELL_H,
      alive: true, row: cell.row, col: cell.col, hp: 1, ...cell.spec };
    bricks.push(br); grid.set(cell.row * BOARD_COLS + cell.col, br);
  }
  const g = { bricks, state: 'colour', reduced: false, time: 0 };
  const ctx = {
    w: 1280, h: 720,
    emit: (name, data) => events.push({ name, data }),
    rng: () => { rngCalls++; seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; },
    at: (row, col) => grid.get(row * BOARD_COLS + col) || null,
    breakBrick(br) {
      if (!br || !br.alive) return;
      br.steel = false; br.gate = null; br.alive = false;
      events.push({ name: 'brick', data: { row: br.row, col: br.col } });
      TWIST.onBreak(g, br, null, ctx);
    },
    schedule(seconds, fn) { const timer = { at: clock + seconds, fn, dead: false }; timers.push(timer); return () => { timer.dead = true; }; },
    powers: { drop() {}, reset() {} },
    startRelapse() {},
  };
  /** Run the sim forward: due timers first, in due order, then the twist's update. */
  function step(dt) {
    clock += dt;
    const due = timers.filter(t => !t.dead && t.at <= clock).sort((a, b) => a.at - b.at);
    for (const t of due) t.dead = true;
    for (const t of due) t.fn(g, ctx);
    TWIST.update(g, dt, ctx);
  }
  const hit = br => { TWIST.onHit(g, br, null, ctx); };
  const pop = br => { br.alive = false; TWIST.onBreak(g, br, null, ctx); };
  const named = name => events.filter(e => e.name === name);
  const cell = (row, col) => ctx.at(row, col);
  TWIST.build(g, ctx);
  return { g, ctx, events, named, step, pop, hit, cell, timers, rngCalls: () => rngCalls };
}

const KEYS_BOARD = boardById('lock_keys2').board.rows;
/** A small board of my own: one gold box with a key, and cyan plates with no key on the board. */
const SMALL = ['MMMM.WW', 'MU2M.WW', 'MMMM...', '.......', 'K......'].map(r => (r + '.'.repeat(BOARD_COLS)).slice(0, BOARD_COLS));

/* ----------------------------------------------------------------- shape */
test('keys: the module keeps the shape twists/CONTRACT.md froze', () => {
  assert.equal(TWIST.id, 'keys');
  for (const hook of ['build', 'onHit', 'onBreak', 'update', 'onCatch', 'wallCleared'])
    if (TWIST[hook] !== undefined) assert.equal(typeof TWIST[hook], 'function', hook + ' must be a function or absent');
  for (const hook of ['brick', 'under', 'over'])
    if (RENDER[hook] !== undefined) assert.equal(typeof RENDER[hook], 'function', hook + ' must be a function or absent');
  assert.deepEqual(Object.keys(CUES).sort(), ['gateOpen', 'keyTurn']);
  assert.deepEqual(Object.keys(FX).sort(), ['gateOpen', 'keyTurn']);
});

/* ------------------------------------------------------------- loot boxes */
test('keys: a box is the rectangle its plates enclose, and it knows its prize', () => {
  const h = harness(KEYS_BOARD);
  const boxes = h.g.keysTwist.boxes;
  assert.equal(boxes.length, 2);
  const gold = boxes.find(b => b.gate === 'M'), cyan = boxes.find(b => b.gate === 'W');
  assert.equal(gold.prize, 'multiball');                                  // MUG2M: the U inside the gold box
  assert.equal(cyan.prize, 'fireball');                                   // W2GFW: the F inside the cyan one
  for (const box of [gold, cyan]) {
    assert.equal(box.seal, 1); assert.equal(box.opening, false);
    assert.ok(box.w > CELL_W * 2 && box.w < CELL_W * 4, 'three cells wide, and not the plates too');
    assert.ok(box.h > 0 && box.h <= CELL_H + 1, 'one course tall');
  }
  assert.ok(gold.x < cyan.x, 'the gold box is the left one');
});

test('keys: plates with nothing behind them are not a box', () => {
  const rows = ['MM..............', 'MM..............', '................'];
  assert.deepEqual(boxesFrom(harness(rows).g.bricks, () => null), []);
});

test('keys: a box survives every plate coming down (the rect is measured once)', () => {
  const h = harness(KEYS_BOARD);
  const before = { ...h.g.keysTwist.boxes.find(b => b.gate === 'M') };
  h.pop(h.cell(6, 14));                                                   // K, the gold key
  for (let i = 0; i < 40; i++) h.step(1 / 60);
  const after = h.g.keysTwist.boxes.find(b => b.gate === 'M');
  assert.equal(after.x, before.x); assert.equal(after.w, before.w);
  assert.equal(after.seal, 0, 'the pane is gone once the box is open');
});

/* ------------------------------------------------------------ the ripple */
test('keys: the plates let go nearest the key first, outward', () => {
  const h = harness(KEYS_BOARD);
  const key = h.cell(6, 14);
  const plates = h.g.bricks.filter(b => b.gate === 'W');
  const kx = key.x + key.w / 2, ky = key.y + key.h / 2;
  const order = gateOrder(plates, kx, ky);
  const d = br => Math.hypot(br.x + br.w / 2 - kx, br.y + br.h / 2 - ky);
  for (let i = 1; i < order.length; i++) assert.ok(d(order[i]) >= d(order[i - 1]) - 1e-9, 'never steps back toward the key');
  assert.ok(order[0].col > order[order.length - 1].col, 'it starts at the near edge column');
});

test('keys: gateOrder ties break on row then column, so the ripple never shuffles', () => {
  const mk = (row, col) => ({ row, col, x: col * 10, y: row * 10, w: 10, h: 10 });
  const a = mk(0, 1), b = mk(1, 0);                                       // the same distance from the key
  assert.deepEqual(gateOrder([b, a], 15, 15).map(x => [x.row, x.col]), [[0, 1], [1, 0]]);
  assert.deepEqual(gateOrder([a, b], 15, 15).map(x => [x.row, x.col]), [[0, 1], [1, 0]]);
});

test('keys: a gold key opens the gold box and leaves the cyan one locked', () => {
  const h = harness(KEYS_BOARD);
  const goldPlates = h.g.bricks.filter(b => b.gate === 'M').length;
  h.pop(h.cell(6, 14));
  assert.deepEqual(h.named('keyTurn').map(e => e.data.gate), ['M']);
  const open = h.named('gateOpen');
  assert.equal(open.length, 1);
  assert.equal(open[0].data.gate, 'M');
  assert.equal(open[0].data.n, goldPlates);
  assert.ok(h.events.findIndex(e => e.name === 'keyTurn') < h.events.findIndex(e => e.name === 'gateOpen'),
    'the lock turns before the box lets go');
  for (let i = 0; i < 40; i++) h.step(1 / 60);
  assert.equal(h.g.bricks.filter(b => b.alive && b.gate === 'M').length, 0);
  assert.ok(h.g.bricks.filter(b => b.alive && b.gate === 'W').length > 0, 'the other trim is untouched');
});

test('keys: the first plate waits the lead, and the whole box is quick', () => {
  const h = harness(SMALL);
  const plates = h.g.bricks.filter(b => b.gate === 'M').length;
  h.pop(h.cell(4, 0));                                                    // K
  for (let i = 0; i < Math.floor((GATE_LEAD_S - 0.02) * 60); i++) h.step(1 / 60);
  assert.equal(h.g.bricks.filter(b => b.alive && b.gate === 'M').length, plates, 'nothing moves before the lead');
  const span = GATE_LEAD_S + plates * GATE_STEP_S + 0.05;
  for (let i = 0; i < Math.round(span * 60); i++) h.step(1 / 60);
  assert.equal(h.g.bricks.filter(b => b.alive && b.gate === 'M').length, 0);
  assert.ok(span < 0.8, 'a box is open inside a moment, not a wait');
});

test('keys: a key at the board edge opens its box just the same', () => {
  const h = harness(KEYS_BOARD);
  h.pop(h.cell(6, 2));                                                    // Q, two columns from the left wall
  assert.equal(h.named('gateOpen')[0].data.gate, 'W');
  for (let i = 0; i < 40; i++) h.step(1 / 60);
  assert.equal(h.g.bricks.filter(b => b.alive && b.gate === 'W').length, 0);
  const cols = h.named('brick').map(e => e.data.col);
  assert.ok(cols.includes(11) && cols.includes(15), 'both edge columns of the box came down');
});

test('keys: a second turn on an open lock is a turn and nothing else', () => {
  const h = harness(SMALL);
  h.pop(h.cell(4, 0));
  for (let i = 0; i < 60; i++) h.step(1 / 60);
  const before = h.named('gateOpen').length;
  const ghost = { alive: false, key: 'M', x: 0, y: 0, w: 10, h: 10, row: 4, col: 0 };
  TWIST.onBreak(h.g, ghost, null, h.ctx);
  assert.equal(h.named('gateOpen').length, before, 'no second gateOpen');
  assert.equal(h.named('keyTurn').length, 2, 'the lock still turns');
});

/* ------------------------------------------------------- the cracked key */
test('keys: the cracked key primes the clay through the crumble module', () => {
  const h = harness(KEYS_BOARD);
  const clay = h.g.bricks.filter(b => b.clay);
  assert.ok(clay.length >= 10 && clay.every(b => b.hp === 3), 'the wax seal starts healthy');
  h.pop(h.cell(6, 8));                                                    // P
  assert.deepEqual(h.named('keyTurn').map(e => e.data.gate), ['clay']);
  assert.equal(h.named('clayPrimed')[0].data.n, clay.length);
  assert.equal(h.named('clayReady').length, clay.length);
  assert.ok(clay.every(b => b.hp === 1), 'every clay brick is precarious');
  assert.equal(h.named('gateOpen').length, 0, 'it opens no box');
  assert.ok(h.g.bricks.filter(b => b.alive && b.gate).length > 0, 'both boxes stay shut');
});

test('keys: the cracked key on a board with no clay is harmless', () => {
  const h = harness(SMALL);
  const ghost = { alive: false, key: 'clay', x: 0, y: 0, w: 10, h: 10, row: 4, col: 3 };
  TWIST.onBreak(h.g, ghost, null, h.ctx);
  assert.equal(h.named('keyTurn').length, 1);
  assert.equal(h.named('clayPrimed').length, 0);
  assert.equal(h.g.keysTwist.beams.length, 0, 'no beam to nowhere');
});

/* ------------------------------------------------- locked plates and time */
test('keys: a ball on a locked plate rattles it, and the rattle settles', () => {
  const h = harness(KEYS_BOARD);
  const plate = h.g.bricks.find(b => b.gate === 'M');
  h.hit(plate);
  assert.equal(plate.lockT, LOCK_SHAKE_S);
  h.step(0.1);
  assert.ok(plate.lockT > 0 && plate.lockT < LOCK_SHAKE_S);
  for (let i = 0; i < 20; i++) h.step(1 / 60);
  assert.equal(plate.lockT, 0, 'it settles, it never goes negative');
  assert.equal(plate.alive, true, 'a locked plate never breaks to a ball');
});

test('keys: beams fade out and the pane melts over the fade window', () => {
  const h = harness(KEYS_BOARD);
  h.pop(h.cell(6, 14));
  assert.equal(h.g.keysTwist.beams.length, 1);
  const box = h.g.keysTwist.boxes.find(b => b.gate === 'M');
  assert.equal(box.opening, true);
  h.step(SEAL_FADE_S / 2);
  assert.ok(box.seal > 0.3 && box.seal < 0.7, 'halfway through the fade');
  for (let i = 0; i < 60; i++) h.step(1 / 60);
  assert.equal(box.seal, 0);
  assert.equal(h.g.keysTwist.beams.length, 0, 'the beam is gone');
});

test('keys: update on a board without the twist does nothing and never throws', () => {
  const g = { bricks: [] };
  assert.doesNotThrow(() => TWIST.update(g, 0.016, {}));
  TWIST.wallCleared(g);
  assert.equal(g.keysTwist, null);
});

/* ------------------------------------------------------------ determinism */
test('keys: the same board plays the same way twice, and never asks for a roll', () => {
  const run = () => {
    const h = harness(KEYS_BOARD);
    h.pop(h.cell(6, 14)); h.pop(h.cell(6, 2)); h.pop(h.cell(6, 8));
    for (let i = 0; i < 90; i++) h.step(1 / 60);
    return { trace: h.events.map(e => e.name + ':' + JSON.stringify(e.data)), rolls: h.rngCalls() };
  };
  const a = run(), b = run();
  assert.deepEqual(a.trace, b.trace);
  assert.equal(a.rolls, 0, 'the twist is decided by the board, not by a roll');
  assert.ok(a.trace.length > 20);
});

/* ---------------------------------------------------------------- sounds */
test('keys: the arpeggio is one note per plate, capped, and the trims sit an octave apart', () => {
  assert.equal(arpLength(1), 2); assert.equal(arpLength(3), 3); assert.equal(arpLength(99), ARP_MAX);
  assert.equal(arpLength(undefined), 2);
  assert.equal(octaveFor('W') / octaveFor('M'), 2);
  assert.ok(octaveFor('clay') < octaveFor('M'));
});

/* --------------------------------------------------------------- drawing */
function stub2d() {
  const calls = [];
  const rec = name => (...args) => { calls.push(name); return name === 'createRadialGradient' || name === 'createLinearGradient' ? { addColorStop() {} } : undefined; };
  const x = { calls };
  for (const name of ['save', 'restore', 'beginPath', 'moveTo', 'lineTo', 'quadraticCurveTo', 'closePath', 'fill', 'stroke',
    'arc', 'fillRect', 'strokeRect', 'clip', 'translate', 'rotate', 'scale', 'createRadialGradient', 'createLinearGradient'])
    x[name] = rec(name);
  return x;
}

test('keys: every render hook draws without a canvas and without throwing, reduced or not', () => {
  const h = harness(KEYS_BOARD);
  h.pop(h.cell(6, 14));
  h.step(1 / 60);
  for (const reduced of [false, true]) {
    h.g.reduced = reduced;
    const x = stub2d();
    for (const br of h.g.bricks.filter(b => b.gate || b.key)) RENDER.brick(x, br, h.g, 1.25);
    RENDER.under(x, h.g, 1.25);
    RENDER.over(x, h.g, 1.25);
    assert.ok(x.calls.length > 40, 'it actually drew something');
  }
});

test('keys: a rattled plate jitters in colour and holds still under reduced motion', () => {
  const plate = { gate: 'M', lockT: LOCK_SHAKE_S, x: 0, y: 0, w: 48, h: 27, row: 0, col: 0 };
  const colour = stub2d(); RENDER.brick(colour, plate, { state: 'colour', reduced: false }, 3.1);
  const still = stub2d(); RENDER.brick(still, plate, { state: 'colour', reduced: true }, 3.1);
  assert.equal(colour.calls.includes('translate'), true);
  assert.equal(still.calls.includes('translate'), false);
  assert.ok(still.calls.filter(c => c === 'stroke').length >= 2, 'the flare still says denied');
});

test('keys: the render hooks survive a board that has no twist state', () => {
  const x = stub2d();
  assert.doesNotThrow(() => { RENDER.under(x, { bricks: [] }, 0); RENDER.over(x, { bricks: [] }, 0); });
  assert.doesNotThrow(() => RENDER.brick(x, { row: 0, col: 0, x: 0, y: 0, w: 10, h: 10 }, { state: 'grey' }, 0));
});
