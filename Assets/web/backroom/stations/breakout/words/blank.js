/* BLANK: immediate pale veil and a recorded finger snap on the first displayed frame. */

const OUT_S = 0.5;
const VEIL = '255,214,236';

/** Cut in immediately; return over the final OUT_S seconds. */
export function blankCurve(t, dur) {
  if (t < 0) return 0;
  if (t <= dur - OUT_S) return 1;
  return Math.max(0, Math.min(1, (dur - t) / OUT_S));
}

function holeRect(ctx, x, y, w, h, r) {
  r = Math.min(r, w / 2, h / 2);
  ctx.moveTo(x + r, y);
  ctx.lineTo(x + w - r, y); ctx.arc(x + w - r, y + r, r, -Math.PI / 2, 0);
  ctx.lineTo(x + w, y + h - r); ctx.arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
  ctx.lineTo(x + r, y + h); ctx.arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
  ctx.lineTo(x, y + r); ctx.arc(x + r, y + r, r, Math.PI, Math.PI * 1.5);
  ctx.closePath();
}

export default {
  key: 'BLANK', heavy: true, dur: 1.2,
  sim: {
    start(g, fx, api) { g.mod.hideBricks = 1; g.mod.safe = true; },
    tick(g, fx, dt, api) {
      g.mod.safe = true;
      g.mod.hideBricks = blankCurve(fx.t, fx.dur);
    },
    end(g, fx, api) { /* g.mod is fresh every round; nothing lingers */ },
  },
  render: {
    world(ctx, s, fx, R) { /* nothing: the veil sits over the field */ },
    over(ctx, s, fx, R) {
      const a = blankCurve(fx.t, fx.dur);
      if (a <= 0.003) return;
      const W = R.W, H = R.H;
      ctx.beginPath(); ctx.rect(0, 0, W, H);
      for (const b of s.balls) {
        if (b.lost) continue;
        const r = (b.r || 8) + 10;
        ctx.moveTo(b.x + r, b.y); ctx.arc(b.x, b.y, r, 0, Math.PI * 2);
      }
      const p = s.paddle;
      if (p) { const pad = 6 + p.w * 0.1; holeRect(ctx, p.x - p.w / 2 - pad, p.y - p.h / 2 - pad, p.w + pad * 2, p.h + pad * 2, 11 + pad); }
      ctx.clip('evenodd');
      ctx.fillStyle = `rgba(${VEIL},${a.toFixed(3)})`; ctx.fillRect(0, 0, W, H);
    },
    post(ctx, s, fx, R) { /* nothing: no whole-frame tricks, the wall is simply gone */ },
  },
  sound(synth, fx) { synth.snap(); },
};
