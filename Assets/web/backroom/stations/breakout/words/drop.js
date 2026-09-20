/* ============================================================================
 * stations/breakout/words/drop.js - the DROP trigger.
 * Knees give out. A frame goes missing and the room rings: the sim holds for
 * three frames (a hit stop, paddle included), then a hard shake, a 3 degree tilt
 * that rights itself like a damped spring, and two ghost echoes of the whole
 * field about 40 ms apart with a cheap chromatic split (the first ghost is
 * tinted warm and drawn left, the second cool and drawn right, both 'screen').
 * Sound: a body thud (sine 90 -> 45 Hz under a low-passed noise burst) and a
 * short reversed cymbal that lands into the room. Play: every live ball drops
 * straight down its lane for 0.3 s, then gets its sideways component back.
 * Contract: see ../word-fx.js (sim / render / sound hooks, fx and R shapes).
 *
 * Notes on the scaffold: the renderer restores the ctx after each hook, so a
 * rotate in render.world does not reach the world; the tilt is done in
 * render.post on a copy of the frame instead (the copy feeds the ghosts too).
 * The hold is the sim's own hit stop (g.hitStopMs): step returns before the
 * paddle moves and before wordSim.advance, so fx.t stays 0 for the held frames
 * and the hooks read "fx.t > 0" as "the freeze has ended". (g.freeze would do
 * the same but also stalls the combo hit-stop countdown the sim tests measure.)
 * ==========================================================================*/

const FREEZE_MS = 50;                   // three frames of nothing
const LANE_S = 0.3;                     // the straight drop
const TILT = 3 * Math.PI / 180;         // the tilt, rad
const GHOST_GAP_S = 0.04;               // the echoes trail this far apart
const WARM = 'rgb(255,120,150)', COOL = 'rgb(110,180,255)';

/* The frame copies live on the module, not on fx.data: one set of canvases for every DROP, never re-allocated. */
const cache = { cur: null, g1: null, g2: null };

const liveBall = b => !b.stuck && !b.lost && !b.falling && !b.orbit;
const speedOf = (b, g) => Math.hypot(b.vx, b.vy) || g.speed || 300;

/** The straight drop is over: any ball still vertical gets a sideways component back (normalise keeps the speed). */
function release(g, fx) {
  if (fx.data.released) return;
  fx.data.released = true;
  for (const b of g.balls) {
    if (!liveBall(b) || Math.abs(b.vx) > 1) continue;
    const sp = speedOf(b, g);
    b.vx = (fx.data.side || 1) * sp * 0.6; b.vy = (b.vy < 0 ? -1 : 1) * sp * 0.8;
  }
}

function tint(c, color) {
  const x = c.getContext('2d');
  x.save(); x.setTransform(1, 0, 0, 1, 0, 0); x.globalCompositeOperation = 'multiply'; x.fillStyle = color; x.fillRect(0, 0, c.width, c.height); x.restore();
}

export default {
  key: 'DROP', heavy: true, dur: 0.7,
  sim: {
    start(g, fx, api) {
      fx.data.side = 0; fx.data.released = false;
      g.hitStopMs = Math.max(g.hitStopMs || 0, FREEZE_MS);
      for (const b of g.balls) {
        if (!liveBall(b)) continue;
        if (!fx.data.side) fx.data.side = b.vx < 0 ? -1 : b.vx > 0 ? 1 : 0;
        b.vx = 0; b.vy = speedOf(b, g);                                   // straight down its lane
      }
      if (!fx.data.side) fx.data.side = api.rng() < 0.5 ? -1 : 1;
    },
    tick(g, fx, dt, api) {
      g.mod.safe = true;
      if (fx.t >= LANE_S) release(g, fx);
    },
    end(g, fx, api) { release(g, fx); },
  },
  render: {
    world(ctx, s, fx, R) {
      if (fx.t > 0 && !fx.data.kicked) { fx.data.kicked = true; R.cam.kick(14, 1, 0.02); }   // the freeze just ended: the room jolts
    },
    over(ctx, s, fx, R) {
      if (!R.reduced) return;                                             // reduced motion: a short dark flash stands in for the jolt
      const a = 0.55 * Math.max(0, 1 - fx.t / 0.18);
      if (a > 0.01) { ctx.fillStyle = `rgba(0,0,0,${a.toFixed(3)})`; ctx.fillRect(0, 0, R.W, R.H); }
    },
    post(ctx, s, fx, R) {
      if (R.reduced || fx.t <= 0) return;                                 // the freeze frame stays exactly as it is
      const d = fx.data, t = fx.t, k = 1 - fx.phase, cw = R.cw, ch = R.ch;
      if (k <= 0.02) return;
      cache.cur = R.frame(cache.cur);                                     // this frame, for the tilt and as the next echo
      const tilt = (d.side || 1) * TILT * Math.exp(-7 * t) * Math.cos(9 * t);
      if (Math.abs(tilt) > 0.0015) {
        ctx.fillStyle = '#000'; ctx.fillRect(0, 0, cw, ch);
        ctx.translate(cw / 2, ch / 2); ctx.rotate(tilt); ctx.translate(-cw / 2, -ch / 2);
        ctx.drawImage(cache.cur, 0, 0);
      }
      const px = R.scale || 1;
      ctx.globalCompositeOperation = 'screen';
      if (cache.g1) { ctx.globalAlpha = 0.35 * k; ctx.drawImage(cache.g1, -3 * px, 2 * px); }
      if (cache.g2) { ctx.globalAlpha = 0.2 * k; ctx.drawImage(cache.g2, 3 * px, 5 * px); }
      ctx.globalAlpha = 1; ctx.globalCompositeOperation = 'source-over';
      // Every GHOST_GAP_S this frame becomes the first echo and the first becomes the second (warm, then warm x cool).
      d.capAcc = (d.capAcc == null ? GHOST_GAP_S : d.capAcc) + (R.dt || 1 / 60);
      if (d.capAcc >= GHOST_GAP_S) {
        d.capAcc = 0;
        const old = cache.g2; cache.g2 = cache.g1; cache.g1 = cache.cur; cache.cur = old;
        try { tint(cache.g1, WARM); if (cache.g2) tint(cache.g2, COOL); } catch (e) { /* no tint, still an echo */ }
      }
    },
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
