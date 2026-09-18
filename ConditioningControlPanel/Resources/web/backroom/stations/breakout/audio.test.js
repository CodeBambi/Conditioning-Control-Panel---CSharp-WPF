import test from 'node:test';
import assert from 'node:assert/strict';
import { createAudio, createBeat, cutoffFor, layerLevel, hitSemis, hitCutoff, LOOP_STEPS, MIN_LEAD_S } from './audio.js';

/* ---- a fake Web Audio graph: enough surface for the module, every scheduled start is counted ---- */
function fakeContext() {
  const log = { starts: [], nodes: 0 };
  const param = v => ({
    value: v,
    setValueAtTime(x) { this.value = x; return this; },
    linearRampToValueAtTime(x) { this.value = x; return this; },
    exponentialRampToValueAtTime(x) { this.value = x; return this; },
    cancelScheduledValues() { return this; },
  });
  const node = extra => { log.nodes++; return { connect() { return this; }, disconnect() {}, ...extra }; };
  const source = extra => node({ onended: null, start(t) { log.starts.push({ t, ...extra.tag }); }, stop() {}, ...extra });
  const ctx = {
    state: 'suspended', currentTime: 0, sampleRate: 48000, destination: {}, log,
    resume() { ctx.state = 'running'; return Promise.resolve(); },
    suspend() { ctx.state = 'suspended'; return Promise.resolve(); },
    close() { ctx.state = 'closed'; return Promise.resolve(); },
    createGain: () => node({ gain: param(1) }),
    createBiquadFilter: () => node({ type: 'lowpass', frequency: param(350), Q: param(1) }),
    createDelay: () => node({ delayTime: param(0) }),
    createStereoPanner: () => node({ pan: param(0) }),
    createBuffer: (ch, len) => ({ getChannelData: () => new Float32Array(len) }),
    createBufferSource: () => source({ tag: { k: 'noise' }, buffer: null, loop: false }),
    createOscillator: () => source({ tag: { k: 'tone' }, type: 'sine', frequency: param(440) }),
  };
  return ctx;
}
function make(opts = {}) {
  let ctx = null;
  const AudioContext = function () { ctx = fakeContext(); return ctx; };
  const audio = createAudio({ AudioContext, ...opts });
  return { audio, ctx: () => ctx };
}

test('beat math: spb, phase, next sixteenth, quantise with the 15 ms guard', () => {
  const b = createBeat(120, 0);
  assert.equal(b.spb, 0.5);
  assert.equal(b.sixteenth, 0.125);
  assert.ok(Math.abs(b.phase(0.3) - 0.6) < 1e-9);
  assert.ok(Math.abs(b.phase(1.0)) < 1e-9);
  assert.ok(Math.abs(b.nextSixteenth(0.3) - 0.375) < 1e-9);
  assert.ok(Math.abs(b.nextSixteenth(0.375) - 0.5) < 1e-9, 'on a boundary the next one is the one after');
  assert.ok(Math.abs(b.quantise(0.3) - 0.375) < 1e-9);
  assert.ok(Math.abs(b.quantise(0.365) - 0.5) < 1e-9, 'under 15 ms away: use the one after');
  assert.ok(Math.abs(b.quantise(0.375 - MIN_LEAD_S - 0.001) - 0.375) < 1e-9);
  for (let i = 0; i < 200; i++) {
    const t = i * 0.0137, q = b.quantise(t);
    assert.ok(q > t && q - t <= b.sixteenth + MIN_LEAD_S, 'never more than a sixteenth (plus the guard) late');
  }
});

test('beat math: rebase moves the grid, negative phase wraps', () => {
  const b = createBeat(96, 0);
  b.rebase(10);
  assert.ok(Math.abs(b.phase(10 + b.spb * 0.25) - 0.25) < 1e-9);
  assert.ok(b.phase(9.9) >= 0 && b.phase(9.9) < 1);
  assert.equal(b.stepIndex(10), 0);
  assert.equal(b.stepIndex(10 + b.sixteenth * 3.5), 3);
  assert.equal(createBeat('nope').spb, 60 / 96, 'a bad bpm falls back to 96');
});

test('curves: cutoff is exponential 300..12k, layers fade in above their thresholds, combo capped at 14', () => {
  assert.equal(cutoffFor(0), 300);
  assert.ok(Math.abs(cutoffFor(1) - 12000) < 1e-6);
  assert.ok(Math.abs(cutoffFor(0.5) - Math.sqrt(300 * 12000)) < 1e-6);
  assert.equal(cutoffFor(-3), 300);
  assert.equal(layerLevel(0.3, 0.4), 0);
  assert.ok(Math.abs(layerLevel(0.5, 0.4) - 0.5) < 1e-9);
  assert.equal(layerLevel(0.9, 0.7), 1);
  assert.equal(hitSemis(0), 0);
  assert.equal(hitSemis(4), 9);
  assert.equal(hitSemis(5), 12);
  assert.equal(hitSemis(14), 33);
  assert.equal(hitSemis(40), 33);
  assert.equal(hitSemis(-2), 0);
  assert.equal(hitCutoff(0), 1200);
  assert.ok(Math.abs(hitCutoff(1) - 9600) < 1e-6);
});

test('before start(): every method is a no-op, the beat still runs on the fallback clock', () => {
  const { audio, ctx } = make();
  const t0 = audio.now();
  assert.ok(t0 >= 0 && t0 < 1);
  assert.equal(audio.hit('brick', { combo: 3 }), 0);
  audio.relapse(); audio.breakout(); audio.crack(); audio.wallCleared(); audio.split();
  audio.setSaturation(0.6); audio.setState('grey');
  assert.equal(ctx(), null, 'no context is built before a gesture');
  assert.ok(audio.beat.nextSixteenth(audio.now()) > audio.now());
  assert.equal(audio.beat.spb, 60 / 96);
});

test('start() is idempotent, builds one context, resumes it, and the bed schedules ahead', () => {
  const { audio, ctx } = make({ bpm: 120 });
  assert.equal(audio.start(), true);
  const c = ctx();
  assert.equal(c.state, 'running');
  assert.equal(audio.start(), true);
  assert.equal(audio.start(), true);
  assert.equal(ctx(), c, 'the same context every time');
  const before = c.log.starts.length;
  assert.ok(before > 0, 'the first pump writes the first step');
  c.currentTime = 2.0; audio.pump();
  assert.ok(c.log.starts.length > before, 'the pump keeps writing as the clock advances');
  const latest = Math.max(...c.log.starts.map(s => s.t));
  assert.ok(latest <= 2.0 + 0.12 + 1e-9, 'never further than the lookahead');
  assert.ok(latest > 2.0, 'and always ahead of now');
  audio.destroy();
});

test('hits land on the bed grid at the next sixteenth and carry a pan', () => {
  const { audio, ctx } = make({ bpm: 120 });
  audio.start();
  const c = ctx();
  c.currentTime = 1.0;
  const n0 = c.log.starts.length;
  const at = audio.hit('brick', { combo: 2, x: 0.9 });
  assert.ok(at > 1.0 && at - 1.0 <= audio.beat.sixteenth + MIN_LEAD_S);
  assert.ok(Math.abs(audio.beat.phase(at) * 4 % 1) < 1e-6, 'a sixteenth boundary');
  assert.equal(c.log.starts.length - n0, 3, 'a brick is a bell, a partial and a transient');
  assert.equal(audio.hit('bogus'), 0);
  for (const kind of ['wall', 'paddle', 'gif', 'spiral']) assert.ok(audio.hit(kind, { combo: 20 }) > 0);
  audio.destroy();
});

test('grey state: dull single-voice hits, filter slammed, layers muted; colour restores', () => {
  const { audio, ctx } = make();
  audio.start();
  const c = ctx();
  audio.setSaturation(0.9);
  audio.setState('grey');
  assert.equal(audio.state, 'grey');
  const n0 = c.log.starts.length;
  audio.hit('brick', { combo: 5 });
  assert.equal(c.log.starts.length - n0, 1, 'one dull voice in grey');
  audio.setState('colour');
  assert.equal(audio.state, 'colour');
  assert.equal(audio.saturation, 0.9);
  audio.destroy();
});

test('relapse goes grey by itself, breakout comes back to colour; one-shots schedule voices', () => {
  const { audio, ctx } = make();
  audio.start();
  const c = ctx();
  c.currentTime = 3;
  let n = c.log.starts.length;
  audio.relapse();
  assert.equal(audio.state, 'grey');
  assert.ok(c.log.starts.length > n);
  n = c.log.starts.length;
  audio.breakout();
  assert.equal(audio.state, 'colour');
  assert.ok(c.log.starts.length - n >= 8);
  for (const fn of ['crack', 'wallCleared', 'split']) { n = c.log.starts.length; audio[fn](); assert.ok(c.log.starts.length > n, fn); }
  audio.destroy();
});

test('stop suspends, start resumes, destroy closes and makes everything a no-op', () => {
  const { audio, ctx } = make();
  audio.start();
  const c = ctx();
  audio.stop();
  assert.equal(c.state, 'suspended');
  audio.start();
  assert.equal(c.state, 'running');
  audio.destroy();
  assert.equal(c.state, 'closed');
  assert.equal(audio.start(), false);
  assert.equal(audio.hit('brick'), 0);
  assert.equal(audio.context, null);
});

test('the loop is 4 bars of 16 sixteenths and a whole loop schedules without throwing', () => {
  assert.equal(LOOP_STEPS, 64);
  const { audio, ctx } = make({ bpm: 240 });
  audio.start();
  const c = ctx();
  audio.setSaturation(1);
  for (let t = 0; t < audio.beat.loop * 2; t += 0.1) { c.currentTime = t; audio.pump(); }
  assert.ok(c.log.starts.length > 200);
  audio.destroy();
});
