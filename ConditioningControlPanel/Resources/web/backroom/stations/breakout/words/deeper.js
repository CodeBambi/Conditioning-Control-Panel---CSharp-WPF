/* ============================================================================
 * stations/breakout/words/deeper.js - the DEEPER trigger.
 * A floor under the floor: zoom 8% toward the ball while rings rush outward behind the bricks; one descending octave step, each DEEPER lower than the last; the saturation floor rises a notch.
 * Contract: see ../word-fx.js (sim / render / sound hooks, fx and R shapes).
 * STUB: a placeholder that only keeps the ball safe and stamps the word; the
 * real effect is written by the owner of this file.
 * ==========================================================================*/

export default {
  key: "DEEPER", heavy: true, dur: 1.2,
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
