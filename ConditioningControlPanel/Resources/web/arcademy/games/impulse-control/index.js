/* ============================================================================
 * games/impulse-control/index.js - IMPULSE CONTROL (Go/No-Go).
 * Family: reflex. Quick class, 90s. BUILD-CONTRACT §11 module.
 *
 * The Arcademy's clinical reflex assessment: act on GO stimuli fast, withhold on
 * their near-twins - while the Distraction Engine actively LIES to you. Every
 * error is attributed in the debrief to the exact lie that induced it, so failing
 * because you were distracted is the visible, itemised product. S demands BOTH
 * sub-baseline speed AND near-perfect restraint (the dual hard gate, SYNTHESIS
 * #14) - neither axis can buy the other.
 *
 * FILES
 *   stream.js    the seeded schedule (pure): phases, foreperiods, GO/NO-GO, tiers
 *   stimset.js   what a record looks like (pure): word / glyph / CSS-filter media
 *   lies.js      the five typed lie set-pieces + attribution log
 *   scoring.js   the composite + the dual S-gate + baseline folding (pure)
 *   render.js    every pixel (mockup look), namespaced .g-ic-*
 *   style.js     the self-injected stylesheet
 *   lex.js       every lexicon row this class can render
 *
 * THE LAWS THIS FILE KEEPS
 *   - effects ARE the difficulty: heat and the lie dials move by tier BEFORE the
 *     classic knobs (window / NO-GO share / similarity) - see stream.TIERS.
 *   - input trust (DECISIONS #9): the GO surface is a tap target, so EVERY burst
 *     this class fires passes clickSafe:true and is decoration only.
 *   - baseline-relative scoring on the per-game meta store (SYNTHESIS #15): the
 *     first class calibrates and writes; later classes are graded against it.
 *   - the class NEVER grades itself: it reports {metrics:{composite}, hardGates}
 *     and core/grades.js does the rest.
 *   - the debrief renders BEFORE endClass, because endClass tears this DOM down.
 *   - AudioOnlySession never reaches us (DECISIONS #4: the Arcademy does not open);
 *     a mid-class suspend still freezes everything through suspend(on).
 *
 * CLOCK. `now()` and the timer helpers resolve `performance` / `setTimeout` off
 * the global at CALL time, on purpose: the scratch harness swaps in a fake clock
 * and drives a whole 90s class in milliseconds without a line of test-only code
 * in here.
 * ==========================================================================*/

import { makeRng, hash01 } from '../../core/rng.js';
import { buildStream, tierDials, FEEDBACK_GAP_MS, BREATHER_MS } from './stream.js';
import { createStimset } from './stimset.js';
import { createLies, TRAP_DELAY_MS, ABORT_GRACE_MS, ATTRIBUTION_MS } from './lies.js';
import { createRender } from './render.js';
import {
  metricsFrom, composite as compositeOf, sGate as sGateOf, flavorXp as flavorXpOf,
  foldBaseline, slipKey, offRecordPct, median,
} from './scoring.js';
import { IC_LEX } from './lex.js';

/* ----------------------------------------------------------------- clock -- */
function now() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') {
      return performance.now();
    }
  } catch (e) { /* fall through */ }
  return Date.now();
}
function createTimers() {
  const live = new Set();
  return {
    after(ms, fn) {
      const id = setTimeout(() => { live.delete(id); try { fn(); } catch (e) { /* noop */ } }, Math.max(0, Math.round(ms) || 0));
      live.add(id);
      return id;
    },
    cancel(id) { if (id != null) { try { clearTimeout(id); } catch (e) { /* noop */ } live.delete(id); } },
    killAll() { for (const id of Array.from(live)) { try { clearTimeout(id); } catch (e) { /* noop */ } } live.clear(); },
    get size() { return live.size; },
  };
}
/** Reduced motion / coarse pointer: ctx carries neither, so we probe (guarded). */
function probe(query) {
  try {
    if (typeof window !== 'undefined' && window && typeof window.matchMedia === 'function') {
      const m = window.matchMedia(query);
      return !!(m && m.matches);
    }
  } catch (e) { /* noop */ }
  return false;
}

const GAME_KEY = 'impulse_control';
const PRESS_DEDUPE_MS = 120;         // one press = one response (pointerdown + click)
const EDGE_NEAR_MS = 40;             // "JUST made it" band at the window edge
const BEST_NEAR_MS = 5;              // "within 5ms of your record" ping
const STREAK_RATCHET = 5;            // clean responses per chime ratchet
const AUTO_SUBMIT_MS = 45000;        // the debrief files itself if nobody clicks
const INTRO_MS = 1400;

/** Declarative asset needs, in ONE place (the manifest and the claim share it).
 *  8 media-stimulus loops + the aperture target; NO-GO twins are runtime CSS
 *  filters of the same asset, so decoys need no assets of their own. */
const ASSET_SPEC = Object.freeze({ loops: 8, targets: 1, stills: 0, canvasSafe: false });

/* ============================================================================
 * THE MODULE
 * ==========================================================================*/
export default {
  key: GAME_KEY,
  family: 'reflex',
  meaty: false,
  flagship: false,
  timeBudgetSec: 90,
  title: 'Impulse Control',

  manifest: {
    /* The dossier's list (distraction integration) plus SYNTHESIS #11's
       gif_rain / gif_burst. bubble_field + row_drift + flash_burst are the
       peripheral-decoy lie and the block-transition burst - they were missing
       from the placeholder stub's list; see the build report. */
    effectsConsumed: ['audio_trigger', 'glitch_swap', 'sub_flash', 'bubble_field',
      'row_drift', 'wash', 'ambient_field', 'crt', 'flash_burst', 'gif_rain', 'gif_burst'],
    /* 8 media-stimulus loops + the aperture target. NO-GO twins are runtime CSS
       filters of the same asset, so decoys need no assets of their own.
       canvasSafe:false - there is no canvas consumer here, which is exactly why
       CORS-tainted remote media is legal as a stimulus. */
    assetNeeds: ASSET_SPEC,
    boardSizes: null,
    keybinds: [{ verb: 'go', label_key: 'ic_go_key', default: 'Space' }],
    settings: [
      { key: 'ic_stimulus_style', kind: 'enum', values: ['mixed', 'words', 'glyphs', 'media'], default: 'mixed', label_key: 'ic_stimulus_style' },
      { key: 'ic_show_rt', kind: 'bool', default: true, label_key: 'ic_show_rt' },
      { key: 'ic_inverse_audio', kind: 'bool', default: true, label_key: 'ic_inverse_audio' },
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
    const timers = createTimers();
    const reduced = probe('(prefers-reduced-motion: reduce)');
    const coarse = probe('(pointer: coarse)');

    /* class-scoped state (nulled by destroy) */
    let S = null;
    let ended = false;
    let destroyed = false;

    /* ---------------------------------------------------------------- util */
    const settingOf = (key, dflt) => {
      try {
        const bag = ctx.settings || {};
        return Object.prototype.hasOwnProperty.call(bag, key) ? bag[key] : dflt;
      } catch (e) { return dflt; }
    };
    const engine = () => (S && S.engine) || null;
    const fire = (kind, opts) => {
      const e = engine();
      if (!e || typeof e.fire !== 'function') return null;
      try { return e.fire(kind, opts); } catch (err) { say('fire ' + kind + ': ' + (err && err.message)); return null; }
    };
    const sustain = (kind, opts) => {
      const e = engine();
      if (!e || typeof e.sustain !== 'function') return null;
      try { return e.sustain(kind, opts); } catch (err) { say('sustain ' + kind + ': ' + (err && err.message)); return null; }
    };
    const stop = (kind) => {
      const e = engine();
      if (!e || typeof e.stop !== 'function') return null;
      try { return e.stop(kind); } catch (err) { return null; }
    };
    const setHeat = (h) => {
      const e = engine();
      if (!e || typeof e.setHeat !== 'function') return;
      try { e.setHeat(h); } catch (err) { /* noop */ }
    };

    /* ============================================================= START */
    function start(classSpec) {
      const spec = classSpec || {};
      const gradeTier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
      const seed = String(spec.seed == null ? (GAME_KEY + '|noseed') : spec.seed);
      const timeBudgetSec = Math.max(45, Math.min(300, Number(spec.timeBudgetSec) || 90));
      const dials = tierDials(gradeTier);

      const stream = buildStream({ rng: makeRng(seed + '|stream'), gradeTier, timeBudgetSec });
      const meta = (() => { try { return ctx.store.gameMeta(GAME_KEY) || {}; } catch (e) { return {}; } })();

      const render = createRender({
        root: ctx.root,
        t,
        ceremonies: ctx.ceremonies,
        showRt: settingOf('ic_show_rt', true) !== false,
        reduced,
        log: say,
      });

      S = {
        gradeTier, seed, timeBudgetSec, dials, stream, meta, render,
        engine: ctx.engine,
        rngLies: makeRng(seed + '|lies'),
        rngReward: makeRng(seed + '|reward'),
        rngStim: makeRng(seed + '|stimset'),
        subject: String(1000 + Math.floor(hash01(seed + '|subject') * 9000)),
        startedAt: now(),
        pool: null, stimset: null, lies: null,
        phaseKey: 'intro', blockNo: 0,
        queue: [], awaiting: null, trialTimer: 0, gapTimer: 0, trapTimer: 0, abortTimer: 0,
        lastPressAt: -1e9, pressCount: 0,
        paused: false, suspended: false, running: false,
        baselineRts: [],
        tally: {
          goCount: 0, nogoCount: 0, hits: 0, misses: 0, lates: 0,
          commissions: 0, isiCommissions: 0, rts: [],
          lieTrials: 0, cleanTrials: 0, lieErrors: 0, cleanErrors: 0,
        },
        errors: [],            // {kind, atMs, induced, lieLabel, lieLagMs, recIndex}
        blockErrors: 0,
        streak: 0, bestStreak: 0,
        sessionBest: null,
        buzzerLied: false,
        commendations: 0,
        effectCalls: 0,
        debriefed: false,
      };

      render.mount();
      render.setChrome({ subject: '#' + S.subject, block: t('ic_baseline_block', 'Calibration'), nogoPct: dials.nogoShare * 100 });
      render.footline({ record: meta.baselineMs || null, restraintPct: 100, induced: 0, clean: 0 });
      render.breakCard({
        title: t('ic_assessment', 'Reflex & Compliance Assessment'),
        note: t('ic_calibrating', 'Calibrating - hold still, subject.'),
        stampline: goHintLine(),
      });

      bindInputs();

      /* The asset pool: local resolves immediately by contract, so this only ever
         delays the intro card. A slow/absent provider falls through to glyphs and
         words - a stimulus paint NEVER waits on media (RT integrity dies if paint
         timing hitches). */
      let begun = false;
      const begin = () => {
        if (begun || destroyed || !S) return;
        begun = true;
        buildStimset();
        S.lies = createLies({
          engine: ctx.engine, tier: gradeTier, rng: S.rngLies, now, t, log: say,
          allowInverse: settingOf('ic_inverse_audio', true) !== false,
          reduced, coarse,
          hooks: {
            stimEl: () => render.nodes.stim,
            apertureEl: () => render.nodes.aperture,
            primeText: () => (S.stimset ? S.stimset.primeText() : null),
            swapToTwin: (rec) => swapToTwin(rec),
            // Lies are DECIDED on the beat and LAND on their own offset (lies.js):
            // a sting 200ms before onset can pull a finger, one fired 1.4s early
            // cannot - and the debrief would have nothing to attribute.
            schedule: (ms, fn) => timers.after(ms, () => {
              if (S && !destroyed && !S.paused && !S.suspended) fn();
            }),
            decoyBump: () => decoyBump(),
            onFired: (ev) => onLieFired(ev),
          },
        });
        S.lies.planTaste(stream.records);
        S.running = true;
        enterPhase('baseline', 0);
      };

      try {
        const claim = ctx.assets && typeof ctx.assets.claim === 'function'
          ? ctx.assets.claim(ASSET_SPEC)
          : null;
        if (claim && typeof claim.then === 'function') {
          claim.then((p) => { if (S && p && typeof p.next === 'function') S.pool = p; }).catch(() => {})
            .then(() => timers.after(INTRO_MS, begin));
        } else {
          timers.after(INTRO_MS, begin);
        }
      } catch (e) {
        say('asset claim failed (' + ((e && e.message) || e) + ') - glyph/word stimuli only');
        timers.after(INTRO_MS, begin);
      }
      // Hard floor: never let a hanging provider promise eat the class.
      timers.after(INTRO_MS + 900, begin);

      say('class started: tier ' + gradeTier + ', ' + stream.counts.total + ' stimuli ('
        + stream.counts.go + ' go / ' + stream.counts.nogo + ' nogo), '
        + stream.counts.lieEligible + ' lie-eligible');
    }

    function goHintLine() {
      let key = 'Space';
      try { key = ctx.keys.labelFor('go') || 'Space'; } catch (e) { /* noop */ }
      return t('ic_go_hint', IC_LEX.ic_go_hint).replace('{key}', key);
    }

    function buildStimset() {
      S.stimset = createStimset({
        t,
        rng: S.rngStim,
        style: String(settingOf('ic_stimulus_style', 'mixed')),
        words: (ctx.words && Array.isArray(ctx.words)) ? ctx.words : null,
        mediaUrl: () => {
          if (!S || !S.pool) return null;
          try { const got = S.pool.next('loop'); return (got && got.url) || null; } catch (e) { return null; }
        },
        log: say,
      });
      say('stimulus styles available: ' + S.stimset.styles().join(',') + ' (asked ' + S.stimset.requested + ')');
    }

    /* ====================================================== INPUT SURFACE */
    let unbind = [];
    function bindInputs() {
      const arena = S.render.nodes.arena;
      const onPress = () => press('pointer');
      /* pointerdown is the RT-honest event (down, never up). mousedown/touchstart
         are the fallback for a webview without PointerEvent; the dedupe window
         makes a doubled press impossible either way. */
      if (arena && arena.addEventListener) {
        for (const evt of ['pointerdown', 'mousedown', 'touchstart', 'click']) {
          arena.addEventListener(evt, onPress);
          unbind.push(() => { try { arena.removeEventListener(evt, onPress); } catch (e) { /* noop */ } });
        }
      }
      try {
        const off = ctx.keys.on('go', () => press('key'));
        if (typeof off === 'function') unbind.push(off);
        const offUp = ctx.keys.on('go:up', () => release());
        if (typeof offUp === 'function') unbind.push(offUp);
      } catch (e) { say('keybind wiring: ' + ((e && e.message) || e)); }
    }
    function unbindInputs() {
      for (const fn of unbind) { try { fn(); } catch (e) { /* noop */ } }
      unbind = [];
    }

    /* ========================================================= PHASE FLOW */
    function enterPhase(key, blockNo) {
      if (!S || destroyed) return;
      S.phaseKey = key;
      S.blockNo = blockNo || 0;
      if (S.lies) S.lies.setPhase(key === 'baseline' ? 'baseline' : key);
      applyEffects(key, blockNo);

      if (key === 'baseline') {
        S.render.setChrome({ block: t('ic_baseline_block', 'Calibration'), nogoPct: 25 });
        queuePhase('baseline', 0);
        S.render.clearBreak();
        nextTrial();
        return;
      }
      if (key === 'assess') {
        S.render.clearBreak();
        S.render.setChrome({
          block: blockNo + '/3',
          nogoPct: blockNoGoPct(blockNo),
        });
        if (S.stimset) S.stimset.lockBlock(blockSimilarity(blockNo));
        queuePhase('assess', blockNo);
        nextTrial();
        return;
      }
      if (key === 'hold') {
        S.render.setChrome({ block: t('ic_composure_hold', 'Composure hold'), nogoPct: 70 });
        if (S.stimset) S.stimset.lockBlock(S.dials.similarity);
        S.render.breakCard({
          title: t('ic_composure_hold', 'Composure hold'),
          note: t('ic_hold_intro', 'Composure hold. Withhold, mostly.'),
        });
        queuePhase('hold', 4);
        timers.after(1100, () => { if (S && !destroyed) { S.render.clearBreak(); nextTrial(); } });
        return;
      }
      if (key === 'debrief') { debrief(); }
    }

    function blockNoGoPct(blockNo) {
      const p = (Math.max(1, blockNo) - 1) / 2;
      return S.dials.nogoShare * (0.85 + 0.15 * p) * 100;
    }
    function blockSimilarity(blockNo) {
      const p = (Math.max(1, blockNo) - 1) / 2;
      return S.dials.similarity * (0.9 + 0.1 * p);
    }

    function queuePhase(phase, block) {
      S.queue = S.stream.records.filter((r) => r.phase === phase && (block == null || r.block === block)).slice();
      S.phaseStartedAt = now();
      S.phaseBudgetMs = phase === 'baseline' ? S.stream.plan.baselineMs
        : phase === 'hold' ? S.stream.plan.holdMs : S.stream.plan.blockMs;
    }

    /** What comes after the phase that just ran out of stimuli (or time). */
    function advancePhase() {
      if (!S || destroyed) return;
      const key = S.phaseKey;
      if (key === 'baseline') {
        const m = median(S.baselineRts);
        say('baseline block: ' + S.baselineRts.length + ' clean RTs, median ' + (m == null ? 'n/a' : m + 'ms'));
        S.render.footline({ medianRt: m, record: S.meta.baselineMs || null, restraintPct: restraintPct(), induced: inducedCount(), clean: cleanCount() });
        enterPhase('assess', 1);
        return;
      }
      if (key === 'assess') {
        blockBreak(S.blockNo);
        return;
      }
      if (key === 'hold') { enterPhase('debrief', 0); return; }
      enterPhase('debrief', 0);
    }

    /** The 3s breather between blocks: stats, a wash pulse, and the commendation
     *  roll (the class's slot machine - DECISIONS #8 keeps the fiction-crack). */
    function blockBreak(blockNo) {
      const perfect = S.blockErrors === 0;
      const commended = perfect && rollCommendation();
      S.render.breakCard({
        title: t('ic_block_clear', 'Block clear') + ' ' + blockNo + '/3',
        note: t('ic_breather', 'Breathe. The next block runs hotter.'),
        stampline: commended ? t('ic_commended', 'COMMENDED') : null,
      });
      if (commended) {
        S.commendations++;
        // The fiction-crack reward: reward media bursting over a clinical card.
        // clickSafe by law - the GO surface is a tap target (DECISIONS #9).
        countEffect(fire('gif_burst', { clickSafe: true, count: 6 }));
        S.render.stamp(t('ic_commended', 'COMMENDED'), 'gild');
        S.render.reward('jackpot');
        fire('audio_trigger', { name: 'jackpot', level: 0.6, bus: 'fx', duck: 'voice' });
      }
      if (S.gradeTier >= 2) countEffect(sustain('wash', { variant: 'pink', holdMs: 1600 }));
      // flash_burst lives in block transitions ONLY - never over a live stimulus.
      if (S.gradeTier >= 3) countEffect(fire('flash_burst', { clickSafe: true, clickable: false }));
      S.blockErrors = 0;

      timers.after(BREATHER_MS, () => {
        if (!S || destroyed) return;
        stop('wash');
        if (blockNo >= 3) enterPhase('hold', 4);
        else enterPhase('assess', blockNo + 1);
      });
    }

    /* ======================================================== THE EFFECTS */
    function countEffect(res) { if (S && res !== false && res !== null && res !== undefined) S.effectCalls++; return res; }

    /**
     * Effects per phase. GROUND-RULES §6: the game spends dials, it never sets an
     * absolute - `setHeat` is the one scalar, and every strength opt asks for at
     * most the clamped channel ceiling. The BASELINE BLOCK IS EXEMPT FROM ALL
     * DISTRACTION: it is the honest control the rest of the class is scored
     * against, so heat goes to 0 and every sustain stops.
     */
    function applyEffects(phase, blockNo) {
      const tier = S.gradeTier;
      if (phase === 'baseline') {
        setHeat(0);
        for (const k of ['ambient_field', 'crt', 'bubble_field', 'row_drift', 'wash', 'gif_rain', 'sub_flash']) stop(k);
        return;
      }
      const p = phase === 'hold' ? 1 : (Math.max(1, blockNo || 1) - 1) / 2;
      // Per-phase sawtooth: each band starts higher and breathes within itself.
      const heat = Math.min(1, S.dials.heat * (0.72 + 0.28 * p) + (phase === 'hold' ? 0.12 : 0));
      setHeat(heat);

      countEffect(sustain('ambient_field', { kind: 'motes', density: tier >= 3 ? null : 0.25 }));
      countEffect(sustain('crt', { variant: tier >= 3 ? 'scanline' : 'bloom', level: tier >= 3 ? null : 0.2 }));

      if (tier >= 2) {
        // Peripheral decoys: faint fake stimuli OUTSIDE the aperture. Decoration
        // only (clickSafe) - only aperture stimuli are real, and a clickable
        // bubble over a timing game would break input trust.
        countEffect(sustain('bubble_field', {
          clickSafe: true,
          max: coarse ? 12 : 8,
          alpha: tier >= 3 ? null : 0.35,
        }));
      }
      if (tier >= 3) {
        // Ambient subliminal pressure (the targeted priming lie is separate).
        countEffect(sustain('sub_flash', { variant: 'scatter' }));
      }
      if (tier >= 4) {
        if (S.lies && S.lies.apertureSlideAllowed()) {
          // The aperture itself drifts: a parked cursor decays off-target and
          // muscle memory stops being free. Coarse pointer / reduced motion get
          // a decoy-density bump instead (portability rule).
          countEffect(sustain('row_drift', {
            targets: [S.render.nodes.aperture], axis: 'x', variant: 'sway', speedMult: 0.5,
          }));
        } else {
          countEffect(sustain('bubble_field', { clickSafe: true, max: 14 }));
        }
        if (phase === 'hold') {
          countEffect(sustain('wash', { variant: 'drain', sustainForever: true }));
          countEffect(sustain('gif_rain', { clickSafe: true, variant: 'light' }));
        }
      }
    }

    function decoyBump() {
      countEffect(sustain('bubble_field', { clickSafe: true, max: coarse ? 18 : 14, cadenceMs: 400 }));
      timers.after(2600, () => {
        if (!S || destroyed || S.phaseKey === 'baseline') return;
        countEffect(sustain('bubble_field', { clickSafe: true, max: coarse ? 12 : 8 }));
      });
    }

    /** The variable-ratio schedule (reward.js canon): base .30 -> .60 by tier. */
    function rollCommendation() {
      const base = 0.30 + 0.10 * (S.gradeTier - 1);
      const streakBonus = Math.min(8, S.bestStreak) * 0.03;
      return S.rngReward() < Math.min(0.85, base + streakBonus);
    }

    /* ========================================================= THE TRIALS */
    function nextTrial() {
      if (!S || destroyed || !S.running || S.paused || S.suspended) return;
      S.render.clearBreak();
      const overBudget = (now() - S.phaseStartedAt) >= S.phaseBudgetMs;
      const rec = S.queue.shift();
      if (!rec || overBudget) { advancePhase(); return; }

      rec.effClass = rec.cls;                  // a trap can flip this mid-trial
      S.awaiting = { rec, state: 'fore', foreAt: now(), shownAt: 0, resolved: false, pressedAt: 0 };

      /* THE BEAT. Lies resolve at the START of the foreperiod, because a lie that
         precedes the stimulus (a false cue, a priming flash) is the whole trick. */
      if (S.lies && (S.phaseKey === 'assess' || S.phaseKey === 'hold')) {
        if (S.lies.isTasteTarget(rec)) S.render.telegraph(true);
        S.lies.beat(rec);
      }

      const wait = Math.max(80, rec.foreperiodMs);
      S.trialTimer = timers.after(wait, () => onset(rec));
    }

    function onset(rec) {
      if (!S || destroyed || !S.awaiting || S.awaiting.rec !== rec) return;
      if (S.paused || S.suspended) return;
      const dressed = S.stimset ? S.stimset.dress(rec) : { render: 'glyph', text: '?' };
      rec.dressed = dressed;
      S.render.showStimulus(dressed, rec.cls);
      S.render.telegraph(false);
      S.awaiting.state = 'shown';
      S.awaiting.shownAt = now();
      // rAF alignment: if the platform has one, take the paint timestamp from it
      // (down-event honesty demands the onset be a real paint, not a schedule).
      try {
        if (typeof requestAnimationFrame === 'function') {
          requestAnimationFrame(() => {
            if (S && S.awaiting && S.awaiting.rec === rec && S.awaiting.state === 'shown' && !S.awaiting.pressedAt) {
              S.awaiting.shownAt = now();
            }
          });
        }
      } catch (e) { /* noop */ }

      if (rec.cls === 'go') fire('audio_trigger', { name: 'blip', level: 0.28, bus: 'fx' });

      /* the commitment trap: the swap lands INSIDE the human commit point */
      if (rec.lie === 'commitment_trap' && rec.cls === 'go') {
        const delay = TRAP_DELAY_MS[0] + Math.round(S.rngLies() * (TRAP_DELAY_MS[1] - TRAP_DELAY_MS[0]));
        S.trapTimer = timers.after(delay, () => {
          if (!S || !S.awaiting || S.awaiting.rec !== rec || S.awaiting.resolved) return;
          // lies.js already fired the glitch on the beat; its onSwap calls
          // swapToTwin(), so all this timer does is make sure the flip happens
          // even when the engine is a null object (no onSwap ever arrives).
          swapToTwin(rec);
        });
      }

      S.trialTimer = timers.after(rec.presentMs, () => closeTrial(rec, 'timeout'));
    }

    /** The trap's content flip: same node, the twin face, and the truth flips too. */
    function swapToTwin(rec) {
      if (!S || destroyed || !rec || rec.swapped) return;
      rec.swapped = true;
      rec.effClass = 'nogo';
      rec.swappedAt = now();
      const dressed = S.stimset ? S.stimset.dress(Object.assign({}, rec, { cls: 'nogo' })) : null;
      if (dressed) S.render.swapStimulus(dressed);
    }

    /* ------------------------------------------------------------- presses */
    function press(source) {
      if (!S || destroyed || ended || !S.running || S.paused || S.suspended) return;
      const at = now();
      if (at - S.lastPressAt < PRESS_DEDUPE_MS) return;      // pointerdown + click
      S.lastPressAt = at;
      S.pressCount++;

      const a = S.awaiting;
      if (!a || a.resolved || a.state !== 'shown') {
        // Nothing is showing: a commission during the rest gap. It has its own
        // attribution check, because a false cue during a foreperiod is exactly
        // the lie that causes it.
        commission('isi', a ? a.rec : null, at);
        return;
      }
      a.pressedAt = at;
      const rec = a.rec;
      const rt = at - a.shownAt;

      if (rec.effClass === 'go') {
        if (rt <= rec.windowMs) return resolveHit(rec, rt, at, source);
        return resolveLate(rec, rt, at);
      }

      /* a press on a NO-GO. If a commitment trap swapped this stimulus moments
         ago, hold the verdict for the abort grace: releasing inside it is the
         "almost had you" near-miss, not an error. */
      if (rec.swapped && S.lies && S.lies.trapJustSwapped(rec, at) && (at - rec.swappedAt) <= (TRAP_DELAY_MS[1] + ATTRIBUTION_MS)) {
        a.pendingAbort = { rec, at };
        timers.cancel(S.abortTimer);
        S.abortTimer = timers.after(ABORT_GRACE_MS, () => {
          if (!S || !S.awaiting || S.awaiting.rec !== rec || S.awaiting.resolved) return;
          if (S.awaiting.pendingAbort) { S.awaiting.pendingAbort = null; commission('commission', rec, at); }
        });
        return;
      }
      commission('commission', rec, at);
    }

    /** Keyup inside the abort grace on a trap = the press was aborted. */
    function release() {
      if (!S || destroyed || !S.awaiting) return;
      const a = S.awaiting;
      if (!a.pendingAbort) return;
      const rec = a.pendingAbort.rec;
      a.pendingAbort = null;
      timers.cancel(S.abortTimer);
      // Near-miss #2: the decoy winks at you.
      S.render.toast(t('ic_almost', 'Almost had you'), t('ic_lie_commitment_trap', 'mid-presentation swap'), false);
      S.render.reward('near_miss', { text: t('ic_almost', 'Almost had you') });
      countTrial(rec);
      S.tally.nogoCount++;
      bumpStreak();
      finishTrial(rec, 'abort');
    }

    /* ---------------------------------------------------------- resolutions */
    function resolveHit(rec, rt, at, source) {
      const base = S.meta.baselineMs || median(S.baselineRts) || null;
      const isBaseline = rec.phase === 'baseline';
      if (isBaseline) {
        S.baselineRts.push(rt);
      } else {
        countTrial(rec);
        S.tally.goCount++;
        S.tally.hits++;
        S.tally.rts.push(rt);
        if (S.sessionBest == null || rt < S.sessionBest) S.sessionBest = rt;
      }
      const best = !isBaseline && (S.meta.bestMedianMs ? rt < S.meta.bestMedianMs : false);
      S.render.hit({
        rtMs: rt,
        best,
        underBaseline: base ? rt <= base : true,
        edge: (rec.windowMs - rt) <= EDGE_NEAR_MS,
      });
      if ((rec.windowMs - rt) <= EDGE_NEAR_MS) {
        // Near-miss #1: the window visualised as a closing gate.
        S.render.reward('near_miss', { text: t('ic_just_made_it', 'JUST made it') });
      } else if (S.meta.bestMedianMs && Math.abs(rt - S.meta.bestMedianMs) <= BEST_NEAR_MS && !best) {
        // Near-miss #3: within 5ms of your record, without crossing it.
        S.render.reward('near_miss', { text: t('ic_personal_record', 'personal record') });
      }
      if (best) S.render.stamp(t('ic_new_best', 'NEW BEST'), 'gild');

      if (!isBaseline) {
        bumpStreak();
        /* DECISIONS #7 - THE INVERSE AUDIO LIE. A clean, in-window GO answered
           with the ERROR buzzer, tier 4 only, once per class, its own set-piece,
           and ALWAYS attributed in the debrief. */
        if (S.lies && S.gradeTier >= 4 && rec.lie == null) {
          const forceTail = S.phaseKey === 'hold' && S.lies.inverseArmed();
          const ev = S.lies.maybeInverseAudio(rec, { force: forceTail });
          if (ev) {
            S.buzzerLied = true;
            S.render.errorMark();
            S.render.toast(t('ic_debrief_buzzer_lied', 'That buzzer lied.'),
              t('ic_debrief_buzzer_body', 'A clean GO was answered with the error buzzer.'), false);
          }
        }
      }
      finishTrial(rec, 'hit');
    }

    function resolveLate(rec, rt, at) {
      if (rec.phase !== 'baseline') {
        countTrial(rec);
        S.tally.goCount++;
        S.tally.lates++;
        logError('late', rec, at);
      }
      S.render.errorMark();
      breakStreak(t('ic_err_late', 'Late response'));
      finishTrial(rec, 'late');
    }

    function commission(kind, rec, at) {
      // The calibration block is unscored - including presses into its rest gaps,
      // where `rec` is null and only the phase can tell us where we are.
      const graded = S.phaseKey !== 'baseline' && (!rec || rec.phase !== 'baseline');
      if (graded) {
        if (kind === 'isi') S.tally.isiCommissions++;
        else { countTrial(rec); S.tally.nogoCount++; S.tally.commissions++; }
        logError(kind === 'isi' ? 'isi' : 'commission', rec, at);
      }
      S.render.errorMark();
      fire('audio_trigger', { name: 'stamp_bad', level: 0.42, bus: 'fx' });
      breakStreak(kind === 'isi' ? t('ic_err_isi', 'Commission during rest') : t('ic_err_commission', 'Impulse error'));
      if (rec && S.awaiting && S.awaiting.rec === rec && S.awaiting.state === 'shown') finishTrial(rec, 'commission');
    }

    /** A withheld NO-GO (or a missed GO) at timeout. */
    function closeTrial(rec, why) {
      if (!S || destroyed || !S.awaiting || S.awaiting.rec !== rec || S.awaiting.resolved) return;
      const graded = rec.phase !== 'baseline';
      if (rec.effClass === 'nogo') {
        if (graded) { countTrial(rec); S.tally.nogoCount++; }
        S.render.withhold();
        fire('audio_trigger', { name: 'blip', level: 0.2, bus: 'fx' });
        bumpStreak();
      } else {
        if (graded) { countTrial(rec); S.tally.goCount++; S.tally.misses++; logError('miss', rec, now()); }
        S.render.errorMark();
        breakStreak(t('ic_err_miss', 'Missed cue'));
      }
      finishTrial(rec, why || 'timeout');
    }

    function finishTrial(rec, why) {
      if (!S || !S.awaiting || S.awaiting.rec !== rec) return;
      S.awaiting.resolved = true;
      timers.cancel(S.trialTimer);
      timers.cancel(S.trapTimer);
      S.render.hideStimulus();
      S.render.footline({
        medianRt: median(S.tally.rts),
        record: S.meta.baselineMs || null,
        restraintPct: restraintPct(),
        induced: inducedCount(),
        clean: cleanCount(),
      });
      S.gapTimer = timers.after(FEEDBACK_GAP_MS, () => { S.awaiting = null; nextTrial(); });
    }

    /* --------------------------------------------------- tallies + streaks */
    function countTrial(rec) {
      if (!rec || rec.phase === 'baseline' || rec.counted) return;
      rec.counted = true;
      if (rec.lie) S.tally.lieTrials++; else S.tally.cleanTrials++;
    }

    /**
     * ATTRIBUTION - the product. An error with a lie live in the last 400ms is
     * INDUCED (the machine got you, and the debrief says which lie); an error
     * without one is CLEAN (yours). Induced errors are praise for the engine,
     * never shame for the player.
     */
    function logError(kind, rec, at) {
      const ev = S.lies ? S.lies.activeAt(at) : null;
      const induced = !!ev;
      const err = {
        kind, atMs: at, induced,
        lieKind: ev ? ev.kind : null,
        lieLabel: ev ? ev.label : null,
        lieLagMs: ev ? (at - ev.atMs) : 0,
        recIndex: rec ? rec.i : -1,
      };
      S.errors.push(err);
      S.blockErrors++;
      if (induced) S.tally.lieErrors++; else S.tally.cleanErrors++;
      const pct = ((at - S.startedAt) / Math.max(1, S.stream.plan.totalMs)) * 100;
      S.render.logMark(pct, induced ? 'induced' : 'clean');
      if (induced) {
        S.render.toast(ev.label + ' -',
          t('ic_debrief_induced_line', IC_LEX.ic_debrief_induced_line), false);
      } else {
        S.render.toast(t('ic_err_' + kind, kind) + ' -',
          t('ic_debrief_clean_line', IC_LEX.ic_debrief_clean_line), true);
      }
      return err;
    }

    function onLieFired(ev) {
      if (!S) return;
      const pct = ((ev.atMs - S.startedAt) / Math.max(1, S.stream.plan.totalMs)) * 100;
      S.render.logMark(pct, 'lie');
      if (ev.telegraphed) {
        // The taste-of-the-twist is debriefed the moment it lands, by name: the
        // player must MEET the game's identity before they form an opinion of it.
        S.render.toast(ev.label + ' -', t('ic_debrief_induced_line', IC_LEX.ic_debrief_induced_line), false);
        S.render.telegraph(false);
      }
    }

    function bumpStreak() {
      S.streak++;
      if (S.streak > S.bestStreak) S.bestStreak = S.streak;
      S.render.streak(Math.min(10, S.streak));
      if (S.streak % STREAK_RATCHET === 0) {
        // The chime pitch-ratchets and the aperture ring tightens one notch: the
        // room holds its breath with you. (Capped, per the Intake precedent.)
        const step = Math.min(7, Math.floor(S.streak / STREAK_RATCHET));
        fire('audio_trigger', { name: 'streak', level: 0.35 + 0.04 * step, bus: 'fx' });
        S.render.tighten(true);
      }
    }
    function breakStreak(why) {
      if (S.streak >= STREAK_RATCHET) {
        // A break shows what broke it before the meter drains - the break is
        // information, not just loss.
        S.render.stamp(why, 'bad');
      }
      S.streak = 0;
      S.render.streak(0);
      S.render.tighten(false);
    }

    function restraintPct() {
      const opp = S.tally.nogoCount;
      const bad = S.tally.commissions + S.tally.isiCommissions;
      if (!opp) return bad ? 0 : 100;
      return Math.max(0, 100 - (bad / opp) * 100);
    }
    function inducedCount() { return S.errors.filter((e) => e.induced).length; }
    function cleanCount() { return S.errors.filter((e) => !e.induced).length; }

    /* ============================================================ DEBRIEF */
    function debrief() {
      if (!S || destroyed || S.debriefed) return;
      S.debriefed = true;
      S.running = false;
      timers.cancel(S.trialTimer);
      timers.cancel(S.gapTimer);
      timers.cancel(S.trapTimer);
      for (const k of ['ambient_field', 'crt', 'bubble_field', 'row_drift', 'wash', 'gif_rain', 'sub_flash']) stop(k);
      setHeat(0.1);

      const sessionBaseline = median(S.baselineRts);
      const m = metricsFrom(S.tally, S.meta.baselineMs || null, sessionBaseline);
      const gate = sGateOf(m);
      const comp = compositeOf(m);
      const fold = foldBaseline(S.meta, sessionBaseline, Date.now(), !!S.recalibrate);
      const flavor = flavorXpOf(m, S.meta.bestMedianMs || 0);

      S.result = { m, gate, comp, fold, flavor };

      const slipLine = (() => {
        const key = slipKey(m, gate);
        const line = t(key, IC_LEX[key]);
        const off = offRecordPct(m);
        return off > 0 && key === 'ic_slip_speed' ? line.replace('off your record', off + '% off your record') : line;
      })();

      S.render.debrief({
        subject: S.subject,
        medianRt: m.medianRt,
        baselineMs: m.baselineMs,
        established: !!(fold && fold.established),
        restraintPct: (1 - m.falseAlarmRate) * 100,
        induced: inducedCount(),
        clean: cleanCount(),
        gate,
        slipLine,
        events: S.lies ? S.lies.events : [],
        errors: S.errors,
        startedAt: S.startedAt,
        durationMs: Math.max(1, now() - S.startedAt),
        tier: S.gradeTier,
        buzzerLied: S.buzzerLied,
        hint: gate.ok
          ? ''
          : 'S needs both axes: ' + gate.reasons.join(' + '),
      }, submit, recalibrate);

      say('debrief: median ' + (m.medianRt == null ? 'n/a' : Math.round(m.medianRt) + 'ms')
        + ', speedIndex ' + m.speedIndex.toFixed(3)
        + ', FAR ' + (m.falseAlarmRate * 100).toFixed(1) + '%'
        + ', induced ' + inducedCount() + ' / clean ' + cleanCount()
        + ', composite ' + comp.toFixed(3) + ', sGate ' + gate.ok);

      S.autoTimer = timers.after(AUTO_SUBMIT_MS, submit);
    }

    /** The per-game meta write (SYNTHESIS #15): the persisted baseline lives here. */
    function writeMeta() {
      if (!S || !S.result) return null;
      const { m, fold } = S.result;
      const patch = {};
      if (fold) { patch.baselineMs = fold.baselineMs; patch.baselineUpdatedAt = Date.now(); }
      if (m.medianRt != null) {
        const prev = S.recalibrated ? 0 : (Number(S.meta.bestMedianMs) || 0);
        patch.bestMedianMs = prev > 0 ? Math.min(prev, Math.round(m.medianRt)) : Math.round(m.medianRt);
      }
      patch.inducedLifetime = (Number(S.meta.inducedLifetime) || 0) + inducedCount();
      patch.lastSubject = S.subject;
      patch.lastPlayedAt = Date.now();
      try { ctx.store.mergeGameMeta(GAME_KEY, patch); }
      catch (e) { say('meta write failed (grade unaffected): ' + ((e && e.message) || e)); }
      return patch;
    }

    function recalibrate() {
      if (!S) return;
      const sessionBaseline = median(S.baselineRts);
      const fold = foldBaseline(S.meta, sessionBaseline, Date.now(), true);
      if (fold) {
        S.recalibrated = true;
        S.result.fold = fold;
        try { ctx.store.mergeGameMeta(GAME_KEY, { baselineMs: fold.baselineMs, baselineUpdatedAt: Date.now(), bestMedianMs: 0 }); }
        catch (e) { /* noop */ }
        say('baseline recalibrated to ' + fold.baselineMs + 'ms');
      }
    }

    function submit() {
      if (!S || ended) return;
      ended = true;
      timers.cancel(S.autoTimer);
      const { m, gate, comp, flavor } = S.result || {};
      writeMeta();
      try {
        ctx.endClass({
          metrics: { composite: comp == null ? 0 : comp },
          // The dual gate is DECLARED on every class: grades.js only counts gates
          // a game declared, and a failed gate caps the letter at A.
          hardGates: { sGate: !!(gate && gate.ok) },
          flavorXp: flavor ? flavor.xp : 0,
        });
      } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
      say('submitted: grade inputs composite=' + (comp == null ? 'n/a' : comp.toFixed(3))
        + ' sGate=' + !!(gate && gate.ok)
        + ' flavorXp=' + (flavor ? flavor.xp : 0)
        + (m ? ' (' + (m.hits) + '/' + m.goCount + ' go, ' + (m.commissions + m.isiCommissions) + ' false alarms)' : ''));
    }

    /* =========================================================== LIFECYCLE */
    return {
      start(classSpec) {
        try { start(classSpec); }
        catch (e) {
          say('start failed: ' + ((e && e.message) || e));
          try { ctx.root.textContent = 'This class could not start.'; } catch (err) { /* noop */ }
        }
      },

      pause() {
        if (!S || S.paused) return;
        S.paused = true;
        timers.cancel(S.trialTimer); timers.cancel(S.gapTimer); timers.cancel(S.trapTimer);
        if (S.awaiting && !S.awaiting.resolved) { S.awaiting.resolved = true; S.render.hideStimulus(); }
        S.awaiting = null;
      },

      resume() {
        if (!S || !S.paused) return;
        S.paused = false;
        // A paused trial's RT is worthless, so the trial restarts rather than
        // resuming - never score a reaction across a pause.
        if (S.running && !S.debriefed) timers.after(500, nextTrial);
      },

      suspend(on) {
        if (!S) return;
        S.suspended = !!on;
        if (on) {
          timers.cancel(S.trialTimer); timers.cancel(S.gapTimer); timers.cancel(S.trapTimer);
          if (S.awaiting && !S.awaiting.resolved) { S.awaiting.resolved = true; S.render.hideStimulus(); }
          S.awaiting = null;
          for (const k of ['ambient_field', 'crt', 'bubble_field', 'row_drift', 'wash', 'gif_rain', 'sub_flash']) stop(k);
        } else if (S.running && !S.debriefed && !S.paused) {
          timers.after(600, nextTrial);
        }
      },

      destroy() {
        destroyed = true;
        timers.killAll();
        unbindInputs();
        if (S) {
          try { if (S.lies) S.lies.destroy(); } catch (e) { /* noop */ }
          try { if (S.pool && typeof S.pool.release === 'function') S.pool.release(); } catch (e) { /* noop */ }
          try { S.render.destroy(); } catch (e) { /* noop */ }
        }
        S = null;
      },

      /* Diagnostics for the scratch harness and for a future debug overlay. They
         are extra properties on the instance the shell never calls - the five
         lifecycle methods above are the contract. */
      __state() { return S; },
      __submit() { submit(); },
    };
  },
};
