import { pentatonic, ROOT_HZ } from '../../shared/sound/kit.js';

/* ============================================================================
 * stations/breakout/audio.js - the cabinet's ears. Everything synthesised
 * (oscillators, one shared noise buffer, biquads), nothing fetched, so the dev
 * harness works offline. See SPEC.md "Audio interface".
 *
 *   createAudio({ bpm, master, AudioContext }) ->
 *     start() stop() destroy() now() beat setSaturation setState hit relapse
 *     breakout crack wallCleared split nearMiss perfect jackpot shatterWall
 *     brickLand popOut burst setTimeScale (bed pitch for slow-mo; grey adds a vinyl wobble)
 *
 * Graph:  bed bus -> low-pass (the saturation filter) -> master -> out
 *         sfx bus -> master (hits carry their own saturation-scaled low-pass,
 *                            so the combo ladder always rings)
 *         sub bus -> master (pulse, thud, crack: the low end is never filtered)
 *         one small delay room on the sfx bus for the wetter cues
 *
 * The bed is a 4-bar loop in C pentatonic on a lookahead scheduler (a 25 ms
 * interval writing 120 ms ahead), so it never drifts against the ball. Before
 * start() the beat math runs off performance.now(), so the game can pace the
 * ball before the browser lets audio through.
 * ==========================================================================*/

const SEMI = s => 2 ** (s / 12);
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const num = (v, d) => (Number.isFinite(Number(v)) ? Number(v) : d);

export const CUTOFF_MIN = 300, CUTOFF_MAX = 12000, GREY_CUTOFF = 250;
export const MELODY_FROM = 0.4, ARP_FROM = 0.7, LAYER_FADE = 0.2;
export const MAX_COMBO = 14;
export const LOOKAHEAD_S = 0.12, TICK_MS = 25, MIN_LEAD_S = 0.015;
export const STEPS_PER_BEAT = 4, BEATS_PER_BAR = 4, BARS = 4;
export const LOOP_STEPS = STEPS_PER_BEAT * BEATS_PER_BAR * BARS;
const HIT_KINDS = ['wall', 'paddle', 'brick', 'gif', 'spiral'];

/** Saturation 0..1 -> the bed's low-pass cutoff, exponential 300 Hz .. 12 kHz. */
export const cutoffFor = s => CUTOFF_MIN * (CUTOFF_MAX / CUTOFF_MIN) ** clamp(num(s, 0), 0, 1);
/** A layer's gain: silent up to `from`, full LAYER_FADE above it. */
export const layerLevel = (s, from) => clamp((num(s, 0) - from) / LAYER_FADE, 0, 1);
/** Semitones above the root for a hit at `combo` (pentatonic ladder, capped at MAX_COMBO). */
export const hitSemis = combo => pentatonic(clamp(Math.floor(num(combo, 0)), 0, MAX_COMBO));
/** A hit's own low-pass in colour: 1.2 kHz flat and grey, 9.6 kHz at full juice. */
export const hitCutoff = s => 1200 * 8 ** clamp(num(s, 0), 0, 1);
/** Slow-mo: the bed's detune in cents for a time scale, 0 at 1 down to -200 (two semitones) at 0.35. */
export const SLOWMO_SCALE = 0.35, SLOWMO_CENTS = -200, WOBBLE_CENTS = 8, WOBBLE_HZ = 0.5;
export const timeScaleCents = s => SLOWMO_CENTS * clamp((1 - num(s, 1)) / (1 - SLOWMO_SCALE), 0, 1) + 0;

/** Pure beat math. `origin` is the clock time of step 0; rebase() moves it when the clock changes. */
export function createBeat(bpm = 96, origin = 0) {
  const spb = 60 / clamp(num(bpm, 96), 30, 300), step = spb / STEPS_PER_BEAT;
  const beat = {
    spb, sixteenth: step, bar: spb * BEATS_PER_BAR, loop: step * LOOP_STEPS, origin,
    rebase(o) { beat.origin = num(o, beat.origin); },
    phase(now) { const p = ((num(now, 0) - beat.origin) / spb) % 1; return p < 0 ? p + 1 : p; },
    stepIndex(now) { return Math.floor((num(now, 0) - beat.origin) / step + 1e-6); },
    nextSixteenth(now) { return beat.origin + (beat.stepIndex(now) + 1) * step; },
    /** The boundary a hit lands on: the next one, or the one after when the next is under `lead` away. */
    quantise(now, lead = MIN_LEAD_S) { const t = beat.nextSixteenth(now); return t - num(now, 0) < lead ? t + step : t; },
  };
  return beat;
}

/* ---- THE LOOP. Semitones above the root; the pad sits an octave down, the arp an octave up. ---- */
const CHORDS = [[0, 4, 7], [-3, 0, 4], [-5, 0, 4], [-3, 2, 7]];   // C E G / A C E / G C E / A D G
/** [step, pentatonic index, duration in steps]: a sparse tune that leans on the chord tones. */
const MELODY = [
  [0, 4, 3], [6, 2, 2], [10, 3, 4],
  [16, 5, 2], [20, 4, 3], [26, 2, 4],
  [32, 3, 2], [36, 6, 3], [42, 5, 4],
  [48, 4, 2], [52, 2, 2], [56, 1, 3], [60, 0, 4],
];

export function createAudio({ bpm = 96, master = 0.8, AudioContext: AC = null } = {}) {
  const perf = () => (typeof performance !== 'undefined' ? performance.now() : Date.now()) / 1000;
  const perfOrigin = perf();
  const beat = createBeat(bpm, 0);
  let ctx = null, out = null, lp = null, room = null, noiseBuf = null, timer = 0, destroyed = false, running = false;
  const bus = { bed: null, sfx: null, sub: null };
  const layer = { melody: null, arp: null };
  // The bed's shared detune inputs: a constant for slow-mo pitch and a slow LFO for the grey vinyl wobble.
  let bedDetune = null, wobbleGain = null, timeScale = 1;
  let saturation = 0, state = 'colour', stepIndex = 0, nextStepTime = 0;
  const level = clamp(num(master, 0.8), 0, 1);

  const Ctor = () => AC || globalThis.AudioContext || globalThis.webkitAudioContext || null;
  const live = () => !!ctx && !destroyed && ctx.state !== 'closed';
  const now = () => (ctx ? ctx.currentTime : perf() - perfOrigin);

  /* ---- graph ---- */
  function build() {
    const C = Ctor();
    if (!C) return false;
    try {
      ctx = new C();
      out = ctx.createGain(); out.gain.value = level; out.connect(ctx.destination);
      lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.Q.value = 0.8; lp.frequency.value = cutoffFor(saturation); lp.connect(out);
      for (const k of Object.keys(bus)) { const g = ctx.createGain(); g.gain.value = 1; g.connect(k === 'bed' ? lp : out); bus[k] = g; }
      for (const k of Object.keys(layer)) { const g = ctx.createGain(); g.gain.value = 0.0001; g.connect(bus.bed); layer[k] = g; }
      // The room: one short delay fed back under a low-pass, tapped by the wetter sfx cues.
      const delay = ctx.createDelay(1); delay.delayTime.value = 0.11;
      const fb = ctx.createGain(); fb.gain.value = 0.36;
      const tone = ctx.createBiquadFilter(); tone.type = 'lowpass'; tone.frequency.value = 3200;
      delay.connect(tone); tone.connect(fb); fb.connect(delay); tone.connect(bus.sfx);
      room = ctx.createGain(); room.gain.value = 0.28; room.connect(delay);
      const n = ctx.createBuffer(1, Math.floor(ctx.sampleRate * 2), ctx.sampleRate), d = n.getChannelData(0);
      for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
      noiseBuf = n;
      try {                                                           // detune inputs are optional: an old context just plays straight
        if (typeof ctx.createConstantSource === 'function') {
          bedDetune = ctx.createConstantSource(); bedDetune.offset.value = timeScaleCents(timeScale); bedDetune.start(0);
        }
        const lfo = ctx.createOscillator(); lfo.type = 'sine'; lfo.frequency.value = WOBBLE_HZ;
        wobbleGain = ctx.createGain(); wobbleGain.gain.value = state === 'grey' ? WOBBLE_CENTS : 0;
        lfo.connect(wobbleGain); lfo.start(0);
      } catch (e) { bedDetune = null; wobbleGain = null; }
    } catch (e) { ctx = null; out = null; lp = null; room = null; return false; }
    return true;
  }
  const isBedDest = d => d === bus.bed || d === layer.melody || d === layer.arp;
  function envelope(g, t, dur, lvl, attack) {
    const a = Math.max(0.003, Math.min(dur * 0.9, dur * (attack == null ? 0.01 : attack)));
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(Math.max(0.0002, lvl), t + a);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  }
  /**
   * One voice at absolute time `t` onto `dest`. n = { k:'tone'|'noise', wave, hz, hzTo, dur, level, attack,
   * lp, lpTo, q, type, pan, wet }. Every node disconnects itself when the source ends.
   */
  function voice(n, t, dest) {
    if (!live()) return;
    const dur = Math.max(0.005, n.dur), g = ctx.createGain(), nodes = [g];
    let src, head;
    if (n.k === 'noise') {
      src = ctx.createBufferSource(); src.buffer = noiseBuf; src.loop = true;
      const f = ctx.createBiquadFilter(); f.type = n.type || 'bandpass'; f.Q.value = n.q || 1;
      f.frequency.setValueAtTime(n.hz, t);
      if (n.hzTo && n.hzTo !== n.hz) f.frequency.exponentialRampToValueAtTime(n.hzTo, t + dur);
      src.connect(f); head = f; nodes.push(f);
    } else {
      src = ctx.createOscillator(); src.type = n.wave || 'sine';
      src.frequency.setValueAtTime(n.hz, t);
      if (n.hzTo && n.hzTo !== n.hz) src.frequency.exponentialRampToValueAtTime(n.hzTo, t + dur);
      if (src.detune && isBedDest(dest)) {                            // the bed follows slow-mo and the grey wobble
        try { if (bedDetune) bedDetune.connect(src.detune); if (wobbleGain) wobbleGain.connect(src.detune); } catch (e) { /* straight */ }
      }
      head = src;
    }
    if (n.lp) {
      const f = ctx.createBiquadFilter(); f.type = 'lowpass'; f.frequency.setValueAtTime(n.lp, t);
      if (n.lpTo) f.frequency.exponentialRampToValueAtTime(n.lpTo, t + dur);
      head.connect(f); head = f; nodes.push(f);
    }
    nodes.push(src);
    envelope(g, t, dur, n.level, n.attack);
    head.connect(g);
    let tail = g;
    if (n.pan != null && typeof ctx.createStereoPanner === 'function') {
      const p = ctx.createStereoPanner(); p.pan.value = clamp((num(n.pan, 0.5) - 0.5) * 1.6, -0.8, 0.8);
      g.connect(p); tail = p; nodes.push(p);
    }
    tail.connect(dest);
    if (n.wet && room) tail.connect(room);
    src.onended = () => { for (const x of nodes) { try { x.disconnect(); } catch (e) { /* gone */ } } };
    src.start(t); src.stop(t + dur + 0.02);
  }
  const play = (notes, t, dest) => { for (const n of notes) { try { voice(n, t + (n.at || 0), dest); } catch (e) { /* a note never breaks a beat */ } } };
  const tone = (hz, dur, level, o = {}) => ({ k: 'tone', hz, dur, level, ...o });
  const noise = (hz, dur, level, o = {}) => ({ k: 'noise', hz, dur, level, ...o });
  /** Move a param to `to` over `secs`, starting `at` (audio time; default now). Exponential for Hz, linear for gain. */
  function glide(param, to, secs, at = null, exp = false) {
    const t = at == null ? ctx.currentTime : at, v = Math.max(0.0001, to);
    try {
      param.cancelScheduledValues(t);
      param.setValueAtTime(Math.max(0.0001, param.value), t);
      if (exp) param.exponentialRampToValueAtTime(v, t + secs); else param.linearRampToValueAtTime(v, t + secs);
    } catch (e) { param.value = v; }
  }

  /* ---- the bed: one sixteenth at a time, written LOOKAHEAD_S ahead ---- */
  function scheduleStep(i, t) {
    const s = i % LOOP_STEPS, bar = Math.floor(s / (STEPS_PER_BEAT * BEATS_PER_BAR)), chord = CHORDS[bar % CHORDS.length];
    const grey = state === 'grey';
    if (s % STEPS_PER_BEAT === 0) {                                   // THE PULSE: every beat, the sub's heartbeat
      play([tone(96, 0.2, 0.2 * (0.75 + 0.25 * saturation), { hzTo: 50, attack: 0.02 })], t, bus.sub);
    } else if (s % STEPS_PER_BEAT === 2 && saturation >= 0.5 && !grey) { // the off-beat ghost above half juice
      play([tone(88, 0.12, 0.07, { hzTo: 50, attack: 0.02 })], t, bus.sub);
    }
    if (s % (STEPS_PER_BEAT * BEATS_PER_BAR) === 0) {                 // THE PAD: a chord a bar, overlapping its neighbour
      play(chord.map(semi => tone(ROOT_HZ / 2 * SEMI(semi), beat.bar + 0.4, 0.045, { wave: 'triangle', attack: 0.3, lp: 1500 })), t, bus.bed);
      play([tone(ROOT_HZ / 4 * SEMI(chord[0]), beat.bar + 0.2, 0.05, { attack: 0.25 })], t, bus.bed);
    }
    for (const [at, k, len] of MELODY) if (at === s) {               // THE MELODY: fades in above MELODY_FROM
      const hz = ROOT_HZ * SEMI(pentatonic(k));
      play([tone(hz, len * beat.sixteenth, 0.07, { attack: 0.08 }), tone(hz * 2, len * beat.sixteenth * 0.6, 0.012, { attack: 0.1 })], t, layer.melody);
    }
    if (s % 2 === 0) {                                                // THE ARP: eighth-note plucks, fades in above ARP_FROM
      const semi = chord[(s / 2) % chord.length] + (s % 8 === 6 ? 24 : 12);
      play([tone(ROOT_HZ * SEMI(semi), 0.13, 0.035, { wave: 'triangle', attack: 0.02, lp: 4200 })], t, layer.arp);
    }
  }
  function pump() {
    if (!running || !live()) return;
    const cur = ctx.currentTime;
    if (nextStepTime < cur) {                                         // the tab slept: skip, never catch up in a burst
      const k = Math.ceil((cur - nextStepTime) / beat.sixteenth); stepIndex += k; nextStepTime += k * beat.sixteenth;
    }
    const until = cur + LOOKAHEAD_S;
    for (let i = 0; i < 32 && nextStepTime < until; i++) { scheduleStep(stepIndex, nextStepTime); stepIndex++; nextStepTime += beat.sixteenth; }
  }

  /* ---- saturation and state ---- */
  function applyState(secs, at = null) {
    if (!live()) return;
    const grey = state === 'grey';
    glide(lp.frequency, grey ? GREY_CUTOFF : cutoffFor(saturation), secs, at, true);
    glide(layer.melody.gain, grey ? 0 : layerLevel(saturation, MELODY_FROM), secs, at);
    glide(layer.arp.gain, grey ? 0 : layerLevel(saturation, ARP_FROM), secs, at);
    if (wobbleGain) glide(wobbleGain.gain, grey ? WOBBLE_CENTS : 0, Math.max(secs, 0.3), at);   // the vinyl wobble, grey only
  }

  /* ---- the hit palette ---- */
  function hitNotes(kind, combo, x) {
    const hz = ROOT_HZ * SEMI(hitSemis(combo)), pan = clamp(num(x, 0.5), 0, 1);
    if (state === 'grey') {                                           // dull, dry, low-passed: the Old Self's cabinet
      const dur = kind === 'wall' ? 0.05 : kind === 'paddle' ? 0.1 : 0.12;
      return [tone(hz / 2, dur, 0.1, { wave: 'triangle', lp: 500, pan })];
    }
    const cut = hitCutoff(saturation);
    switch (kind) {
      case 'wall': return [noise(1500 * SEMI(hitSemis(combo) / 2), 0.022, 0.06, { q: 3, pan }), tone(hz / 2, 0.02, 0.03, { pan })];
      case 'paddle': return [tone(hz / 2, 0.17, 0.13, { wave: 'triangle', lp: Math.min(cut, 1800), pan }), tone(hz / 2, 0.12, 0.06, { attack: 0.02, pan })];
      case 'brick': return [
        tone(hz, 0.28, 0.12, { lp: cut, pan, wet: true }), tone(hz * 2.76, 0.11, 0.028, { lp: cut, pan }),
        noise(6000, 0.012, 0.045, { q: 0.8, pan })];
      case 'gif': return [
        // Half the gif bounce (owner, 2026-09-18: too loud next to the bricks).
        tone(hz, 0.5, 0.055, { hzTo: hz * 0.72, attack: 0.03, lp: cut, pan, wet: true }),
        tone(hz * 1.006, 0.45, 0.03, { hzTo: hz * 0.72, wave: 'triangle', attack: 0.04, lp: Math.min(cut, 2400), pan, wet: true }),
        noise(2400, 0.3, 0.018, { hzTo: 900, q: 1.2, attack: 0.15, pan, wet: true })];
      case 'spiral': return [
        // The spiral's pull, halved in level and in how high the sweep climbs (owner, 2026-09-18).
        noise(700, 0.42, 0.035, { hzTo: 2600, q: 2.2, attack: 0.35, pan, wet: true }),
        tone(hz * 2, 0.36, 0.022, { hzTo: hz * 1.5, attack: 0.3, lp: cut, pan, wet: true })];
      default: return [];
    }
  }

  const api = {
    beat,
    now,
    /** Build or resume the context inside a gesture. Safe to call on every pointer event. */
    start() {
      if (destroyed) return false;
      if (!ctx && !build()) return false;
      if (ctx.state === 'suspended') ctx.resume().catch(() => {});
      if (!running) {
        running = true;
        stepIndex = Math.max(0, beat.stepIndex(perf() - perfOrigin));   // carry the fallback clock's position into the loop
        nextStepTime = ctx.currentTime + 0.05;
        beat.rebase(nextStepTime - stepIndex * beat.sixteenth);          // the grid the hits quantise to IS the bed's grid
        applyState(0.01);
        pump();
        timer = setInterval(pump, TICK_MS);
        if (timer && typeof timer.unref === 'function') timer.unref();
      }
      return true;
    },
    /** Hold everything; start() brings it back on the same grid (ctx time stops with it). */
    stop() { if (live() && ctx.state === 'running') ctx.suspend().catch(() => {}); },
    destroy() {
      destroyed = true; running = false;
      if (timer) { clearInterval(timer); timer = 0; }
      if (ctx) { try { ctx.close().catch(() => {}); } catch (e) { /* already */ } }
      ctx = null; out = null; lp = null; room = null; noiseBuf = null; bedDetune = null; wobbleGain = null;
    },
    setSaturation(s) {
      saturation = clamp(num(s, saturation), 0, 1);
      if (state === 'colour') applyState(0.25);
    },
    setState(next) {
      const want = next === 'grey' ? 'grey' : 'colour';
      if (want === state) return;
      state = want;
      applyState(want === 'grey' ? 0.06 : 0.4);
    },
    /** A hit on the next sixteenth (the one after when the next is under 15 ms away). `x` 0..1 pans. */
    hit(kind, { combo = 0, x = 0.5 } = {}) {
      if (!running || !live() || !HIT_KINDS.includes(kind)) return 0;
      const t = beat.quantise(ctx.currentTime);
      play(hitNotes(kind, combo, x), t, bus.sfx);
      return t;
    },
    /** THE THUD: a sine dropping 120 -> 40 Hz over 250 ms and a noise burst, now, and the filter slams shut. */
    relapse() {
      if (!running || !live()) return;
      const t = ctx.currentTime;
      play([tone(120, 0.25, 0.5, { hzTo: 40 })], t, bus.sub);
      play([noise(900, 0.07, 0.16, { type: 'lowpass' }), noise(200, 0.3, 0.08, { type: 'lowpass', hzTo: 80, attack: 0.05 })], t, bus.sfx);
      api.setState('grey');
    },
    /** THE SHATTER: glass, then a riser up over 350 ms and a whoosh, then the filter opens over 400 ms. */
    breakout() {
      if (!running || !live()) return;
      const t = ctx.currentTime;
      play([
        noise(5200, 0.16, 0.14, { hzTo: 2600, q: 0.7 }), noise(8000, 0.05, 0.08, { q: 2 }),
        ...[0.03, 0.07, 0.12, 0.18].map((at, i) => tone(3200 * SEMI(i * 3), 0.09, 0.03, { at, wet: true })),
        noise(300, 0.35, 0.09, { hzTo: 4000, q: 1.4, attack: 0.5 }),                       // the riser
        tone(180, 0.35, 0.06, { hzTo: 720, wave: 'triangle', attack: 0.4, lp: 600, lpTo: 3000 }),
        noise(600, 0.45, 0.1, { at: 0.3, hzTo: 2200, q: 0.9, attack: 0.25, wet: true }),    // the whoosh
        tone(ROOT_HZ * 2, 0.6, 0.05, { at: 0.35, attack: 0.1, wet: true }),
      ], t, bus.sfx);
      play([tone(70, 0.4, 0.3, { at: 0.35, hzTo: 45, attack: 0.05 })], t, bus.sub);
      if (state !== 'colour') { state = 'colour'; applyState(0.4, t + 0.35); }
    },
    /** THE CRACK: one deep low crack, the field's hairline fracture. */
    crack() {
      if (!running || !live()) return;
      const t = ctx.currentTime;
      play([tone(70, 0.5, 0.45, { hzTo: 30 })], t, bus.sub);
      play([noise(3000, 0.03, 0.12, { q: 1.5 }), noise(420, 0.16, 0.2, { type: 'lowpass', hzTo: 120 })], t, bus.sfx);
    },
    /** A wall cleared: five bells climbing the pentatonic an octave up, 90 ms apart. */
    wallCleared() {
      if (!running || !live()) return;
      const t = beat.quantise(ctx.currentTime), notes = [];
      for (let k = 0; k < 5; k++) {
        const hz = ROOT_HZ * SEMI(12 + pentatonic(k)), at = k * 0.09;
        notes.push(tone(hz, 0.34, 0.11, { at, wet: true }), tone(hz * 2.76, 0.12, 0.025, { at }));
      }
      play(notes, t, bus.sfx);
    },
    /** Slow-mo: the bed drops two semitones at 0.35 and comes back at 1 (glide 150 ms). Signed, so not glide(). */
    setTimeScale(s) {
      timeScale = clamp(num(s, 1), 0.05, 2);
      if (!bedDetune || !live()) return;
      const t = ctx.currentTime, p = bedDetune.offset;
      try { p.cancelScheduledValues(t); p.setValueAtTime(p.value, t); p.linearRampToValueAtTime(timeScaleCents(timeScale), t + 0.15); }
      catch (e) { p.value = timeScaleCents(timeScale); }
    },
    /** A near miss: one short high ping, dry. */
    nearMiss() {
      if (!running || !live()) return;
      play([tone(ROOT_HZ * 4, 0.07, 0.07, { hzTo: ROOT_HZ * 3.6, pan: 0.5 }), noise(7000, 0.015, 0.03, { q: 2 })], ctx.currentTime, bus.sfx);
    },
    /** A perfect paddle hit: a bright two-note stamp on the grid (root, then the fifth an octave up). */
    perfect() {
      if (!running || !live()) return;
      const t = beat.quantise(ctx.currentTime), hz = ROOT_HZ * 2;
      play([tone(hz, 0.16, 0.12, { wet: true }), tone(hz * SEMI(7), 0.28, 0.12, { at: 0.07, wet: true }),
        tone(hz * SEMI(7) * 2.76, 0.1, 0.03, { at: 0.07 })], t, bus.sfx);
    },
    /** The jackpot: six bells up the scale on the grid, 80 ms apart, and a warm sub under the last one. */
    jackpot() {
      if (!running || !live()) return;
      const t = beat.quantise(ctx.currentTime), notes = [];
      const steps = [0, 2, 4, 5, 7, 9];
      steps.forEach((k, i) => { const hz = ROOT_HZ * SEMI(pentatonic(k) + (i > 3 ? 12 : 0)), at = i * 0.08;
        notes.push(tone(hz, 0.3 + i * 0.05, 0.11, { at, wet: true }), tone(hz * 2, 0.12, 0.03, { at, wave: 'triangle' })); });
      play(notes, t, bus.sfx);
      play([tone(90, 0.6, 0.3, { at: 0.4, hzTo: 45, attack: 0.05 })], t, bus.sub);
    },
    /** A wall shattered whole: a big glass break, then a sub drop. */
    shatterWall() {
      if (!running || !live()) return;
      const t = ctx.currentTime;
      play([
        noise(6000, 0.24, 0.18, { hzTo: 1800, q: 0.6 }), noise(9000, 0.08, 0.1, { q: 2 }),
        ...[0, 0.04, 0.09, 0.15, 0.22, 0.3].map((at, i) => tone(2400 * SEMI(i * 4 + (i % 2) * 1), 0.12, 0.035, { at, wet: true })),
        noise(400, 0.5, 0.08, { hzTo: 120, type: 'lowpass', attack: 0.02 }),
      ], t, bus.sfx);
      play([tone(110, 0.55, 0.45, { at: 0.06, hzTo: 36, attack: 0.02 })], t, bus.sub);
    },
    /** One soft tick per landing brick as a new wall settles. Dry, tiny, on the grid. */
    brickLand({ x = 0.5 } = {}) {
      if (!running || !live()) return;
      play([tone(ROOT_HZ / 2, 0.04, 0.04, { wave: 'triangle', lp: 1400, pan: clamp(num(x, 0.5), 0, 1) })], ctx.currentTime, bus.sfx);
    },
    /** The multiball split: two voices a few cents apart, blipping up. */
    split() {
      if (!running || !live()) return;
      const t = ctx.currentTime, hz = ROOT_HZ * 2;
      play([
        tone(hz, 0.16, 0.09, { hzTo: hz * SEMI(7), wave: 'triangle', lp: 5000, pan: 0.35, wet: true }),
        tone(hz * 1.012, 0.16, 0.09, { hzTo: hz * SEMI(7) * 1.012, wave: 'triangle', lp: 5000, pan: 0.65, wet: true }),
      ], t, bus.sfx);
    },
    /** A GIF brick knocked out of the wall: a short woody knock and a tick, panned to the brick. */
    popOut({ x = 0.5 } = {}) {
      if (!running || !live()) return;
      const p = clamp(num(x, 0.5), 0, 1);
      play([tone(320, 0.09, 0.1, { hzTo: 170, wave: 'triangle', lp: 2200, pan: p }), noise(2600, 0.02, 0.05, { q: 1.5, pan: p })], ctx.currentTime, bus.sfx);
    },
    /** The bubble bursting: a wet pop, two quick blips up, a little air. */
    burst({ x = 0.5 } = {}) {
      if (!running || !live()) return;
      const p = clamp(num(x, 0.5), 0, 1), hz = ROOT_HZ * 2;
      play([
        noise(1800, 0.05, 0.12, { hzTo: 4200, q: 0.9, pan: p }),
        tone(hz, 0.09, 0.08, { hzTo: hz * SEMI(5), wave: 'triangle', lp: 5000, pan: p, wet: true }),
        tone(hz * SEMI(7), 0.12, 0.06, { at: 0.05, hzTo: hz * SEMI(12), wave: 'triangle', lp: 5000, pan: p, wet: true }),
      ], ctx.currentTime, bus.sfx);
    },
    /** Test and tuning seams. */
    pump,
    get context() { return ctx; },
    get running() { return running; },
    get state() { return state; },
    get saturation() { return saturation; },
    get timeScale() { return timeScale; },
    get wobbleDepth() { return wobbleGain ? wobbleGain.gain.value : 0; },
    get bedDetuneCents() { return bedDetune ? bedDetune.offset.value : 0; },
    setBus(name, v) { const g = bus[name]; if (g && live()) glide(g.gain, clamp(num(v, 1), 0, 1), 0.05); },
    setMaster(v) { if (out && live()) glide(out.gain, clamp(num(v, level), 0, 1), 0.05); },
  };
  return api;
}

export default createAudio;
