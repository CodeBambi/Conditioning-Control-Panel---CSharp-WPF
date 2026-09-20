import test from 'node:test';
import assert from 'node:assert/strict';
import { LADDER, ladderSteps, ladderSemis, ladderPlan } from '../ladder.js';
import { FEEL, ladderSemis as slotSemis, ladderPlan as slotPlan } from '../../../stations/slot/feel.js';

test('THE CHIME LADDER keeps the House Book numbers', () => {
  assert.equal(LADDER.CAP, 7);
  assert.equal(LADDER.OCTAVE, -12);
  assert.equal(LADDER.STROBE_MIN_MS, 1000 / 6);
  assert.deepEqual([...LADDER.STEPS], [0, 1, 3, 5, 7]);
  assert.ok(Object.isFrozen(LADDER) && Object.isFrozen(LADDER.STEPS));
});

test('it is the SLOT ladder, lifted: same semitones, same plan, nothing drifted', () => {
  assert.equal(LADDER.CAP, FEEL.LADDER_CAP);
  assert.equal(LADDER.STROBE_MIN_MS, FEEL.STROBE_MIN_MS);
  for (const step of [-3, 0, 1, 4, 7, 9, 99]) {
    for (const melted of [false, true]) assert.equal(ladderSemis(step, melted), slotSemis(step, melted), `${step}/${melted}`);
  }
  for (const tier of [0, 1, 2, 3, 4]) {
    for (const ms of [0, 120, 500, 1200, 2000, 6000]) {
      for (const melted of [false, true]) {
        assert.deepEqual(ladderPlan(tier, ms, melted), slotPlan(tier, ms, melted), `tier ${tier} over ${ms} melted ${melted}`);
      }
    }
  }
});

test('Law IX: a small win gets the landing note and nothing after it', () => {
  assert.deepEqual([0, 1, 2, 3, 4].map(t => ladderSteps(t)), [0, 1, 3, 5, 7]);
  assert.equal(ladderPlan(1, 500).length, 1);
  assert.ok(ladderPlan(4, 6000).length > ladderPlan(2, 1200).length);
});

test('Brake 5: melted flattens it to one note and drops the whole thing an octave', () => {
  for (const t of [1, 2, 3, 4]) assert.equal(ladderSteps(t, true), 1);
  assert.equal(ladderPlan(4, 6000, true).length, 1);
  assert.equal(ladderSemis(5, true), 5 - 12);
  assert.equal(ladderSemis(0, true), -12);   // never silent, just lower (Brake 6)
});

test('the cap holds: 7 layers at most, whatever is asked for', () => {
  assert.equal(ladderSemis(99, false), 7);
  assert.equal(ladderSteps(9), 7);
  assert.ok(ladderPlan(4, 60000).length <= LADDER.CAP);
});

test('Brake 7: no two notes closer than the 6 Hz floor, and they only ever rise', () => {
  for (const tier of [1, 2, 3, 4]) {
    for (const ms of [0, 1, 80, 166, 300, 500, 1200, 2000, 6000]) {
      const plan = ladderPlan(tier, ms);
      assert.ok(plan.length >= 1, 'there is always a landing note');
      assert.equal(plan[0].at, 0);
      for (let i = 1; i < plan.length; i++) {
        assert.ok(plan[i].at - plan[i - 1].at >= Math.floor(LADDER.STROBE_MIN_MS), `${tier}/${ms} step ${i} too fast`);
        assert.equal(plan[i].semis, plan[i - 1].semis + 1);
      }
      assert.ok(plan[plan.length - 1].at <= Math.max(ms, LADDER.STROBE_MIN_MS), 'a note after the party ended');
    }
  }
});

test('nonsense in, a landing note out - the ladder never throws and never goes silent', () => {
  for (const junk of [undefined, null, NaN, 'big', -4]) {
    assert.equal(ladderPlan(junk, junk).length, 1);
    assert.equal(ladderSteps(junk), 0);
  }
});
