/* ============================================================================
 * stations/breakout/words/let-go.js - the LET GO trigger.
 * Hands off: the paddle glides on its own under the ball, the HUD fades, a bloom rides the ball, a rising airy chord; control fades back in.
 * Contract: see ../word-fx.js (sim / render / sound hooks, fx and R shapes).
 * STUB: a placeholder that only keeps the ball safe and stamps the word; the
 * real effect is written by the owner of this file.
 * ==========================================================================*/

export default {
  key: "LET GO", heavy: false, dur: 1.5,
  sim: {
    start(g, fx, api) { /* nothing yet */ },
    tick(g, fx, dt, api) { g.mod.safe = true; },
    end(g, fx, api) { /* nothing yet */ },
  },
  render: {
    world(ctx, s, fx, R) { /* nothing yet */ },
    over(ctx, s, fx, R) {
      const a = 0.5 * (1 - fx.phase);
      ctx.save(); ctx.font = `900 40px ${R.FONT}`; ctx.textAlign = "center"; ctx.textBaseline = "middle";
      ctx.fillStyle = R.col(R.PINK, R.mix, a); ctx.fillText(fx.word, R.W / 2, R.H * 0.45); ctx.restore();
    },
    post(ctx, s, fx, R) { /* nothing yet */ },
  },
  sound(synth, fx) { /* nothing yet */ },
};
