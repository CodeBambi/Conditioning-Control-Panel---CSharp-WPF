/* node --test game.test.js - the state machine and the saturation ladder, nothing visual. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGame, rungsFor, RUNG_AT, BRICK } from './game.js';

const seeded = (seed = 7) => () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
function make(opts = {}) {
  const events = [], calls = [];
  const audio = new Proxy({ beat: { spb: 60 / 96 } }, { get: (t, k) => k in t ? t[k] : (...a) => calls.push([k, ...a]) });
  const game = createGame({ rng: seeded(), audio, onEvent: (n, d) => events.push([n, d]), ...opts });
  return { game, events, calls, names: () => events.map(e => e[0]) };
}

test('rungs unlock at their thresholds and force overrides them', () => {
  assert.deepEqual(rungsFor(0.05, 'colour', null), [true, false, false, false, false, false, false, false, false, false]);
  assert.deepEqual(rungsFor(0.65, 'colour', null).filter(Boolean).length, 7);
  assert.equal(rungsFor(1, 'colour', null).every(Boolean), true);
  assert.equal(rungsFor(1, 'grey', null).filter(Boolean).length, 1, 'grey has only the base rung');
  assert.equal(rungsFor(0, 'colour', { 9: true })[9], true);
  assert.equal(rungsFor(1, 'colour', { 3: false })[3], false);
  assert.equal(RUNG_AT.length, 10);
});

test('a fresh session starts in COLOUR at 0.05 with a full wall and a stuck ball', () => {
  const { game } = make();
  const s = game.snapshot();
  assert.equal(s.state, 'colour'); assert.equal(s.sat, 0.05);
  assert.equal(s.bricks.length, BRICK.cols * BRICK.rows);
  assert.equal(s.balls.length, 1); assert.equal(s.balls[0].stuck, true); assert.equal(s.balls[0].ghost, false);
});

test('bricks add 0.012 saturation and a wall clear adds 0.1 plus one SP', () => {
  const { game, events, names } = make();
  game.breakBrick(0);
  assert.ok(Math.abs(game.snapshot().sat - 0.062) < 1e-9);
  assert.equal(names()[0], 'brick');
  for (let i = 1; i < BRICK.cols * BRICK.rows; i++) game.breakBrick(i);
  const s = game.snapshot();
  assert.ok(names().includes('wall'));
  assert.equal(s.stats.walls, 1); assert.equal(s.stats.sp, 1);
  assert.equal(s.bricks.filter(b => b.alive).length, BRICK.cols * BRICK.rows, 'a new wall descends');
  assert.ok(Math.abs(s.sat - (0.05 + 60 * 0.012 + 0.1)) < 1e-9);
  assert.equal(events.filter(e => e[0] === 'crack').length, 0, 'no crack yet at 0.87');
  for (let i = 0; i < 60; i++) game.breakBrick(i);
  assert.equal(game.snapshot().sat, 1, 'capped at 1');
  assert.equal(events.filter(e => e[0] === 'crack').length, 1, 'the crack fires once past 0.9, once per session');
});

test('losing the ball in COLOUR is a RELAPSE: grey, saved saturation, ghost ball', () => {
  const { game, names, calls } = make({ saturation: 0.6 });
  game.loseBall();
  const s = game.snapshot();
  assert.equal(s.state, 'grey'); assert.equal(s.sat, 0); assert.equal(s.savedSat, 0.6);
  assert.equal(s.balls.length, 1); assert.equal(s.balls[0].ghost, true); assert.equal(s.balls[0].stuck, true);
  assert.deepEqual(names(), ['relapse']);
  assert.ok(calls.some(c => c[0] === 'relapse') && calls.some(c => c[0] === 'setState' && c[1] === 'grey'));
  game.breakBrick(0);
  assert.equal(game.snapshot().sat, 0, 'grey bricks do not add saturation');
  game.loseBall();
  assert.deepEqual(names(), ['relapse', 'brick', 'lost'], 'losing again while grey is not another relapse');
});

test('N grey bricks is a BREAKOUT: 100 ms freeze, then the world snaps back', () => {
  const { game, names, calls } = make({ saturation: 0.55, breakoutN: 3 });
  game.loseBall();
  game.breakBrick(0); game.breakBrick(1);
  assert.equal(game.snapshot().state, 'grey');
  game.breakBrick(2);
  let s = game.snapshot();
  assert.equal(s.pendingBreakout, true); assert.ok(s.freeze > 0); assert.equal(s.state, 'grey', 'still grey during the freeze');
  game.step(0.05);
  assert.equal(game.snapshot().state, 'grey');
  game.step(0.06);
  s = game.snapshot();
  assert.equal(s.state, 'colour'); assert.equal(s.sat, 0.55); assert.equal(s.balls[0].ghost, false);
  assert.equal(names().at(-1), 'breakout');
  assert.ok(calls.some(c => c[0] === 'breakout'));
  assert.equal(s.bricks.filter(b => !b.alive).length, 3, 'the grey stretch still counts');
});

test('the loop runs, launches and keeps the ball at the beat speed', () => {
  const { game } = make();
  for (let i = 0; i < 90; i++) game.step(1 / 60, { x: 240 });
  const s = game.snapshot();
  const b = s.balls[0];
  assert.equal(b.stuck, false, 'auto launch after 1.2 s');
  assert.ok(Math.abs(Math.hypot(b.vx, b.vy) - s.speed) < 1e-6);
  assert.ok(s.speed > 220);
  assert.ok(s.time > 1.4);
});

test('dev hooks: relapseNow, breakoutNow and setSaturation', () => {
  const { game } = make({ saturation: 0.3 });
  game.setSaturation(0.8);
  assert.equal(game.snapshot().sat, 0.8);
  game.relapseNow();
  assert.equal(game.snapshot().state, 'grey');
  game.breakoutNow(); game.step(0.2);
  assert.equal(game.snapshot().state, 'colour'); assert.equal(game.snapshot().sat, 0.8);
});
