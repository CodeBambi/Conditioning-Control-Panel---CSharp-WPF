/* ============================================================================
 * mat.js - the Velvet Vortex betting mat and chip stacks on a 2D canvas
 * (CONTRACT 10.13.F). A real mat for the five bet kinds: the 37 straights (zero
 * down the left, 1-36 in three rows of twelve), the three rows of twelve (sip
 * 1-12, sink 13-24, deep 25-36) and the two colours (rose, plum). Spot ids are
 * the table's own (state.spots), so a click is already a bet the server knows.
 *
 * Chips after a landing (the page effects of roulette.land.*):
 *   chip_vortex  a lost chip spirals into the bowl, trailing, 1.8 s
 *   chips_in     winnings slide in from the bank edge onto the winning stack
 *   pulled_pair  a Spiral Wake win: a second pair pulled out of the whirlpool
 * Still (Calm, reduced) takes the settled state: no travel, chips appear or
 * fade where they end.
 * ==========================================================================*/

import { chipLanding, traceProgress } from './juice.js';
import { FEEL } from './feel.js';
import { HIGHLIGHT_MS } from '../../shared/hypno/callout.js';

const TAU = Math.PI * 2;
const COL = { brass: '#e8c27a', mint: '#5fffd0', rose: '#ff5fa2', text: '#efe6ff', muted: '#a898c4', zero: '#1f8f74', roseFelt: '#c8286e', plum: '#3a1f5c', felt: '#1b1230' };
const FONT = 'Segoe UI, Figtree, Arial, sans-serif';
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const ease = (p) => 1 - Math.pow(1 - clamp(p, 0, 1), 3);
const lerp = (a, b, t) => a + (b - a) * t;

/**
 * @param {{spots: string[], rose: number[], label: (spot: string) => string}} o
 */
export function createMat({ spots, rose, label }) {
  const roseSet = new Set((rose || []).map(Number));
  const has = (id) => !Array.isArray(spots) || spots.includes(id);
  let cells = [];   // { spot, x, y, w, h, fill }
  let anims = [];
  const arrivals = new Map(); let traceAt = -Infinity, traceSpot = null;   // { kind: 'in' | 'lose' | 'pull', spot, t0, i }
  let hits = { spots: new Set(), at: -Infinity };   // THE GLYPH HIT: the paying chips' rim glow over HIGHLIGHT_MS (callout.js)
  const box = { x: 0, y: 0, w: 0, h: 0, cell: 0 };
  /** A paying landing: the chips on `spots` glow and pop from station time `now`. */
  function glow(spots, now) { hits = { spots: new Set(Array.isArray(spots) ? spots : []), at: now }; }
  const hitPulse = (spot, now) => { if (!hits.spots.has(spot)) return 0; const q = (now - hits.at) / HIGHLIGHT_MS; return q >= 0 && q < 1 ? Math.sin(q * Math.PI) : 0; };

  /** Lay the mat into x, y, w, h (CSS px); it keeps its proportions and centres itself. */
  function layout(x, y, w, h) {
    const cell = Math.max(14, Math.min(w / 13, h / 5.4));
    const mw = cell * 13, mh = cell * 5.4, ox = x + (w - mw) / 2, oy = y + (h - mh) / 2;
    Object.assign(box, { x: ox, y: oy, w: mw, h: mh, cell });
    cells = [];
    const add = (spot, cx, cy, cw, ch, fill) => { if (has(spot)) cells.push({ spot, x: cx, y: cy, w: cw, h: ch, fill }); };
    add('s0', ox, oy, cell, cell * 3, COL.zero);
    for (let c = 1; c <= 12; c++) {
      for (let r = 0; r < 3; r++) {
        const n = 3 * c - r;
        add('s' + n, ox + c * cell, oy + r * cell, cell, cell, roseSet.has(n) ? COL.roseFelt : COL.plum);
      }
    }
    const y2 = oy + cell * 3.2;
    ['sip', 'sink', 'deep'].forEach((id, k) => add(id, ox + cell * (1 + 4 * k), y2, cell * 4, cell, COL.felt));
    const y3 = y2 + cell * 1.2;
    add('rose', ox + cell, y3, cell * 6, cell, COL.roseFelt);
    add('plum', ox + cell * 7, y3, cell * 6, cell, COL.plum);
  }

  const cellOf = (spot) => cells.find((c) => c.spot === spot) || null;
  /** The spot under a point, or null. */
  function hit(px, py) {
    for (const c of cells) if (px >= c.x && px < c.x + c.w && py >= c.y && py < c.y + c.h) return c.spot;
    return null;
  }
  /** Where a spot's chip stack sits (straights: the cell centre; outside bets: toward the right end). */
  function stackAt(spot) {
    const c = cellOf(spot);
    if (!c) return { x: box.x, y: box.y };
    return c.w > box.cell * 1.5 ? { x: c.x + c.w - box.cell * 0.55, y: c.y + c.h / 2 } : { x: c.x + c.w / 2, y: c.y + c.h / 2 };
  }

  function chip(g, x, y, r, col, alpha) {
    if (alpha <= 0) return;
    g.save(); g.globalAlpha = alpha; g.fillStyle = col; g.shadowColor = 'rgba(0,0,0,.5)'; g.shadowBlur = 6;
    g.beginPath(); g.arc(x, y, r, 0, TAU); g.fill(); g.shadowBlur = 0;
    g.strokeStyle = COL.text; g.setLineDash([3, 3]); g.lineWidth = 1.4; g.beginPath(); g.arc(x, y, r * 0.7, 0, TAU); g.stroke(); g.setLineDash([]);
    g.restore();
  }

  /** Queue the landing's chip moves at station time `now` (ms). */
  function animate(list, now) {
    let i = 0;
    for (const a of list) anims.push({ kind: a.kind, spot: a.spot, t0: now + FEEL.CHIP_DELAY_MS + (a.kind === 'lose' ? 0 : i++ * FEEL.CHIP_STAGGER_MS), i: anims.length });
  }
  function clearAnims() { anims = []; arrivals.clear(); traceAt = -Infinity; }

  /**
   * view = { now, chips {spot: amt}, hover, hits: string[], landed: number | null, k, still, bowl: {cx, cy, R}, locked }
   */
  function draw(g, view) {
    const { now, k } = view, cell = box.cell, chipR = Math.max(6, cell * 0.28);
    const hitsNow = new Set(view.hits || []);
    if (view.still) { arrivals.clear(); traceAt = -Infinity; }
    g.save();
    g.fillStyle = 'rgba(12,7,22,.72)'; g.strokeStyle = 'rgba(232,194,122,.35)'; g.lineWidth = 1;
    g.beginPath(); g.roundRect(box.x - cell * 0.3, box.y - cell * 0.3, box.w + cell * 0.6, box.h + cell * 0.6, cell * 0.3); g.fill(); g.stroke();
    for (const c of cells) {
      const hover = view.hover === c.spot && !view.locked;
      g.globalAlpha = hover ? 1 : 0.86; g.fillStyle = c.fill; g.fillRect(c.x + 1, c.y + 1, c.w - 2, c.h - 2); g.globalAlpha = 1;
      if (hover) { g.fillStyle = 'rgba(255,243,214,.14)'; g.fillRect(c.x + 1, c.y + 1, c.w - 2, c.h - 2); }
      g.strokeStyle = 'rgba(232,194,122,.55)'; g.lineWidth = 1; g.strokeRect(c.x + 0.5, c.y + 0.5, c.w - 1, c.h - 1);
      const landedHere = view.landed != null && c.spot === 's' + view.landed;
      const pulse = hitPulse(c.spot, now);
      if (hitsNow.has(c.spot) || landedHere || pulse > 0) {
        g.save(); g.strokeStyle = COL.mint; g.lineWidth = 2.5 + 1.5 * pulse; g.shadowColor = COL.mint; g.shadowBlur = (12 + 16 * pulse) * k;
        const grow = c.w * 0.03 * pulse;   // the 1.06 pop of the winning frame
        g.strokeRect(c.x + 2 - grow, c.y + 2 - grow, c.w - 4 + grow * 2, c.h - 4 + grow * 2); g.restore();
      }
      const straight = /^s\d+$/.test(c.spot);
      g.fillStyle = COL.text; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.font = `${straight ? 600 : 500} ${Math.max(10, cell * (straight ? 0.36 : 0.3))}px ${FONT}`;
      g.fillText(straight ? c.spot.slice(1) : label(c.spot), c.x + (straight ? c.w / 2 : c.w * 0.42), c.y + c.h / 2);
    }

    // the layout's chips; a chip that is being lost flies instead of sitting
    const flying = new Set(anims.filter((a) => a.kind === 'lose').map((a) => a.spot));
    for (const [spot, amt] of Object.entries(view.chips || {})) {
      if (!(amt > 0) || flying.has(spot)) continue;
      const p = stackAt(spot), pulse = hitPulse(spot, now), r = chipR * (1 + 0.06 * pulse);
      if (pulse > 0) { g.save(); g.globalAlpha = 0.7 * pulse * k; g.fillStyle = COL.mint; g.shadowColor = COL.mint; g.shadowBlur = 20; g.beginPath(); g.arc(p.x, p.y, r * 1.35, 0, TAU); g.fill(); g.restore(); }
      for (let n = 0; n < amt; n++) {
        const at = arrivals.get(spot), motion = chipLanding(at != null && n === amt - 1 ? now - at : -1, view.still);
        chip(g, p.x + motion.tilt * chipR, p.y - n * 3 - chipR * (motion.lift * .6 + (view.still ? 0 : pulse * .65)) * k, r, COL.rose, 1);
      }
      if (amt > 1) { g.fillStyle = COL.text; g.font = `700 ${Math.max(9, chipR)}px ${FONT}`; g.fillText(String(amt), p.x, p.y - (amt - 1) * 3); }
    }

    const bowl = view.bowl || { cx: box.x - cell * 4, cy: box.y + box.h / 2, R: cell * 3 };
    const tq = traceProgress(now - traceAt, view.still);
    if (tq !== null) {
      const to = stackAt(traceSpot);g.fillStyle = COL.mint;g.shadowColor = COL.mint;g.shadowBlur = 8 * k;
      g.beginPath();g.arc(lerp(bowl.cx, to.x, tq), lerp(bowl.cy, to.y, tq) - Math.sin(tq * Math.PI) * cell, Math.max(2, cell * .075), 0, TAU);g.fill();g.shadowBlur = 0;
    }
    const bankX = box.x + box.w + cell * 1.2;
    const stacked = {};
    for (const a of anims) {
      const t = clamp((now - a.t0) / FEEL.CHIP_MS, 0, 1);
      if (now < a.t0) continue;
      const to = stackAt(a.spot), e = ease(t);
      if (a.kind === 'lose') {
        if (view.still) { chip(g, to.x, to.y, chipR, COL.rose, 1 - t); continue; }
        // spirals into the bowl, trailing its travel
        const ang = -Math.PI / 2 + e * TAU * 0.8, r = (1 - e) * bowl.R * 0.75;
        chip(g, lerp(to.x, bowl.cx, e) + Math.cos(ang) * r * e, lerp(to.y, bowl.cy, e) + Math.sin(ang) * r * e, chipR, COL.rose, 1 - e);
        continue;
      }
      const n = (stacked[a.spot] = (stacked[a.spot] || 0) + 1);
      const end = { x: to.x + chipR * 1.1 + n * chipR * 0.55, y: to.y - n * 3 };
      if (view.still) { chip(g, end.x, end.y, chipR, COL.mint, Math.min(1, t * 3)); continue; }
      if (a.kind === 'in') {
        chip(g, lerp(bankX, end.x, e), lerp(to.y, end.y, e), chipR, COL.mint, 1);
      } else {
        // pulled out of the whirlpool: starts wound in the turret and unwinds onto the stack
        const ang = -Math.PI / 2 + (1 - e) * TAU * 1.2, r = (1 - e) * bowl.R * 0.5;
        chip(g, lerp(bowl.cx, end.x, e) + Math.cos(ang) * r, lerp(bowl.cy, end.y, e) + Math.sin(ang) * r, chipR, COL.mint, Math.min(1, t * 3));
      }
    }
    g.restore();
  }

  return {
    layout, hit, glow, draw, animate, clearAnims, stackAt,
    place(spot, now) { arrivals.set(spot, now); },
    trace(pocket, now) { traceSpot = 's' + pocket; traceAt = now; },
    get box() { return { ...box }; },
    /** A spot's cell rect (CSS px), or null. */
    rectOf(spot) { const c = cellOf(spot); return c ? { x: c.x, y: c.y, w: c.w, h: c.h } : null; },
    debug() { return { cells: cells.length, anims: anims.map((a) => a.kind + ':' + a.spot), cell: box.cell, glow: { spots: [...hits.spots], at: hits.at } }; },
  };
}
