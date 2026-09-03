/* ============================================================================
 * loomSpiral.js - THE LOOM's shared spiral renderer + param schema (Part 2).
 *
 * ONE drawSpiral is used by BOTH the live preview (game/loomStudio.js) and the
 * GIF encoder (engine/loomWorker.js), so what you see spinning in the pane is
 * exactly what lands in the file - no drift between the two.
 *
 * Schema v1 (persisted verbatim in the loom_<slug>.json sidecar so a saved
 * spiral can be re-edited):
 *   { schema: 1, arms: 2-8, turns: 0.5-4, style: 'log'|'arch'|'ribbon',
 *     duty: 0.2-0.8, speed: 1-5, direction: 1|-1, colors: ['#rrggbb' x1-2],
 *     bg: '#rrggbb' }
 *
 * Seamless looping: a spiral with N identical arms repeats every 2π/N of
 * rotation. With 2 alternating colors, rotating one arm span swaps the colors,
 * so the true period is colors.length arm spans when N divides evenly - and a
 * full 2π (N arm spans) when it doesn't (odd arms + 2 colors). loopMs folds
 * that into wall-clock so preview phase and GIF frame phase agree.
 * ==========================================================================*/

export const LOOM_SCHEMA = 1;

const STYLES = ['log', 'arch', 'ribbon'];
/** speed 1-5 -> GIF frame delay in centiseconds (100/70/50/30/20ms). */
const DELAY_CS = { 1: 10, 2: 7, 3: 5, 4: 3, 5: 2 };

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const HEX_RE = /^#[0-9a-f]{6}$/i;
const hex = (v, fb) => (typeof v === 'string' && HEX_RE.test(v) ? v.toLowerCase() : fb);

/** Coerce anything (a stale sidecar, a half-built form) into valid v1 params. */
export function normalizeParams(p) {
  const q = p && typeof p === 'object' ? p : {};
  const colors = Array.isArray(q.colors) ? q.colors.slice(0, 2).map((c) => hex(c, null)).filter(Boolean) : [];
  if (!colors.length) colors.push('#ff69b4');
  return {
    schema: LOOM_SCHEMA,
    arms: clamp(Math.round(q.arms) || 4, 2, 8),
    turns: clamp(+q.turns || 2, 0.5, 4),
    style: STYLES.includes(q.style) ? q.style : 'log',
    duty: clamp(+q.duty || 0.5, 0.2, 0.8),
    speed: clamp(Math.round(q.speed) || 3, 1, 5),
    direction: q.direction === -1 ? -1 : 1,
    colors,
    bg: hex(q.bg, '#14060f'),
  };
}

export function delayCsFor(p) { return DELAY_CS[normalizeParams(p).speed] || 5; }

/** One seamless rotation period in ms (preview phase = (elapsed % loopMs) / loopMs). */
export function loopMs(p, frames = 36) {
  return frames * delayCsFor(p) * 10;
}

/** The symmetry span this loop animates across, in radians: colors.length arm
 *  spans when the color cycle divides the arm count evenly (one arm span just
 *  swaps the colors), else a full turn (odd arms + 2 colors have no sub-2π symmetry). */
export function loopSpanRad(p) {
  const q = normalizeParams(p);
  const n = q.colors.length;
  return (Math.PI * 2 / q.arms) * (q.arms % n === 0 ? n : q.arms);
}

/** Radius easing per style: t in 0..1 (center -> rim) -> radius fraction 0..1. */
function radiusAt(style, t) {
  if (style === 'log') return Math.pow(t, 1.75);      // tight center, opening rim
  if (style === 'arch') return t;                     // archimedean - even bands
  return t * t * (3 - 2 * t);                         // ribbon - smoothstep swell
}

/**
 * Draw one spiral frame. phase 0..1 = position inside the seamless loop.
 * ctx may be a 2D canvas OR OffscreenCanvas context; size is the square edge.
 * Renders arms as filled annular wedge strips (cheap, alias-friendly at any
 * size, no stroke-width tuning) - duty is the lit fraction of each arm band.
 */
export function drawSpiral(ctx, size, p, phase) {
  const q = normalizeParams(p);
  const c = size / 2;
  const maxR = c * 1.45;                       // overdraw the corners so no dead edges
  const rot = q.direction * phase * loopSpanRad(q);
  const armSpan = (Math.PI * 2) / q.arms;
  const litSpan = armSpan * q.duty;
  const twist = q.turns * Math.PI * 2;         // how far a band shears over the radius
  const RINGS = Math.max(48, Math.round(size / 6));   // radial resolution

  ctx.fillStyle = q.bg;
  ctx.fillRect(0, 0, size, size);

  for (let ring = 0; ring < RINGS; ring++) {
    const t0 = ring / RINGS, t1 = (ring + 1) / RINGS;
    const r0 = radiusAt(q.style, t0) * maxR;
    const r1 = radiusAt(q.style, t1) * maxR + 0.75;   // slight overlap: no moiré seams
    if (r1 <= 0.5) continue;
    const shear = twist * ((t0 + t1) / 2);
    for (let arm = 0; arm < q.arms; arm++) {
      const a0 = rot + arm * armSpan + shear;
      ctx.fillStyle = q.colors[arm % q.colors.length];
      ctx.beginPath();
      ctx.arc(c, c, r1, a0, a0 + litSpan, false);
      if (r0 > 0.5) ctx.arc(c, c, r0, a0 + litSpan, a0, true);
      else ctx.lineTo(c, c);
      ctx.closePath();
      ctx.fill();
    }
  }
}
