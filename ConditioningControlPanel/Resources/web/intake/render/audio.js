/* ============================================================================
 * audio.js — WebAudio binaural + reward voices for "Graded Intake" (Agent E).
 *
 * ONE depth scalar drives a binaural beat: two sine oscillators (L / R), the
 * right detuned above the left by the beat frequency, summed through a lowpass
 * -> depth gain -> limiter -> destination. As depth 0->1 the beat tightens from
 * 10 Hz to 3.5 Hz and the carrier rises 174 Hz -> 196 Hz (memory note + brief).
 *
 * CONTRACT (contracts.js §"EFFECTS + AUDIO (Agent E)"):
 *   createAudio({ caps }) -> {
 *     setDepth(depth),        // binauralDepth (clamped) -> beat/carrier Hz + gain, glided
 *     chime(rewardEvent),     // DISTINCT VOICE PER RewardKind, scaled by clampIntensity;
 *                             //   streak raises pitch; jackpot -> sting; nearMiss -> tick
 *     emerge(),               // Recovery: reverse the ramp (beat -> 10 Hz, carrier down, gain out)
 *   }
 *
 * AUDIO IDENTITY (the "0 variety" fix, sound half):
 *   Chime  — the app's REAL chime: one of the CCP chime mp3s (chime1-3, random,
 *            served under the page origin at ../dtrh/assets/bubbles/sfx/),
 *            lazily fetch+decoded on first use and routed through the same
 *            limiter/caps chain. Streak pitch rides playbackRate. Until the
 *            samples are ready — or if fetch/decode fails (file:// standalone,
 *            missing asset; failure emits an 'intake-log' CustomEvent) — the
 *            original synth bell below plays instead. Never blocks, never throws.
 *   Bell   — soft synth bell (triangle + sine partials) — the Chime fallback
 *   Bubble — watery blip (short bandpassed noise + pitch-down sine blip)
 *   Flash  — airy shimmer sweep (bandpassed noise, filter swept upward)
 *   Praise — warm two-note major swell (root + major third, slow attack)
 *   Drop   — low soft boom with a downward glide into the binaural bed
 *   Streak — +1 semitone per consecutive streak (cap +7) on Chime/Bubble
 *   jackpot -> short 3-note major sting + noise shimmer; nearMiss -> muted tick
 *
 * GARNISH CUES (presentation upgrade): effects.js sometimes pairs a reward
 *   with a fullscreen garnish; the two with a sound ping us via the
 *   'intake-garnish' window CustomEvent (loose seam — boot wiring unchanged;
 *   detail = { name, intensity } with intensity RAW, clamped HERE by our caps):
 *   spiral -> soft airy shimmer (slow-swept air + a faint fifth pad)
 *   sublim -> barely-there tick train + a whisper of highpassed breath
 *   Also callable directly as Audio.garnishCue(name, intensity) (additive).
 *
 * INVARIANTS:
 *   #2 — the binaural LEVEL flows from clampToCaps(depthToChannels(depth)).binauralDepth
 *        and every reward voice from clampIntensity(intensity, caps). No hardcoded
 *        loudness beyond the fixed 0..1 -> amplitude/Hz translation constants.
 *   #3 — emerge() glides the whole graph back to the depth-0 base and silences it.
 *
 * EVERY one-shot routes through the shared limiter (when the graph is up) so the
 * existing master gain / brick-wall topology stays authoritative — no voice gets
 * its own path to the destination once the bed exists.
 *
 * USER PREF BUSES (ui/prefs.js — the Options panel's volume sliders):
 *   Three pref-aware gain nodes sit BETWEEN the existing per-voice gains and the
 *   shared limiter, one per category:
 *
 *     merger -> lowpass -> binGain --> [binBus]   -\
 *     one-shot voices ---------------> [fxBus]     --> limiter -> destination
 *     VO buffer sources -------------> [voiceBus] -/
 *
 *   WHY a choke point and not a multiplier at ~30 call sites: every *Spec() here
 *   is a PURE, headless-tested translation constant, and the caps math
 *   (clampIntensity / clampToCaps) is invariant #2. Folding a user slider into
 *   either would make the tested math and the shipped math diverge and would
 *   conflate "how loud in the room" with "how hard the host may push". Three
 *   nodes downstream of every gain leave all of that byte-identical — and they
 *   also let a slider retune a line that is ALREADY playing, which per-call-site
 *   multipliers cannot do. The bed's duck/suspend bookkeeping (voBedSaved /
 *   bedSavedGain) still reads and writes binGain alone, so those latches keep
 *   working untouched while binBus rides on top.
 *
 *   Final gain therefore stays: spec.gain * caps * prefs — multiplied, never
 *   conflated. Pref changes GLIDE (setTargetAtTime) so a dragged slider never
 *   clicks. VO additionally hard-gates: voiceScale() === 0 (the Voiceover OFF
 *   switch, or master/voice at 0) makes playVoice a no-op that returns null and
 *   still fires opts.onEnd — byte-identical to the existing muted path, so the
 *   intro chain in boot.js and the garble chains in beats.js advance normally.
 *
 * SIDE-EFFECT FREE AT IMPORT: the AudioContext is created LAZILY (first setDepth /
 * chime, armed to resume on the first user gesture). No WebAudio/DOM at load, so
 * importing never throws. Pure Hz/gain mappings are exported for headless tests.
 * ==========================================================================*/

import { depthToChannels, clampToCaps, clampIntensity, RewardKind, clamp01, lerp } from '../core/contracts.js';
import {
  binauralScale, effectsScale, voiceScale, subscribe as subscribePrefs,
} from '../ui/prefs.js';

/* ----------------------------------------------------------------------------
 * PURE MAPPINGS — binauralDepth (0..1) -> Hz + amplitude. Exported + tested.
 * Endpoints are LOCKED by the brief/memory note:
 *   depth 0 -> beat 10.0 Hz, carrier 174 Hz
 *   depth 1 -> beat  3.5 Hz, carrier 196 Hz
 * -------------------------------------------------------------------------- */
export const BEAT_HZ    = { rest: 10.0, deep: 3.5 };
export const CARRIER_HZ = { rest: 174,  deep: 196 };
/** Peak binaural amplitude at full clamped depth (before limiter). Translation constant. */
export const BINAURAL_GAIN_MAX = 0.22;

/** binauralDepth (already clamped to caps) -> { beatHz, carrierHz }. Pure. */
export function binauralToHz(binauralDepth) {
  const b = clamp01(binauralDepth);
  return {
    beatHz:    lerp(BEAT_HZ.rest, BEAT_HZ.deep, b),       // 10 -> 3.5
    carrierHz: lerp(CARRIER_HZ.rest, CARRIER_HZ.deep, b), // 174 -> 196
  };
}
/** binauralDepth -> master binaural amplitude (0 at rest). Pure. */
export function binauralGainFor(binauralDepth) {
  return clamp01(binauralDepth) * BINAURAL_GAIN_MAX;
}

/* ----------------------------------------------------------------------------
 * PURE VOICE SPECS — reward intensity (already clampIntensity'd) -> envelope /
 * frequency numbers per RewardKind. All exported for headless tests; the live
 * voices below consume EXACTLY these so the tested math is the shipped math.
 * -------------------------------------------------------------------------- */

/** Chime: soft bell envelope (the original voice). Pure. */
export function chimeSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:       0.03 + i * 0.17,   // 0.03 .. 0.20
    durSec:     0.26 + i * 0.22,   // 0.26 .. 0.48 s
    baseHz:     660 + i * 220,     // brighter when it pays more
    partials:   [1, 2, 3],         // simple bell-ish stack
  };
}

/** Streak -> semitone offset, capped at +7 (a fifth). Pure. */
export function streakSemitones(streak) {
  const s = Math.max(0, streak | 0);
  return Math.min(s, 7);
}
/** Semitones -> frequency multiplier (equal temperament). Pure. */
export function semitoneFactor(semis) {
  return Math.pow(2, Math.max(0, semis) / 12);
}

/** Bubble: watery pitch-down blip + short filtered-noise splash. Pure. */
export function bubbleSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:     0.03 + i * 0.15,
    durSec:   0.16 + i * 0.10,
    startHz:  820 + i * 260,   // blip starts here...
    endHz:    340 + i * 120,   // ...and glides DOWN here (the "bloop")
    noiseSec: 0.06,
    noiseHz:  1400,            // bandpass center for the splash
  };
}

/** Flash: airy shimmer — bandpassed noise, filter swept upward. Pure. */
export function flashSweepSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:    0.02 + i * 0.10,  // airy = quiet by design
    durSec:  0.30 + i * 0.25,
    startHz: 1200 + i * 400,
    endHz:   3800 + i * 1600,
    q:       2.5,
  };
}

/** Praise: warm two-note major swell (root + major third). Pure. */
export function praiseSwellSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:       0.03 + i * 0.14,
    durSec:     0.55 + i * 0.35,
    rootHz:     392 + i * 88,   // ~G4 warming upward
    thirdRatio: 1.25,           // just-intonation major third
    attackSec:  0.12,           // swell, not strike
    thirdDelay: 0.10,           // the third blooms in slightly late
  };
}

/** Drop: low soft boom + downward glide toward the binaural bed. Pure. */
export function dropBoomSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:    0.05 + i * 0.18,
    durSec:  0.50 + i * 0.40,
    startHz: 130 + i * 40,
    endHz:   55,               // sinks under the 174-196 Hz carrier bed
    thumpSec: 0.09,
    thumpHz: 200,              // lowpass ceiling for the noise thump
  };
}

/** Jackpot: short 3-note major sting + noise shimmer. Pure. */
export function stingSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:       0.04 + i * 0.16,
    notesHz:    [523.25, 659.25, 783.99], // C5 - E5 - G5, rising
    noteGapSec: 0.09,
    noteSec:    0.34,
    shimmerSec: 0.9,
    shimmerHz:  3200,          // highpass floor for the sparkle tail
  };
}

/** Near-miss: one muted tick (fire=false tease). Pure. */
export function nearMissTickSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:   0.012 + i * 0.028, // deliberately faint
    durSec: 0.05,
    hz:     900,               // bandpass center of the tick
  };
}

/* ----------------------------------------------------------------------------
 * REAL CHIME SAMPLES — the CCP app's chime1-3.mp3, byte-identical copies of
 * which are already served under the page origin by the dtrh asset host.
 * URLs resolve against THIS MODULE (import.meta.url) so the page's own path
 * never matters; the string fallback covers exotic hosts. Pure / lazy — no
 * fetch happens until the factory's first chime.
 * -------------------------------------------------------------------------- */
export const CHIME_SAMPLE_COUNT = 3;
export function chimeSampleUrls() {
  const rel = (n) => `../../dtrh/assets/bubbles/sfx/chime${n}.mp3`;
  try {
    return [1, 2, 3].map((n) => new URL(rel(n), import.meta.url).href);
  } catch (_e) {
    // no URL/base available: hand back page-relative strings and hope
    return [1, 2, 3].map((n) => `../dtrh/assets/bubbles/sfx/chime${n}.mp3`);
  }
}
/** Reward intensity -> mp3-chime playback gain (the sample carries its own
 *  envelope; the limiter brick-walls the sum as usual). Pure. */
export function chimeSampleSpec(intensity) {
  const i = clamp01(intensity);
  return { gain: 0.06 + i * 0.24 }; // 0.06 .. 0.30
}

/* ----------------------------------------------------------------------------
 * REAL POP SAMPLE — the CCP app's bubble Pop.mp3, served under the page origin
 * by the dtrh asset host (same folder as the chimes). Resolves against THIS
 * module (import.meta.url) with a page-relative string fallback for exotic
 * hosts. Lazy — no fetch until the first pop(). If the file can't load
 * (file:// standalone / missing asset), popSynth() below covers it.
 * -------------------------------------------------------------------------- */
export function popSampleUrl() {
  const rel = '../../dtrh/assets/bubbles/sfx/Pop.mp3';
  try { return new URL(rel, import.meta.url).href; }
  catch (_e) { return '../dtrh/assets/bubbles/sfx/Pop.mp3'; }
}
/** POP intensity -> real Pop.mp3 playback gain (sample carries its own envelope,
 *  limiter brick-walls the sum). Pops are punchy. Pure. */
export function popSampleSpec(intensity, rate) {
  const i = clamp01(intensity);
  // `rate` (0.5..2, default 1) pitches the pop up/down via playbackRate — the
  // same mechanism the chime's streak pitch rides. Used by the bubble SPLIT
  // chain: each generation is smaller, so each pop is a notch tighter.
  const r = (typeof rate === 'number' && rate >= 0.5 && rate <= 2) ? rate : 1;
  return { gain: 0.10 + i * 0.30, rate: r }; // 0.10 .. 0.40
}
/** POP synth fallback: a short bandpassed noise burst + a low sine thump.
 *  Used until Pop.mp3 decodes, and forever if it never does. Pure. */
export function popSynthSpec(intensity, rate) {
  const i = clamp01(intensity);
  const r = (typeof rate === 'number' && rate >= 0.5 && rate <= 2) ? rate : 1;
  return {
    gain:     0.06 + i * 0.20,
    noiseHz:  1800 * r,   // bandpass center of the burst (pitched by `rate`)
    noiseSec: 0.045 / r,
    thumpHz:  190 * r,    // low sine thump start
    thumpEnd: 60 * r,     // ...gliding down under the burst
    thumpSec: 0.11 / r,
  };
}

/** Spiral garnish: soft airy shimmer — a long, quiet band of air swept slowly
 *  upward with a faint root+fifth sine pad underneath. Pure. */
export function spiralShimmerSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:      0.015 + i * 0.045, // airy = well under every reward voice
    durSec:    2.4 + i * 1.2,
    attackSec: 0.7,               // bloom, never a strike
    startHz:   900 + i * 300,
    endHz:     2400 + i * 900,
    q:         1.6,
    toneHz:    523.25,            // C5 pad root under the air
    toneRatio: 1.5,               // + a just fifth
    toneGain:  0.35,              // relative to gain
  };
}

/** Subliminal-flash garnish: a barely-there tick train (matching the word-blink
 *  cadence) over one long whisper of highpassed breath. Pure. */
export function subliminalTickSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:      0.008 + i * 0.020, // fainter than the near-miss tick on purpose
    ticks:     3,
    gapSec:    0.35,              // mirrors the ~350ms flash spacing
    tickSec:   0.045,
    tickHz:    1250,              // bandpass center of each tick
    swellSec:  1.2,
    swellHz:   2600,              // highpass floor of the breath
    swellGain: 0.5,               // relative to gain
  };
}

/* ----------------------------------------------------------------------------
 * JUMPSCARE EVENT VOICES — the "are you sure?" mechanic (beats.js). Additive.
 * Pure specs (translation constants only); the live voices below consume them.
 * -------------------------------------------------------------------------- */

/** Glitch burst: a run of bit-crushy square stabs through a lowpass (grit) with
 *  a harsh bandpassed noise wash under them. ~400ms total. Pure. */
export function glitchStabSpec() {
  return {
    stabs:     5,
    stabSec:   0.085,
    gapSec:    0.07,          // 5 stabs * 0.07 ≈ 0.35s + tail ≈ 0.4s
    gain:      0.22,
    freqs:     [92, 138, 174, 116, 205],  // dissonant low square stabs
    crushHz:   1400,          // lowpass ceiling = the bit-crush grit
    noiseSec:  0.4,
    noiseHz:   1800,          // bandpass center of the wash
    noiseGain: 0.16,
  };
}

/** One "error ding": a two-tone descending square blip (~120ms). Pure. */
export function errorBlipSpec() {
  return {
    hiHz:    784,   // ~G5
    loHz:    523,   // ~C5 — the classic falling "error" interval
    toneSec: 0.06,  // hi then lo, each ~60ms => ~120ms blip
    gain:    0.15,
  };
}

/* ----------------------------------------------------------------------------
 * GENERIC INTAKE SFX LIBRARY — owner-approved mp3 cues under the intake tree
 * (Resources/web/intake/assets/sfx/), keyed by id. Mirrors the chime/pop lazy-
 * sample pattern: URLs resolve against THIS MODULE (import.meta.url) with a
 * page-relative string fallback; lazy fetch + decodeAudioData; per-id
 * AudioBuffer[] cache; every voice routes through the SAME shared limiter.
 *
 * The manifest may be fetched lazily (sfx_manifest.json) OR fall back to this
 * inline table — the two stay in sync as gen_intake_sfx.py authors the files.
 * Files are <id>-<n>.mp3. The manifest `gain` balances harsh (glitch) vs soft
 * (melt) at author time; the shared limiter still brick-walls the sum. `loop`
 * marks seamless loops.
 *
 * MISSING FILES ARE SILENT-SAFE: playSfx() looks the id up here (sfxEntry), then
 * tries to fetch <id>-<n>.mp3; a 404 leaves the id un-decoded and playSfx returns
 * null (no throw, no console noise). So an id may be REGISTERED here before its
 * audio exists — the cue simply stays silent until the mp3 lands, then goes live
 * with no code change. That is how the captcha family below is wired ahead of its
 * assets: `stamp` + `chime` are fired TODAY by the shared chrome kit
 * (render/captcha/chrome.js: stamp() -> 'stamp'; spinner.resolve(ok) -> 'chime')
 * and are silent purely because they were unregistered; the remaining ids are the
 * per-item `sfxWanted` cues named in banks/_staging/cap_*.json, registered here so
 * the renderers can switch off their current substitutes the moment the files ship.
 * -------------------------------------------------------------------------- */
export const SFX_MANIFEST_FALLBACK = Object.freeze({
  'card-melt':          { variants: 2, gain: 0.5,  loop: false },
  'loom-spiral-up':     { variants: 1, gain: 0.15, loop: false },
  'loom-spiral-down':   { variants: 1, gain: 0.15, loop: false },
  'glitch-burst':       { variants: 2, gain: 0.9,  loop: false },
  'error-blip':         { variants: 1, gain: 0.9,  loop: false },
  'freeze-sting':       { variants: 1, gain: 0.9,  loop: false },
  'destruct-shatter':   { variants: 2, gain: 0.5,  loop: false },
  'pink-wash':          { variants: 2, gain: 0.15, loop: false },
  'drain-wash':         { variants: 1, gain: 0.4,  loop: false },
  'sticker-drag':       { variants: 2, gain: 0.15, loop: false },
  'surface-bloom':      { variants: 1, gain: 0.45, loop: false },
  'briefing-open':      { variants: 1, gain: 0.5,  loop: false },
  'pressure-heartbeat': { variants: 2, gain: 0.5,  loop: true  },
  'incorrect-feedback': { variants: 2, gain: 0.15, loop: false },
  // ---- CAPTCHA CHROME (fired today by render/captcha/chrome.js; silent until authored) ----
  'stamp':              { variants: 1, gain: 0.45, loop: false }, // chrome.stamp() LOGGED/verdict thunk (chrome-wide)
  'chime':              { variants: 1, gain: 0.4,  loop: false }, // chrome.spinner.resolve(true) green-check confirm
  // ---- CAPTCHA per-item wanted cues (banks/_staging/cap_*.json sfxWanted; forward-registered, no files yet) ----
  'grid-tile-flicker':  { variants: 2, gain: 0.2,  loop: false }, // cap_grid: tile flicks to a user asset and back
  'grid-verify-stamp':  { variants: 1, gain: 0.45, loop: false }, // cap_grid: VeriTru verdict stamp
  'grid-settle':        { variants: 1, gain: 0.2,  loop: false }, // cap_grid: frozen climax/recovery tile settle
  'verify-hold':        { variants: 1, gain: 0.15, loop: false }, // cap_checkbox: rising tone under press-and-hold fill
  'verify-lock':        { variants: 1, gain: 0.45, loop: false }, // cap_checkbox: the check greens / hold completes
  'checkbox-tick':      { variants: 1, gain: 0.2,  loop: false }, // cap_checkbox: soft tick as the box first fills
  'captcha-garble':     { variants: 1, gain: 0.4,  loop: false }, // cap_transcribe: warbly non-speech audio-icon garble
  'captcha-rewarp':     { variants: 1, gain: 0.15, loop: false }, // cap_transcribe: paper/ink re-warp whir (refresh)
  'captcha-verify-ok':  { variants: 1, gain: 0.45, loop: false }, // cap_transcribe: quiet green-check confirm on accept
  'captcha-reject':     { variants: 2, gain: 0.15, loop: false }, // cap_transcribe: control-word miss "try again" blip
  'captcha-logged':     { variants: 1, gain: 0.45, loop: false }, // cap_transcribe: LOGGED rubber-stamp thunk
  'verify-tick':        { variants: 2, gain: 0.1,  loop: false }, // cap_regen: ~1/s progress heartbeat during a regen clear
  // ---- CAPTCHA per-item wanted cues (banks/_staging/cap_regen.json sfxWanted; forward-registered) ----
  'regen-swarm':        { variants: 2, gain: 0.3,  loop: false }, // cap_regen: climax auto-spawn onset swell
  'regen-loop':         { variants: 1, gain: 0.32, loop: false }, // cap_regen: "end of library reached. looping." seam click
});

/** The sfx manifest URL (resolved against this module; page-relative fallback). */
export function sfxManifestUrl() {
  const rel = '../assets/sfx/sfx_manifest.json';
  try { return new URL(rel, import.meta.url).href; }
  catch (_e) { return './assets/sfx/sfx_manifest.json'; }
}
/** One sfx sample URL for `id` variant `n` (1-based). Same resolution rules. */
export function sfxSampleUrl(id, n) {
  const rel = '../assets/sfx/' + id + '-' + n + '.mp3';
  try { return new URL(rel, import.meta.url).href; }
  catch (_e) { return './assets/sfx/' + id + '-' + n + '.mp3'; }
}
/** Manifest gain x already-clamped intensity -> the buffer-source gain. Pure. */
export function sfxPlaybackGain(manifestGain, clampedIntensity) {
  const g = (typeof manifestGain === 'number' && manifestGain >= 0) ? manifestGain : 0.5;
  return g * clamp01(clampedIntensity == null ? 1 : clampedIntensity);
}
/** No-repeat-last variant pick over 1..count (returns 1 when count<=1). Pure. */
export function pickSfxVariant(count, last, rnd) {
  const n = Math.max(1, count | 0);
  if (n === 1) return 1;
  const r = (typeof rnd === 'function') ? rnd : Math.random;
  let v = 1 + ((r() * n) | 0);
  if (v > n) v = n;
  if (v === last) { v = v === n ? 1 : v + 1; } // one deterministic step off the repeat
  return v;
}

/* ----------------------------------------------------------------------------
 * VOICEOVER (spoken prompt lines) — the foreground interviewer voice. Directed
 * mp3s under the intake tree (Resources/web/intake/assets/vo/), described by
 * vo_manifest.json ([{ id, text, file, ... }]). URLs resolve against THIS MODULE
 * (import.meta.url) with a page-relative string fallback, EXACTLY like the sfx /
 * chime loaders. The manifest may list ~600 lines, so we NEVER prefetch: each id
 * is lazily fetched+decoded on first play and kept in a tiny LRU (last VOICE_CACHE_MAX
 * buffers) — these are one-shot spoken lines, not loops. A missing manifest entry
 * or a 404/decode failure is SILENT (text-only) and never surfaced to gameplay.
 * -------------------------------------------------------------------------- */
/** Foreground spoken-voice gain (moderate — sits above the bed, under the limiter). */
export const VOICE_GAIN = 0.8;
/** While a voice line plays, the binaural bed glides to this fraction of its level. */
export const VOICE_BED_DUCK = 0.6;
/** LRU cap: keep the last ~8 decoded line buffers (one-shot lines, evict oldest). */
export const VOICE_CACHE_MAX = 8;

/** The vo manifest URL (resolved against this module; page-relative fallback). */
export function voManifestUrl() {
  const rel = '../assets/vo/vo_manifest.json';
  try { return new URL(rel, import.meta.url).href; }
  catch (_e) { return './assets/vo/vo_manifest.json'; }
}
/** One vo sample URL for `file` (the manifest's `file` field). Same resolution rules. */
export function voSampleUrl(file) {
  const rel = '../assets/vo/' + file;
  try { return new URL(rel, import.meta.url).href; }
  catch (_e) { return './assets/vo/' + file; }
}

/* Full depth->binauralDepth channel read (curve x caps in one place). */
function binauralChannel(depth, caps) {
  return clampToCaps(depthToChannels(clamp01(depth)), caps).binauralDepth;
}

/* ----------------------------------------------------------------------------
 * SELF-CONTAINED MUTE — a tiny master switch (persisted). Kept local so audio.js
 * never depends on the dtrh shared/audioMute.js module.
 * -------------------------------------------------------------------------- */
const MUTE_KEY = 'intake-audio-muted';
let _muted = false;
try { _muted = (typeof localStorage !== 'undefined') && localStorage.getItem(MUTE_KEY) === '1'; } catch (_e) {}
export function isMuted() { return _muted; }
export function setMuted(v) {
  _muted = !!v;
  try { if (typeof localStorage !== 'undefined') localStorage.setItem(MUTE_KEY, _muted ? '1' : '0'); } catch (_e) {}
  return _muted;
}

/* ----------------------------------------------------------------------------
 * FACTORY
 * -------------------------------------------------------------------------- */
const GESTURES = ['pointerdown', 'touchstart', 'keydown', 'wheel'];
const GLIDE_SEC   = 0.6;   // depth-change glide (no zipper noise)
const EMERGE_SEC  = 4.0;   // recovery walk-out
/** Window CustomEvent effects.js fires when a spiral/sublim garnish shows —
 *  the same literal lives there (contracts.js stays untouched). */
export const GARNISH_CUE_EVENT = 'intake-garnish';
/** Window CustomEvent for the generic SFX seam: effects.js / steering.js (which
 *  hold no audio handle) dispatch { id, intensity?, variant?, loop? } and audio
 *  routes it to sfx(). The same literal is duplicated in those modules. */
export const SFX_CUE_EVENT = 'intake-sfx';

export function createAudio({ caps } = {}) {
  let ctx = null;
  let dead = false;             // constructor unavailable / failed: stop trying
  let resumeHook = null;

  // graph nodes
  let oscL = null, oscR = null;
  let gainL = null, gainR = null;
  let merger = null, lowpass = null, binGain = null, limiter = null;
  let started = false;
  // USER PREF BUSES (see the header note): one gain node per category, sitting
  // between every existing per-voice gain and the shared limiter.
  let binBus = null, fxBus = null, voiceBus = null;

  let depthNow = 0;             // last requested depth (for lazy graph seed)
  let noiseBuf = null;          // shared 1s white-noise buffer (lazy, per ctx)
  let chimeBufs = [];           // decoded REAL chime AudioBuffers (lazy)
  let chimeLoad = 'idle';       // 'idle' | 'loading' | 'ready' | 'dead'
  let popBuf = null;            // decoded REAL Pop.mp3 AudioBuffer (lazy)
  let popLoad = 'idle';         // 'idle' | 'loading' | 'ready' | 'dead'
  // generic sfx library: per-id AudioBuffer[] + load state + variant rotation
  let sfxManifest = SFX_MANIFEST_FALLBACK; // inline fallback until (maybe) fetched
  let sfxManifestState = 'idle';           // 'idle' | 'loading' | 'ready' | 'dead'
  const sfxBufs = {};           // id -> AudioBuffer[]
  const sfxLoad = {};           // id -> 'idle' | 'loading' | 'ready' | 'dead'
  const sfxLastVariant = {};    // id -> last-played variant (no-repeat-last)
  let bedSuspended = false;     // suspendBed() latch (jumpscare event freeze)
  let bedSavedGain = null;      // binGain level captured at suspend (restored on resume)
  // VOICEOVER state: cached manifest + a tiny LRU of decoded line buffers, plus
  // the single live voice handle (only one VO plays at a time) + bed-duck latch.
  let voManifest = null;        // id -> manifest entry (resolved; {} on failure)
  let voManifestPromise = null; // cached fetch promise (never throws, never re-fetches)
  const voCache = new Map();    // id -> AudioBuffer (LRU: insertion order = recency)
  const voLoading = new Map();  // id -> in-flight Promise<AudioBuffer|null> (dedupe)
  let voHandle = null;          // the current live voice { stop(fade) } (one at a time)
  let voToken = 0;              // bumped per voice() call — invalidates older async loads
  let voDucked = false;         // the bed is currently ducked for a voice line
  let voBedSaved = null;        // binGain level captured at duck (restored on voice end)

  function capsOf() { return caps || undefined; }

  /** Diagnostic seam (same pattern as effects.js): the app has no devtools,
   *  but page logs reach C# via the shim. Harmless if nobody listens. */
  function logSeam(msg) {
    try {
      if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
        window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixau: ' + msg } }));
      }
    } catch (_e) {}
  }

  function ensureCtx() {
    if (dead) return null;
    if (!ctx) {
      const AC = (typeof window !== 'undefined') && (window.AudioContext || window.webkitAudioContext);
      if (!AC) { dead = true; return null; }
      try { ctx = new AC(); } catch (_e) { dead = true; return null; }
      resumeHook = () => { if (ctx && ctx.state === 'suspended') ctx.resume().catch(() => {}); };
      if (typeof window !== 'undefined') {
        GESTURES.forEach((ev) => window.addEventListener(ev, resumeHook, { passive: true }));
      }
    }
    if (ctx.state === 'suspended') ctx.resume().catch(() => {});
    return ctx;
  }

  /** Where a bus should currently point: the shared limiter once the bed graph
   *  exists, the destination before that (mirrors routeOut's old rule). */
  function busTarget(c) { return (started && limiter) ? limiter : c.destination; }

  /** Create the three pref buses once, seeded from the CURRENT prefs. Safe to
   *  call before the bed graph exists — they land on the destination and are
   *  re-pointed at the limiter by rewireBuses() as soon as ensureGraph() runs.
   *  Returns true when the buses are usable. Never throws. */
  function ensureBuses(c) {
    if (!c) return false;
    if (fxBus) return true;
    try {
      const t = busTarget(c);
      binBus   = c.createGain(); binBus.gain.value   = binauralScale();
      fxBus    = c.createGain(); fxBus.gain.value    = effectsScale();
      voiceBus = c.createGain(); voiceBus.gain.value = voiceScale();
      binBus.connect(t); fxBus.connect(t); voiceBus.connect(t);
      return true;
    } catch (_e) {
      binBus = fxBus = voiceBus = null;
      return false;
    }
  }

  /** Move the buses onto the limiter once the bed graph has come up. */
  function rewireBuses() {
    if (!fxBus || !limiter) return;
    for (const b of [binBus, fxBus, voiceBus]) {
      if (!b) continue;
      try { b.disconnect(); } catch (_e) {}
      try { b.connect(limiter); } catch (_e) {}
    }
  }

  /** Build the binaural graph once. Starts silent (gain 0) at the current depth. */
  function ensureGraph() {
    const c = ensureCtx();
    if (!c || started) return c;
    try {
      const { beatHz, carrierHz } = binauralToHz(binauralChannel(depthNow, capsOf()));

      oscL = c.createOscillator(); oscL.type = 'sine'; oscL.frequency.value = carrierHz;
      oscR = c.createOscillator(); oscR.type = 'sine'; oscR.frequency.value = carrierHz + beatHz;

      gainL = c.createGain(); gainL.gain.value = 0.5;
      gainR = c.createGain(); gainR.gain.value = 0.5;

      merger = c.createChannelMerger(2);
      oscL.connect(gainL); gainL.connect(merger, 0, 0);   // L
      oscR.connect(gainR); gainR.connect(merger, 0, 1);   // R

      lowpass = c.createBiquadFilter();
      lowpass.type = 'lowpass'; lowpass.frequency.value = 900; lowpass.Q.value = 0.6;

      binGain = c.createGain(); binGain.gain.value = 0;    // fade in via setDepth

      limiter = c.createDynamicsCompressor();
      // gentle brick-wall so summed sines + reward voices never clip.
      try {
        limiter.threshold.value = -6; limiter.knee.value = 6;
        limiter.ratio.value = 12; limiter.attack.value = 0.003; limiter.release.value = 0.25;
      } catch (_e) {}

      ensureBuses(c);   // may already exist (a one-shot fired before the bed)
      merger.connect(lowpass); lowpass.connect(binGain);
      binGain.connect(binBus || limiter);
      limiter.connect(c.destination);

      oscL.start(); oscR.start();
      started = true;
      rewireBuses();    // buses now point at the limiter, not the destination
    } catch (_e) {
      started = false;
    }
    return c;
  }

  function glide(param, value, sec) {
    if (!param || !ctx) return;
    try {
      const t = ctx.currentTime;
      param.cancelScheduledValues(t);
      // setTargetAtTime gives a smooth exponential-ish glide with no zipper.
      param.setValueAtTime(param.value, t);
      param.setTargetAtTime(value, t, Math.max(0.02, sec / 3));
    } catch (_e) {
      try { param.value = value; } catch (_e2) {}
    }
  }

  /* ----- one-shot plumbing --------------------------------------------------- */

  /** Route a one-shot's output through the EFFECTS pref bus and on into the
   *  shared limiter (topology invariant). Falls back to the old direct wiring if
   *  the bus could not be created. */
  function routeOut(c, node) {
    if (ensureBuses(c) && fxBus) { node.connect(fxBus); return; }
    node.connect(busTarget(c));
  }

  /** Same, for spoken VO — the VOICE pref bus (its own slider + on/off switch). */
  function routeVoice(c, node) {
    if (ensureBuses(c) && voiceBus) { node.connect(voiceBus); return; }
    node.connect(busTarget(c));
  }

  /** Shared 1s white-noise buffer (lazy per-context). */
  function ensureNoise(c) {
    if (noiseBuf && noiseBuf.sampleRate === c.sampleRate) return noiseBuf;
    const len = Math.max(1, Math.floor(c.sampleRate * 1.0));
    const buf = c.createBuffer(1, len, c.sampleRate);
    const data = buf.getChannelData(0);
    for (let k = 0; k < len; k++) data[k] = Math.random() * 2 - 1;
    noiseBuf = buf;
    return buf;
  }

  /** A gain envelope node: fast/linear attack then exponential release. */
  function envOut(c, t0, peak, attackSec, durSec) {
    const out = c.createGain();
    out.gain.setValueAtTime(0, t0);
    out.gain.linearRampToValueAtTime(peak, t0 + Math.max(0.005, attackSec));
    out.gain.exponentialRampToValueAtTime(0.0001, t0 + durSec);
    routeOut(c, out);
    return out;
  }

  /** A noise burst through a biquad filter into `out`. */
  function noiseInto(c, out, t0, { type, hz, q = 1, gain = 1, durSec }) {
    const src = c.createBufferSource();
    src.buffer = ensureNoise(c);
    const filt = c.createBiquadFilter();
    filt.type = type; filt.frequency.value = hz; filt.Q.value = q;
    const g = c.createGain(); g.gain.value = gain;
    src.connect(filt); filt.connect(g); g.connect(out);
    src.start(t0);
    src.stop(t0 + durSec + 0.05);
    return filt; // caller may automate filt.frequency (sweeps)
  }

  /* ----- reward voices (one per RewardKind) --------------------------------- */

  /** Kick the lazy fetch+decode of the real chime mp3s. Fire-and-forget: the
   *  caller plays the synth bell until chimeLoad flips to 'ready'. Tolerates
   *  partial failure (keeps whatever decoded); total failure -> 'dead' (synth
   *  forever) + an 'intake-log' event so the shim can surface it. */
  function loadChimes(c) {
    if (chimeLoad !== 'idle') return;
    chimeLoad = 'loading';
    (async () => {
      const fails = [];
      try {
        if (typeof fetch !== 'function') throw new Error('no fetch in this host');
        const urls = chimeSampleUrls();
        const settled = await Promise.all(urls.map(async (u) => {
          try {
            const r = await fetch(u);
            if (!r.ok) throw new Error('HTTP ' + r.status);
            const ab = await r.arrayBuffer();
            return await c.decodeAudioData(ab);
          } catch (e) {
            fails.push(u.split('/').pop() + ': ' + ((e && e.message) ? e.message : String(e)));
            return null;
          }
        }));
        chimeBufs = settled.filter((b) => b != null);
        chimeLoad = chimeBufs.length ? 'ready' : 'dead';
      } catch (e) {
        chimeBufs = [];
        chimeLoad = 'dead';
        fails.push((e && e.message) ? e.message : String(e));
      }
      if (fails.length) {
        logSeam('chime mp3 load ' + (chimeLoad === 'dead' ? 'failed (synth fallback): ' : 'partial: ') + fails.join('; '));
      }
    })();
  }

  /** Chime — the app's REAL chime: one of the decoded mp3s at random, gain from
   *  the clamped intensity, streak pitch via playbackRate, out through the
   *  shared limiter like every other voice. */
  function mp3Chime(c, intensity, pitchFactor) {
    const spec = chimeSampleSpec(intensity);
    const buf = chimeBufs[(Math.random() * chimeBufs.length) | 0];
    const t0 = c.currentTime;
    const src = c.createBufferSource();
    src.buffer = buf;
    try { src.playbackRate.value = pitchFactor; } catch (_e) {}
    const g = c.createGain();
    g.gain.value = spec.gain;
    src.connect(g);
    routeOut(c, g);
    src.start(t0);
  }

  /** Bell — the original soft synth bell (now the Chime FALLBACK: plays until
   *  the real mp3s decode, and forever if they never do). Streak pitch applies. */
  function bellChime(c, intensity, pitchFactor) {
    const spec = chimeSpec(intensity);
    const t0 = c.currentTime;
    const out = envOut(c, t0, spec.gain, 0.012, spec.durSec);
    spec.partials.forEach((mult, idx) => {
      const o = c.createOscillator();
      o.type = idx === 0 ? 'triangle' : 'sine';
      o.frequency.value = spec.baseHz * pitchFactor * mult;
      const pg = c.createGain();
      pg.gain.value = 1 / (mult * 1.6); // higher partials quieter
      o.connect(pg); pg.connect(out);
      o.start(t0);
      o.stop(t0 + spec.durSec + 0.05);
    });
  }

  /** Bubble — watery blip: pitch-down sine "bloop" + short noise splash. */
  function bubbleBlip(c, intensity, pitchFactor) {
    const spec = bubbleSpec(intensity);
    const t0 = c.currentTime;
    const out = envOut(c, t0, spec.gain, 0.008, spec.durSec);
    const o = c.createOscillator();
    o.type = 'sine';
    try {
      o.frequency.setValueAtTime(spec.startHz * pitchFactor, t0);
      o.frequency.exponentialRampToValueAtTime(
        Math.max(40, spec.endHz * pitchFactor), t0 + spec.durSec * 0.8);
    } catch (_e) { o.frequency.value = spec.endHz * pitchFactor; }
    o.connect(out);
    o.start(t0);
    o.stop(t0 + spec.durSec + 0.05);
    // the splash: a breath of bandpassed noise on top, quick decay
    noiseInto(c, out, t0, {
      type: 'bandpass', hz: spec.noiseHz, q: 1.2, gain: 0.5, durSec: spec.noiseSec,
    });
  }

  /** Flash — airy shimmer: bandpassed noise swept upward, swell-release. */
  function flashSweep(c, intensity) {
    const spec = flashSweepSpec(intensity);
    const t0 = c.currentTime;
    const out = envOut(c, t0, spec.gain, spec.durSec * 0.35, spec.durSec);
    const filt = noiseInto(c, out, t0, {
      type: 'bandpass', hz: spec.startHz, q: spec.q, gain: 1, durSec: spec.durSec,
    });
    try {
      filt.frequency.setValueAtTime(spec.startHz, t0);
      filt.frequency.exponentialRampToValueAtTime(spec.endHz, t0 + spec.durSec);
    } catch (_e) {}
  }

  /** Praise — warm two-note major swell (root, then the third blooms in). */
  function praiseSwell(c, intensity) {
    const spec = praiseSwellSpec(intensity);
    const t0 = c.currentTime;
    const out = envOut(c, t0, spec.gain, spec.attackSec, spec.durSec);
    const notes = [
      { hz: spec.rootHz,                    at: t0,                   g: 1.0, type: 'triangle' },
      { hz: spec.rootHz * spec.thirdRatio,  at: t0 + spec.thirdDelay, g: 0.7, type: 'sine' },
    ];
    for (const n of notes) {
      const o = c.createOscillator();
      o.type = n.type;
      o.frequency.value = n.hz;
      const pg = c.createGain(); pg.gain.value = n.g;
      o.connect(pg); pg.connect(out);
      o.start(n.at);
      o.stop(t0 + spec.durSec + 0.05);
    }
  }

  /** Drop — low soft boom, gliding down under the binaural bed. */
  function dropBoom(c, intensity) {
    const spec = dropBoomSpec(intensity);
    const t0 = c.currentTime;
    const out = envOut(c, t0, spec.gain, 0.015, spec.durSec);
    const o = c.createOscillator();
    o.type = 'sine';
    try {
      o.frequency.setValueAtTime(spec.startHz, t0);
      o.frequency.exponentialRampToValueAtTime(spec.endHz, t0 + spec.durSec * 0.9);
    } catch (_e) { o.frequency.value = spec.endHz; }
    o.connect(out);
    o.start(t0);
    o.stop(t0 + spec.durSec + 0.05);
    // soft body thump: lowpassed noise, very short
    noiseInto(c, out, t0, {
      type: 'lowpass', hz: spec.thumpHz, q: 0.7, gain: 0.6, durSec: spec.thumpSec,
    });
  }

  /** Jackpot — short rising 3-note sting + a sparkle of highpassed noise. */
  function sting(c, intensity) {
    const spec = stingSpec(intensity);
    const t0 = c.currentTime;
    spec.notesHz.forEach((hz, k) => {
      const at = t0 + k * spec.noteGapSec;
      const out = envOut(c, at, spec.gain * (0.8 + k * 0.1), 0.010, spec.noteSec);
      [1, 2].forEach((mult, idx) => {
        const o = c.createOscillator();
        o.type = idx === 0 ? 'triangle' : 'sine';
        o.frequency.value = hz * mult;
        const pg = c.createGain(); pg.gain.value = idx === 0 ? 1 : 0.35;
        o.connect(pg); pg.connect(out);
        o.start(at);
        o.stop(at + spec.noteSec + 0.05);
      });
    });
    // shimmer tail under the notes
    const shT0 = t0 + spec.noteGapSec;
    const out = envOut(c, shT0, spec.gain * 0.35, spec.shimmerSec * 0.3, spec.shimmerSec);
    noiseInto(c, out, shT0, {
      type: 'highpass', hz: spec.shimmerHz, q: 0.8, gain: 1, durSec: spec.shimmerSec,
    });
  }

  /** Near-miss — one muted tick (the slot machine that almost paid). */
  function nearMissTick(c, intensity) {
    const spec = nearMissTickSpec(intensity);
    const t0 = c.currentTime;
    const out = envOut(c, t0, spec.gain, 0.004, spec.durSec);
    noiseInto(c, out, t0, {
      type: 'bandpass', hz: spec.hz, q: 2.0, gain: 1, durSec: spec.durSec,
    });
  }

  /** Kick the lazy fetch+decode of the real Pop.mp3. Fire-and-forget: pop()
   *  plays the synth burst until popLoad flips to 'ready', and forever if the
   *  fetch/decode fails (file:// / missing asset -> 'dead' + an 'intake-log'). */
  function loadPop(c) {
    if (popLoad !== 'idle') return;
    popLoad = 'loading';
    (async () => {
      try {
        if (typeof fetch !== 'function') throw new Error('no fetch in this host');
        const r = await fetch(popSampleUrl());
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const ab = await r.arrayBuffer();
        popBuf = await c.decodeAudioData(ab);
        popLoad = 'ready';
      } catch (e) {
        popBuf = null;
        popLoad = 'dead';
        logSeam('pop mp3 load failed (synth fallback): ' + ((e && e.message) ? e.message : String(e)));
      }
    })();
  }

  /** Pop (real Pop.mp3): one shot at random-free single sample, gain from the
   *  clamped intensity, out through the shared limiter like every other voice. */
  function popSample(c, intensity, rate) {
    const spec = popSampleSpec(intensity, rate);
    const t0 = c.currentTime;
    const src = c.createBufferSource();
    src.buffer = popBuf;
    if (spec.rate !== 1) { try { src.playbackRate.value = spec.rate; } catch (_e) {} }
    const g = c.createGain();
    g.gain.value = spec.gain;
    src.connect(g);
    routeOut(c, g);
    src.start(t0);
  }

  /** Pop synth fallback — short bandpassed noise burst + a low sine thump. */
  function popSynth(c, intensity, rate) {
    const spec = popSynthSpec(intensity, rate);
    const t0 = c.currentTime;
    // the burst
    const out = envOut(c, t0, spec.gain, 0.002, spec.noiseSec);
    noiseInto(c, out, t0, {
      type: 'bandpass', hz: spec.noiseHz, q: 0.8, gain: 1, durSec: spec.noiseSec,
    });
    // the low thump under it
    const tout = envOut(c, t0, spec.gain * 0.9, 0.003, spec.thumpSec);
    const o = c.createOscillator();
    o.type = 'sine';
    try {
      o.frequency.setValueAtTime(spec.thumpHz, t0);
      o.frequency.exponentialRampToValueAtTime(Math.max(30, spec.thumpEnd), t0 + spec.thumpSec * 0.9);
    } catch (_e) { o.frequency.value = spec.thumpEnd; }
    o.connect(tout);
    o.start(t0);
    o.stop(t0 + spec.thumpSec + 0.05);
  }

  /* ----- generic sfx library ------------------------------------------------- */

  /** The per-id manifest entry (fetched-or-inline; inline fallback guaranteed). */
  function sfxEntry(id) {
    return (sfxManifest && sfxManifest[id]) || SFX_MANIFEST_FALLBACK[id] || null;
  }
  function sfxReady(id) {
    return sfxLoad[id] === 'ready' && sfxBufs[id] && sfxBufs[id].length > 0;
  }

  /** Lazily fetch sfx_manifest.json to OVERRIDE the inline table. Never throws;
   *  on failure the inline SFX_MANIFEST_FALLBACK stays authoritative. */
  function loadManifest() {
    if (sfxManifestState !== 'idle') return;
    sfxManifestState = 'loading';
    (async () => {
      try {
        if (typeof fetch !== 'function') throw new Error('no fetch in this host');
        const r = await fetch(sfxManifestUrl());
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const j = await r.json();
        if (j && typeof j === 'object') sfxManifest = Object.assign({}, SFX_MANIFEST_FALLBACK, j);
        sfxManifestState = 'ready';
      } catch (e) {
        sfxManifest = SFX_MANIFEST_FALLBACK;
        sfxManifestState = 'dead';
        logSeam('sfx manifest load failed (inline fallback): ' + ((e && e.message) ? e.message : String(e)));
      }
    })();
  }

  /** Lazily fetch+decode every variant of one sfx id. Fire-and-forget; tolerates
   *  partial failure (keeps what decoded); total failure -> 'dead' + a log. */
  function loadSfx(c, id) {
    if (sfxLoad[id] && sfxLoad[id] !== 'idle') return;
    const entry = sfxEntry(id);
    if (!entry) { sfxLoad[id] = 'dead'; return; }
    sfxLoad[id] = 'loading';
    const count = Math.max(1, entry.variants | 0);
    (async () => {
      const fails = [];
      try {
        if (typeof fetch !== 'function') throw new Error('no fetch in this host');
        const urls = [];
        for (let n = 1; n <= count; n++) urls.push(sfxSampleUrl(id, n));
        const settled = await Promise.all(urls.map(async (u) => {
          try {
            const r = await fetch(u);
            if (!r.ok) throw new Error('HTTP ' + r.status);
            const ab = await r.arrayBuffer();
            return await c.decodeAudioData(ab);
          } catch (e) {
            fails.push(u.split('/').pop() + ': ' + ((e && e.message) ? e.message : String(e)));
            return null;
          }
        }));
        sfxBufs[id] = settled.filter((b) => b != null);
        sfxLoad[id] = sfxBufs[id].length ? 'ready' : 'dead';
      } catch (e) {
        sfxBufs[id] = []; sfxLoad[id] = 'dead';
        fails.push((e && e.message) ? e.message : String(e));
      }
      if (fails.length) {
        logSeam('sfx "' + id + '" load ' + (sfxLoad[id] === 'dead' ? 'failed: ' : 'partial: ') + fails.join('; '));
      }
    })();
  }

  /** Warm (fetch+decode) an sfx id without playing it — for loops that must be
   *  ready by the time their event fires (e.g. the urgent-timer heartbeat). */
  function warmSfx(id) {
    const c = ensureCtx();
    if (!c) return;
    loadManifest();
    loadSfx(c, id);
  }

  /** Synth fallback for the sfx ids that HAVE one (glitch/error) while their
   *  samples are still decoding / if they never do. Others resolve silently. */
  function sfxSynthFallback(c, id) {
    try {
      if (id === 'glitch-burst') { glitchBurst(c); return true; }
      if (id === 'error-blip')   { errorBlip(c, c.currentTime); return true; }
    } catch (_e) {}
    return false;
  }

  /** Play one sfx cue. Returns a handle { stop(fadeSec?) } (loops must stop; the
   *  handle is inert for one-shots). Respects mute + clampIntensity caps; applies
   *  the manifest gain; variant = no-repeat-last unless opts.variant pins it.
   *  Never throws. Warms the manifest + this id's samples on first use. */
  function playSfx(id, intensity, opts) {
    if (_muted || typeof id !== 'string') return null;
    opts = opts || {};
    const c = ensureCtx();
    if (!c) return null;
    loadManifest();
    loadSfx(c, id);
    const entry = sfxEntry(id);
    if (!entry) return null;
    if (!sfxReady(id)) { sfxSynthFallback(c, id); return null; } // not decoded yet
    const bufs = sfxBufs[id];
    const clamped = clampIntensity(intensity == null ? 1 : intensity, capsOf());
    if (clamped <= 0.0005) return null; // caps disabled this voice
    const gain = sfxPlaybackGain(entry.gain, clamped);
    const wantLoop = (opts.loop != null) ? !!opts.loop : !!entry.loop;
    // variant: explicit pin (clamped to what actually decoded) else no-repeat-last
    let idx;
    if (opts.variant != null && opts.variant >= 1 && opts.variant <= bufs.length) {
      idx = (opts.variant | 0) - 1;
    } else {
      const v = pickSfxVariant(bufs.length, sfxLastVariant[id] || 0);
      sfxLastVariant[id] = v;
      idx = v - 1;
    }
    const buf = bufs[idx] || bufs[0];
    try {
      const src = c.createBufferSource();
      src.buffer = buf;
      src.loop = wantLoop;
      const g = c.createGain();
      g.gain.value = gain;
      src.connect(g);
      routeOut(c, g);
      const t0 = c.currentTime;
      src.start(t0);
      let stopped = false;
      const stop = (fadeSec) => {
        if (stopped) return; stopped = true;
        try {
          const f = Math.max(0, fadeSec == null ? 0.2 : fadeSec);
          const tn = c.currentTime;
          g.gain.cancelScheduledValues(tn);
          g.gain.setValueAtTime(g.gain.value, tn);
          g.gain.setTargetAtTime(0.0001, tn, Math.max(0.02, f / 3));
          src.stop(tn + f + 0.05);
        } catch (_e) { try { src.stop(); } catch (_e2) {} }
      };
      // one-shots self-terminate; loops run until stop()
      if (!wantLoop) { try { src.stop(t0 + buf.duration + 0.05); } catch (_e) {} }
      return { stop, source: src, loop: wantLoop };
    } catch (_e) { return null; }
  }

  /* ----- voiceover (spoken prompt lines) ------------------------------------ */

  /** Lazily fetch+parse vo_manifest.json ONCE (cached promise, never throws).
   *  Resolves to an { id -> entry } map; on any failure resolves to {} so every
   *  later id lookup simply misses and plays silent (text-only). */
  function loadVoManifest() {
    if (voManifestPromise) return voManifestPromise;
    voManifestPromise = (async () => {
      try {
        if (typeof fetch !== 'function') throw new Error('no fetch in this host');
        const r = await fetch(voManifestUrl());
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const j = await r.json();
        const map = {};
        const entries = (j && Array.isArray(j.entries)) ? j.entries : [];
        for (const e of entries) { if (e && typeof e.id === 'string') map[e.id] = e; }
        voManifest = map;
        return map;
      } catch (e) {
        voManifest = voManifest || {};
        logSeam('vo manifest load failed (silent VO): ' + ((e && e.message) ? e.message : String(e)));
        return voManifest;
      }
    })();
    return voManifestPromise;
  }

  /** Evict oldest entries until the LRU is within cap (Map key order = recency). */
  function evictVoCache() {
    while (voCache.size > VOICE_CACHE_MAX) {
      const oldest = voCache.keys().next().value;
      if (oldest === undefined) break;
      voCache.delete(oldest);
    }
  }

  /** Lazily fetch+decode ONE line's buffer (LRU cache + in-flight dedupe). Resolves
   *  to the AudioBuffer, or null when the id is missing / fetch / decode fails
   *  (silent line). Never throws. */
  function loadVoBuffer(c, id) {
    if (voCache.has(id)) {
      const b = voCache.get(id);      // refresh recency: re-insert at the tail
      voCache.delete(id); voCache.set(id, b);
      return Promise.resolve(b);
    }
    if (voLoading.has(id)) return voLoading.get(id);
    const p = (async () => {
      try {
        const man = await loadVoManifest();
        const entry = man && man[id];
        if (!entry || typeof entry.file !== 'string' || !entry.file) return null; // missing = silent
        if (typeof fetch !== 'function') return null;
        const r = await fetch(voSampleUrl(entry.file));
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const ab = await r.arrayBuffer();
        const buf = await c.decodeAudioData(ab);
        voCache.set(id, buf); evictVoCache();
        return buf;
      } catch (e) {
        logSeam('vo "' + id + '" load failed (silent): ' + ((e && e.message) ? e.message : String(e)));
        return null;
      } finally {
        voLoading.delete(id);
      }
    })();
    voLoading.set(id, p);
    return p;
  }

  /** Duck the binaural bed to ~60% while a voice line plays. COMPOSES with
   *  suspendBed: if the bed is already suspended (jumpscare), do nothing — the
   *  suspend owns binGain. Partial glide, reusing glide(). */
  function duckBedForVoice() {
    if (voDucked || bedSuspended) return;
    if (!ctx || !started || !binGain) return;
    try {
      voBedSaved = binGain.gain.value;
      glide(binGain.gain, voBedSaved * VOICE_BED_DUCK, 0.25);
      voDucked = true;
    } catch (_e) {}
  }
  /** Restore the bed after a voice line ends. If the bed got suspended meanwhile
   *  (jumpscare — which subsumes the duck), leave it to resumeBed. */
  function restoreBedFromVoice() {
    if (!voDucked) { voBedSaved = null; return; }
    voDucked = false;
    if (bedSuspended) { voBedSaved = null; return; }   // suspend/resume owns binGain now
    if (!ctx || !started || !binGain) { voBedSaved = null; return; }
    try {
      const fallback = _muted ? 0 : binauralGainFor(binauralChannel(depthNow, capsOf()));
      const target = (voBedSaved != null) ? voBedSaved : fallback;
      glide(binGain.gain, target, 0.4);
    } catch (_e) {}
    voBedSaved = null;
  }

  /** Play ONE spoken line by id (lazy fetch+decode; missing/failed = silent).
   *  Starting a new voice STOPS the previous one (only one VO at a time). Ducks
   *  the bed while it plays; restores on end. `opts.onEnd` fires EXACTLY once —
   *  on natural end, on stop, or on a silent miss — so callers can chain lines.
   *  `opts.rate` (0.25..3, default 1) plays the line slower/faster (pitch rides
   *  with it — the buffer-source playbackRate) and `opts.offset` (seconds,
   *  default 0) starts it PART-WAY IN. Both exist for the CORRUPTED QUESTION
   *  set-piece in beats.js (stutter/chop/wobble garble); every other caller is
   *  unaffected. Returns a handle { stop(fade) }, or null when audio can't play
   *  at all (muted / no AudioContext) — in which case onEnd is still fired. */
  function playVoice(id, opts) {
    opts = opts || {};
    const onEnd = (typeof opts.onEnd === 'function') ? opts.onEnd : null;
    const fireEnd = () => { if (onEnd) { try { onEnd(); } catch (_e) {} } };
    if (typeof id !== 'string' || !id) { fireEnd(); return null; }
    if (_muted) { fireEnd(); return null; }
    // VOICEOVER OFF (or master/voice slider at 0): behave EXACTLY like the muted
    // path — fire onEnd once, return null. boot.js's intro chain treats a null
    // handle as "advance immediately" and beats.js only ever ignores the handle,
    // so a silent run paces itself the same as a missing VO file already does.
    if (voiceScale() <= 0) { fireEnd(); return null; }
    const c = ensureCtx();
    if (!c) { fireEnd(); return null; }

    // one VO at a time: stop whatever is currently speaking (quick fade).
    stopVoice(0.1);
    loadVoManifest();

    const token = ++voToken;
    let ended = false;
    let srcRef = null;
    let gainRef = null;
    let endTimer = 0;

    const finishOnce = () => {
      if (ended) return; ended = true;
      if (endTimer) { try { clearTimeout(endTimer); } catch (_e) {} endTimer = 0; }
      if (voToken === token) {            // only the CURRENT voice unwinds shared state
        restoreBedFromVoice();
        if (voHandle === handle) voHandle = null;
      }
      fireEnd();
    };
    const stop = (fade) => {
      if (ended) return;
      try {
        if (srcRef) {
          const f = Math.max(0, fade == null ? 0.2 : fade);
          const tn = c.currentTime;
          if (gainRef) {
            gainRef.gain.cancelScheduledValues(tn);
            gainRef.gain.setValueAtTime(gainRef.gain.value, tn);
            gainRef.gain.setTargetAtTime(0.0001, tn, Math.max(0.02, f / 3));
          }
          try { srcRef.stop(tn + f + 0.05); } catch (_e) {}
        }
      } catch (_e) {}
      finishOnce();
    };
    const handle = { stop };
    voHandle = handle;

    (async () => {
      const buf = await loadVoBuffer(c, id);
      if (voToken !== token || ended) return;   // superseded / stopped before decode
      if (!buf) { finishOnce(); return; }        // missing/failed line = silent, still ends
      try {
        duckBedForVoice();
        const src = c.createBufferSource();
        src.buffer = buf;
        // optional garble controls (default: untouched 1.0 / from the top)
        const rate = (typeof opts.rate === 'number' && opts.rate >= 0.25 && opts.rate <= 3)
          ? opts.rate : 1;
        const off = (typeof opts.offset === 'number' && opts.offset > 0
          && opts.offset < Math.max(0, buf.duration - 0.15)) ? opts.offset : 0;
        if (rate !== 1) { try { src.playbackRate.value = rate; } catch (_e) {} }
        const g = c.createGain();
        g.gain.value = VOICE_GAIN;
        src.connect(g);
        routeVoice(c, g);                        // VOICE pref bus -> SHARED limiter
        srcRef = src; gainRef = g;
        try { src.onended = () => finishOnce(); } catch (_e) {}
        src.start(c.currentTime, off);
        // belt-and-suspenders: onended can be unreliable on some hosts.
        endTimer = setTimeout(finishOnce, Math.max(200, ((buf.duration - off) / rate + 0.3) * 1000));
      } catch (_e) {
        finishOnce();
      }
    })();

    return handle;
  }

  /** Stop the current voice line (short fade). Idempotent; safe when nothing plays. */
  function stopVoice(fade) {
    const h = voHandle;
    voHandle = null;
    if (h && typeof h.stop === 'function') { try { h.stop(fade); } catch (_e) {} }
    else if (voDucked) restoreBedFromVoice();   // orphaned duck (no handle) — restore
  }

  /* ----- garnish cues (spiral shimmer / subliminal tick swell) ---------------- */

  /** Spiral garnish — soft airy shimmer: swept band of air + faint fifth pad. */
  function spiralShimmer(c, intensity) {
    const spec = spiralShimmerSpec(intensity);
    const t0 = c.currentTime;
    const out = envOut(c, t0, spec.gain, spec.attackSec, spec.durSec);
    const filt = noiseInto(c, out, t0, {
      type: 'bandpass', hz: spec.startHz, q: spec.q, gain: 1, durSec: spec.durSec,
    });
    try {
      filt.frequency.setValueAtTime(spec.startHz, t0);
      filt.frequency.exponentialRampToValueAtTime(spec.endHz, t0 + spec.durSec);
    } catch (_e) {}
    // the pad: root + fifth sines, well below the air
    [1, spec.toneRatio].forEach((ratio, idx) => {
      const o = c.createOscillator();
      o.type = 'sine';
      o.frequency.value = spec.toneHz * ratio;
      const pg = c.createGain(); pg.gain.value = spec.toneGain * (idx === 0 ? 1 : 0.6);
      o.connect(pg); pg.connect(out);
      o.start(t0);
      o.stop(t0 + spec.durSec + 0.05);
    });
  }

  /** Subliminal garnish — the tick train + one long whisper of breath. */
  function subliminalTicks(c, intensity) {
    const spec = subliminalTickSpec(intensity);
    const t0 = c.currentTime;
    for (let k = 0; k < spec.ticks; k++) {
      const at = t0 + k * spec.gapSec;
      const out = envOut(c, at, spec.gain, 0.004, spec.tickSec);
      noiseInto(c, out, at, {
        type: 'bandpass', hz: spec.tickHz, q: 2.2, gain: 1, durSec: spec.tickSec,
      });
    }
    const out = envOut(c, t0, spec.gain * spec.swellGain, spec.swellSec * 0.45, spec.swellSec);
    noiseInto(c, out, t0, {
      type: 'highpass', hz: spec.swellHz, q: 0.7, gain: 1, durSec: spec.swellSec,
    });
  }

  /* ----- jumpscare event voices (are-you-sure mechanic) --------------------- */

  /** Glitch burst — bit-crushy square stabs (lowpassed) + a bandpassed noise
   *  wash, all through the shared limiter. ~400ms. */
  function glitchBurst(c) {
    const spec = glitchStabSpec();
    const t0 = c.currentTime;
    for (let k = 0; k < spec.stabs; k++) {
      const at = t0 + k * spec.gapSec;
      const out = envOut(c, at, spec.gain, 0.004, spec.stabSec);
      const lp = c.createBiquadFilter();
      lp.type = 'lowpass'; lp.frequency.value = spec.crushHz; lp.Q.value = 0.7;
      lp.connect(out);
      const o = c.createOscillator();
      o.type = 'square';
      o.frequency.value = spec.freqs[k % spec.freqs.length];
      o.connect(lp);
      o.start(at);
      o.stop(at + spec.stabSec + 0.02);
    }
    const nOut = envOut(c, t0, spec.noiseGain, 0.02, spec.noiseSec);
    noiseInto(c, nOut, t0, {
      type: 'bandpass', hz: spec.noiseHz, q: 0.8, gain: 1, durSec: spec.noiseSec,
    });
  }

  /** One two-tone "error ding" scheduled at absolute time `at`. */
  function errorBlip(c, at) {
    const spec = errorBlipSpec();
    [[spec.hiHz, at], [spec.loHz, at + spec.toneSec]].forEach(([hz, when]) => {
      const out = envOut(c, when, spec.gain, 0.003, spec.toneSec);
      const o = c.createOscillator();
      o.type = 'square';
      o.frequency.value = hz;
      o.connect(out);
      o.start(when);
      o.stop(when + spec.toneSec + 0.02);
    });
  }

  /* ----- public API --------------------------------------------------------- */

  /** SUSPEND the binaural bed (jumpscare event): capture the current bed level
   *  and glide it to silence. Idempotent (a second call is a no-op). One-shot
   *  voices are unaffected — they route to the limiter, not through binGain. */
  function suspendBed() {
    if (bedSuspended) return;
    bedSuspended = true;
    if (!ctx || !started || !binGain) return;
    try {
      // If a voice line has ducked the bed, capture the UN-ducked level so resume
      // restores to full (the duck is subsumed by the suspend), not to the 60%.
      bedSavedGain = (voDucked && voBedSaved != null) ? voBedSaved : binGain.gain.value;
      voDucked = false; voBedSaved = null;
      glide(binGain.gain, 0, 0.15);
    } catch (_e) {}
  }

  /** RESUME the binaural bed: glide back to the captured level (or, if it was
   *  never captured, to the depth-derived level). Idempotent. */
  function resumeBed() {
    if (!bedSuspended) return;
    bedSuspended = false;
    if (!ctx || !started || !binGain) return;
    try {
      const fallback = _muted ? 0 : binauralGainFor(binauralChannel(depthNow, capsOf()));
      const target = (bedSavedGain != null) ? bedSavedGain : fallback;
      glide(binGain.gain, target, 0.3);
    } catch (_e) {}
    bedSavedGain = null;
  }

  /** GLITCH one-shot — the harsh burst for a caught "Yes". PREFERS the real
   *  glitch-burst sample once decoded; the synth stab burst is the fallback.
   *  Signature unchanged (beats.js jumpscare calls it as before). Respects mute. */
  function glitch() {
    if (_muted) return;
    const c = ensureCtx();
    if (!c) return;
    loadManifest();
    loadSfx(c, 'glitch-burst');
    if (sfxReady('glitch-burst')) { if (playSfx('glitch-burst')) return; }
    try { glitchBurst(c); } catch (_e) {}
  }

  /** ERROR SPAM — n overlapping "error ding" blips at ~90ms intervals (default
   *  8). PREFERS the real error-blip sample (staggered the same way) once
   *  decoded; the two-tone synth is the fallback. Signature unchanged. */
  function errorSpam(n) {
    if (_muted) return;
    const c = ensureCtx();
    if (!c) return;
    const count = Math.max(1, Math.min(24, (n | 0) || 8));
    const t0 = c.currentTime;
    loadManifest();
    loadSfx(c, 'error-blip');
    if (sfxReady('error-blip')) {
      try {
        const entry = sfxEntry('error-blip');
        const bufs = sfxBufs['error-blip'];
        const gain = sfxPlaybackGain(entry.gain, clampIntensity(1, capsOf()));
        for (let k = 0; k < count; k++) {
          const buf = bufs[(Math.random() * bufs.length) | 0];
          const src = c.createBufferSource(); src.buffer = buf;
          const g = c.createGain(); g.gain.value = gain;
          src.connect(g); routeOut(c, g);
          src.start(t0 + k * 0.09);
        }
        return;
      } catch (_e) {}
    }
    try { for (let k = 0; k < count; k++) errorBlip(c, t0 + k * 0.09); } catch (_e) {}
  }

  /** Drive the binaural from one depth scalar (invariant #2: via caps). */
  function setDepth(depth) {
    depthNow = clamp01(depth);
    const c = ensureGraph();
    if (!c || !started) return;
    loadChimes(c); // warm the real chimes at boot so the FIRST reward is real
    loadPop(c);    // warm the real pop so the FIRST bubble-pop is the real sample
    loadManifest();       // warm the sfx manifest (cheap; overrides inline table)
    loadVoManifest();     // warm the vo manifest (cheap; per-line buffers stay lazy)
    warmSfx('pressure-heartbeat'); // the urgent-timer loop must be ready on time
    const b = binauralChannel(depthNow, capsOf());
    const { beatHz, carrierHz } = binauralToHz(b);
    const gain = _muted ? 0 : binauralGainFor(b);
    glide(oscL.frequency, carrierHz, GLIDE_SEC);
    glide(oscR.frequency, carrierHz + beatHz, GLIDE_SEC);
    glide(binGain.gain, gain, GLIDE_SEC);
  }

  /** Reward one-shot: a distinct voice per RewardKind, scaled by clamped
   *  intensity (invariant #2). Streak raises pitch on the chime/bubble voices;
   *  jackpot plays the sting; a nearMiss (fire=false) plays a muted tick. */
  function chime(rewardEvent) {
    if (_muted || !rewardEvent) return;
    const intensity = clampIntensity(rewardEvent.intensity, capsOf());
    if (!rewardEvent.fire) {
      // fire=false is no longer a silent early-out: near-misses get their tick.
      if (rewardEvent.nearMiss && intensity > 0.0005) {
        const c = ensureCtx();
        if (c) { try { nearMissTick(c, intensity); } catch (_e) {} }
      }
      return;
    }
    if (intensity <= 0.0005) return;
    const c = ensureCtx();
    if (!c) return;
    try {
      if (rewardEvent.jackpot) {
        sting(c, intensity);
        return;
      }
      const pitchFactor = semitoneFactor(streakSemitones(rewardEvent.streak || 0));
      switch (rewardEvent.kind) {
        case RewardKind.Bubble: bubbleBlip(c, intensity, pitchFactor); break;
        case RewardKind.Flash:  flashSweep(c, intensity); break;
        case RewardKind.Praise: praiseSwell(c, intensity); break;
        case RewardKind.Drop:   dropBoom(c, intensity); break;
        case RewardKind.None:   break;
        case RewardKind.Chime:
        default:
          // the REAL CCP chime leads once decoded; synth bell until then/if dead
          loadChimes(c); // no-op after the first call
          if (chimeLoad === 'ready' && chimeBufs.length) mp3Chime(c, intensity, pitchFactor);
          else bellChime(c, intensity, pitchFactor);
          break;
      }
    } catch (_e) {}
  }

  /** Pop one-shot: the app's REAL Pop.mp3 (synth burst until it decodes / if it
   *  never does), scaled by clamped intensity (invariant #2), through the shared
   *  limiter. Additive surface — callers guard `typeof audio.pop === 'function'`.
   *  Default intensity when unspecified keeps a bubble-pop satisfyingly present.
   *  Optional `rate` (0.5..2, default 1) pitches the pop — smaller bubble, tighter
   *  pop (the BubblePop split chain steps it up one notch per generation). */
  function pop(intensity, rate) {
    if (_muted) return;
    const i = clampIntensity(intensity == null ? 0.6 : intensity, capsOf());
    const c = ensureCtx();
    if (!c) return;
    loadPop(c); // no-op after the first call
    try {
      if (popLoad === 'ready' && popBuf) popSample(c, i, rate);
      else popSynth(c, i, rate);
    } catch (_e) {}
  }

  /** Garnish one-shot: a small additive cue for the spiral / subliminal-flash
   *  garnishes. `rawIntensity` arrives UN-clamped from effects and is clamped
   *  HERE by our caps (invariant #2); unknown names are silently ignored. */
  function garnishCue(name, rawIntensity) {
    if (_muted) return;
    const intensity = clampIntensity(rawIntensity, capsOf());
    if (intensity <= 0.0005) return;
    const c = ensureCtx();
    if (!c) return;
    try {
      if (name === 'spiral') spiralShimmer(c, intensity);
      else if (name === 'sublim') subliminalTicks(c, intensity);
    } catch (_e) {}
  }

  // the loose seam: effects.js dispatches GARNISH_CUE_EVENT on window when a
  // sounded garnish shows (boot wiring unchanged). Registered in the factory —
  // never at import — and torn down in dispose().
  let garnishHook = null;
  if (typeof window !== 'undefined') {
    garnishHook = (ev) => {
      const d = ev && ev.detail;
      if (d && typeof d.name === 'string') garnishCue(d.name, d.intensity);
    };
    try { window.addEventListener(GARNISH_CUE_EVENT, garnishHook); } catch (_e) { garnishHook = null; }
  }

  // the generic sfx seam: effects.js / steering.js (no audio handle) dispatch
  // SFX_CUE_EVENT { id, intensity?, variant?, loop? }; we route it to playSfx.
  // (One-shots only over the wire — no handle is returned to the dispatcher.)
  let sfxHook = null;
  if (typeof window !== 'undefined') {
    sfxHook = (ev) => {
      const d = ev && ev.detail;
      if (d && typeof d.id === 'string') {
        try { playSfx(d.id, d.intensity, { variant: d.variant, loop: d.loop }); } catch (_e) {}
      }
    };
    try { window.addEventListener(SFX_CUE_EVENT, sfxHook); } catch (_e) { sfxHook = null; }
  }

  // LIVE PREF RETUNE: the Options panel can be opened from the pause menu mid-run,
  // so a slider must retune the bed/effects/VO that are ALREADY sounding. Always
  // GLIDE (setTargetAtTime) — stepping a gain node mid-signal clicks audibly.
  // Cheap enough to run on every notify; prefs.setPref already suppresses
  // no-change writes, so a dragged slider does not storm this.
  const unsubPrefs = subscribePrefs((p) => {
    try {
      if (binBus)   glide(binBus.gain,   binauralScale(p), 0.30);
      if (fxBus)    glide(fxBus.gain,    effectsScale(p),  0.15);
      if (voiceBus) glide(voiceBus.gain, voiceScale(p),    0.15);
      // Switching Voiceover OFF while a line is mid-sentence: fade it out for
      // real rather than leaving a muted source running to its natural end (the
      // stop() fade also unwinds the bed duck and fires the line's onEnd).
      if (voiceScale(p) <= 0 && voHandle) stopVoice(0.2);
    } catch (_e) {}
  });

  /** Invariant #3: emerge — glide beat back to 10 Hz, carrier down, gain out. */
  function emerge() {
    depthNow = 0;
    if (!ctx || !started) return;
    const { beatHz, carrierHz } = binauralToHz(0); // 10 Hz / 174 Hz
    glide(oscL.frequency, carrierHz, EMERGE_SEC);
    glide(oscR.frequency, carrierHz + beatHz, EMERGE_SEC);
    glide(binGain.gain, 0, EMERGE_SEC);
  }

  /* ----- "are you sure?" FREEZE GATE surfaces (beats.js) --------------------- */

  /** Resolve one VO line's manifest metadata (id/text/file) WITHOUT playing it.
   *  Used by the freeze gate to mirror the spoken taunt as a red subtitle. Resolves
   *  to { id, text, file } or null (missing id / no manifest). Never throws. */
  function voiceInfo(id) {
    if (typeof id !== 'string' || !id) return Promise.resolve(null);
    return loadVoManifest().then((man) => {
      const e = man && man[id];
      if (!e) return null;
      return { id, text: (typeof e.text === 'string' ? e.text : ''), file: (typeof e.file === 'string' ? e.file : '') };
    }).catch(() => null);
  }

  /** RUMBLE RISER — a synthesized low-rumble that ramps UP over `durSec` (default
   *  20s): a sub-sine + detuned triangle (38->~60 Hz) plus a lowpassed noise bed
   *  whose tremolo shudder deepens and quickens toward the end. Returns a handle
   *  { stop(fadeSec) }; stop(0) (the default) cuts it INSTANTLY. Routes through the
   *  shared limiter like every one-shot. Respects mute. Never throws. */
  function rumbleRiser(durSec) {
    if (_muted) return null;
    const c = ensureCtx();
    if (!c) return null;
    const dur = Math.max(1, (typeof durSec === 'number' && durSec > 0) ? durSec : 20);
    try {
      const t0 = c.currentTime;
      const master = c.createGain();
      master.gain.setValueAtTime(0.0001, t0);
      master.gain.linearRampToValueAtTime(0.05, t0 + 0.35);      // quick presence
      master.gain.linearRampToValueAtTime(0.5, t0 + dur);        // saturate over the window
      routeOut(c, master);

      const osc = c.createOscillator();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(38, t0);
      osc.frequency.linearRampToValueAtTime(58, t0 + dur);
      const oGain = c.createGain(); oGain.gain.value = 0.7;
      osc.connect(oGain); oGain.connect(master);

      const osc2 = c.createOscillator();
      osc2.type = 'triangle';
      osc2.frequency.setValueAtTime(41, t0);
      osc2.frequency.linearRampToValueAtTime(63, t0 + dur);
      const o2Gain = c.createGain(); o2Gain.gain.value = 0.32;
      osc2.connect(o2Gain); o2Gain.connect(master);

      // lowpassed noise bed with a growing tremolo shudder (the "trembling")
      const noise = c.createBufferSource();
      noise.buffer = ensureNoise(c); noise.loop = true;
      const lp = c.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 120; lp.Q.value = 0.7;
      const nGain = c.createGain(); nGain.gain.value = 0.24;
      const lfo = c.createOscillator(); lfo.type = 'sine';
      lfo.frequency.setValueAtTime(6, t0);
      lfo.frequency.linearRampToValueAtTime(18, t0 + dur);       // faster shudder later
      const lfoGain = c.createGain();
      lfoGain.gain.setValueAtTime(0.02, t0);
      lfoGain.gain.linearRampToValueAtTime(0.2, t0 + dur);       // deeper shudder later
      lfo.connect(lfoGain); lfoGain.connect(nGain.gain);
      noise.connect(lp); lp.connect(nGain); nGain.connect(master);

      osc.start(t0); osc2.start(t0); lfo.start(t0); noise.start(t0);

      let stopped = false;
      const stop = (fadeSec) => {
        if (stopped) return; stopped = true;
        try {
          const f = Math.max(0, fadeSec == null ? 0 : fadeSec);
          const tn = c.currentTime;
          master.gain.cancelScheduledValues(tn);
          master.gain.setValueAtTime(master.gain.value, tn);
          if (f <= 0.001) master.gain.setValueAtTime(0.0001, tn);         // INSTANT cut
          else master.gain.setTargetAtTime(0.0001, tn, Math.max(0.02, f / 3));
          const end = tn + (f <= 0.001 ? 0.03 : f + 0.05);
          try { osc.stop(end); } catch (_e) {}
          try { osc2.stop(end); } catch (_e) {}
          try { lfo.stop(end); } catch (_e) {}
          try { noise.stop(end); } catch (_e) {}
        } catch (_e) {
          try { osc.stop(); osc2.stop(); lfo.stop(); noise.stop(); } catch (_e2) {}
        }
      };
      return { stop };
    } catch (_e) { return null; }
  }

  /** Full teardown (optional; boot doesn't call it, but hosts may on unload). */
  function dispose() {
    try { if (oscL) oscL.stop(); } catch (_e) {}
    try { if (oscR) oscR.stop(); } catch (_e) {}
    if (resumeHook && typeof window !== 'undefined') {
      GESTURES.forEach((ev) => window.removeEventListener(ev, resumeHook));
      resumeHook = null;
    }
    if (garnishHook && typeof window !== 'undefined') {
      try { window.removeEventListener(GARNISH_CUE_EVENT, garnishHook); } catch (_e) {}
      garnishHook = null;
    }
    if (sfxHook && typeof window !== 'undefined') {
      try { window.removeEventListener(SFX_CUE_EVENT, sfxHook); } catch (_e) {}
      sfxHook = null;
    }
    try { if (typeof unsubPrefs === 'function') unsubPrefs(); } catch (_e) {}
    if (ctx) { try { ctx.close(); } catch (_e) {} }
    ctx = null; started = false; dead = false; noiseBuf = null;
    chimeBufs = []; chimeLoad = 'idle'; // re-fetch against a future fresh ctx
    popBuf = null; popLoad = 'idle';    // re-fetch against a future fresh ctx
    // re-fetch the sfx library against a future fresh ctx
    for (const k of Object.keys(sfxBufs)) delete sfxBufs[k];
    for (const k of Object.keys(sfxLoad)) delete sfxLoad[k];
    for (const k of Object.keys(sfxLastVariant)) delete sfxLastVariant[k];
    sfxManifestState = 'idle';
    bedSuspended = false; bedSavedGain = null;
    // re-fetch the vo library against a future fresh ctx
    try { if (voHandle && typeof voHandle.stop === 'function') voHandle.stop(0); } catch (_e) {}
    voHandle = null; voManifest = null; voManifestPromise = null;
    voCache.clear(); voLoading.clear();
    voDucked = false; voBedSaved = null; voToken++;
    oscL = oscR = gainL = gainR = merger = lowpass = binGain = limiter = null;
    binBus = fxBus = voiceBus = null;   // rebuilt (from live prefs) on a fresh ctx
  }

  return { setDepth, chime, garnishCue, pop, sfx: playSfx, emerge, dispose, setMuted, isMuted,
           suspendBed, resumeBed, glitch, errorSpam, voice: playVoice, voiceStop: stopVoice,
           voiceInfo, rumbleRiser };
}
