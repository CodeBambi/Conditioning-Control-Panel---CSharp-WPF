/* ============================================================================
 * stations/breakout/reactions/twist-crumble.js - what clay throws off.
 * Twist: Crumble (Pink Fog). A crack sheds grit, a link throws a dust burst that
 * grows down the run, and the end of a run pays once. Cosmetic only.
 * Reduced motion keeps the rings, because the rings carry the information, and
 * drops every burst, kick and flash.
 * ==========================================================================*/

/** Clay dust and the pale glaze on a brick with one hit left. */
const DUST = [224, 201, 172], GLAZE = [255, 244, 230];
const DOWN = Math.PI / 2;

export default {
  /** A crack: grit off the face, and a rim of light on the crack itself. */
  clayCrack(fx, d) {
    if (!fx.colour) return;
    const last = (d.hp | 0) <= 1;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 3, r1: last ? 26 : 16, life: 0.2, rgb: last ? GLAZE : DUST });
    if (fx.reduced || !fx.rungs(3)) return;
    fx.P.spray(d.x, d.y, DOWN, 1.5, DUST, last ? 7 : 4, 120, 0.42, { gv: 320 });
    if (last) fx.P.burst(d.x, d.y, GLAZE, 4, 90, 0.3, { rise: 20, gv: 260, r0: 1, r1: 1.6 });
  },

  /** Armed. One pale ring, quiet, and it survives reduced motion: this is the tell. */
  clayReady(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 6, r1: 34, life: 0.32, rgb: GLAZE });
  },

  /**
   * A LINK. The burst grows down the run and the ring widens with it, so a long
   * vein looks like it is gathering rather than repeating.
   */
  clayChain(fx, d) {
    if (!fx.colour) return;
    const n = Math.max(1, d.n | 0), grow = Math.min(n, 8) / 8;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 5, r1: 30 + grow * 44, life: 0.26 + grow * 0.12, rgb: DUST });
    if (fx.reduced) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 2, r1: 18 + grow * 20, life: 0.16, rgb: fx.colours.WHITE });
    if (fx.rungs(3)) {
      fx.P.burst(d.x, d.y, DUST, 8 + Math.round(grow * 10), 150 + grow * 120, 0.5, { rise: 30, gv: 340, r0: 1.4, r1: 2.6 });
      fx.P.burst(d.x, d.y, GLAZE, 4, 220, 0.26, { rise: 0, gv: 180, r0: 1, r1: 1.6 });
      fx.P.rects(d.x, d.y, DUST, 2 + Math.round(grow * 3), { speed: 130 + grow * 90, life: 0.55, size: 3 });
    }
    fx.cam.kick(1.4 + grow * 3.4, 0.24, 0.004);
  },

  /** The run ends: one payoff, sized by how long it was. Under three links it barely speaks. */
  clayChainEnd(fx, d) {
    if (!fx.colour) return;
    const n = Math.max(1, d.n | 0), big = Math.min(n, 12) / 12;
    fx.stamps.push({ kind: 'ring', x: fx.W / 2, y: fx.H * 0.42, r0: 20, r1: 90 + big * 190, life: 0.42 + big * 0.3, rgb: GLAZE });
    if (n >= 3) fx.stamps.push({ kind: 'text', text: n + ' PIECES', x: fx.W / 2, y: fx.H * 0.4, life: 0.85, rgb: fx.colours.GOLD, size: 17 + Math.round(big * 13) });
    if (fx.reduced || n < 3) return;
    if (fx.rungs(3)) fx.P.burst(fx.W / 2, fx.H * 0.42, DUST, 10 + Math.round(big * 22), 230, 0.7, { rise: 60, gv: 300, r0: 1.6, r1: 3 });
    fx.cam.kick(2 + big * 5, 0.45, 0.006);
    fx.flash(0.05 + big * 0.1);
  },

  /** The whole wall went wobbly: one ring off the middle and a word for it. */
  clayPrimed(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: fx.W / 2, y: fx.H * 0.36, r0: 30, r1: 260, life: 0.6, rgb: GLAZE });
    fx.stamps.push({ kind: 'text', text: 'BRITTLE', x: fx.W / 2, y: fx.H * 0.34, life: 0.9, rgb: GLAZE, size: 22 });
    if (fx.reduced) return;
    fx.cam.kick(3, 0.5, 0.005);
    fx.flash(0.09);
  },
};
