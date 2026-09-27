/* ============================================================================
 * stations/breakout/words/relax.js - the RELAX trigger (soft, 3 s).
 * "A pink breath rolls over the field and everything eases."
 *
 * Screen: pink mist blooms from the word and drifts over the field (about 120
 * pooled dots, spawned in small puffs over the first 0.8 s), under a pale pink
 * veil that grows from the word to cover the field, holds, then thins over the
 * last second; the borders soften with a light pink vignette during the hold.
 * Play: slowmo eases to 0.85x over 0.8 s, holds until t = 2 s and ramps back
 * over the last second; the paddle widens 20% (eased in and out); the ball
 * keeps its base speed so the slowdown is not multiplied; the ball is
 * safe throughout. Sound: a slow low-passed exhale under a warm detuned pad,
 * the bed covered a little. Reduced motion: the veil and the sound only.
 * Contract: see ../word-fx.js (sim / render / sound hooks, fx and R shapes).
 * ==========================================================================*/

const DUR = 3, SLOW_IN = 0.8, HOLD_TO = 2, RAMP = DUR - HOLD_TO;
const GROW = 0.8, PUFFS = 10, PUFF_N = 12, PUFF_EVERY = 0.08;      // 10 x 12 = 120 dots at most
const PALE = [255, 196, 226];
let mistLayer = null, mistKey = null;

const clamp01 = v => v < 0 ? 0 : v > 1 ? 1 : v;
const smooth = t => { t = clamp01(t); return t * t * (3 - 2 * t); };
/** 0 -> 1 over the first `inS`, 1 through the hold, 1 -> 0 over the last `outS`. */
const window_ = (t, inS, outS) => t < inS ? smooth(t / inS) : t > DUR - outS ? 1 - smooth((t - (DUR - outS)) / outS) : 1;
/** Smoothly enter and leave a gentle 15% slowdown. */
export const timeScaleAt = t => t < SLOW_IN ? 1 - 0.15 * smooth(t / SLOW_IN) : t < HOLD_TO ? 0.85 : 0.85 + 0.15 * smooth((t - HOLD_TO) / RAMP);

export default {
  key: "RELAX", heavy: false, dur: DUR,
  sim: {
    start(g, fx, api) { fx.data.puffs = 0; fx.data.nextPuff = 0; },
    tick(g, fx, dt, api) {
      const t = fx.t, m = g.mod;
      m.safe = true;
      if (!g.reduced) {
        const ts = timeScaleAt(t);
        m.timeScale = Math.min(m.timeScale, ts);
      }
      m.paddleW *= 1 + 0.2 * window_(t, 0.3, 0.5);
    },
    end(g, fx, api) { /* the mods reset with the next freshMod(); nothing lingers */ },
  },
  render: {
    world(ctx, s, fx, R) { /* nothing in the world: the breath sits over it */ },
    over(ctx, s, fx, R) {
      const t = fx.t, d = fx.data, W = R.W, H = R.H, x = fx.x, y = fx.y;
      // The mist: small puffs over the first 0.8 s, each a ring of slow pale dots that widens as the breath rolls out.
      if (!R.reduced && d.puffs < PUFFS && t >= d.nextPuff && t < GROW + 0.2) {
        const spread = 30 + 300 * (d.puffs / PUFFS), rgb = d.puffs % 3 === 1 ? R.PINK : PALE;   // mostly pale, a little pink
        for (let i = 0; i < PUFF_N; i++) {                          // each dot lands on its own spot so the mist scatters, not clumps
          const a = R.rng() * 6.283, k = spread * (0.35 + 0.65 * R.rng());
          R.P.burst(x + Math.cos(a) * k, y + Math.sin(a) * k * 0.85, rgb, 1, 34, 1.5, { rise: 20, gv: -12, r0: 3.5, r1: 5 });
        }
        d.puffs++; d.nextPuff = t + PUFF_EVERY;
      }
      const grow = smooth(t / GROW), fade = t > HOLD_TO ? 1 - smooth((t - HOLD_TO) / RAMP) : 1;
      const reach = Math.hypot(Math.max(x, W - x), Math.max(y, H - y));
      const r = 30 + (reach - 30) * grow, a = 0.22 * Math.min(1, grow * 1.4) * fade;
      const vig = window_(t, 0.6, 1.2) * 0.16;
      const key = [W,H,x,y,r,a,vig,R.mix].join('|');
      // The hold has identical gradients; reuse them without losing any mist particles.
      if (mistLayer && mistKey === key) { ctx.drawImage(mistLayer,0,0,W,H); return; }
      // Rasterize both soft gradients on a small surface, then composite once.
      const target = ctx;
      if (!mistLayer && typeof document !== 'undefined') { mistLayer = document.createElement('canvas'); mistLayer.width = 320; mistLayer.height = 180; }
      if (mistLayer) { ctx = mistLayer.getContext('2d', { willReadFrequently: true }); ctx.setTransform(1,0,0,1,0,0); ctx.clearRect(0,0,320,180);
      ctx.setTransform(320/W,0,0,180/H,0,0); }
      // The veil: a pale pink breath that grows from the word to cover the field, then thins over the last second.
      if (a > 0.003) {
        const veil = ctx.createRadialGradient(x, y, 0, x, y, r);
        veil.addColorStop(0, R.col(PALE, R.mix, a)); veil.addColorStop(0.5, R.col(PALE, R.mix, a * 0.8)); veil.addColorStop(1, R.col(PALE, R.mix, a * 0.25 * grow));
        ctx.fillStyle = veil; ctx.fillRect(0, 0, W, H);
      }
      // The edges soften: a light pink vignette during the hold.
      if (vig > 0.003) {
        const cx = W / 2, cy = H / 2;
        const edge = ctx.createRadialGradient(cx, cy, Math.min(W, H) * 0.34, cx, cy, Math.hypot(cx, cy));
        edge.addColorStop(0, R.col(PALE, R.mix, 0)); edge.addColorStop(1, R.col(PALE, R.mix, vig));
        ctx.fillStyle = edge; ctx.fillRect(0, 0, W, H);
      }
      if (mistLayer) { mistKey = key; target.drawImage(mistLayer,0,0,W,H); }
    },
    post(ctx, s, fx, R) { /* nothing in canvas space */ },
  },
  sound(synth, fx) {
    const { tone, noise, play, now, dest, SEMI, ROOT_HZ } = synth;
    synth.duck(0.35, 2.0, 1.0);                                   // the music feels covered, not gone
    const lo = ROOT_HZ / 2;
    play([
      // the exhale: low-passed air with a long attack and a longer release, darkening as it goes
      noise(1100, 1.4, 0.05, { type: 'lowpass', hzTo: 220, q: 0.7, attack: 0.3, wet: true, pan: 0.5 }),
      // the pad: three soft detuned voices a fifth and an octave apart, slow attack
      tone(lo * 0.997, 2.5, 0.032, { wave: 'triangle', attack: 0.3, lp: 1300, lpTo: 700, wet: true, pan: 0.42 }),
      tone(lo * SEMI(7) * 1.004, 2.5, 0.026, { wave: 'sine', attack: 0.34, lp: 1400, wet: true, pan: 0.58 }),
      tone(lo * 2 * 1.002, 2.4, 0.016, { wave: 'triangle', attack: 0.4, lp: 1600, lpTo: 800, wet: true, pan: 0.5, at: 0.15 }),
    ], now, dest);
  },
};
