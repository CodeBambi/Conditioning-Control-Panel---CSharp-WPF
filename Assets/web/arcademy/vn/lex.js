/* ============================================================================
 * vn/lex.js - FIRST BELL's copy, as data.
 *
 * Same shape and the same law as `shell/enrollment.js`'s ENROLL_LEX and
 * `games/impulse-control/lex.js`'s IC_LEX: this table IS the English, every
 * value is mirrored VERBATIM in `ArcademyHostService.NeutralLexicon`, and the
 * renderer reads it through `t(key, VN_LEX[key])` so a mod can re-voice the
 * school without the page ever printing a raw key (trap 15).
 *
 * TWO RULES THIS FILE EXISTS TO KEEP
 *
 * 1. NO ROW OVER 96 CHARACTERS. `MergeModTable` drops a mod string longer than
 *    that (trap 26), so a long row can never be re-voiced. The two papers are
 *    therefore stored as CLAUSE ROWS and composed back into paragraphs by
 *    joining with a single space - `PAPERS` below names the order. The joined
 *    result is byte-for-byte the owner-vetted paragraph; splitting is a storage
 *    decision and never a rewrite.
 * 2. THE WORDS ARE NOT OURS TO EDIT. Every string below is verbatim from
 *    `FIRST-BELL.md` (owner-vetted 2026-08-24, rulings 1-5). House style bans
 *    em-dashes, fragment stacks and officialese in ALL user-facing text here,
 *    the two chrome rows and the aria copy included. If a line reads wrong the
 *    fix lands in the beat sheet first and is copied down.
 * ==========================================================================*/

export const VN_LEX = Object.freeze({
  /* --- chrome ----------------------------------------------------------- */
  vn_skip: 'Hold to skip',
  vn_tap: 'Tap to continue',

  /* --- s01, the gates (two vetted captions, nothing else) --------------- */
  vn_s01_cap1: 'The gates open at dusk and classes run every night, holidays included.',
  vn_s01_cap2: 'Your enrollment went through last week. First bell rings in the main hall.',

  /* --- paper #1, on the admissions desk --------------------------------- */
  vn_p1_title: 'WELCOME TO THE ARCADEMY',
  vn_p1_a: 'Hi! You\'re all set.',
  vn_p1_b: 'Tonight\'s four classes go up on the big board over this desk at first bell,',
  vn_p1_c: 'homeroom first and then whatever order you feel like.',
  vn_p1_d: 'You don\'t need to bring anything, every room already has its own machine',
  vn_p1_e: 'and the machine has everything.',
  vn_p1_f: 'Nobody\'s at the desk after dark, so if a cabinet acts up,',
  vn_p1_g: 'give it one gentle kick and leave us a note in the tray.',
  vn_p1_h: 'Have a great first night!',

  /* --- s03, the walk to Homeroom ---------------------------------------- */
  vn_s03_cap: 'Homeroom is room 101, first door on your left, just follow the footprint decals.',

  /* --- paper #2, out from under the board ------------------------------- */
  vn_p2_title: 'NICE ONE!',
  vn_p2_a: 'That\'s your first stamp of the year.',
  vn_p2_b: 'Three classes are still lit on the board if you\'re up for another,',
  vn_p2_c: 'and if you\'re done for tonight that\'s fine too,',
  vn_p2_d: 'the board deals fresh at dusk either way.',
  vn_p2_e: 'Replay anything as much as you like,',
  vn_p2_f: 'the card just takes one stamp per class a night.',
  vn_p2_g: 'Spare tokens can go in the fountain, it\'s supposed to be good luck,',
  vn_p2_h: 'or at least that\'s what everybody writes in the yearbook.',

  /* --- both papers are signed the same way (ruling 1) ------------------- */
  vn_sign: '- the front desk',
});

/**
 * THE TWO PAPERS, as paragraphs of clause rows. A paragraph is its rows joined
 * with ONE space; that is what reproduces the vetted text exactly.
 */
export const PAPERS = Object.freeze({
  p1: Object.freeze({
    title: 'vn_p1_title',
    paras: Object.freeze([
      Object.freeze(['vn_p1_a', 'vn_p1_b', 'vn_p1_c', 'vn_p1_d', 'vn_p1_e']),
      Object.freeze(['vn_p1_f', 'vn_p1_g']),
      Object.freeze(['vn_p1_h']),
    ]),
    sign: 'vn_sign',
  }),
  p2: Object.freeze({
    title: 'vn_p2_title',
    paras: Object.freeze([
      Object.freeze(['vn_p2_a', 'vn_p2_b', 'vn_p2_c', 'vn_p2_d', 'vn_p2_e', 'vn_p2_f']),
      Object.freeze(['vn_p2_g', 'vn_p2_h']),
    ]),
    sign: 'vn_sign',
  }),
});

export default VN_LEX;
