/* ============================================================================
 * story/act3.js - THEY NOTICE. Walls 13 to 19. Owned by the act 3 lane.
 *
 * The act where the grey world gets inside the colour one. Steel `X` is that
 * leak: a few rivets on the first wall, a sealed vault by the middle, a whole
 * grey lattice by the last. It never seals a breakable brick away from every
 * approach (the mirror boards are the one exception, and the mirror is the key).
 * The word bricks stop being the game's own voice and become other people's.
 * The eye is glimpsed on wall 15 and barely hides by wall 19.
 *
 * Pure data, no imports. See story/CONTRACT.md section 2 for the board shape.
 * ==========================================================================*/

/** An empty course. Written out because a board row is always sixteen wide. */
const E = '................';

export default {
  id: 'notice',
  title: 'THEY NOTICE',
  cap: 0.85,
  colour: '#A774E8',
  /* A word brick renders nine characters and clips the rest (render.js drawWordLabel), so every
   * line here is nine or under. That is why the voice is clipped short: it also reads better. */
  words: ['WEIRD', 'GROW UP', 'REALLY?', 'YOUR AGE', 'WHAT?', 'STILL?', 'A GAME?', 'AGAIN?'],
  boards: [
    /* 13 - the dip at the top of the act. Two rivets, and nothing else is wrong yet. */
    { id: 'st_notice_01', name: 'Small Talk', twist: null, family: 'breather',
      line: 'Two clusters and a low shelf. Two grey rivets sit in it and do not move.',
      rows: [
        '..2222....2222..',
        '..2G12....21G2..',
        '..2222....2222..',
        E,
        '..1X11111111X1..',
        '...1111SS1111...',
      ] },

    /* 14 - the mirror, taught small: two figures, each holding what the other opens. */
    { id: 'st_notice_02', name: 'Two Faces', twist: 'mirror', family: 'precision',
      line: 'Two matching figures. Each one is sealed, and the seal is on the other side.',
      rows: [
        '.22222....22222.',
        '2XUX2......2G122',
        '.22222....22222.',
        '221G2......2XFX2',
        '.22222....22222.',
      ] },

    /* 15 - the eye, taught small. A wide middle and open side channels: plenty
     * of places to be seen from, and the wall itself is the only cover there is. */
    { id: 'st_notice_03', name: 'First Look', twist: 'stare', family: 'sides',
      line: 'Open lanes down both sides. Stay in the open too long and the wall starts going grey.',
      rows: [
        '..333333333333..',
        '..3X22222222X3..',
        '..322G2SS2G223..',
        '..3X22222222X3..',
        '..333333333333..',
        '...1X1....1X1...',
      ] },

    /* 16 - one net, and the grey has got into the frame around it. */
    { id: 'st_notice_04', name: 'Switchboard', twist: 'node', family: 'chambers',
      line: 'One core in a lit frame. Snip an arm high up, or dig in and take the heart.',
      rows: [
        '2222222222222222',
        '2wwwwwwwwwwwwww2',
        '2wX1w1Gw1G1wX1w2',
        '2wX1w11C111wX1w2',
        '.w11w111111w11w.',
        '.w..w......w..w.',
        '.wXXw......wXXw.',
      ] },

    /* 17 - the mirror again, bigger, and this time the grey really has sealed
     * something: a vault with no way in at all, except its reflection. */
    { id: 'st_notice_05', name: 'Both Sides', twist: 'mirror', family: 'breakthrough',
      line: 'A vault with no door. Its twin cell on the far side is a plain brick.',
      rows: [
        '3333333333333333',
        '3XXX33333333X333',
        '3XUX33333333X333',
        '3XXX33333333X333',
        '222222222222X222',
        '.2211G12S21G122.',
      ] },

    /* 18 - a breather from the twists, not from the grey: pillars and a lintel. */
    { id: 'st_notice_06', name: 'Open Plan', twist: null, family: 'cascade',
      line: 'Grey pillars and a grey lintel. Drop through the middle and the courses fall.',
      rows: [
        '3333333333333333',
        '3XX333333333XX33',
        '3XX3G3S3G333XX33',
        '3XX333333333XX33',
        '2222222222222222',
        '..2222XXXX2222..',
        '...11111111111..',
      ] },

    /* 19 - the act's last wall. A grey lattice, nowhere to hide, and the eye is
     * not hiding either. Every lane you use is a lane it can see you in. */
    { id: 'st_notice_07', name: 'Full View', twist: 'stare', family: 'precision',
      line: 'A grey lattice with five open lanes. Every lane is a line of sight.',
      rows: [
        '3333333333333333',
        '33X333X333X333X3',
        '33X33GX33SX33GX3',
        '33X333X333X333X3',
        '22X222X222X222X2',
        '22X222X222X222X2',
        '11X11111111X1111',
        '..111111111111..',
      ] },
  ],
};
