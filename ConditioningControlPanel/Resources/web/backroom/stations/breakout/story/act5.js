/* ============================================================================
 * story/act5.js - OUT. Walls 26 to 28. Owned by the act 5 lane.
 *
 * After THE HABIT, THEY NOTICE and ENOUGH, this act stops arguing. Two open,
 * generous, full-juice walls and then the house finale. No steel, no twists,
 * nobody talking: the only words left are warm ones.
 *
 * The LAST entry stays `{ house: 'finale' }` - the existing wall 8 (the eye and
 * the Loom core), built exactly as the house game builds it, then the office pan
 * out and story/ending.js over it. Written as a literal on purpose: importing
 * story/index.js from here is a cycle and the whole story fails to load.
 * ==========================================================================*/

export default {
  id: 'out',
  title: 'OUT',
  cap: 1,
  colour: '#5FFFD0',
  // Not the office voice, not other people's, not the doubts. Six warm ones.
  words: ['BREATHE', 'OPEN', 'HERE', 'LIGHT', 'EASY', 'YOURS'],
  boards: [
    { id: 'st_out_01', name: 'Open Air', twist: null, family: 'breather',
      line: 'A breather. Everything is one hit, nothing is in the way, and nobody is talking.',
      rows: [
        '...1G1....1G1...',
        '..111111111111..',
        '.11S11111111S11.',
        '..1111U11V1111..',
        '...1111111111...',
        '....11G11G11....',
      ] },
    { id: 'st_out_02', name: 'All Yours', twist: null, family: 'cascade',
      line: 'The last stand before the last stand: a wide stack that falls in long sweeps.',
      rows: [
        '1111111111111111',
        '11SS11111111SS11',
        '11GG11111111GG11',
        '2222222222222222',
        '.22222222222222.',
        '..1U11111111V1..',
        '...1111111111...',
        '....1S1111S1....',
        '.....1F11F1.....',
        '......1111......',
      ] },
    { house: 'finale' },
  ],
};
