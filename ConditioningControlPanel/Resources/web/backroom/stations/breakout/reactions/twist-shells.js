/* ============================================================================
 * stations/breakout/reactions/twist-shells.js - what a shell throws off.
 * Twist: Shells (act 4). Going through one gets a grey ring and a breath of
 * aberration, no kick and no burst: it is a price, not a hit. Letting one go
 * gets a pale ring opening outward and a thin spray, the only bright thing the
 * twist owns. Cosmetic only. Honours fx.reduced and fx.colour.
 * ==========================================================================*/

/** The ghost ball's own greys, so a shell throws off exactly what it is made of. */
export const SHELL_RGB = [154, 154, 154], RIM_RGB = [208, 208, 208];

export default {
  /**
   * Through one. The room cools for a moment: one slow grey ring where the ball
   * went in, and a breath of aberration. Nothing shakes, nothing flashes.
   */
  shellTouch(fx, d) {
    if (!fx.colour) return;
    const x = Number.isFinite(d && d.x) ? d.x : fx.W / 2;
    const y = Number.isFinite(d && d.y) ? d.y : fx.H / 2;
    fx.stamps.push({ kind: 'ring', x, y, r0: 10, r1: 74, life: 0.5, rgb: SHELL_RGB });
    if (fx.reduced) return;
    fx.aberr(0.22);
  },

  /**
   * Let it go. A pale ring opening wide and slow, and a thin upward spray at full
   * motion. The release is brighter than the touch was, and still quiet.
   */
  shellPop(fx, d) {
    if (!fx.colour) return;
    const x = Number.isFinite(d && d.x) ? d.x : fx.W / 2;
    const y = Number.isFinite(d && d.y) ? d.y : fx.H / 2;
    fx.stamps.push({ kind: 'ring', x, y, r0: 14, r1: 150, life: 0.7, rgb: RIM_RGB });
    if (fx.reduced) return;
    fx.stamps.push({ kind: 'ring', x, y, r0: 6, r1: 84, life: 0.42, rgb: SHELL_RGB });
    if (fx.rungs(3)) fx.P.burst(x, y, RIM_RGB, 10, 120, 0.5, { rise: 90, gv: 60, r0: 0.8, r1: 1.5 });
  },
};
