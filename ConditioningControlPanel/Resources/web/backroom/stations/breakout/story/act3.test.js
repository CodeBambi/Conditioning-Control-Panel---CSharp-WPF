/* Act 3, THEY NOTICE. The seven walls, the grey leak, and the eye on the last one. */
import test from 'node:test';
import assert from 'node:assert/strict';
import ACT from './act3.js';
import { ACT_TARGET, storyBoardById } from './index.js';
import { parseBoard, brickSpec, BOARD_COLS, BOARD_MAX_ROWS, LEGEND } from '../doors.js';
import { createGame } from '../game.js';

const BOARDS = ACT.boards;
const STEEL = new Set(['X', 'M', 'W']);
const FAMILIES = new Set(['breakthrough', 'sides', 'chambers', 'cascade', 'precision', 'breather']);
const TWISTS = new Set(['crumble', 'mirror', 'keys', 'node', 'justone', 'stare', 'shells']);
const steelCount = b => b.rows.join('').split('').filter(ch => STEEL.has(ch)).length;

/**
 * Every breakable brick has to be reachable. Steel is the only thing that blocks:
 * flood over every other cell from off the board (the field is open on all four
 * sides of the wall), and a brick is reachable if the flood found its cell.
 * On a MIRROR board a sealed brick also counts as open when its twin at
 * `15 - col` is reachable, because breaking the twin breaks it (twists/mirror.js).
 */
function reachable(board) {
  const rows = board.rows, R = rows.length, C = BOARD_COLS;
  const solid = (r, c) => STEEL.has(rows[r][c]);
  const seen = new Set(), queue = [];
  const key = (r, c) => r * C + c;
  const push = (r, c) => {
    if (r < 0 || c < 0 || r >= R || c >= C) return;
    if (solid(r, c) || seen.has(key(r, c))) return;
    seen.add(key(r, c)); queue.push([r, c]);
  };
  for (let r = 0; r < R; r++) { push(r, 0); push(r, C - 1); }
  for (let c = 0; c < C; c++) { push(0, c); push(R - 1, c); }
  for (let i = 0; i < queue.length; i++) {
    const [r, c] = queue[i];
    push(r - 1, c); push(r + 1, c); push(r, c - 1); push(r, c + 1);
  }
  const open = (r, c) => seen.has(key(r, c));
  const stranded = [];
  for (let r = 0; r < R; r++) for (let c = 0; c < C; c++) {
    const spec = brickSpec(rows[r][c]);
    if (!spec || spec.steel) continue;
    if (open(r, c)) continue;
    if (board.twist === 'mirror' && open(r, C - 1 - c)) continue;   // the mirror is the key
    stranded.push(r + ':' + c);
  }
  return stranded;
}

/* --------------------------------------------------------------- the act */

test('act three is THEY NOTICE and ships exactly its seven walls', () => {
  assert.equal(ACT.id, 'notice');
  assert.equal(ACT.title, 'THEY NOTICE');
  assert.equal(ACT.cap, 0.85);
  assert.equal(ACT.colour, '#A774E8');
  assert.equal(BOARDS.length, 7);
  assert.equal(BOARDS.length, ACT_TARGET.notice, 'a short act would shift every wall after it');
});

test('the words are other people, short, and never shouted', () => {
  assert.ok(ACT.words.length >= 6);
  for (const word of ACT.words) {
    assert.equal(word, word.toUpperCase(), word + ' is a word brick');
    assert.ok(word.length <= 12, word + ' fits on a brick');
    assert.ok(!/[!—–]/.test(word), word + ' is in the project voice');
  }
});

test('every board is legal: id, name, family, twist, rows', () => {
  const ids = new Set();
  BOARDS.forEach((b, i) => {
    assert.equal(b.id, 'st_notice_0' + (i + 1), 'boards are numbered in play order');
    assert.ok(!ids.has(b.id)); ids.add(b.id);
    assert.ok(b.name && b.name.split(' ').length <= 2, b.id + ' has a two word name');
    assert.ok(FAMILIES.has(b.family), b.id + ' family ' + b.family);
    assert.ok(b.twist === null || TWISTS.has(b.twist), b.id + ' twist ' + b.twist);
    assert.equal(typeof b.line, 'string', b.id + ' has a line for the harness');
    assert.ok(b.rows.length <= BOARD_MAX_ROWS, b.id + ' is at most twelve courses');
    for (const row of b.rows) {
      assert.equal(row.length, BOARD_COLS, b.id + ' row width');
      for (const ch of row) assert.ok(ch in LEGEND, b.id + ' char "' + ch + '"');
    }
    assert.doesNotThrow(() => parseBoard(b.rows), b.id + ' parses');
    assert.ok(parseBoard(b.rows).length > 6, b.id + ' is a wall and not a hint');
  });
});

test('no em-dash and no exclamation mark anywhere in the act', () => {
  const text = JSON.stringify(ACT);
  assert.ok(!/[—–]/.test(text), 'no dashes of the wrong kind');
  assert.ok(!/!/.test(text), 'nothing is shouted');
});

/* ------------------------------------------------------------ the design */

test('the grey leaks in: steel rises wall by wall and never falls back', () => {
  const counts = BOARDS.map(steelCount);
  assert.deepEqual(counts, [...counts].sort((a, b) => a - b), 'it only ever grows');
  for (let i = 1; i < counts.length; i++) assert.ok(counts[i] > counts[i - 1], BOARDS[i].id + ' has more grey than the wall before it');
  assert.ok(counts[0] <= 4, 'the first wall is only a couple of rivets');
  assert.ok(counts[counts.length - 1] >= 16, 'the last wall is a lattice');
});

test('steel never seals a breakable brick away from every approach', () => {
  for (const b of BOARDS) assert.deepEqual(reachable(b), [], b.id + ' strands bricks at these cells');
});

test('a sealed vault is only ever sealed on a mirror board', () => {
  for (const b of BOARDS) {
    if (b.twist === 'mirror') continue;
    const rows = b.rows, C = BOARD_COLS;
    const fake = { rows, twist: null };
    assert.deepEqual(reachable(fake), [], b.id + ' needs no key and has none');
    assert.equal(rows[0].length, C);
  }
  const vault = BOARDS.find(b => b.id === 'st_notice_05');
  assert.equal(vault.twist, 'mirror');
  assert.ok(reachable({ rows: vault.rows, twist: null }).length > 0, 'and the big mirror really does seal one');
});

test('the twists are rotated: mirror twice, node once, stare taught then tested last', () => {
  const twists = BOARDS.map(b => b.twist);
  assert.equal(twists.filter(t => t === 'mirror').length, 2);
  assert.equal(twists.filter(t => t === 'node').length, 1);
  assert.equal(twists.filter(t => t === 'stare').length, 2);
  assert.equal(twists.filter(Boolean).length, 5, 'five of seven carry one');
  assert.equal(twists[twists.length - 1], 'stare', 'the act ends under the eye');
  const stares = BOARDS.filter(b => b.twist === 'stare');
  assert.ok(parseBoard(stares[0].rows).length < parseBoard(stares[1].rows).length, 'taught small, tested big');
  const mirrors = BOARDS.filter(b => b.twist === 'mirror');
  assert.ok(steelCount(mirrors[0]) < steelCount(mirrors[1]), 'and the mirror gets more to open');
});

test('no two neighbouring walls are the same kind of wall, and one of them is a breather', () => {
  const fams = BOARDS.map(b => b.family);
  for (let i = 1; i < fams.length; i++) assert.notEqual(fams[i], fams[i - 1], BOARDS[i].id + ' feels like the one before it');
  assert.ok(fams.includes('breather'), 'the act gets one wall off');
  assert.equal(fams[0], 'breather', 'and it is the dip at the top of the act');
});

/* -------------------------------------------------------------- the run */

test('a stare wall really builds under a story run, with the eye and the act behind it', () => {
  const wall = storyBoardById('st_notice_07').wall;
  const events = [];
  const game = createGame({ onEvent: (n, d) => events.push([n, d]), story: true, from: wall });
  game.step(0.016);                                     // story events flush at the top of the next step
  const s = game.snapshot();
  assert.equal(s.storyWall, wall);
  assert.equal(s.storyAct, 'notice');
  assert.equal(s.storyCap, 0.85);
  assert.equal(s.doorColour, ACT.colour);
  assert.equal(s.door, null);
  assert.equal(s.doorBoard, 'st_notice_07');
  assert.equal(s.twist, 'stare');
  assert.ok(s.twists.includes('stare'), 'the twist is on the board');
  assert.ok(s.stare && s.stare.board === 'st_notice_07', 'and it built its own state');
  assert.ok(s.stare.presence > 0.8, 'the eye is not hiding on the last wall');
  const said = events.find(e => e[0] === 'storyWall');
  assert.equal(said[1].board, 'st_notice_07');
  assert.equal(said[1].twist, 'stare');
  assert.ok(s.bricks.filter(b => b.alive && b.steel).length >= 16, 'the lattice is there');
});

test('the act opens on its first wall when the story reaches it', () => {
  const wall = storyBoardById('st_notice_01').wall;
  const events = [];
  const game = createGame({ onEvent: (n, d) => events.push([n, d]), story: true, from: wall });
  game.step(0.016);
  const start = events.find(e => e[0] === 'actStart');
  assert.deepEqual(start[1], { act: 'notice', title: 'THEY NOTICE', wall, cap: 0.85, colour: ACT.colour });
  assert.equal(game.snapshot().doorBoard, 'st_notice_01');
});
