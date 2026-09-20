/* ============================================================================
 * stations/breakout/words/blank.js - the BLANK trigger.
 * The wall is gone. Only the ball and your paddle are left: everything else
 * fades to a flat pale pink in 0.2 s, holds, and comes back over the last
 * 0.5 s. The bricks fade through g.mod.hideBricks (they still collide and
 * still count) and a veil in render.over covers the rest of the field with
 * holes punched around each ball and the paddle (an even-odd clip).
 * Sound: white noise swells for 0.25 s and cuts to a hard silence (the bed,
 * sfx and sub buses go to nothing), the buses come back over 0.15 s when the
 * wall returns at 0.7 s, with one dry click. Play: you play blind for a second.
 * Reduced motion keeps the veil (a tint) and the sound.
 * Contract: see ../word-fx.js (sim / render / sound hooks, fx and R shapes).
 * ==========================================================================*/

const IN_S = 0.2, OUT_S = 0.5;
const VEIL = '255,214,236';

/** 0 -> 1 over IN_S, hold, back to 0 over the last OUT_S of `dur`. */
export function blankCurve(t, dur) {
  if (!(t > 0)) return 0;
  if (t < IN_S) return t / IN_S;
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
    start(g, fx, api) { /* nothing to set up: the curve reads fx.t */ },
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
  sound(synth, fx) {
    const t0 = synth.now, cut = 0.25, back = 0.7;
    synth.play([synth.noise(70, cut, 0.2, { type: 'highpass', q: 0.5, attack: 0.9 })], t0, synth.dest);   // the swell, then the cut
    for (const k of ['bed', 'sfx', 'sub']) {
      const b = synth.bus[k]; if (!b || !b.gain) continue;
      synth.glide(b.gain, 0.0001, 0.03, t0 + cut);                                                        // hard silence
      try { b.gain.setValueAtTime(0.0001, t0 + back); b.gain.linearRampToValueAtTime(1, t0 + back + 0.15); } catch (e) { /* the glide already holds */ }
    }
    synth.play([synth.noise(4200, 0.004, 0.3, { type: 'highpass', q: 0.7, at: back })], t0, synth.dest);  // the wall is back: one dry click
  },
};
