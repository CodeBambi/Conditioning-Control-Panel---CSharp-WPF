/* ============================================================================
 * games/lost-and-found/index.js - LOST & FOUND (family: search, flagship).
 *
 * Where's Waldo, except the crowd is alive: a dense mosaic of looping tiles
 * drifts, glitches and swaps under you while you hunt one clip five times. The
 * Distraction Engine is the difficulty slider - effect dials rise FIRST, classic
 * difficulty (density, near-twin share) second (GROUND-RULES §6 ordering).
 *
 * ---------------------------------------------------------------------------
 * WHERE THE RULES LIVE
 *   constants.js  every tunable, incl. the PLAYTEST block the dossier flagged
 *   board.js      the mosaic (look signatures, the swap primitive, two-layer drift)
 *   hud.js        chrome + the briefing / peek / spotlight cards
 *   grade.js      the game-specific inputs to the SHARED rubric (no letters here)
 *   styles.js     this game's CSS, namespaced .g-lf-*
 *
 * ---------------------------------------------------------------------------
 * THE FIVE-FIND ARC (synthesis ruling - the class has a beginning/middle/end)
 *   finds 1-2  the tier's baseline board
 *   find  3    THE MODIFIER: the board wakes up (hotter heat, wider swap bursts),
 *              announced, for the rest of the class
 *   find  5    THE FINAL BELL: announced, and the clutch ease ("the board
 *              relents") is GUARANTEED rather than conditional on the clock
 *
 * TASTE OF THE TWIST (SYNTHESIS #2, from grade_tier 2): the first relocation of
 * the class is telegraphed and slowed once, so the player MEETS the signature
 * twist - "the target moves inside the same glitch the decoys use" - before they
 * form an opinion of the game. Every later relocation is silent and identical to
 * noise churn.
 *
 * INPUT TRUST (DECISIONS #9): this is a click-precision surface, so flash_burst
 * is NOT consumed at all and every burst/bubble we do fire passes clickSafe:true
 * (pointer-events:none). Nothing decorative can ever steal a click from a tile.
 * ==========================================================================*/

import {
  FINDS_PER_CLASS, MODIFIER_AFTER_FIND, FINAL_BELL_FIND, PLAYTEST, TIERS,
  MOBILE_DENSITY, HEAT_BAND, MODIFIER_HEAT_STEP, BEAT_MS, TICK_MS,
  ASSEMBLE_STAGGER_MS, CLAIM_TIMEOUT_MS, POOL_OVERPROVISION, DISCRETE_STEP_MS,
} from './constants.js';
import { createBoard } from './board.js';
import { createHud } from './hud.js';
import { injectStyles } from './styles.js';
import { scoreClass } from './grade.js';
import { createTimers, probeReduced, probeCoarse, clamp, clamp01, shuffle } from './util.js';

/* ----------------------------------------------------------------------------
 * THE REWARD CANON, loaded LATE and never fatally.
 *
 * The variable-ratio schedule is shared canon (Intake reward.js -> engine/
 * schedule.js: baseChance .30->.60, JACKPOT_ROLL .85, STREAK_CAP 8 x .03) and a
 * game inventing its own would be a private formula. The per-class engine handle
 * does not expose rewardRoll() (flagged in the build report), so we import the
 * pure module directly - dynamically, because engine/ is an OPTIONAL layer by
 * contract and a missing file must cost us the spectacle, not the class.
 * -------------------------------------------------------------------------- */
const FALLBACK_SCHEDULE = {
  roll: () => ({ fire: true, intensity: 0.5, jackpot: false, nearMiss: false, kind: 'chime' }),
};

function loadSchedule(seed, log) {
  return import('../../engine/schedule.js')
    .then((m) => {
      if (m && typeof m.createRewardSchedule === 'function') {
        return m.createRewardSchedule({ seed: seed + '|lf-reward', mode: m.RewardMode.VariableRatio });
      }
      return FALLBACK_SCHEDULE;
    })
    .catch((e) => {
      log('reward schedule unavailable (' + ((e && e.message) || e) + ') - flat rewards');
      return FALLBACK_SCHEDULE;
    });
}

export default {
  key: 'lost_and_found',
  family: 'search',
  // MEATY-ELIGIBLE (orchestrator ruling): Lost & Found fills the timetable's one
  // meaty slot in Semester 1, since The Deep End - the dossier's meaty comfort
  // class - is not in this build. The class itself is still ~120s; `meaty` only
  // widens the budget the shell may hand us (up to 300s, BUILD-CONTRACT §7) and
  // we grade against whatever budget arrives (grade.js parSecFor).
  meaty: true,
  flagship: true,
  timeBudgetSec: 120,
  title: 'Lost & Found',

  manifest: {
    // Everything we fire or sustain, including what the ceremonies reach for.
    // flash_burst is deliberately ABSENT (input-trust law, DECISIONS #9).
    effectsConsumed: ['glitch_swap', 'row_drift', 'sub_flash', 'bubble_field', 'wash',
      'gif_burst', 'audio_trigger', 'crt', 'ambient_field'],
    // DOM-layer consumer: canvasSafe FALSE, so the CORS-tainted remote pool is
    // legal here. Remote arrives LATE and upgrades tiles through the ordinary
    // swap churn; a draw is never blocked on it (FlashService posture).
    assetNeeds: { loops: 40 + POOL_OVERPROVISION, targets: 1, stills: 6, canvasSafe: false },
    // values[0] is the SHELL'S DEFAULT, so it must be the "no cap" end of the
    // ladder: par is met at every tier unless the player deliberately caps down
    // (which is what "playing below tier par caps at A" is for).
    boardSizes: { values: [40, 30, 24, 20, 16, 12], par: { 1: 16, 2: 20, 3: 30, 4: 40 } },
    keybinds: [{ verb: 'peek', label_key: 'lf_peek_key', default: 'Space' }],
    // NOTE the key name: shell/settings.js reads `values` for an enum (an
    // `options` array is silently an "unknown kind" and the row never renders).
    settings: [
      {
        key: 'lf_zen', kind: 'bool', default: false,
        label_key: 'lf_zen', hint_key: 'lf_zen_hint',
      },
      {
        key: 'lf_peek_input', kind: 'enum', default: 'auto',
        values: ['auto', 'hold', 'tap-toggle'],
        label_key: 'lf_peek_input', hint_key: 'lf_peek_input_hint',
      },
    ],
    peek: true,
  },

  create(ctx) {
    const t = ctx.lexicon;
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };
    const rng = typeof ctx.rng === 'function' ? ctx.rng : Math.random;
    const timers = createTimers();

    /* ------------------------------------------------------------- state */
    let phase = 'idle';           // idle | briefing | hunt | ceremony | done
    let tier = 1;
    let dials = TIERS[1];
    let density = 16;
    let zen = false;
    let budgetSec = 120;
    let reduced = false;
    let coarse = false;

    let board = null;
    let hud = null;
    let pool = null;
    let reward = FALLBACK_SCHEDULE;
    let drift = null;             // the row_drift sustain handle
    let subStream = null;
    let burstPiece = null;

    let finds = 0;
    let misclicks = 0;
    let misclickStreak = 0;
    let cleanStreak = 0;
    let bestCleanStreak = 0;
    let jackpots = 0;
    let relocations = 0;
    const findTimes = [];

    let modifierOn = false;
    let bellRung = false;
    let clutchOn = false;
    let tasteShown = false;

    let clockStartedAt = 0;
    let findStartedAt = 0;
    let pausedMs = 0;
    let pausedAt = 0;
    let findPausedBase = 0;
    let penaltySec = 0;
    let cleanThisFind = true;

    let churnTimer = 0;
    let beatTimer = 0;
    let tickTimer = 0;
    let pityTimer = 0;
    let relocTimer = 0;
    let stepTimer = 0;

    let ended = false;
    let halted = false;           // pause() or suspend(true)

    /* ------------------------------------------------------- small helpers */
    const heatNow = () => clamp01(
      dials.heat
      + HEAT_BAND * (finds / FINDS_PER_CLASS)
      + (modifierOn ? MODIFIER_HEAT_STEP : 0)
      - (clutchOn ? PLAYTEST.CLUTCH_HEAT_EASE : 0)
    );

    function setHeat() {
      try { ctx.engine.setHeat(heatNow()); } catch (e) { /* engine is optional */ }
    }

    const elapsedSec = () => (clockStartedAt
      ? Math.max(0, (Date.now() - clockStartedAt - pausedMs) / 1000)
      : 0);
    const secLeft = () => Math.max(0, budgetSec - elapsedSec() - penaltySec);

    function targetLook() {
      const tile = board && board.targetTile();
      return tile ? { grad: tile.grad, hue: tile.hue, url: tile.url } : {};
    }

    /** The board size we actually deal: tier density, capped by device and player. */
    function effectiveDensity() {
      let d = dials.density;
      if (coarse) d = Math.min(d, MOBILE_DENSITY[tier] || d);
      const cap = Number(ctx.settings && ctx.settings.boardSize);
      if (Number.isFinite(cap) && cap > 0) d = Math.min(d, cap);
      return clamp(d, 8, 40);
    }

    /** lf_peek_input: auto resolves from the live coarse-pointer probe. */
    function peekMode() {
      const want = String((ctx.settings && ctx.settings.lf_peek_input) || 'auto');
      if (want === 'hold' || want === 'tap-toggle') return want;
      return coarse ? 'tap-toggle' : 'hold';
    }

    function announce(text, ms) {
      if (!hud) return;
      hud.taunt(text);
      timers.after(ms || 1600, () => { if (hud) hud.clearTaunt(); });
    }

    /* --------------------------------------------------------------- assets */
    /**
     * Claim the pool. NEVER blocks the class: if the provider is slow, missing or
     * throwing we deal a gradient-only board (still winnable - every tile has a
     * unique look signature) and dress it if the pool turns up later.
     */
    function claimAssets() {
      const spec = {
        loops: density + POOL_OVERPROVISION,
        targets: 1,
        stills: 6,
        canvasSafe: false,
      };
      if (tier >= 3) {
        // Provider hints for near-twin decoys (same-niche remote / same-folder
        // local). The provider does not read them yet - flagged in the build
        // report - so board.assignWarm() does the local fallback either way.
        spec.nearTwinBias = true;
        spec.nearTwinTag = true;
      }
      let settled = false;
      const done = (p) => {
        if (settled) return;
        settled = true;
        pool = p || null;
        if (pool) dressBoard();
      };
      timers.after(CLAIM_TIMEOUT_MS, () => {
        if (!settled) say('asset claim slow - dealing the gradient board, dressing later');
      });
      try {
        Promise.resolve(ctx.assets.claim(spec))
          .then((p) => done(p))
          .catch((e) => { say('asset claim failed (degrading): ' + ((e && e.message) || e)); done(null); });
      } catch (e) {
        say('asset claim threw (degrading): ' + ((e && e.message) || e));
        done(null);
      }
    }

    /** Draw a decoy url that is not the target's (low tiers must have no twins). */
    function drawDecoy(targetUrl) {
      if (!pool || typeof pool.next !== 'function') return null;
      for (let i = 0; i < 3; i++) {
        const got = pool.next('loop');
        if (!got || !got.url) return null;
        if (!targetUrl || got.url !== targetUrl) return got;
      }
      return null;
    }

    /** Put media on the board: the target first, then decoys that avoid it. */
    function dressBoard() {
      if (!board || !pool) return;
      const target = board.targetTile();
      // A pool that lands LATE (slow disk, remote batch mid-class) may still
      // upgrade the decoys, but the target's look is frozen the moment the player
      // has memorised it - changing it under them would be a lie, not an upgrade.
      const late = phase !== 'idle' && phase !== 'briefing';
      let targetUrl = target ? target.url : null;
      try {
        const got = pool.next('target');
        if (got && got.url && !late) { targetUrl = got.url; board.setUrl(target, got); }
      } catch (e) { say('target draw failed - gradient target stands'); }
      for (const tile of board.tiles) {
        if (tile.target) continue;
        const got = drawDecoy(targetUrl);
        if (got) board.setUrl(tile, got);
      }
      // Near-twin decoys are the classic-difficulty lever, tiers 3-4 only.
      const warm = board.assignWarm({ share: dials.nearTwinShare, rng });
      // remote/local media can land after the briefing card is up
      if (hud) hud.refreshCards(targetLook());
      say('board dressed: ' + density + ' tiles, ' + warm + ' near-twins, '
        + (targetUrl ? 'target media ok' : 'target on its look signature only'));
    }

    /* ---------------------------------------------------------------- swaps */
    /**
     * THE swap primitive: pick pairs, dress them with ONE glitch_swap, exchange
     * looks at the transition midpoint. Relocation calls exactly this with the
     * target's tile in the first pair, which is what makes target motion and
     * decoy noise indistinguishable.
     *
     * If the engine refuses (bgIntensity capped to 0, or no engine at all) the
     * swap still happens - immediately and undressed. A player who capped effects
     * off must still be able to finish the class.
     */
    function swapBurst(pairs, opts) {
      if (!board) return 0;
      const o = opts || {};
      const pool2 = board.tiles.filter((tile) => (o.withTarget ? true : !tile.target));
      const picks = shuffle(pool2, rng);
      const chosen = [];
      if (o.withTarget) {
        const target = board.targetTile();
        const other = picks.find((tile) => tile !== target);
        if (target && other) chosen.push([target, other]);
      }
      for (const tile of picks) {
        if (chosen.length >= pairs) break;
        if (chosen.some((p) => p[0] === tile || p[1] === tile)) continue;
        const mate = picks.find((m) => m !== tile
          && !chosen.some((p) => p[0] === m || p[1] === m));
        if (!mate) break;
        chosen.push([tile, mate]);
      }
      if (!chosen.length) return 0;

      const els = [];
      for (const [a, b] of chosen) { els.push(...a.els, ...b.els); }
      const apply = () => {
        for (const [a, b] of chosen) board.swapLooks(a, b);
        // THE REMOTE UPGRADE PATH: media that arrives late reaches the board here,
        // one decoy per churn tick, under the same glitch cover as the swap - so a
        // remote batch never repaints the whole mosaic in one frame.
        if (o.upgrade) {
          // never the target (its look is memorised) and never a near-twin (its
          // look is the whole point of the warm tease)
          let tile = null;
          for (const [a, b] of chosen) {
            if (!a.target && !a.warm) { tile = a; break; }
            if (!b.target && !b.warm) { tile = b; break; }
          }
          const target = board.targetTile();
          const got = tile ? drawDecoy(target ? target.url : null) : null;
          if (got) board.setUrl(tile, got);
        }
      };

      let dressed = null;
      try {
        dressed = ctx.engine.fire('glitch_swap', {
          targets: els,
          seconds: o.seconds == null ? 0.6 : o.seconds,
          durationMult: o.durationMult == null ? 1 : o.durationMult,
          variant: o.variant,
          onSwap: apply,
        });
      } catch (e) { dressed = null; }
      if (!dressed) apply();          // effects capped off must never wedge the hunt
      return chosen.length;
    }

    function noiseSwap() {
      if (phase !== 'hunt') return;
      const n = dials.swapBurst
        + (rng() < dials.burstChance ? 2 : 0)
        + (modifierOn ? 1 : 0);
      // upgrade:true is how late-arriving media gets onto the board (see apply())
      swapBurst(n, { upgrade: true });
    }

    /**
     * Move the target. Hidden inside a burst of ordinary swaps - except for the
     * one telegraphed instance per class from tier 2 (taste of the twist).
     */
    function relocate() {
      if (!board) return;
      const telegraph = (tier >= 2 && !tasteShown);
      const n = Math.max(2, dials.swapBurst + 1 + (modifierOn ? 1 : 0));
      const moved = swapBurst(n, {
        withTarget: true,
        seconds: telegraph ? 1.4 : 0.6,
        durationMult: telegraph ? 1.6 : 1,
        variant: telegraph ? 'crossfade' : undefined,
      });
      relocations += 1;
      if (telegraph) {
        tasteShown = true;
        announce(t('lf_relocate', 'It moved - the same glitch hides the churn'), 2400);
      }
      return moved;
    }

    /* ------------------------------------------------------------ briefing */
    function briefing(onDone) {
      phase = 'briefing';
      if (!board || !hud) { onDone(); return; }
      // The mosaic assembles tile by tile - diegetic loading that doubles as
      // preloader cover for whatever the provider is still fetching.
      const els = board.tiles.map((tile) => board.primaryEl(tile)).filter(Boolean);
      els.forEach((node, i) => {
        if (node && node.style) node.style.opacity = '0';
        timers.after(Math.min(1400, i * ASSEMBLE_STAGGER_MS), () => {
          if (node && node.style) node.style.opacity = '';
        });
      });
      hud.showBriefing(targetLook(), t('lf_briefing', 'Memorize her, then find her five times.'));
      hud.setProgress(0, FINDS_PER_CLASS);
      hud.setChips(0, 0);

      const previewMs = Math.round(dials.previewSec * 1000);
      timers.after(previewMs, () => {
        // The collapse is a fake-out: the card shrinks INTO the board and ends
        // nowhere, so the player never sees where the target landed.
        if (hud) hud.collapseBriefing();
        swapBurst(Math.max(2, dials.swapBurst + 1), { withTarget: true, seconds: 0.5 });
        timers.after(reduced ? 60 : 420, () => {
          if (hud) hud.hideBriefing();
          onDone();
        });
      });
    }

    /* ---------------------------------------------------------------- hunt */
    function startEffects() {
      setHeat();
      try {
        drift = ctx.engine.sustain('row_drift', {
          targets: board.rowEls(), axis: 'x', variant: 'slide',
        });
      } catch (e) { drift = null; }
      if (dials.crt) {
        try { ctx.engine.sustain('crt', { level: tier >= 4 ? 0.4 : 0.2, variant: 'scanline' }); } catch (e) { /* optional */ }
      }
      if (dials.bubbles) {
        // clickSafe: decoy bubbles OCCLUDE, they never take a click off a tile.
        try { ctx.engine.sustain('bubble_field', { clickSafe: true, max: tier >= 4 ? 14 : 8 }); } catch (e) { /* optional */ }
      }
      if (dials.sub) {
        // the cadence-driven sub_flash stream (word pool may be EMPTY - the
        // engine then skips word cards silently, which is the contract)
        try { subStream = ctx.engine.sustain('sub_flash', { variant: 'scatter' }); } catch (e) { subStream = null; }
      }
      if (tier >= 3) {
        try {
          burstPiece = ctx.engine.setpiece({
            key: 'lf_burst_swap',
            perBeatChance: 0.35,
            minGap: 2,
            run: () => swapBurst(3, { upgrade: true }),
          });
        } catch (e) { burstPiece = null; }
      }
    }

    function stopEffects() {
      for (const kind of ['row_drift', 'bubble_field', 'crt', 'wash', 'sub_flash', 'ambient_field']) {
        try { ctx.engine.stop(kind); } catch (e) { /* optional */ }
      }
      drift = null; subStream = null;
      if (burstPiece && typeof burstPiece.unregister === 'function') {
        try { burstPiece.unregister(); } catch (e) { /* ignore */ }
      }
      burstPiece = null;
    }

    function armLoops() {
      churnTimer = timers.every(dials.swapMs, () => { if (!halted) noiseSwap(); });
      beatTimer = timers.every(BEAT_MS, () => {
        if (halted || phase !== 'hunt') return;
        // garnish rotation decorates ordinary beats from tier 2 (tier 1 = no overlays)
        try { ctx.engine.beat({ garnish: tier >= 2 }); } catch (e) { /* optional */ }
      });
      tickTimer = timers.every(TICK_MS, tick);
      if (reduced) {
        // continuous drift -> discrete row steps (the shell has frozen every
        // animation, so this is the only motion the board has left)
        stepTimer = timers.every(DISCRETE_STEP_MS, () => { if (!halted) board.step(); });
      }
      if (tier >= 4 && PLAYTEST.TIER4_MIDHUNT_RELOCATE) {
        relocTimer = timers.every(PLAYTEST.TIER4_MIDHUNT_RELOCATE_MS, () => {
          if (halted || phase !== 'hunt') return;
          if (Date.now() - findStartedAt < PLAYTEST.MIDHUNT_RELOCATE_GRACE_MS) return;
          relocate();
        });
      }
    }

    function beginHunt() {
      phase = 'hunt';
      cleanThisFind = true;
      findStartedAt = Date.now();
      findPausedBase = pausedMs;
      if (hud) {
        hud.setProgress(finds, FINDS_PER_CLASS);
        hud.dim(false);
      }
      if (board) { board.freeze(false); board.clearMark('g-lf-found'); }
      setHeat();
      armPity();
      if (finds + 1 === FINAL_BELL_FIND && !bellRung) {
        bellRung = true;
        announce(t('lf_final_bell', 'Final bell'), 2000);
        // "guaranteed clutch cinematics": if the clock never gets tight enough,
        // the board relents anyway on the last find.
        timers.after(PLAYTEST.CLUTCH_BELL_DELAY_MS, () => {
          if (phase === 'hunt' && !clutchOn) clutch();
        });
      }
    }

    function armPity() {
      if (pityTimer) { timers.cancel(pityTimer); pityTimer = 0; }
      if (!board) return;
      pityTimer = timers.after(PLAYTEST.PITY_STUCK_MS, function pulse() {
        if (halted || phase !== 'hunt') return;
        if (Date.now() - findStartedAt < PLAYTEST.PITY_MIN_ELAPSED_MS) return;
        const target = board.targetTile();
        board.mark(target, 'g-lf-pity', true);
        timers.after(PLAYTEST.PITY_SHIMMER_MS + 60, () => board.mark(target, 'g-lf-pity', false));
        pityTimer = timers.after(PLAYTEST.PITY_REPEAT_MS, pulse);
      });
    }

    function clutch() {
      if (clutchOn) return;
      clutchOn = true;
      if (churnTimer) { timers.cancel(churnTimer); churnTimer = 0; }   // the churn pauses
      if (board) board.setDriftMult(PLAYTEST.CLUTCH_DRIFT_EASE);
      setHeat();
      announce(t('lf_clutch', 'The board relents'), 1800);
      say('clutch ease engaged');
    }

    function tick() {
      if (halted || phase === 'done') return;
      if (!hud) return;
      if (zen) { hud.setClock(null); return; }
      const left = secLeft();
      hud.setClock(left);
      if (left <= 0) { finish(false); return; }
      if (finds === FINDS_PER_CLASS - 1 && left <= PLAYTEST.CLUTCH_SEC_LEFT) clutch();
    }

    /* --------------------------------------------------------------- clicks */
    function onTileClick(tile, e) {
      if (phase !== 'hunt' || halted) return;
      if (tile.target) onFind(tile);
      else if (tile.warm) onWarm(tile, e);
      else onMiss(tile, e);
    }

    function peeksNow() {
      try { return (ctx.peek && ctx.peek.stats && ctx.peek.stats.count) || 0; } catch (e) { return 0; }
    }

    function onFind(tile) {
      phase = 'ceremony';
      const took = Math.max(0.05, (Date.now() - findStartedAt - (pausedMs - findPausedBase)) / 1000);
      findTimes.push(took);
      finds += 1;
      if (cleanThisFind) {
        cleanStreak += 1;
        if (cleanStreak > bestCleanStreak) bestCleanStreak = cleanStreak;
      } else {
        cleanStreak = 0;
      }

      if (pityTimer) { timers.cancel(pityTimer); pityTimer = 0; }
      if (churnTimer) { timers.cancel(churnTimer); churnTimer = 0; }

      /* ---- the found ceremony (board dims, target spotlights, sting) ---- */
      if (hud) {
        hud.dim(true);
        hud.setProgress(finds, FINDS_PER_CLASS);
        hud.showSpot(targetLook(), t('lf_found', 'Found her'));
      }
      if (board) { board.freeze(true); board.mark(tile, 'g-lf-found', true); }
      try {
        ctx.engine.fire('audio_trigger', {
          name: 'sting', level: clamp01(0.45 + 0.05 * cleanStreak), bus: 'fx', duck: 'spotlight',
        });
      } catch (e) { /* optional */ }
      try { ctx.ceremonies.stamp({ text: t('lf_found', 'Found her'), target: hud && hud.stampAnchor }); } catch (e) { /* optional */ }
      try { ctx.ceremonies.streakMeter({ target: hud && hud.streakMount, filled: cleanStreak, gold: cleanStreak >= 3 }); } catch (e) { /* optional */ }
      if (!reduced) {
        try { ctx.engine.sustain('ambient_field', { kind: cleanStreak >= 3 ? 'goldleaf' : 'confetti' }); } catch (e) { /* optional */ }
        timers.after(900, () => { try { ctx.engine.stop('ambient_field'); } catch (e) { /* ignore */ } });
      }

      /* ---- the variable-reward beat -------------------------------------- */
      let roll = null;
      try { roll = reward.roll({ heat: heatNow(), success: true, streak: cleanStreak }); } catch (e) { roll = null; }
      if (roll && roll.jackpot) {
        jackpots += 1;
        try { ctx.ceremonies.reward('jackpot', { intensity: roll.intensity, target: hud && hud.stampAnchor, text: t('lf_jackpot', 'Jackpot') }); } catch (e) { /* optional */ }
        // INPUT TRUST: clickSafe over a click-precision board, always.
        try { ctx.engine.fire('gif_burst', { clickSafe: true, count: 4, assetKind: 'loop' }); } catch (e) { /* optional */ }
      } else if (roll && roll.nearMiss) {
        try { ctx.ceremonies.reward('near_miss', { intensity: roll.intensity, target: hud && hud.stampAnchor }); } catch (e) { /* optional */ }
      }

      /* ---- the arc ------------------------------------------------------- */
      if (finds >= MODIFIER_AFTER_FIND && !modifierOn) {
        modifierOn = true;
        announce(t('lf_modifier', 'The board wakes up'), 2000);
        say('modifier engaged after find ' + finds);
      }

      const ceremonyMs = reduced ? PLAYTEST.FOUND_CEREMONY_MS_REDUCED : PLAYTEST.FOUND_CEREMONY_MS;
      timers.after(ceremonyMs, () => {
        if (phase === 'done') return;
        if (hud) hud.hideSpot();
        if (finds >= FINDS_PER_CLASS) { finish(true); return; }
        relocate();
        // the churn resumes unless the board has already relented
        if (!clutchOn) churnTimer = timers.every(dials.swapMs, () => { if (!halted) noiseSwap(); });
        beginHunt();
      });
    }

    /** Shared accounting for any wrong click; only the punishment differs. */
    function countWrong() {
      misclicks += 1;
      misclickStreak += 1;
      cleanThisFind = false;
      cleanStreak = 0;
      if (!zen) penaltySec += PLAYTEST.MISCLICK_TIME_PENALTY_SEC;
      if (hud) hud.setChips(misclicks, peeksNow());
    }

    function onMiss(tile, e) {
      countWrong();
      const anchor = (tile && tile.els && tile.els[0]) || (hud && hud.view) || null;
      // the punishment IS a distraction, clamped under the caps vector like
      // everything else - a word card at the cursor plus a small bubble puff
      try {
        ctx.engine.fire('sub_flash', {
          text: t('lf_misclick', 'Wrong one'), image: false, anchor, variant: 'stamp',
        });
      } catch (err) { /* optional */ }
      try {
        ctx.engine.sustain('bubble_field', { clickSafe: true, max: 4, cadenceMs: 220 });
        timers.after(700, () => { if (!dials.bubbles) { try { ctx.engine.stop('bubble_field'); } catch (e2) { /* ignore */ } } });
      } catch (err) { /* optional */ }
      try { ctx.engine.fire('audio_trigger', { name: 'stamp_bad', level: 0.45, bus: 'fx' }); } catch (err) { /* optional */ }

      if (misclickStreak >= PLAYTEST.MISCLICK_STREAK_FOR_WASH) {
        misclickStreak = 0;
        announce(t('lf_misclick_streak', 'Focus'), 1500);
        if (tier >= PLAYTEST.MISCLICK_WASH_FROM_TIER) {
          try { ctx.engine.sustain('wash', { variant: 'pink', holdMs: 600 }); } catch (err) { /* optional */ }
        }
      }
    }

    /**
     * A near-twin decoy: same accounting as any wrong click, but the tease
     * replaces the detonation - a soft tick and a shimmer on the REAL target, so
     * the player learns they were warm.
     */
    function onWarm(tile, e) {
      countWrong();
      misclickStreak = Math.max(0, misclickStreak - 1);    // warm is not "flailing"
      try { ctx.ceremonies.reward('near_miss', { target: hud && hud.stampAnchor, text: t('lf_warm', 'Warm') }); } catch (err) { /* optional */ }
      try { ctx.engine.fire('audio_trigger', { name: 'blip', level: 0.3, bus: 'fx' }); } catch (err) { /* optional */ }
      if (board) {
        const target = board.targetTile();
        board.mark(target, 'g-lf-warm', true);
        timers.after(PLAYTEST.NEAR_TWIN_SHIMMER_MS + 60, () => board.mark(target, 'g-lf-warm', false));
      }
    }

    /* --------------------------------------------------------------- finish */
    function finish(complete) {
      if (ended) return;
      ended = true;
      phase = 'done';
      timers.killAll();
      stopEffects();
      if (hud) { hud.hideSpot(); hud.hidePeek(); hud.dim(false); hud.setClock(zen ? null : secLeft()); }
      if (board) board.freeze(true);

      const peeks = peeksNow();
      const score = scoreClass({
        findTimesSec: findTimes,
        misclicks,
        peeks,
        bestCleanStreak,
        density,
        drift: dials.drift,
        timeBudgetSec: zen ? null : budgetSec,
        jackpots,
      });
      if (!complete && !zen) announce(t('lf_timeout', 'Time'), 2000);

      say('class over: ' + finds + '/' + FINDS_PER_CLASS + ' finds, median '
        + score.medianSec.toFixed(1) + 's (par ' + score.par.toFixed(1) + 's), '
        + misclicks + ' misses, ' + peeks + ' peeks, best clean ' + bestCleanStreak
        + ', ' + relocations + ' relocations, composite ' + score.composite.toFixed(3)
        + (score.hardGates.sGate ? ' [S gate ok]' : ' [S gate failed]'));

      try {
        ctx.endClass({
          metrics: { composite: score.composite },
          hardGates: score.hardGates,
          zen,
          flavorXp: score.flavorXp,
          // No share payload: v1 ships Daily Trigger's card ONLY (DECISIONS #6).
          // The numbers-only report-card share is designed, not built.
        });
      } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
    }

    /* ------------------------------------------------------------ the peek */
    function wirePeek() {
      ctx.peek.setHandlers({
        onReveal: () => { if (hud) hud.showPeek(targetLook()); },
        onHide: () => {
          if (!hud) return;
          hud.hidePeek();
          hud.setChips(misclicks, peeksNow());     // the count moves on EVERY peek
        },
        onFirstUse: () => {
          // The cap itself is the shell's; we only tell the player, once.
          announce(t('peek_hint', 'Hold to peek. Using it caps this class at A.'), 1800);
          if (hud) hud.setChips(misclicks, peeksNow());
        },
      });
      const btn = hud && hud.peekButton;
      if (peekMode() === 'tap-toggle') {
        // Touch and motor-limited players get a toggle instead of a sustained
        // hold; it drives the SAME shell verb, so the A-cap can only apply once.
        if (btn) {
          btn.addEventListener('click', () => {
            if (ctx.peek.holding) ctx.peek.release(); else ctx.peek.hold();
            timers.after(40, () => { if (hud) hud.setChips(misclicks, peeksNow()); });
          });
        }
      } else if (btn) {
        ctx.peek.attach(btn);
      }
      ctx.peek.bindKeys(ctx.keys, 'peek');
    }

    /* --------------------------------------------------------- the instance */
    return {
      start(classSpec) {
        const spec = classSpec || {};
        tier = clamp(Math.round(Number(spec.gradeTier) || 1), 1, 4);
        dials = TIERS[tier] || TIERS[1];
        budgetSec = clamp(Number(spec.timeBudgetSec) || 120, 30, 300);
        zen = !!(ctx.settings && ctx.settings.lf_zen);
        reduced = probeReduced();
        coarse = probeCoarse();
        density = effectiveDensity();

        injectStyles();
        hud = createHud({
          root: ctx.root, t, keys: ctx.keys, coarse, lite: coarse, zen,
        });
        board = createBoard({
          mount: hud.view, density, rng, drift: dials.drift,
          lite: coarse, reduced, log: say, onTileClick,
        });
        // Somebody has to be the target before any media exists, so the class is
        // playable even if the provider never answers.
        board.setTarget(board.tiles[Math.floor(rng() * board.tiles.length)] || board.tiles[0]);
        if (hud) hud.setTargetArt(targetLook());
        wirePeek();
        claimAssets();
        loadSchedule(String(spec.seed || 'lf'), say).then((s) => { reward = s || FALLBACK_SCHEDULE; });

        say('class start: tier ' + tier + ', density ' + density + '/' + dials.density
          + (coarse ? ' (coarse)' : '') + (reduced ? ' (reduced motion)' : '')
          + ', budget ' + (zen ? 'zen' : budgetSec + 's'));

        briefing(() => {
          clockStartedAt = Date.now();
          startEffects();
          armLoops();
          beginHunt();
          if (hud) hud.setClock(zen ? null : budgetSec);
        });
      },

      pause() { halt(true); },
      resume() { halt(false); },
      /** The host says stop NOW (mandatory video / panic). Same freeze, no UI. */
      suspend(on) { halt(!!on); },

      destroy() {
        ended = true;
        phase = 'done';
        timers.dispose();
        stopEffects();
        try { if (pool && pool.release) pool.release(); } catch (e) { /* ignore */ }
        pool = null;
        try { if (board) board.destroy(); } catch (e) { /* ignore */ }
        try { if (hud) hud.destroy(); } catch (e) { /* ignore */ }
        board = null; hud = null;
        try { if (ctx.root) ctx.root.textContent = ''; } catch (e) { /* ignore */ }
      },

      /**
       * Diagnostics seam (not part of the module contract - the shell never calls
       * it). The engine has diagnostics(), the shell has assetStats(); this is the
       * same idea for a headless harness, and it is the only way to assert the arc
       * without scraping the DOM.
       */
      diagnostics() {
        return {
          phase, tier, density, zen, budgetSec, reduced, coarse,
          finds, misclicks, misclickStreak, cleanStreak, bestCleanStreak,
          jackpots, relocations, findTimes: findTimes.slice(),
          modifierOn, bellRung, clutchOn, tasteShown,
          heat: heatNow(), secLeft: zen ? null : secLeft(),
          peeks: peeksNow(),
          targetIndex: board && board.targetTile() ? board.targetTile().i : -1,
          warmTiles: board ? board.tiles.filter((x) => x.warm).length : 0,
          warmIndexes: board ? board.tiles.filter((x) => x.warm).map((x) => x.i) : [],
          poolReady: !!pool,
          timers: timers.size,
        };
      },
    };

    /** pause / resume / suspend all fold into one freeze so nothing double-counts. */
    function halt(on) {
      if (on === halted) return;
      halted = !!on;
      if (halted) {
        pausedAt = Date.now();
        if (board) board.freeze(true);
        if (hud) hud.hidePeek();
      } else {
        if (pausedAt) { pausedMs += Math.max(0, Date.now() - pausedAt); pausedAt = 0; }
        if (board && phase === 'hunt') board.freeze(false);
      }
    }
  },
};
