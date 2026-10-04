import test from 'node:test';
import assert from 'node:assert/strict';

import { softPulseAmount, softPulse, SOFT_PULSE_REDUCED_CAP } from '../shared/softPulse.js';
import { setMotionLevel, motionLevel } from '../shared/motion.js';

test('full motion passes the amount through', () => {
  assert.equal(softPulseAmount(0.7, 'full'), 0.7);
});

test('reduced caps a bright pulse and leaves a soft one alone', () => {
  assert.equal(softPulseAmount(0.8, 'reduced'), SOFT_PULSE_REDUCED_CAP);
  assert.equal(softPulseAmount(0.1, 'reduced'), 0.1);
});

test('off never pulses', () => {
  assert.equal(softPulseAmount(0.8, 'off'), 0);
});

test('a bad amount is no pulse', () => {
  assert.equal(softPulseAmount(NaN, 'full'), 0);
  assert.equal(softPulseAmount(-1, 'full'), 0);
});

test('softPulse follows the page motion level and skips under off', () => {
  const calls = [];
  const fx = { pulseFlash: (a) => calls.push(a) };
  const before = motionLevel();
  setMotionLevel('full'); softPulse(fx, 0.7);
  setMotionLevel('reduced'); softPulse(fx, 0.7);
  setMotionLevel('off'); softPulse(fx, 0.7);
  setMotionLevel(before);
  assert.deepEqual(calls, [0.7, SOFT_PULSE_REDUCED_CAP]);
});
