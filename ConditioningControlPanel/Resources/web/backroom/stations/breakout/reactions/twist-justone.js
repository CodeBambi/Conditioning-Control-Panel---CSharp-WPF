/* ============================================================================
 * stations/breakout/reactions/twist-justone.js - Just One (The Ward).
 * The offer opens a small red ring where the treat left its brick; taking one
 * throws warm sparks up off the paddle; letting one go gets a pale ring at the
 * floor and nothing else; the bill is one slow grey ring and a breath of
 * aberration, no shake, no burst - it is a price, not a punishment.
 * Cosmetic only. Honours fx.reduced and fx.colour.
 * ==========================================================================*/

/** The Ward's red and the hot centre of the treat (twists/justone-render.js). */
export const TREAT_RGB = [238, 90, 68], HOT_RGB = [255, 154, 107];

export default {
  treatDrop(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 5, r1: 34, life: 0.34, rgb: TREAT_RGB });
    if (fx.reduced || !fx.rungs(3)) return;
    fx.P.burst(d.x, d.y, HOT_RGB, 6, 90, 0.4, { rise: 60, gv: 120, r0: 1, r1: 1.8 });
  },

  /** Taken: two rings out of the paddle and a warm spray upward. The catch's own FIRE stamp still speaks. */
  treatCatch(fx, d, s) {
    if (!fx.colour) return;
    const x = s && s.paddle ? s.paddle.x : fx.W / 2;
    const y = s && s.paddle ? s.paddle.y - 10 : fx.H - 50;
    fx.stamps.push({ kind: 'ring', x, y, r0: 12, r1: 96, life: 0.46, rgb: TREAT_RGB });
    if (fx.reduced) return;
    fx.stamps.push({ kind: 'ring', x, y, r0: 4, r1: 50, life: 0.3, rgb: HOT_RGB });
    if (fx.rungs(3)) {
      fx.P.burst(x, y, HOT_RGB, 16, 220, 0.62, { rise: 150, gv: 190 });
      fx.P.burst(x, y, fx.colours.WHITE, 6, 140, 0.44, { rise: 170, gv: 80, r0: 1.1, r1: 1.7 });
    }
    fx.cam.kick(2.4, 0.34, 0.005);
    fx.flash(0.08);
  },

  /** Let go: one pale ring where it landed. Nothing shakes, nothing flashes, nothing was lost. */
  treatMiss(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: Number.isFinite(d && d.x) ? d.x : fx.W / 2, y: fx.H - 18, r0: 6, r1: 30, life: 0.3, rgb: fx.colours.GREY });
  },

  /**
   * The bill. One slow grey ring off the paddle and a breath of aberration. No
   * kick and no burst even at full motion: the relapse that follows has its own
   * cut, and this beat must read as the room cooling, not as a hit.
   */
  comedown(fx, d, s) {
    const x = s && s.paddle ? s.paddle.x : fx.W / 2;
    const y = s && s.paddle ? s.paddle.y : fx.H - 40;
    fx.stamps.push({ kind: 'ring', x, y, r0: 14, r1: fx.colour ? 190 : 90, life: 0.8, rgb: fx.colours.GREY });
    if (fx.reduced) return;
    fx.stamps.push({ kind: 'ring', x, y, r0: 8, r1: 120, life: 0.55, rgb: TREAT_RGB });
    fx.aberr(0.35);
  },
};
