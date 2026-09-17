/* buses.test.mjs - the three buses under the one master: the table, the routing, the mix. node --test, no audio. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createKit, CUES, CUE_BUS, BUSES, DEFAULT_BUS, SEND_LEVEL, busOf, BEDS, ROLLS, BED_LEVEL, DEFAULT_MASTER } from '../kit.js';

/* ------------------------------------------------------------------ a mocked AudioContext that remembers
 * what connected to what: which bus a voice hangs on is the whole point of this file. */
function makeMock() {
  const log = { nodes: [], sources: [], state: 'running' };
  const param = (v = 0) => {
    const p = { value: v, calls: [] };
    for (const m of ['setValueAtTime', 'exponentialRampToValueAtTime', 'linearRampToValueAtTime', 'cancelScheduledValues']) {
      p[m] = (...a) => { p.calls.push([m, ...a]); if (m === 'setValueAtTime') p.value = a[0]; return p; };
    }
    return p;
  };
  class Node {
    constructor(kind) { this.kind = kind; this.to = []; log.nodes.push(this); }
    connect(target) { this.to.push(target); return this; }
    disconnect() { this.to = []; }
  }
  class Source extends Node {
    constructor(kind) { super(kind); this.onended = null; log.sources.push(this); }
    start() { /* no audio */ }
    stop() { /* no audio */ }
  }
  class AC {
    constructor() { this.sampleRate = 48000; this.currentTime = 0; this.destination = new Node('destination'); log.ctx = this; log.state = 'running'; }
    get state() { return log.state; }
    createGain() { const n = new Node('gain'); n.gain = param(1); return n; }
    createOscillator() { const n = new Source('osc'); n.type = 'sine'; n.frequency = param(440); return n; }
    createBufferSource() { const n = new Source('buffer'); n.buffer = null; n.loop = false; n.playbackRate = param(1); return n; }
    createBiquadFilter() { const n = new Node('filter'); n.type = 'lowpass'; n.frequency = param(350); n.Q = param(1); return n; }
    createDelay() { const n = new Node('delay'); n.delayTime = param(0); return n; }
    createBuffer(ch, len, rate) { return { length: len, sampleRate: rate, getChannelData: () => new Float32Array(Math.min(len, 64)) }; }
    suspend() { log.state = 'suspended'; return Promise.resolve(); }
    resume() { log.state = 'running'; return Promise.resolve(); }
    close() { log.state = 'closed'; return Promise.resolve(); }
  }
  return { AC, log };
}
/** The graph the kit built, from node `from` on (a re-arm builds a second one into the same log). The legs and
 *  the taps come back in BUSES order, which is the order graph() builds them; the setter test pins that. */
function graphOf(log, from = 0) {
  const nodes = log.nodes.slice(from), gains = nodes.filter(n => n.kind === 'gain');
  const master = gains[0], delay = nodes.find(n => n.kind === 'delay');
  const lp = delay.to[0], fb = lp.to.find(n => n.kind === 'gain' && n.to.includes(delay));
  return {
    master, delay,
    legs: gains.filter(n => n !== master && n.to.includes(master)),
    taps: gains.filter(n => n !== fb && n.to.includes(delay)),
  };
}
const near = (a, b, eps = 1e-9) => Math.abs(a - b) < eps;
const ramps = p => p.calls.filter(c => c[0] === 'linearRampToValueAtTime').map(c => c[1]);
const SETTER = Object.freeze({ sub: 'setSub', sfx: 'setSfx', bed: 'setBed' });
const armed = (opts = {}) => { const { AC, log } = makeMock(); const k = createKit({ AudioContext: AC, ...opts }); k.arm(); return { k, log }; };

/* ------------------------------------------------------------------ the table */
test('every cue is on a bus, and the table names no cue the kit does not have', () => {
  for (const name of CUES) {
    assert.ok(CUE_BUS[name], name + ' is on no bus');
    assert.ok(BUSES.includes(CUE_BUS[name]), name + ' names a bus that is not there: ' + CUE_BUS[name]);
    assert.equal(busOf(name), CUE_BUS[name]);
  }
  assert.deepEqual(Object.keys(CUE_BUS).sort(), [...CUES].sort(), 'the table is exactly the cue list, no more and no less');
  assert.equal(busOf('nope'), 'sfx', 'a name the kit never scores still has a home');
});

test('the taxonomy: the whisper and the breath are subliminal, the beds are the beds, the floor is everything else', () => {
  assert.deepEqual(CUES.filter(n => CUE_BUS[n] === 'sub'), ['word', 'breath'], 'the subliminal lane is those two and no others');
  assert.deepEqual(CUES.filter(n => CUE_BUS[n] === 'bed'), [...BEDS], 'the beds are the two loops');
  for (const n of ROLLS) assert.equal(CUE_BUS[n], 'sfx', n + ' is a drum, not a bed');
  const floor = CUES.filter(n => CUE_BUS[n] === 'sfx');
  for (const n of ['win', 'ladder', 'settle', 'sigh', 'almost', 'riser', 'ticks', 'tick', 'chips', 'clicker', 'whir', 'emi-bleep', 'card-slide', 'prize-drop'])
    assert.ok(floor.includes(n), n + ' belongs to the floor');
  assert.equal(floor.length, CUES.length - 4, 'everything but the two whispers and the two beds');
});

/* ------------------------------------------------------------------ the graph */
test('the graph is one master over three buses, each with its own leg and its own tap into the one room', () => {
  const { k, log } = armed();
  const { master, legs, taps } = graphOf(log);
  assert.equal(master.gain.value, DEFAULT_MASTER, 'the master is still the first gain, at the default');
  assert.deepEqual(master.to, [log.ctx.destination], 'and it is still the only thing on the destination');
  assert.equal(legs.length, 3, 'three legs under the master');
  assert.equal(taps.length, 3, 'three taps into the room');
  assert.equal(log.nodes.filter(n => n.kind === 'delay').length, 1, 'ONE room, not one delay line per bus');
  assert.deepEqual(k.debug().bus, { ...DEFAULT_BUS }, 'and nothing has been mixed yet');
  k.dispose();
});

test('a kit nobody mixed is the old graph exactly: a unity leg, a 0.22 tap, the bed still at BED_LEVEL', () => {
  const { k, log } = armed();
  const { legs, taps } = graphOf(log);
  for (const leg of legs) assert.equal(leg.gain.value, 1, 'the one dry leg this replaces was 1');
  for (const tap of taps) assert.equal(tap.gain.value, SEND_LEVEL, 'and the one send was 0.22');
  const at = log.nodes.length;
  k.play('ambience');
  const bed = log.nodes.slice(at).find(n => n.kind === 'gain' && n.gain.calls.some(c => c[0] === 'exponentialRampToValueAtTime' && near(c[1], BED_LEVEL)));
  assert.ok(bed, 'the bed still fades to BED_LEVEL: the bus is a multiplier over it, never a replacement for it');
  assert.equal(k.debug().bus.bed, 1, 'and the multiplier is 1');
  k.stop('ambience'); k.dispose();
});

/* ------------------------------------------------------------------ the routing */
test('a one-shot hangs on the bus its cue names, and touches no other', () => {
  const { k, log } = armed();
  const g = graphOf(log), leg = key => g.legs[BUSES.indexOf(key)];
  for (const [cue, key] of [['word', 'sub'], ['breath', 'sub'], ['lever', 'sfx'], ['win', 'sfx'], ['clicker', 'sfx'], ['settle', 'sfx']]) {
    const at = log.nodes.length;
    k.play(cue);
    const hung = log.nodes.slice(at).filter(n => n.kind === 'gain');
    assert.ok(hung.length > 0, cue + ' scheduled voices');
    for (const v of hung) {
      assert.ok(v.to.includes(leg(key)), cue + ' hangs on ' + key);
      for (const other of BUSES.filter(b => b !== key)) assert.ok(!v.to.includes(leg(other)), cue + ' never reaches ' + other);
    }
    k.stop(cue);
  }
  k.dispose();
});

test('the beds hang on the bed bus; the drums and the rotor stay on the floor', () => {
  const { k, log } = armed();
  const g = graphOf(log), leg = key => g.legs[BUSES.indexOf(key)];
  const hangs = (from, key) => log.nodes.slice(from).filter(n => n.to.includes(leg(key))).length;
  let at = log.nodes.length;
  k.play('ambience');
  assert.equal(hangs(at, 'bed'), 1, 'one bed, on the bed bus');
  assert.equal(hangs(at, 'sfx') + hangs(at, 'sub'), 0, 'and nowhere else');
  at = log.nodes.length;
  k.play('spiral');
  assert.equal(hangs(at, 'bed'), 1, 'the spiral is a bed too');
  at = log.nodes.length;
  k.play('reel', { reel: 0, variant: 'D' });   // D purrs under its ticks, so the drum has a bed gain of its own
  assert.ok(hangs(at, 'sfx') >= 1, 'a drum is on the floor');
  assert.equal(hangs(at, 'bed'), 0, 'a drum is not a bed');
  at = log.nodes.length;
  k.play('wheel');
  assert.ok(hangs(at, 'sfx') >= 1, 'and so is the rotor');
  assert.equal(hangs(at, 'bed') + hangs(at, 'sub'), 0);
  k.stop('reel'); k.stop('wheel'); k.stop('ambience'); k.stop('spiral'); k.dispose();
});

test('a foley sample lands on the floor, dry, exactly where the scored cue it replaces lands', async () => {
  const { AC, log } = makeMock();
  const k = createKit({ AudioContext: AC, loadSample: async () => ({ duration: 0.5 }) });
  k.arm();
  await new Promise(resolve => setImmediate(resolve));
  const g = graphOf(log), at = log.nodes.length;
  k.play('card-slide');
  const hung = log.nodes.slice(at).filter(n => n.kind === 'gain');
  assert.equal(hung.length, 1, 'one sample voice');
  assert.deepEqual(hung[0].to, [g.legs[BUSES.indexOf('sfx')]], 'onto the floor leg, and never into the room');
  k.dispose();
});

test('the room is tapped per bus: a wet note takes its own bus tap, a dry note takes none', () => {
  const { k, log } = armed();
  const g = graphOf(log), tap = key => g.taps[BUSES.indexOf(key)];
  let at = log.nodes.length;
  k.play('win', { tier: 'small' });            // the win rings: its notes are wet
  const wet = log.nodes.slice(at).filter(n => n.kind === 'gain');
  assert.ok(wet.length > 0);
  for (const v of wet) {
    assert.ok(v.to.includes(tap('sfx')), 'a wet note goes into the room through the floor tap');
    assert.ok(!v.to.includes(tap('sub')) && !v.to.includes(tap('bed')), 'and never through another bus tap');
  }
  k.stop('win');
  at = log.nodes.length;
  k.play('tap');                               // the croupier's tap is dry: true in every note
  for (const v of log.nodes.slice(at).filter(n => n.kind === 'gain')) {
    assert.ok(g.taps.every(t => !v.to.includes(t)), 'a dry note never reaches the room at all');
  }
  k.dispose();
});

/* ------------------------------------------------------------------ the mix */
test('setSub, setSfx and setBed each move their own leg and its tap, and nothing else', () => {
  for (const key of BUSES) {
    const { k, log } = armed();
    const { master, legs, taps } = graphOf(log), i = BUSES.indexOf(key);
    k[SETTER[key]](0.4);
    assert.deepEqual(ramps(legs[i].gain), [0.4], key + ': the leg ramps to 0.4');
    assert.equal(ramps(taps[i].gain).length, 1, key + ': the tap moves with it');
    assert.ok(near(ramps(taps[i].gain)[0], SEND_LEVEL * 0.4), key + ': the tap is 0.22 of the leg, so the tail goes quiet with it');
    assert.equal(k.debug().bus[key], 0.4, key + ' is where it was set');
    legs.forEach((leg, j) => { if (j !== i) assert.deepEqual(ramps(leg.gain), [], BUSES[j] + ' never moved'); });
    taps.forEach((t, j) => { if (j !== i) assert.deepEqual(ramps(t.gain), [], BUSES[j] + "'s tap never moved"); });
    assert.deepEqual(ramps(master.gain), [], 'and the master never moved');
    k.dispose();
  }
});

test('a bus level is 0..1, and nonsense falls back to its default', () => {
  const { k } = armed();
  k.setSfx(7); assert.equal(k.debug().bus.sfx, 1, 'clamped');
  k.setSfx(-1); assert.equal(k.debug().bus.sfx, 0);
  k.setSfx('x'); assert.equal(k.debug().bus.sfx, DEFAULT_BUS.sfx, 'nonsense falls back');
  k.setSub(0.25); k.setBed(0.5);
  assert.deepEqual(k.debug().bus, { sub: 0.25, sfx: 1, bed: 0.5 }, 'debug() reports all three');
  k.dispose();
});

test('the master, the trim and the mute still scale all three buses from over the top', () => {
  const { k, log } = armed({ master: 0.5 });
  const { master, legs, taps } = graphOf(log);
  k.setSub(0.5); k.setSfx(0.25); k.setBed(0.75);
  for (const leg of legs) assert.deepEqual(leg.to, [master], 'every bus hangs under the master');
  const moved = [...legs, ...taps].map(n => n.gain.calls.length);
  k.setTrim(0.6);
  assert.ok(ramps(master.gain).some(v => near(v, 0.3)), 'the trim moves the master');
  k.mute(true);
  assert.equal(master.gain.calls.at(-1)[1], 0, 'the mute takes the master to 0, so all three go with it');
  k.mute(false); k.setMaster(0.9);
  assert.ok(near(ramps(master.gain).at(-1), 0.9 * 0.6), 'and the master ramps under the trim it kept');
  assert.deepEqual([...legs, ...taps].map(n => n.gain.calls.length), moved, 'not one bus gain was touched: the master scales all three by where it sits');
  assert.deepEqual(k.debug().bus, { sub: 0.5, sfx: 0.25, bed: 0.75 }, 'and the mix is where the room left it');
  k.dispose();
});

test('the mix survives the context: dispose and arm again builds the buses where the room left them', () => {
  const { k, log } = armed();
  k.setSub(0.2); k.setSfx(0.3); k.setBed(0.4);
  k.dispose();
  const from = log.nodes.length;
  k.arm();
  const { legs, taps } = graphOf(log, from);
  assert.deepEqual(legs.map(l => l.gain.value), [0.2, 0.3, 0.4], 'the new legs come up already mixed');
  assert.deepEqual(taps.map(t => Number((t.gain.value / SEND_LEVEL).toFixed(6))), [0.2, 0.3, 0.4], 'and their taps with them');
  assert.deepEqual(k.debug().bus, { sub: 0.2, sfx: 0.3, bed: 0.4 });
  k.dispose();
});

test('a bus at 0 silences its own lane and leaves the others playing', () => {
  const { k, log } = armed();
  const g = graphOf(log), i = BUSES.indexOf('sub');
  k.setSub(0);
  assert.deepEqual(ramps(g.legs[i].gain), [0], 'the subliminal leg is shut');
  assert.deepEqual(ramps(g.taps[i].gain), [0], 'and so is its tail');
  assert.equal(k.play('word'), 2, 'the cue still schedules: a bus is a level, never a mute for the lane');
  assert.equal(k.debug().bus.sfx, 1, 'the floor is untouched');
  assert.equal(k.debug().bus.bed, 1, 'and so are the beds');
  k.dispose();
});
