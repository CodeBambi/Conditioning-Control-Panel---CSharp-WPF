/* node --test word-fx.test.js - the word triggers: bricks carry words, breaking one fires its effect, the rules hold. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGame, BRICK } from './game.js';
import { WORD_FX, WORD_KEYS, wordKey, HEAVY_GAP_S, MOD_DEFAULTS, freshMod } from './word-fx.js';

const seeded = (seed = 7) => () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
function make(opts = {}) {
  const events = [], calls = [];
  const audio = new Proxy({ beat: { spb: 60 / 96 } }, { get: (t, k) => k in t ? t[k] : (...a) => calls.push([k, ...a]) });
  const game = createGame({ rng: seeded(), audio, onEvent: (n, d) => events.push([n, d]), ...opts });
  return { game, events, calls };
}
const run = (game, secs, input = {}) => { for (let i = 0; i < Math.round(secs * 60); i++) game.step(1 / 60, input); };

test('every word module honours the contract', () => {
  for (const key of WORD_KEYS) {
    const d = WORD_FX[key];
    assert.equal(d.key, key);
    assert.equal(typeof d.heavy, 'boolean'); assert.ok(d.dur > 0 && d.dur <= 4, `${key} lasts ${d.dur}s`);
    for (const h of ['start', 'tick', 'end']) assert.equal(typeof d.sim[h], 'function', `${key}.sim.${h}`);
    for (const h of ['world', 'over', 'post']) assert.equal(typeof d.render[h], 'function', `${key}.render.${h}`);
    assert.equal(typeof d.sound, 'function');
  }
  assert.deepEqual(freshMod(), MOD_DEFAULTS);
});

test('mod words land on a family: first match wins, unknown words map to nothing', () => {
  assert.equal(wordKey('sink'), 'SINK'); assert.equal(wordKey('Bambi Sleep'), 'SINK');
  assert.equal(wordKey('let go'), 'LET GO'); assert.equal(wordKey('surrender'), 'LET GO');
  assert.equal(wordKey('Bambi Freeze'), 'BLANK'); assert.equal(wordKey('go deeper'), 'DEEPER');
  assert.equal(wordKey('drop for me'), 'DROP'); assert.equal(wordKey('breathe'), 'RELAX');
  assert.equal(wordKey('good girl'), null); assert.equal(wordKey(''), null); assert.equal(wordKey(null), null);
});

test('about one plain brick in six carries a word, dealt in turn, never on a picture brick', () => {
  const { game } = make({ words: ['SINK', 'DROP', 'RELAX', 'LET GO'] });
  const s = game.snapshot();
  const worded = s.bricks.filter(b => b.word);
  const plain = s.bricks.filter(b => b.gif < 0).length;
  assert.ok(worded.length > plain * 0.08 && worded.length < plain * 0.28, `${worded.length} of ${plain} plain bricks`);
  assert.ok(worded.every(b => b.gif < 0));
  assert.equal(worded[0].word, 'SINK'); assert.equal(worded[1].word, 'DROP'); assert.equal(worded[4].word, 'SINK');
  assert.equal(s.bricks.length, BRICK.cols * BRICK.rows);
});

test('breaking a word brick in COLOUR fires its effect; the effect runs on wall-clock and ends', () => {
  const { game, events } = make({ words: ['RELAX'] });
  const s = game.snapshot();
  const i = s.bricks.findIndex(b => b.word === 'RELAX');
  game.breakBrick(i);
  const w = events.filter(e => e[0] === 'word');
  assert.equal(w.length, 1); assert.equal(w[0][1].key, 'RELAX'); assert.equal(w[0][1].fired, true);
  assert.equal(s.fx.active.length, 1); assert.equal(s.fx.active[0].key, 'RELAX');
  run(game, 1);
  assert.ok(s.fx.active[0].phase > 0.25 && s.fx.active[0].phase < 0.45, `phase ${s.fx.active[0].phase} after 1 s of a 3 s word`);
  run(game, 2.2);
  assert.equal(s.fx.active.length, 0, 'ended');
  assert.deepEqual(s.mod, MOD_DEFAULTS, 'mods reset after the last effect');
});

test('a second heavy inside the gap is a stamp only; a soft word under a heavy is a stamp only', () => {
  const { game, events } = make({ words: ['SINK', 'DROP', 'RELAX'] });
  const s = game.snapshot();
  game.setNoLose(true);                                   // the unsteered ball would relapse into grey mid-test
  assert.ok(game.fireWordNow('SINK'), 'the first heavy fires');
  assert.equal(game.fireWordNow('DROP'), null, 'a heavy while a heavy runs is a stamp');
  assert.equal(game.fireWordNow('RELAX'), null, 'a soft under a heavy is a stamp');
  run(game, WORD_FX.SINK.dur + 0.1);
  assert.equal(s.fx.active.length, 0);
  assert.equal(game.fireWordNow('DROP'), null, 'still inside the heavy gap');
  assert.ok(game.fireWordNow('RELAX'), 'a soft word fires once the heavy is over');
  run(game, HEAVY_GAP_S);
  assert.ok(game.fireWordNow('DROP'), 'the gap has passed');
  assert.equal(s.fx.active.filter(f => f.key === 'RELAX').length, 0, 'the heavy ended the soft one');
  const fired = events.filter(e => e[0] === 'word').map(e => e[1].fired);
  assert.deepEqual(fired, [true, false, false, false, true, true]);
});

test('in GREY a word brick is +3 and fires nothing; a relapse ends every running word', () => {
  const { game, events } = make({ words: ['SINK'] });
  const s = game.snapshot();
  assert.ok(game.fireWordNow('SINK'));
  game.relapseNow(); run(game, 1.2);
  assert.equal(s.state, 'grey'); assert.equal(s.fx.active.length, 0, 'the relapse ended the word');
  const i = s.bricks.findIndex(b => b.alive && b.word);
  events.length = 0;
  game.breakBrick(i);
  const br = events.find(e => e[0] === 'brick')[1];
  assert.equal(br.plus, 3); assert.equal(br.word, 'SINK');
  assert.equal(events.filter(e => e[0] === 'word').length, 0);
  assert.equal(game.fireWordNow('SINK'), null, 'no effects in grey');
});

test('the mods do what they say: safe keeps the ball, autopilot steers the paddle, paddleW and ballSpeed scale', () => {
  const { game } = make({ words: ['SINK'] });
  const s = game.snapshot();
  const fx = game.fireWordNow('SINK');
  // Borrow the running effect: pin the mods through a tick hook of our own.
  const tickOld = WORD_FX.SINK.sim.tick;
  WORD_FX.SINK.sim.tick = (g) => { g.mod.safe = true; g.mod.autopilot = true; g.mod.paddleW = 1.5; g.mod.ballSpeed = 0.5; };
  try {
    game.launchNow();
    const b = s.balls[0]; b.x = 60; b.y = s.h - 20; b.vx = 0; b.vy = 900;
    run(game, 0.5, { x: 400 });
    assert.equal(s.state, 'colour', 'safe: the floor bounced');
    assert.equal(s.balls.length, 1);
    assert.ok(Math.abs(s.paddle.x - s.balls[0].x) < 120, `autopilot ignores the player's x=400 (paddle ${s.paddle.x | 0}, ball ${s.balls[0].x | 0})`);
    assert.ok(s.paddle.w > 90 * (1 + 0.6 * s.sat) * 1.4);
    assert.ok(Math.hypot(s.balls[0].vx, s.balls[0].vy) < s.speed / 0.5 * 0.55 + 1);
  } finally { WORD_FX.SINK.sim.tick = tickOld; }
  assert.ok(fx);
});
