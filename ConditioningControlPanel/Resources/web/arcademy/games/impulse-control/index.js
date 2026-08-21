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
 * FILES
 *   schedule.js  the seeded bubble plan (pure): kinds, flavors, ramp, windows
 *   scoring.js   points + composite + the dual S-gate + baseline fold (pure)
 *   render.js    every pixel: backdrop, basin, HUD, cards; picks the tube tier
 *   tube3d.js    the three.js chute (vendored r185, dynamic import)
 *   tube2d.js    the canvas chute -> static css chute (no-WebGL fallbacks)
 *   style.js     the self-injected stylesheet
 *   lex.js       every lexicon row this class can render
 *
 * THE LAWS THIS FILE KEEPS
 *   - input trust: the bubble is the ONE tap target; every engine burst this
 *     class fires passes clickSafe:true and is decoration only.
 *   - RT integrity: the reveal paint is a class toggle + src swap, and the
 *     clock starts on that same call - nothing asynchronous sits between.
 *   - baseline-relative scoring on the per-game meta store (SYNTHESIS #15).
 *   - the class NEVER grades itself: it reports {metrics:{composite}, hardGates}
 *     and core/grades.js does the rest.
 *   - the debrief renders BEFORE endClass, because endClass tears this DOM down.
 *   - suspend(on) freezes EVERYTHING - timers, tube loop, CSS - and the bubble
 *     in flight is dealt again from the mouth on resume (never scored across
 *     a freeze).
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
const PRESS_DEDUPE_MS = 120;      // one press = one response (pointerdown + click)
const INTRO_MS = 2200;
const TRAVEL_TICK_MS = 40;        // glow position refresh during the slide
const AUTO_SUBMIT_MS = 45000;     // the debrief files itself if nobody clicks
const RESUME_DELAY_MS = 600;

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
  title: 'Impulse Control',

  manifest: {
    /* flash pops fire flash_burst, subliminal pops fire sub_flash; the spiral
       flourish is drawn in-game (the engine has no spiral one-shot). */
    effectsConsumed: ['flash_burst', 'sub_flash'],
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
    const timers = createTimers();
    const reduced = probe('(prefers-reduced-motion: reduce)');

    let S = null;
    let ended = false;
    let destroyed = false;

    const settingOf = (key, dflt) => {
      try {
        const bag = ctx.settings || {};
        return Object.prototype.hasOwnProperty.call(bag, key) ? bag[key] : dflt;
      } catch (e) { return dflt; }
    };
    const fire = (kind, opts) => {
      try {
        const e = ctx.engine;
        if (e && typeof e.fire === 'function') return e.fire(kind, opts);
      } catch (err) { say('fire ' + kind + ': ' + (err && err.message)); }
      return null;
    };
    const setHeat = (h) => {
      try { if (ctx.engine && typeof ctx.engine.setHeat === 'function') ctx.engine.setHeat(h); }
      catch (e) { /* noop */ }
    };

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
        perf: false,
        showRt: settingOf('ic_show_rt', true) !== false,
        seed,                    // the tube grows its skin from the class seed
        log: say,
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
        tally: {
          goodShown: 0, popped: 0, drifted: 0,
          deniedShown: 0, deniedHeld: 0, xClicked: 0,
          rts: [], score: 0,
        },
        swaps: 0,
        voided: false,           // a freeze voided the bubble at the cursor
        debriefed: false,
        recalibrated: false,
        result: null,
      };

      render.mount();
      const capBg = (() => { try { return Number(ctx.caps && ctx.caps.bgIntensity); } catch (e) { return 1; } })();
      render.setBgFade(Number(settingOf('ic_bg_fade', 0.35)) * (isFinite(capBg) && capBg >= 0 ? capBg : 1));
      render.hud({ score: 0, n: 0, total: plan.counts.total, streak: 0, subject: '#' + S.subject });
      render.intro({
        title: t('ic_tube_title', 'The Drop Tube'),
        note: t('ic_tube_rules', IC_LEX.ic_tube_rules),
        hint: goHintLine(),
      });

      bindInputs();

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
      setHeat(0.2);
      S.phaseTimer = timers.after(INTRO_MS, () => { render.clearCard(); nextBubble(); });

      say('class started: tier ' + gradeTier + ', ' + plan.counts.total + ' bubbles ('
        + plan.counts.good + ' good / ' + plan.counts.denied + ' denied), slide '
        + SLIDE_START_MS + '->' + SLIDE_END_MS + 'ms');
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
        const kind = S.rngFx() < 0.7 ? 'loop' : 'still';
        const got = S.pool.next(kind);
        if (got && got.url) { S.render.swapBg(got.url); S.swaps++; }
      } catch (e) { /* keep the old one */ }
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
      S.render.hud({ score: S.score, n: S.idx + 1, total: S.plan.counts.total, streak: S.streak });
      S.render.showLoad();
      setHeat(0.2 + 0.5 * b.progress);
      S.phaseTimer = timers.after(LOAD_MS, () => beginSlide(b));
    }

    function beginSlide(b) {
      if (!S || S.paused || S.suspended) return;
      S.stagePhase = 'slide';
      S.slideStart = now();
      S.slideMs = b.slideMs;
      S.render.setTravel(0);
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
      S.windowTimer = timers.after(b.windowMs, () => windowEnd(b));
    }

    function windowEnd(b) {
      if (!S || S.stagePhase !== 'reveal') return;
      S.stagePhase = 'gap';
      if (b.kind === 'denied') {
        /* the X survived - restraint pays, and the backdrop turns over */
        S.score += DENIED_BONUS;
        S.tally.deniedHeld++;
        S.render.deniedPassed();
        S.render.stamp('calm', t('ic_denied_pass', 'Withheld'));
        swapBackdrop();
      } else {
        /* drifted away - 0 points, NOT an error */
        S.tally.drifted++;
        S.streak = 0;
        S.render.fadeBubble();
        S.render.stamp('', t('ic_missed', 'It drifted away'));
      }
      S.tally.score = S.score;
      S.render.hud({ score: S.score, n: S.idx + 1, total: S.plan.counts.total, streak: S.streak });
      S.phaseTimer = timers.after(b.gapMs, nextBubble);
    }

    function press(source) {
      if (!S || destroyed || S.paused || S.suspended || S.debriefed) return;
      const at = now();
      if (at - S.lastPressAt < PRESS_DEDUPE_MS) return;
      S.lastPressAt = at;
      if (S.stagePhase !== 'reveal' || !S.bubble) return;   // nothing to pop
      const b = S.bubble;
      timers.cancel(S.windowTimer);
      S.stagePhase = 'gap';

      if (b.kind === 'denied') {
        /* THE X. The one error in the game. */
        S.score -= X_PENALTY;
        S.tally.xClicked++;
        S.streak = 0;
        S.render.hitDenied();
        S.render.stamp('bad', t('ic_denied_hit', 'THAT WAS THE X'));
        say('X clicked (' + source + ') at +' + Math.round(at - S.revealAt) + 'ms: -' + X_PENALTY);
      } else {
        const rt = Math.max(0, at - S.revealAt);
        const pts = popPoints(rt, b.windowMs);
        S.score += pts;
        S.streak += 1;
        S.bestStreak = Math.max(S.bestStreak, S.streak);
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
        S.render.hud({ score: S.score, n: S.idx + 1, total: S.plan.counts.total, streak: S.streak, rt });
      }
      S.tally.score = S.score;
      S.phaseTimer = timers.after(b.gapMs, nextBubble);
    }

    /** The pop's effect beat - one of exactly three, all decoration. */
    function beat(flavor) {
      if (flavor === 'flash') {
        fire('flash_burst', { clickSafe: true, strength: 0.6 });
      } else if (flavor === 'sub') {
        fire('sub_flash', { clickSafe: true, strength: 0.6 });
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
      S.render.setTravel(null);
      setHeat(0);

      const sessionMedian = median(S.tally.rts);
      const fold = foldBaseline(S.meta, sessionMedian, false);
      const led = ledger(S.tally, S.meta);
      S.result = { led, fold };

      const record = Number(S.meta.bestRtMs) || 0;
      const newBest = led.bestRt != null && (record === 0 || led.bestRt < record);

      const line = (() => {
        if (fold && fold.established) return t('ic_baseline_new', IC_LEX.ic_baseline_new);
        const slowSlip = led.speed < 0.5;
        const xSlip = S.tally.xClicked > 0;
        if (slowSlip && xSlip) return t('ic_slip_both', IC_LEX.ic_slip_both);
        if (xSlip) return t('ic_slip_restraint', IC_LEX.ic_slip_restraint);
        if (slowSlip) return t('ic_slip_speed', IC_LEX.ic_slip_speed);
        return t('ic_slip_none', IC_LEX.ic_slip_none);
      })();

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
        line,
        hint: led.sGate.ok ? '' : t('ic_gate_hint', IC_LEX.ic_gate_hint),
      }, submit, recalibrate);

      say('debrief: score ' + led.score
        + ', median ' + (led.medianRt == null ? 'n/a' : Math.round(led.medianRt) + 'ms')
        + ', popped ' + S.tally.popped + '/' + S.tally.goodShown
        + ', X ' + S.tally.xClicked + '/' + S.tally.deniedShown
        + ', composite ' + led.composite.toFixed(3) + ', sGate ' + led.sGate.ok
        + ', swaps ' + S.swaps);

      S.autoTimer = timers.after(AUTO_SUBMIT_MS, submit);
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
      timers.cancel(S.autoTimer);
      const { led } = S.result || {};
      writeMeta();
      try {
        ctx.endClass({
          metrics: { composite: led ? led.composite : 0 },
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
        try { start(classSpec); }
        catch (e) {
          say('start failed: ' + ((e && e.message) || e));
          try { ctx.root.textContent = 'This class could not start.'; } catch (err) { /* noop */ }
        }
      },

      pause() {
        if (!S || S.paused) return;
        S.paused = true;
        freeze();
        S.render.suspend(true);
      },

      resume() {
        if (!S || !S.paused) return;
        S.paused = false;
        S.render.suspend(false);
        thaw();
      },

      suspend(on) {
        if (!S) return;
        const want = !!on;
        if (want === S.suspended) return;
        S.suspended = want;
        if (want) {
          freeze();
          S.render.suspend(true);
        } else {
          S.render.suspend(false);
          if (!S.paused) thaw();
        }
      },

      destroy() {
        destroyed = true;
        timers.killAll();
        unbindInputs();
        if (S) {
          try { if (S.pool && typeof S.pool.release === 'function') S.pool.release(); } catch (e) { /* noop */ }
          try { S.render.destroy(); } catch (e) { /* noop */ }
        }
        S = null;
      },

      /* Diagnostics for the scratch harness and a future debug overlay. */
      __state() { return S; },
      __submit() { submit(); },
    };
  },
};
