/* ============================================================================
 * THE ANNEX CHARTS. A figure is DATA plus a renderer, never a picture: the
 * six documents that talk in numbers hand this file {type, x, y} and get an
 * <svg> back. No png, no canvas, no innerHTML, no animation, no fetch.
 *
 * Two palettes, because the same figure has to read in two materials: the
 * OS draws it in phosphor (cold green on the terminal's own black, amber for
 * the one marked week), and paper draws it in pen (blue ink on a paper tone).
 * Wave 2's stapled strip is the SAME call with palette:'pen' - which is the
 * whole reason the colours live here and not in the caller.
 *
 * Laws inherited from the room:
 * - A figure on a screen is an ARCHIVE figure. Nothing here reads a live
 *   number; the caller hands over a frozen row and the caption says so.
 * - Built with createElementNS, element by element (no innerHTML anywhere in
 *   this bundle).
 * - Nothing moves. Trap 36 has no purchase on a sheet that never re-rasters.
 * ==========================================================================*/

const NS = 'http://www.w3.org/2000/svg';

/** The two inks. Phosphor is sampled off os.css / cams.css, pen off the
 *  paper plate in the pitch (--pen #2B4C8C on --paper2). */
const PALETTES = Object.freeze({
  phosphor: Object.freeze({
    bg: '#0A1416', axis: '#1E4A44', band: '#1E4A44',
    ink: '#8FE0CE', mark: '#D9A66A', label: '#8FA39D',
  }),
  pen: Object.freeze({
    bg: '#F4F2E6', axis: '#8A8F92', band: '#B4B09A',
    ink: '#2B4C8C', mark: '#A0522D', label: '#6E6C58',
  }),
});

/* One viewBox for every figure, so a row of them stacks evenly in the
 * sidecar and a stapled strip keeps the same proportions on paper. */
const VB_W = 180;
const VB_H = 80;
const PAD = Object.freeze({ l: 20, r: 8, t: 10, b: 14 });
const INSET = 6;          /* keeps the first and last mark off the axis      */

function el(doc, tag, attrs) {
  const n = doc.createElementNS(NS, tag);
  const keys = Object.keys(attrs || {});
  for (let i = 0; i < keys.length; i++) {
    const v = attrs[keys[i]];
    if (v != null) n.setAttribute(keys[i], String(v));
  }
  return n;
}

function txt(doc, s, attrs) {
  const n = el(doc, 'text', Object.assign({
    'font-size': 6.5, 'font-family': 'monospace',
  }, attrs || {}));
  n.textContent = String(s == null ? '' : s);
  return n;
}

function r2(n) { return Math.round(n * 100) / 100; }

function nums(a) {
  const out = [];
  if (!Array.isArray(a)) return out;
  for (let i = 0; i < a.length; i++) {
    const v = Number(a[i]);
    out.push(isFinite(v) ? v : 0);
  }
  return out;
}

/** The label under an axis end (or over a mark): the caller's own xlabels
 *  first, then the raw x row. Never invented, never unit-suffixed here. */
function xLabel(spec, i) {
  const ls = Array.isArray(spec.xlabels) ? spec.xlabels : null;
  if (ls && ls[i] != null) return String(ls[i]);
  const x = Array.isArray(spec.x) ? spec.x : [];
  return x[i] == null ? '' : String(x[i]);
}

/**
 * renderChart(spec, opts) -> <svg>
 *   spec: { type:'line'|'step'|'bars', x:[], y:[], mark?:index,
 *           band?:[lo,hi], xlabels?:[], markLabel?:string }
 *   opts: { palette:'phosphor'|'pen', caption:string, doc:Document }
 * A malformed row draws an empty frame rather than throwing: a figure is
 * decoration on top of a document, and a bad number may not cost the reader
 * the paragraph it belongs to.
 */
export function renderChart(spec, opts) {
  const o = opts || {};
  const doc = o.doc || document;
  const s = spec || {};
  const pal = PALETTES[o.palette] || PALETTES.phosphor;
  const type = s.type === 'bars' || s.type === 'step' ? s.type : 'line';
  const y = nums(s.y);
  const n = y.length;

  const svg = el(doc, 'svg', {
    viewBox: '0 0 ' + VB_W + ' ' + VB_H,
    class: 'aoc-chart aoc-' + (PALETTES[o.palette] ? o.palette : 'phosphor'),
    role: 'img',
    'aria-label': String(o.caption || s.caption || 'figure'),
  });
  /* self-sufficient sizing: the sheet that owns the caller may not be linked
   * (the paper layer has its own), so the box measures itself */
  try {
    svg.style.display = 'block';
    svg.style.width = '100%';
    svg.style.height = 'auto';
  } catch (e) { /* an svg with no style object is still a chart */ }

  svg.appendChild(el(doc, 'rect', { width: VB_W, height: VB_H, fill: pal.bg }));

  const px0 = PAD.l;
  const px1 = VB_W - PAD.r;
  const py0 = PAD.t;
  const py1 = VB_H - PAD.b;

  /* axes first, so every stroke lands on top of them */
  const axis = el(doc, 'g', { stroke: pal.axis, 'stroke-width': 1, fill: 'none' });
  axis.appendChild(el(doc, 'line', { x1: px0, y1: py0, x2: px0, y2: py1 }));
  axis.appendChild(el(doc, 'line', { x1: px0, y1: py1, x2: px1, y2: py1 }));
  svg.appendChild(axis);

  if (!n) return svg;

  /* the vertical scale takes the band in too, or a band drawn off the top
   * of the plot is a line the reader never sees */
  const band = Array.isArray(s.band) && s.band.length === 2 ? nums(s.band) : null;
  let lo = y[0];
  let hi = y[0];
  for (let i = 1; i < n; i++) { if (y[i] < lo) lo = y[i]; if (y[i] > hi) hi = y[i]; }
  if (band) {
    lo = Math.min(lo, band[0], band[1]);
    hi = Math.max(hi, band[0], band[1]);
  }
  if (hi - lo < 1e-6) { hi = lo + 1; lo = lo - 1; }
  if (type === 'bars') {
    /* A BAR STARTS AT ZERO. A floated baseline draws four in a hundred as a
     * sliver and thirty four as a tower of six times the ink, which is the
     * one kind of lying a figure in this room is not allowed to do. */
    lo = Math.min(0, lo);
    hi += (hi - lo) * 0.10;
  } else {
    const head = (hi - lo) * 0.12;
    lo -= head;
    hi += head;
  }

  const yAt = (v) => r2(py1 - ((v - lo) / (hi - lo)) * (py1 - py0));

  /* bars own the whole span in slots; a line hangs its points inside it */
  const span = px1 - px0;
  const slot = span / n;
  const xLine = (i) => r2(px0 + INSET + (n === 1 ? (span - INSET * 2) / 2
    : (i * (span - INSET * 2)) / (n - 1)));
  const xBar = (i) => r2(px0 + slot * i + slot * 0.225);
  const barW = r2(slot * 0.55);

  if (band) {
    const g = el(doc, 'g', {
      stroke: pal.band, 'stroke-width': 1, 'stroke-dasharray': '2 3', fill: 'none',
    });
    [band[0], band[1]].forEach((v) => {
      g.appendChild(el(doc, 'line', { x1: px0, y1: yAt(v), x2: px1, y2: yAt(v) }));
    });
    svg.appendChild(g);
  }

  if (type === 'bars') {
    const g = el(doc, 'g', { fill: pal.ink });
    for (let i = 0; i < n; i++) {
      const top = yAt(y[i]);
      g.appendChild(el(doc, 'rect', {
        x: xBar(i), y: top, width: barW, height: r2(Math.max(0.5, py1 - top)),
      }));
    }
    svg.appendChild(g);
  } else {
    const pts = [];
    for (let i = 0; i < n; i++) {
      if (type === 'step' && i > 0) pts.push(xLine(i) + ',' + yAt(y[i - 1]));
      pts.push(xLine(i) + ',' + yAt(y[i]));
    }
    svg.appendChild(el(doc, 'polyline', {
      fill: 'none', stroke: pal.ink, 'stroke-width': 2,
      'stroke-linejoin': 'round', 'stroke-linecap': 'round',
      points: pts.join(' '),
    }));
  }

  /* the mark: one week, one dot, one word. The amber is the only warm pixel
   * a figure is allowed, and it is the reason the figure exists. */
  const m = typeof s.mark === 'number' && s.mark >= 0 && s.mark < n ? Math.floor(s.mark) : -1;
  if (m >= 0) {
    const mx = type === 'bars' ? r2(xBar(m) + barW / 2) : xLine(m);
    const my = yAt(y[m]);
    svg.appendChild(el(doc, 'circle', { cx: mx, cy: my, r: 3, fill: pal.mark }));
    const label = s.markLabel != null ? String(s.markLabel) : xLabel(s, m);
    if (label) {
      svg.appendChild(txt(doc, label, {
        x: r2(Math.max(px0 + 10, Math.min(px1 - 10, mx))),
        y: r2(Math.max(py0 + 6, my - 6)),
        'font-size': 7, 'text-anchor': 'middle', fill: pal.mark,
      }));
    }
  }

  /* axis labels: unit-less, first and last only, or one per bar when the
   * caller wrote them and the bars are few enough to read */
  const perBar = type === 'bars' && Array.isArray(s.xlabels) && n <= 8;
  if (perBar) {
    for (let i = 0; i < n; i++) {
      svg.appendChild(txt(doc, xLabel(s, i), {
        x: r2(xBar(i) + barW / 2), y: VB_H - 4,
        'text-anchor': 'middle', fill: pal.label,
      }));
    }
  } else {
    const first = xLabel(s, 0);
    const last = xLabel(s, n - 1);
    if (first) svg.appendChild(txt(doc, first, { x: px0 + 2, y: VB_H - 4, fill: pal.label }));
    if (last && n > 1) {
      svg.appendChild(txt(doc, last, {
        x: px1, y: VB_H - 4, 'text-anchor': 'end', fill: pal.label,
      }));
    }
  }

  return svg;
}

export default renderChart;
