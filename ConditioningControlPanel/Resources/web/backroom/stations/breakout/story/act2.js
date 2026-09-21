/* ============================================================================
 * story/act2.js - THE HABIT. Walls 6 to 12. Owned by the act 2 lane.
 *
 * Seven walls. You come back every night and the wall comes back with you.
 * Colour opens up (cap 0.65), power-up bricks appear (U V F), pictures and
 * spirals are common, and the first two twists are taught ONE AT A TIME:
 * crumble small then big, keys small then two-colour with the cracked key.
 *
 * Wall numbers are computed by story/index.js, never authored. Seven boards.
 * ==========================================================================*/
export default {
  id: 'habit',
  title: 'THE HABIT',
  cap: 0.65,
  colour: '#F062A8',
  words: ['SINK', 'DROP', 'RELAX', 'LET GO', 'DEEPER', 'BLANK'],
  boards: [
    /* 1. The dip. Small, loose, everything one or two hits, and the first
     * power-up bricks the story hands over. You are only here for a minute. */
    { id: 'st_habit_01', name: 'Every Night', twist: null, family: 'breather',
      line: 'A loose plate, two prizes and a pair of spirals. One minute, then bed.',
      rows: [
        '..222222222222..',
        '.11111111111111.',
        '.1G1111UU1111G1.',
        '....1S1..1S1....',
      ] },

    /* 2. Crumble, taught small. Two short precarious veins high up that pop on
     * sight, one long seam of two-hit clay under the prizes that does not. The
     * lesson is the wait: soften the seam, then choose the end to light. */
    { id: 'st_habit_02', name: 'Soft Spot', twist: 'crumble', family: 'cascade',
      line: 'Two brittle veins go at a touch. The long seam wants softening first.',
      rows: [
        '.22222222222222.',
        '.1eee111111eee1.',
        '..1G11111111G1..',
        '....dddddddd....',
        '.....1S11S1.....',
      ] },

    /* 3. Two open channels down the sides. Nothing is hard; the board is about
     * learning that the way in is never through the front. */
    { id: 'st_habit_03', name: 'Side Doors', twist: null, family: 'sides',
      line: 'Two channels down the sides. The front of the wall is the slow way.',
      rows: [
        '22..22222222..22',
        '11..1G1SS1G1..11',
        '22..11111111..22',
        '11..1V1111V1..11',
        '..111111111111..',
        '....11111111....',
      ] },

    /* 4. Keys, taught small. One trim, one box, one key, and the key sits in the
     * open. Hit the gold plates while it is locked and they only rattle. */
    { id: 'st_habit_04', name: 'One Key', twist: 'keys', family: 'chambers',
      line: 'One box, one key. The plates rattle until the key goes.',
      rows: [
        '......MMMMM.....',
        '......MUGVM.....',
        '......MMMMM.....',
        '................',
        '.22222222222222.',
        '.11G1111S1111G1.',
        '................',
        '.......1K1......',
      ] },

    /* 5. The first wall that is genuinely dense. A capped shaft up the middle:
     * two spirals hold the lid on, and above them the ball runs free. */
    { id: 'st_habit_05', name: 'The Chimney', twist: null, family: 'breakthrough',
      line: 'A shaft up the middle with two spirals capping it. Open it and stand back.',
      rows: [
        '.33333333333333.',
        '.222222..222222.',
        '.222222..222222.',
        '.1G1111..1111G1.',
        '..111F1..1F111..',
        '...1111SS1111...',
      ] },

    /* 6. Crumble, tested. Three seams: two short vertical veins, one long
     * horizontal one, and a precarious run low down that goes at once. Three
     * ways to spend the same board. */
    { id: 'st_habit_06', name: 'Long Vein', twist: 'crumble', family: 'cascade',
      line: 'Three seams through one wall. Pick the one you want to light.',
      rows: [
        '.22222222222222.',
        '.2cc22222222cc2.',
        '.2dd22G22G22dd2.',
        '.1dddddddddddd1.',
        '.11111111111111.',
        '.S111eeeeee111S.',
        '...1111111111...',
      ] },

    /* 7. Keys, tested. Two trims, two boxes, and the cracked key in the middle
     * primes the whole wax seam instead of opening anything. */
    { id: 'st_habit_07', name: 'Two Locks', twist: 'keys', family: 'chambers',
      line: 'Gold left, cyan right, and a cracked key that only primes the seam.',
      rows: [
        'MMMMM......WWWWW',
        'MU2GM......WG2VW',
        'MMMMM......WWWWW',
        '................',
        '.1cccccccccccc1.',
        '................',
        '.1K1...1P1...1Q1',
        '.111...111...111',
      ] },
  ],
};
