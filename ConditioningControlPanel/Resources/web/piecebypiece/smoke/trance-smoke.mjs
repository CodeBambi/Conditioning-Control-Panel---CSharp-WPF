/* ============================================================================
 * smoke/trance-smoke.mjs - node tests for Distraction's audio ladder and the
 * flash centre fade (Mort's notes, 2026-09-28).
 *
 *   node ConditioningControlPanel/Resources/web/piecebypiece/smoke/trance-smoke.mjs
 * ==========================================================================*/

import { stageLevels, TRANCE, createTrance } from '../audio/trance.js';
import { centreFade } from '../ramp/layers/flash.js';
import { RAMP_TUNING } from '../ramp/meter.js';
import { sustainedFor } from '../ramp/schedule.js';

let passed = 0;
const failures = [];
const check = (name, cond, detail) => { if (cond) passed += 1; else failures.push(name + (detail ? ' -> ' + detail : '')); };

/* ---- the ladder ---------------------------------------------------------- */

const at = (m) => stageLevels(m);
check('meter 0 is plain game noise', !at(0).heartbeat.on && !at(0).binaural.on && !at(0).whisper.on);
check('heartbeat comes in first', at(0.2).heartbeat.on && !at(0.2).binaural.on && !at(0.2).whisper.on);
check('then the binaural', at(0.4).heartbeat.on && at(0.4).binaural.on && !at(0.4).whisper.on);
check('then the whispers', at(0.6).heartbeat.on && at(0.6).binaural.on && at(0.6).whisper.on);
check('heartbeat is early (Mort: "earlier")', TRANCE.heartbeat.from <= 0.15, 'from ' + TRANCE.heartbeat.from);
check('stages come in order', TRANCE.heartbeat.from < TRANCE.binaural.from && TRANCE.binaural.from < TRANCE.whisper.from);
check('the heart speeds up', at(1).heartbeat.bpm > at(0.3).heartbeat.bpm);
check('the heart gets fuller', at(1).heartbeat.gain > at(0.3).heartbeat.gain);
check('the binaural stays soft', at(1).binaural.gain <= 0.05 + 1e-9, 'gain ' + at(1).binaural.gain);
check('whispers get louder', at(1).whisper.gain > at(0.55).whisper.gain);
check('the whisper filter opens', at(1).whisper.hpHz < at(0.55).whisper.hpHz / 4, `${at(0.55).whisper.hpHz} -> ${at(1).whisper.hpHz}`);
check('whispers come more often', at(1).whisper.gapMs < at(0.55).whisper.gapMs);
check('the breath fades as the voice clears', at(1).whisper.breath < at(0.55).whisper.breath);
check('a bad meter is 0', !stageLevels(NaN).heartbeat.on && !stageLevels(-3).heartbeat.on);

/* ---- the live bed against a fake context --------------------------------- */

function fakeCtx() {
  const made = [];
  const param = () => ({ value: 0, setValueAtTime() {}, exponentialRampToValueAtTime() {}, setTargetAtTime(v) { this.value = v; } });
  const node = (kind) => {
    const n = { kind, connect() {}, disconnect() {}, start() { n.started = true; }, stop() { n.stopped = true; },
      frequency: param(), gain: param(), Q: param(), pan: param(), detune: param() };
    made.push(n); return n;
  };
  return {
    made, currentTime: 0, sampleRate: 48000,
    createGain: () => node('gain'), createOscillator: () => node('osc'), createBiquadFilter: () => node('filter'),
    createChannelMerger: () => node('merger'), createStereoPanner: () => node('pan'), createBufferSource: () => node('src'),
    decodeAudioData: (buf, ok) => ok({ duration: 12 }),
  };
}
{
  const ctx = fakeCtx();
  const t = createTrance({ ctx, master: { connect() {} }, clips: () => ['https://ccp.assets/braindrain/a.mp3'],
    fetcher: async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(8) }) });
  t.setMeter(0);
  check('silent at 0', !t.debug().beating && ctx.made.length === 0);
  t.setMeter(0.35);
  check('beating by 0.35', t.debug().beating);
  check('binaural oscillators started', ctx.made.filter(n => n.kind === 'osc' && n.started).length >= 2);
  t.setMeter(0);
  check('back to 0 stops the heart', !t.debug().beating);
  t.dispose();
  check('dispose stops the binaural', ctx.made.filter(n => n.kind === 'osc').every(n => n.stopped));
}

/* ---- the veils: Mort, "reduce the opacity scaling" ----------------------- */

const top = sustainedFor(1, RAMP_TUNING);
check('the veils together stay under half', top.spiral.alpha + top.overlay.alpha <= 0.4 + 1e-9, `${top.spiral.alpha + top.overlay.alpha}`);
check('the melt stays a tint', top.melt.alpha <= 0.3 + 1e-9, 'melt ' + top.melt.alpha);
const early = sustainedFor(0.3, RAMP_TUNING);
check('early stages still show the melt', early.melt.on && early.melt.alpha > 0.05);

/* ---- flashes fade toward the centre --------------------------------------- */

const t = RAMP_TUNING.flash;
check('a centred flash is mostly see-through', centreFade({ left: 50, top: 50 }, t) <= 0.3, '' + centreFade({ left: 50, top: 50 }, t));
check('an edge flash is full strength', centreFade({ left: 8, top: 10 }, t) === 1);
check('the fade is monotonic', centreFade({ left: 50, top: 50 }, t) < centreFade({ left: 38, top: 42 }, t)
  && centreFade({ left: 38, top: 42 }, t) < centreFade({ left: 22, top: 30 }, t));

if (failures.length) {
  console.error(`trance-smoke: ${failures.length} FAILED, ${passed} passed`);
  for (const f of failures) console.error('  x ' + f);
  process.exit(1);
}
console.log(`trance-smoke: ${passed} passed`);
