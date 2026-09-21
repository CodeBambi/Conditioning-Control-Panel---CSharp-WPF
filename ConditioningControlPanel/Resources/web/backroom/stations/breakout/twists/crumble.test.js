import test from 'node:test';
import assert from 'node:assert/strict';
import TWIST, { precarious, neighbours, prime, CHAIN_STEP_S, CRUMBLE_SHOCK, N4 } from './crumble.js';
import RENDER from './crumble-render.js';
import CUES from '../cues/twist-crumble.js';
import REACTIONS from '../reactions/twist-crumble.js';

const COLS = 16;

/* A board out of the doors.js char legend, on the same 16-wide grid the real one uses.
 * `c d e` are clay at 3 / 2 / 1 hits left, `2` is plain, `.` is empty. */
function board(rows) {
  const bricks = [], grid = new Map(), events = [], timers = [];
  let clock = 0;
  rows.forEach((row, r) => [...row].forEach((ch, c) => {
    if (ch === '.') return;
    const clay = 'cde'.includes(ch);
    const hp = clay ? { c: 3, d: 2, e: 1 }[ch] : Number(ch) || 1;
    const br = { x: c * 40, y: r * 20, w: 38, h: 18, alive: true, row: r, col: c,
      clay, strength: clay ? 3 : hp, hp, push: { dx: 0, dy: 0 } };
    bricks.push(br); grid.set(r * COLS + c, br);
  }));
  const g = { bricks, reduced: false, time: 0 };
  const ctx = {
    emit: (name, data) => events.push({ name, ...data }),
    rng: () => 0.5, w: 640, h: 480,
    at: (r, c) => grid.get(r * COLS + c) || null,
    breakBrick(br) { if (!br || !br.alive) return; br.alive = false; TWIST.onBreak(g, br, null, ctx); },
    schedule(seconds, fn) { const timer = { at: clock + seconds, fn, dead: false }; timers.push(timer); return () => { timer.dead = true; }; },
    powers: { drop() {}, reset() {} }, startRelapse() {},
  };
  /** Run the scheduler forward, the way game.js runDoorTimers does: due order, on the sim clock. */
  const tick = (dt = CHAIN_STEP_S) => {
    clock += dt;
    const due = timers.filter(t => !t.dead && t.at <= clock).sort((a, b) => a.at - b.at);
    for (let i = timers.length - 1; i >= 0; i--) if (!timers[i].dead && timers[i].at <= clock) timers.splice(i, 1);
    for (const t of due) t.fn(g, ctx);
  };
  const settle = (n = 60) => { for (let i = 0; i < n; i++) tick(); };
  const named = name => events.filter(e => e.name === name);
  TWIST.build(g, ctx);
  return { g, ctx, events, named, tick, settle, at: ctx.at, alive: () => bricks.filter(b => b.alive).length };
}

/** One ball hit, the way game.js deals it: a damaging hit calls onHit, the killing one breaks it. */
function hit(t, br) {
  if (br.hp > 1) { br.hp--; TWIST.onHit(t.g, br, null, t.ctx); return; }
  t.ctx.breakBrick(br);
}

test('crumble: the module keeps the shape twists/CONTRACT.md froze', () => {
  assert.equal(TWIST.id, 'crumble');
  for (const hook of ['build', 'onHit', 'onBreak', 'update', 'onCatch', 'wallCleared'])
    if (TWIST[hook] !== undefined) assert.equal(typeof TWIST[hook], 'function', hook + ' must be a function or absent');
  for (const hook of ['brick', 'under', 'over'])
    if (RENDER[hook] !== undefined) assert.equal(typeof RENDER[hook], 'function', hook + ' must be a function or absent');
});

test('crumble: it owns only its own event names, and every one it emits is in the contract', () => {
  const mine = ['clayCrack', 'clayReady', 'clayChain', 'clayChainEnd', 'clayPrimed'];
  for (const name of Object.keys(CUES)) assert.ok(mine.includes(name), name + ' is not a crumble event');
  for (const name of Object.keys(REACTIONS)) assert.ok(mine.includes(name), name + ' is not a crumble event');
});

test('crumble: three hits, a crack each, and the third one breaks it', () => {
  const t = board(['c...............']);
  const br = t.at(0, 0);
  hit(t, br); assert.equal(br.hp, 2); assert.equal(br.alive, true);
  hit(t, br); assert.equal(br.hp, 1);
  assert.deepEqual(t.named('clayCrack').map(e => e.hp), [2, 1]);
  hit(t, br); assert.equal(br.alive, false);
});

test('crumble: precarious is exactly one hit left, alive and clay', () => {
  const t = board(['cde2............']);
  assert.equal(precarious(t.at(0, 0)), false);
  assert.equal(precarious(t.at(0, 1)), false);
  assert.equal(precarious(t.at(0, 2)), true);
  assert.equal(precarious(t.at(0, 3)), false, 'a one-hit plain brick is not clay');
  t.at(0, 2).alive = false;
  assert.equal(precarious(t.at(0, 2)), false);
  assert.equal(precarious(null), false);
});

test('crumble: the second crack says ready once, and only clay says anything', () => {
  const t = board(['d2..............']);
  hit(t, t.at(0, 1));
  assert.equal(t.named('clayCrack').length, 0, 'a plain brick is not the twist"s business');
  hit(t, t.at(0, 0));
  assert.equal(t.named('clayReady').length, 1);
});

test('crumble: popping one precarious brick takes the whole precarious vein, one link at a time', () => {
  const t = board(['eeeee...........']);
  t.ctx.breakBrick(t.at(0, 0));
  assert.equal(t.alive(), 4, 'the chain has not run yet: it is on the scheduler');
  t.tick(); assert.equal(t.alive(), 3);
  t.tick(); assert.equal(t.alive(), 2);
  t.settle();
  assert.equal(t.alive(), 0);
  assert.deepEqual(t.named('clayChain').map(e => e.n), [1, 2, 3, 4], 'one link per pop, counting up');
  assert.deepEqual(t.named('clayChainEnd').map(e => e.n), [4]);
});

test('crumble: healthy and once-cracked clay never joins, and stops the run dead', () => {
  const t = board(['eecee...........']);
  t.ctx.breakBrick(t.at(0, 0));
  t.settle();
  assert.equal(t.at(0, 1).alive, false, 'the neighbour was precarious');
  assert.equal(t.at(0, 2).alive, true, 'three hits left: the fuse ends here');
  assert.equal(t.at(0, 3).alive, true, 'and nothing past it goes');
  assert.deepEqual(t.named('clayChainEnd').map(e => e.n), [1]);
});

test('crumble: the chain turns corners and runs up and down, but never diagonally', () => {
  const t = board(['ee..............', 'e...............', '.e..............']);
  t.ctx.breakBrick(t.at(0, 0));
  t.settle();
  assert.equal(t.at(0, 1).alive, false);
  assert.equal(t.at(1, 0).alive, false);
  assert.equal(t.at(2, 1).alive, true, 'diagonally adjacent to (1,0) only, so it is safe');
});

test('crumble: a lone pop is no chain at all', () => {
  const t = board(['e.c.............']);
  t.ctx.breakBrick(t.at(0, 0));
  t.settle();
  assert.equal(t.named('clayChain').length, 0);
  assert.equal(t.named('clayChainEnd').length, 0);
});

test('crumble: the left and right edges do not wrap onto the row beside them', () => {
  const t = board(['e..............e', 'e..............e']);
  t.ctx.breakBrick(t.at(0, 15));
  t.settle();
  assert.equal(t.at(1, 15).alive, false, 'down is a real neighbour');
  assert.equal(t.at(0, 0).alive, true, 'column 16 is off the board, not column 0 of the next row');
  assert.equal(t.at(1, 0).alive, true);
});

test('crumble: a brick is queued once, however many lit neighbours it has', () => {
  const t = board(['eee.............', 'eee.............', 'eee.............']);
  t.ctx.breakBrick(t.at(1, 1));
  t.settle();
  assert.equal(t.alive(), 0);
  const n = t.named('clayChain').map(e => e.n);
  assert.deepEqual(n, [1, 2, 3, 4, 5, 6, 7, 8], 'eight links, no brick counted twice');
  assert.deepEqual(t.named('clayChainEnd').map(e => e.n), [8]);
});

test('crumble: the same board lit the same way runs the same way, every time', () => {
  const run = () => { const t = board(['eeee............', 'e.ee............']); t.ctx.breakBrick(t.at(0, 0)); t.settle();
    return t.named('clayChain').map(e => e.x + ':' + e.y + ':' + e.n).join('|'); };
  assert.equal(run(), run());
});

test('crumble: a chain that runs into an already dead brick skips it and keeps counting', () => {
  const t = board(['eeee............']);
  t.at(0, 2).alive = false;                       // something else took it first
  t.ctx.breakBrick(t.at(0, 0));
  t.settle();
  assert.equal(t.at(0, 1).alive, false);
  assert.equal(t.at(0, 3).alive, true, 'the gap breaks the vein');
  assert.deepEqual(t.named('clayChain').map(e => e.n), [1]);
});

test('crumble: prime lights every living clay brick and says how many it moved', () => {
  const t = board(['cdec2...........']);
  const n = prime(t.g, t.ctx);
  assert.equal(n, 3, 'the one-hit clay was already lit, the plain brick is not clay');
  assert.equal(t.at(0, 0).hp, 1);
  assert.equal(t.at(0, 1).hp, 1);
  assert.equal(t.at(0, 4).hp, 2, 'a plain two-hit brick is untouched');
  assert.deepEqual(t.named('clayPrimed').map(e => e.n), [3]);
  assert.equal(t.named('clayReady').length, 1, 'the wave starts now, the rest of the ticks ripple');
  t.settle();
  assert.equal(t.named('clayReady').length, 3);
});

test('crumble: prime on a board with no clay left is silent', () => {
  const t = board(['22..............']);
  assert.equal(prime(t.g, t.ctx), 0);
  assert.equal(t.named('clayPrimed').length, 0);
});

test('crumble: prime still works for a caller with no scheduler', () => {
  const t = board(['cc..............']);
  const bare = { ...t.ctx, schedule: undefined };
  assert.equal(prime(t.g, bare), 2);
  assert.equal(t.named('clayReady').length, 2);
});

test('crumble: a primed board goes in one shot', () => {
  const t = board(['cccc............']);
  prime(t.g, t.ctx);
  t.ctx.breakBrick(t.at(0, 0));
  t.settle();
  assert.equal(t.alive(), 0);
});

test('crumble: neighbours reads in a fixed order, up and down before left and right', () => {
  const t = board(['.e..............', 'eee.............', '.e..............']);
  const got = neighbours(t.ctx, t.at(1, 1)).map(b => b.row + ',' + b.col);
  assert.deepEqual(got, ['0,1', '2,1', '1,0', '1,2']);
  assert.deepEqual(N4, [[-1, 0], [1, 0], [0, -1], [0, 1]]);
});

test('crumble: the shock crack is off, and healthy clay beside a shatter keeps its three hits', () => {
  assert.equal(CRUMBLE_SHOCK, false);
  const t = board(['ec..............']);
  t.ctx.breakBrick(t.at(0, 0));
  t.settle();
  assert.equal(t.at(0, 1).hp, 3);
});

test('crumble: a precarious brick wobbles, and reduced motion holds it perfectly still', () => {
  const t = board(['ec..............']);
  const lit = t.at(0, 0), healthy = t.at(0, 1);
  let moved = 0;
  for (let i = 0; i < 40; i++) { TWIST.update(t.g, 1 / 60, t.ctx); if (Math.abs(lit.push.dx) > 0.01) moved++; }
  assert.ok(moved > 10, 'it should be moving most frames');
  assert.equal(healthy.push.dx, 0, 'healthy clay sits still');
  t.g.reduced = true;
  TWIST.update(t.g, 1 / 60, t.ctx);
  assert.ok(Math.abs(lit.push.dx) < 1e-9, 'reduced motion: dead still');
  assert.ok(Math.abs(lit.push.dy) < 1e-9);
});

test('crumble: the wobble never drifts, however long it runs', () => {
  const t = board(['e...............']);
  const br = t.at(0, 0);
  for (let i = 0; i < 600; i++) TWIST.update(t.g, 1 / 60, t.ctx);
  assert.ok(Math.abs(br.push.dx) < 2 && Math.abs(br.push.dy) < 2, 'it is a nudge, not a journey');
  br.alive = false;
  TWIST.update(t.g, 1 / 60, t.ctx);
  assert.ok(Math.abs(br.push.dx) < 1e-9, 'a dead brick is put back where it was');
});

test('crumble: the wobble rides on top of the hit push, it does not eat it', () => {
  const t = board(['e...............']);
  const br = t.at(0, 0);
  TWIST.update(t.g, 1 / 60, t.ctx);
  const wob = br.push.dx;
  br.push.dx = 6 + wob;                            // game.js recomputes push from push0 * pushT, then we run
  TWIST.update(t.g, 1 / 60, t.ctx);
  assert.ok(br.push.dx > 4, 'the six pixels of knockback survived');
});

test('crumble: build wipes the last board off, chain and all', () => {
  const t = board(['eee.............']);
  t.ctx.breakBrick(t.at(0, 0));
  assert.equal(t.g.crumble.queue.length, 1);
  TWIST.build(t.g, t.ctx);
  assert.equal(t.g.crumble.queue.length, 0);
  assert.equal(t.g.crumble.running, false);
  assert.equal(t.at(0, 1).crumbleQueued, false);
});
