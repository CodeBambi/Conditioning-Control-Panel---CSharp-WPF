/* ============================================================================
 * Power-up reactions: the catch in the kind's colour, the laser's muzzle and
 * impact sparks, a ring when a drop leaves its brick and when a power ends.
 * Cosmetic only. Grey gets nothing; reduced motion keeps the rings (they carry
 * the information) and drops every burst, kick and flash.
 * ==========================================================================*/

/** The pickup colours of powerups-render.js COLORS, as the rgb triples the particle pool wants. */
export const KIND_RGB = { multiball: [198, 147, 255], fireball: [255, 196, 109], laser: [255, 143, 206], shield: [133, 245, 219] };
const LABEL = { multiball: 'MULTI', fireball: 'FIRE', laser: 'LASER', shield: 'SHIELD' };
const UP = -Math.PI / 2, DOWN = Math.PI / 2;

export default {
  powerDrop(fx, d) {
    if (!fx.colour || !KIND_RGB[d.kind]) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 4, r1: 28, life: 0.3, rgb: KIND_RGB[d.kind] });
  },
  powerCatch(fx, d) {
    const rgb = KIND_RGB[d.kind];
    if (!fx.colour || !rgb) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 10, r1: 84, life: 0.42, rgb });
    fx.stamps.push({ kind: 'text', text: LABEL[d.kind], x: d.x, y: d.y - 30, life: 0.8, rgb, size: 17 });
    if (fx.reduced) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 4, r1: 46, life: 0.28, rgb: fx.colours.WHITE });
    if (fx.rungs(3)) {
      fx.P.burst(d.x, d.y, rgb, 18, 210, 0.6, { rise: 110, gv: 220 });
      fx.P.burst(d.x, d.y, fx.colours.WHITE, 7, 130, 0.5, { rise: 140, gv: 90, r0: 1.2, r1: 1.8 });
    }
    fx.cam.kick(3, 0.4, 0.006);
    fx.flash(0.1);
  },
  /** The muzzle: a few sparks straight up from each barrel (the flash itself is drawn from the snapshot). */
  laserShot(fx, d, s) {
    if (!fx.colour || fx.reduced || !fx.rungs(3)) return;
    const half = (s && s.paddle ? s.paddle.w : 160) / 2 - 10;
    for (const sign of [-1, 1]) fx.P.spray(d.x + sign * half, d.y, UP, 0.8, KIND_RGB.laser, 3, 190, 0.2, { gv: 0 });
  },
  /** The bolt lands: sparks thrown back down the way it came, and a pinprick ring. */
  laserHit(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 2, r1: 15, life: 0.18, rgb: KIND_RGB.laser });
    if (fx.reduced || !fx.rungs(3)) return;
    fx.P.spray(d.x, d.y, DOWN, 1.9, KIND_RGB.laser, 4, 170, 0.24, { gv: 240 });
    fx.P.spray(d.x, d.y, DOWN, 1.2, fx.colours.WHITE, 2, 120, 0.16, { gv: 120 });
  },
  /** A power ends: its ring closes back onto the paddle. No burst, no kick: an ending settles. */
  powerExpire(fx, d, s) {
    if (!fx.colour || !KIND_RGB[d.kind] || !s || !s.paddle) return;
    fx.stamps.push({ kind: 'ring', x: s.paddle.x, y: s.paddle.y, r0: 56, r1: 8, life: 0.4, rgb: KIND_RGB[d.kind] });
  },
};
