/* ============================================================================
 * games/echo/index.js - ECHO (Simon; family: memory; Semester II, 120s).
 *
 * Six pads in a ring. The room plays a sequence - each pad lights and sounds
 * its own note - and you play it back. Clear it and the sequence grows by one.
 * Miss and the room simply deals another one: ONE FAIL IS NOT THE CLASS. The
 * class ends on the bell, and it is graded on the longest echo you held, how
 * clean you were, how well you kept tempo, and how many lies you refused.
 *
 * WHOSE TURN IT IS (the owner's verdict, 2026-08-23 - "it's not clear when it
 * is our turn"). Two phases, never implicit:
 *   LISTEN    the room plays. The ring is LOCKED and dimmed, every pad is
 *             desaturated, the cursor says not-allowed, and the banner reads
 *             ec_phase_listen. The step strip fills as each note lands.
 *   YOUR TURN the hand-off is a whole BEAT of its own: the ring sweeps bright,
 *             every pad pulses once, one high chime that is not a pad tone, the
 *             banner flips to ec_phase_yours. Then the strip empties and fills
 *             again under your fingers.
 * The banner (.g-ec-phase) and the strip (.g-ec-steps) are TEXT AND ATTRIBUTES,
 * so both still say everything under reduced motion / motionLevel 0. The tier 3+
 * window ring stays, but it is no longer the only "your turn" tell.
 *
 * RIGHT AND WRONG (the same verdict - "when we get it wrong or right"). A press
 * answers itself three ways at once: the pad's own light, its note, and its dot.
 * A wrong press flashes the pad RED and shakes its face, buzzes, then HOLDS the
 * pad you needed lit for REVEAL_MS wearing a "this one" halo, and the shell
 * stamps MISS over the ring. A cleared round sweeps the whole ring.
 *
 * THE PADS ARE THE BUBBLES (the same verdict again - "the gifs in a bubble are
 * just bad, we should have bigger bubbles and have triggers associated with
 * them"). Each pad is bound, seeded, to ONE trigger from the player's own
 * active pool: the PHRASE is the face, big enough to read across the room, and
 * the trigger's own whisper clip plays FAINTLY UNDER the pad's note when that
 * pad sounds (init.triggers carries {text, audio}; the note still carries the
 * pitch ratchet, the clip never outruns it, and ctx.audioAudible === false
 * means no clip at all). Media faces are still plumbed, behind
 * ec_pad_words = 'media'.
 *
 * ROUND 2 (owner play-test, 2026-08-23) - three changes to that, and one fix:
 *   THE VEIL     a pad's phrase is FROSTED until you look at it. Hover (or
 *                press, on a touch screen, or focus, on a keyboard) clears the
 *                frost. During LISTEN the pad that is PLAYING clears for its
 *                own beat and frosts again after - the reveal IS the tell, and
 *                it is how you learn which phrase lives on which colour. The
 *                glyph is never veiled: the truth node stays readable.
 *                The frost is `color:transparent` + a text-shadow of the
 *                letterforms, NOT `filter: blur()` - no render surface, no
 *                per-frame re-raster, nothing for trap 36 to charge us for.
 *   THE RESHUFFLE the COLOURS and the HITBOXES never move (Law II), but the
 *                phrase inside a pad is re-dealt every round. The deal walks a
 *                seeded permutation of the whole pool with a cursor, so a pool
 *                larger than six is seen ENTIRELY before anything repeats, and
 *                a retake replays the identical run of deals (Law V).
 *   THE 25%      a whisper rides only one hit in four (CLIP_CHANCE), seeded per
 *                beat, and never two at once.
 *   THE FIT      long phrases are no longer sliced. One size is fitted for the
 *                WHOLE ring (so the trickster's word-swap can never overflow
 *                either) by a binary search over what actually fits the glass.
 *
 * THE ROUND (pinned by the Semester II/III contract):
 *   deal      a sequence of `len` from this attempt's seeded stream
 *   echo      the ring plays it: pad lights + ONE engine audio_trigger recipe
 *             at the pad's own pitch (+ the pad's trigger clip, faint, under
 *             it); a DECOY may light at a seam (tier 2+)
 *   seam      the interference beat - engine.beat(), sub_flash word blips,
 *             the input pressure comes up (tier 2+)
 *   input     tap or the declared pad1..pad6 verbs; every clean press ratchets
 *             the pitch +1 semitone (cap 7) so a good run becomes a melody
 *   clear     len + 1, a reward roll, the streak meter
 *   fail      the correct pad shows itself, then EITHER the ENCORE (once per
 *             class: the SAME sequence again at half tempo) or a new sequence
 *             at max(startLen, len - 2)
 *   bell      the real budget. Dim out, end card, endClass exactly once.
 *
 * THE WARM START (the critic's top fix, contract ruling 5). Simon's structural
 * flaw is that every class restarts at 3. Echo opens at
 * clamp(3 + floor(gameMeta.bestLen / 4), 3, 6) + a per-tier bonus, so a
 * veteran never plays the trivial opening and a first class still opens at 3.
 *
 * LAWS THIS FILE KEEPS:
 *   I    the ledger is honest - length, presses, latency, decoys and the clock
 *        are computed HERE and never routed through a deck. The trickster may
 *        lie on a chip FACE or a pad's WORD; the pad's GLYPH and its data-pad
 *        are truth and are repainted. The step strip is the ledger drawn: a dot
 *        is filled by a real press, never by a deck.
 *   II   a pad's hitbox never moves and the key under the finger never moves;
 *        every engine one-shot over the ring is decoration (fireSafe() welds
 *        clickSafe on, bubbles are never poppable here).
 *   III  something always breathes: the ambient field runs from the first beat.
 *   IV   the class-rules sheet is DRAWN, GO-only and FREE OF THE CLOCK: it
 *        shows ONCE per grade tier (gameMeta.howtoTiers) and ctx.hideTutorial
 *        skips even that first showing. startClock() runs in the GO callback.
 *   V    stream, decoy plan, the per-round face deal, the clip roll, casino,
 *        trickster and pressure are all scoped off the class seed; a retake
 *        replays the identical class. Nothing here calls Math.random.
 *   VI   pause / resume / suspend / destroy: the timer registry defers, the
 *        decks ride it, no timer survives destroy, every listener is removed.
 *   VII  every visible string is ctx.lexicon(key, fallback) over lex.js EC_LEX.
 *
 * ctx.audioAudible === false is a FIRST-CLASS MODE here, not an afterthought:
 * Echo is the one class that can be played by ear, so when the ear is gone the
 * LIGHT has to carry the whole signal - pads light SILENT_LIT_MULT longer, the
 * stage wears data-audible="0" for the creative's stronger tell, and the
 * proctor says so once.
 *
 * WHAT THIS FILE DOES NOT OWN: grades (core/grades.js via ctx.endClass), XP
 * (C#), the tier (registry + meta), effect strengths (the engine's ceiling
 * rule), the LOOK (style.js), the lighting (casino.js), the lies
 * (trickster.js) and the CCP-effects ladder (pressure.js). The pure model is
 * sequence.js and the composite grade.js.
 *
 * ENGINE TARGETING NOTE: glitch_swap adds `.ae-glitch{position:relative;
 * animation}` to its targets, so it may never touch a `.g-ec-pad` (style.js
 * owns the pad's transform and the ring's geometry). The decoy shudder targets
 * the pad's FACE nodes (`.g-ec-face` children), never the pad itself.
 * ==========================================================================*/

import { injectEchoStyle } from './style.js';
import { createEcCasino } from './casino.js';
import { createEcTrickster } from './trickster.js';
import { createEcPressure } from './pressure.js';
import { EC_LEX } from './lex.js';
import {
  PLAYTEST, alphabetFor, warmStartLen, nextLenAfterFail, stepMsFor, litMsFor,
  audioCeilFor, pitchFor, ratchetAfterMiss, isNearMiss, heatFor, buildRound,
  fitFontPx,
} from './sequence.js';
import { compositeFor, hardGates, flavorXp } from './grade.js';
import { makeRng, makeTaggedRoll, shuffled } from '../../core/rng.js';

const GAME_KEY = 'echo';

/** A url an <img> cannot show (a webm/mp4 loop). Mirrors engine/util.js
 *  VIDEO_URL_RE; games never import the engine, so the rule is repeated. */
const VIDEO_URL_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;

/** The truthful pad glyphs (Law IV: the glyph never lies, the word may).
 *  Six shapes that read at a glance and carry no meaning of their own. */
const GLYPHS = Object.freeze(['◆', '●', '▲', '■', '★', '✦']);

/** The pad tone. ONE recipe, six pitches (sequence.js PAD_SEMIS).
 *  `sting` is shell/audio.js's triangle 440->520 / 140ms - musical enough to
 *  ratchet. If the orchestrator lands the requested dedicated `pad` recipe,
 *  this ONE line adopts it (an unknown name degrades to `blip`, never to
 *  silence - web CLAUDE.md trap 18). */
const PAD_SFX = 'pad';
/** The decoy's own cue at the TELEGRAPHED tiers only. At tier 3+ a decoy
 *  sounds exactly like a real step - that is the whole point of the trap. */
const DECOY_SFX = 'glitch';
const FAIL_SFX = 'bump';
const CLEAR_SFX = 'streak';
/** THE HAND-OFF. A high clean bell that is deliberately NOT a pad tone: the one
 *  sound in the room that only ever means "now it is you". */
const HANDOFF_SFX = 'chime';
const HANDOFF_PITCH = 1.5;
/** The soft tick under a correct press (the note carries the melody, this
 *  carries the "yes"). */
const TICK_SFX = 'tell';
/** The fallback recipe behind a trigger CLIP: a host that cannot play the url
 *  whispers instead of going silent (shell/audio.js falls through on its own). */
const CLIP_SFX = 'whisper';

/**
 * THE CLIP CHANCE (owner, round 2: "we only got about 25% chance of the trigger
 * playing"). One hit in four carries its trigger's whisper; the other three are
 * just the note. Two rules keep it honest:
 *   - the roll is SEEDED and ALWAYS CONSUMED, even when the result is going to
 *     be dropped, so the run of clips is a function of the seed and not of the
 *     wall clock - a retake replays the same whispers in the same places;
 *   - a roll that LANDS while another clip is still in the air is dropped
 *     rather than layered (it counts as one of the three-in-four), because two
 *     overlapping whispers read as noise, not as a trigger.
 */
const CLIP_CHANCE = 0.25;

/** The pad-face setting. `words` = the player's own triggers (the default and
 *  the point); `glyphs` = the truthful marks only; `media` = the old pool-media
 *  faces, kept plumbed but no longer the look anyone gets by accident. */
const FACE_MODES = Object.freeze(['words', 'glyphs', 'media']);

/** How often the honest window ring repaints (the trickster polls it). */
const WINDOW_TICK_MS = 60;

/* Diagnostics seams (the DV / DE precedent): the live class, the last report,
 * the final snapshot. The shell never reads these; the harness does. */
let liveClass = null;
let lastReport = null;
let lastSnapshot = null;

/** Test seam: the scratch harness compresses the clock. Production = 1. */
let timeScale = 1;
export function setTimeScale(f) { const v = Number(f); timeScale = Number.isFinite(v) && v > 0 ? v : 1; }
export function getTimeScale() { return timeScale; }
function scaled(ms) { return Math.max(0, Math.round(ms * timeScale)); }

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** Reduced motion from the shell's projection first, then the two probes. */
function probeReduced(ctx) {
  try { if (ctx && ctx.motion && ctx.motion.reducedMotion) return true; } catch (e) { /* ignore */ }
  try {
    if (typeof document !== 'undefined' && document.documentElement
      && document.documentElement.classList
      && document.documentElement.classList.contains('arc-reduced')) return true;
  } catch (e) { /* ignore */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* ignore */ }
  return false;
}

function mmss(secLeft) {
  const s = Math.max(0, secLeft | 0);
  return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
}

/** A pool phrase, normalised for a pad face. Never a lexicon row.
 *  ROUND 2: the cap is NO LONGER HOW A PHRASE IS MADE TO FIT. Slicing at 20 is
 *  exactly what produced the owner's "I CANT RESIST MY TRI - then was cut";
 *  the glass now SHRINKS THE TYPE to hold the whole phrase (fitWords), and this
 *  is only a sanity bound against a pathological pool entry. At the fit floor a
 *  40-character phrase still lands inside three lines of the smallest pad. */
const PAD_WORD_MAX = 40;
function padWordOf(raw) {
  const s = String(raw == null ? '' : raw).trim().replace(/\s+/g, ' ');
  if (!s) return '';
  return s.length > PAD_WORD_MAX ? s.slice(0, PAD_WORD_MAX).trim() : s;
}

export default {
  key: GAME_KEY,
  family: 'memory',
  meaty: false,
  flagship: false,
  /* 120s (owner ruling, the class-length wave; it was 105). Nothing in the
   * class is a count - the room deals sequences until the bell and a fail is
   * not the class - so the extra fifteen seconds are simply more rounds. */
  timeBudgetSec: 120,
  orientation: 'portrait',   // phone only; see games/registry.js ORIENTATIONS
  title: 'Echo',

  manifest: {
    /* audio_trigger is the mechanic here, not a garnish: the pad tones ARE the
     * second half of the signal. Everything else is the tier's dials. */
    effectsConsumed: [
      'audio_trigger', 'wash', 'sub_flash', 'glitch_swap',
      'flash_burst', 'ambient_field', 'bubble_field', 'gif_burst',
    ],
    /* Six loops (one pad face each) + six stills; pads are DOM and nothing is
     * drawn into a canvas, so the provider may serve remote media here. */
    assetNeeds: { loops: 6, targets: 0, stills: 6, canvasSafe: false },
    boardSizes: null,
    /* SYNTHESIS #7: the game DECLARES the verb slots, the shell owns the UI,
     * the storage and the panic-key conflict check. ctx.keys.on('pad3', fn). */
    keybinds: [
      { verb: 'pad1', label_key: 'ec_key_pad1', default: '1' },
      { verb: 'pad2', label_key: 'ec_key_pad2', default: '2' },
      { verb: 'pad3', label_key: 'ec_key_pad3', default: '3' },
      { verb: 'pad4', label_key: 'ec_key_pad4', default: '4' },
      { verb: 'pad5', label_key: 'ec_key_pad5', default: '5' },
      { verb: 'pad6', label_key: 'ec_key_pad6', default: '6' },
    ],
    settings: [
      {
        key: 'ec_pad_words', kind: 'enum', values: FACE_MODES.slice(), default: 'words',
        label_key: 'ec_pad_words', hint_key: 'ec_pad_words_hint',
      },
    ],
    peek: false,
  },

  create(ctx) {
    const t = (key, fallback) => {
      const fb = fallback == null ? (EC_LEX[key] == null ? key : EC_LEX[key]) : fallback;
      try { const v = ctx.lexicon(key, fb); return v == null ? fb : v; } catch (e) { return fb; }
    };
    const say = (m) => { try { ctx.log('[ec] ' + m); } catch (e) { /* noop */ } };

    /* ---- lifecycle flags ------------------------------------------------ */
    let dead = false;
    let paused = false;
    let ended = false;
    let reported = false;
    let busy = true;                    // input closed (briefing / playback / holds)

    /* ---- class state ---------------------------------------------------- */
    let spec = null;
    let seed = '';
    let tier = 1;
    let reduced = false;
    let retake = false;
    let audible = true;
    let budgetMs = 120000;
    let pool = null;
    let rollLocal = null;
    let subRoll = null;                 // the seeded sub_flash word stream

    let casino = null;
    let trickster = null;
    let pressure = null;

    /* ---- the round ------------------------------------------------------ */
    let attempt = 0;                    // the seeded stream index
    let startLen = 3;
    let curLen = 3;
    let round = null;                   // sequence.js buildRound() output
    let stepIdx = 0;                    // playback cursor
    let expectIdx = 0;                  // input cursor
    let inputOpen = false;
    let encoreArmed = true;             // once per class
    let encoreUsed = false;
    let inEncore = false;
    let ratchet = 0;

    /* ---- the ledger (Law I) --------------------------------------------- */
    let bestLen = 0;                    // longest CLEARED echo this class
    let bestReach = 0;                  // deepest correct index in any sequence
    let lifetimeBefore = 0;
    let sequencesDealt = 0;
    let sequencesCleared = 0;
    let fails = 0;
    let timeouts = 0;
    let presses = 0;
    let correctPresses = 0;
    let pressStreak = 0;                // clean presses in a row (the ratchet)
    let bestStreak = 0;
    let clearStreak = 0;
    let decoysPresented = 0;
    let decoysResisted = 0;
    let decoysTaken = 0;
    let latencySum = 0;
    let latencyCount = 0;
    let jackpots = 0;
    let nearMisses = 0;
    let subFlashes = 0;
    let currentHeat = 0;
    let bellOn = false;
    let stallMs = 0;
    let lastPressAt = 0;
    let newRecord = false;

    /* ---- pads / faces --------------------------------------------------- */
    let faceMode = 'words';             // ec_pad_words, after the empty-pool rule
    const padWords = [];                // index -> the word a pad wears (truth)
    /** index -> {text, audio} | null. The pad's BOUND TRIGGER, RE-DEALT EVERY
     *  ROUND off the class seed (Law V), never repeated across two pads.
     *  `audio` is the phrase's own whisper clip or null. */
    const padTriggers = [];
    /* THE DEAL. `facePool` is the class's whole trigger pool; `dealOrder` is a
     * seeded permutation of it and `dealCursor` walks that permutation, so a
     * pool larger than the ring is seen ENTIRELY before anything comes back.
     * `dealCycle` seeds each fresh permutation, which is what keeps the whole
     * run deterministic across a retake. */
    let facePool = [];
    let dealOrder = [];
    let dealCursor = 0;
    let dealCycle = 0;
    let roundIdx = 0;                   // which face deal the ring is wearing
    /* THE FIT: one size for the WHOLE ring (see fitWords). */
    let fittedPx = 0;
    let rulerEl = null;                 // the hidden measuring span (see ruler())
    let tokenW100 = 0;                  // widest dealt word at 100px type
    let fitPending = 0;
    let onResize = null;
    /* THE CLIP ROLL (CLIP_CHANCE). `clipUntil` is when the airwave frees up. */
    let clipRoll = null;
    let clipBeat = 0;
    let clipUntil = 0;
    let clipsFired = 0;                 // diagnostics: how many clips we asked for
    let clipsRolledOff = 0;             // the three-in-four the roll declined
    let clipsSkipped = 0;               // landed, but another whisper was in the air
    const faceUrls = new Map();         // pad -> {url, kind} | {broken:true}
    let faceKind = 'loop';
    let faceLogged = false;

    /* ---- dom ------------------------------------------------------------ */
    let stage = null; let backdrop = null; let hud = null; let ring = null;
    let msgEl = null; let well = null; let endEl = null; let howtoEl = null;
    let lenChip = null; let clockChip = null; let streakChip = null; let bestChip = null;
    /* THE TURN, drawn twice over so neither the ear nor a still frame can miss
     * it: the BANNER says whose turn it is in words, the STRIP says how far
     * through the sequence we are in dots. Both are attribute+text only, so
     * motionLevel 0 loses nothing. */
    let bannerEl = null; let bannerTextEl = null;
    let stepsEl = null; let stampWell = null;
    const stepEls = [];
    const stepFill = [];                // 'off' | 'on' | 'bad', one per sequence step
    /* THE WINDOW RING: the honest face of the soft input timer (tier 3+ only).
     * CORE writes `--ec-k` 1 -> 0 on it and NOTHING else; the trickster's
     * Crooked Clock paints a BENT twin over it and the real one underneath is
     * never touched (Law I). At the untimed tiers the node simply never
     * appears and the card folds itself. */
    let timerEl = null;
    const padEls = [];
    const padTimers = new Map();        // pad -> the timer that un-lights it

    /* ---- clock ---------------------------------------------------------- */
    let clockId = 0;
    let lastTick = 0;
    let elapsedMs = 0;

    /* ---- ambience ------------------------------------------------------- */
    let washOn = false;
    let bubblesOn = false;
    let ambientOn = false;

    /* ==================================================================== *
     * TIMERS - every step goes through run() so a suspend freezes the class
     * mid-playback and a resume finishes it. `every` simply skips while paused.
     * (The Deep End's registry, verbatim in shape - the decks ride it too.)
     * ==================================================================== */
    const timers = new Map();
    let nextTimerId = 1;
    const deferred = [];
    function run(fn) {
      if (dead) return;
      if (paused) { deferred.push(fn); return; }
      try { fn(); } catch (e) { say('step failed: ' + ((e && e.message) || e)); }
    }
    function after(ms, fn) {
      const id = nextTimerId++;
      const h = setTimeout(() => { timers.delete(id); run(fn); }, scaled(ms));
      timers.set(id, { kind: 'after', h });
      return id;
    }
    function every(ms, fn) {
      const id = nextTimerId++;
      const h = setInterval(() => {
        if (dead || paused) return;
        try { fn(); } catch (e) { say('tick failed: ' + ((e && e.message) || e)); }
      }, Math.max(4, scaled(ms)));
      timers.set(id, { kind: 'every', h });
      return id;
    }
    function clearTimer(id) {
      const rec = timers.get(id);
      if (!rec) return;
      if (rec.kind === 'after') clearTimeout(rec.h); else clearInterval(rec.h);
      timers.delete(id);
    }
    function clearTimers() {
      for (const id of Array.from(timers.keys())) clearTimer(id);
      timers.clear();
      deferred.length = 0;
    }
    /** The decks' registry: this class's own pause-aware timers. */
    const deckTimers = { after, every, clear: clearTimer };

    /* ==================================================================== *
     * ENGINE - one wrapper, the input-trust law and the tier's audio ceiling
     * welded on (the contract's deckEngine weld).
     * ==================================================================== */
    function fireSafe(kind, opts) {
      if (dead || paused || !ctx.engine) return null;
      const o = Object.assign({}, opts || {});
      if (kind === 'flash_burst' || kind === 'gif_burst') {
        /* DECISIONS #9: the ring is a tap-precision surface, so every burst
         * over it is pointer-events:none decoration and never poppable. */
        o.clickSafe = true;
        o.clickable = false;
        delete o.onPop;
      }
      if (kind === 'audio_trigger') {
        const ceil = audioCeilFor(tier);
        o.level = Math.min(ceil, o.level == null ? 0.4 : Number(o.level) || 0);
        /* `pitch` is a pass-through (engine/oneshots.js forwards it and
         * shell/audio.js owns the 0.5..2 clamp) - we never clamp it twice. */
      }
      try { return ctx.engine.fire(kind, o) || null; } catch (e) { say('fire(' + kind + ') failed'); return null; }
    }
    function sustainSafe(kind, opts) {
      if (dead || paused || !ctx.engine) return null;
      const o = Object.assign({}, opts || {});
      if (kind === 'bubble_field') { o.clickSafe = true; delete o.onPop; }
      try { return ctx.engine.sustain(kind, o) || null; } catch (e) { return null; }
    }
    function stopSafe(kind) { try { if (ctx.engine) ctx.engine.stop(kind); } catch (e) { /* noop */ } }
    function beatSafe(opts) { try { if (ctx.engine && ctx.engine.beat) ctx.engine.beat(opts || {}); } catch (e) { /* noop */ } }
    /* THE SEEP'S CLASS-SIDE DOOR (tell 13, the Overseen Frame). We name a DEAD
     * MOMENT and the engine asks the director; it answers null the overwhelming
     * majority of the time and that is the feature. The engine owns the pixels,
     * on its own pointer-events:none fx layer, and the claim releases itself on
     * the tell's last frame - there is nothing here to hold and nothing to undo.
     * A `dead`/`paused` class never asks: a dead moment in a class nobody is
     * playing is not a dead moment, it is an absence. */
    function deadBeatSafe(name) {
      if (dead || paused || !ctx.engine || typeof ctx.engine.deadBeat !== 'function') return null;
      try { return ctx.engine.deadBeat(name) || null; } catch (e) { return null; }
    }
    function ceremonySafe(kind, opts) {
      if (dead || !ctx.engine || typeof ctx.engine.ceremony !== 'function') return null;
      try { return ctx.engine.ceremony(kind, opts || {}) || null; } catch (e) { return null; }
    }
    /** The engine, as a deck sees it: the welded primitives plus a READ of the
     *  clamped channel vector (THE CEILING RULE - a deck asks, never raises). */
    const deckEngine = {
      fire: fireSafe,
      sustain: sustainSafe,
      stop: stopSafe,
      beat: beatSafe,
      ceremony: ceremonySafe,
      channels: () => {
        try { return (ctx.engine && typeof ctx.engine.channels === 'function') ? ctx.engine.channels() : null; }
        catch (e) { return null; }
      },
    };
    /** The player's own media, as a deck sees it. The pool lands ASYNC, so a
     *  deck gets a LIVE reader rather than the pool object. */
    const deckAssets = {
      next(kind) {
        try { return (pool && typeof pool.next === 'function') ? (pool.next(kind) || null) : null; }
        catch (e) { return null; }
      },
    };
    /** bgIntensity 0 is the player's exit: read it LIVE, never a snapshot. */
    function capsArmed() { return !(ctx.caps && Number(ctx.caps.bgIntensity) === 0); }

    /** A pad tone (or any cue) through the engine, at the tier's ceiling. */
    function tone(name, level, pitch, extra) {
      return fireSafe('audio_trigger', Object.assign(
        { name, level: level == null ? 0.4 : level },
        pitch != null ? { pitch } : {},
        extra || {},
      ));
    }
    /* THE REFUSED PRESS (W2 chrome vocabulary). A press on the ring while the
     * room is still playing is a DEAD INPUT - the LISTEN LOCK already says so
     * with the desaturated ring and the not-allowed cursor, and the House Book
     * says a dead input answers with a muted `bump`. THROTTLED, because a
     * mashed pad must not machine-gun it. The faint pad tick that plays beside
     * it is the pad tone system's and is deliberately untouched. */
    const CHROME_BUMP_MS = 250;
    let lastBumpAt = 0;
    function bumpRefused() {
      const now = Date.now();
      if (now - lastBumpAt < CHROME_BUMP_MS) return;
      lastBumpAt = now;
      tone('bump', 0.15, 1);   /* owner 2026-08-24: error cues -50% */
    }
    /** Deck IV's haptics hook: one call per rung, silent where there is no hardware. */
    function haptic(ms) {
      try {
        if (!ctx.platform || !ctx.platform.hasHaptics) return;
        if (typeof navigator === 'undefined' || typeof navigator.vibrate !== 'function') return;
        navigator.vibrate(Math.max(4, Math.min(40, Number(ms) || 10)));
      } catch (e) { /* a haptic must never be the thing that fails */ }
    }

    /* ---- the decks, null-safe ------------------------------------------- */
    function deck(which, method, ...args) {
      const d = which === 'casino' ? casino : which === 'pressure' ? pressure : trickster;
      if (!d || typeof d[method] !== 'function') return undefined;
      try { return d[method](...args); }
      catch (e) { say(which + '.' + method + ' threw: ' + ((e && e.message) || e)); return undefined; }
    }

    /* ==================================================================== *
     * HEAT - the class's own ladder is the sequence length; gradeTier caps it.
     * ==================================================================== */
    function heat() {
      const h = heatFor(curLen, tier, startLen, pressStreak);
      currentHeat = h;
      try { if (ctx.engine) ctx.engine.setHeat(h); } catch (e) { /* engine is optional */ }
      deck('casino', 'setHeat', h);
      deck('pressure', 'setHeat', h);
      /* The RUNG rides the ladder (the sequence length), the MAGNITUDE rides
       * heat - and this is the one place both are known. The second argument
       * is additive: a deck that declared setStreak(n) simply ignores it. */
      deck('pressure', 'setStreak', curLen, { links: pressStreak, cleared: clearStreak, heat: h });
    }

    /* ==================================================================== *
     * DOM (the contract's exact shape)
     * ==================================================================== */
    function setPhase(p) {
      if (stage) stage.setAttribute('data-phase', p);
      paintBanner();
    }
    function phase() { return stage ? stage.getAttribute('data-phase') : null; }

    /* ==================================================================== *
     * THE PHASE BANNER - the one node that answers "is it me?" without a
     * single animation. `data-p` is its own enum (the stage's data-phase has
     * six values; the player only needs to know four things), and a transient
     * verdict (miss / late / clear) simply overwrites it until the next phase
     * change paints over the top.
     * ==================================================================== */
    /** stage phase -> banner state. The room's turn is ONE word, yours is ONE word. */
    function bannerStateFor(p) {
      if (p === 'input') return 'yours';
      if (p === 'echo' || p === 'play' || p === 'encore') return 'listen';
      if (p === 'ended') return 'over';
      return 'ready';
    }
    function bannerKeyFor(state) {
      if (state === 'yours') return ['ec_phase_yours', EC_LEX.ec_phase_yours];
      if (state === 'listen') return ['ec_phase_listen', EC_LEX.ec_phase_listen];
      if (state === 'over') return ['ec_phase_over', EC_LEX.ec_phase_over];
      return ['ec_phase_ready', EC_LEX.ec_phase_ready];
    }
    function bannerSet(state, text) {
      if (!bannerEl) return;
      bannerEl.setAttribute('data-p', state);
      if (bannerTextEl) bannerTextEl.textContent = String(text == null ? '' : text);
    }
    function paintBanner() {
      const state = bannerStateFor(phase());
      const pair = bannerKeyFor(state);
      bannerSet(state, t(pair[0], pair[1]));
    }
    /** A VERDICT on the banner: the clear / miss / late line, until the next phase. */
    function bannerVerdict(state, key, fallback, subst) {
      let text = t(key, fallback);
      if (subst) for (const k of Object.keys(subst)) text = text.split('{' + k + '}').join(String(subst[k]));
      bannerSet(state, text);
    }

    /* ==================================================================== *
     * THE STEP STRIP - one dot per step of the sequence. During LISTEN the
     * dots fill as the room plays; during YOUR TURN they empty and fill again
     * under the player, and a wrong press turns one red. This is the single
     * clearest Simon affordance and Echo did not have it.
     *
     * A DECOY NEVER FILLS A DOT (Law I): the strip counts the real sequence,
     * which is exactly what makes a telegraphed decoy readable.
     * ==================================================================== */
    function buildSteps(len) {
      stepEls.length = 0;
      stepFill.length = 0;
      if (!stepsEl) return;
      stepsEl.textContent = '';
      const want = Math.max(0, Math.floor(Number(len) || 0));
      for (let i = 0; i < want; i++) {
        const dot = el('i', 'g-ec-step');
        dot.setAttribute('data-fill', 'off');
        dot.setAttribute('aria-hidden', 'true');
        dot.style.setProperty('--i', String(i));
        stepsEl.appendChild(dot);
        stepEls.push(dot);
        stepFill.push('off');
      }
      paintStepsAria();
    }
    function stepFillSet(i, how) {
      if (i < 0 || i >= stepEls.length) return;
      stepFill[i] = how;
      const dot = stepEls[i];
      if (dot) dot.setAttribute('data-fill', how);
      paintStepsAria();
    }
    function resetSteps() {
      for (let i = 0; i < stepEls.length; i++) stepFillSet(i, 'off');
    }
    /** The strip's honest label: N of LEN, for a reader that cannot see dots. */
    function paintStepsAria() {
      if (!stepsEl) return;
      const done = stepFill.filter((f) => f === 'on').length;
      const line = t('ec_step_aria', EC_LEX.ec_step_aria).replace('{n}', String(done))
        + ' / ' + String(stepFill.length);
      stepsEl.setAttribute('aria-label', line);
    }
    function msg(key, fallback) {
      if (!msgEl) return;
      msgEl.textContent = t(key, fallback);
    }

    function buildDom() {
      const root = ctx.root;
      root.textContent = '';
      stage = el('div', 'g-ec-stage');
      stage.setAttribute('data-phase', 'briefing');
      stage.setAttribute('data-tier', String(tier));
      stage.setAttribute('data-faces', faceMode);
      /* The audio tell is a STAGE attribute so style.js can make the light
       * carry the whole signal when the ear cannot (ctx.audioAudible). */
      stage.setAttribute('data-audible', audible ? '1' : '0');
      if (reduced) stage.setAttribute('data-reduced', '1');

      backdrop = el('div', 'g-ec-backdrop');
      backdrop.setAttribute('aria-hidden', 'true');
      backdrop.style.pointerEvents = 'none';
      stage.appendChild(backdrop);

      hud = el('div', 'g-ec-hud');
      lenChip = el('span', 'g-ec-chip g-ec-len', '0');
      lenChip.setAttribute('aria-label', t('ec_chip_len', EC_LEX.ec_chip_len));
      clockChip = el('span', 'g-ec-chip g-ec-clock', clockText());
      clockChip.setAttribute('aria-label', t('ec_chip_clock', EC_LEX.ec_chip_clock));
      streakChip = el('span', 'g-ec-chip g-ec-streak', 'x 0');
      streakChip.setAttribute('aria-label', t('ec_chip_streak', EC_LEX.ec_chip_streak));
      streakChip.hidden = true;
      bestChip = el('span', 'g-ec-chip g-ec-best', '0');
      bestChip.setAttribute('aria-label', t('ec_chip_best', EC_LEX.ec_chip_best));
      hud.appendChild(lenChip);
      hud.appendChild(clockChip);
      hud.appendChild(streakChip);
      hud.appendChild(bestChip);
      timerEl = el('span', 'g-ec-chip g-ec-timer');
      timerEl.setAttribute('aria-hidden', 'true');
      timerEl.hidden = true;
      hud.appendChild(timerEl);
      if (retake) hud.appendChild(el('span', 'g-ec-chip g-ec-retake', t('ec_retake', EC_LEX.ec_retake)));
      stage.appendChild(hud);

      /* THE BANNER + THE STRIP. Both sit between the HUD and the ring so the
       * eye crosses them on the way to the pads. aria-live polite: the turn
       * flipping is exactly the kind of thing a screen reader should announce. */
      bannerEl = el('div', 'g-ec-phase');
      bannerEl.setAttribute('data-p', 'ready');
      bannerEl.setAttribute('aria-live', 'polite');
      bannerEl.setAttribute('aria-label', t('ec_phase_aria', EC_LEX.ec_phase_aria));
      /* The listening glyph is DRAWN (style.js owns it) and aria-hidden: it is
       * the non-text half of the signal, for the frame where the word is read
       * already and only the shape registers. */
      const glyph = el('span', 'g-ec-phase-glyph');
      glyph.setAttribute('aria-hidden', 'true');
      bannerEl.appendChild(glyph);
      bannerTextEl = el('span', 'g-ec-phase-text', t('ec_phase_ready', EC_LEX.ec_phase_ready));
      bannerEl.appendChild(bannerTextEl);
      stage.appendChild(bannerEl);

      stepsEl = el('div', 'g-ec-steps');
      stepsEl.setAttribute('role', 'img');
      stepsEl.setAttribute('aria-label', t('ec_steps_aria', EC_LEX.ec_steps_aria));
      stage.appendChild(stepsEl);
      stepEls.length = 0;
      stepFill.length = 0;

      ring = el('div', 'g-ec-ring');
      ring.setAttribute('role', 'group');
      ring.setAttribute('aria-label', t('ec_ring_aria', EC_LEX.ec_ring_aria));
      ring.style.setProperty('--ec-n', String(PLAYTEST.PADS));
      padEls.length = 0;
      for (let i = 0; i < PLAYTEST.PADS; i++) {
        const pad = el('button', 'g-ec-pad');
        pad.setAttribute('type', 'button');
        try { pad.type = 'button'; } catch (e) { /* the DOM double has no button semantics */ }
        pad.setAttribute('data-pad', String(i));
        pad.setAttribute('data-state', 'idle');
        /* Born FROSTED. The word is in the DOM from the first frame (a reader
         * and the trickster both need it there); style.js is what hides it. */
        pad.setAttribute('data-veil', 'on');
        pad.setAttribute('data-glyph', String(i));
        pad.setAttribute('aria-label', t('ec_pad_aria', EC_LEX.ec_pad_aria).replace('{n}', String(i + 1)));
        /* GEOMETRY IS THE CREATIVE'S. index.js only ever writes the vars: the
         * pad's index, the ring's size and the unit-circle position of the
         * slot. style.js owns every transform and transition. */
        const ang = (-90 + (360 / PLAYTEST.PADS) * i);
        pad.style.setProperty('--i', String(i));
        pad.style.setProperty('--ang', String(ang) + 'deg');
        pad.style.setProperty('--x', Math.cos(ang * Math.PI / 180).toFixed(4));
        pad.style.setProperty('--y', Math.sin(ang * Math.PI / 180).toFixed(4));

        const face = el('span', 'g-ec-face');
        const media = el('img', 'g-ec-media');
        armMedia(media, pad, false);
        face.appendChild(media);
        /* THE TRUTH NODE (Law IV + the Unreliable Label card): the glyph is
         * what a pad IS. The word below it is decoration the trickster may
         * borrow; the glyph never moves and is never re-written. */
        face.appendChild(el('span', 'g-ec-glyph', GLYPHS[i] || ''));
        pad.appendChild(face);
        pad.appendChild(el('span', 'g-ec-word', ''));
        /* THE REVEAL CAPTION. Drawn on every pad, shown by style.js ONLY under
         * data-state="reveal" - so the pad you needed does not merely light, it
         * says out loud that it was the one. */
        const hint = el('span', 'g-ec-hint', t('ec_this_one', EC_LEX.ec_this_one));
        hint.setAttribute('aria-hidden', 'true');
        pad.appendChild(hint);

        pad.addEventListener('pointerdown', onPadDown);
        pad.addEventListener('pointerup', onPadUp);
        pad.addEventListener('pointerleave', onPadUp);
        pad.addEventListener('pointercancel', onPadUp);
        ring.appendChild(pad);
        padEls.push(pad);
      }
      stage.appendChild(ring);

      /* THE STAMP WELL. shell/ceremonies.js only centres a stamp when it lands
       * on the fixed ceremony layer; an in-flow target gets it at the target's
       * top-left corner. The ring IS where the stamp belongs, so it gets a
       * centred, pointer-events-none host of its own rather than the game
       * fighting `.arc-stamp`'s own rotate transform. */
      stampWell = el('div', 'g-ec-stampwell');
      stampWell.setAttribute('aria-hidden', 'true');
      ring.appendChild(stampWell);

      msgEl = el('p', 'g-ec-msg');
      msgEl.setAttribute('aria-live', 'polite');
      stage.appendChild(msgEl);

      well = el('div', 'g-ec-flashwell');
      well.setAttribute('aria-hidden', 'true');
      well.style.pointerEvents = 'none';
      stage.appendChild(well);

      endEl = el('div', 'g-ec-end');
      endEl.hidden = true;
      stage.appendChild(endEl);

      root.appendChild(stage);
    }

    /* ---- pad faces (Deck VI asset chrome) -------------------------------- */
    function armMedia(node, pad, video) {
      if (!node) return;
      try {
        node.setAttribute('alt', '');
        node.setAttribute('draggable', 'false');
        node.draggable = false;
        if (video) {
          node.muted = true; node.loop = true; node.autoplay = true; node.playsInline = true;
          node.setAttribute('muted', ''); node.setAttribute('loop', ''); node.setAttribute('autoplay', '');
          node.setAttribute('playsinline', ''); node.setAttribute('preload', 'auto');
          node.addEventListener('loadeddata', () => {
            if (dead) return;
            pad.classList.add('is-loaded');
            try { const p = node.play(); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch (e) { /* ignore */ }
          });
        } else {
          node.decoding = 'async';
          node.addEventListener('load', () => { if (!dead) pad.classList.add('is-loaded'); });
        }
        node.addEventListener('error', () => { if (!dead) faceBroken(pad); });
      } catch (e) { /* the double has no img semantics; fine */ }
    }
    function childOf(node, cls) {
      try { for (const k of node.children || []) if (k.classList && k.classList.contains(cls)) return k; } catch (e) { /* ignore */ }
      return null;
    }
    function faceNodeOf(pad) { return childOf(pad, 'g-ec-face'); }
    function mediaOf(pad) { const f = faceNodeOf(pad); return f ? childOf(f, 'g-ec-media') : null; }
    function wordNodeOf(pad) { return childOf(pad, 'g-ec-word'); }
    function glyphNodeOf(pad) { const f = faceNodeOf(pad); return f ? childOf(f, 'g-ec-glyph') : null; }
    /** The glitch_swap target set for a pad: the FACE's children, never the pad. */
    function faceTargets(pad) {
      const f = faceNodeOf(pad);
      if (!f) return [];
      const out = [];
      try { for (const k of f.children || []) out.push(k); } catch (e) { /* ignore */ }
      return out;
    }
    function mediaNodeFor(pad, url) {
      const face = faceNodeOf(pad);
      if (!face) return null;
      const cur = childOf(face, 'g-ec-media');
      const wantVideo = VIDEO_URL_RE.test(String(url || ''));
      const isVideo = !!(cur && String(cur.tagName || '').toUpperCase() === 'VIDEO');
      if (cur && wantVideo === isVideo) return cur;
      const next = el(wantVideo ? 'video' : 'img', 'g-ec-media');
      armMedia(next, pad, wantVideo);
      try {
        if (cur && typeof face.replaceChild === 'function') face.replaceChild(next, cur);
        else face.appendChild(next);
      } catch (e) { try { face.appendChild(next); } catch (e2) { /* ignore */ } }
      return next;
    }
    function faceBroken(pad) {
      const i = Number(pad && pad.getAttribute('data-pad'));
      pad.classList.remove('is-loaded');
      if (i >= 0) {
        faceUrls.set(i, { broken: true });
        say('faces: pad ' + i + ' media failed - plain face for the class');
      }
    }
    /** One url per pad, dealt once and frozen for the class. MEDIA FACES ONLY:
     *  the owner's verdict retired the gif faces as the default look, so the
     *  plumbing stays and the opt-in is ec_pad_words = 'media'. */
    function dressFaces() {
      if (faceMode !== 'media') return;
      if (!pool || typeof pool.next !== 'function') return;
      for (let i = 0; i < padEls.length; i++) {
        const have = faceUrls.get(i);
        if (have) continue;
        let got = null;
        try { got = pool.next(faceKind); } catch (e) { got = null; }
        const url = got && got.url ? String(got.url) : null;
        if (!url) continue;
        faceUrls.set(i, { url, kind: faceKind });
        const pad = padEls[i];
        const media = mediaNodeFor(pad, url) || mediaOf(pad);
        pad.classList.remove('is-loaded');
        try { if (media) media.src = url; } catch (e) { /* ignore */ }
      }
      if (!faceLogged && faceUrls.size) {
        faceLogged = true;
        say('faces: ' + faceKind + ' on ' + faceUrls.size + ' pads');
      }
    }

    /* ==================================================================== *
     * THE TRIGGER POOL - the player's OWN active triggers, as {text, audio}.
     * `init.triggers` is the host's projection of the enabled SubliminalPool
     * with each phrase's whisper clip resolved to a ccp.subaudio / ccp.modaudio
     * url (or null). `init.words` is the same phrases WITHOUT audio and is what
     * every other class reads, so we prefer triggers and fall back to words: a
     * host that predates the field still gets text faces, just silent ones.
     * ==================================================================== */
    function triggerPool() {
      const out = [];
      const seen = new Set();
      const push = (rawText, rawAudio) => {
        const text = padWordOf(rawText);
        if (!text) return;
        const k = text.toLowerCase();
        if (seen.has(k)) return;          // never the same trigger on two pads
        seen.add(k);
        const audio = (typeof rawAudio === 'string' && rawAudio) ? rawAudio : null;
        out.push({ text, audio });
      };
      try {
        const src = Array.isArray(ctx.triggers) ? ctx.triggers : null;
        if (src) for (const row of src) {
          if (!row) continue;
          if (typeof row === 'string') push(row, null);
          else push(row.text, row.audio);
        }
      } catch (e) { /* a malformed projection is an empty pool, never a crash */ }
      try {
        const words = Array.isArray(ctx.words) ? ctx.words : [];
        for (const w of words) push(w, null);   // de-duplicated against the above
      } catch (e) { /* ignore */ }
      return out;
    }

    /* ---- pad faces: ONE trigger per pad, glyphs for the rest ------------- */
    /** Once per class: settle the face mode and the pool, then deal round 0. */
    function assignWords() {
      const want = String(ctx.settings && ctx.settings.ec_pad_words != null ? ctx.settings.ec_pad_words : 'words')
        .trim().toLowerCase();
      faceMode = FACE_MODES.indexOf(want) >= 0 ? want : 'words';
      facePool = triggerPool();
      /* THE MAY-BE-EMPTY CONTRACT (the dossier's mod-agnosticism proof) still
       * holds at ZERO: with no triggers at all the ring wears glyphs and the
       * whole mechanic runs unchanged. A PARTIAL pool wears what it has - the
       * pads that got one wear it, the rest wear their glyph. */
      const wordy = faceMode === 'words' || faceMode === 'media';
      if (wordy && facePool.length === 0) {
        faceMode = 'glyphs';
        say('pad triggers: the active pool is empty - glyph faces (the mechanic is unchanged)');
      }
      if (stage) stage.setAttribute('data-faces', faceMode);
      dealOrder = [];
      dealCursor = 0;
      dealCycle = 0;
      roundIdx = 0;
      dealFaces();
      const withAudio = padTriggers.filter((x) => x && x.audio).length;
      say('faces ' + faceMode + ': pool ' + facePool.length + ', '
        + padWords.filter(Boolean).length + ' on the ring, ' + withAudio + ' with a clip'
        + (audible ? '' : ' (INAUDIBLE - no clip will play)'));
    }

    /** The next seeded permutation of the pool. `dealCycle` is what makes the
     *  WHOLE run of deals replayable, not just the first one. */
    function refillDealOrder() {
      dealOrder = facePool.length ? shuffled(facePool, makeRng(seed + '|ec-words|' + dealCycle)) : [];
      dealCursor = 0;
      dealCycle += 1;
    }

    /**
     * THE PER-ROUND DEAL (owner round 2: "those should change from round to
     * round, while the colors stay the same"). Nothing about the RING moves -
     * hue, angle, hitbox and glyph are all functions of the pad INDEX and are
     * never touched here (Law II). Only which phrase sits inside changes.
     *
     * The cursor walks a seeded permutation, so with a pool bigger than the
     * ring every trigger is shown before any comes back. A phrase already on
     * the ring is skipped rather than duplicated - which can only happen across
     * a permutation boundary, and costs that phrase its slot in the new cycle.
     */
    function dealFaces() {
      padWords.length = 0;
      padTriggers.length = 0;
      const wordy = faceMode === 'words' || faceMode === 'media';
      if (!wordy || facePool.length === 0) {
        for (let i = 0; i < PLAYTEST.PADS; i++) { padTriggers.push(null); padWords.push(''); }
      } else {
        const take = Math.min(PLAYTEST.PADS, facePool.length);
        const picked = [];
        const seen = new Set();
        let guard = 0;
        while (picked.length < take && guard < facePool.length * 4 + 8) {
          guard += 1;
          if (dealCursor >= dealOrder.length) refillDealOrder();
          if (!dealOrder.length) break;
          const cand = dealOrder[dealCursor];
          dealCursor += 1;
          if (!cand || !cand.text) continue;
          const k = cand.text.toLowerCase();
          if (seen.has(k)) continue;      // never the same trigger on two pads
          seen.add(k);
          picked.push(cand);
        }
        for (let i = 0; i < PLAYTEST.PADS; i++) {
          const trig = picked[i] || null;
          padTriggers.push(trig);
          padWords.push(trig ? trig.text : '');
        }
      }
      /* Per-PAD truth as well as per-stage: a ring with four triggers and two
       * glyphs has to size each face for what it actually wears. */
      for (let i = 0; i < padEls.length; i++) {
        const pad = padEls[i];
        if (!pad) continue;
        pad.setAttribute('data-face', padWords[i] ? 'word' : 'glyph');
        /* A fresh phrase arrives FROSTED however the pad was lit a moment ago:
         * a re-deal must never hand the new word out for free. */
        if (pad.getAttribute('data-state') === 'idle') setVeil(pad, true);
      }
      paintWords();
    }

    /* ==================================================================== *
     * THE FIT (owner round 2: "some triggers were too long and got cut").
     *
     * ONE SIZE FOR THE WHOLE RING, not one per pad, for three reasons: six pads
     * wearing six type sizes looks like a bug; the trickster's Unreliable Label
     * swaps a word onto ANOTHER pad, and a per-pad size would let a long phrase
     * overflow the short pad it was lied onto; and one shared search is 5
     * measurements instead of 30.
     *
     * The search is sequence.js's pure fitFontPx; this half only measures. The
     * word box is FIT_LINES lines tall in `em`, so at any candidate size the
     * box is exactly three lines and the question is only "does the wrapped
     * phrase fit in three lines of THIS size". Headless (no layout) it bails and
     * the stylesheet's own clamp stands.
     * ==================================================================== */
    /* ==================================================================== *
     * THE RULER - one hidden span at a reference 100px, wearing the word's own
     * typography, used to measure the widest single WORD in the dealt hand.
     *
     * Why not just read the word node? Because the box is text-align:center
     * and Chrome does not count inline-START overflow: GIGGLETIME hanging off
     * both sides still measures scrollWidth === clientWidth, so a search that
     * trusted the box happily "fitted" a size that renders GIGGLETIM / E.
     * A token's width scales linearly with the font size (letter-spacing is in
     * `em`), so one measurement answers every candidate size.
     * ==================================================================== */
    function ruler() {
      if (rulerEl && rulerEl.parentNode) return rulerEl;
      if (!stage || typeof document === 'undefined' || !document.createElement) return null;
      try {
        const el = document.createElement('span');
        el.className = 'g-ec-ruler';
        el.setAttribute('aria-hidden', 'true');
        stage.appendChild(el);
        rulerEl = el;
      } catch (e) { rulerEl = null; }
      return rulerEl;
    }
    /** Widest token of the dealt hand, in px at 100px type. 0 = cannot measure
     *  (headless, or no layout yet), and the width rule simply stands down. */
    function measureTokens() {
      tokenW100 = 0;
      const el = ruler();
      if (!el || typeof el.getBoundingClientRect !== 'function') return;
      /* WEAR WHAT THE WORD WEARS. Copied, not duplicated: the one thing that
       * can quietly break this measurement is the ruler and the word ending up
       * in different type, and that is exactly what happened once already. */
      try {
        const w0 = wordNodeOf(padEls[0]);
        const cs = (w0 && typeof window !== 'undefined' && window.getComputedStyle)
          ? window.getComputedStyle(w0) : null;
        if (cs && el.style) {
          const base = parseFloat(cs.fontSize) || 0;
          const ls = parseFloat(cs.letterSpacing);
          el.style.fontFamily = cs.fontFamily || '';
          el.style.fontWeight = cs.fontWeight || '';
          el.style.fontStyle = cs.fontStyle || '';
          el.style.fontStretch = cs.fontStretch || '';
          el.style.textTransform = cs.textTransform || '';
          /* letter-spacing comes back in px at the WORD's size; the ruler is
           * at 100px, so it is rescaled rather than copied. */
          el.style.letterSpacing = (base > 0 && isFinite(ls)) ? (((ls / base) * 100) + 'px') : '';
          el.style.fontSize = '100px';
        }
      } catch (e) { /* the CSS floor already dressed it */ }
      for (let i = 0; i < padWords.length; i++) {
        const parts = String(padWords[i] || '').split(/\s+/);
        for (let k = 0; k < parts.length; k++) {
          if (!parts[k]) continue;
          try {
            el.textContent = parts[k];
            const w = el.getBoundingClientRect().width || 0;
            if (w > tokenW100) tokenW100 = w;
          } catch (e) { /* noop */ }
        }
      }
      try { el.textContent = ''; } catch (e) { /* noop */ }
    }
    function ringFitsAt(px) {
      if (!stage) return true;
      try { stage.style.setProperty('--ec-word-px', px + 'px'); } catch (e) { return true; }
      for (let i = 0; i < padEls.length; i++) {
        if (!padWords[i]) continue;
        const w = wordNodeOf(padEls[i]);
        if (!w) continue;
        /* +1px of slack: sub-pixel line metrics otherwise reject a size that
         * looks perfect, and the search would walk a step further down. */
        if (w.scrollHeight > w.clientHeight + 1) return false;
        if (w.scrollWidth > w.clientWidth + 1) return false;
        /* NO WORD MAY BE SPLIT. overflow-wrap:anywhere is the net that keeps a
         * monster token from being cut off - this is what stops the net from
         * ever being needed on a phrase a smaller size would spell whole. */
        if (tokenW100 > 0 && w.clientWidth > 0
          && (tokenW100 * px) / 100 > w.clientWidth + 1) return false;
      }
      return true;
    }
    function fitWords() {
      if (dead || !stage || !padEls.length) return;
      const probe = wordNodeOf(padEls[0]);
      if (!probe || typeof probe.scrollHeight !== 'number' || typeof probe.clientHeight !== 'number') return;
      let padPx = 0;
      try {
        padPx = (padEls[0] && typeof padEls[0].getBoundingClientRect === 'function')
          ? Math.round(padEls[0].getBoundingClientRect().width) : 0;
      } catch (e) { padPx = 0; }
      if (!padPx) return;               // not laid out yet; the next pass gets it
      measureTokens();                    // the widest word, once, for every candidate
      const maxPx = Math.max(
        PLAYTEST.FIT_MIN_PX,
        Math.min(PLAYTEST.FIT_MAX_PX, Math.round(padPx * PLAYTEST.FIT_MAX_K)),
      );
      const px = fitFontPx({ maxPx, minPx: PLAYTEST.FIT_MIN_PX, fits: ringFitsAt });
      try { stage.style.setProperty('--ec-word-px', px + 'px'); } catch (e) { /* noop */ }
      if (px !== fittedPx) {
        fittedPx = px;
        say('fit: the ring wears ' + px + 'px (pad ' + padPx + 'px, longest "'
          + padWords.reduce((a, b) => (String(b || '').length > String(a || '').length ? b : a), '') + '")');
      }
    }
    /** Coalesce fits: a deal, a resize and a lie can all ask in the same frame. */
    function scheduleFit() {
      if (dead || fitPending) return;
      fitPending = after(16, () => { fitPending = 0; fitWords(); });
    }

    /* ==================================================================== *
     * THE TRIGGER CLIP - the owner's "playing faintly with the tune we play
     * hitting the bubble". The NOTE stays the primary sound and keeps the
     * pitch ratchet; the clip rides UNDER it on the voice bus at CLIP_LEVEL,
     * one per press, cut on a re-press of the same pad (the mixer's `key` is
     * the voice slot) and hard-capped at CLIP_MAX_MS so a fast sequence never
     * smears. ctx.audioAudible === false is the whole-feature off switch: the
     * player has muted whispers app-wide and a class may not undo that.
     * ==================================================================== */
    function padClip(i) {
      if (!audible) return null;
      const trig = padTriggers[i];
      if (!trig || !trig.audio) return null;
      /* ALWAYS consume the roll, even when the answer is going to be no: the
       * stream position must not depend on the wall clock, or a retake would
       * whisper in different places than the first run did (Law V). */
      const roll = clipRoll ? clipRoll('beat|' + clipBeat) : 1;
      clipBeat += 1;
      if (!(roll < CLIP_CHANCE)) { clipsRolledOff += 1; return null; }
      /* ONE WHISPER AT A TIME. A roll that lands while the last clip is still
       * in the air is dropped, not layered - it simply spends its turn. The
       * window is the mixer's own hard cap, scaled with the harness clock so a
       * compressed test run sees the same proportion a real class does. */
      const now = Date.now();
      if (now < clipUntil) { clipsSkipped += 1; return null; }
      clipUntil = now + Math.max(1, scaled(PLAYTEST.CLIP_MAX_MS));
      const r = fireSafe('audio_trigger', {
        name: CLIP_SFX,                 // the recipe a host that cannot play urls falls back to
        url: trig.audio,
        key: 'ec-pad-' + i,
        maxMs: PLAYTEST.CLIP_MAX_MS,
        fadeMs: PLAYTEST.CLIP_FADE_MS,
        bus: PLAYTEST.CLIP_BUS,
        level: PLAYTEST.CLIP_LEVEL,
      });
      if (r) clipsFired += 1;
      return r;
    }
    /** The pad's whole voice: the note (primary, pitched) and its trigger under it. */
    function padVoice(i, level, pitch) {
      tone(PAD_SFX, level, pitch);
      padClip(i);
    }
    /** THE TRUTH on every pad word (the trickster's Unreliable Label restores
     *  exactly this string; the glyph it can never touch). */
    function paintWords() {
      let moved = false;
      for (let i = 0; i < padEls.length; i++) {
        const w = wordNodeOf(padEls[i]);
        if (w && w.textContent !== (padWords[i] || '')) { w.textContent = padWords[i] || ''; moved = true; }
      }
      /* New text means a new longest phrase, which means a new size. */
      if (moved) scheduleFit();
    }
    function padWordText(i) { return padWords[i] || ''; }

    /* ---- HUD paint (truth) ---------------------------------------------- */
    function secLeft() { return Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)); }
    function clockText() { return mmss(secLeft()); }
    function lenText() { return String(curLen); }
    function bestText() { return String(Math.max(bestLen, lifetimeBefore)); }
    /** What the trickster's Stat Flicker restores after a lie. */
    function chipText(which) {
      if (which === 'clock') return clockText();
      if (which === 'best') return bestText();
      if (which === 'streak') return 'x ' + pressStreak;
      return lenText();
    }
    function paintHud() {
      if (clockChip) clockChip.textContent = clockText();
      if (lenChip) lenChip.textContent = lenText();
      if (bestChip) bestChip.textContent = bestText();
      paintStreak();
    }
    function paintStreak() {
      if (!streakChip) return;
      if (pressStreak < PLAYTEST.STREAK_VISIBLE) {
        streakChip.hidden = true;
        streakChip.textContent = 'x ' + pressStreak;
        return;
      }
      streakChip.hidden = false;
      streakChip.textContent = 'x ' + pressStreak;
      /* The meter is the SHELL's primitive (10 segments, always); it rides
       * inside the chip so the contract's DOM gains no extra node. */
      try {
        const meter = ctx.ceremonies.streakMeter({
          filled: Math.min(PLAYTEST.STREAK_CAP, pressStreak),
          gold: ratchet >= PLAYTEST.RATCHET_CAP,
        });
        if (meter) streakChip.appendChild(meter);
      } catch (e) { /* a ceremony must never be the thing that fails */ }
    }

    /* ==================================================================== *
     * THE PADS - lighting and state. `data-state` is the contract's enum:
     * idle | lit | pressed | decoy | wrong | reveal. Nothing else is ever
     * written to it.
     *
     * THE VEIL rides the same switch (owner round 2). A pad at rest FROSTS its
     * phrase; ANY other state clears it. That one rule buys three things at
     * once: during LISTEN the playing pad shows its word for its own beat (the
     * reveal IS the tell), your own press shows you what you just said, and the
     * miss reveal shows the phrase you should have echoed. The DECOY clears too
     * - deliberately: at tier 3+ a decoy that stayed frosted while every real
     * step cleared would be a free tell, and the decoy's whole job is to be
     * indistinguishable. Hover / focus / press clear it as well, but that half
     * is CSS (style.js) because a pointer is not a game state.
     * ==================================================================== */
    /* THE TRIGGER FLASH (owner, 2026-08-25). Desktop reads a frosted word by
     * hovering; a finger cannot hover, so EVERY press - refused and locked taps
     * included - flashes the pad's word for FLASH_MS. The flash OWNS the veil
     * while it runs: `flashOn` gates setVeil so a shorter state hold (a 130ms
     * `pressed`, say) cannot re-frost the word mid-read, and the restore rides
     * the pause-aware timer registry like every other beat. A pad that is in a
     * non-idle state when the flash expires is left alone - that state already
     * unveils, and its own idle restore re-frosts (padState's timer). */
    const flashTimers = new Map();      // pad index -> the timer that re-frosts
    const flashOn = new Set();          // pads whose word a press is showing
    function flashWord(i, ms) {
      const pad = padEls[i];
      if (!pad) return;
      const dur = Number(ms) > 0 ? Number(ms)
        : (reduced ? PLAYTEST.FLASH_MS_REDUCED : PLAYTEST.FLASH_MS);
      const prev = flashTimers.get(i);
      if (prev) clearTimer(prev);
      flashOn.add(i);
      setVeil(pad, false);
      flashTimers.set(i, after(dur, () => {
        flashTimers.delete(i);
        flashOn.delete(i);
        const p = padEls[i];
        if (p && p.getAttribute('data-state') === 'idle') setVeil(p, true);
      }));
    }
    function clearFlashes() {
      for (const id of Array.from(flashTimers.values())) clearTimer(id);
      flashTimers.clear();
      flashOn.clear();
    }
    function setVeil(pad, on) {
      if (!pad) return;
      let want = !!on;
      if (want) {
        /* A live flash refuses the frost - whoever asked (a state hold ending,
         * a re-deal) the player is still owed the rest of the read. */
        try { if (flashOn.has(Number(pad.getAttribute('data-pad')))) want = false; } catch (e) { /* noop */ }
      }
      try { pad.setAttribute('data-veil', want ? 'on' : 'off'); } catch (e) { /* noop */ }
    }
    function padState(i, state, holdMs) {
      const pad = padEls[i];
      if (!pad) return;
      const prev = padTimers.get(i);
      if (prev) { clearTimer(prev); padTimers.delete(i); }
      pad.setAttribute('data-state', state);
      setVeil(pad, state === 'idle');
      if (holdMs > 0) {
        const id = after(holdMs, () => {
          padTimers.delete(i);
          const p = padEls[i];
          if (p && p.getAttribute('data-state') === state) {
            p.setAttribute('data-state', 'idle');
            setVeil(p, true);
          }
        });
        padTimers.set(i, id);
      }
    }
    function clearPads() {
      for (const id of Array.from(padTimers.values())) clearTimer(id);
      padTimers.clear();
      clearFlashes();                   // a fresh deal never inherits a live flash
      for (const pad of padEls) {
        if (!pad) continue;
        pad.setAttribute('data-state', 'idle');
        setVeil(pad, true);
      }
    }

    /* ==================================================================== *
     * INPUT - tap or the declared pad verbs. Law II: the hitbox is the pad's
     * own, nothing decorative sits over it, and a press outside an input turn
     * is IGNORED (never a fail: the player is not being punished for a reflex
     * during the machine's own turn).
     * ==================================================================== */
    const keyOffs = [];
    let heldByKey = new Set();

    function padIndexOf(node) {
      let n = node;
      let guard = 0;
      while (n && guard < 6) {
        try {
          if (n.classList && n.classList.contains('g-ec-pad')) {
            const i = Number(n.getAttribute('data-pad'));
            return Number.isFinite(i) ? i : -1;
          }
        } catch (e) { /* ignore */ }
        n = n.parentNode;
        guard += 1;
      }
      return -1;
    }
    function onPadDown(e) {
      const i = padIndexOf(e && (e.currentTarget || e.target));
      if (i < 0) return;
      try { if (e && typeof e.preventDefault === 'function') e.preventDefault(); } catch (err) { /* noop */ }
      press(i, 'tap');
    }
    function onPadUp(e) {
      const i = padIndexOf(e && (e.currentTarget || e.target));
      if (i < 0) return;
      releaseVisual(i);
    }
    /** A press that is not a sequence step still pays a tick (Deck IV: a verb
     *  with no sensory echo is a broken slot handle) - but it moves nothing. */
    function press(i, how) {
      if (dead || paused || ended) return;
      /* THE FLASH rides the ONE press funnel, refused taps included: on touch
       * the press IS the hover, and even a locked tap during LISTEN answers by
       * showing you what that pad says (the tell you were owed anyway). */
      flashWord(i);
      if (!inputOpen) {
        padState(i, 'pressed', reduced ? 90 : 130);
        tone(PAD_SFX, 0.16, pitchFor(i, 0));
        bumpRefused();
        return;
      }
      commit(i, how);
    }
    function releaseVisual(i) {
      const pad = padEls[i];
      if (!pad) return;
      if (pad.getAttribute('data-state') === 'pressed' && !padTimers.has(i)) {
        pad.setAttribute('data-state', 'idle');
      }
    }

    function bindInput() {
      /* SYNTHESIS #7: never a raw key - the shell owns the binding, the
       * conflict check and the panic key. The ':up' twin only clears the
       * pressed LOOK, so a held key cannot double-commit. */
      for (let i = 0; i < PLAYTEST.PADS; i++) {
        const verb = 'pad' + (i + 1);
        const idx = i;
        try {
          keyOffs.push(ctx.keys.on(verb, () => {
            if (heldByKey.has(idx)) return;
            heldByKey.add(idx);
            press(idx, 'key');
          }));
          keyOffs.push(ctx.keys.on(verb + ':up', () => {
            heldByKey.delete(idx);
            releaseVisual(idx);
          }));
        } catch (e) { say('keybind ' + verb + ' refused: ' + ((e && e.message) || e)); }
      }
    }
    function unbindInput() {
      for (const off of keyOffs.splice(0)) { try { off(); } catch (e) { /* noop */ } }
      heldByKey = new Set();
      for (const pad of padEls) {
        if (!pad) continue;
        try {
          pad.removeEventListener('pointerdown', onPadDown);
          pad.removeEventListener('pointerup', onPadUp);
          pad.removeEventListener('pointerleave', onPadUp);
          pad.removeEventListener('pointercancel', onPadUp);
        } catch (e) { /* noop */ }
      }
    }

    /* ==================================================================== *
     * THE ROUND
     * ==================================================================== */
    function dealSequence(len) {
      if (dead || ended) return;
      const want = Math.max(PLAYTEST.WARM_MIN, Math.min(PLAYTEST.MAX_LEN, Math.floor(len)));
      curLen = want;
      inEncore = false;
      if (stage) stage.removeAttribute('data-encore');
      round = buildRound({ seed, gradeTier: tier, attempt, len: want, encore: false });
      /* THE RESHUFFLE. Every sequence after the first re-deals the phrases -
       * the ENCORE deliberately does not, because it is the same melody again
       * and moving the words under it would be a different room. */
      if (sequencesDealt > 0) { roundIdx += 1; dealFaces(); }
      sequencesDealt += 1;
      expectIdx = 0;
      stepIdx = 0;
      buildSteps(round.seq.length);
      clearPads();
      paintWords();
      paintHud();
      heat();
      setPhase('play');
      /* TIER 2 TELEGRAPHS THE DECOY (SYNTHESIS #2 - the signature twist enters
       * at tier 2, announced; tier 3+ says nothing at all). */
      const telegraphed = round.decoys.some((d) => d.telegraph);
      if (telegraphed) {
        msg('ec_msg_decoy_warn', EC_LEX.ec_msg_decoy_warn);
        if (stage) stage.setAttribute('data-telegraph', '1');
      } else {
        if (stage) stage.removeAttribute('data-telegraph');
        msg('ec_msg_watch', EC_LEX.ec_msg_watch);
      }
      after(telegraphed ? (reduced ? 600 : 900) : (reduced ? 240 : 380), () => startPlayback());
    }

    /** Replay the SAME sequence at half tempo. Once per class (ruling 5). */
    function startEncore() {
      if (dead || ended) return;
      encoreArmed = false;
      encoreUsed = true;
      inEncore = true;
      round = buildRound({ seed, gradeTier: tier, attempt, len: curLen, encore: true });
      expectIdx = 0;
      stepIdx = 0;
      buildSteps(round.seq.length);
      clearPads();
      paintHud();
      setPhase('encore');
      if (stage) stage.setAttribute('data-encore', '1');
      msg('ec_msg_encore', EC_LEX.ec_msg_encore);
      deck('casino', 'encore', true);
      after(reduced ? 500 : 800, () => startPlayback());
    }

    function startPlayback() {
      if (dead || ended || !round) return;
      busy = true;
      inputOpen = false;
      stepIdx = 0;
      resetSteps();
      setPhase(inEncore ? 'encore' : 'echo');
      if (!audible && sequencesDealt <= 1) msg('ec_msg_silent', EC_LEX.ec_msg_silent);
      else if (!inEncore) msg('ec_msg_watch', EC_LEX.ec_msg_watch);
      playStep();
    }

    function playStep() {
      if (dead || ended || !round) return;
      if (stepIdx >= round.steps.length) { after(round.stepMs, () => seam()); return; }
      const step = round.steps[stepIdx];
      const i = stepIdx;
      stepIdx += 1;
      const lit = litMsFor(round.stepMs, { silent: !audible });
      if (step.decoy) {
        padState(step.pad, 'decoy', lit);
        /* THE DECOY. At the telegraphed tiers it wears its own cue and a
         * shudder so the player can learn the shape of the trap; at tier 3+ it
         * is indistinguishable from a real step - same state class, same tone,
         * same length - and only the SEQUENCE tells you it does not belong. */
        if (step.telegraph) {
          tone(DECOY_SFX, 0.3, 1);
          if (!reduced) {
            const targets = faceTargets(padEls[step.pad]);
            if (targets.length) fireSafe('glitch_swap', { targets, seconds: 0.4, sfx: false, onSwap: () => {} });
          }
        } else {
          /* At tier 3+ a decoy is indistinguishable from a real step, and that
           * includes its trigger: a pad that sounded silent would be a free tell.
           * (Level matches the real step below - always.) */
          padVoice(step.pad, 0.55, pitchFor(step.pad, ratchet));
        }
        deck('casino', 'padLit', step.pad, i, round.len);
      } else {
        padState(step.pad, 'lit', lit);
        /* .55, was .34 (2026-08-25, "no sound in Echo"): the note is the second
         * half of the SIGNAL, not a garnish - it plays at signal level. */
        padVoice(step.pad, 0.55, pitchFor(step.pad, ratchet));
        /* THE STRIP DURING LISTEN: the room fills the dots as it plays, so the
         * player can see how much of the sequence is still coming. */
        stepFillSet(step.index, 'on');
        deck('casino', 'padLit', step.pad, step.index, round.len);
      }
      after(round.stepMs, () => playStep());
    }

    /** THE INTERFERENCE BEAT: the seam between playback and input is the
     *  Distraction Engine's, and only the engine's - it never moves a hitbox. */
    function seam() {
      if (dead || ended || !round) return;
      beatSafe();
      deck('trickster', 'afterPlayback');
      deck('pressure', 'beat', 'seam');
      /* Tier 2+: sub_flash word blips, deliberately drawn from the PAD words
       * so the distraction is semantically confusable with the signal. The
       * engine owns the alpha; we only choose the text. */
      if (tier >= 2 && capsArmed()) {
        const w = padWords.filter(Boolean);
        const pick = w.length && subRoll ? w[Math.floor(subRoll('word') * w.length) % w.length] : null;
        const r = fireSafe('sub_flash', Object.assign(
          { anchor: well, variant: tier >= 3 ? 'scatter' : 'whisper' },
          pick ? { text: pick } : {},
        ));
        if (r) subFlashes += 1;
      }
      after(reduced ? PLAYTEST.SEAM_MS_REDUCED : PLAYTEST.SEAM_MS, () => openInput());
    }

    /* THE HAND-OFF. Not a state change - a BEAT. One chime that is not a pad
     * tone, one pulse across every pad, the ring sweeping bright, the banner
     * flipping. Under reduced motion the chime and the banner still land, which
     * is why neither of them is an animation. */
    function handoff() {
      if (dead || ended) return;
      tone(HANDOFF_SFX, 0.45, HANDOFF_PITCH);
      haptic(12);
      if (stage) {
        stage.setAttribute('data-handoff', '1');
        after(reduced ? PLAYTEST.HANDOFF_MS_REDUCED * 2 : PLAYTEST.HANDOFF_MS * 2, () => {
          if (stage) stage.removeAttribute('data-handoff');
        });
      }
      /* Every pad at once - unmistakably NOT a sequence step, which lights one. */
      for (let i = 0; i < padEls.length; i++) padState(i, 'lit', PLAYTEST.HANDOFF_PULSE_MS);
    }

    function openInput() {
      if (dead || ended || !round) return;
      setPhase('input');
      msg('ec_msg_input', EC_LEX.ec_msg_input);
      /* The room's dots come off and the player's go on: the strip now counts
       * what YOU have played, not what the room played. */
      resetSteps();
      handoff();
      busy = false;
      inputOpen = true;
      /* A key whose ':up' never arrived (focus loss, a suspend mid-hold) must
       * never lock its pad out of the next turn. */
      try { heldByKey.clear(); } catch (e) { heldByKey = new Set(); }
      stallMs = 0;
      lastPressAt = Date.now();
      armInputWindow();
      /* Tier 2+ pressure DURING the input turn (the dossier's ladder). Every
       * one of these is decoration over a tap surface (fireSafe / sustainSafe
       * weld clickSafe on) - Law II is never spent for a garnish. */
      if (capsArmed()) {
        if (tier >= 2 && !bubblesOn) { bubblesOn = !!sustainSafe('bubble_field', { variant: 'drift', max: 8 }); }
        if (tier >= 3 && !reduced) { sustainSafe('wash', { variant: 'pink', holdMs: 900 }); washOn = true; }
      }
    }

    let windowTimer = 0;
    let windowTick = 0;
    let windowUntil = 0;
    function armInputWindow() {
      if (windowTimer) { clearTimer(windowTimer); windowTimer = 0; }
      if (!round || !round.windowMs) return;
      windowUntil = Date.now() + round.windowMs;
      if (timerEl) {
        timerEl.hidden = false;
        try { timerEl.style.setProperty('--ec-k', '1'); } catch (e) { /* noop */ }
      }
      if (!windowTick) windowTick = every(WINDOW_TICK_MS, paintWindow);
      windowTimer = after(round.windowMs, () => {
        windowTimer = 0;
        if (!inputOpen || dead || ended) return;
        timeouts += 1;
        fail(-1, 'timeout');
      });
    }
    /** The TRUE remaining share of the window, 1 -> 0. The only writer. */
    function paintWindow() {
      if (!timerEl || !round || !round.windowMs || !inputOpen) return;
      const k = Math.max(0, Math.min(1, (windowUntil - Date.now()) / round.windowMs));
      try { timerEl.style.setProperty('--ec-k', k.toFixed(3)); } catch (e) { /* noop */ }
    }
    function disarmInputWindow() {
      if (windowTimer) { clearTimer(windowTimer); windowTimer = 0; }
      if (windowTick) { clearTimer(windowTick); windowTick = 0; }
      windowUntil = 0;
      if (timerEl) {
        timerEl.hidden = true;
        try { timerEl.style.setProperty('--ec-k', ''); } catch (e) { /* noop */ }
      }
    }

    /** ONE press against the truth. Everything here is the ledger (Law I). */
    function commit(i, how) {
      if (!round || !inputOpen) return;
      const now = Date.now();
      const dt = Math.max(0, now - lastPressAt);
      lastPressAt = now;
      stallMs = 0;
      presses += 1;
      latencySum += dt;
      latencyCount += 1;

      /* THE DECOY TRAP: a decoy at this seam, and the finger landed on it. */
      const bait = round.decoys.find((d) => d.afterIndex === expectIdx);
      if (bait && bait.pad === i) {
        decoysPresented += 1;
        decoysTaken += 1;
        padState(i, 'decoy', reduced ? 260 : 420);
        deck('casino', 'padPressed', i, false, pressStreak);
        fail(i, 'decoy');
        return;
      }

      const want = round.seq[expectIdx];
      if (want === i) {
        correctPresses += 1;
        pressStreak += 1;
        bestStreak = Math.max(bestStreak, pressStreak);
        ratchet = Math.min(PLAYTEST.RATCHET_CAP, ratchet + 1);
        padState(i, 'pressed', reduced ? 140 : 200);
        /* .65, was .42 (2026-08-25): your own note answers you at full voice. */
        padVoice(i, 0.65, pitchFor(i, ratchet));
        /* The soft tick: the note is the melody, this is the "yes". */
        tone(TICK_SFX, 0.22, 1);
        /* THE DOT the player just earned. */
        stepFillSet(expectIdx, 'on');
        haptic(8);
        deck('casino', 'padPressed', i, true, pressStreak);
        /* The decoy at this seam was PRESENTED and REFUSED. The game says so:
         * the dossier's resist-sparkle acknowledges the trap you dodged. */
        if (bait) {
          decoysPresented += 1;
          decoysResisted += 1;
          deck('casino', 'decoyResisted');
          deck('trickster', 'afterInput');
          if (decoysResisted === 1) msg('ec_msg_resisted', EC_LEX.ec_msg_resisted);
        }
        expectIdx += 1;
        bestReach = Math.max(bestReach, expectIdx);
        paintStreak();
        deck('pressure', 'setStreak', curLen, { links: pressStreak, cleared: clearStreak, heat: currentHeat });
        if (expectIdx >= round.seq.length) { clearRound(); return; }
        armInputWindow();
        deck('trickster', 'afterInput');
        return;
      }
      /* WRONG. The pad flashes RED and its face shakes - a state of its own, so
       * a still frame of the room says "that was wrong" with no sound at all. */
      padState(i, 'wrong', reduced ? 260 : 420);
      deck('casino', 'padPressed', i, false, pressStreak);
      fail(i, how === 'timeout' ? 'timeout' : 'wrong');
    }

    function clearRound() {
      if (!round) return;
      inputOpen = false;
      busy = true;
      disarmInputWindow();
      const len = round.seq.length;
      if (len > bestLen) { bestLen = len; if (bestLen > lifetimeBefore) newRecord = true; }
      sequencesCleared += 1;
      clearStreak += 1;
      paintHud();
      deck('casino', 'sequenceDone', len);
      deck('pressure', 'beat', 'clear');
      tone(CLEAR_SFX, 0.5, pitchFor(PLAYTEST.PADS - 1, ratchet));
      haptic(18);
      /* THE BANNER SAYS WHAT YOU HELD, with the number in it - the length is the
       * whole score of this class and it should not live only in a HUD chip. */
      bannerVerdict('clear', 'ec_clear', EC_LEX.ec_clear, { n: len });
      ringSweep();
      stamp('ec_stamp_clear', EC_LEX.ec_stamp_clear, false);
      msg(inEncore ? 'ec_msg_encore_clear' : 'ec_msg_clear',
        inEncore ? EC_LEX.ec_msg_encore_clear : EC_LEX.ec_msg_clear);
      rewardBeat();
      stopInputPressure();
      /* THE BREATH BETWEEN ROUNDS. `inputOpen` is false, the input window timer
       * is disarmed and the next deal is a whole hold away (950ms, 520 reduced):
       * nothing is armed, nothing is timed and a press here already costs
       * nothing. The CLEAR path only - the fail path spends its hold teaching
       * the answer with a 700ms pad reveal, and a monitor frame over a lesson is
       * a monitor frame in the way. */
      deadBeatSafe('round_gap');
      const hold = reduced ? PLAYTEST.CLEAR_HOLD_MS_REDUCED : PLAYTEST.CLEAR_HOLD_MS;
      after(hold, () => { if (!ended) dealSequence(len + 1); });
    }

    /** THE ROUND-CLEAR FLOURISH: every pad in turn, and the chime climbs with
     *  them. Purely decorative - it runs inside the clear hold and the next deal
     *  clears the pads anyway. */
    function ringSweep() {
      const stepMs = reduced ? Math.round(PLAYTEST.SWEEP_STEP_MS * 0.6) : PLAYTEST.SWEEP_STEP_MS;
      const litMs = reduced ? Math.round(PLAYTEST.SWEEP_LIT_MS * 0.6) : PLAYTEST.SWEEP_LIT_MS;
      for (let i = 0; i < padEls.length; i++) {
        const idx = i;
        after(stepMs * i, () => {
          if (ended || dead) return;
          padState(idx, 'lit', litMs);
          tone(HANDOFF_SFX, 0.22, pitchFor(idx, 0));
        });
      }
    }

    function fail(pressedPad, why) {
      if (!round) return;
      inputOpen = false;
      busy = true;
      disarmInputWindow();
      fails += 1;
      const len = round.seq.length;
      const near = isNearMiss(expectIdx, len);
      ratchet = ratchetAfterMiss(ratchet, expectIdx, len);
      pressStreak = 0;
      clearStreak = 0;
      paintStreak();
      deck('casino', 'fail', pressedPad, round.seq[expectIdx]);
      deck('pressure', 'beat', 'fail');
      deck('pressure', 'setStreak', curLen, { links: 0, cleared: 0, heat: currentHeat });
      /* THE BUZZER: low, short, and nothing else in the room sounds like it. */
      tone(FAIL_SFX, 0.2, 1);
      stopInputPressure();
      if (near) { nearMisses += 1; ceremonySafe('near_miss', {}); }
      /* THE DOT that broke it goes red, and stays red through the hold. */
      if (expectIdx >= 0 && expectIdx < stepEls.length) stepFillSet(expectIdx, 'bad');
      const late = why === 'timeout';
      /* THE BANNER + THE STAMP: two ways of saying the same thing, one in the
       * place the eye already is and one across the ring. */
      bannerVerdict('miss', late ? 'ec_late' : 'ec_miss',
        late ? EC_LEX.ec_late : EC_LEX.ec_miss);
      stamp(late ? 'ec_stamp_late' : 'ec_stamp_miss',
        late ? EC_LEX.ec_stamp_late : EC_LEX.ec_stamp_miss, true);
      msg(late ? 'ec_msg_timeout' : near ? 'ec_msg_near' : 'ec_msg_fail',
        late ? EC_LEX.ec_msg_timeout : near ? EC_LEX.ec_msg_near : EC_LEX.ec_msg_fail);
      /* THE REVEAL: the pad you needed shows itself AFTER the commit - never
       * during input (that would be a colour tell) - and it does not merely
       * light: `reveal` is its own state, held for REVEAL_MS, wearing the halo
       * and the "this one" caption, so the answer is impossible to miss. */
      const wanted = round.seq[expectIdx];
      if (wanted != null) {
        after(reduced ? 160 : 260, () => {
          padState(wanted, 'reveal', PLAYTEST.REVEAL_MS);
          padVoice(wanted, 0.26, pitchFor(wanted, 0));
        });
      }
      const hold = reduced ? PLAYTEST.FAIL_HOLD_MS_REDUCED : PLAYTEST.FAIL_HOLD_MS;
      after(hold, () => {
        if (ended || dead) return;
        /* ONE FAIL IS NOT THE CLASS. The encore is the comeback hook (once),
         * then the room simply deals a shorter one and keeps going. */
        if (encoreArmed && !inEncore) { startEncore(); return; }
        if (inEncore) {
          msg('ec_msg_encore_fail', EC_LEX.ec_msg_encore_fail);
          inEncore = false;
          if (stage) stage.removeAttribute('data-encore');
          deck('casino', 'encore', false);
        } else {
          msg('ec_msg_new', EC_LEX.ec_msg_new);
        }
        attempt += 1;                       // a fresh stream: a new melody
        dealSequence(nextLenAfterFail(len, startLen));
      });
    }

    /** THE STAMP, over the middle of the ring. The shell owns the node and the
     *  engine gets first refusal; all this adds is where it lands and, for a
     *  miss, the one colour the shell's own floor has no word for (it knows
     *  `pink`, and the engine's `bad` tone never reaches the CSS version). */
    function stamp(key, fallback, bad) {
      try {
        const node = ctx.ceremonies.stamp({
          text: t(key, fallback),
          tone: bad ? 'bad' : 'pink',
          target: stampWell || ring,
        });
        if (bad && node && node.classList) node.classList.add('g-ec-stamp-bad');
        return node;
      } catch (e) { return null; }   // a ceremony must never be the thing that fails
    }

    /** The variable-ratio canon on a clear (the engine's, with a seeded local
     *  fallback so a null engine still paces the class). */
    function rewardBeat() {
      let r = null;
      try {
        if (ctx.engine && typeof ctx.engine.rewardRoll === 'function') r = ctx.engine.rewardRoll({}) || null;
      } catch (e) { r = null; }
      if (!r && rollLocal) r = rollLocal();
      if (!r || !r.fire) return;
      if (r.jackpot) {
        jackpots += 1;
        ceremonySafe('jackpot', {});
        if (capsArmed()) fireSafe('flash_burst', { variant: 'scatter' });
      } else {
        if (capsArmed()) fireSafe('gif_burst', { variant: 'single' });
      }
    }

    function stopInputPressure() {
      if (bubblesOn) { stopSafe('bubble_field'); bubblesOn = false; }
      /* NEVER stop('wash') mid-class (trap 33): a held wash is stepped DOWN by
       * re-triggering it at a lower alpha, and the deadline does the rest. */
      if (washOn && capsArmed()) { sustainSafe('wash', { variant: 'pink', alpha: 0.08, holdMs: 300 }); }
    }

    /* ==================================================================== *
     * AMBIENCE (Law III: something always breathes)
     * ==================================================================== */
    function openAmbience() {
      heat();
      if (!capsArmed()) return;
      if (!ambientOn) ambientOn = !!sustainSafe('ambient_field', { kind: tier >= 3 ? 'motes' : 'specks' });
    }
    function stopAmbience() {
      for (const k of ['ambient_field', 'bubble_field', 'wash']) stopSafe(k);
      ambientOn = false; bubblesOn = false; washOn = false;
    }

    /* ==================================================================== *
     * THE CLASS-RULES SHEET (Law IV / Deck VI) - drawn, GO-only and FREE OF
     * THE CLOCK. THE LAW, uniform across every open class (owner ruling
     * 2026-08-24): the sheet SHOWS the first time this player meets this class
     * at this grade tier and AUTO-SKIPS every later class at that tier,
     * whatever the setting says; the shell's "Skip class tutorials" switch
     * means "skip even the first showing". The memory is the GAME's
     * (gameMeta.howtoTiers), never the shell's, and no meta = the sheet shows.
     * ==================================================================== */
    function howtoSeenTiers() {
      try {
        const m = (ctx.store && typeof ctx.store.gameMeta === 'function')
          ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
        return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
      } catch (e) { return []; }
    }
    function rememberHowto() {
      try {
        const list = howtoSeenTiers();
        if (list.indexOf(tier) >= 0) return;
        list.push(tier);
        if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
          ctx.store.mergeGameMeta(GAME_KEY, { howtoTiers: list });
        }
      } catch (e) { /* best effort - the sheet just shows again next time */ }
    }
    function hideHowto() {
      if (!howtoEl) return;
      try { howtoEl.remove(); } catch (e) { /* noop */ }
      howtoEl = null;
    }
    function howto(onDone) {
      const seen = howtoSeenTiers();
      const skip = (ctx.dev === true && spec && spec.devSkipHowto === true)
        || ctx.hideTutorial === true
        || seen.indexOf(tier) >= 0;
      if (skip) { onDone(); return; }
      howtoEl = el('div', 'g-ec-howto');
      howtoEl.setAttribute('role', 'dialog');
      howtoEl.appendChild(el('h3', 'g-ec-howto-title', t('ec_howto_title', EC_LEX.ec_howto_title)));
      const rows = el('div', 'g-ec-howto-rows');
      const drawRow = (art, key, fallback) => {
        const row = el('div', 'g-ec-howto-row');
        const cell = el('span', 'g-ec-howto-art');
        cell.setAttribute('data-art', art);
        cell.setAttribute('aria-hidden', 'true');
        row.appendChild(cell);
        row.appendChild(el('p', 'g-ec-howto-line', t(key, fallback)));
        rows.appendChild(row);
      };
      drawRow('watch', 'ec_howto_watch', EC_LEX.ec_howto_watch);
      drawRow('repeat', 'ec_howto_repeat', EC_LEX.ec_howto_repeat);
      /* The decoy line only appears at the tiers that deal decoys - a rule
       * sheet that explains a mechanic the class cannot show is noise. */
      if ((PLAYTEST.DECOY_MAX[tier] || 0) > 0) drawRow('decoy', 'ec_howto_decoy', EC_LEX.ec_howto_decoy);
      howtoEl.appendChild(rows);
      const go = el('button', 'g-ec-howto-go', t('ec_howto_go', EC_LEX.ec_howto_go));
      go.setAttribute('type', 'button');
      try { go.type = 'button'; } catch (e) { /* the double has no button semantics */ }
      let done = false;
      go.addEventListener('click', () => {
        if (done || dead) return;
        done = true;
        /* THE START PRESS (W2 chrome). This one button both dismisses the rules
         * sheet and starts the class, so it gets the school's start cue and NOT
         * a second page-turn `slide` on top of it - the sheet is one page and
         * there is no page to turn. */
        tone('lift', 0.5, 1);
        rememberHowto();
        hideHowto();
        onDone();
      });
      howtoEl.appendChild(go);
      stage.appendChild(howtoEl);
      try { go.focus(); } catch (e) { /* noop */ }
    }

    /* ==================================================================== *
     * THE CLOCK + THE BELL
     * ==================================================================== */
    function startClock() {
      lastTick = Date.now();
      clockId = every(250, () => {
        if (ended) return;
        const now = Date.now();
        const dt = now - lastTick;
        lastTick = now;
        elapsedMs += dt / Math.max(0.0001, timeScale);
        if (clockChip) clockChip.textContent = clockText();
        const left = secLeft();
        if (!bellOn && left <= PLAYTEST.BELL_WARN_SEC && elapsedMs < budgetMs) {
          bellOn = true;
          deck('casino', 'bell', true);
          deck('pressure', 'beat', 'bell');
          msg('ec_msg_bell_warn', EC_LEX.ec_msg_bell_warn);
          tone('sting', 0.4, 1);
        }
        if (elapsedMs >= budgetMs) { stopClock(); run(bell); }
      });
    }
    function stopClock() { if (clockId) { clearTimer(clockId); clockId = 0; } }

    function bell() {
      if (ended || dead) return;
      finish();
    }

    /* ==================================================================== *
     * THE END - endClass exactly once, and never with a grade of our own.
     * ==================================================================== */
    function finish() {
      if (ended) return;
      ended = true;
      inputOpen = false;
      busy = true;
      disarmInputWindow();
      stopClock();
      clearPads();
      stopAmbience();
      setPhase('ended');
      deck('casino', 'dimOut');
      deck('pressure', 'stop');
      deck('trickster', 'stop');
      try { if (ctx.engine) ctx.engine.setHeat(0); } catch (e) { /* noop */ }

      const meanLatencyMs = latencyCount > 0 ? (latencySum / latencyCount) : 0;
      const stepMs = stepMsFor(tier, 1);
      const graded = compositeFor({
        gradeTier: tier,
        bestLen,
        correct: correctPresses,
        presses,
        meanLatencyMs,
        stepMs,
        decoysPresented,
        decoysResisted,
      });
      const gates = hardGates(encoreUsed);
      const fx = flavorXp(bestLen, tier);

      const lifetimeAfter = Math.max(lifetimeBefore, bestLen);
      try {
        if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
          const prior = (typeof ctx.store.gameMeta === 'function' ? (ctx.store.gameMeta(GAME_KEY) || {}) : {});
          ctx.store.mergeGameMeta(GAME_KEY, {
            bestLen: lifetimeAfter,
            plays: Math.max(0, Number(prior.plays) || 0) + 1,
            lastLen: bestLen,
            lastSeed: seed,
            lastPlayedAt: Date.now(),
            decoysResisted: Math.max(0, Number(prior.decoysResisted) || 0) + decoysResisted,
            sequencesCleared: Math.max(0, Number(prior.sequencesCleared) || 0) + sequencesCleared,
          });
        }
      } catch (e) { say('meta merge failed (the class still grades): ' + ((e && e.message) || e)); }

      renderEnd(graded, meanLatencyMs, stepMs, lifetimeAfter);

      const report = { metrics: { composite: graded.composite }, hardGates: gates, flavorXp: fx };
      lastReport = Object.assign({}, report, {
        inputs: {
          tier, seed, retake, startLen, bestLen, bestReach, lifetimeBefore, lifetimeAfter,
          sequencesDealt, sequencesCleared, fails, timeouts, presses, correctPresses,
          bestStreak, decoysPresented, decoysResisted, decoysTaken,
          meanLatencyMs, stepMs, encoreUsed, elapsedMs, terms: graded.terms, sLen: graded.sLen,
        },
      });
      try { lastSnapshot = instance.snapshot(); } catch (e) { /* diagnostics only */ }
      say('class over: best echo ' + bestLen + ' (lifetime ' + lifetimeAfter + '), '
        + sequencesCleared + '/' + sequencesDealt + ' held, ' + correctPresses + '/' + presses + ' presses, '
        + 'decoys ' + decoysResisted + '/' + decoysPresented + (encoreUsed ? ', ENCORE' : '')
        + ' -> composite ' + graded.composite.toFixed(3));

      after(reduced ? PLAYTEST.END_HOLD_MS_REDUCED : PLAYTEST.END_HOLD_MS, () => {
        if (reported) return;
        reported = true;
        try { ctx.endClass(report); } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
      });
    }

    function renderEnd(graded, meanLatencyMs, stepMs, lifetimeAfter) {
      if (!endEl) return;
      endEl.textContent = '';
      endEl.hidden = false;
      /* THE DEBRIEF (W2 chrome). Every row is appended in THIS frame and the
       * card fades in as one object (.g-ec-end / g-ec-endin), so there is no
       * visual stagger for a blip ladder to ride: the House Book's answer to an
       * unstaggered debrief is ONE `slide`, on the same beat as the fade. */
      tone('slide', 0.35, 1);
      endEl.appendChild(el('h3', 'g-ec-end-title', t('ec_end_title', EC_LEX.ec_end_title)));
      const row = (cls, k, v) => {
        const r = el('div', 'g-ec-end-row' + (cls ? ' ' + cls : ''));
        r.appendChild(el('span', 'g-ec-end-k', k));
        r.appendChild(el('span', 'g-ec-end-v', v));
        endEl.appendChild(r);
        return r;
      };
      const best = row('g-ec-end-best', t('ec_end_best', EC_LEX.ec_end_best), String(bestLen));
      best.setAttribute('data-len', String(bestLen));
      row('', t('ec_end_sequences', EC_LEX.ec_end_sequences), sequencesCleared + ' / ' + sequencesDealt);
      row('', t('ec_end_accuracy', EC_LEX.ec_end_accuracy),
        Math.round((presses > 0 ? correctPresses / presses : 0) * 100) + '%');
      row('', t('ec_end_tempo', EC_LEX.ec_end_tempo), Math.round(graded.terms.tempo * 100) + '%');
      row('', t('ec_end_streak', EC_LEX.ec_end_streak), String(bestStreak));
      if (decoysPresented > 0) {
        row('', t('ec_end_decoys', EC_LEX.ec_end_decoys), decoysResisted + ' / ' + decoysPresented);
      }
      if (encoreUsed) {
        row('g-ec-end-encore', t('ec_end_encore', EC_LEX.ec_end_encore), t('ec_end_yes', EC_LEX.ec_end_yes));
      }
      const tail = el('div', 'g-ec-end-tail');
      tail.setAttribute('data-len', String(lifetimeAfter));
      if (newRecord) tail.appendChild(el('span', 'g-ec-end-record', t('ec_end_record', EC_LEX.ec_end_record)));
      tail.appendChild(el('p', 'g-ec-end-line', t('ec_end_line', EC_LEX.ec_end_line)));
      endEl.appendChild(tail);
    }

    /* ---- assets (never block a draw) ------------------------------------ */
    function claimAssets() {
      Promise.resolve()
        .then(() => ctx.assets.claim({ loops: 6, targets: 0, stills: 6, canvasSafe: false }))
        .then((p) => {
          if (dead || !p || typeof p.next !== 'function') return;
          pool = p;
          run(dressFaces);
        })
        .catch((e) => say('asset claim failed - plain pad faces: ' + ((e && e.message) || e)));
    }

    /* ==================================================================== *
     * THE MODULE INSTANCE
     * ==================================================================== */
    const instance = {
      start(classSpec) {
        spec = classSpec || { gradeTier: 1, seed: GAME_KEY + '|none', timeBudgetSec: 120 };
        tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        seed = String(spec.seed == null ? GAME_KEY : spec.seed);
        budgetMs = Math.max(20000, (Number(spec.timeBudgetSec) || 120) * 1000);
        retake = !!spec.retake;
        reduced = probeReduced(ctx);
        audible = ctx.audioAudible !== false;
        attempt = 0;
        encoreArmed = true;
        encoreUsed = false;
        inEncore = false;
        ratchet = 0;
        newRecord = false;
        faceUrls.clear();
        clipsFired = 0;
        faceKind = (reduced || motionLevelOf() <= 1) ? 'still' : 'loop';

        try {
          lifetimeBefore = Math.max(0, Math.floor(Number((ctx.store.gameMeta(GAME_KEY) || {}).bestLen) || 0));
        } catch (e) { lifetimeBefore = 0; }
        startLen = warmStartLen(lifetimeBefore, tier);
        curLen = startLen;

        subRoll = makeTaggedRoll(seed + '|ec-sub');
        /* The clip roll is its own tag namespace, so adding a roll anywhere
         * else can never shift which hits whisper (Law V, append-only). */
        clipRoll = makeTaggedRoll(seed + '|ec-clip');
        clipBeat = 0;
        clipUntil = 0;
        clipsRolledOff = 0;
        clipsSkipped = 0;
        fittedPx = 0;
        rollLocal = (() => {
          const roll = makeTaggedRoll(seed + '|ec-vr');
          return () => {
            const base = 0.30 + 0.30 * currentHeat;
            const chance = Math.min(1, base + Math.min(PLAYTEST.STREAK_CAP, clearStreak) * 0.03);
            const r = roll('fire');
            const fire = r < chance;
            return { fire, jackpot: fire && roll('jack') >= 0.85, nearMiss: !fire && r < chance + 0.08 };
          };
        })();

        try { injectEchoStyle(); } catch (e) { say('style inject failed (class unaffected): ' + ((e && e.message) || e)); }
        buildDom();
        assignWords();
        paintHud();

        const capsOk = capsArmed();
        try {
          casino = createEcCasino({
            seed, tier, stage, board: ring, ring, hud, backdrop,
            timers: deckTimers, reduced, capsOk, t, engine: deckEngine, assets: deckAssets,
            padEl: (i) => padEls[i] || null,
            pads: () => padEls.slice(),
            padCount: PLAYTEST.PADS,
            log: say,
          }) || null;
        } catch (e) { casino = null; say('casino refused: ' + ((e && e.message) || e)); }
        try {
          trickster = createEcTrickster({
            seed, tier, timers: deckTimers, reduced, capsOk,
            stage, board: ring, hud, backdrop, engine: deckEngine, assets: deckAssets,
            budgetSec: Math.round(budgetMs / 1000),
            coarse: !!(ctx.platform && ctx.platform.isTouch),
            isHalted: () => dead || paused || ended || busy,
            /* READ-ONLY views of the truth. A deck may READ the ledger (the
             * Ghost Cursor lures to a pad ADJACENT to the due one, which is
             * only a lie worth telling if it knows the truth); it may never
             * write one. Law I is kept because nothing here is a setter. */
            phase: () => phase(),
            nextPad: () => ((inputOpen && round && round.seq[expectIdx] != null) ? round.seq[expectIdx] : -1),
            wordsOn: () => faceMode === 'words',
            t,
            stats: () => ({
              len: curLen, best: Math.max(bestLen, lifetimeBefore), streak: pressStreak,
              secLeft: secLeft(), sequences: sequencesDealt, expect: expectIdx, inputOpen,
            }),
            chipEl: (which) => (which === 'clock' ? clockChip
              : which === 'best' ? bestChip
                : which === 'streak' ? streakChip
                  : which === 'ring' ? timerEl : lenChip),
            chipText,
            /* THE LIE TARGET (Unreliable Label): a pad's WORD may briefly wear
             * another pad's word. The GLYPH is the truth and the deck is never
             * handed it; paintWords() restores the truth on every deal. */
            padEl: (i) => padEls[i] || null,
            pads: () => padEls.slice(),
            wordEl: (i) => wordNodeOf(padEls[i] || null),
            wordText: padWordText,
            restoreWords: paintWords,
            ring,
            /* THE CUE ROAD (W2 sec 2). The deck never gets the engine - it gets
             * this class's own clamped helper, so every cue it asks for lands
             * under the tier's audio ceiling exactly like the pads' own tones. */
            cue: (name, level, extra) => tone(name, level, null, extra),
            announce: (text, ms) => {
              if (!msgEl || !text) return;
              msgEl.textContent = String(text);
              const mine = msgEl.textContent;
              deckTimers.after(Math.max(400, Number(ms) || 1600), () => {
                if (msgEl && msgEl.textContent === mine) msgEl.textContent = '';
              });
            },
            log: say,
          }) || null;
        } catch (e) { trickster = null; say('trickster refused: ' + ((e && e.message) || e)); }
        try {
          pressure = createEcPressure({
            seed,
            gradeTier: tier,
            tier,
            reduced,
            motionLevel: motionLevelOf(),
            stage,
            ring,
            backdrop,
            /* CHROME ONLY (Law I/II): the tremor rides the HUD and the stage
             * frame, never a pad - a pad that moves is a moved hitbox. */
            chrome: [hud, lenChip, clockChip, streakChip, bestChip].filter(Boolean),
            hud: { len: lenChip, clock: clockChip, streak: streakChip, best: bestChip },
            engine: deckEngine,
            assets: deckAssets,
            timers: deckTimers,
            capsOk: capsArmed,
            log: say,
          }) || null;
        } catch (e) { pressure = null; say('pressure refused: ' + ((e && e.message) || e)); }

        bindInput();
        claimAssets();
        /* THE FIT has to run once the pads have a real width, and again whenever
         * the window changes it. Law VI: the listener comes off in destroy(). */
        scheduleFit();
        after(220, () => scheduleFit());
        try {
          if (typeof window !== 'undefined' && typeof window.addEventListener === 'function') {
            onResize = () => scheduleFit();
            window.addEventListener('resize', onResize);
          }
        } catch (e) { onResize = null; }

        every(PLAYTEST.STALL_TICK_MS, () => {
          if (ended || !inputOpen) return;
          stallMs += PLAYTEST.STALL_TICK_MS;
          if (stallMs >= PLAYTEST.STALL_MS) deck('trickster', 'stalled', stallMs);
        });

        msg('ec_brief', EC_LEX.ec_brief);
        howto(() => {
          if (dead || ended) return;
          /* THE CLOCK STARTS AT GO AND NOWHERE ELSE (owner ruling 2026-08-24).
           * It used to be armed above, beside bindInput/claimAssets, which
           * charged the player for reading the rules sheet - the one thing the
           * class asks them to read. The BRIEF beat below IS on the clock: it
           * is a game beat, not the sheet, exactly as Instant Recall's is. */
          startClock();
          after(reduced ? PLAYTEST.BRIEF_MS_REDUCED : PLAYTEST.BRIEF_MS, () => {
            if (dead || ended) return;
            deck('casino', 'start');
            deck('pressure', 'start');
            deck('trickster', 'start');
            openAmbience();
            busy = false;
            dealSequence(startLen);
          });
        });

        liveClass = instance;
        lastReport = null;
        lastSnapshot = null;
        say('tier ' + tier + ', ' + alphabetFor(tier) + ' of ' + PLAYTEST.PADS + ' pads in play, warm start '
          + startLen + ' (lifetime best ' + lifetimeBefore + '), budget ' + Math.round(budgetMs / 1000) + 's'
          + (reduced ? ', reduced' : '') + (audible ? '' : ', SILENT (visual tells)')
          + (retake ? ', RETAKE' : '') + ', faces ' + faceMode);
      },

      pause() {
        if (paused) return;
        paused = true;
        deck('pressure', 'pause');
        deck('trickster', 'pause');
        deck('casino', 'pause');
        if (stage) stage.classList.add('suspended');
      },

      resume() {
        if (!paused) return;
        paused = false;
        if (stage) stage.classList.remove('suspended');
        deck('pressure', 'resume');
        deck('trickster', 'resume');
        deck('casino', 'resume');
        lastTick = Date.now();
        lastPressAt = Date.now();
        const q = deferred.splice(0);
        for (const fn of q) run(fn);
      },

      /** The shell owns the overlay and the engine's suspend; we just freeze. */
      suspend(on) { if (on) instance.pause(); else instance.resume(); },

      destroy() {
        dead = true;
        stopClock();
        disarmInputWindow();
        clearTimers();
        stopAmbience();
        unbindInput();
        hideHowto();
        try { if (trickster) trickster.destroy(); } catch (e) { /* noop */ }
        trickster = null;
        try { if (casino) casino.destroy(); } catch (e) { /* noop */ }
        casino = null;
        try { if (pressure) pressure.destroy(); } catch (e) { /* noop */ }
        pressure = null;
        if (pool && typeof pool.release === 'function') { try { pool.release(); } catch (e) { /* noop */ } }
        pool = null;
        try {
          if (onResize && typeof window !== 'undefined' && typeof window.removeEventListener === 'function') {
            window.removeEventListener('resize', onResize);
          }
        } catch (e) { /* noop */ }
        onResize = null;
        fitPending = 0;
        padTimers.clear();
        flashTimers.clear();
        flashOn.clear();
        padEls.length = 0;
        stepEls.length = 0;
        stepFill.length = 0;
        padTriggers.length = 0;
        bannerEl = null; bannerTextEl = null; stepsEl = null; stampWell = null;
        rulerEl = null; tokenW100 = 0;
        timerEl = null;
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
        if (liveClass === instance) liveClass = null;
      },

      /* -------- test / diagnostics seams (never read by the shell) -------- */
      /** Press a pad exactly as the finger would. */
      press(i) { press(i, 'tap'); },
      /** The TRUE chip text - what the trickster restores after a lie. */
      chipText(which) { return chipText(which); },
      /** The live round (the suite asserts the dealt plan against sequence.js). */
      round() { return round; },
      /** End the class as the bell would (the suite never waits the budget). */
      ringBell() { run(bell); },
      /** Deal the NEXT round's faces, as a cleared sequence would. Diagnostics
       *  only - the shell never calls it; the suite uses it to walk a pool
       *  bigger than the ring without playing every round in real time. */
      dealRound() { roundIdx += 1; run(dealFaces); return padWords.slice(); },
      /** The pure fit search, wired to a caller-supplied measurement. The
       *  ruler is re-read first so a seam call answers with the same numbers
       *  the real search would use. */
      fitAt(px) { measureTokens(); return ringFitsAt(px); },

      snapshot() {
        return {
          tier, seed, retake, reduced, audible, faceMode,
          startLen, curLen, attempt, expectIdx, stepIdx, inputOpen, busy, paused, dead, ended, reported,
          phase: phase(),
          encoreArmed, encoreUsed, inEncore, ratchet,
          bestLen, bestReach, lifetimeBefore,
          sequencesDealt, sequencesCleared, fails, timeouts,
          presses, correctPresses, pressStreak, bestStreak, clearStreak,
          decoysPresented, decoysResisted, decoysTaken,
          latencySum, latencyCount, jackpots, nearMisses, subFlashes,
          currentHeat, bellOn, elapsedMs, budgetMs, secLeft: secLeft(),
          padStates: padEls.map((p) => (p ? p.getAttribute('data-state') : null)),
          padWords: padWords.slice(),
          /* The BINDING, as the suite reads it: text + whether a clip exists. */
          padTriggers: padTriggers.map((x) => (x ? { text: x.text, audio: !!x.audio } : null)),
          veils: padEls.map((p) => (p ? p.getAttribute('data-veil') : null)),
          roundIdx,
          poolSize: facePool.length,
          dealCycle,
          dealCursor,
          fittedPx,
          clipsFired,
          clipsRolledOff,
          clipsSkipped,
          clipBeat,
          banner: bannerEl ? bannerEl.getAttribute('data-p') : null,
          bannerText: bannerTextEl ? bannerTextEl.textContent : null,
          steps: stepFill.slice(),
          faces: faceUrls.size,
          round: round ? {
            len: round.len, seq: round.seq.slice(), decoys: round.decoys.slice(),
            steps: round.steps.length, stepMs: round.stepMs, windowMs: round.windowMs, encore: round.encore,
          } : null,
          howtoUp: !!howtoEl,
          stage, ring, hud, well, msgEl, endEl, lenChip, clockChip, streakChip, bestChip,
          bannerEl, stepsEl, stampWell,
          pads: padEls.slice(),
          casino: casino && typeof casino.diagnostics === 'function' ? (() => { try { return casino.diagnostics(); } catch (e) { return null; } })() : null,
          trickster: trickster && typeof trickster.diagnostics === 'function' ? (() => { try { return trickster.diagnostics(); } catch (e) { return null; } })() : null,
          pressure: pressure && typeof pressure.diagnostics === 'function' ? (() => { try { return pressure.diagnostics(); } catch (e) { return null; } })() : null,
        };
      },
    };

    function motionLevelOf() {
      try { const v = ctx.motion && ctx.motion.motionLevel; return Number.isFinite(Number(v)) ? Number(v) : 2; }
      catch (e) { return 2; }
    }

    return instance;
  },

  /** The live class's state, or null. Never read by the shell. */
  diagnostics() { return liveClass ? liveClass.snapshot() : null; },

  /** The last report handed to endClass (survives teardown). Diagnostics only. */
  get lastReport() { return lastReport; },

  /** The final snapshot of the last class (survives teardown). Diagnostics only. */
  get lastSnapshot() { return lastSnapshot; },

  setTimeScale,
};
