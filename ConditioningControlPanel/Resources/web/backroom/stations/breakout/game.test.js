/* node --test game.test.js - the state machine and the saturation ladder, nothing visual. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DOME_AIM_TURNS, DOME_AIM_AT, DOME_AIM_FEW, DOME_AIM_TURNS_FEW, DOME_TURN_JITTER, DOME_DRAW_RATE, TAIL_BRICKS, TAIL_LIMIT_S, TAIL_DROP_GAP_S, domeAims, domeAimTurns, steerToward, turnToward, PADDLE, SPLIT_CHANCE, BUBBLE_DRIFT, BUBBLE_PUSH, BUBBLE_MAX, createGame, gifScaleForWall, bubbleTier, rungsFor, layoutWord, RUNG_AT, BRICK, WELL_PRESETS } from './game.js';

const seeded = (seed = 7) => () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
// These scoring/state fixtures use one-hit walls; durability has its own integration suite.
function make(opts = {}, colour = true) {
  const events = [], calls = [];
  const audio = new Proxy({ beat: { spb: 60 / 96 } }, { get: (t, k) => k in t ? t[k] : (...a) => calls.push([k, ...a]) });
  const game = createGame({ brickStrength: [], greyMetal: false, rng: seeded(), audio, onEvent: (n, d) => events.push([n, d]), ...opts });
  if (colour) {
    game.breakoutNow();
    for (let i = 0; i < 4; i++) game.step(0.1);
    game.snapshot().breakoutShield = null;
    events.length = 0; calls.length = 0;
  }
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

test('a fresh session starts in GREY with saved colour and a stuck ghost ball', () => {
  const { game, calls } = make({}, false);
  const s = game.snapshot();
  assert.equal(s.state, 'grey'); assert.equal(s.sat, 0); assert.equal(s.savedSat, 0.15);
  assert.equal(s.breakoutN, 20); assert.equal(s.greyBricks, 0);
  assert.equal(s.rungs.filter(Boolean).length, 1);
  assert.ok(calls.some(c => c[0] === 'setState' && c[1] === 'grey'));
  assert.equal(s.bricks.length, BRICK.cols * BRICK.rows);
  assert.equal(s.balls.length, 1); assert.equal(s.balls[0].stuck, true); assert.equal(s.balls[0].ghost, true);
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
  assert.ok(Math.abs(s.sat - Math.min(1, 0.05 + BRICK.cols * BRICK.rows * 0.012 + 0.1)) < 1e-9);
  assert.equal(events.filter(e => e[0] === 'crack').length, 0, 'colour play never cracks the screen');
  for (let i = 0; i < 60; i++) game.breakBrick(i);
  assert.equal(game.snapshot().sat, 1, 'capped at 1');
  assert.equal(events.filter(e => e[0] === 'crack').length, 0, 'saturation never triggers cracks');
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
  const plain = game.snapshot().bricks.map((b, i) => (b.gif < 0 && !b.spiral && !b.word && !b.split && !b.jackpot ? i : -1)).filter(i => i >= 0);
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
  for (let i = 0; i < 6; i++) game.step(0.05, { x: 440 });
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

test('combo climbs without pausing motion, and a paddle hit resets it', () => {
  const { game, events } = make({ saturation: 0.5, audio: { beat: { spb: 60 / 96, phase: () => 0.03 }, now: () => 0 } });
  for (let i = 0; i < 10; i++) game.breakBrick(i);
  let s = game.snapshot();
  assert.equal(s.combo, 10); assert.equal(s.comboBest, 10); assert.equal(s.hitStopMs, 0);
  assert.equal(events.filter(e => e[0] === 'hit' && e[1].combo === 10).length, 1);
  game.step(0.01); assert.equal(game.snapshot().hitStopMs, 0, 'routine hits do not freeze gameplay');
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

test('Spell is the third sequential wall and uses a varied word-shaped formation', () => {
  const { game } = make();
  const s = game.snapshot();
  assert.equal(s.spell, null);
  for (let wall = 0; wall < 2; wall++) {
    assert.equal(s.spell, null);
    for (let i = s.bricks.length - 1; i >= 0; i--) game.breakBrick(i);
  }
  assert.equal(s.stats.walls, 2); assert.ok(s.spell);
  assert.equal(s.bricks.length, layoutWord(s.spell.word).cells.length);
  assert.ok(s.bricks.every(b => b.letter && b.alive && b.x >= 0 && b.x + b.w <= s.w));
  const targets = new Set();
  for (let i = 0; i < 12; i++) { game.jumpToWall(3); targets.add(s.spell.word); }
  assert.ok(targets.size > 1, 'entries vary instead of hardcoding DROP');
  game.jumpToWall(4); assert.equal(s.spell, null);
  game.jumpToWall(5); assert.equal(s.spell, null, 'old fifth-wall mantra retired');
});

test('Spell fills matching slots, cycles useful letters, completes once and resets without slowing play', () => {
  const { game, events } = make(); game.jumpToWall(3);
  const s = game.snapshot(), first = s.spell.word;
  for (const index of Array.from(first).map((_, i) => i).reverse()) {
    if (first[index] === ' ') continue;
    const brick = s.bricks.findIndex(b => b.alive);
    s.bricks[brick].letter = first[index];
    const before = s.spell.filled.filter(Boolean).length;
    game.breakBrick(brick); game.breakBrick(brick);
    assert.equal(s.spell.filled.filter(Boolean).length, before + 1, 'one live brick fills only one matching slot');
    assert.equal(s.hitStopMs, 0); assert.equal(s.freeze, 0); assert.equal(s.timeScale, 1);
    const useful = Array.from(first).filter((letter, i) => !s.spell.filled[i]);
    if (useful.length) assert.ok(s.bricks.filter(b => b.alive).every(b => useful.includes(b.letter)));
  }
  assert.equal(events.filter(e => e[0] === 'spellComplete').length, 1);
  assert.ok(s.spell.complete); assert.equal(s.spell.celebrate, 1.6);
  game.breakBrick(s.bricks.findIndex(b => b.alive));
  assert.equal(events.filter(e => e[0] === 'spellComplete').length, 1);
  // Park all balls, including split rewards, while advancing the celebration clock.
  for (const ball of s.balls) { ball.stuck = true; ball.vx = ball.vy = 0; }
  for (let i = 0; i < 16; i++) { s.launchTimer = 0; game.step(0.1); }
  assert.equal(s.spell.round, 2); assert.notEqual(s.spell.word, first);
  assert.equal(s.spell.complete, false);
  assert.ok(s.spell.filled.every((value, i) => value === (s.spell.word[i] === ' ')));
});

test('two balls fill separate Spell slots in one step and grey hits do not reveal the target', () => {
  const { game, events } = make(); game.jumpToWall(3);
  const s = game.snapshot();
  const targets = [s.bricks[0], s.bricks.find(b => b.row === 0 && b.x > s.bricks[0].x + 70)];
  assert.ok(targets[1]);
  s.balls = targets.map(br => ({ ...s.balls[0], stuck: false, trail: [], x: br.x + br.w / 2,
    y: br.y + br.h + s.balls[0].r - 1, vx: 0, vy: -400 }));
  game.step(1 / 120);
  assert.equal(events.filter(e => e[0] === 'spellFill').length, 2);
  assert.equal(s.spell.filled.filter(Boolean).length, 2 + (s.spell.word.includes(' ') ? 1 : 0));
  const grey = make({}, false).game; grey.jumpToWall(3);
  grey.breakBrick(0);
  assert.equal(grey.snapshot().spell.filled.filter(Boolean).length, grey.snapshot().spell.word.includes(' ') ? 1 : 0);
  assert.equal(grey.snapshot().greyBricks, (grey.snapshot().bricks[0].jackpot || grey.snapshot().bricks[0].split || grey.snapshot().bricks[0].spiral || grey.snapshot().bricks[0].gif >= 0) ? 3 : 1);
});

test('a last-brick Spell completion holds its celebration before the next wall', () => {
  const { game, events } = make(); game.jumpToWall(3);
  const s = game.snapshot(), last = s.bricks[0];
  for (const brick of s.bricks) brick.alive = false;
  last.alive = true; last.letter = s.spell.word[0];
  s.spell.filled.fill(true); s.spell.filled[0] = false;
  game.breakBrick(0);
  assert.equal(s.stats.walls, 2); assert.ok(s.spell.complete);
  for (let i = 0; i < 16; i++) { s.launchTimer = 0; game.step(0.1); }
  assert.equal(s.stats.walls, 3); assert.equal(s.spell, null);
  assert.equal(events.filter(e => e[0] === 'spellComplete').length, 1);
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
  for (const ball of s.balls) { ball.stuck = true; ball.vx = ball.vy = 0; }
  for (let i = 0; i < 80; i++) game.step(1 / 60, {});
  assert.equal(game.snapshot().pops.length, 0, 'the pop has burst within 1.3 s');
  const burst = events.find(e => e[0] === 'burst');
  assert.ok(burst && burst[1].kind === 'well', 'it burst into the well');
  const w = game.snapshot().well;
  assert.ok(w && w.r === 70 * 1.33 && w.pull === 110 * 1.33);
  assert.equal(w.preset, brick.spiral, 'the well is the field the brick showed');
  assert.equal(w.hue, brick.hue); assert.equal(w.spin, brick.spin);
  assert.ok(w.x >= 110 && w.x <= s.w - 110 && w.y >= 280 && w.y <= 520, 'inside the band');
  assert.equal(burst[1].x, w.x); assert.equal(burst[1].y, w.y);
  assert.equal(events.filter(e => e[0] === 'brick').at(-1)[1].plus, 3, 'a spiral brick is a special');
  // A second spiral brick while the well is live and empty: the new well replaces it (one at a time, never a collider).
  const sp2 = s.bricks.findIndex((b, i) => b.alive && b.spiral && i !== sp);
  if (sp2 >= 0) {
    game.breakBrick(sp2);
    for (const ball of s.balls) { ball.stuck = true; ball.vx = ball.vy = 0; }
  for (let i = 0; i < 80; i++) game.step(1 / 60, {});
    assert.equal(events.filter(e => e[0] === 'burst').at(-1)[1].kind, 'well', 'the new spiral takes over');
    assert.equal(game.snapshot().well.preset, s.bricks[sp2].spiral);
    assert.equal(game.snapshot().colliders.length, 0, 'a spiral never becomes a collider');
  }
  // A GIF brick is always a collider, well or no well.
  const gif = s.bricks.findIndex(b => b.alive && b.gif >= 0);
  game.breakBrick(gif);
  for (const ball of s.balls) { ball.stuck = true; ball.vx = ball.vy = 0; }
  for (let i = 0; i < 80; i++) game.step(1 / 60, {});
  assert.equal(game.snapshot().colliders.length, 1, 'a picture brick is a collider');
  assert.equal(game.snapshot().colliders[0].gif, s.bricks[gif].gif);
  // Below rung 7 a spiral brick still makes its well (the brick is the gate); a GIF brick is still a collider.
  const low = make({ saturation: 0.3 });
  const s2 = low.game.snapshot(), l1 = s2.bricks.findIndex(b => b.alive && b.spiral), l2 = s2.bricks.findIndex(b => b.alive && b.gif >= 0);
  low.game.breakBrick(l1); low.game.breakBrick(l2);
  for (let i = 0; i < 80; i++) low.game.step(1 / 60, {});
  assert.ok(s2.well && s2.well.preset === s2.bricks[l1].spiral, 'the well is there at low saturation'); assert.equal(s2.colliders.length, 1);
  assert.deepEqual(low.events.filter(e => e[0] === 'burst').map(e => e[1].kind).sort(), ['collider', 'well']);
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
  const plain = idx(b => b.gif < 0 && !b.spiral && !b.word && !b.split && !b.jackpot), gif = idx(b => b.gif >= 0 && !b.jackpot), jackpot = idx(b => b.jackpot);
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


test('grey costs shrink per completed breakout, survive misses and wall clears, and reset per sit-down', () => {
  const { game } = make({ saturation: 0.42 }, false);
  const s = game.snapshot();
  for (const cost of [20, 15, 11, 8, 6, 5, 5]) {
    // Exercise ordinary wall clears; the wall-7 finale gate has its own lifecycle tests.
    game.jumpToWall(1);
    assert.equal(s.state, 'grey'); assert.equal(s.breakoutN, cost);
    // End a wall during grey, then miss: neither operation resets earned progress or cost.
    for (const br of s.bricks) br.alive = false;
    Object.assign(s.bricks[0], { alive: true, gif: -1, spiral: null, word: null, split: false, jackpot: false, irisCore: false });
    game.breakBrick(0);
    assert.equal(s.greyBricks, 1); assert.equal(s.breakoutN, cost);
    game.loseBall();
    assert.equal(s.greyBricks, 1); assert.equal(s.breakoutN, cost); assert.equal(s.savedSat, 0.42);
    for (let i = 0; i < cost - 1; i++) {
      Object.assign(s.bricks[i], { gif: -1, spiral: null, word: null, split: false, jackpot: false, irisCore: false });
      game.breakBrick(i);
      assert.equal(s.pendingBreakout, i === cost - 2, 'only the threshold starts breakout');
    }
    for (let i = 0; i < 4; i++) game.step(0.1);
    assert.equal(s.state, 'colour'); assert.equal(s.sat, 0.42);
    assert.equal(s.balls.length, 1, 'breakout adds no gameplay ball');
    game.loseBall(); for (let i = 0; i < 7; i++) game.step(0.1);
  }
  assert.equal(make({}, false).game.snapshot().breakoutN, 20);
});

test('each grey special earns three and explicit debug N stays fixed across relapses', () => {
  const { game } = make({ breakoutN: 40 }, false);
  const s = game.snapshot();
  for (const [i, special] of [{ gif: 0 }, { spiral: 'candy' }, { word: 'DROP' }, { split: true }, { jackpot: true }].entries()) {
    Object.assign(s.bricks[i], { gif: -1, spiral: null, word: null, split: false, jackpot: false }, special);
    game.breakBrick(i);
    assert.equal(s.greyBricks, (i + 1) * 3);
  }
  game.breakoutNow(); for (let i = 0; i < 4; i++) game.step(0.1);
  game.loseBall(); for (let i = 0; i < 7; i++) game.step(0.1);
  assert.equal(s.breakoutN, 40);
  game.setBreakoutN(2); assert.equal(s.breakoutN, 2);
});


test('bubble tier distribution uses 70/20/10 boundaries', () => {
  assert.deepEqual([0, 0.6999, 0.7, 0.8999, 0.9, 0.9999].map(bubbleTier), [1, 1, 2, 2, 3, 3]);
});
for (const tier of [1, 2, 3]) test(`tier ${tier} requires exact hits and rewards only its final pop`, () => {
  const { game, events } = make(); const s = game.snapshot();
  s.bricks = []; game.launchNow();
  const c = { x: 240, y: 400, r: 46, vx: 0, vy: 0, hits: 0, tier, gif: 0, age: 80, alpha: 1, pulse: 0, fading: false };
  s.colliders = [c]; const initial = s.sat;
  for (let hit = 1; hit <= tier; hit++) {
    s.freeze = 0;
    Object.assign(s.balls[0], { x: 187, y: 400, vx: 300, vy: 0, stuck: false, lost: false });
    game.step(1 / 60);
    assert.equal(c.hits, hit);
    assert.equal(c.fading, hit === tier);
    assert.equal(events.filter(e => e[0] === 'bubblePop').length, hit === tier ? 1 : 0);
    assert.ok(Math.abs(s.sat - initial - (hit === tier ? [0, .03, .06, .10][tier] : 0)) < 1e-9);
  }
});


test('Spell picture and spiral tiles retain normal payloads', () => {
  const { game, events } = make();
  game.jumpToWall(3);
  const s = game.snapshot();
  for (const kind of ['gif', 'spiral']) {
    const index = s.bricks.findIndex(b => b.alive && (kind === 'gif' ? b.gif >= 0 : !!b.spiral));
    assert.ok(index >= 0, `formation contains ${kind}`);
    game.breakBrick(index);
    const pop = events.filter(e => e[0] === 'popOut').at(-1)?.[1];
    assert.ok(pop);
    assert.ok(kind === 'gif' ? pop.gif >= 0 : !!pop.spiral);
  }
});


test('landscape formations fit the arena and leave a clear play lane', () => {
  const { game } = make();
  for (const wall of [1, 3]) {
    game.jumpToWall(wall);
    const s = game.snapshot();
    assert.equal(s.w / s.h, 16 / 9);
    for (const b of s.bricks) {
      assert.ok(b.x >= 0 && b.x + b.w <= s.w);
      assert.ok(b.y + b.h < s.paddle.y - 250);
    }
  }
});


test('GIF growth starts smaller, reaches its cap at wall six and stays capped', () => {
  assert.equal(gifScaleForWall(0), .8);
  assert.ok(Math.abs(gifScaleForWall(5) - 1.7) < 1e-9);
  assert.equal(gifScaleForWall(99), gifScaleForWall(5));
  for (let wall = 1; wall <= 5; wall++) assert.ok(gifScaleForWall(wall) > gifScaleForWall(wall - 1));
});


test('breakout shield catches a miss and expires after its flicker', () => {
  const { game } = make({}, false);
  game.breakoutNow(); for (let i = 0; i < 4; i++) game.step(.1);
  const s = game.snapshot(); assert.ok(s.breakoutShield);
  const b = s.balls[0]; b.stuck = false; b.x = 20; b.y = s.paddle.y + 14; b.vx = 0; b.vy = 300;
  game.step(.05, { x: s.w / 2 });
  assert.ok(b.vy < 0); assert.equal(s.state, 'colour');
  s.balls = [];
  for (let i = 0; i < 19; i++) game.step(.1);
  assert.ok(s.breakoutShield && s.mod.shield);
  for (let i = 0; i < 8; i++) game.step(.1);
  assert.equal(s.breakoutShield, null); assert.equal(s.mod.shield, false);
});

 test('grey fractures follow counter and shatter only on breakout', () => {
  const { game, events } = make({ breakoutN: 20 }, false);
  game.breakBrick(0);
  const s = game.snapshot();
  assert.equal(s.fractures, Math.min(1, s.greyBricks / 20));
  assert.ok(s.fractures > 0);
  assert.equal(events.filter(e => e[0] === 'shatterWall').length, 0);
  game.breakoutNow();
  for (let i = 0; i < 4; i++) game.step(.1);
  assert.equal(s.state, 'colour');
  assert.equal(s.fractures, 0);
  assert.equal(events.filter(e => e[0] === 'shatterWall').length, 1);
 });


test('wall four is a separated dome with a persistent central well; wall seven replaces the dome with pendulums', () => {
  const { game } = make(); game.jumpToWall(4);
  const s = game.snapshot(), bricks = s.bricks;
  assert.equal(s.dome, true); assert.equal(s.spell, null); assert.equal(bricks.length, 85);
  assert.equal(s.well.persistent, true); assert.equal(s.well.x, s.w / 2); assert.equal(s.well.y, s.h / 2);
  for (let i = 0; i < bricks.length; i++) for (let j = i + 1; j < bricks.length; j++) {
    const a = bricks[i], b = bricks[j];
    assert.ok(a.x + a.w <= b.x || b.x + b.w <= a.x || a.y + a.h <= b.y || b.y + b.h <= a.y, 'no overlapping collision boxes');
  }
  const well = s.well; well.age = 100; s.balls = []; game.step(.1);
  assert.equal(s.well, well); assert.equal(well.fade, 1);
  game.jumpToWall(7); assert.equal(s.dome, false); assert.equal(s.well, null);
  assert.equal(s.pendulums.length, 3); assert.equal(s.bricks.length, 171);
});

test('dome energy rises on hits, remains bounded, and falls between hits', () => {
  const { game } = make(); game.jumpToWall(4);
  const s = game.snapshot(), well = s.well; s.balls = [];
  game.breakBrick(0); assert.ok(well.energy > 0);
  for (let i = 1; i < 12; i++) game.breakBrick(i);
  assert.equal(well.energy, 1);
  const angle = well.rot; game.step(.1);
  assert.ok(well.energy < 1 && well.energy > .95); assert.ok(well.rot < angle);
  assert.ok(well.spin <= 3.2);
});

test('dome releases with a bounded boost and cannot immediately recapture or replace its held ball', () => {
  const { game } = make(); game.jumpToWall(4);
  const s = game.snapshot(), well = s.well, ball = s.balls[0];
  well.energy = 1; ball.stuck = false; ball.x = well.x + 85; ball.y = well.y; ball.vx = 0; ball.vy = -300;
  game.step(.01); assert.equal(well.captured, ball); assert.ok(ball.orbit.turns < 1.15);
  const other = { ...ball, orbit: null, trail: [], x: well.x + 70 }; s.balls.push(other);
  game.step(.01); assert.equal(well.captured, ball); assert.equal(other.orbit, null);
  ball.orbit.done = 100; game.step(.01);
  assert.equal(ball.orbit, null); assert.equal(well.captured, null); assert.equal(well.used, false);
  assert.ok(ball.domeBoost > 0 && ball.domeBoost <= .25); assert.ok(ball.domeCooldown > 1);
  game.step(.1); assert.equal(ball.orbit, null);
});

test('dome is absent in grey and restored by breakout on the same wall', () => {
  const { game } = make({}, false); game.jumpToWall(4);
  const s = game.snapshot(); assert.equal(s.well, null);
  game.breakoutNow(); for (let i = 0; i < 5; i++) game.step(.1);
  assert.equal(s.well.persistent, true);
  game.relapseNow(); for (let i = 0; i < 8; i++) game.step(.1);
  assert.equal(s.state, 'grey'); assert.equal(s.well, null);
  game.breakoutNow(); for (let i = 0; i < 5; i++) game.step(.1);
  assert.equal(s.well.persistent, true);
});


test('expired and barely visible wells cannot capture a passing ball', () => {
  for (const state of [{ age: 6, fade: .05, born: 6 }, { age: 0, fade: 1, born: 0 }]) {
    const { game } = make(); const s = game.snapshot(), b = s.balls[0];
    b.stuck = false; b.x = 650; b.y = 420; b.vx = 100; b.vy = 200;
    s.well = { x: 640, y: 420, r: 90, pull: 145, ttl: 6, rot: 0, used: false, captured: null, ...state };
    game.step(.01);
    assert.equal(b.orbit, null);
    assert.ok(b.vx > 0 && b.vy > 0);
  }
});

test('an exiting ball cannot start relapse while another ball survives, regardless of iteration order',()=>{
 for(const reverse of [false,true]) for(const count of [2,3]) {
  const {game,events}=make();const s=game.snapshot();
  const base={...s.balls[0],stuck:false,falling:false,lost:false,orbit:null};
  const gone={...base,x:50,y:740,vx:0,vy:200,trail:[]};
  const survivors=Array.from({length:count-1},(_,i)=>({...base,x:600+i*60,y:500,vx:0,vy:-100,trail:[]}));
  s.balls=reverse?[...survivors,gone]:[gone,...survivors];
  game.step(.02);
  assert.equal(s.state,'colour');assert.equal(s.transition,null);assert.equal(s.balls.length,count-1);
  assert.equal(events.filter(e=>e[0]==='relapseStart').length,0);
  while(s.balls.length>1)game.loseBall();
  assert.equal(s.transition,null);game.loseBall();
  assert.equal(s.transition.kind,'relapse');
  assert.equal(events.filter(e=>e[0]==='relapseStart').length,1);
 }
});

test('cleanup aim ramps through the last twenty percent only at paddle bounces',()=>{
 let previous = Infinity;
 for(const count of [1,8,16,17]) {
  const {game}=make();const s=game.snapshot();s.wallAge=2;
  s.bricks.forEach((br,i)=>Object.assign(br,{alive:i<count,x:800+i*20,y:100,angle:0,reformSafe:false}));
  const b=s.balls[0];Object.assign(b,{stuck:false,x:s.paddle.x,y:s.paddle.y-s.paddle.h/2-b.r-1,vx:0,vy:220});
  game.step(.02);
  assert.ok(b.vy<0);
  if(count<=16){const turn=Math.atan2(b.vx,-b.vy);assert.ok(turn>0);assert.ok(turn<=Math.PI/12+1e-8);assert.ok(turn<previous);previous=turn;}
  else assert.equal(b.vx,0);
  const direction=Math.atan2(b.vx,-b.vy);game.step(.02);
  assert.ok(Math.abs(Math.atan2(b.vx,-b.vy)-direction)<1e-8,'no mid-flight steering');
 }
});


test('Tide occupies wall two, keeps payloads while moving, and hands off to Spell', () => {
  const { game } = make();
  game.jumpToWall(2);
  const s = game.snapshot(), bricks = s.bricks.slice();
  assert.equal(s.stats.walls, 1); assert.ok(s.tide); assert.equal(bricks.length, 80);
  assert.equal(s.dome, false); assert.equal(s.well, null);
  for (const kind of ['gif', 'spiral', 'word', 'split']) {
    assert.ok(bricks.some(b => kind === 'gif' ? b.gif >= 0 : b[kind]), kind + ' roster retained');
  }
  s.balls = [];
  const dead = bricks.findIndex(b => b.gif < 0 && !b.spiral && !b.word && !b.split && !b.jackpot);
  const first = bricks[dead === 0 ? 1 : 0], x = first.x;
  const payloads = bricks.map(b => [b.gif, b.spiral, b.split]);
  game.breakBrick(dead);
  for (let i = 0; i < 100; i++) game.step(.05);
  assert.notEqual(first.x, x); assert.equal(s.bricks.length, 80);
  assert.equal(s.bricks[dead].alive, false, 'the current never refills destroyed pieces');
  for (let i = 0; i < bricks.length; i++) {
    assert.equal(s.bricks[i], bricks[i], 'surviving identities remain stable');
    assert.deepEqual([bricks[i].gif, bricks[i].spiral, bricks[i].split], payloads[i]);
  }
  for (let i = 0; i < bricks.length; i++) game.breakBrick(i);
  assert.equal(s.stats.walls, 2); assert.equal(s.tide, null); assert.ok(s.spell);
});

test('Tide reduced motion is still and its moving faces use oriented ball collision', () => {
  const quiet = make({ reduced: true }).game; quiet.jumpToWall(2);
  const qs = quiet.snapshot(); qs.balls = [];
  const pose = qs.bricks.map(b => [b.x, b.y, b.angle]);
  for (let i = 0; i < 20; i++) quiet.step(.05);
  assert.deepEqual(qs.bricks.map(b => [b.x, b.y, b.angle]), pose);
  const { game } = make(); game.jumpToWall(2);
  const s = game.snapshot(), br = s.bricks[70], ball = s.balls[0];
  Object.assign(br, { gif: -1, word: null, spiral: null, split: false, jackpot: false });
  const nx = -Math.sin(br.angle), ny = Math.cos(br.angle), d = br.h / 2 + ball.r + 1;
  Object.assign(ball, { stuck: false, x: br.x + br.w / 2 + nx * d, y: br.y + br.h / 2 + ny * d, vx: -nx * 300, vy: -ny * 300 });
  game.step(.02);
  assert.equal(br.alive, false);
  assert.ok(ball.vx * nx + ball.vy * ny > 0, 'ball rebounds away from the visible face');
});

test('opening ball waits for wall arrival even when launch is pressed', () => {
  const game = createGame(); game.replayEntrance();
  for(let i=0;i<18;i++) game.step(.1,{launch:true});
  assert.equal(game.snapshot().balls[0].stuck,true);
  for(let i=0;i<3;i++) game.step(.1,{launch:true});
  assert.equal(game.snapshot().balls[0].stuck,false);
});


test('loss recovery retains a short falling transition and relaunches on the first downbeat after 1.2 seconds',()=>{
 const {game}=make({saturation:.5});game.loseBall();
 for(let i=0;i<4;i++)game.step(.1);assert.equal(game.snapshot().state,'colour');
 game.step(.1);const s=game.snapshot();assert.equal(s.state,'grey');assert.equal(s.balls[0].stuck,true);
 for(let i=0;i<6;i++)game.step(.1);assert.equal(s.balls[0].stuck,true,'the serve holds for its 1.2 s');
 for(let i=0;i<8;i++)game.step(.1);assert.equal(s.balls[0].stuck,false,'and leaves within one more beat');
});

/* ------------------------------------------------------------ feel pass (2026-09-21) */
const lastIndex = s => s.bricks.findIndex(b => b.alive);
function clearBut(game, keep = 1) { const s = game.snapshot(); while (s.bricks.filter(b => b.alive).length > keep) game.breakBrick(lastIndex(s)); }
const beatAudio = ref => ({ beat: { spb: 0.625, phase: () => ref.ph }, now: () => 0 });
function dropOnPaddle(game, off = 0) {
  const s = game.snapshot(), p = s.paddle;
  s.balls = [{ ...s.balls[0], x: p.x + off, y: p.y - p.h / 2 - 12, vx: 0, vy: 300, stuck: false, ghost: false, trail: [] }];
  game.step(1 / 60); game.step(1 / 60);
  return s.balls[0];
}

test('the last brick fires once, before its wall, and bends time only in COLOUR with motion on', () => {
  const { game, names } = make({ saturation: 0.2 });
  clearBut(game);
  let s = game.snapshot();
  assert.equal(names().includes('lastBrick'), false); assert.equal(s.hitStopMs, 0, 'routine bricks never stall the ball');
  game.breakBrick(lastIndex(s), true);
  assert.equal(names().filter(n => n === 'lastBrick').length, 1);
  assert.equal(names().includes('wall'), false, 'the next wall is held back while the moment plays');
  assert.equal(s.hitStopMs, 90); assert.ok(s.clearing && s.clearing.wall);
  game.step(0.05); assert.equal(s.hitStopMs, 40, 'the freeze runs first');
  game.step(0.05); game.step(0.05);
  assert.equal(s.timeScale, 0.35, 'then the slow-mo'); assert.equal(s.stats.walls, 0);
  for (let i = 0; i < 10; i++) game.step(0.05);
  assert.equal(s.stats.walls, 1); assert.equal(s.clearing, null); assert.equal(s.timeScale, 1);
  const order = names().filter(n => n === 'lastBrick' || n === 'wall');
  assert.deepEqual(order, ['lastBrick', 'wall']);
  for (const opts of [{ reduced: true }, { grey: true }]) {
    const t = make({ saturation: 0.2, reduced: !!opts.reduced, breakoutN: 9999 }, !opts.grey);
    clearBut(t.game); t.game.breakBrick(lastIndex(t.game.snapshot()), true);
    assert.deepEqual(t.names().filter(n => n === 'lastBrick' || n === 'wall'), ['lastBrick', 'wall'], 'the event still leads the wall');
    assert.equal(t.game.snapshot().hitStopMs, 0); assert.equal(t.game.snapshot().clearing, null);
  }
});

test('a relapse during the last-brick moment lands the held wall first', () => {
  const { game, names } = make({ saturation: 0.2 });
  clearBut(game); game.breakBrick(lastIndex(game.snapshot()), true);
  game.relapseNow(); for (let i = 0; i < 8; i++) game.step(0.1);
  const s = game.snapshot();
  assert.equal(s.state, 'grey'); assert.equal(s.stats.walls, 1); assert.ok(s.bricks.some(b => b.alive));
  assert.equal(names().filter(n => n === 'wall').length, 1);
});

test('hit-stop belongs to the jackpot and a bubble final pop, nothing routine', () => {
  const { game } = make({ saturation: 0.5 });
  const s = game.snapshot();
  game.breakBrick(s.bricks.findIndex(b => !b.jackpot), true); assert.equal(s.hitStopMs, 0);
  game.breakBrick(s.bricks.findIndex(b => b.jackpot), true); assert.equal(s.hitStopMs, 60);
  s.hitStopMs = 0;
  s.colliders.push({ x: 600, y: 420, r: 50, vx: 0, vy: 0, hits: 0, pulse: 0, alpha: 1, fading: false, gif: 0, tier: 2, age: 1 });
  const ball = () => { s.balls = [{ ...s.balls[0], x: 600, y: 480, vx: 0, vy: -300, stuck: false, ghost: false, trail: [] }]; };
  ball(); game.step(1 / 60); game.step(1 / 60);
  assert.equal(s.colliders[0].hits, 1); assert.equal(s.hitStopMs, 0, 'a bubble that holds does not stall');
  ball(); game.step(1 / 60); game.step(1 / 60);
  assert.ok(s.hitStopMs > 0 && s.hitStopMs <= 40, 'the final pop does');
});

test('a bounce squashes the ball against the surface and springs back', () => {
  const { game } = make({ saturation: 0.3 });
  const s = game.snapshot();
  s.balls = [{ ...s.balls[0], x: 10, y: 400, vx: -300, vy: -200, stuck: false, ghost: false, trail: [] }];
  game.step(1 / 60);
  const b = s.balls[0];
  assert.ok(b.squash > 0.7 && b.sqx > 0.99, 'the left wall pushes back along +x');
  for (let i = 0; i < 8; i++) game.step(1 / 60);
  assert.equal(b.squash, 0);
});

test('perfect paddle hits count a streak; an ordinary hit resets it', () => {
  const ref = { ph: 0.02 };
  const { game, events } = make({ saturation: 0.2, audio: beatAudio(ref) });
  const s = game.snapshot(), streaks = () => events.filter(e => e[0] === 'perfect').map(e => e[1].streak);
  const before = s.sat;
  dropOnPaddle(game); dropOnPaddle(game); dropOnPaddle(game);
  assert.deepEqual(streaks(), [1, 2, 3]);
  assert.ok(Math.abs(s.sat - before - (0.06 + 0.003 + 0.006)) < 1e-9, 'a tiny capped bonus per streak step');
  ref.ph = 0.5; dropOnPaddle(game);
  assert.equal(s.perfectStreak, 0);
  ref.ph = 0.97; dropOnPaddle(game);
  assert.deepEqual(streaks(), [1, 2, 3, 1]);
});

test('layer fires once per crossing, one per hit, and a relapse re-arms it', () => {
  const { game, events } = make({ saturation: 0.395 });
  const s = game.snapshot(), layers = () => events.filter(e => e[0] === 'layer').map(e => e[1].name);
  let i = 0;
  game.breakBrick(i++); assert.deepEqual(layers(), ['melody']);
  game.breakBrick(i++); game.breakBrick(i++); assert.deepEqual(layers(), ['melody']);
  s.sat = 0.695; game.breakBrick(i++); assert.deepEqual(layers(), ['melody', 'arp']);
  game.breakBrick(i++); assert.deepEqual(layers(), ['melody', 'arp']);
  game.relapseNow(); for (let k = 0; k < 8; k++) game.step(0.1);
  assert.equal(s.state, 'grey'); game.breakBrick(i++); assert.equal(layers().length, 2, 'never in GREY');
  game.breakoutNow(); for (let k = 0; k < 6; k++) game.step(0.1);
  assert.equal(s.state, 'colour');
  game.breakBrick(i++); assert.deepEqual(layers(), ['melody', 'arp', 'melody']);
  game.breakBrick(i++); assert.deepEqual(layers(), ['melody', 'arp', 'melody', 'arp']);
});

test('the auto launch waits for the downbeat; a manual launch does not', () => {
  const ref = { ph: 0.5 };
  const { game, names } = make({ saturation: 0.2, audio: beatAudio(ref) });
  const s = game.snapshot();
  s.balls = [{ ...s.balls[0], stuck: true, vx: 0, vy: 0 }]; s.launchTimer = 0;
  for (let i = 0; i < 80; i++) game.step(1 / 60);
  assert.equal(s.balls[0].stuck, true, '1.33 s in and no beat boundary yet');
  ref.ph = 0.05; game.step(1 / 60);
  assert.equal(s.balls[0].stuck, false); assert.ok(names().includes('launch'));
  s.balls = [{ ...s.balls[0], stuck: true, vx: 0, vy: 0 }]; s.launchTimer = 0;
  game.step(1 / 60, { launch: true });
  assert.equal(s.balls[0].stuck, false, 'a tap is immediate');
  s.balls = [{ ...s.balls[0], stuck: true, vx: 0, vy: 0 }]; s.launchTimer = 0;
  for (let i = 0; i < 120; i++) game.step(1 / 60);
  assert.equal(s.balls[0].stuck, false, 'a stalled clock launches after one beat of grace');
});

test('paddle english is bounded at 8 degrees and never passes the 60 degree tips', () => {
  const { game } = make({ saturation: 0.2, audio: beatAudio({ ph: 0.5 }) });
  const s = game.snapshot(), deg = b => Math.atan2(b.vx, -b.vy) * 180 / Math.PI;
  s.paddle.vx = 0; assert.ok(Math.abs(deg(dropOnPaddle(game))) < 1e-6, 'a still paddle at the centre sends it straight up');
  s.paddle.vx = 1e5; const right = dropOnPaddle(game); assert.ok(deg(right) > 7.9 && deg(right) <= 8 + 1e-6);
  s.paddle.vx = -1e5; assert.ok(Math.abs(deg(dropOnPaddle(game)) + 8) < 0.1);
  s.paddle.vx = 1e5; assert.ok(deg(dropOnPaddle(game, s.paddle.w / 2)) <= 60 + 1e-6);
});

test('keys ease in to full speed in about 120 ms and stop dead on release', () => {
  const { game } = make({ saturation: 0.2 });
  const s = game.snapshot(), dt = 1 / 60, moves = [];
  for (let i = 0; i < 12; i++) { const x = s.paddle.x; game.step(dt, { left: true }); moves.push(x - s.paddle.x); }
  assert.ok(moves[0] > 0 && moves[0] < 640 * dt * 0.1, 'a tap is a nudge');
  assert.ok(moves[3] < moves[7], 'it builds'); assert.ok(Math.abs(moves[11] - 640 * dt) < 1e-6, 'to the full 640 px/s');
  const x = s.paddle.x; game.step(dt, {}); assert.equal(s.paddle.x, x);
  game.step(dt, { right: true }); assert.ok(s.paddle.x - x < 640 * dt * 0.1, 'a reversal starts from rest');
});

test('a hit bubble jiggles, is shoved away from the ball and settles back to its drift', () => {
  const { game, events } = make({ saturation: 0.5 });
  const s = game.snapshot();
  s.colliders.push({ x: 600, y: 420, r: 50, vx: BUBBLE_DRIFT, vy: 0, hits: 0, pulse: 0, alpha: 1, fading: false, gif: 0, tier: 3, age: 1, jelly: 0, ph: 1 });
  s.balls = [{ ...s.balls[0], x: 600, y: 480, vx: 0, vy: -300, stuck: false, ghost: false, trail: [] }];
  game.step(1 / 60); game.step(1 / 60);
  const c = s.colliders[0]; assert.equal(c.hits, 1);
  assert.ok(c.jelly > .9 && c.jny > .9, 'jelly rings along the hit normal');
  assert.ok(c.vy < -BUBBLE_PUSH * .8, 'pushed up, away from a ball that came from below');
  const hit = events.find(e => e[0] === 'gif')[1];
  assert.ok(Math.abs(hit.hx - 600) < 2 && Math.abs(hit.hy - 470) < 2 && hit.ny > .9, 'the event carries the contact point');
  s.balls = [{ ...s.balls[0], x: 100, y: 650, vx: 0, vy: 0, stuck: true }];
  for (let i = 0; i < 240; i++) game.step(1 / 60);
  assert.equal(c.jelly, 0); assert.ok(Math.abs(Math.hypot(c.vx, c.vy) - BUBBLE_DRIFT) < .5, 'back to the resting drift');
});

test('a bubble hammered by several balls never leaves faster than its cap', () => {
  const { game } = make({ saturation: 0.5 });
  const s = game.snapshot();
  s.colliders.push({ x: 600, y: 420, r: 50, vx: 0, vy: 0, hits: 0, pulse: 0, alpha: 1, fading: false, gif: 0, tier: 9, age: 1, jelly: 0, ph: 0 });
  for (let i = 0; i < 6; i++) {
    const c = s.colliders[0]; c.x = 600; c.y = 420;
    s.balls = [{ ...s.balls[0], x: 600, y: 480, vx: 0, vy: -300, stuck: false, ghost: false, trail: [] }];
    game.step(1 / 60); game.step(1 / 60);
  }
  assert.ok(s.colliders[0].hits >= 4); assert.ok(Math.hypot(s.colliders[0].vx, s.colliders[0].vy) <= BUBBLE_MAX + 1e-6);
});

test('the colour paddle grows by half at full saturation, no more; split bricks are rare', () => {
  assert.equal(PADDLE.grow, 0.5); assert.ok(SPLIT_CHANCE <= 0.025);
  const { game } = make({ saturation: 1 });
  const s = game.snapshot(); game.step(1 / 60);
  assert.ok(Math.abs(s.paddle.w - PADDLE.baseW * (1 + PADDLE.grow * s.sat) * s.mod.paddleW) < 1e-6);
  assert.ok(s.paddle.w <= PADDLE.baseW * 1.5 + 1e-6);
});

test('the dome spiral holds its ball until the throw points at a brick, and gives up after its extra turns', () => {
  const aimed = (keep) => {
    const { game } = make({ saturation: 0.5 }); game.jumpToWall(4);
    const s = game.snapshot(), well = s.well, ball = s.balls[0];
    s.bricks.forEach((br, i) => { br.alive = keep(br, i); });
    Object.assign(ball, { stuck: false, x: well.x + 70, y: well.y, vx: 0, vy: 0 }); game.step(.01);
    assert.equal(well.captured, ball);
    const due = ball.orbit.turns * Math.PI * 2; let released = -1;
    for (let i = 0; i < 2000 && ball.orbit; i++) { const done = ball.orbit.done; game.step(1 / 120); if (!ball.orbit) released = done; }
    return { s, ball, due, released };
  };
  const two = aimed((br, i) => i === 3 || i === 4);
  assert.ok(two.released >= two.due - .1, 'never before its turns are done');
  assert.ok(two.released < two.due + (domeAimTurns(2) + .1) * Math.PI * 2);
  const sp = Math.hypot(two.ball.vx, two.ball.vy), ux = two.ball.vx / sp, uy = two.ball.vy / sp;
  const onLine = two.s.bricks.filter(br => br.alive).some(br => { const dx = br.x + br.w / 2 - two.ball.x, dy = br.y + br.h / 2 - two.ball.y;
    return dx * ux + dy * uy > 0 && Math.abs(dx * uy - dy * ux) < Math.hypot(br.w, br.h) / 2 + two.ball.r + 8; });
  assert.ok(onLine, 'the throw is on a line with one of the two bricks left');
  // Above a quarter of the wall left, the spiral does not aim at all: it lets go on time wherever that points.
  assert.equal(DOME_AIM_AT, .25);
  assert.ok(!domeAims(85, 85) && !domeAims(23, 85) && domeAims(22, 85) && domeAims(2, 85) && !domeAims(0, 85) && !domeAims(0, 0));
  const total = two.s.bricks.length, early = aimed((br, i) => i < Math.ceil(total * .4));
  assert.ok(early.released >= early.due - .1 && early.released < early.due + .3, 'with 40 percent left it lets go on time');
  const none = aimed(() => false);
  assert.ok(none.released >= none.due - .1 && none.released < none.due + .3, 'an empty wall lets go on time');
});

test('with five bricks or fewer the dome always aims, holds longer, and throws at the nearest brick when the sweep never lines one up', () => {
  // The few-bricks rule is a floor under the quarter rule, so it also bites on a wall too small for a quarter to mean anything.
  assert.equal(DOME_AIM_FEW, 5);
  assert.ok(domeAims(5, 12) && domeAims(1, 12) && !domeAims(6, 12) && !domeAims(0, 12));
  assert.equal(domeAimTurns(5), DOME_AIM_TURNS_FEW); assert.equal(domeAimTurns(6), DOME_AIM_TURNS); assert.ok(DOME_AIM_TURNS_FEW > DOME_AIM_TURNS);
  // One brick sitting in the well's own centre: no tangent of the orbit ever points at it, so the sweep gives up and steers.
  const { game } = make({ saturation: 0.5 }); game.jumpToWall(4);
  const s = game.snapshot(), well = s.well, ball = s.balls[0];
  s.bricks.forEach((br, i) => { br.alive = i === 0; });
  const br = s.bricks[0]; br.x = well.x - br.w / 2; br.y = well.y - br.h / 2;
  Object.assign(ball, { stuck: false, x: well.x + 70, y: well.y, vx: 0, vy: 0 }); game.step(.01);
  assert.equal(well.captured, ball);
  const due = ball.orbit.turns * Math.PI * 2; let released = -1;
  for (let i = 0; i < 4000 && ball.orbit; i++) { const done = ball.orbit.done; game.step(1 / 120); if (!ball.orbit) released = done; }
  assert.ok(released >= due + (DOME_AIM_TURNS_FEW - .1) * Math.PI * 2, 'it held for the longer few-bricks allowance');
  const sp = Math.hypot(ball.vx, ball.vy), dx = br.x + br.w / 2 - ball.x, dy = br.y + br.h / 2 - ball.y, d = Math.hypot(dx, dy);
  assert.ok(sp > 0 && (ball.vx * dx + ball.vy * dy) / (sp * d) > .999, 'the throw points straight at the last brick');
  // steerToward keeps the speed and leaves a still ball alone.
  const st = steerToward(3, 4, 0, 0, 10, 0); assert.ok(Math.abs(st.vx - 5) < 1e-9 && Math.abs(st.vy) < 1e-9);
  assert.deepEqual(steerToward(0, 0, 0, 0, 10, 0), { vx: 0, vy: 0 });
  assert.deepEqual(steerToward(3, 4, 1, 1, 1, 1), { vx: 3, vy: 4 });
});

test('a flat dome throw reaches its brick: the shuttle rule does not bend an aimed ball until it touches a wall or the paddle', () => {
  // One brick left, level with the well and far to its left: the straight line is 7 degrees off horizontal, under the 14 the shuttle rule wants.
  const { game, events } = make({ saturation: 0.5 }); game.jumpToWall(4);
  const s = game.snapshot(), well = s.well, ball = s.balls[0]; s.noLose = true;
  s.bricks.forEach((br, i) => { br.alive = i === 0; });
  const br = s.bricks[0]; br.x = well.x - 400 - br.w / 2; br.y = well.y - 50 - br.h / 2; br.hp = 1; br.strength = 0;
  Object.assign(ball, { stuck: false, x: well.x + 70, y: well.y, vx: 0, vy: 0 }); game.step(.01);
  assert.equal(well.captured, ball);
  for (let i = 0; i < 4000 && ball.orbit; i++) game.step(1 / 120);
  assert.equal(ball.orbit, null, 'thrown');
  const sp = Math.hypot(ball.vx, ball.vy);
  assert.ok(Math.abs(ball.vy) < .25 * sp, 'the throw is flatter than the shuttle rule allows');
  assert.ok(ball.aimed > 0);
  events.length = 0;
  for (let i = 0; i < 240 && br.alive; i++) game.step(1 / 120);
  assert.ok(!br.alive, 'the brick is hit inside two seconds');
  assert.ok(!events.some(e => e[0] === 'wallhit'), 'without touching a wall first');
  // A ball that is not aimed still obeys the rule.
  const { game: g2 } = make({ saturation: 0.5 }); const s2 = g2.snapshot(); s2.bricks = []; s2.noLose = true;
  const b2 = s2.balls[0]; Object.assign(b2, { stuck: false, x: 640, y: 300, vx: 400, vy: 10, aimed: 0 }); g2.step(1 / 120);
  assert.ok(Math.abs(b2.vy) >= .24 * Math.hypot(b2.vx, b2.vy), 'an unaimed flat ball is bent to the minimum');
  // turnToward is bounded and keeps the speed.
  const t = turnToward(10, 0, 0, 0, 0, 10, Math.PI / 4);
  assert.ok(Math.abs(Math.hypot(t.vx, t.vy) - 10) < 1e-9 && Math.abs(t.vx - t.vy) < 1e-9 && t.vy > 0);
  assert.deepEqual(turnToward(0, 0, 0, 0, 5, 5, 1), { vx: 0, vy: 0 });
});

test('with five bricks or fewer the dome draws a free ball outside its reach back in, and never above five', () => {
  const ride = (alive) => {
    const { game } = make({ saturation: 0.5 }); game.jumpToWall(4);
    const s = game.snapshot(), well = s.well, ball = s.balls[0]; s.noLose = true;
    s.bricks.forEach((br, i) => { br.alive = i < alive; });
    // Riding the right edge top to bottom, 450 px from the well: the loop the owner saw.
    Object.assign(ball, { stuck: false, x: well.x + 450, y: well.y + 200, vx: 5, vy: -420 });
    let captured = false, minD = Infinity;
    for (let i = 0; i < 120 * 8 && !captured; i++) { game.step(1 / 120); if (ball.orbit) captured = true; minD = Math.min(minD, Math.hypot(ball.x - well.x, ball.y - well.y)); }
    return { captured, minD };
  };
  assert.ok(DOME_DRAW_RATE > 0);
  assert.ok(ride(DOME_AIM_FEW).captured, 'five left: drawn in and caught inside eight seconds');
  const many = ride(DOME_AIM_FEW + 20);
  assert.ok(!many.captured && many.minD > 300, 'with a wall still up the edge ride is left alone');
});

test('the tail: five bricks or fewer for two minutes and the rest fall off on their own, lowest first', () => {
  assert.equal(TAIL_BRICKS, 5); assert.equal(TAIL_LIMIT_S, 120);
  const { game, events } = make({ saturation: 0.5 });   // wall one: still bricks (wall two's tide would carry the parked ones back in)
  const s = game.snapshot(); s.noLose = true;
  // Three bricks left, parked above the field where no ball can reach them.
  s.bricks.forEach((br, i) => { br.alive = i < 3; if (br.alive) br.y = -1000 - i * 40; });
  const walls = s.stats.walls;
  const secs = (n) => { for (let i = 0; i < Math.round(n * 120); i++) game.step(1 / 120); };
  secs(TAIL_LIMIT_S - 1);
  assert.equal(s.bricks.filter(b => b.alive).length, 3, 'nothing falls before the limit');
  assert.ok(s.tail > TAIL_LIMIT_S - 4 && s.tail < TAIL_LIMIT_S, 'the clock runs');
  secs(1 + TAIL_DROP_GAP_S + .05);
  const drops = events.filter(e => e[0] === 'tailDrop');
  assert.equal(drops.length, 1, 'one brick lets go at the limit');
  assert.equal(drops[0][1].y, s.bricks[0].y + s.bricks[0].h / 2, 'the lowest brick first');
  secs(TAIL_DROP_GAP_S * 3 + 3);
  assert.equal(s.stats.walls, walls + 1, 'the wall clears through the ordinary break');
  assert.equal(s.tail, 0, 'the clock resets with the wall');
  // Six bricks: no clock.
  const { game: g2 } = make({ saturation: 0.5 }); const s2 = g2.snapshot(); s2.noLose = true;
  s2.bricks.forEach((br, i) => { br.alive = i < 6; if (br.alive) br.y = -1000 - i * 40; });
  for (let i = 0; i < 120 * 5; i++) g2.step(1 / 120);
  assert.equal(s2.tail, 0);
});

test('every dome throw wears a little jitter on its turns, so a fixed catch is not a fixed throw', () => {
  const turnsAt = (r) => {
    const { game } = make({ saturation: 0.5, rng: () => r }); game.jumpToWall(4);
    const s = game.snapshot(), well = s.well, ball = s.balls[0]; well.energy = 0;
    Object.assign(ball, { stuck: false, x: well.x + 70, y: well.y, vx: 0, vy: 0 }); game.step(.01);
    assert.equal(well.captured, ball);
    return ball.orbit.turns;
  };
  const lo = turnsAt(0), hi = turnsAt(0.999);
  assert.ok(Math.abs((hi - lo) - DOME_TURN_JITTER * .999) < 1e-6, 'the spread is the jitter constant');
  assert.ok(lo > 0.9 && hi < 1.4, 'still about one held turn either way');
  assert.ok(DOME_TURN_JITTER <= .4, 'small: the sweep still reads as one beat');
});

test('a newborn bubble is not solid until it can be seen and no ball is inside it', () => {
  const { game } = make({ saturation: 0.5 });
  const s = game.snapshot(); s.bricks = [];
  const fly = () => { s.balls = [{ ...s.balls[0], x: 600, y: 470, vx: 0, vy: -300, stuck: false, ghost: false, trail: [] }]; return s.balls[0]; };
  s.colliders.push({ x: 600, y: 420, r: 50, vx: 0, vy: 0, hits: 0, pulse: 0, alpha: 0, fading: false, solid: false, gif: 0, tier: 3, age: 0, jelly: 0, ph: 0 });
  let b = fly(); for (let i = 0; i < 12; i++) game.step(1 / 120);
  assert.equal(s.colliders[0].hits, 0, 'still fading in: the ball passes through'); assert.ok(b.vy < 0, 'and keeps its heading');
  b = fly(); b.x = 600; b.y = 420; b.vx = 0; b.vy = 0; s.colliders[0].alpha = 1;
  game.step(1 / 120); assert.equal(s.colliders[0].solid, false, 'visible, but a ball is inside: it waits'); assert.equal(b.x, 600);
  b = fly(); b.y = 600; game.step(1 / 120); assert.equal(s.colliders[0].solid, true, 'clear and visible: solid from here on');
  b = fly(); b.y = 480; for (let i = 0; i < 6 && !s.colliders[0].hits; i++) game.step(1 / 120);
  assert.equal(s.colliders[0].hits, 1, 'and now it bounces');
});
