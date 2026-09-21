/* ============================================================================
 * stations/breakout/reactions/twist-node.js - the Node twist's event visuals.
 * Registered through reactions.js; signature (fx, d, snapshot) => void.
 *
 * Cosmetic only. Grey is payload-free (fx.colour). Reduced motion keeps the
 * rings, because a ring is where the information is, and drops every burst,
 * spray, kick and flash.
 * ==========================================================================*/

/** The Hive's green, and the pale the current leaves behind. */
const LIVE = [80, 226, 150];
const DARK = [104, 100, 126];

/** A spark crawls ALONG the cable, so a pop throws it both ways down the run, not upward. */
const ALONG = [0, Math.PI];
const SPREAD = 0.7;

export const REACTIONS = {
  /**
   * NODECUT: a branch lost the current. One grey ring over the middle of the
   * dark group, and a little ash falling out of it. A big group says so.
   */
  nodeCut(fx, d) {
    if (!fx.colour) return;
    const n = Math.max(1, Number(d && d.n) || 1);
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 8, r1: 34 + Math.min(70, n * 6), life: 0.44, rgb: DARK });
    if (n >= 4) fx.stamps.push({ kind: 'text', text: 'LIGHTS OUT', x: d.x, y: d.y - 18, life: 0.8, rgb: fx.colours.GREY, size: 15 });
    if (fx.reduced) return;
    if (fx.rungs(3)) fx.P.burst(d.x, d.y, DARK, Math.min(18, 4 + n * 2), 120, 0.7, { rise: 10, gv: 300, r0: 1.4, r1: 2.2 });
    fx.aberr(0.12);
  },

  /**
   * NODEZAP: one wire in the wave letting go. A pinprick ring and a spark crawl
   * along the cable. They come 60 ms apart, so each one stays cheap.
   */
  nodeZap(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 2, r1: 20, life: 0.18, rgb: LIVE });
    if (fx.reduced || !fx.rungs(3)) return;
    for (const way of ALONG) {
      fx.P.spray(d.x, d.y, way, SPREAD, LIVE, 4, 200, 0.26, { gv: 140 });
      fx.P.spray(d.x, d.y, way, 0.4, fx.colours.WHITE, 1, 260, 0.15, { gv: 40 });
    }
  },

  /**
   * COREDOWN: the heart is out and the whole net is about to go with it. The one
   * big moment on this board: a shockwave, a white flash and a green ring that
   * runs ahead of the wave the sim is already scheduling.
   */
  coreDown(fx, d, s) {
    if (!fx.colour) return;
    const n = Math.max(1, Number(d && d.n) || 1);
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 12, r1: 90 + Math.min(120, n * 5), life: 0.7, rgb: LIVE });
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 4, r1: 60, life: 0.36, rgb: fx.colours.WHITE });
    if (fx.reduced) return;
    fx.shockwaves.push({ x: d.x, y: d.y, at: 0, life: 0.7 });
    fx.flash(0.2);
    fx.cam.kick(7, 0.5, 0.01);
    if (!fx.rungs(3)) return;
    fx.P.burst(d.x, d.y, LIVE, 30, 300, 0.85, { rise: 40, gv: 240 });
    fx.P.burst(d.x, d.y, fx.colours.WHITE, 12, 200, 0.6, { rise: 70, gv: 120, r0: 1.4, r1: 2.4 });
    fx.P.rects(d.x, d.y, LIVE, 6, { speed: 220, life: 0.6, size: 4 });
    fx.P.after(0.12, () => fx.P.burst(d.x, d.y, DARK, 16, 160, 0.9, { rise: 0, gv: 320, r0: 1.4, r1: 2.2 }));
  },
};

export default REACTIONS;
