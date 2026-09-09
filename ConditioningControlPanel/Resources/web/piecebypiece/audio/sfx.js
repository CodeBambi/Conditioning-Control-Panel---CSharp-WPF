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
 *   capture    the thud plus a squelch     `land` with capture
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
  logSize: 32,
});

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
  let disposed = false;
  let wet = null;            // the delay's return, beside the master
  let meter = 0;
  let wetLevel = 0;          // what the wet gain was last asked for
  let drift = 0;             // cents, applied to every new voice
  let lowClock = false;      // the mover is under lowClockMs
  const log = [];
  const settings = () => (win && win.PBP && win.PBP.settings) || {};
  const volume = () => {
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
    return ctx;
  }
  function wake() {
    const c = makeContext();
    if (c && c.state === 'suspended' && c.resume) { try { c.resume(); } catch { /* not yet */ } }
  }
  function onVisibility() {
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
  function setMeter(m) { meter = clamp01(m); applyRoom(); }
  const tune = (node) => { if (drift && node.detune) { try { node.detune.value = drift; } catch { /* a source without detune */ } } };

  // --- the palette ------------------------------------------------------------
  /** An oscillator with a gain envelope: attack straight up, decay to zero. */
  function tone(type, hz, sec, gain, { at = 0, slideTo = null, filterHz = null } = {}) {
    const t0 = ctx.currentTime + at;
    const osc = ctx.createOscillator();
    const env = ctx.createGain();
    osc.type = type;
    tune(osc);
    osc.frequency.setValueAtTime(hz, t0);
    if (slideTo) osc.frequency.exponentialRampToValueAtTime(Math.max(1, slideTo), t0 + sec);
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
    env.connect(master);
    osc.start(t0);
    osc.stop(t0 + sec + 0.02);
  }
  /** Filtered noise with a sweep, for taps and whooshes. */
  function hiss(sec, gain, { at = 0, from = 1000, to = 1000, type = 'bandpass', attack = null } = {}) {
    const t0 = ctx.currentTime + at;
    const src = ctx.createBufferSource();
    src.buffer = noise;
    tune(src);
    const f = ctx.createBiquadFilter();
    f.type = type;
    f.frequency.setValueAtTime(from, t0);
    if (to !== from) f.frequency.exponentialRampToValueAtTime(to, t0 + sec);
    const env = ctx.createGain();
    env.gain.setValueAtTime(0.0001, t0);
    env.gain.exponentialRampToValueAtTime(gain, t0 + (attack != null ? Math.min(attack, sec * 0.5) : Math.min(0.03, sec * 0.3)));
    env.gain.exponentialRampToValueAtTime(0.0001, t0 + sec);
    src.connect(f); f.connect(env); env.connect(master);
    src.start(t0);
    src.stop(t0 + sec + 0.02);
  }
  const thudHz = (height) => {
    const T = TUNING.thud;
    const k = clamp01((height - T.pawnH) / (T.kingH - T.pawnH));
    return T.pawnHz + (T.kingHz - T.pawnHz) * k;
  };

  const cues = {
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
  };

  function play(name, opts = {}) {
    if (disposed || !cues[name]) return false;
    if (!ctx && !makeContext()) return false;
    if (doc && doc.hidden) return false;
    const v = volume();
    if (v <= 0) return false;
    if (master.gain.value !== v) master.gain.value = v;
    try { cues[name](opts); } catch (e) { console.warn('[pbp] cue failed ' + name, e); return false; }
    log.push({ name, opts, at: ctx.currentTime, state: ctx.state });
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
    if (!p || p.refused) return;
    play(p.capture ? 'capture' : 'land', { height: p.height });
  });
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
    log: () => log.slice(),
    ready: () => !!ctx,
    state: () => ({
      context: ctx ? ctx.state : 'none', volume: master ? master.gain.value : 0, pulsing: !!checkTimer, over, hover,
      meter, wet: +wetLevel.toFixed(3), drift: Math.round(drift), lowClock, room: !!wet,
    }),
    setMeter,
    setVolume(v) { settings().sfxVolume = clamp01(v); if (master) master.gain.value = doc && doc.hidden ? 0 : clamp01(v); },
    dispose() {
      disposed = true;
      stopPulse();
      for (const off of offs) { try { off(); } catch { /* gone */ } }
      if (observer) observer.disconnect();
      if (doc) {
        doc.removeEventListener('pointerdown', wake, { capture: true });
        doc.removeEventListener('keydown', wake, { capture: true });
        doc.removeEventListener('visibilitychange', onVisibility);
      }
      if (ctx && ctx.close) { try { ctx.close(); } catch { /* already */ } }
      ctx = null; master = null; wet = null;
    },
  };
}
