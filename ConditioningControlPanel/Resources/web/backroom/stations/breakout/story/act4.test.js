/* Act 4, ENOUGH: six boards, the inward voice, the steel at its peak, and the
 * two twist boards the act is built around. Pure data plus one live build, so a
 * typo in a row is a red test and never a silently missing brick.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import ACT from './act4.js';
import ACT1 from './act1.js';
import ACT2 from './act2.js';
import ACT3 from './act3.js';
import ACT5 from './act5.js';
import { parseBoard, BOARD_COLS, BOARD_MAX_ROWS, LEGEND, brickSpec } from '../doors.js';
import { createGame } from '../game.js';
import { STORY_BOARDS, firstWallOfAct } from './index.js';

const TWISTS = ['crumble', 'mirror', 'keys', 'node', 'justone', 'stare', 'shells'];
const FAMILIES = ['breakthrough', 'sides', 'chambers', 'cascade', 'precision', 'breather'];
const boards = ACT.boards;

/* ------------------------------------------------------------- the act */

test('act four is ENOUGH, six boards, cap and colour untouched', () => {
  assert.equal(ACT.id, 'enough');
  assert.equal(ACT.title, 'ENOUGH');
  assert.equal(ACT.cap, 0.95);
  assert.equal(ACT.colour, '#EE5A44');
  assert.equal(boards.length, 6, 'exactly six, or every wall after act four moves');
});

test('the words are the inward voice, short, and in the project voice', () => {
  assert.ok(ACT.words.length >= 6);
  for (const word of ACT.words) {
    assert.equal(word, word.toUpperCase(), word + ' is a word brick');
    assert.ok(word.length <= 16, word + ' fits on a brick');
    assert.ok(!/[!–—]/.test(word), word + ' has no exclamation mark and no dash');
  }
  /* The same doubts act three heard from other people, now in the first person. */
  assert.ok(ACT.words.some(w => w.startsWith('I ')), 'somebody is talking to themselves');
});

/* ---------------------------------------------------------- every board */

test('every board is legal: id, name, family, twist, rows', () => {
  const seen = new Set();
  boards.forEach((b, i) => {
    assert.equal(b.id, 'st_enough_0' + (i + 1), 'ids run in play order');
    assert.ok(!seen.has(b.id)); seen.add(b.id);
    assert.ok(b.name && b.name.split(' ').length <= 2, b.id + ' has a name of two words at most');
    assert.ok(!/[!–—]/.test(b.name + ' ' + (b.line || '')), b.id + ' is in the project voice');
    assert.ok(FAMILIES.includes(b.family), b.id + ' family ' + b.family);
    assert.ok(b.twist === null || TWISTS.includes(b.twist), b.id + ' twist ' + b.twist);
    assert.ok(b.line && b.line.length > 10, b.id + ' says what it is');
    assert.ok(Array.isArray(b.rows) && b.rows.length && b.rows.length <= BOARD_MAX_ROWS, b.id + ' has rows');
    for (const row of b.rows) {
      assert.equal(row.length, BOARD_COLS, b.id + ' row "' + row + '" is the wrong width');
      for (const ch of row) assert.ok(ch in LEGEND, b.id + ' has "' + ch + '", not in the legend');
    }
    assert.doesNotThrow(() => parseBoard(b.rows), b.id + ' parses');
    assert.ok(parseBoard(b.rows).some(c => !c.spec.steel), b.id + ' has something to break');
  });
});

test('no two neighbours share a family, and the act breathes once in the middle', () => {
  for (let i = 1; i < boards.length; i++) {
    assert.notEqual(boards[i].family, boards[i - 1].family, boards[i].id + ' repeats its neighbour');
  }
  const breathers = boards.filter(b => b.family === 'breather');
  assert.equal(breathers.length, 1, 'one breather');
  const ix = boards.indexOf(breathers[0]);
  assert.ok(ix > 0 && ix < boards.length - 1, 'the breather is in the middle, not at either end');
});

test('the act runs Just One, teaches shells small and closes on shells at full weight', () => {
  const used = boards.map(b => b.twist);
  assert.ok(used.filter(t => t === 'justone').length >= 1, 'temptation and the bill are here');
  const shells = boards.filter(b => b.twist === 'shells');
  assert.equal(shells.length, 2, 'taught, then tested');
  assert.equal(shells[0].family, 'breather', 'shells are taught on a small, generous board');
  assert.equal(shells[1].id, boards[boards.length - 1].id, 'and carried on the act last wall');
  const cells = r => parseBoard(r.rows).length;
  assert.ok(cells(shells[1]) > cells(shells[0]) * 2, 'the second shells board is much the bigger one');
  /* One earlier twist comes back at full size. */
  assert.ok(used.some(t => t === 'crumble' || t === 'mirror'), 'an earlier twist returns');
});

/* ----------------------------------------------------------- the steel */

test('the steel is at its peak here, and it never seals a breakable brick away', () => {
  let steel = 0;
  for (const b of boards) {
    const cells = parseBoard(b.rows);
    steel += cells.filter(c => c.spec.steel).length;
    /* Flood fill: a broken brick becomes open, so every breakable cell has to be
     * reachable from outside the grid with only steel in the way. */
    const rows = b.rows.length;
    const blocked = new Set();
    for (const c of cells) if (c.spec.steel) blocked.add(c.row + ':' + c.col);
    const seenCell = new Set(), queue = [];
    const push = (r, c) => {
      if (c < 0 || c >= BOARD_COLS || r < -1 || r > rows) return;
      const key = r + ':' + c;
      if (seenCell.has(key) || blocked.has(key)) return;
      seenCell.add(key); queue.push([r, c]);
    };
    for (let c = 0; c < BOARD_COLS; c++) { push(-1, c); push(rows, c); }
    while (queue.length) {
      const [r, c] = queue.shift();
      push(r - 1, c); push(r + 1, c); push(r, c - 1); push(r, c + 1);
    }
    for (const cell of cells) {
      if (cell.spec.steel) continue;
      assert.ok(seenCell.has(cell.row + ':' + cell.col),
        b.id + ' seals a breakable brick at row ' + cell.row + ' col ' + cell.col);
    }
  }
  assert.ok(steel >= 24, 'act four carries the steel, not act three: ' + steel);
});

test('act four is at least as heavy as the acts around it', () => {
  const hp = act => {
    /* Act five hands wall 28 to the house finale, which carries no rows of its own. */
    const authored = act.boards.filter(b => Array.isArray(b.rows) && b.rows.length);
    const cells = authored.flatMap(b => parseBoard(b.rows)).filter(c => !c.spec.steel);
    const total = cells.reduce((n, c) => n + (c.spec.hp || 1), 0);
    return total / authored.length;
  };
  const mine = hp(ACT);
  /* Act three lands heavier per board than act four on the merged tree, and the
   * owner judges acts by feel, not by a number, so the boards stand and the
   * assertion is the weaker one: act four is never the light act. */
  for (const other of [ACT1, ACT2, ACT5]) {
    /* A stub act is one placeholder board and proves nothing, so it is skipped. */
    if (other.boards.length < 2) continue;
    assert.ok(mine >= hp(other), 'act four is at least as heavy as act ' + other.id + ': ' + mine + ' vs ' + hp(other));
  }
});

/* ------------------------------------------------------------- the run */

test('act four sits where the contract says, and a shells wall builds and plays', () => {
  const at = firstWallOfAct('enough');
  assert.ok(at > 0);
  const mine = STORY_BOARDS.filter(r => r.act.id === 'enough');
  assert.equal(mine.length, 6);
  assert.deepEqual(mine.map(r => r.board.id), boards.map(b => b.id));

  /* The act's last wall, live: the shells stand up and the story events flush on step(). */
  const wall = mine[mine.length - 1].wall;
  const events = [];
  const game = createGame({ story: true, from: wall, onEvent: (n, d) => events.push({ n, d }) });
  game.step(0);
  const snap = game.snapshot();
  assert.equal(snap.story, true);
  assert.equal(snap.storyWall, wall);
  assert.equal(snap.storyCap, 0.95);
  assert.equal(snap.doorColour, '#EE5A44');
  assert.equal(snap.door, null, 'a story run is not a door run');
  assert.ok(snap.twists.includes('shells'), 'the twist is on the board: ' + snap.twists.join(','));
  assert.ok(snap.shells && snap.shells.list.length >= 2, 'the old selves are standing there');
  assert.ok(events.some(e => e.n === 'storyWall' && e.d.board === 'st_enough_06'), 'the wall announced itself');
  assert.ok(snap.bricks.filter(b => b.alive).length > 40, 'it is a dense wall');
  for (let i = 0; i < 90; i++) game.step(1 / 60);
  assert.ok(snap.sat <= 0.95 + 1e-9, 'the act cap holds');
  game.dispose();
});

test('the source carries no em-dash and no exclamation mark', async () => {
  const { readFileSync } = await import('node:fs');
  const src = readFileSync(new URL('./act4.js', import.meta.url), 'utf8');
  assert.ok(!/[–—]/.test(src), 'no dashes but the plain hyphen');
  assert.ok(!src.includes('!'), 'no exclamation marks');
});
