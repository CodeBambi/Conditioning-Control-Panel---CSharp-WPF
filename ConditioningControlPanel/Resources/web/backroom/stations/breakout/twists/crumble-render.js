/* ============================================================================
 * stations/breakout/twists/crumble-render.js - the twist's own drawing.
 * Twist: Crumble (Pink Fog). Clay is a MATERIAL, not a colour: grain, a sheen
 * band and a bevel, over the durability face render.js already drew. Durability
 * stays brightness (AGENTS.md, "Breakout visual language"), so a three-hit brick
 * is dark and packed and a one-hit brick is pale, glassy and shedding dust.
 * The clay wash follows the door: sugar glass, porcelain, plaster, wax.
 * Reduced motion: the sheen and the dust hold still. State still reads.
 * ==========================================================================*/

/** The clay wash by hits left, as the pitch drew it (mockup-v3 CLAYS). */
const CLAY = { 3: [138, 90, 60], 2: [185, 133, 96], 1: [230, 201, 168] };
/** How much of the wash goes on: the pale rung takes less, so it stays the bright one. */
const WASH = { 3: 0.42, 2: 0.34, 1: 0.24 };
const rgba = (c, a) => 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',' + a + ')';

/** One stable number per brick and index: the grain never crawls between frames. */
function hash(br, i) {
  const n = Math.sin((br.row + 1) * 12.9898 + (br.col + 1) * 78.233 + i * 37.719) * 43758.5453;
  return n - Math.floor(n);
}

function roundRect(x, w, h, r) {
  x.beginPath();
  x.moveTo(-w / 2 + r, -h / 2);
  x.arcTo(w / 2, -h / 2, w / 2, h / 2, r);
  x.arcTo(w / 2, h / 2, -w / 2, h / 2, r);
  x.arcTo(-w / 2, h / 2, -w / 2, -h / 2, r);
  x.arcTo(-w / 2, -h / 2, w / 2, -h / 2, r);
  x.closePath();
}

/** Drawn for a brick the twist flags, inside the brick's own transform (origin = its centre). */
export function brick(x, br, snap, t) {
  if (!br || !br.clay) return;
  const hp = Math.max(1, Math.min(3, br.hp | 0)), w = br.w, h = br.h, r = 5;
  const lit = br.hp <= 1, reduced = !!(snap && snap.reduced);
  const time = Number(t) || 0;

  x.save();
  roundRect(x, w, h, r); x.clip();

  // The material: a clay wash that keeps the face's own brightness, then grain packed into it.
  x.fillStyle = rgba(CLAY[hp], WASH[hp]);
  x.fillRect(-w / 2, -h / 2, w, h);
  x.fillStyle = lit ? 'rgba(255,248,238,.30)' : 'rgba(52,26,10,.26)';
  for (let i = 0; i < 12; i++) {
    const gx = (hash(br, i) - 0.5) * (w - 6), gy = (hash(br, i + 40) - 0.5) * (h - 5);
    const s = 1 + Math.round(hash(br, i + 80) * 1.4);
    x.fillRect(gx, gy, s, s);
  }

  // A sheen band across it: glass at one hit, a dull wax slab at three.
  const slide = reduced ? 0 : Math.sin(time * 0.45 + br.col * 0.7) * w * 0.22;
  x.globalAlpha = lit ? 0.26 : 0.1;
  x.fillStyle = '#fff6ea';
  x.beginPath();
  x.moveTo(-w * 0.34 + slide, h / 2); x.lineTo(-w * 0.1 + slide, -h / 2);
  x.lineTo(w * 0.02 + slide, -h / 2); x.lineTo(-w * 0.22 + slide, h / 2);
  x.closePath(); x.fill();
  x.globalAlpha = 1;

  // The bevel: a lit top edge and a shaded sill, so it reads as a solid block of stuff.
  x.strokeStyle = 'rgba(255,240,224,.34)'; x.lineWidth = 1.4;
  x.beginPath(); x.moveTo(-w / 2 + 2, -h / 2 + 1.4); x.lineTo(w / 2 - 2, -h / 2 + 1.4); x.stroke();
  x.strokeStyle = 'rgba(40,18,6,.38)';
  x.beginPath(); x.moveTo(-w / 2 + 2, h / 2 - 1.4); x.lineTo(w / 2 - 2, h / 2 - 1.4); x.stroke();
  x.restore();

  if (!lit) return;

  // PRECARIOUS. A pale rim breathing on the brick, so "one hit left" reads across the wall.
  const pulse = reduced ? 0.5 : 0.5 + 0.5 * Math.sin(time * 6.1 + br.col * 1.3 + br.row * 0.8);
  x.save();
  x.strokeStyle = 'rgba(255,246,236,' + (0.34 + pulse * 0.34).toFixed(3) + ')';
  x.lineWidth = 1.6;
  roundRect(x, w - 1.6, h - 1.6, r); x.stroke();
  x.restore();

  // And it sheds. Three motes falling out of the sill on their own loops; reduced motion
  // leaves the same three as a static spill, because the shedding is the tell.
  x.save();
  x.fillStyle = 'rgba(224,201,172,.85)';
  for (let i = 0; i < 3; i++) {
    const dx = (hash(br, i + 7) - 0.5) * (w - 10);
    if (reduced) { x.globalAlpha = 0.5 - i * 0.13; x.fillRect(dx, h / 2 + 2 + i * 3.4, 1.6, 1.6); continue; }
    const span = 1.1 + hash(br, i + 13) * 0.7;
    const age = ((time + hash(br, i + 21) * span) % span) / span;
    x.globalAlpha = (1 - age) * 0.8;
    x.fillRect(dx + age * 2.2, h / 2 - 1 + age * age * 16, 1.7, 1.7);
  }
  x.restore();
}

export default { brick };
