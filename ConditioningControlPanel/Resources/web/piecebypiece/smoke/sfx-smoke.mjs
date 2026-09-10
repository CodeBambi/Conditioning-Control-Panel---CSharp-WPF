/* ============================================================================
 * smoke/sfx-smoke.mjs - every cue schedules, in node, with a fake AudioContext.
 *
 * Builds the real bus and the real sfx module around a recording stand-in for
 * the Web Audio graph, walks a game through the bus (grab, hover, refused
 * drop, land, capture, check, promotion, low clock, mate) and checks that each
 * cue logged and started at least one node; then checks the tab-hidden mute,
 * the volume setting, and that dispose stops the check pulse timer.
 *
 *   node smoke/sfx-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { createBus } from '../game/events.js';
import { createSfx, TUNING } from '../audio/sfx.js';

class Param {
  constructor(v) { this.value = v; }
  setValueAtTime(v) { this.value = v; return this; }
  linearRampToValueAtTime() { return this; }
  exponentialRampToValueAtTime(v) { if (!(v > 0)) throw new Error('exponential ramp to ' + v); return this; }
  setTargetAtTime(v) { this.value = v; return this; }
}
class FakeNode {
  constructor(ctx, kind) { this.ctx = ctx; this.kind = kind; }
  connect(to) { this.ctx.wires.push([this.kind, to && to.kind || 'destination']); return to; }
  start(t) { this.ctx.started.push({ kind: this.kind, at: t }); }
  stop() { }
}
class FakeContext {
  constructor() {
    this.state = 'suspended'; this.currentTime = 0; this.sampleRate = 48000;
    this.started = []; this.wires = []; this.destination = { kind: 'destination' };
  }
  createGain() { const n = new FakeNode(this, 'gain'); n.gain = new Param(1); return n; }
  createOscillator() { const n = new FakeNode(this, 'osc'); n.frequency = new Param(440); n.type = 'sine'; return n; }
  createBiquadFilter() { const n = new FakeNode(this, 'filter'); n.frequency = new Param(1000); n.type = 'lowpass'; return n; }
  createBufferSource() { return new FakeNode(this, 'noise'); }
  createBuffer(ch, len) { return { getChannelData: () => new Float32Array(len) }; }
  resume() { this.state = 'running'; return Promise.resolve(); }
  close() { this.state = 'closed'; return Promise.resolve(); }
}

function fakeDoc() {
  const listeners = new Map();
  return {
    hidden: false,
    addEventListener(type, fn) { if (!listeners.has(type)) listeners.set(type, new Set()); listeners.get(type).add(fn); },
    removeEventListener(type, fn) { listeners.get(type)?.delete(fn); },
    fire(type) { for (const fn of listeners.get(type) || []) fn({ type }); },
  };
}

const problems = [];
const expect = (ok, msg) => { if (!ok) problems.push(msg); console.log((ok ? 'ok   ' : 'FAIL ') + msg); };

const bus = createBus();
const doc = fakeDoc();
const win = { PBP: { settings: { sfxVolume: 0.6 } } };
let fake = null;
const heldMan = { position: { x: 0.5, y: 0.6, z: 0.5 }, userData: { held: true, type: 'p', side: 'w' } };
const group = { children: [{ userData: {} }, heldMan] };
const squareOf = (x, z) => 'abcdefgh'[Math.round(x + 3.5)] + (Math.round(3.5 - z) + 1);
let history = [];
const game = {
  legalTargets: (sq) => (sq === 'e2' ? ['e3', 'e4'] : []),
  rules: { chess: { history: () => history } },
};

const sfx = createSfx({
  bus, game, group, squareOf, doc, win,
  context: () => { fake = new FakeContext(); return fake; },
});
const names = () => sfx.log().map((e) => e.name);
const startedSince = (n) => fake.started.length - n;

// a grab before any gesture still makes the context (suspended) and logs
let n0 = 0;
bus.emit('grab', { square: 'e2', piece: 'p' });
expect(fake && names().includes('grab'), 'grab pops (context made lazily on the first cue)');
expect(fake.state === 'suspended', 'context starts suspended before a gesture');
doc.fire('pointerdown');
expect(fake.state === 'running', 'the first pointerdown resumes it');

// hover: a tick per legal square passed over, silence elsewhere
heldMan.position.x = 0.5; heldMan.position.z = 1.5;      // e3
bus.emit('dragmove', {}); bus.emit('dragmove', {});
heldMan.position.z = 0.5;                                 // e4
bus.emit('dragmove', {});
heldMan.position.x = 1.5;                                 // f4, not legal
bus.emit('dragmove', {});
expect(names().filter((x) => x === 'tick').length === 2, 'two legal squares crossed, two ticks, no tick on f4 or on a repeat');

n0 = fake.started.length;
bus.emit('drop', { ok: false });
expect(names().at(-1) === 'boing' && startedSince(n0) > 0, 'refused drop boings');
bus.emit('land', { square: 'e2', piece: 'p', height: 0.55, refused: true });
expect(names().at(-1) === 'boing', 'a refused landing makes no thud');

n0 = fake.started.length;
bus.emit('land', { square: 'e4', piece: 'p', height: 0.55, capture: false });
expect(names().at(-1) === 'land' && startedSince(n0) >= 2, 'a pawn lands: tone plus tap');
const pawnHz = fake.started.length;
bus.emit('land', { square: 'd5', piece: 'k', height: 1.25, capture: true });
expect(names().at(-1) === 'capture' && fake.started.length - pawnHz > 2, 'a capture: thud plus squelch');

// check pulses through the same move's turn and stops on the next
bus.emit('check', { side: 'b' });
bus.emit('turn', { side: 'b', ply: 3 });
expect(sfx.state().pulsing && names().includes('check'), 'check pulses, and the turn that follows it keeps the pulse');
bus.emit('turn', { side: 'w', ply: 4 });
expect(!sfx.state().pulsing, 'the next turn stops the pulse');

history = [{ from: 'e7', to: 'e8', promotion: 'q' }];
bus.emit('turn', { side: 'b', ply: 5 });
expect(names().at(-1) === 'promote', 'a promotion chimes on its turn');
history = [];

// the clock: one tick a second under 30 s, sharper under 10 s, only the active side
n0 = names().length;
bus.emit('clock', { w: 45000, b: 29500, total: 900000, active: 'w' });
bus.emit('clock', { w: 29800, b: 29500, total: 900000, active: 'w' });
bus.emit('clock', { w: 29600, b: 29500, total: 900000, active: 'w' });   // same second
bus.emit('clock', { w: 28900, b: 29500, total: 900000, active: 'w' });
bus.emit('clock', { w: 9800, b: 29500, total: 900000, active: 'w' });
const ticks = sfx.log().slice(n0).filter((e) => e.name === 'clock');
expect(ticks.length === 3 && !ticks[0].opts.sharp && ticks[2].opts.sharp, 'three seconds under 30 s tick three times, the one under 10 s sharp');

bus.emit('gameover', { result: 'checkmate', winner: 'w' });
expect(names().at(-1) === 'mate', 'checkmate stings');
n0 = names().length;
bus.emit('clock', { w: 5000, b: 29500, total: 900000, active: 'w' });
expect(names().length === n0, 'no clock tick once the game is over');
sfx.play('draw');
expect(names().at(-1) === 'draw', 'draw sting plays by hand');
expect(sfx.play('cardOpen') && sfx.play('cardClose'), 'card whooshes play by hand');

doc.hidden = true; doc.fire('visibilitychange');
expect(sfx.state().volume === 0 && !sfx.play('grab'), 'hidden tab: master to 0 and nothing schedules');
doc.hidden = false; doc.fire('visibilitychange');
expect(sfx.state().volume === 0.6, 'visible again: master back to the setting');
win.PBP.settings.sfxVolume = 0.25;
sfx.play('tick');
expect(Math.abs(sfx.state().volume - 0.25) < 1e-9, 'settings.sfxVolume is read on play');
win.PBP.settings.sfxVolume = 0;
expect(!sfx.play('tick'), 'volume 0 schedules nothing');

expect(Object.isFrozen(TUNING), 'TUNING is frozen');
expect(fake.wires.some((w) => w[0] === 'gain' && w[1] === 'destination'), 'the master reaches the destination');

bus.emit('check', { side: 'w' });
sfx.dispose();
expect(!sfx.state().pulsing && fake.state === 'closed', 'dispose stops the pulse and closes the context');

if (problems.length) { console.log('\n' + problems.length + ' problem(s)'); process.exit(1); }
console.log('\nsfx smoke: every cue schedules');
