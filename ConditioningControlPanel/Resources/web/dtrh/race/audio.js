/* ============================================================================
 * race/audio.js - every sound The Caucus Race makes. See race/AUDIO.md.
 *
 *   createRaceAudio({ bridge, hud, settings, input }) -> audio
 *   audio.sfx(name, scale)       the run's sfx() helper lands here: host legs + in-page beats
 *   audio.update(dt, live)       live = { world, run, kart }; once per step, after everything moved
 *   audio.duck(on, why)          'host' (native video) | 'brake' | 'end'
 *   audio.toggleMute() -> bool   M key; shared with the dive's master mute (shared/audioMute.js)
 *   audio.dispose()
 *
 * Two doors. The hot, latency-bound beats (pops, chimes, ticks, thumps, the
 * bed) are WebAudio in this page on the dive's shared context (engine/audioBus)
 * so the DtRH master colour applies. The fanfares the host already owns
 * (`Resources/sounds/chaos/*.mp3`) go out as `sfx` bridge messages using ONLY
 * names in HOST_SFX; a wrong name is silent on the host, so the set is closed.
 *
 * Sources of truth. In-page sounds come from the world's own events (field
 * pops, score rungs, item roll/arm/use) and from per-frame edges (airborne,
 * boost, drift, the Big Wheel span, the room). The `sfx(name)` calls in run.js
 * are the HOST legs; the router below swallows the ones an event already
 * covers so nothing sounds twice.
 *
 * bubbles.js plays its own kind-blind pop through makeSfxPlayer; that player
 * honours shared/audioMute isDucked(), so this module sets ducked for the
 * page's life and owns every pop (combo pitch, pan, per-kind file).
 * ==========================================================================*/

import { getAudioCtx, getMasterOut } from '../engine/audioBus.js';
import { isMuted, setMuted, onMuteChange, setDucked } from '../shared/audioMute.js';
import { audioUrl, altAudioUrl } from '../shared/audioSrc.js';
import { ROAD_HALF_W, KART_MIN_SPEED, KART_MAX_SPEED } from './consts.js';

export const SFX_BASE = '/dtrh/assets/bubbles/sfx/';
export const MAX_VOICES = 6;            // one-shots at once; the quietest is dropped for the 7th
export const PITCH_CAP_SEMIS = 7;       // the combo ladder tops out here
export const PITCH_STEP_COMBO = 4;      // +1 semitone per this many combo

/** Host catalogue names this page may send (verified against Resources/sounds/chaos/). */
export const HOST_SFX = new Set([
  'depth_change', 'pb_fanfare', 'streak_milestone', 'golden_pop', 'ui_click',
  'tunnel_powerup_collect', 'time_slow_in', 'time_slow_out', 'surface', 'thud',
]);

const FILES = { pop: 'Pop.mp3', pop2: 'Pop2.mp3', pop3: 'Pop3.mp3', chime1: 'chime1.mp3', chime2: 'chime2.mp3', chime3: 'chime3.mp3', burst: 'Burst.mp3', gg: 'GG.mp3' };
const POPS = ['pop', 'pop2', 'pop3'];
const LEVELS = { sfx: 1, pop: 0.34, effect: 0.42, whisper: 0.1, chime: 0.36, burst: 0.5, gg: 0.5, tick: 0.16, thud: 0.55 };
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const semisToRate = (s) => Math.pow(2, s / 12);

/** Pops climb with the combo: small steps, capped, so the chain sings without squeaking. */
export function pitchSemis(combo) { return clamp(Math.floor((combo | 0) / PITCH_STEP_COMBO), 0, PITCH_CAP_SEMIS); }

/** The rung ladder: chime1 -> chime2 -> chime3, then chime3 keeps rising with the multiplier. */
export function chimeFor(mult) {
  const m = Number(mult) || 1;
  if (m <= 2) return { file: 'chime1', semis: 0 };
  if (m <= 3) return { file: 'chime2', semis: 0 };
  if (m <= 4) return { file: 'chime3', semis: 0 };
  return { file: 'chime3', semis: clamp(Math.round((m - 4) * 1.5), 2, PITCH_CAP_SEMIS) };
}

/** Voice cap: the index of the quietest live voice (ties: the oldest). */
export function pickVoiceToDrop(voices) {
  let at = -1, low = Infinity;
  for (let i = 0; i < voices.length; i++) if (voices[i].level < low) { low = voices[i].level; at = i; }
  return at;
}

export function createRaceAudio({ bridge, hud, settings = {}, input } = {}) {
  const log = (m) => { try { bridge && bridge.log && bridge.log('[audio] ' + m); } catch (e) { /* host gone */ } };
  const send = (m) => { try { bridge && bridge.send && bridge.send(m); } catch (e) { /* host gone */ } };
  const masterLevel = clamp((Number(settings.masterVolume) == null || isNaN(Number(settings.masterVolume)) ? 60 : Number(settings.masterVolume)) / 100, 0, 1);
  const buffers = new Map();     // key -> AudioBuffer | 'pending' | 'failed'
  const voices = [];             // live one-shots: { src, gain, level, t0 }
  const live = { world: null, run: null, kart: null };   // what update() last saw (run/kart are live objects)
  const edge = { room: null, airborne: false, boost: 0, d: null, drift: false };
  let ctx = null, master = null, sfxBus = null, noise = null, popRR = 0, disposed = false, dropped = 0, dropLogT = 0;
  let lastScorePop = null;       // the score 'pop' event that ran just before the field pop reaches us

  setDucked(true);               // bubbles.js's kind-blind pop steps aside (see header)

  // ---- graph ----
  function ensureGraph() {
    if (master || disposed) return !!master;
    ctx = getAudioCtx();
    if (!ctx) return false;
    try {
      master = ctx.createGain(); master.gain.value = isMuted() ? 0 : masterLevel;
      master.connect(getMasterOut() || ctx.destination);
      sfxBus = ctx.createGain(); sfxBus.gain.value = LEVELS.sfx; sfxBus.connect(master);
      const n = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
      const data = n.getChannelData(0);
      for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
      noise = n;
    } catch (e) { master = null; log('graph failed: ' + e); return false; }
    return true;
  }
  function load(key) {
    if (buffers.has(key)) return;
    buffers.set(key, 'pending');
    const path = SFX_BASE + FILES[key];
    const grab = (url) => fetch(url).then((r) => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.arrayBuffer(); })
      .then((raw) => { const c = getAudioCtx(); if (!c) throw new Error('no ctx'); return new Promise((res, rej) => c.decodeAudioData(raw, res, rej)); });
    const first = audioUrl(path);
    grab(first).catch(() => { const alt = altAudioUrl(first); if (!alt) throw new Error('no alt'); return grab(alt); })
      .then((buf) => buffers.set(key, buf))
      .catch((e) => { buffers.set(key, 'failed'); log('sfx missing: ' + FILES[key] + ' (' + (e && e.message || e) + ')'); });
  }
  Object.keys(FILES).forEach(load);

  // ---- one-shots (the voice cap lives here) ----
  function admit(level) {
    if (voices.length < MAX_VOICES) return;
    const i = pickVoiceToDrop(voices);
    if (i < 0) return;
    const v = voices.splice(i, 1)[0];
    try { v.gain.gain.setTargetAtTime(0, ctx.currentTime, 0.012); v.src.stop(ctx.currentTime + 0.06); } catch (e) { /* already gone */ }
    dropped++;
    const now = performance.now();
    if (now - dropLogT > 5000) { dropLogT = now; log('voice cap: dropped ' + dropped + ' so far (last level ' + v.level.toFixed(2) + ' for ' + level.toFixed(2) + ')'); }
  }
  /** Play a decoded file. opts: { level, semis, pan, lowpass, at (seconds from now) }. */
  function play(key, opts = {}) {
    if (isMuted() || disposed || !ensureGraph()) return null;
    const buf = buffers.get(key);
    if (!buf || buf === 'pending' || buf === 'failed') return null;
    const level = clamp(opts.level == null ? 0.3 : opts.level, 0, 1);
    admit(level);
    try {
      const t = ctx.currentTime + Math.max(0, opts.at || 0);
      const src = ctx.createBufferSource(); src.buffer = buf;
      src.playbackRate.value = semisToRate((opts.semis || 0) + (Math.random() - 0.5) * 0.4);   // +-20 cents so a chain never machine-guns
      const gain = ctx.createGain(); gain.gain.value = level;
      let head = src;
      if (opts.lowpass) { const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = opts.lowpass; lp.Q.value = 0.7; head.connect(lp); head = lp; }
      if (opts.pan && ctx.createStereoPanner) { const p = ctx.createStereoPanner(); p.pan.value = clamp(opts.pan, -1, 1); head.connect(p); head = p; }
      head.connect(gain); gain.connect(sfxBus);
      const v = { src, gain, level, t0: t };
      voices.push(v);
      src.onended = () => { const i = voices.indexOf(v); if (i >= 0) voices.splice(i, 1); try { gain.disconnect(); } catch (e) { /* ignore */ } };
      src.start(t);
      return v;
    } catch (e) { return null; }
  }
  /** Synth one-shot: a sine sweep (thumps, rises) or a filtered noise burst (whooshes, puffs). */
  function synth({ kind = 'sine', f0 = 200, f1 = 60, sec = 0.2, level = 0.3, filter = null, q = 0.8, at = 0 }) {
    if (isMuted() || disposed || !ensureGraph()) return;
    admit(level);
    try {
      const t = ctx.currentTime + at;
      let src;
      if (kind === 'noise') { src = ctx.createBufferSource(); src.buffer = noise; src.loop = true; }
      else { src = ctx.createOscillator(); src.type = kind; src.frequency.setValueAtTime(f0, t); src.frequency.exponentialRampToValueAtTime(Math.max(1, f1), t + sec); }
      const gain = ctx.createGain();
      gain.gain.setValueAtTime(0.0001, t); gain.gain.exponentialRampToValueAtTime(level, t + Math.min(0.02, sec * 0.2)); gain.gain.exponentialRampToValueAtTime(0.0001, t + sec);
      let head = src;
      if (filter) { const bq = ctx.createBiquadFilter(); bq.type = filter.type || 'bandpass'; bq.Q.value = q; bq.frequency.setValueAtTime(filter.f0, t); bq.frequency.exponentialRampToValueAtTime(Math.max(1, filter.f1), t + sec); head.connect(bq); head = bq; }
      head.connect(gain); gain.connect(sfxBus);
      const v = { src, gain, level, t0: t };
      voices.push(v);
      src.onended = () => { const i = voices.indexOf(v); if (i >= 0) voices.splice(i, 1); try { gain.disconnect(); } catch (e) { /* ignore */ } };
      src.start(t); src.stop(t + sec + 0.02);
    } catch (e) { /* a beat never breaks the run */ }
  }

  // ---- the beats ----
  const panOf = (x) => { const k = live.kart; return k ? clamp((x - k.x) / ROAD_HALF_W, -1, 1) * 0.6 : 0; };
  const combo = () => (live.world && live.world.score ? live.world.score.state.combo : 0);
  function onFieldPop(p) {
    const scored = lastScorePop; lastScorePop = null;
    const semis = pitchSemis(combo()), pan = panOf(p.x);
    const asEffect = p.kind === 'effect' && (!scored || scored.kindId !== 'treat');
    if (asEffect) { play('pop', { level: LEVELS.effect, semis: semis - 5, pan, lowpass: 1100 }); synth({ kind: 'sine', f0: 140, f1: 50, sec: 0.16, level: 0.28 }); return; }
    if (p.id === 'golden') { play('burst', { level: LEVELS.burst, semis: 0, pan }); return; }
    if (p.id === 'prism') { play('burst', { level: LEVELS.burst * 0.6, semis: 3 + semis, pan }); return; }
    if (p.id === 'lucky') { play('pop2', { level: LEVELS.pop, semis, pan }); play('chime1', { level: LEVELS.chime * 0.5, semis: 5, pan, at: 0.05 }); return; }
    play(POPS[popRR++ % POPS.length], { level: LEVELS.pop, semis, pan });
  }
  function onScore(e) {
    switch (e.type) {
      case 'pop': lastScorePop = e; break;
      case 'almost': play('pop2', { level: LEVELS.whisper, semis: -4 }); break;
      case 'mult': if (e.to > e.from) { const c = chimeFor(e.to); play(c.file, { level: LEVELS.chime, semis: c.semis }); } break;
      case 'bank': bankThud(); arpeggio(LEVELS.chime * 0.8, 0.09); break;
      case 'jackpot': play('gg', { level: e.tier === 'major' ? LEVELS.gg : LEVELS.gg * 0.7, at: 0.12 }); break;
    }
  }
  function onItem(e) {
    switch (e.type) {
      case 'itemRoll': for (let i = 0; i < 6; i++) play('pop3', { level: LEVELS.tick, semis: 8 + i * 1.5, at: i * 0.14 }); break;
      case 'itemArm': play('chime1', { level: LEVELS.chime * 0.8, semis: 4 }); break;
      case 'itemUse': play('pop2', { level: LEVELS.pop, semis: -3 }); synth({ kind: 'noise', sec: 0.28, level: 0.16, filter: { f0: 600, f1: 2600 }, q: 1.2 }); break;
    }
  }
  function bankThud() { synth({ kind: 'sine', f0: 120, f1: 38, sec: 0.32, level: LEVELS.thud }); synth({ kind: 'noise', sec: 0.05, level: 0.14, filter: { f0: 1800, f1: 400 }, q: 0.7 }); }
  function arpeggio(level, gap) { ['chime1', 'chime2', 'chime3'].forEach((f, i) => play(f, { level, at: i * gap })); }
  function boostWhoosh() { synth({ kind: 'noise', sec: 0.7, level: 0.3, filter: { f0: 300, f1: 3200 }, q: 1.6 }); }
  function rampRise() { synth({ kind: 'sine', f0: 220, f1: 700, sec: 0.36, level: 0.1, filter: { type: 'lowpass', f0: 900, f1: 2200 } }); }
  function rampLand() { synth({ kind: 'sine', f0: 110, f1: 42, sec: 0.2, level: 0.42 }); synth({ kind: 'noise', sec: 0.06, level: 0.12, filter: { f0: 900, f1: 300 } }); }
  function wheelSwoosh() { synth({ kind: 'noise', sec: 1.3, level: 0.26, filter: { f0: 240, f1: 2400 }, q: 2.2 }); synth({ kind: 'noise', sec: 1.0, level: 0.2, filter: { f0: 2400, f1: 200 }, q: 2.2, at: 0.9 }); }
  function gateSting() { play('chime2', { level: LEVELS.chime * 0.75, semis: -3 }); }
  function endSting() { arpeggio(LEVELS.chime, 0.13); play('chime3', { level: LEVELS.chime * 0.8, semis: 5, at: 0.5 }); }

  // ---- the host legs (run.js sfx(name, scale) lands here) ----
  function sfx(name, scale = 0.8) {
    if (!HOST_SFX.has(name)) { log('unknown host sfx dropped: ' + name); return; }
    const paused = !!(live.run && live.run.paused);
    if (name === 'streak_milestone' && scale < 0.8) return;   // the rung: the ladder above owns it
    if (name === 'ui_click' && !paused) return;               // item roll / arm: the ticks own it
    if (name === 'surface') endSting();
    if (isMuted()) return;
    send({ type: 'sfx', name, scale });
  }

  // ---- per-frame edges ----
  function attach(world) {
    live.world = world;
    edge.d = null; edge.room = null; edge.airborne = false; edge.boost = 0; edge.drift = false;
    try { world.field.onPop(onFieldPop); world.score.onEvent(onScore); world.items.onEvent(onItem); } catch (e) { log('attach: ' + e); }
    log('attached to world' + (live.run ? ' seed ' + live.run.seed : ''));
  }
  function update(dt, l) {
    if (disposed || !l) return;
    live.run = l.run || live.run; live.kart = l.kart || live.kart;
    if (l.world && l.world !== live.world) attach(l.world);
    const k = live.kart, run = live.run, w = live.world;
    if (!k || !run || !w) return;
    const roomId = run.room ? run.room.id : null;
    if (roomId !== edge.room) { if (edge.room) gateSting(); edge.room = roomId; }
    if (k.airborne && !edge.airborne) rampRise(); else if (!k.airborne && edge.airborne) rampLand();
    edge.airborne = !!k.airborne;
    if (k.boostSec > 0 && edge.boost <= 0) boostWhoosh();
    edge.boost = k.boostSec;
    if (edge.d != null && w.layout && w.layout.featuresBetween) { for (const f of w.layout.featuresBetween(edge.d, k.d)) if (f.type === 'loop') wheelSwoosh(); }
    edge.d = k.d;
    edge.drift = !!k.drift;
  }

  // ---- duck / mute / dispose ----
  function duck(on, why) {
    if (why === 'brake') { if (on) play('pop', { level: 0.3, semis: -7 }); else play('pop2', { level: 0.24, semis: 3 }); }
  }
  function applyMute(m) {
    if (master) { try { master.gain.setTargetAtTime(m ? 0 : masterLevel, ctx.currentTime, 0.03); } catch (e) { /* ignore */ } }
  }
  const offMute = onMuteChange(applyMute);
  function toggleMute() {
    const m = !isMuted();
    setMuted(m);
    if (hud && hud.toast) hud.toast(m ? 'muted. m to unmute' : 'sound on', 'item');
    log(m ? 'muted' : 'unmuted');
    return m;
  }
  if (input && input.onAction) input.onAction((a) => { if (a === 'mute') toggleMute(); });
  if (isMuted() && hud && hud.toast) setTimeout(() => { try { hud.toast('muted. m to unmute', 'item'); } catch (e) { /* hud gone */ } }, 800);

  function dispose() {
    if (disposed) return;
    disposed = true;
    offMute();
    setDucked(false);
    for (const v of voices.splice(0)) { try { v.src.stop(); } catch (e) { /* ignore */ } }
    try { master && master.disconnect(); } catch (e) { /* ignore */ }
    master = null;
  }

  return { sfx, update, duck, toggleMute, dispose, _voices: voices };
}

// self-check: node --check is the bar; the pure parts (pitchSemis, chimeFor, pickVoiceToDrop)
// are exercised by the node harness in AUDIO.md.
