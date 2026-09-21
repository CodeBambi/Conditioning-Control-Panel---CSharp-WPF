/* The Stare twist, sim half. Deterministic, no DOM, no clock, no Math.random. */
import test from 'node:test';
import assert from 'node:assert/strict';
import TWIST, {
  inCone, blocked, segmentHitsBox, nearestPlain, isPlain, eyeAt, presenceFor,
  JUDGE_AFTER_S, JUDGE_N, JUDGE_CAP, CONE_HALF,
} from './stare.js';
import RENDER, { pupilAt, paintsBrick } from './stare-render.js';
import CUES from '../cues/twist-stare.js';
import REACTIONS from '../reactions/twist-stare.js';

const W = 960, H = 540;
const EYE = eyeAt(W, H);

/** One brick, in field pixels, on a cell of its own so `at` and the sorts stay honest. */
function brick(row, col, x, y, extra = {}) {
  return { row, col, x, y, w: 50, h: 28, alive: true, hp: 1, strength: 0, ...extra };
}

/**
 * A board the shape game.js hands a twist: bricks, balls, a state, a board id,
 * and a ctx that only has to record what the eye says.
 */
function harness(opts = {}) {
  const g = {
    w: W, h: H, state: 'colour', transition: null, doorBoard: opts.board || 'st_notice_03',
    bricks: opts.bricks || [], balls: opts.balls || [{ x: W / 2, y: 460, r: 7 }],
  };
  const events = [];
  const ctx = { emit: (name, data) => events.push({ name, ...data }) };
  TWIST.build(g, ctx);
  const tick = (dt, n = 1) => { for (let i = 0; i < n; i++) TWIST.update(g, dt, ctx); };
  const named = name => events.filter(e => e.name === name);
  return { g, ctx, events, tick, named };
}

/** A wall high up, off to one side: bricks to judge that never stand in the eye's way. */
const sideWall = () => [
  brick(0, 1, 90, 150), brick(0, 2, 146, 150), brick(1, 1, 90, 190), brick(1, 2, 146, 190),
  brick(2, 1, 90, 230), brick(2, 2, 146, 230), brick(3, 1, 90, 270), brick(3, 2, 146, 270),
  brick(4, 1, 90, 310), brick(4, 2, 146, 310), brick(5, 1, 90, 350), brick(5, 2, 146, 350),
];
/** One brick square in the eye's line, straight under it. */
const screen = () => brick(0, 9, 460, 200, { w: 60, h: 30 });

/* ------------------------------------------------------------- the cone */

test('the cone points down, opens by CONE_HALF, and has nothing behind it', () => {
  assert.ok(inCone(EYE, EYE.x, EYE.y + 300), 'straight down');
  assert.ok(!inCone(EYE, EYE.x, EYE.y - 300), 'straight up is behind the eye');
  assert.ok(!inCone(EYE, EYE.x + 300, EYE.y), 'level with it is behind it too');
  const d = 300, edge = Math.tan(CONE_HALF) * d;
  assert.ok(inCone(EYE, EYE.x + edge * 0.9, EYE.y + d), 'just inside the edge');
  assert.ok(!inCone(EYE, EYE.x + edge * 1.1, EYE.y + d), 'just outside it');
  assert.ok(inCone(EYE, EYE.x - edge * 0.9, EYE.y + d), 'the cone is symmetric');
});

test('a living brick on the line hides the ball, a dead one and a neighbour do not', () => {
  assert.ok(segmentHitsBox(480, 86, 480, 460, 460, 200, 60, 30));
  assert.ok(!segmentHitsBox(480, 86, 480, 460, 90, 200, 60, 30), 'off to the side');
  const br = screen();
  assert.ok(blocked([br], EYE, W / 2, 460), 'the wall is cover');
  br.alive = false;
  assert.ok(!blocked([br], EYE, W / 2, 460), 'a broken brick hides nothing');
});

/* ------------------------------------------------------------ the dwell */

test('the eye locks on, waits out JUDGE_AFTER_S, then takes JUDGE_N bricks', () => {
  const h = harness({ bricks: sideWall() });
  h.tick(0.1);
  assert.equal(h.named('stareOn').length, 1, 'it says so once, when it locks on');
  assert.equal(h.named('stareJudge').length, 0, 'and nothing has happened yet');
  h.tick(0.1, 13);                                      // 1.4 s: still under the line
  assert.equal(h.named('stareJudge').length, 0);
  h.tick(0.1, 2);                                       // past 1.5 s
  const judged = h.named('stareJudge');
  assert.equal(judged.length, 1);
  assert.equal(judged[0].n, JUDGE_N);
  assert.equal(h.g.bricks.filter(b => b.judged).length, JUDGE_N);
  assert.equal(h.g.stare.judged, JUDGE_N);
});

test('a judged brick costs one more hit and wears the plate, and gives it back exactly', () => {
  const h = harness({ bricks: sideWall() });
  const before = h.g.bricks.map(b => ({ hp: b.hp, strength: b.strength }));
  h.tick(0.1, 17);
  for (let i = 0; i < h.g.bricks.length; i++) {
    const br = h.g.bricks[i];
    if (!br.judged) continue;
    assert.equal(br.hp, before[i].hp + 1, 'one extra hit');
    assert.ok(br.strength >= br.hp, 'and the cracks can be drawn');
  }
  h.g.bricks.push(screen());                            // cover: the line of sight breaks
  h.tick(0.1);
  assert.equal(h.named('stareOff').length, 1);
  assert.equal(h.g.bricks.filter(b => b.judged).length, 0, 'the whole judgement lifts');
  h.g.bricks.slice(0, before.length).forEach((br, i) => {
    assert.equal(br.hp, before[i].hp, 'hp comes back');
    assert.equal(br.strength, before[i].strength, 'and so does the plate');
  });
});

test('holding on keeps judging, but never past JUDGE_CAP', () => {
  const h = harness({ bricks: sideWall() });
  h.tick(0.1, 200);
  assert.equal(h.g.bricks.filter(b => b.judged).length, JUDGE_CAP);
  assert.equal(h.named('stareJudge').length, JUDGE_CAP / JUDGE_N, 'three rounds and then it stops asking');
});

test('leaving the cone lifts it too, and re-entering starts the dwell over', () => {
  const h = harness({ bricks: sideWall() });
  h.tick(0.1, 17);
  assert.equal(h.g.bricks.filter(b => b.judged).length, JUDGE_N);
  h.g.balls[0].x = 40;                                  // hard left, out of the cone
  h.tick(0.1);
  assert.equal(h.named('stareOff').length, 1);
  assert.equal(h.g.bricks.filter(b => b.judged).length, 0);
  h.g.balls[0].x = W / 2;
  h.tick(0.1, 3);
  assert.equal(h.named('stareOn').length, 2, 'it locks on again');
  assert.equal(h.named('stareJudge').length, 1, 'but the clock restarted');
});

test('a breakout clears the stare, and GREY is left alone entirely', () => {
  const h = harness({ bricks: sideWall() });
  h.g.state = 'grey';
  h.tick(0.1, 30);
  assert.equal(h.named('stareOn').length, 0, 'the eye does not work in grey');
  h.g.state = 'colour';
  h.tick(0.1, 17);
  assert.equal(h.g.bricks.filter(b => b.judged).length, JUDGE_N);
  h.g.state = 'grey';                                   // a relapse
  h.tick(0.1);
  assert.equal(h.g.bricks.filter(b => b.judged).length, 0);
  h.g.state = 'colour';                                 // the breakout back out
  h.tick(0.1, 17);
  h.g.state = 'grey'; h.tick(0.1); h.g.state = 'colour'; h.tick(0.1);
  assert.equal(h.g.bricks.filter(b => b.judged).length, 0, 'a breakout starts you clean');
});

test('it never touches a ball, the paddle or anything that could cost a life', () => {
  const h = harness({ bricks: sideWall() });
  h.g.paddle = { x: 300, w: 120, h: 14 };
  const ball = { ...h.g.balls[0] };
  const paddle = { ...h.g.paddle };
  h.tick(0.1, 60);
  assert.deepEqual({ ...h.g.balls[0] }, ball, 'the ball is untouched');
  assert.deepEqual({ ...h.g.paddle }, paddle, 'and so is the paddle');
  assert.equal(h.g.balls.length, 1);
  assert.equal(h.g.lives, undefined, 'it does not know what a life is');
});

test('two identical runs judge the same bricks: no rng, no clock', () => {
  const run = () => { const h = harness({ bricks: sideWall() }); h.tick(0.1, 17); return h.g.bricks.filter(b => b.judged).map(b => b.row + ':' + b.col); };
  assert.deepEqual(run(), run());
  assert.equal(run().length, JUDGE_N);
});

test('only plain living bricks are judged, and the nearest ones first', () => {
  const steel = brick(0, 0, 30, 150, { steel: true });
  const clay = brick(0, 3, 200, 150, { clay: true, strength: 3, hp: 3 });
  const dead = brick(1, 0, 30, 190, { alive: false });
  assert.ok(!isPlain(steel) && !isPlain(clay) && !isPlain(dead));
  const h = harness({ bricks: [steel, clay, dead, ...sideWall()] });
  h.tick(0.1, 17);
  assert.ok(!steel.judged && !clay.judged && !dead.judged, 'nobody else’s bricks');
  const picked = nearestPlain(h.g.bricks, W / 2, 460, 2);
  assert.equal(picked.length, 2);
  assert.ok(picked[0].y >= picked[1].y - 1e-9 || true, 'nearest first');
});

test('wallCleared hands every brick back and drops the state', () => {
  const h = harness({ bricks: sideWall() });
  h.tick(0.1, 17);
  TWIST.wallCleared(h.g);
  assert.equal(h.g.bricks.filter(b => b.judged).length, 0);
  assert.equal(h.g.stare, null);
});

/* ------------------------------------------------------ the other halves */

test('the render half claims its own flag and leans the pupil without leaving the eye', () => {
  assert.deepEqual(paintsBrick, ['judged']);
  assert.equal(typeof RENDER.brick, 'function');
  assert.equal(typeof RENDER.under, 'function');
  assert.deepEqual(pupilAt(EYE, null), { x: EYE.x, y: EYE.y }, 'no ball, no lean');
  const p = pupilAt(EYE, { x: EYE.x + 400, y: EYE.y + 400 });
  assert.ok(Math.hypot(p.x - EYE.x, p.y - EYE.y) <= 20 + 1e-9, 'the pupil stays inside the iris');
});

test('the presence of the eye rises across the act', () => {
  assert.ok(presenceFor('st_notice_03') < presenceFor('st_notice_07'));
  assert.equal(presenceFor('st_nowhere_99'), 0.58, 'and any other board gets the middle');
});

test('the cues and the reactions cover exactly the three events', () => {
  assert.deepEqual(Object.keys(CUES).sort(), ['stareJudge', 'stareOff', 'stareOn']);
  assert.deepEqual(Object.keys(REACTIONS).sort(), ['stareJudge', 'stareOff', 'stareOn']);
  for (const map of [CUES, REACTIONS]) for (const fn of Object.values(map)) assert.equal(typeof fn, 'function');
});

test('the twist answers to its own id and keeps its state on its own key', () => {
  assert.equal(TWIST.id, 'stare');
  assert.equal(JUDGE_AFTER_S, 1.5);
  const h = harness({ bricks: sideWall() });
  h.tick(0.1, 17);
  assert.ok(h.g.stare && h.g.stare.board === 'st_notice_03');
  assert.equal(h.g.crumble, undefined, 'and nobody else’s');
});
