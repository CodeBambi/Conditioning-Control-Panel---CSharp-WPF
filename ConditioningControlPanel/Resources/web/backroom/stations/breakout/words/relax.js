/* ============================================================================
 * stations/breakout/words/relax.js - the RELAX trigger.
 * A pink breath rolls over the field: mist particles bloom from the word and cover the screen, slowmo 0.5x for 2 s then a 1 s ramp back, an exhale pad; the paddle widens 20%.
 * Contract: see ../word-fx.js (sim / render / sound hooks, fx and R shapes).
 * STUB: a placeholder that only keeps the ball safe and stamps the word; the
 * real effect is written by the owner of this file.
 * ==========================================================================*/

export default {
  key: "RELAX", heavy: false, dur: 3,
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
