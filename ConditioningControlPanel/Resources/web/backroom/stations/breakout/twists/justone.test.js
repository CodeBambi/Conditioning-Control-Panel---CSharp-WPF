import test from 'node:test';
import assert from 'node:assert/strict';
import TWIST, { comedownCount, comedown, stateOf, doseCount, FIREBALL_S, PADDLE_SCALE, COMEDOWN_CAP } from './justone.js';
import RENDER from './justone-render.js';

/* ---------------------------------------------------------------- harness */
/** A game object with only what the twist is allowed to read (twists/CONTRACT.md). */
function mkGame(over = {}) {
  return {
    state: 'colour', transition: null, greyBricks: 0, breakoutN: 10, fractures: 0, paddleScale: 1,
    paddle: { x: 200, y: 400, w: 100, h: 12 }, power: { drops: [], fireball: 0 }, bricks: [], ...over,
  };
}
/** ctx, with an rng that fails the test if the twist ever reaches for one. */
function mkCtx() {
  const events = [], timers = [];
  const ctx = {
    events, timers, relapses: 0,
    emit: (name, data) => events.push({ name, data }),
    rng: () => { throw new Error('justone must never use rng'); },
    at: () => null, breakBrick: () => {}, powers: { drop() {}, reset() {} },
    schedule: (sec, fn) => { const t = { sec, fn, dead: false }; timers.push(t); return () => { t.dead = true; }; },
    startRelapse: () => { ctx.relapses++; },
    w: 400, h: 500,
  };
  return ctx;
}
const names = ctx => ctx.events.map(e => e.name);
const last = (ctx, name) => [...ctx.events].reverse().find(e => e.name === name);
const mkDrop = (o = {}) => ({ kind: 'fireball', x: 200, y: 100, age: 0, vy: 140, ph: 0, ...o });
/** Break a treat brick at `x`, with the drop powerups.js has already pushed. */
function breakTreat(g, ctx, x = 200, y = 100) {
  const br = { treat: true, x: x - 16, y, w: 32, h: 16, alive: false };
  const d = mkDrop({ x, y: y + 8 });
  g.power.drops.push(d);
  TWIST.onBreak(g, br, null, ctx);
  return d;
}
/** Put a drop where the paddle really catches it, then tell the twist the catch happened. */
function catchDrop(g, ctx, d) {
  d.x = g.paddle.x; d.y = g.paddle.y - g.paddle.h / 2 - 6;
  g.power.fireball = 8;                                    // powerups.js activate() ran first
  ctx.emit('powerCatch', { kind: 'fireball', x: g.paddle.x, y: g.paddle.y });
  TWIST.onCatch(g, { kind: 'fireball', x: g.paddle.x, y: g.paddle.y }, ctx);
  g.power.drops = g.power.drops.filter(x => x !== d);      // the sweep rebuilds the list after the catch
}

/* ------------------------------------------------------------------ shape */
test('justone: the module keeps the shape twists/CONTRACT.md froze', () => {
  assert.equal(TWIST.id, 'justone');
  for (const hook of ['build', 'onHit', 'onBreak', 'update', 'onCatch', 'wallCleared'])
    if (TWIST[hook] !== undefined) assert.equal(typeof TWIST[hook], 'function', hook + ' must be a function or absent');
  for (const hook of ['brick', 'under', 'over'])
    if (RENDER[hook] !== undefined) assert.equal(typeof RENDER[hook], 'function', hook + ' must be a function or absent');
});

/* ------------------------------------------------------------- the price */
test('justone: the comedown is 6 + 3 per treat, capped at 18', () => {
  assert.equal(comedownCount(0), 6);
  assert.equal(comedownCount(1), 9);
  assert.equal(comedownCount(2), 12);
  assert.equal(comedownCount(3), 15);
  assert.equal(comedownCount(4), COMEDOWN_CAP);
  assert.equal(comedownCount(9), COMEDOWN_CAP);
  assert.equal(comedownCount(-3), 6);
  assert.equal(comedownCount(undefined), 6);
});

/* -------------------------------------------------------------- the offer */
test('justone: a treat brick tags its own drop and announces it once', () => {
  const g = mkGame(), ctx = mkCtx();
  const d = breakTreat(g, ctx);
  assert.equal(d.treat, true);
  assert.deepEqual(names(ctx), ['treatDrop']);
  assert.equal(stateOf(g).flights.length, 1);
  TWIST.onBreak(g, { treat: true, x: 184, y: 100, w: 32, h: 16 }, null, ctx);   // the same drop again
  assert.deepEqual(names(ctx), ['treatDrop']);
});

test('justone: a plain brick, a foreign drop and a GREY break are all nothing', () => {
  const g = mkGame(), ctx = mkCtx();
  TWIST.onBreak(g, { x: 0, y: 0, w: 32, h: 16 }, null, ctx);                     // not a treat
  g.power.drops.push(mkDrop({ x: 9 }));
  TWIST.onBreak(g, { treat: true, x: 184, y: 100, w: 32, h: 16 }, null, ctx);    // somebody else's drop
  g.power.drops.length = 0;
  TWIST.onBreak(g, { treat: true, x: 184, y: 100, w: 32, h: 16 }, null, ctx);    // GREY: nothing fell
  assert.deepEqual(names(ctx), []);
  assert.equal(stateOf(g).flights.length, 0);
});

/* ------------------------------------------------------------- the taking */
test('justone: catching one buys exactly six seconds and books one comedown', () => {
  const g = mkGame(), ctx = mkCtx();
  const d = breakTreat(g, ctx);
  catchDrop(g, ctx, d);
  assert.equal(g.power.fireball, FIREBALL_S);
  assert.equal(last(ctx, 'treatCatch').data.doses, 1);
  assert.equal(ctx.timers.length, 1);
  assert.equal(ctx.timers[0].sec, FIREBALL_S);
  assert.equal(doseCount(g), 1);
});

test('justone: a treat still in the air, past the tip, or a foreign power is not a catch', () => {
  const g = mkGame(), ctx = mkCtx();
  const d = breakTreat(g, ctx);
  TWIST.onCatch(g, { kind: 'fireball', x: g.paddle.x }, ctx);                    // d.y is still up at 108
  TWIST.onCatch(g, { kind: 'multiball', x: g.paddle.x }, ctx);
  d.y = g.paddle.y - g.paddle.h / 2;
  TWIST.onCatch(g, { kind: 'fireball', x: g.paddle.x + g.paddle.w / 2 + 40 }, ctx);
  assert.equal(doseCount(g), 0);
  TWIST.onCatch(g, { kind: 'fireball', x: g.paddle.x + g.paddle.w / 2 + 17 }, ctx);   // just inside the reach
  assert.equal(doseCount(g), 1);
});

test('justone: a second treat restarts the high and moves the one comedown, it never books two', () => {
  const g = mkGame(), ctx = mkCtx();
  catchDrop(g, ctx, breakTreat(g, ctx));
  const first = ctx.timers[0];
  g.power.fireball = 2.5;
  catchDrop(g, ctx, breakTreat(g, ctx, 120));
  assert.equal(first.dead, true, 'the first comedown is cancelled');
  assert.equal(ctx.timers.filter(t => !t.dead).length, 1);
  assert.equal(g.power.fireball, FIREBALL_S);
  assert.equal(doseCount(g), 2);
});

/* ------------------------------------------------------------ letting go */
test('justone: a treat that reaches the floor says so once and costs nothing', () => {
  const g = mkGame(), ctx = mkCtx();
  const d = breakTreat(g, ctx);
  d.missed = true;
  TWIST.update(g, 1 / 60, ctx);
  TWIST.update(g, 1 / 60, ctx);
  assert.deepEqual(names(ctx).filter(n => n === 'treatMiss'), ['treatMiss']);
  assert.equal(doseCount(g), 0);
  assert.equal(g.paddleScale, 1);
  assert.equal(ctx.timers.length, 0);
});

test('justone: a relapse that clears the drops mid-flight reads as a miss, not a catch', () => {
  const g = mkGame(), ctx = mkCtx();
  breakTreat(g, ctx);
  g.power.drops.length = 0;                                 // powerups.js reset() on the way into GREY
  TWIST.update(g, 1 / 60, ctx);
  assert.equal(last(ctx, 'treatMiss') !== undefined, true);
  assert.equal(stateOf(g).flights.length, 0);
});

/* ------------------------------------------------------------- the bill */
test('justone: the comedown squeezes the paddle, asks for the relapse and prices the grey', () => {
  const g = mkGame(), ctx = mkCtx();
  catchDrop(g, ctx, breakTreat(g, ctx));
  comedown(g, ctx);
  assert.deepEqual(last(ctx, 'comedown').data, { doses: 1, count: 9 });
  assert.equal(g.paddleScale, PADDLE_SCALE);
  assert.equal(ctx.relapses, 1);
  assert.equal(g.breakoutN, 10, 'the price lands when the grey does, not before');

  g.transition = { kind: 'relapse' };                       // the relapse is on its way: do not ask again
  TWIST.update(g, 1 / 60, ctx);
  assert.equal(ctx.relapses, 1);

  g.transition = null; g.state = 'grey'; g.greyBricks = 0;
  TWIST.update(g, 1 / 60, ctx);
  assert.equal(g.breakoutN, 9);
  assert.equal(g.fractures, 0);

  g.greyBricks = 9; g.state = 'colour';                     // the breakout
  TWIST.update(g, 1 / 60, ctx);
  assert.equal(g.paddleScale, 1, 'the paddle comes back');
  assert.equal(stateOf(g).want, 0, 'the next relapse is the game own again');
});

test('justone: a relapse that did not take is asked for again', () => {
  const g = mkGame(), ctx = mkCtx();
  ctx.startRelapse = () => { ctx.relapses++; };
  comedown(g, ctx);
  TWIST.update(g, 1 / 60, ctx);
  TWIST.update(g, 1 / 60, ctx);
  assert.equal(ctx.relapses, 3);
  g.state = 'grey';
  TWIST.update(g, 1 / 60, ctx);
  assert.equal(g.breakoutN, 6);
  assert.equal(ctx.relapses, 3);
});

test('justone: a comedown that lands in an existing grey lengthens it, it never shortens it', () => {
  const g = mkGame({ state: 'grey', greyBricks: 4, breakoutN: 14 }), ctx = mkCtx();
  catchDrop(g, ctx, breakTreat(g, ctx));                    // doses 1 -> a price of 9
  comedown(g, ctx);
  assert.equal(ctx.relapses, 0, 'already grey: no second relapse');
  assert.equal(g.breakoutN, 14);
  assert.equal(g.fractures, 4 / 14);
  assert.equal(g.paddleScale, PADDLE_SCALE);
});

test('justone: a comedown at the very top of a fresh grey prices it exactly', () => {
  const g = mkGame({ state: 'grey', greyBricks: 0, breakoutN: 14 }), ctx = mkCtx();
  stateOf(g).doses = 2;
  comedown(g, ctx);
  assert.equal(g.breakoutN, 12);
});

/* ------------------------------------------------ determinism and resets */
test('justone: the same play twice is the same events, and the rng is never touched', () => {
  const run = () => {
    const g = mkGame(), ctx = mkCtx();
    TWIST.build(g, ctx);
    const a = breakTreat(g, ctx, 120);
    catchDrop(g, ctx, a);
    const b = breakTreat(g, ctx, 260);
    b.missed = true;
    TWIST.update(g, 1 / 60, ctx);
    ctx.timers.find(t => !t.dead).fn(g, ctx);
    g.state = 'grey';
    TWIST.update(g, 1 / 60, ctx);
    return { events: ctx.events.map(e => e.name + ':' + JSON.stringify(e.data)), n: g.breakoutN, p: g.paddleScale };
  };
  assert.deepEqual(run(), run());
});

test('justone: a new board is a new tab', () => {
  const g = mkGame(), ctx = mkCtx();
  catchDrop(g, ctx, breakTreat(g, ctx));
  comedown(g, ctx);
  TWIST.build(g, ctx);
  assert.equal(doseCount(g), 0);
  assert.equal(g.paddleScale, 1);
  assert.equal(stateOf(g).want, 0);
});

/* ------------------------------------------------------------------ paint */
const stub = () => new Proxy({}, {
  get: (t, k) => (k in t ? t[k] : () => ({ addColorStop() {} })),
  set: (t, k, v) => { t[k] = v; return true; },
});
test('justone: the painters survive an empty board, a full one and reduced motion', () => {
  const br = { treat: true, alive: true, x: 40, y: 60, w: 44, h: 18 };
  for (const reduced of [false, true]) {
    const snap = { w: 400, h: 500, reduced, time: 3.2, power: { drops: [{ ...mkDrop(), treat: true }, mkDrop()], fireball: 4 },
      justone: { doses: 11, high: 4 } };
    RENDER.brick(stub(), br, snap, snap.time);
    RENDER.under(stub(), snap, snap.time);
    RENDER.over(stub(), snap, snap.time);
  }
  const bare = { w: 400, h: 500, reduced: false, time: 0 };
  RENDER.brick(stub(), { treat: false }, bare, 0);
  RENDER.under(stub(), bare, 0);
  RENDER.over(stub(), bare, 0);
});
