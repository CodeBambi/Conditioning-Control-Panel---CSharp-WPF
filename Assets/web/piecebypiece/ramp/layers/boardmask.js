/* ============================================================================
 * layers/boardmask.js - the board stays readable under every layer.
 *
 * Owner, 2026-10-02: "keep the pieces and the grid always visible or we can't
 * play. should be distractions, not you can't play." Every layer in #fx and the
 * front plane is full-screen, so at a full meter the flashes, the veils, the
 * melt wash and the tape all stacked over the pieces.
 *
 * This writes ONE CSS mask on both planes: full strength everywhere except over
 * the board's screen footprint (its eight edge corners, at the floor and at
 * piece height, hulled), where everything is thinned to `inside`. The edge is
 * feathered, so the distraction fades toward the board instead of stopping on
 * a line. The footprint is measured from the camera, re-checked on the ramp's
 * beat and only rewritten when it moved, so a follow-cam or an orbit is
 * tracked without rebuilding the mask every frame.
 *
 * Pure geometry (boardHull, maskSvg) is exported for node; the rest guards
 * every DOM touch.
 * ==========================================================================*/

/** How much of every layer survives over the board, and the feather in px. */
export const BOARD_MASK = Object.freeze({ inside: 0.38, featherPx: 26, pieceHeight: 1.05, moveEps: 3 });

/** Monotone chain convex hull of [{x, y}], counter-clockwise, no duplicates. */
export function hull(points) {
  const p = points.filter((q) => q && Number.isFinite(q.x) && Number.isFinite(q.y))
    .map((q) => ({ x: q.x, y: q.y }))
    .sort((a, b) => a.x - b.x || a.y - b.y);
  if (p.length < 3) return p;
  const cross = (o, a, b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
  const lower = [];
  for (const q of p) { while (lower.length >= 2 && cross(lower[lower.length - 2], lower[lower.length - 1], q) <= 0) lower.pop(); lower.push(q); }
  const upper = [];
  for (let i = p.length - 1; i >= 0; i--) { const q = p[i]; while (upper.length >= 2 && cross(upper[upper.length - 2], upper[upper.length - 1], q) <= 0) upper.pop(); upper.push(q); }
  upper.pop(); lower.pop();
  return lower.concat(upper);
}

/**
 * The board's screen footprint. `project(sq, h)` answers screen px for a square
 * centre. The corner squares' centres sit 3.5 squares from the middle and the
 * board's edge 4, so each corner is pushed out by 8/7 from the centre of its
 * own height layer. Null when the projection is not usable.
 */
export function boardHull(project, pieceHeight = BOARD_MASK.pieceHeight) {
  const corners = ['a1', 'h1', 'h8', 'a8'];
  const pts = [];
  for (const h of [0, pieceHeight]) {
    const layer = corners.map((sq) => { try { return project(sq, h); } catch { return null; } });
    if (layer.some((q) => !q || !Number.isFinite(q.x) || !Number.isFinite(q.y))) return null;
    const cx = layer.reduce((s, q) => s + q.x, 0) / 4;
    const cy = layer.reduce((s, q) => s + q.y, 0) / 4;
    for (const q of layer) pts.push({ x: cx + (q.x - cx) * (8 / 7), y: cy + (q.y - cy) * (8 / 7) });
  }
  const out = hull(pts);
  if (out.length < 3) return null;
  // a degenerate footprint (board off screen, camera mid-reset) is not worth a mask
  let area = 0;
  for (let i = 0; i < out.length; i++) { const a = out[i], b = out[(i + 1) % out.length]; area += a.x * b.y - b.x * a.y; }
  return Math.abs(area) / 2 > 400 ? out : null;
}

const r1 = (v) => Math.round(v * 10) / 10;

/** The mask image: opaque everywhere, `inside` over the polygon, feathered. */
export function maskSvg(poly, w, h, t = BOARD_MASK) {
  const d = 'M' + poly.map((q) => r1(q.x) + ' ' + r1(q.y)).join('L') + 'Z';
  const pad = Math.max(200, t.featherPx * 4);
  const outer = `M${-pad} ${-pad}H${w + pad}V${h + pad}H${-pad}Z`;
  const svg = `<svg xmlns='http://www.w3.org/2000/svg' width='${w}' height='${h}' viewBox='0 0 ${w} ${h}'>`
    + `<filter id='f' x='-20%' y='-20%' width='140%' height='140%'><feGaussianBlur stdDeviation='${t.featherPx}'/></filter>`
    + `<g filter='url(#f)'><path fill='#fff' fill-rule='evenodd' d='${outer}${d}'/>`
    + `<path fill='#fff' fill-opacity='${t.inside}' d='${d}'/></g></svg>`;
  return `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
}

/** Did the footprint move far enough to be worth a new mask? */
export function moved(a, b, eps = BOARD_MASK.moveEps) {
  if (!a || !b || a.length !== b.length) return true;
  for (let i = 0; i < a.length; i++) if (Math.abs(a[i].x - b[i].x) > eps || Math.abs(a[i].y - b[i].y) > eps) return true;
  return false;
}

/** createBoardMask({ board, planes: [el...], tuning }) -> { update, clear, dispose, debug } */
export function createBoardMask({ board, planes = [], tuning = BOARD_MASK } = {}) {
  let last = null;
  let size = '';
  let disposed = false;

  function write(css) {
    for (const el of planes) {
      if (!el || !el.style) continue;
      try {
        el.style.maskImage = css; el.style.webkitMaskImage = css;
        const size2 = css ? '100% 100%' : '';
        el.style.maskSize = size2; el.style.webkitMaskSize = size2;
        el.style.maskRepeat = css ? 'no-repeat' : ''; el.style.webkitMaskRepeat = css ? 'no-repeat' : '';
      } catch { /* the plane went away */ }
    }
  }

  function update() {
    if (disposed || !board || typeof board.projectSquare !== 'function') return false;
    let w = 0, h = 0;
    try { w = Math.round(window.innerWidth); h = Math.round(window.innerHeight); } catch { return false; }
    if (!(w > 0 && h > 0)) return false;
    const poly = boardHull((sq, ht) => board.projectSquare(sq, ht), tuning.pieceHeight);
    const sz = w + 'x' + h;
    if (!poly) { if (last) { last = null; write(''); } return false; }
    if (sz === size && !moved(poly, last, tuning.moveEps)) return false;
    last = poly; size = sz;
    write(maskSvg(poly, w, h, tuning));
    return true;
  }

  function clear() { last = null; size = ''; write(''); }

  return {
    update, clear,
    dispose() { disposed = true; clear(); },
    debug: () => ({ poly: last, size }),
  };
}

export default createBoardMask;
