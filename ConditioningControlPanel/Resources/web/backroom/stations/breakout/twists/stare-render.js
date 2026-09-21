/* ============================================================================
 * stations/breakout/twists/stare-render.js - the twist's own drawing.
 *
 * Twist: Stare (act 3). The eye lives UNDER the bricks, in the background:
 * a black and white spiral iris in a pale almond, with a pupil that leans after
 * the ball. It is the only thing on the board with no colour in it at all, which
 * is the point: the grey world is looking in. It is faint on its first board and
 * plainer on its last (`g.stare.presence`, authored in stare.js).
 *
 * A judged brick is washed grey over its own face and wears a thin dead rim, so
 * "this one costs you an extra hit" reads without a number.
 *
 * REDUCED MOTION: the iris does not turn, the pupil does not track, the lids do
 * not breathe. The eye is still there and a judged brick is still grey.
 *
 * The act 3 lane owns this file. See twists/CONTRACT.md.
 * ==========================================================================*/

/** `judged` is this twist's own flag, so its brick painter has to claim it. */
export const paintsBrick = ['judged'];

/** The iris, in field pixels, before presence scales what you can see of it. */
const IRIS_R = 74;
/** How far the pupil may lean off centre. */
const LEAN = 20;
/** Turns per second of the spiral while the eye is holding on. */
const SPIN = 0.22;

const stareOf = snap => (snap && snap.stare) || null;
const clamp01 = v => (v < 0 ? 0 : v > 1 ? 1 : v);

/** The ball the pupil leans after: the first one actually in play. */
export function pupilTarget(snap) {
  for (const b of (snap && Array.isArray(snap.balls) ? snap.balls : [])) {
    if (!b || b.lost || b.falling) continue;
    return { x: b.x, y: b.y };
  }
  return null;
}

/** Where the pupil sits, given the eye, the ball and how far it may lean. */
export function pupilAt(eye, target, lean = LEAN) {
  if (!target) return { x: eye.x, y: eye.y };
  const dx = target.x - eye.x, dy = target.y - eye.y;
  const d = Math.hypot(dx, dy) || 1;
  const k = Math.min(1, d / 260) * lean;
  return { x: eye.x + (dx / d) * k, y: eye.y + (dy / d) * k };
}

/* ----------------------------------------------------------------- under */
/** The eye, behind the wall. Black and white only, and never louder than the bricks. */
export function under(c2d, snap, t) {
  const st = stareOf(snap);
  if (!st || !st.eye) return;
  const reduced = !!snap.reduced;
  const presence = clamp01(Number(st.presence) >= 0 ? Number(st.presence) : 0.58);
  // Holding on brings it forward. Idling it is barely there.
  const hold = st.on ? clamp01(0.35 + (Number(st.dwell) || 0) / 1.5 * 0.65) : 0;
  const a = 0.1 + presence * 0.24 + hold * 0.2 * presence;
  const r = IRIS_R * (0.78 + presence * 0.22);
  const eye = st.eye;
  const spin = reduced ? 0 : t * SPIN * Math.PI * 2;
  const pupil = reduced ? { x: eye.x, y: eye.y } : pupilAt(eye, pupilTarget(snap));

  c2d.globalAlpha = a;
  // The almond: a pale lens the spiral is cut out of.
  c2d.fillStyle = '#d8d8de';
  c2d.beginPath();
  c2d.moveTo(eye.x - r * 1.72, eye.y);
  c2d.quadraticCurveTo(eye.x, eye.y - r * 1.28, eye.x + r * 1.72, eye.y);
  c2d.quadraticCurveTo(eye.x, eye.y + r * 1.28, eye.x - r * 1.72, eye.y);
  c2d.fill();

  // The iris is a clipped spiral, so it can never bleed past the lid.
  c2d.save();
  c2d.clip();
  c2d.translate(pupil.x, pupil.y);
  c2d.rotate(spin);
  c2d.fillStyle = '#efeff3';
  c2d.beginPath(); c2d.arc(0, 0, r, 0, 7); c2d.fill();
  // Four black arms winding in: a spiral drawn as widening strokes, not a hundred segments.
  c2d.strokeStyle = '#141420';
  c2d.lineCap = 'round';
  for (let arm = 0; arm < 4; arm++) {
    c2d.beginPath();
    const turn = arm * Math.PI / 2;
    for (let i = 0; i <= 18; i++) {
      const f = i / 18;
      const rad = r * (0.12 + f * 0.9);
      const ang = turn + f * 2.6;
      const x = Math.cos(ang) * rad, y = Math.sin(ang) * rad;
      if (i === 0) c2d.moveTo(x, y); else c2d.lineTo(x, y);
    }
    c2d.lineWidth = r * 0.16;
    c2d.stroke();
  }
  // The pupil: flat black, and it is the part that looks at you.
  c2d.fillStyle = '#07070c';
  c2d.beginPath(); c2d.arc(0, 0, r * 0.3, 0, 7); c2d.fill();
  c2d.restore();

  // A lid line over the top, so the almond reads as an eye and not a coin.
  c2d.strokeStyle = 'rgba(12,12,20,.7)';
  c2d.lineWidth = 3;
  c2d.beginPath();
  c2d.moveTo(eye.x - r * 1.72, eye.y);
  c2d.quadraticCurveTo(eye.x, eye.y - r * 1.28, eye.x + r * 1.72, eye.y);
  c2d.stroke();
  c2d.globalAlpha = 1;
}

/* ----------------------------------------------------------------- brick */
/** A judged brick: the colour drained out of it, and a dead rim around it. */
export function brick(c2d, br, snap) {
  if (!br || !br.judged) return;
  const hw = br.w / 2, hh = br.h / 2;
  c2d.fillStyle = 'rgba(112,110,124,.72)';
  c2d.fillRect(-hw, -hh, br.w, br.h);
  c2d.strokeStyle = 'rgba(26,24,36,.75)';
  c2d.lineWidth = 1.4;
  c2d.strokeRect(-hw + 1, -hh + 1, br.w - 2, br.h - 2);
  // A small flat mark: this brick has been noticed. Static, so reduced motion loses nothing.
  c2d.fillStyle = 'rgba(232,232,238,.5)';
  c2d.fillRect(-3, -1.2, 6, 2.4);
  if (snap && snap.reduced) return;
  c2d.strokeStyle = 'rgba(232,232,238,.22)';
  c2d.lineWidth = 1;
  c2d.strokeRect(-hw + 3.5, -hh + 3.5, br.w - 7, br.h - 7);
}

export default { brick, under, paintsBrick, pupilAt, pupilTarget };
