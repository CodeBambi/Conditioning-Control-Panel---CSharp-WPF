/* kit.test.mjs - the schedules the kit scores and the way it treats a context. node --test, no audio. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { score, createKit, pentatonic, TIERS, TIER_ORDER, CUES, BEDS, ROLLS, DEFAULT_MASTER, BED_LEVEL, DEDUPE_MS,
  LEVER_VARIANTS, REEL_VARIANTS, DEFAULT_SFX, ROLL_TICK, ROLL_BED, tickGap,
  WHEEL_ROLL, WHEEL_TICK, WHEEL_BED, wheelGap } from '../kit.js';

/* ------------------------------------------------------------------ a mocked AudioContext */
function makeParam(v = 0) {
  const p = { value: v, calls: [] };
  for (const m of ['setValueAtTime', 'exponentialRampToValueAtTime', 'linearRampToValueAtTime', 'cancelScheduledValues']) {
    p[m] = (...a) => { p.calls.push([m, ...a]); if (m === 'setValueAtTime') p.value = a[0]; return p; };
  }
  return p;
}
function makeMock() {
  const log = { nodes: [], sources: [], started: 0, stopped: 0, state: 'running', suspends: 0, resumes: 0, closes: 0 };
  class Node {
    constructor(kind) { this.kind = kind; this.connections = 0; this.disconnected = false; log.nodes.push(this); }
    connect() { this.connections++; return this; }
    disconnect() { this.disconnected = true; this.connections = 0; }
  }
  class Source extends Node {
    constructor(kind) { super(kind); this.startedAt = null; this.stopAt = null; this.onended = null; log.sources.push(this); }
    start(t = 0) { this.startedAt = t; log.started++; }
    stop(t = 0) { if (this.stopAt == null) log.stopped++; this.stopAt = t; }
  }
  class AC {
    constructor() { this.sampleRate = 48000; this.currentTime = 0; this.destination = new Node('destination'); log.ctx = this; log.state = 'running'; }
    get state() { return log.state; }
    createGain() { const n = new Node('gain'); n.gain = makeParam(1); return n; }
    createOscillator() { const n = new Source('osc'); n.type = 'sine'; n.frequency = makeParam(440); return n; }
    createBufferSource() { const n = new Source('buffer'); n.buffer = null; n.loop = false; n.playbackRate = makeParam(1); return n; }
    createBiquadFilter() { const n = new Node('filter'); n.type = 'lowpass'; n.frequency = makeParam(350); n.Q = makeParam(1); return n; }
    createDelay() { const n = new Node('delay'); n.delayTime = makeParam(0); return n; }
    createBuffer(ch, len, rate) { return { length: len, sampleRate: rate, getChannelData: () => new Float32Array(Math.min(len, 64)) }; }
    suspend() { log.suspends++; log.state = 'suspended'; return Promise.resolve(); }
    resume() { log.resumes++; log.state = 'running'; return Promise.resolve(); }
    close() { log.closes++; log.state = 'closed'; return Promise.resolve(); }
  }
  return { AC, log };
}
const seq = (...vals) => { let i = 0; return () => vals[(i++) % vals.length]; };
const tones = s => s.notes.filter(n => n.k === 'tone');
const parts = (s, p) => s.notes.filter(n => n.part === p);
/** No voice anywhere in the cue sweeps downward. */
const neverFalls = s => s.notes.every(n => !(n.hzTo && n.hzTo < n.hz));
/** The voices in `part` never go lower as time goes on. */
const risesInTime = (s, part) => {
  const xs = parts(s, part).filter(n => n.k === 'tone').sort((a, b) => a.at - b.at);
  return xs.every((n, i) => i === 0 || n.hz >= xs[i - 1].hz - 1e-9);
};
const near = (a, b, eps = 1e-6) => Math.abs(a - b) < eps;

/* ------------------------------------------------------------------ the scores */
test('every cue scores or is a bed or a roll, and an unknown name is null', () => {
  for (const name of CUES) {
    if (BEDS.includes(name) || ROLLS.includes(name)) { assert.equal(score(name), null); continue; }
    const s = score(name, {}, seq(0.5));
    assert.ok(s && s.notes.length > 0, name + ' has notes');
    assert.ok(s.tail > 0 && Number.isFinite(s.tail), name + ' has a tail');
    for (const n of s.notes) { assert.ok(n.hz > 0 && n.dur > 0 && n.level >= 0 && n.at >= 0, name + ' note is sane'); }
  }
  assert.equal(score('nope'), null);
});

test('pentatonic climbs and never repeats a rung', () => {
  const rungs = Array.from({ length: 12 }, (_, k) => pentatonic(k));
  assert.deepEqual(rungs, [0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24, 26]);
});

test('WIN: every tier has a longer and richer tail than the one below, and its tail matches the table', () => {
  let lastTail = 0, lastNotes = 0;
  for (const tier of TIER_ORDER) {
    const s = score('win', { tier });
    assert.ok(s.tail > lastTail, tier + ' tail ' + s.tail + ' > ' + lastTail);
    assert.ok(s.notes.length > lastNotes, tier + ' has more voices than the tier below');
    assert.ok(near(s.tail, TIERS[tier].tail, 1e-9), tier + ' tail is the published ' + TIERS[tier].tail);
    for (const p of TIERS[tier].parts) assert.ok(parts(s, p).length > 0, tier + ' carries a ' + p);
    lastTail = s.tail; lastNotes = s.notes.length;
  }
  assert.ok(score('win', { tier: 'big' }).tail >= 2.3, 'big: chord plus a 2 s shimmer tail');
  const hero = score('win', { tier: 'hero' });
  assert.ok(hero.tail >= 4 && hero.tail <= 6, 'hero: a 4-6 s chime tree');
  assert.ok(parts(hero, 'sub').length === 1 && parts(hero, 'sub')[0].hz < 60, 'hero: a sub-bass bloom');
  assert.ok(parts(hero, 'arp').length >= 10, 'hero: a rolling arpeggio');
});

test('WIN: wins always rise in pitch (no voice sweeps down, each voice family only climbs)', () => {
  for (const tier of TIER_ORDER) {
    const s = score('win', { tier });
    assert.ok(neverFalls(s), tier + ' never falls');
    for (const p of ['arp', 'chord']) assert.ok(risesInTime(s, p), tier + ' ' + p + ' climbs');
    const arps = parts(s, 'arp').sort((a, b) => a.at - b.at), chords = parts(s, 'chord');
    if (arps.length && chords.length) assert.ok(arps[0].hz >= Math.max(...chords.map(n => n.hz)) - 1e-9, tier + ': the arpeggio starts above the chord');
    const crown = parts(s, 'crown');
    if (crown.length) assert.ok(crown[0].hz >= Math.max(...arps.map(n => n.hz)), tier + ': the crown bell tops the tree');
  }
  const up = score('win', { tier: 'mid', semis: 2 }), base = score('win', { tier: 'mid' });
  assert.ok(near(up.notes[0].hz / base.notes[0].hz, 2 ** (2 / 12)), 'semis moves the root');
  assert.equal(score('win', { tier: 'nope' }).tail, TIERS.small.tail, 'an unknown tier is the small one');
});

test('LADDER: pentatonic, ascending, as long as the win, and a plan fixes its times', () => {
  const short = score('ladder', { ms: 400 }), long = score('ladder', { ms: 2400 });
  const fundamentals = s => parts(s, 'arp').sort((a, b) => a.at - b.at);
  assert.ok(fundamentals(long).length > fundamentals(short).length, 'a longer count-up gets more bells');
  for (const s of [short, long]) {
    const f = fundamentals(s);
    for (let i = 1; i < f.length; i++) assert.ok(f[i].hz > f[i - 1].hz && f[i].at > f[i - 1].at, 'each bell higher and later');
    assert.ok(near(f[1].hz / f[0].hz, 2 ** (2 / 12)), 'the second rung is a whole tone up');
    assert.ok(neverFalls(s));
  }
  const planned = score('ladder', { plan: [{ at: 0, semis: 0 }, { at: 150, semis: 1 }, { at: 700, semis: 2 }], semis: 3 });
  assert.deepEqual(fundamentals(planned).map(n => n.at), [0, 0.15, 0.7], 'the plan sets the times');
  assert.ok(near(fundamentals(planned)[0].hz, 523.25 * 2 ** (3 / 12)), 'the plan root follows semis');
  assert.equal(fundamentals(score('ladder', { ms: 60000 })).length, 16, 'capped at 16 bells');
});

test('LOSS: settle is near-silent, under 700 ms, never a fall; sigh and almost too', () => {
  const s = score('settle');
  assert.ok(s.tail < 0.7, 'under 700 ms: ' + s.tail);
  assert.ok(Math.max(...s.notes.map(n => n.level)) <= 0.06, 'near-silent');
  assert.ok(neverFalls(s), 'never a descending fail sound');
  assert.equal(parts(s, 'felt').length, 1, 'one felt-wrapped chime');
  assert.ok(parts(s, 'felt')[0].attack >= 0.1, 'touched, not struck');
  const breath = parts(s, 'breath')[0];
  assert.ok(breath && breath.k === 'noise' && breath.hzTo > breath.hz, 'and a breath in the ambience that opens');
  assert.ok(score('settle', { level: 0.5 }).notes[0].level < s.notes[0].level, 'level scales it');
  for (const name of ['sigh', 'almost']) {
    const x = score(name);
    assert.ok(neverFalls(x), name + ' never falls');
    assert.ok(Math.max(...x.notes.map(n => n.level)) <= 0.06, name + ' stays quiet');
  }
});

test('ANTICIPATION: the riser climbs over 1.5-2.5 s and resolves on a quiet top note', () => {
  for (const ms of [1500, 2000, 2500]) {
    const s = score('riser', { ms }), rise = parts(s, 'rise')[0], resolve = parts(s, 'resolve')[0];
    assert.ok(rise.hzTo > rise.hz && rise.lpTo > rise.lp, 'pitch and filter both open upward');
    assert.ok(near(rise.dur, ms / 1000), 'the sweep is the hold');
    assert.ok(near(resolve.at, ms / 1000) && resolve.level < rise.level, 'resolves quietly at the top');
    assert.ok(near(resolve.hz, rise.hzTo), 'on the note the sweep reached');
  }
  const calm = score('riser', { ms: 2000, level: 0.5 });
  assert.ok(calm.notes[0].level < score('riser', { ms: 2000 }).notes[0].level, 'Calm plays it softer');
});

test('TICKS: a rising ladder per reel, decelerating into the stop', () => {
  const a = score('ticks', { reel: 0, ms: 1800 }), b = score('ticks', { reel: 2, ms: 1800 });
  assert.ok(b.notes[0].hz > a.notes[0].hz, 'a later reel sits higher');
  const gaps = a.notes.slice(1).map((n, i) => n.at - a.notes[i].at);
  assert.ok(gaps.every((g, i) => i === 0 || g >= gaps[i - 1] - 1e-9), 'the gaps only grow');
  assert.ok(a.notes.every((n, i) => i === 0 || n.hz >= a.notes[i - 1].hz), 'the pitch only climbs');
  assert.ok(score('tick', { reel: 1, step: 3 }).notes[0].hz > score('tick', { reel: 1 }).notes[0].hz);
});

test('THE THREE NEW CUES: clicker, word, settle', () => {
  const c1 = score('clicker', {}, seq(0)), c2 = score('clicker', {}, seq(1)), c3 = score('clicker', {}, seq(0.5, 0.9));
  for (const c of [c1, c2, c3]) {
    const click = parts(c, 'click').find(n => n.k === 'noise');
    assert.ok(click.dur >= 0.03 - 1e-9 && click.dur <= 0.06 + 1e-9, 'one click, 30-60 ms: ' + click.dur);
    assert.ok(c.notes.every(n => n.dry), 'dry, off the room send');
    assert.ok(c.tail <= 0.06 + 1e-9);
  }
  assert.ok(c1.notes[0].hz < c2.notes[0].hz && c3.notes[0].hz > c1.notes[0].hz, 'a little random in pitch');
  assert.ok(c2.notes[0].hz / c1.notes[0].hz <= 2 ** (4 / 12) + 1e-9, 'within four semitones');

  const w0 = score('word'), w1 = score('word', { index: 1 }), w2 = score('word', { index: 2 });
  for (const w of [w0, w1, w2]) {
    assert.ok(near(w.tail, 0.9), '0.9 s');
    const bed = parts(w, 'bed').find(n => n.k === 'noise');
    assert.ok(bed.hzTo < bed.hz, 'a gentle downward filter sweep');
    assert.ok(Math.max(...w.notes.map(n => n.level)) <= 0.05, 'a whisper');
  }
  assert.ok(near(w1.notes[0].hz / w0.notes[0].hz, 2 ** (2 / 12)), 'the second word a whole tone up');
  assert.ok(near(w2.notes[0].hz / w1.notes[0].hz, 2 ** (2 / 12)), 'the third another');
  assert.ok(near(score('word', { index: 9 }).notes[0].hz, w2.notes[0].hz), 'index caps at 2');

  assert.ok(score('settle').tail < 0.7 && neverFalls(score('settle')));
});

test('the rest of the palette: breath 6 s, chips randomised, card kinds, rattle decays, drop is one thud, clack follows semis', () => {
  const b = score('breath');
  assert.ok(near(b.tail, 6), 'a 6 s breath');
  assert.ok(parts(b, 'bed')[0].hzTo > parts(b, 'bed')[0].hz && parts(b, 'bed')[1].hzTo < parts(b, 'bed')[1].hz, 'in, then out');
  const chips = score('chips', { n: 4 }, seq(0.1, 0.9, 0.5));
  assert.equal(parts(chips, 'clink').length, 12, 'three voices a clink');
  const hz = parts(chips, 'clink').filter(n => n.k === 'tone' && n.level > 0.05).map(n => n.hz);
  assert.ok(new Set(hz).size > 1, 'randomised pitch');
  assert.ok(score('card', { kind: 'flip' }).tail < score('card').tail, 'a flip is shorter than a slide');
  const r = score('rattle', { n: 6 }).notes;
  const rGaps = r.slice(1).map((n, i) => n.at - r[i].at);
  assert.ok(r.every((n, i) => i === 0 || n.level < r[i - 1].level) && rGaps.every((g, i) => i === 0 || g > rGaps[i - 1]), 'clicks spread out and die down');
  assert.equal(parts(score('drop'), 'thud').filter(n => n.k === 'tone').length, 1, 'one damped thud');
  assert.ok(score('clack', { semis: 5 }).notes[0].hz > score('clack').notes[0].hz);
  assert.ok(score('token', { i: 3 }).notes[0].hz > score('token', { i: 0 }).notes[0].hz, 'tokens rise with the count');
  assert.ok(score('token', { last: true }).notes[0].part === 'thud', 'the last token is the mini-thud');
  assert.ok(score('thud', { muted: true }).notes[0].level < score('thud').notes[0].level, 'a muted thud is quieter');
});

test('THE LEVER: four voices, each a whole gesture in 500-700 ms', () => {
  const seen = new Map();
  for (const v of LEVER_VARIANTS) {
    const x = score('lever', { variant: v });
    assert.ok(x.tail >= 0.5 - 1e-9 && x.tail <= 0.7 + 1e-9, v + ' is a 500-700 ms gesture: ' + x.tail);
    const bottom = x.notes.reduce((m, n) => (n.level > m.level ? n : m), x.notes[0]);
    assert.ok(bottom.at >= 0.2 && bottom.at <= 0.4, v + ' stops hardest at the bottom of the stroke: ' + bottom.at);
    assert.ok(x.notes.some(n => n.at < bottom.at), v + ' has a stroke before it');
    assert.ok(x.notes.some(n => n.at > bottom.at), v + ' springs back after it');
    seen.set(v, x.notes.map(n => Math.round(n.hz) + '@' + n.at).join(','));
  }
  assert.equal(new Set(seen.values()).size, LEVER_VARIANTS.length, 'four levers, four different sounds');
  assert.equal(seen.get(DEFAULT_SFX.lever), score('lever', { variant: 'nope' }).notes.map(n => Math.round(n.hz) + '@' + n.at).join(','), 'an unknown variant is the default');
  assert.ok(Math.max(...score('lever', { level: 0.5 }).notes.map(n => n.level)) < Math.max(...score('lever').notes.map(n => n.level)), 'level scales it');
  const iron = parts(score('lever', { variant: 'A' }), 'ratchet');
  assert.ok(iron.length >= 3 && iron.every((n, i) => i === 0 || (n.hz > iron[i - 1].hz && n.at > iron[i - 1].at)), 'A ratchets up the stroke');
  assert.equal(parts(score('lever', { variant: 'A' }), 'clank').length, 1, 'A lands on one metallic clank');
  const chime = parts(score('lever', { variant: 'B' }), 'chime').sort((a, b) => a.at - b.at);
  assert.ok(chime.length >= 2 && chime[chime.length - 1].at > chime[0].at, 'B ends on a two-note chime');
  assert.ok(parts(score('lever', { variant: 'B' }), 'whoosh')[0].hzTo > parts(score('lever', { variant: 'B' }), 'whoosh')[0].hz, 'B strokes on an opening whoosh');
  assert.equal(parts(score('lever', { variant: 'B' }), 'catch').length, 1, 'B catches on the grab, so the pull is heard on its own frame');
  for (const v of LEVER_VARIANTS) assert.ok(score('lever', { variant: v }).notes.some(n => n.at <= 0.03), v + ' makes a sound on the frame the stroke starts');
  assert.ok(parts(score('lever', { variant: 'C' }), 'boing').length >= 2, 'C springs back on a boing');
  assert.ok(score('lever', { variant: 'C' }).tail < score('lever', { variant: 'D' }).tail, 'the toy lever is shorter than the vintage one');
  assert.equal(parts(score('lever', { variant: 'D' }), 'krrr').length, 1, 'D hands the beat over to the reels');
});

test('THE REEL STOP: a landing per voice, and the ding climbs reel to reel', () => {
  for (const v of REEL_VARIANTS) {
    for (const r of [0, 1, 2]) {
      const x = score('reelStop', { variant: v, reel: r });
      assert.ok(x.notes.length > 0, v + r + ' has notes');
      assert.ok(x.tail > 0.1 && x.tail < 0.5, v + r + ' is a short landing: ' + x.tail);
    }
  }
  const ding = r => parts(score('reelStop', { variant: 'C', reel: r }), 'ding')[0];
  assert.ok(ding(0).hz < ding(1).hz && ding(1).hz < ding(2).hz, 'C: first reel low, second mid, third high');
  const chime = r => parts(score('reelStop', { variant: 'D', reel: r }), 'chime')[0];
  assert.ok(chime(0).hz < chime(1).hz && chime(1).hz < chime(2).hz, 'D: the digital chime climbs too');
  const ticker = score('reelStop', { variant: 'A' });
  assert.ok(parts(ticker, 'thud').length > 0 && parts(ticker, 'ding').length > 0, 'A: a thud and a damped bell');
  const purr = score('reelStop', { variant: 'B' });
  assert.ok(purr.notes.every(n => n.part === 'thump'), 'B: a soft thump, nothing else');
  assert.ok(Math.max(...purr.notes.map(n => n.level)) <= Math.max(...ticker.notes.map(n => n.level)), 'and it is the gentlest landing');
  assert.equal(score('reelStop', { variant: 'zz' }).notes.length, score('reelStop', { variant: DEFAULT_SFX.reel }).notes.length, 'an unknown variant is the default');
});

test('THE ROLL: the tick rate is the drum speed, and the drums sit over the bed without shouting', () => {
  assert.ok(tickGap(1) < tickGap(0.5) && tickGap(0.5) < tickGap(0), 'the ticks spread out as the drum slows');
  assert.ok(near(tickGap(1), 0.038), '38 ms a tick at full blur');
  assert.ok(tickGap(0) <= 0.27, 'and about a quarter second crawling into the stop');
  assert.equal(tickGap('nonsense'), tickGap(1), 'nonsense is full speed');
  assert.ok(ROLL_TICK > BED_LEVEL * 0.4 && ROLL_TICK < 0.12, 'a tick reads through the bed without being harsh (the owner took the drums down 35% twice on 2026-09-15)');
  assert.ok(ROLL_BED > BED_LEVEL * 0.4 && ROLL_BED <= ROLL_TICK, 'the purr sits under the bed and under the ticks (the owner took the drums down 35% twice on 2026-09-15)');
});

test('THE THROW: the wheel has no lever, so the spin-up and the rotor loop are its gesture', () => {
  const s = score('whir');
  assert.ok(s && s.notes.length >= 3, 'the whir is scored, not a bed');
  assert.ok(s.tail > 0.2 && s.tail <= 0.8, 'a spin-up, not a run: ' + s.tail + ' s');
  assert.ok(parts(s, 'catch').length > 0 && parts(s, 'whir').length > 0 && parts(s, 'bearing').length > 0,
    'the catch, the whir and the bearing under it');
  assert.ok(neverFalls(s), 'the throw only opens upward, the way the rotor picks up');
  const peak = Math.max(...s.notes.map(n => n.level));
  assert.ok(peak > 0.08 && peak <= 0.26, 'about the slot lever level (0.24), never over it: ' + peak);
  assert.ok(score('whir', { level: 0.5 }).notes.every((n, i) => near(n.level, s.notes[i].level / 2)), 'level scales every voice');
  assert.ok(score('whir', { level: 0 }).notes.every(n => n.level === 0), 'level 0 is silent');
  assert.equal(WHEEL_ROLL, 'wheel');
  assert.ok(ROLLS.includes(WHEEL_ROLL) && CUES.includes(WHEEL_ROLL), 'the rotor is a roll, and it is a cue');
  assert.equal(score(WHEEL_ROLL), null, 'a roll is not scored');
  assert.ok(WHEEL_TICK > 0 && WHEEL_TICK < ROLL_TICK, 'the ratchet sits under the drums ticks: ' + WHEEL_TICK);
  assert.ok(WHEEL_BED > 0 && WHEEL_BED < ROLL_BED && WHEEL_BED <= WHEEL_TICK, 'the bearing bed sits under both: ' + WHEEL_BED);
  assert.ok(wheelGap(1) < wheelGap(0.5) && wheelGap(0.5) < wheelGap(0), 'the ratchet rate IS the rotor speed');
  assert.ok(wheelGap(1) > tickGap(1), 'and it never clatters faster than a drum');
  assert.equal(wheelGap('nonsense'), wheelGap(1), 'nonsense is full speed');
});

test('THE THROW on a context: one rotor loop for the room, following its speed, down on the landing frame', () => {
  const { AC } = makeMock();
  const k = createKit({ AudioContext: AC });
  k.arm();
  assert.equal(k.debug().wheel, false, 'the wheel is still until it is thrown');
  assert.equal(k.play('wheel', { speed: 1 }), 1, 'the throw starts the loop');
  assert.equal(k.debug().wheel, true);
  assert.ok(k.debug().live > 0, 'the ratchet is scheduled a beat ahead');
  assert.equal(k.play('wheel', { speed: 0.4 }), 1, 'a second throw follows it, it does not stack');
  assert.deepEqual(k.debug().rolls, [], 'and it never shows up as a drum');
  k.setWheelSpeed(0.1);
  k.stop('wheel');
  assert.equal(k.debug().wheel, false, 'the landing frame takes it down');
  k.setWheelSpeed(0.5);   // nothing turning: a no-op, never a throw
  k.play('wheel', { speed: 1 });
  k.suspend(true);
  assert.equal(k.debug().wheel, false, 'Law VI: a suspend takes the rotor down');
  assert.equal(k.play('wheel'), 0, 'and nothing turns while suspended');
  k.suspend(false);
  assert.equal(k.play('wheel', { speed: 1 }), 1, 'a resumed room can be thrown again');
  k.stopAll();
  assert.equal(k.debug().wheel, false, 'stopAll takes it too');
  k.play('wheel', { speed: 1 });
  k.dispose();
  assert.equal(k.debug().wheel, false, 'dispose takes the rotor with the context');
});

/* ------------------------------------------------------------------ the kit on a context */
test('no AudioContext: every call is a no-op that still traces', () => {
  const k = createKit({ AudioContext: null });
  const saved = globalThis.AudioContext; globalThis.AudioContext = undefined;
  try {
    assert.equal(k.arm(), false);
    assert.equal(k.play('win', { tier: 'hero' }), 0);
    assert.equal(k.play('ambience'), 0);
    k.stop('win'); k.suspend(true); k.suspend(false); k.setMaster(0.5); k.dispose();
    assert.deepEqual(k.trace.map(t => t.name), ['win', 'ambience']);
    assert.equal(k.debug().has, false);
  } finally { globalThis.AudioContext = saved; }
});

test('the graph is built once, inside arm(), with the master at the default level', () => {
  const { AC, log } = makeMock();
  const k = createKit({ AudioContext: AC });
  assert.equal(k.debug().has, false);
  assert.equal(k.arm(), true);
  assert.equal(k.arm(), true);
  assert.equal(log.nodes.filter(n => n.kind === 'destination').length, 1, 'one context');
  assert.equal(k.master, DEFAULT_MASTER);
  const master = log.nodes.find(n => n.kind === 'gain');
  assert.equal(master.gain.value, DEFAULT_MASTER, 'master gain 0.8');
  k.setMaster(0.3);
  assert.equal(k.master, 0.3);
  assert.ok(master.gain.calls.some(c => c[0] === 'linearRampToValueAtTime' && near(c[1], 0.3)), 'ramped to the new level');
  k.setMaster(7); assert.equal(k.master, 1, 'clamped');
  k.setMaster(-1); assert.equal(k.master, 0);
  k.setMaster('x'); assert.equal(k.master, DEFAULT_MASTER, 'nonsense falls back');
  k.mute(true);
  assert.ok(master.gain.calls.some(c => c[0] === 'linearRampToValueAtTime' && c[1] === 0), 'mute ramps to 0');
});

test('a win schedules its score onto the graph, in order, at the context time', () => {
  const { AC, log } = makeMock();
  const k = createKit({ AudioContext: AC });
  k.arm(); log.ctx.currentTime = 10;
  const s = score('win', { tier: 'hero' });
  const before = log.sources.length;
  assert.equal(k.play('win', { tier: 'hero' }), s.notes.length, 'every note rendered');
  const made = log.sources.slice(before);
  assert.equal(made.length, s.notes.length);
  made.forEach((src, i) => {
    assert.ok(near(src.startedAt, 10 + s.notes[i].at), 'note ' + i + ' starts at its time');
    assert.ok(near(src.stopAt, 10 + s.notes[i].at + Math.max(0.005, s.notes[i].dur) + 0.02), 'and stops after its dur');
    if (s.notes[i].k === 'tone') assert.equal(src.frequency.calls[0][1], s.notes[i].hz);
  });
  assert.equal(k.debug().live, s.notes.length);
  assert.equal(k.play('win', { tier: 'small', at: 2 }), 2);
  assert.ok(near(log.sources[log.sources.length - 2].startedAt, 12), '`at` schedules ahead');
});

test('the ambience bed starts on play, loops until stop, and is idempotent', () => {
  const { AC, log } = makeMock();
  const k = createKit({ AudioContext: AC });
  k.arm();
  assert.equal(k.play('ambience'), 1);
  assert.deepEqual(k.debug().beds, ['ambience']);
  const before = log.started;
  assert.equal(k.play('ambience'), 1, 'already on: a second play changes nothing');
  assert.equal(log.started, before);
  const bedGain = log.nodes.filter(n => n.kind === 'gain').find(n => n.gain.calls.some(c => c[0] === 'exponentialRampToValueAtTime' && near(c[1], BED_LEVEL)));
  assert.ok(bedGain, 'the bed fades in to -24 dB');
  const stoppedBefore = log.stopped;
  k.stop('ambience');
  assert.deepEqual(k.debug().beds, []);
  assert.ok(log.stopped > stoppedBefore, 'its oscillators are stopped');
  assert.ok(bedGain.gain.calls.some(c => c[0] === 'exponentialRampToValueAtTime' && c[1] === 0.0001), 'faded out');
});

test('the spiral hum comes in and goes out on its own timer when given ms', async () => {
  const { AC } = makeMock();
  const k = createKit({ AudioContext: AC });
  k.arm();
  k.play('spiral', { ms: 10 });
  assert.deepEqual(k.debug().beds, ['spiral']);
  await new Promise(r => setTimeout(r, 30));
  assert.deepEqual(k.debug().beds, [], 'gone after ms');
  k.play('spiral'); k.stop('spiral');
  assert.deepEqual(k.debug().beds, []);
});

test('Law VI: suspend stops every voice and bed and suspends the context; resume brings the ambience back', () => {
  const { AC, log } = makeMock();
  const k = createKit({ AudioContext: AC });
  k.arm();
  k.play('ambience'); k.play('spiral');
  k.play('win', { tier: 'hero' }); k.play('ladder', { ms: 2000 }); k.play('riser', { ms: 2000 });
  const live = k.debug().live;
  assert.ok(live > 20);
  k.suspend(true);
  assert.equal(k.debug().live, 0, 'every one-shot is stopped');
  assert.deepEqual(k.debug().beds, [], 'every bed is stopped');
  assert.equal(log.suspends, 1);
  assert.equal(log.state, 'suspended');
  assert.ok(log.sources.every(s => s.stopAt != null), 'no source left running');
  assert.equal(k.play('win', { tier: 'mid' }), 0, 'nothing plays while suspended (still traced)');
  assert.equal(k.trace[k.trace.length - 1].name, 'win');
  k.suspend(true); assert.equal(log.suspends, 1, 'idempotent');
  k.suspend(false);
  assert.equal(log.resumes, 1);
  assert.deepEqual(k.debug().beds, ['ambience'], 'the ambience is back, the spiral is not');
  assert.equal(k.play('win', { tier: 'mid' }), 4);
});

test('stop(name) takes back only that cue, scheduled-ahead notes included', () => {
  const { AC } = makeMock();
  const k = createKit({ AudioContext: AC });
  k.arm();
  k.play('ladder', { ms: 3000 }); k.play('riser', { ms: 2000 });
  const all = k.debug().live;
  k.stop('ladder');
  assert.equal(k.debug().live, 2, 'the riser is still up');
  assert.ok(all > 2);
  k.stop('riser');
  assert.equal(k.debug().live, 0);
  k.stop('nothing-here');
});

test('THE ROLL on a context: one drum per reel, following its speed, down on its own stop frame', () => {
  const { AC } = makeMock();
  const k = createKit({ AudioContext: AC });
  k.arm();
  for (const v of REEL_VARIANTS) {
    assert.equal(k.play('reel', { reel: 0, variant: v, speed: 1 }), 1, v + ' starts a drum');
    assert.deepEqual(k.debug().rolls, ['reel:0'], v + ' is turning');
    assert.equal(k.play('reel', { reel: 0, variant: v, speed: 0.3 }), 1, 'a second play follows it, it does not stack');
    assert.deepEqual(k.debug().rolls, ['reel:0']);
    k.setRollSpeed(0, 0.2);
    k.stop('reel', { reel: 0 });
    assert.deepEqual(k.debug().rolls, [], v + ' comes down on its stop frame');
  }
  k.setRollSpeed(0, 0.5);   // no drum there: a no-op, never a throw
  for (const i of [0, 1, 2]) k.play('reel', { reel: i, variant: 'C', speed: 1 });
  assert.deepEqual(k.debug().rolls, ['reel:0', 'reel:1', 'reel:2'], 'three drums, one per reel');
  assert.ok(k.debug().live > 0, 'the tick train is scheduled a beat ahead');
  k.stop('reel', { reel: 1 });
  assert.deepEqual(k.debug().rolls, ['reel:0', 'reel:2'], 'one drum stops without touching the others');
  k.stop('reel');
  assert.deepEqual(k.debug().rolls, [], 'and stop with no reel takes them all');
  for (const i of [0, 1, 2]) k.play('reel', { reel: i, variant: 'D', speed: 1 });
  k.play('ambience');
  k.suspend(true);
  assert.deepEqual(k.debug().rolls, [], 'Law VI: a suspend takes every drum down');
  assert.equal(k.play('reel', { reel: 0 }), 0, 'and nothing turns while suspended');
  k.suspend(false);
  assert.equal(k.play('reel', { reel: 0, variant: 'B', speed: 1 }), 1, 'a resumed room can turn again');
  k.dispose();
  assert.deepEqual(k.debug().rolls, [], 'dispose takes the drums with the context');
});

test('no node leaks over 200 plays: every voice ends, every node disconnects', () => {
  const { AC, log } = makeMock();
  const k = createKit({ AudioContext: AC });
  k.arm();
  const graphNodes = log.nodes.length;   // the master, dry, send, delay, feedback, lowpass, destination
  const names = ['win', 'ladder', 'settle', 'clicker', 'word', 'chips', 'card', 'rattle', 'drop', 'clack', 'thud', 'token', 'riser', 'almost', 'tap', 'launch', 'lever', 'reelStop'];
  for (let i = 0; i < 200; i++) k.play(names[i % names.length], { tier: TIER_ORDER[i % 4], index: i % 3, n: 3 });
  assert.ok(k.debug().live > 0);
  for (const src of log.sources) { if (src.onended) src.onended(); }
  assert.equal(k.debug().live, 0, 'nothing live once the sources have ended');
  const voiceNodes = log.nodes.slice(graphNodes);
  assert.ok(voiceNodes.length > 400);
  assert.ok(voiceNodes.every(n => n.disconnected), 'every voice node disconnected');
  assert.ok(log.nodes.slice(0, graphNodes).every(n => !n.disconnected), 'the graph itself stays up');
});

test('dedupe: two lanes saying settle on the same frame make one settle; a beat later it plays again', () => {
  const { AC } = makeMock();
  let t = 1000;
  const k = createKit({ AudioContext: AC, now: () => t });
  k.arm();
  assert.equal(k.play('settle'), 3);
  assert.equal(k.play('settle'), 0, 'the second lane on the same frame');
  assert.equal(k.trace.filter(x => x.name === 'settle').length, 2, 'both traced');
  t += DEDUPE_MS.settle;
  assert.equal(k.play('settle'), 3);
  assert.equal(k.play('clicker'), 2);
  assert.equal(k.play('clicker'), 0);
  t += DEDUPE_MS.clicker;
  assert.equal(k.play('clicker'), 2);
  assert.equal(k.play('word'), 2); assert.equal(k.play('word'), 2, 'the word bed is not deduped (a chain)');
});

test('dispose closes the context and a later arm builds a fresh one', () => {
  const { AC, log } = makeMock();
  const k = createKit({ AudioContext: AC });
  k.arm(); k.play('ambience'); k.play('win', { tier: 'big' });
  k.dispose();
  assert.equal(log.closes, 1);
  assert.equal(k.debug().has, false);
  assert.equal(k.debug().live, 0);
  assert.equal(k.play('win'), 0, 'nothing before the next gesture');
  assert.equal(k.arm(), true);
  assert.equal(log.nodes.filter(n => n.kind === 'destination').length, 2, 'a second context');
  assert.equal(k.play('win'), 2);
});

test('trim: Calm turns the whole room down without touching the master setting', () => {
  const { AC, log } = makeMock();
  const k = createKit({ AudioContext: AC, master: 0.5 });
  k.arm();
  const master = log.nodes.find(n => n.kind === 'gain');
  k.setTrim(0.5);
  assert.ok(master.gain.calls.some(c => c[0] === 'linearRampToValueAtTime' && near(c[1], 0.25)));
  assert.equal(k.master, 0.5);
  assert.equal(k.debug().trim, 0.5);
});
