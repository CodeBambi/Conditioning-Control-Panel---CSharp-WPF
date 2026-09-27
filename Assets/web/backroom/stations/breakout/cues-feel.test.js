import test from 'node:test';
import assert from 'node:assert/strict';
import FEEL, { STREAK_CLIMB_MAX } from './cues/feel.js';
import { CUES } from './cues.js';
import { createBeat, hitSemis, hitCutoff } from './audio.js';
import { pentatonic, ROOT_HZ } from '../../shared/sound/kit.js';

/* ---- a fake synth: the same builders audio.js hands a cue, with every play() and quantise() recorded ---- */
const NOW = 10.03, SEMI = s => 2 ** (s / 12);
function fakeSynth({ state = 'colour', saturation = 0.6 } = {}) {
  const beat = createBeat(96, 0), log = { plays: [], quantised: 0 };
  const bus = { bed: { n: 'bed' }, sfx: { n: 'sfx' }, sub: { n: 'sub' }, word: { n: 'word' } };
  const synth = {
    ctx: {}, now: NOW, bus, SEMI, ROOT_HZ, pentatonic, hitSemis, hitCutoff, beat, saturation, state, room: {},
    tone: (hz, dur, level, o = {}) => ({ k: 'tone', hz, dur, level, ...o }),
    noise: (hz, dur, level, o = {}) => ({ k: 'noise', hz, dur, level, ...o }),
    play: (notes, t, dest) => log.plays.push({ notes, t, dest }),
    glide() {}, duck() {},
    quantise: lead => { log.quantised++; return beat.quantise(NOW, lead); },
  };
  return { synth, log, beat, bus };
}
const PENTA = [0, 2, 4, 7, 9];
function inKey(hz) {
  const n = 12 * Math.log2(hz / ROOT_HZ), r = Math.round(n);
  return Math.abs(n - r) < 1e-6 && PENTA.includes(((r % 12) + 12) % 12);
}
const onGrid = (beat, t) => { const k = (t - beat.origin) / beat.sixteenth; return Math.abs(k - Math.round(k)) < 1e-6; };
const notesOf = log => log.plays.flatMap(p => p.notes);
const pitched = log => notesOf(log).filter(n => n.k === 'tone' && !n.partial);

const CASES = [
  { name: 'launch', data: { x: 100, y: 500, xN: 0.25 }, quantised: true },
  { name: 'lost', data: { x: 100, y: 700, xN: 0.25 }, quantised: false },
  { name: 'lastBrick', data: { x: 300, y: 120, xN: 0.25 }, quantised: false },
  { name: 'layer', data: { name: 'melody', sat: 0.4 }, quantised: true },
  { name: 'layer', data: { name: 'arp', sat: 0.7 }, quantised: true },
  { name: 'perfect', data: { streak: 2, xN: 0.5 }, quantised: true },
  { name: 'perfect', data: { streak: 9, xN: 0.5 }, quantised: true },
];

test('the feel cues are registered under the contract names', () => {
  for (const name of ['launch', 'lost', 'lastBrick', 'layer', 'perfect']) {
    assert.equal(typeof FEEL[name], 'function', name);
    assert.equal(CUES[name], FEEL[name], name + ' reaches the registry');
  }
});

for (const state of ['colour', 'grey']) for (const c of CASES) {
  test(`${c.name} ${c.data.name || c.data.streak || ''} in ${state}: in key, on the right clock, quiet and short`, () => {
    const { synth, log, beat, bus } = fakeSynth({ state });
    const played = FEEL[c.name](synth, { ...c.data, state }) !== false;
    if (state === 'grey' && (c.name === 'layer' || c.name === 'perfect')) {
      assert.equal(played, false, 'grey: layers never enter and the older stamp is left alone');
      assert.equal(log.plays.length, 0);
      return;
    }
    assert.ok(played && log.plays.length > 0);
    for (const n of pitched(log)) {
      assert.ok(inKey(n.hz), `${c.name}: ${n.hz.toFixed(2)} Hz is in the major pentatonic of the root`);
      if (n.hzTo) assert.ok(inKey(n.hzTo), `${c.name}: a lean lands in key too`);
    }
    for (const p of log.plays) {
      assert.equal(p.dest, bus.sfx, 'feel cues live on the sfx bus: never the bed, never the unducked word bus');
      if (c.quantised) { assert.ok(p.t > NOW && onGrid(beat, p.t), 'a note: on the grid'); }
      else assert.equal(p.t, NOW, 'a body: immediate');
    }
    assert.equal(log.quantised > 0, c.quantised, c.quantised ? 'asks the grid' : 'never asks the grid');
    for (const n of notesOf(log)) {
      assert.ok(n.level <= 0.13, 'inside the house level range');
      assert.ok(n.dur <= beat.bar, 'short');
      if (state === 'grey') { assert.ok(!n.wet, 'grey is dry'); if (n.k === 'tone') assert.ok(n.lp <= 500, 'and dull'); }
    }
  });
}

test('launch blips UP; the lost ball settles and never falls for more than 80 ms', () => {
  let { synth, log } = fakeSynth();
  FEEL.launch(synth, { xN: 0.5 });
  const up = pitched(log).sort((a, b) => (a.at || 0) - (b.at || 0));
  assert.ok(up.length >= 2 && up[up.length - 1].hz > up[0].hz);
  ({ synth, log } = fakeSynth());
  FEEL.lost(synth, { xN: 0.5 });
  for (const n of notesOf(log)) {
    if (n.hzTo && n.hzTo < n.hz) assert.ok(n.dur <= 0.08, 'a downward lean is a tap, not a fall');
    if (n.k === 'tone') assert.ok(n.lp <= 600 && n.hz < ROOT_HZ / 2 + 1, 'muffled and low');
  }
  const body = pitched(log).reduce((a, b) => (b.dur > a.dur ? b : a));
  assert.ok(Math.abs(body.hz - ROOT_HZ / 4) < 1e-9 && !body.hzTo, 'it comes to rest on the root');
});

test('lastBrick is a 0.4 s reverse swell with a soft root under the wall-clear bells', () => {
  const { synth, log } = fakeSynth();
  FEEL.lastBrick(synth, { xN: 0.5 });
  const swell = notesOf(log).find(n => n.k === 'noise'), root = pitched(log)[0];
  assert.ok(swell && Math.abs(swell.dur - 0.4) < 0.05 && swell.attack >= 0.8 && swell.hzTo > swell.hz, 'rising, and loudest at the end');
  assert.ok(root.hz < ROOT_HZ * 2, 'below the bells (which start an octave above the root)');
  assert.ok(root.level <= 0.05 && swell.level <= 0.05, 'quieter than the bells it sets up (0.11)');
});

test('layer: melody and arp are different gestures on beats, and an unknown layer is silent', () => {
  const a = fakeSynth(), b = fakeSynth(), c = fakeSynth();
  FEEL.layer(a.synth, { name: 'melody' }); FEEL.layer(b.synth, { name: 'arp' });
  assert.equal(FEEL.layer(c.synth, { name: 'bass' }), false);
  assert.equal(c.log.plays.length, 0);
  for (const x of [a, b]) {
    const k = (x.log.plays[0].t - x.beat.origin) / x.beat.spb;
    assert.ok(Math.abs(k - Math.round(k)) < 1e-6, 'the announcement starts on a beat');
    for (const n of pitched(x.log)) assert.ok(onGrid(x.beat, x.log.plays[0].t + (n.at || 0)), 'every note of the fill is on the grid');
  }
  assert.ok(notesOf(a.log).every(n => n.k === 'tone'), 'the melody entry is a fill');
  assert.ok(notesOf(b.log).some(n => n.k === 'noise'), 'the arp entry carries a riser');
  assert.notDeepEqual(pitched(a.log).map(n => n.hz), pitched(b.log).map(n => n.hz));
});

test('perfect: streak 1 defers to the older stamp; from 2 the second note climbs the pentatonic, capped', () => {
  const second = streak => { const { synth, log } = fakeSynth(); const r = FEEL.perfect(synth, { streak }); return r === false ? null : pitched(log)[1].hz; };
  assert.equal(second(undefined), null);
  assert.equal(second(0), null);
  assert.equal(second(1), null);
  const fifth = ROOT_HZ * 2 * SEMI(7);
  let last = fifth;
  for (let s = 2; s <= STREAK_CLIMB_MAX + 1; s++) { const hz = second(s); assert.ok(hz > last, `streak ${s} climbs`); last = hz; }
  assert.equal(second(STREAK_CLIMB_MAX + 2), last, 'capped');
  assert.equal(second(99), last);
  const { synth, log } = fakeSynth();
  FEEL.perfect(synth, { streak: 2 });
  const [root, up] = pitched(log);
  assert.equal(root.hz, ROOT_HZ * 2); assert.equal(root.level, 0.12); assert.equal(up.level, 0.12);
  assert.ok(notesOf(log).some(n => n.partial), 'the bell partial is flagged, so it is not mistaken for a pitch');
});
