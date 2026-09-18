/* ============================================================================
 * glyphs.js - THE POCKET GLYPHS (GLYPHS.md), PURE but for paintGlyph's canvas.
 *
 * Four faded marks on the wheel's inner slope, one effect each, so the player
 * can read what a pocket will do before the ball lands. The key is the pocket
 * NUMBER, page-side only: glyphFor(n) = GLYPH_RING[(n - 1) % 4] for 1..36 and
 * null for the house pocket 0. It reads nothing from the server, the tape, the
 * bet or the pay (Law I: the ring is the same every spin, nothing brightens
 * before the ball is in the server's pocket). feel.fxPlan('glyph', { pocket })
 * turns the mark into its section 4 id; bowl-3d.js paints and lights it.
 * ==========================================================================*/

export const GLYPHS = Object.freeze({
  spiral: Object.freeze({ fx: 'fx.spiral_brief' }),   // it spins you
  eye: Object.freeze({ fx: 'fx.gif_burst' }),         // it shows you pictures
  bubble: Object.freeze({ fx: 'fx.sub_pair' }),       // it puts words in you
  drop: Object.freeze({ fx: 'fx.melt' }),             // it melts you
});
export const GLYPH_RING = Object.freeze(['spiral', 'eye', 'bubble', 'drop']);
export const GLYPH_IDS = Object.freeze(Object.keys(GLYPHS));

/** The glyph on pocket `pocket` (a number 1..36), null for 0 or anything else. */
export function glyphFor(pocket) {
  const n = Number(pocket);
  if (!Number.isInteger(n) || n < 1 || n > 36) return null;
  return GLYPH_RING[(n - 1) % GLYPH_RING.length];
}
/** The section 4 id a landing on `pocket` fires, null when the pocket has no glyph. */
export function glyphFx(pocket) {
  const id = glyphFor(pocket);
  return id ? GLYPHS[id].fx : null;
}

/* THE CONTOUR. The pockets alternate hot pink and near-black, so a cream engraving at 26% (GLYPHS.md) had a
 * bright half of the wheel to disappear into: on a phone the marks read as smudges (owner, 2026-09-16). The
 * shape is now traced TWICE, a dark line first and the cream over it, which buys the mark an edge against both
 * halves without touching the authored 26% or the cream itself. */
const CONTOUR = '#1b0d27', CONTOUR_SCALE = 2.3;

/**
 * Paint glyph `id` on a transparent `size` x `size` box of `g` (a 2d context): a dark contour under a cream
 * mark. Strokes only, thick and round, so it reads at 14 px on a phone and takes its tint from the material.
 */
export function paintGlyph(g, id, size = 128) {
  const s = size, c = s / 2, w = s * 0.085;
  if (!GLYPH_IDS.includes(id)) return false;
  g.clearRect(0, 0, s, s);
  g.lineCap = g.lineJoin = 'round';
  g.strokeStyle = g.fillStyle = CONTOUR; g.lineWidth = w * CONTOUR_SCALE;
  trace(g, id, s, c);
  g.strokeStyle = g.fillStyle = '#ffffff'; g.lineWidth = w;
  return trace(g, id, s, c);
}

/** The shape alone, on whatever line the caller has set. Run once per pass. */
function trace(g, id, s, c) {
  g.beginPath();
  if (id === 'spiral') {
    const turns = 2.6, steps = 72;
    for (let k = 0; k <= steps; k++) {
      const t = k / steps, a = t * turns * Math.PI * 2, r = s * 0.06 + t * s * 0.36;
      const x = c + Math.cos(a) * r, y = c + Math.sin(a) * r;
      if (k === 0) g.moveTo(x, y); else g.lineTo(x, y);
    }
    g.stroke();
  } else if (id === 'eye') {
    const rx = s * 0.4, ry = s * 0.24;
    g.moveTo(c - rx, c); g.quadraticCurveTo(c, c - ry * 2.2, c + rx, c); g.quadraticCurveTo(c, c + ry * 2.2, c - rx, c);
    g.stroke();
    g.beginPath(); g.arc(c, c, s * 0.11, 0, Math.PI * 2); g.fill();
  } else if (id === 'bubble') {
    const x0 = s * 0.14, y0 = s * 0.2, x1 = s * 0.86, y1 = s * 0.66, r = s * 0.14;
    g.moveTo(x0 + r, y0); g.lineTo(x1 - r, y0); g.quadraticCurveTo(x1, y0, x1, y0 + r); g.lineTo(x1, y1 - r);
    g.quadraticCurveTo(x1, y1, x1 - r, y1); g.lineTo(s * 0.42, y1); g.lineTo(s * 0.26, s * 0.86); g.lineTo(s * 0.28, y1);
    g.lineTo(x0 + r, y1); g.quadraticCurveTo(x0, y1, x0, y1 - r); g.lineTo(x0, y0 + r); g.quadraticCurveTo(x0, y0, x0 + r, y0);
    g.closePath(); g.stroke();
    for (const dx of [-0.18, 0, 0.18]) { g.beginPath(); g.arc(c + dx * s, (y0 + y1) / 2, s * 0.05, 0, Math.PI * 2); g.fill(); }
  } else if (id === 'drop') {
    const top = s * 0.1, r = s * 0.24, cy = s * 0.64;
    g.moveTo(c, top);
    g.bezierCurveTo(c + r * 0.4, cy - r * 1.3, c + r, cy - r * 0.6, c + r, cy);
    g.arc(c, cy, r, 0, Math.PI, false);
    g.bezierCurveTo(c - r, cy - r * 0.6, c - r * 0.4, cy - r * 1.3, c, top);
    g.closePath(); g.stroke();
  } else return false;
  return true;
}
