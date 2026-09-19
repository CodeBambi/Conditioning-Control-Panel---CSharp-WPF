/* node --test game.test.js - the state machine and the saturation ladder, nothing visual. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGame, rungsFor, layoutWord, RUNG_AT, BRICK, WELL_PRESETS } from './game.js';

const seeded = (seed = 7) => () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
function make(opts = {}) {
  const events = [], calls = [];
  const audio = new Proxy({ beat: { spb: 60 / 96 } }, { get: (t, k) => k in t ? t[k] : (...a) => calls.push([k, ...a]) });
  const game = createGame({ rng: seeded(), audio, onEvent: (n, d) => events.push([n, d]), ...opts });
  return { game, events, calls, names: () => events.map(e => e[0]).filter(n => n !== 'hit') };
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

test('a fresh session starts in COLOUR at 0.15 with a full wall and a stuck ball', () => {
  const { game } = make();
  const s = game.snapshot();
  assert.equal(s.state, 'colour'); assert.equal(s.sat, 0.15);
  assert.equal(s.bricks.length, BRICK.cols * BRICK.rows);
  assert.equal(s.balls.length, 1); assert.equal(s.balls[0].stuck, true); assert.equal(s.balls[0].ghost, false);
});

test('bricks add 0.012 saturation and a wall clear adds 0.1 plus one SP', () => {
  const { game, events, names } = make({ saturation: 0.05 });
  game.breakBrick(0);
  assert.ok(Math.abs(game.snapshot().sat - 0.062) < 1e-9);
  assert.equal(names()[0], 'brick');
  for (let i = 1; i < BRICK.cols * BRICK.rows; i++) game.breakBrick(i);
  const s = game.snapshot();
  assert.ok(names().includes('wall'));
  assert.equal(s.stats.walls, 1); assert.equal(s.stats.sp, 6, '1 SP for the wall plus 5 for the hidden jackpot brick');
  assert.equal(events.filter(e => e[0] === 'jackpot').length, 1, 'one jackpot per wall');
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
  let s = game.snapshot();
  assert.equal(s.state, 'colour', 'the cut waits for the slow-motion fall');
  assert.equal(s.transition.kind, 'relapse'); assert.ok(s.smear && s.smear.a === 1);
  assert.deepEqual(names(), ['relapseStart']);
  game.step(0.1);
  assert.equal(game.snapshot().timeScale, 0.35);
  for (let i = 0; i < 7; i++) game.step(0.1);
  s = game.snapshot();
  assert.equal(s.transition, null); assert.equal(s.timeScale, 1);
  assert.equal(s.state, 'grey'); assert.equal(s.sat, 0); assert.equal(s.savedSat, 0.6);
  assert.equal(s.balls.length, 1); assert.equal(s.balls[0].ghost, true); assert.equal(s.balls[0].stuck, true);
  assert.deepEqual(names(), ['relapseStart', 'relapse']);
  assert.ok(calls.some(c => c[0] === 'relapse') && calls.some(c => c[0] === 'setState' && c[1] === 'grey'));
  game.breakBrick(0);
  assert.equal(game.snapshot().sat, 0, 'grey bricks do not add saturation');
  game.loseBall();
  assert.deepEqual(names().filter(n => n !== 'jackpot'), ['relapseStart', 'relapse', 'brick', 'lost'], 'losing again while grey is not another relapse');
});

test('N grey bricks is a BREAKOUT: 0.3 s rewind, 100 ms freeze, then the world snaps back', () => {
  const { game, names, calls } = make({ saturation: 0.55, breakoutN: 3 });
  game.loseBall(); for (let i = 0; i < 8; i++) game.step(0.1);
  const plain = game.snapshot().bricks.map((b, i) => (b.gif < 0 && !b.split && !b.jackpot ? i : -1)).filter(i => i >= 0);
  game.breakBrick(plain[0]); game.breakBrick(plain[1]);
  assert.equal(game.snapshot().state, 'grey');
  game.breakBrick(plain[2]);
  let s = game.snapshot();
  assert.equal(s.pendingBreakout, true); assert.equal(s.transition.kind, 'breakout'); assert.equal(s.state, 'grey', 'still grey during the rewind');
  assert.equal(names().at(-1), 'breakoutStart');
  game.step(0.05);
  assert.equal(game.snapshot().state, 'grey');
  for (let i = 0; i < 5; i++) game.step(0.05);
  s = game.snapshot();
  assert.ok(s.freeze > 0, 'the freeze follows the rewind'); assert.equal(s.state, 'grey');
  game.step(0.11);
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

test('never lose (dev): the floor bounces the ball and no relapse starts', () => {
  const { game, names } = make({ saturation: 0.6 });
  game.setNoLose(true);
  const s = game.snapshot();
  const b = s.balls[0]; b.stuck = false; b.x = 40; b.y = s.paddle.y + 30; b.vx = 0; b.vy = 400;
  for (let i = 0; i < 20; i++) game.step(0.05, { x: 440 });
  assert.equal(game.snapshot().state, 'colour');
  assert.ok(!names().includes('relapseStart'));
  assert.ok(game.snapshot().balls[0].vy < 0 && !game.snapshot().balls[0].lost, 'bounced back up');
  game.setNoLose(false);
  game.loseBall(); for (let i = 0; i < 8; i++) game.step(0.1);
  assert.equal(game.snapshot().state, 'grey', 'off again, the ball is lost as usual');
});

test('dev hooks: relapseNow, breakoutNow and setSaturation', () => {
  const { game } = make({ saturation: 0.3 });
  game.setSaturation(0.8);
  assert.equal(game.snapshot().sat, 0.8);
  game.relapseNow(); for (let i = 0; i < 8; i++) game.step(0.1);
  assert.equal(game.snapshot().state, 'grey');
  game.breakoutNow(); for (let i = 0; i < 5; i++) game.step(0.1);
  assert.equal(game.snapshot().state, 'colour'); assert.equal(game.snapshot().sat, 0.8);
});

test('combo climbs on bricks, hit stop scales with it, and a paddle hit resets it', () => {
  const { game, events } = make({ saturation: 0.5, audio: { beat: { spb: 60 / 96, phase: () => 0.03 }, now: () => 0 } });
  for (let i = 0; i < 10; i++) game.breakBrick(i);
  let s = game.snapshot();
  assert.equal(s.combo, 10); assert.equal(s.comboBest, 10); assert.equal(s.hitStopMs, 80);
  assert.equal(events.filter(e => e[0] === 'hit' && e[1].combo === 10).length, 1);
  game.step(0.05); assert.equal(game.snapshot().hitStopMs, 30, 'hit stop counts down in real time');
  // Drop the ball onto the paddle: combo resets, and the hit lands on phase 0 so it is a perfect.
  for (let i = 0; i < 20; i++) game.step(0.05);
  s = game.snapshot();
  const b = s.balls[0]; b.stuck = false; b.x = s.paddle.x; b.y = s.paddle.y - 30; b.vx = 0; b.vy = 300;
  game.step(0.05);
  s = game.snapshot();
  assert.equal(s.combo, 0); assert.equal(s.comboBest, 10);
  assert.ok(events.some(e => e[0] === 'hit' && e[1].kind === 'paddle'));
  assert.ok(events.some(e => e[0] === 'perfect')); assert.ok(s.lastPerfectAt > 0);
});

test('every fifth wall spells a dealt word in bricks', () => {
  assert.equal(layoutWord('DROP').cells.length, 14 + 14 + 12 + 12);
  assert.equal(layoutWord('DROP').cols, 23);
  const { game, events } = make();
  game.setWords(['DROP']);
  for (let wall = 0; wall < 4; wall++) for (let i = game.snapshot().bricks.length - 1; i >= 0; i--) game.breakBrick(i);
  const s = game.snapshot();
  assert.equal(s.stats.walls, 4); assert.equal(s.mantra, 'DROP');
  assert.ok(s.bricks.length > 0 && s.bricks.every(b => b.letter && b.alive));
  assert.ok(s.bricks.some(b => b.letter === 'D') && s.bricks.some(b => b.letter === 'P'));
  assert.ok(s.bricks.every(b => b.x >= 0 && b.x + b.w <= 480));
  assert.ok(events.some(e => e[0] === 'mantra' && e[1].word === 'DROP'));
  assert.equal(s.bricks.filter(b => b.jackpot).length, 1);
  for (let i = s.bricks.length - 1; i >= 0; i--) game.breakBrick(i);
  assert.equal(game.snapshot().mantra, null, 'the next wall is plain again');
});

test('a brick hit pushes the brick and ripples jelly outward by ring', () => {
  const { game } = make();
  const s = game.snapshot();
  s.balls[0].stuck = false; s.balls[0].vx = 100; s.balls[0].vy = -100;
  const target = s.bricks.find(b => b.row === 3 && b.col === 5);
  game.breakBrick(s.bricks.indexOf(target));
  assert.ok(target.push.dy < 0 && target.push.dx > 0, 'pushed along the ball direction');
  const ring1 = s.bricks.find(b => b.row === 3 && b.col === 6), ring2 = s.bricks.find(b => b.row === 3 && b.col === 7);
  assert.equal(ring1.jelly, 0); assert.ok(ring1.jellyIn > 0 && ring2.jellyIn > ring1.jellyIn);
  game.step(0.1); game.step(0.1);
  assert.ok(ring1.jelly > 0 && ring2.jelly > 0);
  assert.equal(target.pushT, 0, 'push decayed after 120 ms');
});

test('a SPIRAL brick broken in colour pops out and bursts into the well (rung 7, one live); a GIF brick into a collider', () => {
  const { game, events, names } = make({ saturation: 0.75 });
  game.setForce(7, true);
  const s = game.snapshot();
  const sp = s.bricks.findIndex(b => b.alive && b.spiral);
  assert.ok(sp >= 0, 'the wall deals spiral bricks');
  const brick = s.bricks[sp];
  assert.ok(WELL_PRESETS.includes(brick.spiral) && brick.gif < 0 && !brick.word, 'a spiral brick wears a Loom preset, never a picture or a word');
  game.breakBrick(sp);
  assert.equal(s.pops.length, 1, 'the brick face pops out');
  assert.equal(s.pops[0].spiral, brick.spiral, 'the pop carries the brick field');
  assert.equal(s.pops[0].gif, -1, 'no picture on a spiral pop');
  assert.ok(names().includes('popOut'));
  assert.equal(s.well, null, 'nothing spawns before the burst');
  for (let i = 0; i < 80; i++) game.step(1 / 60, {});
  assert.equal(game.snapshot().pops.length, 0, 'the pop has burst within 1.3 s');
  const burst = events.find(e => e[0] === 'burst');
  assert.ok(burst && burst[1].kind === 'well', 'it burst into the well');
  const w = game.snapshot().well;
  assert.ok(w && w.r === 70 && w.pull === 110);
  assert.equal(w.preset, brick.spiral, 'the well is the field the brick showed');
  assert.equal(w.hue, brick.hue); assert.equal(w.spin, brick.spin);
  assert.ok(w.x >= 110 && w.x <= 370 && w.y >= 280 && w.y <= 520, 'inside the band');
  assert.equal(burst[1].x, w.x); assert.equal(burst[1].y, w.y);
  assert.equal(events.filter(e => e[0] === 'brick').at(-1)[1].plus, 3, 'a spiral brick is a special');
  // A second spiral brick while the well is live: the pop bursts into nothing but sparkle.
  const sp2 = s.bricks.findIndex((b, i) => b.alive && b.spiral && i !== sp);
  if (sp2 >= 0) {
    game.breakBrick(sp2);
    for (let i = 0; i < 80; i++) game.step(1 / 60, {});
    assert.equal(events.filter(e => e[0] === 'burst').at(-1)[1].kind, 'none', 'one well at a time');
    assert.equal(game.snapshot().colliders.length, 0, 'a spiral never becomes a collider');
  }
  // A GIF brick is always a collider, well or no well.
  const gif = s.bricks.findIndex(b => b.alive && b.gif >= 0);
  game.breakBrick(gif);
  for (let i = 0; i < 80; i++) game.step(1 / 60, {});
  assert.equal(game.snapshot().colliders.length, 1, 'a picture brick is a collider');
  assert.equal(game.snapshot().colliders[0].gif, s.bricks[gif].gif);
  // Below rung 7 a spiral brick's pop bursts into nothing; a GIF brick is still a collider.
  const low = make({ saturation: 0.3 });
  const s2 = low.game.snapshot(), l1 = s2.bricks.findIndex(b => b.alive && b.spiral), l2 = s2.bricks.findIndex(b => b.alive && b.gif >= 0);
  low.game.breakBrick(l1); low.game.breakBrick(l2);
  for (let i = 0; i < 80; i++) low.game.step(1 / 60, {});
  assert.equal(s2.well, null); assert.equal(s2.colliders.length, 1);
  assert.deepEqual(low.events.filter(e => e[0] === 'burst').map(e => e[1].kind).sort(), ['collider', 'none']);
});

test('word bricks swap their word on their own clocks with a glitch, in colour only, never the same word twice running', () => {
  const { game, events } = make({ saturation: 0.75, words: ['SINK', 'DROP', 'RELAX', 'BLANK'] });
  game.setNoLose(true);
  const s = game.snapshot();
  const worded = s.bricks.filter(b => b.word);
  assert.ok(worded.length >= 4, `${worded.length} word bricks`);
  const clocks = new Set(worded.map(b => b.wordAt.toFixed(3)));
  assert.ok(clocks.size >= worded.length - 1, 'each brick starts its clock somewhere else');
  const first = worded.map(b => b.word);
  for (let i = 0; i < 60; i++) game.step(1 / 60, { x: 240 });   // 1 s: nothing has swapped yet on most, some may have
  for (let i = 0; i < 120; i++) game.step(1 / 60, { x: 240 });  // 3 s in: every brick has swapped at least once
  const alive = worded.filter(b => b.alive);
  assert.ok(alive.every(b => b.swaps >= 1), 'every live word brick swapped within 3 s');
  assert.ok(alive.some((b, i) => b.word !== first[worded.indexOf(b)]), 'the words changed');
  const swaps = events.filter(e => e[0] === 'wordSwap');
  assert.ok(swaps.length >= alive.length, 'a wordSwap event per swap');
  assert.ok(swaps.every(e => ['SINK', 'DROP', 'RELAX', 'BLANK'].includes(e[1].word)));
  // The glitch runs 1 -> 0 in about a third of a second after a swap.
  const b0 = alive[0]; b0.wordAt = 0; const before = b0.word;
  game.step(1 / 60, { x: 240 });
  assert.ok(b0.glitch > 0.9 && b0.glitch <= 1, 'the glitch starts at 1');
  assert.notEqual(b0.word, before, 'a different word');
  for (let i = 0; i < 30; i++) game.step(1 / 60, { x: 240 });
  assert.equal(b0.glitch, 0, 'and is gone half a second later');
  // Not in grey: the clocks stop.
  game.setNoLose(false); game.loseBall(); for (let i = 0; i < 8; i++) game.step(0.1);
  assert.equal(game.snapshot().state, 'grey');
  const n0 = events.filter(e => e[0] === 'wordSwap').length;
  for (let i = 0; i < 240; i++) game.step(1 / 60, { x: 240 });
  assert.equal(events.filter(e => e[0] === 'wordSwap').length, n0, 'no swaps in grey');
});

test('no timer spawns: 20 s without a GIF or spiral brick broken leaves no well and no colliders', () => {
  const { game } = make({ saturation: 0.95 });
  game.setNoLose(true);
  for (const b of game.snapshot().bricks) { b.gif = -1; b.spiral = null; b.word = null; }   // the ball may hit bricks; only a GIF or spiral brick spawns (and a word would slow the clock)
  for (let i = 0; i < 20 * 60; i++) game.step(1 / 60, { x: 240 });
  const s = game.snapshot();
  assert.equal(s.well, null); assert.equal(s.colliders.length, 0);
  assert.ok(s.time >= 19);
});

test('in grey a special brick (gif, split, jackpot) is +3 on the counter, a plain one is +1', () => {
  const { game, events } = make({ saturation: 0.5, breakoutN: 40 });
  game.loseBall(); for (let i = 0; i < 8; i++) game.step(0.1);
  const s = game.snapshot();
  const idx = (fn) => s.bricks.findIndex(b => b.alive && fn(b));
  const plain = idx(b => b.gif < 0 && !b.split && !b.jackpot), gif = idx(b => b.gif >= 0 && !b.jackpot), jackpot = idx(b => b.jackpot);
  game.breakBrick(plain);
  assert.equal(s.greyBricks, 1);
  assert.equal(events.at(-2)[1].plus, 1);
  game.breakBrick(gif);
  assert.equal(s.greyBricks, 4);
  assert.equal(events.filter(e => e[0] === 'brick').at(-1)[1].plus, 3);
  assert.equal(s.pops.length, 0, 'no pop-out in grey'); assert.equal(s.well, null);
  game.breakBrick(jackpot);
  assert.equal(s.greyBricks, 7);
  const split = idx(b => b.split && b.gif < 0 && !b.jackpot);
  if (split >= 0) { game.breakBrick(split); assert.equal(s.greyBricks, 10); }
  assert.equal(events.filter(e => e[0] === 'burst').length, 0);
});

test('reduced motion: the well appears at once, no tumble', () => {
  const { game, events } = make({ saturation: 0.75, reduced: true });
  game.setForce(7, true);
  const s = game.snapshot(), sp = s.bricks.findIndex(b => b.alive && b.spiral);
  game.breakBrick(sp);
  assert.equal(s.pops.length, 0); assert.ok(s.well, 'the well is there on the same tick');
  assert.equal(events.filter(e => e[0] === 'burst').length, 1);
});
