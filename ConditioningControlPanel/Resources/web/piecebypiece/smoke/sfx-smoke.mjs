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
import { createCrowd } from '../audio/crowd.js';
import { Chess } from '../vendor/chess.js';
import { createSfx, TUNING, hitCues, replayVoice, slamRise } from '../audio/sfx.js';

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
  disconnect() { this.ctx.disconnected++; }
  start(t) { this.startAt = t; this.ctx.started.push({ kind: this.kind, at: t, node: this }); }
  stop(t) { this.stopAt = t; }
}
class FakeContext {
  constructor() {
    this.state = 'suspended'; this.currentTime = 0; this.sampleRate = 48000;
    this.started = []; this.wires = []; this.sources = []; this.disconnected = 0; this.destination = { kind: 'destination' };
  }
  createGain() { const n = new FakeNode(this, 'gain'); n.gain = new Param(1); return n; }
  createOscillator() { const n = new FakeNode(this, 'osc'); n.frequency = new Param(440); n.detune = new Param(0); n.type = 'sine'; this.sources.push(n); return n; }
  createBiquadFilter() { const n = new FakeNode(this, 'filter'); n.frequency = new Param(1000); n.type = 'lowpass'; return n; }
  createBufferSource() { const n = new FakeNode(this, 'noise'); n.detune = new Param(0); this.sources.push(n); return n; }
  createStereoPanner() { const n = new FakeNode(this, 'pan'); n.pan = new Param(0); return n; }
  createDelay() { const n = new FakeNode(this, 'delay'); n.delayTime = new Param(0); return n; }
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

// the room: a delay beside the master whose wet follows the meter, and a drift
expect(fake.wires.some((w) => w[0] === 'gain' && w[1] === 'delay') && fake.wires.some((w) => w[0] === 'delay' && w[1] === 'gain'), 'the master feeds the delay and the delay comes back through a gain');
const lastSource = () => fake.sources.at(-1);
sfx.setMeter(0.2); sfx.play('tick');
expect(sfx.state().wet === 0 && sfx.state().drift === 0 && lastSource().detune.value === 0, 'meter 0.2: dry, in tune');
sfx.setMeter(0.55); sfx.play('tick');
expect(Math.abs(sfx.state().wet - 0.14) < 1e-6 && sfx.state().drift === 0, 'meter 0.55: some wet (0.14), still in tune');
sfx.setMeter(1.0); sfx.play('tick');
expect(Math.abs(sfx.state().wet - 0.35) < 1e-9 && sfx.state().drift === -200 && lastSource().detune.value === -200, 'meter 1.0: wet 0.35 and every new voice two semitones flat');
sfx.play('whisper');
expect(names().at(-1) === 'whisper' && lastSource().kind === 'noise' && lastSource().detune.value === -200, 'the whisper is a breath of noise, and it drifts too');
bus.emit('clock', { w: 20000, b: 60000, total: 900000, active: 'w' });
sfx.play('tick');
expect(sfx.state().lowClock && sfx.state().wet === 0 && lastSource().detune.value === 0, 'the mover under 30 s: the room goes dry and the ticks stay in tune');
bus.emit('clock', { w: 60000, b: 20000, total: 900000, active: 'w' });
expect(!sfx.state().lowClock && Math.abs(sfx.state().wet - 0.35) < 1e-9, 'the other side low does not count; the room comes back');
win.PBP.settings.reducedMotion = true; sfx.setMeter(1.0);
expect(sfx.state().wet === 0 && sfx.state().drift === 0, 'reduced motion: dry and in tune at meter 1');
win.PBP.settings.reducedMotion = false; sfx.setMeter(0);

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

for (const name of ['crowdApplause','crowdCheer','crowdBoo','hooves','neigh','stomp','headbutt','whip','sweep','spin','breakdance','charge','launch']) {
  const before = fake.started.length;
  expect(sfx.play(name) && fake.started.length > before, name + ' schedules its sound');
}

doc.hidden = true; doc.fire('visibilitychange');
expect(sfx.state().volume === 0 && !sfx.play('grab'), 'hidden tab: master to 0 and nothing schedules');
doc.hidden = false; doc.fire('visibilitychange');
expect(sfx.state().volume === 0.6, 'visible again: master back to the setting');
win.PBP.settings.sfxVolume = 0.25;
sfx.play('tick');
expect(Math.abs(sfx.state().volume - 0.25) < 1e-9, 'settings.sfxVolume is read on play');
win.PBP.settings.sfxVolume = 0;
expect(!sfx.play('crowdBoo') && !sfx.play('crowdCheer') && !sfx.play('tick') && !sfx.play('neigh') && !sfx.play('launch'), 'volume 0 silences new capture sounds too');

expect(Object.isFrozen(TUNING), 'TUNING is frozen');
expect(fake.wires.some((w) => w[0] === 'gain' && w[1] === 'destination'), 'the master reaches the destination');

bus.emit('check', { side: 'w' });
sfx.dispose();
expect(!sfx.state().pulsing && fake.state === 'closed', 'dispose stops the pulse and closes the context');


// Completed exchange evidence, with a deterministic clock and delayed cue scheduler.
{
  const events = createBus(), chess = new Chess('6k1/8/5n2/8/8/8/8/3Q2K1 w - - 0 1');
  let time = 0, pending = null, audible = true, settled = true; const crowdLog = [];
  const audience = createCrowd({ bus: events, game: { rules: { chess } }, now: () => time,
    play: name => { crowdLog.push(name); return true; }, canPlay: () => audible, settled: () => settled,
    later: fn => { pending = fn; return 1; }, clear: () => { pending = null; } });
  const move = san => { time += 2000; chess.move(san); events.emit('turn'); };
  const flush = () => { time += 600; const fn = pending; pending = null; fn?.(); };
  move('Qh5'); move('Nxh5'); flush();
  expect(crowdLog.length === 0, 'crowd waits for a reply before judging a heavy loss');
  move('Kg2'); flush();
  expect(crowdLog.at(-1) === 'crowdBoo', 'safe queen walked into capture and a quiet reply confirms the loss');
  chess.load('6k1/8/5n2/8/5N2/8/8/3Q2K1 w - - 0 1'); events.emit('local'); crowdLog.length = 0;
  move('Qh5'); move('Nxh5'); move('Nxh5'); flush();
  expect(!crowdLog.includes('crowdBoo'), 'an actual recapture suppresses the negative reaction');
  chess.load('6k1/8/5n2/8/8/8/8/3Q2K1 w - - 0 1'); events.emit('local'); crowdLog.length = 0;
  for (const san of ['Qh5', 'Nxh5', 'Kg2']) { time += 10; chess.move(san); events.emit('turn'); }
  flush(); expect(crowdLog.length === 0, 'bulk catch-up cannot stack audience reactions');
  events.emit('gameover', { result: 'checkmate' }); time += 1500; settled = false; flush();
  expect(crowdLog.length === 0, 'audience waits for the visible capture to settle');
  settled = true; flush();
  expect(crowdLog.at(-1) === 'crowdCheer', 'checkmate earns a finish cheer');
  events.emit('gameover', { result: 'checkmate' }); audible = false; audience.update(); flush();
  expect(crowdLog.length === 1, 'menu, replay or mute cancels a pending crowd cue');
  audience.dispose();
}

// The capture replay: board/director.js beats -> the replay cues, in order.
{
  const rbus = createBus(), rdoc = fakeDoc(), rwin = { PBP: { settings: { sfxVolume: 0.6 } } };
  let rf = null;
  const rsfx = createSfx({ bus: rbus, doc: rdoc, win: rwin, context: () => { rf = new FakeContext(); return rf; } });
  rsfx.wake(); rdoc.fire('pointerdown');
  const rnames = () => rsfx.log().map((e) => e.name);
  // what one beat scheduled: the sources it started, with their first pitch and length
  const during = (fn) => { const n = rf.started.length; fn(); return rf.started.slice(n).map((s) => ({ kind: s.node.kind, hz: s.node.frequency?.value, sec: s.node.stopAt - s.node.startAt })); };
  const oscHz = (made) => made.filter((m) => m.kind === 'osc').map((m) => m.hz);
  const longest = (made) => Math.max(...made.map((m) => m.sec));

  expect(JSON.stringify(hitCues({ piece: 'r', sound: 'launch', manner: 'signature' })) === '["rebound","launch"]', 'hitCues: a rook fling is its rebound then its launch, as live');
  expect(JSON.stringify(hitCues({ piece: 'b' })) === '["rebound","whip"]', 'hitCues: a bishop whip with no sound is rebound + whip');
  expect(JSON.stringify(hitCues({ piece: 'p', manner: 'signature' })) === '["stomp"]', 'hitCues: a pawn signature stomps');
  expect(JSON.stringify(hitCues({ piece: 'n', sound: 'kick' })) === '["rebound","kick"]', 'hitCues: a kick rebounds');
  expect(replayVoice(9) === replayVoice(2) && replayVoice(-1) === replayVoice(0), 'replayVoice clamps to the table');
  expect(slamRise(1) > slamRise(0) && slamRise(2) > slamRise(1), 'slams rise panel by panel');

  // the live strike, for comparison with its replay
  const hit = { piece: 'r', sound: 'launch', impact: 'fling', victim: 'p', height: 0.8, manner: 'signature' };
  let n0 = rnames().length;
  const live = during(() => rbus.emit('hit', hit));
  expect(rnames().slice(n0).join() === 'rebound,launch', 'the live hit still plays rebound then launch');

  n0 = rnames().length;
  const shown = during(() => rbus.emit('replay-show', { layout: 'trio', n: 3, hit }));
  expect(shown.length >= 4, 'replay-show: the rewind and the riser schedule (' + shown.length + ' voices)');
  expect(rsfx.state().ducked && Math.abs(rsfx.state().beds - TUNING.replay.duck) < 1e-9, 'replay-show ducks the crowd/trance beds to ' + TUNING.replay.duck);
  const slams = [0, 1, 2].map((i) => during(() => rbus.emit('replay-panel-in', { i, n: 3, layout: 'trio' })));
  expect(slams.every((m) => m.length >= 3), 'every replay-panel-in slams (thunk, snap, paper)');
  expect(oscHz(slams[0])[0] < oscHz(slams[1])[0] && oscHz(slams[1])[0] < oscHz(slams[2])[0], 'each slam is pitched higher than the last');
  const hits = [0, 1, 2].map((i) => during(() => rbus.emit('replay-panel-hit', { i, n: 3, layout: 'trio', hit })));
  expect(hits.every((m) => m.length >= live.length), 'every replay-panel-hit replays the whole original sound');
  const liveTop = Math.max(...oscHz(live)), top = hits.map((m) => Math.max(...oscHz(m)));
  expect(top.every((hz) => hz < liveTop * 0.8), 'a replayed hit is pitched well below the live one (' + liveTop.toFixed(0) + ' Hz live, ' + top.map((h) => h.toFixed(0)).join('/') + ' replayed)');
  expect(top[0] < top[1] && top[1] < top[2], 'each panel replays it a little differently (lowest first)');
  expect(longest(hits[0]) > longest(live) * 1.8, 'panel 0 is slow motion: its longest voice ' + longest(hits[0]).toFixed(2) + ' s vs ' + longest(live).toFixed(2) + ' s live');
  const sub = (m) => m.some((v) => v.kind === 'osc' && v.hz === TUNING.replay.sub.hz);
  expect(sub(hits[0]) && !sub(hits[1]) && !sub(hits[2]), 'the sub drop comes under the first hit only');
  const hitLog = rsfx.log().filter((e) => e.name === 'replayHit');
  expect(hitLog.length === 3 && hitLog.every((e) => e.cues.join() === 'rebound,launch'), 'the log names the original cues each replayed hit played');
  const out = during(() => rbus.emit('replay-exit', { layout: 'trio', skipped: false }));
  expect(out.length >= 2, 'replay-exit whooshes out');
  rbus.emit('replay-done', {});
  expect(!rsfx.state().ducked && rsfx.state().beds === 1, 'replay-done brings the beds back up');
  expect(rnames().slice(n0).join() === 'replayIn,replaySlam,replaySlam,replaySlam,replayHit,replayHit,replayHit,replayOut', 'the beats trigger the cues in order: ' + rnames().slice(n0).join());
  expect(rf.wires.some((w) => w[0] === 'delay' && w[1] === 'filter'), 'the replay has its own dark room tail');

  // a skip: tape stop instead of the whoosh, and the next replay still sounds
  rbus.emit('replay-show', { layout: 'corner', n: 1, hit: null });
  rbus.emit('replay-panel-in', { i: 0, n: 1, layout: 'corner' });
  const stopped = during(() => rbus.emit('replay-exit', { layout: 'corner', skipped: true }));
  expect(rnames().at(-1) === 'replayStop' && stopped.length >= 2 && oscHz(stopped).length > 0, 'a skipped replay ends in a tape stop');
  rbus.emit('replay-done', {});
  const again = during(() => { rbus.emit('replay-show', { layout: 'corner', n: 1, hit: null }); rbus.emit('replay-panel-hit', { i: 0, n: 1, layout: 'corner', hit: null }); });
  expect(again.length > 4 && rsfx.log().at(-1).cues.join() === 'capture', 'after a skip the next replay sounds; no hit payload replays the plain capture');
  // a reset in the middle (game over, menu) lets the beds up
  rbus.emit('gameover', { result: 'checkmate' });
  expect(!rsfx.state().ducked && rsfx.state().beds === 1, 'a game over mid-replay un-ducks the beds');
  // many replays: old replay gains are let go of
  for (let k = 0; k < 5; k++) { rbus.emit('replay-show', { layout: 'corner', n: 1, hit }); rbus.emit('replay-done', {}); }
  expect(rf.disconnected >= 3, 'old replay gains are disconnected (' + rf.disconnected + ')');
  // the director drops a replay silently (a new move, the menu): update() lets the beds up
  let showing = true;
  rwin.PBP.board = { director: { active: () => showing } };
  rbus.emit('replay-show', { layout: 'trio', n: 3, hit });
  rsfx.update();
  expect(rsfx.state().ducked, 'update() keeps the duck while the director still shows the replay');
  showing = false; rsfx.update();
  expect(!rsfx.state().ducked && rsfx.state().beds === 1, 'a replay dropped without replay-done: update() un-ducks the beds');
  delete rwin.PBP.board;
  // hidden tab and volume 0: nothing schedules
  rdoc.hidden = true; rdoc.fire('visibilitychange');
  let quiet = during(() => { rbus.emit('replay-show', { layout: 'corner', n: 1, hit }); rbus.emit('replay-panel-hit', { i: 0, n: 1, layout: 'corner', hit }); });
  expect(quiet.length === 0, 'hidden tab: the replay is silent');
  rdoc.hidden = false; rdoc.fire('visibilitychange'); rbus.emit('replay-done', {});
  rwin.PBP.settings.sfxVolume = 0;
  quiet = during(() => rbus.emit('replay-panel-hit', { i: 0, n: 1, layout: 'corner', hit }));
  expect(quiet.length === 0, 'volume 0: the replay is silent');
  rsfx.dispose();
}

if (problems.length) { console.log('\n' + problems.length + ' problem(s)'); process.exit(1); }
console.log('\nsfx smoke: every cue schedules');
