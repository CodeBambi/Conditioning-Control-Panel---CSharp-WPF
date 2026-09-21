/* ============================================================================
 * stations/breakout/reactions/twist-keys.js - Keys and Gates, felt.
 *
 * The key turning throws a ring and a few filings in its own trim; the box
 * letting go throws a wider ring at the box and a small kick, so the eye is
 * already at the loot when the plates ripple down. No text: the colours and
 * the beam (drawn by keys-render.js) carry it.
 * Cosmetic only. Grey gets nothing; reduced motion keeps the rings and drops
 * every burst, kick and flash.
 * ==========================================================================*/

/** The trims as the particle pool wants them (doors.js `M` gold, `W` cyan, `P` clay). */
export const GATE_RGB = { M: [246, 211, 107], W: [127, 214, 232], clay: [217, 167, 124] };
const UP = -Math.PI / 2;

export const REACTIONS = {
  /** The lock turns: a tight ring, a spray of filings, one white pinprick. */
  keyTurn(fx, d) {
    const rgb = GATE_RGB[d && d.gate];
    if (!fx.colour || !rgb) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 4, r1: 40, life: 0.32, rgb });
    if (fx.reduced) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 2, r1: 18, life: 0.18, rgb: fx.colours.WHITE });
    if (fx.rungs(3)) {
      fx.P.spray(d.x, d.y, UP, 1.5, rgb, 9, 190, 0.34, { gv: 260 });
      fx.P.rects(d.x, d.y, rgb, 3, { speed: 150, life: 0.4, size: 2 });
    }
    fx.cam.kick(2, 0.3, 0.004);
  },

  /**
   * The box lets go. One ring per plate up to a handful, opening outward from
   * the box's middle, and a short flash: the plates themselves come down over
   * the next fifth of a second and do their own breaking.
   */
  gateOpen(fx, d, s) {
    const rgb = GATE_RGB[d && d.gate];
    if (!fx.colour || !rgb) return;
    const x = Number.isFinite(d.x) ? d.x : fx.W / 2, y = Number.isFinite(d.y) ? d.y : fx.H * 0.2;
    const n = Math.max(1, Math.min(4, Math.round(Number(d.n) || 1)));
    for (let i = 0; i < n; i++) {
      fx.stamps.push({ kind: 'ring', x, y, r0: 8 + i * 6, r1: 74 + i * 26, life: 0.42 + i * 0.06, rgb });
    }
    if (fx.reduced) return;
    fx.stamps.push({ kind: 'ring', x, y, r0: 4, r1: 52, life: 0.26, rgb: fx.colours.WHITE });
    if (fx.rungs(3)) {
      fx.P.burst(x, y, rgb, 16, 230, 0.55, { rise: 60, gv: 180 });
      fx.P.burst(x, y, fx.colours.WHITE, 6, 300, 0.3, { rise: 0, gv: 0, r0: 1, r1: 1.7 });
    }
    fx.cam.kick(3, 0.45, 0.006);
    fx.flash(0.12);
  },
};

export default REACTIONS;
