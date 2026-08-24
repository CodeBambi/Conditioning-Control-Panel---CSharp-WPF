/* ============================================================================
 * emi/vox.js - BLIPESE: the sound EMI makes while a line is on screen.
 *
 * She has no mouth and she never mouths words (the talk rule is locked in
 * chains.js), so the voice is not speech - it is a short seeded BABBLE that
 * plays under the landed bubble and stops when the bubble does. Think Banjo,
 * not Undertale: a warm little instrument with a cadence, never a typewriter.
 *
 *   const vox = createVox({ log });
 *   vox.tick();                        // one dot frame typed
 *   vox.speak('nice one!', { face: 'celebration' });
 *   vox.stop();                        // the bubble cleared - cut, no tail
 *
 * WHAT THIS FILE IS NOT: an audio node. CLAUDE.md trap 18 is absolute -
 * `shell/audio.js` is the only thing in the Arcademy that may hold one. So this
 * module is a pure EMITTER: it computes a SCORE (a list of {atMs, pitch, gain})
 * and fires one `arcademy-sfx` CustomEvent per blip off its own setTimeout
 * ladder, exactly the way `chains.js playChain` walks its frames. Everything
 * downstream - the mixer, the mute, the Voice slider, the ducking - is already
 * built and this file gets all of it for free by asking politely.
 *
 * WHY IT IS QUIET ON PURPOSE. The cue level is VOX_DIALS.LEVEL (0.4) on the
 * `voice` bus and the recipe's own gain is 0.35, which puts her clearly UNDER
 * every game one-shot (a stamp is level 1.0, a pop 0.8). A mascot MURMURS. She
 * also never sends `duck` - the existing DUCK_TARGETS already pull the voice bus
 * down under a whisper or a spotlight, which is the correct direction.
 *
 * DETERMINISM IS THE IDENTITY. The seed is the LINE TEXT (core/rng.js makeRng,
 * the only sanctioned randomness in this codebase - never Math.random), so a
 * line always sounds like itself: the same sentence twice in one night is the
 * same little melody, and two different sentences are two different ones. That
 * is what turns a bag of beeps into a voice you recognise.
 *
 * ONE VOICE. `speak()` cuts any babble still running. A report card and a stamp
 * landing back to back can never stack into two EMIs talking over each other.
 *
 * THE NODE DOM DOUBLE drives these modules in the suites and has no
 * `CustomEvent`, so every dispatch is wrapped: a cue must never be the thing
 * that throws (the precedent is shell/ceremonies.js `sfx()`).
 * ==========================================================================*/

import { makeRng } from '../core/rng.js';

/* ---------------------------------------------------------------------------
 * THE DIALS. One table, so a re-tune is a number here and not a read of the
 * machinery below. Same shape and same intent as voice.js VOICE_DIALS.
 * ------------------------------------------------------------------------- */
export const VOX_DIALS = Object.freeze({
  ENABLED: true,             // the whole-voice off switch (no settings row in v1)
  BUS: 'voice',              // the user-facing Voice slider already exists
  LEVEL: 0.4,                // cue level per blip - deliberately under the games
  TICK_LEVEL: 0.22,          // ...and the typing tick is quieter again

  /* --- rhythm (pace is identity: compression NEVER speeds these up) ------ */
  GAP_SYL_MS: 62,            // between two syllables of one word
  GAP_WORD_MS: 115,          // between two words
  GAP_SENT_MS: 160,          // ...added on top at a sentence boundary
  TAIL_REST_MS: 180,         // ...and again after an ellipsis (the trailing off)
  JITTER_GAP_MS: 10,         // +/- per gap, seeded - a human never lands on a grid
  MAX_BLIPS: 13,             // hard ceiling on the burst
  BURST_MAX_MS: 1400,        // ...and on its length. Always ends inside the 3s hold.

  /* --- prosody, in semitones -------------------------------------------- */
  /* THE TRANSPOSE (owner verdict, 2026-08-24: "too acute - transposed down").
   * She sat right on the recipe's own 760Hz and read as shrill over a long
   * night, so the whole voice drops 18% - about three and a half semitones. It
   * is a MULTIPLIER and nothing below it moved, so every mood travels with her:
   * celebration is still the brightest, sad still the lowest, and the distance
   * between any two is what it always was. A transpose, not a re-voicing.
   * Measured over 200k blips per mood afterwards, the lowest `sad` blip lands
   * at 0.566 and the highest `celebration` one at 1.27, so neither end of the
   * clamp window below is ever reached and no mood is flattened against it. */
  BASE_PITCH: 0.82,
  JITTER_SEMI: 1.2,          // +/- per blip (scaled by the mood's own jitter)
  DECLINE_SEMI: 2.0,         // a sentence starts +1 and drifts down this far
  QUESTION_RISE: 3.0,        // '?' lifts the last blips +1..+RISE, stepped and CLEAN
  QUESTION_TAIL: 3,          // ...how many blips the lift owns
  BANG_SEMI: 1.0,            // '!' raises the whole utterance
  BANG_GAIN: 1.25,           // ...and leans on it
  BANG_GAP: 0.85,            // ...and hurries it slightly
  SAD_TAIL_SEMI: -2.0,       // an ellipsis sags before the rest
  TAIL_GAIN: 0.8,            // ...and softens

  /* --- the typing tick --------------------------------------------------- */
  DOT_TICKS: true,           // ON by default (owner: the anticipation is the point)

  /* --- the moods, keyed by BODY FRAME family (widget.js frameForFace) ----
   * `decline` is in semitones per sentence and a NEGATIVE one rises; `tail` is
   * added to the very last blip of the utterance; `jitter` scales JITTER_SEMI. */
  MOODS: Object.freeze({
    idle:        Object.freeze({ pitch: 1.00, gap: 1.00, gain: 1.00, jitter: 1.0, decline:  2.0, tail:  0.0 }),
    celebration: Object.freeze({ pitch: 1.15, gap: 0.85, gain: 1.15, jitter: 1.4, decline: -1.5, tail:  0.0 }),
    pet:         Object.freeze({ pitch: 1.05, gap: 1.10, gain: 0.80, jitter: 0.7, decline:  1.4, tail:  0.0 }),
    smug:        Object.freeze({ pitch: 0.95, gap: 1.25, gain: 0.90, jitter: 0.9, decline:  2.2, tail: -1.5 }),
    sad:         Object.freeze({ pitch: 0.85, gap: 1.30, gain: 0.75, jitter: 0.5, decline:  0.6, tail: -2.0 }),
    shock:       Object.freeze({ pitch: 1.20, gap: 0.60, gain: 1.10, jitter: 1.6, decline:  2.0, tail:  0.0 }),
  }),
});

/** audio.js clamps a cue's pitch to this window; we never send one outside it.
 *  The floor stayed at 0.5 through the 2026-08-24 transpose on purpose:
 *  `shell/audio.js clampPitch` holds the same two numbers and re-clamps every
 *  cue, so widening ours would move where a clip happened rather than whether
 *  it did - and nothing clips. The sad and smug sags keep their full depth. */
const PITCH_MIN = 0.5;
const PITCH_MAX = 2;

const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
const semiToRatio = (s) => Math.pow(2, s / 12);

/**
 * SYLLABLES, cheaply. Vowel GROUPS, clamped 1..4 - a word always gets at least
 * one blip and no word is ever allowed to become a solo. This is not linguistics
 * and does not need to be: the ear reads it as pace, not as pronunciation.
 */
export function syllables(word) {
  const w = String(word == null ? '' : word).toLowerCase().replace(/[^a-z]+/g, '');
  if (!w) return 1;
  const m = w.match(/[aeiouy]+/g);
  return clamp(m ? m.length : 1, 1, 4);
}

/** Trailing punctuation -> the flags that shape a sentence. Never throws. */
function tokenize(text) {
  const words = [];
  const raw = String(text == null ? '' : text).trim();
  if (!raw) return words;
  for (const chunk of raw.split(/\s+/)) {
    const punct = (chunk.match(/[.,!?;:…)"'\]]+$/) || [''])[0];
    const body = chunk.slice(0, chunk.length - punct.length) || chunk;
    const ellipsis = /\.\.\.|…/.test(punct);
    const question = punct.indexOf('?') >= 0;
    const bang = punct.indexOf('!') >= 0;
    words.push({
      syl: syllables(body),
      ends: ellipsis || question || bang || punct.indexOf('.') >= 0,
      ellipsis, question, bang,
    });
  }
  // A line with no terminator still ENDS - the last word closes its sentence.
  if (words.length) words[words.length - 1].ends = true;
  return words;
}

/**
 * COMPRESSION, when a line is longer than the burst may be. Two rules, in
 * order, and neither of them touches the gaps: the PACE is the identity and a
 * sped-up EMI is a different character.
 *   1. drop a word-INTERNAL syllable from the longest word (the first syllable
 *      of every word survives, so the word count - the rhythm you hear - holds)
 *   2. only then drop a whole word, from the MIDDLE out, never the first and
 *      never the last (the opening and the cadence are what carry the mood)
 * @returns {boolean} false when there is nothing left to give up
 */
function dropOne(words) {
  let best = -1;
  for (let i = 0; i < words.length; i++) {
    if (words[i].syl > 1 && (best < 0 || words[i].syl > words[best].syl)) best = i;
  }
  if (best >= 0) { words[best].syl -= 1; return true; }
  if (words.length <= 2) return false;
  const cut = Math.floor(words.length / 2);
  const gone = words.splice(cut, 1)[0];
  // The removed word may have been carrying a sentence end. Hand it backwards
  // rather than losing the rest - a dropped word must not merge two sentences.
  if (gone.ends) {
    const prev = words[cut - 1];
    prev.ends = true;
    prev.ellipsis = prev.ellipsis || gone.ellipsis;
    prev.question = prev.question || gone.question;
    prev.bang = prev.bang || gone.bang;
  }
  return true;
}

/** One pass: words (already compressed) -> the score. Fresh rng every pass. */
function layout(words, M, bang, D, seed) {
  const rng = makeRng('emi-vox|' + seed);
  const gapMul = M.gap * (bang ? D.BANG_GAP : 1);
  const jgap = (ms) => Math.max(16, ms * gapMul + (rng() * 2 - 1) * D.JITTER_GAP_MS);

  /* Sentence ids, and how many blips each sentence holds - the declination
   * curve needs to know where it is inside its own sentence. */
  let s = 0;
  const sentOf = [];
  const counts = [];
  for (const w of words) {
    sentOf.push(s);
    counts[s] = (counts[s] || 0) + w.syl;
    if (w.ends) s += 1;
  }

  const blips = [];
  let t = 0;
  const seen = [];
  for (let i = 0; i < words.length; i++) {
    const w = words[i];
    if (i > 0) {
      const prev = words[i - 1];
      let g = D.GAP_WORD_MS;
      if (prev.ends) g += D.GAP_SENT_MS;
      if (prev.ellipsis) g += D.TAIL_REST_MS;   // ...trailing off is a REST, not a rush
      t += jgap(g);
    }
    for (let k = 0; k < w.syl; k++) {
      if (k > 0) t += jgap(D.GAP_SYL_MS);
      const si = sentOf[i];
      const j = seen[si] || 0;
      seen[si] = j + 1;
      blips.push({
        atMs: Math.round(t),
        s: si, j, n: counts[si],
        lastOfWord: k === w.syl - 1,
        ellipsis: w.ellipsis,
        question: w.question,
      });
    }
  }
  if (!blips.length) return blips;

  /* Does this sentence ask a question? The flag lives on its LAST word. */
  const asks = [];
  for (const b of blips) if (b.question) asks[b.s] = true;

  const last = blips.length - 1;
  for (let i = 0; i <= last; i++) {
    const b = blips[i];
    const frac = b.n > 1 ? b.j / (b.n - 1) : 0;
    let semi = 1 - M.decline * frac;
    let jitter = true;
    let lifted = false;

    /* THE LIFT IS A GESTURE, SO IT IS CLEAN. A question's last blips step up
     * +1..+QUESTION_RISE off a flat base and take NO jitter - a smeared lift
     * reads as a wrong note, not as a question. It is also what makes the rise
     * provable in the harness. */
    if (asks[b.s]) {
      const k = Math.min(D.QUESTION_TAIL, b.n);
      const m = b.j - (b.n - k);
      if (m >= 0) { semi = 1 + (D.QUESTION_RISE * (m + 1)) / k; jitter = false; lifted = true; }
    }
    if (bang) semi += D.BANG_SEMI;
    if (b.ellipsis && b.lastOfWord) { semi += D.SAD_TAIL_SEMI; jitter = false; }
    /* A MOOD'S SAG NEVER LANDS ON A QUESTION. `smug` and `sad` drop the last
     * blip of an utterance, which is the drawl - but a line that ENDS on a
     * question ends UP, and the two cancelling out is heard as a wrong note
     * rather than as either feeling. The lift wins; it is the louder gesture. */
    if (i === last && !lifted) semi += M.tail;
    if (jitter) semi += (rng() * 2 - 1) * D.JITTER_SEMI * M.jitter;

    let gain = D.LEVEL * M.gain * (bang ? D.BANG_GAIN : 1);
    if ((b.ellipsis && b.lastOfWord) || (i === last && !lifted && M.tail < 0)) gain *= D.TAIL_GAIN;

    b.pitch = clamp(D.BASE_PITCH * M.pitch * semiToRatio(semi), PITCH_MIN, PITCH_MAX);
    b.gain = clamp(gain, 0.02, 1);
    delete b.s; delete b.j; delete b.n;
    delete b.lastOfWord; delete b.ellipsis; delete b.question;
  }
  return blips;
}

/**
 * makeScore(text, mood, dials?) -> [{atMs, pitch, gain}, ...]
 *
 * PURE and DETERMINISTIC: same text + same mood always returns the same score,
 * byte for byte. `gain` is the cue's `level` field (audio.js applies the sqrt
 * curve to it); `atMs` is an offset from the start of the burst.
 *
 * `mood` is a BODY FRAME FAMILY - one of VOX_DIALS.MOODS' keys, which is
 * exactly what `widget.js` already resolved for the reaction face. Anything
 * else is `idle`, deliberately: a face nobody paired is a face she has no
 * strong feeling about (the same rule FACE_BODY_FRAME follows).
 */
export function makeScore(text, mood, dials) {
  const D = (dials && typeof dials === 'object') ? Object.assign({}, VOX_DIALS, dials) : VOX_DIALS;
  const moods = D.MOODS || VOX_DIALS.MOODS;
  const M = (typeof mood === 'string' && moods[mood]) ? moods[mood] : moods.idle;
  const seed = String(text == null ? '' : text);

  const words = tokenize(seed);
  if (!words.length) return [];
  const bang = words.some((w) => w.bang);

  /* FIT THE BURST. Compress until it is inside BOTH ceilings - never by moving
   * the clock, always by giving up a syllable. The guard is belt and braces:
   * dropOne() bottoms out at two words and the loop returns what it has. */
  let score = layout(words, M, bang, D, seed);
  for (let guard = 0; guard < 240; guard++) {
    const dur = score.length ? score[score.length - 1].atMs : 0;
    if (score.length <= D.MAX_BLIPS && dur <= D.BURST_MAX_MS) break;
    if (!dropOne(words)) { score = score.slice(0, D.MAX_BLIPS); break; }
    score = layout(words, M, bang, D, seed);
  }
  return score;
}

/**
 * createVox({ dials, log }) -> { speak, tick, stop, destroy }
 * Owns nothing but timers. Every entry point is safe to call on a platform with
 * no document, no CustomEvent and no audio at all.
 */
export function createVox({ dials, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const D = Object.assign({}, VOX_DIALS, (dials && typeof dials === 'object') ? dials : {});
  const timers = new Set();
  let dead = false;
  const stats = { spoke: 0, blips: 0, ticks: 0, cut: 0 };

  /** The ONE way a sound leaves this file. A cue may never be what throws. */
  function cue(name, level, pitch) {
    try {
      if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
      const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
      if (!Ctor) return;
      document.dispatchEvent(new Ctor('arcademy-sfx', {
        detail: { name, level, bus: D.BUS, pitch },   // NEVER a duck: see the header
      }));
    } catch (e) { /* a cue must never be the thing that throws */ }
  }

  function stop() {
    if (timers.size) stats.cut += 1;
    for (const id of timers) { try { clearTimeout(id); } catch (e) { /* noop */ } }
    timers.clear();
  }

  return {
    /** The dials this instance actually runs on (test/debug seam). */
    dials: D,

    /**
     * ONE typing tick, for a `.`/`..`/`...` bubble frame. Very quiet and behind
     * DOT_TICKS - this is the annoyance risk in the whole feature, so it is the
     * one thing with its own off switch.
     */
    tick() {
      if (dead || !D.ENABLED || !D.DOT_TICKS) return false;
      stats.ticks += 1;
      cue('emi_tick', D.TICK_LEVEL, 1);
      return true;
    },

    /**
     * Babble one landed line. Fires the first blip SYNCHRONOUSLY so the sound
     * lands on the same frame as the bubble (house doctrine: <=50ms), then
     * walks the rest on setTimeout.
     * @param {string} text
     * @param {{face?:string}=} opts  `face` is a body-frame family (widget.bodyFrame)
     */
    speak(text, opts) {
      if (dead || !D.ENABLED) return false;
      const line = String(text == null ? '' : text);
      if (!line.trim()) return false;
      stop();                                   // ONE VOICE: never two at once
      const score = makeScore(line, (opts && opts.face) || 'idle', D);
      if (!score.length) return false;
      stats.spoke += 1;
      stats.blips += score.length;
      for (const b of score) {
        if (b.atMs <= 0) { cue('emi_blip', b.gain, b.pitch); continue; }
        const id = setTimeout(() => { timers.delete(id); cue('emi_blip', b.gain, b.pitch); }, b.atMs);
        timers.add(id);
      }
      return true;
    },

    /** Cut. The worst tail is the one blip already in flight (<=60ms). */
    stop,

    /** Read-only, for the suite and a later Records Office beat. */
    stats() { return Object.assign({}, stats, { pending: timers.size }); },

    destroy() {
      stop();
      dead = true;
      say('emi: vox down');
    },
  };
}

export default createVox;
