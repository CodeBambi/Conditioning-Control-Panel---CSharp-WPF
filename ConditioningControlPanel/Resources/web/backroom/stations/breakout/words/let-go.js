/* ============================================================================
 * stations/breakout/words/let-go.js - the LET GO trigger (soft, 1.5 s).
 * "Your hands come off. The game plays itself for a moment."
 *
 * Play: g.mod.autopilot for the whole word, so the sim's movePaddle glides the
 * paddle under the lowest live ball and ignores the player's input; the ball
 * is safe throughout. When the word ends the sim drops autopilot and the
 * player's input sets the paddle base directly, so control simply returns.
 * Screen: a soft pink and white bloom rides each live ball (rising over 0.3 s,
 * holding, fading over the last 0.4 s) and a faint mint tether runs from the
 * paddle up to the ball it follows. The scaffold stamps the word itself.
 * Sound: a rising airy chord that holds, then one soft bell when control
 * returns; the bed dips a little. Reduced motion: a static glow, plus the sound.
 * Contract: see ../word-fx.js (sim / render / sound hooks, fx and R shapes).
 * ==========================================================================*/

const DUR = 1.5, RISE = 0.3, FADE = 0.4, BLOOM_R = 40;
const PALE = [255, 214, 236];

const clamp01 = v => v < 0 ? 0 : v > 1 ? 1 : v;
const smooth = t => { t = clamp01(t); return t * t * (3 - 2 * t); };
/** The bloom's strength: up over RISE, held, down over the last FADE. */
export const bloomAt = t => t < RISE ? smooth(t / RISE) : t > DUR - FADE ? 1 - smooth((t - (DUR - FADE)) / FADE) : 1;

export default {
  key: "LET GO", heavy: false, dur: DUR,
  sim: {
    start(g, fx, api) { /* nothing to prime */ },
    tick(g, fx, dt, api) { g.mod.autopilot = true; g.mod.safe = true; },
    end(g, fx, api) { /* autopilot drops with the next freshMod(); the player's next input takes the paddle */ },
  },
  render: {
    world(ctx, s, fx, R) { /* nothing in the world */ },
    over(ctx, s, fx, R) {
      const a = R.reduced ? 0.6 : bloomAt(fx.t);
      if (a <= 0.003) return;
      const p = s.paddle, balls = s.balls || [];
      let low = null;
      for (const b of balls) if (!b.lost && !b.stuck && !b.falling && (!low || b.y > low.y)) low = b;
      // The tether: a thin mint thread from the paddle's top to the ball it follows, so "the paddle follows the ball" reads.
      if (low && p) {
        const px = p.x, py = p.y - p.h / 2;
        ctx.lineCap = 'round';
        ctx.strokeStyle = R.col(R.MINT, R.mix, 0.09 * a); ctx.lineWidth = 5;
        ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(low.x, low.y + low.r); ctx.stroke();
        ctx.strokeStyle = R.col(R.MINT, R.mix, 0.25 * a); ctx.lineWidth = 1.4;
        ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(low.x, low.y + low.r); ctx.stroke();
        ctx.fillStyle = R.col(R.MINT, R.mix, 0.5 * a); ctx.beginPath(); ctx.arc(px, py, 2.6, 0, 7); ctx.fill();
      }
      // The bloom: a soft pink and white glow riding every live ball.
      const r = R.reduced ? BLOOM_R : BLOOM_R * (0.55 + 0.45 * a);
      for (const b of balls) {
        if (b.lost || b.ghost) continue;
        const grd = ctx.createRadialGradient(b.x, b.y, b.r * 0.6, b.x, b.y, r);
        grd.addColorStop(0, R.col(R.WHITE, 1, 0.34 * a)); grd.addColorStop(0.4, R.col(PALE, R.mix, 0.2 * a)); grd.addColorStop(1, R.col(R.PINK, R.mix, 0));
        ctx.fillStyle = grd; ctx.beginPath(); ctx.arc(b.x, b.y, r, 0, 7); ctx.fill();
      }
    },
    post(ctx, s, fx, R) { /* nothing in canvas space */ },
  },
  sound(synth, fx) {
    const { tone, play, now, dest, SEMI, ROOT_HZ } = synth;
    synth.duck(0.25, 1.2, 0.5);
    const voices = [];
    // The chord: three airy voices in a major spread, each drifting slightly upward as it holds.
    [[0, 'triangle', 0.4, 0.03], [4, 'sine', 0.5, 0.026], [7, 'triangle', 0.6, 0.024]].forEach(([semi, wave, pan, level], i) => {
      const hz = ROOT_HZ * SEMI(semi);
      voices.push(tone(hz, 1.3, level, { hzTo: hz * 1.025, wave, attack: 0.42, lp: 2400, wet: true, pan, at: i * 0.05 }));
    });
    // The bell: one soft sine with a fast decay as control returns, and a faint partial for the ring.
    const bell = ROOT_HZ * 4;
    voices.push(tone(bell, 0.5, 0.05, { at: 1.4, attack: 0.012, hzTo: bell * 0.996, wet: true, pan: 0.5 }));
    voices.push(tone(bell * 2.76, 0.16, 0.012, { at: 1.4, attack: 0.02, wet: true, pan: 0.5 }));
    play(voices, now, dest);
  },
};
