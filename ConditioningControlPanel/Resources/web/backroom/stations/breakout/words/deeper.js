/* ============================================================================
 * stations/breakout/words/deeper.js - the DEEPER trigger (heavy, 1.2 s).
 * The field has a floor under its floor: the field zooms 8% toward the ball
 * (fast in, held, eased back to 1 over the last 0.4 s), concentric rings rush
 * outward from the ball behind everything, and a ghost of the field one layer
 * down shows through (the previous frame, scaled 0.92 around the ball at ~20%
 * under 'lighter'). One descending octave step, and every DEEPER in a session
 * starts a semitone lower than the last (floor: four semitones down). The
 * saturation floor rises a notch (addSat 0.04 on start), the ball runs 5%
 * faster while the word runs, and the ball cannot be lost.
 * On end the module writes g.deeperBoost = (g.deeperBoost || 0) + 1: the sim
 * has no hook to keep a mod past a word, so the count is left on g for the
 * wall-long +5% a future game.js hook can honour.
 * Reduced motion: no zoom, no ghost layer, the rings become a gentle tint
 * pulse, plus the sound.
 * Contract: see ../word-fx.js (sim / render / sound hooks, fx and R shapes).
 * ==========================================================================*/

const DUR = 1.2, ZOOM = 1.08, IN_S = 0.16, OUT_S = 0.4, GHOST_SCALE = 0.92, GHOST_A = 0.2;
const MAX_DROP = 4;                                       // semitones: the floor each DEEPER descends toward
let fires = 0;                                            // session-long: each DEEPER starts lower than the last
let ghost = null;                                         // the previous frame, kept for the whole session and reused

const clamp01 = v => (v < 0 ? 0 : v > 1 ? 1 : v);
const easeOut = t => 1 - (1 - clamp01(t)) ** 3;
const smooth = t => { t = clamp01(t); return t * t * (3 - 2 * t); };

/** The drop, 0..1: fast in over IN_S, held, then eased back over the last OUT_S. */
function drop(t) {
  if (t < IN_S) return easeOut(t / IN_S);
  if (t > DUR - OUT_S) return 1 - smooth((t - (DUR - OUT_S)) / OUT_S);
  return 1;
}
/** The ball the field drops toward, clamped to the middle band like the renderer's zoom. */
function anchor(s, W, H) {
  const b = s.balls && s.balls[0];
  const x = b ? Math.min(W * 0.7, Math.max(W * 0.3, b.x)) : W / 2;
  const y = b ? Math.min(H * 0.7, Math.max(H * 0.3, b.y)) : H / 2;
  return { x, y };
}

export default {
  key: 'DEEPER', heavy: true, dur: DUR,
  sim: {
    start(g, fx, api) { fx.data.frames = 0; fx.data.ghostOk = false; api.addSat(0.04); },
    tick(g, fx, dt, api) {
      g.mod.zoom = g.reduced ? 1 : 1 + (ZOOM - 1) * drop(fx.t);
      g.mod.ballSpeed = 1.05;
      g.mod.safe = true;
    },
    end(g, fx, api) { g.deeperBoost = (g.deeperBoost || 0) + 1; fx.data.ghostOk = false; },
  },
  render: {
    world(ctx, s, fx, R) { /* the zoom is the renderer's (g.mod.zoom) */ },
    /** Rings rush outward from the ball; low alpha and 'lighter' so they read as depth without hiding the bricks. */
    over(ctx, s, fx, R) {
      const reduced = R.reduced || s.reduced, d = drop(fx.t), env = Math.min(1, d + 0.15) * (1 - fx.phase * 0.35);
      if (reduced) {                                         // a gentle tint pulse only
        const a = 0.12 * Math.sin(Math.PI * fx.phase);
        if (a > 0.002) { ctx.fillStyle = R.col(R.VIOLET, R.mix, a); ctx.fillRect(0, 0, R.W, R.H); }
        return;
      }
      const { x, y } = anchor(s, R.W, R.H);
      const rMax = Math.hypot(R.W, R.H) * 0.7, gap = rMax / 5, run = fx.t * 520;
      ctx.globalCompositeOperation = 'lighter'; ctx.lineWidth = 3;
      for (let i = 0; i < 6; i++) {
        const r = (run + i * gap) % rMax, fade = 1 - r / rMax;
        if (r < 6) continue;
        ctx.strokeStyle = R.col(i & 1 ? R.PINK : R.VIOLET, R.mix, 0.16 * env * fade * fade);
        ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.stroke();
      }
      const halo = ctx.createRadialGradient(x, y, 0, x, y, R.W * 0.32);
      halo.addColorStop(0, R.col(R.VIOLET, R.mix, 0.1 * env)); halo.addColorStop(1, R.col(R.VIOLET, R.mix, 0));
      ctx.fillStyle = halo; ctx.fillRect(0, 0, R.W, R.H);
    },
    /** The layer under: the previous frame, scaled around the ball, faint, 'lighter'. Captured every other frame. */
    post(ctx, s, fx, R) {
      if (R.reduced || s.reduced || typeof R.frame !== 'function') return;
      const n = fx.data.frames = (fx.data.frames | 0) + 1;
      const a = GHOST_A * Math.min(1, fx.t / 0.15) * (1 - smooth((fx.t - (DUR - 0.3)) / 0.3));
      if (fx.data.ghostOk && ghost && ghost.width === R.cw && ghost.height === R.ch && a > 0.004) {
        const { x, y } = anchor(s, R.W, R.H);
        const cx = R.ox + x * R.scale, cy = R.oy + y * R.scale;
        ctx.globalCompositeOperation = 'lighter'; ctx.globalAlpha = a;
        ctx.translate(cx, cy); ctx.scale(GHOST_SCALE, GHOST_SCALE); ctx.translate(-cx, -cy);
        ctx.drawImage(ghost, 0, 0);
      }
      if (n & 1 || !fx.data.ghostOk) { ghost = R.frame(ghost); fx.data.ghostOk = true; }
    },
  },
  sound(synth, fx) {
    const t = synth.now, down = Math.min(MAX_DROP, fires++);
    const root = synth.ROOT_HZ / 4 * synth.SEMI(-down);       // C3 and down a semitone per DEEPER
    synth.duck(0.5, 0.6, 0.5);
    synth.play([
      synth.tone(root, 0.5, 0.11, { wave: 'triangle', attack: 0.04, lp: 900, lpTo: 300, wet: true }),
      synth.tone(root / 2, 0.9, 0.13, { wave: 'triangle', attack: 0.05, lp: 520, lpTo: 180, at: 0.35, wet: true }),
      synth.tone(root / 4, 1.1, 0.06, { wave: 'sine', attack: 0.2, at: 0.35 }),                                   // the sub under the second step
    ], t, synth.dest);
  },
};
