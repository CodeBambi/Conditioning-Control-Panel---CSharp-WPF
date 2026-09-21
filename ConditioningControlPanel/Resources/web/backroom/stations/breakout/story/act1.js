/* ============================================================================
 * story/act1.js - MONDAY. Walls 1 to 5. Owned by the act 1 lane.
 *
 * SCAFFOLD STUB: one placeholder board, so the run plays end to end from
 * minute one. The lane replaces `boards` with five (story/CONTRACT.md).
 * Grey office life. Small, generous, teaching boards. No twists.
 * ==========================================================================*/
export default {
  id: 'monday',
  title: 'MONDAY',
  cap: 0.35,
  colour: '#8FA3B8',
  words: ['MEETING', 'LATER', 'FINE', 'BUSY', 'MONDAY', 'ASAP'],
  boards: [
    { id: 'st_monday_01', name: 'Desk Row', twist: null, family: 'breather',
      line: 'Placeholder. Three open courses, nothing in the way.',
      rows: [
        '..111111111111..',
        '..1S1111111G11..',
        '..111111111111..',
      ] },
  ],
};
