/* Act 1, MONDAY. Walls 1 to 5: the grey office. Small, generous, no twists.
 * This file guards the act's own shape. The seam (wall numbers, the cap, the
 * events) is story/story.test.js and belongs to the scaffold.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseBoard, brickSpec, BOARD_COLS, BOARD_MAX_ROWS, LEGEND } from '../doors.js';
import ACT1 from './act1.js';

const FAMILIES = ['breakthrough', 'sides', 'chambers', 'cascade', 'precision', 'breather'];
const boards = ACT1.boards;

/** Hit points a board asks for: the honest measure of how long a wall takes. */
function hitPoints(rows) {
  let hp = 0;
  for (const cell of parseBoard(rows)) {
    const n = Number(cell.spec.hp);
    if (Number.isFinite(n)) hp += n;
  }
  return hp;
}

/**
 * Is every breakable brick reachable? Flood the board from outside it, moving
 * through anything that is not steel (an empty cell, or a brick the ball will
 * eventually break through). A breakable brick the flood never touches is
 * walled in behind steel and the wall can never be cleared.
 */
function unreachable(rows) {
  const h = rows.length, w = BOARD_COLS;
  const steel = (r, c) => {
    if (r < 0 || r >= h || c < 0 || c >= w) return false;
    const spec = brickSpec(rows[r][c]);
    return !!(spec && spec.steel);
  };
  const seen = new Set();
  const stack = [];
  // Seed from every border cell that is not steel, plus the open space around the board.
  for (let r = 0; r < h; r++) for (let c = 0; c < w; c++) {
    if ((r === 0 || r === h - 1 || c === 0 || c === w - 1) && !steel(r, c)) stack.push([r, c]);
  }
  while (stack.length) {
    const [r, c] = stack.pop();
    const key = r + ':' + c;
    if (seen.has(key)) continue;
    if (r < 0 || r >= h || c < 0 || c >= w) continue;
    if (steel(r, c)) continue;
    seen.add(key);
    stack.push([r + 1, c], [r - 1, c], [r, c + 1], [r, c - 1]);
  }
  const out = [];
  for (let r = 0; r < h; r++) for (let c = 0; c < w; c++) {
    const spec = brickSpec(rows[r][c]);
    if (spec && !spec.steel && !seen.has(r + ':' + c)) out.push(r + ':' + c);
  }
  return out;
}

test('act one is MONDAY and ships exactly five walls', () => {
  assert.equal(ACT1.id, 'monday');
  assert.equal(ACT1.title, 'MONDAY');
  assert.equal(ACT1.cap, 0.35);
  assert.equal(boards.length, 5, 'a wrong count shifts every wall after this act');
  assert.ok(ACT1.words.length >= 4, 'the act has an office voice');
  for (const word of ACT1.words) assert.match(word, /^[A-Z ]{3,9}$/, 'word brick "' + word + '"');
});

test('every board is legal: id, name, family, rows, legend', () => {
  const ids = new Set();
  boards.forEach((b, i) => {
    assert.equal(b.id, 'st_monday_0' + (i + 1), 'board ' + i + ' is numbered in play order');
    assert.ok(!ids.has(b.id)); ids.add(b.id);
    assert.ok(b.name && b.name.split(' ').length <= 2, b.id + ' has a name of two words at most');
    assert.ok(FAMILIES.includes(b.family), b.id + ' has a real family');
    assert.ok(typeof b.line === 'string' && b.line.length > 0, b.id + ' has a line');
    assert.ok(b.rows.length <= BOARD_MAX_ROWS, b.id + ' is at most twelve rows');
    for (const row of b.rows) {
      assert.equal(row.length, BOARD_COLS, b.id + ' row is sixteen wide');
      for (const ch of row) assert.ok(ch in LEGEND, b.id + ' char "' + ch + '"');
    }
    assert.doesNotThrow(() => parseBoard(b.rows), b.id + ' parses');
    assert.ok(parseBoard(b.rows).length > 0, b.id + ' lays bricks');
  });
});

test('MONDAY is the grey office: no twists, no steel, no power-ups', () => {
  for (const b of boards) {
    assert.equal(b.twist, null, b.id + ' runs no twist');
    for (const cell of parseBoard(b.rows)) {
      assert.ok(!cell.spec.steel, b.id + ' lays no steel');
      assert.ok(!cell.spec.clay && !cell.spec.wire && !cell.spec.core, b.id + ' lays no twist furniture');
      assert.ok(!cell.spec.powerup && !cell.spec.key, b.id + ' hands out nothing yet');
    }
  }
});

test('the act teaches: mostly one-hit bricks, the spiral held back to the last wall', () => {
  for (const b of boards) {
    const cells = parseBoard(b.rows);
    const ones = cells.filter(c => Number(c.spec.hp) === 1).length;
    assert.ok(ones / cells.length >= 0.55, b.id + ' is mostly one-hit bricks');
    assert.ok(cells.every(c => Number(c.spec.hp) <= 2), b.id + ' has nothing tougher than a two-hit brick');
  }
  const spirals = boards.map(b => parseBoard(b.rows).filter(c => c.spec.spiralBrick).length);
  assert.deepEqual(spirals, [0, 0, 0, 0, 1], 'one spiral brick, late, as the first taste of colour');
  const pictures = boards.reduce((n, b) => n + parseBoard(b.rows).filter(c => c.spec.picture).length, 0);
  assert.ok(pictures >= 1 && pictures <= 10, 'a picture brick or two, not a gallery');
});

test('the families rotate and the act has a breather', () => {
  const fams = boards.map(b => b.family);
  assert.ok(fams.includes('breather'), 'every act owes a breather');
  assert.equal(boards[0].family, 'breather', 'wall one is the gentlest wall in the story');
  fams.forEach((f, i) => { if (i) assert.notEqual(f, fams[i - 1], 'no two neighbours share a family'); });
});

test('difficulty climbs gently and never drops sharply inside the act', () => {
  const hp = boards.map(b => hitPoints(b.rows));
  assert.ok(hp[0] <= 32, 'wall one is clearable in well under a minute, not ' + hp[0] + ' hit points');
  hp.forEach((n, i) => {
    if (!i) return;
    assert.ok(n >= hp[i - 1] * 0.8, 'wall ' + (i + 1) + ' (' + n + ') does not drop sharply from ' + hp[i - 1]);
  });
  assert.ok(hp[hp.length - 1] > hp[0], 'the act climbs');
  assert.ok(hp.every(n => n <= 110), 'MONDAY stays small and generous');
});

test('every breakable brick on every board can be reached', () => {
  for (const b of boards) {
    assert.deepEqual(unreachable(b.rows), [], b.id + ' walls a brick in');
  }
});

test('no board is shaped like an object, and the copy is in the project voice', () => {
  // A board that is one solid blob or one thin ring reads as a picture of a thing.
  for (const b of boards) {
    const cells = parseBoard(b.rows).length;
    const area = b.rows.length * BOARD_COLS;
    assert.ok(cells / area <= 0.95, b.id + ' is not one solid slab');
    assert.ok(cells >= 20, b.id + ' has a wall, not an outline');
  }
  const copy = [ACT1.title, ...ACT1.words, ...boards.map(b => b.name), ...boards.map(b => b.line)].join(' ');
  assert.ok(!/[—–]/.test(copy), 'no em-dashes or en-dashes');
  assert.ok(!/!/.test(copy), 'no exclamation marks');
  // eslint-disable-next-line no-control-regex
  assert.ok(!/[^\x00-\x7F]/.test(copy), 'plain ASCII copy');
});
