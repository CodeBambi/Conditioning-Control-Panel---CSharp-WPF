// node --test backroom/stations/roulette/tests/ : the Lighthouse clock, the planned run, the landing moments
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  FEEL, SEG, POCKETS, landMoment, beamAngle, beamLit, litNumbers, pocketAngle, whirlAngle, planRun, sampleRun, seedFor, nextLaunchAt, wrapAngle, angDist, restRel,
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
  assert.equal(whirlAngle(1), FEEL.WHIRL_MUL);
  const p = planRun({ index: 0, seed: 5 });
  assert.ok(p.rot[p.rot.length - 1] > p.rot[0], 'the rotor turns clockwise on screen (its angle grows)');
  assert.ok(sampleRun(p, 1).rel < sampleRun(p, 0.5).rel, 'the ball runs against it');
  assert.ok(Math.abs(pocketAngle(0, 0) - SEG / 2) < 1e-12);
});
