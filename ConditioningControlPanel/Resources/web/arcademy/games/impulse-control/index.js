/* ============================================================================
 * games/impulse-control/index.js - IMPULSE CONTROL: THE DROP TUBE.
 * Family: reflex. Quick class, 90s. BUILD-CONTRACT §11 module.
 *
 * One opaque spiral chute, one bubble at a time. It loads at the mouth, slides
 * down INSIDE the tube (you see a glow, never the kind), and reveals in the
 * concave basin at screen centre:
 *   - a GOOD bubble (flash / spiral / subliminal skin): pop it FAST. Reaction
 *     time from reveal to pop is the score. The pop fires its effect beat and
 *     swaps the faded fullscreen backdrop.
 *   - the X (denied): touch NOTHING for 2s. Survive it and the backdrop swaps;
 *     pop it and the room shakes, the sting plays, and a LOT of score burns.
 *   - a good bubble nobody pops just drifts away: 0 points, NOT an error.
 * The slide ramps 2000ms -> 500ms across the class (schedule.js); the grade
 * tier tightens the reveal window and the denied share instead of the slide.
 *
 * THE HOUSE RULES WAVE. The class opens on a DRAWN rules sheet (render.js
 * showHowto - Deck VI, images over text), and three decks ride the room:
 *   casino.js     the lighting rig: the marquee ring, the sound ladder pitched
 *                 by the chain, near-miss staging, the jackpot ladder, the
 *                 royal at the end card
 *   pressure.js   THE SURGE: the CCP effects ladder, rung = YOUR pop streak,
 *                 stepped down behind hysteresis when the chain breaks
 *   trickster.js  the lies: the mouth's tell, the crooked hold ring, the ghost
 *                 cursor, the stat flicker
 * This file owns none of that look; it owns the WIRING. It builds all three
 * after render.mount(), hands each a null-safe engine + a pause-aware timer
 * registry, and routes one read-only event per moment at the SAME call sites
 * the ledger is updated. A deck that refuses to build is null and a log line;
 * it is never a failed class. THE HOUSE LIES TO YOUR EYES, NEVER TO YOUR
 * LEDGER: no deck writes score, streak, tally or grade, and the bubble stays
 * the ONE tap target at the exact size and place index.js put it.
 *
 * FILES
 *   schedule.js  the seeded bubble plan (pure): kinds, flavors, ramp, windows
 *   scoring.js   points + composite + the dual S-gate + baseline fold (pure)
 *   render.js    every pixel: backdrop, basin, lit HUD, rules sheet, ticket
 *   casino.js    DECK II - the lighting rig + the sound ladder (FX)
 *   pressure.js  DECK III - the streak-driven effects ladder + the tremor
 *   trickster.js DECK III - the dark patterns, presentation only
 *   tube3d.js    the three.js chute (vendored r185, dynamic import)
 *   tube2d.js    the canvas chute -> static css chute (no-WebGL fallbacks)
 *   style.js     the self-injected stylesheet
 *   lex.js       every lexicon row this class can render
 *
 * THE LAWS THIS FILE KEEPS
 *   - input trust: the bubble is the ONE tap target; every engine burst this
 *     class or a deck fires passes clickSafe:true and is decoration only.
 *   - RT integrity: the reveal paint is a class toggle + src swap, and the
 *     clock starts on that same call - nothing asynchronous sits between it
 *     and `revealAt` (the deck event is routed AFTER the stamp is taken).
 *   - baseline-relative scoring on the per-game meta store (SYNTHESIS #15).
 *   - the class NEVER grades itself: it reports {metrics:{composite}, hardGates}
 *     and core/grades.js does the rest.
 *   - the debrief renders BEFORE endClass, because endClass tears this DOM down.
 *   - suspend(on) freezes EVERYTHING - decks first, then timers, tube loop and
 *     CSS - and the bubble in flight is dealt again from the mouth on resume
 *     (never scored across a freeze).
 *   - the rules sheet obeys the school's tutorial law: shown the FIRST class at
 *     a new grade tier and auto-skipped at that tier ever after, with "Skip
 *     class tutorials" skipping even that first showing.
 *
 * CLOCK. `now()` and the timer helpers resolve `performance` / `setTimeout` off
 * the global at CALL time, so the scratch harness swaps in a fake clock and
 * drives a whole 90s class in milliseconds with no test-only code in here.
 * ==========================================================================*/

import { makeRng, hash01 } from '../../core/rng.js';
import {
  buildPlan, LOAD_MS, DENIED_HOLD_MS, SLIDE_START_MS, SLIDE_END_MS,
} from './schedule.js';
import {
  ledger, popPoints, popStamp, foldBaseline, median,
  DENIED_BONUS, X_PENALTY,
} from './scoring.js';
import { createRender } from './render.js';
import { IC_LEX } from './lex.js';
import { createIcCasino, IC_CASINO, isJust, isRecordPing } from './casino.js';
import { createIcPressure } from './pressure.js';
import { createIcTrickster } from './trickster.js';

/* ----------------------------------------------------------------- clock -- */
function now() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') {
      return performance.now();
    }
  } catch (e) { /* fall through */ }
  return Date.now();
}
/**
 * The class's timer registry. `after` is a one-shot, `every` a self-re-arming
 * chain (NOT setInterval: the chain resolves setTimeout off the global at call
 * time, so the fake clock drives it too, and one cancel kills the whole run).
 * A repeat handle is a STRING, a one-shot an integer - `cancel` takes either.
 */
function createTimers() {
  const live = new Set();
  const repeats = new Map();
  let nextRepeat = 1;
  return {
    after(ms, fn) {
      const id = setTimeout(() => { live.delete(id); try { fn(); } catch (e) { /* noop */ } }, Math.max(0, Math.round(ms) || 0));
      live.add(id);
      return id;
    },
    every(ms, fn) {
      const key = 'ic-every-' + (nextRepeat++);
      const period = Math.max(16, Math.round(ms) || 16);
      const rec = { timer: 0, dead: false };
      repeats.set(key, rec);
      const arm = () => {
        rec.timer = setTimeout(() => {
          if (rec.dead) return;
          try { fn(); } catch (e) { /* noop */ }
          if (!rec.dead) arm();
        }, period);
      };
      arm();
      return key;
    },
    cancel(id) {
      if (id == null) return;
      if (typeof id === 'string') {
        const rec = repeats.get(id);
        if (rec) { rec.dead = true; try { clearTimeout(rec.timer); } catch (e) { /* noop */ } repeats.delete(id); }
        return;
      }
      try { clearTimeout(id); } catch (e) { /* noop */ }
      live.delete(id);
    },
    killAll() {
      for (const id of Array.from(live)) { try { clearTimeout(id); } catch (e) { /* noop */ } }
      live.clear();
      for (const [k, rec] of Array.from(repeats)) {
        rec.dead = true;
        try { clearTimeout(rec.timer); } catch (e) { /* noop */ }
        repeats.delete(k);
      }
    },
    get size() { return live.size + repeats.size; },
  };
}
function probe(query) {
  try {
    if (typeof window !== 'undefined' && window && typeof window.matchMedia === 'function') {
      const m = window.matchMedia(query);
      return !!(m && m.matches);
    }
  } catch (e) { /* noop */ }
  return false;
}
/* THE PHONE PROBE (web CLAUDE.md trap 42's seam): coarse pointer or a real
 * touch digitiser. The CSS side rides the shell's global html.ae-touch; JS
 * decisions use this local probe so the module answers the same on a bare
 * harness. A Windows touchscreen laptop also answers true - accepted
 * deliberately, the ceiling is hardware-protective and cheap. */
function coarseTouch() {
  if (probe('(pointer: coarse)')) return true;
  try {
    if (typeof navigator !== 'undefined' && navigator && Number(navigator.maxTouchPoints) > 1) return true;
  } catch (e) { /* noop */ }
  return false;
}
function clamp01(v) {
  const x = Number(v);
  if (!isFinite(x)) return 0;
  return x < 0 ? 0 : x > 1 ? 1 : x;
}

const GAME_KEY = 'impulse_control';
const PRESS_DEDUPE_MS = 120;      // one press = one response (pointerdown + click)
/** W2: the floor between two REFUSED-press bumps. A mashed key must not
 *  machine-gun the room, so a refusal inside this window is swallowed. */
const REFUSE_BUMP_MS = 250;
const INTRO_MS = 2200;
const TRAVEL_TICK_MS = 40;        // glow position refresh during the slide
const AUTO_SUBMIT_MS = 45000;     // the debrief files itself if nobody clicks
const RESUME_DELAY_MS = 600;

/* THE ONE DIAL. Heat rides the class progress AND the player's live chain -
 * the owner's spec for this room: "the room heats up with YOUR chain". A chain
 * of STREAK_HEAT_CAP is the whole streak half of the dial. */
const STREAK_HEAT_CAP = 12;
/* A cue's level may never exceed the tier's ceiling. The decks ask; the ceiling
 * answers. (shell/audio.js multiplies by the player's own levels after this.) */
const AUDIO_CEIL = Object.freeze([0.45, 0.6, 0.75, 0.9]);
/* The kinds that paint OVER the basin: every one is welded clickSafe, because
 * the bubble is the single tap target and no decoration may steal its press. */
const CLICK_SAFE_KINDS = Object.freeze({
  flash_burst: 1, gif_burst: 1, bubble_field: 1, gif_rain: 1,
});

/** Declarative asset needs: the backdrop pool. */
const ASSET_SPEC = Object.freeze({ loops: 12, stills: 6, targets: 0, canvasSafe: false });

/* ============================================================================
 * THE MODULE
 * ==========================================================================*/
export default {
  key: GAME_KEY,
  family: 'reflex',
  meaty: false,
  flagship: false,
  timeBudgetSec: 90,
  orientation: 'any',   // phone only; see games/registry.js ORIENTATIONS
  title: 'Impulse Control',

  manifest: {
    /* flash pops fire flash_burst, subliminal pops fire sub_flash, the spiral
       flourish is drawn in-game; the rest of the list is the decks' ladder
       (casino cues through audio_trigger, pressure climbs wash -> crt ->
       gif_burst -> glitch_swap -> gif_rain -> ambient_field). */
    effectsConsumed: [
      'flash_burst', 'sub_flash', 'gif_burst', 'glitch_swap', 'audio_trigger',
      'wash', 'crt', 'gif_rain', 'ambient_field',
    ],
    assetNeeds: ASSET_SPEC,
    boardSizes: null,
    keybinds: [{ verb: 'go', label_key: 'ic_go_key', default: 'Space' }],
    settings: [
      { key: 'ic_show_rt', kind: 'bool', default: true, label_key: 'ic_show_rt' },
      { key: 'ic_bg_fade', kind: 'range', min: 0, max: 0.8, step: 0.05, default: 0.35, label_key: 'ic_bg_fade' },
    ],
    peek: false,
  },

  /** Every lexicon row this class renders (the host table mirrors these). */
  lexicon: IC_LEX,

  create(ctx) {
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };
    const t = (k, f) => {
      try { return ctx.lexicon(k, f == null ? IC_LEX[k] : f); }
      catch (e) { return f == null ? (IC_LEX[k] || k) : f; }
    };
    /* EMI COMMENTARY SEAMS (the heartbeat wave). note() names a moment the
     * mascot may react to - the shell prefixes 'game:' and its own voice engine
     * decides whether the moment is worth a face, a line or nothing at all.
     * hold() fences a timing-critical window where she may pull faces but never
     * words. Both are additive, one-way and fully guarded: an older shell has
     * neither, and a mascot may never break a class. */
    const note = (id, extra) => {
      try { if (ctx.mood && typeof ctx.mood.note === 'function') ctx.mood.note(id, extra); }
      catch (e) { /* a mascot may never break a class */ }
    };
    const holdWords = (on) => {
      try { if (ctx.mood && typeof ctx.mood.hold === 'function') ctx.mood.hold(!!on); }
      catch (e) { /* a mascot may never break a class */ }
    };
    const timers = createTimers();
    const reduced = probe('(prefers-reduced-motion: reduce)')
      || !!(ctx.motion && ctx.motion.reducedMotion);
    const touch = coarseTouch();
    const dev = ctx.dev === true;

    let S = null;
    let ended = false;
    let devSkipHowto = false;         // the rig's bypass (ctx.dev gates it)
    let destroyed = false;
    let casino = null;
    let pressure = null;
    let trickster = null;

    const settingOf = (key, dflt) => {
      try {
        const bag = ctx.settings || {};
        return Object.prototype.hasOwnProperty.call(bag, key) ? bag[key] : dflt;
      } catch (e) { return dflt; }
    };
    const setHeat = (h) => {
      try { if (ctx.engine && typeof ctx.engine.setHeat === 'function') ctx.engine.setHeat(h); }
      catch (e) { /* noop */ }
    };

    /* ==================================================================== *
     * THE DECKS' SEAMS - a frozen class runs no deck, and a deck may never
     * raise a ceiling it did not set.
     * ==================================================================== */
    /** True while nothing decorative may move: dead, gone, paused or frozen. */
    const halted = () => destroyed || !S || S.paused || S.suspended;
    /** bgIntensity 0 is the player's own exit: read it LIVE, never a snapshot. */
    const capsOk = () => !(ctx.caps && Number(ctx.caps.bgIntensity) === 0);
    const motionLevel = () => {
      try {
        const v = Number(ctx.motion && ctx.motion.motionLevel);
        return isFinite(v) ? v : 2;
      } catch (e) { return 2; }
    };
    const audioCeil = () => AUDIO_CEIL[Math.max(0, Math.min(3, (S ? S.gradeTier : 1) - 1))];

    /** INPUT TRUST welded on: a burst over the basin can never take a press. */
    function weld(kind, opts) {
      const o = Object.assign({}, opts || {});
      if (CLICK_SAFE_KINDS[kind]) {
        o.clickSafe = true;
        o.clickable = false;
        delete o.onPop;
      }
      return o;
    }
    /**
     * The engine as a deck sees it. Every member is null-safe (no engine, or a
     * frozen class, answers null) and every one READS a clamped channel rather
     * than raising it.
     *
     * STOP IS GLOBAL. `stop('wash')` kills EVERY wash this class holds, not
     * just the caller's - the engine addresses sustains by KIND. Two decks both
     * holding a wash must therefore step theirs DOWN by re-triggering at a low
     * alpha; a deck that calls stop() takes the other deck's wash with it.
     * (The Deep End paid for this lesson; the note travels with the seam.)
     */
    const deckEngine = {
      fire(kind, opts) {
        if (halted() || !ctx.engine || typeof ctx.engine.fire !== 'function') return null;
        try { return ctx.engine.fire(kind, weld(kind, opts)) || null; }
        catch (e) { say('deck fire(' + kind + ') failed'); return null; }
      },
      sustain(kind, opts) {
        if (halted() || !ctx.engine || typeof ctx.engine.sustain !== 'function') return null;
        try { return ctx.engine.sustain(kind, weld(kind, opts)) || null; }
        catch (e) { say('deck sustain(' + kind + ') failed'); return null; }
      },
      stop(kind) {
        try { if (ctx.engine && typeof ctx.engine.stop === 'function') ctx.engine.stop(kind); }
        catch (e) { /* noop */ }
      },
      ceremony(kind, opts) {
        if (halted() || !ctx.engine || typeof ctx.engine.ceremony !== 'function') return null;
        try { return ctx.engine.ceremony(kind, opts || {}) || null; }
        catch (e) { return null; }
      },
      channels() {
        try { return (ctx.engine && typeof ctx.engine.channels === 'function') ? ctx.engine.channels() : null; }
        catch (e) { return null; }
      },
      rewardRoll(opts) {
        try { return (ctx.engine && typeof ctx.engine.rewardRoll === 'function') ? ctx.engine.rewardRoll(opts || {}) : null; }
        catch (e) { return null; }
      },
      /** A cue through the ONE audio owner. `pitch` rides `extra` untouched -
       *  shell/audio.js multiplies every frequency in the recipe by it, so the
       *  casino's chime ratchets UP the chain instead of speeding up. */
      audio(name, level, extra) {
        const lv = Math.min(audioCeil(), level == null ? 0.4 : Number(level) || 0);
        return deckEngine.fire('audio_trigger', Object.assign({ name, level: lv }, extra || {}));
      },
    };

    /**
     * W2 CHROME - THE REFUSED PRESS. The pop key or a tap that lands on
     * nothing: the chute is still loading, the bubble is already resolved,
     * the ticket is up. The school's answer is one muted `bump`, THROTTLED so
     * a mashed key cannot machine-gun the room. It rides deckEngine.audio, so
     * it is clamped to this tier's audio ceiling like every other cue here.
     */
    let lastRefuseAt = -1e9;
    function refused() {
      const at = now();
      if (at - lastRefuseAt < REFUSE_BUMP_MS) return;
      lastRefuseAt = at;
      deckEngine.audio('bump', 0.15);   /* owner 2026-08-24: error cues -50% */
    }

    /** The decks' registry: this class's own timers, dead while frozen. */
    const deckTimers = {
      after(ms, fn) { return timers.after(ms, () => { if (halted()) return; fn(); }); },
      every(ms, fn) { return timers.every(ms, () => { if (halted()) return; fn(); }); },
      clear(id) { timers.cancel(id); },
    };

    /** Call one deck, null-safe. A deck that throws is logged, never fatal. */
    function deck(which, method, ...args) {
      const d = which === 'casino' ? casino : which === 'pressure' ? pressure : trickster;
      if (!d || typeof d[method] !== 'function') return undefined;
      try { return d[method](...args); }
      catch (e) { say(which + '.' + method + ' threw: ' + ((e && e.message) || e)); return undefined; }
    }
    /** Every deck sees every moment. Args are plain objects and READ-ONLY. */
    function decks(method, ...args) {
      deck('casino', method, ...args);
      deck('pressure', method, ...args);
      deck('trickster', method, ...args);
    }

    /* ---- HEAT: the one dial, and the owner's chain drives it ------------- */
    function progressNow() {
      if (!S) return 0;
      const b = S.bubble;
      if (b && isFinite(b.progress)) return clamp01(b.progress);
      const total = S.plan ? S.plan.counts.total : 0;
      if (total <= 1) return 0;
      return clamp01(Math.max(0, S.idx) / (total - 1));
    }
    function heat() {
      if (!S) return 0;
      const h = clamp01(0.2 + 0.35 * progressNow() + 0.45 * Math.min(1, S.streak / STREAK_HEAT_CAP));
      S.heat = h;
      setHeat(h);
      deck('casino', 'setHeat', h);
      deck('pressure', 'setHeat', h);
      deck('trickster', 'setHeat', h);
      return h;
    }

    /* ============================================================= START */
    function start(classSpec) {
      const spec = classSpec || {};
      const gradeTier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
      const seed = String(spec.seed == null ? (GAME_KEY + '|noseed') : spec.seed);
      const timeBudgetSec = Math.max(45, Math.min(300, Number(spec.timeBudgetSec) || 90));

      const plan = buildPlan({ rng: makeRng(seed + '|plan'), gradeTier, timeBudgetSec });
      const meta = (() => { try { return ctx.store.gameMeta(GAME_KEY) || {}; } catch (e) { return {}; } })();

      const render = createRender({
        root: ctx.root,
        t,
        reduced,
        /* the tube's cheap ladder (tube3d: fewer segments, smaller band canvas,
           4Hz band redraw, fewer particles) arms on a coarse-pointer device -
           a phone's GPU is the ceiling, not the frame budget. Desktop answers
           false and renders byte-identically. */
        perf: touch,
        showRt: settingOf('ic_show_rt', true) !== false,
        seed,                    // the tube grows its skin from the class seed
        log: say,
        /* W0 (2026-08-24): denied.mp3 finally obeys the mixer. The clip rides
         * the engine's audio_trigger clip path (mute / master / bus level /
         * ducking all apply; `stamp_bad` is the recipe FALLBACK if the browser
         * refuses the decode). render.js keeps its raw-element path only for an
         * engine-less class - the X-hit must never be the beat that goes silent. */
        sting: (clipUrl) => deckEngine.fire('audio_trigger', {
          name: 'stamp_bad',
          level: Math.min(audioCeil(), 0.45),
          url: clipUrl, key: 'ic-denied', maxMs: 1500,
        }),
      });

      S = {
        gradeTier, seed, timeBudgetSec, plan, meta, render,
        rngFx: makeRng(seed + '|fx'),
        subject: String(1000 + Math.floor(hash01(seed + '|subject') * 9000)),
        startedAt: now(),
        pool: null,
        idx: -1,                 // plan cursor
        bubble: null,            // the bubble being dealt right now
        stagePhase: 'intro',     // intro|load|slide|reveal|gap|debrief
        slideStart: 0, slideMs: 0,
        revealAt: 0,
        travelTimer: 0, phaseTimer: 0, windowTimer: 0, autoTimer: 0,
        lastPressAt: -1e9,
        paused: false, suspended: false, running: false,
        score: 0, streak: 0, bestStreak: 0, sessionBestRt: null,
        heat: 0.2,
        tally: {
          goodShown: 0, popped: 0, drifted: 0,
          deniedShown: 0, deniedHeld: 0, xClicked: 0,
          rts: [], score: 0,
        },
        swaps: 0,
        voided: false,           // a freeze voided the bubble at the cursor
        howtoShown: false,
        debriefed: false,
        recalibrated: false,
        result: null,
      };

      /* DEV SEAM (rig only). ctx.dev is never true in the shell, so production
         behaviour is byte-identical: the rig starts a class already on rung N
         so a shot can show the ladder without playing it. */
      if (dev) {
        const ds = Math.max(0, Math.min(30, Math.round(Number(spec.devStreak) || 0)));
        if (ds > 0) { S.streak = ds; S.bestStreak = ds; }
      }

      render.mount();
      buildDecks(seed, gradeTier);

      const capBg = (() => { try { return Number(ctx.caps && ctx.caps.bgIntensity); } catch (e) { return 1; } })();
      render.setBgFade(Number(settingOf('ic_bg_fade', 0.35)) * (isFinite(capBg) && capBg >= 0 ? capBg : 1));
      hud({ n: 0, subject: '#' + S.subject });

      /* the backdrop pool: never blocks, never gates a reveal */
      try {
        const claim = ctx.assets && typeof ctx.assets.claim === 'function'
          ? ctx.assets.claim(ASSET_SPEC)
          : null;
        if (claim && typeof claim.then === 'function') {
          claim.then((p) => {
            if (S && p && typeof p.next === 'function') { S.pool = p; firstBackdrop(); }
          }).catch(() => {});
        }
      } catch (e) { say('asset claim failed (' + ((e && e.message) || e) + ') - gradient backdrop only'); }

      S.running = true;
      heat();

      /* THE CLASS RULES SHEET first (Deck VI): drawn, not told. Then the short
         incoming beat, then the tube opens. Inputs are bound after GO - the
         sheet's button is the only way past it, so nothing the player presses
         at the rules can eat a bubble that has not been dealt yet. */
      howto(() => {
        if (!S || destroyed || S.debriefed) return;
        render.intro({
          title: t('ic_tube_title', 'The Drop Tube'),
          note: '',
          hint: goHintLine(),
        });
        bindInputs();
        S.phaseTimer = timers.after(INTRO_MS, () => {
          if (!S || destroyed) return;
          render.clearCard();
          decks('start');
          nextBubble();
        });
      });

      say('class started: tier ' + gradeTier + ', ' + plan.counts.total + ' bubbles ('
        + plan.counts.good + ' good / ' + plan.counts.denied + ' denied), slide '
        + SLIDE_START_MS + '->' + SLIDE_END_MS + 'ms, decks '
        + (casino ? 'casino ' : '') + (pressure ? 'pressure ' : '') + (trickster ? 'trickster' : ''));
    }

    /* ------------------------------------------------------------- decks */
    /** The UTC day the class was seeded on (the shell's seed opens with it) -
     *  the casino's "tonight only" bulb temperature is a pure function of it. */
    function utcDateOf(seed) {
      const m = /^(\d{4}-\d{2}-\d{2})/.exec(String(seed || ''));
      if (m) return m[1];
      try { return new Date().toISOString().slice(0, 10); } catch (e) { return '1970-01-01'; }
    }
    /** Build all three right after mount. A refused deck is null + a log line. */
    function buildDecks(seed, gradeTier) {
      const nodes = S.render.nodes || {};
      const base = {
        seed,
        gradeTier,
        reduced,
        motionLevel: motionLevel(),
        nodes,
        engine: deckEngine,
        timers: deckTimers,
        capsOk,
        /* W2 - THE DECK'S CUE ROAD: the game's own clamped helper, handed
           down as cue(name, level, extra). A deck asks for sound; it never
           holds a node and can never raise this tier's audio ceiling. */
        cue: (name, level, extra) => deckEngine.audio(name, level, extra),
        log: say,
        /* EMI COMMENTARY SEAMS: two moments live only inside a deck (the
           casino's ALMOST, the pressure ladder stepping back down), so the
           guarded note() rides down with the rest of the base kit. */
        note,
      };
      try {
        casino = createIcCasino(Object.assign({}, base, {
          utcDate: utcDateOf(seed),
          t,
        })) || null;
      } catch (e) { casino = null; say('casino refused: ' + ((e && e.message) || e)); }
      try {
        pressure = createIcPressure(Object.assign({}, base, {
          assets: ctx.assets || null,
        })) || null;
      } catch (e) { pressure = null; say('pressure refused: ' + ((e && e.message) || e)); }
      try {
        trickster = createIcTrickster(Object.assign({}, base, {
          isHalted: halted,
          /* the HOST's answer outranks a media probe (CLAUDE.md §5): a coarse
             pointer gets no ghost cursor at all. */
          coarse: !!(ctx.platform && ctx.platform.isTouch),
          stats: () => ({
            idx: S ? S.idx : -1,
            total: S && S.plan ? S.plan.counts.total : 0,
            streak: S ? S.streak : 0,
            score: S ? S.score : 0,
            phase: S ? S.stagePhase : 'debrief',
          }),
          t,
        })) || null;
      } catch (e) { trickster = null; say('trickster refused: ' + ((e && e.message) || e)); }
    }

    /* -------------------------------------------------------- rules sheet */
    /** Tiers this player has already had the rules sheet for (persisted). */
    function howtoSeenTiers() {
      try {
        const m = (ctx.store && typeof ctx.store.gameMeta === 'function')
          ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
        return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
      } catch (e) { return []; }
    }
    /**
     * The sheet, ahead of the incoming beat. THE LAW, uniform across every open
     * class (owner ruling 2026-08-24): the sheet SHOWS the first time this
     * player meets the tube at this grade tier and AUTO-SKIPS every later class
     * at that tier, whatever the setting says; the shell's "Skip class
     * tutorials" switch (ctx.hideTutorial) means "skip even the first showing".
     * No meta = no memory = the sheet shows. Dismissal is the sheet's own
     * button only - binding the POP key here would teach the player to press
     * it at a bubble that has not been dealt. The sheet costs the class nothing
     * either way: this class has no wall clock at all (the plan is the length),
     * so there is no budget for a reader to burn.
     */
    function howto(onDone) {
      const seen = howtoSeenTiers();
      const skip = (dev && devSkipHowto)
        || ctx.hideTutorial === true
        || seen.indexOf(S.gradeTier) >= 0;
      if (skip) { onDone(); return; }
      if (!S.render || typeof S.render.showHowto !== 'function') { onDone(); return; }
      let done = false;
      let node = null;
      try {
        node = S.render.showHowto({
          onGo: () => {
            if (done || !S || destroyed) return;
            done = true;
            /* W2 CHROME - THE START PRESS. The sheet has no pages and this
               button is the only way past it, so the press that opens the
               tube takes the school's `lift`, not a page-turn `slide`. */
            deckEngine.audio('lift', 0.5);
            try {
              const list = howtoSeenTiers();
              if (list.indexOf(S.gradeTier) < 0) {
                list.push(S.gradeTier);
                if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
                  ctx.store.mergeGameMeta(GAME_KEY, { howtoTiers: list });
                }
              }
            } catch (e) { /* best effort - the sheet just shows again next time */ }
            try { S.render.hideHowto(); } catch (e) { /* noop */ }
            onDone();
          },
          keyLabel: (() => { try { return ctx.keys.labelFor('go') || 'Space'; } catch (e) { return 'Space'; } })(),
          coarse: !!(ctx.platform && ctx.platform.isTouch),
        });
      } catch (e) { say('rules sheet refused: ' + ((e && e.message) || e)); node = null; }
      if (!node) { done = true; onDone(); return; }
      S.howtoShown = true;
    }

    function goHintLine() {
      let key = 'Space';
      try { key = ctx.keys.labelFor('go') || 'Space'; } catch (e) { /* noop */ }
      return t('ic_go_hint', IC_LEX.ic_go_hint).replace('{key}', key);
    }

    function firstBackdrop() {
      if (!S || !S.pool || S.swaps > 0) return;
      swapBackdrop();
    }
    function swapBackdrop() {
      if (!S || !S.pool) return;
      try {
        /* the desktop draws loops 70% of the time (gifs, cheap in an <img>);
           a phone flips that share to stills-first so the blurred backdrop
           never spends one of iOS's few hardware video decode sessions. ONE
           rng draw either way, so the seeded stream stays aligned. */
        const roll = S.rngFx();
        const kind = roll < 0.7 ? (touch ? 'still' : 'loop') : (touch ? 'loop' : 'still');
        const got = S.pool.next(kind);
        if (got && got.url) { S.render.swapBg(got.url); S.swaps++; }
      } catch (e) { /* keep the old one */ }
    }

    /** The HUD, and the SHELL's 10-segment meter in its anchor (never forked). */
    function hud(extra) {
      if (!S) return;
      const h = Object.assign({
        score: S.score,
        n: Math.max(0, S.idx + 1),
        total: S.plan.counts.total,
        streak: S.streak,
      }, extra || {});
      try { S.render.hud(h); } catch (e) { /* cosmetic */ }
      try {
        const cer = ctx.ceremonies;
        const slot = S.render.nodes && S.render.nodes.meterSlot;
        if (cer && typeof cer.streakMeter === 'function' && slot) {
          cer.streakMeter({ target: slot, filled: S.streak });
        }
      } catch (e) { /* a ceremony must never be the thing that fails */ }
    }

    /* ====================================================== INPUT SURFACE */
    let unbind = [];
    function bindInputs() {
      const bub = S.render.nodes.bubble;
      const onPress = (ev) => { if (ev && ev.preventDefault) ev.preventDefault(); press('pointer'); };
      if (bub && bub.addEventListener) {
        for (const evt of ['pointerdown', 'mousedown', 'touchstart', 'click']) {
          bub.addEventListener(evt, onPress);
          unbind.push(() => { try { bub.removeEventListener(evt, onPress); } catch (e) { /* noop */ } });
        }
        /* HOVER is a decoration-only signal (the casino's "almost" over the X,
           the trickster's ghost cursor). It changes no input and no ledger. */
        const onEnter = () => decks('hover', true);
        const onLeave = () => decks('hover', false);
        bub.addEventListener('pointerenter', onEnter);
        bub.addEventListener('pointerleave', onLeave);
        unbind.push(() => { try { bub.removeEventListener('pointerenter', onEnter); } catch (e) { /* noop */ } });
        unbind.push(() => { try { bub.removeEventListener('pointerleave', onLeave); } catch (e) { /* noop */ } });
      }
      try {
        const off = ctx.keys.on('go', () => press('key'));
        if (typeof off === 'function') unbind.push(off);
      } catch (e) { say('keybind wiring: ' + ((e && e.message) || e)); }
    }
    function unbindInputs() {
      for (const fn of unbind) { try { fn(); } catch (e) { /* noop */ } }
      unbind = [];
    }

    /* ========================================================== THE LOOP */
    function nextBubble() {
      if (!S || destroyed || S.paused || S.suspended || S.debriefed) return;
      S.idx += 1;
      if (S.idx >= S.plan.bubbles.length) { debrief(); return; }
      dealCurrent();
    }

    /** Deal the bubble at the cursor (also used to re-deal after a freeze). */
    function dealCurrent() {
      const b = S.plan.bubbles[S.idx];
      S.bubble = b;
      S.stagePhase = 'load';
      hud();
      S.render.showLoad();
      heat();
      /* the decks learn the KIND at the mouth - the trickster's tell needs it
         this early. The casino and the pressure deck must render no tell. */
      decks('load', {
        idx: S.idx,
        total: S.plan.counts.total,
        progress: b.progress,
        kind: b.kind,
        flavor: b.flavor,
        slideMs: b.slideMs,
      });
      S.phaseTimer = timers.after(LOAD_MS, () => beginSlide(b));
    }

    function beginSlide(b) {
      if (!S || S.paused || S.suspended) return;
      S.stagePhase = 'slide';
      S.slideStart = now();
      S.slideMs = b.slideMs;
      S.render.setTravel(0);
      decks('slide', { idx: S.idx, slideMs: b.slideMs });
      const tick = () => {
        if (!S || S.stagePhase !== 'slide' || S.paused || S.suspended) return;
        const p = (now() - S.slideStart) / S.slideMs;
        if (p >= 1) { reveal(b); return; }
        S.render.setTravel(p);
        S.travelTimer = timers.after(TRAVEL_TICK_MS, tick);
      };
      S.travelTimer = timers.after(TRAVEL_TICK_MS, tick);
    }

    function reveal(b) {
      if (!S || S.paused || S.suspended) return;
      S.stagePhase = 'reveal';
      if (b.kind === 'denied') S.tally.deniedShown++; else S.tally.goodShown++;
      S.render.revealBubble(b);
      S.revealAt = now();                       // the clock starts ON the paint call
      /* RT INTEGRITY: the deck event is routed only AFTER the stamp is taken,
         so no deck can ever sit between the paint and the clock. */
      S.windowTimer = timers.after(b.windowMs, () => windowEnd(b));
      /* THE GO / NO-GO WINDOW IS LIVE. Faces yes, words never: the score here
         is literally (window - rt), and on the X the correct play is to touch
         nothing for two seconds. Placed after the clock stamp and the window
         timer so it can never sit between the paint and the reading. Every
         resolution path below closes it. */
      holdWords(true);
      decks('reveal', {
        idx: S.idx,
        kind: b.kind,
        flavor: b.flavor,
        windowMs: b.windowMs,
        progress: b.progress,
        streak: S.streak,
      });
    }

    function windowEnd(b) {
      if (!S || S.stagePhase !== 'reveal') return;
      S.stagePhase = 'gap';
      holdWords(false);                 // the window resolved on its own clock
      const streakBefore = S.streak;
      if (b.kind === 'denied') {
        /* the X survived - restraint pays, and the backdrop turns over */
        S.score += DENIED_BONUS;
        S.tally.deniedHeld++;
        S.render.deniedPassed();
        S.render.stamp('calm', t('ic_denied_pass', 'Withheld'));
        swapBackdrop();
        note('ic.heldTheX', { kind: 'celebrate', n: S.tally.deniedHeld, streak: S.streak });
      } else {
        /* drifted away - 0 points, NOT an error */
        S.tally.drifted++;
        S.streak = 0;
        S.render.fadeBubble();
        S.render.stamp('', t('ic_missed', 'It drifted away'));
        note('ic.driftedAway', { kind: 'commiserate', n: S.tally.drifted, streak: streakBefore });
      }
      S.tally.score = S.score;
      hud();
      heat();
      if (b.kind === 'denied') decks('denyPass', { idx: S.idx, streak: S.streak, score: S.score });
      else decks('drift', { idx: S.idx, streakBefore });
      S.phaseTimer = timers.after(b.gapMs, nextBubble);
    }

    function press(source) {
      if (!S || destroyed || S.paused || S.suspended || S.debriefed) return;
      const at = now();
      if (at - S.lastPressAt < PRESS_DEDUPE_MS) return;
      S.lastPressAt = at;
      // W2: nothing to pop - a dead press, and a dead press is never silent
      if (S.stagePhase !== 'reveal' || !S.bubble) { refused(); return; }
      const b = S.bubble;
      timers.cancel(S.windowTimer);
      S.stagePhase = 'gap';
      holdWords(false);                 // the window resolved on a real press

      if (b.kind === 'denied') {
        /* THE X. The one error in the game. */
        const rt = Math.max(0, at - S.revealAt);
        const streakBefore = S.streak;
        S.score -= X_PENALTY;
        S.tally.xClicked++;
        S.streak = 0;
        S.render.hitDenied();
        S.render.stamp('bad', t('ic_denied_hit', 'THAT WAS THE X'));
        S.tally.score = S.score;
        hud();
        heat();
        decks('denyHit', { idx: S.idx, rt, penalty: X_PENALTY, streakBefore, score: S.score });
        /* EMI COLOR: a big streak dying on the X is the class's one K.O.; a
         * small slip is just a stumble. Face-side only, shell-throttled. */
        try {
          if (ctx.mood && streakBefore >= 6) { ctx.mood.runLost(); ctx.mood.calm(); }
          else if (ctx.mood) { ctx.mood.stumble(); ctx.mood.calm(); }
        } catch (e) { /* noop */ }
        /* the one K.O. in the room: a long chain eaten by the X */
        if (streakBefore >= 6) note('ic.xEatenBigChain', { kind: 'commiserate', n: Math.round(rt), streak: streakBefore });
        say('X clicked (' + source + ') at +' + Math.round(rt) + 'ms: -' + X_PENALTY);
      } else {
        const rt = Math.max(0, at - S.revealAt);
        const pts = popPoints(rt, b.windowMs);
        S.score += pts;
        S.streak += 1;
        S.bestStreak = Math.max(S.bestStreak, S.streak);
        /* EMI COLOR: she leans in with the hot streak, wide-eyed past 14. */
        try {
          if (ctx.mood && S.streak >= 14) ctx.mood.clutch();
          else if (ctx.mood && S.streak >= 8) ctx.mood.tense();
        } catch (e) { /* noop */ }
        S.tally.popped++;
        S.tally.rts.push(rt);
        const isBest = S.sessionBestRt == null || rt < S.sessionBestRt;
        if (isBest) S.sessionBestRt = rt;
        const record = Number(S.meta.bestRtMs) || 0;
        const newRecord = record > 0 && rt < record;
        S.render.popBubble(true);
        const kind = popStamp(rt);
        S.render.stamp(
          newRecord ? 'perfect' : kind,
          newRecord ? t('ic_new_best', 'NEW BEST')
            : kind === 'perfect' ? t('ic_pop_perfect', 'PERFECT')
              : kind === 'fast' ? t('ic_pop_fast', 'Quick')
                : t('ic_pop_ok', 'Popped')
        );
        beat(b.flavor);
        swapBackdrop();
        S.tally.score = S.score;
        hud({ rt });
        heat();
        /* the ledger has already moved: every number below is the TRUTH the
           decks may light, and none of them may write it back. */
        decks('pop', {
          idx: S.idx,
          rt,
          pts,
          streak: S.streak,
          bestStreak: S.bestStreak,
          stampKind: kind,
          newRecord,
          sessionBest: S.sessionBestRt,
          recordRt: record,
          windowMs: b.windowMs,
          flavor: b.flavor,
          score: S.score,
        });
        /* EMI COMMENTARY SEAMS, all four once per tap and all off the numbers
           the ledger already settled above - nothing here is recomputed for a
           payload and nothing here may write back. */
        if (newRecord) note('ic.newPersonalBest', { kind: 'celebrate', n: Math.round(rt), streak: S.streak });
        if (kind === 'perfect') note('ic.perfectPop', { kind: 'celebrate', n: Math.round(rt), streak: S.streak });
        if (isJust(rt, b.windowMs)) note('ic.justMadeIt', { kind: 'tease', n: Math.round(rt), streak: S.streak });
        else if (!newRecord && isRecordPing(rt, record)) note('ic.recordPing', { kind: 'commiserate', n: Math.round(rt), streak: S.streak });
        if (IC_CASINO.MILESTONES.indexOf(S.streak) >= 0) note('ic.streakMilestone', { kind: 'celebrate', streak: S.streak, n: S.bestStreak });
      }
      S.phaseTimer = timers.after(b.gapMs, nextBubble);
    }

    /** The pop's effect beat - one of exactly three, all decoration. */
    function beat(flavor) {
      if (flavor === 'flash') {
        /* sized against the viewport (the engine's default 120-270px box is a
           postage stamp on a chute this bright) and announced to the pressure
           deck, which dims the tube under it for its hold (THE FLARE) */
        let vmin = 720;
        try { vmin = Math.min(window.innerWidth || 0, window.innerHeight || 0) || 720; } catch (e) { /* headless */ }
        const holdMs = 900;
        const went = deckEngine.fire('flash_burst', { clickSafe: true, strength: 0.6, sizePx: Math.round(vmin * 0.34), holdMs });
        if (went) deck('pressure', 'beat', { flavor, holdMs });
      } else if (flavor === 'sub') {
        deckEngine.fire('sub_flash', { clickSafe: true, strength: 0.6 });
      } else if (flavor === 'spiral') {
        S.render.flourish();
      }
    }

    /* ========================================================== DEBRIEF */
    function debrief() {
      if (!S || S.debriefed) return;
      S.debriefed = true;
      S.running = false;
      S.stagePhase = 'debrief';
      holdWords(false);                 // class over: nothing is scored again
      S.render.setTravel(null);
      /* the ENGINE cools here, as it always has. The decks keep the heat they
         earned until their own end()/destroy() sighs it out - zeroing them
         first would take the casino's royal down with it. */
      setHeat(0);

      const sessionMedian = median(S.tally.rts);
      const fold = foldBaseline(S.meta, sessionMedian, false);
      const led = ledger(S.tally, S.meta);
      S.result = { led, fold };

      const record = Number(S.meta.bestRtMs) || 0;
      const newBest = led.bestRt != null && (record === 0 || led.bestRt < record);
      // EMI ASKS: carried on the result so `submit()` can report it additively.
      S.result.newBest = newBest;
      /* a PERFECT class: the X row untouched and not one bubble left to drift */
      const perfect = S.tally.xClicked === 0 && S.tally.drifted === 0;

      const line = (() => {
        if (fold && fold.established) return t('ic_baseline_new', IC_LEX.ic_baseline_new);
        const slowSlip = led.speed < 0.5;
        const xSlip = S.tally.xClicked > 0;
        if (slowSlip && xSlip) return t('ic_slip_both', IC_LEX.ic_slip_both);
        if (xSlip) return t('ic_slip_restraint', IC_LEX.ic_slip_restraint);
        if (slowSlip) return t('ic_slip_speed', IC_LEX.ic_slip_speed);
        return t('ic_slip_none', IC_LEX.ic_slip_none);
      })();

      /* the casino decides the ROYAL and says so on the way out; the other two
         get the same object and answer nothing. */
      const endInfo = {
        score: led.score,
        popped: S.tally.popped,
        goodShown: S.tally.goodShown,
        drifted: S.tally.drifted,
        deniedHeld: S.tally.deniedHeld,
        xClicked: S.tally.xClicked,
        medianRt: led.medianRt,
        bestRt: led.bestRt,
        newBest,
        perfect,
        sGateOk: !!led.sGate.ok,
      };
      const casinoEnd = deck('casino', 'end', endInfo);
      deck('pressure', 'end', endInfo);
      deck('trickster', 'end', endInfo);
      const royal = !!(casinoEnd && casinoEnd.royal);
      if (royal) note('ic.royal', { kind: 'celebrate', n: led.score, streak: S.bestStreak });

      /* W2 CHROME - THE TICKET. style.js prints the whole receipt in one
         pass (no per-row stagger anywhere in .g-ic-paper-grid), so the ladder
         rule's other half applies: ONE `slide`, never six blips against a
         stagger that does not exist. */
      deckEngine.audio('slide', 0.35);
      S.render.debrief({
        subject: S.subject,
        score: led.score,
        medianRt: led.medianRt,
        bestRt: led.bestRt,
        newBest,
        baselineMs: (fold && fold.baselineMs) || S.meta.baselineMs || null,
        popped: S.tally.popped,
        goodShown: S.tally.goodShown,
        deniedHeld: S.tally.deniedHeld,
        xClicked: S.tally.xClicked,
        perfect,
        royal,
        line,
        hint: led.sGate.ok ? '' : t('ic_gate_hint', IC_LEX.ic_gate_hint),
      }, submit, recalibrate);

      /* THE GRADE ARRIVES AS AN OBJECT (Deck VI): the shell's stamp, dropped
         into the ticket's own stamp area. Null-safe - under the DOM double
         ceremonies may not exist at all. */
      try {
        const cer = ctx.ceremonies;
        const slot = S.render.nodes && S.render.nodes.ticketStamp;
        if (cer && typeof cer.stamp === 'function' && slot) {
          cer.stamp({
            target: slot,
            text: royal ? t('ic_royal', 'ROYAL')
              : perfect ? t('ic_perfect_class', 'Perfect class')
                : newBest ? t('ic_new_best', 'NEW BEST')
                  : t('ic_debrief', 'Debrief'),
            tone: (royal || perfect) ? 'gild' : 'good',
            /* the shell's stamp self-removes after its hold: a grade that is
               an OBJECT on the ticket has to outlive the ticket, not the hold */
            hold: 600000,
          });
        }
      } catch (e) { /* a ceremony must never be the thing that fails */ }

      say('debrief: score ' + led.score
        + ', median ' + (led.medianRt == null ? 'n/a' : Math.round(led.medianRt) + 'ms')
        + ', popped ' + S.tally.popped + '/' + S.tally.goodShown
        + ', X ' + S.tally.xClicked + '/' + S.tally.deniedShown
        + ', composite ' + led.composite.toFixed(3) + ', sGate ' + led.sGate.ok
        + ', swaps ' + S.swaps + (perfect ? ', PERFECT' : '') + (royal ? ', ROYAL' : ''));

      S.autoTimer = timers.after(AUTO_SUBMIT_MS, submit);
      /* THE TICKET IS UP. The class's biggest hole: a static screen, two
         buttons and up to AUTO_SUBMIT_MS of dead air. Announced ONCE, right
         here - no timer of its own, the seam simply says how long she has. */
      note('ic.debriefIdle', { kind: 'ambient', n: Math.round(AUTO_SUBMIT_MS / 1000) });
    }

    function writeMeta() {
      if (!S || !S.result) return null;
      const { led, fold } = S.result;
      const patch = {};
      if (fold) { patch.baselineMs = fold.baselineMs; patch.baselineUpdatedAt = Date.now(); }
      if (led.bestRt != null) {
        const prev = S.recalibrated ? 0 : (Number(S.meta.bestRtMs) || 0);
        patch.bestRtMs = prev > 0 ? Math.min(prev, Math.round(led.bestRt)) : Math.round(led.bestRt);
      }
      patch.bestScore = Math.max(Number(S.meta.bestScore) || 0, led.score);
      patch.lastSubject = S.subject;
      patch.lastPlayedAt = Date.now();
      try { ctx.store.mergeGameMeta(GAME_KEY, patch); }
      catch (e) { say('meta write failed (grade unaffected): ' + ((e && e.message) || e)); }
      return patch;
    }

    function recalibrate() {
      if (!S || !S.result) return;
      const sessionMedian = median(S.tally.rts);
      const fold = foldBaseline(S.meta, sessionMedian, true);
      if (fold) {
        S.recalibrated = true;
        S.result.fold = fold;
        try { ctx.store.mergeGameMeta(GAME_KEY, { baselineMs: fold.baselineMs, baselineUpdatedAt: Date.now(), bestRtMs: 0 }); }
        catch (e) { /* noop */ }
        say('baseline recalibrated to ' + fold.baselineMs + 'ms');
      }
    }

    function submit() {
      if (!S || ended) return;
      ended = true;
      holdWords(false);                 // class end, whichever door it came out
      timers.cancel(S.autoTimer);
      const { led } = S.result || {};
      writeMeta();
      try {
        ctx.endClass({
          metrics: { composite: led ? led.composite : 0 },
          /* ADDITIVE, and read by exactly one thing: EMI's `faster than last
           * time. bet.` (a11). The record was already folded in by writeMeta
           * above, so `S.result` carries the comparison this run actually made
           * and nothing recomputes it. Every other consumer ignores the field.
           * Trap 54's rule: a new frame field is additive or it is a
           * regression in nine other classes. */
          newBest: !!(S.result && S.result.newBest),
          bestRt: led && led.bestRt != null ? Math.round(led.bestRt) : null,
          /* The dual gate survives the rework: an untouched X row AND real
             speed, or the class caps at A. */
          hardGates: { sGate: !!(led && led.sGate.ok) },
          flavorXp: led ? led.flavorXp : 0,
        });
      } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
      say('submitted: composite=' + (led ? led.composite.toFixed(3) : 'n/a')
        + ' sGate=' + !!(led && led.sGate.ok)
        + ' flavorXp=' + (led ? led.flavorXp : 0)
        + ' score=' + (led ? led.score : 0));
    }

    /* ============================================================ FREEZE */
    function freeze() {
      timers.cancel(S.phaseTimer); timers.cancel(S.windowTimer); timers.cancel(S.travelTimer);
      /* pause / tab-away / mandatory video: any live window is void, so the
         word fence comes down here too - it must never outlive the bubble. */
      holdWords(false);
      /* a bubble in flight is void - hide it, it re-deals from the mouth */
      if (S.stagePhase === 'reveal' || S.stagePhase === 'slide' || S.stagePhase === 'load') {
        /* a voided REVEAL was already counted as shown; un-count it, or the
           freeze itself would depress catchRate on a bubble nobody saw twice */
        if (S.stagePhase === 'reveal' && S.bubble) {
          if (S.bubble.kind === 'denied') S.tally.deniedShown = Math.max(0, S.tally.deniedShown - 1);
          else S.tally.goodShown = Math.max(0, S.tally.goodShown - 1);
        }
        S.render.fadeBubble();
        S.render.setTravel(null);
        S.stagePhase = 'gap';
        S.voided = true;          // this bubble owes us a re-deal
      }
    }
    function thaw() {
      if (!S.running || S.debriefed) return;
      S.phaseTimer = timers.after(RESUME_DELAY_MS, () => {
        if (!S || S.paused || S.suspended) return;
        /* Only a bubble the freeze actually VOIDED is dealt again. A freeze
           that landed in the settle gap has nothing in flight - re-dealing the
           cursor there would replay a bubble that already resolved (double
           points, a second restraint bonus, a fresh chance to eat the X). */
        if (S.idx < 0 || !S.voided) { S.voided = false; nextBubble(); return; }
        S.voided = false;
        dealCurrent();
      });
    }

    /* =========================================================== LIFECYCLE */
    return {
      start(classSpec) {
        try {
          devSkipHowto = dev && !!(classSpec && classSpec.devSkipHowto);
          start(classSpec);
        } catch (e) {
          say('start failed: ' + ((e && e.message) || e));
          try { ctx.root.textContent = 'This class could not start.'; } catch (err) { /* noop */ }
        }
      },

      /* THE EXITS ARE SACRED. The decks stop FIRST - a deck must never paint
         one more frame over a class the player just froze - then the loop, then
         the stage. Coming back is the same order reversed. */
      pause() {
        if (!S || S.paused) return;
        S.paused = true;
        decks('pause');
        freeze();
        S.render.suspend(true);
      },

      resume() {
        if (!S || !S.paused) return;
        S.paused = false;
        S.render.suspend(false);
        if (!S.suspended) decks('resume');
        thaw();
      },

      suspend(on) {
        if (!S) return;
        const want = !!on;
        if (want === S.suspended) return;
        S.suspended = want;
        if (want) {
          decks('pause');
          freeze();
          S.render.suspend(true);
        } else {
          S.render.suspend(false);
          /* a class the player ALSO paused stays frozen: the pause outranks
             the host's thaw, and resume() is what wakes the decks then. */
          if (!S.paused) { decks('resume'); thaw(); }
        }
      },

      destroy() {
        destroyed = true;
        holdWords(false);               // teardown never leaves the fence up
        decks('destroy');
        casino = null; pressure = null; trickster = null;
        timers.killAll();
        unbindInputs();
        if (S) {
          try { if (S.pool && typeof S.pool.release === 'function') S.pool.release(); } catch (e) { /* noop */ }
          try { S.render.destroy(); } catch (e) { /* noop */ }
        }
        S = null;
      },

      /* Diagnostics for the scratch harness, the rig and a future debug
         overlay. Never read by the shell. */
      diagnostics() {
        const dg = (d) => {
          if (!d || typeof d.diagnostics !== 'function') return null;
          try { return d.diagnostics(); } catch (e) { return null; }
        };
        return {
          heat: S ? S.heat : 0,
          streak: S ? S.streak : 0,
          decks: { casino: dg(casino), pressure: dg(pressure), trickster: dg(trickster) },
        };
      },
      __state() { return S; },
      __submit() { submit(); },
    };
  },
};
