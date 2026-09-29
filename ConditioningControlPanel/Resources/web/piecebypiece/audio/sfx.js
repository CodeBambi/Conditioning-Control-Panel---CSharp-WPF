import { createCrowdVoices } from './crowd-voices.js';
import { createCrowd } from './crowd.js';
import { createTrance } from './trance.js';

/* ============================================================================
 * audio/sfx.js - the board makes sounds.
 *
 * Every cue is drawn with Web Audio on the spot: a few oscillators, one
 * shared noise buffer, an envelope. No audio files, no dependencies. The
 * AudioContext is made lazily on the first pointerdown or keydown (browsers
 * refuse to start one any other way), muted while the tab is hidden, and the
 * master gain follows window.PBP.settings.sfxVolume (0.6 when unset).
 *
 * What plays, and off which bus event:
 *   grab       a wet little pop           `grab`
 *   tick       a dry tick per legal square the held man passes over `dragmove`
 *   land       a thud pitched by the man's height, the king lowest  `land`
 *   capture    the thud plus a squelch     `land` with capture (not a whip's:
 *              the crack already paid for that one, the square gets a thud)
 *   whip       a crack and a slap          `hit` (board/whip.js, the bishop)
 *   boing      a sympathetic wobble        `drop` ok:false
 *   check      a low pulse, beating every 0.9 s while the side to move is in
 *              check                       `check` .. `turn` / `gameover`
 *   mate/draw  a sting for the end         `gameover`
 *   clock      one low tick a second for the side to move under 30 s, sharper
 *              under 10 s, never once the game is over  `clock`
 *   promote    a rising chime              `turn` after a promotion
 *   cardOpen/cardClose  a soft whoosh when the ramp's video card comes and
 *              goes (a MutationObserver on the fx root; the ramp emits nothing)
 *   whisper    a breathy sigh, quiet     board/watch.js, when a man is held
 *              too long
 *
 * The capture replay (board/director.js beats; never the live 'hit'):
 *   replayIn    a tape rewind, then a riser into panel 0's hit  `replay-show`
 *   replaySlam  a comic thunk + paper snap, a step higher per panel
 *                                         `replay-panel-in`
 *   replayHit   the ORIGINAL capture's own sound again, stretched and pitched
 *              down (slow motion), through a deep room tail, a little
 *              different per panel; panel 0 adds a sub drop
 *                                         `replay-panel-hit`
 *   replayOut   a whoosh out               `replay-exit`
 *   replayStop  a tape stop when skipped   `replay-exit` skipped: every replay
 *              voice still ringing is faded out under it
 *   While a replay is up the crowd and the trance bed sit on their own gain
 *   and duck to TUNING.replay.duck, back up on `replay-done` (or any reset).
 *
 * The room (sfx.setMeter, driven by board.setMeter): a feedback delay sits
 * beside the master and its wet level follows the meter, nothing at 0.25 and
 * 0.35 at 1.0; past 0.55 every new voice is detuned, down to two semitones
 * flat at 1.0. Both stay dry while the mover's clock is under 30 s (the
 * clock ticks must stay crisp) and under reduced motion.
 *
 * sfx.play(name, opts) plays any cue by hand; sfx.log() is the last cues with
 * the context state, so a harness can prove each one scheduled. A `context`
 * factory can be injected for tests (node has no AudioContext).
 * ==========================================================================*/

export const TUNING = Object.freeze({
  volume: 0.6,            // default master, settings.sfxVolume overrides
  pop: { from: 520, to: 260, sec: 0.07, gain: 0.22 },
  tick: { hz: 1800, sec: 0.02, gain: 0.05 },
  thud: { pawnHz: 210, kingHz: 95, pawnH: 0.55, kingH: 1.25, sec: 0.18, gain: 0.5, tapGain: 0.12 },
  squelch: { from: 900, to: 150, sec: 0.18, gain: 0.16 },
  whip: { crackFrom: 5200, crackTo: 700, crackSec: 0.07, crackGain: 0.34, slapHz: 170, slapTo: 55, slapSec: 0.14, slapGain: 0.3 },
  boing: { hz: [300, 180, 240, 200], sec: 0.25, gain: 0.14 },
  check: { hz: 58, sec: 0.28, gain: 0.32, everySec: 0.9 },
  mate: { hz: [392, 311, 233], step: 0.16, sec: 0.5, gain: 0.2 },
  draw: { hz: [262, 262], step: 0.22, sec: 0.4, gain: 0.16 },
  clock: { hz: 1200, sharpHz: 1900, sec: 0.015, gain: 0.07, sharpGain: 0.11, underMs: 30000, sharpMs: 10000 },
  promote: { hz: [523, 659, 784, 1047], step: 0.06, sec: 0.28, gain: 0.12 },
  whoosh: { lowHz: 200, highHz: 4000, sec: 0.26, gain: 0.1 },
  whisper: { from: 1100, to: 380, sec: 0.6, gain: 0.05, attack: 0.18 },
  room: {
    delaySec: 0.21, feedback: 0.32,      // the echo and how much of it comes back
    wetFrom: 0.25, wetAt1: 0.35,         // wet level: 0 at wetFrom, wetAt1 at meter 1
    driftFrom: 0.55, driftCents: 200,    // detune: 0 at driftFrom, this flat at 1
    lowClockMs: 30000,                   // dry while the mover has less than this
  },
  // The capture replay. Levels matched to the live kit, not heard: rewind and
  // whoosh sit at the card whoosh (.1), the slam at hopland/kick, the sub at
  // the check pulse, a slowed hit at .7 of its live gain because it lasts longer.
  replay: Object.freeze({
    rewind: { from: 5200, to: 700, sec: .28, gain: .08, chatterHz: 38, chatterGain: .035 },
    riser: { at: .22, sec: .40, from: 180, to: 2600, gain: .065, toneFrom: 110, toneTo: 330, toneGain: .03 },
    slam: { hz: 118, to: 52, sec: .13, gain: .2, snapHz: 520, snapGain: .07, paperGain: .05, stepSemis: 3 },
    // panel i: how much slower, how much lower, how loud, where
    panels: [
      { stretch: 2.2, pitch: .58, gain: .72, pan: 0 },
      { stretch: 1.8, pitch: .66, gain: .6, pan: -.28 },
      { stretch: 1.5, pitch: .75, gain: .52, pan: .28 },
    ],
    sub: { hz: 52, to: 27, sec: .75, gain: .28, filterHz: 140 },
    tail: { taps: [.13, .29], feedback: .42, filterHz: 1150, gain: .42 },
    out: { from: 3800, to: 240, sec: .26, gain: .09, thumpHz: 90, thumpGain: .08 },
    stop: { from: 340, to: 38, sec: .24, gain: .1, filterHz: 1400, hissGain: .05, fadeSec: .05 },
    duck: .55, duckTc: .08, releaseTc: .25, safetyMs: 4500,
  }),
  logSize: 32,
});

const SIGNATURE = Object.freeze({ p: 'stomp', n: 'stomp', b: 'whip', k: 'headbutt', q: 'breakdance', r: 'launch' });
/**
 * The cues a live 'hit' plays, in order: the rebound under a heavy man's (or
 * a kick's) strike, then the strike itself. The replay plays the same list.
 */
export function hitCues(p) {
  const main = p?.sound || (p?.manner === 'signature' ? (SIGNATURE[p.piece] || 'capture') : 'whip');
  return (['q', 'r', 'b'].includes(p?.piece) || p?.sound === 'kick') ? ['rebound', main] : [main];
}
/** How panel i replays its hit (stretch, pitch, gain, pan); past the table, the last row. */
export function replayVoice(i) {
  const P = TUNING.replay.panels;
  return P[Math.max(0, Math.min(P.length - 1, i | 0))];
}
/** Panel i's slam pitch: a few semitones higher each panel. */
export const slamRise = (i) => 2 ** (((i | 0) * TUNING.replay.slam.stepSemis) / 12);

function reducedMotion(win) {
  const s = win && win.PBP && win.PBP.settings;
  if ((s && s.reducedMotion) || (win && win.PBP && win.PBP.reducedMotion)) return true;
  try { return !!(win && win.matchMedia) && win.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

const clamp01 = (v) => Math.max(0, Math.min(1, Number(v) || 0));

export function createSfx({ bus, game = null, group = null, squareOf = null, root = null,
                            doc = (typeof document !== 'undefined' ? document : null),
                            win = (typeof window !== 'undefined' ? window : null),
                            context = null } = {}) {
  let ctx = null;
  let master = null;
  let noise = null;
  let crowdVoices = null;
  let trance = null;         // Distraction's sound bed (audio/trance.js), made with the context
  let disposed = false;
  let wet = null;            // the delay's return, beside the master
  let meter = 0;
  let wetLevel = 0;          // what the wet gain was last asked for
  let cuePitch = 1;
  let drift = 0;             // cents, applied to every new voice
  let lowClock = false;      // the mover is under lowClockMs
  let beds = null;           // crowd + trance ride this gain, so a replay can duck them
  let deepIn = null;         // the replay's deep room: dry to master, and into two taps
  let tailOut = null;        // the taps' return, faded on a skip
  let replayGain = null;     // this replay's voices, faded on a skip
  let sink = null;           // where tone/hiss connect; null = master
  let stretch = 1;           // time stretch for tone/hiss (a replayed hit is slow motion)
  let ducked = false;
  let duckTimer = 0;
  const log = [];
  const settings = () => (win && win.PBP && win.PBP.settings) || {};
  let hushed = false;        // the Esc pause: every voice this page makes goes quiet (hush())
  const volume = () => {
    if (hushed) return 0;
    const v = settings().sfxVolume;
    return clamp01(v === undefined ? TUNING.volume : v);
  };

  // --- the context, made on the first gesture -------------------------------
  function makeContext() {
    if (ctx || disposed) return ctx;
    try {
      const Ctor = context || (win && (win.AudioContext || win.webkitAudioContext));
      ctx = typeof Ctor === 'function' && !context ? new Ctor() : (context ? context() : null);
    } catch (e) { console.warn('[pbp] no audio', e); ctx = null; }
    if (!ctx) return null;
    master = ctx.createGain();
    master.gain.value = doc && doc.hidden ? 0 : volume();
    master.connect(ctx.destination);
    // The room: master -> delay -> (feedback -> delay) and delay -> wet -> out.
    if (typeof ctx.createDelay === 'function') {
      const R = TUNING.room;
      const delay = ctx.createDelay(1.0);
      delay.delayTime.value = R.delaySec;
      const back = ctx.createGain();
      back.gain.value = R.feedback;
      wet = ctx.createGain();
      wet.gain.value = 0;
      master.connect(delay);
      delay.connect(back); back.connect(delay);
      delay.connect(wet); wet.connect(ctx.destination);
      applyRoom();
    }
    const seconds = 1;
    const buf = ctx.createBuffer(1, Math.max(1, Math.floor(ctx.sampleRate * seconds)), ctx.sampleRate);
    const data = buf.getChannelData(0);
    for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
    noise = buf;
    // the beds (crowd, trance) through one gain on the master, for the replay duck
    beds = ctx.createGain();
    beds.gain.value = 1;
    beds.connect(master);
    // Distraction's bed rides the same master (volume, hidden-tab mute); the
    // heart skips a beat while the check pulse is thumping so the two never stumble
    trance = createTrance({ ctx, master: beds, noise, clips: () => settings().whispers || [], canBeat: () => !checkTimer });
    trance.setMeter(meter);
    return ctx;
  }
  function wake() {
    const c = makeContext();
    if (c && c.state === 'suspended' && c.resume) { try { c.resume(); } catch { /* not yet */ } }
  }
  function onVisibility() {
    if (doc?.hidden) crowd.cancel();
    if (!master || !ctx) return;
    const to = doc && doc.hidden ? 0 : volume();
    try { master.gain.setTargetAtTime(to, ctx.currentTime, 0.02); } catch { master.gain.value = to; }
  }

  // --- the room --------------------------------------------------------------
  const lerp01 = (m, from) => (m <= from ? 0 : Math.min(1, (m - from) / (1 - from)));
  /** Work out the wet level and the drift from the meter, and set the wet gain. */
  function applyRoom() {
    const R = TUNING.room;
    const dry = lowClock || reducedMotion(win);
    wetLevel = dry ? 0 : R.wetAt1 * lerp01(meter, R.wetFrom);
    drift = dry ? 0 : -R.driftCents * lerp01(meter, R.driftFrom);
    if (wet && ctx) {
      try { wet.gain.setTargetAtTime(wetLevel, ctx.currentTime, 0.15); } catch { wet.gain.value = wetLevel; }
    }
  }
  function setMeter(m) { meter = clamp01(m); applyRoom(); if (trance) trance.setMeter(meter); }
  const tune = (node) => { if (drift && node.detune) { try { node.detune.value = drift; } catch { /* a source without detune */ } } };

  // --- the palette ------------------------------------------------------------
  /** An oscillator with a gain envelope: attack straight up, decay to zero. */
  function tone(type, hz, sec, gain, { at = 0, slideTo = null, filterHz = null } = {}) {
    const t0 = ctx.currentTime + at * stretch;
    sec *= stretch;
    const osc = ctx.createOscillator();
    const env = ctx.createGain();
    osc.type = type;
    tune(osc);
    osc.frequency.setValueAtTime(hz * cuePitch, t0);
    if (slideTo) osc.frequency.exponentialRampToValueAtTime(Math.max(1, slideTo * cuePitch), t0 + sec);
    env.gain.setValueAtTime(0.0001, t0);
    env.gain.exponentialRampToValueAtTime(gain, t0 + 0.005);
    env.gain.exponentialRampToValueAtTime(0.0001, t0 + sec);
    let head = osc;
    if (filterHz) {
      const f = ctx.createBiquadFilter();
      f.type = 'lowpass';
      f.frequency.value = filterHz;
      osc.connect(f);
      head = f;
    }
    head.connect(env);
    env.connect(sink || master);
    osc.start(t0);
    osc.stop(t0 + sec + 0.02);
  }
  /** Filtered noise with a sweep, for taps and whooshes. */
  function hiss(sec, gain, { at = 0, from = 1000, to = 1000, type = 'bandpass', attack = null } = {}) {
    const t0 = ctx.currentTime + at * stretch;
    sec *= stretch;
    const src = ctx.createBufferSource();
    src.buffer = noise;
    tune(src);
    const f = ctx.createBiquadFilter();
    f.type = type;
    f.frequency.setValueAtTime(from * cuePitch, t0);
    if (to !== from) f.frequency.exponentialRampToValueAtTime(to * cuePitch, t0 + sec);
    const env = ctx.createGain();
    env.gain.setValueAtTime(0.0001, t0);
    env.gain.exponentialRampToValueAtTime(gain, t0 + (attack != null ? Math.min(attack, sec * 0.5) : Math.min(0.03, sec * 0.3)));
    env.gain.exponentialRampToValueAtTime(0.0001, t0 + sec);
    src.connect(f); f.connect(env); env.connect(sink || master);
    src.start(t0);
    src.stop(t0 + sec + 0.02);
  }
  const thudHz = (height) => {
    const T = TUNING.thud;
    const k = clamp01((height - T.pawnH) / (T.kingH - T.pawnH));
    return T.pawnHz + (T.kingHz - T.pawnHz) * k;
  };

  function audience(kind) {
    if (!crowdVoices) crowdVoices = createCrowdVoices({ ctx, master: beds || master, noise });
    crowdVoices.play(kind);
  }
  const cues = {
    crowdApplause() { audience('crowdApplause'); },
    crowdCheer() { audience('crowdCheer'); },
    crowdBoo() { audience('crowdBoo'); },
    hooves() {
      for (const at of [0, .085, .21, .295]) {
        tone('triangle', at < .2 ? 680 : 510, .045, .12, { at, slideTo: 290 });
        hiss(.025, .07, { at, from: 1900, to: 700, attack: .002 });
      }
    },
    // A short stylised horse whinny, not speech or a voice service.
    neigh() {
      const t = ctx.currentTime, osc = ctx.createOscillator(), env = ctx.createGain();
      const filter = ctx.createBiquadFilter(); filter.type = 'lowpass'; filter.frequency.value = 2300;
      osc.type = 'sawtooth';
      osc.frequency.setValueAtTime(470, t);
      osc.frequency.exponentialRampToValueAtTime(1020, t + .11);
      for (let i = 1; i <= 14; i++) osc.frequency.linearRampToValueAtTime(850 - i * 29 + Math.sin(i * 2.4) * 115, t + .11 + i * .035);
      env.gain.setValueAtTime(.0001, t); env.gain.exponentialRampToValueAtTime(.055, t + .065);
      env.gain.exponentialRampToValueAtTime(.0001, t + .64);
      osc.connect(filter); filter.connect(env); env.connect(master); osc.start(t); osc.stop(t + .66);
    },
    hop() { tone('sine', 160, .10, .09, { slideTo: 340 }); hiss(.035, .035, { from: 700, to: 1600 }); },
    hopland() { tone('sine', 150, .12, .14, { slideTo: 65 }); hiss(.035, .055, { from: 1800, to: 400 }); },
    kick() {
      tone('triangle', 155, .14, .34, { slideTo: 52 });
      hiss(.055, .16, { from: 1800, to: 380 });
    },
    rebound() {
      tone('sine', 220, .16, .065, { at: .045, slideTo: 380 });
      tone('triangle', 380, .12, .035, { at: .13, slideTo: 190, filterHz: 1000 });
    },
    stretch() { tone('triangle', 145, .30, .065, { slideTo: 310, filterHz: 900 }); },
    tension() {
      for (let i = 0; i < 5; i++) tone('triangle', 210 + i * 14, .065, .04 + i * .004, { at: i * .06, slideTo: 170 + i * 12, filterHz: 950 });
    },
    stomp() { cues.land({ height: .8 }); tone('sine', 95, .22, .22, { slideTo: 40 }); hiss(.11, .09, { from: 1700, to: 300 }); },
    headbutt() { tone('sine', 125, .26, .42, { slideTo: 38 }); tone('triangle', 620, .055, .13, { slideTo: 180 }); },
    sweep() { hiss(.16, .10, { from: 450, to: 4800, attack: .06 }); },
    spin() { hiss(.38, .11, { from: 350, to: 2400, attack: .15 }); tone('triangle', 240, .22, .055, { slideTo: 490 }); },
    breakdance() { cues.whip(); tone('triangle', 190, .20, .16, { slideTo: 65 }); },
    charge() { tone('triangle', 75, .45, .12, { slideTo: 190 }); hiss(.40, .055, { from: 200, to: 1600, attack: .17 }); },
    launch() { tone('sine', 80, .28, .45, { slideTo: 30 }); hiss(.055, .20, { from: 3500, to: 500, attack: .002 }); hiss(.40, .09, { from: 2200, to: 250, at: .06 }); },
    grab() { const P = TUNING.pop; tone('sine', P.from, P.sec, P.gain, { slideTo: P.to }); },
    tick() { const P = TUNING.tick; tone('triangle', P.hz, P.sec, P.gain); },
    land({ height = 1 } = {}) {
      const T = TUNING.thud;
      const hz = thudHz(height);
      tone('sine', hz, T.sec, T.gain, { slideTo: hz * 0.6 });
      hiss(0.04, T.tapGain, { from: 2500, to: 600, type: 'lowpass' });
    },
    capture({ height = 1 } = {}) {
      cues.land({ height });
      const S = TUNING.squelch;
      tone('sawtooth', S.from, S.sec, S.gain, { slideTo: S.to, filterHz: 1200, at: 0.02 });
      hiss(0.12, S.gain * 0.5, { from: 3000, to: 300, at: 0.03 });
    },
    whip() {
      const Wc = TUNING.whip;
      hiss(Wc.crackSec, Wc.crackGain, { from: Wc.crackFrom, to: Wc.crackTo, type: 'bandpass', attack: 0.004 });
      tone('sine', Wc.slapHz, Wc.slapSec, Wc.slapGain, { slideTo: Wc.slapTo, at: 0.01 });
      hiss(0.05, Wc.crackGain * 0.35, { from: 1800, to: 300, type: 'lowpass', at: 0.02 });
    },
    boing() {
      const B = TUNING.boing;
      const t0 = ctx.currentTime;
      const osc = ctx.createOscillator();
      const env = ctx.createGain();
      osc.type = 'triangle';
      tune(osc);
      osc.frequency.setValueAtTime(B.hz[0], t0);
      for (let i = 1; i < B.hz.length; i++) osc.frequency.exponentialRampToValueAtTime(B.hz[i], t0 + B.sec * (i / (B.hz.length - 1)));
      env.gain.setValueAtTime(B.gain, t0);
      env.gain.exponentialRampToValueAtTime(0.0001, t0 + B.sec);
      osc.connect(env); env.connect(master);
      osc.start(t0); osc.stop(t0 + B.sec + 0.02);
    },
    check() { const C = TUNING.check; tone('sine', C.hz, C.sec, C.gain, { filterHz: 200 }); },
    mate() { const M = TUNING.mate; M.hz.forEach((hz, i) => tone('sawtooth', hz, M.sec, M.gain, { at: i * M.step, filterHz: 900 })); },
    draw() { const D = TUNING.draw; D.hz.forEach((hz, i) => tone('triangle', hz, D.sec, D.gain, { at: i * D.step, filterHz: 700 })); },
    clock({ sharp = false } = {}) {
      const C = TUNING.clock;
      tone('square', sharp ? C.sharpHz : C.hz, C.sec, sharp ? C.sharpGain : C.gain, { filterHz: 3000 });
    },
    promote() { const P = TUNING.promote; P.hz.forEach((hz, i) => tone('sine', hz, P.sec, P.gain, { at: i * P.step })); },
    cardOpen() { const W = TUNING.whoosh; hiss(W.sec, W.gain, { from: W.lowHz, to: W.highHz, type: 'lowpass' }); },
    cardClose() { const W = TUNING.whoosh; hiss(W.sec, W.gain, { from: W.highHz, to: W.lowHz, type: 'lowpass' }); },
    whisper() { const W = TUNING.whisper; hiss(W.sec, W.gain, { from: W.from, to: W.to, type: 'bandpass', attack: W.attack }); },

    // --- the capture replay ---------------------------------------------------
    replayIn() {
      const R = TUNING.replay, W = R.rewind, S = R.riser;
      withSink(freshReplay(), 1, () => {
        // the rewind: bright noise running down fast, a flutter of tape chatter over it
        hiss(W.sec, W.gain, { from: W.from, to: W.to, attack: .01 });
        tone('square', W.chatterHz * 9, W.sec * .9, W.chatterGain, { slideTo: W.chatterHz * 3, filterHz: 1800 });
        // the riser into panel 0's hit, swelling, cut by the hit itself
        hiss(S.sec, S.gain, { at: S.at, from: S.from, to: S.to, type: 'bandpass', attack: S.sec * .5 });
        tone('triangle', S.toneFrom, S.sec, S.toneGain, { at: S.at, slideTo: S.toneTo, filterHz: 900 });
      });
    },
    replaySlam({ i = 0 } = {}) {
      const S = TUNING.replay.slam, k = slamRise(i);
      withSink(replayGain, 1, () => {
        tone('sine', S.hz * k, S.sec, S.gain, { slideTo: S.to * k });
        tone('triangle', S.snapHz * k, .04, S.snapGain, { slideTo: S.snapHz * k * .5 });
        hiss(.09, S.paperGain, { from: 5200 * k, to: 1400, type: 'highpass', attack: .004 });
      });
    },
    replayHit({ i = 0, hit = null } = {}) {
      const R = TUNING.replay, V = replayVoice(i);
      const list = hit ? hitCues(hit) : ['capture'];
      const mass = { p: 1.08, n: 1, b: 1.04, r: .89, q: .96, k: .86 }[hit?.piece] || 1;
      const opts = { piece: hit?.piece, height: hit?.height ?? 1 };
      const into = panelNode(V);
      withSink(into, V.stretch, () => {
        for (const name of list) {
          cuePitch = (name === 'rebound' ? 1 : mass) * V.pitch;
          cues[name](opts);
        }
      });
      if (i === 0) {
        const U = R.sub;
        cuePitch = 1;
        withSink(replayGain, 1, () => tone('sine', U.hz, U.sec, U.gain, { slideTo: U.to, filterHz: U.filterHz }));
      }
      return list;
    },
    replayOut() {
      const O = TUNING.replay.out;
      withSink(replayGain, 1, () => {
        hiss(O.sec, O.gain, { from: O.from, to: O.to, type: 'lowpass', attack: .02 });
        tone('sine', O.thumpHz, .12, O.thumpGain, { at: .04, slideTo: O.thumpHz * .6 });
      });
    },
    replayStop() {
      const T = TUNING.replay.stop;
      fadeReplay(T.fadeSec);
      // the tape stop goes straight to the master: the replay bus is fading
      tone('sawtooth', T.from, T.sec, T.gain, { slideTo: T.to, filterHz: T.filterHz });
      hiss(T.sec * .8, T.hissGain, { from: 2600, to: 180, type: 'lowpass', attack: .004 });
    },
  };

  // --- the replay's own graph --------------------------------------------------
  /** Build the deep room once: deepIn -> master dry, and -> two dark taps -> tailOut -> master. */
  function ensureDeep() {
    if (deepIn || !ctx) return deepIn;
    const D = TUNING.replay.tail;
    deepIn = ctx.createGain();
    deepIn.gain.value = 1;
    deepIn.connect(master);
    tailOut = ctx.createGain();
    tailOut.gain.value = D.gain;
    tailOut.connect(master);
    if (typeof ctx.createDelay === 'function') {
      for (const sec of D.taps) {
        const d = ctx.createDelay(1.0);
        d.delayTime.value = sec;
        const back = ctx.createGain();
        back.gain.value = D.feedback;
        const dark = ctx.createBiquadFilter();
        dark.type = 'lowpass';
        dark.frequency.value = D.filterHz;
        deepIn.connect(d);
        d.connect(dark); dark.connect(back); back.connect(d);
        dark.connect(tailOut);
      }
    }
    return deepIn;
  }
  /** A new gain for this replay's voices (a skip fades the old one away). */
  const retired = [];        // old replay gains, let go of once long silent
  function retire(node) {
    if (node) retired.push(node);
    while (retired.length > 2) { try { retired.shift().disconnect(); } catch { /* gone */ } }
  }
  function freshReplay() {
    ensureDeep();
    retire(replayGain);
    replayGain = ctx.createGain();
    replayGain.gain.value = 1;
    replayGain.connect(deepIn);
    try { tailOut.gain.cancelScheduledValues?.(ctx.currentTime); tailOut.gain.setTargetAtTime(TUNING.replay.tail.gain, ctx.currentTime, .02); }
    catch { tailOut.gain.value = TUNING.replay.tail.gain; }
    return replayGain;
  }
  /** One panel's hit: its own gain and pan into the replay gain. */
  function panelNode(V) {
    if (!replayGain) freshReplay();
    const g = ctx.createGain();
    g.gain.value = V.gain;
    if (V.pan && typeof ctx.createStereoPanner === 'function') {
      const pan = ctx.createStereoPanner();
      pan.pan.value = V.pan;
      g.connect(pan); pan.connect(replayGain);
    } else g.connect(replayGain);
    return g;
  }
  function fadeReplay(sec) {
    if (!ctx) return;
    const t = ctx.currentTime;
    for (const node of [replayGain, tailOut]) {
      if (!node) continue;
      try { node.gain.setTargetAtTime(0, t, sec / 3); } catch { node.gain.value = 0; }
    }
    retire(replayGain);
    replayGain = null;
  }
  /** Run fn with tone/hiss pointed at `node` and stretched; always put them back. */
  function withSink(node, s, fn) {
    const prevSink = sink, prevStretch = stretch;
    sink = node || (ensureDeep(), freshReplay());
    stretch = s;
    try { fn(); } finally { sink = prevSink; stretch = prevStretch; }
  }
  function duck(on) {
    if (duckTimer) { clearTimeout(duckTimer); duckTimer = 0; }
    ducked = !!on;
    if (on) duckTimer = setTimeout(() => duck(false), TUNING.replay.safetyMs);
    if (!beds || !ctx) return;
    const R = TUNING.replay;
    try { beds.gain.setTargetAtTime(on ? R.duck : 1, ctx.currentTime, on ? R.duckTc : R.releaseTc); }
    catch { beds.gain.value = on ? R.duck : 1; }
  }

  function play(name, opts = {}) {
    if (disposed || !cues[name]) return false;
    if (!ctx && !makeContext()) return false;
    if (doc && doc.hidden) return false;
    const v = volume();
    if (v <= 0) return false;
    if (master.gain.value !== v) master.gain.value = v;
    const varied = ['hop', 'hopland', 'land', 'stomp', 'headbutt', 'whip', 'launch', 'rebound', 'kick'].includes(name);
    const mass = { p: 1.08, n: 1, b: 1.04, r: .89, q: .96, k: .86 }[opts.piece] || 1;
    cuePitch = varied ? mass * (.97 + Math.random() * .06) : 1;
    let made;
    try { made = cues[name](opts); } catch (e) { console.warn('[pbp] cue failed ' + name, e); return false; }
    finally { sink = null; stretch = 1; }
    log.push({ name, opts, pitch: cuePitch, at: ctx.currentTime, state: ctx.state, ...(Array.isArray(made) ? { cues: made } : {}) });
    if (log.length > TUNING.logSize) log.shift();
    return true;
  }

  // --- the bus --------------------------------------------------------------
  const offs = [];
  const on = (type, fn) => { if (bus && bus.on) offs.push(bus.on(type, fn)); };
  let held = null;
  let legal = [];
  let hover = null;
  let checkArmed = false;   // `check` comes before `turn` on the same move
  let checkTimer = 0;
  let lastSecond = -1;
  let over = false;

  function stopPulse() { if (checkTimer) { clearInterval(checkTimer); checkTimer = 0; } }
  function startPulse() {
    stopPulse();
    play('check');
    checkTimer = setInterval(() => play('check'), TUNING.check.everySec * 1000);
  }

  on('grab', (p) => {
    play('grab');
    held = group ? group.children.find((c) => c.userData && c.userData.held) || null : null;
    legal = game && game.legalTargets && p && p.square ? game.legalTargets(p.square) : [];
    hover = p && p.square ? p.square : null;
  });
  on('dragmove', () => {
    if (!held || !squareOf) return;
    const sq = squareOf(held.position.x, held.position.z);
    if (sq === hover) return;
    hover = sq;
    if (sq && legal.includes(sq)) play('tick');
  });
  on('drop', (p) => {
    held = null; legal = []; hover = null;
    if (!p || !p.ok) play('boing');
  });
  on('land', (p) => {
    if (!p || p.refused || p.skipped) return;
    play(p.capture && !['whip', 'signature'].includes(p.manner) ? 'capture' : 'land', { height: p.height });
  });
  on('captureCue', p => { if (p?.name) play(p.name); });
  on('hit', p => { for (const name of hitCues(p)) play(name, name === 'rebound' ? {} : { piece: p?.piece, height: p?.height }); });
  // the capture replay (board/director.js): its own cues, never the live ones above
  on('replay-show', p => { duck(true); play('replayIn', { layout: p?.layout, n: p?.n }); });
  on('replay-panel-in', p => play('replaySlam', { i: p?.i | 0, layout: p?.layout }));
  on('replay-panel-hit', p => play('replayHit', { i: p?.i | 0, layout: p?.layout, hit: p?.hit || null }));
  on('replay-exit', p => play(p?.skipped ? 'replayStop' : 'replayOut', { layout: p?.layout }));
  on('replay-done', () => duck(false));
  for (const type of ['local', 'newgame', 'menu', 'takeback', 'gameover']) on(type, () => { if (ducked) { fadeReplay(.05); duck(false); } });
  on('check', () => { checkArmed = true; startPulse(); });
  on('turn', () => {
    lastSecond = -1;
    if (checkArmed) checkArmed = false; else stopPulse();
    try {
      const chess = game && game.rules && game.rules.chess;
      const hist = chess && chess.history ? chess.history({ verbose: true }) : [];
      const last = hist[hist.length - 1];
      if (last && last.promotion) play('promote');
    } catch { /* the referee keeps its own counsel */ }
  });
  on('gameover', (p) => {
    over = true;
    stopPulse();
    const r = p && p.result;
    play(r === 'stalemate' || r === 'draw' ? 'draw' : 'mate');
  });
  on('clock', (s) => {
    if (over || !s || !s.active) return;
    const ms = s[s.active];
    const low = ms < TUNING.room.lowClockMs;
    if (low !== lowClock) { lowClock = low; applyRoom(); }
    if (!(ms < TUNING.clock.underMs) || ms <= 0) return;
    const second = Math.ceil(ms / 1000);
    if (second === lastSecond) return;
    lastSecond = second;
    play('clock', { sharp: ms < TUNING.clock.sharpMs });
  });

  const crowd = createCrowd({ bus, game, play, settled: () => !win?.PBP?.board?.anim?.busy?.(), cancel: () => crowdVoices?.cancel(), canPlay: () => {
    const door = win?.PBP?.door;
    return !disposed && ctx?.state === 'running' && !doc?.hidden && volume() > 0
      && (!door?.isUp?.() || door?.screen?.() === 'end');
  } });
  on('local', () => { over = false; stopPulse(); lastSecond = -1; });

  // --- the video card, seen not heard from -----------------------------------
  let observer = null;
  const isCard = (n) => n && n.classList && n.classList.contains('pbp-card');
  if (root && typeof MutationObserver === 'function') {
    observer = new MutationObserver((records) => {
      for (const r of records) {
        if (r.type === 'attributes') {
          if (isCard(r.target) && r.target.classList.contains('is-out') && !r.target.dataset.pbpClosed) {
            r.target.dataset.pbpClosed = '1';
            play('cardClose');
          }
          continue;
        }
        for (const n of r.addedNodes) if (isCard(n)) play('cardOpen');
        for (const n of r.removedNodes) if (isCard(n) && !n.dataset.pbpClosed) play('cardClose');
      }
    });
    observer.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
  }

  if (doc) {
    doc.addEventListener('pointerdown', wake, { capture: true, passive: true });
    doc.addEventListener('keydown', wake, { capture: true, passive: true });
    doc.addEventListener('visibilitychange', onVisibility);
  }

  return {
    play,
    wake,
    crowd,
    update() {
      crowd.update();
      // The director can drop a replay without replay-exit/replay-done (a new
      // move, the menu, a reset): once it no longer shows one, stop ducking and
      // let the slowed hits still ringing go.
      if (!ducked) return;
      const director = win?.PBP?.board?.director;
      if (director && typeof director.active === 'function' && !director.active()) { fadeReplay(.1); duck(false); }
    },
    log: () => log.slice(),
    ready: () => !!ctx,
    state: () => ({
      context: ctx ? ctx.state : 'none', volume: master ? master.gain.value : 0, pulsing: !!checkTimer, over, hover,
      meter, wet: +wetLevel.toFixed(3), drift: Math.round(drift), lowClock, room: !!wet,
      ducked, beds: beds ? +beds.gain.value.toFixed(3) : null,
      trance: trance ? trance.debug() : null,
    }),
    setMeter,
    setVolume(v) { if (v <= 0) crowd.cancel(); settings().sfxVolume = clamp01(v); if (master) master.gain.value = doc && doc.hidden ? 0 : volume(); },
    /** The Esc pause. On: everything on the master (sfx, crowd, the Distraction bed and whispers) fades out
     *  in about 0.1 s. Off: it comes back slowly (about 3.5 s to full), so a trance bed never snaps back. */
    hush(on) {
      hushed = !!on;
      if (hushed) crowd.cancel();
      if (!master || !ctx) return;
      const to = doc && doc.hidden ? 0 : volume();
      try { master.gain.cancelScheduledValues(ctx.currentTime); master.gain.setTargetAtTime(to, ctx.currentTime, hushed ? 0.03 : 1.2); } catch { master.gain.value = to; }
    },
    isHushed: () => hushed,
    dispose() {
      disposed = true;
      crowd.dispose();
      stopPulse();
      if (duckTimer) { clearTimeout(duckTimer); duckTimer = 0; }
      if (trance) { trance.dispose(); trance = null; }
      for (const off of offs) { try { off(); } catch { /* gone */ } }
      if (observer) observer.disconnect();
      if (doc) {
        doc.removeEventListener('pointerdown', wake, { capture: true });
        doc.removeEventListener('keydown', wake, { capture: true });
        doc.removeEventListener('visibilitychange', onVisibility);
      }
      if (ctx && ctx.close) { try { ctx.close(); } catch { /* already */ } }
      ctx = null; master = null; wet = null; beds = null; deepIn = null; tailOut = null; replayGain = null;
    },
  };
}
