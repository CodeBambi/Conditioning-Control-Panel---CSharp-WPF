/* node --test words/sink-deeper.test.js - SINK and DEEPER: the mods they set, the saturation bumps, nothing left behind. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGame } from '../game.js';
import { MOD_DEFAULTS } from '../word-fx.js';
import SINK from './sink.js';
import DEEPER from './deeper.js';

const seeded = (seed = 7) => () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
function make(opts = {}) {
  const events = [], calls = [];
  const audio = new Proxy({ beat: { spb: 60 / 96 } }, { get: (t, k) => k in t ? t[k] : (...a) => calls.push([k, ...a]) });
  const game = createGame({ rng: seeded(), audio, onEvent: (n, d) => events.push([n, d]), ...opts });
  game.setNoLose(true);
  return { game, events, calls, s: game.snapshot() };
}
const run = (game, secs) => { for (let i = 0; i < Math.round(secs * 60); i++) game.step(1 / 60, {}); };
const near = (a, b, eps, msg) => assert.ok(Math.abs(a - b) <= eps, `${msg}: ${a} vs ${b}`);

/** A canvas 2D context stand-in: every method is a no-op that returns another stand-in (so gradients take stops). */
function fakeCtx() {
  const bag = new Proxy({}, { get: (t, k) => (k in t ? t[k] : () => bag), set: (t, k, v) => { t[k] = v; return true; } });
  return bag;
}
const R = (over = {}) => ({ W: 480, H: 720, cw: 780, ch: 1688, scale: 780 / 480, ox: 0, oy: 0, mix: 1, dt: 1 / 60, fxDt: 1 / 60,
  col: (rgb, m, a = 1) => `rgba(${rgb.join(',')},${a})`, PINK: [255, 105, 180], VIOLET: [165, 108, 255], WHITE: [255, 255, 255], BG: [26, 26, 46], FONT: 'Arial',
  reduced: false, rng: seeded(3), frame: (c) => c || { width: 780, height: 1688 }, ...over });
function fakeSynth() {
  const played = [], ducks = [];
  return { played, ducks, now: 10, ROOT_HZ: 523.25, SEMI: s => 2 ** (s / 12), dest: 'word',
    play: (v, t, dest) => played.push({ v, t, dest }), duck: (...a) => ducks.push(a),
    tone: (hz, dur, level, o = {}) => ({ k: 'tone', hz, dur, level, ...o }), noise: (hz, dur, level, o = {}) => ({ k: 'noise', hz, dur, level, ...o }) };
}

test('SINK: slow-mo to 0.3x in the middle, safe throughout, eases back and leaves the world a notch pinker', () => {
  const { game, s } = make({ words: ['SINK'] });
  for (const b of s.bricks) b.word = null;   // the ball's own hits must not fire a SINK under the test's
  const sat0 = s.sat;
  const fx = game.fireWordNow('SINK');
  assert.ok(fx && fx.key === 'SINK');
  run(game, 0.5);
  near(s.mod.timeScale, 0.3, 0.02, 'timeScale mid-word');
  assert.equal(s.mod.safe, true);
  near(s.mod.zoom, 0.88, 0.005, 'the field drops 12% toward the ball');
  near(s.timeScale, 0.3, 0.02, 'the sim runs slowed');
  run(game, 0.8);                                                                  // t = 1.3 of 1.6: on the way out
  assert.ok(s.mod.timeScale > 0.5 && s.mod.timeScale < 1, `easing out: ${s.mod.timeScale}`);
  assert.equal(s.mod.safe, true);
  run(game, 0.4);
  assert.equal(s.fx.active.length, 0, 'ended');
  assert.deepEqual(s.mod, MOD_DEFAULTS, 'nothing persists in g.mod');
  assert.equal(s.timeScale, 1);
  near(s.sat, sat0 + 0.02, 1e-9, 'sat bumped once on end');
  assert.equal(s.balls.length, 1);
  near(s.fx.wall - s.fx.lastHeavyAt, 1.7, 0.05, 'the heavy gap is measured in wall-clock despite the slow-mo');
  game.setReduced(true);
  run(game, 4.1);
  assert.ok(game.fireWordNow('SINK'));
  run(game, 0.5);
  assert.equal(s.mod.zoom, 1, 'reduced motion: no drop');
  near(s.mod.timeScale, 0.3, 0.02, 'the slow-mo stays');
});

test('DEEPER: zoom 1.08 in the middle, ball 5% faster, safe, sat +0.04 on start, back to 1 and a boost count on end', () => {
  const { game, s } = make({ words: ['DEEPER'] });
  const sat0 = s.sat;
  assert.ok(game.fireWordNow('DEEPER'));
  near(s.sat, sat0 + 0.04, 1e-9, 'sat floor rises on start');
  run(game, 0.5);
  near(s.mod.zoom, 1.08, 0.005, 'zoom mid-word');
  assert.equal(s.mod.ballSpeed, 1.05);
  assert.equal(s.mod.safe, true);
  assert.equal(s.mod.timeScale, 1);
  run(game, 0.55);                                                                 // t = 1.05 of 1.2: easing back
  assert.ok(s.mod.zoom > 1 && s.mod.zoom < 1.05, `easing back: ${s.mod.zoom}`);
  run(game, 0.3);
  assert.equal(s.fx.active.length, 0, 'ended');
  assert.deepEqual(s.mod, MOD_DEFAULTS, 'nothing persists in g.mod');
  assert.equal(s.deeperBoost, 1, 'the wall-long boost is counted on g for a future hook');
  near(s.sat, sat0 + 0.04, 1e-9, 'no second bump on end');
  game.setReduced(true);
  run(game, 4.1);
  assert.ok(game.fireWordNow('DEEPER'));
  run(game, 0.5);
  assert.equal(s.mod.zoom, 1, 'reduced motion: no zoom');
  run(game, 1);
  assert.equal(s.deeperBoost, 2);
});

test('SINK sound: a sub glide one octave down on the word bus, the rest ducked 60%', () => {
  const synth = fakeSynth();
  SINK.sound(synth, { t: 0, phase: 0, x: 240, y: 300 });
  assert.deepEqual(synth.ducks, [[0.6, 1.0, 0.6]]);
  assert.equal(synth.played.length, 1); assert.equal(synth.played[0].dest, 'word');
  const glide = synth.played[0].v.find(n => n.k === 'tone' && n.hzTo);
  near(glide.hzTo / glide.hz, 0.5, 1e-9, 'one octave down');
  assert.ok(synth.played[0].v.some(n => n.k === 'noise'), 'the whoomp');
});

test('DEEPER sound: two steps an octave apart, each DEEPER a semitone lower, floor at four semitones', () => {
  const roots = [];
  for (let i = 0; i < 7; i++) {
    const synth = fakeSynth();
    DEEPER.sound(synth, { t: 0, phase: 0 });
    assert.deepEqual(synth.ducks, [[0.5, 0.6, 0.5]]);
    const [a, b] = synth.played[0].v;
    near(a.hz / b.hz, 2, 1e-9, 'an octave step'); assert.ok(b.at > 0.3 && b.at < 0.4, 'the second lands ~0.35 s later');
    roots.push(a.hz);
  }
  for (let i = 1; i < 5; i++) near(roots[i - 1] / roots[i], 2 ** (1 / 12), 1e-9, `fire ${i} a semitone lower`);
  near(roots[4], roots[5], 1e-9, 'floor'); near(roots[5], roots[6], 1e-9, 'floor holds');
});

test('the render hooks run clean at every phase, live and reduced, and the ghost frame is captured once per two frames', () => {
  const { game, s } = make({ words: ['SINK', 'DEEPER'] });
  const ctx = fakeCtx();
  for (const [mod, key] of [[SINK, 'SINK'], [DEEPER, 'DEEPER']]) {
    const fx = game.fireWordNow(key);
    assert.ok(fx, key);
    let frames = 0;
    const r = R({ frame: (c) => { frames++; return c || { width: 780, height: 1688 }; } }), rr = R({ reduced: true });
    for (let i = 0; i < 12; i++) {
      run(game, 0.1);
      for (const hook of ['world', 'over', 'post']) { mod.render[hook](ctx, s, fx, r); mod.render[hook](ctx, s, fx, rr); }
    }
    if (key === 'DEEPER') assert.ok(frames >= 6 && frames <= 7, `ghost captured every other frame: ${frames}`);
    else assert.ok(frames > 0, 'SINK melts a copy of the frame once per post');
    run(game, 4.2);
  }
  assert.deepEqual(s.mod, MOD_DEFAULTS);
});
