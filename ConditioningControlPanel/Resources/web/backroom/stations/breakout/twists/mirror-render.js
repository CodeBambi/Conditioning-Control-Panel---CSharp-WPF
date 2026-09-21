/* ============================================================================
 * stations/breakout/twists/mirror-render.js - the twist's own drawing.
 *
 * MIRROR (The Wardrobe). Two things, and only two:
 *   THE SEAM, under the bricks: a shimmer down the fold the wall is folded on,
 *     so the mirror reads before the first hit and without a word of text.
 *   THE BEAM, over everything: a white line to the twin with a spark running
 *     along it, and the twin pops as the spark lands. The eye is told why.
 *
 * REDUCED MOTION keeps both, still: the seam stops travelling and the spark is
 * gone, but the fold and the line are exactly as visible (twists/CONTRACT.md 5).
 * ==========================================================================*/

import { TWIN_DELAY } from './mirror.js';

/** The seam's half width, and how far past the wall it runs. */
const SEAM_HALF = 15, SEAM_PAD = 34;
/** The seam's travelling glints: how many, how long, how fast down the fold. */
const GLINTS = 3, GLINT_LEN = 52, GLINT_SPEED = 96;

/** The fold: the board is centred, so its mirror line is the middle of the brick band. */
function fold(snap) {
  let lo = Infinity, hi = -Infinity, top = Infinity, bottom = -Infinity;
  for (const br of snap.bricks || []) {
    if (br.x < lo) lo = br.x;
    if (br.x + br.w > hi) hi = br.x + br.w;
    if (br.y < top) top = br.y;
    if (br.y + br.h > bottom) bottom = br.y + br.h;
  }
  if (!Number.isFinite(lo)) return { x: snap.w / 2, top: 0, bottom: snap.h * 0.6 };
  return { x: (lo + hi) / 2, top: Math.max(0, top - SEAM_PAD), bottom: bottom + SEAM_PAD };
}

/** Under the bricks: the fold itself. A soft band, a dashed line, and glints that drift down it. */
export function under(ctx2d, snap, t) {
  const st = snap.mirror;
  const f = fold(snap);
  const flare = st ? Math.max(0, Math.min(1, st.seam)) : 0;
  const h = f.bottom - f.top;
  if (h <= 0) return;

  // The band: the wall's own fold, barely there, a little brighter for a moment after a pop.
  const band = ctx2d.createLinearGradient(f.x - SEAM_HALF, 0, f.x + SEAM_HALF, 0);
  const peak = 0.07 + 0.1 * flare;
  band.addColorStop(0, 'rgba(255,255,255,0)');
  band.addColorStop(0.5, 'rgba(255,255,255,' + peak.toFixed(3) + ')');
  band.addColorStop(1, 'rgba(255,255,255,0)');
  ctx2d.fillStyle = band;
  ctx2d.fillRect(f.x - SEAM_HALF, f.top, SEAM_HALF * 2, h);

  // The line: a dashed hairline down the middle, crawling unless motion is off.
  ctx2d.strokeStyle = 'rgba(255,255,255,' + (0.26 + 0.34 * flare).toFixed(3) + ')';
  ctx2d.lineWidth = 1.5;
  ctx2d.setLineDash([5, 9]);
  ctx2d.lineDashOffset = snap.reduced ? 0 : -(t * 22) % 14;
  ctx2d.beginPath();
  ctx2d.moveTo(f.x, f.top);
  ctx2d.lineTo(f.x, f.bottom);
  ctx2d.stroke();
  ctx2d.setLineDash([]);

  if (snap.reduced) return;
  // The glints: short bright runs down the fold, so the seam is alive without asking for attention.
  for (let i = 0; i < GLINTS; i++) {
    const span = h + GLINT_LEN;
    const y = f.top - GLINT_LEN + (((t * GLINT_SPEED) + (i * span) / GLINTS) % span);
    const glint = ctx2d.createLinearGradient(0, y, 0, y + GLINT_LEN);
    glint.addColorStop(0, 'rgba(255,255,255,0)');
    glint.addColorStop(0.5, 'rgba(255,255,255,0.38)');
    glint.addColorStop(1, 'rgba(255,255,255,0)');
    ctx2d.fillStyle = glint;
    ctx2d.fillRect(f.x - 1, Math.max(f.top, y), 2, Math.min(GLINT_LEN, f.bottom - Math.max(f.top, y)));
  }
}

/** Over everything: the beam to the twin, and the spark that arrives as the twin goes. */
export function over(ctx2d, snap, t) {
  const st = snap.mirror;
  if (!st || !st.beams || !st.beams.length) return;
  ctx2d.lineCap = 'round';
  for (const beam of st.beams) {
    const k = Math.max(0, Math.min(1, 1 - beam.age / beam.life));
    if (k <= 0) continue;
    const a = snap.reduced ? k : Math.min(1, k * 1.8);     // holds full for the first half, then fades
    const mx = (beam.x1 + beam.x2) / 2, my = (beam.y1 + beam.y2) / 2;
    const dx = beam.x2 - beam.x1, dy = beam.y2 - beam.y1;
    // The bow is across the line, so two beams in one row are two lines and not one.
    const bow = snap.reduced ? 0 : beam.bow;
    const cx = mx - dy * bow, cy = my + dx * bow;

    ctx2d.beginPath();
    ctx2d.moveTo(beam.x1, beam.y1);
    ctx2d.quadraticCurveTo(cx, cy, beam.x2, beam.y2);
    ctx2d.strokeStyle = 'rgba(255,255,255,' + (0.22 * a).toFixed(3) + ')';
    ctx2d.lineWidth = 12;
    ctx2d.stroke();
    ctx2d.strokeStyle = 'rgba(255,255,255,' + (0.95 * a).toFixed(3) + ')';
    ctx2d.lineWidth = 3;
    ctx2d.stroke();

    if (snap.reduced) continue;
    // The spark: it leaves the brick that broke and lands as the twin goes.
    const run = Math.max(0, Math.min(1, beam.age / TWIN_DELAY));
    const u = 1 - run;
    const sx = u * u * beam.x1 + 2 * u * run * cx + run * run * beam.x2;
    const sy = u * u * beam.y1 + 2 * u * run * cy + run * run * beam.y2;
    const spark = (run < 1 ? 1 : a) * (beam.prize ? 1 : 0.8);
    ctx2d.fillStyle = 'rgba(255,255,255,' + spark.toFixed(3) + ')';
    ctx2d.beginPath();
    ctx2d.arc(sx, sy, 2.4 + 1.6 * (1 - run), 0, Math.PI * 2);
    ctx2d.fill();
  }
}

export default { under, over };
