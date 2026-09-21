import {createGame} from './game.js';
import {routeFinaleAudio} from './finale-audio.js';
import test from 'node:test';
import assert from 'node:assert/strict';
import { createAudio, createBeat, cutoffFor, layerLevel, hitSemis, hitCutoff, timeScaleCents, LOOP_STEPS, MIN_LEAD_S, WOBBLE_CENTS,
  PAUSE_FADE_S, PAUSE_CUTOFF, PERFECT_GUARD_S, GREY_LEVEL } from './audio.js';
import { ROOT_HZ } from '../../shared/sound/kit.js';

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
    createOscillator: () => source({ tag: { k: 'tone' }, type: 'sine', frequency: param(440), detune: param(0) }),
    createConstantSource: () => source({ tag: { k: 'const' }, offset: param(0) }),
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

test('before start(): cues stay silent and grey tempo applies to the fallback clock', () => {
  const { audio, ctx } = make();
  const t0 = audio.now();
  assert.ok(t0 >= 0 && t0 < 1);
  assert.equal(audio.hit('brick', { combo: 3 }), 0);
  audio.relapse(); audio.breakout(); audio.crack(); audio.wallCleared(); audio.split();
  audio.nearMiss(); audio.perfect(); audio.jackpot(); audio.shatterWall(); audio.brickLand(); audio.setTimeScale(0.35);
  audio.setSaturation(0.6); audio.setState('grey');
  assert.equal(ctx(), null, 'no context is built before a gesture');
  assert.ok(audio.beat.nextSixteenth(audio.now()) > audio.now());
  assert.equal(audio.beat.spb, 60 / (96 * .72));
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

const wait = ms => new Promise(r => setTimeout(r, ms));
const FADE_MS = PAUSE_FADE_S * 1000 + 60;

test('stop sweeps down and THEN suspends, start resumes and opens up, destroy closes and makes everything a no-op', async () => {
  const { audio, ctx } = make();
  audio.start();
  const c = ctx();
  assert.equal(audio.pauseLevel, 1, 'the gate is open from the first start: no fade-in on a fresh context');
  const origin = audio.beat.origin;
  audio.stop();
  assert.equal(c.state, 'running', 'no dead cut: the context runs through the fade');
  assert.ok(audio.pausing);
  assert.ok(audio.pauseLevel < 0.001, 'the gate glides shut');
  assert.equal(audio.pauseCutoff, PAUSE_CUTOFF, 'under a closing low-pass');
  await wait(FADE_MS);
  assert.equal(c.state, 'suspended');
  assert.equal(audio.pausing, false);
  audio.start();
  assert.equal(c.state, 'running');
  assert.equal(audio.pauseLevel, 1);
  assert.ok(audio.pauseCutoff > 15000, 'the filter is open again');
  assert.equal(audio.beat.origin, origin, 'the same grid on the way back');
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

test('v2 one-shots each schedule voices; the jackpot is six bells, the perfect two notes on the grid', () => {
  const { audio, ctx } = make({ bpm: 120 });
  audio.start();
  const c = ctx();
  c.currentTime = 2;
  for (const fn of ['nearMiss', 'shatterWall', 'brickLand']) { const n = c.log.starts.length; audio[fn](); assert.ok(c.log.starts.length > n, fn); }
  let n = c.log.starts.length;
  const at = audio.jackpot();
  assert.ok(c.log.starts.length - n >= 12, 'six bells with a partial each');
  n = c.log.starts.length;
  audio.perfect();
  assert.ok(c.log.starts.length - n >= 3);
  const tones = c.log.starts.slice(n).filter(s => s.k === 'tone').map(s => s.t);
  const k = (Math.min(...tones) - audio.beat.origin) / audio.beat.sixteenth;
  assert.ok(Math.abs(k - Math.round(k)) < 1e-6, 'the perfect stamp lands on a sixteenth');
  audio.destroy();
});

test('slow-mo pitches the bed down two semitones at 0.35, straight at 1; grey adds the vinyl wobble', () => {
  assert.equal(timeScaleCents(1) + 0, 0);
  assert.ok(Math.abs(timeScaleCents(0.35) + 200) < 1e-9);
  assert.ok(Math.abs(timeScaleCents(0.675) + 100) < 1e-9);
  assert.equal(timeScaleCents(0.1), -200, 'never past two semitones');
  assert.equal(timeScaleCents(1.5) + 0, 0);
  const { audio } = make();
  audio.start();
  assert.equal(audio.bedDetuneCents + 0, 0);
  assert.ok(audio.wobbleDepth < 0.01, 'no wobble in colour');
  audio.setTimeScale(0.35);
  assert.equal(audio.timeScale, 0.35);
  assert.ok(Math.abs(audio.bedDetuneCents + 200) < 1e-9);
  audio.setTimeScale(1);
  assert.equal(audio.bedDetuneCents + 0, 0);
  audio.setState('grey');
  assert.equal(audio.wobbleDepth, 18, 'every grey entry uses the established distorted scene');
  audio.setState('colour');
  assert.ok(audio.wobbleDepth < 0.01, 'colour: the wobble is gone');
  audio.destroy();
});

test('tempo changes keep beat helpers consistent', () => {
  const beat=createBeat(96);
  beat.setTempo(96*.72);
  assert.ok(Math.abs(beat.spb-60/(96*.72))<1e-9);
  assert.equal(beat.stepIndex(beat.sixteenth*8),8);
  beat.setTempo(96);
  assert.equal(beat.spb,.625);
});

test('browser beat-only integration plays finale static, metal and scene changes exactly once',()=>{
 const {audio,ctx}=make();audio.start();
 const calls=[];const routed={...audio};
 for(const name of ['finaleInterrupt','finaleGrey','metal'])routed[name]=(...args)=>{calls.push([name,...args]);return audio[name](...args);};
 const game=createGame({rng:()=>.5,audio:{beat:audio.beat,now:audio.now},onEvent:(n,d)=>routeFinaleAudio(n,d,routed,1280)});
 const advance=t=>{for(let i=0;i<Math.ceil(t/.05);i++)game.step(.05);};
 game.jumpToWall(8);advance(2);game.step(.02,{launch:true});
 let start=ctx().log.starts.length;game.breakBrick(0);
 assert.equal(calls.filter(c=>c[0]==='finaleInterrupt').length,1);
 assert.ok(ctx().log.starts.slice(start).some(n=>n.k==='noise'),'freeze schedules audible static');
 advance(4.05);assert.equal(game.snapshot().finale.phase,'locked');
 assert.ok(calls.some(c=>c[0]==='finaleGrey'&&c[1]===true));
 assert.ok(Math.abs(audio.beat.spb-60/(96*.72))<1e-9);
 start=ctx().log.starts.length;game.breakBrick(0);
 assert.equal(calls.filter(c=>c[0]==='metal').length,1);
 assert.ok(ctx().log.starts.slice(start).filter(n=>n.k==='tone').length>=3,'sealed brick schedules metallic partials');
 const defense=game.snapshot().bricks.findIndex(b=>b.finaleDefense);game.breakBrick(defense);
 assert.equal(calls.filter(c=>c[0]==='metal').length,1,'defensive row has ordinary hits');
 game.breakoutNow();advance(.6);
 assert.ok(calls.some(c=>c[0]==='finaleGrey'&&c[1]===false));assert.equal(audio.beat.spb,.625);
 audio.destroy();
});


test('every ordinary grey entry slows the existing music scene and colour restores it',()=>{
 const {audio}=make({bpm:120});
 audio.setState('colour');assert.equal(audio.beat.spb,.5);
 audio.setState('grey');assert.ok(Math.abs(audio.beat.spb-.5/.72)<1e-9);
 audio.setState('grey');assert.ok(Math.abs(audio.beat.spb-.5/.72)<1e-9);
 audio.setState('colour');assert.equal(audio.beat.spb,.5);
 audio.destroy();
});

/* ---- the polish pass: the damage tink, the pause sweep, the perfect double-trigger guard ---- */
const longest = notes => Math.max(...notes.map(n => n.dur));
const loudest = notes => Math.max(...notes.map(n => n.level));
const brightest = notes => Math.max(...notes.filter(n => n.k === 'tone').map(n => n.lp || Infinity));

test('damage is a tink: shorter, duller, quieter and drier than the brick it did not break, still in key', () => {
  const { audio } = make();
  audio.start();
  for (const sat of [0, 0.5, 1]) {
    audio.setSaturation(sat);
    const tink = audio.hitNotes('damage', 0, 0.3), brick = audio.hitNotes('brick', 0, 0.3);
    assert.ok(tink.length > 0);
    assert.ok(longest(tink) <= longest(brick) / 2, 'at most half the ring');
    assert.ok(loudest(tink) <= loudest(brick) / 2, 'at most half the level');
    assert.ok(brightest(tink) <= brightest(brick), 'never brighter than the brick');
    assert.ok(brightest(tink) <= hitCutoff(sat), 'and still under the saturation cutoff');
    assert.ok(tink.every(n => !n.wet), 'dry: no room send, the break owns the room');
    assert.ok(brick.some(n => n.wet));
    assert.equal(tink[0].hz, ROOT_HZ, 'the fundamental is the root: in key');
    assert.ok(tink.every(n => n.pan === 0.3), 'panned to the brick');
  }
  audio.setState('grey');
  const dull = audio.hitNotes('damage', 0, 0.5), knock = audio.hitNotes('brick', 0, 0.5);
  assert.equal(dull.length, 1, 'grey stays one dull voice');
  assert.ok(dull[0].dur < knock[0].dur && dull[0].level < knock[0].level && dull[0].lp <= 500);
  const c = audio.context, n0 = c.log.starts.length;
  audio.setState('colour');
  assert.ok(audio.hit('damage', { x: 0.2 }) > 0, 'routed through hit() like every other kind');
  assert.equal(c.log.starts.length - n0, 3);
  audio.destroy();
});

test('pause sweep: start() inside the fade cancels the suspend; the context never stops', async () => {
  const { audio, ctx } = make();
  audio.start();
  const c = ctx();
  let suspends = 0; const suspend = c.suspend; c.suspend = () => { suspends++; return suspend(); };
  audio.stop();
  assert.ok(audio.pausing);
  audio.start();
  assert.equal(audio.pausing, false, 'the pending suspend is gone');
  assert.equal(audio.pauseLevel, 1, 'and the gate is on its way back up');
  await wait(FADE_MS);
  assert.equal(suspends, 0);
  assert.equal(c.state, 'running');
  audio.destroy();
});

test('pause sweep: stop twice is one fade and one suspend; stop while suspended and stop after destroy are no-ops', async () => {
  const { audio, ctx } = make();
  audio.stop();                                   // before any context exists
  audio.start();
  const c = ctx();
  let suspends = 0; const suspend = c.suspend; c.suspend = () => { suspends++; return suspend(); };
  audio.stop(); audio.stop(); audio.stop();
  await wait(FADE_MS);
  assert.equal(suspends, 1);
  audio.stop();
  assert.equal(audio.pausing, false, 'already suspended: nothing to fade');
  audio.start(); audio.stop(); audio.destroy();
  await wait(FADE_MS);
  assert.equal(suspends, 1, 'destroy() drops the pending suspend');
  assert.equal(c.state, 'closed');
  audio.stop();
});

test('pause sweep: a start() that lands while suspend() is still settling resumes afterwards', async () => {
  const { audio, ctx } = make();
  audio.start();
  const c = ctx();
  let settle = null;
  c.suspend = () => new Promise(r => { settle = () => { c.state = 'suspended'; r(); }; });
  audio.stop();
  await wait(FADE_MS);
  assert.ok(settle, 'suspend() was asked for');
  audio.start();                                  // the state still reads running, so a plain resume would be skipped
  settle(); await wait(5);
  assert.equal(c.state, 'running');
  audio.destroy();
});

test('pause sweep leaves the master, the music gate and the duck buses alone', () => {
  const { audio, ctx } = make();
  audio.start();
  const c = ctx(), n0 = c.log.nodes;
  audio.setMaster(0.4); audio.duck(0.6, 1, 0.5);
  audio.stop(); audio.start();
  assert.equal(c.log.nodes, n0, 'no new nodes: the sweep reuses its one gate and one filter');
  assert.equal(audio.pauseLevel, 1);
  audio.destroy();
});

test('perfect: a streak cue stamps the moment and perfect() skips exactly once inside the guard window', () => {
  const { audio, ctx } = make({ bpm: 120 });
  audio.start();
  const c = ctx();
  c.currentTime = 2;
  let n = c.log.starts.length;
  assert.equal(audio.cue('perfect', { streak: 1 }), false, 'the first perfect has no cue');
  audio.perfect();
  assert.equal(c.log.starts.length - n, 3, 'so the older stamp plays unchanged');
  n = c.log.starts.length;
  assert.equal(audio.cue('perfect', { streak: 3, xN: 0.5 }), true);
  assert.equal(c.log.starts.length - n, 3, 'the streak stamp');
  audio.perfect();
  assert.equal(c.log.starts.length - n, 3, 'station.js calling au(perfect) in the same moment adds nothing');
  audio.perfect();
  assert.equal(c.log.starts.length - n, 6, 'the guard is spent: another caller (a finished spell) still gets its stamp');
  n = c.log.starts.length;
  audio.cue('perfect', { streak: 4 });
  c.currentTime = 2 + PERFECT_GUARD_S * 4;
  audio.perfect();
  assert.equal(c.log.starts.length - n, 6, 'a stale guard never eats a later perfect');
  audio.destroy();
});

test('the grey world sits a little lower: the whole mix dips, colour brings it back, the master is left alone', () => {
  assert.ok(GREY_LEVEL >= .6 && GREY_LEVEL < .85, 'slightly lower, not a mute');
  const { audio } = make({ master: 0.5 });
  assert.equal(audio.greyLevel, 1, 'no graph, no dip');
  audio.start();
  assert.equal(audio.greyLevel, 1);
  audio.setState('grey'); assert.equal(audio.greyLevel, GREY_LEVEL);
  audio.setMaster(0.9); assert.equal(audio.greyLevel, GREY_LEVEL, 'a volume change does not undo the dip');
  audio.breakout(); assert.equal(audio.state, 'colour'); assert.equal(audio.greyLevel, 1);
  audio.relapse(); assert.equal(audio.greyLevel, GREY_LEVEL, 'a relapse dips again');
  audio.destroy(); assert.equal(audio.greyLevel, 1);
});

test('the eye voice loads from the station own assets first, and the three clips are really there', async () => {
  const fs = await import('node:fs'), src = fs.readFileSync(new URL('./audio.js', import.meta.url), 'utf8');
  assert.ok(src.indexOf("./assets/voice/drift-") > 0 && src.indexOf("./assets/voice/drift-") < src.indexOf('dtrh/assets/barks'), 'own assets before the cross-tree fallback');
  for (let i = 1; i <= 3; i++) assert.ok(fs.statSync(new URL('./assets/voice/drift-' + i + '.mp3', import.meta.url)).size > 100000, 'a real mp3, not a pointer');
});
