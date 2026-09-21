/* ============================================================================
 * story/act2.test.js - THE HABIT, walls 6 to 12. Owned by the act 2 lane.
 *
 * Seven boards: legal, clearable, and the two taught twists really run. Pure:
 * the act file is data and createGame needs no DOM.
 * ==========================================================================*/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseBoard, brickSpec, BOARD_COLS, BOARD_MAX_ROWS, LEGEND } from '../doors.js';
import { createGame } from '../game.js';
import { storyBoardById } from './index.js';
import ACT2 from './act2.js';

const BOARDS = ACT2.boards;
const FAMILIES = ['breakthrough', 'sides', 'chambers', 'cascade', 'precision', 'breather'];
const TWISTS = ['crumble', 'mirror', 'keys', 'node', 'justone', 'stare', 'shells'];
const CLAY = new Set(['c', 'd', 'e']);
const GATE_KEY = { M: 'K', W: 'Q' };

/* --------------------------------------------------------------- the shape */

test('the act is THE HABIT and ships exactly seven boards', () => {
  assert.equal(ACT2.id, 'habit');
  assert.equal(ACT2.title, 'THE HABIT');
  assert.equal(ACT2.cap, 0.65);
  assert.equal(ACT2.colour, '#F062A8');
  assert.equal(BOARDS.length, 7, 'wall numbers are computed from the count');
});

test('the words are the game own soft triggers, and only those', () => {
  assert.deepEqual(ACT2.words, ['SINK', 'DROP', 'RELAX', 'LET GO', 'DEEPER', 'BLANK']);
  // every one has a word-fx module behind it (words/sink.js, words/let-go.js, ...)
  for (const w of ACT2.words) assert.match(w, /^[A-Z ]+$/, w + ' is a plain upper-case trigger');
});

test('every board is legal: id, name, family, twist, rows', () => {
  const seen = new Set();
  BOARDS.forEach((b, i) => {
    assert.equal(b.id, 'st_habit_0' + (i + 1), 'boards are numbered in play order');
    assert.ok(!seen.has(b.id), b.id + ' is unique'); seen.add(b.id);
    assert.ok(b.name && b.name.split(' ').length <= 2, b.id + ' has a two-word name');
    assert.ok(FAMILIES.includes(b.family), b.id + ' family ' + b.family);
    assert.ok(b.twist === null || TWISTS.includes(b.twist), b.id + ' twist ' + b.twist);
    assert.ok(typeof b.line === 'string' && b.line.length > 0, b.id + ' has a line');
    assert.ok(Array.isArray(b.rows) && b.rows.length <= BOARD_MAX_ROWS, b.id + ' is at most 12 rows');
    for (const row of b.rows) {
      assert.equal(row.length, BOARD_COLS, b.id + ' row is 16 wide');
      for (const ch of row) assert.ok(ch in LEGEND, b.id + ' char "' + ch + '"');
    }
    assert.doesNotThrow(() => parseBoard(b.rows), b.id + ' parses');
    assert.ok(parseBoard(b.rows).length > 10, b.id + ' lays a wall, not a handful');
  });
});

test('the copy is in the project voice: no em-dash, no en-dash, no exclamation', () => {
  const text = [ACT2.title, ...BOARDS.map(b => b.name + ' ' + b.line)].join(' ');
  assert.ok(!/[—–!]/.test(text), 'act 2 copy is dry');
});

/* ------------------------------------------------------------- the pacing */

test('the families rotate and the act gets its breather', () => {
  const fams = BOARDS.map(b => b.family);
  for (let i = 1; i < fams.length; i++) assert.notEqual(fams[i], fams[i - 1], 'wall ' + (i + 1) + ' feels like the one before it');
  assert.ok(fams.includes('breather'), 'at least one breather');
});

test('four boards carry a twist, and each twist is taught small before it is tested big', () => {
  const twisted = BOARDS.filter(b => b.twist);
  assert.equal(twisted.length, 4, 'about one in two');
  const cells = b => parseBoard(b.rows).length;
  const of = id => BOARDS.find(b => b.id === id);
  assert.equal(of('st_habit_02').twist, 'crumble');
  assert.equal(of('st_habit_06').twist, 'crumble');
  assert.ok(cells(of('st_habit_02')) < cells(of('st_habit_06')), 'crumble is taught small, then tested');
  assert.equal(of('st_habit_04').twist, 'keys');
  assert.equal(of('st_habit_07').twist, 'keys');
  assert.ok(cells(of('st_habit_04')) < cells(of('st_habit_07')), 'keys is taught small, then tested');
});

test('the act opens below where it will end, and power-up bricks arrive', () => {
  const size = b => parseBoard(b.rows).length;
  assert.ok(size(BOARDS[0]) < size(BOARDS[BOARDS.length - 1]), 'the first wall is the dip');
  const powers = new Set();
  for (const b of BOARDS) for (const row of b.rows) for (const ch of row) if ('UVF'.includes(ch)) powers.add(ch);
  assert.deepEqual([...powers].sort(), ['F', 'U', 'V'], 'all three power-up bricks show up in the act');
  const ornament = BOARDS.filter(b => b.rows.join('').includes('G') && b.rows.join('').includes('S'));
  assert.ok(ornament.length >= 4, 'pictures and spirals are common in the habit');
});

test('no plain steel anywhere in the act: the only steel is a gate with a key', () => {
  for (const b of BOARDS) {
    assert.ok(!b.rows.join('').includes('X'), b.id + ' has no unbreakable steel');
  }
});

/* --------------------------------------------------------- the clay rules */

/** Every 4-connected run of clay on the board, biggest first. */
function clayRuns(rows) {
  const R = rows.length, seen = new Set(), runs = [];
  const clay = (r, c) => r >= 0 && r < R && c >= 0 && c < BOARD_COLS && CLAY.has(rows[r][c]);
  for (let r = 0; r < R; r++) for (let c = 0; c < BOARD_COLS; c++) {
    if (!clay(r, c) || seen.has(r + ',' + c)) continue;
    const stack = [[r, c]]; let n = 0; seen.add(r + ',' + c);
    while (stack.length) {
      const [y, x] = stack.pop(); n++;
      for (const [dy, dx] of [[-1, 0], [1, 0], [0, -1], [0, 1]]) {
        const k = (y + dy) + ',' + (x + dx);
        if (clay(y + dy, x + dx) && !seen.has(k)) { seen.add(k); stack.push([y + dy, x + dx]); }
      }
    }
    runs.push(n);
  }
  return runs.sort((a, b) => b - a);
}

test('a clay board always has a chain worth lighting', () => {
  const clayBoards = BOARDS.filter(b => [...b.rows.join('')].some(ch => CLAY.has(ch)));
  assert.equal(clayBoards.length, 3, 'crumble twice, plus the cracked key board');
  for (const b of clayBoards) {
    const runs = clayRuns(b.rows);
    assert.ok(runs[0] >= 3, b.id + ' has a chain of at least three adjacent clay (biggest run ' + runs[0] + ')');
  }
  assert.ok(clayRuns(BOARDS[1].rows).length >= 3, 'Soft Spot lays clay in separate seams, so the player picks one');
  // Long Vein: the two vertical veins run down INTO the long horizontal seam, so they are one
  // cascade, and the precarious run low down is the other. Two choices, not one.
  const vein = clayRuns(BOARDS[5].rows);
  assert.ok(vein.length >= 2, 'Long Vein gives the player more than one fuse');
  assert.ok(vein[0] >= 12, 'and the big seam is worth the walk down to it');
});

/* ------------------------------------------------------- clearable, proven */

/**
 * Flood fill from the open field (left, right and below the board) through the
 * empties. Anything the flood touches comes down, which opens more field; a
 * gate comes down only once a key of its trim has already been reached. If a
 * pass changes nothing and a breakable brick is still walled in, the board
 * cannot be cleared.
 */
function unreachable(rows) {
  const R = rows.length;
  const open = rows.map(row => [...row].map(ch => ch === '.'));
  const down = new Set();                       // cells already proven reachable
  const keysTurned = new Set();
  let changed = true;
  while (changed) {
    changed = false;
    const seen = rows.map(() => new Array(BOARD_COLS).fill(false));
    const stack = [];
    const push = (r, c) => {
      if (r < 0 || r >= R || c < 0 || c >= BOARD_COLS) return;
      if (seen[r][c] || !open[r][c]) return;
      seen[r][c] = true; stack.push([r, c]);
    };
    for (let r = 0; r < R; r++) { push(r, 0); push(r, BOARD_COLS - 1); }
    for (let c = 0; c < BOARD_COLS; c++) push(R - 1, c);
    while (stack.length) { const [r, c] = stack.pop(); push(r - 1, c); push(r + 1, c); push(r, c - 1); push(r, c + 1); }
    // outside the board on three sides is open field; above the top row is the ceiling
    const lit = (r, c) => (r >= R || c < 0 || c >= BOARD_COLS) ? true : (r < 0 ? false : seen[r][c]);
    for (let r = 0; r < R; r++) for (let c = 0; c < BOARD_COLS; c++) {
      if (open[r][c]) continue;
      const ch = rows[r][c];
      if (ch === 'X') continue;                                   // plain steel never comes down
      if ((ch === 'M' || ch === 'W') && !keysTurned.has(ch)) continue;
      if (![[r - 1, c], [r + 1, c], [r, c - 1], [r, c + 1]].some(([y, x]) => lit(y, x))) continue;
      open[r][c] = true; down.add(r + ',' + c); changed = true;
      if (ch === 'K') keysTurned.add('M');
      if (ch === 'Q') keysTurned.add('W');
    }
  }
  const left = [];
  for (let r = 0; r < R; r++) for (let c = 0; c < BOARD_COLS; c++) {
    const ch = rows[r][c];
    if (ch === '.' || ch === 'X') continue;
    if (!down.has(r + ',' + c)) left.push(ch + '@' + r + ',' + c);
  }
  return left;
}

test('every breakable brick on every board can be reached, gates included', () => {
  for (const b of BOARDS) {
    assert.deepEqual(unreachable(b.rows), [], b.id + ' walls nothing in');
  }
});

test('the flood fill is honest: a box with no key stays shut', () => {
  const sealed = ['......MMMMM.....', '......MUGVM.....', '......MMMMM.....', '.11111111111111.'];
  assert.ok(unreachable(sealed).length >= 3, 'no key, no box');
});

test('every gate trim on a board has a key of that trim, reachable before the gate', () => {
  for (const b of BOARDS) {
    const chars = new Set(b.rows.join(''));
    for (const gate of ['M', 'W']) {
      if (!chars.has(gate)) continue;
      assert.ok(chars.has(GATE_KEY[gate]), b.id + ' gate ' + gate + ' has its ' + GATE_KEY[gate] + ' key');
      assert.equal(brickSpec(GATE_KEY[gate]).key, gate, 'the legend still pairs them');
    }
    // and a key with no gate would be a dead brick
    for (const key of ['K', 'Q']) {
      if (chars.has(key)) assert.ok(chars.has(key === 'K' ? 'M' : 'W'), b.id + ' key ' + key + ' opens something');
    }
  }
});

test('a gate box really holds a prize, so opening it pays', () => {
  for (const b of BOARDS.filter(x => x.twist === 'keys')) {
    const inside = [...b.rows.join('')].filter(ch => 'UVF'.includes(ch));
    assert.ok(inside.length >= 1, b.id + ' has a loot box worth the key');
  }
});

/* ------------------------------------------------------------- it really runs */

/** Build the story at this board's wall and run it for a while. */
function live(id, steps = 300) {
  const row = storyBoardById(id);
  assert.ok(row, id + ' is in the story');
  const events = [];
  const game = createGame({ onEvent: (n, d) => events.push([n, d]), story: true, from: row.wall });
  for (let i = 0; i < steps; i++) game.step(0.016);
  return { row, game, events, s: game.snapshot() };
}

test('the small crumble board builds and runs crumble', () => {
  const { s, row } = live('st_habit_02');
  assert.equal(s.storyBoard, 'st_habit_02');
  assert.equal(s.storyAct, 'habit');
  assert.equal(s.storyCap, 0.65);
  assert.equal(s.doorColour, '#F062A8');
  assert.equal(s.door, null);
  assert.deepEqual(s.twists, ['crumble'], 'its own twist, listed once');
  assert.ok(s.bricks.some(b => b.clay), 'the seams are laid');
  assert.ok(row.act.id === 'habit');
});

test('the small keys board builds one gated box and runs keys alone', () => {
  const { s } = live('st_habit_04');
  assert.deepEqual(s.twists, ['keys'], 'no clay on this one, so no crumble along for the ride');
  assert.ok(s.bricks.some(b => b.gate === 'M'), 'the gold plates are up');
  assert.ok(!s.bricks.some(b => b.gate === 'W'), 'one trim only: this is the teaching board');
  assert.ok(s.bricks.some(b => b.key === 'M'), 'and the key is on the board');
});

test('the last board runs keys and crumble together, which is the cracked key', () => {
  const { s, events } = live('st_habit_07');
  assert.deepEqual(s.twists, ['keys', 'crumble']);
  assert.ok(s.bricks.some(b => b.key === 'M') && s.bricks.some(b => b.key === 'W'), 'both trims have keys');
  assert.ok(s.bricks.some(b => b.key === 'clay'), 'and the cracked key primes the seam');
  assert.ok(events.some(e => e[0] === 'storyWall'), 'the wall announced itself');
});

test('the seven walls sit end to end in the story, in order, inside the act', () => {
  const walls = BOARDS.map(b => storyBoardById(b.id).wall);
  assert.equal(walls.length, 7);
  walls.forEach((w, i) => { if (i) assert.equal(w, walls[i - 1] + 1, 'wall ' + w + ' follows the one before it'); });
  for (const b of BOARDS) assert.equal(storyBoardById(b.id).act.id, 'habit');
});

test('every board builds through the real game without throwing', () => {
  for (const b of BOARDS) {
    const { s } = live(b.id, 60);
    assert.equal(s.storyBoard, b.id, b.id + ' is what came up');
    assert.ok(s.bricks.length > 10, b.id + ' laid its wall');
    assert.ok(s.sat <= 0.65 + 1e-9, b.id + ' respects the act cap');
  }
});
