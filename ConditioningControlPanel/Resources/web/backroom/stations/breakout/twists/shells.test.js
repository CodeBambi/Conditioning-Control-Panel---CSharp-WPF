/* The shells twist: how many stand up, what one touch costs, what three of them
 * buy back, and that the drift is a pure function of sim time. No DOM anywhere.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import TWIST, {
  build, update, stateOf, posAt, shellCount,
  MAX_SHELLS, MIN_SHELLS, SHELL_HP, SHELL_R, SLOW_S, SLOW_MUL, SAT_COST, SAT_GIVE,
} from './shells.js';
import RENDER from './shells-render.js';
import CUES from '../cues/twist-shells.js';
import REACTIONS from '../reactions/twist-shells.js';

/* ---------------------------------------------------------------- harness */
/** A game object with only what the twist is allowed to read (twists/CONTRACT.md). */
function mkGame(over = {}) {
  return {
    w: 1280, h: 720, state: 'colour', reduced: false, sat: 0.5, storyCap: 0.95,
    speed: 400, doorBoard: 'st_enough_03', balls: [], bricks: [], ...over,
  };
}
/** A ball the sim will accept: live, launched, moving. */
const mkBall = (o = {}) => ({ x: 0, y: 0, vx: 300, vy: -400, r: 8, stuck: false, lost: false, falling: false, ...o });

/** `ctx`, with a seeded rng so every build in this file is the same build. */
function mkCtx(seed = 1) {
  const events = [];
  let s = seed;
  const rng = () => { s = (s * 1664525 + 1013904223) % 4294967296; return s / 4294967296; };
  return {
    events, rng, w: 1280, h: 720,
    emit: (name, data) => events.push({ name, data }),
    at: () => null, breakBrick: () => {}, powers: { drop() {}, reset() {} },
    schedule: () => () => {}, startRelapse: () => {},
  };
}
const names = ctx => ctx.events.map(e => e.name);
const last = (ctx, name) => [...ctx.events].reverse().find(e => e.name === name);
/** Put a ball inside a shell and run one step, so the entry lands. */
function enter(g, ctx, sh, b) { b.x = sh.x; b.y = sh.y; update(g, 1 / 60, ctx); }
/** Take the ball back out, so the next entry counts as a new visit. */
function leave(g, ctx, b) { b.x = -900; b.y = -900; update(g, 1 / 60, ctx); }

/* ------------------------------------------------------------ the count */

test('the count is the relapses this run, floored at two and capped at five', () => {
  assert.equal(shellCount(0), MIN_SHELLS);
  assert.equal(shellCount(1), MIN_SHELLS);
  assert.equal(shellCount(2), 2);
  assert.equal(shellCount(4), 4);
  assert.equal(shellCount(5), MAX_SHELLS);
  assert.equal(shellCount(99), MAX_SHELLS);
  assert.equal(shellCount('nonsense'), MIN_SHELLS);
});

test('a fresh board stands up two shells, each at full hp', () => {
  const g = mkGame(), ctx = mkCtx();
  build(g, ctx);
  const st = stateOf(g);
  assert.equal(st.list.length, MIN_SHELLS);
  for (const sh of st.list) {
    assert.equal(sh.hp, SHELL_HP);
    assert.equal(sh.r, SHELL_R);
    assert.ok(sh.x > 0 && sh.x < g.w, 'a shell is on the field');
    assert.ok(sh.y > 0 && sh.y < g.h);
  }
});

test('the relapse count is carried across boards and raises the next board count', () => {
  const g = mkGame(), ctx = mkCtx();
  build(g, ctx);
  stateOf(g).relapses = 4;
  g.doorBoard = 'st_enough_06';
  build(g, ctx);
  assert.equal(stateOf(g).list.length, 4);
  stateOf(g).relapses = 12;
  build(g, ctx);
  assert.equal(stateOf(g).list.length, MAX_SHELLS, 'never more than five');
});

test('a relapse mid board leaves one more old self behind, up to the cap', () => {
  const g = mkGame(), ctx = mkCtx();
  build(g, ctx);
  const before = stateOf(g).list.length;
  g.state = 'grey';
  update(g, 1 / 60, ctx);
  assert.equal(stateOf(g).relapses, 1);
  assert.equal(stateOf(g).list.length, before + 1);
  for (let i = 0; i < 8; i++) { g.state = 'colour'; update(g, 1 / 60, ctx); g.state = 'grey'; update(g, 1 / 60, ctx); }
  assert.equal(stateOf(g).list.length, MAX_SHELLS);
});

/* ------------------------------------------------------ pass-through */

test('the ball passes THROUGH a shell: nothing turns it, nothing stops it', () => {
  const g = mkGame(), ctx = mkCtx();
  build(g, ctx);
  const sh = stateOf(g).list[0];
  const b = mkBall({ x: sh.x, y: sh.y, vx: 300, vy: -400 });
  g.balls = [b];
  update(g, 1 / 60, ctx);
  assert.equal(Math.sign(b.vx), 1, 'the ball keeps its direction');
  assert.equal(Math.sign(b.vy), -1);
  assert.ok(!b.lost && !b.falling, 'a shell never costs a ball');
  assert.equal(names(ctx).filter(n => n === 'shellTouch').length, 1);
});

test('sitting inside a shell is one touch, not a touch every frame', () => {
  const g = mkGame(), ctx = mkCtx();
  build(g, ctx);
  const sh = stateOf(g).list[0];
  const b = mkBall({ x: sh.x, y: sh.y });
  g.balls = [b];
  for (let i = 0; i < 10; i++) { b.x = sh.x; b.y = sh.y; update(g, 1 / 600, ctx); }
  assert.equal(names(ctx).filter(n => n === 'shellTouch').length, 1);
});

/* --------------------------------------------------- the cost and the slow */

test('one touch takes a little saturation and hands the ball back heavy', () => {
  const g = mkGame({ sat: 0.5 }), ctx = mkCtx();
  build(g, ctx);
  const sh = stateOf(g).list[0];
  const b = mkBall();
  g.balls = [b];
  enter(g, ctx, sh, b);
  assert.ok(Math.abs(g.sat - (0.5 - SAT_COST)) < 1e-9, 'the room goes a shade quieter');
  assert.ok(b.shellSlow > 0, 'the ball is carrying the weight');
  const speed = Math.hypot(b.vx, b.vy);
  assert.ok(speed < g.speed * 0.8, 'it came out slower: ' + speed);
  assert.ok(speed > g.speed * SLOW_MUL * 0.9, 'it never stops');
});

test('the weight is gone about a beat later and the ball is back at speed', () => {
  const g = mkGame(), ctx = mkCtx();
  build(g, ctx);
  const sh = stateOf(g).list[0];
  const b = mkBall();
  g.balls = [b];
  enter(g, ctx, sh, b);
  b.x = -900; b.y = -900;
  for (let i = 0; i < 80; i++) update(g, SLOW_S / 20, ctx);
  assert.equal(b.shellSlow, 0);
  assert.ok(Math.abs(Math.hypot(b.vx, b.vy) - g.speed) < 1, 'nothing is kept');
});

test('saturation has a floor of zero and a shell never starts a relapse', () => {
  const g = mkGame({ sat: 0.01 }), ctx = mkCtx();
  build(g, ctx);
  const sh = stateOf(g).list[0];
  const b = mkBall();
  g.balls = [b];
  enter(g, ctx, sh, b);
  assert.equal(g.sat, 0);
  assert.equal(g.state, 'colour', 'a shell is never a relapse by itself');
});

test('GREY takes nothing: it is payload-free', () => {
  const g = mkGame({ state: 'grey', sat: 0 }), ctx = mkCtx();
  build(g, ctx);
  const sh = stateOf(g).list[0];
  const b = mkBall({ x: sh.x, y: sh.y });
  g.balls = [b];
  update(g, 1 / 60, ctx);
  assert.equal(names(ctx).filter(n => n === 'shellTouch').length, 0);
  assert.equal(g.sat, 0);
});

/* ------------------------------------------------------------- the pop */

test('three touches pop a shell for good and give a little back', () => {
  const g = mkGame({ sat: 0.5 }), ctx = mkCtx();
  build(g, ctx);
  const st = stateOf(g);
  const sh = st.list[0], before = st.list.length;
  const b = mkBall();
  g.balls = [b];
  for (let i = 0; i < SHELL_HP; i++) { enter(g, ctx, sh, b); leave(g, ctx, b); }
  assert.equal(names(ctx).filter(n => n === 'shellTouch').length, SHELL_HP);
  const pop = last(ctx, 'shellPop');
  assert.ok(pop, 'the shell popped');
  assert.equal(pop.data.left, before - 1);
  assert.equal(stateOf(g).list.length, before - 1, 'it is gone for good');
  const want = 0.5 - SHELL_HP * SAT_COST + SAT_GIVE;
  assert.ok(Math.abs(g.sat - want) < 1e-9, 'the release gives back less than the touches took');
  assert.ok(g.sat < 0.5, 'a shell is never profitable');
  /* Going back through where it stood does nothing at all. */
  const n = ctx.events.length;
  enter(g, ctx, sh, b);
  assert.equal(ctx.events.length, n);
});

test('the release never raises saturation past the act cap', () => {
  const g = mkGame({ sat: 0.95, storyCap: 0.95 }), ctx = mkCtx();
  build(g, ctx);
  const sh = stateOf(g).list[0], b = mkBall();
  g.balls = [b];
  for (let i = 0; i < SHELL_HP; i++) { enter(g, ctx, sh, b); leave(g, ctx, b); }
  assert.ok(g.sat <= 0.95 + 1e-9, 'the cap holds: ' + g.sat);
});

/* ------------------------------------------------------------- the drift */

test('the drift is deterministic: the same seed puts them in the same places', () => {
  const a = mkGame(), b = mkGame();
  build(a, mkCtx(7)); build(b, mkCtx(7));
  for (let i = 0; i < 120; i++) { update(a, 1 / 60, mkCtx(7)); update(b, 1 / 60, mkCtx(7)); }
  assert.deepEqual(stateOf(a).list.map(s => [s.x, s.y]), stateOf(b).list.map(s => [s.x, s.y]));
});

test('posAt is a pure function of sim time, and stands still in reduced motion', () => {
  const g = mkGame(), ctx = mkCtx();
  build(g, ctx);
  const sh = stateOf(g).list[0];
  assert.deepEqual(posAt(sh, 4.5), posAt(sh, 4.5));
  assert.notDeepEqual(posAt(sh, 0), posAt(sh, 9), 'it drifts');
  assert.deepEqual(posAt(sh, 9, true), { x: sh.x0, y: sh.y0 }, 'reduced motion holds the pose');
});

test('in reduced motion the shells hold still on the board too', () => {
  const g = mkGame({ reduced: true }), ctx = mkCtx();
  build(g, ctx);
  const first = stateOf(g).list.map(s => [s.x, s.y]);
  for (let i = 0; i < 200; i++) update(g, 1 / 60, ctx);
  assert.deepEqual(stateOf(g).list.map(s => [s.x, s.y]), first);
});

/* ------------------------------------------------- the shape of the module */

test('the twist is wired the way the contract asks', () => {
  assert.equal(TWIST.id, 'shells');
  assert.equal(typeof TWIST.build, 'function');
  assert.equal(typeof TWIST.update, 'function');
  assert.equal(typeof RENDER.over, 'function', 'shells draws in over');
  assert.equal(typeof RENDER.brick, 'undefined', 'shells flags no bricks');
  assert.deepEqual(Object.keys(CUES).sort(), ['shellPop', 'shellTouch']);
  assert.deepEqual(Object.keys(REACTIONS).sort(), ['shellPop', 'shellTouch']);
});

test('nothing in the sim reaches for the clock, the DOM or Math.random', async () => {
  const { readFileSync } = await import('node:fs');
  const src = readFileSync(new URL('./shells.js', import.meta.url), 'utf8');
  const code = src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '');
  for (const bad of [/Math\.random\s*\(/, /\bdocument\b/, /\bwindow\b/, /Date\.now/, /performance\.now/]) {
    assert.ok(!bad.test(code), 'shells.js must not use ' + bad);
  }
  assert.ok(!/[–—]/.test(src), 'no em-dashes or en-dashes');
});

test('the render and the reactions survive a shell-less snapshot', () => {
  const calls = [];
  const c2d = new Proxy({}, { get: (t, k) => (k === 'font' || k === 'fillStyle' || k === 'strokeStyle' || k === 'globalAlpha' || k === 'lineWidth' || k === 'textAlign' || k === 'textBaseline')
    ? undefined : (...a) => calls.push([k, a]) });
  RENDER.over(c2d, { state: 'colour', reduced: false }, 0);
  RENDER.over(c2d, { state: 'colour', reduced: false, shells: { list: [] } }, 0);
  assert.equal(calls.length, 0, 'nothing to draw, nothing drawn');
  const fx = { colour: false, reduced: true, W: 100, H: 100, stamps: [], aberr() {}, rungs: () => false, P: { burst() {} } };
  REACTIONS.shellTouch(fx, {});
  REACTIONS.shellPop(fx, {});
  assert.equal(fx.stamps.length, 0, 'GREY throws off nothing');
});
