// node --test backroom/stations/roulette/tests/ : the Lighthouse clock, the planned run, the landing moments, the host recipe
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  FEEL, SEG, POCKETS, landMoment, beamAngle, beamLit, litNumbers, pocketAngle, whirlAngle, planRun, sampleRun, seedFor, nextLaunchAt, wrapAngle, angDist, restRel,
  FX, FX_GATE, FX_RECIPE, FX_BEATS, fxPlan, fxSymbols, landBeat, nearMisses, createFxCooldowns, CALLOUTS, calloutFor,
} from '../feel.js';
import { createMockServer } from '../mock-server.js';

const { WHEEL } = createMockServer();

test('landing moments: miss, win, big on a wake win or a straight hit', () => {
  assert.equal(landMoment({ pay: 0, wake: true, straight: false }), 'roulette.land.miss');
  assert.equal(landMoment({ pay: 2, wake: false, straight: false }), 'roulette.land.win');
  assert.equal(landMoment({ pay: 4, wake: true, straight: false }), 'roulette.land.big');
  assert.equal(landMoment({ pay: 36, wake: false, straight: true }), 'roulette.land.big');
});

test('law 4: the beam runs on its own clock, -0.7 rad/s, half-width 0.24', () => {
  assert.equal(beamAngle(0) + 0, 0);
  assert.ok(Math.abs(beamAngle(10) + 7) < 1e-9);
  assert.equal(beamLit(1, 1), 1); assert.equal(beamLit(1 + 0.24, 1), 0); assert.ok(beamLit(1.12, 1) > 0.49 && beamLit(1.12, 1) < 0.51);
  assert.ok(Math.abs(angDist(0.1, Math.PI * 2 - 0.1) - 0.2) < 1e-9, 'across zero');
});

test('law 4: over 10 s against a rotor at idle the beam lights at least 30 distinct numbers', () => {
  const lit = new Set();
  for (let t = 0; t <= 10; t += 0.05) for (const n of litNumbers(WHEEL, FEEL.ROTOR_IDLE * t, beamAngle(t))) lit.add(n);
  assert.ok(lit.size >= 30, `${lit.size} numbers lit`);
  // the rotor-locked bug the mockup's first cut had: a beam at the rotor angle lights the same two numbers forever
  const locked = new Set();
  for (let t = 0; t <= 10; t += 0.05) for (const n of litNumbers(WHEEL, FEEL.ROTOR_IDLE * t, FEEL.ROTOR_IDLE * t)) locked.add(n);
  assert.ok(locked.size <= 3 && lit.size > locked.size * 5);
});

test('planned backwards: every pocket lands where the server said, clips at frets, sparks spaced', () => {
  let clips = [0, 0, 0, 0], worst = 0;
  for (let s = 0; s < 148; s++) {
    for (const calm of [false, true]) {
      const index = s % POCKETS, plan = planRun({ index, seed: seedFor('r_test_' + s, s), calm });
      const end = sampleRun(plan, 60);
      assert.equal(end.phase, 'rest');
      assert.equal(Math.floor(wrapAngle(end.rel) / SEG), index, `seed ${s} lands on index ${index}`);
      assert.ok(angDist(end.rel, restRel(index)) < 1e-3, 'at the pocket centre');
      const land = sampleRun(plan, plan.landAt + FEEL.DT);
      assert.equal(Math.floor(wrapAngle(land.rel) / SEG), index, 'already in the pocket on the landing frame');
      assert.ok(plan.hits >= 0 && plan.hits <= FEEL.MAX_CLIPS);
      clips[plan.hits]++;
      for (let k = 1; k < plan.sparks.length; k++) assert.ok(plan.sparks[k].at - plan.sparks[k - 1].at >= FEEL.SPARK_GAP_S - 1e-9, 'law 5: sparks 340 ms apart');
      for (const sp of plan.sparks) assert.ok(Math.abs(sp.a / SEG - Math.round(sp.a / SEG)) < 1e-6, 'a spark sits on a fret');
      worst = Math.max(worst, plan.restAt);
    }
  }
  assert.ok(clips[1] + clips[2] + clips[3] === 296, `every drop clips a fret: ${clips}`);
  assert.ok(worst <= FEEL.SPIN_MS / 1000 - 1.2, `the slowest run rests at ${worst.toFixed(2)} s, inside the 8 s spin`);
});

test('the rattle is slow motion: 0.42, raised to 0.7 for Calm', () => {
  const minScale = (calm) => { const p = planRun({ index: 3, seed: 99, calm }); return Math.min(...p.tscale); };
  assert.ok(Math.abs(minScale(false) - FEEL.SLOW) < 0.03, 'Normal floor ' + minScale(false));
  assert.ok(Math.abs(minScale(true) - FEEL.SLOW_CALM) < 0.03, 'Calm floor ' + minScale(true));
  const p = planRun({ index: 3, seed: 99 });
  const phases = new Set(); for (let t = 0; t < p.restAt; t += 0.05) phases.add(sampleRun(p, t).phase);
  assert.deepEqual([...phases], ['run', 'drop', 'rattle', 'settle']);
  assert.ok(sampleRun(p, 0.1).speed > 7 && sampleRun(p, p.landAt - 0.05).speed < 3.5, 'the ball slows down');
});

test('the same spin plans the same run (a reopen replays the choreography)', () => {
  const a = planRun({ index: 11, seed: seedFor('r_1_abc', 2) }), b = planRun({ index: 11, seed: seedFor('r_1_abc', 2) });
  assert.deepEqual(Array.from(a.rel.slice(0, 50)), Array.from(b.rel.slice(0, 50)));
  assert.notEqual(seedFor('r_1_abc', 2), seedFor('r_1_abc', 3));
});

test('pace, the whirl angle and the rotor handedness', () => {
  assert.equal(nextLaunchAt(1000, 6000), 9000);
  assert.equal(nextLaunchAt(1000, 8500), 10000);
  assert.equal(FEEL.WIN_HOLD_MS, 2000, 'a paying landing: FX_DELAY_MS + CALLOUT_MS (callout.js)');
  assert.equal(nextLaunchAt(1000, 8500, { landMs: 8000, win: false }), 10000, 'a miss stays quick');
  assert.equal(nextLaunchAt(1000, 8500, { landMs: 8000, win: true }), 10000, 'a win: 8000 + 2000 is inside the pace already');
  assert.equal(nextLaunchAt(1000, 8800, { landMs: 8700, win: true }), 10700, 'a win landing late holds WIN_HOLD_MS from the landing frame');
  assert.equal(nextLaunchAt(1000, 8800, { landMs: null, win: true }), 10300, 'no landing time: the old pace');
  assert.equal(whirlAngle(1), FEEL.WHIRL_MUL);
  const p = planRun({ index: 0, seed: 5 });
  assert.ok(p.rot[p.rot.length - 1] > p.rot[0], 'the rotor turns clockwise on screen (its angle grows)');
  assert.ok(sampleRun(p, 1).rel < sampleRun(p, 0.5).rel, 'the ball runs against it');
  assert.ok(Math.abs(pocketAngle(0, 0) - SEG / 2) < 1e-12);
});

/* ---- the host recipe (feel.FX_RECIPE): every beat, its ids, the gates, Calm, the streak, the cooldowns ---- */

const ALL_ON = { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true };
const ids = (beat, o) => fxPlan(beat, { gates: ALL_ON, ...o }).map((s) => s.fx);
const HOST_IDS = ['fx.gif_burst', 'fx.gif_from', 'fx.gif_storm', 'fx.haze', 'fx.jackpot', 'fx.loom_spiral', 'fx.melt', 'fx.spiral_brief', 'fx.spiral_full', 'fx.sub_cascade', 'fx.sub_pair', 'fx.sub_single', 'fx.wash'];

test('the recipe table: every beat of a spin maps to host ids the bridge handles, frozen', () => {
  assert.deepEqual(FX_BEATS, ['nomore', 'launch', 'wake', 'rattle', 'run', 'near', 'glyph', 'land.miss', 'land.win', 'land.straight', 'land.wake', 'land.full', 'streak', 'skip']);
  assert.ok(Object.isFrozen(FX_RECIPE) && Object.isFrozen(FX_RECIPE['land.full'][0]) && Object.isFrozen(FX.COOLDOWN_MS));
  for (const beat of FX_BEATS) for (const s of FX_RECIPE[beat]) assert.ok(HOST_IDS.includes(s.fx) && FX_GATE[s.fx], `${beat}: ${s.fx} is a host id with a gate`);
  const table = {
    nomore: ['fx.sub_single'], launch: ['fx.gif_burst'], wake: ['fx.sub_single'], rattle: ['fx.sub_single'],
    run: [], near: ['fx.spiral_brief'], glyph: [], 'land.miss': [], 'land.win': [], 'land.straight': [],
    'land.wake': ['fx.spiral_full'], 'land.full': ['fx.sub_cascade'], streak: ['fx.gif_storm'], skip: [],
  };
  for (const [beat, want] of Object.entries(table)) assert.deepEqual(ids(beat), want, beat + ' at Normal');
  assert.deepEqual(ids('land.full', { full: true }), ['fx.jackpot'], 'Full: the straight-up hit on a wake is the jackpot hero');
  assert.deepEqual(ids('land.straight', { full: true }), [], 'Full changes only land.full');
  assert.deepEqual(fxPlan('not.a.beat'), [], 'an unknown beat fires nothing');
  assert.deepEqual(fxPlan('skip', { gates: ALL_ON, full: true, streak: 9 }), [], 'Law VI: Back and suspend fire nothing');
});

test('Law I: the beats before the landing are the same for every spin', () => {
  for (const beat of ['nomore', 'launch', 'rattle']) {
    const a = JSON.stringify(fxPlan(beat, { gates: ALL_ON, streak: 0 })), b = JSON.stringify(fxPlan(beat, { gates: ALL_ON, streak: 5 }));
    assert.equal(a, b, beat + ' does not read the outcome');
  }
  assert.deepEqual(ids('launch', { streak: 3 }), ['fx.gif_burst'], 'the streak rides a landing only');
});

test('gates never drop a step (2026-09-15): every toggle off plans the same beats as every toggle on', () => {
  const off = (k) => ({ ...ALL_ON, [k]: false });
  assert.deepEqual(ids('launch', { gates: off('flash') }), ['fx.gif_burst']);
  assert.deepEqual(ids('nomore', { gates: off('subliminal') }), ['fx.sub_single']);
  assert.deepEqual(ids('near', { gates: off('spiral') }), ['fx.spiral_brief']);
  assert.deepEqual(ids('land.win', { gates: off('flash'), streak: 2 }), ['fx.gif_storm'], 'the flash gate no longer drops the storm');
  assert.deepEqual(ids('land.full', { gates: { flash: false, spiral: false, subliminal: false }, full: true }), ['fx.jackpot'], 'all three off: the hero still posts');
  const none = { flash: false, subliminal: false, spiral: false, brainDrain: false, tunnel: false };
  for (const beat of FX_BEATS) assert.deepEqual(fxPlan(beat, { gates: none, full: true, streak: 3 }), fxPlan(beat, { gates: ALL_ON, full: true, streak: 3 }), beat + ': gates off changes nothing');
  assert.deepEqual(ids('launch', { gates: null }), ['fx.gif_burst'], 'a host that sends no gates reads the same');
  assert.deepEqual(ids('nomore', { gates: {} }), ['fx.sub_single']);
});

test('the callout for a landing beat: one name a spin, the streak only as the fallback, none on a miss (callout.js)', () => {
  assert.deepEqual(Object.keys(CALLOUTS).sort(), ['land.full', 'land.straight', 'land.wake', 'land.win', 'streak']);
  for (const c of Object.values(CALLOUTS)) assert.ok(/^br_callout_[a-z_]+$/.test(c.key) && c.fallback && ['small', 'big', 'hero'].includes(c.tier));
  assert.deepEqual(calloutFor('land.win'), { key: 'br_callout_chips_in', fallback: 'Chips In', tier: 'small' });
  assert.equal(calloutFor('land.straight').tier, 'big');
  assert.equal(calloutFor('land.wake').key, 'br_callout_spiral_wake');
  assert.deepEqual(calloutFor('land.full', { streak: 5 }), { key: 'br_callout_full_wake', fallback: 'Full Wake', tier: 'hero' }, 'the streak never replaces the beat name');
  assert.equal(calloutFor('land.win', { streak: 5 }).key, 'br_callout_chips_in', 'Brake 2: one callout a spin');
  assert.equal(calloutFor('land.miss', { streak: 5 }), null, 'a miss names nothing, streak or not');
  assert.equal(calloutFor('near', { streak: 5 }), null, 'a near miss gets its spiral and no name');
  assert.equal(calloutFor('streak'), null, 'the streak key is never a beat of its own');
  for (const beat of ['nomore', 'launch', 'wake', 'rattle', 'run', 'skip']) assert.equal(calloutFor(beat, { streak: 9 }), null, beat);
});

test('Calm strips motion (the burst, the storm, the hero) and keeps the words and spirals for the host to halve', () => {
  assert.deepEqual(ids('launch', { calm: true }), []);
  assert.deepEqual(ids('land.win', { calm: true, streak: 4 }), [], 'the glyph carries the effect of a win; Calm strips the storm');
  assert.deepEqual(ids('land.full', { calm: true, full: true }), ['fx.sub_cascade'], 'Calm beats Full: no hero, the cascade instead');
  assert.deepEqual(ids('near', { calm: true }), ['fx.spiral_brief']);
  assert.deepEqual(ids('land.wake', { calm: true }), ['fx.spiral_full']);
  assert.deepEqual(ids('nomore', { calm: true }), ['fx.sub_single']);
});

test('the streak: STREAK_FROM paying spins in a row bring the storm, a miss resets it, the hero keeps its frame', () => {
  assert.equal(FX.STREAK_FROM, 2);
  assert.deepEqual(ids('land.win', { streak: 1 }), [], 'the glyph beat carries the effect (glyphs.js)');
  assert.deepEqual(ids('land.win', { streak: 2 }), ['fx.gif_storm']);
  assert.deepEqual(ids('land.straight', { streak: 3 }), ['fx.gif_storm']);
  assert.deepEqual(ids('land.miss', { streak: 5 }), [], 'a miss never storms');
  assert.deepEqual(ids('near', { streak: 5 }), ['fx.spiral_brief']);
  assert.deepEqual(ids('land.full', { full: true, streak: 2 }), ['fx.jackpot'], 'Brake 2: one hero per beat');
  assert.deepEqual(ids('land.full', { streak: 2 }), ['fx.sub_cascade', 'fx.gif_storm']);
});

test('the landing beat by outcome, and a near miss from the wheel order (never the mat)', () => {
  assert.equal(landBeat({ pay: 0 }), 'land.miss');
  assert.equal(landBeat({ pay: 0 }, [32]), 'near');
  assert.equal(landBeat({ pay: 2, wake: true, straight: false }, [32]), 'land.wake', 'a paying spin is never a near miss');
  assert.equal(landBeat({ pay: 2 }), 'land.win');
  assert.equal(landBeat({ pay: 36, straight: true }), 'land.straight');
  assert.equal(landBeat({ pay: 72, straight: true, wake: true }), 'land.full');
  assert.equal(landBeat(null), 'land.miss');
  const bets = [{ spot: 's32', amt: 1 }, { spot: 'rose', amt: 1 }, { spot: 's26', amt: 1 }];
  assert.deepEqual(nearMisses({ index: 0 }, bets, WHEEL), [26, 32], '0 sits between 26 and 32 on the wheel');
  assert.deepEqual(nearMisses({ index: 1 }, bets, WHEEL), [], '32 itself is not a near miss of 32 (15 and 0 are uncovered)');
  assert.deepEqual(nearMisses({ index: 36 }, [{ spot: 's0' }], WHEEL), [0], 'the ring closes: 26 is next to 0');
  assert.deepEqual(nearMisses({ index: 0 }, [{ spot: 's17' }], WHEEL), [], '17 is far away on the wheel although next to 0 on nothing');
  assert.deepEqual(nearMisses({ index: -1 }, bets, WHEEL), []);
  assert.deepEqual(nearMisses({ index: 0 }, null, WHEEL), []);
});

test('symbols: the spin picture rides the gif steps, word keys turn with the spin index', () => {
  assert.deepEqual(fxSymbols({ fx: 'fx.gif_burst', gif: true }, { gif: 'g2', spin: 0 }), ['g2']);
  assert.deepEqual(fxSymbols({ fx: 'fx.gif_burst', gif: true }, { gif: null }), [], 'no deck yet: the host picks from its own deal');
  assert.deepEqual(fxSymbols({ fx: 'fx.gif_burst', gif: true }, { gif: '../x' }), [], 'only a dealt key ever goes back');
  assert.deepEqual(fxSymbols({ fx: 'fx.sub_pair', words: 2 }, { spin: 3 }), ['s3', 's0']);
  assert.deepEqual(fxSymbols({ fx: 'fx.sub_single', words: 1 }, { spin: 5 }), ['s1']);
  assert.deepEqual(fxSymbols({ fx: 'fx.sub_cascade', gif: true }, { gif: 'g0', spin: 1 }), ['g0']);
});

test('the cooldowns: one clock per id, the rattle once a spin, Back resets', () => {
  const c = createFxCooldowns();
  const single = { fx: 'fx.sub_single', once: null }, rattle = { fx: 'fx.sub_single', once: 'spin' };
  assert.equal(c.take(single, 0), true, 'nomore at the press');
  assert.equal(c.take(single, 100), false, 'the wake at the launch, 100 ms on: cooled');
  assert.equal(c.take(rattle, 4500, 0), true, 'the first clip of spin 0, 4.5 s on');
  assert.equal(c.take(rattle, 5000, 0), false, 'the second clip of the same spin: once a spin');
  assert.equal(c.take(rattle, 12500, 1), true, 'spin 1 gets its own');
  assert.equal(c.take('fx.gif_storm', 0), true);
  assert.equal(c.take('fx.gif_storm', FX.COOLDOWN_MS['fx.gif_storm'] - 1), false);
  assert.equal(c.take('fx.gif_storm', FX.COOLDOWN_MS['fx.gif_storm']), true);
  assert.equal(c.take({ fx: 'fx.spiral_brief' }, 0), true);
  assert.equal(c.take({ fx: 'fx.spiral_full' }, 0), true, 'each id has its own clock');
  c.reset();
  assert.equal(c.take(single, 200), true, 'Law VI: Back drops the ledger');
  assert.equal(c.take(rattle, 300, 1), false, 'the rattle word is cooled by the press word...');
  assert.equal(c.take(rattle, 4300, 1), true, '...and still owed to the spin once the clock allows');
  for (const fx of Object.keys(FX_GATE)) assert.ok(FX.COOLDOWN_MS[fx] >= 4000, fx + ' has a cooldown of 4 s or more');
});
