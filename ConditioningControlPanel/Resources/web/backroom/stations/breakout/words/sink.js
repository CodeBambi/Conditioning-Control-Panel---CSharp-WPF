/* ============================================================================
 * stations/breakout/words/sink.js - the SINK trigger (heavy, 1.6 s).
 * The screen falls into the monitor and you go with it: slow-mo to 0.3x with a
 * fast ease in and a smooth ease out over the last 0.8 s, the whole field drops
 * about 12% toward the ball (g.mod.zoom below 1: the renderer's own field zoom,
 * since a transform set in render.world is undone by the hook's save/restore)
 * into a pit of darkening rings, a violet vignette
 * closes from the sides and opens again, the top band leans in. A low sub glide
 * falls one octave under a soft whoomp and a long dark tail; the rest ducks 60%.
 * The paddle stays live at slow speed, the ball cannot be lost, and sinking
 * leaves the world a notch pinker (addSat 0.02 on end).
 * Reduced motion: no scale, no vignette movement, a static tint and the sound.
 * Contract: see ../word-fx.js (sim / render / sound hooks, fx and R shapes).
 * ==========================================================================*/

const DUR = 1.6, SLOW = 0.3, IN_S = 0.18, OUT_S = 0.8, SCALE = 0.12;
const clamp01 = v => (v < 0 ? 0 : v > 1 ? 1 : v);
const easeOut = t => 1 - (1 - clamp01(t)) ** 3;
const smooth = t => { t = clamp01(t); return t * t * (3 - 2 * t); };

/** How far in we are, 0..1: fast in over IN_S, held, then a smooth climb out over the last OUT_S. */
function depth(t) {
  if (t < IN_S) return easeOut(t / IN_S);
  if (t > DUR - OUT_S) return 1 - smooth((t - (DUR - OUT_S)) / OUT_S);
  return 1;
}
/** The vignette's own curve: closes fast, peaks around 40% and opens from there. */
function vignette(phase) { return phase < 0.4 ? easeOut(phase / 0.4) : 1 - smooth((phase - 0.4) / 0.6); }

/** The ball the field drops toward, clamped to the middle band like the renderer's zoom. */
function anchor(s, R) {
  const b = s.balls && s.balls[0];
  const x = b ? Math.min(R.W * 0.7, Math.max(R.W * 0.3, b.x)) : R.W / 2;
  const y = b ? Math.min(R.H * 0.7, Math.max(R.H * 0.3, b.y)) : R.H / 2;
  return { x, y };
}

/** The frame copy the melt reuses across frames and fires (never allocated per frame). */
const melt = { canvas: null };

export default {
  key: 'SINK', heavy: true, dur: DUR,
  sim: {
    start(g, fx, api) { fx.data.sat = 0; },
    tick(g, fx, dt, api) {
      const d = depth(fx.t), ts = 1 - (1 - SLOW) * d;
      g.mod.timeScale = ts;
      g.mod.zoom = g.reduced ? 1 : 1 - SCALE * d;              // the field drops toward the ball
      g.mod.safe = true;
    },
    end(g, fx, api) { if (!fx.data.sat) { fx.data.sat = 1; api.addSat(0.02); } },
  },
  render: {
    /** The pit: dark rings around the ball fill the margin the shrunk field leaves (the drop itself is g.mod.zoom). */
    world(ctx, s, fx, R) {
      if (R.reduced || s.reduced) return;
      const d = depth(fx.t), { x, y } = anchor(s, R);
      if (d <= 0) return;
      const rMax = Math.hypot(R.W, R.H) * 0.6;
      const pit = ctx.createRadialGradient(x, y, rMax * 0.15, x, y, rMax);
      pit.addColorStop(0, R.col(R.VIOLET, R.mix, 0.35)); pit.addColorStop(0.5, R.col([40, 18, 70], R.mix, 0.9)); pit.addColorStop(1, 'rgba(6,2,14,1)');
      ctx.fillStyle = pit; ctx.fillRect(-R.W, -R.H, R.W * 3, R.H * 3);
      ctx.strokeStyle = R.col(R.VIOLET, R.mix, 0.18 * d); ctx.lineWidth = 1.5;
      for (let i = 1; i <= 5; i++) {                              // rings that tighten as the field goes down
        const rr = rMax * (0.22 + 0.16 * i) * (1 - 0.1 * d * i / 5);
        ctx.beginPath(); ctx.arc(x, y, rr, 0, Math.PI * 2); ctx.stroke();
      }
    },
    /** The vignette closes from the sides, the top band leans in. */
    over(ctx, s, fx, R) {
      const reduced = R.reduced || s.reduced;
      const v = reduced ? 0.45 : vignette(fx.phase);
      if (v <= 0.001) return;
      const { x, y } = anchor(s, R);
      const rIn = reduced ? R.W * 0.55 : R.W * (0.62 - 0.32 * v), rOut = Math.hypot(R.W, R.H) * 0.62;
      const grd = ctx.createRadialGradient(x, y, rIn, x, y, rOut);
      grd.addColorStop(0, 'rgba(60,20,110,0)'); grd.addColorStop(0.55, `rgba(50,16,96,${0.45 * v})`); grd.addColorStop(1, `rgba(12,4,30,${0.92 * v})`);
      ctx.fillStyle = grd; ctx.fillRect(-R.W, -R.H, R.W * 3, R.H * 3);   // past the field: the margin the drop leaves is vignetted too
      if (!reduced) {
        const top = ctx.createLinearGradient(0, -R.H * 0.2, 0, R.H * 0.38);
        top.addColorStop(0, `rgba(10,3,28,${0.85 * v})`); top.addColorStop(1, 'rgba(10,3,28,0)');
        ctx.fillStyle = top; ctx.fillRect(-R.W, -R.H * 0.2, R.W * 3, R.H * 0.58);
      }
    },
    /** The picture goes soft and runs: a blurred copy of the frame over itself, then vertical strips sliding down
     *  by different amounts, so the field melts while it sinks (owner, 2026-09-19: "a little blur/melt"). */
    post(ctx, s, fx, R) {
      if (R.reduced) return;
      const d = depth(fx.t);
      if (d <= 0.02) return;
      const src = R.frame(melt.canvas); melt.canvas = src;
      const cw = R.cw, ch = R.ch;
      // Blur: one full-frame draw through a canvas filter, a few device px at full depth.
      const blur = (1.5 + 2.5 * d) * (cw / 780);
      if ('filter' in ctx) {
        ctx.save(); ctx.globalAlpha = 0.55 * d; ctx.filter = `blur(${blur.toFixed(1)}px)`;
        ctx.drawImage(src, 0, 0); ctx.restore();
      }
      // Melt: 16 strips, each sliding down by its own amount that grows with depth, drawn at low alpha so the
      // picture drips without losing the ball.
      const n = 16, sw = Math.ceil(cw / n), t = fx.t;
      ctx.save(); ctx.globalAlpha = 0.28 * d;
      for (let i = 0; i < n; i++) {
        const k = 0.5 + 0.5 * Math.sin(i * 1.7 + t * 2.1);
        const dy = (6 + 34 * k) * d * (ch / 1688);
        ctx.drawImage(src, i * sw, 0, sw, ch, i * sw, dy, sw, ch);
      }
      ctx.restore();
    },
  },
  sound(synth, fx) {
    const t = synth.now;
    synth.duck(0.6, 1.0, 0.6);
    synth.play([
      synth.tone(110, 1.2, 0.2, { hzTo: 55, wave: 'sine', attack: 0.1, lp: 420, lpTo: 160 }),                 // the sub glide, one octave down
      synth.noise(360, 0.32, 0.14, { hzTo: 70, type: 'lowpass', q: 0.7, attack: 0.08 }),                      // the whoomp
      synth.tone(55, 1.7, 0.07, { hzTo: 41, wave: 'triangle', attack: 0.35, lp: 200, at: 0.45, wet: true }),  // the long dark tail
    ], t, synth.dest);
  },
};
