import test from 'node:test';
import assert from 'node:assert/strict';
import { DOORS, ALL_BOARDS, LEGEND, BOARD_COLS, BOARD_MAX_ROWS, parseBoard, brickSpec, doorById, boardById, POWER_BY_CHAR } from './doors.js';
import { POWER_KINDS } from './powerups.js';
import { TWISTS } from './twists/index.js';

test('five doors, two boards each, ids and names and colours all present and unique', () => {
  assert.equal(DOORS.length, 5);
  assert.deepEqual(DOORS.map(d => d.id), ['fog', 'wardrobe', 'lock', 'hive', 'ward']);
  const boardIds = new Set();
  for (const door of DOORS) {
    assert.equal(door.boards.length, 2, door.id + ' has two boards');
    assert.match(door.colour, /^#[0-9A-F]{6}$/, door.id + ' has a hex colour');
    assert.ok(door.promise.length > 10 && door.name.length, door.id + ' has a name and a promise');
    assert.ok(door.words.length >= 4, door.id + ' has a word list');
    for (const word of door.words) assert.match(word, /^[A-Z ]{2,8}$/, word + ' fits the 5x5 font');
    assert.equal(door.boards[0].twist, null, door.id + ' board one is the clean one');
    assert.ok(door.boards[1].twist, door.id + ' board two bends a rule');
    for (const board of door.boards) {
      assert.equal(boardIds.has(board.id), false, board.id + ' is used once');
      boardIds.add(board.id);
      assert.ok(board.name && board.line, board.id + ' has a name and a one-line promise');
    }
  }
  assert.equal(ALL_BOARDS.length, 10);
});

test('every board row is 16 wide and every char is in the legend', () => {
  for (const { door, board } of ALL_BOARDS) {
    assert.ok(board.rows.length <= BOARD_MAX_ROWS, board.id + ' is not too tall');
    board.rows.forEach((row, r) => {
      assert.equal(row.length, BOARD_COLS, `${door.id}/${board.id} row ${r} is ${row.length} wide`);
      for (const ch of row) assert.ok(ch in LEGEND, `${board.id} row ${r} has "${ch}"`);
    });
    assert.doesNotThrow(() => parseBoard(board.rows), board.id + ' parses');
  }
});

test('parseBoard drops the empties and keeps row, col, char and spec', () => {
  const cells = parseBoard(['1..2............', '................', '..X.............']);
  assert.deepEqual(cells.map(c => [c.row, c.col, c.ch]), [[0, 0, '1'], [0, 3, '2'], [2, 2, 'X']]);
  assert.deepEqual(cells[0].spec, { hp: 1 }, 'a one-hit brick wears no armour plate');
  assert.deepEqual(cells[1].spec, { strength: 2, hp: 2 });
  assert.deepEqual(cells[2].spec, { steel: true });
});

test('a typo in a board is an error, not a silently missing brick', () => {
  assert.throws(() => parseBoard(['111']), /16/);
  assert.throws(() => parseBoard(['Z...............']), /legend/);
  assert.throws(() => parseBoard([]), /rows/);
  assert.throws(() => parseBoard(new Array(BOARD_MAX_ROWS + 1).fill('................')), /rows/);
});

test('the legend maps to what the game really has', () => {
  for (const kind of Object.values(POWER_BY_CHAR)) assert.ok(POWER_KINDS.includes(kind), kind + ' is a real power kind');
  assert.deepEqual(brickSpec('c'), { clay: true, strength: 3, hp: 3 });
  assert.deepEqual(brickSpec('e'), { clay: true, strength: 3, hp: 1 });
  assert.equal(brickSpec('y').cutme, true);
  assert.equal(brickSpec('w').cutme, false);
  assert.equal(brickSpec('M').gate, 'M');
  assert.equal(brickSpec('K').key, 'M');
  assert.equal(brickSpec('P').key, 'clay');
  assert.equal(brickSpec('T').treat, true);
  assert.equal(brickSpec('.'), null);
});

/** The two story twists are registered for the long story, not for a door (story/CONTRACT.md section 8). */
const STORY_TWISTS = ['shells', 'stare'];

test('every twist a board names is in the registry, and every twist is used', () => {
  const named = new Set(ALL_BOARDS.map(x => x.board.twist).filter(Boolean));
  assert.deepEqual([...named].sort(), ['crumble', 'justone', 'keys', 'mirror', 'node']);
  for (const id of named) assert.ok(TWISTS[id], id + ' is registered');
  for (const id of Object.keys(TWISTS)) {
    assert.ok(named.has(id) || STORY_TWISTS.includes(id), id + ' is on a door board or belongs to the story');
  }
  for (const id of STORY_TWISTS) assert.ok(TWISTS[id], id + ' is registered');
});

test('the twisted boards actually carry the pieces their twist needs', () => {
  const chars = id => new Set(boardById(id).board.rows.join(''));
  assert.ok(['c', 'd', 'e'].some(ch => chars('fog_brittle').has(ch)), 'Brittle has clay');
  assert.ok(chars('ward_mirror2').has('X'), 'Mirror, Mirror has steel to open from the far side');
  for (const ch of ['M', 'W', 'K', 'Q', 'P', 'c']) assert.ok(chars('lock_keys2').has(ch), 'Two Keys has ' + ch);
  for (const ch of ['w', 'C']) assert.ok(chars('hive_node2').has(ch), 'Node has ' + ch);
  assert.ok(chars('ward_justone').has('T'), 'Just One has treats');
});

test('lookups', () => {
  assert.equal(doorById('lock').name, 'Lock and Key');
  assert.equal(doorById('nope'), null);
  assert.equal(boardById('hive_node2').door.id, 'hive');
  assert.equal(boardById('nope'), null);
});
