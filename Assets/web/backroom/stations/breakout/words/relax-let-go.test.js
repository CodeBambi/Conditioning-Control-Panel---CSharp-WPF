/* node --test words/relax-let-go.test.js - RELAX eases the game, LET GO takes the hands off; both keep the ball. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGame } from '../game.js';
import RELAX, { timeScaleAt } from './relax.js';
import LETGO, { bloomAt } from './let-go.js';

const seeded = (seed = 7) => () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
function make(opts = {}) {
  const events = [], calls = [];
  const audio = new Proxy({ beat: { spb: 60 / 96 } }, { get: (t, k) => k in t ? t[k] : (...a) => calls.push([k, ...a]) });
  const game = createGame({ rng: seeded(), audio, onEvent: (n, d) => events.push([n, d]), ...opts });
  // These effect tests begin after the opening grey phase.
  game.breakoutNow();
  for (let i = 0; i < 4; i++) game.step(0.1, {});
  events.length = 0;
  calls.length = 0;
  return { game, events, calls };
}
const run = (game, secs, input = {}) => { for (let i = 0; i < Math.round(secs * 60); i++) game.step(1 / 60, input); };
const near = (a, b, eps, msg) => assert.ok(Math.abs(a - b) <= eps, `${msg}: ${a} vs ${b}`);

/** A ctx that accepts any drawing call, and a particle pool that only counts. */
function fakeCtx() {
  const grad = { addColorStop() {} };
  return new Proxy({}, { get: (t, k) => k === 'createRadialGradient' || k === 'createLinearGradient' ? () => grad : (k in t ? t[k] : () => {}), set: () => true });
}
function fakeR(over = {}) {
  let n = 0;
  const P = { burst: (x, y, rgb, k) => { n += k; }, spray: (x, y, ang, sp, rgb, k) => { n += k; }, rects: (x, y, rgb, k) => { n += k; }, count: () => n };
  const col = (rgb, mix, a = 1) => `rgba(${rgb.join(',')},${a})`;
  return { W: 480, H: 720, mix: 1, dt: 1 / 60, fxDt: 1 / 60, P, col, PINK: [255, 105, 180], MINT: [120, 230, 200], WHITE: [255, 255, 255], FONT: 'sans-serif', reduced: false, rng: seeded(3), ...over };
}

test('RELAX: slowmo eases to 0.85, holds, ramps back to 1 without a second speed penalty', () => {
  const { game } = make({ words: ['RELAX'] });
  const s = game.snapshot();
  s.bricks = []; // Isolate effect expiry from new word collisions.
  const fx = game.fireWordNow('RELAX');
  assert.ok(fx && fx.key === 'RELAX');
  run(game, 0.05);
  assert.ok(s.timeScale > 0.99 && s.timeScale < 1, `easing in, not a pop: ${s.timeScale}`);
  run(game, 0.95);                                                  // t = 1.0 s, mid-hold
  near(s.timeScale, 0.85, 0.02, 'timeScale mid-word');
  near(s.mod.paddleW, 1.2, 0.01, 'paddleW mid-word');
  near(s.mod.ballSpeed, 1, 0.001, 'ballSpeed mid-word');
  assert.equal(s.mod.safe, true, 'safe mid-word');
  run(game, 1.5);                                                   // t = 2.5 s, halfway up the ramp
  assert.ok(s.timeScale > 0.85 && s.timeScale < 0.99, `ramping back: ${s.timeScale}`);
  assert.equal(s.mod.ballSpeed, 1);
  run(game, 0.6);                                                   // t = 3.1 s, over
  assert.equal(s.fx.active.length, 0, 'ended');
  assert.equal(s.timeScale, 1); assert.equal(s.mod.paddleW, 1); assert.equal(s.mod.ballSpeed, 1); assert.equal(s.mod.safe, false);
  assert.equal(timeScaleAt(0), 1); assert.equal(timeScaleAt(1), 0.85); assert.equal(timeScaleAt(3), 1);
});

test('RELAX under reduced motion keeps the clock at 1 but still widens the paddle and keeps the ball', () => {
  const { game } = make({ words: ['RELAX'] });
  const s = game.snapshot();
  game.setReduced(true);
  game.fireWordNow('RELAX'); run(game, 1);
  assert.equal(s.timeScale, 1); near(s.mod.paddleW, 1.2, 0.01, 'paddleW'); assert.equal(s.mod.safe, true);
});

test('RELAX render: about 120 mist dots over the word, none under reduced motion, no throw at any phase', () => {
  const ctx = fakeCtx();
  for (const reduced of [false, true]) {
    const R = fakeR({ reduced });
    const fx = { key: 'RELAX', word: 'RELAX', t: 0, dur: 3, phase: 0, heavy: false, x: 240, y: 300, data: {} };
    RELAX.sim.start({ mod: {} }, fx, {});
    for (let i = 0; i <= 180; i++) { fx.t = i / 60; fx.phase = Math.min(1, fx.t / 3); RELAX.render.world(ctx, {}, fx, R); RELAX.render.over(ctx, {}, fx, R); RELAX.render.post(ctx, {}, fx, R); }
    if (reduced) assert.equal(R.P.count(), 0, 'reduced: no particles');
    else assert.ok(R.P.count() >= 100 && R.P.count() <= 120, `budget: ${R.P.count()} dots`);
  }
});

test('LET GO shield catches misses, keeps manual control and expires after five seconds', () => {
  const { game } = make({ words: ['LET GO'] });
  const s = game.snapshot();
  const fx = game.fireWordNow('LET GO');
  assert.equal(fx.dur, 5);
  game.launchNow();
  const b = s.balls[0];
  b.x = 30; b.y = s.paddle.y; b.vx = 0; b.vy = 400;
  run(game, 0.1, { x: 400 });
  assert.equal(s.mod.autopilot, false);
  assert.equal(s.mod.shield, true);
  assert.equal(s.state, 'colour');
  assert.ok(b.vy < 0, 'shield reflects the missed ball');
  assert.ok(b.y < s.h - b.r, 'bounce happens above the bottom');
  near(s.paddle.x, 400, 1, 'manual control stays active');
  // Keep the ball clear of bricks so other word effects cannot extend protection.
  s.bricks.length = 0;
  let n = 0;
  while (s.fx.active.length && n++ < 400) game.step(1 / 60, { x: 400 });
  game.step(1 / 60, { x: 400 });
  assert.equal(s.mod.shield, false);
  assert.equal(s.mod.safe, false);
});

test('LET GO render: shield holds and flickers out; the hooks draw without throwing, with and without a live ball', () => {
  assert.equal(bloomAt(0), 1); assert.equal(bloomAt(4), 1); assert.equal(bloomAt(5), 0);
  assert.ok(bloomAt(4.5) < 1);
  const ctx = fakeCtx(), R = fakeR();
  const paddle = { x: 240, y: 690, w: 100, h: 14 };
  for (const balls of [[], [{ x: 200, y: 400, r: 8, vx: 10, vy: 200 }], [{ x: 200, y: 400, r: 8, stuck: true }, { x: 100, y: 500, r: 8, lost: true }]]) {
    const fx = { key: 'LET GO', word: 'LET GO', t: 0, dur: 3, phase: 0, heavy: false, x: 240, y: 300, data: {} };
    for (let i = 0; i <= 180; i++) { fx.t = i / 60; fx.phase = Math.min(1, fx.t / 3); LETGO.render.over(ctx, { paddle, balls }, fx, R); }
    LETGO.render.over(ctx, { paddle, balls }, fx, { ...R, reduced: true });
  }
});

test('the sounds only touch the synth bag: a duck and one play onto the word bus', () => {
  for (const mod of [RELAX, LETGO]) {
    const calls = [];
    const synth = { ctx: {}, now: 10, dest: 'word-bus', bus: {}, SEMI: s => 2 ** (s / 12), ROOT_HZ: 220,
      tone: (hz, dur, level, o = {}) => ({ k: 'tone', hz, dur, level, ...o }), noise: (hz, dur, level, o = {}) => ({ k: 'noise', hz, dur, level, ...o }),
      play: (v, t, d) => calls.push(['play', v, t, d]), duck: (...a) => calls.push(['duck', ...a]), glide: () => {} };
    mod.sound(synth, { t: 0 });
    const plays = calls.filter(c => c[0] === 'play'), ducks = calls.filter(c => c[0] === 'duck');
    assert.equal(ducks.length, 1, `${mod.key} ducks once`); assert.ok(ducks[0][1] <= 0.4, `${mod.key} ducks gently`);
    assert.equal(plays.length, 1); assert.equal(plays[0][3], 'word-bus'); assert.equal(plays[0][2], 10);
    for (const v of plays[0][1]) { assert.ok(v.level <= 0.06, `${mod.key} stays quiet: ${v.level}`); assert.ok(v.hz > 20 && v.dur > 0); }
    assert.ok(plays[0][1].every(v => (v.at || 0) + v.dur <= mod.dur + 0.5), `${mod.key} sound fits the word`);
  }
});

test('RELAX reuses hold gradients but rebuilds for fade and changed centre', () => {
  const previous = globalThis.document; let gradients = 0, draws = 0;
  const layer = new Proxy({}, { get: (_, key) => key === 'createRadialGradient' ? () => { gradients++; return { addColorStop() {} }; } : () => {} });
  globalThis.document = { createElement: () => ({ width: 0, height: 0, getContext: () => layer }) };
  const target = { drawImage() { draws++; } }, R = fakeR(), fx = { t: 1, x: 240, y: 300, data: { puffs: 10 } };
  try {
    RELAX.render.over(target, {}, fx, R); const built = gradients;
    fx.t = 1.2; RELAX.render.over(target, {}, fx, R);
    assert.equal(gradients, built); assert.equal(draws, 2);
    fx.t = 2.5; RELAX.render.over(target, {}, fx, R); assert.ok(gradients > built);
    const faded = gradients; fx.x += 30; RELAX.render.over(target, {}, fx, R); assert.ok(gradients > faded);
  } finally { globalThis.document = previous; }
});
