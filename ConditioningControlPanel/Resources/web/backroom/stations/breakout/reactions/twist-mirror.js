/* ============================================================================
 * stations/breakout/reactions/twist-mirror.js - the twist's event visuals.
 *
 * MIRROR. The beam itself is drawn from the sim (twists/mirror-render.js over),
 * because it has to be exactly as old as the pair. What lands here is the
 * punctuation: a ring where the brick went, a ring waiting where the twin is
 * about to go, and a few sparks thrown across the fold.
 *
 * Cosmetic only. Reduced motion keeps both rings (they carry the pairing) and
 * drops the sparks and the flash. Grey is payload-free and gets nothing.
 * ==========================================================================*/

const WHITE = [255, 255, 255];

export const REACTIONS = {
  /** A pair went: here, there, and the throw between them. */
  mirrorPop(fx, d) {
    if (!fx.colour) return;
    const rgb = (fx.colours && fx.colours.WHITE) || WHITE;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 3, r1: 22, life: 0.22, rgb });
    // The twin has not gone yet. The ring says where, a breath before it does.
    fx.stamps.push({ kind: 'ring', x: d.tx, y: d.ty, r0: 2, r1: 30, life: 0.34, rgb });
    if (fx.reduced || !fx.rungs(3)) return;
    const ang = Math.atan2(d.ty - d.y, d.tx - d.x);
    fx.P.spray(d.x, d.y, ang, 0.5, rgb, 5, 260, 0.26, { gv: 90 });
    fx.P.spray(d.tx, d.ty, ang + Math.PI, 0.5, rgb, 4, 200, 0.24, { gv: 90 });
    if (d.prize) fx.flash(0.06);
  },
};

export default REACTIONS;
