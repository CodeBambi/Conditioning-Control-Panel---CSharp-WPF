/* ============================================================================
 * games/instant-recall/index.js - INSTANT RECALL, "the vigil" (family: recall).
 *
 * One class is ONE continuous 120-second WALL. A full-bleed mosaic of the
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
 *   IV   IMAGES OVER TEXT. The class-rules sheet is drawn (style.js) and
 *        dismissed by GO only, once per grade tier under `ctx.hideTutorial`.
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
  densityMultFor, PLAYTEST, POOL_KEYS, POOL_BY_KEY, PULSE_MS, STINGS,
} from './vigil.js';
import { createMontage, createLedger, hideTruthNode } from './montage.js';
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

/** The four creative modules, loaded optionally. Never a hard import. */
function loadDecks() {
  return Promise.allSettled([
    import('./style.js'),
    import('./casino.js'),
    import('./trickster.js'),
    import('./pressure.js'),
  ]).then((r) => ({
    style: r[0].status === 'fulfilled' ? r[0].value : null,
    casino: r[1].status === 'fulfilled' ? r[1].value : null,
    trickster: r[2].status === 'fulfilled' ? r[2].value : null,
    pressure: r[3].status === 'fulfilled' ? r[3].value : null,
  }));
}

export default {
  key: GAME_KEY,
  family: 'recall',
  /* MEATY (owner ruling). Not because 120s is long - it is not - but because
   * the ten-game pool re-opened web CLAUDE.md trap 6: no-repeat-3 outranks the
   * meaty preference, so TWO meaty classes left roughly a third of dealt nights
   * with no meaty slot filled at all. A third meaty class closes the gap. The
   * budget, the family and the flagship flag are unchanged - and this file's
   * behaviour does not branch on `meaty` anywhere: it is a timetable fact, not
   * a difficulty one. `games/registry.js` GAME_META must mirror it (the
   * parachute is read for a suspended class too). */
  meaty: true,
  flagship: false,
  timeBudgetSec: 120,
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
    let budgetMs = 120000;
    let pool = null;
    let montage = null;
    let ledger = null;
    let qroll = null;
    let rewardLocal = null;

    let casino = null;
    let trickster = null;
    let pressure = null;

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
    let endEl = null; let howtoEl = null;
    let clockChip = null; let stopsChip = null; let densityChip = null;
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
     * THE CLASS-RULES SHEET (Law IV: drawn, GO-only, once per tier)
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
     * The sheet: three drawn panels and a GO button. Policy is the shell's
     * "Skip class tutorials" contract - default shows every class; with the
     * skip on, a class still explains itself ONCE per grade tier. Dismissal is
     * the sheet's own button only.
     */
    function howto(onDone) {
      if (ctx.hideTutorial === true && howtoSeenTiers().indexOf(tier) >= 0) { onDone(); return; }
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
    /** Re-seed every pool key's due time from the current band. */
    function seedDealer() {
      if (!plan) return;
      for (const key of plan.pool) {
        if (ringAt[key] == null) ringAt[key] = 0;
        /* the first due of a segment is a FRACTION of the cadence so the wall
         * does not go quiet for a full period every time a stop resolves */
        due[key] = elapsedMs + Math.round(cadenceMs(key, band, nextJitter(key)) * (0.25 + 0.75 * nextJitter(key)));
      }
    }
    function armDealer() {
      if (!plan || dead || ended) return;
      if (dealerTimer) { clearTimer(dealerTimer); dealerTimer = 0; }
      const pick = nextEmission(due, lastEmitAt, elapsedMs);
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
          r = fireSafe('flash_burst', { variant, count: Math.max(1, Math.round(1 + 3 * band)) });
          if (r) note('flash', {}, r.variant || variant);
          break;
        }
        case 'subliminal': {
          const hasWords = wordPool().length > 0;
          r = fireSafe('sub_flash', { variant, anchor: well, image: hasWords ? false : true });
          if (r) note('subliminal', r.text ? { word: String(r.text) } : { assetId: 'card' }, r.variant || variant);
          break;
        }
        case 'whisper': {
          r = tick(variant, 0.3 + 0.18 * band);
          if (r) note('whisper', { sting: variant }, variant);
          break;
        }
        case 'corner_gif': {
          /* the classic corner GIF: ONE loop pinned to a corner of the stage
           * for a beat. `count:1` + x/y is the engine's own placement seam -
           * no new primitive, no game-owned decoder. */
          const at = CORNERS[variant] || CORNERS.tl;
          r = fireSafe('gif_burst', {
            count: 1, variant: 'pop', x: at.x, y: at.y,
            sizePx: reduced ? 150 : 210, holdMs: PULSE_MS.corner_gif,
            assetKind: reduced ? 'still' : 'loop',
          });
          if (r) note('corner_gif', { assetId: 'corner:' + variant }, variant);
          break;
        }
        case 'fullscreen_gif': {
          /* THE WHOLE STAGE, for a beat, then gone. `fullBleed` is the engine's
           * additive cover option (engine/oneshots.js) - one node, no
           * transform, object-fit:cover over the layer. */
          r = fireSafe('gif_burst', {
            count: 1, fullBleed: true, holdMs: PULSE_MS.fullscreen_gif,
            assetKind: reduced ? 'still' : 'loop',
          });
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
        case 'spiral':
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

    function beginStop() {
      const stop = nextStop();
      if (!stop || live) return;
      stopIdx += 1;
      halted = true;
      clearChannels();
      if (montage) montage.freeze(true);
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
      after(240, () => { if (live) dealQuestion(0); });
    }

    /* ---- what the tail can be asked ------------------------------------ */
    function availability() {
      /* A PLANT IS NEVER A TRUTH. It fired, it is in the ledger, and the
       * gotcha replay reads it - but it lives on its own `plant` channel and is
       * filtered out of every truth read, so a false memory can never become
       * the answer key to the question it was planted against. */
      const words = ledger.recent((r) => r.channel !== 'plant' && r.payload && r.payload.word, 24);
      const stings = ledger.recent((r) => r.channel === 'whisper' && r.payload.sting, 12);
      const effects = ledger.recent((r) => POOL_KEYS.indexOf(r.channel) >= 0, 12);
      const excl = PLAYTEST.DISTRACTOR_EXCLUDE;

      const recentWords = new Set(words.slice(0, excl).map((r) => r.payload.word));
      const wordDistractors = new Set();
      for (const r of words) if (!recentWords.has(r.payload.word)) wordDistractors.add(r.payload.word);
      for (const w of wordPool()) if (!recentWords.has(w)) wordDistractors.add(w);

      const recentStings = new Set(stings.slice(0, excl).map((r) => r.payload.sting));
      const stingDistractors = STINGS.filter((s) => !recentStings.has(s));

      /* A LAST_EFFECT option must be REACHABLE at this tier: an effect that can
       * never fire is not a distractor, it is a freebie the player can strike
       * off by construction. `plan.pool` is exactly tonight's ten (or four). */
      const recentFx = new Set(effects.slice(0, excl).map((r) => r.channel));
      const reachable = plan ? plan.pool : POOL_KEYS.slice();
      const fxDistractors = reachable.filter((k) => !recentFx.has(k));

      return {
        words: wordDistractors.size,
        wordList: Array.from(wordDistractors),
        hasWord: words.length >= 1,
        hasTwoWords: words.length >= 2,
        wordTail: words,
        stings: stingDistractors.length,
        stingList: stingDistractors,
        stingTail: stings,
        hasSting: stings.length >= 1,
        audible: ctx.audioAudible !== false,
        effects: fxDistractors.length,
        fxList: fxDistractors,
        fxTail: effects,
        hasEffect: effects.length >= 1,
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
          meta: meta || {},
        };
      };
      if (template === 'LAST_WORD') {
        const truth = avail.wordTail[0].payload.word;
        const ds = pickN(avail.wordList, 3, 'word');
        if (ds.length < 3) return null;
        const opts = [{ value: truth, label: truth }].concat(ds.map((w) => ({ value: w, label: w })));
        return mk('ir_q_last_word', IR_LEX.ir_q_last_word, opts, truth, { kind: 'word' });
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
        const outs = pickN(avail.wordList, 4, 'two');
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
      const template = resolveTemplate(dealtQ.template, avail, tier);
      const question = template ? buildQuestion(template, avail) : null;
      if (!question || question.trueIndex < 0) {
        /* The tail can answer nothing: a question is NEVER invented. The stop
         * simply resumes, uncounted - it cannot cost a grade it never asked. */
        say('stop ' + stop.n + ' q' + qi + ': the tail could answer nothing - skipped');
        after(400, () => { if (live) dealQuestion(qi + 1); });
        return;
      }
      question.dealtTemplate = dealtQ.template;
      /* Every template is worth the same now that MODE (the furniture question)
       * is gone; the WINDOW still weights it, so a 4s question is worth 1.5x a
       * 6s one and a tier-4 class is not silently easier to ace. */
      question.weight = 6000 / stop.windowMs;
      question.windowMs = stop.windowMs;
      live.question = question;
      live.askedCount += 1;
      renderQuestion(question);
      startWindow(question);
      if (qi === 0) armPlant(stop, question);
    }

    function renderQuestion(question) {
      stopEl.hidden = false;
      truthEl.hidden = true;
      truthEl.textContent = '';
      qEl.textContent = question.text;
      optsEl.textContent = '';
      optEls = [];
      question.options.forEach((opt, i) => {
        const b = el('button', 'g-ir-opt');
        b.setAttribute('type', 'button');
        b.setAttribute('data-i', String(i));
        const num = el('span', 'g-ir-opt-n', String(i + 1));
        num.setAttribute('aria-hidden', 'true');
        b.appendChild(num);
        b.appendChild(el('span', 'g-ir-opt-t', opt.label));
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
      msg(question.options.length >= 4 ? 'ir_answer_hint' : 'ir_answer_hint3',
        question.options.length >= 4 ? IR_LEX.ir_answer_hint : IR_LEX.ir_answer_hint3);
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
      const step = 100;
      live.windowTimer = every(step, () => {
        if (!live || !live.question) return;
        live.elapsedInWindow += step;
        live.windowLeft = Math.max(0, question.windowMs - live.elapsedInWindow);
        if (timerEl) {
          timerEl.style.setProperty('--ir-left', (live.windowLeft / question.windowMs).toFixed(3));
          timerEl.textContent = Math.ceil(live.windowLeft / 1000) + '';
        }
        if (live.windowLeft <= 0) commit(-1, 'timeout');
      });
    }
    function stopWindow() {
      if (live && live.windowTimer) { clearTimer(live.windowTimer); live.windowTimer = 0; }
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
      const usable = wrong.filter(({ o }) => (
        stop.plant.channel === 'whisper' ? !!o.sting : (kind === 'word' || kind === 'pair')
      ));
      if (!usable.length) return;
      const pick = usable[Math.floor(qroll('plant') * usable.length)];
      live.plantTimer = after(stop.plant.atMs, () => {
        if (!live || !live.question || dead || ended) return;
        live.plantTimer = 0;
        let r = null;
        let payload = null;
        if (stop.plant.channel === 'whisper') {
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
        dealt: q.dealtTemplate,
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

      renderVerdict(q, index, { correct, timedOut, voided, decoyHit });

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
      tick(how.correct ? 'stamp' : 'stamp_bad', 0.4);

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
        bead.textContent = p.word != null ? p.word
          : p.sting != null ? t('ir_sting_' + p.sting, IR_LEX['ir_sting_' + p.sting] || p.sting)
            : t('ir_fx_' + rec.channel, IR_LEX['ir_fx_' + rec.channel] || rec.channel);
        if (p.plant) bead.classList.add('is-plant');
        if (rec === pin) bead.classList.add('is-pin');
        ribbon.appendChild(bead);
      }
      truthEl.appendChild(ribbon);

      let line = t('ir_truth', IR_LEX.ir_truth);
      if (how.decoyHit) line = t('ir_gotcha', IR_LEX.ir_gotcha);
      else if (!how.correct && !how.timedOut && !how.voided && isNearMiss(q, index)) line = t('ir_near', IR_LEX.ir_near);
      else if (how.voided) line = t('ir_voided', IR_LEX.ir_voided);
      truthEl.appendChild(el('p', 'g-ir-truth-line', line));
      if (msgEl) msgEl.textContent = line;
    }
    /** The ledger entry the question was actually about. */
    function answerMoment(q, tail) {
      const kind = q.meta.kind;
      for (let i = tail.length - 1; i >= 0; i--) {
        const r = tail[i];
        if (r.payload && r.payload.plant) continue;
        if (kind === 'word' || kind === 'pair') { if (r.payload && r.payload.word) return r; }
        else if (kind === 'sting') { if (r.channel === 'whisper') return r; }
        else if (POOL_KEYS.indexOf(r.channel) >= 0) return r;
      }
      return null;
    }
    /** The classic recency error: the chosen word DID flash - just earlier. */
    function isNearMiss(q, index) {
      if (index < 0 || !q.options[index]) return false;
      const kind = q.meta.kind;
      if (kind !== 'word' && kind !== 'pair') return false;
      const word = String(q.options[index].value).split('|')[0];
      return !!ledger.lastOf((r) => r.payload && r.payload.word === word && !r.payload.plant);
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
          rewardBeat();
        } else {
          streak = 0;
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

      if (stopsResolved >= plan.stopCount || stopIdx >= plan.stops.length) {
        after(reduced ? 400 : 900, () => finish(false));
        return;
      }
      resumeVigil();
    }

    function resumeVigil() {
      if (dead || ended) return;
      segStartMs = elapsedMs;
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
      if (streak >= 2) sustainSafe('ambient_field', { kind: 'goldleaf', density: 0.3 + 0.3 * band });
      halted = false;
      seedDealer();
      armDealer();
    }

    /* ---- variable ratio (the shared canon, with a local fallback) -------- */
    function rewardBeat() {
      let outcome = null;
      try {
        if (ctx.engine && typeof ctx.engine.rewardRoll === 'function') outcome = ctx.engine.rewardRoll({}) || null;
      } catch (e) { outcome = null; }
      if (!outcome && rewardLocal) outcome = rewardLocal();
      if (!outcome) return;
      if (outcome.jackpot) {
        jackpots += 1;
        try { ctx.ceremonies.reward('jackpot', { target: stage, text: t('ir_jackpot', IR_LEX.ir_jackpot) }); }
        catch (e) { /* noop */ }
        /* "Photographic Memory": gold leaf over the hall. It is DELIBERATELY
         * not a pool primitive - a gif burst here would be a real Corner GIF /
         * Fullscreen GIF that the ledger never saw, and the very next question
         * would have two honest answers. Dressing pays the ceremony instead. */
        sustainSafe('ambient_field', { kind: 'goldleaf', density: 0.55 });
        tick('jackpot', 0.5);
      } else if (outcome.fire) {
        tick('streak', 0.42);
      }
    }

    /* ==================================================================== *
     * THE ESCAPE GUARD - the freeze never traps.
     * Six impatient interactions inside five seconds while a card is up void
     * the stop: it scores as a miss for that question, the vigil resumes at
     * once, and the class is never failed by it (friction, never lockout).
     * ==================================================================== */
    function guardPoke() {
      if (!live || !live.question) return;
      if (elapsedMs - guardSince > PLAYTEST.ESCAPE_MS) { guardHits = 0; guardSince = elapsedMs; }
      guardHits += 1;
      if (guardHits >= PLAYTEST.ESCAPE_TAPS) {
        guardHits = 0;
        say('escape guard: stop ' + live.stop.n + ' voided');
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
      guardPoke();
    }
    function onStagePointer() { guardPoke(); }
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
      stopAmbience();
      if (montage) { montage.stopGovernor(); montage.stop(); montage.freeze(true); }
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
        spec = classSpec || { gradeTier: 1, seed: GAME_KEY + '|none', timeBudgetSec: 120 };
        tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        seed = String(spec.seed == null ? GAME_KEY : spec.seed);
        retake = !!spec.retake;
        budgetMs = Math.max(20000, (Number(spec.timeBudgetSec) || 120) * 1000);
        reduced = probeReduced(ctx);
        const densityValue = ctx.settings ? ctx.settings.ir_density : 'standard';

        plan = buildVigil({
          seed, gradeTier: tier, timeBudgetSec: budgetMs / 1000,
          density: densityValue, reduced,
          /* an option the player cannot hear is a coin flip, so an inaudible
           * class simply never deals the whisper */
          audible: ctx.audioAudible !== false,
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

        buildDom();
        ledger = createLedger({ node: ledgerEl });

        montage = createMontage({
          mount: montageEl,
          seed, tier, reduced,
          coarse: !!(ctx.platform && ctx.platform.isTouch),
          lite: !!(ctx.motion && Number(ctx.motion.motionLevel) <= 1),
          density: densityMultFor(densityValue),
          timeScale,
          log: say,
        });
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
          } : null,
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
            options: live.question ? live.question.options.map((o) => o.label) : [],
            plantMatch: live.plantMatch,
          } : null,
          montage: montage ? montage.diagnostics() : null,
          casino: casino && typeof casino.diagnostics === 'function' ? (() => { try { return casino.diagnostics(); } catch (e) { return null; } })() : null,
          trickster: trickster && typeof trickster.diagnostics === 'function' ? (() => { try { return trickster.diagnostics(); } catch (e) { return null; } })() : null,
          pressure: pressure && typeof pressure.diagnostics === 'function' ? (() => { try { return pressure.diagnostics(); } catch (e) { return null; } })() : null,
          stage, montageEl, ledgerEl, stopEl, qEl, optsEl, timerEl, msgEl, endEl, hud,
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
