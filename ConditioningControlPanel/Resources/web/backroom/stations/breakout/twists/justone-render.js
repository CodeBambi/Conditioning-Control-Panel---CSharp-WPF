/* ============================================================================
 * stations/breakout/twists/justone-render.js - the twist's own drawing.
 * Twist: Just One (The Ward). The treat brick wears a wrapped sweet that
 * breathes; the falling treat wears the same sweet, painted over the fireball
 * capsule the drop really is (twists/CONTRACT.md section 8); the floor warms
 * while the high runs and cools as it goes; a row of sweets along the bottom
 * left, the last one wearing the high's own countdown ring, is the whole mark.
 *
 * REDUCED MOTION: nothing breathes, nothing trails, nothing flickers. The
 * sweets, the pips and the warm floor still read.
 * ==========================================================================*/

const TAU = Math.PI * 2;
/** The Ward's red, its hot centre, the ink every face is drawn with, and the dull sweet GREY wears. */
const RED = '#EE5A44', HOT = '#FF9A6B', INK = '#191326', DULL = '#c8c4cc';

/** One wrapped sweet at the origin: a body, two wrapper twists, one shine. */
function sweet(x, r, fill, shine) {
  x.beginPath();
  x.ellipse(0, 0, r, r * 0.82, 0, 0, TAU);
  for (const s of [-1, 1]) {
    x.moveTo(s * r * 0.85, 0);
    x.lineTo(s * r * 1.75, -r * 0.66);
    x.lineTo(s * r * 1.42, 0);
    x.lineTo(s * r * 1.75, r * 0.66);
    x.closePath();
  }
  x.fillStyle = fill; x.fill();
  x.strokeStyle = INK; x.lineWidth = Math.max(1, r * 0.17); x.stroke();
  if (!shine) return;
  x.fillStyle = 'rgba(255,255,255,.8)';
  x.beginPath(); x.ellipse(-r * 0.3, -r * 0.32, r * 0.27, r * 0.16, -0.6, 0, TAU); x.fill();
}

/** A deterministic phase per cell, so two treats never breathe in lockstep. */
const phaseOf = br => ((br.x || 0) * 0.013 + (br.y || 0) * 0.029) % TAU;

/**
 * The treat brick: an invitation. It is the only brick on the board that moves.
 * GREY is payload-free, so in there the sweet is dull and still, like every other face.
 */
export function brick(x, br, snap, t) {
  if (!br || !br.treat || !br.alive) return;
  const grey = !!(snap && snap.state === 'grey');
  const still = !!(snap && snap.reduced) || grey;
  const beat = still ? 0 : Math.sin(t * 3.1 + phaseOf(br));
  const r = Math.min(br.w * 0.21, br.h * 0.42) * (1 + 0.07 * beat);
  if (grey) { sweet(x, r, DULL, false); return; }
  if (!still) {
    x.save();
    x.globalCompositeOperation = 'lighter';
    x.globalAlpha = 0.1 + 0.06 * (beat + 1) / 2;
    x.fillStyle = RED;
    x.beginPath(); x.arc(0, 0, r * 3.4, 0, TAU); x.fill();
    x.restore();
  }
  sweet(x, r, RED, true);
}

/** While the high burns, the floor is warm. It cools as the six seconds go, so the comedown is never a surprise. */
export function under(x, snap, t) {
  const left = Math.max(0, (snap && snap.justone && snap.justone.high) || 0);
  if (left <= 0) return;
  const h = snap.h || 0, w = snap.w || 0;
  const grad = x.createLinearGradient(0, h * 0.5, 0, h);
  const a = 0.26 * Math.min(1, left / 6);
  grad.addColorStop(0, 'rgba(255,120,60,0)');
  grad.addColorStop(1, 'rgba(255,154,107,' + a.toFixed(3) + ')');
  x.save();
  x.globalCompositeOperation = 'lighter';
  x.fillStyle = grad;
  x.fillRect(0, h * 0.5, w, h * 0.5);
  x.restore();
}

/** The falling treat, and the row of sweets that says how many are already taken. */
export function over(x, snap, t) {
  const still = !!snap.reduced;
  const drops = (snap.power && snap.power.drops) || [];
  for (const d of drops) {
    if (!d || !d.treat) continue;
    const age = d.age || 0, beat = still ? 0 : Math.sin(age * 6);
    x.save();
    x.globalCompositeOperation = 'lighter';
    x.globalAlpha = 0.16 + (still ? 0 : 0.07 * beat);
    x.fillStyle = HOT;
    x.beginPath(); x.arc(d.x, d.y, 24, 0, TAU); x.fill();
    x.restore();
    if (!still) {
      x.save();
      x.globalAlpha = 0.18;
      x.fillStyle = RED;
      for (let k = 1; k <= 3; k++) { x.beginPath(); x.arc(d.x, d.y - k * 9, 7 - k * 1.6, 0, TAU); x.fill(); }
      x.restore();
    }
    x.save();
    x.translate(d.x, d.y);
    /* Opaque first: underneath this is the fireball capsule the drop really is. */
    x.fillStyle = INK;
    x.beginPath(); x.arc(0, 0, 13, 0, TAU); x.fill();
    if (!still) x.rotate(Math.sin(age * 3 + (d.ph || 0)) * 0.24);
    sweet(x, 9 * (still ? 1 : 1 + 0.05 * beat), RED, true);
    x.restore();
  }

  /* The mark: one small sweet per treat taken, and the last one holds the high's own ring.
   * It sits along the bottom edge, under the paddle's lane and clear of the station's own chrome. */
  const doses = (snap.justone && snap.justone.doses) | 0;
  if (!doses) return;
  const shown = Math.min(doses, 8), left = Math.max(0, (snap.justone && snap.justone.high) || 0);
  const markY = (snap.h || 0) - 28, step = 30, x0 = 36;
  /* A dark plate under the row: the field behind it is pink, and a red sweet on pink is a smudge. */
  x.save();
  x.globalAlpha = 0.34;
  x.fillStyle = INK;
  x.beginPath();
  x.ellipse(x0 + (shown - 1) * step / 2, markY, (shown - 1) * step / 2 + 26, 19, 0, 0, TAU);
  x.fill();
  x.restore();
  for (let i = 0; i < shown; i++) {
    const hot = i === shown - 1 && left > 0;
    x.save();
    x.translate(x0 + i * step, markY);
    sweet(x, 9, hot ? HOT : RED, true);
    if (hot) {
      x.strokeStyle = 'rgba(255,196,109,.9)';
      x.lineWidth = 2;
      x.beginPath(); x.arc(0, 0, 15, -Math.PI / 2, -Math.PI / 2 + TAU * Math.min(1, left / 6)); x.stroke();
    }
    x.restore();
  }
  if (doses > shown) {
    x.fillStyle = 'rgba(255,255,255,.72)';
    x.font = '600 12px "DM Mono", monospace';
    x.textAlign = 'left'; x.textBaseline = 'middle';
    x.fillText('+' + (doses - shown), x0 + shown * step - 6, markY);
  }
}

export default { brick, under, over };
