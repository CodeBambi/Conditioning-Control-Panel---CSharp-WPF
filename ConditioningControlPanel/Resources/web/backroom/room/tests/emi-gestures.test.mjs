import { test } from 'node:test';
import assert from 'node:assert/strict';
import { sampleEmiGesture } from '../emi-gestures.js';

const profiles = [
  ['counter', 19, 4, 4.8], ['wheel', 17, 7, 4.2],
  ['cards', 23, 10, 5.5], ['roulette', 21, 2, 4.6],
];
const rest = { yaw: 0, pitch: 0, roll: 0, lift: 0, face: null };

test('only the four NPC stations receive gestures, and invalid clocks stay still', () => {
  for (const id of ['slot', '', 'unknown']) assert.deepEqual(sampleEmiGesture(id, 6), rest);
  for (const t of [-1, NaN, Infinity]) assert.deepEqual(sampleEmiGesture('counter', t), rest);
});

test('gestures keep feet planted, respect small angle limits and imply no game result', () => {
  for (const [id, period] of profiles) {
    let moves = false;
    for (let t = 0; t < period * 3; t += .015) {
      const pose = sampleEmiGesture(id, t);
      for (const key of ['yaw', 'pitch', 'roll']) assert.ok(Number.isFinite(pose[key]));
      assert.ok(Math.abs(pose.yaw) <= .085);
      assert.ok(Math.abs(pose.pitch) <= .05);
      assert.ok(Math.abs(pose.roll) <= .016);
      assert.equal(pose.lift, 0);
      assert.equal(pose.face, null);
      moves ||= Math.abs(pose.yaw) + Math.abs(pose.pitch) > .01;
    }
    assert.ok(moves, id + ' must have a visible gesture');
  }
});

test('all arrivals, departures and cycle seams return smoothly to shared idle', () => {
  for (const [id, period, start, duration] of profiles) {
    assert.deepEqual(sampleEmiGesture(id, start), rest);
    assert.deepEqual(sampleEmiGesture(id, period), rest);
    for (const boundary of [start, start + duration, period]) {
      const before = sampleEmiGesture(id, boundary - .0001);
      const after = sampleEmiGesture(id, boundary + .0001);
      for (const key of ['yaw', 'pitch', 'roll']) {
        assert.ok(Math.abs(before[key] - after[key]) < 1e-7, id + ' ' + boundary);
      }
    }
    assert.ok(sampleEmiGesture(id, start + duration + .1).yaw === 0);
  }
});

test('each station has a distinct gesture and deterministic periodic sampling', () => {
  const signatures = profiles.map(([id, period, start, duration]) => {
    const values = [.2, .45, .7].map(f => sampleEmiGesture(id, start + duration * f));
    for (let i = 0; i < 3; i++) {
      const repeated = sampleEmiGesture(id, start + duration * [.2, .45, .7][i] + period);
      for (const key of ['yaw', 'pitch', 'roll']) assert.ok(Math.abs(repeated[key] - values[i][key]) < 1e-12);
    }
    return JSON.stringify(values);
  });
  assert.equal(new Set(signatures).size, 4);
});