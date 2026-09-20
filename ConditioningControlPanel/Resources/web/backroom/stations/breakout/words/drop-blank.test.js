/* node --test words/drop-blank.test.js - DROP holds the sim and drops the ball a lane; BLANK hides the wall on a curve; both keep the ball safe and leave g.mod clean. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGame } from '../game.js';
import { MOD_DEFAULTS } from '../word-fx.js';
import DROP from './drop.js';
import BLANK, { blankCurve } from './blank.js';

const seeded = (seed = 7) => () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
function make(opts = {}) {
  const events = [], calls = [];
  const audio = new Proxy({ beat: { spb: 60 / 96 } }, { get: (t, k) => k in t ? t[k] : (...a) => calls.push([k, ...a]) });
  const game = createGame({ rng: seeded(), audio, onEvent: (n, d) => events.push([n, d]), words: ['DROP', 'BLANK'], ...opts });
  // These effect tests begin after the opening grey phase.
  game.breakoutNow();
  for (let i = 0; i < 4; i++) game.step(0.1, {});
  game.snapshot().breakoutShield = null;
  events.length = 0;
  calls.length = 0;
  return { game, events, calls };
}
const run = (game, secs, input = {}) => { for (let i = 0; i < Math.round(secs * 60); i++) game.step(1 / 60, input); };
/** A ball in clear air below the wall, heading up and to the right; the paddle parked far left so nothing catches it. */
function airborne(game) {
  const s = game.snapshot();
  game.launchNow(); run(game, 0.05, { x: 60 });
  const b = s.balls[0]; b.x = 300; b.y = 320; b.vx = 210; b.vy = -210;
  return b;
}

test('DROP keeps play moving while the ball falls straight down its lane and is never lost', () => {
  const { game } = make();
  const s = game.snapshot();
  const b = airborne(game);
  const fx = game.fireWordNow('DROP');
  assert.ok(fx && fx.key === 'DROP', 'fired');
  assert.equal(s.hitStopMs, 0, 'no artificial freeze');
  assert.equal(b.vx, 0); assert.ok(b.vy > 0, 'heading down');
  const x0 = b.x, y0 = b.y;
  run(game, 2 / 60, { x: 60 });
  assert.equal(b.x, x0); assert.ok(b.y > y0);
  assert.ok(s.fx.active[0].t > 0, 'effect and play advance together');
  run(game, 4 / 60, { x: 60 });
  assert.equal(s.hitStopMs, 0, 'released');
  assert.ok(b.y > y0 + 5, `moved down (${b.y.toFixed(1)} from ${y0})`); assert.equal(b.x, x0, 'no drift'); assert.equal(b.vx, 0);
  assert.equal(s.mod.safe, true, 'safe while the word runs');
  run(game, 0.32, { x: 60 });
  assert.ok(Math.abs(b.vx) > 50, `sideways again after the lane (vx ${b.vx.toFixed(0)})`);
  assert.equal(s.state, 'colour', 'the floor bounced; no relapse');
  assert.equal(s.balls.length, 1);
  run(game, DROP.dur, { x: 60 });
  assert.equal(s.fx.active.length, 0, 'ended');
  assert.deepEqual(s.mod, MOD_DEFAULTS, 'nothing persists in g.mod');
});

test('DROP ended early still gives the ball its sideways component back', () => {
  const { game } = make();
  const s = game.snapshot();
  const b = airborne(game);
  const fx = game.fireWordNow('DROP');
  run(game, 0.1, { x: 60 });
  assert.equal(b.vx, 0);
  DROP.sim.end(s, fx, {});
  assert.ok(Math.abs(b.vx) > 50);
});

test('BLANK hides the wall on its curve: instant cut, held, back over the last 0.5 s, gone after the end', () => {
  assert.equal(blankCurve(0, 1.2), 1); assert.equal(blankCurve(0.1, 1.2), 1); assert.equal(blankCurve(0.2, 1.2), 1);
  assert.equal(blankCurve(0.6, 1.2), 1); assert.equal(blankCurve(0.7, 1.2), 1); assert.ok(Math.abs(blankCurve(0.95, 1.2) - 0.5) < 1e-9);
  assert.equal(blankCurve(1.2, 1.2), 0); assert.equal(blankCurve(2, 1.2), 0);
  const { game } = make();
  const s = game.snapshot();
  airborne(game);
  assert.equal(s.mod.hideBricks, 0, 'nothing hidden before the word');
  const fx = game.fireWordNow('BLANK');
  assert.ok(fx && fx.key === 'BLANK');
  assert.equal(s.hitStopMs, 0, 'no hold: the ball keeps moving');
  game.step(1 / 60, { x: 60 });
  assert.equal(s.mod.hideBricks, 1, 'white cut on first frame');
  assert.equal(s.mod.safe, true);
  run(game, 0.6, { x: 60 });
  assert.equal(s.mod.hideBricks, 1, 'the wall is gone in the middle');
  assert.equal(s.mod.safe, true);
  assert.equal(s.state, 'colour', 'blind, but never lost');
  run(game, 0.35, { x: 60 });
  assert.ok(s.mod.hideBricks > 0.3 && s.mod.hideBricks < 0.7, `coming back (${s.mod.hideBricks})`);
  run(game, 0.4, { x: 60 });
  assert.equal(s.fx.active.length, 0, 'ended');
  assert.deepEqual(s.mod, MOD_DEFAULTS, 'nothing persists in g.mod');
});

test('a brick hit during BLANK still breaks and still counts', () => {
  const { game, events } = make();
  const s = game.snapshot();
  airborne(game);
  game.fireWordNow('BLANK');
  run(game, 0.3, { x: 60 });
  const before = s.stats.bricks;
  const i = s.bricks.findIndex(b => b.alive);
  game.breakBrick(i);
  assert.equal(s.bricks[i].alive, false);
  assert.equal(s.stats.bricks, before + 1);
  assert.ok(events.some(e => e[0] === 'brick'));
  assert.equal(s.fx.active[0].key, 'BLANK', 'the word is still running');
});

test('both modules honour the contract shape', () => {
  for (const d of [DROP, BLANK]) {
    assert.equal(d.heavy, true); assert.ok(d.dur > 0);
    for (const h of ['start', 'tick', 'end']) assert.equal(typeof d.sim[h], 'function');
    for (const h of ['world', 'over', 'post']) assert.equal(typeof d.render[h], 'function');
    assert.equal(typeof d.sound, 'function');
  }
  assert.equal(DROP.dur, 0.7); assert.equal(BLANK.dur, 1.2);
});


test('DROP restores each affected ball to its own side without steering unrelated balls', () => {
  const left = { vx: -180, vy: -240 }, right = { vx: 180, vy: -240 };
  const g = { balls: [left, right] }, fx = { data: {} };
  DROP.sim.start(g, fx, { rng: () => .5 });
  assert.equal(left.vy, 300, 'preserves full speed before removing lateral velocity');
  const later = { vx: 0, vy: -300 }; g.balls.push(later);
  DROP.sim.end(g, fx, {});
  assert.ok(left.vx < 0); assert.ok(right.vx > 0);
  assert.equal(later.vx, 0, 'a later vertical ball is not part of this DROP');
});
