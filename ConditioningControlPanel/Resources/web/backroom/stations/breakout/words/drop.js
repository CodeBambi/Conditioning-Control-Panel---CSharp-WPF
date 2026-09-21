/* DROP: a brief downward nudge and a 0.3 s vertical ball lane. No frame freeze or echoes.
 * Only the ball that SPOKE the word drops (owner, 2026-09-21: with multiball every other ball on the field snapped
 * straight down mid flight, up to a full reversal, and read as a physics bug). */
const LANE_S = 0.3;

const liveBall = b => !b.stuck && !b.lost && !b.falling && !b.orbit;
const speedOf = (b, g) => Math.hypot(b.vx, b.vy) || g.speed || 300;

/** The straight drop is over: any ball still vertical gets a sideways component back (normalise keeps the speed). */
function release(g, fx) {
  if (fx.data.released) return;
  fx.data.released = true;
  for (const { ball: b, side } of fx.data.lanes) {
    if (!g.balls.includes(b) || !liveBall(b) || Math.abs(b.vx) > 1) continue;
    const sp = speedOf(b, g);
    b.vx = side * sp * 0.6; b.vy = (b.vy < 0 ? -1 : 1) * sp * 0.8;
  }
}

export default {
  key: 'DROP', heavy: true, dur: 0.7,
  sim: {
    start(g, fx, api) {
      fx.data.lanes = []; fx.data.released = false;
      // Keep play continuous; the downward lane carries the effect.
      // The speaker: the live ball nearest the word. It has just hit that brick, so its turn reads as the hit's.
      const live = g.balls.filter(liveBall), near = b => Number.isFinite(fx.x) && Number.isFinite(fx.y) ? Math.hypot(b.x - fx.x, b.y - fx.y) : 0;
      const speaker = live.reduce((best, b) => (!best || near(b) < near(best) ? b : best), null);
      for (const b of speaker ? [speaker] : []) {
        const speed = speedOf(b, g), side = b.vx < 0 ? -1 : b.vx > 0 ? 1 : (api.rng() < .5 ? -1 : 1);
        fx.data.lanes.push({ ball: b, side });
        b.vx = 0; b.vy = speed;                                   // straight down its lane
      }
    },
    tick(g, fx, dt, api) {
      g.mod.safe = true;
      if (fx.t >= LANE_S) release(g, fx);
    },
    end(g, fx, api) { release(g, fx); },
  },
  render: {
    world(ctx, s, fx, R) {
      if (R.reduced) return;
      // A tiny downward nudge, no shake, tilt, frame copies or simulation hold.
      ctx.translate(0, 0.8 * Math.exp(-24 * fx.t));
    },
    over(ctx, s, fx, R) {},
    post(ctx, s, fx, R) {},
  },
  sound(synth, fx) {
    const t = synth.now;
    synth.play([
      synth.tone(90, 0.18, 0.5, { hzTo: 45, attack: 0.02 }),                                          // the body
      synth.noise(220, 0.14, 0.22, { type: 'lowpass', hzTo: 70, q: 0.7 }),                              // the floor
      synth.noise(2600, 0.35, 0.14, { type: 'highpass', hzTo: 7000, q: 0.6, attack: 0.88, wet: true, at: 0.06 }),   // the reversed cymbal, into the room
    ], t, synth.dest);
    synth.duck(0.6, 0.5, 0.4);
  },
};
