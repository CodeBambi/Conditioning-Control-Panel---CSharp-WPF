/* ============================================================================
 * story/act5.js - OUT. Walls 26 to 28. Owned by the act 5 lane.
 *
 * SCAFFOLD STUB: one placeholder board plus the house finale hand-off. The lane
 * replaces the authored part with two open, generous, full-juice walls and
 * fills story/ending.js. The LAST entry stays `{ house: 'finale' }`: that is
 * the existing wall 8 (the eye / Loom core), built exactly as the house game
 * builds it, then the office pan out.
 * ==========================================================================*/

export default {
  id: 'out',
  title: 'OUT',
  cap: 1,
  colour: '#5FFFD0',
  words: [],
  boards: [
    { id: 'st_out_01', name: 'Open Air', twist: null, family: 'breather',
      line: 'Placeholder. Nothing in the way, and nobody talking.',
      rows: [
        '...1111111111...',
        '..11G111111G11..',
        '...111111S111...',
      ] },
    { house: 'finale' },
  ],
};
