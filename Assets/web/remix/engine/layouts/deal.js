/* ============================================================================
 * layouts/deal.js - Deal.
 *
 * A deck sits in the corner. The cards are dealt out into a fan one by one,
 * the fan holds, then they are gathered back in reverse.
 *
 * The loop: the deck is whole at frame 0 and whole again before the loop ends,
 * and gathering in reverse rebuilds the same stack order, so nothing pops.
 * Dealing takes the first third, the fan holds, the gather takes the last.
 * Only the card in flight moves: out on a five frame arc, back on a short one.
 * A card on the deck sits at z = n - 1 - k and a card in flight at z = n + k,
 * so whatever is flying is always over the deck.
 *
 * From the seed: the jitter that keeps the deck from looking printed.
 * ==========================================================================*/

import { easeOut, lerp } from '../rects.js';
import { V, VW, VH, rngFor } from './util.js';

const NAME = 'Deal';
const OUT = 5;    // frames a card takes to reach the fan
const ARC = 30;   // virtual px a card lifts on the way, either way
const CARD = 0.42; // card height, as a share of the short side

/** rects.js carries the other two easings; the gather wants this one. */
function easeIn(u) {
  return 1 - easeOut(1 - u);
}

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const rng = rngFor(cfg, 'deal');
  const ch = VH * CARD;
  const cw = ch * 1.33;
  const deck = { x: VW * 0.17, y: VH * 0.68 };

  const jitter = [];
  for (let k = 0; k < n; k++) {
    jitter.push({
      dx: (rng.next() * 2 - 1) * 3,
      dy: (rng.next() * 2 - 1) * 3,
      rot: (rng.next() * 2 - 1) * 0.06,
    });
  }

  // the fan: a shallow arc across the frame, leaning out from the middle, and
  // carrying the card's own jitter so the fan reads dealt rather than printed
  const fan = [];
  for (let k = 0; k < n; k++) {
    const t = (k + 0.5) / n;
    fan.push({
      x: VW * (0.14 + 0.72 * t),
      y: VH * (0.46 - 0.16 * Math.sin(t * Math.PI)),
      rot: (t - 0.5) * 0.7 + jitter[k].rot,
    });
  }

  const back = Math.max(3, Math.min(5, Math.floor(F * 0.07)));
  const outSpan = Math.round(F * 0.34);
  const gatherAt = Math.round(F * 0.62);
  const gatherSpan = Math.round(F * 0.3);
  const out = [];

  for (let k = 0; k < n; k++) {
    const dealt = Math.round(k * outSpan / n);
    // gathered in reverse, so the last card dealt is the first one taken back
    const taken = gatherAt + Math.round((n - 1 - k) * gatherSpan / n);
    const d = { x: deck.x + jitter[k].dx, y: deck.y + jitter[k].dy, rot: jitter[k].rot };
    const f = fan[k];
    let x, y, rot, z, p;

    if (frame < dealt) {
      x = d.x; y = d.y; rot = d.rot; z = n - 1 - k;
    } else if (frame < dealt + OUT) {
      p = easeOut((frame - dealt) / OUT);
      x = lerp(d.x, f.x, p);
      y = lerp(d.y, f.y, p) - ARC * Math.sin(p * Math.PI);
      rot = lerp(d.rot, f.rot, p); z = n + k;
    } else if (frame < taken) {
      x = f.x; y = f.y; rot = f.rot; z = n + k;
    } else if (frame < taken + back) {
      p = easeIn((frame - taken) / back);
      x = lerp(f.x, d.x, p);
      y = lerp(f.y, d.y, p) - ARC * Math.sin(p * Math.PI);
      rot = lerp(f.rot, d.rot, p); z = n + k;
    } else {
      x = d.x; y = d.y; rot = d.rot; z = n - 1 - k;
    }
    out.push(V(cfg, x - cw / 2, y - ch / 2, cw, ch,
      { g: k, z, rot, plate: true, gap: false }));
  }
  return out;
}

export const deal = {
  backdrop: true,   // sparse on the canvas; a soft copy of the first gif fills the ground
  id: 'deal',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'cheap',
  rectsAt,
};

export default deal;
