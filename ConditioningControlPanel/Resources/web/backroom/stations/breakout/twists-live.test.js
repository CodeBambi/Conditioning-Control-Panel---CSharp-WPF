/* ============================================================================
 * stations/breakout/twists-live.test.js - the five twists through the REAL game.
 *
 * Every lane tested its twist against a fake `ctx`. A fake agrees with whoever
 * wrote it, so these tests drive the same modules through `createGame` instead:
 * the real wall, the real break path, the real timers, the real hp ladder.
 * Two of them exist because the fakes DID disagree with the game and nobody saw
 * it until the merge (the twist list per board, and ctx.breakBrick chipping an
 * armoured brick rather than killing it).
 * ==========================================================================*/
import test from 'node:test';
import assert from 'node:assert/strict';
import { createGame } from './game.js';
import { ALL_BOARDS } from './doors.js';
import { TWISTS } from './twists/index.js';

const seeded = (seed = 7) => () => (seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296;
/** A real game on one authored board, with its events collected. */
function live(board, seed = 11) {
  const events = [];
  const game = createGame({ board, rng: seeded(seed), onEvent: (name, d) => events.push({ name, d: d || {} }) });
  const s = game.snapshot();
  return {
    game, events, s,
    at: (row, col) => s.bricks.find(b => b.row === row && b.col === col) || null,
    // The dev hook is ONE contact, so an armoured brick needs its whole ladder spending.
    kill(br) { const i = s.bricks.indexOf(br); if (i < 0) return; for (let n = 0; n < 4 && br.alive; n++) game.breakBrick(i); },
    /** Run sim time so the twists' scheduled work lands. */
    settle(seconds = 4) { for (let i = 0; i < Math.round(seconds / 0.05); i++) game.step(0.05, { x: s.paddle.x }); },
    named: name => events.filter(e => e.name === name),
  };
}

/* ------------------------------------------- the board runs a LIST of twists */

test('a clay board runs crumble as well as its own twist, so the cracked key really lights a chain', () => {
  const h = live('lock_keys2');
  assert.deepEqual(h.s.twists, ['keys', 'crumble'], 'keys is the board twist, crumble comes along for the clay');
  const clay = h.s.bricks.filter(b => b.clay);
  assert.ok(clay.length >= 10, 'the wax seal is a real row');
  assert.ok(clay.every(b => b.hp === 3), 'and it starts healthy');

  h.kill(h.at(6, 8));                                            // P, the cracked key
  assert.equal(h.named('clayPrimed')[0].d.n, clay.length, 'the key primed every clay brick');
  assert.ok(clay.every(b => b.hp === 1), 'they are all precarious now');

  const end = clay.reduce((a, b) => (b.col > a.col ? b : a), clay[0]);
  h.kill(end);                                                   // light it from one end
  h.settle(6);
  assert.ok(clay.every(b => !b.alive), 'the whole seal came down');
  assert.ok(h.named('clayChain').length >= clay.length - 1, 'and each link said so');
});

test('the house game and a no-clay board run no crumble', () => {
  const house = createGame({ rng: seeded(3) }).snapshot();
  assert.deepEqual(house.twists, [], 'the house game runs no twist at all');
  assert.deepEqual(live('hive_node2').s.twists, ['node'], 'no clay, no crumble');
  assert.deepEqual(live('fog_brittle').s.twists, ['crumble'], 'and crumble is never listed twice');
});

/* --------------------------------------------- ctx.breakBrick is a true kill */

test('a mirror twin comes down whatever its armour', () => {
  const h = live('ward_mirror2');
  const pairs = h.s.bricks.filter(b => !b.steel && b.alive)
    .map(br => ({ br, twin: h.at(br.row, 15 - br.col) }))
    .filter(p => p.twin && p.twin.alive && p.twin.hp > 1);
  assert.ok(pairs.length, 'the board really does pair a brick with an armoured twin');
  const { br, twin } = pairs[0];
  h.kill(br);
  h.settle(2);
  assert.equal(twin.alive, false, `a twin at ${twin.hp} hits was killed, not chipped`);
});

/* ----------------------------------------------- every twist, once, for real */

test('every authored board builds, plays and clears through the real game', () => {
  for (const { board } of ALL_BOARDS) {
    const h = live(board.id, 5);
    assert.ok(h.s.bricks.length > 8, board.id + ' built a wall');
    // Break everything breakable; the twists get their real onBreak, timers and update along the way.
    // A `?board=` run LOOPS, so the proof of a clear is the wall counter, not an empty field.
    const walls = h.s.stats.walls;
    for (let guard = 0; guard < 900 && h.s.stats.walls === walls; guard++) {
      const target = h.s.bricks.find(b => b.alive && !b.steel);
      if (!target) break;
      h.kill(target);
      h.settle(0.2);
    }
    h.settle(3);
    assert.ok(h.s.stats.walls > walls || !h.s.bricks.some(b => b.alive && !b.steel), board.id + ' cleared');
  }
});

test('a door run walks both boards and says doorClear once, twist and all', () => {
  for (const door of ['fog', 'wardrobe', 'lock', 'hive', 'ward']) {
    const events = [];
    const game = createGame({ door, rng: seeded(9), onEvent: (name, d) => events.push({ name, d: d || {} }) });
    const s = game.snapshot();
    for (let wall = 0; wall < 2; wall++) {
      const start = s.stats.walls;
      for (let guard = 0; guard < 1800 && s.stats.walls === start; guard++) {
        const i = s.bricks.findIndex(b => b.alive && !b.steel);
        if (i < 0) break;
        game.breakBrick(i);
      }
      for (let i = 0; i < 40; i++) game.step(0.05, { x: s.paddle.x });
    }
    assert.equal(events.filter(e => e.name === 'doorClear').length, 1, door + ' said doorClear once');
  }
});

/* ------------------------------------------------------------ the edge guard */

test('ctx.at never wraps off the end of a row into the row next door', () => {
  // The crumble chain, the mirror twin, the key sweep and the node net all walk neighbours, so an
  // off-the-edge lookup that answered with the row above would chain sideways off the board.
  let ctx = null;
  const real = TWISTS.crumble;
  TWISTS.crumble = { id: 'crumble', build(g, c) { ctx = c; } };
  try { createGame({ board: 'fog_brittle', rng: seeded(4), onEvent() {} }); } finally { TWISTS.crumble = real; }
  assert.ok(ctx, 'the twist seam handed the module a ctx');
  const inside = ctx.at(1, 1);
  assert.ok(inside && inside.row === 1 && inside.col === 1, 'a real cell still answers');
  assert.equal(ctx.at(1, -1), null, 'column -1 is nothing, not the end of row 0');
  assert.equal(ctx.at(1, 16), null, 'column 16 is nothing, not the start of row 2');
  assert.equal(ctx.at(-1, 3), null, 'above the wall is nothing');
});

test('the door accent is on the snapshot for every board, so render can tint by door', () => {
  for (const { door, board } of ALL_BOARDS) {
    const s = live(board.id).s;
    assert.equal(s.doorColour, door.colour, board.id + ' carries its door colour');
    assert.match(s.doorColour, /^#[0-9A-F]{6}$/);
  }
  assert.equal(createGame({ rng: seeded(1) }).snapshot().doorColour, null, 'the house game has no door colour');
});
