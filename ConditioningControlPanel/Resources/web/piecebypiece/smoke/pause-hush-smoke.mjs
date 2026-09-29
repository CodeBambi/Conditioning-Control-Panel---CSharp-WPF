/* ============================================================================
 * smoke/pause-hush-smoke.mjs - the Esc pause goes quiet at once and comes back slowly.
 *
 * The panic key defaults to Escape and the panel lets the board keep a first
 * Escape as its pause (a second within 2 s is a full panic that closes it), so
 * the pause must silence everything the page makes, not only show a card:
 *   A  ui/pause-hush.js: sfx hushed, replay cut, Distraction layers off; on
 *      resume the sound eases back and the layers wait RETURN_MS.
 *   B  audio/sfx.js hush(): master to 0, held there through a volume change and
 *      a tab-visibility flip, back to the player's volume after.
 *   C  boot.js: an Escape on the pause card leaves (never resumes), leaving
 *      stays hushed, and the slow-turn spiral board drops while paused.
 *
 *   node smoke/pause-hush-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { createBus } from '../game/events.js';
import { createSfx } from '../audio/sfx.js';
import { createPauseHush, RETURN_MS } from '../ui/pause-hush.js';

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}

/* ---- A: the hush module ---------------------------------------------------- */
{
  const calls = [];
  const timers = new Map();
  let nextId = 1;
  const later = (fn, ms) => { const id = nextId++; timers.set(id, { fn, ms }); return id; };
  const cancel = (id) => { timers.delete(id); };
  const fire = () => { for (const [id, t] of [...timers]) { timers.delete(id); t.fn(); } };
  let paused = false;
  const board = {
    sfx: { hush: (on) => calls.push('hush:' + on) },
    director: { skip: () => calls.push('skip') },
  };
  const ramp = { setEnabled: (on) => calls.push('ramp:' + on) };
  const hush = createPauseHush({ board, ramp: () => ramp, isPaused: () => paused, later, cancel });

  paused = true; hush.set(true);
  expect(calls.join() === 'hush:true,skip,ramp:false', 'pause: sound hushed, replay cut, Distraction layers off');
  expect(timers.size === 0, 'pause arms nothing');

  calls.length = 0; paused = false; hush.set(false);
  expect(calls.join() === 'hush:false', 'resume: the sound eases back at once, the layers do not');
  expect(timers.size === 1 && [...timers.values()][0].ms === RETURN_MS && RETURN_MS >= 2000, 'the layers come back after RETURN_MS');
  fire();
  expect(calls.at(-1) === 'ramp:true', 'then the Distraction layers return');

  calls.length = 0; hush.set(false); paused = true; hush.set(true);
  expect(timers.size === 0, 'a pause inside the return window cancels the return');
  expect(!calls.includes('ramp:true'), 'nothing comes back under a pause');

  calls.length = 0; paused = false; hush.set(false); paused = true; fire();
  expect(!calls.includes('ramp:true'), 'a return that lands while paused again stays off');

  const loud = [];
  const broken = createPauseHush({
    board: { sfx: { hush: () => { throw new Error('boom'); } }, director: { skip: () => loud.push('skip') } },
    ramp: () => ({ setEnabled: (on) => loud.push('ramp:' + on) }),
    later, cancel,
  });
  const warn = console.warn; console.warn = () => {};
  broken.set(true);
  console.warn = warn;
  expect(loud.join() === 'skip,ramp:false', 'a part that throws never stops the rest going quiet');

  const bare = createPauseHush({ board: {}, ramp: () => null, later, cancel });
  let threw = false;
  try { bare.set(true); bare.set(false); } catch { threw = true; }
  expect(!threw, 'Classic with no ramp, no director and no sfx yet: still fine');
}

/* ---- B: sfx.hush on the real module ------------------------------------------ */
{
  class Param {
    constructor(v) { this.value = v; }
    setValueAtTime(v) { this.value = v; return this; }
    linearRampToValueAtTime() { return this; }
    exponentialRampToValueAtTime() { return this; }
    setTargetAtTime(v) { this.value = v; return this; }
    cancelScheduledValues() { return this; }
  }
  class FakeNode {
    constructor(ctx, kind) { this.ctx = ctx; this.kind = kind; }
    connect(to) { return to; }
    start() { }
    stop() { }
  }
  class FakeContext {
    constructor() { this.state = 'running'; this.currentTime = 0; this.sampleRate = 48000; this.destination = { kind: 'destination' }; }
    createGain() { const n = new FakeNode(this, 'gain'); n.gain = new Param(1); return n; }
    createOscillator() { const n = new FakeNode(this, 'osc'); n.frequency = new Param(440); n.detune = new Param(0); return n; }
    createBiquadFilter() { const n = new FakeNode(this, 'filter'); n.frequency = new Param(1000); return n; }
    createBufferSource() { const n = new FakeNode(this, 'noise'); n.detune = new Param(0); return n; }
    createDelay() { const n = new FakeNode(this, 'delay'); n.delayTime = new Param(0); return n; }
    createBuffer(ch, len) { return { getChannelData: () => new Float32Array(len) }; }
    resume() { return Promise.resolve(); }
    close() { return Promise.resolve(); }
  }
  const listeners = new Map();
  const doc = {
    hidden: false,
    addEventListener(t, fn) { if (!listeners.has(t)) listeners.set(t, new Set()); listeners.get(t).add(fn); },
    removeEventListener(t, fn) { listeners.get(t)?.delete(fn); },
    fire(t) { for (const fn of listeners.get(t) || []) fn({ type: t }); },
  };
  const win = { PBP: { settings: { sfxVolume: 0.6 } } };
  const game = { legalTargets: () => [], rules: { chess: { history: () => [] } } };
  const sfx = createSfx({ bus: createBus(), game, group: { children: [] }, squareOf: () => 'e4', doc, win, context: () => new FakeContext() });
  sfx.play('grab');                                   // makes the context
  expect(sfx.ready() && sfx.state().volume === 0.6, 'the master starts at the player volume');
  sfx.hush(true);
  expect(sfx.isHushed() && sfx.state().volume === 0, 'hush: the master drops to 0');
  sfx.setVolume(0.9);
  expect(sfx.state().volume === 0, 'moving the volume slider while paused keeps it silent');
  doc.hidden = true; doc.fire('visibilitychange'); doc.hidden = false; doc.fire('visibilitychange');
  expect(sfx.state().volume === 0, 'a minimise and restore while paused keeps it silent');
  sfx.hush(false);
  expect(!sfx.isHushed() && sfx.state().volume === 0.9, 'resume: back to the player volume (eased in the real graph)');
  sfx.dispose();
}

/* ---- C: boot.js wiring ------------------------------------------------------------ */
{
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  expect(/if \(pausedGame\) \{ postToHost\(\{ type: 'pbp:exit' \}\); return; \}/.test(boot), 'Escape on the pause card leaves, and never resumes');
  expect(!/if \(pausedGame\) \{ setGamePaused\(false\)/.test(boot), 'no Escape path un-hushes on the way out');
  expect(/pauseHush\.set\(p\)/.test(boot), 'every pause and resume goes through the hush');
  expect(/menuOpen: \(\) => [^\n]*isPaused/.test(boot), 'the slow-turn spiral board drops while paused');
}

console.log(`\npause hush smoke: ${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
