/* Act 5, OUT: walls 26 to 28. Two open walls and the house hand-off.
 * Nothing here needs the DOM: act5.js is pure data.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseBoard, BOARD_COLS, BOARD_MAX_ROWS, LEGEND } from '../doors.js';
import { createGame } from '../game.js';
import { STORY_WALLS } from './index.js';
import act5 from './act5.js';

const STEEL = new Set(['X', 'M', 'W']);
const CLAY = new Set(['c', 'd', 'e']);
const FAMILIES = new Set(['breakthrough', 'sides', 'chambers', 'cascade', 'precision', 'breather']);
const boards = act5.boards.filter(b => !b.house);

/* ------------------------------------------------------------- the shape */

test('act five is exactly three entries: two authored walls and the house finale', () => {
  assert.equal(act5.id, 'out');
  assert.equal(act5.title, 'OUT');
  assert.equal(act5.cap, 1);
  assert.equal(act5.colour, '#5FFFD0');
  assert.equal(act5.boards.length, 3);
  assert.equal(boards.length, 2);
  assert.deepEqual(act5.boards[2], { house: 'finale' }, 'the last entry is the house literal');
  assert.equal(act5.boards.filter(b => b.house).length, 1, 'one house entry, and it is last');
});

test('act5.js never imports the story index: that cycle kills the whole story', () => {
  const src = readFileSync(new URL('./act5.js', import.meta.url), 'utf8');
  assert.ok(!/from\s+['"]\.\/index\.js['"]/.test(src), 'no import of story/index.js');
  assert.ok(!/^\s*import\s/m.test(src), 'act5.js is pure data, it imports nothing');
});

/* ------------------------------------------------------------ the boards */

test('both boards are legal and in the project voice', () => {
  const ids = new Set();
  boards.forEach((b, i) => {
    assert.equal(b.id, 'st_out_0' + (i + 1));
    assert.ok(!ids.has(b.id)); ids.add(b.id);
    assert.ok(b.name && b.name.split(' ').length <= 2, b.id + ' has a short name');
    assert.ok(FAMILIES.has(b.family), b.id + ' has a real family: ' + b.family);
    assert.equal(b.twist, null, b.id + ' carries no twist: act five is done bending rules');
    assert.ok(typeof b.line === 'string' && b.line.length, b.id + ' has a dev line');
    for (const text of [b.name, b.line]) {
      assert.ok(!/[—–!]/.test(text), b.id + ' has no em-dash, en-dash or exclamation mark');
    }
    assert.ok(Array.isArray(b.rows) && b.rows.length <= BOARD_MAX_ROWS, b.id + ' is at most 12 rows');
    for (const row of b.rows) {
      assert.equal(row.length, BOARD_COLS, b.id + ' row is 16 wide');
      for (const ch of row) assert.ok(ch in LEGEND, b.id + ' char "' + ch + '"');
    }
    assert.doesNotThrow(() => parseBoard(b.rows), b.id + ' parses');
    assert.ok(parseBoard(b.rows).length > 20, b.id + ' is a generous wall');
  });
  assert.deepEqual(boards.map(b => b.family), ['breather', 'cascade'], 'a breather, then the big sweep');
});

test('no steel and no clay anywhere: nothing is in the way and no twist is implied', () => {
  for (const b of boards) {
    for (const row of b.rows) for (const ch of row) {
      assert.ok(!STEEL.has(ch), b.id + ' lays no steel');
      assert.ok(!CLAY.has(ch), b.id + ' lays no clay, so crumble never switches itself on');
    }
  }
});

test('the walls are open: mostly one hit, with pictures, spirals and power-ups', () => {
  for (const b of boards) {
    const cells = parseBoard(b.rows);
    const ones = cells.filter(c => c.ch === '1').length;
    assert.ok(ones / cells.length >= 0.6, b.id + ' is mostly one-hit bricks');
    assert.ok(cells.some(c => c.spec.picture), b.id + ' carries pictures');
    assert.ok(cells.some(c => c.spec.spiralBrick), b.id + ' carries spirals');
    assert.ok(cells.filter(c => c.spec.powerup).length >= 2, b.id + ' carries power-up bricks');
  }
});

/** Flood fill the open field through empties and breakables; steel is the only wall. */
function reachable(rows) {
  const h = rows.length, w = BOARD_COLS;
  const blocked = (r, c) => r >= 0 && r < h && c >= 0 && c < w && STEEL.has(rows[r][c]);
  const seen = new Set(), stack = [];
  for (let c = -1; c <= w; c++) { stack.push([-1, c]); stack.push([h, c]); }
  for (let r = 0; r < h; r++) { stack.push([r, -1]); stack.push([r, w]); }
  while (stack.length) {
    const [r, c] = stack.pop();
    const key = r + ':' + c;
    if (seen.has(key)) continue;
    if (r < -1 || r > h || c < -1 || c > w) continue;
    if (blocked(r, c)) continue;
    seen.add(key);
    stack.push([r + 1, c], [r - 1, c], [r, c + 1], [r, c - 1]);
  }
  return seen;
}

test('every breakable brick can be reached: no wall seals anything away', () => {
  for (const b of boards) {
    const seen = reachable(b.rows);
    b.rows.forEach((row, r) => {
      for (let c = 0; c < BOARD_COLS; c++) {
        if (row[c] === '.' || STEEL.has(row[c])) continue;
        assert.ok(seen.has(r + ':' + c), b.id + ' brick at ' + r + ',' + c + ' is reachable');
      }
    });
  }
});

test('the act speaks warmly or not at all', () => {
  assert.ok(Array.isArray(act5.words) && act5.words.length, 'act five carries its own words');
  for (const word of act5.words) {
    assert.match(word, /^[A-Z ]+$/, word + ' is a plain upper-case word');
    assert.ok(!/[—–!]/.test(word));
  }
  // None of the earlier acts' voices: no office, no other people, no doubt.
  for (const banned of ['MEETING', 'WEIRD', 'GROW UP', 'NOT NORMAL', 'I SHOULD STOP']) {
    assert.ok(!act5.words.includes(banned), banned + ' belongs to another act');
  }
});

/* ------------------------------------------------------- the house hand-off */

test('the house entry hands off: storyClear fires once, with house true, from the outro', () => {
  const events = [];
  const game = createGame({ onEvent: (n, d) => events.push([n, d]), story: true, from: STORY_WALLS, rng: () => 0.5 });
  game.step(0.016);
  const s = game.snapshot();
  assert.equal(s.storyWall, STORY_WALLS);
  assert.ok(s.finale, 'the last entry built the house finale');
  for (let i = 0; i < 80; i++) game.step(0.05);           // let it form
  // The outro is set deep inside a ball-collision branch: stand the ball on the core.
  s.finale.phase = 'released'; s.finale.stage = 2; s.finale.stageAge = 999;
  s.wallAge = 5; s.freeze = 0; s.hitStopMs = 0;
  const b = s.balls[0];
  b.stuck = false; b.falling = false; b.lost = false; b.orbit = false;
  b.x = s.finale.centreX; b.y = s.finale.centreY; b.vx = 60; b.vy = 0;
  game.step(0.02);
  game.step(0.02);                                        // story events flush on the next step
  assert.equal(s.finale.phase, 'outro', 'the office pan out is running');
  const done = events.filter(e => e[0] === 'storyClear');
  assert.equal(done.length, 1, 'storyClear fires exactly once');
  assert.equal(done[0][1].house, true, 'and it says the house finale ended it');
  assert.equal(done[0][1].wall, STORY_WALLS);
});
