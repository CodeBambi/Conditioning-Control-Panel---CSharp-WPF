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
/** The two multiball copy colours (feedback.js BALL_TINTS trails). */
const SPLIT_RGB = [[255, 140, 210], [130, 215, 255]];

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
    for (const sign of [-1, 1]) {
      fx.P.spray(d.x + sign * half, d.y, UP, 0.9, KIND_RGB.laser, 5, 240, 0.24, { gv: 0 });
      fx.P.spray(d.x + sign * half, d.y, UP, 0.5, fx.colours.WHITE, 2, 300, 0.16, { gv: 0 });
    }
  },
  /** The bolt lands: sparks thrown back down the way it came, and a pinprick ring. */
  laserHit(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 3, r1: 24, life: 0.2, rgb: KIND_RGB.laser });
    if (fx.reduced || !fx.rungs(3)) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 1, r1: 12, life: 0.12, rgb: fx.colours.WHITE });
    fx.P.spray(d.x, d.y, DOWN, 2.1, KIND_RGB.laser, 8, 210, 0.3, { gv: 240 });
    fx.P.spray(d.x, d.y, DOWN, 1.3, fx.colours.WHITE, 3, 150, 0.2, { gv: 120 });
    fx.P.rects(d.x, d.y, KIND_RGB.laser, 2, { speed: 120, life: 0.35, size: 3 });
  },
  /** Multiball: the ball bursts into three. A small pop where it split, in the ball's colour and both copy colours, and a small kick. */
  multiSplit(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 6, r1: 70, life: 0.38, rgb: KIND_RGB.multiball });
    if (fx.reduced) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 3, r1: 40, life: 0.24, rgb: fx.colours.WHITE });
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 2, r1: 52, life: 0.3, rgb: SPLIT_RGB[1] });
    if (fx.rungs(3)) {
      fx.P.burst(d.x, d.y, KIND_RGB.multiball, 16, 260, 0.55, { rise: 0, gv: 120 });
      for (const rgb of SPLIT_RGB) fx.P.burst(d.x, d.y, rgb, 10, 200, 0.5, { rise: 0, gv: 120, r0: 1.4, r1: 2.2 });
      fx.P.burst(d.x, d.y, fx.colours.WHITE, 8, 320, 0.3, { rise: 0, gv: 0, r0: 1, r1: 1.6 });
    }
    fx.cam.kick(4, 0.5, 0.008); fx.flash(0.14); fx.aberr(0.5);
  },
  /** The shield catches a ball: light runs out along the line from where it landed. */
  powerSave(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 6, r1: 64, life: 0.35, rgb: KIND_RGB.shield });
    if (fx.reduced || !fx.rungs(3)) return;
    for (const ang of [0, Math.PI]) fx.P.spray(d.x, d.y, ang, 0.35, KIND_RGB.shield, 9, 420, 0.35, { gv: 0 });
    fx.P.spray(d.x, d.y, UP, 1.6, fx.colours.WHITE, 8, 220, 0.4, { gv: 260 });
    fx.cam.kick(3, 0.4, 0.006); fx.flash(0.1);
  },
  /** A brick broken by a burning ball goes up in embers. */
  brick(fx, d, s) {
    if (!fx.colour || fx.reduced || !fx.rungs(3) || !(s && s.power && s.power.fireball > 0)) return;
    fx.P.burst(d.x, d.y, KIND_RGB.fireball, 9, 170, 0.55, { rise: 120, gv: -60, r0: 1, r1: 2 });
    fx.P.burst(d.x, d.y, [255, 106, 61], 5, 110, 0.45, { rise: 90, gv: -40, r0: 1.4, r1: 2.4 });
  },
  /** A power ends: its ring closes back onto the paddle. No burst, no kick: an ending settles. */
  powerExpire(fx, d, s) {
    if (!fx.colour || !KIND_RGB[d.kind] || !s || !s.paddle) return;
    fx.stamps.push({ kind: 'ring', x: s.paddle.x, y: s.paddle.y, r0: 56, r1: 8, life: 0.4, rgb: KIND_RGB[d.kind] });
  },
};
