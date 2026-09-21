/* ============================================================================
 * story/act4.js - ENOUGH. Walls 20 to 25. Owned by the act 4 lane.
 *
 * The voice that was other people in act 3 turns inward and says the same things
 * in the first person. This is the hardest act: dense, three-hit heavy, and the
 * steel X intrusions that started leaking into act 3 are at their peak here.
 *
 * Six boards, families rotated so no two neighbours feel alike, one breather in
 * the middle. Just One is the temptation and the bill. The OLD SELF shells are
 * taught on the breather and carried at full weight on the act's last wall.
 * Crumble comes back full size on wall four: the same thing, again.
 *
 * Pure data. `id`, `cap` and `colour` are the scaffold's and are not touched.
 * ==========================================================================*/
export default {
  id: 'enough',
  title: 'ENOUGH',
  cap: 0.95,
  colour: '#EE5A44',
  /* A word brick renders nine characters and clips the rest (render.js drawWordLabel), so every
   * line here is nine or under. The inward voice is cut off mid thought, which is what it sounds like. */
  words: ['I SHOULD', 'WHAT AM I', 'NOT OK', 'LAST TIME',
    'ONE MORE', 'ENOUGH', 'AGAIN', 'UP LATE'],
  boards: [
    /* The act opens by dipping under the end of act 3, then climbs for five walls. */
    { id: 'st_enough_01', name: 'Two Minds', twist: null, family: 'chambers',
      line: 'Two rooms, the same argument in both. Get inside either one.',
      rows: [
        '.333333..333333.',
        '.3..G..2.2..G..3',
        '.3.222.2.2.222.3',
        '.3..2..2.2..2..3',
        '.333333..333333.',
        '..22........22..',
        '...1S1....1S1...',
      ] },

    /* Temptation arrives early, where there is still room to pay for it. */
    { id: 'st_enough_02', name: 'Just Once', twist: 'justone', family: 'sides',
      line: 'Four red ones on a slab with open lanes down both sides.',
      rows: [
        '..333333333333..',
        '..3T22222222T3..',
        '..332222222233..',
        '..3..222222..3..',
        '..3.T.2222.T.3..',
        '..333322223333..',
        '....G2....2G....',
      ] },

    /* The breather, and where the shells are taught: small, open, nothing to lose. */
    { id: 'st_enough_03', name: 'Old Self', twist: 'shells', family: 'breather',
      line: 'A short wall and two old selves drifting through it. The ball goes straight through them.',
      rows: [
        '...1111111111...',
        '...1.2....2.1...',
        '...1.2.GG.2.1...',
        '...1.2....2.1...',
        '...1111111111...',
      ] },

    /* Crumble, full size: a sugar vein through a dense wall, with steel in the middle of it. */
    { id: 'st_enough_04', name: 'Same Again', twist: 'crumble', family: 'cascade',
      line: 'A clay vein runs the whole wall. Dig down to it and light it from the end.',
      rows: [
        '2222222222222222',
        '2cc3333..3333cc2',
        '22cdd3....3ddc22',
        '333ee3XXXX3ee333',
        '2222e333333e2222',
        '11112eddddde2111',
        '1111.2eccce2.111',
        '..S1..122221..G.',
      ] },

    /* The peak of the steel. Narrow lanes, nothing generous, everything reachable. */
    { id: 'st_enough_05', name: 'Not Normal', twist: null, family: 'precision',
      line: 'Steel posts and thin lanes. There is a way past every one of them.',
      rows: [
        'X3333X2222X3333X',
        '.3..3..22..3..3.',
        '.3..3.2222.3..3.',
        'X3333X2222X3333X',
        '..2G2..S...2G2..',
        '..222..X...222..',
        '...1....X....1..',
      ] },

    /* The act's last wall: one gap in a steel lid, a packed room behind it, and every
     * old self the run has left behind drifting across the approach. */
    { id: 'st_enough_06', name: 'Enough', twist: 'shells', family: 'breakthrough',
      line: 'One gap in the lid, a full room behind it, and the old selves in the way.',
      rows: [
        '3333333333333333',
        '3222222222222223',
        '32XXXXX..XXXXX23',
        '3221111..1111223',
        '32211G1..1G11223',
        '3221111..1111223',
        '32XXXXXXXXXXXX23',
        '3222222222222223',
        '.33S3333333S33..',
      ] },
  ],
};
