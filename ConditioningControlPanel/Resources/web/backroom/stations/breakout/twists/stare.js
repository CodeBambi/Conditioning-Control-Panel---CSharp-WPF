/* ============================================================================
 * stations/breakout/twists/stare.js - the pure sim half of the twist.
 * Twist: Stare (act 3, THEY NOTICE). The eye in the background tracks the ball.
 * While the ball sits in its gaze cone the nearest few plain bricks go grey:
 * flag `judged`, one extra hit, drawn desaturated. Breaking line of sight, or
 * any breakout, clears it. It never touches paddle control and never costs a ball.
 *
 * SCAFFOLD STUB: the shape only. The act 3 lane fills it. Every hook is
 * optional, so this stub is already safe on a board that names the twist.
 * Nothing here may touch the DOM, the clock or Math.random (twists/CONTRACT.md).
 * ==========================================================================*/
export const id = 'stare';

/** How long the ball has to sit in the cone before the eye judges. */
export const JUDGE_AFTER_S = 1.5;
/** How many bricks one judgement takes. */
export const JUDGE_N = 3;

/** The twist's own state, on its own key. Rebuilt with the board. */
function state(g) {
  if (!g.stare || g.stare.board !== g.doorBoard) {
    g.stare = { board: g.doorBoard, on: false, dwell: 0, judged: 0, eye: { x: g.w / 2, y: g.h * 0.22 } };
  }
  return g.stare;
}

export default {
  id,
  build(g) { g.stare = null; state(g); },
  // update(g, dt, ctx) - act 3 lane: dwell, stareOn {x,y}, stareJudge {x,y,n}, stareOff {}
};
