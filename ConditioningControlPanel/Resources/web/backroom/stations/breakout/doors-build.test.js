import test from 'node:test';
import assert from 'node:assert/strict';
import { createGame, BRICK, W } from './game.js';
import { boardById, doorById, parseBoard } from './doors.js';
import { TWISTS } from './twists/index.js';

const seeded = (seed = 7) => () => (seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296;
const make = (opts = {}) => {
  const events = [];
  const game = createGame({ rng: seeded(opts.seed || 11), onEvent: (name, d) => events.push([name, d]), ...opts });
  return { game, events, s: game.snapshot() };
};
/** Hit every breakable brick until this board clears. Returns true if it did. */
function clearBoard(game) {
  const s = game.snapshot(), walls = s.stats.walls;
  for (let guard = 0; guard < 900 && s.stats.walls === walls; guard++) {
    const i = s.bricks.findIndex(b => b.alive && !b.steel);
    if (i < 0) break;
    game.breakBrick(i);
  }
  return s.stats.walls > walls;
}

test('no door option: the house game is untouched', () => {
  const { s } = make();
  assert.equal(s.door, null);
  assert.equal(s.doorBoard, null);
  assert.equal(s.twist, null);
  assert.equal(s.bricks.length, BRICK.cols * BRICK.rows);
});

test('an authored board lays its cells on the ordinary grid, same x0 and pitch as the house wall', () => {
  const { s } = make({ board: 'fog_brittle' });
  const cells = parseBoard(boardById('fog_brittle').board.rows);
  assert.equal(s.bricks.length, cells.length);
  assert.equal(s.doorBoard, 'fog_brittle');
  assert.equal(s.door, 'fog');
  assert.equal(s.twist, 'crumble');
  assert.equal(s.doorColour, '#F062A8');
  const x0 = (W - (BRICK.cols * BRICK.w + (BRICK.cols - 1) * BRICK.gap)) / 2;
  for (const br of s.bricks) {
    assert.ok(Math.abs(br.x - (x0 + br.col * (BRICK.w + BRICK.gap))) < 1e-9, 'column ' + br.col);
    assert.ok(Math.abs(br.y - (BRICK.top + br.row * (BRICK.h + BRICK.gap))) < 1e-9, 'row ' + br.row);
    assert.equal(br.w, BRICK.w);
    assert.equal(br.h, BRICK.h);
  }
});

test('the chars become the right bricks: strength, clay, steel, gates, keys, wires, core, powers, payloads', () => {
  const at = (s, row, col) => s.bricks.find(b => b.row === row && b.col === col);
  const keys = make({ board: 'lock_keys2' }).s;
  assert.equal(at(keys, 0, 0).steel, true);
  assert.equal(at(keys, 0, 0).gate, 'M');
  assert.equal(at(keys, 0, 15).gate, 'W');
  assert.equal(at(keys, 1, 1).powerup, 'multiball');
  assert.equal(at(keys, 1, 14).powerup, 'fireball');
  assert.ok(at(keys, 1, 2).gif >= 0, 'G is a picture brick');
  assert.equal(at(keys, 4, 1).clay, true);
  assert.equal(at(keys, 4, 1).hp, 3);
  assert.equal(at(keys, 6, 2).key, 'W');
  assert.equal(at(keys, 6, 8).key, 'clay');
  assert.equal(at(keys, 6, 14).key, 'M');
  const node = make({ board: 'hive_node2' }).s;
  assert.equal(at(node, 1, 1).wire, true);
  assert.equal(at(node, 1, 1).hp, 3);
  assert.equal(at(node, 3, 7).core, true);
  assert.equal(at(node, 0, 0).strength, 2);
  const brittle = make({ board: 'fog_brittle' }).s;
  assert.ok(brittle.bricks.some(b => b.spiral), 'S is a spiral brick');
  assert.equal(at(brittle, 1, 1).hp, 1, 'e is clay with one hit left');
  assert.equal(at(brittle, 1, 1).strength, 3, 'and three cracks worth of face');
});

test('steel never breaks by a ball and never holds the board back', () => {
  const { game, s, events } = make({ board: 'ward_mirror2' });
  const steel = s.bricks.find(b => b.steel);
  const index = s.bricks.indexOf(steel);
  for (let i = 0; i < 5; i++) game.breakBrick(index);
  assert.equal(steel.alive, true, 'the ball never gets through steel');
  assert.ok(events.some(([n, d]) => n === 'metalHit' && d.steel), 'it answers with a metalHit');
  assert.equal(clearBoard(game), true, 'the board clears with steel still standing');
  assert.equal(steel.alive, true, 'and that steel brick was never broken');
});

test('a door plays its two boards in order, then says doorClear once', () => {
  const { game, events } = make({ door: 'fog' });
  const boards = doorById('fog').boards.map(b => b.id);
  assert.equal(game.snapshot().doorBoard, boards[0]);
  clearBoard(game);
  assert.equal(game.snapshot().doorBoard, boards[1]);
  assert.equal(events.filter(([n]) => n === 'doorClear').length, 0, 'not after the first board');
  clearBoard(game);
  const cleared = events.filter(([n]) => n === 'doorClear');
  assert.equal(cleared.length, 1);
  assert.deepEqual(cleared[0][1], { door: 'fog' });
  clearBoard(game);
  assert.equal(events.filter(([n]) => n === 'doorClear').length, 1, 'once per run');
});

test('?board= opens one board and loops it, and never says doorClear', () => {
  const { game, events } = make({ board: 'fog_bath' });
  clearBoard(game);
  assert.equal(game.snapshot().doorBoard, 'fog_bath');
  assert.equal(events.filter(([n]) => n === 'doorClear').length, 0);
});

test('the door word list feeds the word bricks', () => {
  const { s } = make({ door: 'hive', seed: 3 });
  assert.deepEqual(s.words, doorById('hive').words);
  const worded = s.bricks.filter(b => b.word);
  for (const br of worded) assert.ok(s.words.includes(br.word), br.word + ' comes from the door');
});

/* ------------------------------------------------------------ the twist seam */

function withTwist(id, hooks, opts = {}) {
  const real = TWISTS[id];
  TWISTS[id] = { id, ...hooks };
  try { return opts.run(); } finally { TWISTS[id] = real; }
}

test('every hook fires, with ctx exactly as CONTRACT.md froze it', () => {
  const seen = [];
  let ctxSeen = null;
  withTwist('crumble', {
    build(g, ctx) { seen.push('build'); ctxSeen = ctx; },
    onHit(g, br, ball, ctx) { seen.push('onHit'); },
    onBreak(g, br, ball, ctx) { seen.push('onBreak'); },
    update(g, dt, ctx) { seen.push('update'); },
    wallCleared(g) { seen.push('wallCleared'); },
  }, { run() {
    const { game, s } = make({ board: 'fog_brittle' });
    assert.deepEqual(seen, ['build']);
    for (const key of ['emit', 'rng', 'at', 'breakBrick', 'schedule', 'powers', 'startRelapse', 'w', 'h'])
      assert.ok(key in ctxSeen, 'ctx.' + key);
    assert.equal(ctxSeen.w, s.w);
    assert.equal(typeof ctxSeen.powers.drop, 'function');
    const clay = s.bricks.find(b => b.clay && b.hp > 1);
    game.breakBrick(s.bricks.indexOf(clay));
    assert.equal(clay.hp, 1, 'one crack, still standing');
    assert.ok(seen.includes('onHit'), 'a hit that did not break it');
    game.step(0.05, {});
    assert.ok(seen.includes('update'));
    clearBoard(game);
    assert.ok(seen.includes('onBreak'));
    assert.ok(seen.includes('wallCleared'));
  } });
});

test('ctx.at finds the authored cell, alive or dead; ctx.breakBrick opens steel', () => {
  let ctx = null;
  withTwist('mirror', { build(g, c) { ctx = c; } }, { run() {
    const { game, s } = make({ board: 'ward_mirror2' });
    const steel = ctx.at(1, 1);
    assert.equal(steel.steel, true);
    assert.equal(ctx.at(0, 0), null, 'an empty cell is null');
    ctx.breakBrick(steel, null);
    assert.equal(steel.alive, false, 'a twist may open steel');
    assert.equal(ctx.at(1, 1), steel, 'and the cell still answers');
  } });
});

test('ctx.schedule runs on sim time, in due order, and can be cancelled', () => {
  const fired = [];
  let ctx = null;
  withTwist('crumble', { build(g, c) { ctx = c; } }, { run() {
    const { game } = make({ board: 'fog_brittle' });
    ctx.schedule(0.2, () => fired.push('late'));
    ctx.schedule(0.05, () => fired.push('early'));
    const cancel = ctx.schedule(0.1, () => fired.push('never'));
    cancel();
    game.step(0.04, {});
    assert.deepEqual(fired, []);
    game.step(0.04, {});
    assert.deepEqual(fired, ['early']);
    game.step(0.1, {});  // step() clamps dt to 0.1, so the clock walks
    game.step(0.1, {});
    assert.deepEqual(fired, ['early', 'late']);
  } });
});

test('a twist that throws never stops the game', () => {
  withTwist('crumble', { build() { throw new Error('lane bug'); }, update() { throw new Error('lane bug'); } }, { run() {
    const { game, s } = make({ board: 'fog_brittle' });
    assert.ok(s.bricks.length > 0, 'the board still built');
    game.step(0.05, {});
    assert.ok(true, 'and the step returned');
  } });
});

test('the scaffold ships a working crumble.prime for the keys lane', async () => {
  const { prime } = await import('./twists/crumble.js');
  const { s } = make({ board: 'lock_keys2' });
  const emitted = [];
  const n = prime(s, { emit: (name, d) => emitted.push([name, d]) });
  assert.ok(n > 0);
  assert.equal(s.bricks.filter(b => b.alive && b.clay && b.hp > 1).length, 0, 'every clay brick is precarious');
  assert.equal(emitted.filter(([name]) => name === 'clayReady').length, n);
  assert.deepEqual(emitted.at(-1), ['clayPrimed', { n }]);
});

test('the stub twists leave every board playable: every board can be cleared', () => {
  for (const id of ['fog_bath', 'fog_brittle', 'ward_net', 'ward_mirror2', 'lock_combo', 'lock_keys2',
    'hive_serial', 'hive_node2', 'ward_dose', 'ward_justone']) {
    const { game, s } = make({ board: id, seed: 5 });
    assert.ok(s.bricks.length > 4, id + ' built bricks');
    assert.equal(clearBoard(game), true, id + ' can be cleared');
  }
});
