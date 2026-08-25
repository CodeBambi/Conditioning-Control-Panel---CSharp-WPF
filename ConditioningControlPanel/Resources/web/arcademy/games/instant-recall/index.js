/* ============================================================================
 * games/instant-recall/index.js - INSTANT RECALL, "the vigil" (family: recall).
 *
 * One class is ONE continuous 180-second WALL. A full-bleed mosaic of the
 * player's own media fills the stage and every tile keeps changing itself on
 * its own beat (the FYP look, the Just Drop backdrop); CCP's own classic
 * effects fire over it denser and denser - and then, without warning,
 * everything FREEZES and a quiz card asks what just happened. There is nothing
 * to memorise, only a present to inhabit: the stop can land at any moment and
 * it only ever asks about the last thing that happened, so sustained attention
 * is the only strategy.
 *
 * THE VIGIL LOOP:
 *   the wall -> effect emissions on the density sawtooth, every one of them
 *               written to the LEDGER (the truth tail) with what the engine
 *               ACTUALLY did. THE WALL'S OWN SWAPS ARE NEVER LEDGER ENTRIES.
 *   (five of those a minute now - vigil.js THE CADENCE, which is a RATE and
 *    not a count: 180s deals 14-16 stops at every tier. The gap between two
 *    stops is DERIVED from the freeze it has to outlast, so however long the
 *    player takes there are always >= 4s of live wall and >= 2 fresh ledger
 *    entries before the next freeze: a question always has something new.)
 *   stop     -> the wall freezes MID-SWAP (the class clock keeps running), quiz
 *               card, 6s/5s/4s window by tier, answer by tap or number key
 *   verdict  -> the truth replay proves it ("it really did flash that"), then
 *   resume   -> the wall RESHUFFLES (every tile turns over inside 1.5s),
 *               density relaxed ONE band, never back to the floor
 *   FINAL    -> the seeded schedule ALWAYS puts a stop inside the last 15s, so
 *               the class ends on a quiz and never on a fade-out.
 *
 * THE EFFECT POOL (owner ruling 2026-08-23). A question may only ever be about
 * an effect a CCP user knows BY NAME from the app's own tabs - Flash image,
 * Subliminal, Whisper, Corner GIF, Fullscreen GIF, Cascade, Bubbles, Spiral,
 * Pink Filter, Brain Drain. Ten of them, four unlocked at Year 1 and all ten by
 * Year 4 (vigil.js EFFECT_POOL). Scanlines, glitch swaps, row drift and ambient
 * grain are NOT triggers and can no longer be an option or a ledger entry; the
 * decks may still wear them as weather. Two emissions never START inside 700ms
 * of one another (vigil.js MIN_SEPARATION_MS), so "what just happened" always
 * has exactly one honest answer.
 *
 * ---------------------------------------------------------------------------
 * LAWS THIS FILE KEEPS
 *   I    THE LEDGER IS THE TRUTH. Every question is instantiated from the tail
 *        of `ledger`, and every ledger entry is written HERE from the engine's
 *        own return value - never from what we asked for. The decks may lie on
 *        a chip face or an option label; the truth is repainted and the answer
 *        key never comes from a deck. The wall's own maintenance (a tile swap,
 *        a reshuffle, the frame governor, the density band) never writes an
 *        entry, so shedding a gif seat under a frame lock cannot change an
 *        answer.
 *   II   INPUT HONEST. The wall is decoration end to end (every burst over it
 *        is welded clickSafe); the only real hitboxes in the class are the
 *        option buttons, the how-to GO button, and the audition button inside a
 *        sting option - which never selects.
 *   III  NEVER STILL. The wall turns over the whole class - at any moment some
 *        tile is mid-swap; the only still frame is the freeze, and the freeze
 *        IS the mechanic.
 *   IV   IMAGES OVER TEXT. The class-rules sheet is drawn (style.js), is
 *        dismissed by GO only, shows ONCE per grade tier and is FREE OF THE
 *        CLOCK - `ctx.hideTutorial` skips even that first showing, and
 *        startClock() runs inside the GO callback.
 *   V    SEEDED. vigil.js deals the whole show off the class seed before a
 *        frame renders. The only Math.random in this game is the frame
 *        governor's (montage.js), which never consumes the seeded stream.
 *   VI   EXITS SACRED. pause/resume/suspend/destroy follow Deja Vu's discipline
 *        (a pause-aware timer registry, nothing survives destroy); the answer
 *        window is an ANSWER, never a lock - it times out, it can be voided by
 *        the escape guard, and reduced motion / a zeroed caps vector disarm the
 *        decks and the plants.
 *   VII  LEXICON. Every visible string is ctx.lexicon(key, fallback) over
 *        lex.js IR_LEX. No AI, anywhere.
 *
 * WHAT THIS FILE DOES NOT OWN: grades (core/grades.js via ctx.endClass), XP
 * (C#), the tier (registry + meta), effect strengths (the engine's CEILING
 * RULE), the whole look (style.js), the lighting (casino.js), the lies
 * (trickster.js) and the CCP-effects ladder (pressure.js).
 *
 * ---------------------------------------------------------------------------
 * THE CREATIVE SEAM. style.js / casino.js / trickster.js / pressure.js belong
 * to the parallel creative agent. They are loaded with a DYNAMIC import +
 * Promise.allSettled (the shell's own loadOptional posture for engine/provider)
 * so this module is importable before they land and the class still runs -
 * silent - if one of them throws. Every deck method is called through `deck()`,
 * which is null-safe and try/catch'd.
 *
 *   injectInstantRecallStyle()
 *   createIrCasino({ seed, tier, stage, montage, hud, backdrop, stopEl, timers,
 *                    reduced, capsOk, t, engine, log })
 *      start stop destroy setHeat diagnostics
 *      layoutChange(kind) densityPeak() stopBeat(n, announced)
 *      answer({correct, latencyMs, streak}) plantResisted() bell(on) dimOut()
 *   createIrTrickster({ seed, tier, stopEl, timers, reduced, capsOk, isHalted, t,
 *                       stats, chipEl, chipText, optEls, optText, windowLeft,
 *                       announce, log })
 *      start stop destroy diagnostics  afterStop() afterAnswer() stalled(ms)
 *   createIrPressure({ seed, gradeTier, reduced, motionLevel, stage, montage,
 *                      chrome, hud, engine, assets, timers, capsOk, log })
 *      start stop destroy setHeat diagnostics setProgress(p01) setStreak(n)
 *      beat(kind)  - every emission kind, plus 'hit' / 'miss' on a commit and
 *                    the two structural beats 'stop' and 'layout'
 *      pause() resume()   (the class's pause-aware registry already defers timers;
 *                          these let a deck drop a held look while the class is frozen)
 *
 * NOTE on the casino's stop event: the contract names it `stop(n, announced)`
 * and the common deck method is also `stop()`. CORE prefers `stopBeat(n,
 * announced)` and only falls back to calling `stop(n, announced)` when the deck
 * exports no `stopBeat` - so a casino should export `stopBeat`.
 *
 * A NOTE ON THE DECKS AND THE POOL. A deck may never fire a POOL primitive: a
 * pink wash from pressure.js that the ledger knows nothing about would give the
 * quiz a second honest answer. pressure.js's ladder was retuned for exactly
 * that reason (it now spends only crt / ambient_field / glitch_swap on the
 * CHROME, plus its own CSS tremor) and casino.js asks its jackpot ceremony for
 * `garnish:false` so the engine's forced wash cannot fire behind CORE's back.
 *
 * ENGINE TARGETING NOTE: `.g-ir-face` is the only element the engine may ever
 * be handed - style.js owns `.g-ir-tile`'s own transform. CORE targets nothing
 * on the wall today; pressure.js targets the HUD.
 * ==========================================================================*/

import { IR_LEX } from './lex.js';
import {
  buildVigil, assertPlan, densityAt, heatFor, cadenceMs, nextEmission, resolveTemplate,
  densityMultFor, seedDues, optionWeight, PLAYTEST, POOL_KEYS, POOL_BY_KEY, PULSE_MS, STINGS,
  MEDIA_TEMPLATES, CUE_KEY,
} from './vigil.js';
import { createMontage, createLedger, hideTruthNode, mediaElFor, isAnimatedUrl } from './montage.js';
import { compositeFor, hardGates, flavorXp } from './grade.js';
import { makeTaggedRoll } from '../../core/rng.js';

const GAME_KEY = 'instant_recall';

/** Where a Corner GIF pins itself (percent of the stage, the engine's own
 *  --ae-x / --ae-y placement seam). */
const CORNERS = Object.freeze({
  tl: Object.freeze({ x: 13, y: 20 }),
  tr: Object.freeze({ x: 87, y: 20 }),
  bl: Object.freeze({ x: 13, y: 80 }),
  br: Object.freeze({ x: 87, y: 80 }),
});

/** Keys that belong to the shell (or to a focused button), never to the guard. */
const CHROME_KEYS = Object.freeze(['Escape', 'Tab', 'Shift', 'Control', 'Alt', 'Meta',
  'Enter', ' ', 'CapsLock', 'F5', 'F11', 'F12']);

/** Diagnostics seams (the DV/DE precedent). The shell never reads these. */
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

/**
 * A key press that belongs to a form control is never an answer - with ONE
 * documented exception. Daily Trigger's window-keydown guard also ignores
 * BUTTON, because its letters would fight the shell's chrome. Ours cannot: the
 * answer surface IS a row of buttons and the first one holds focus, so a
 * blanket BUTTON ignore would eat every number key. The exception is therefore
 * narrow and explicit - our OWN `g-ir-*` buttons are answerable, every other
 * form target (including any button the shell or a deck owns) is not.
 */
function isFormTarget(target) {
  try {
    if (!target) return false;
    const tag = String(target.tagName || '').toUpperCase();
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
    if (target.isContentEditable) return true;
    if (tag === 'BUTTON') return !isOurs(target);
  } catch (e) { /* ignore */ }
  return false;
}
/** Is this one of the class's own controls? */
function isOurs(node) {
  try { return String((node && node.className) || '').indexOf('g-ir-') >= 0; }
  catch (e) { return false; }
}

function mmss(secLeft) {
  const s = Math.max(0, secLeft | 0);
  return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
}

/** The creative modules plus spirals.js, loaded optionally. Never a hard
 *  import: LOT B's `spirals.js` may simply not be there, and the class then
 *  runs with the shell's own class spiral and never resolves a SPIRAL card. */
function loadDecks() {
  return Promise.allSettled([
    import('./style.js'),
    import('./casino.js'),
    import('./trickster.js'),
    import('./pressure.js'),
    import('./spirals.js'),
  ]).then((r) => ({
    style: r[0].status === 'fulfilled' ? r[0].value : null,
    casino: r[1].status === 'fulfilled' ? r[1].value : null,
    trickster: r[2].status === 'fulfilled' ? r[2].value : null,
    pressure: r[3].status === 'fulfilled' ? r[3].value : null,
    spirals: r[4].status === 'fulfilled' ? r[4].value : null,
  }));
}

export default {
  key: GAME_KEY,
  family: 'recall',
  /* MEATY = THE ANCHOR SLOT, not a length. The ten-game pool re-opened web
   * CLAUDE.md trap 6: no-repeat-3 outranks the anchor preference, so TWO anchor
   * classes left roughly a third of dealt nights with no anchor at all. A third
   * closes the gap. This file's behaviour does not branch on `meaty` anywhere -
   * it is a timetable fact, not a difficulty one - and it says nothing about
   * seconds: the vigil runs 180s while Anomaly, a non-anchor class, runs 300.
   * `games/registry.js` GAME_META must mirror both fields (the parachute is
   * read for a suspended class too).
   *
   * THE BUDGET IS 180s (owner ruling, the class-length wave; it was 120). The
   * cadence is a RATE, so nothing else moved: vigil.js scales the per-120s
   * STOPS_BAND by budget and then holds it inside STOPS_PER_MIN {4.4, 5.6},
   * which deals 14-15 stops at tiers 1-2 and 15-16 at tiers 3-4, all of them
   * inside maxStopsFor()'s legal fit for 180s (15/15/16/18). */
  meaty: true,
  flagship: false,
  timeBudgetSec: 180,
  orientation: 'portrait',   // phone only; see games/registry.js ORIENTATIONS
  title: 'Instant Recall',

  manifest: {
    /* EXACTLY what the class spends, and nothing else.
     *   THE POOL (CORE, every one of them a ledger entry and a quiz option):
     *     flash_burst / sub_flash / bubble_field / wash / audio_trigger /
     *     gif_burst / gif_rain
     *   THE WEATHER (pressure.js only, never a ledger entry, never an option):
     *     ambient_field / crt / glitch_swap (the last one on the HUD, not the wall)
     * flash_burst / gif_burst are declared ONLY as clickSafe decoration over
     * the wall - fireSafe() welds that on. `row_drift` is gone with the rows. */
    effectsConsumed: [
      'flash_burst', 'sub_flash', 'bubble_field', 'wash', 'audio_trigger',
      'gif_burst', 'gif_rain',
      'ambient_field', 'crt', 'glitch_swap',
    ],
    /* The wall is stills-heavy by law (the decoder budget), so it wants a deep
     * still pool and a bounded loop pool; everything renders DOM-layer
     * (img/video tiles, never canvas), so the provider may serve remote media. */
    assetNeeds: { loops: 24, targets: 0, stills: 24, canvasSafe: false },
    boardSizes: null,
    /* Answers are tap or the number keys 1-4 on a window keydown (the Daily
     * Trigger pattern) - no verb slots, so nothing to declare or rebind. */
    keybinds: null,
    settings: [
      {
        key: 'ir_density', kind: 'enum', values: ['calm', 'standard', 'dense'], default: 'standard',
        label_key: 'ir_density', hint_key: 'ir_density_hint',
      },
    ],
    peek: false,
  },

  create(ctx) {
    const t = (key, fallback) => {
      const fb = fallback == null ? (IR_LEX[key] == null ? key : IR_LEX[key]) : fallback;
      try { const v = ctx.lexicon(key, fb); return v == null ? fb : v; } catch (e) { return fb; }
    };
    const say = (m) => { try { ctx.log('[ir] ' + m); } catch (e) { /* noop */ } };

    /* ---- lifecycle ------------------------------------------------------ */
    let dead = false;
    let paused = false;
    let ended = false;
    let reported = false;
    let halted = true;                 // emissions closed until the vigil opens

    /* ---- class state ---------------------------------------------------- */
    let spec = null;
    let seed = '';
    let tier = 1;
    let plan = null;
    let reduced = false;
    let retake = false;
    let budgetMs = 180000;
    let pool = null;
    let montage = null;
    let ledger = null;
    let qroll = null;
    let rewardLocal = null;

    let casino = null;
    let trickster = null;
    let pressure = null;
    /** LOT B's spirals.js, when it is there. `set` is the class's four spirals,
     *  `ring` the subset the emission ring walks (PLAYTEST.SPIRAL_RING). */
    let spirals = null;
    let spiralSet = { set: [], ring: [], kin: {} };
    /** THE WOVEN CLASS SPIRAL (Loom directive 2026-08-25): the shell's
     *  generated loom pool row {loom:true, id, params, href}, when one exists.
     *  Its params live in loomParamsById so a 'loom:' id can grow a thumbnail
     *  face wherever it appears - truth or decoy. */
    let loomSpiralRow = null;
    const loomParamsById = new Map();
    const isLoomId = (v) => typeof v === 'string' && v.indexOf('loom:') === 0;
    /**
     * THE WALL BOOK. One frozen `montage.snapshot()` per stop, captured AFTER
     * the freeze and the quench, capped at 16. Every WALL_* question is read
     * from the book and never from the plan or from a plant REQUEST: a plant
     * that failed is simply not in `snapshot.dups`, and the answer is whatever
     * the DOM was actually wearing at the freeze. The wall still writes no
     * ledger entry - the book is CORE's bookkeeping, not the room's.
     */
    const wallBook = [];
    /** Live one-shot handles the quench can cancel (flash / gif bursts). */
    const burstHandles = [];
    let quenchCount = 0;

    /* ---- the vigil's own bookkeeping ------------------------------------ */
    let elapsedMs = 0;
    let clockId = 0;
    let lastTick = 0;
    let bellOn = false;
    let stopIdx = 0;                   // the next stop to fire
    let stopsResolved = 0;
    let segStartMs = 0;                // when the current density segment opened
    let easedNext = false;             // comeback hook: the last stop was missed
    let band = 0;
    let currentHeat = 0;
    /** The stage has ONE mode now: the wall. Kept as a field because the ledger
     *  entry carries it and the trickster reads it off `stats()`. */
    const layout = 'wall';
    let bellArmed = -1;                // which stop has already had its warning
    let stallMs = 0;
    let lastStallFire = 0;

    /* the live stop (null between stops) */
    let live = null;

    /* results (Law I: computed here, never by a deck) */
    const results = { questions: [], stops: [] };
    let streak = 0;
    let bestRun = 0;
    let plantExposures = 0;
    let plantsResisted = 0;
    let plantsFired = 0;
    let correctedWeight = 0;
    let correctionPending = 0;         // weight a fully-correct NEXT stop restores
    let correctionUsed = false;
    let voidedStops = 0;
    let timeouts = 0;
    let emissions = 0;
    let jackpots = 0;

    /* escape guard (a frozen quiz card is never a trap) */
    let guardHits = 0;
    let guardSince = 0;

    /* ---- dom ------------------------------------------------------------ */
    let stage = null; let backdrop = null; let hud = null; let montageEl = null;
    let ledgerEl = null; let stopEl = null; let qEl = null; let optsEl = null;
    let timerEl = null; let truthEl = null; let msgEl = null; let well = null;
    let endEl = null; let howtoEl = null; let qFaceEl = null;
    let clockChip = null; let stopsChip = null; let densityChip = null;
    let payoutEl = null;
    let optEls = [];

    /* THE DEALER. One timer, one queue: every pool key carries its own due time
     * on the class clock and vigil.js `nextEmission` picks the earliest that is
     * also at least MIN_SEPARATION_MS after the previous emission. */
    const due = Object.create(null);
    const ringAt = Object.create(null);
    let dealerTimer = 0;
    let lastEmitAt = -Infinity;
    /** Live pulses: pool key -> the timer that ends it. */
    const pulses = new Map();

    /* ==================================================================== *
     * TIMERS - every step goes through run(), so a suspend freezes the class
     * mid-beat and a resume finishes it (the Deep End registry).
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
     * ENGINE - one wrapper, the input-trust law and the audio ceiling welded.
     * ==================================================================== */
    function fireSafe(kind, opts) {
      if (dead || paused || !ctx.engine) return null;
      const o = Object.assign({}, opts || {});
      if (kind === 'flash_burst' || kind === 'gif_burst') {
        o.clickSafe = true;              // decoration only over the stream
        o.clickable = false;
        delete o.onPop;
      }
      if (kind === 'audio_trigger') {
        const ceil = plan ? plan.audioCeil : 0.4;
        o.level = Math.min(ceil, o.level == null ? 0.35 : o.level);
      }
      try { return ctx.engine.fire(kind, o) || null; }
      catch (e) { say('fire(' + kind + ') failed'); return null; }
    }
    function sustainSafe(kind, opts) {
      if (dead || paused || !ctx.engine) return null;
      const o = Object.assign({}, opts || {});
      if (kind === 'bubble_field' || kind === 'gif_rain') {
        o.clickSafe = true;
        delete o.onPop;
      }
      try { return ctx.engine.sustain(kind, o) || null; } catch (e) { return null; }
    }
    function stopSafe(kind) { try { if (ctx.engine) ctx.engine.stop(kind); } catch (e) { /* noop */ } }

    /* ==================================================================== *
     * 16 THE UNLISTED FRAME - the seep's one Instant Recall special, and the
     * boldest card in its pitch: mid-stream, one montage tile is briefly the
     * campus plan, cold and lined, sliding by with everything else.
     *
     * THE DEAD MOMENT is the watch-only wall between stops: no card is up, no
     * option is armed, nothing is being timed, and by this class's own
     * discipline a stop never lands mid-swap.
     *
     * THE LEAD GUARD is ours, not the director's. The director cannot see the
     * schedule, so we refuse the beat unless the next stop is comfortably
     * further away than the frame is long - a frame still on the wall when the
     * shutter falls would be a blueprint over a tile the quiz is reading. The
     * wall clears it on `freeze(true)` as well, which is the second brace.
     *
     * THE FRAME CAN NEVER BE AN ANSWER, and that is enforced at WRITE TIME, not
     * at read time: `montage.seepFrame()` writes no ledger entry, assigns no
     * `tile.url` and adds nothing to `seen`, so the ledger tail the questions
     * are built from, the freeze snapshot they are resolved against and the
     * decoy pool they draw from are all blind to it by construction. See
     * montage.js seepFrame() for the full argument.
     * ==================================================================== */
    const SEEP_STREAM_LEAD_MS = 2000;

    function seepStream() {
      if (dead || ended || halted || live) return;
      if (!montage || !ctx.engine || typeof ctx.engine.deadBeat !== 'function') return;
      const nx = nextStop();
      const lead = nx ? nx.atMs - elapsedMs : Infinity;
      if (!(lead >= SEEP_STREAM_LEAD_MS)) return;
      try {
        ctx.engine.deadBeat('stream', {
          draw: (ms) => (montage ? montage.seepFrame(ms) : null),
        });
      } catch (e) { /* a refused frame is not an error */ }
    }

    /* ---- THE WALL, PRESENCE-CHECKED -------------------------------------
     * montage.js's snapshot / unseen / plant / highlight are LOT B's surface.
     * Every call goes through one of these, so a build without them simply
     * never deals a WALL family (`wallOk` false at plan time) and never throws.
     * -------------------------------------------------------------------- */
    function wallOk() {
      try { return !!(montage && typeof montage.snapshot === 'function'); } catch (e) { return false; }
    }
    function snapshotSafe() {
      if (!wallOk()) return null;
      try { const s = montage.snapshot(); return (s && typeof s === 'object') ? s : null; }
      catch (e) { return null; }
    }
    function unseenSafe(kind, n) {
      if (!montage || typeof montage.unseen !== 'function') return [];
      try {
        const out = montage.unseen(kind, Math.max(0, n | 0));
        return Array.isArray(out) ? out.filter((u) => typeof u === 'string' && u) : [];
      } catch (e) { return []; }
    }
    function highlightSafe(indices, on) {
      if (!montage || typeof montage.highlight !== 'function') return;
      try { montage.highlight(indices, !!on); } catch (e) { /* noop */ }
    }
    function plantWallSafe(opts) {
      if (!montage || typeof montage.plant !== 'function') return;
      try { montage.plant(opts); } catch (e) { /* noop */ }
    }
    /** A one-shot handle the quench may need to cancel. Ring of 6. */
    function keepBurst(h) {
      if (!h || typeof h.cancel !== 'function') return h;
      burstHandles.push(h);
      while (burstHandles.length > 6) burstHandles.shift();
      return h;
    }
    /** The engine, as a deck sees it: welded primitives plus a READ of the
     *  clamped channel vector (a deck spends a channel, it never raises one).
     *  `pitch` passes straight through to shell/audio.js. */
    const deckEngine = {
      fire: fireSafe,
      sustain: sustainSafe,
      stop: stopSafe,
      ceremony: (kind, opts) => {
        try { return (ctx.engine && ctx.engine.ceremony) ? ctx.engine.ceremony(kind, opts || {}) : null; }
        catch (e) { return null; }
      },
      channels: () => {
        try { return (ctx.engine && typeof ctx.engine.channels === 'function') ? ctx.engine.channels() : null; }
        catch (e) { return null; }
      },
    };
    /** The player's own media, as a deck sees it: a LIVE reader, because the
     *  pool lands async and a captured null stays null forever. */
    const deckAssets = {
      next(kind) {
        try { return (pool && typeof pool.next === 'function') ? (pool.next(kind) || null) : null; }
        catch (e) { return null; }
      },
    };
    /** bgIntensity 0 is the player's exit: read it LIVE, never a snapshot. */
    function capsArmed() { return !(ctx.caps && Number(ctx.caps.bgIntensity) === 0); }
    /** A cue through the engine, under the tier's audio ceiling, pitched by streak. */
    function tick(name, level, extra) {
      const semis = Math.min(PLAYTEST.PITCH_CAP, streak);
      const o = Object.assign({ name, level, pitch: 1 + semis * PLAYTEST.PITCH_STEP }, extra || {});
      return fireSafe('audio_trigger', o);
    }

    /* W3 P2-5: THE GOLDLEAF'S OWN NOTE. A run of two lights the wall with gold
     * grain and said nothing, so the run was visible and inaudible. The mixer
     * has no sustain (trap 108), so this is a RE-STRUCK `pad` - one soft high
     * note every GOLD_HUM_MS while the run is alive, self-limiting because it
     * re-arms only from inside itself. It is stopped at the streak break, at
     * the bell and in destroy: a hum with no owner outlives its class. */
    const GOLD_HUM_MS = 2200;
    let goldHumTimer = 0;
    function stopGoldHum() {
      if (goldHumTimer) { clearTimer(goldHumTimer); goldHumTimer = 0; }
    }
    function startGoldHum() {
      if (goldHumTimer || dead || ended) return;
      const strike = () => {
        goldHumTimer = 0;
        if (dead || ended || streak < 2) return;
        tick('pad', 0.1, { pitch: 1.4 });
        goldHumTimer = after(GOLD_HUM_MS, strike);
      };
      strike();
    }

    /** Every deck call: null-safe, try/catch'd, never able to break the class. */
    function deck(which, method, ...args) {
      const d = which === 'casino' ? casino : which === 'pressure' ? pressure : trickster;
      if (!d || typeof d[method] !== 'function') return undefined;
      try { return d[method](...args); }
      catch (e) { say(which + '.' + method + ' threw: ' + ((e && e.message) || e)); return undefined; }
    }

    /* ==================================================================== *
     * THE LEDGER - the ONE way an emission becomes truth.
     * ==================================================================== */
    function note(channel, payload, variant) {
      if (!ledger) return null;
      emissions += 1;
      return ledger.append({ t: Math.round(elapsedMs), channel, payload: payload || {}, variant, mode: layout });
    }

    /* ==================================================================== *
     * HEAT + DENSITY
     * ==================================================================== */
    function progress01() { return Math.max(0, Math.min(1, elapsedMs / Math.max(1, budgetMs))); }
    function recomputeHeat() {
      if (!plan) return;
      const h = heatFor(plan, progress01() * 0.55 + band * 0.45, streak);
      currentHeat = h;
      try { if (ctx.engine) ctx.engine.setHeat(h); } catch (e) { /* engine is optional */ }
      deck('casino', 'setHeat', h);
      deck('pressure', 'setHeat', h);
    }
    /** THE DENSITY, PUBLISHED. `--ir-density` (0..1) rides the density chip AND
     *  the stage, so a deck's meter can read it wherever it chooses to draw -
     *  it is the same number the chip's percentage renders, so the meter and
     *  the text can never disagree. Presentation only: the SCHEDULE is never
     *  meter-derived (a meter that could be read as a countdown would hand the
     *  player the one thing the vigil refuses to tell them). */
    function paintDensity() {
      const v = band.toFixed(3);
      if (densityChip && densityChip.style) densityChip.style.setProperty('--ir-density', v);
      if (stage && stage.style) stage.style.setProperty('--ir-density', v);
    }
    function recomputeBand() {
      if (!plan) return;
      const b = densityAt(plan, elapsedMs - segStartMs, stopsResolved, easedNext);
      const changed = Math.abs(b - band) > 0.02;
      band = b;
      if (montage && changed) montage.setBand(band);
      paintDensity();
      if (densityChip) densityChip.textContent = Math.round(band * 100) + '%';
      deck('pressure', 'setProgress', progress01());
      if (changed && band >= plan.densityCeil * 0.96) deck('casino', 'densityPeak');
    }

    /* ==================================================================== *
     * DOM (the contract's exact shape)
     * ==================================================================== */
    function setPhase(p) { if (stage) stage.setAttribute('data-phase', p); }
    function msg(key, fallback) { if (msgEl) msgEl.textContent = t(key, fallback); }
    function chipText(which) {
      if (which === 'clock') return mmss(Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)));
      if (which === 'stops') return stopsResolved + '/' + (plan ? plan.stopCount : 0);
      return Math.round(band * 100) + '%';
    }
    function paintHud() {
      if (clockChip) clockChip.textContent = chipText('clock');
      if (stopsChip) stopsChip.textContent = chipText('stops');
      if (densityChip) densityChip.textContent = chipText('density');
      paintDensity();
    }

    function buildDom() {
      const root = ctx.root;
      root.textContent = '';
      stage = el('div', 'g-ir-stage');
      stage.setAttribute('data-phase', 'briefing');
      stage.setAttribute('data-layout', layout);
      stage.setAttribute('data-tier', String(tier));
      if (reduced) stage.setAttribute('data-reduced', '1');

      backdrop = el('div', 'g-ir-backdrop');
      backdrop.setAttribute('aria-hidden', 'true');
      if (backdrop.style) backdrop.style.setProperty('pointer-events', 'none');
      stage.appendChild(backdrop);

      hud = el('div', 'g-ir-hud');
      clockChip = el('span', 'g-ir-chip g-ir-clock', mmss(budgetMs / 1000));
      clockChip.setAttribute('aria-label', t('ir_chip_clock', IR_LEX.ir_chip_clock));
      stopsChip = el('span', 'g-ir-chip g-ir-stops', '0/' + (plan ? plan.stopCount : 0));
      stopsChip.setAttribute('aria-label', t('ir_chip_stops', IR_LEX.ir_chip_stops));
      densityChip = el('span', 'g-ir-chip g-ir-density', '0%');
      densityChip.setAttribute('aria-label', t('ir_chip_density', IR_LEX.ir_chip_density));
      hud.appendChild(clockChip);
      hud.appendChild(stopsChip);
      hud.appendChild(densityChip);
      stage.appendChild(hud);

      montageEl = el('div', 'g-ir-montage');
      montageEl.setAttribute('aria-hidden', 'true');
      stage.appendChild(montageEl);

      /* THE TRUTH TAIL. In the DOM (so a screenshot and the suite can read it),
       * out of the accessibility tree, and never painted - inline, because this
       * file injects no stylesheet and must never write a bare display rule. */
      ledgerEl = el('div', 'g-ir-ledger');
      hideTruthNode(ledgerEl);
      stage.appendChild(ledgerEl);

      stopEl = el('div', 'g-ir-stop');
      stopEl.hidden = true;
      qEl = el('div', 'g-ir-q', '');
      optsEl = el('div', 'g-ir-opts');
      timerEl = el('div', 'g-ir-timer');
      timerEl.setAttribute('aria-hidden', 'true');
      truthEl = el('div', 'g-ir-truth');
      truthEl.hidden = true;
      stopEl.appendChild(qEl);
      stopEl.appendChild(optsEl);
      stopEl.appendChild(timerEl);
      stopEl.appendChild(truthEl);
      stage.appendChild(stopEl);

      msgEl = el('div', 'g-ir-msg', '');
      stage.appendChild(msgEl);
      well = el('div', 'g-ir-flashwell');
      well.setAttribute('aria-hidden', 'true');
      stage.appendChild(well);
      endEl = el('div', 'g-ir-end');
      endEl.hidden = true;
      stage.appendChild(endEl);

      root.appendChild(stage);
    }

    /* ==================================================================== *
     * THE CLASS-RULES SHEET (Law IV: drawn, GO-only, once per tier, and never
     * on the clock - startClock() is inside the GO callback)
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
        if (list.indexOf(tier) < 0) {
          list.push(tier);
          if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
            ctx.store.mergeGameMeta(GAME_KEY, { howtoTiers: list });
          }
        }
      } catch (e) { /* best effort - the sheet just shows again next time */ }
    }
    /**
     * The sheet: three drawn panels and a GO button. THE LAW, uniform across
     * every open class (owner ruling 2026-08-24): it SHOWS the first time this
     * player meets the vigil at this grade tier and AUTO-SKIPS every later
     * class at that tier, whatever the setting says; the shell's "Skip class
     * tutorials" switch (ctx.hideTutorial) means "skip even the first showing".
     * No meta = no memory = the sheet shows. Dismissal is the sheet's own
     * button only, and the clock is armed past GO, never over the sheet.
     */
    function howto(onDone) {
      if (ctx.hideTutorial === true || howtoSeenTiers().indexOf(tier) >= 0) { onDone(); return; }
      howtoEl = el('div', 'g-ir-howto');
      howtoEl.appendChild(el('h3', 'g-ir-howto-title', t('ir_howto_title', IR_LEX.ir_howto_title)));
      /* THREE FIGURES (Law IV, drawn): the wall / the freeze / the pick. The
       * first one carries three of the POOL'S OWN ICONS - a flash, a spiral, a
       * bubble - because the whole rework is "the effects should be the ones
       * they can recognise", and a drawn flash says that faster than a caption. */
      const cards = [
        ['wall', t('ir_howto_1', IR_LEX.ir_howto_1), ['flash', 'spiral', 'bubble']],
        ['freeze', t('ir_howto_2', IR_LEX.ir_howto_2), []],
        ['answer', t('ir_howto_3', IR_LEX.ir_howto_3), []],
      ];
      const rowEl = el('div', 'g-ir-howto-row');
      for (const [art, cap, icons] of cards) {
        const card = el('div', 'g-ir-howto-card');
        const artEl = el('div', 'g-ir-howto-art');
        artEl.setAttribute('data-art', art);
        artEl.setAttribute('aria-hidden', 'true');
        for (const ico of icons) {
          const iconEl = el('i', 'g-ir-howto-ico');
          iconEl.setAttribute('data-ico', ico);
          artEl.appendChild(iconEl);
        }
        card.appendChild(artEl);
        card.appendChild(el('p', 'g-ir-howto-cap', cap));
        rowEl.appendChild(card);
      }
      howtoEl.appendChild(rowEl);
      howtoEl.appendChild(el('p', 'g-ir-howto-note', tier === 1
        ? t('ir_howto_bell', IR_LEX.ir_howto_bell)
        : t('ir_howto_nobell', IR_LEX.ir_howto_nobell)));
      const go = el('button', 'g-ir-go', t('ir_howto_go', IR_LEX.ir_howto_go));
      go.setAttribute('type', 'button');
      let done = false;
      go.addEventListener('click', () => {
        if (done || dead) return;
        done = true;
        /* THE START PRESS (W2 chrome). This one button both dismisses the rules
         * sheet and starts the vigil, so it gets the school's start cue and NOT
         * a second page-turn `slide` over the top of it - the sheet is one
         * page and there is no page to turn. */
        tick('lift', 0.5, { pitch: 1 });
        rememberHowto();
        hideHowto();
        onDone();
      });
      howtoEl.appendChild(go);
      stage.appendChild(howtoEl);
      try { if (go.focus) go.focus(); } catch (e) { /* noop */ }
    }
    function hideHowto() {
      if (!howtoEl) return;
      try { howtoEl.remove(); } catch (e) { /* noop */ }
      howtoEl = null;
    }

    /* ==================================================================== *
     * THE WALL
     * ==================================================================== */
    /** THE RESHUFFLE: the wall's answer to "a new layout on resume". Every tile
     *  turns over inside ~1.5s, staggered. It writes NO ledger entry (Law I). */
    function reshuffleWall() {
      if (montage) montage.reshuffle();
      deck('casino', 'layoutChange', 'reshuffle');
      deck('pressure', 'beat', 'layout');
    }

    /* ==================================================================== *
     * THE EFFECT POOL - the game itself.
     *
     * ONE dealer, not ten timers. Every pool key carries its own due time on
     * the class clock; `nextEmission` picks the earliest and pushes it out
     * when it would land inside 700ms of the previous emission, so two effects
     * can never start together and "what just happened" always has exactly one
     * answer. A stop clears the dealer; a resume re-seeds it.
     * ==================================================================== */
    function chanFor(key) {
      if (!plan) return null;
      for (const ch of plan.channels) if (ch.key === key) return ch;
      return null;
    }
    function nextJitter(key) {
      const ch = chanFor(key);
      if (!ch) return 0.5;
      const i = (ringAt[key] | 0);
      return ch.jitter[i % ch.jitter.length];
    }
    function nextVariant(key) {
      const ch = chanFor(key);
      if (!ch) return '';
      const i = (ringAt[key] | 0);
      ringAt[key] = i + 1;
      return ch.variants[i % ch.variants.length];
    }
    /** The ring index the CURRENT emission is on (nextVariant already stepped
     *  it), so a key's extra rings - the spiral's, the whisper's clips - walk
     *  in lockstep with its variants instead of a beat behind. */
    function ringIndex(key) { return Math.max(0, (ringAt[key] | 0) - 1); }
    /** The shell's spiral pool (bundled + the Loom), as rows. Absent on a host
     *  that predates the seam, and then SPIRAL is simply never dealt. */
    function spiralPool() {
      const out = [];
      try {
        for (const r of (ctx.spiralPool || [])) {
          if (r && typeof r.url === 'string' && r.url) {
            out.push({ url: r.url, weight: Number.isFinite(Number(r.weight)) ? Number(r.weight) : 1 });
          }
        }
      } catch (e) { /* ignore */ }
      return out;
    }
    /** Tonight's trigger rows that actually carry a clip url. */
    function clipRows() {
      const out = [];
      try {
        for (const r of (ctx.triggers || [])) {
          if (r && typeof r.audio === 'string' && r.audio) out.push({ text: String(r.text || ''), audio: r.audio });
        }
      } catch (e) { /* ignore */ }
      return out;
    }
    /** Re-seed every pool key's due time from the current band. THE FRESH-TAIL
     *  GUARANTEE lives in vigil.js `seedDues` (pure, so the suite can walk every
     *  tier x density x band): the wall always says at least two things inside
     *  the fresh window the min gap reserves. */
    function seedDealer() {
      if (!plan) return;
      for (const key of plan.pool) if (ringAt[key] == null) ringAt[key] = 0;
      /* THE CUE. The family the NEXT stop was dealt tells us which channel has
       * to have said something by then, so its due is pulled in. It does not
       * choose the answer - the ledger still does - it only stops a dealt
       * family from being theatre. */
      const nx = nextStop();
      const want = (nx && nx.questions && nx.questions[0]) ? CUE_KEY[nx.questions[0].template] : undefined;
      const seeded = seedDues(plan.pool, elapsedMs, band, nextJitter,
        (want && plan.pool.indexOf(want) >= 0) ? want : undefined);
      for (const key of plan.pool) due[key] = seeded[key];
    }
    function armDealer() {
      if (!plan || dead || ended) return;
      if (dealerTimer) { clearTimer(dealerTimer); dealerTimer = 0; }
      /* THE QUIET. Nothing may start inside `quietFor(key)` of the next stop,
       * so "the last thing" is always something the player fully perceived
       * rather than a 50ms-old ghost the freeze cut in half. A pick of null
       * arms no timer; the resume re-seeds every due anyway. */
      const nx = nextStop();
      const pick = nextEmission(due, lastEmitAt, elapsedMs, nx ? nx.atMs : undefined);
      if (!pick) return;
      dealerTimer = after(Math.max(16, pick.waitMs), () => {
        dealerTimer = 0;
        if (!halted) {
          const fired = emitPool(pick.key);
          if (fired) lastEmitAt = elapsedMs;
        }
        due[pick.key] = elapsedMs + cadenceMs(pick.key, band, nextJitter(pick.key));
        armDealer();
      });
    }
    function clearChannels() {
      if (dealerTimer) { clearTimer(dealerTimer); dealerTimer = 0; }
      for (const id of pulses.values()) clearTimer(id);
      pulses.clear();
    }

    /** A PULSED sustain: hold it for the pool key's pulse length, then let it
     *  go. The engine's own fade carries it out - a wash is stepped DOWN by a
     *  low-alpha re-trigger, never stop('wash') (trap 33). */
    function pulse(key, endFn) {
      const ms = PULSE_MS[key] || 2400;
      const prev = pulses.get(key);
      if (prev) clearTimer(prev);
      const id = after(ms, () => { pulses.delete(key); try { endFn(); } catch (e) { /* noop */ } });
      pulses.set(key, id);
    }

    /** ONE emission, addressed by POOL KEY. Whatever the engine ACTUALLY
     *  returned is what the ledger records - a refused or capped-to-zero
     *  primitive writes nothing at all, which is exactly why a question can
     *  never desync from the show. */
    function emitPool(key) {
      const row = POOL_BY_KEY[key];
      if (!row) return null;
      const variant = nextVariant(key);
      let r = null;
      switch (key) {
        case 'flash': {
          r = keepBurst(fireSafe('flash_burst', { variant, count: Math.max(1, Math.round(1 + 3 * band)) }));
          if (r) note('flash', {}, r.variant || variant);
          break;
        }
        case 'subliminal': {
          const hasWords = wordPool().length > 0;
          /* THE HOLD (owner: "the text on the sub is pretty faded so they are
           * kinda hard"). NOT an intensity raise - the alpha is still the
           * engine's clamped channel. `holdMs` stretches the blip's PLATEAU so
           * the word sits at full alpha for half a second instead of ~170ms,
           * and style.js's plate does the rest. An engine that ignores the
           * option renders exactly as before. */
          r = fireSafe('sub_flash', {
            variant, anchor: well, image: hasWords ? false : true,
            holdMs: PLAYTEST.SUB_HOLD_MS[tier],
          });
          if (r) note('subliminal', r.text ? { word: String(r.text) } : { assetId: 'card' }, r.variant || variant);
          /* W3 P1-13, THE PILOT. Eight of the ten pool channels are silent and
           * this is the ONE that gets a mark, because a held word is what the
           * questions ask about most and the easiest thing to miss with the eye
           * on the wall. Deliberately faint (.12) and READ-ONLY: it writes NO
           * ledger entry (the sub_flash above already wrote the truth), it is
           * not an emission, and nothing may ever resolve against it. If it
           * reads as a tell in play-test, this one call is the whole feature. */
          if (r) tick('whisper', 0.12, { pitch: 1.1 });
          break;
        }
        case 'whisper': {
          /* THE WHISPER IS A REAL WHISPER NOW. When the mix carries trigger
           * clips the class plays ONE - the phrase is the content, and HEARD
           * asks what it said. With no clips it is the synthesised sting and
           * LAST_STING asks which one; the two families never both deal
           * (vigil.js templateDrops), so the card is never a guess. */
          const rows = clipRows();
          if (rows.length && ctx.audioAudible !== false) {
            const ch = chanFor('whisper');
            const walk = (ch && Array.isArray(ch.clipIdx) && ch.clipIdx.length) ? ch.clipIdx : [0];
            const row = rows[walk[ringIndex('whisper') % walk.length] % rows.length];
            r = fireSafe('audio_trigger', {
              name: 'whisper', url: row.audio, key: 'ir-whisper',
              maxMs: PLAYTEST.WHISPER_CLIP_MAX_MS, level: 0.3 + 0.18 * band,
            });
            if (r) note('whisper', { phrase: row.text, url: row.audio }, 'clip');
            break;
          }
          r = tick(variant, 0.3 + 0.18 * band);
          if (r) note('whisper', { sting: variant }, variant);
          break;
        }
        case 'corner_gif': {
          /* the classic corner GIF: ONE loop pinned to a corner of the stage
           * for a beat. `count:1` + x/y is the engine's own placement seam -
           * no new primitive, no game-owned decoder. */
          const at = CORNERS[variant] || CORNERS.tl;
          r = keepBurst(fireSafe('gif_burst', {
            count: 1, variant: 'pop', x: at.x, y: at.y,
            sizePx: reduced ? 150 : 210, holdMs: PULSE_MS.corner_gif,
            assetKind: reduced ? 'still' : 'loop',
          }));
          if (r) note('corner_gif', { assetId: 'corner:' + variant }, variant);
          break;
        }
        case 'fullscreen_gif': {
          /* THE WHOLE STAGE, for a beat, then gone. `fullBleed` is the engine's
           * additive cover option (engine/oneshots.js) - one node, no
           * transform, object-fit:cover over the layer. */
          r = keepBurst(fireSafe('gif_burst', {
            count: 1, fullBleed: true, holdMs: PULSE_MS.fullscreen_gif,
            assetKind: reduced ? 'still' : 'loop',
          }));
          if (r) note('fullscreen_gif', { assetId: 'full' }, 'full');
          break;
        }
        case 'cascade': {
          r = sustainSafe('gif_rain', { variant, durationMs: PULSE_MS.cascade, restart: true });
          if (r) note('cascade', {}, variant);
          break;
        }
        case 'bubbles': {
          r = sustainSafe('bubble_field', { variant, max: Math.round(6 + 10 * band), restart: true });
          if (r) {
            note('bubbles', {}, variant);
            pulse('bubbles', () => stopSafe('bubble_field'));
          }
          break;
        }
        case 'spiral': {
          /* WHICH spiral, seeded. The class draws a SET of four off its own
           * seed (spirals.js, kin-first so the decoys are look-alikes) and the
           * dealt `spiralIdx` ring walks the ring with no repeat back to back.
           * The url the engine ACTUALLY painted comes back on the handle and
           * that - not what we asked for - is what the ledger records (Law I).
           * No set, no url: the engine uses the shell's class spiral and the
           * entry carries `url: null`, which canAsk refuses. */
          const ring = spiralSet && Array.isArray(spiralSet.ring) ? spiralSet.ring : [];
          const ch = chanFor('spiral');
          const walk = (ch && Array.isArray(ch.spiralIdx) && ch.spiralIdx.length) ? ch.spiralIdx : [0];
          const opts = {
            variant: 'spiral', holdMs: PULSE_MS.spiral,
            alpha: PLAYTEST.SPIRAL_ALPHA[0]
              + (PLAYTEST.SPIRAL_ALPHA[1] - PLAYTEST.SPIRAL_ALPHA[0]) * band,
          };
          if (ring.length) {
            const entry = ring[walk[ringIndex('spiral') % walk.length] % ring.length];
            /* a WOVEN ring row goes to the engine WHOLE (the loom wrapper
             * contract, engine/sustained.js): the handle still answers a
             * STRING - the stable 'loom:' id, or the fallback gif url the
             * WebGL floor actually painted - so the ledger line below is
             * unchanged either way. */
            opts.url = (entry && typeof entry === 'object' && entry.loom === true)
              ? { loom: true, id: entry.id, params: entry.params, href: entry.href }
              : entry;
          }
          r = sustainSafe('wash', opts);
          if (r) {
            note('spiral', {
              url: (r && typeof r.url === 'string') ? r.url : null,
              alpha: Number.isFinite(r.alpha) ? r.alpha : opts.alpha,
            }, 'spiral');
          }
          break;
        }
        case 'pink':
        case 'brain_drain': {
          const washVariant = row.variant;
          r = sustainSafe('wash', { variant: washVariant, alpha: 0.10 + 0.22 * band, holdMs: PULSE_MS[key] });
          if (r) note(key, {}, washVariant);
          break;
        }
        default: break;
      }
      deck('pressure', 'beat', key);
      return r;
    }

    /** The day pool plus anything this vigil has already flashed. */
    function wordPool() {
      const out = [];
      const seen = new Set();
      try {
        for (const w of (ctx.words || [])) {
          const s = String(w || '').trim();
          if (s && !seen.has(s)) { seen.add(s); out.push(s); }
        }
      } catch (e) { /* ignore */ }
      return out;
    }

    /* ==================================================================== *
     * THE STOP
     * ==================================================================== */
    function nextStop() { return plan && stopIdx < plan.stops.length ? plan.stops[stopIdx] : null; }

    function warnStop(stop) {
      if (bellArmed === stop.n) return;
      bellArmed = stop.n;
      msg('ir_stop_incoming', IR_LEX.ir_stop_incoming);
      tick('sting', 0.34);
    }

    /**
     * THE QUENCH. `#arc-fx` is a fixed layer over the whole page, so a held
     * wash (2.4s), a corner GIF (2s) or a bubble field (3.4s) all OUTLIVE the
     * freeze and float over the slip - which for a SPIRAL or a WALL card would
     * simply hand over the answer. So the air is cleared BEFORE the card, and
     * none of it writes a ledger entry: the quench is housekeeping, not an
     * event, and the truth of what happened is already written.
     */
    function quench() {
      quenchCount += 1;
      for (const h of burstHandles.splice(0)) {
        try { if (h && typeof h.cancel === 'function') h.cancel(); } catch (e) { /* noop */ }
      }
      stopSafe('bubble_field');
      stopSafe('gif_rain');
      /* trap 33: a wash is stepped DOWN, never stop('wash')'d. */
      for (const v of ['spiral', 'pink', 'drain']) sustainSafe('wash', { variant: v, alpha: 0.01, holdMs: 400 });
      /* the shell's one control message cuts a whisper clip mid-word. NOT
       * note()d - a silence is not an emission. */
      fireSafe('audio_trigger', { name: 'stop_clips' });
      /* the flashwell is ours, so an inline opacity is legal here: a sub word
       * still fading when the freeze lands would otherwise sit on the slip. */
      try { if (well && well.style) well.style.setProperty('opacity', '0'); } catch (e) { /* noop */ }
    }

    function beginStop() {
      const stop = nextStop();
      if (!stop || live) return;
      stopIdx += 1;
      halted = true;
      clearChannels();
      if (montage) montage.freeze(true);
      quench();
      /* THE WALL BOOK: the frozen DOM state, captured once, read by every
       * WALL_* question at this stop. */
      const snap = snapshotSafe();
      if (snap) {
        wallBook.push({ stop: stop.n, gen: snap.gen, snapshot: snap });
        while (wallBook.length > 16) wallBook.shift();
      }
      setPhase('stop');
      msg('ir_stop_now', IR_LEX.ir_stop_now);
      guardHits = 0;
      guardSince = elapsedMs;
      stallMs = 0;
      lastStallFire = 0;
      live = {
        stop,
        qIdx: -1,
        question: null,
        windowLeft: 0,
        elapsedInWindow: 0,
        windowTimer: 0,
        answeredWeight: 0,
        correctCount: 0,
        askedCount: 0,
        voided: false,
        plantEntry: null,
        plantMatch: -1,
        plantTimer: 0,
      };
      /* the casino's stop beat: `stopBeat` if the deck exports it, else the
       * contract's `stop(n, announced)` shape. */
      if (casino && typeof casino.stopBeat === 'function') deck('casino', 'stopBeat', stop.n, stop.announced);
      else deck('casino', 'stop', stop.n, stop.announced);
      deck('pressure', 'beat', 'stop');
      deck('trickster', 'afterStop');
      if (!stop.announced && tier === 2) {
        /* SYNTHESIS #2: the taste of the twist is DEBRIEFED, once. */
        after(1200, () => { if (live) msg('ir_nobell_debrief', IR_LEX.ir_nobell_debrief); });
      }
      after(PLAYTEST.DEAL_BEAT_MS, () => { if (live) dealQuestion(0); });
    }

    /* ---- what the tail can be asked ------------------------------------ */
    function availability() {
      /* A PLANT IS NEVER A TRUTH. It fired, it is in the ledger, and the
       * gotcha replay reads it - but it lives on its own `plant` channel and is
       * filtered out of every truth read, so a false memory can never become
       * the answer key to the question it was planted against. */
      const words = ledger.recent((r) => r.channel !== 'plant' && r.payload && r.payload.word, 24);
      const stings = ledger.recent((r) => r.channel === 'whisper' && r.payload.sting, 12);
      const phrases = ledger.recent((r) => r.channel === 'whisper' && r.payload.phrase, 12);
      const effects = ledger.recent((r) => POOL_KEYS.indexOf(r.channel) >= 0, 12);
      const spiralTail = ledger.recent((r) => r.channel === 'spiral'
        && r.payload && typeof r.payload.url === 'string' && r.payload.url, 8);

      /* THE TAIL ALLOWANCE replaces the old blanket exclusion. "The LAST word"
       * is unique by the 700ms rule, so a word that flashed EARLIER is the
       * recency-error decoy the near-miss line was written for - not an
       * ambiguity. `TAIL_DISTRACTORS[tier]` is how many of the three decoys may
       * come out of the tail; the rest come from the day pool as before. */
      const wordTailList = [];
      for (const r of words.slice(1)) {
        const w = r.payload.word;
        if (w && wordTailList.indexOf(w) < 0) wordTailList.push(w);
      }
      const wordOuter = [];
      for (const w of wordPool()) if (wordTailList.indexOf(w) < 0 && wordOuter.indexOf(w) < 0) wordOuter.push(w);

      const stingDistractors = STINGS.slice();

      const phraseTailList = [];
      for (const r of phrases.slice(1)) {
        const p = r.payload.phrase;
        if (p && phraseTailList.indexOf(p) < 0) phraseTailList.push(p);
      }
      const phraseOuter = [];
      for (const row of clipRows()) {
        if (row.text && phraseTailList.indexOf(row.text) < 0 && phraseOuter.indexOf(row.text) < 0) {
          phraseOuter.push(row.text);
        }
      }
      if (!phraseOuter.length) {
        for (const w of wordPool()) if (phraseTailList.indexOf(w) < 0 && phraseOuter.indexOf(w) < 0) phraseOuter.push(w);
      }

      /* A LAST_EFFECT option must be REACHABLE at this tier: an effect that can
       * never fire is not a distractor, it is a freebie the player can strike
       * off by construction. `plan.pool` is exactly tonight's ten (or four).
       * There is NO recency exclusion any more - that was the bug. */
      const reachable = plan ? plan.pool : POOL_KEYS.slice();

      /* THE SPIRAL has to be recent AND bright enough to have registered. */
      const spiralTruth = spiralTail.find((r) => {
        const a = Number(r.payload.alpha);
        return (!Number.isFinite(a) || a >= PLAYTEST.SPIRAL_MIN_ALPHA)
          && (elapsedMs - r.t) <= PLAYTEST.SPIRAL_RECENT_MS;
      }) || null;

      /* THE WALL, read from the BOOK (the frozen DOM at this stop's freeze). */
      const book = wallBook.length ? wallBook[wallBook.length - 1] : null;
      const snap = book ? book.snapshot : null;
      const wallTiles = (snap && Array.isArray(snap.tiles))
        ? snap.tiles.filter((x) => x && x.painted && !x.swapping) : [];
      const dupRows = (snap && Array.isArray(snap.dups)) ? snap.dups : [];
      const singleUrls = (snap && Array.isArray(snap.singles)) ? snap.singles.slice() : [];
      /* on a phone a preview video is a decode we cannot afford (trap 42), so
       * the wall families prefer STILL faces there. */
      const wantKind = (ctx.platform && ctx.platform.isTouch) ? 'still' : 'loop';
      const unseenList = wallTiles.length ? unseenSafe(wantKind, 4) : [];
      const unseenAlt = (wallTiles.length && unseenList.length < 4 && wantKind === 'loop')
        ? unseenSafe('still', 4) : [];
      const unseen = unseenList.concat(unseenAlt.filter((u) => unseenList.indexOf(u) < 0));

      return {
        templates: plan ? plan.templates.slice() : null,
        words: wordTailList.length + wordOuter.length,
        wordList: wordOuter,
        wordTailList,
        hasWord: words.length >= 1,
        hasTwoWords: words.length >= 2,
        wordTail: words,
        stings: stingDistractors.length,
        stingList: stingDistractors,
        stingTail: stings,
        hasSting: stings.length >= 1,
        phrases: phraseTailList.length + phraseOuter.length,
        phraseList: phraseOuter,
        phraseTailList,
        phraseTail: phrases,
        hasPhrase: phrases.length >= 1,
        audible: ctx.audioAudible !== false,
        poolSize: reachable.length,
        effects: reachable.length - 1,
        fxList: reachable.slice(),
        fxTail: effects,
        hasEffect: effects.length >= 1,
        spiralSet: (spiralSet && Array.isArray(spiralSet.set)) ? spiralSet.set.length : 0,
        spiralTruth,
        hasSpiral: !!spiralTruth,
        wallSnap: snap,
        wallTiles,
        painted: wallTiles.length,
        dupRows,
        dups: dupRows.length,
        singleUrls,
        singles: singleUrls.length,
        unseenList: unseen,
        unseen: unseen.length,
        wantKind,
        /* WALL_SEEN's coin is drawn ONCE per availability read, so canAsk and
         * buildQuestion agree and the seed owns the answer's polarity. */
        seenCoin: qroll('seen') < 0.5,
      };
    }

    function pickN(list, n, tag) {
      const src = list.slice();
      const out = [];
      while (out.length < n && src.length) {
        const i = Math.floor(qroll(tag) * src.length);
        out.push(src.splice(Math.min(src.length - 1, i), 1)[0]);
      }
      return out;
    }
    function shuffleSeeded(list, tag) {
      const a = list.slice();
      for (let i = a.length - 1; i > 0; i--) {
        const j = Math.min(i, Math.floor(qroll(tag) * (i + 1)));
        const tmp = a[i]; a[i] = a[j]; a[j] = tmp;
      }
      return a;
    }

    /**
     * Instantiate a question from the LEDGER TAIL. The truth is read here and
     * nowhere else; distractors are drawn from the day's pool / the class's own
     * vocabulary and NEVER from the last-N tail entries, so "the last one" is
     * always unambiguous.
     */
    function buildQuestion(template, avail) {
      const mk = (textKey, fb, opts, trueValue, meta) => {
        const shuffled = shuffleSeeded(opts, 'shuffle');
        const trueIndex = shuffled.findIndex((o) => o.value === trueValue);
        return {
          template,
          text: t(textKey, fb),
          options: shuffled,
          trueIndex,
          media: MEDIA_TEMPLATES.indexOf(template) >= 0,
          meta: meta || {},
        };
      };
      /**
       * THE TAIL ALLOWANCE. Up to `TAIL_DISTRACTORS[tier]` decoys may be things
       * that DID happen, earlier - the recency trap `isNearMiss()` captions -
       * and the rest come from outside the tail. Both halves are drawn seeded,
       * and nothing ever equals the truth.
       */
      function decoysWithTail(tailList, outerList, truth, n, tag) {
        const allow = Math.max(0, Math.min(n, PLAYTEST.TAIL_DISTRACTORS[tier] | 0));
        const tailSrc = tailList.filter((v) => v !== truth);
        const outerSrc = outerList.filter((v) => v !== truth && tailSrc.indexOf(v) < 0);
        const out = pickN(tailSrc, Math.min(allow, tailSrc.length), tag + '-tail');
        for (const v of pickN(outerSrc, n - out.length, tag)) if (out.indexOf(v) < 0) out.push(v);
        /* the tail is the last resort, never the first: only if the day pool
         * cannot fill the card do we spend more of the allowance. */
        if (out.length < n) {
          for (const v of pickN(tailSrc.filter((x) => out.indexOf(x) < 0), n - out.length, tag + '-more')) {
            if (out.indexOf(v) < 0) out.push(v);
          }
        }
        return out;
      }
      /** A media option. `kind` is what style.js sizes the face by. */
      const face = (url, kind) => ({
        value: url, url, media: kind || (isAnimatedUrl(url) ? 'loop' : 'still'), label: '',
      });
      /** Which tile indices in the freeze snapshot wear this url. */
      const tilesWearing = (url) => avail.wallTiles.filter((x) => x.url === url).map((x) => x.i);
      /** A seeded painted, non-swapping tile - the one the wall really wore. */
      function wallTruthTile(tag) {
        let cands = avail.wallTiles.filter((x) => (x.shownForMs | 0) >= 1500);
        if (!cands.length) cands = avail.wallTiles.slice();
        if (avail.wantKind === 'still') {
          const stills = cands.filter((x) => !isAnimatedUrl(x.url));
          if (stills.length) cands = stills;
        }
        if (!cands.length) return null;
        const i = Math.floor(qroll(tag) * cands.length);
        return cands[Math.min(cands.length - 1, Math.max(0, i))];
      }
      /** Unseen urls that MATCH the truth's kind, so the card is not a shape quiz. */
      function unseenLike(truthUrl, n, tag) {
        const wantAnim = isAnimatedUrl(truthUrl);
        let src = avail.unseenList.filter((u) => u !== truthUrl && isAnimatedUrl(u) === wantAnim);
        if (src.length < n) src = avail.unseenList.filter((u) => u !== truthUrl);
        return pickN(src, n, tag);
      }

      if (template === 'LAST_WORD') {
        const truth = avail.wordTail[0].payload.word;
        const ds = decoysWithTail(avail.wordTailList, avail.wordList, truth, 3, 'word');
        if (ds.length < 3) return null;
        const opts = [{ value: truth, label: truth }].concat(ds.map((w) => ({ value: w, label: w })));
        return mk('ir_q_last_word', IR_LEX.ir_q_last_word, opts, truth, { kind: 'word' });
      }
      if (template === 'HEARD') {
        /* NO RE-LISTEN. The clip IS the content; a play button would turn
         * recall into matching and hand over the answer (contract §5). Each
         * option carries its own clip url anyway, because a whisper PLANT
         * needs one to lie with. */
        const truth = avail.phraseTail[0].payload.phrase;
        const ds = decoysWithTail(avail.phraseTailList, avail.phraseList, truth, 3, 'phrase');
        if (ds.length < 3) return null;
        const audioFor = (text) => {
          const row = clipRows().find((x) => x.text === text);
          return row ? row.audio : null;
        };
        const opts = [{ value: truth, label: truth, audio: audioFor(truth) }]
          .concat(ds.map((p) => ({ value: p, label: p, audio: audioFor(p) })));
        return mk('ir_q_heard', IR_LEX.ir_q_heard, opts, truth, { kind: 'phrase' });
      }
      if (template === 'SPIRAL') {
        const truth = avail.spiralTruth.payload.url;
        const set = (spiralSet && Array.isArray(spiralSet.set)) ? spiralSet.set : [];
        /** a WOVEN option's face: value = the ledger id, url = a rendered
         *  ~96px thumb (spirals.loomThumbDataUrl). null = not paintable here
         *  (headless / no canvas) - the option, or the question, stands down. */
        const loomFace = (id, params) => {
          const thumb = (spirals && typeof spirals.loomThumbDataUrl === 'function' && params)
            ? spirals.loomThumbDataUrl(params, 96) : null;
          return thumb ? { value: id, url: thumb, media: 'spiral', label: '' } : null;
        };
        if (isLoomId(truth)) {
          /* a WOVEN truth: three genuinely different seeded weaves from the
           * same generator (the doctrine in spirals.js) - never the gif set,
           * never a filtered copy of what played. */
          const tp = loomParamsById.get(truth)
            || (loomSpiralRow && loomSpiralRow.id === truth ? loomSpiralRow.params : null);
          if (!tp || !spirals || typeof spirals.loomDecoyParams !== 'function') return null;
          const tFace = loomFace(truth, tp);
          if (!tFace) return null;
          const dFaces = [];
          for (const d of (spirals.loomDecoyParams(seed, 3, truth) || [])) {
            const f = loomFace(d.id, d.params);
            if (f) { loomParamsById.set(d.id, d.params); dFaces.push(f); }
          }
          if (dFaces.length < 3) return null;
          return mk('ir_q_spiral', IR_LEX.ir_q_spiral, [tFace].concat(dFaces.slice(0, 3)), truth, { kind: 'spiral' });
        }
        let ds = [];
        if (spirals && typeof spirals.spiralDecoys === 'function') {
          try { ds = spirals.spiralDecoys(truth, set, qroll) || []; } catch (e) { ds = []; }
        }
        if (!Array.isArray(ds) || ds.length < 3) ds = set.filter((u) => u !== truth).slice(0, 4);
        /* the set may now hold the woven id: it decoys a gif truth through its
         * thumb, and an unpaintable weave simply stands down (never a bare
         * 'loom:' string into an <img>). */
        const opts = [face(truth, 'spiral')];
        for (const u of ds) {
          if (opts.length >= 4) break;
          if (typeof u !== 'string' || !u || u === truth) continue;
          if (isLoomId(u)) {
            const f = loomFace(u, loomParamsById.get(u));
            if (f) opts.push(f);
            continue;
          }
          opts.push(face(u, 'spiral'));
        }
        if (opts.length < 4) return null;
        return mk('ir_q_spiral', IR_LEX.ir_q_spiral, opts, truth, { kind: 'spiral' });
      }
      if (template === 'WALL_PICK') {
        const tile = wallTruthTile('wallpick');
        if (!tile) return null;
        const ds = unseenLike(tile.url, 3, 'wallpick-d');
        if (ds.length < 3) return null;
        const opts = [face(tile.url)].concat(ds.map((u) => face(u)));
        return mk('ir_q_wall_pick', IR_LEX.ir_q_wall_pick, opts, tile.url,
          { kind: 'wall', truthTiles: tilesWearing(tile.url) });
      }
      if (template === 'WALL_TWICE') {
        const dup = avail.dupRows[0];
        if (!dup || !dup.url) return null;
        const ds = pickN(avail.singleUrls.filter((u) => u !== dup.url), 3, 'walltwice');
        if (ds.length < 3) return null;
        const opts = [face(dup.url)].concat(ds.map((u) => face(u)));
        return mk('ir_q_wall_twice', IR_LEX.ir_q_wall_twice, opts, dup.url,
          { kind: 'wall', truthTiles: tilesWearing(dup.url) });
      }
      if (template === 'WALL_GONE') {
        const truth = avail.unseenList[0];
        if (!truth) return null;
        const onWall = [];
        for (const x of avail.wallTiles) if (x.url && x.url !== truth && onWall.indexOf(x.url) < 0) onWall.push(x.url);
        const ds = pickN(onWall, 3, 'wallgone');
        if (ds.length < 3) return null;
        const opts = [face(truth)].concat(ds.map((u) => face(u)));
        return mk('ir_q_wall_gone', IR_LEX.ir_q_wall_gone, opts, truth, { kind: 'wall' });
      }
      if (template === 'WALL_SEEN') {
        /* TWO options, so it weighs half a question (OPTION_WEIGHT). The coin
         * was drawn in availability(), so canAsk and this agree. */
        const yes = avail.seenCoin || !avail.unseenList.length;
        let preview = null;
        let truthTiles = [];
        if (yes) {
          const tile = wallTruthTile('wallseen');
          if (!tile) return null;
          preview = tile.url;
          truthTiles = tilesWearing(tile.url);
        } else {
          preview = avail.unseenList[0];
          if (!preview) return null;
        }
        const truth = yes ? 'yes' : 'no';
        const opts = [
          { value: 'yes', label: t('ir_yes', IR_LEX.ir_yes) },
          { value: 'no', label: t('ir_no', IR_LEX.ir_no) },
        ];
        return mk('ir_q_wall_seen', IR_LEX.ir_q_wall_seen, opts, truth,
          { kind: 'seen', preview, seenYes: yes, truthTiles });
      }
      if (template === 'LAST_TWO') {
        const a = avail.wordTail[1].payload.word;
        const b = avail.wordTail[0].payload.word;
        const arrow = ' → ';
        const truth = a + '|' + b;
        const opts = [
          { value: truth, label: a + arrow + b },
          { value: b + '|' + a, label: b + arrow + a },
        ];
        const outs = decoysWithTail(avail.wordTailList, avail.wordList, null, 4, 'two');
        for (let i = 0; i + 1 < outs.length && opts.length < 4; i += 2) {
          const v = outs[i] + '|' + outs[i + 1];
          if (opts.some((o) => o.value === v)) continue;
          opts.push({ value: v, label: outs[i] + arrow + outs[i + 1] });
        }
        while (opts.length < 4 && outs.length) {
          const v = outs[0] + '|' + a;
          if (opts.some((o) => o.value === v)) break;
          opts.push({ value: v, label: outs[0] + arrow + a });
        }
        if (opts.length < 4) return null;
        return mk('ir_q_last_two', IR_LEX.ir_q_last_two, opts, truth, { kind: 'pair' });
      }
      if (template === 'LAST_EFFECT') {
        const truth = avail.fxTail[0].channel;
        const ds = pickN(avail.fxList.filter((k) => k !== truth), 3, 'fx');
        if (ds.length < 3) return null;
        const label = (k) => t('ir_fx_' + k, IR_LEX['ir_fx_' + k] || k);
        const opts = [{ value: truth, label: label(truth) }].concat(ds.map((k) => ({ value: k, label: label(k) })));
        return mk('ir_q_last_effect', IR_LEX.ir_q_last_effect, opts, truth, { kind: 'effect' });
      }
      if (template === 'LAST_STING') {
        const truth = avail.stingTail[0].payload.sting;
        const ds = pickN(avail.stingList.filter((s) => s !== truth), 3, 'sting');
        if (ds.length < 3) return null;
        const label = (s) => t('ir_sting_' + s, IR_LEX['ir_sting_' + s] || s);
        const opts = [{ value: truth, label: label(truth), sting: truth }]
          .concat(ds.map((s) => ({ value: s, label: label(s), sting: s })));
        return mk('ir_q_last_sting', IR_LEX.ir_q_last_sting, opts, truth, { kind: 'sting' });
      }
      return null;
    }

    function dealQuestion(qi) {
      if (!live || dead || ended) return;
      const stop = live.stop;
      if (qi >= stop.questions.length) { resolveStop(); return; }
      live.qIdx = qi;
      const dealtQ = stop.questions[qi];
      const avail = availability();
      /* THE HISTORY-AWARE WALK. A starved family used to fall to the same
       * replacement every time, which is how a class that DEALT ten families
       * asked one of them nine times. The fallback now picks the family this
       * class has asked LEAST, and never the one the last question resolved to
       * while another is askable. */
      const history = results.questions.map((q) => q.template);
      const template = resolveTemplate(dealtQ.template, avail, tier, history);
      const question = template ? buildQuestion(template, avail) : null;
      if (!question || question.trueIndex < 0) {
        /* The tail can answer nothing: a question is NEVER invented. The stop
         * simply resumes, uncounted - it cannot cost a grade it never asked. */
        say('stop ' + stop.n + ' q' + qi + ': the tail could answer nothing - skipped');
        after(400, () => { if (live) dealQuestion(qi + 1); });
        return;
      }
      question.dealtTemplate = dealtQ.template;
      /* THE WINDOW weights every question (a 4s question is worth 1.5x a 6s
       * one, so a tier-4 class is not silently easier to ace) and the OPTION
       * COUNT weights it again: WALL_SEEN is a coin flip with a preview
       * attached and is worth half a card. A media family may also be given a
       * longer window - the bonus ships at 0 and `derivedMinGap()` already
       * paid for it, so flipping it is legal. */
      const bonus = question.media ? PLAYTEST.PREVIEW_WINDOW_BONUS_MS : 0;
      question.windowMs = stop.windowMs + bonus;
      question.weight = (6000 / question.windowMs) * optionWeight(question.options.length);
      live.question = question;
      live.askedCount += 1;
      /* W3 P1-13: the FIRST card of a stop arrives on the freeze's own beat and
       * needs no cue of its own; every card after it used to appear in silence.
       * One sheet of paper says "another one". */
      if (qi > 0) tick('paper', 0.22, { pitch: 1 });
      renderQuestion(question);
      startWindow(question);
      if (qi === 0) armPlant(stop, question);
    }

    /** One preview box. `mediaElFor` mints an <img> or a muted looping <video>;
     *  the box is aria-hidden, pointer-inert decoration inside its button. */
    function faceBox(cls, url, kind) {
      const box = el('div', cls);
      box.setAttribute('aria-hidden', 'true');
      if (kind) box.setAttribute('data-media', kind);
      try {
        const m = mediaElFor(url);
        if (m) box.appendChild(m);
      } catch (e) { /* a broken preview is an empty box, never a dead class */ }
      return box;
    }

    function renderQuestion(question) {
      stopEl.hidden = false;
      truthEl.hidden = true;
      truthEl.textContent = '';
      qEl.textContent = question.text;
      const kindAttr = question.meta.kind === 'seen' ? 'seen' : (question.media ? 'media' : 'text');
      try {
        stopEl.setAttribute('data-kind', kindAttr);
        stopEl.setAttribute('data-family', question.template);
      } catch (e) { /* noop */ }
      /* THE SHROUD. The wall is frozen at .42 behind the slip; with its faces
       * still readable a WALL question would have its answer on screen. The
       * verdict lifts it again (the wall IS the proof) and the resume clears
       * the attribute. */
      if (question.meta.kind === 'wall' || question.meta.kind === 'seen') {
        try { if (stage) stage.setAttribute('data-shroud', '1'); } catch (e) { /* noop */ }
      }
      /* WALL_SEEN shows ONE face above the two options - the thing being asked
       * about. It is decoration, never a hitbox. */
      const oldQFace = qFaceEl;
      qFaceEl = null;
      if (oldQFace) { try { oldQFace.remove(); } catch (e) { /* noop */ } }
      if (question.meta.kind === 'seen' && question.meta.preview) {
        qFaceEl = faceBox('g-ir-q-face', question.meta.preview,
          isAnimatedUrl(question.meta.preview) ? 'loop' : 'still');
        try { stopEl.insertBefore(qFaceEl, optsEl); } catch (e) { qFaceEl = null; }
      }
      optsEl.textContent = '';
      optEls = [];
      question.options.forEach((opt, i) => {
        const b = el('button', 'g-ir-opt' + (opt.url ? ' g-ir-opt-media' : ''));
        b.setAttribute('type', 'button');
        b.setAttribute('data-i', String(i));
        const num = el('span', 'g-ir-opt-n', String(i + 1));
        num.setAttribute('aria-hidden', 'true');
        b.appendChild(num);
        if (opt.url) {
          /* A MEDIA option carries NO `.g-ir-opt-t`, which is exactly how the
           * trickster's Unreliable Label folds on these cards: `labelSurface()`
           * finds no text node and the lie has nowhere to live. */
          b.setAttribute('data-media', opt.media || 'still');
          b.setAttribute('aria-label', t('ir_opt', IR_LEX.ir_opt) + ' ' + (i + 1));
          b.appendChild(faceBox('g-ir-opt-face', opt.url, opt.media));
        } else {
          b.appendChild(el('span', 'g-ir-opt-t', opt.label));
        }
        if (opt.sting && ctx.audioAudible !== false) {
          const hear = el('button', 'g-ir-hear', t('ir_hear', IR_LEX.ir_hear));
          hear.setAttribute('type', 'button');
          hear.setAttribute('data-sting', opt.sting);
          /* Law II: this is a real button that auditions and NEVER selects. */
          hear.addEventListener('click', (e) => {
            try { if (e && e.stopPropagation) e.stopPropagation(); } catch (err) { /* noop */ }
            try { if (e && e.preventDefault) e.preventDefault(); } catch (err) { /* noop */ }
            fireSafe('audio_trigger', { name: opt.sting, level: 0.42 });
          });
          b.appendChild(hear);
        }
        b.addEventListener('click', () => answer(i, 'tap'));
        optsEl.appendChild(b);
        optEls.push(b);
      });
      const hintKey = question.options.length >= 4 ? 'ir_answer_hint'
        : question.options.length === 3 ? 'ir_answer_hint3' : 'ir_answer_hint2';
      msg(hintKey, IR_LEX[hintKey]);
      try { if (optEls[0] && optEls[0].focus) optEls[0].focus(); } catch (e) { /* noop */ }
    }

    /** The REAL window. It counts accumulated, pause-aware ticks - never wall
     *  clock - so a suspend mid-question freezes it honestly. The trickster's
     *  crooked ring may lie on the FACE; this is the truth underneath. */
    function startWindow(question) {
      live.windowLeft = question.windowMs;
      live.elapsedInWindow = 0;
      if (timerEl) {
        timerEl.style.setProperty('--ir-win', question.windowMs + 'ms');
        timerEl.style.setProperty('--ir-left', '1');
        timerEl.textContent = Math.ceil(question.windowMs / 1000) + '';
      }
      /* W3 P0-2: the countdown's own state. The window ticker runs at 100ms; the
       * cue rides the whole-seconds figure the chip already prints. */
      live.tickSec = -1;
      live.ticks = 0;
      const step = 100;
      live.windowTimer = every(step, () => {
        if (!live || !live.question) return;
        live.elapsedInWindow += step;
        live.windowLeft = Math.max(0, question.windowMs - live.elapsedInWindow);
        const secs = Math.ceil(live.windowLeft / 1000);
        if (timerEl) {
          timerEl.style.setProperty('--ir-left', (live.windowLeft / question.windowMs).toFixed(3));
          timerEl.textContent = secs + '';
        }
        /* W3 P0-2: the last 3 seconds enter the ear, one tick per boundary,
         * pitch and level climbing. stopWindow() kills it - which is every road
         * out of a question, a commit and a timeout alike. */
        if (live.windowLeft > 0 && secs <= 3 && secs !== live.tickSec) {
          live.tickSec = secs;
          const n = Math.min(4, live.ticks);
          tick('clock_tick', 0.1 + 0.02 * n, { pitch: 1 + 0.06 * n });
          live.ticks += 1;
        }
        if (live.windowLeft <= 0) commit(-1, 'timeout');
      });
    }
    function stopWindow() {
      if (live && live.windowTimer) { clearTimer(live.windowTimer); live.windowTimer = 0; }
      if (live) { live.tickSec = -1; live.ticks = 0; }   // W3 P0-2: the countdown is disarmed with the window
    }

    /* ---- the decoy plant (tier 3+, SetPiece-gated) ---------------------- */
    /**
     * The freeze is the one moment the player relaxes their vigilance - which
     * is exactly when a false memory lands hardest. The plant fires a REAL
     * emission over the dimmed freeze, deliberately matching a WRONG option.
     *
     * It is written to the ledger under its own `plant` channel, never under
     * `sub_flash` / `audio_trigger`: the ledger stays honest about what
     * happened (Law I) while the plant can never become the truth of a later
     * question. If the question has no option a plant could match (an effect or
     * a layout question), the plant is not armed at all - a plant that matches
     * nothing is noise, and noise is not a decoy, so it is never scored.
     */
    function armPlant(stop, question) {
      if (!stop.plant || reduced || !capsArmed()) return;
      const wrong = [];
      question.options.forEach((o, i) => { if (i !== question.trueIndex) wrong.push({ o, i }); });
      const kind = question.meta.kind;
      /* SPIRAL and every WALL family take NO plant: there is no option a real
       * emission could match, and a plant that matches nothing is noise, not a
       * decoy - it would be scored against a player who never saw a choice. */
      const usable = wrong.filter(({ o }) => (
        stop.plant.channel === 'whisper' ? (!!o.sting || !!o.audio) : (kind === 'word' || kind === 'pair')
      ));
      if (!usable.length) return;
      const pick = usable[Math.floor(qroll('plant') * usable.length)];
      live.plantTimer = after(stop.plant.atMs, () => {
        if (!live || !live.question || dead || ended) return;
        live.plantTimer = 0;
        let r = null;
        let payload = null;
        if (stop.plant.channel === 'whisper' && pick.o.audio) {
          /* the decoy PHRASE, in its own voice, over the freeze */
          r = fireSafe('audio_trigger', {
            name: 'whisper', url: pick.o.audio, key: 'ir-plant',
            maxMs: PLAYTEST.WHISPER_PLANT_MAX_MS, level: 0.4,
          });
          payload = { phrase: pick.o.value, url: pick.o.audio, plant: true, matched: pick.i };
        } else if (stop.plant.channel === 'whisper') {
          r = fireSafe('audio_trigger', { name: pick.o.sting, level: 0.4 });
          payload = { sting: pick.o.sting, plant: true, matched: pick.i };
        } else {
          const word = String(pick.o.value).split('|')[0];
          r = fireSafe('sub_flash', { text: word, image: false, variant: 'centre', anchor: stopEl });
          payload = { word, plant: true, matched: pick.i };
        }
        if (!r) return;                      // the caps refused it: no exposure
        plantsFired += 1;
        plantExposures += 1;
        live.plantMatch = pick.i;
        live.plantEntry = note('plant', payload, stop.plant.channel);
      });
    }

    /* ---- answering ------------------------------------------------------ */
    function answer(i, how) {
      if (!live || !live.question) return;
      const n = Number(i);
      if (!Number.isFinite(n) || n < 0 || n >= live.question.options.length) return;
      commit(n, how || 'tap');
    }

    function commit(index, how) {
      if (!live || !live.question) return;
      stopWindow();
      if (live.plantTimer) { clearTimer(live.plantTimer); live.plantTimer = 0; }
      const q = live.question;
      const timedOut = index < 0 && how === 'timeout';
      const voided = how === 'void';
      const correct = !timedOut && !voided && index === q.trueIndex;
      const latencyMs = Math.max(0, live.elapsedInWindow || 0);
      const decoyHit = live.plantMatch >= 0 && index === live.plantMatch;

      if (live.plantMatch >= 0) { if (!decoyHit) plantsResisted += 1; }
      if (timedOut) timeouts += 1;

      results.questions.push({
        stop: live.stop.n,
        template: q.template,
        family: q.template,
        dealt: q.dealtTemplate,
        nOptions: q.options.length,
        media: !!q.media,
        correct,
        timedOut,
        voided,
        decoyHit,
        latencyMs,
        windowMs: q.windowMs,
        weight: q.weight,
      });
      if (correct) live.correctCount += 1;
      if (voided) live.voided = true;

      /* the option buttons stop being buttons the moment one is committed */
      for (const b of optEls) { try { b.disabled = true; } catch (e) { /* noop */ } }
      optEls.forEach((b, k) => {
        try {
          if (k === q.trueIndex) b.classList.add('is-true');
          else if (k === index) b.classList.add('is-wrong');
        } catch (e) { /* noop */ }
      });

      deck('casino', 'answer', { correct, latencyMs, streak });
      deck('trickster', 'afterAnswer');
      deck('pressure', 'beat', correct ? 'hit' : 'miss');
      if (live.plantMatch >= 0 && !decoyHit) deck('casino', 'plantResisted');

      /* THE VERDICT IS THE PROOF, so the wall comes back for it and the tiles
       * that really wore the answer are ringed. */
      try { if (stage) stage.setAttribute('data-shroud', '0'); } catch (e) { /* noop */ }
      if (Array.isArray(q.meta.truthTiles) && q.meta.truthTiles.length) {
        highlightSafe(q.meta.truthTiles, true);
      }
      renderVerdict(q, index, { correct, timedOut, voided, decoyHit });
      /* W3 P1-13: taking the PLANT is not the same mistake as a plain miss, and
       * it used to make the same sound. The false memory answers in its own
       * voice, 200ms behind the verdict stamp so the two do not smear. Quiet:
       * it is still a loss. */
      if (decoyHit) after(200, () => { tick('whisper', 0.22, { pitch: 0.6 }); });

      const hold = reduced ? PLAYTEST.VERDICT_MS_REDUCED : PLAYTEST.VERDICT_MS;
      live.plantMatch = -1;
      live.question = null;
      after(hold, () => {
        if (!live || dead || ended) return;
        dealQuestion(live.qIdx + 1);
      });
    }

    /* ---- the truth replay ----------------------------------------------- */
    /**
     * Every answer resolves against the ledger, and the reveal PROVES it: the
     * last few entries are pinned on a ribbon with the answer's own moment
     * marked. Default posture is the dossier's `on-miss` - a miss gets the full
     * ribbon, a hit gets the short confirmation - because the rewind is a
     * learning aid on a miss and a pacing drag when it is always shown.
     */
    function renderVerdict(q, index, how) {
      const tone = how.correct ? 'good' : 'bad';
      const label = how.timedOut ? t('ir_timeout', IR_LEX.ir_timeout)
        : how.voided ? t('ir_voided', IR_LEX.ir_voided)
          : how.correct ? t('ir_correct', IR_LEX.ir_correct) : t('ir_wrong', IR_LEX.ir_wrong);
      try { ctx.ceremonies.stamp({ text: label, tone, target: stopEl }); } catch (e) { /* noop */ }
      tick(how.correct ? 'stamp' : 'stamp_bad', how.correct ? 0.4 : 0.2);   /* owner 2026-08-24: error cues -50% */

      truthEl.textContent = '';
      truthEl.hidden = false;
      const ribbon = el('div', 'g-ir-ribbon');
      ribbon.setAttribute('aria-hidden', 'true');
      const tail = ledger.tail(7);
      const pin = answerMoment(q, tail);
      for (const rec of tail) {
        const bead = el('span', 'g-ir-bead');
        bead.setAttribute('data-ch', rec.channel);
        const p = rec.payload || {};
        /* the bead prints WORDS and PHRASES, never a url: a face's filename is
         * not a thing the player saw. */
        bead.textContent = p.word != null ? p.word
          : p.phrase != null ? p.phrase
            : p.sting != null ? t('ir_sting_' + p.sting, IR_LEX['ir_sting_' + p.sting] || p.sting)
              : t('ir_fx_' + rec.channel, IR_LEX['ir_fx_' + rec.channel] || rec.channel);
        if (p.plant) bead.classList.add('is-plant');
        if (rec === pin) bead.classList.add('is-pin');
        ribbon.appendChild(bead);
      }
      truthEl.appendChild(ribbon);

      const kind = q.meta.kind;
      const truthKey = kind === 'spiral' ? 'ir_truth_spiral'
        : kind === 'phrase' ? 'ir_truth_heard'
          : kind === 'wall' ? (q.template === 'WALL_GONE' ? 'ir_truth_wall_gone' : 'ir_truth_wall')
            : kind === 'seen' ? (q.meta.seenYes ? 'ir_truth_wall' : 'ir_truth_wall_gone')
              : 'ir_truth';
      let line = t(truthKey, IR_LEX[truthKey]);
      if (how.decoyHit) {
        const gk = kind === 'phrase' ? 'ir_gotcha_heard' : 'ir_gotcha';
        line = t(gk, IR_LEX[gk]);
      } else if (!how.correct && !how.timedOut && !how.voided && isNearMiss(q, index)) {
        const nk = kind === 'spiral' ? 'ir_near_spiral' : kind === 'phrase' ? 'ir_near_heard' : 'ir_near';
        line = t(nk, IR_LEX[nk]);
      } else if (how.voided) line = t('ir_voided', IR_LEX.ir_voided);
      truthEl.appendChild(el('p', 'g-ir-truth-line', line));
      if (msgEl) msgEl.textContent = line;
    }
    /** The ledger entry the question was actually about. A WALL question pins
     *  NO bead - its proof is the wall itself, ringed behind the slip. */
    function answerMoment(q, tail) {
      const kind = q.meta.kind;
      if (kind === 'wall' || kind === 'seen') return null;
      for (let i = tail.length - 1; i >= 0; i--) {
        const r = tail[i];
        if (r.payload && r.payload.plant) continue;
        if (kind === 'word' || kind === 'pair') { if (r.payload && r.payload.word) return r; }
        else if (kind === 'sting') { if (r.channel === 'whisper') return r; }
        else if (kind === 'phrase') { if (r.channel === 'whisper' && r.payload && r.payload.phrase) return r; }
        else if (kind === 'spiral') { if (r.channel === 'spiral') return r; }
        else if (POOL_KEYS.indexOf(r.channel) >= 0) return r;
      }
      return null;
    }
    /** THE CLASSIC RECENCY ERROR: the thing you picked DID happen - earlier.
     *  It generalises across every family whose options are things that can be
     *  in the ledger (a word, a pair, a phrase, a spiral url). */
    function isNearMiss(q, index) {
      if (index < 0 || !q.options[index]) return false;
      const kind = q.meta.kind;
      const chosen = String(q.options[index].value);
      if (kind === 'word' || kind === 'pair') {
        const word = chosen.split('|')[0];
        return !!ledger.lastOf((r) => r.payload && r.payload.word === word && !r.payload.plant);
      }
      if (kind === 'phrase') {
        return !!ledger.lastOf((r) => r.payload && r.payload.phrase === chosen && !r.payload.plant);
      }
      if (kind === 'spiral') {
        return !!ledger.lastOf((r) => r.channel === 'spiral' && r.payload && r.payload.url === chosen);
      }
      return false;
    }

    /* ---- resolving a stop ----------------------------------------------- */
    function resolveStop() {
      if (!live) return;
      const stop = live.stop;
      const asked = live.askedCount;
      const fullyCorrect = asked > 0 && live.correctCount === asked && !live.voided;
      /* A stop whose tail could answer NOTHING asked nothing, so it scores
       * nothing - in either direction. It is not a miss, it is not a streak
       * link, and it never reaches the rubric: a class cannot be marked down
       * for a question it never asked (the may-be-empty word contract). */
      if (asked > 0) results.stops.push({ n: stop.n, fullyCorrect, voided: live.voided, asked });
      else say('stop ' + stop.n + ' asked nothing - uncounted');
      if (live.voided) voidedStops += 1;

      /* THE COMEBACK HOOK. One blown stop is recoverable: the missed stop's
       * weight is restored if the very NEXT stop is fully correct. Once per
       * class - it can never launder an S, because the S gate reads the stops
       * themselves and a corrected stop was still not fully correct. */
      if (correctionPending > 0) {
        if (fullyCorrect) {
          correctedWeight += correctionPending;
          msg('ir_corrected', IR_LEX.ir_corrected);
          /* W3 P1-13: THE COMEBACK. Once a class, and it was a banner and
           * nothing else. It lands 250ms behind the stop's own win so it reads
           * as a second, separate piece of good news. */
          after(250, () => { tick('lift', 0.28, { pitch: 1.2 }); });
        }
        correctionPending = 0;
      } else if (!fullyCorrect && !correctionUsed && asked > 0) {
        correctionUsed = true;
        let lost = 0;
        for (const r of results.questions) if (r.stop === stop.n && !r.correct && !r.voided) lost += r.weight;
        correctionPending = lost;
      }

      if (asked > 0) {
        if (fullyCorrect) {
          streak += 1;
          bestRun = Math.max(bestRun, streak);
          /* W3 P0-5: THE GUARANTEED WIN. Clearing a whole stop only ever
           * sounded when the variable-ratio roll happened to pay, so the ear
           * heard the misses (a `sting` at .6) far more reliably than the wins.
           * The floor is fired HERE, before rewardBeat(), so a roll that pays
           * stacks ON TOP of it instead of replacing it. Pitch is the run. */
          tick('streak', 0.3, { pitch: 1 + 0.06 * Math.min(PLAYTEST.PITCH_CAP, streak) });
          rewardBeat();
        } else {
          streak = 0;
          stopGoldHum();          // W3 P2-5: the run is over, its dressing goes
        }
      }
      deck('pressure', 'setStreak', streak);
      try { ctx.ceremonies.streakMeter({ filled: Math.min(10, streak), gold: streak >= 3, target: hud }); }
      catch (e) { /* noop */ }

      easedNext = asked > 0 && !fullyCorrect;
      stopsResolved += 1;
      live = null;
      stopEl.hidden = true;
      truthEl.hidden = true;
      optEls = [];
      try {
        stopEl.removeAttribute('data-kind');
        stopEl.removeAttribute('data-family');
      } catch (e) { /* noop */ }
      if (qFaceEl) { try { qFaceEl.remove(); } catch (e) { /* noop */ } qFaceEl = null; }

      if (stopsResolved >= plan.stopCount || stopIdx >= plan.stops.length) {
        after(reduced ? 400 : 900, () => finish(false));
        return;
      }
      resumeVigil();
    }

    /**
     * THE DUPLICATE PLANT. WALL_TWICE needs a face the wall wore TWICE, and a
     * natural duplicate is rare, so the montage is asked to land one - by the
     * ordinary swap path, far enough ahead that it has settled before the
     * freeze. The plan only ever REQUESTS it: the question is still read from
     * the freeze snapshot, so a plant that failed is simply not in `dups` and
     * the family falls back like any other.
     */
    function armWallPlant() {
      const nx = nextStop();
      if (!nx || !nx.questions || !nx.questions[0]) return;
      if (nx.questions[0].template !== 'WALL_TWICE') return;
      const lead = nx.atMs - elapsedMs;
      if (!(lead >= PLAYTEST.DUP_LEAD_MS)) return;
      plantWallSafe({
        kind: 'dup',
        byMs: lead - PLAYTEST.DUP_LEAD_MS,
        holdMs: lead + PLAYTEST.DUP_HOLD_PAD_MS,
      });
    }

    function resumeVigil() {
      if (dead || ended) return;
      segStartMs = elapsedMs;
      /* the freeze is over: the shroud, the truth ring and the flashwell all
       * come back exactly as the quench left them. */
      try { if (stage) stage.removeAttribute('data-shroud'); } catch (e) { /* noop */ }
      highlightSafe(null, false);
      try { if (well && well.style) well.style.setProperty('opacity', '1'); } catch (e) { /* noop */ }
      /* W3 P1-13: the freeze had an entrance and no exit. `shutter_close` is
       * the shutter opening in reverse, so the wall going live again is the
       * counterpart of the wall stopping. Pitch stays 1 HERE: with no mp3 the
       * name falls to `shutter` and the alias already carries the .8, and a
       * second .8 on top would drop it to .64. */
      tick('shutter_close', 0.3, { pitch: 1 });
      if (montage) montage.freeze(false);
      reshuffleWall();
      recomputeBand();
      recomputeHeat();
      setPhase('vigil');
      msg('ir_resume', IR_LEX.ir_resume);
      /* THE RUN ADVERTISES ITSELF. At a 2-stop streak the resumed wall takes a
       * golden grain. It is DRESSING, not an effect: `ambient_field` is not a
       * pool key, it writes NO ledger entry and it can never be an option -
       * the owner's ruling is that a quiz only ever names what CCP names. */
      if (streak >= 2) { sustainSafe('ambient_field', { kind: 'goldleaf', density: 0.3 + 0.3 * band }); startGoldHum(); }
      else stopGoldHum();
      halted = false;
      seedDealer();
      armDealer();
      armWallPlant();
      /* THE WATCH-ONLY WALL is back and nothing is armed on it. See seepStream:
       * asked last, so the plan above has already decided where the next stop
       * is and the lead guard has a real number to read. */
      seepStream();
    }

    /* ---- variable ratio (the shared canon, with a local fallback) -------- */
    /**
     * THE PAYOUT LAYER - the fire branch's pixels. Game-local chrome and
     * NOTHING else: one solid radial gold bloom over the stage, opacity-only,
     * no blend mode / no filter / no engine kind and NO ledger write, so it
     * can never be mistaken for a POOL effect and can never hand the next
     * question a second honest answer (the same law that keeps the jackpot's
     * goldleaf off the pool). Created once, reused; restarted reflow-free
     * (WAAPI rewind first, a rAF class re-add as the fallback - never a
     * `void offsetWidth` layout flush over a wall of live decodes).
     */
    function payoutBloom(big) {
      if (dead || !stage || !capsArmed()) return;
      if (!payoutEl) {
        payoutEl = el('div', 'g-ir-payout');
        payoutEl.setAttribute('aria-hidden', 'true');
        stage.appendChild(payoutEl);
      }
      try { payoutEl.classList.toggle('is-big', !!big); } catch (e) { /* noop */ }
      const on = payoutEl.classList.contains('is-on');
      if (!on) { payoutEl.classList.add('is-on'); return; }
      if (typeof payoutEl.getAnimations === 'function') {
        try {
          const anims = payoutEl.getAnimations();
          if (anims.length) {
            for (const a of anims) { a.currentTime = 0; a.play(); }
            return;
          }
        } catch (e) { /* fall through to the class toggle */ }
      }
      payoutEl.classList.remove('is-on');
      const arm = () => { try { if (!dead && payoutEl) payoutEl.classList.add('is-on'); } catch (e) { /* noop */ } };
      if (typeof requestAnimationFrame === 'function') requestAnimationFrame(arm); else after(16, arm);
    }
    function rewardBeat() {
      let outcome = null;
      try {
        if (ctx.engine && typeof ctx.engine.rewardRoll === 'function') {
          /* the roll is FED now: heat lifts the base chance off its .30 floor
           * (schedule.js: .30 + .30*smoothstep(heat)), streak rides the
           * intensity multiplier, success keeps the run bookkeeping honest.
           * Same shape as the-deep-end / sort. */
          outcome = ctx.engine.rewardRoll({ heat: currentHeat, streak, success: true }) || null;
        }
      } catch (e) { outcome = null; }
      if (!outcome && rewardLocal) outcome = rewardLocal();
      if (!outcome) return;
      if (outcome.jackpot) {
        jackpots += 1;
        /* garnish:false is LOAD-BEARING (casino.js's own law, mosaic rework
         * 2026-08-23): the engine's jackpot ceremony otherwise FORCES a
         * drain|spiral wash - both POOL effects in this class - and a wash
         * the ledger never saw would give the next question a second honest
         * answer. */
        try {
          ctx.ceremonies.reward('jackpot', {
            target: stage, text: t('ir_jackpot', IR_LEX.ir_jackpot), garnish: false,
          });
        } catch (e) { /* noop */ }
        /* "Photographic Memory": gold leaf over the hall. It is DELIBERATELY
         * not a pool primitive - a gif burst here would be a real Corner GIF /
         * Fullscreen GIF that the ledger never saw, and the very next question
         * would have two honest answers. Dressing pays the ceremony instead.
         * `count` raises the FLECK count (the alpha ceiling is engine-side and
         * stays the engine's); the payout layer below is the visible part. */
        sustainSafe('ambient_field', { kind: 'goldleaf', density: 0.55, count: 48 });
        payoutBloom(true);
        tick('jackpot', 0.5);
      } else if (outcome.fire) {
        /* the fire branch was audio-only; now it pays PIXELS - the payout
         * bloom plus a gild stamp on the HUD, both game-local chrome. */
        payoutBloom(false);
        try { ctx.ceremonies.stamp({ text: t('ir_payout', IR_LEX.ir_payout), tone: 'gild', target: hud }); }
        catch (e) { /* noop */ }
        tick('streak', 0.42);
      }
    }

    /* ==================================================================== *
     * THE ESCAPE GUARD - the freeze never traps.
     * Six impatient interactions inside five seconds while a card is up void
     * the stop: it scores as a miss for that question, the vigil resumes at
     * once, and the class is never failed by it (friction, never lockout).
     * ==================================================================== */
    /* THE REFUSED INPUT (W2 chrome vocabulary). A poke at the slip that is not
     * an answer - a wrong-phase key, a press on the card's dead space - was
     * SILENT: the escape guard counted the player's impatience and the room
     * never said "not that". The House Book's answer to a dead input is a muted
     * `bump`, THROTTLED so a mashed key cannot machine-gun it. A press that
     * LANDS on an option (or on its Hear button, which lives inside one) is not
     * a refusal at all, so it never bumps - only the guard counts it. */
    const CHROME_BUMP_MS = 250;
    let lastBumpAt = 0;
    function bumpRefused() {
      const now = Date.now();
      if (now - lastBumpAt < CHROME_BUMP_MS) return;
      lastBumpAt = now;
      tick('bump', 0.15, { pitch: 1 });
    }
    /** Did this press land inside one of the live option buttons? */
    function onOptionNode(node) {
      let n = node;
      let guard = 0;
      while (n && guard < 8) {
        if (optEls.indexOf(n) >= 0) return true;
        try { n = n.parentNode; } catch (e) { return false; }
        guard += 1;
      }
      return false;
    }
    function guardPoke() {
      if (!live || !live.question) return;
      if (elapsedMs - guardSince > PLAYTEST.ESCAPE_MS) { guardHits = 0; guardSince = elapsedMs; }
      guardHits += 1;
      if (guardHits >= PLAYTEST.ESCAPE_TAPS) {
        guardHits = 0;
        say('escape guard: stop ' + live.stop.n + ' voided');
        /* W3 P2-5: a VOID is not a wrong answer, it is the class letting you
         * out, and it sounded identical to a miss. A low slide says the card
         * was taken away rather than failed. */
        tick('slide', 0.18, { pitch: 0.7 });
        commit(-1, 'void');
      }
    }

    /* ==================================================================== *
     * INPUT - tap, or the number keys, on a window keydown (Daily Trigger).
     * ==================================================================== */
    function onKeyDown(e) {
      if (dead || ended || paused) return;
      if (!e || e.ctrlKey || e.altKey || e.metaKey) return;
      if (isFormTarget(e.target)) return;
      const k = String(e.key || '');
      if (!live || !live.question) return;
      /* the exits and the chrome keys are never impatience */
      if (CHROME_KEYS.indexOf(k) >= 0) return;
      if (/^[1-9]$/.test(k)) {
        const i = Number(k) - 1;
        if (i < live.question.options.length) {
          try { if (e.preventDefault) e.preventDefault(); } catch (err) { /* noop */ }
          answer(i, 'key');
          return;
        }
      }
      /* anything else, while a card is up, is impatience */
      bumpRefused();
      guardPoke();
    }
    function onStagePointer(e) {
      if (!onOptionNode(e && (e.target || e.currentTarget))) bumpRefused();
      guardPoke();
    }
    function bindInput() {
      try { if (typeof window !== 'undefined') window.addEventListener('keydown', onKeyDown); }
      catch (e) { say('keydown bind failed: ' + ((e && e.message) || e)); }
      try { if (stopEl) stopEl.addEventListener('pointerdown', onStagePointer); } catch (e) { /* noop */ }
    }
    function unbindInput() {
      try { if (typeof window !== 'undefined') window.removeEventListener('keydown', onKeyDown); }
      catch (e) { /* noop */ }
      try { if (stopEl) stopEl.removeEventListener('pointerdown', onStagePointer); } catch (e) { /* noop */ }
    }

    /* ==================================================================== *
     * THE CLOCK
     * ==================================================================== */
    function startClock() {
      lastTick = Date.now();
      clockId = every(PLAYTEST.CLOCK_TICK_MS, () => {
        if (ended) return;
        const now = Date.now();
        const dt = now - lastTick;
        lastTick = now;
        /* THE CLASS CLOCK KEEPS RUNNING THROUGH A FREEZE. The montage stops;
         * the budget does not. That is what makes the final-stop guarantee a
         * guarantee rather than a hope. */
        elapsedMs += dt / Math.max(0.0001, timeScale);
        paintHud();
        if (!live) {
          recomputeBand();
          recomputeHeat();
          stallMs += PLAYTEST.CLOCK_TICK_MS;
          if (stallMs - lastStallFire >= PLAYTEST.STALL_TICK_MS) {
            lastStallFire = stallMs;
            deck('trickster', 'stalled', stallMs);
          }
        }
        const left = Math.max(0, (budgetMs - elapsedMs) / 1000);
        if (!bellOn && left <= PLAYTEST.BELL_WARN_SEC) {
          bellOn = true;
          deck('casino', 'bell', true);
          /* W3 P0-3: the bell vocabulary. This class's last-stretch warning had
           * no sound at all. One school, one bell: warn .3, the end .5. */
          tick('bell', 0.3, { pitch: 1 });
          if (!live) msg('ir_bell_warn', IR_LEX.ir_bell_warn);
        }
        const stop = nextStop();
        if (stop && !live) {
          if (stop.announced && elapsedMs >= stop.atMs - PLAYTEST.BELL_LEAD_MS) warnStop(stop);
          if (elapsedMs >= stop.atMs) { run(beginStop); return; }
        }
        /* The budget can only run out with no stop pending when the schedule
         * was degenerate (a harness budget shorter than one window). The class
         * still ends warm rather than hanging. */
        if (elapsedMs >= budgetMs && !live && stopIdx >= plan.stops.length) { run(() => finish(true)); }
      });
    }
    function stopClock() { if (clockId) { clearTimer(clockId); clockId = 0; } }

    /* ==================================================================== *
     * THE END
     * ==================================================================== */
    function stopAmbience() {
      for (const k of ['bubble_field', 'gif_rain', 'ambient_field', 'crt']) stopSafe(k);
      /* trap 33: a wash is stepped DOWN, never stop('wash')'d - that would black
       * out every wash kind at once, including anything a deck still holds. */
      for (const v of ['spiral', 'pink', 'drain']) sustainSafe('wash', { variant: v, alpha: 0.01, holdMs: 400 });
    }

    function finish(viaBell) {
      if (ended) return;
      ended = true;
      halted = true;
      stopClock();
      clearChannels();
      stopWindow();
      stopGoldHum();                    // W3 P2-5: the run's dressing never outlives the class
      stopAmbience();
      /* W3 P0-3: the class ends on the bell, and the debrief's own `slide`
       * follows it 420ms later (renderEnd) instead of sharing the frame. */
      tick('bell', 0.5, { pitch: 1 });
      /* the bell's freeze is HOUSEKEEPING, not a stop: no shutter (the debrief
       * slide is the beat here, and two cues on one frame is a smear). */
      if (montage) { montage.stopGovernor(); montage.stop(); montage.freeze(true, { silent: true }); }
      deck('trickster', 'stop');
      deck('casino', 'dimOut');
      deck('casino', 'stop');
      deck('pressure', 'stop');
      paintHud();                       // truth on every chip, whatever the trickster left
      stopEl.hidden = true;
      setPhase('ended');

      const plantsEnabled = !reduced && tier >= 3 && plan.plantCount > 0;
      const graded = compositeFor({
        gradeTier: tier,
        questions: results.questions,
        stops: results.stops,
        plantExposures,
        plantsResisted,
        plantsEnabled,
        correctedWeight,
        bestRun,
      });
      const gates = hardGates(results.stops);
      const cleanStops = results.stops.filter((s) => s.fullyCorrect).length;
      const fx = flavorXp(plantsResisted, cleanStops);

      try {
        const prior = (ctx.store && typeof ctx.store.gameMeta === 'function')
          ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
        ctx.store.mergeGameMeta(GAME_KEY, {
          vigils: Math.max(0, Number(prior.vigils) || 0) + 1,
          bestRun: Math.max(Math.max(0, Number(prior.bestRun) || 0), bestRun),
          plantsTaken: Math.max(0, Number(prior.plantsTaken) || 0) + (plantExposures - plantsResisted),
          lastSeed: seed,
          lastPlayedAt: Date.now(),
        });
      } catch (e) { say('meta write failed (class unaffected): ' + ((e && e.message) || e)); }

      renderEnd(graded);

      const report = { metrics: { composite: graded.composite }, hardGates: gates, flavorXp: fx };
      lastReport = Object.assign({}, report, {
        inputs: {
          tier, seed, retake, reduced, viaBell: !!viaBell,
          stops: results.stops.slice(), questions: results.questions.slice(),
          plantExposures, plantsResisted, plantsFired, plantsEnabled,
          correctedWeight, bestRun, timeouts, voidedStops, emissions, jackpots,
          terms: graded.terms, capped: graded.capped, elapsedMs,
        },
      });
      try { lastSnapshot = instance.snapshot(); } catch (e) { /* diagnostics only */ }
      say('vigil over: ' + cleanStops + '/' + results.stops.length + ' clean stops, '
        + results.questions.filter((q) => q.correct).length + '/' + results.questions.length + ' correct, '
        + timeouts + ' blanked, plants ' + plantsResisted + '/' + plantExposures
        + ' -> composite ' + graded.composite.toFixed(3) + (graded.capped ? ' (timeout cap)' : ''));

      after(reduced ? PLAYTEST.END_HOLD_MS_REDUCED : PLAYTEST.END_HOLD_MS, () => {
        if (reported) return;
        reported = true;
        try { ctx.endClass(report); } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
      });
    }

    function renderEnd(graded) {
      if (!endEl) return;
      endEl.textContent = '';
      endEl.hidden = false;
      /* THE DEBRIEF (W2 chrome). Every row is appended in THIS frame and the
       * card fades in as one object (.g-ir-end / g-ir-endin), so there is no
       * visual stagger for a blip ladder to ride: the House Book's answer to an
       * unstaggered debrief is ONE `slide`, on the same beat as the fade. */
      /* W3 P0-3: +420ms, so the bell finish() struck has the frame to itself. */
      after(420, () => { tick('slide', 0.35, { pitch: 1 }); });
      endEl.appendChild(el('h3', 'g-ir-end-title', t('ir_end_title', IR_LEX.ir_end_title)));
      const row = (k, v, cls) => {
        const r = el('div', 'g-ir-end-row' + (cls ? ' ' + cls : ''));
        r.appendChild(el('span', 'g-ir-end-k', k));
        r.appendChild(el('span', 'g-ir-end-v', v));
        endEl.appendChild(r);
        return r;
      };
      const clean = results.stops.filter((s) => s.fullyCorrect).length;
      row(t('ir_end_stops', IR_LEX.ir_end_stops), clean + ' / ' + results.stops.length);
      row(t('ir_end_accuracy', IR_LEX.ir_end_accuracy), Math.round(graded.terms.accuracy * 100) + '%');
      const answered = results.questions.filter((q) => !q.timedOut && !q.voided);
      const meanMs = answered.length
        ? Math.round(answered.reduce((n, q) => n + q.latencyMs, 0) / answered.length) : 0;
      row(t('ir_end_latency', IR_LEX.ir_end_latency), meanMs ? (meanMs / 1000).toFixed(1) + 's' : t('ir_end_none', IR_LEX.ir_end_none));
      row(t('ir_end_streak', IR_LEX.ir_end_streak), String(bestRun));
      /* HOW MANY DIFFERENT THINGS THE CLASS ACTUALLY ASKED, over how many it
       * could have. This row exists because the variety rework's whole failure
       * mode was invisible: a class that dealt ten families and asked one. */
      const askedKinds = new Set(results.questions.map((q) => q.template));
      row(t('ir_end_kinds', IR_LEX.ir_end_kinds),
        askedKinds.size + ' / ' + (plan ? plan.templates.length : 0));
      if (plantExposures > 0) row(t('ir_end_plants', IR_LEX.ir_end_plants), plantsResisted + ' / ' + plantExposures, 'g-ir-end-plants');
      if (timeouts > 0) row(t('ir_end_timeouts', IR_LEX.ir_end_timeouts), String(timeouts), 'g-ir-end-blank');
      endEl.appendChild(el('p', 'g-ir-end-line', t('ir_end_line', IR_LEX.ir_end_line)));
    }

    /* ---- assets (never block a draw) ------------------------------------ */
    function claimAssets() {
      Promise.resolve()
        .then(() => ctx.assets.claim({ loops: 24, targets: 0, stills: 24, canvasSafe: false }))
        .then((p) => {
          if (dead || !p || typeof p.next !== 'function') return;
          pool = p;
          run(() => { if (montage) { montage.dress(pool); montage.setBand(band); } });
        })
        .catch((e) => say('asset claim failed - the wall runs on the placeholder floor: ' + ((e && e.message) || e)));
    }

    /* ---- the decks ------------------------------------------------------ */
    function buildDecks(mods) {
      if (dead) return;
      const capsOk = capsArmed();
      try {
        if (mods.style && typeof mods.style.injectInstantRecallStyle === 'function') {
          mods.style.injectInstantRecallStyle();
        }
      } catch (e) { say('style inject failed (class unaffected): ' + ((e && e.message) || e)); }
      /* THE CLASS'S SPIRALS. Four of them, drawn kin-first off the class seed
       * so the decoys on a SPIRAL card are look-alikes rather than obviously
       * different arms - and preloaded, because a preview that has not decoded
       * by the time the slip lands is a blank option. */
      try {
        if (mods.spirals && typeof mods.spirals.buildSpiralSet === 'function') {
          spirals = mods.spirals;
          const built = spirals.buildSpiralSet({
            pool: spiralPool(), seed, ringSize: PLAYTEST.SPIRAL_RING[tier],
          });
          if (built && Array.isArray(built.set)) {
            spiralSet = { set: built.set.slice(), ring: (built.ring || built.set).slice(), kin: Object.assign({}, built.kin || {}) };
            /* THE WOVEN ROW (Loom directive 2026-08-25). The shell ships the
             * generated class loom as a URL-LESS pool row, so buildSpiralSet's
             * string reader skipped it by design; loomRowsOf is the read that
             * takes it. It LEADS the ring - it is the very spiral the shell's
             * own washes wear - and its id joins the set so it can stand as a
             * decoy on a gif question. The emitter below unwraps ring rows. */
            if (typeof spirals.loomRowsOf === 'function') {
              try {
                const woven = (spirals.loomRowsOf(spiralPool()) || [])[0] || null;
                if (woven && woven.id && woven.params) {
                  loomSpiralRow = woven;
                  loomParamsById.set(woven.id, woven.params);
                  spiralSet.ring.unshift(woven);
                  if (spiralSet.set.indexOf(woven.id) < 0) spiralSet.set.push(woven.id);
                  spiralSet.kin[woven.id] = 'loom';
                }
              } catch (e) { /* the woven row is a nicety; the gif ring stands */ }
            }
            if (typeof spirals.preloadSpirals === 'function') {
              // 'loom:' ids are params hashes, not fetchable urls - never Image.src them
              try { spirals.preloadSpirals(spiralSet.set.filter((u) => !isLoomId(u))); } catch (e) { /* best effort */ }
            }
          }
        }
      } catch (e) { spirals = null; say('spirals refused: ' + ((e && e.message) || e)); }
      try {
        if (mods.casino && typeof mods.casino.createIrCasino === 'function') {
          casino = mods.casino.createIrCasino({
            seed, tier, stage, montage: montageEl, hud, backdrop, stopEl,
            timers: deckTimers, reduced, capsOk, t, engine: deckEngine, log: say,
          }) || null;
        }
      } catch (e) { casino = null; say('casino refused: ' + ((e && e.message) || e)); }
      try {
        if (mods.trickster && typeof mods.trickster.createIrTrickster === 'function') {
          trickster = mods.trickster.createIrTrickster({
            seed, tier, stopEl, timers: deckTimers, reduced, capsOk,
            isHalted: () => dead || paused || ended,
            t,
            stats: () => ({
              stops: stopsResolved, total: plan ? plan.stopCount : 0, streak,
              band, secLeft: Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)),
              frozen: !!live, layout,
            }),
            chipEl: (which) => (which === 'clock' ? clockChip : which === 'stops' ? stopsChip
              : which === 'timer' ? timerEl : densityChip),
            chipText,
            /* THE OPTION LABELS are the trickster's Unreliable Label surface -
             * it may wear another option's text and must snap back to truth
             * before the window's last 40%. The VALUES never move (Law I/II):
             * these accessors read and repaint TEXT only. */
            optEls: () => optEls.slice(),
            optText: (i) => {
              const q = live && live.question;
              return q && q.options[i] ? q.options[i].label : '';
            },
            windowLeft: () => (live ? live.windowLeft : 0),
            /* THE CUE ROAD (W2 sec 2). The deck never gets the engine - it gets
             * this class's own clamped helper, so every cue it asks for lands
             * under the tier's audio ceiling ({0.32,0.38,0.44,0.50}). */
            cue: (name, level, extra) => tick(name, level, extra),
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
        }
      } catch (e) { trickster = null; say('trickster refused: ' + ((e && e.message) || e)); }
      try {
        if (mods.pressure && typeof mods.pressure.createIrPressure === 'function') {
          pressure = mods.pressure.createIrPressure({
            seed, gradeTier: tier, reduced,
            motionLevel: (ctx.motion && Number.isFinite(ctx.motion.motionLevel)) ? ctx.motion.motionLevel : 2,
            stage, montage: montageEl, hud,
            chrome: [hud, clockChip, stopsChip, densityChip, msgEl].filter(Boolean),
            engine: deckEngine, assets: deckAssets, timers: deckTimers,
            capsOk: capsArmed, log: say,
          }) || null;
        }
      } catch (e) { pressure = null; say('pressure refused: ' + ((e && e.message) || e)); }

      if (!ended && !dead) {
        deck('casino', 'start');
        deck('pressure', 'start');
        deck('trickster', 'start');
        deck('casino', 'layoutChange', layout);
        recomputeHeat();
      }
    }

    /* ==================================================================== *
     * THE MODULE INSTANCE
     * ==================================================================== */
    const instance = {
      start(classSpec) {
        spec = classSpec || { gradeTier: 1, seed: GAME_KEY + '|none', timeBudgetSec: 180 };
        tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        seed = String(spec.seed == null ? GAME_KEY : spec.seed);
        retake = !!spec.retake;
        budgetMs = Math.max(20000, (Number(spec.timeBudgetSec) || 180) * 1000);
        reduced = probeReduced(ctx);
        const densityValue = ctx.settings ? ctx.settings.ir_density : 'standard';

        /* THE WALL IS BUILT BEFORE THE PLAN, because the plan has to know
         * whether it may deal a WALL family at all: `wallOk` is a presence
         * check on LOT B's `montage.snapshot`, and a build without that surface
         * must never deal a card it can never read. Nothing here needs `plan`
         * (the stops chip repaints from `paintHud()` a few lines down). */
        buildDom();
        ledger = createLedger({ node: ledgerEl });
        montage = createMontage({
          mount: montageEl,
          /* THE SHUTTER'S ROAD (W2 sec 2). montage.js gets no engine - it gets
           * this class's own clamped helper, the way impulse-control hands
           * render.js its `sting`. It is used for exactly ONE beat: the freeze.
           * It is not a ledger verb and it fires no POOL primitive, so neither
           * of the wall's two inherited laws is touched. */
          cue: (name, level, extra) => tick(name, level, extra),
          seed, tier, reduced,
          coarse: !!(ctx.platform && ctx.platform.isTouch),
          /* LITE ENGAGES ON TOUCH TOO (mobile web, ../CLAUDE.md trap 42): a
           * phone at the default motion level was getting the desktop dials
           * (LIVE_LOOP_CAP 12 / VIDEO_TILE_CAP 4), and iOS caps hardware video
           * decode sessions at ~3-4 - so the wall now STARTS from the 6/2 lite
           * dials on a coarse pointer instead of the governor catching the
           * fire late. Same signal as `coarse` above; desktop WebView2 answers
           * isTouch false and keeps the motionLevel test byte-identical. */
          lite: !!((ctx.motion && Number(ctx.motion.motionLevel) <= 1)
            || (ctx.platform && ctx.platform.isTouch)),
          density: densityMultFor(densityValue),
          timeScale,
          log: say,
        });

        /* WHAT TONIGHT'S MATERIAL CAN ACTUALLY BE ASKED ABOUT. A family whose
         * material does not exist is dropped at PLAN time, not fallen out of at
         * stop time - the whole point of the variety rework. */
        plan = buildVigil({
          seed, gradeTier: tier, timeBudgetSec: budgetMs / 1000,
          density: densityValue, reduced,
          /* an option the player cannot hear is a coin flip, so an inaudible
           * class simply never deals the whisper */
          audible: ctx.audioAudible !== false,
          wordCount: wordPool().length,
          clipCount: clipRows().length,
          spiralCount: spiralPool().length,
          wallOk: wallOk(),
        });
        const broken = assertPlan(plan);
        if (broken.length) say('PLAN INVARIANT BROKEN: ' + broken.join('; '));

        qroll = makeTaggedRoll(seed + '|ir-quiz');
        rewardLocal = (() => {
          const roll = makeTaggedRoll(seed + '|ir-vr');
          return () => {
            const chance = Math.min(1, 0.30 + 0.30 * currentHeat + Math.min(3, streak) * 0.04);
            const r = roll('fire');
            const fire = r < chance;
            return { fire, jackpot: fire && roll('jack') >= 0.85, nearMiss: !fire && r < chance + 0.08 };
          };
        })();

        montage.build();
        band = plan.densityFloor;
        montage.setBand(band);
        paintHud();

        bindInput();
        claimAssets();
        loadDecks().then((mods) => run(() => buildDecks(mods)))
          .catch((e) => say('decks unavailable (class runs plain): ' + ((e && e.message) || e)));

        msg(tier === 1 ? 'ir_brief_bell' : 'ir_brief',
          tier === 1 ? IR_LEX.ir_brief_bell : IR_LEX.ir_brief);

        /* the class-rules sheet first, then the vigil opens */
        howto(() => {
          if (dead || ended) return;
          setPhase('vigil');
          msg('ir_vigil_hint', IR_LEX.ir_vigil_hint);
          segStartMs = 0;
          elapsedMs = 0;
          halted = false;
          lastEmitAt = -Infinity;
          seedDealer();
          armDealer();
          armWallPlant();
          startClock();
          montage.start();
          montage.startGovernor();
          after(reduced ? PLAYTEST.BRIEF_MS_REDUCED : PLAYTEST.BRIEF_MS, () => {
            if (msgEl && !live) msgEl.textContent = '';
          });
        });

        liveClass = instance;
        lastReport = null;
        lastSnapshot = null;
        say('tier ' + tier + ', ' + plan.stopCount + ' stops (' + plan.stops.map((s) => Math.round(s.atMs / 1000) + 's'
          + (s.announced ? '' : '!')).join(' ') + '), ' + plan.windowMs + 'ms windows, '
          + plan.qPerStop + ' q/stop, pool [' + plan.pool.join(' ') + '], density ' + densityValue
          + ' ceiling ' + plan.densityCeil.toFixed(2)
          + ', plants ' + plan.plantCount + (reduced ? ', reduced' : '') + (retake ? ', RETAKE' : ''));
        say('families [' + plan.templates.join(' ') + ']'
          + (Object.keys(plan.templateDrops).length
            ? ' dropped ' + Object.keys(plan.templateDrops).map((k) => k + '=' + plan.templateDrops[k]).join(' ')
            : '')
          + ', material words ' + wordPool().length + ' clips ' + plan.clipCount
          + ' spirals ' + plan.spiralCount + ' wall ' + (plan.wallOk ? 'yes' : 'no'));
      },

      pause() {
        if (paused) return;
        paused = true;
        deck('pressure', 'pause');
        if (stage) stage.classList.add('suspended');
      },

      resume() {
        if (!paused) return;
        paused = false;
        if (stage) stage.classList.remove('suspended');
        deck('pressure', 'resume');
        lastTick = Date.now();
        const q = deferred.splice(0);
        for (const fn of q) run(fn);
      },

      /** The shell owns the overlay and the engine's suspend; we just freeze. */
      suspend(on) { if (on) instance.pause(); else instance.resume(); },

      destroy() {
        dead = true;
        stopClock();
        clearChannels();
        stopGoldHum();                  // W3 P2-5: every hold has an owner (trap 108)
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
        try { if (montage) montage.destroy(); } catch (e) { /* noop */ }
        montage = null;
        if (pool && typeof pool.release === 'function') { try { pool.release(); } catch (e) { /* noop */ } }
        pool = null;
        live = null;
        optEls = [];
        payoutEl = null;               // removed with the stage below
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
        if (liveClass === instance) liveClass = null;
      },

      /* -------- test / diagnostics seams (never read by the shell) -------- */
      /** Answer as the player would. */
      answer(i) { return answer(i, 'tap'); },
      /** Drive the class clock forward without a wall clock (the harness). */
      advance(ms) {
        elapsedMs += Math.max(0, Number(ms) || 0);
        paintHud();
        if (!live) { recomputeBand(); recomputeHeat(); }
        const stop = nextStop();
        if (stop && !live && elapsedMs >= stop.atMs) beginStop();
      },
      /** Emit one POOL KEY as the dealer would (the harness fills the tail). */
      emitKind(key) {
        return plan && plan.pool.indexOf(key) >= 0 ? emitPool(key) : null;
      },
      /** The dealer's live view: due times + the last emission (the 700ms rule). */
      dealer() { return { due: Object.assign({}, due), lastEmitAt, armed: !!dealerTimer }; },
      ledger() { return ledger; },
      plan() { return plan; },
      montage() { return montage; },
      liveQuestion() { return live ? live.question : null; },
      chipText(which) { return chipText(which); },
      forceEnd() { finish(false); },

      snapshot() {
        return {
          tier, seed, retake, reduced, budgetMs, elapsedMs,
          phase: stage ? stage.getAttribute('data-phase') : null,
          layout, band, heat: currentHeat, halted, paused, ended, reported, dead,
          stopIdx, stopsResolved, streak, bestRun, easedNext,
          plan: plan ? {
            stopCount: plan.stopCount, windowMs: plan.windowMs, qPerStop: plan.qPerStop,
            stops: plan.stops.map((s) => ({ n: s.n, atMs: s.atMs, announced: s.announced, plant: !!s.plant })),
            densityCeil: plan.densityCeil, densityFloor: plan.densityFloor,
            pool: plan.pool.slice(), audible: plan.audible,
            plantCount: plan.plantCount,
            templates: plan.templates.slice(), templateDrops: plan.templateDrops,
          } : null,
          /* WHAT WAS DEALT vs WHAT WAS ASKED - the one diagnostic that would
           * have caught the "it only asks about the subliminals" report. */
          families: (() => {
            const dealtF = {};
            const askedF = {};
            if (plan) for (const s of plan.stops) for (const q of s.questions) dealtF[q.template] = (dealtF[q.template] || 0) + 1;
            for (const q of results.questions) askedF[q.template] = (askedF[q.template] || 0) + 1;
            return { dealt: dealtF, asked: askedF };
          })(),
          wallBook: wallBook.slice(-4).map((b) => ({
            gen: b.gen,
            painted: b.snapshot ? b.snapshot.painted : 0,
            dups: (b.snapshot && Array.isArray(b.snapshot.dups)) ? b.snapshot.dups.length : 0,
            plant: b.snapshot ? b.snapshot.plant : null,
          })),
          spiralSet: { set: spiralSet.set.slice(), ring: spiralSet.ring.slice() },
          quench: quenchCount,
          ledger: ledger ? ledger.all() : [],
          emissions,
          questions: results.questions.slice(),
          stops: results.stops.slice(),
          plantExposures, plantsResisted, plantsFired, timeouts, voidedStops,
          correctedWeight, correctionPending, jackpots,
          live: live ? {
            stop: live.stop.n, qIdx: live.qIdx, asked: live.askedCount,
            correct: live.correctCount, windowLeft: live.windowLeft,
            template: live.question ? live.question.template : null,
            trueIndex: live.question ? live.question.trueIndex : -1,
            media: live.question ? !!live.question.media : false,
            options: live.question ? live.question.options.map((o) => o.label || o.value) : [],
            plantMatch: live.plantMatch,
          } : null,
          montage: montage ? montage.diagnostics() : null,
          casino: casino && typeof casino.diagnostics === 'function' ? (() => { try { return casino.diagnostics(); } catch (e) { return null; } })() : null,
          trickster: trickster && typeof trickster.diagnostics === 'function' ? (() => { try { return trickster.diagnostics(); } catch (e) { return null; } })() : null,
          pressure: pressure && typeof pressure.diagnostics === 'function' ? (() => { try { return pressure.diagnostics(); } catch (e) { return null; } })() : null,
          stage, montageEl, ledgerEl, stopEl, qEl, optsEl, timerEl, msgEl, endEl, hud, well, qFaceEl,
          clockChip, stopsChip, densityChip, optEls: optEls.slice(),
        };
      },
    };
    return instance;
  },

  /** The live class's state, or null. Never read by the shell. */
  diagnostics() { return liveClass ? liveClass.snapshot() : null; },
  get lastReport() { return lastReport; },
  get lastSnapshot() { return lastSnapshot; },
  setTimeScale,
};
