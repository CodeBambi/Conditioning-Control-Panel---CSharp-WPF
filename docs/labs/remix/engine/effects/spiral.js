/* ============================================================================
 * effects/spiral.js - the house motif, drawn rather than loaded.
 *
 * Archimedean arms built from a filled path: outward along one edge, back
 * along the other, so the shape is one closed polygon and the browser's own
 * anti aliasing keeps the edges clean at any rotation. Three arm styles, the
 * dice picks one:
 *
 *   0 ribbon    a few wide arms, the classic barber pole
 *   1 thread    one or two hairline arms wound many times
 *   2 pinwheel  six short arms, more wheel than tunnel
 *
 *   over     screened on top, it glows through the picture
 *   through  a colour plate masked by the arms, the colour sits in the picture
 *
 * A user can drop their own spiral; then it is rotated in place instead.
 * ==========================================================================*/

import { colourFor, knob, rgba, clamp } from './util.js';

const STYLES = [
  { arms: 3, turns: 3.0, duty: 0.5, stroke: 0 },
  { arms: 2, turns: 7.0, duty: 0.12, stroke: 2.2 },
  { arms: 6, turns: 1.25, duty: 0.5, stroke: 0 },
];

/**
 * Build (and fill) the arm shapes of a spiral.
 * opts: { cx, cy, r, style, phase, dir, steps }
 */
export function drawSpiralArms(ctx, opts) {
  const st = STYLES[clamp(opts.style | 0, 0, STYLES.length - 1)];
  const arms = st.arms;
  const thetaMax = st.turns * Math.PI * 2;
  const steps = opts.steps || 120;
  const width = ((Math.PI * 2) / arms) * st.duty;

  for (let a = 0; a < arms; a++) {
    const base = opts.phase + (a * Math.PI * 2) / arms;
    ctx.beginPath();
    for (let i = 0; i <= steps; i++) {
      const th = (thetaMax * i) / steps;
      const r = opts.r * (th / thetaMax);
      const ang = base + th * opts.dir;
      const x = opts.cx + Math.cos(ang) * r;
      const y = opts.cy + Math.sin(ang) * r;
      if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
    }
    if (st.stroke) {
      ctx.lineWidth = st.stroke;
      ctx.lineCap = 'round';
      ctx.lineJoin = 'round';
      ctx.stroke();
      continue;
    }
    for (let i = steps; i >= 0; i--) {
      const th = (thetaMax * i) / steps;
      const r = opts.r * (th / thetaMax);
      const ang = base + th * opts.dir + width * opts.dir;
      ctx.lineTo(opts.cx + Math.cos(ang) * r, opts.cy + Math.sin(ang) * r);
    }
    ctx.closePath();
    ctx.fill();
  }
}

export function render(ctx, src, block, t, env) {
  const s = knob(block.params, 'strength', 55) * env.ramp;
  if (s <= 0.005) return;
  const speed = knob(block.params, 'speed', 50);
  const rect = env.rect;
  const colour = colourFor(block.params, env);
  const dir = block.params && block.params.dir === -1 ? -1 : 1;
  const style = (block.params && block.params.style) | 0;
  const phase = env.frame * (0.015 + speed * 0.13) * dir;

  const cx = rect.x + rect.w / 2;
  const cy = rect.y + rect.h / 2;
  const r = Math.hypot(rect.w, rect.h) * 0.62;

  if (env.spiralImage) {
    drawDroppedSpiral(ctx, env.spiralImage, cx, cy, r, phase, s, block.mode, colour);
    return;
  }

  const w = Math.max(1, Math.round(rect.w));
  const h = Math.max(1, Math.round(rect.h));
  const b = env.buf('spiral', w, h);
  const c = b.ctx;
  c.setTransform(1, 0, 0, 1, 0, 0);
  c.globalCompositeOperation = 'source-over';
  c.clearRect(0, 0, w, h);

  // arms first, in white, so the plate below can be any colour
  c.fillStyle = '#fff';
  c.strokeStyle = '#fff';
  drawSpiralArms(c, { cx: w / 2, cy: h / 2, r, style, phase, dir, steps: 160 });

  // a colour plate brighter at the eye, masked down to the arms
  const g = c.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, r);
  if (block.mode === 'through') {
    g.addColorStop(0, rgba(colour, 1));
    g.addColorStop(0.55, rgba(colour, 0.92));
    g.addColorStop(1, rgba(colour, 0.5));
  } else {
    g.addColorStop(0, rgba('#FFFFFF', 0.95));
    g.addColorStop(0.35, rgba(colour, 0.95));
    g.addColorStop(1, rgba(colour, 0.35));
  }
  c.globalCompositeOperation = 'source-in';
  c.fillStyle = g;
  c.fillRect(0, 0, w, h);

  // the arms let go towards the outside, so it sits in the frame instead of
  // being stamped across it
  const fade = c.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, r);
  fade.addColorStop(0, 'rgba(0,0,0,1)');
  fade.addColorStop(0.55, 'rgba(0,0,0,1)');
  fade.addColorStop(0.85, 'rgba(0,0,0,0.6)');
  fade.addColorStop(1, 'rgba(0,0,0,0.12)');
  c.globalCompositeOperation = 'destination-in';
  c.fillStyle = fade;
  c.fillRect(0, 0, w, h);

  // a soft eye at the middle, so the centre is a light rather than a pinch
  const eye = c.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, Math.max(3, r * 0.12));
  eye.addColorStop(0, rgba('#FFFFFF', 0.85));
  eye.addColorStop(0.45, rgba(colour, 0.55));
  eye.addColorStop(1, rgba(colour, 0));
  c.globalCompositeOperation = 'source-over';
  c.fillStyle = eye;
  c.fillRect(0, 0, w, h);

  ctx.save();
  ctx.globalAlpha = block.mode === 'through' ? 0.2 + 0.7 * s : 0.15 + 0.62 * s;
  ctx.globalCompositeOperation = block.mode === 'through' ? 'source-over' : 'screen';
  ctx.drawImage(b.canvas, 0, 0, w, h, rect.x, rect.y, rect.w, rect.h);
  ctx.restore();
}

function drawDroppedSpiral(ctx, img, cx, cy, r, phase, s, mode, colour) {
  ctx.save();
  ctx.globalAlpha = s;
  ctx.globalCompositeOperation = mode === 'through' ? 'source-over' : 'screen';
  ctx.translate(cx, cy);
  ctx.rotate(phase);
  const size = r * 2;
  ctx.drawImage(img, -size / 2, -size / 2, size, size);
  ctx.restore();
}
