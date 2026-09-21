/* ============================================================================
 * stations/breakout/twists/mirror.js - the pure sim half of the twist.
 *
 * MIRROR (The Wardrobe). Two matching figures. A brick's twin is the cell at
 * `15 - col` in the same row, and breaking a brick breaks its twin a beat
 * later, steel twin included: steel is the seal, the mirror is the key. The
 * contents of the two figures do NOT map one to one, so a plain brick's twin
 * can be a power-up brick sealed in a steel box, and looking across the board
 * is the way in.
 *
 * Three rules this file exists to keep:
 *   1. ONE HOP. A twin's own pop never bounces back (`mirrorEcho`), so a pair
 *      cannot ping-pong and a break can never run round the board.
 *   2. ONE TIMER PER TWIN. A twin already on its way is not booked twice
 *      (`mirrorPending`), whatever breaks first.
 *   3. THE PAIR READS. The beam that links the two is sim state, aged on the
 *      sim clock, so what the eye sees is what the wall did (mirror-render.js).
 *
 * No DOM, no clock, no Math.random: this half runs under `node --test`.
 * See twists/CONTRACT.md.
 * ==========================================================================*/

export const id = 'mirror';

/** The authored wall is 16 wide (doors.js BOARD_COLS); a brick's twin is its reflection in that row. */
export const COLS = 16;
/** The beat between a brick breaking and its twin going: long enough to read as caused, short enough to feel like one move. */
export const TWIN_DELAY = 0.09;
/** How long the white beam between a pair stays up, and how long the seam's flare takes to fade. */
export const BEAM_LIFE = 0.7;
export const SEAM_FLARE = 0.55;
/** A beam bows by up to this much of its own length, so two beams in one row are not one line. */
export const BEAM_BOW = 0.09;
/** Beams on screen at once. Older ones drop off rather than pile up on a multiball. */
export const MAX_BEAMS = 6;

/** The mirror column: `15 - col` on the authored wall. */
export function twinCol(col, cols = COLS) {
  const c = Math.round(Number(col));
  if (!Number.isFinite(c)) return -1;
  return cols - 1 - c;
}

/** The twin brick of `br`, alive or dead, or null. A brick that is its own reflection has no twin. */
export function twinOf(br, ctx, cols = COLS) {
  if (!br || !ctx || typeof ctx.at !== 'function') return null;
  const tc = twinCol(br.col, cols);
  if (tc < 0 || tc === br.col) return null;
  const twin = ctx.at(br.row, tc);
  return twin && twin !== br ? twin : null;
}

/** The twist's own cosmetic state on the game object. Written by build, read by mirror-render.js. */
export function mirrorState(g) {
  if (!g.mirror) g.mirror = { beams: [], seam: 0, pops: 0, cols: COLS };
  return g.mirror;
}

/** A beam record: the two centres, its age, and the side it bows to. Pure, so a test can read it. */
export function makeBeam(br, twin, prize, bow) {
  return {
    x1: br.x + br.w / 2, y1: br.y + br.h / 2,
    x2: twin.x + twin.w / 2, y2: twin.y + twin.h / 2,
    age: 0, life: BEAM_LIFE, prize: !!prize, bow: Number(bow) || 0,
  };
}

/** Age the beams and the seam's flare. Exported so the test can run time without a game. */
export function ageBeams(st, dt) {
  const step = Math.max(0, Number(dt) || 0);
  if (st.beams.length) {
    for (const beam of st.beams) beam.age += step;
    st.beams = st.beams.filter(beam => beam.age < beam.life);
  }
  st.seam = Math.max(0, st.seam - step / SEAM_FLARE);
  return st;
}

export default {
  id,

  /** Once, after the authored wall is built: a clean slate for the beams and the seam. */
  build(g, ctx) {
    g.mirror = { beams: [], seam: 0, pops: 0, cols: COLS };
  },

  /**
   * A brick broke. Its twin follows TWIN_DELAY later through ctx.breakBrick, which clears
   * steel first, so a sealed loot box comes down with its plain reflection.
   */
  onBreak(g, br, ball, ctx) {
    // Rule 1: this break IS a twin's pop. It is the end of the hop, never the start of another.
    if (br.mirrorEcho) { br.mirrorEcho = false; return; }
    const st = mirrorState(g);
    const twin = twinOf(br, ctx, st.cols);
    if (!twin || !twin.alive) return;
    // Rule 2: a twin already on its way is not booked twice.
    if (twin.mirrorPending) return;
    twin.mirrorPending = true;

    const prize = !!(twin.powerup || twin.treat);
    const bow = ((typeof ctx.rng === 'function' ? ctx.rng() : 0.5) * 2 - 1) * BEAM_BOW;
    st.beams.push(makeBeam(br, twin, prize, bow));
    if (st.beams.length > MAX_BEAMS) st.beams.shift();
    st.seam = 1;
    st.pops++;

    ctx.emit('mirrorPop', {
      x: br.x + br.w / 2, y: br.y + br.h / 2,
      tx: twin.x + twin.w / 2, ty: twin.y + twin.h / 2,
      prize,
    });

    ctx.schedule(TWIN_DELAY, () => {
      twin.mirrorPending = false;
      if (!twin.alive) return;                 // the player got there first: nothing owed
      twin.mirrorEcho = true;
      ctx.breakBrick(twin, ball || null);
      twin.mirrorEcho = false;                 // belt and braces: a twin that somehow held keeps no flag
    });
  },

  /** Beams and the seam's flare live on the sim clock, so a held frame holds the pair. */
  update(g, dt, ctx) {
    if (g.mirror) ageBeams(g.mirror, dt);
  },
};
