/* ============================================================================
 * stations/breakout/doors.js - the five doors, their ten boards, and the legend.
 *
 * Pure data and pure functions. No DOM, no imports, no game state. The boards,
 * names, colours and twist assignments are copied from the owner-approved pitch
 * (C:/wt-bo/doors/mockup-v3.html, GROUPS + mk()); the word lists are ours.
 *
 * The house game (the existing eight walls) is untouched and stays the default.
 * A door is a short run: board one is clean, board two bends one rule.
 * ==========================================================================*/

/** Every char a board row may carry, and what it means. `.` is an empty cell. */
export const LEGEND = {
  '.': 'empty',
  1: 'plain brick, one hit', 2: 'plain brick, two hits', 3: 'plain brick, three hits',
  G: 'picture brick', S: 'spiral brick',
  X: 'steel, never breaks', M: 'gate steel, gold trim', W: 'gate steel, cyan trim',
  K: 'key, opens the gold gates', Q: 'key, opens the cyan gates', P: 'key, primes every clay brick',
  c: 'clay, three hits left', d: 'clay, two hits left', e: 'clay, one hit left (precarious)',
  w: 'wire', y: 'wire, the one the board wants cut', C: 'core',
  U: 'power-up brick, multiball', V: 'power-up brick, shield', F: 'power-up brick, fireball',
  T: 'treat brick',
};

/** Board rows are this wide, always. A row of any other width is a bug, not a style. */
export const BOARD_COLS = 16;
/** No authored board may be taller than this (the field has room for twelve courses). */
export const BOARD_MAX_ROWS = 12;

/** `U V F` name the game's real power kinds (powerups.js POWER_KINDS). */
export const POWER_BY_CHAR = { U: 'multiball', V: 'shield', F: 'fireball' };

/**
 * What one board char asks the wall builder for. Plain data: game.js turns this
 * into mkBrick extras, nothing here knows about a brick.
 * Fields: strength/hp (the game's own durability pair), and the twist flags the
 * lanes read (clay, wire, core, key, steel, gate, picture, spiralBrick, treat).
 */
export function brickSpec(ch) {
  if (ch === '.' || ch == null) return null;
  // `strength` is the game's armoured look (a cracking plate). A one-hit brick does not wear it,
  // so a picture, a spiral, a key and a power-up brick keep their own faces.
  if (ch === '1') return { hp: 1 };
  if (ch === '2' || ch === '3') { const n = +ch; return { strength: n, hp: n }; }
  if (ch === 'G') return { hp: 1, picture: true };
  if (ch === 'S') return { hp: 1, spiralBrick: true };
  if (ch === 'X') return { steel: true };
  if (ch === 'M' || ch === 'W') return { steel: true, gate: ch };
  if (ch === 'K') return { hp: 1, key: 'M' };
  if (ch === 'Q') return { hp: 1, key: 'W' };
  if (ch === 'P') return { hp: 1, key: 'clay' };
  if (ch === 'c' || ch === 'd' || ch === 'e') return { clay: true, strength: 3, hp: { c: 3, d: 2, e: 1 }[ch] };
  if (ch === 'w' || ch === 'y') return { wire: true, powered: true, cutme: ch === 'y', strength: 3, hp: 3 };
  if (ch === 'C') return { core: true, strength: 3, hp: 3 };
  if (ch === 'U' || ch === 'V' || ch === 'F') return { hp: 1, powerup: POWER_BY_CHAR[ch] };
  if (ch === 'T') return { hp: 1, treat: true, powerup: 'fireball' };
  return null;
}

/**
 * Read a board's rows into a cell list: `[{ row, col, ch, spec }]`, empties dropped.
 * Throws on a row of the wrong width or a char outside the legend, so a typo in a
 * board is a red test and never a silently missing brick.
 */
export function parseBoard(rows) {
  if (!Array.isArray(rows) || !rows.length) throw new Error('doors: a board needs rows');
  if (rows.length > BOARD_MAX_ROWS) throw new Error('doors: a board is at most ' + BOARD_MAX_ROWS + ' rows');
  const cells = [];
  rows.forEach((row, r) => {
    const text = String(row);
    if (text.length !== BOARD_COLS) throw new Error('doors: row ' + r + ' is ' + text.length + ' wide, not ' + BOARD_COLS);
    for (let c = 0; c < BOARD_COLS; c++) {
      const ch = text[c];
      if (!(ch in LEGEND)) throw new Error('doors: row ' + r + ' col ' + c + ' has "' + ch + '", not in the legend');
      const spec = brickSpec(ch);
      if (spec) cells.push({ row: r, col: c, ch, spec });
    }
  });
  return cells;
}

const E = '................';
/* The two matching figures. One board, used once; both halves hide a prize the other side opens. */
const MIRROR = ['.22222....22222.', '2XXX222..2221112', '2XUX222..2221112', '2XXX222..2221112',
  '2221112..2XXX222', '2G21112..2XFX222', '2221112..2XXX222', '.22222....22222.'];

/** The five doors, in the owner's order. `words` feeds the game's subliminal word bricks. */
export const DOORS = [
  { id: 'fog', name: 'Pink Fog', colour: '#F062A8', promise: 'Thinking is hard. Popping is easy.',
    words: ['DROP', 'FLOAT', 'PINK', 'SOFT', 'EMPTY', 'GIGGLE'],
    boards: [
      { id: 'fog_bath', name: 'Bubble Bath', twist: null,
        line: 'Five loose clusters, a prize in each. All momentum.',
        rows: ['.11....11....11.', '1221..1221..1221', '12G1..1S21..12G1', '.11....11....11.', E,
          '....11....11....', '...1221..1221...', '...1G21..12G1...', '....11....11....'] },
      { id: 'fog_brittle', name: 'Brittle', twist: 'crumble',
        line: 'A sugar-glass vein runs through the wall. Dig to it, light it.',
        rows: ['2222222222222222', '2eeee222222dddd2', '222Ge222222dG222', '2222eeeddddd2222',
          '1111111dd1111111', '111S111dd111S111'] },
    ] },
  { id: 'wardrobe', name: 'The Wardrobe', colour: '#A774E8', promise: 'Laced, zipped, buckled. The ball does the undressing.',
    words: ['LACE', 'ZIP', 'SILK', 'PRETTY', 'MIRROR', 'DRESS'],
    boards: [
      { id: 'ward_net', name: 'Fishnet', twist: null,
        line: 'A diamond mesh under a dark band. Every gap is a doorway.',
        rows: ['3333333333333333', '2.2.2.2.2.2.2.2.', '.2.2.2.2.2.2.2.2', '2.2.G.2.2.2.G.2.',
          '.2.2.2.2.2.2.2.2', '2.2.2.2.S.2.2.2.', '.2.2.2.2.2.2.2.2', '1.1.1.1.1.1.1.1.', '.1.1.1.1.1.1.1.1'] },
      { id: 'ward_mirror2', name: 'Mirror, Mirror', twist: 'mirror',
        line: 'Two matching figures. Each hides a prize only the other side opens.',
        rows: MIRROR },
    ] },
  { id: 'lock', name: 'Lock and Key', colour: '#D9A531', promise: 'Someone else holds the key.',
    words: ['LOCKED', 'WAIT', 'KEY', 'HOLD', 'DENIED', 'PATIENT'],
    boards: [
      { id: 'lock_combo', name: 'Combination', twist: null,
        line: 'A vault: two rings, doors on opposite sides, prizes in the core.',
        rows: ['3222222222222223', '2..............2', '2.111111111111.2', '2.1..........1.2',
          '2.1.22G2S2G2.1.2', '2.1..........1.2', '2.11111..11111.2', '2..............2', '32..222222222223'] },
      { id: 'lock_keys2', name: 'Two Keys', twist: 'keys',
        line: 'Two loot boxes, crossed keys, and a wax-seal wall the middle key can prime.',
        rows: ['MMMMM..22..WWWWW', 'MUG2M..22..W2GFW', 'MMMMM..22..WWWWW', E,
          '.cccccccccccccc.', E, '.1Q1...1P1...1K1', '.111...111...111'] },
    ] },
  { id: 'hive', name: 'The Hive', colour: '#2FCB72', promise: 'You are a unit. Units do not decide.',
    words: ['UNIT', 'OBEY', 'DRONE', 'SYNC', 'BLANK', 'SERVE'],
    boards: [
      { id: 'hive_serial', name: 'Serial Number', twist: null,
        line: 'A barcode. The ball slips into a channel and strips both walls.',
        rows: ['3.22.1.33.2.11.3', '3.22.1.33.2.11.3', '3.2G.1.33.2.1G.3', '3.22.1.33.2.11.3',
          '3.22.1.33.2.11.3', '3.22.1.3S.2.11.3', '3.22.1.33.2.11.3', '3.22.1.33.2.11.3', E, '1.1.11.1.1.11.1.'] },
      { id: 'hive_node2', name: 'Node', twist: 'node',
        line: 'One core, four dangling arms. Snip an arm high up, or dig to the core.',
        rows: ['2222222222222222', '2wwwwwwwwwwwwww2', '2w11w1Gw1G1w11w2', '2w11w11C111w11w2',
          '.w11w111111w11w.', '.w..w......w..w.', '.w............w.'] },
    ] },
  { id: 'ward', name: 'The Ward', colour: '#EE5A44', promise: 'You were doing so well.',
    words: ['DOSE', 'RELAPSE', 'REST', 'STILL', 'AGAIN', 'GOOD'],
    boards: [
      { id: 'ward_dose', name: 'Dosage', twist: null,
        line: 'Two-tone capsules on a pill tray. Every one is an aimed shot.',
        rows: ['1133..1133..1133', E, '..3311..3311....', E, '1G33..11S3..1G33', E,
          '..3311..3311....', E, '1133..1133..1133'] },
      { id: 'ward_justone', name: 'Just One', twist: 'justone',
        line: 'Same tray, three red ones. Each is six seconds of fireball and a longer grey after.',
        rows: ['1133..1133..1133', E, '..3311.T..3311..', E, '1133..11S3..1133', E,
          '.T....3311....T.', E, '1133..1G33..1133'] },
    ] },
];

export const doorById = id => DOORS.find(d => d.id === id) || null;
export const boardById = id => {
  for (const door of DOORS) { const board = door.boards.find(b => b.id === id); if (board) return { door, board }; }
  return null;
};
/** Every board, flat, in door order: what the dev harness and the picker walk. */
export const ALL_BOARDS = DOORS.flatMap(door => door.boards.map(board => ({ door, board })));

export default DOORS;
