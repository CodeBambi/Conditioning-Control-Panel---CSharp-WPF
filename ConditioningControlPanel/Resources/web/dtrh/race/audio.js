/* ============================================================================
 * race/audio.js - every sound Racing Thoughts makes. See race/AUDIO.md.
 *
 *   createRaceAudio({ bridge, hud, settings, input }) -> audio
 *   audio.sfx(name, scale)       the run's sfx() helper lands here: host legs + in-page beats
 *   audio.update(dt, live)       live = { world, run, kart }; once per step, after everything moved
 *   audio.menu(on)               the menu theme: MENU_TRACK on the same chain, entered like a room
 *   audio.ui(name, value)        the menu blips: 'tick' | 'pick' | 'back' | 'step' | 'page'
 *   audio.setLevels({ music, sfx })  the two option sliders, live
 *   audio.duck(on, why)          'host' (native video) | 'brake' | 'end' | 'track'
 *                                'track' is the standing one: a loaded file (CHART.md) is the
 *                                soundtrack, so the OST and the bed sit under it until it is cleared
 *   audio.toggleMute() -> bool   M key; shared with the dive's master mute (shared/audioMute.js)
 *   audio.dispose()
 *
 * Two doors. The hot, latency-bound beats (pops, chimes, ticks, thumps, the
 * bed) are WebAudio in this page on the dive's shared context (engine/audioBus)
 * so the DtRH master colour applies. The fanfares the host already owns
 * (`Resources/sounds/chaos/*.mp3`) go out as `sfx` bridge messages using ONLY
 * names in HOST_SFX; a wrong name is silent on the host, so the set is closed.
 * With no host (a plain browser) there is nobody to send to, so the same eleven
 * cues play in page instead: web copies of those mp3s ship under SFX_BASE and
 * ride our own sfx bus at the host's own volume (master * scale). Hosted, not one
 * byte of that path runs and the host still plays them natively.
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
import { ROAD_HALF_W, KART_MIN_SPEED, KART_MAX_SPEED, makeRng } from './consts.js';

export const SFX_BASE = '/dtrh/assets/bubbles/sfx/';
export const MAX_VOICES = 8;            // one-shots at once; the quietest is dropped for the 9th
export const POP_GAP_MS = 28;           // two pops closer than this are one pop (a chain, not a machine gun)
export const ALMOST_GAP_MS = 260;       // the near-miss whisper at most this often
export const PITCH_CAP_SEMIS = 7;       // the combo ladder tops out here
export const PITCH_STEP_COMBO = 4;      // +1 semitone per this many combo

/** Host catalogue names this page may send (verified against Resources/sounds/chaos/).
 *  Unhosted, each one is SFX_BASE + name + '.mp3' - the same clip, copied into the web
 *  tree - so this set is also the closed list of files playHostSfx may ask for. */
export const HOST_SFX = new Set([
  'depth_change', 'pb_fanfare', 'streak_milestone', 'golden_pop', 'ui_click',
  'tunnel_powerup_collect', 'time_slow_in', 'time_slow_out', 'surface', 'thud', 'chain_pop',
]);

// Burst.mp3 and GG.mp3 are deliberately NOT here: the owner cut both (they read as random
// stingers on a bubble pop). Golden, prism and the jackpot are pops + chimes now.
const FILES = { pop: 'Pop.mp3', pop2: 'Pop2.mp3', pop3: 'Pop3.mp3', chime1: 'chime1.mp3', chime2: 'chime2.mp3', chime3: 'chime3.mp3' };
const HOST_KEY = 'host:';   // buffer-key prefix for the HOST_SFX clips, so they cannot collide with FILES
const POPS = ['pop', 'pop2', 'pop3'];
const POP_KEYS = new Set(POPS);
const LEVELS = { sfx: 1, pop: 0.34, effect: 0.42, whisper: 0.1, chime: 0.36, tick: 0.16, thud: 0.55 };
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

/** A slider folded into a fixed base gain: 0..1 in, base * that out. A value that is not a number reads as 1. */
export function levelGain(base, v) { const n = Number(v); return base * clamp(isFinite(n) ? n : 1, 0, 1); }
/** The ui blip rate limit: true when `nowMs` is far enough past the last blip. */
export function uiAllowed(nowMs, lastMs, gap = UI_GAP_MS) { return (nowMs - lastMs) >= gap; }

/** Voice cap: the index of the quietest live voice (ties: the oldest). */
export function pickVoiceToDrop(voices) {
  let at = -1, low = Infinity;
  for (let i = 0; i < voices.length; i++) if (voices[i].level < low) { low = voices[i].level; at = i; }
  return at;
}

// ---------------------------------------------------------------------------
// THE SOUNDTRACK. The Arcademy OST for now (owner: "same as the arcademy, mix
// those upbeat songs"); the owner's mix drops in by editing TRACKS + ROOM_POOLS
// and nothing else. `name` is the file: OST_BASE + name + '.mp3'. `mood` and
// `energy` are guesses from the Arcademy's own table (shell/ost.js) and the
// file lengths; nobody here has listened. Every file loops whole (no loop
// points in the Arcademy either); `start` skips a quiet intro if one needs it.
// ---------------------------------------------------------------------------
export const OST_BASE = '/arcademy/assets/sfx/';
export const CROSSFADE_SEC = 1.5;
export const MUSIC_LEVEL = 0.3;          // the OSTs sit at about -15.5 LUFS; this keeps them under the pops
export const TRACK_DUCK = 0.12;          // where the OST sits under a loaded track: felt, never heard over her
// The MENU. No new files: the tea garden's hub tune doubles as the menu theme, so the front door
// and the first lap are one piece of music. Another theme is this one line.
export const MENU_TRACK = 'ost_campus';
export const MENU_ROOM = '__menu';       // the menu is just another room in the playlist, so it crossfades like one
export const UI_GAP_MS = 45;             // a ui blip at most this often: key repeat on a slider must not machine-gun
export const UI_NAMES = Object.freeze(['tick', 'pick', 'back', 'step', 'page']);
export const TRACKS = Object.freeze([
  { name: 'ost_campus',          title: 'Star Byte Loop',     mood: 'soft', energy: 0.45, sec: 75,  start: 0 },
  { name: 'ost_deep_end',        title: 'Pixel Rush',         mood: 'loud', energy: 0.8,  sec: 77,  start: 0 },
  { name: 'ost_sort',            title: 'Pixel Rush 2',       mood: 'loud', energy: 0.85, sec: 52,  start: 0 },
  { name: 'ost_records',         title: 'Midnight Static',    mood: 'soft', energy: 0.3,  sec: 109, start: 0 },
  { name: 'ost_lost_found',      title: 'Neon Skyline',       mood: 'soft', energy: 0.5,  sec: 141, start: 0 },
  { name: 'ost_instant_recall',  title: 'Midnight Static 2',  mood: 'soft', energy: 0.25, sec: 121, start: 0 },
  { name: 'ost_anomaly',         title: 'Midnight Static 3',  mood: 'soft', energy: 0.3,  sec: 124, start: 0 },
  { name: 'ost_daily_trigger',   title: 'Neon Pixel Rain',    mood: 'loud', energy: 0.75, sec: 164, start: 0 },
  { name: 'ost_impulse_control', title: 'Neon Pixel Rain 2',  mood: 'loud', energy: 0.8,  sec: 159, start: 0 },
  { name: 'ost_prizes',          title: 'Neon Jackpot 3',     mood: 'loud', energy: 0.7,  sec: 98,  start: 0 },
  { name: 'ost_misdirection',    title: 'Neon Jackpot',       mood: 'loud', energy: 0.75, sec: 76,  start: 0 },
  { name: 'ost_deja_vu',         title: 'Neon Jackpot 2',     mood: 'loud', energy: 0.7,  sec: 41,  start: 0 },
  { name: 'ost_annex',           title: 'Corroded Pulse',     mood: 'soft', energy: 0.35, sec: 139, start: 0 },
]);
export const TRACK_BY_NAME = Object.freeze(Object.fromEntries(TRACKS.map((t) => [t.name, t])));
/** room -> the tracks it may draw, best first. The per-run roll rotates each pool by the seed
 *  and skips a track another room already took, so one run never hears the same tune twice. */
export const ROOM_POOLS = Object.freeze({
  teagarden:  ['ost_campus'],
  toybox:     ['ost_daily_trigger', 'ost_deep_end', 'ost_impulse_control'],
  casino:     ['ost_misdirection', 'ost_prizes', 'ost_deja_vu'],
  chapel:     ['ost_impulse_control', 'ost_sort', 'ost_daily_trigger'],
  coronation: ['ost_prizes', 'ost_deep_end', 'ost_misdirection'],
  undertow:   ['ost_instant_recall', 'ost_records', 'ost_anomaly'],
  mirrors:    ['ost_anomaly', 'ost_lost_found', 'ost_deja_vu'],
  greyward:   ['ost_annex', 'ost_records', 'ost_instant_recall'],
});

/** Deterministic per-run playlist: { roomId: trackName }. `rng` is a 0..1 function (consts makeRng). */
export function rollPlaylist(rng, roomOrder, pools = ROOM_POOLS, dead = new Set()) {
  const used = new Set(), out = {};
  for (const room of roomOrder) {
    const pool = (pools[room] || []).filter((n) => !dead.has(n));
    if (!pool.length) continue;
    const off = Math.floor(rng() * pool.length);
    let pick = null;
    for (let i = 0; i < pool.length && !pick; i++) { const n = pool[(off + i) % pool.length]; if (!used.has(n)) pick = n; }
    if (!pick) pick = pool[off];
    used.add(pick); out[room] = pick;
  }
  return out;
}

export function createRaceAudio({ bridge, hud, settings = {}, input } = {}) {
  const log = (m) => { try { bridge && bridge.log && bridge.log('[audio] ' + m); } catch (e) { /* host gone */ } };
  const send = (m) => { try { bridge && bridge.send && bridge.send(m); } catch (e) { /* host gone */ } };
  // WebView2 or a browser? Only a real host has the ccp.content mirror to retry a missing
  // clip against, and only a real host can play the HOST_SFX catalogue for us.
  //
  // `isHosted` alone is not that question any more. The web build installs a transport double so the
  // page boots hosted (cclabs-web scripts/race-web-ext), and that host is JavaScript in this same
  // tab: no ccp.content, no Resources/sounds/chaos. It says so with `hostSfx: false` in its init, and
  // both roads below then take the unhosted branch, which is the one that works there - the eleven
  // cues ship as ordinary files beside the page. The C# host never sets the key, so on the desktop it
  // is undefined and this reads exactly as it always did.
  const hosted = !!(bridge && bridge.isHosted) && settings.hostSfx !== false;
  const masterLevel = clamp((Number(settings.masterVolume) == null || isNaN(Number(settings.masterVolume)) ? 60 : Number(settings.masterVolume)) / 100, 0, 1);
  const levels = { music: 1, sfx: 1 };   // the two option sliders (menu.js persists them); setLevels moves them
  const buffers = new Map();     // key -> AudioBuffer | 'pending' | 'failed'
  const loading = new Map();     // key -> Promise<AudioBuffer|null>, so a first play can wait on its file
  const voices = [];             // live one-shots: { src, gain, level, t0 }
  const live = { world: null, run: null, kart: null };   // what update() last saw (run/kart are live objects)
  const edge = { room: null, airborne: false, boost: 0, d: null, drift: false };
  let ctx = null, master = null, sfxBus = null, noise = null, popRR = 0, disposed = false, dropped = 0, dropLogT = 0;
  let lastPopAt = -1e9, lastAlmostAt = -1e9;   // performance.now() ms: the pop gap and the whisper gap
  let lastScorePop = null;       // the score 'pop' event that ran just before the field pop reaches us
  // music: the chain is duck -> lowpass (fraught / Undertow / tea time) -> highpass (Grey Ward) -> level -> master
  let musicDuck = null, musicLp = null, musicHp = null, musicLevelNode = null, elementMode = false;
  let standing = 1;   // the level update() eases the music back to: 1, or TRACK_DUCK while a track is loaded
  const tracks = new Map();      // name -> { name, el, gain, bound, failed, vol, fadeTimer, pauseTimer }
  const music = { cur: null, playlist: {}, dead: new Set(), duckTo: 1, duckWhy: null, lp: 20000, hp: 20, gesture: false, roomId: null, menu: false };

  setDucked(true);               // bubbles.js's kind-blind pop steps aside (see header)

  // ---- graph ----
  function ensureGraph() {
    if (master || disposed) return !!master;
    ctx = getAudioCtx();
    if (!ctx) return false;
    try {
      master = ctx.createGain(); master.gain.value = isMuted() ? 0 : masterLevel;
      master.connect(getMasterOut() || ctx.destination);
      sfxBus = ctx.createGain(); sfxBus.gain.value = levelGain(LEVELS.sfx, levels.sfx); sfxBus.connect(master);
      musicDuck = ctx.createGain(); musicDuck.gain.value = 1;
      musicLp = ctx.createBiquadFilter(); musicLp.type = 'lowpass'; musicLp.frequency.value = 20000; musicLp.Q.value = 0.6;
      musicHp = ctx.createBiquadFilter(); musicHp.type = 'highpass'; musicHp.frequency.value = 20; musicHp.Q.value = 0.6;
      musicLevelNode = ctx.createGain(); musicLevelNode.gain.value = levelGain(MUSIC_LEVEL, levels.music);
      musicDuck.connect(musicLp); musicLp.connect(musicHp); musicHp.connect(musicLevelNode); musicLevelNode.connect(master);
      const n = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
      const data = n.getChannelData(0);
      for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
      noise = n;
    } catch (e) { master = null; log('graph failed: ' + e); return false; }
    return true;
  }

  // ---- music ----
  function trackFor(name) {
    if (tracks.has(name)) return tracks.get(name);
    const spec = TRACK_BY_NAME[name];
    if (!spec) return null;
    const el = new Audio();
    el.preload = 'auto'; el.loop = true; el.crossOrigin = 'anonymous';
    const first = audioUrl(OST_BASE + name + '.mp3');
    const t = { name, el, gain: null, bound: false, failed: false, vol: 0, fadeTimer: 0, pauseTimer: 0, alt: hosted ? altAudioUrl(first) : null };
    el.addEventListener('error', () => {
      if (t.alt) { const a = t.alt; t.alt = null; el.src = a; if (music.cur === t) el.play().catch(() => {}); return; }
      t.failed = true; music.dead.add(name);
      log('track missing: ' + name + ' (' + (el.error && el.error.code) + ')');
      reroll();                                                          // the room draws again from what is left
      if (music.cur === t) { music.cur = null; music.roomId = null; }   // the next update re-enters the room on another track
    });
    el.src = first;
    tracks.set(name, t);
    return t;
  }
  /** Element -> WebAudio, once; a throw (no ctx, CORS) leaves the whole player in element-volume mode. */
  function bind(t) {
    if (t.bound) return;
    t.bound = true;
    if (elementMode || !ensureGraph()) { elementMode = true; return; }
    try {
      const src = ctx.createMediaElementSource(t.el);
      t.gain = ctx.createGain(); t.gain.gain.value = 0;
      src.connect(t.gain); t.gain.connect(musicDuck);
      t.el.volume = 1;
    } catch (e) { t.gain = null; elementMode = true; log('media element route failed, element volume mode: ' + e); }
  }
  const elVol = (t) => clamp(t.vol * levelGain(MUSIC_LEVEL, levels.music) * music.duckTo * (isMuted() ? 0 : masterLevel), 0, 1);
  function fade(t, to, sec) {
    t.vol = to;
    if (t.gain && ctx) {
      try { const now = ctx.currentTime; t.gain.gain.cancelScheduledValues(now); t.gain.gain.setValueAtTime(t.gain.gain.value, now); t.gain.gain.linearRampToValueAtTime(to, now + sec); } catch (e) { t.gain.gain.value = to; }
      return;
    }
    clearInterval(t.fadeTimer);
    const from = t.el.volume, target = elVol(t), steps = Math.max(1, Math.round(sec / 0.05));
    let i = 0;
    t.fadeTimer = setInterval(() => { i++; try { t.el.volume = from + (target - from) * Math.min(1, i / steps); } catch (e) { /* ignore */ } if (i >= steps) clearInterval(t.fadeTimer); }, 50);
  }
  function armGesture() {
    if (music.gesture) return;
    music.gesture = true;
    const retry = () => { music.gesture = false; window.removeEventListener('pointerdown', retry); window.removeEventListener('keydown', retry); if (music.cur) startEl(music.cur); };
    window.addEventListener('pointerdown', retry, { passive: true }); window.addEventListener('keydown', retry, { passive: true });
  }
  function startEl(t) {
    try { getAudioCtx(); } catch (e) { /* ignore */ }
    try { t.el.muted = isMuted(); const p = t.el.play(); if (p && p.catch) p.catch(() => { if (!t.failed) armGesture(); }); } catch (e) { armGesture(); }
  }
  function enterTrack(roomId) {
    const name = music.playlist[roomId];
    if (!name || (music.cur && music.cur.name === name)) return;
    const next = trackFor(name);
    if (!next || next.failed) return;
    const prev = music.cur;
    if (prev) { fade(prev, 0, CROSSFADE_SEC); clearTimeout(prev.pauseTimer); prev.pauseTimer = setTimeout(() => { if (music.cur !== prev) { try { prev.el.pause(); } catch (e) { /* ignore */ } } }, CROSSFADE_SEC * 1000 + 200); }
    music.cur = next;
    clearTimeout(next.pauseTimer);
    bind(next);
    const spec = TRACK_BY_NAME[name];
    if (next.el.paused && spec.start > 0 && next.el.currentTime < spec.start) { try { next.el.currentTime = spec.start; } catch (e) { /* not seekable yet */ } }
    startEl(next);
    fade(next, 1, prev ? CROSSFADE_SEC : 0.8);
    log('track ' + name + ' in (' + roomId + ')' + (prev ? ', ' + prev.name + ' out, crossfade ' + CROSSFADE_SEC + 's' : ''));
  }
  /**
   * The menu theme. It is entered exactly like a room, so the ordinary CROSSFADE_SEC fade carries it
   * in and the run's first room carries it out. menu(false) stops nothing on purpose: the first room
   * fades over it, and when that room draws MENU_TRACK itself the tune simply plays on.
   * Autoplay is the run's: a rejected play() arms the same pointer/key retry (startEl -> armGesture).
   */
  function menuMusic(on) {
    if (disposed) return;
    music.menu = !!on;
    log('menu theme ' + (on ? MENU_TRACK + ' requested' : 'released, the run takes the music'));
    if (!on) return;
    music.playlist[MENU_ROOM] = MENU_TRACK;
    music.roomId = MENU_ROOM;
    enterTrack(MENU_ROOM);
  }

  // ---- the speed bed + drift sparks: loops under the music, outside the voice cap ----
  // wind = lowpassed noise whose cutoff and gain follow speed (boost and air open it up);
  // hum = a triangle under 200 Hz that climbs with speed; sparks = highpassed noise that
  // crackles (random gain per frame) only while drifting. All three ride bedBus, which the
  // duck pulls to 0 with the music.
  let bed = null, bedBus = null;
  function ensureBed() {
    if (bed || !ensureGraph()) return !!bed;
    try {
      bedBus = ctx.createGain(); bedBus.gain.value = music.duckTo < 1 ? 0 : 1; bedBus.connect(master);
      const mk = (type, f, q) => { const b = ctx.createBiquadFilter(); b.type = type; b.frequency.value = f; b.Q.value = q; return b; };
      const wind = ctx.createBufferSource(); wind.buffer = noise; wind.loop = true;
      const windLp = mk('lowpass', 200, 0.9), windGain = ctx.createGain(); windGain.gain.value = 0;
      wind.connect(windLp); windLp.connect(windGain); windGain.connect(bedBus);
      const hum = ctx.createOscillator(); hum.type = 'triangle'; hum.frequency.value = 50;
      const humLp = mk('lowpass', 180, 0.7), humGain = ctx.createGain(); humGain.gain.value = 0;
      hum.connect(humLp); humLp.connect(humGain); humGain.connect(bedBus);
      const sparks = ctx.createBufferSource(); sparks.buffer = noise; sparks.loop = true;
      const sparksHp = mk('highpass', 4500, 0.8), sparksGain = ctx.createGain(); sparksGain.gain.value = 0;
      sparks.connect(sparksHp); sparksHp.connect(sparksGain); sparksGain.connect(bedBus);
      wind.start(); hum.start(); sparks.start(0, 0.5);   // sparks read the noise from another offset so the two never correlate
      bed = { wind, windLp, windGain, hum, humGain, sparks, sparksGain };
    } catch (e) { bed = null; log('bed failed: ' + e); }
    return !!bed;
  }
  function bedUpdate(k) {
    if (!bed && isMuted()) return;   // nothing is built under mute; once built it runs on (master is 0)
    if (!ensureBed()) return;
    const now = ctx.currentTime, tc = 0.08;
    const v = clamp((k.speed - KART_MIN_SPEED) / (KART_MAX_SPEED - KART_MIN_SPEED), 0, 1);
    const boost = k.boostSec > 0 ? 1 : 0, air = k.airborne ? 1 : 0;
    try {
      bed.windLp.frequency.setTargetAtTime(160 + 1300 * Math.pow(v, 1.4) + 500 * boost + 400 * air, now, tc);
      bed.windGain.gain.setTargetAtTime((0.012 + 0.05 * v + 0.03 * boost) * (1 + 0.4 * air), now, tc);
      bed.hum.frequency.setTargetAtTime(46 + 64 * v + 12 * boost, now, tc);
      bed.humGain.gain.setTargetAtTime((0.022 + 0.02 * v) * (1 - 0.6 * air), now, tc);
      bed.sparksGain.gain.setTargetAtTime(k.drift && !k.airborne ? 0.03 + Math.random() * 0.045 : 0, now, 0.03);
    } catch (e) { /* a param never breaks the run */ }
  }
  function stopBed() {
    if (!bed) return;
    try { bed.wind.stop(); bed.hum.stop(); bed.sparks.stop(); bedBus.disconnect(); } catch (e) { /* ignore */ }
    bed = null; bedBus = null;
  }

  /** Room colour on the music only: lowpass under fraught state, the Undertow and tea time; highpass in the Grey Ward. */
  function colour(roomId, fraught, timeScale) {
    let lp = 20000, hp = 20;
    if (fraught > 0) lp = Math.exp(Math.log(20000) * (1 - fraught) + Math.log(800) * fraught);
    if (roomId === 'undertow') lp = Math.min(lp, 650);
    if (timeScale < 1) lp = Math.min(lp, 1400);
    if (roomId === 'greyward') hp = 240;
    if (!musicLp || !ctx) return;
    if (Math.abs(lp - music.lp) > music.lp * 0.02) { music.lp = lp; try { musicLp.frequency.setTargetAtTime(lp, ctx.currentTime, 0.5); } catch (e) { /* ignore */ } }
    if (Math.abs(hp - music.hp) > music.hp * 0.02) { music.hp = hp; try { musicHp.frequency.setTargetAtTime(hp, ctx.currentTime, 0.5); } catch (e) { /* ignore */ } }
  }
  function setDuck(to, why) {
    if (to === music.duckTo) return;
    music.duckTo = to; music.duckWhy = to < 1 ? why : null;
    if (musicDuck && ctx) { try { musicDuck.gain.setTargetAtTime(to, ctx.currentTime, to < 1 ? 0.15 : 0.4); } catch (e) { /* ignore */ } }
    else if (music.cur) fade(music.cur, music.cur.vol, 0.4);
    if (bedBus && ctx) { try { bedBus.gain.setTargetAtTime(to < 1 ? 0 : 1, ctx.currentTime, to < 1 ? 0.1 : 0.4); } catch (e) { /* ignore */ } }
  }
  function stopMusic() {
    for (const t of tracks.values()) { clearInterval(t.fadeTimer); clearTimeout(t.pauseTimer); try { t.el.pause(); t.el.removeAttribute('src'); t.el.load(); } catch (e) { /* ignore */ } }
    tracks.clear(); music.cur = null; music.roomId = null;
  }
  /** Fetch + decode one file under SFX_BASE, once. Resolves to the buffer, or null when the
   *  clip is not there. The second host is only worth a try when there IS one: in a browser
   *  https://ccp.content does not resolve, so an unhosted page fails once and moves on. */
  function load(key, file) {
    if (loading.has(key)) return loading.get(key);
    buffers.set(key, 'pending');
    const grab = (url) => fetch(url).then((r) => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.arrayBuffer(); })
      .then((raw) => { const c = getAudioCtx(); if (!c) throw new Error('no ctx'); return new Promise((res, rej) => c.decodeAudioData(raw, res, rej)); });
    const first = audioUrl(SFX_BASE + file);
    const job = grab(first)
      .catch(() => { const alt = hosted ? altAudioUrl(first) : null; if (!alt) throw new Error('no alt'); return grab(alt); })
      .then((buf) => { buffers.set(key, buf); return buf; })
      .catch((e) => { buffers.set(key, 'failed'); log('sfx missing: ' + file + ' (' + (e && e.message || e) + ')'); return null; });
    loading.set(key, job);
    return job;
  }
  Object.keys(FILES).forEach((k) => load(k, FILES[k]));

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
    if (POP_KEYS.has(key) && !opts.at) {   // a chain of pops inside POP_GAP_MS is one pop
      const nowMs = performance.now();
      if (nowMs - lastPopAt < POP_GAP_MS) return null;
      lastPopAt = nowMs;
    }
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
    if (p.id === 'golden') { play('pop3', { level: LEVELS.pop, semis: semis + 2, pan }); play('chime3', { level: LEVELS.chime * 0.8, semis: 7, pan, at: 0.05 }); play('chime3', { level: LEVELS.chime * 0.6, semis: 12, pan, at: 0.14 }); return; }
    if (p.id === 'prism') { play('pop2', { level: LEVELS.pop, semis: 3 + semis, pan }); play('chime1', { level: LEVELS.chime * 0.45, semis: 5 + semis, pan, at: 0.04 }); return; }
    if (p.id === 'lucky') { play('pop2', { level: LEVELS.pop, semis, pan }); play('chime1', { level: LEVELS.chime * 0.5, semis: 5, pan, at: 0.05 }); return; }
    play(POPS[popRR++ % POPS.length], { level: LEVELS.pop, semis, pan });
  }
  function onScore(e) {
    switch (e.type) {
      case 'pop': lastScorePop = e; break;
      case 'almost': { const nowMs = performance.now(); if (nowMs - lastAlmostAt >= ALMOST_GAP_MS) { lastAlmostAt = nowMs; play('pop2', { level: LEVELS.whisper, semis: -4 }); } break; }
      case 'mult': if (e.to > e.from) { const c = chimeFor(e.to); play(c.file, { level: LEVELS.chime, semis: c.semis }); } break;
      case 'bank': bankThud(); arpeggio(LEVELS.chime * 0.8, 0.09); break;
      case 'jackpot': arpeggio(e.tier === 'major' ? LEVELS.chime : LEVELS.chime * 0.7, 0.08); if (e.tier === 'major') play('chime3', { level: LEVELS.chime * 0.8, semis: 12, at: 0.3 }); break;
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

  // ---- the menu blips. No files: five little synth voices on the shared context, all through
  // sfxBus, so the sfx slider owns them and mute silences them. `value` (0..1) only means anything
  // to 'step', where the tick climbs an octave with the slider so the ear hears the number move.
  let lastUiAt = -1e9;
  function ui(name, value) {
    if (disposed || isMuted() || UI_NAMES.indexOf(name) < 0) return;
    const nowMs = performance.now();
    if (!uiAllowed(nowMs, lastUiAt)) return;
    lastUiAt = nowMs;
    const n = Number(value), v = clamp(isFinite(n) ? n : 0.5, 0, 1);
    switch (name) {
      case 'tick': synth({ kind: 'triangle', f0: 1180, f1: 1130, sec: 0.035, level: 0.045 }); break;
      case 'pick': synth({ kind: 'sine', f0: 660, f1: 652, sec: 0.07, level: 0.15 }); synth({ kind: 'sine', f0: 990, f1: 978, sec: 0.11, level: 0.12, at: 0.06 }); break;
      case 'back': synth({ kind: 'sine', f0: 620, f1: 612, sec: 0.07, level: 0.13 }); synth({ kind: 'sine', f0: 415, f1: 408, sec: 0.12, level: 0.11, at: 0.06 }); break;
      case 'step': synth({ kind: 'triangle', f0: 520 * Math.pow(2, v), f1: 512 * Math.pow(2, v), sec: 0.045, level: 0.075 }); break;
      case 'page': synth({ kind: 'noise', sec: 0.16, level: 0.09, filter: { type: 'bandpass', f0: 2200, f1: 700 }, q: 1.1 }); synth({ kind: 'triangle', f0: 880, f1: 700, sec: 0.06, level: 0.05, at: 0.02 }); break;
    }
  }

  // ---- the host legs (run.js sfx(name, scale) lands here) ----
  /** No host: play the host's own clip ourselves. Same file, same volume the host would use
   *  (its master times the cue's scale), through the page's sfx bus so the mute, the voice cap
   *  and the sfx slider all still apply. Loaded on first ask, then straight from the buffer. */
  function playHostSfx(name, scale) {
    const key = HOST_KEY + name;
    const level = clamp(Number(scale) || 0, 0, 1);
    const buf = buffers.get(key);
    if (buf === 'failed') return;                                    // asked once, not there: stay quiet
    if (buf && buf !== 'pending') { play(key, { level }); return; }
    load(key, name + '.mp3').then((b) => { if (b && !disposed && !isMuted()) play(key, { level }); });
  }
  function sfx(name, scale = 0.8) {
    if (!HOST_SFX.has(name)) { log('unknown host sfx dropped: ' + name); return; }
    const paused = !!(live.run && live.run.paused);
    if (name === 'streak_milestone' && scale < 0.8) return;   // the rung: the ladder above owns it
    if (name === 'golden_pop') return;                        // the golden pop: the chime stack above owns it
    if (name === 'ui_click' && !paused) return;               // item roll / arm: the ticks own it
    if (name === 'surface') endSting();
    if (isMuted()) return;
    if (!hosted) { playHostSfx(name, scale); return; }
    send({ type: 'sfx', name, scale });
  }

  // ---- per-frame edges ----
  function attach(world) {
    live.world = world;
    edge.d = null; edge.room = null; edge.airborne = false; edge.boost = 0; edge.drift = false;
    try { world.field.onPop(onFieldPop); world.score.onEvent(onScore); world.items.onEvent(onItem); } catch (e) { log('attach: ' + e); }
    music.seed = live.run ? live.run.seed : 1;
    music.order = Object.keys(ROOM_POOLS);
    try { if (world.dresser && world.dresser.spans) music.order = world.dresser.spans.map((s) => s.id); } catch (e) { /* default order */ }
    reroll();
    music.roomId = null;
    log('attached to world seed ' + music.seed + ', playlist ' + music.order.map((r) => r + ':' + (music.playlist[r] || '-')).join(' '));
  }
  function reroll() { music.playlist = rollPlaylist(makeRng(((music.seed | 0) ^ 0xa11d10) >>> 0), music.order || Object.keys(ROOM_POOLS), ROOM_POOLS, music.dead); }
  function update(dt, l) {
    if (disposed || !l) return;
    live.run = l.run || live.run; live.kart = l.kart || live.kart;
    if (l.world && l.world !== live.world) attach(l.world);
    const k = live.kart, run = live.run, w = live.world;
    if (!k || !run || !w) return;
    const roomId = run.room ? run.room.id : null;
    if (roomId !== edge.room) { if (edge.room) gateSting(); edge.room = roomId; }
    if (roomId && roomId !== music.roomId) { music.roomId = roomId; enterTrack(roomId); }
    if (music.duckTo !== standing) setDuck(standing, standing < 1 ? 'track' : null);   // update() only runs while the run is live: only a loaded track ducks it
    colour(roomId, clamp((run.effects ? run.effects.length : 0) / 3, 0, 1), run.timeScale == null ? 1 : run.timeScale);
    if (k.airborne && !edge.airborne) rampRise(); else if (!k.airborne && edge.airborne) rampLand();
    edge.airborne = !!k.airborne;
    if (k.boostSec > 0 && edge.boost <= 0) boostWhoosh();
    edge.boost = k.boostSec;
    if (edge.d != null && w.layout && w.layout.featuresBetween) { for (const f of w.layout.featuresBetween(edge.d, k.d)) if (f.type === 'loop') wheelSwoosh(); }
    edge.d = k.d;
    edge.drift = !!k.drift;
    bedUpdate(k);
  }

  // ---- levels / duck / mute / dispose ----
  /**
   * The two option sliders, live under a moving slider. music multiplies MUSIC_LEVEL on the music
   * chain's level node; sfx multiplies sfxBus, which every in-page one-shot and every ui blip rides.
   * The HOST legs are deliberately untouched WHEN THERE IS A HOST: those play on the host's own
   * mixer at its own volume. Unhosted they are ordinary one-shots on sfxBus, so the slider moves them.
   */
  function setLevels(l) {
    if (disposed || !l || typeof l !== 'object') return;
    if (l.music != null) levels.music = clamp(Number(l.music) || 0, 0, 1);
    if (l.sfx != null) levels.sfx = clamp(Number(l.sfx) || 0, 0, 1);
    const ease = (node, to) => { if (!node || !ctx) return; try { node.gain.setTargetAtTime(to, ctx.currentTime, 0.05); } catch (e) { try { node.gain.value = to; } catch (e2) { /* ignore */ } } };
    ease(musicLevelNode, levelGain(MUSIC_LEVEL, levels.music));
    ease(sfxBus, levelGain(LEVELS.sfx, levels.sfx));
    for (const t of tracks.values()) if (!t.gain) { try { t.el.volume = elVol(t); } catch (e) { /* ignore */ } }   // element-volume mode
    log('levels applied: music ' + levels.music.toFixed(2) + ' sfx ' + levels.sfx.toFixed(2));
  }
  function duck(on, why) {
    if (why === 'track') { standing = on ? TRACK_DUCK : 1; setDuck(standing, on ? 'track' : null); return; }
    if (why === 'brake') { if (on) play('pop', { level: 0.3, semis: -7 }); else play('pop2', { level: 0.24, semis: 3 }); }
    if (on) setDuck(why === 'end' ? 0.45 : 0.1, why || 'host'); else setDuck(1, null);
  }
  function applyMute(m) {
    if (master) { try { master.gain.setTargetAtTime(m ? 0 : masterLevel, ctx.currentTime, 0.03); } catch (e) { /* ignore */ } }
    for (const t of tracks.values()) { try { t.el.muted = m; if (!t.gain) t.el.volume = elVol(t); } catch (e) { /* ignore */ } }   // unmuting is the owner's job (audioMute.js)
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
    stopMusic(); stopBed();
    try { master && master.disconnect(); } catch (e) { /* ignore */ }
    master = null;
  }

  return { sfx, ui, menu: menuMusic, setLevels, update, duck, toggleMute, dispose, _voices: voices, _music: music, _levels: levels, _tracks: tracks };
}

// self-check: node --check is the bar; the pure parts (pitchSemis, chimeFor, pickVoiceToDrop)
// are exercised by the node harness in AUDIO.md.
