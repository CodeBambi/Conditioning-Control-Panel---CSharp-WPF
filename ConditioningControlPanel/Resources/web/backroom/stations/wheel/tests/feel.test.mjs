import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as feel from '../feel.js';
import { FEEL, tierOf, recipe, winTokens, tickValues, tick, glance, landPose, shiverPx, breath, POSES, revealCount, bezier } from '../feel.js';
import { readResult } from '../wheel.js';

const R = (o) => readResult({ day: '2026-09-14', sliceId: 'x', sliceIndex: 1, pay: 0, snoozeCarryPaid: 0, jackpot: false, jackpotFallback: false, snoozed: false, ...o });

test('tiers by pay: Snooze 0, 1-3 small, 5-20 bigger, 40 and 100 big, the pot on top', () => {
  assert.equal(tierOf(R({ snoozed: true })), 0);
  assert.deepEqual([1, 2, 3].map(pay => tierOf(R({ pay }))), [1, 1, 1]);
  assert.deepEqual([5, 8, 12, 20].map(pay => tierOf(R({ pay }))), [2, 2, 2, 2]);
  assert.deepEqual([40, 100].map(pay => tierOf(R({ pay }))), [3, 3]);
  assert.equal(tierOf(R({ pay: 100, jackpotFallback: true })), 3, 'a gated pot hit pays Dazed, a big win, not the reveal');
  assert.equal(tierOf(R({ pay: 550, jackpot: true })), 4);
  assert.equal(tierOf(null), 0);
});

test('the old size-based fx table is gone: moments decide the fullscreen (CONTRACT 10.13.F)', () => {
  for (const name of ['FX_BY_TIER', 'fxFor', 'usesGifs']) assert.equal(name in feel, false, name);
});

test('recipe: celebrate small on purpose, Snooze sleepy with a shiver, the reveal only for the pot', () => {
  const snooze = recipe(R({ snoozed: true }));
  assert.equal(snooze.sound, 'snooze'); assert.ok(snooze.sleepy && snooze.shiver && !snooze.tokens);
  const small = recipe(R({ pay: 2 }));
  assert.equal(small.sound, 'chime'); assert.ok(!small.jolt && !small.reveal && !small.gold && small.tokens);
  assert.equal(recipe(R({ pay: 8 })).sound, 'two');
  assert.equal(recipe(R({ pay: 40 })).sound, 'thud');
  const pot = recipe(R({ pay: 550, jackpot: true }));
  assert.ok(pot.reveal && pot.gold && pot.sparks && pot.sound === 'reveal');
  for (let tier = 0; tier < 4; tier++) assert.ok(FEEL.PARTY_MS[tier] <= 1000, 'no party over a second but the declared hero');
});

test('Law VI: reduced motion and Calm take the state (no shiver, no reveal travel), the cue stays', () => {
  const s = recipe(R({ snoozed: true }), { still: true });
  assert.ok(!s.shiver && s.sleepy && s.sound === 'snooze');
  const p = recipe(R({ pay: 550, jackpot: true }), { still: true });
  assert.ok(!p.reveal && !p.sparks && p.gold && p.sound === 'reveal');
});

test('THE BANK token counts and ticks', () => {
  assert.deepEqual([1, 2, 3, 4].map(t => winTokens(t, false)), [3, 4, 5, 7]);
  assert.deepEqual([1, 2, 3, 4].map(t => winTokens(t, true)), [3, 4, 4, 4], 'Calm (lite) flies 4 at most, the pot too');
  assert.deepEqual(tickValues(57, 62, 4), [58, 60, 61, 62]);
  assert.equal(tickValues(57, 607, 7).at(-1), 607);
});

test('CHIME LADDER: never more than 6 played ticks a second, climbs only as the wheel slows, capped at 7', () => {
  let ladder = null, played = [];
  for (let t = 0; t < 3000; t += 30) {   // fast crossings every 30 ms
    const r = tick(ladder, t, 30); ladder = r.ladder;
    if (r.play) { played.push(t); assert.equal(r.semis, 0, 'no climb while fast'); }
  }
  for (let i = 1; i < played.length; i++) assert.ok(played[i] - played[i - 1] >= FEEL.STROBE_MIN_MS);
  assert.ok(played.length <= 3000 / FEEL.STROBE_MIN_MS + 1);
  const steps = [];
  for (let i = 0, t = 10000; i < 12; i++, t += 400) { const r = tick(ladder, t, 400); ladder = r.ladder; steps.push(r.semis); }
  assert.deepEqual(steps, [1, 2, 3, 4, 5, 6, 7, 7, 7, 7, 7, 7]);
});

test('THE MASCOT GLANCE never repeats a pose, and lands on the right face', () => {
  for (const p of POSES) assert.notEqual(glance(p, p), p);
  assert.equal(landPose(R({ snoozed: true })), 'melt');
  assert.equal(landPose(R({ pay: 5 })), 'hearts');
  assert.equal(landPose(R({ pay: 550, jackpot: true })), 'jackpot');
});

test('THE SHIVER and THE BREATH stay in their bounds', () => {
  for (let ms = 0; ms < 300; ms += 5) assert.ok(Math.abs(shiverPx(ms)) <= FEEL.SHIVER_PX);
  assert.equal(shiverPx(FEEL.SHIVER_MS), 0);
  assert.equal(breath(0), 0);
  assert.ok(Math.abs(breath(FEEL.BREATH_MS / 2) - 1) < 1e-9);
  assert.ok(FEEL.BREATH_MS >= 2600 && FEEL.BREATH_MS <= 4000);
});

test('THE REVEAL count-up never reads above the pay, though its ease overshoots', () => {
  assert.ok(Math.round(550 * bezier(FEEL.REVEAL_EASE, 0.6)) > 550, 'the motion ease does overshoot (the old +551)');
  for (const pay of [1, 12, 550, 5321, 99999]) {
    let prev = 0;
    for (let i = 0; i <= 200; i++) {
      const n = revealCount(pay, i / 200);
      assert.ok(n >= 0 && n <= pay, `pay ${pay} at q ${i / 200}: ${n}`);
      assert.ok(n >= prev, `pay ${pay}: the count never steps back (${prev} -> ${n})`);
      prev = n;
    }
    assert.equal(revealCount(pay, 1), pay, 'it ends on the pay');
    assert.equal(revealCount(pay, 0), 0);
  }
  assert.equal(revealCount(550, 2), 550, 'q past the end is the pay');
  assert.equal(revealCount(-5, 0.5), 0);
});

/* ---- THE DESKTOP RECIPE (feel.js FX_MOMENTS): every moment to its host ids, the gates, Calm, the cooldowns. ---- */

const { FX_MOMENTS, FX_GATE, FX_REPEAT_CAP, landMoment, nearMiss, fxPlan, fxAllowed, pickKeys, hash32, freshCool } = feel;
const HOST_IDS = ['fx.gif_burst', 'fx.gif_from', 'fx.gif_storm', 'fx.haze', 'fx.jackpot', 'fx.loom_spiral', 'fx.melt', 'fx.spiral_brief',
  'fx.spiral_full', 'fx.sub_cascade', 'fx.sub_pair', 'fx.sub_single', 'fx.wash'];
const GIFS = ['g0', 'g1', 'g2', 'g3'], WORDS = ['s0', 's1', 's2', 's3'];
const ids = plan => plan.fx.map(f => f.id);
const play = (moment, o = {}) => fxPlan(moment, { gifs: GIFS, words: WORDS, seed: '2026-09-15|x|1', ...o });

test('recipe table: every row names only ids the host renders, each with a gate, and every gate the host reports', () => {
  for (const [moment, row] of Object.entries(FX_MOMENTS)) {
    for (const step of [...row.fx, ...(row.calm || [])]) {
      assert.ok(HOST_IDS.includes(step.id), `${moment}: ${step.id} is a host id`);
      assert.ok(Array.isArray(FX_GATE[step.id]) && FX_GATE[step.id].length, `${moment}: ${step.id} has a gate`);
    }
  }
  const gates = new Set(Object.values(FX_GATE).flat());
  assert.deepEqual([...gates].sort(), ['brainDrain', 'flash', 'spiral', 'subliminal']);
  assert.equal(fxAllowed('fx.nope', null), false, 'an id with no gate never fires');
});

test('the moment table: grab, coast, near miss, and every landing by tier and reward', () => {
  const table = {
    grab: ['fx.sub_single'], coast: ['fx.wash'], nearMiss: ['fx.spiral_brief'], snooze: [],
    small: ['fx.sub_single'], mid: ['fx.gif_burst', 'fx.sub_single'], big: ['fx.gif_storm', 'fx.sub_pair'], jackpot: ['fx.jackpot'],
    double: ['fx.sub_pair'], gift: ['fx.gif_burst', 'fx.wash'], empty: ['fx.melt'],
  };
  assert.deepEqual(Object.keys(FX_MOMENTS).sort(), Object.keys(table).sort());
  for (const [moment, want] of Object.entries(table)) assert.deepEqual(ids(play(moment)), want, moment);
  assert.deepEqual(play('nope'), { fx: [], cool: freshCool(), why: 'unknown' });
});

test('landMoment: the reward kind first, then the pay tier; Snooze and no pay stay quiet', () => {
  assert.equal(landMoment(null), 'snooze');
  assert.equal(landMoment(R({ snoozed: true })), 'snooze');
  assert.equal(landMoment(R({ pay: 0 })), 'snooze');
  assert.deepEqual([1, 2, 3].map(pay => landMoment(R({ pay }))), ['small', 'small', 'small']);
  assert.deepEqual([5, 8, 12, 15, 20, 30].map(pay => landMoment(R({ pay }))), ['mid', 'mid', 'mid', 'mid', 'mid', 'mid']);
  assert.deepEqual([40, 60, 100, 150].map(pay => landMoment(R({ pay }))), ['big', 'big', 'big', 'big']);
  assert.equal(landMoment(R({ pay: 100, jackpotFallback: true })), 'big', 'a gated pot hit pays Dazed: big, not the pot');
  assert.equal(landMoment(R({ pay: 550, jackpot: true })), 'jackpot');
  assert.equal(landMoment(R({ pay: 0, reward: { kind: 'double' } })), 'double', 'Seeing Double');
  assert.equal(landMoment(R({ pay: 0, reward: { kind: 'decoration', decorationId: 'ivy' } })), 'gift', 'Room Service');
  assert.equal(landMoment(R({ pay: 75, reward: { kind: 'decoration', fallback: true } })), 'big', 'a complete collection pays 75 SP: a big win');
  assert.equal(landMoment(R({ pay: 0, reward: { kind: 'nothing' } })), 'empty', 'Head Empty');
  assert.equal(landMoment(R({ pay: 15, reward: { kind: 'sp' } })), 'mid');
});

test('Law I: grab and coast are the same whatever the result, and fire no landing id', () => {
  const g = play('grab'), c = play('coast');
  assert.deepEqual(ids(g), ['fx.sub_single']);
  assert.deepEqual(c.fx, [{ id: 'fx.wash', symbols: undefined, args: { color: '#9b6bff', strength: 0.35 } }], 'a fixed plum wash, no picture');
  for (const f of [...g.fx, ...c.fx]) assert.ok(!['fx.jackpot', 'fx.gif_storm', 'fx.gif_from', 'fx.loom_spiral', 'fx.melt'].includes(f.id));
});

test('gated variants drop: each toggle strips its ids, a two-gate id needs only one of them, everything off is silent', () => {
  assert.deepEqual(ids(play('mid', { gates: { flash: false } })), ['fx.sub_single']);
  assert.deepEqual(ids(play('mid', { gates: { subliminal: false } })), ['fx.gif_burst']);
  assert.deepEqual(ids(play('big', { gates: { subliminal: false } })), ['fx.gif_storm', 'fx.sub_pair'], 'sub_pair still carries its spiral');
  assert.deepEqual(ids(play('big', { gates: { subliminal: false, spiral: false } })), ['fx.gif_storm']);
  assert.deepEqual(ids(play('empty', { gates: { brainDrain: false } })), []);
  assert.deepEqual(ids(play('nearMiss', { gates: { spiral: false } })), []);
  assert.deepEqual(ids(play('jackpot', { gates: { spiral: false, flash: false } })), ['fx.jackpot'], 'the pot still has its words');
  const off = { flash: false, subliminal: false, spiral: false, brainDrain: false, tunnel: false };
  for (const moment of Object.keys(FX_MOMENTS)) {
    const p = play(moment, { gates: off });
    assert.deepEqual(p.fx, [], `${moment} off`);
    assert.equal(p.why, FX_MOMENTS[moment].fx.length ? 'gated' : null);
  }
  assert.deepEqual(ids(play('big', { gates: null })), ['fx.gif_storm', 'fx.sub_pair'], 'no gates reported reads as all on');
  assert.deepEqual(ids(play('big', { gates: { melt: false } })), ['fx.gif_storm', 'fx.sub_pair'], 'a gate we do not read changes nothing');
});

test('Calm strips motion: no coast wash, no near-miss spiral, no melt, the storm becomes a burst; the cues stay', () => {
  const still = { still: true };
  assert.deepEqual(play('coast', still), { fx: [], cool: { at: { coast: 0 }, seen: { coast: 0 } }, why: 'still' });
  assert.equal(play('nearMiss', still).why, 'still');
  assert.equal(play('empty', still).why, 'still');
  assert.deepEqual(ids(play('big', still)), ['fx.gif_burst', 'fx.sub_pair']);
  assert.deepEqual(ids(play('grab', still)), ['fx.sub_single']);
  assert.deepEqual(ids(play('small', still)), ['fx.sub_single']);
  assert.deepEqual(ids(play('mid', still)), ['fx.gif_burst', 'fx.sub_single']);
  assert.deepEqual(ids(play('jackpot', still)), ['fx.jackpot'], 'the host plays its own Calm recipe for the pot');
  assert.deepEqual(ids(play('double', still)), ['fx.sub_pair']);
  assert.deepEqual(ids(play('gift', still)), ['fx.gif_burst', 'fx.wash']);
  for (const moment of Object.keys(FX_MOMENTS)) {
    for (const f of play(moment, still).fx) assert.ok(!['fx.gif_storm', 'fx.spiral_brief', 'fx.melt'].includes(f.id), `${moment} still: ${f.id}`);
    assert.deepEqual(play(moment, still).fx.map(f => f.args), play(moment, still).fx.map(f => f.args), 'Normal args either way (nobody halves twice)');
  }
  assert.equal(play('coast', still).fx.length + play('coast').fx.length, 1);
  assert.equal(play('gift', still).fx[1].args.strength, 0.6, 'the page never halves an arg');
});

test('cooldowns: a rim grabbed twice inside 6 s fires once, a coast inside 2 s once; the landing rows never wait', () => {
  let cool = freshCool();
  const a = play('grab', { cool, now: 1000 }); cool = a.cool;
  const b = play('grab', { cool, now: 4000 }); cool = b.cool;
  const c = play('grab', { cool, now: 7001 });
  assert.deepEqual([ids(a), ids(b), ids(c)], [['fx.sub_single'], [], ['fx.sub_single']]);
  assert.equal(b.why, 'cool');
  assert.equal(b.cool, cool, 'a refused play leaves the ledger as it was');
  let k = freshCool();
  const c1 = play('coast', { cool: k, now: 100 }); k = c1.cool;
  const c2 = play('coast', { cool: k, now: 1500 });
  assert.deepEqual([ids(c1), ids(c2)], [['fx.wash'], []]);
  let l = freshCool();
  for (let i = 0; i < 2; i++) { const p = play('mid', { cool: l, now: i }); l = p.cool; assert.equal(p.fx.length, 2, 'two landings a millisecond apart both play'); }
  assert.equal(FX_MOMENTS.grab.coolMs, 6000); assert.equal(FX_MOMENTS.coast.coolMs, 2000);
  for (const m of ['small', 'mid', 'big', 'jackpot', 'double', 'gift', 'empty', 'nearMiss']) assert.ok(!(FX_MOMENTS[m].coolMs > 0), m);
});

test('Brake 3: a landing extra shrinks to nothing after FX_REPEAT_CAP plays in one sit-down; the pot and the coast never cap', () => {
  let cool = freshCool();
  const got = [];
  for (let i = 0; i < FX_REPEAT_CAP + 2; i++) { const p = play('mid', { cool, now: i * 10000 }); cool = p.cool; got.push([p.fx.length, p.why]); }
  assert.deepEqual(got, [[2, null], [2, null], [2, null], [0, 'capped'], [0, 'capped']]);
  assert.equal(cool.seen.mid, FX_REPEAT_CAP, 'a capped play does not count');
  let j = freshCool();
  for (let i = 0; i < 6; i++) { const p = play('jackpot', { cool: j, now: i * 10000 }); j = p.cool; assert.deepEqual(ids(p), ['fx.jackpot']); }
  let c = freshCool();
  for (let i = 0; i < 6; i++) { const p = play('coast', { cool: c, now: i * 10000 }); c = p.cool; assert.deepEqual(ids(p), ['fx.wash']); }
  assert.deepEqual(freshCool(), { at: {}, seen: {} }, 'a new sit-down starts fresh');
});

test('symbols: dealt keys only, stable per result, distinct within a step, none when the deal is empty', () => {
  const p = play('big');
  assert.deepEqual(p.fx[0].symbols.length, 3); assert.ok(p.fx[0].symbols.every(k => GIFS.includes(k)));
  assert.equal(new Set(p.fx[0].symbols).size, 3);
  assert.deepEqual(p.fx[1].symbols.length, 2); assert.ok(p.fx[1].symbols.every(k => WORDS.includes(k)));
  assert.deepEqual(play('big').fx, p.fx, 'the same seed picks the same keys');
  const pot = play('jackpot').fx[0].symbols;
  assert.equal(pot.length, 6); assert.ok(pot.slice(0, 3).every(k => /^g\d$/.test(k)) && pot.slice(3).every(k => /^s\d$/.test(k)));
  const bare = play('big', { gifs: [], words: [] });
  assert.deepEqual(bare.fx.map(f => f.symbols), [undefined, undefined], 'no deal: the host picks from its own deal');
  assert.deepEqual(play('mid', { gifs: ['g0'], words: ['s0', null, 3] }).fx.map(f => f.symbols), [['g0'], ['s0']], 'a short deal gives fewer, never a made-up key');
  assert.deepEqual(pickKeys(['a', 'b', 'c'], 5, 'z').length, 3);
  assert.equal(hash32('Drop'), hash32('Drop')); assert.notEqual(hash32('Drop'), hash32('Relax'));
});

test('THE ALMOST on the wheel: one slice off the pot on either side of the ring, never the pot, never a wound-down wheel', () => {
  const layout = ['jackpot', 'sip_a', 'glow', 'sparkle_a', 'dreamy', 'sip_b', 'shimmer', 'snooze', 'twinkle', 'deep', 'sparkle_b', 'dazzle', 'sip_c', 'dazed'].map(id => ({ id }));
  const r = R({ pay: 1 });
  assert.equal(nearMiss(layout, 1, r), true, 'sip_a, clockwise of the star');
  assert.equal(nearMiss(layout, 13, R({ pay: 100, jackpotFallback: true })), true, 'Dazed, the other side (the star slipped past)');
  assert.equal(nearMiss(layout, 2, r), false);
  assert.equal(nearMiss(layout, 7, R({ snoozed: true })), false);
  assert.equal(nearMiss(layout, 0, R({ pay: 550, jackpot: true })), false, 'the pot itself is no near miss');
  assert.equal(nearMiss(layout, -1, r), false, 'no slice to point at');
  assert.equal(nearMiss(null, 1, r), false);
  assert.equal(nearMiss(layout, 1, null), false);
  const rolled = [...layout.slice(3), ...layout.slice(0, 3)];   // the pot at index 11: its neighbours wrap
  assert.equal(nearMiss(rolled, 10, r), true); assert.equal(nearMiss(rolled, 12, r), true); assert.equal(nearMiss(rolled, 0, r), false);
  const potLast = [...layout.slice(1), layout[0]];
  assert.equal(nearMiss(potLast, 0, r), true, 'the ring wraps: index 0 neighbours the last slice');
});
