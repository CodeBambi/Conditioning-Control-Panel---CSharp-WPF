/* ============================================================================
 * stations/breakout/twists/keys-render.js - Keys and Gates, drawn.
 *
 * Three things have to read at a glance, without a word of text:
 *   1. a gate plate is LOCKED - it wears a keyhole in its trim colour, and it
 *      flares and the keyhole jitters when a ball bounces off it;
 *   2. a loot box HOLDS something - the prize stands behind a tinted pane with
 *      a lit pad under it, so the picture and the power-up show through;
 *   3. a key belongs to a box - the brick wears the box's colour and a key.
 * The beam from the key to the box it opened is the one moving part.
 *
 * Reduced motion: no jitter, no sheen crawl, no travelling spark. The keyhole,
 * the pane, the flare and the beam all still say what they say.
 * ==========================================================================*/

/** The trims, matching render.js GATE_TRIM and doors.js `M` / `W` / `P`. */
export const KEY_COL = { M: '#F6D36B', W: '#7FD6E8', clay: '#D9A77C' };
const RGB = { M: [246, 211, 107], W: [127, 214, 232], clay: [217, 167, 124] };
const PALE = [214, 214, 219];
const rgba = (gate, a) => { const c = RGB[gate] || PALE; return `rgba(${c[0]},${c[1]},${c[2]},${a})`; };
const isGrey = snap => !!snap && snap.state === 'grey';

function roundRect(x, rx, ry, w, h, r) {
  x.beginPath();
  x.moveTo(rx + r, ry); x.lineTo(rx + w - r, ry); x.quadraticCurveTo(rx + w, ry, rx + w, ry + r);
  x.lineTo(rx + w, ry + h - r); x.quadraticCurveTo(rx + w, ry + h, rx + w - r, ry + h);
  x.lineTo(rx + r, ry + h); x.quadraticCurveTo(rx, ry + h, rx, ry + h - r);
  x.lineTo(rx, ry + r); x.quadraticCurveTo(rx, ry, rx + r, ry);
  x.closePath();
}

/** The lock itself: a dark keyhole with a lit rim, centred on the plate. */
function keyhole(x, gate, lit) {
  const r = 3.6;
  x.fillStyle = 'rgba(8,7,12,.82)';
  x.beginPath(); x.arc(0, -1.5, r, 0, Math.PI * 2); x.fill();
  x.beginPath(); x.moveTo(-r * 0.62, -0.4); x.lineTo(r * 0.62, -0.4);
  x.lineTo(r * 0.44, 5.4); x.lineTo(-r * 0.44, 5.4); x.closePath(); x.fill();
  x.strokeStyle = rgba(gate, 0.55 + 0.4 * lit); x.lineWidth = 1.1;
  x.beginPath(); x.arc(0, -1.5, r + 1.2, 0, Math.PI * 2); x.stroke();
}

/** A key: a ringed bow, a shaft and two bits. The cracked key is the same key, snapped. */
function keyGlyph(x, gate) {
  x.strokeStyle = 'rgba(24,18,6,.9)'; x.fillStyle = 'rgba(24,18,6,.9)'; x.lineWidth = 2;
  if (gate === 'clay') {
    x.beginPath(); x.arc(-8, -1, 3.2, 0, Math.PI * 2); x.stroke();
    x.beginPath(); x.moveTo(-4.8, -1); x.lineTo(0, -1); x.lineTo(-1.6, 3); x.stroke();     // the break
    x.beginPath(); x.moveTo(1.8, -3); x.lineTo(9, -3); x.moveTo(6.5, -3); x.lineTo(6.5, 2); x.stroke();
    return;
  }
  x.beginPath(); x.arc(-7.5, -1, 3.4, 0, Math.PI * 2); x.stroke();
  x.fillRect(-4.6, -2, 13, 2.2);
  x.fillRect(6.2, -2, 2.1, 5.2);
  x.fillRect(2.4, -2, 2.1, 3.8);
}

/** Drawn for a brick the twist flags, inside the brick's own transform (origin = its centre). */
export function brick(ctx2d, br, snap, t) {
  const reduced = !!(snap && snap.reduced), dull = isGrey(snap);
  const time = Number.isFinite(t) ? t : 0;
  if (br.gate) {
    const lock = Math.max(0, Math.min(1, (br.lockT || 0) / 0.26));
    const lit = br.gateOpening != null ? 1 : 0;
    if (lit && !dull) { ctx2d.fillStyle = rgba(br.gate, 0.34); ctx2d.fillRect(-br.w / 2, -br.h / 2, br.w, br.h); }
    ctx2d.save();
    if (lock > 0 && !reduced) ctx2d.translate(Math.sin(time * 74) * lock * 1.6, Math.sin(time * 61) * lock * 0.8);
    keyhole(ctx2d, dull ? null : br.gate, Math.max(lit, lock));
    ctx2d.restore();
    if (lock > 0) {                                                  // denied: the plate flares in its own trim
      ctx2d.strokeStyle = dull ? `rgba(220,220,228,${0.5 * lock})` : rgba(br.gate, 0.75 * lock);
      ctx2d.lineWidth = 2;
      roundRect(ctx2d, -br.w / 2 + 1, -br.h / 2 + 1, br.w - 2, br.h - 2, 3); ctx2d.stroke();
    }
    return;
  }
  if (br.key) {
    roundRect(ctx2d, -br.w / 2 + 1, -br.h / 2 + 1, br.w - 2, br.h - 2, 4);
    ctx2d.fillStyle = dull ? '#d6d6db' : KEY_COL[br.key] || '#d6d6db'; ctx2d.fill();
    ctx2d.fillStyle = 'rgba(255,255,255,.22)'; ctx2d.fillRect(-br.w / 2 + 1, -br.h / 2 + 1, br.w - 2, 2.4);
    ctx2d.fillStyle = 'rgba(0,0,0,.2)'; ctx2d.fillRect(-br.w / 2 + 1, br.h / 2 - 3.4, br.w - 2, 2.4);
    keyGlyph(ctx2d, br.key);
  }
}

/** Under the bricks: the lit pad inside a sealed box, so the prize looks held, not parked. */
export function under(ctx2d, snap, t) {
  const st = snap && snap.keysTwist;
  if (!st || !st.boxes) return;
  const reduced = !!snap.reduced, time = Number.isFinite(t) ? t : 0;
  for (const box of st.boxes) {
    if (box.seal <= 0) continue;
    const cx = box.x + box.w / 2, cy = box.y + box.h / 2;
    const breath = reduced ? 0.5 : 0.5 + 0.5 * Math.sin(time * 1.6 + (box.gate === 'W' ? 1.7 : 0));
    const r = Math.max(box.w, box.h) * 0.78;
    const grad = ctx2d.createRadialGradient(cx, cy, 2, cx, cy, r);
    grad.addColorStop(0, rgba(box.gate, (0.2 + 0.12 * breath) * box.seal));
    grad.addColorStop(1, rgba(box.gate, 0));
    ctx2d.fillStyle = grad;
    ctx2d.fillRect(cx - r, cy - r, r * 2, r * 2);
  }
}

/** Over everything: the pane the prize sits behind, and the beam the key throws. */
export function over(ctx2d, snap, t) {
  const st = snap && snap.keysTwist;
  if (!st) return;
  const reduced = !!snap.reduced, time = Number.isFinite(t) ? t : 0;
  for (const box of st.boxes || []) {
    if (box.seal <= 0) continue;
    const a = box.seal, pad = 3;
    const x = box.x - pad, y = box.y - pad, w = box.w + pad * 2, h = box.h + pad * 2;
    ctx2d.save();
    roundRect(ctx2d, x, y, w, h, 5); ctx2d.clip();
    ctx2d.fillStyle = rgba(box.gate, 0.15 * a);
    ctx2d.fillRect(x, y, w, h);
    const slide = reduced ? 0.3 : (time * 0.22) % 1;                 // one slow highlight crossing the glass
    const gx = x - w * 0.6 + slide * w * 2.2;
    ctx2d.fillStyle = `rgba(255,255,255,${0.12 * a})`;
    ctx2d.beginPath();
    ctx2d.moveTo(gx, y + h); ctx2d.lineTo(gx + w * 0.22, y);
    ctx2d.lineTo(gx + w * 0.4, y); ctx2d.lineTo(gx + w * 0.18, y + h);
    ctx2d.closePath(); ctx2d.fill();
    ctx2d.restore();
    ctx2d.strokeStyle = rgba(box.gate, 0.8 * a); ctx2d.lineWidth = 2;
    roundRect(ctx2d, x, y, w, h, 5); ctx2d.stroke();
    ctx2d.strokeStyle = `rgba(255,255,255,${0.3 * a})`; ctx2d.lineWidth = 1;
    roundRect(ctx2d, x + 2, y + 2, w - 4, h - 4, 4); ctx2d.stroke();
    ctx2d.strokeStyle = rgba(box.gate, a); ctx2d.lineWidth = 3;      // brackets: a display case, not a window
    const arm = Math.min(14, w * 0.18, h * 0.4);
    for (const sx of [1, -1]) for (const sy of [1, -1]) {
      const px = sx > 0 ? x : x + w, py = sy > 0 ? y : y + h;
      ctx2d.beginPath();
      ctx2d.moveTo(px + sx * arm, py); ctx2d.lineTo(px, py); ctx2d.lineTo(px, py + sy * arm);
      ctx2d.stroke();
    }
  }
  for (const b of st.beams || []) {
    const a = Math.max(0, Math.min(1, b.life / (b.max || 1)));
    ctx2d.save();
    ctx2d.lineCap = 'round';
    if (!reduced) {
      ctx2d.strokeStyle = rgba(b.gate, 0.18 * a); ctx2d.lineWidth = 11 * a;
      ctx2d.beginPath(); ctx2d.moveTo(b.x1, b.y1); ctx2d.lineTo(b.x2, b.y2); ctx2d.stroke();
    }
    ctx2d.strokeStyle = rgba(b.gate, 0.9 * a); ctx2d.lineWidth = reduced ? 2 : 3 * a + 1;
    ctx2d.beginPath(); ctx2d.moveTo(b.x1, b.y1); ctx2d.lineTo(b.x2, b.y2); ctx2d.stroke();
    if (!reduced) {                                                  // the turn running up the beam to the box
      const k = 1 - a, px = b.x1 + (b.x2 - b.x1) * k, py = b.y1 + (b.y2 - b.y1) * k;
      ctx2d.fillStyle = `rgba(255,255,255,${0.85 * a})`;
      ctx2d.beginPath(); ctx2d.arc(px, py, 3.2 + 2 * a, 0, Math.PI * 2); ctx2d.fill();
    }
    ctx2d.restore();
  }
}

export default { brick, under, over };
