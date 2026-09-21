/* ============================================================================
 * story/act1.js - MONDAY. Walls 1 to 5. Owned by the act 1 lane.
 *
 * Grey office life. Small, generous, teaching boards. No twists, no steel,
 * mostly one-hit bricks with a few two-hit ones. Colour is a rumour: a picture
 * brick here and there, and one spiral brick on the last wall.
 *
 * Wall 1 is over in well under a minute. Difficulty climbs gently across the
 * act (28, 44, 62, 68, 70 hit points) and never dips inside it, and the five
 * families rotate so no two neighbours feel alike.
 *
 * Pure data. `id`, `cap` and `colour` belong to the scaffold and are not the
 * lane's to change (story/CONTRACT.md section 1).
 * ==========================================================================*/
export default {
  id: 'monday',
  title: 'MONDAY',
  cap: 0.35,
  colour: '#8FA3B8',
  words: ['MEETING', 'LATER', 'FINE', 'BUSY', 'MONDAY', 'ASAP', 'PENDING', 'REPLY'],
  boards: [
    { id: 'st_monday_01', name: 'Desk Row', twist: null, family: 'breather',
      line: 'Three courses of nothing much, and nowhere to get stuck.',
      rows: [
        '...1111111111...',
        '...11111G1111...',
        '....11111111....',
      ] },

    { id: 'st_monday_02', name: 'Inbox', twist: null, family: 'cascade',
      line: 'Stacked trays with a clear lane up each side.',
      rows: [
        '.11111111111111.',
        '.1............1.',
        '.11111111111111.',
        '.1............1.',
        '..111111G11111..',
      ] },

    { id: 'st_monday_03', name: 'Two Doors', twist: null, family: 'sides',
      line: 'A block in the middle and a door down either flank.',
      rows: [
        '11..11111111..11',
        '11..12222221..11',
        '11..1G2222G1..11',
        '11..11111111..11',
        '11............11',
      ] },

    { id: 'st_monday_04', name: 'Overtime', twist: null, family: 'breakthrough',
      line: 'One dense course with two seams, and an open field above it.',
      rows: [
        '..111111111111..',
        '.11111111111111.',
        '.1G1111111111G1.',
        '2222.222222.2222',
      ] },

    { id: 'st_monday_05', name: 'Friday', twist: null, family: 'precision',
      line: 'Narrow columns and thin gaps, so every shot is aimed.',
      rows: [
        '.11.11.11.11.11.',
        '.G1.22.11.22.1G.',
        '.11.22.22.22.11.',
        '.11.22.1S.22.11.',
        '.11.22.22.22.11.',
      ] },
  ],
};
