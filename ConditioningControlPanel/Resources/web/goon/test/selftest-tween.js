// Self-contained pass over the juice maths (exec/tween.js) and the DOM-free
// guarantees of exec/motion.js.
//
//   node Resources/web/goon/test/selftest-tween.js
//
// Pins: easings are total and hit their ends, backOut overshoots, the spring
// settles, keyframes carry offsets 0..1, exits end on the authored hold, the
// shake decays to 0 and starts at its amplitude, the scheduler is cancel-safe,
// and every motion call is a harmless no-op on a node without .animate.

import {
  TIMING, clamp01, linear, quadOut, cubicOut, cubicIn, cubicInOut, expoOut, backOut, backIn, elasticOut,
  spring, springSettleS, wobble, toKeyframes, exitWindow, shakePath, burstVectors, createTweens,
} from '../exec/tween.js';
import * as motion from '../exec/motion.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const near = (a, b, eps = 1e-6) => Math.abs(a - b) <= eps;

/* ---- easings: total, clamped, ends pinned */
{
  const eases = { linear, quadOut, cubicOut, cubicIn, cubicInOut, expoOut, backOut, backIn, elasticOut };
  for (const [name, f] of Object.entries(eases)) {
    ok(near(f(0), 0), `${name}(0) = 0`, String(f(0)));
    ok(near(f(1), 1), `${name}(1) = 1`, String(f(1)));
    ok(near(f(-3), f(0)) && near(f(7), f(1)), `${name} clamps its input`);
    ok(Number.isFinite(f(NaN)) && Number.isFinite(f(undefined)) && Number.isFinite(f('x')), `${name} is total on junk`);
  }
  ok(clamp01(NaN) === 0 && clamp01(Infinity) === 1 && clamp01(-1) === 0 && clamp01(0.3) === 0.3, 'clamp01 is total');
  let peak = 0;
  for (let u = 0; u <= 1; u += 0.01) peak = Math.max(peak, backOut(u));
  ok(peak > 1.05 && peak < 1.2, 'backOut overshoots ~10% and comes back', String(peak));
  let dip = 0;
  for (let u = 0; u <= 1; u += 0.01) dip = Math.min(dip, backIn(u));
  ok(dip < -0.05, 'backIn winds back first (anticipation)', String(dip));
  let ePeak = 0;
  for (let u = 0; u <= 1; u += 0.005) ePeak = Math.max(ePeak, elasticOut(u));
  ok(ePeak > 1.01, 'elasticOut rings past 1', String(ePeak));
  ok(near(elasticOut(0.999), 1, 0.01), 'elasticOut has settled at the end');
  for (let u = 0; u <= 1.0001; u += 0.05) {
    ok(cubicOut(u) >= u - 1e-9, `cubicOut is front-loaded at ${u.toFixed(2)}`);
  }
}

/* ---- spring */
{
  ok(spring(0) === 0 && spring(-1) === 0 && spring(NaN) === 0, 'spring starts at 0 and is total');
  let peak = 0;
  for (let s = 0; s < 1; s += 0.002) peak = Math.max(peak, spring(s));
  ok(peak > 1.02 && peak < 1.3, 'the default spring overshoots a little', String(peak));
  ok(near(spring(3), 1, 0.001), 'and rests at 1');
  const settle = springSettleS();
  ok(settle > 0.15 && settle < 0.8, 'it settles inside an entrance-sized window', String(settle));
  let crit = 0;
  for (let s = 0; s < 2; s += 0.002) crit = Math.max(crit, spring(s, { damping: 1 }));
  ok(crit <= 1 + 1e-9, 'critically damped never overshoots', String(crit));
}

/* ---- wobble */
{
  ok(near(wobble(0), 1) && wobble(1) === 0 && wobble(2) === 0, 'wobble starts at 1 and ends at 0');
  let late = 0;
  for (let u = 0.8; u < 1; u += 0.01) late = Math.max(late, Math.abs(wobble(u)));
  ok(late < 0.05, 'wobble has died down by the end', String(late));
}

/* ---- keyframes */
{
  const kf = toKeyframes(5, (u) => ({ opacity: u }));
  ok(kf.length === 5, 'toKeyframes makes n frames');
  ok(kf[0].offset === 0 && kf[4].offset === 1, 'offsets run 0..1');
  ok(kf.every((k, i) => i === 0 || k.offset > kf[i - 1].offset), 'offsets strictly increase');
  ok(toKeyframes(1, () => ({})).length === 2 && toKeyframes(500, () => ({})).length === 60, 'n clamps to 2..60');
  ok(near(kf[2].opacity, 0.5), 'the mapper sees linear time');
}

/* ---- exits end on the hold: motion never extends an effect */
{
  for (const hold of [0, 90, 300, 900, 1400, 5000, 12000]) {
    const w = exitWindow(hold, 200);
    ok(w.atMs + w.outMs <= hold + 1e-9, `exit ends by the authored hold (${hold})`, JSON.stringify(w));
    ok(w.outMs <= 200 && w.outMs >= 0 && w.atMs >= 0, `exit length is bounded (${hold})`, JSON.stringify(w));
  }
  ok(exitWindow(5000, 200).outMs === 200, 'a long hold gets the full exit');
  ok(exitWindow(300, 200).outMs === 100, 'a short hold gives up at most a third of itself');
  ok(exitWindow('junk', 200).outMs === 0, 'a junk hold gets no exit');
}

/* ---- shake: real pixels, decays to rest */
{
  let seed = 7;
  const rng = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
  const p = shakePath(6, 8, rng);
  ok(p.length === 8, 'shakePath has the asked steps');
  ok(p[p.length - 1].x === 0 && p[p.length - 1].y === 0, 'shake ends at rest');
  ok(p.every((q) => Math.abs(q.x) <= 6 && Math.abs(q.y) <= 6), 'never past its amplitude');
  const early = Math.max(Math.abs(p[1].x), Math.abs(p[1].y));
  const late2 = Math.max(Math.abs(p[6].x), Math.abs(p[6].y));
  ok(late2 <= early + 1e-9 || late2 < 0.5, 'and it decays', `${early} -> ${late2}`);
  ok(shakePath(-3).every((q) => q.x === 0 && q.y === 0), 'a negative shake is no shake');
  ok(motion.shakeForStrength(0) === 2 && motion.shakeForStrength(100) === motion.MOTION.SHAKE_MAX_PX,
    'shake px maps 2..SHAKE_MAX_PX over the bubble strength');
  ok(motion.MOTION.SHAKE_MAX_PX <= 8, 'the biggest shake stays small (real pixels, no gain stage)');
}

/* ---- burst vectors */
{
  const v = burstVectors(12, { minR: 40, maxR: 100, rng: () => 0.5 });
  ok(v.length === 12, 'burstVectors gives n');
  ok(v.every((q) => { const r = Math.hypot(q.dx, q.dy); return r >= 39 && r <= 101; }), 'every particle travels inside the radius band');
  ok(burstVectors(-2).length === 0, 'a negative count is empty');
}

/* ---- scheduler */
{
  const tw = createTweens();
  const seen = [];
  let done = 0;
  tw.add({ ms: 100, ease: linear, from: 10, to: 20, onUpdate: (v) => seen.push(v), onDone: () => done++ });
  tw.step(1000);
  ok(near(seen[0], 10), 'the first step is t = 0 (no skipped start)', String(seen[0]));
  tw.step(1050);
  ok(near(seen[1], 15), 'midway is midway', String(seen[1]));
  const left = tw.step(1100);
  ok(near(seen[2], 20) && done === 1 && left === 0, 'it lands and calls onDone once');
  tw.step(1200);
  ok(done === 1 && seen.length === 3, 'and nothing after');

  let cancelledDone = 0;
  const h = tw.add({ ms: 100, onDone: () => cancelledDone++ });
  tw.step(0);
  h.cancel();
  h.cancel();
  tw.step(500);
  ok(cancelledDone === 0 && h.done, 'cancel is idempotent and onDone never fires');

  let after = 0;
  tw.add({ ms: 50, onUpdate: () => { throw new Error('boom'); } });
  tw.add({ ms: 50, onUpdate: () => { after++; } });
  tw.step(0);
  ok(after === 1 && tw.size === 1, 'a throwing tween dies alone');
  tw.cancelAll();
  ok(tw.size === 0, 'cancelAll empties it');

  const d = [];
  tw.add({ ms: 10, delayMs: 100, onUpdate: (v) => d.push(v) });
  tw.step(0); tw.step(50);
  ok(d.length === 0, 'a delayed tween waits');
  tw.step(100);
  ok(d.length === 1, 'and starts on time');
}

/* ---- timings stay inside the brief */
{
  const M = motion.MOTION;
  for (const k of ['FLASH_IN_MS', 'PANE_IN_MS', 'CARD_IN_MS', 'VIDEO_IN_MS', 'GLOW_IN_MS', 'FADE_IN_MS']) {
    ok(M[k] >= TIMING.IN_MIN_MS && M[k] <= TIMING.IN_MAX_MS, `${k} is an entrance (150-350 ms)`, String(M[k]));
  }
  for (const k of ['FLASH_OUT_MS', 'PANE_OUT_MS', 'CARD_OUT_MS', 'FADE_OUT_MS']) {
    ok(M[k] >= TIMING.OUT_MIN_MS && M[k] <= TIMING.OUT_MAX_MS, `${k} is an exit (120-250 ms)`, String(M[k]));
  }
  for (const [k, c] of Object.entries(motion.TINTS)) {
    const m = /rgb\((\d+),\s*(\d+),\s*(\d+)\)/.exec(c);
    ok(!!m && !(+m[1] > 240 && +m[2] > 240 && +m[3] > 240), `tint ${k} is not white`, c);
  }
}

/* ---- motion is a no-op without a DOM or without .animate */
{
  const bare = { style: { setProperty() {} }, isConnected: true };
  let threw = false;
  try {
    ok(motion.landFlash(bare) === null, 'landFlash on a node with no .animate is null');
    ok(motion.leaveFlash(bare) === null, 'leaveFlash too');
    ok(motion.growPane(bare, { x: 1, y: 2 }) === null && motion.shrinkPane(bare) === null, 'panes too');
    ok(motion.shake(bare, 5) === null, 'shake too');
    ok(motion.burst(null, 0, 0) === 0 && motion.ring(null, 0, 0) === null, 'burst/ring without a host');
    motion.cancelMotion(bare);
    motion.cancelMotion(null);
    ok(motion.fadeOut(bare) === null && motion.thudIn(bare) === null && motion.zoomIn(bare) === null, 'generic helpers too');
  } catch (e) { threw = e; }
  ok(!threw, 'nothing throws headless', String(threw && threw.message));

  // A fake animating node: the entrance must hand WAAPI keyframes that start
  // invisible and end at the asked opacity, with a transform on every frame.
  const calls = [];
  const fake = {
    style: { setProperty() {} },
    isConnected: true,
    animate(kf, opts) {
      calls.push({ kf, opts });
      return { addEventListener() {}, cancel() {} };
    },
  };
  motion.landFlash(fake, { dx: 120, dy: -80, rot: 4, opacity: 0.8 });
  const c = calls[0];
  ok(!!c && c.opts.duration === motion.MOTION.FLASH_IN_MS, 'landFlash runs FLASH_IN_MS');
  ok(!!c && c.kf[0].opacity === 0 && near(c.kf[c.kf.length - 1].opacity, 0.8), 'from invisible to its opacity');
  ok(!!c && c.kf.every((k) => /translate\(-50%, -50%\)/.test(k.transform)), 'every frame keeps the CSS anchor');
  ok(!!c && /translate\(120px, -80px\)/.test(c.kf[0].transform), 'it starts at the spawn offset');
  ok(!!c && /translate\(0px, 0px\)/.test(c.kf[c.kf.length - 1].transform), 'and ends home');
  const scales = c ? c.kf.map((k) => +/scale\(([\d.]+)\)/.exec(k.transform)[1]) : [];
  ok(Math.max(...scales) > 1.05, 'it overshoots on the way in', String(Math.max(...scales)));
  ok(near(scales[scales.length - 1], 1, 0.001), 'and settles at 1');
  ok(motion.play(fake, [{}, {}], { duration: 10 }) !== null, 'play returns the animation');
  motion.leaveFlash(fake, { opacity: 0.8 });
  const out = calls[calls.length - 1];
  ok(out.opts.fill === 'forwards' && out.kf[out.kf.length - 1].opacity === 0, 'leaveFlash ends invisible and stays so');
}

console.log(failures
  ? `selftest-tween: ${n - failures}/${n} checks passed ${failures} FAILURE(S)`
  : `selftest-tween: ${n}/${n} checks passed`);
process.exit(failures ? 1 : 0);
