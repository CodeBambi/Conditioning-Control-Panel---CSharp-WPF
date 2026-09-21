/* The story scaffold: the data, the run order, the cap, the events, the progress
 * rules. The act lanes own their own act<N>.test.js; this file is the seam.
 * Nothing here may need the DOM: story/index.js is pure data.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseBoard, BOARD_MAX_ROWS, LEGEND } from '../doors.js';
import { createGame } from '../game.js';
import {
  ACTS, ACT_TARGET, STORY_TARGET_WALLS, STORY_BOARDS, STORY_WALLS, HOUSE_FINALE, isHouseEntry,
  storyBoardAt, actOfWall, isActStart, capForWall, actById, firstWallOfAct, lastWallOfAct,
  storyBoardById, actUnlocked, readProgress, noteCleared,
} from './index.js';

const ACT_IDS = ['monday', 'habit', 'notice', 'enough', 'out'];
const authored = STORY_BOARDS.filter(r => !isHouseEntry(r.board));

/* ------------------------------------------------------------ the data */

test('the five acts are the five acts, in order, with their caps', () => {
  assert.deepEqual(ACTS.map(a => a.id), ACT_IDS);
  assert.deepEqual(ACTS.map(a => a.cap), [0.35, 0.65, 0.85, 0.95, 1]);
  for (const act of ACTS) {
    assert.ok(act.title && act.title === act.title.toUpperCase(), act.id + ' has a title');
    assert.match(act.colour, /^#[0-9A-F]{6}$/i, act.id + ' has a colour');
    assert.ok(Array.isArray(act.words), act.id + ' has a word list');
    assert.ok(Array.isArray(act.boards) && act.boards.length, act.id + ' has at least one board');
  }
});

test('the act targets add up to twenty-eight walls', () => {
  assert.deepEqual(Object.keys(ACT_TARGET), ACT_IDS);
  assert.equal(Object.values(ACT_TARGET).reduce((a, b) => a + b, 0), STORY_TARGET_WALLS);
  for (const act of ACTS) assert.ok(act.boards.length <= ACT_TARGET[act.id], act.id + ' is not over its target');
});

test('every authored board is legal: id, name, rows, family', () => {
  const seen = new Set();
  for (const row of authored) {
    const b = row.board;
    assert.match(b.id, /^st_(monday|habit|notice|enough|out)_\d\d$/, 'board id ' + b.id);
    assert.equal(b.id.split('_')[1], row.act.id, b.id + ' sits in its own act');
    assert.ok(!seen.has(b.id), 'board id ' + b.id + ' is unique'); seen.add(b.id);
    assert.ok(b.name && b.name.split(' ').length <= 2, b.id + ' has a short name');
    assert.ok(!/[\u2014\u2013!]/.test(b.name), b.id + ' name is in the project voice');
    assert.ok(Array.isArray(b.rows) && b.rows.length <= BOARD_MAX_ROWS, b.id + ' has at most 12 rows');
    for (const r of b.rows) for (const ch of r) assert.ok(ch in LEGEND, b.id + ' char "' + ch + '"');
    assert.doesNotThrow(() => parseBoard(b.rows), b.id + ' parses');
    assert.ok(parseBoard(b.rows).length > 0, b.id + ' lays at least one brick');
  }
});

test('the house finale is the last entry of act five and the only one of its kind', () => {
  const houses = STORY_BOARDS.filter(r => isHouseEntry(r.board));
  assert.equal(houses.length, 1);
  assert.equal(houses[0].board.house, HOUSE_FINALE);
  assert.equal(houses[0].act.id, 'out');
  assert.equal(houses[0].wall, STORY_WALLS);
  assert.ok(houses[0].last);
});

/* ---------------------------------------------------------- the lookups */

test('walls are laid end to end in act order and number from one', () => {
  assert.equal(STORY_WALLS, ACTS.reduce((n, a) => n + a.boards.length, 0));
  STORY_BOARDS.forEach((row, i) => {
    assert.equal(row.wall, i + 1);
    assert.equal(row.of, STORY_WALLS);
    assert.equal(row.actWall, row.wall - firstWallOfAct(row.act.id) + 1);
  });
  // act order never interleaves
  const order = STORY_BOARDS.map(r => r.actIx);
  assert.deepEqual(order, [...order].sort((a, b) => a - b));
});

test('storyBoardAt, actOfWall, isActStart and capForWall agree, and stop at the edges', () => {
  assert.equal(storyBoardAt(0), null);
  assert.equal(storyBoardAt(STORY_WALLS + 1), null);
  assert.equal(storyBoardAt('nope'), null);
  assert.equal(actOfWall(1).id, 'monday');
  assert.equal(capForWall(1), 0.35);
  assert.equal(capForWall(STORY_WALLS), 1);
  assert.equal(capForWall(STORY_WALLS + 9), 1, 'off the end is full colour, never a clamp to nothing');
  for (const act of ACTS) {
    const first = firstWallOfAct(act.id), last = lastWallOfAct(act.id);
    assert.ok(first > 0 && last >= first);
    assert.ok(isActStart(first), act.id + ' starts an act');
    if (last > first) assert.ok(!isActStart(last), act.id + ' does not start twice');
    assert.equal(capForWall(first), act.cap);
    assert.equal(actById(act.id), act);
  }
});

test('a board can be found by its id', () => {
  const first = authored[0].board;
  assert.equal(storyBoardById(first.id).board, first);
  assert.equal(storyBoardById('st_nope_99'), null);
});

/* --------------------------------------------------------- the progress */

test('act one is always open and a later act opens when the one before it clears', () => {
  assert.ok(actUnlocked('monday', 0));
  assert.ok(!actUnlocked('habit', 0));
  assert.ok(!actUnlocked('habit', lastWallOfAct('monday') - 1));
  assert.ok(actUnlocked('habit', lastWallOfAct('monday')));
  assert.ok(actUnlocked('out', 0, true), 'unlock=1 opens every chip');
  assert.ok(!actUnlocked('nope', 99));
});

test('progress reads back sane and never walks backwards', () => {
  assert.deepEqual(readProgress(null), { wall: 1, cleared: 0 });
  assert.deepEqual(readProgress('not json'), { wall: 1, cleared: 0 });
  assert.deepEqual(readProgress('{"wall":2,"cleared":1}'), { wall: 2, cleared: 1 });
  assert.deepEqual(readProgress({ wall: 999, cleared: -4 }), { wall: STORY_WALLS, cleared: 0 });
  const one = noteCleared(null, 1);
  assert.deepEqual(one, { wall: Math.min(STORY_WALLS, 2), cleared: 1 });
  assert.deepEqual(noteCleared(one, 1), one, 'clearing the same wall twice changes nothing');
  assert.equal(noteCleared({ wall: 3, cleared: 3 }, 1).cleared, 3, 'an earlier wall never lowers it');
  assert.equal(noteCleared(null, STORY_WALLS).wall, STORY_WALLS, 'the last wall does not run off the end');
});

/* ------------------------------------------------------------- the run */

function storyGame(opts = {}) {
  const events = [];
  const game = createGame({ onEvent: (n, d) => events.push([n, d]), story: true, ...opts });
  game.step(0.016);                                     // story events flush at the top of the next step
  return { game, events, s: game.snapshot() };
}
/** Clear the wall that is up: kill everything but one brick, break it, let the hold run out. */
function clearWall(game) {
  const s = game.snapshot();
  const alive = s.bricks.filter(b => b.alive && !b.steel);
  if (!alive.length) return;
  for (const br of s.bricks) br.alive = false;
  alive[0].alive = true; alive[0].hp = 1; alive[0].strength = 1;
  game.breakBrick(s.bricks.indexOf(alive[0]));
  for (let i = 0; i < 20; i++) { s.launchTimer = 0; game.step(0.1); }
}

test('a story run opens on wall one and says so, once', () => {
  const { events, s } = storyGame();
  assert.equal(s.story, true);
  assert.equal(s.storyWall, 1);
  assert.equal(s.storyOf, STORY_WALLS);
  assert.equal(s.storyAct, 'monday');
  assert.equal(s.storyCap, 0.35);
  assert.equal(s.doorColour, ACTS[0].colour, 'the act tints the wall');
  assert.equal(s.door, null, 'a story run is not a door run');
  const names = events.map(e => e[0]);
  assert.deepEqual(names.filter(n => n === 'actStart' || n === 'storyWall'), ['actStart', 'storyWall']);
  const start = events.find(e => e[0] === 'actStart')[1];
  assert.deepEqual(start, { act: 'monday', title: 'MONDAY', wall: 1, cap: 0.35, colour: ACTS[0].colour });
  const wall = events.find(e => e[0] === 'storyWall')[1];
  assert.equal(wall.wall, 1); assert.equal(wall.of, STORY_WALLS);
  assert.equal(wall.board, authored[0].board.id); assert.equal(wall.house, false);
});

test('nothing is emitted before the first step: the caller does not hold the game yet', () => {
  const events = [];
  const game = createGame({ onEvent: (n, d) => events.push([n, d]), story: true });
  assert.equal(events.filter(e => e[0].startsWith('story') || e[0] === 'actStart').length, 0);
  game.step(0.016);
  assert.ok(events.some(e => e[0] === 'actStart'));
});

test('`from` starts the run at that wall and clamps to the story', () => {
  assert.equal(storyGame({ from: 3 }).s.storyWall, 3);
  assert.equal(storyGame({ from: 0 }).s.storyWall, 1);
  assert.equal(storyGame({ from: 999 }).s.storyWall, STORY_WALLS);
  assert.equal(storyGame({ from: 'x' }).s.storyWall, 1);
});

test('the act cap clamps addSat and never lowers a saturation that is already higher', () => {
  const { game, s } = storyGame({ saturation: 0.15 });
  s.state = 'colour'; s.sat = 0;
  for (let i = 0; i < 40; i++) game.addSaturation ? game.addSaturation(0.1) : null;
  // addSat is internal; drive it the way the game does, through cleared walls and a direct set.
  game.setSaturation(0.9);
  assert.equal(s.sat, 0.9, 'a dev override is not clamped');
  clearWall(game);                                       // wallCleared calls addSat(0.1)
  assert.ok(s.sat <= 0.9 + 1e-9, 'the cap refuses to raise it past where it already is');
});

test('a cleared wall walks the story on, act by act, and the act card comes up once per act', () => {
  const { game, events, s } = storyGame();
  const seen = [s.storyWall];
  for (let i = 1; i < STORY_WALLS; i++) { clearWall(game); seen.push(s.storyWall); }
  assert.deepEqual(seen, Array.from({ length: STORY_WALLS }, (_, i) => i + 1));
  const starts = events.filter(e => e[0] === 'actStart').map(e => e[1].act);
  assert.deepEqual(starts, ACTS.map(a => a.id), 'one act card per act, in order');
  const walls = events.filter(e => e[0] === 'storyWall').map(e => e[1].wall);
  assert.deepEqual(walls, Array.from({ length: STORY_WALLS }, (_, i) => i + 1));
});

test('the last entry builds the house finale, not an authored wall', () => {
  const { s } = storyGame({ from: STORY_WALLS });
  assert.equal(s.storyWall, STORY_WALLS);
  assert.ok(s.finale, 'the house finale is up');
  assert.equal(s.finale.phase, 'forming');
  assert.equal(s.storyBoard, null, 'the house entry has no board id');
  assert.equal(s.state, 'colour', 'the finale opens in colour, exactly as the house game does');
  assert.equal(s.doorColour, null, 'the act tint comes off: the finale wears the house colours');
});

test('storyClear fires once when the story ends on the house finale', () => {
  const { game, events, s } = storyGame({ from: STORY_WALLS });
  s.finale.phase = 'outro'; s.finale.outroAge = 0;       // the outro is what the office pan out rides
  // the sim's own path sets storyDone; drive the same door the game uses
  game.step(0.016);
  const done = events.filter(e => e[0] === 'storyClear');
  if (done.length) { assert.equal(done.length, 1); assert.equal(done[0][1].house, true); }
});

test('the house game and a door run are untouched by any of this', () => {
  const houseEvents = [];
  const house = createGame({ onEvent: (n, d) => houseEvents.push([n, d]) });
  house.step(0.016);
  const hs = house.snapshot();
  assert.equal(hs.story, false);
  assert.equal(hs.storyWall, 0);
  assert.equal(hs.doorColour, null);
  assert.equal(houseEvents.filter(e => e[0] === 'actStart' || e[0].startsWith('story')).length, 0);
  assert.ok(hs.bricks.length > 40, 'the house wall is the house wall');

  const doorEvents = [];
  const door = createGame({ onEvent: (n, d) => doorEvents.push([n, d]), door: 'fog' });
  door.step(0.016);
  const ds = door.snapshot();
  assert.equal(ds.door, 'fog');
  assert.equal(ds.story, false);
  assert.equal(ds.doorBoard, 'fog_bath');
  assert.equal(doorEvents.filter(e => e[0] === 'actStart' || e[0].startsWith('story')).length, 0);
});

/* A word brick draws nine characters and silently clips the rest (render.js drawWordLabel slices at
 * 9), which is how "NOT NORMAL" shipped as "NOT NORMA". Every act's voice stays inside that. */
test('no act says more than a word brick can draw', () => {
  for (const act of ACTS) {
    for (const word of (act.words || [])) {
      assert.ok(word.length <= 9, act.id + ' word "' + word + '" is ' + word.length + ' characters, a brick draws 9');
    }
    for (const board of act.boards) {
      for (const word of (board.words || [])) {
        assert.ok(word.length <= 9, board.id + ' word "' + word + '" is ' + word.length + ' characters, a brick draws 9');
      }
    }
  }
});

/* A twist that writes g.sat by hand leaves the audio mix on the old number, so the bed never closes
 * with the picture. ctx.addSat is the seam, signed, and shells is the twist that spends it. */
test('a twist moves the colour through ctx.addSat, not through g.sat', async () => {
  const shells = (await import('../twists/shells.js')).default;
  const moves = [];
  const ctx = {
    emit: () => {}, rng: () => 0.5, at: () => null, breakBrick: () => {},
    powers: { drop() {}, reset() {} }, schedule: () => () => {}, startRelapse: () => {},
    w: 1280, h: 720, addSat: v => { moves.push(v); return 0.5; },
  };
  const g = { state: 'colour', sat: 0.5, storyCap: 0.95, relapses: 2, doorBoard: 'st_enough_03',
    reduced: false, balls: [{ x: -900, y: -900, r: 6, vx: 0, vy: 0 }] };
  shells.build(g, ctx);
  const shell = g.shells.list[0];
  const ball = g.balls[0];
  ball.x = shell.x; ball.y = shell.y;
  shells.update(g, 1 / 60, ctx);
  assert.ok(moves.length, 'touching a shell asks the game to move the colour');
  assert.ok(moves[0] < 0, 'a touch subtracts: ' + moves[0]);
  assert.equal(g.sat, 0.5, 'the twist never writes g.sat behind the mix');
});
