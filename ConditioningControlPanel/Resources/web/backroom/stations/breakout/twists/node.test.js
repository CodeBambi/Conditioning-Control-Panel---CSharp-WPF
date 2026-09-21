import test from 'node:test';
import assert from 'node:assert/strict';
import TWIST, { computePower, repower, darkGroups, groupCentre, POP_LEAD_S, POP_STEP_S } from './node.js';
import RENDER, { pulseAt } from './node-render.js';
import { parseBoard, BOARD_COLS } from '../doors.js';

/* --------------------------------------------------------------------------
 * A board harness the shape game.js builds: the same cell grid, the same
 * `ctx`, the same break path (a wire that breaks calls onBreak, which is how
 * the net finds out). Timers run on a sim clock, in due order, like the game's.
 * ------------------------------------------------------------------------*/
const CELL_W = 48.96, CELL_H = 27.54, GAP = 6, TOP = 26;

function harness(rows) {
  const g = { bricks: [] };
  const grid = new Map();
  for (const cell of parseBoard(rows)) {
    const br = {
      x: cell.col * (CELL_W + GAP), y: TOP + cell.row * (CELL_H + GAP), w: CELL_W, h: CELL_H,
      alive: true, row: cell.row, col: cell.col, ...cell.spec,
    };
    g.bricks.push(br);
    grid.set(cell.row * BOARD_COLS + cell.col, br);
  }
  const events = [];
  let clock = 0, timers = [], seed = 1337;
  const ctx = {
    w: 960, h: 540,
    rng: () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; },
    emit: (name, data) => { events.push({ name, ...data }); },
    at: (row, col) => grid.get(row * BOARD_COLS + col) || null,
    schedule(seconds, fn) {
      const timer = { at: clock + Math.max(0, Number(seconds) || 0), fn, dead: false };
      timers.push(timer);
      return () => { timer.dead = true; };
    },
    // game.js, exactly: ctx.breakBrick clears steel and gate steel and then runs the
    // ORDINARY contact path, armour ladder and all. Faithful on purpose: a twist that
    // hands it a three-hit brick gets a chip, not a break, and this harness says so.
    breakBrick: (br) => { if (br && br.alive) { br.steel = false; br.gate = null; hit(br); } },
    powers: { drop() {}, reset() {} },
    startRelapse() {},
  };
  /** The break itself: the game's own order, the `brick` event then the twist. */
  function kill(br) {
    br.alive = false;
    events.push({ name: 'brick', row: br.row, col: br.col });
    TWIST.onBreak(g, br, null, ctx);
  }
  /** One ball contact, the way game.js resolves it: armour first, then the break. */
  function hit(br) {
    if (!br || !br.alive || br.steel) return;
    if (br.strength && br.hp > 1) { br.hp--; return; }
    kill(br);
  }
  /** Advance the sim clock and fire whatever came due, in due order. */
  function tick(dt) {
    clock += dt;
    const due = timers.filter(t => !t.dead && t.at <= clock).sort((a, b) => a.at - b.at);
    timers = timers.filter(t => !t.dead && t.at > clock);
    for (const timer of due) timer.fn(g, ctx);
  }
  const cell = (row, col) => ctx.at(row, col);
  const named = name => events.filter(e => e.name === name);
  TWIST.build(g, ctx);
  return { g, ctx, events, hit, tick, cell, named, pending: () => timers.length, clockNow: () => clock };
}

/* The shipped board, so the tests fail when doors.js moves under us. */
const HIVE = ['2222222222222222', '2wwwwwwwwwwwwww2', '2w11w1Gw1G1w11w2', '2w11w11C111w11w2',
  '.w11w111111w11w.', '.w..w......w..w.', '.w............w.'];
const livingWires = g => g.bricks.filter(b => b.alive && b.wire);

test('node: the module keeps the shape twists/CONTRACT.md froze', () => {
  assert.equal(TWIST.id, 'node');
  for (const hook of ['build', 'onHit', 'onBreak', 'update', 'onCatch', 'wallCleared'])
    if (TWIST[hook] !== undefined) assert.equal(typeof TWIST[hook], 'function', hook + ' must be a function or absent');
  for (const hook of ['brick', 'under', 'over'])
    if (RENDER[hook] !== undefined) assert.equal(typeof RENDER[hook], 'function', hook + ' must be a function or absent');
});

test('node: build lights the whole shipped net and says nothing about it', () => {
  const h = harness(HIVE);
  assert.equal(h.named('nodeCut').length, 0, 'a fresh board never reports a cut');
  const wires = livingWires(h.g);
  assert.ok(wires.length > 20, 'the hive board is a big net');
  assert.ok(wires.every(b => b.powered === true), 'every authored wire reaches the core');
  assert.ok(wires.every(b => b.hp === 3 && b.strength === 3), 'a powered wire is three hits');
  assert.equal(h.cell(3, 7).netDepth, 0, 'the core is depth zero');
  assert.equal(h.cell(2, 7).netDepth, 1, 'the wire over the core is one step out');
  assert.equal(h.cell(1, 7).netDepth, 2, 'then the bus');
  assert.equal(h.cell(1, 8).netDepth, 3, 'and along it');
});

test('node: the current runs four ways only, never a diagonal and never round a row', () => {
  //  a core, a wire directly under it, a wire only diagonal to it, and two wires
  //  at the two ends of the same row, which are fifteen columns apart, not one.
  const h = harness(['w.....C......w..', '......w.......w.', '.......w........']);
  assert.equal(h.cell(0, 6).netDepth, 0);
  assert.equal(h.cell(1, 6).netDepth, 1, 'straight down is one step');
  assert.equal(h.cell(2, 7).powered, false, 'a diagonal neighbour is not a neighbour');
  assert.equal(h.cell(0, 0).powered, false, 'column zero does not wrap to column fifteen');
  assert.equal(h.cell(0, 13).powered, false);
  assert.equal(h.cell(1, 14).powered, false);
});

test('node: a cut wire goes pale, goes soft, and takes exactly one hit', () => {
  const h = harness(['..C.............', '..w.............', '..w.............']);
  const far = h.cell(2, 2);
  assert.equal(far.hp, 3);
  h.hit(h.cell(1, 2)); h.hit(h.cell(1, 2)); h.hit(h.cell(1, 2));   // three hits to snip the link
  assert.equal(far.powered, false);
  assert.equal(far.hp, 1, 'one hit left');
  assert.equal(far.strength, 0, 'and the armoured plate came off with the current');
  h.hit(far);
  assert.equal(far.alive, false, 'so the next contact breaks it');
});

test('node: snipping the one wire under the core darkens the whole hive net at once', () => {
  const h = harness(HIVE);
  const link = h.cell(2, 7);
  const before = livingWires(h.g).length;
  h.hit(link); h.hit(link); h.hit(link);
  const cuts = h.named('nodeCut');
  assert.equal(cuts.length, 1, 'one net, one failure, not one per brick');
  assert.equal(cuts[0].n, before - 1, 'everything but the wire that broke');
  assert.ok(livingWires(h.g).every(b => b.powered === false));
  assert.ok(Number.isFinite(cuts[0].x) && Number.isFinite(cuts[0].y), 'the report has somewhere to draw');
});

test('node: two arms orphaned by one break are two failures, not one', () => {
  //  One core feeding a bus. Break the middle of the bus and the two ends go
  //  dark independently: they never touch each other.
  const h = harness(['.......C........', '.wwwwwwwwwwwwww.']);
  assert.equal(h.named('nodeCut').length, 0);
  const middle = h.cell(1, 7);
  h.hit(middle); h.hit(middle); h.hit(middle);
  const cuts = h.named('nodeCut');
  assert.equal(cuts.length, 2, 'left arm and right arm');
  assert.deepEqual(cuts.map(c => c.n).sort((a, b) => a - b), [6, 7]);
  assert.ok(cuts[0].x < cuts[1].x, 'reported in board order, left first');
});

test('node: darkGroups keeps touching bricks together and groupCentre sits in the middle', () => {
  const h = harness(['.......C........', '.wwwwwwwwwwwwww.']);
  const pair = [h.cell(1, 1), h.cell(1, 2), h.cell(1, 12)];
  const groups = darkGroups(pair, h.ctx);
  assert.equal(groups.length, 2);
  assert.equal(groups[0].length, 2);
  assert.equal(groups[1].length, 1);
  const middle = groupCentre(groups[0]);
  assert.ok(middle.x > h.cell(1, 1).x && middle.x < h.cell(1, 2).x + h.cell(1, 2).w);
});

test('node: killing the core blows the net outward, a step per wire distance', () => {
  const h = harness(['..C.............', '..w.............', '..w.............', '..w.............']);
  const core = h.cell(0, 2);
  const doomed = livingWires(h.g).length;
  h.hit(core); h.hit(core); h.hit(core);
  const down = h.named('coreDown');
  assert.equal(down.length, 1);
  assert.equal(down[0].n, doomed, 'it says how big the net was');
  assert.equal(h.named('nodeCut').length, 0, 'the net blows, it does not quietly go dark');
  assert.equal(h.named('nodeZap').length, 0, 'and nothing has popped yet');

  h.tick(POP_LEAD_S + POP_STEP_S + 0.001);
  assert.deepEqual(h.named('nodeZap').map(z => z.depth), [1], 'the wire nearest the core goes first');
  h.tick(POP_STEP_S);
  h.tick(POP_STEP_S);
  const zaps = h.named('nodeZap');
  assert.deepEqual(zaps.map(z => z.depth), [1, 2, 3], 'then outward, in order');
  assert.equal(livingWires(h.g).length, 0, 'and every one of them is gone');
  assert.equal(h.pending(), 0, 'with no timer left behind');
});

test('node: the blast never also reports a cut, however many wires it takes', () => {
  const h = harness(HIVE);
  const core = h.cell(3, 7);
  h.hit(core); h.hit(core); h.hit(core);
  for (let i = 0; i < 40; i++) h.tick(POP_STEP_S);
  assert.equal(h.named('nodeCut').length, 0);
  assert.equal(livingWires(h.g).length, 0);
  assert.equal(h.named('coreDown')[0].n, h.named('nodeZap').length, 'it popped exactly what it promised');
});

test('node: the blast breaks armoured wires outright, it never just chips them', () => {
  // ctx.breakBrick walks game.js's own hp ladder, so a three-hit wire handed to it
  // raw would survive the blast at two hits. If this goes red the workaround is gone.
  const h = harness(['..C.............', '..w.............', '..w.............']);
  const core = h.cell(0, 2);
  assert.equal(h.cell(1, 2).hp, 3, 'the wires really are armoured');
  h.hit(core); h.hit(core); h.hit(core);
  for (let i = 0; i < 10; i++) h.tick(POP_STEP_S);
  assert.deepEqual(h.g.bricks.filter(b => b.wire).map(b => b.alive), [false, false]);
});

test('node: a second core keeps the lights on, and only the last one blows the net', () => {
  const h = harness(['C.......C.......', 'w.......w.......', 'wwwwwwwwwwwwwwww']);
  const first = h.cell(0, 0);
  h.hit(first); h.hit(first); h.hit(first);
  assert.equal(h.named('coreDown').length, 0, 'the other core still feeds the net');
  assert.equal(h.named('nodeCut').length, 0, 'and nothing lost its path');
  assert.ok(livingWires(h.g).every(b => b.powered === true));
  const second = h.cell(0, 8);
  h.hit(second); h.hit(second); h.hit(second);
  assert.equal(h.named('coreDown').length, 1);
});

test('node: the same board played the same way twice reads back the same', () => {
  const play = () => {
    const h = harness(HIVE);
    const bus = h.cell(1, 4);
    h.hit(bus); h.hit(bus); h.hit(bus);
    const core = h.cell(3, 7);
    h.hit(core); h.hit(core); h.hit(core);
    for (let i = 0; i < 40; i++) h.tick(POP_STEP_S);
    for (let i = 0; i < 6; i++) h.ctx.rng();
    return h.events;
  };
  assert.deepEqual(play(), play());
});

test('node: repower is idempotent and silent when nothing moved', () => {
  const h = harness(HIVE);
  assert.equal(repower(h.g, h.ctx).length, 0);
  assert.equal(computePower(h.g, h.ctx).length, 0);
  assert.equal(h.named('nodeCut').length, 0);
});

test('node: wallCleared hands the next board a clean net', () => {
  const h = harness(HIVE);
  const core = h.cell(3, 7);
  h.hit(core); h.hit(core); h.hit(core);
  assert.equal(h.g.node.down, true);
  TWIST.wallCleared(h.g);
  assert.equal(h.g.node.down, false, 'or the next board would never light up');
});

/* ----------------------------------------------------------------- render */
/** Enough 2d context for the painters to run against. Records nothing: we only ask that it survives. */
function stubCtx() {
  const calls = [];
  const rec = name => (...args) => { calls.push(name); return name === 'createRadialGradient' ? { addColorStop() {} } : undefined; };
  const c = { calls };
  for (const name of ['beginPath', 'moveTo', 'lineTo', 'arc', 'stroke', 'fill', 'fillRect', 'strokeRect', 'createRadialGradient', 'save', 'restore'])
    c[name] = rec(name);
  return c;
}

test('node: the pulse runs outward and rests between trains', () => {
  const low = { alive: true, core: true, netDepth: 0 };
  assert.ok(pulseAt(low, 0) >= 0 && pulseAt(low, 0) < 1, 'it is on the cable at the top of a cycle');
  assert.ok(pulseAt(low, 0.1) > pulseAt(low, 0), 'and it moves along it');
  assert.equal(pulseAt(low, 0.5), -1, 'between trains the cable is just a cable');
  const far = { alive: true, wire: true, powered: true, netDepth: 3 };
  assert.equal(pulseAt(far, 0), -1, 'a far wire has not been reached yet');
  assert.ok(pulseAt(far, 3 / 5) >= 0, 'it lights one fifth of a second per cell out from the core');
});

test('node: every painter survives a live board, and reduced motion drops the pulse', () => {
  const h = harness(HIVE);
  h.g.doorColour = '#2FCB72'; h.g.time = 0.7; h.g.state = 'colour';
  for (const reduced of [false, true]) {
    h.g.reduced = reduced;
    const c = stubCtx();
    RENDER.under(c, h.g, h.g.time);
    RENDER.over(c, h.g, h.g.time);
    for (const br of h.g.bricks) if (br.wire || br.core) RENDER.brick(c, br, h.g, h.g.time);
    assert.ok(c.calls.length > 50, 'it drew the net');
  }
  const cut = h.cell(2, 7);
  h.hit(cut); h.hit(cut); h.hit(cut);
  const c = stubCtx();
  RENDER.under(c, h.g, 1.2);
  assert.ok(c.calls.length > 20, 'a dark net still draws its dead trace');
});

test('node: a painter given a board with no net at all does nothing and does not throw', () => {
  const empty = { bricks: [], reduced: false, doorColour: null };
  const c = stubCtx();
  RENDER.under(c, empty, 0);
  RENDER.over(c, empty, 0);
  assert.equal(c.calls.length, 0);
  RENDER.brick(c, { wire: false, core: false }, empty, 0);
  assert.equal(c.calls.length, 0, 'and it never paints a brick that is not on the net');
});
