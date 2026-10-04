import test from 'node:test';
import assert from 'node:assert/strict';

import {
  rungForStreak, freqForRung, gainForRung, depthCutoffHz, depthDroneMul, createFeelAudio,
  LADDER_RUNGS, LADDER_ROOT_HZ, POP_GAIN, DEPTH_OPEN_HZ, DEPTH_DEEP_HZ, DEPTH_DRONE_LIFT,
  VOICE_DUCK, POP_GAP_MS, RESOLVE_HZ,
} from '../game/feelAudio.js';

const near = (a, b, eps = 0.01) => assert.ok(Math.abs(a - b) <= eps, `${a} vs ${b}`);

test('the ladder starts on the root, climbs one rung a pop, and caps', () => {
  assert.equal(rungForStreak(1), 0);
  assert.equal(rungForStreak(2), 1);
  assert.equal(rungForStreak(LADDER_RUNGS), LADDER_RUNGS - 1);
  assert.equal(rungForStreak(500), LADDER_RUNGS - 1);
});

test('a broken streak, or nonsense, is the root', () => {
  for (const v of [0, -3, NaN, undefined, null, 'x']) assert.equal(rungForStreak(v), 0);
});

test('rungs are the C pentatonic: C D E G A, then the octave', () => {
  near(freqForRung(0), 261.63);
  near(freqForRung(1), 293.66);
  near(freqForRung(2), 329.63);
  near(freqForRung(3), 392.0);
  near(freqForRung(4), 440.0);
  near(freqForRung(5), 523.25, 0.02);
  near(freqForRung(7), 659.26, 0.02);
});

test('no two rungs sit a semitone apart, and the ladder only rises', () => {
  for (let r = 1; r < 12; r++) {
    const ratio = freqForRung(r) / freqForRung(r - 1);
    assert.ok(ratio > Math.pow(2, 1.5 / 12), `rung ${r} is too close to the one below`);
  }
});

test('the ladder tilts down in level as it climbs', () => {
  assert.equal(gainForRung(0), POP_GAIN);
  for (let r = 1; r < LADDER_RUNGS; r++) assert.ok(gainForRung(r) < gainForRung(r - 1));
  assert.ok(gainForRung(LADDER_RUNGS - 1) > POP_GAIN * 0.5);
});

test('depth closes the low-pass exponentially and lifts the drone linearly', () => {
  near(depthCutoffHz(0), DEPTH_OPEN_HZ);
  near(depthCutoffHz(1), DEPTH_DEEP_HZ);
  near(depthCutoffHz(0.5), Math.sqrt(DEPTH_OPEN_HZ * DEPTH_DEEP_HZ), 0.5);
  near(depthCutoffHz(7), DEPTH_DEEP_HZ);
  near(depthCutoffHz(NaN), DEPTH_OPEN_HZ);
  assert.equal(depthDroneMul(0), 1);
  near(depthDroneMul(0.5), 1 + DEPTH_DRONE_LIFT / 2);
  near(depthDroneMul(2), 1 + DEPTH_DRONE_LIFT);
});

/** A context that records what a voice asked of it. */
function fakeCtx() {
  const log = { oscs: [], peaks: [] };
  const param = (onPeak) => ({
    value: 0,
    setValueAtTime() {},
    linearRampToValueAtTime(v) { if (onPeak) onPeak(v); },
    exponentialRampToValueAtTime() {},
  });
  return {
    log,
    state: 'running',
    currentTime: 1,
    destination: {},
    createGain() { return { gain: param((v) => log.peaks.push(v)), connect() {}, disconnect() {} }; },
    createOscillator() {
      const o = { type: '', frequency: { value: 0 }, connect() {}, disconnect() {}, start() {}, stop() {} };
      log.oscs.push(o);
      return o;
    },
  };
}

function rig(over = {}) {
  const c = fakeCtx();
  let t = 0;
  const io = {
    getCtx: () => c, getOut: () => c.destination, silent: () => false, level: () => 0.48,
    voiceActive: () => false, now: () => t, ...over,
  };
  return { c, feel: createFeelAudio(io), tick: (ms) => { t += ms; } };
}

test('a pop sounds its rung as a sine with one octave partial', () => {
  const { c, feel, tick } = rig();
  tick(1000);
  assert.equal(feel.pop(3), true);
  assert.equal(c.log.oscs.length, 2);
  near(c.log.oscs[0].frequency.value, freqForRung(2));
  near(c.log.oscs[1].frequency.value, freqForRung(2) * 2);
  assert.ok(c.log.oscs.every((o) => o.type === 'sine'));
  near(c.log.peaks[0], gainForRung(2), 1e-9);
});

test('a sweep of pops inside the gap is one note, not a cluster', () => {
  const { c, feel, tick } = rig();
  tick(1000);
  feel.pop(1); feel.pop(2); feel.pop(3);
  assert.equal(c.log.oscs.length, 2);
  tick(POP_GAP_MS + 1);
  feel.pop(4);
  assert.equal(c.log.oscs.length, 4);
});

test('muted, a zeroed slider or a sleeping context play nothing', () => {
  let r = rig({ silent: () => true }); r.tick(1000);
  assert.equal(r.feel.pop(1), false);
  r = rig({ level: () => 0 }); r.tick(1000);
  assert.equal(r.feel.pop(1), false);
  r = rig(); r.c.state = 'suspended'; r.tick(1000);
  assert.equal(r.feel.pop(1), false);
  assert.equal(r.c.log.oscs.length, 0);
});

test('every voice sits under a speaking voice line', () => {
  const { c, feel, tick } = rig({ voiceActive: () => true });
  tick(1000);
  feel.pop(1);
  near(c.log.peaks[0], POP_GAIN * VOICE_DUCK, 1e-9);
});

test('the slider can lift a note to at most twice its quoted gain', () => {
  const { c, feel, tick } = rig({ level: () => 1 });
  tick(1000);
  feel.pop(1);
  assert.ok(c.log.peaks[0] <= POP_GAIN * 2 + 1e-9 && c.log.peaks[0] > POP_GAIN);
});

test('a gold pop does not climb: the current rung an octave up', () => {
  const { c, feel, tick } = rig();
  tick(1000);
  feel.pop(2, { gold: true });
  near(c.log.oscs[0].frequency.value, freqForRung(1) * 2);
});

test('the landing resolves on the low root, an octave under the ladder', () => {
  const { c, feel } = rig();
  feel.resolve();
  near(c.log.oscs[0].frequency.value, RESOLVE_HZ);
  near(RESOLVE_HZ * 2, LADDER_ROOT_HZ);
});
