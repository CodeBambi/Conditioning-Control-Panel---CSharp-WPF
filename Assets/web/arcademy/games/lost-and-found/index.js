/* ============================================================================
 * games/lost-and-found/index.js - LOST & FOUND (family: search, flagship).
 *
 * Where's Waldo, except the crowd is alive: a dense mosaic of looping tiles
 * drifts, glitches and swaps under you while you hunt one clip a whole class
 * long. The Distraction Engine is the difficulty slider - effect dials rise
 * FIRST, classic difficulty (density, near-twin share) second (§6 ordering).
 *
 * ---------------------------------------------------------------------------
 * WHERE THE RULES LIVE
 *   constants.js  every tunable, incl. the PLAYTEST block the dossier flagged
 *   board.js      the mosaic (look signatures, the swap primitive, two-layer drift)
 *   hud.js        chrome + the briefing / peek / spotlight cards
 *   grade.js      the game-specific inputs to the SHARED rubric (no letters here)
 *   trickster.js  House Rules Deck III (melt / ghost cursor / glitch-to-asset)
 *   casino.js     House Rules Deck II (marquee chase / almost / ken-burns)
 *   styles.js     this game's CSS, namespaced .g-lf-*
 *
 * ---------------------------------------------------------------------------
 * THE ARC, AND IT IS PER-TIER (synthesis ruling - the class has a beginning /
 * middle / end; the class-length wave of 2026-08-24 made the LENGTH a tier dial)
 *   the class is findsForTier(tier) finds:  t1 26  t2 22  t3 16  t4 13,
 *              all sized to ~250s of typical competent play against a 300s bell
 *              (constants.js carries the arithmetic)
 *   first third  the tier's baseline board
 *   ~1/3 in     THE MODIFIER: the board wakes up (hotter heat, wider swap
 *              bursts), announced, for the rest of the class
 *              (modifierFindForTier: 9 / 7 / 5 / 4)
 *   last find  THE FINAL BELL: announced, and the clutch ease ("the board
 *              relents") is GUARANTEED rather than conditional on the clock
 *
 * ONE WALL, MANY FINDS. The target RELOCATES between finds on the same tile
 * wall, so a longer class costs no extra tiles and no extra look signatures -
 * density, DENSITY_HARD_CAP and the signature pool are untouched by the wave.
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
  findsForTier, modifierFindForTier, finalBellFindForTier, PLAYTEST, TIERS,
  MOBILE_DENSITY, HEAT_BAND, MODIFIER_HEAT_STEP, BEAT_MS, TICK_MS,
  ASSEMBLE_STAGGER_MS, CLAIM_TIMEOUT_MS, POOL_OVERPROVISION, DISCRETE_STEP_MS,
  DENSITY_LEVELS, DENSITY_HARD_CAP, DENSITY_COARSE_CAP, TOUCH_MIN_TILE_H,
} from './constants.js';
import { makeRng } from '../../core/rng.js';
import { createBoard, paintLook, isVideoUrl, isAnimatedUrl } from './board.js';
import { createHud } from './hud.js';
import { createTrickster } from './trickster.js';
import { createCasino } from './casino.js';
import { injectStyles } from './styles.js';
import { scoreClass, sGateStreakFor } from './grade.js';
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

/* ----------------------------------------------------------------------------
 * THE TIER AUDIO CEILING (House Book, canonical - the same four numbers every
 * room of this school uses, indexed by gradeTier-1). No cue this class fires
 * may exceed it, which is why cue() below is the ONE road: index.js keeps the
 * engine handle and every other module gets the CLOSURE. A deck holding the
 * engine could slip the ceiling; a deck holding cue() cannot.
 * -------------------------------------------------------------------------- */
const AUDIO_CEIL = Object.freeze([0.45, 0.6, 0.75, 0.9]);

/** A refused / wrong press is answered once per this window - a mashed wall
 *  must not machine-gun (House Book: the chrome bump is THROTTLED). */
const BUMP_THROTTLE_MS = 250;
/** The hover tell's floor: one tick per window, however fast the pointer
 *  crosses a 200-tile mosaic. */
const HOVER_THROTTLE_MS = 150;

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
  // class - is not in this build. `meaty` is what lets the shell hand us the
  // full 300s (BUILD-CONTRACT §7), and as of the class-length wave (2026-08-24)
  // we ASK for all of it: the class is a per-tier find count sized to ~250s of
  // typical competent play, so the bell is a real ceiling instead of decoration.
  // We still grade against whatever budget actually arrives (grade.js parSecFor)
  // and start() re-clamps it to 30..300.
  //
  // KEEP IN SYNC: games/registry.js GAME_META mirrors this number (it is the
  // parachute the timetable reads for a suspended class) - CLAUDE.md §2.
  meaty: true,
  flagship: true,
  timeBudgetSec: 300,
  orientation: 'landscape',   // phone only; see games/registry.js ORIENTATIONS
  title: 'Lost & Found',

  manifest: {
    // Everything we fire or sustain, including what the ceremonies reach for.
    // flash_burst is deliberately ABSENT (input-trust law, DECISIONS #9).
    effectsConsumed: ['glitch_swap', 'row_drift', 'sub_flash', 'bubble_field', 'wash',
      'gif_burst', 'audio_trigger', 'crt', 'ambient_field'],
    // DOM-layer consumer: canvasSafe FALSE, so the CORS-tainted remote pool is
    // legal here. Remote arrives LATE and upgrades tiles through the ordinary
    // swap churn; a draw is never blocked on it (FlashService posture).
    assetNeeds: { loops: 130 + POOL_OVERPROVISION, targets: 1, stills: 6, canvasSafe: false },
    // values[0] is the SHELL'S DEFAULT, so it must be the "no cap" end of the
    // ladder: par is met at every tier unless the player deliberately caps down
    // (which is what "playing below tier par caps at A" is for).
    // values[0] must stay the "no cap" end now that lf_density can deal up to
    // DENSITY_HARD_CAP tiles; par tracks the EASY ladder (the least a tier may
    // deal and still earn full grades).
    boardSizes: { values: [200, 130, 84, 56, 42, 28, 22, 16, 12], par: { 1: 22, 2: 28, 3: 42, 4: 56 } },
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
      {
        key: 'lf_density', kind: 'enum', default: 'medium',
        values: ['easy', 'medium', 'hard'],
        label_key: 'lf_density', hint_key: 'lf_density_hint',
      },
    ],
    peek: true,
  },

  create(ctx) {
    const t = ctx.lexicon;
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };
    const rng = typeof ctx.rng === 'function' ? ctx.rng : Math.random;
    const timers = createTimers();

    /* EMI COMMENTARY SEAMS (the heartbeat wave). note() names a moment the
     * mascot may react to - the shell prefixes 'game:' and its own voice engine
     * decides whether the moment is worth a face, a line or nothing at all.
     * It is additive, one-way and fully guarded: an older shell has no note()
     * at all, and a mascot may never break a class.
     * NO hold() HERE: this hunt is click-precision but LONG, and the ruling is
     * that a long phase is not fenced - the voice engine rations itself. */
    const note = (id, extra) => {
      try { if (ctx.mood && typeof ctx.mood.note === 'function') ctx.mood.note(id, extra); }
      catch (e) { /* a mascot may never break a class */ }
    };

    /* ------------------------------------------------------------- state */
    let phase = 'idle';           // idle | briefing | hunt | ceremony | done
    let tier = 1;
    let dials = TIERS[1];
    let density = 16;
    let zen = false;
    let budgetSec = 300;
    /* THE LENGTH OF THIS CLASS, resolved from the tier in start(). Every arc
       beat below is a function of these three, never of a literal find number.
       They are seeded with tier 1's answers so anything that reads them before
       start() (a harness, a stray tick) sees a coherent class, never a 5. */
    let findsTarget = findsForTier(1);
    let modifierFind = modifierFindForTier(1);
    let bellFind = finalBellFindForTier(1);
    let reduced = false;
    let coarse = false;
    let touch = false;            // coarse probe OR ctx.platform.isTouch
    let classSeed = 'lf';         // spec.seed, for the per-round rotation streams

    let board = null;
    let hud = null;
    let trickster = null;         // House Rules Deck III (melt / ghost / chrome)
    let casino = null;            // House Rules Deck II (marquee / almost / ken-burns)
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
    /* EMI SEAM STATE. Read by the commentary seams and by nothing else - no
       game rule, grade input or timing hangs off any of these four. */
    let emiFindSum = 0;            // running sum of finds so far (fast / slow)
    let emiWarmNotedFind = -1;     // one warm note per hunt round, not per click
    let emiRelocAtHuntStart = 0;   // relocations banked when this hunt began
    let emiPeekNoted = false;      // the first peek is remarked on once ever

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
      + HEAT_BAND * (finds / findsTarget)
      + (modifierOn ? MODIFIER_HEAT_STEP : 0)
      - (clutchOn ? PLAYTEST.CLUTCH_HEAT_EASE : 0)
    );

    function setHeat() {
      const h = heatNow();
      try { ctx.engine.setHeat(h); } catch (e) { /* engine is optional */ }
      // the marquee rides the SAME scalar - the frame may never outshout the engine
      if (casino) casino.setHeat(h);
    }

    const elapsedSec = () => (clockStartedAt
      ? Math.max(0, (Date.now() - clockStartedAt - pausedMs) / 1000)
      : 0);
    const secLeft = () => Math.max(0, budgetSec - elapsedSec() - penaltySec);

    function targetLook() {
      const tile = board && board.targetTile();
      return tile ? { grad: tile.grad, hue: tile.hue, url: tile.url } : {};
    }

    /** The board size we actually deal: tier density scaled by the player's
     *  lf_density level, then capped by device and player boardSize. */
    function effectiveDensity(viewH) {
      const lvl = String((ctx.settings && ctx.settings.lf_density) || 'medium');
      const mult = DENSITY_LEVELS[lvl] || DENSITY_LEVELS.medium;
      let d = Math.round(dials.density * mult);
      if (coarse) d = Math.min(DENSITY_COARSE_CAP, Math.min(d, Math.round((MOBILE_DENSITY[tier] || d) * mult)));
      // THE TOUCH TILE FLOOR (constants.js TOUCH_MIN_TILE_H): cap the deal so
      // a row is never shorter than a fingertip. rowsFor() is
      // round(sqrt(d/1.75)) clamped 3..12, so the largest deal that still fits
      // maxRows rows is 1.75*(maxRows+.5)^2 - 1. Shrink-only, touch-only, and
      // par follows density (grade.js), so the grade stays exactly as fair.
      if (touch && Number.isFinite(viewH) && viewH > 120) {
        const gap = 5;                                   // styles.js --g-lf-gap
        const maxRows = Math.floor((viewH + gap) / (TOUCH_MIN_TILE_H + gap));
        if (maxRows < 12) {
          const dMax = Math.ceil(1.75 * (maxRows + 0.5) * (maxRows + 0.5)) - 1;
          d = Math.min(d, Math.max(8, dMax));
        }
      }
      const cap = Number(ctx.settings && ctx.settings.boardSize);
      if (Number.isFinite(cap) && cap > 0) d = Math.min(d, cap);
      return clamp(d, 8, DENSITY_HARD_CAP);
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

    /* ------------------------------------------------------------- THE CUE */
    /**
     * THE ONE ROAD every sound in this class takes. The level is clamped to the
     * grade tier's audio ceiling before it ever reaches the engine, so no
     * caller - game, hud or deck - can shout past the tier. `extra` carries
     * pitch / duck / bus; bus defaults to fx and a caller may override it.
     */
    function cue(name, level, extra) {
      const ceil = AUDIO_CEIL[clamp(tier, 1, 4) - 1] || AUDIO_CEIL[0];
      const lv = Math.min(ceil, level == null ? 0.4 : level);
      try {
        ctx.engine.fire('audio_trigger', Object.assign({ name, level: lv, bus: 'fx' }, extra || {}));
      } catch (e) { /* the engine is optional - a cue never throws */ }
    }

    /**
     * REFUSED / WRONG INPUT: a muted thud, never a bright tick and never
     * silence - and never more than one per BUMP_THROTTLE_MS, so mashing the
     * wall costs one bump instead of thirty.
     */
    let lastBumpAt = 0;
    function bump(level) {
      const now = Date.now();
      if (now - lastBumpAt < BUMP_THROTTLE_MS) return;
      lastBumpAt = now;
      cue('bump', level == null ? 0.15 : level);   /* owner 2026-08-24: error cues -50% */
    }

    /**
     * THE HOVER TELL - the ONE hover sound in the school (owner-approved), and
     * it is here because on a 200-tile wall "the pointer is over a seat" is
     * gameplay information, not decoration. Three things keep it from being
     * spectacle: it sits at the very bottom of the level band (0.12), it is
     * BOUNDED to one tick per HOVER_THROTTLE_MS however fast the pointer
     * crosses the mosaic, and it is silent unless the class is actually live -
     * never during the briefing, the found ceremony, a pause, a suspend, or
     * after the bell.
     */
    let lastHoverAt = 0;
    function onTileHover() {
      if (ended || halted || phase !== 'hunt') return;
      const now = Date.now();
      if (now - lastHoverAt < HOVER_THROTTLE_MS) return;
      lastHoverAt = now;
      cue('tell', 0.05); /* owner 2026-08-24: hover cues -60%, this is the busiest one */
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
        if (pool) {
          dressBoard();
          // Remote media streams in AFTER the claim resolves (the provider's
          // ask-again loop): upgrade whatever is still wearing the glyph floor
          // as each batch lands. The subscription dies with pool.release().
          if (typeof pool.onUpdate === 'function') {
            try { pool.onUpdate(() => { if (pool && board) dressBoard({ onlyBare: true }); }); } catch (e) { /* optional seam */ }
          }
        }
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

    /* ------------------------------------------------------------------------
     * THE TWO DRAWS (0821 perf pass - see board.js's header for the physics).
     *
     * A LIVE seat animates and costs a decoder + a clock; a SLEEPING seat wears
     * a still and costs a texture. Which seat a tile is stays a property of its
     * LOOK, so it only ever changes through the swap primitive - the roaming
     * churn - and never behind the player's back.
     * ---------------------------------------------------------------------- */

    /** Does the library have real stills for the sleeping seats to rest on?
     *  A gifs-only assets folder has none, and the bundled placeholder floor is
     *  six SVGs - which would be a worse wall than the one we are fixing. In
     *  that case sleepers ride the LIVE set instead: same url, same resource,
     *  same clock, no new decoder. (Not under reduced motion: there the honest
     *  answer is a still wall, even if it is a placeholder one.) */
    function haveStills() {
      try {
        const s = ctx.assets && typeof ctx.assets.stats === 'function' ? ctx.assets.stats() : null;
        if (!s) return true;
        if (s.placeholderFloor) return true;      // both kinds are the same six tiles
        const local = (s.local && s.local.still) | 0;
        const remote = (s.remote && s.remote.still) | 0;
        return (local + remote) > 0;
      } catch (e) { return true; }
    }

    /**
     * A LIVE seat's url: animated, not the target's, and NOT a url the wall is
     * already animating. That last clause is the whole sync fix - two tiles on
     * one url share Chromium's one clock for that resource and there is no way
     * to prise them apart that does not mint a second decoder.
     *
     * Videos win a tie: a muted looping mp4 decodes on the GPU and owns its own
     * clock, so it can never join anybody's lockstep.
     */
    function drawLive(targetUrl) {
      if (!pool || typeof pool.next !== 'function' || !board) return null;
      const have = new Set(board.liveUrls().map((e) => e.url));
      // Preferring video is only useful while there is a player slot left for
      // one: past the element ceiling every video draw would be refused and the
      // seat would fall back to a still, quietly shrinking the live window.
      const st = board.liveStats();
      const wantVideo = PLAYTEST.PREFER_VIDEO_LOOPS && st.videoTiles < st.videoCap;
      let fallback = null;
      for (let i = 0; i < PLAYTEST.LIVE_DRAW_TRIES; i++) {
        const got = pool.next('loop');
        if (!got || !got.url) break;
        if (targetUrl && got.url === targetUrl) continue;
        if (have.has(got.url)) continue;
        // the bundled placeholder floor lands in the loop pool too and animates
        // nothing - usable, but never worth spending a live seat on
        if (!isAnimatedUrl(got.url)) { if (!fallback) fallback = got; continue; }
        if (isVideoUrl(got.url) && !wantVideo) { if (!fallback) fallback = got; continue; }
        if (!wantVideo || isVideoUrl(got.url)) return got;
        if (!fallback || !isAnimatedUrl(fallback.url)) fallback = got;
      }
      return fallback;
    }

    /** A SLEEPING seat's url: a still, or - failing that - a url the wall is
     *  ALREADY animating, which is free (one resource, one clock, any number of
     *  tiles). Parking is strictly better than the six bundled placeholder
     *  tiles, and strictly better than a bare gradient. */
    function parkedUrl(targetUrl) {
      if (reduced || !board) return null;         // reduced motion parks nowhere
      const live = board.liveUrls();
      if (!live.length) return null;
      for (let i = 0; i < 3; i++) {
        const pick = live[Math.floor(rng() * live.length)];
        if (pick && pick.url && (!targetUrl || pick.url !== targetUrl)) return pick;
      }
      return null;
    }
    /* ------------------------------------------------------------------------
     * THE SLEEPER LEDGER (0826). ~16 sleeping seats dress themselves off a
     * still pool that can be six urls deep, and drawSleeper used to exclude
     * exactly one thing - the target - so the same still landed on three seats
     * of one wall. The provider's recency ring now spreads the DRAWS; this
     * spreads what we do with them, and it does it WITHOUT a single extra
     * pool.next(): the seats are dressed in one pass, so the draws a seat makes
     * and discards (the target's own url, an animated row when a still was
     * asked for) are BANKED instead of thrown away, and the next seat that
     * would otherwise repeat spends a banked url instead. Draw-then-assign, on
     * draws that already happened.
     *   used  the urls this dressing pass has already given to sleeper seats
     *   bank  drawn-but-unspent urls, oldest first
     * ---------------------------------------------------------------------- */
    const SLEEPER_BANK_MAX = 8;
    let sleeperUsed = new Set();
    const sleeperBank = [];
    function sleeperReset() {
      sleeperUsed = new Set();
      sleeperBank.length = 0;
    }
    function sleeperBankPush(got) {
      if (!got || !got.url || sleeperUsed.has(got.url)) return;
      if (sleeperBank.some((e) => e.url === got.url)) return;
      sleeperBank.push(got);
      while (sleeperBank.length > SLEEPER_BANK_MAX) sleeperBank.shift();
    }
    /** A banked url no seat is wearing yet, preferring a real still. */
    function sleeperBankTake(targetUrl) {
      let at = -1;
      for (let i = 0; i < sleeperBank.length; i++) {
        const e = sleeperBank[i];
        if (!e || !e.url || sleeperUsed.has(e.url)) continue;
        if (targetUrl && e.url === targetUrl) continue;
        if (!isAnimatedUrl(e.url)) { at = i; break; }
        if (at < 0) at = i;
      }
      return at < 0 ? null : sleeperBank.splice(at, 1)[0];
    }
    function sleeperTake(got, targetUrl) {
      if (!got || !got.url) return got;
      if (!sleeperUsed.has(got.url)) { sleeperUsed.add(got.url); return got; }
      /* this seat would repeat: spend a banked url if one is free, and bank the
       * repeat in its place (it may still dress a later seat) */
      const alt = sleeperBankTake(targetUrl);
      if (!alt) return got;                       // nothing banked: the repeat stands
      sleeperBankPush(got);
      sleeperUsed.add(alt.url);
      return alt;
    }

    function drawSleeper(targetUrl) {
      if (!pool || typeof pool.next !== 'function') return null;
      // a gifs-only library HAS no stills; six bundled SVGs across 170 seats is
      // a worse wall than the one this pass is fixing, so park instead
      if (!haveStills()) { const p = parkedUrl(targetUrl); if (p) return p; }
      let animated = null;
      for (let i = 0; i < 3; i++) {
        const got = pool.next('still');
        if (!got || !got.url) break;
        if (targetUrl && got.url === targetUrl) continue;
        // the still we actually asked for
        if (!isAnimatedUrl(got.url)) { if (animated) sleeperBankPush(animated); return sleeperTake(got, targetUrl); }
        if (!animated) animated = got;            // a pool that ignores `kind`
        else sleeperBankPush(got);
      }
      const parked = parkedUrl(targetUrl);
      if (parked) { if (animated) sleeperBankPush(animated); return parked; }
      return animated ? sleeperTake(animated, targetUrl) : null;
    }

    /** Redraw for a seat that already exists, keeping it on the same side of
     *  the live window (an upgrade must never quietly change the mix). */
    function drawFor(tile, targetUrl) {
      return (tile && tile.live) ? drawLive(targetUrl) : drawSleeper(targetUrl);
    }

    /* ------------------------------------------------------------------------
     * THE FRAME GOVERNOR (0821 smoothness pass - constants.js GOV_* for the
     * discovery). A watchdog on the achieved rAF cadence: learns the display's
     * true frame interval, and when the page sits at half-rate (the viz video
     * cadence lock, or a machine weaker than the tuning box) it sheds live
     * seats - videos first, then gifs - until the rate recovers. Sheds go
     * through setUrl like everything else, never touch the target or a twin,
     * and use Math.random on purpose: a frame-timing-driven pick can never be
     * deterministic, so it must not consume the class's seeded stream.
     * ---------------------------------------------------------------------- */
    const gov = {
      on: false, raf: 0, base: Infinity, med: 0,
      badSince: 0, lastShed: 0, healthySince: 0,
      shedVideos: 0, shedGifs: 0, regrown: 0,
    };
    const govGaps = [];
    const govScratch = new Array(96);   // reused by govTick's median (no per-frame allocs)
    let govLast = 0;

    /** Rest a seat on something that costs no decoder and no player: a real
     *  still, else a live GIF url to park on (shared clock, free), else the
     *  bare gradient. Returns true if the seat changed. */
    function restSeat(tile) {
      if (!tile || !board) return false;
      let got = null;
      try { got = pool && typeof pool.next === 'function' ? pool.next('still') : null; } catch (e) { got = null; }
      if (got && got.url && !isAnimatedUrl(got.url) && board.setUrl(tile, got)) return true;
      const target = board.targetTile();
      const live = board.liveUrls().filter((e) => !isVideoUrl(e.url)
        && (!target || e.url !== target.url));
      const pick = live.length ? live[Math.floor(Math.random() * live.length)] : null;
      if (pick && board.setUrl(tile, pick)) return true;
      return board.setUrl(tile, { url: null });
    }

    function shedVideoSeat() {
      if (!board) return false;
      const vids = board.tiles.filter((t) => t.isVideo && !t.target && !t.warm);
      if (vids.length <= (PLAYTEST.GOV_VIDEO_FLOOR | 0)) return false;
      // Only worth it when gifs are carrying the wall: a video-dominant pool
      // at half-rate has no gif judder for the lock to expose.
      const gifLive = board.tiles.reduce((n, t) => n + ((t.live && !t.isVideo) ? 1 : 0), 0);
      if (gifLive < (PLAYTEST.GOV_SHED_VIDEO_MIN_GIFS | 0)) return false;
      return restSeat(vids[Math.floor(Math.random() * vids.length)]);
    }

    function shedGifSeat() {
      if (!board) return false;
      const urlTiles = new Map();
      const gifUrls = new Set();
      for (const t of board.tiles) {
        if (!t.live || t.isVideo || !t.url) continue;
        gifUrls.add(t.url);
        urlTiles.set(t.url, (urlTiles.get(t.url) | 0) + 1);
      }
      if (gifUrls.size <= (PLAYTEST.GOV_GIF_FLOOR | 0)) return false;
      const lives = board.tiles.filter((t) => t.live && !t.isVideo && !t.target && !t.warm && t.url);
      if (!lives.length) return false;
      // a decoder only dies with its LAST tile, so shed sole holders first
      const solo = lives.filter((t) => (urlTiles.get(t.url) | 0) === 1);
      const from = solo.length ? solo : lives;
      return restSeat(from[Math.floor(Math.random() * from.length)]);
    }

    function growGifSeat() {
      if (!board || !pool) return false;
      const sleepers = board.tiles.filter((t) => !t.live && !t.target && !t.warm);
      if (!sleepers.length) return false;
      const target = board.targetTile();
      const got = drawLive(target ? target.url : null);
      if (!got || !got.url || isVideoUrl(got.url) || !isAnimatedUrl(got.url)) return false;
      return board.setUrl(sleepers[Math.floor(Math.random() * sleepers.length)], got);
    }

    function govTick(ts) {
      if (!gov.on) return;
      gov.raf = requestAnimationFrame(govTick);
      if (govLast) {
        const gap = ts - govLast;
        // a hidden tab / suspended class produces junk gaps; restart the window
        if (gap > 0 && gap < 500 && !halted
          && !(typeof document !== 'undefined' && document.hidden)) {
          govGaps.push(gap);
          if (govGaps.length > 90) govGaps.shift();
        } else {
          govGaps.length = 0;
        }
      }
      govLast = ts;
      if (govGaps.length < 48) return;
      // Alloc-free median (0830 audit): slice().sort() here ran EVERY FRAME for
      // the life of the class, feeding the GC on exactly the devices the
      // governor exists to protect. Insertion into a reused scratch buffer
      // yields the identical median with zero garbage (window is <= 90 wide).
      const gn = govGaps.length;
      for (let gi = 0; gi < gn; gi++) {
        const gx = govGaps[gi];
        let gj = gi - 1;
        while (gj >= 0 && govScratch[gj] > gx) { govScratch[gj + 1] = govScratch[gj]; gj -= 1; }
        govScratch[gj + 1] = gx;
      }
      const med = govScratch[gn >> 1];
      gov.med = med;
      if (med < gov.base) gov.base = med;
      const locked = med >= gov.base * PLAYTEST.GOV_LOCK_X;
      if (!locked) {
        gov.badSince = 0;
        if (!gov.healthySince) gov.healthySince = ts;
        if (gov.shedGifs > 0 && phase === 'hunt' && !halted
          && ts - gov.healthySince > PLAYTEST.GOV_GROW_MS) {
          gov.healthySince = ts;
          if (growGifSeat()) { gov.shedGifs -= 1; gov.regrown += 1; }
        }
        return;
      }
      gov.healthySince = 0;
      if (!gov.badSince) { gov.badSince = ts; return; }
      if (ts - gov.badSince < PLAYTEST.GOV_BAD_MS) return;
      if (ts - gov.lastShed < PLAYTEST.GOV_SETTLE_MS) return;
      if (phase !== 'hunt' || halted || !board) return;
      gov.lastShed = ts;
      if (shedVideoSeat()) {
        gov.shedVideos += 1;
        say('governor: shed a video seat (median ' + med.toFixed(1)
          + 'ms vs base ' + gov.base.toFixed(1) + 'ms)');
        return;
      }
      let g = 0;
      for (let k = 0; k < (PLAYTEST.GOV_GIF_SHED_STEP | 0); k++) { if (shedGifSeat()) g += 1; }
      if (g) {
        gov.shedGifs += g;
        say('governor: shed ' + g + ' gif seat(s) (median ' + med.toFixed(1)
          + 'ms vs base ' + gov.base.toFixed(1) + 'ms)');
      }
    }

    function startGovernor() {
      if (!PLAYTEST.GOVERNOR || gov.on) return;
      if (typeof requestAnimationFrame !== 'function') return;   // headless suite
      gov.on = true;
      govLast = 0;
      gov.raf = requestAnimationFrame(govTick);
    }
    function stopGovernor() {
      gov.on = false;
      if (gov.raf && typeof cancelAnimationFrame === 'function') {
        try { cancelAnimationFrame(gov.raf); } catch (e) { /* ignore */ }
      }
      gov.raf = 0;
    }

    /** Dress one seat: try the side it was planned for, fall back to the other
     *  rather than leaving a bare gradient (a refusal is a budget answer, not
     *  an error). Returns true if any look took. */
    function dressTile(tile, targetUrl, wantLive, delayMs) {
      const o = { paintDelayMs: delayMs };
      if (wantLive) {
        const got = drawLive(targetUrl);
        if (got && board.setUrl(tile, got, o)) return true;
      }
      const rest = drawSleeper(targetUrl);
      if (rest && board.setUrl(tile, rest, o)) return true;
      if (!wantLive) {
        const got = drawLive(targetUrl);          // still-less library, cap free
        if (got && board.setUrl(tile, got, o)) return true;
      }
      return false;
    }

    /** Which seats animate: a seeded stride across the board so the live window
     *  is spread over every row instead of clustering in the first ones. */
    function planLiveSeats(cap) {
      const seats = new Set();
      const n = board ? board.tiles.length : 0;
      const k = Math.max(0, Math.min(cap | 0, n));
      if (!k || !n) return seats;
      const stride = n / k;
      for (let i = 0; i < k; i++) {
        let idx = Math.floor(i * stride + rng() * stride);
        if (idx >= n) idx = n - 1;
        const tile = board.tiles[idx];
        if (!tile || tile.target) continue;      // the target's look is its own draw
        seats.add(idx);
      }
      return seats;
    }

    /** A tile still wearing the provider's bundled glyph floor (or nothing). */
    const PLACEHOLDER_RE = /\/ae-ph-\d+\.svg(\?|#|$)/i;
    function wearsPlaceholder(tile) {
      return !tile || !tile.url || PLACEHOLDER_RE.test(String(tile.url));
    }

    /**
     * Put media on the board: the target first, then decoys that avoid it.
     * onlyBare = a late remote batch landed: upgrade ONLY placeholder-wearing
     * tiles, so already-dressed media never churns outside the swap schedule.
     */
    function dressBoard(o) {
      if (!board || !pool) return;
      const onlyBare = !!(o && o.onlyBare);
      /* a full dressing is a fresh wall: the sleeper ledger starts empty. A
       * LATE batch keeps it, because the seats it is upgrading around are
       * exactly the ones the ledger already knows about. */
      if (!onlyBare) sleeperReset();
      const target = board.targetTile();
      // A pool that lands LATE (slow disk, remote batch mid-class) may still
      // upgrade the decoys, but the target's look is frozen the moment the player
      // has memorised it - changing it under them would be a lie, not an upgrade.
      const late = phase !== 'idle' && phase !== 'briefing';
      let targetUrl = target ? target.url : null;
      try {
        if (!late && (!onlyBare || wearsPlaceholder(target))) {
          const got = pool.next('target');
          if (got && got.url) { targetUrl = got.url; board.setUrl(target, got); }
        }
      } catch (e) { say('target draw failed - gradient target stands'); }
      /* THE LIVE WINDOW. Only `cap` seats animate; the rest wear stills. On a
         late batch we keep the mix that is already on the wall and only let a
         bare tile join the live side if the budget still has room. */
      const budget = board.liveStats();
      const seats = onlyBare ? null : planLiveSeats(budget.cap);
      const queue = [];
      for (const tile of board.tiles) {
        if (tile.target) continue;
        if (onlyBare && !wearsPlaceholder(tile)) continue;
        queue.push({ tile, live: seats ? seats.has(tile.i) : !!tile.live });
      }
      // Live seats first: on a still-less library the sleepers park ON the live
      // set, so it has to exist before they draw. Array.sort is stable, so the
      // row order inside each group survives.
      queue.sort((a, b) => (b.live ? 1 : 0) - (a.live ? 1 : 0));
      const total = Math.max(1, board.tiles.length);
      let bareDressed = 0;
      let qi = 0;
      for (const item of queue) {
        // Progressive dressing: the wall fills in over DRESS_WINDOW_MS in row
        // order (diegetic preloader cover behind the briefing card) and every
        // seat adds its own jitter, so no two clocks start on the same tick.
        // A LATE batch spreads over half the window too - it used to land on
        // jitter alone, which let several fresh decoders spin up in one breath.
        const delay = (onlyBare
          ? Math.round((PLAYTEST.DRESS_WINDOW_MS / 2) * (qi++ / Math.max(1, queue.length)))
          : Math.round(PLAYTEST.DRESS_WINDOW_MS * (item.tile.i / total)))
          + Math.floor(rng() * PLAYTEST.DRESS_JITTER_MS);
        let live = item.live;
        if (onlyBare && !live && board.liveStats().used < budget.cap) live = true;
        if (dressTile(item.tile, targetUrl, live, delay)) bareDressed += 1;
      }
      if (onlyBare) {
        if (bareDressed && hud) hud.refreshCards(targetLook());
        if (bareDressed) say('late media batch dressed ' + bareDressed + ' bare tile(s)');
        return;
      }
      // Near-twin decoys are the classic-difficulty lever, tiers 3-4 only.
      const warm = board.assignWarm({ share: dials.nearTwinShare, rng });
      // remote/local media can land after the briefing card is up
      if (hud) hud.refreshCards(targetLook());
      const after = board.liveStats();
      say('board dressed: ' + density + ' tiles, ' + warm + ' near-twins, '
        + after.used + '/' + after.cap + ' live urls on ' + after.tiles + ' seats ('
        + after.elements + ' animated els, ' + after.videoTiles + ' video), '
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
      /* ROAMING (0821): trade a few (animated seat <-> still seat) pairs on top
         of the ordinary picks, so the live window drifts across the wall like a
         marquee instead of animating the seats it was dealt for two minutes.
         It is the SAME swap primitive under the same glitch cover - a roam is
         indistinguishable from noise churn, which is the game's whole twist -
         and it rides the churn timer, so it stops with pause/suspend and during
         the found ceremony for free. board.roamPairs() excludes the target and
         its twins: their looks move on the relocation schedule and nowhere else. */
      if (o.roam > 0 && typeof board.roamPairs === 'function') {
        const taken = new Set();
        for (const p of chosen) { taken.add(p[0]); taken.add(p[1]); }
        for (const pair of board.roamPairs(o.roam, rng)) {
          if (taken.has(pair[0]) || taken.has(pair[1])) continue;
          taken.add(pair[0]); taken.add(pair[1]);
          chosen.push(pair);
        }
      }
      if (!chosen.length) return 0;

      const els = [];
      for (const [a, b] of chosen) { els.push(...a.els, ...b.els); }
      const apply = () => {
        /* CHUNKED APPLY (0821 smoothness): a burst used to swap every pair in
           one synchronous gulp - up to a dozen repaints (x wrap clones) in a
           single frame. The pairs are unchanged (seeded choice above); only
           the application is spread over ~frame-sized ticks. chosen[0] is the
           target's pair when withTarget, so a relocation still lands first. */
        const chunk = Math.max(1, PLAYTEST.SWAP_APPLY_CHUNK | 0);
        let i = 0;
        const step = () => {
          if (!board) return;
          const end = Math.min(chosen.length, i + chunk);
          for (; i < end; i++) board.swapLooks(chosen[i][0], chosen[i][1]);
          if (i < chosen.length) { timers.after(17, step); return; }
          // THE REMOTE UPGRADE PATH: media that arrives late reaches the board
          // here, one decoy per churn tick, under the same glitch cover as the
          // swap - so a remote batch never repaints the whole mosaic at once.
          if (o.upgrade) {
            // never the target (its look is memorised) and never a near-twin
            // (its look is the whole point of the warm tease)
            let tile = null;
            for (const [a, b] of chosen) {
              if (!a.target && !a.warm) { tile = a; break; }
              if (!b.target && !b.warm) { tile = b; break; }
            }
            const target = board.targetTile();
            // ...on the SAME side of the live window the seat is already on, so
            // a remote batch can never quietly inflate the animated set.
            const got = tile ? drawFor(tile, target ? target.url : null) : null;
            if (got) board.setUrl(tile, got);
          }
        };
        step();
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
      // upgrade:true is how late-arriving media gets onto the board (see apply());
      // roam is how the live window drifts across the wall (see swapBurst)
      swapBurst(n, { upgrade: true, roam: PLAYTEST.ROAM_PAIRS_PER_CHURN });
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
        /* W3 P1-14: THE TASTE OF THE TWIST. The one relocate the class shows you
         * gets a long, low glitch - two strikes so the smear lasts as long as
         * the 1.4s crossfade does. Every OTHER relocate stays exactly as silent
         * as the churn it hides inside, and that is the twist, not an omission:
         * a lie that announces itself is not a lie. */
        cue('glitch', 0.3, { pitch: 0.8 });
        timers.after(90, () => cue('glitch', 0.22, { pitch: 0.75 }));
        announce(t('lf_relocate', 'It moved - the same glitch hides the churn'), 2400);
      }
      return moved;
    }

    /**
     * PER-ROUND TARGET ROTATION (owner 2026-08-25): a NEW target look every
     * round. The class PROMOTES a different tile that is already on the wall -
     * a fresh grad+hue+url for free, zero provider draws, zero new decoders
     * (the look is already dealt and budgeted).
     *
     * DETERMINISM: the draw rides a FRESH per-round stream
     * (seed|lf-target|round - the decks' append-only per-tag makeRng idiom).
     * It NEVER consumes ctx.rng: finds are player-paced, so a shared-stream
     * draw here would shift every downstream seeded choice on a retake.
     * assignWarm re-keys the near-twins off the SAME round stream, so the
     * whole rotation is one self-contained draw.
     *
     * The OLD target's look stays on the wall as a red herring - intended;
     * the re-brief card (ceremony tail below) is the fairness guarantee.
     * TIER4_MIDHUNT_RELOCATE stays seat-only: it calls relocate(), never this.
     */
    function rotateTarget(round) {
      if (!board) return false;
      const r = makeRng(classSeed + '|lf-target|' + round);
      const old = board.targetTile();
      // candidates: a real look to memorise - never the outgoing target, never
      // a warm twin (its look is a tease of the OLD target), never the bundled
      // glyph floor; fall back to any other tile on a bare board
      let cands = board.tiles.filter((tile) => tile !== old && !tile.warm
        && tile.url && !wearsPlaceholder(tile));
      if (!cands.length) cands = board.tiles.filter((tile) => tile !== old);
      if (!cands.length) return false;
      const pick = cands[Math.floor(r() * cands.length)];
      board.setTarget(pick);
      pick.warm = false;             // setTarget marks, it never un-warms - and
                                     // a target that is warm would tease, not find
      // Near-twins re-key to the NEW look, seeded off the round stream. On
      // touch the strong-twin repaint is capped and staggered so the ceremony
      // tail never lands on a decode stampede (paintDelayMs = board.js seam).
      board.assignWarm({
        share: dials.nearTwinShare,
        rng: r,
        urlCap: touch ? Math.min(2, PLAYTEST.NEAR_TWIN_URL_CAP) : undefined,
        paintDelayMs: touch ? 240 : 0,
      });
      // THE FUNNEL: refreshCards repaints the chip AND any live peek/spot card
      // and the howto polaroid - never setTargetArt alone.
      if (hud) hud.refreshCards(targetLook());
      return true;
    }

    /* ------------------------------------------------------------- how-to */
    /** Tiers this player has already had the rules sheet for (persisted). */
    function howtoSeenTiers() {
      try {
        const m = (ctx.store && typeof ctx.store.gameMeta === 'function')
          ? (ctx.store.gameMeta('lost_and_found') || {}) : {};
        return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
      } catch (e) { return []; }
    }

    /**
     * The class-rules sheet, ahead of the briefing. THE LAW, uniform across
     * every open class (owner ruling 2026-08-24): it SHOWS the first time this
     * player meets the mosaic at this grade tier and AUTO-SKIPS every later
     * class at that tier, whatever the setting says; the shell's "Skip class
     * tutorials" switch (ctx.hideTutorial) means "skip even the first showing".
     * No meta = no memory = the sheet shows. Dismissal is the sheet's own
     * button only - peek is already bound, so any key shortcut here would spend
     * the player's A-cap on a tutorial. The GATE lives at the call site in
     * start(), because the skip path there is beginClass() itself.
     */
    function howto(onDone) {
      phase = 'briefing';
      if (!hud) { onDone(); return; }
      hud.dim(true);
      let done = false;
      const cardNode = hud.showHowto(targetLook(), () => {
        if (done || ended) return;
        done = true;
        try {
          if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
            const seen = howtoSeenTiers();
            if (seen.indexOf(tier) < 0) seen.push(tier);
            ctx.store.mergeGameMeta('lost_and_found', { howtoTiers: seen });
          }
        } catch (e) { /* best effort - the sheet just shows again next time */ }
        hud.hideHowto();
        hud.dim(false);
        onDone();
      }, findsTarget);        // the sheet SAYS the number, so it is handed it
      if (!cardNode) { done = true; hud.dim(false); onDone(); }
    }

    /* ------------------------------------------------------------ briefing */
    function briefing(onDone) {
      phase = 'briefing';
      if (!board || !hud) { onDone(); return; }
      // The mosaic assembles tile by tile - diegetic loading that doubles as
      // preloader cover for whatever the provider is still fetching.
      const els = board.tiles.map((tile) => board.primaryEl(tile)).filter(Boolean);
      /* W3 P1-14. THE ASSEMBLE. The mosaic builds itself tile by tile and did it
       * in silence, so the opening of the class had no sound at all. A very
       * faint tell per tile turns the load into a ladder - CAPPED AT 24 ticks,
       * because a 200-tile board would be a hailstorm, and the level is .06:
       * this is texture under a picture, not an event. */
      let assembleTicks = 0;
      els.forEach((node, i) => {
        if (node && node.style) node.style.opacity = '0';
        const cueThis = assembleTicks < 24;
        if (cueThis) assembleTicks += 1;
        const n = assembleTicks - 1;
        timers.after(Math.min(1400, i * ASSEMBLE_STAGGER_MS), () => {
          if (node && node.style) node.style.opacity = '';
          if (cueThis) cue('tell', 0.06, { pitch: 1 + 0.02 * n });
        });
      });
      // The count is a NUMBER in the sentence now, so the row carries a {n} slot
      // and a new key (the old lf_briefing row still says "five times" in every
      // shipped lexicon - reusing it would have printed the old number).
      hud.showBriefing(
        targetLook(),
        t('lf_briefing_n', 'Memorize her, then find her {n} times.').replace('{n}', String(findsTarget))
      );
      hud.setProgress(0, findsTarget);
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
      if (casino) casino.start();      // the marquee lights before the first swap
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

    /* W3 P1-14. THE BOARD WAKES UP, and until now that was a banner and a set of
     * numbers nobody could hear change. The woken board carries a presence: a
     * `seep_hum` re-struck on its own timer (the mixer has no sustain - trap
     * 108), just under the threshold of "a sound". It is stopped in
     * stopEffects(), which is every road out of this class. */
    const WAKE_HUM_MS = 640;
    let wakeHumTimer = 0;
    function stopWakeHum() {
      if (wakeHumTimer) { timers.cancel(wakeHumTimer); wakeHumTimer = 0; }
    }
    function startWakeHum() {
      if (wakeHumTimer || ended) return;
      const strike = () => {
        wakeHumTimer = 0;
        if (ended || phase === 'done' || !modifierOn) return;
        /* a paused / suspended class is not breathing: the loop stays alive so
           the room comes back with the player, but it says nothing meanwhile. */
        if (!halted) cue('seep_hum', 0.08, { pitch: 0.95 });
        wakeHumTimer = timers.after(WAKE_HUM_MS, strike);
      };
      strike();
    }

    function stopEffects() {
      stopWakeHum();                 // W3 P1-14: every hold has an owner (trap 108)
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
      emiRelocAtHuntStart = relocations;
      if (hud) {
        hud.setProgress(finds, findsTarget);
        hud.dim(false);
      }
      if (board) { board.freeze(false); board.clearMark('g-lf-found'); }
      setHeat();
      armPity();
      if (finds + 1 === bellFind && !bellRung) {
        bellRung = true;
        if (casino) casino.bell(true);       // the frame goes gold for the last hunt
        announce(t('lf_final_bell', 'Final bell'), 2000);
        note('lf.finalBell', { kind: 'tension', n: findsTarget, left: 1, streak: bestCleanStreak });
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
      /* W3 P1-14: the pity shimmer is the room quietly helping, and it was
       * purely visual - on a wall of moving pictures, easy to miss entirely. A
       * high, tiny tell says "over here". CAPPED AT THREE: a fourth would stop
       * being help and start being a metronome. */
      let pityCues = 0;
      pityTimer = timers.after(PLAYTEST.PITY_STUCK_MS, function pulse() {
        if (halted || phase !== 'hunt') return;
        if (Date.now() - findStartedAt < PLAYTEST.PITY_MIN_ELAPSED_MS) return;
        const target = board.targetTile();
        if (pityCues < 3) { pityCues += 1; cue('tell', 0.1, { pitch: 1.6 }); }
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
      /* W3 P0-17. THE CLUTCH. The churn stops, the drift eases and the board
       * lets go, and all of it was a banner. One low whoosh with a duck under
       * it, so the room audibly makes space. ONCE - the clutchOn latch above
       * has already returned for every repeat call. */
      cue('whoosh', 0.3, { pitch: 0.7, duck: 'voice', duckMs: 500 });
      /* EMI COLOR: the board's own clutch beat is the mascot's too. */
      try { if (ctx.mood) ctx.mood.clutch(); } catch (e) { /* noop */ }
      note('lf.clutch', { kind: 'tension', n: finds, left: Math.max(0, findsTarget - finds) });
      say('clutch ease engaged');
    }

    /* W3 P0-2. THE COUNTDOWN. `tick` runs on TICK_MS, so the cue rides the
     * WHOLE SECONDS figure changing and never the ticker. ZEN NEVER TICKS -
     * a mode whose whole promise is no clock does not get a clock in the ear -
     * and the run is short by design: the pitch ladder is 1 + .06n over five
     * rungs, which is what makes the last one read as the last one. */
    const CLOCK_TICK_FROM_SEC = 5;
    let clockTickSec = -1;
    function tick() {
      if (halted || phase === 'done') return;
      if (!hud) return;
      if (zen) { hud.setClock(null); return; }
      const left = secLeft();
      hud.setClock(left);
      if (left > 0 && left <= CLOCK_TICK_FROM_SEC) {
        const secs = Math.ceil(left);
        if (secs !== clockTickSec) {
          clockTickSec = secs;
          const n = Math.min(4, CLOCK_TICK_FROM_SEC - secs);
          cue('clock_tick', 0.1 + 0.02 * n, { pitch: 1 + 0.06 * n });
        }
      } else if (left > CLOCK_TICK_FROM_SEC) clockTickSec = -1;
      if (left <= 0) { clockTickSec = -1; finish(false); return; }
      if (finds === findsTarget - 1 && left <= PLAYTEST.CLUTCH_SEC_LEFT) clutch();
    }

    /* --------------------------------------------------------------- clicks */
    function onTileClick(tile, e) {
      // REFUSED INPUT: a press that arrives while the class is not taking any
      // (the briefing, the found ceremony, a pause, a suspend) is ANSWERED -
      // one throttled bump. After the bell the room is simply over: silent.
      if (phase !== 'hunt' || halted) { if (!ended) bump(); return; }
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
      /* EMI COLOR: the home stretch (final fifth of the hunt) leans her in. */
      try {
        if (ctx.mood && findsTarget > 1 && finds >= Math.ceil(findsTarget * 0.8)
          && finds < findsTarget) ctx.mood.tense();
      } catch (e) { /* noop */ }
      /* THE CONFIRM: the press that lands her, on the beat of the press. The
         pitch climbs across the WHOLE class - first find at 1.0, last find at
         1.5, however many finds this tier deals. It used to be a flat +0.06 a
         find against a cap of 1.5, which at 13-26 finds pinned the ladder at the
         top from find 9 onward and spent two thirds of the class saying the same
         thing. */
      const arc = findsTarget > 1 ? (finds - 1) / (findsTarget - 1) : 1;
      cue('pop', 0.45, { pitch: Math.min(1.5, 1 + 0.5 * arc) });
      if (cleanThisFind) {
        cleanStreak += 1;
        if (cleanStreak > bestCleanStreak) bestCleanStreak = cleanStreak;
      } else {
        cleanStreak = 0;
      }

      /* EMI SEAMS: the two branches of ONE find resolution, judged against this
       * class's own running mean rather than a par recompute on the beat (a
       * 200-tile wall at tier 1 and a 40-tile wall at tier 4 are not the same
       * fast), plus the tier-4 case where the seat moved mid-hunt and the
       * player tracked it anyway. */
      const emiPrior = findTimes.length - 1;
      const emiMean = emiPrior > 0 ? emiFindSum / emiPrior : 0;
      const emiLeft = Math.max(0, findsTarget - finds);
      const emiMoved = Math.max(0, relocations - emiRelocAtHuntStart);
      emiFindSum += took;
      if (emiPrior >= 2 && took <= emiMean * 0.55) {
        note('lf.fastFind', { kind: 'celebrate', n: Math.round(took), streak: cleanStreak, left: emiLeft });
      } else if (emiPrior >= 2 && took >= emiMean * 1.8) {
        note('lf.slowFind', { kind: 'commiserate', n: Math.round(took), streak: cleanStreak, left: emiLeft });
      }
      if (emiMoved > 0) {
        note('lf.trackedRelocation', { kind: 'celebrate', n: emiMoved, streak: cleanStreak, left: emiLeft });
      }

      if (pityTimer) { timers.cancel(pityTimer); pityTimer = 0; }
      if (churnTimer) { timers.cancel(churnTimer); churnTimer = 0; }

      /* ---- the found ceremony (board dims, target spotlights, sting) ---- */
      if (hud) {
        hud.dim(true);
        hud.setProgress(finds, findsTarget);
        hud.showSpot(targetLook(), t('lf_found', 'Found her'));
      }
      if (board) { board.freeze(true); board.mark(tile, 'g-lf-found', true); }
      // THE SPOTLIGHT: the ceremony's own sting, ducking everything under it.
      // The streak term is WINDOWED at five, or a long class would sit pinned at
      // 1.0 from the fifth clean find on and the sting would stop meaning it.
      cue('sting', clamp01(0.45 + 0.05 * Math.min(cleanStreak, 5)), { duck: 'spotlight' });
      // THE CHIME LADDER (Deck II): each find stacks one more rising layer on
      // the sting, so the class gets audibly richer as it climbs. Capped at 4
      // layers; the LAST find pays its own way (the royal jackpot below). The
      // level rides class PROGRESS, not the raw find count - a flat per-find
      // term clipped to 1.0 two thirds of the way through a 26-find class.
      for (let L = 0; L < Math.min(finds, 4); L++) {
        timers.after(110 * (L + 1), () => {
          cue('streak', clamp01(0.2 + 0.07 * L + 0.15 * (finds / findsTarget)));
        });
      }
      // one pulse of the frame, brighter up the ladder - the ladder is the whole
      // class, so the frame is told how long the class is
      if (casino) casino.payout(finds, findsTarget);
      try { ctx.ceremonies.stamp({ text: t('lf_found', 'Found her'), target: hud && hud.stampAnchor }); } catch (e) { /* optional */ }
      // GOLD IS THE S GATE'S OWN STREAK, not a flat 3. On a 26-find class three
      // clean finds in a row is Tuesday; the gate number (8/7/5/4 by tier) is
      // the streak that is actually buying the player something.
      const goldStreak = sGateStreakFor(findsTarget);
      try { ctx.ceremonies.streakMeter({ target: hud && hud.streakMount, filled: cleanStreak, gold: cleanStreak >= goldStreak }); } catch (e) { /* optional */ }
      // the CROSSING, not the state - a streak that stays gold is gold once
      if (cleanStreak === goldStreak) note('lf.cleanStreakGold', { kind: 'celebrate', streak: cleanStreak, n: finds, left: emiLeft });
      if (!reduced) {
        try { ctx.engine.sustain('ambient_field', { kind: cleanStreak >= goldStreak ? 'goldleaf' : 'confetti' }); } catch (e) { /* optional */ }
        timers.after(900, () => { try { ctx.engine.stop('ambient_field'); } catch (e) { /* ignore */ } });
      }

      /* ---- the variable-reward beat -------------------------------------- */
      /* The LEDGER half of this block is untouched by Deck II: the roll still
         happens on every find and `jackpots` (graded, XP_PER_JACKPOT) still
         moves only when the canon says jackpot. What Deck II changes is the
         SHOW on the LAST find: the final bell always pays a ROYAL jackpot
         visual at intensity 1.0 - engine jackpotSpec's own rarity dial - and a
         same-find canon jackpot folds into it instead of playing twice. */
      const finalFind = finds >= findsTarget;
      let roll = null;
      try { roll = reward.roll({ heat: heatNow(), success: true, streak: cleanStreak }); } catch (e) { roll = null; }
      if (roll && roll.jackpot) jackpots += 1;
      if (finalFind) {
        try { ctx.ceremonies.reward('jackpot', { intensity: 1, target: hud && hud.stampAnchor, text: t('lf_royal', 'ROYAL PAYOUT') }); } catch (e) { /* optional */ }
        // INPUT TRUST: clickSafe over a click-precision board, always.
        try { ctx.engine.fire('gif_burst', { clickSafe: true, count: 5, assetKind: 'loop' }); } catch (e) { /* optional */ }
        note('lf.royalPayout', { kind: 'celebrate', n: finds, streak: bestCleanStreak });
      } else if (roll && roll.jackpot) {
        try { ctx.ceremonies.reward('jackpot', { intensity: roll.intensity, target: hud && hud.stampAnchor, text: t('lf_jackpot', 'Jackpot') }); } catch (e) { /* optional */ }
        try { ctx.engine.fire('gif_burst', { clickSafe: true, count: 4, assetKind: 'loop' }); } catch (e) { /* optional */ }
        note('lf.jackpot', { kind: 'celebrate', n: jackpots, streak: cleanStreak });
      } else if (roll && roll.nearMiss) {
        try { ctx.ceremonies.reward('near_miss', { intensity: roll.intensity, target: hud && hud.stampAnchor }); } catch (e) { /* optional */ }
      }

      /* ---- the arc ------------------------------------------------------- */
      if (finds >= modifierFind && !modifierOn) {
        modifierOn = true;
        announce(t('lf_modifier', 'The board wakes up'), 2000);
        startWakeHum();          // W3 P1-14: the escalation stops being a banner
        note('lf.boardWakesUp', { kind: 'tension', n: finds, left: emiLeft });
        say('modifier engaged after find ' + finds);
      }

      const ceremonyMs = reduced ? PLAYTEST.FOUND_CEREMONY_MS_REDUCED : PLAYTEST.FOUND_CEREMONY_MS;
      timers.after(ceremonyMs, () => {
        if (phase === 'done') return;
        if (hud) hud.hideSpot();
        if (finds >= findsTarget) { finish(true); return; }
        /* PER-ROUND ROTATION (owner 2026-08-25): rotate the LOOK, re-brief,
           THEN relocate the seat under the collapse - the same fake-out as the
           opening briefing, so the player never sees where the new look lands.
           The re-brief runs ON the clock exactly like the ceremony it extends
           (the opening briefing is off the clock only because the clock has
           not started yet; mid-class there is no pause to borrow). phase stays
           'ceremony' through it: clicks bump, the trickster holds, the hover
           tell is silent, and dressBoard's late-target guard stays shut. The
           board stays dimmed and frozen from the ceremony; beginHunt() lifts
           both. */
        rotateTarget(finds);
        if (hud) hud.showBriefing(targetLook(), t('lf_rebrief', 'New target. Memorize her.'));
        /* SAMPLED. Up to 25 re-briefs a class, so the seam offers the FIRST one
         * and then every fourth - the board is dimmed and frozen here, which
         * makes it the one guaranteed-safe speech window, but a reaction to
         * every single new face would narrate the whole class. */
        if (finds === 1 || finds % 4 === 0) note('lf.rebrief', { kind: 'curiosity', n: finds, left: Math.max(0, findsTarget - finds) });
        const holdMs = reduced ? 700 : Math.min(Math.round(dials.previewSec * 1000), 1400);
        timers.after(holdMs, () => {
          if (phase === 'done') return;
          if (hud) hud.collapseBriefing();
          /* W3 P2-6: the MID-CLASS re-brief card shrinks into the board a frame
           * before the relocate it covers. A short high whoosh sells the card
           * going in. The OPENING briefing keeps its own silence - that one is
           * off the clock and the room has not started yet. */
          cue('whoosh', 0.22, { pitch: 1.2 });
          relocate();
          timers.after(reduced ? 60 : 420, () => {
            if (phase === 'done') return;
            if (hud) hud.hideBriefing();
            // the churn resumes unless the board has already relented
            if (!clutchOn) churnTimer = timers.every(dials.swapMs, () => { if (!halted) noiseSwap(); });
            beginHunt();
          });
        });
      });
    }

    /** Shared accounting for any wrong click; only the punishment differs. */
    function countWrong() {
      misclicks += 1;
      misclickStreak += 1;
      /* EMI COLOR: a small >_< on the mascot, shell-rationed to 3 a class. */
      try { if (ctx.mood) ctx.mood.stumble(); } catch (e) { /* noop */ }
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
      // THE WRONG TILE: a muted loss on the beat of the stamp card, throttled
      // so a mashed wall is one thud and not a machine gun. (`bump` and
      // `stamp_bad` are near-identical sawtooth thunks in shell/audio.js, so
      // this is the House Book's own loss recipe at the House Book's own level;
      // it REPLACES the raw stamp_bad fire, so one press stays ONE cue.)
      bump(0.15);

      if (misclickStreak >= PLAYTEST.MISCLICK_STREAK_FOR_WASH) {
        misclickStreak = 0;
        announce(t('lf_misclick_streak', 'Focus'), 1500);
        if (tier >= PLAYTEST.MISCLICK_WASH_FROM_TIER) {
          try { ctx.engine.sustain('wash', { variant: 'pink', holdMs: 600 }); } catch (err) { /* optional */ }
          /* W3 P2-6: the third miss in a row escalates and the escalation was
           * silent. The QUIETEST of this class's three washes on purpose - it
           * is a loss cue, and the House Book halves those. */
          cue('wash', 0.15, { pitch: 0.7 });
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
      // Deck II near-miss staging: the clicked twin flashes what it ALMOST was
      // (the target's look ghosts through it, slot-reel settle). Presentation
      // only - countWrong() has already done the honest accounting above.
      if (casino) casino.almost(tile, targetLook(), paintLook);
      try { ctx.ceremonies.reward('near_miss', { target: hud && hud.stampAnchor, text: t('lf_warm', 'Warm') }); } catch (err) { /* optional */ }
      // THE ALMOST: the near-tease, landing with casino.almost()'s ghost and
      // the shimmer on the real target. `blip` used to sit here - a BRIGHT TICK
      // on a wrong press, the one thing the House Book forbids on a loss.
      cue('near', 0.175);
      if (board) {
        const target = board.targetTile();
        board.mark(target, 'g-lf-warm', true);
        timers.after(PLAYTEST.NEAR_TWIN_SHIMMER_MS + 60, () => board.mark(target, 'g-lf-warm', false));
      }
      /* ONCE PER HUNT ROUND, never per press: a flailing player can hit two
       * twins in a row and she only gets to be fooled once a target. */
      if (emiWarmNotedFind !== finds) {
        emiWarmNotedFind = finds;
        note('lf.warmClick', { kind: 'tease', tile: Number(tile && tile.i) | 0, n: misclicks, left: Math.max(0, findsTarget - finds) });
      }
    }

    /* --------------------------------------------------------------- finish */
    function finish(complete) {
      if (ended) return;
      ended = true;
      phase = 'done';
      timers.killAll();
      stopGovernor();
      stopEffects();
      if (trickster) trickster.stop();
      /* W3 P0-16. THE COMPLETION. Finishing the hunt used to end on exactly the
       * same descending sigh as running out of time - the marquee's dimOut -
       * so the class scored a failure better than a success. The win gets its
       * own bright note FIRST, and the sigh then plays under it as the lights
       * going down rather than as the verdict. */
      if (complete) cue('chime', 0.35, { pitch: 1.3 });
      if (casino) { casino.stop(); casino.dimOut(); }
      // hideBriefing: the bell can land mid-RE-BRIEF (per-round rotation) and
      // killAll() has just eaten the timer that would have dismissed the card
      if (hud) { hud.hideSpot(); hud.hidePeek(); hud.hideBriefing(); hud.dim(false); hud.setClock(zen ? null : secLeft()); }
      if (board) board.freeze(true);

      const peeks = peeksNow();
      const score = scoreClass({
        findTimesSec: findTimes,
        misclicks,
        peeks,
        bestCleanStreak,
        density,
        drift: dials.drift,
        // the length of THIS class - every scaled threshold in grade.js hangs
        // off it, and the par clamp needs the ceremony overhead it implies
        findsTarget,
        ceremonySec: (reduced ? PLAYTEST.FOUND_CEREMONY_MS_REDUCED : PLAYTEST.FOUND_CEREMONY_MS) / 1000,
        timeBudgetSec: zen ? null : budgetSec,
        jackpots,
      });
      if (!complete && !zen) {
        announce(t('lf_timeout', 'Time'), 2000);
        // Deck II: a loss is acknowledged, never silent - a muted stamp and a
        // low thud while the marquee sighs out. Scaled down, still a ceremony.
        try { ctx.ceremonies.stamp({ text: t('lf_timeout', 'Time'), tone: 'pink', target: hud && hud.stampAnchor }); } catch (e) { /* optional */ }
        cue('stamp_bad', 0.15);
      }

      say('class over: ' + finds + '/' + findsTarget + ' finds, median '
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
      /* W3 P1-14. THE PEEK COSTS THE CLASS AN A AND SOUNDED LIKE NOTHING, in
       * either direction. Both cues hang off the shell's own handlers rather
       * than the button, so the KEY path is covered too: a lift on the way in
       * (the card comes up), a low slide on the way out (it is taken away). */
      ctx.peek.setHandlers({
        onReveal: () => {
          if (hud) hud.showPeek(targetLook());
          cue('lift', 0.25, { pitch: 1.15 });
        },
        onHide: () => {
          cue('slide', 0.2, { pitch: 0.85 });
          if (!hud) return;
          hud.hidePeek();
          hud.setChips(misclicks, peeksNow());     // the count moves on EVERY peek
        },
        onFirstUse: () => {
          // The cap itself is the shell's; we only tell the player, once.
          announce(t('peek_hint', 'Hold to peek. Using it caps this class at A.'), 1800);
          if (hud) hud.setChips(misclicks, peeksNow());
          if (!emiPeekNoted) {
            emiPeekNoted = true;
            note('lf.peekFirstUse', { kind: 'tease', n: peeksNow(), left: Math.max(0, findsTarget - finds) });
          }
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
        // THE LENGTH OF THE CLASS is the tier's, and it is resolved exactly once
        // here so no beat below can disagree with another about how long the
        // class is (constants.js carries the pacing arithmetic).
        findsTarget = findsForTier(tier);
        modifierFind = modifierFindForTier(tier);
        bellFind = finalBellFindForTier(tier);
        // The fallback is the module's own declared budget, not a stale 120: a
        // spec with no budget must not deal a 26-find class two minutes.
        budgetSec = clamp(Number(spec.timeBudgetSec) || 300, 30, 300);
        zen = !!(ctx.settings && ctx.settings.lf_zen);
        reduced = probeReduced();
        coarse = probeCoarse();
        touch = coarse || !!(ctx.platform && ctx.platform.isTouch);
        classSeed = String(spec.seed || 'lf');

        injectStyles();
        hud = createHud({
          root: ctx.root, t, keys: ctx.keys, coarse, lite: coarse, zen,
          // the chrome vocabulary rides the game's clamped helper, never the engine
          cue,
        });
        // Deliberately AFTER createHud: on touch the deal is capped by the
        // view's real height (the touch tile floor), and only the mounted view
        // knows it. Nothing before this line reads density, and createHud
        // draws no rng, so the seeded stream is byte-identical either way.
        density = effectiveDensity(hud.view && hud.view.clientHeight
          ? hud.view.clientHeight
          : (typeof window !== 'undefined' && window.innerHeight ? window.innerHeight - 100 : NaN));
        board = createBoard({
          mount: hud.view, density, rng, drift: dials.drift,
          lite: coarse, touch, reduced, log: say, onTileClick, onTileHover,
        });
        // Somebody has to be the target before any media exists, so the class is
        // playable even if the provider never answers.
        board.setTarget(board.tiles[Math.floor(rng() * board.tiles.length)] || board.tiles[0]);
        if (hud) hud.setTargetArt(targetLook());
        wirePeek();
        claimAssets();
        loadSchedule(String(spec.seed || 'lf'), say).then((s) => { reward = s || FALLBACK_SCHEDULE; });

        // Deck III (House Rules): melt / ghost cursor / glitch-to-asset. Seeded
        // deals, presentation-only - it reads game state, it never writes any.
        trickster = createTrickster({
          seed: String(spec.seed || 'lf'),
          tier,
          // zen has no bell, so the deck is given a NOMINAL span to spread its
          // cards across - the length of the same class played timed. It was
          // 180s when the class was 5 finds; a zen class is the tier's full
          // find count now, so cards would have stopped two thirds of the way in
          budgetSec: zen ? 300 : budgetSec,
          timers, board, hud, reduced, coarse,
          // A player who capped effects off gets no trickery either (Law VI).
          capsOk: !(ctx.caps && Number(ctx.caps.bgIntensity) === 0),
          getPhase: () => phase,
          isHalted: () => halted,
          isClutch: () => clutchOn,
          getStill: () => {
            try {
              const got = pool && typeof pool.next === 'function' ? pool.next('still') : null;
              return (got && got.url) || null;
            } catch (e) { return null; }
          },
          announce, t, log: say,
          // Deck III speaks through the game's clamped helper, never the engine
          cue,
        });

        // Deck II (House Rules): the lighting rig. Same disarm rule as the
        // trickster; the marquee itself lights in startEffects().
        casino = createCasino({
          seed: String(spec.seed || 'lf'),
          tier, density,
          board, hud, timers, reduced, lite: coarse,
          capsOk: !(ctx.caps && Number(ctx.caps.bgIntensity) === 0),
          log: say,
          // Deck II speaks through the game's clamped helper, never the engine
          cue,
        });

        say('class start: tier ' + tier + ', density ' + density + '/' + dials.density
          + (coarse ? ' (coarse)' : '') + (reduced ? ' (reduced motion)' : '')
          + ', budget ' + (zen ? 'zen' : budgetSec + 's')
          + ', ' + findsTarget + ' finds (modifier ' + modifierFind + ', bell ' + bellFind + ')');

        // learn the display's true frame interval while the wall is still
        // cheap - the briefing frames are the cleanest baseline we ever get
        startGovernor();

        const beginClass = () => briefing(() => {
          clockStartedAt = Date.now();
          startEffects();
          armLoops();
          if (trickster) trickster.start();
          beginHunt();
          if (hud) hud.setClock(zen ? null : budgetSec);
        });
        // Rules sheet first, and it is FREE OF THE CLOCK: clockStartedAt is
        // taken inside beginClass, on the far side of GO and the briefing.
        // AUTO-SKIP once this tier is on the record; ctx.hideTutorial (the
        // shell's "Skip class tutorials" switch, absent on old harnesses ->
        // falsy) skips the first showing too. Both skip paths are the same
        // instant dismiss the sheet's own GO performs: beginClass().
        if (ctx.hideTutorial || howtoSeenTiers().indexOf(tier) >= 0) beginClass();
        else howto(beginClass);
      },

      pause() { halt(true); },
      resume() { halt(false); },
      /** The host says stop NOW (mandatory video / panic). Same freeze, no UI. */
      suspend(on) { halt(!!on); },

      destroy() {
        ended = true;
        phase = 'done';
        timers.dispose();
        stopGovernor();
        stopEffects();
        try { if (trickster) trickster.destroy(); } catch (e) { /* ignore */ }
        trickster = null;
        try { if (casino) casino.destroy(); } catch (e) { /* ignore */ }
        casino = null;
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
          finds, findsTarget, modifierFind, bellFind,
          misclicks, misclickStreak, cleanStreak, bestCleanStreak,
          jackpots, relocations, findTimes: findTimes.slice(),
          modifierOn, bellRung, clutchOn, tasteShown,
          heat: heatNow(), secLeft: zen ? null : secLeft(),
          peeks: peeksNow(),
          targetIndex: board && board.targetTile() ? board.targetTile().i : -1,
          warmTiles: board ? board.tiles.filter((x) => x.warm).length : 0,
          warmIndexes: board ? board.tiles.filter((x) => x.warm).map((x) => x.i) : [],
          poolReady: !!pool,
          // the live window (0821 perf pass): decoders spent vs budgeted
          live: board && typeof board.liveStats === 'function' ? board.liveStats() : null,
          // the frame governor: what it learned and what it shed
          governor: {
            on: gov.on, baseMs: Number.isFinite(gov.base) ? Math.round(gov.base * 10) / 10 : null,
            medianMs: Math.round(gov.med * 10) / 10,
            shedVideos: gov.shedVideos, shedGifs: gov.shedGifs, regrown: gov.regrown,
          },
          timers: timers.size,
          trickster: trickster ? trickster.diagnostics() : null,
          casino: casino ? casino.diagnostics() : null,
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
        if (casino) casino.freeze(true);       // the chase freezes with the board
        if (hud) hud.hidePeek();
      } else {
        if (pausedAt) { pausedMs += Math.max(0, Date.now() - pausedAt); pausedAt = 0; }
        if (board && phase === 'hunt') board.freeze(false);
        if (casino && phase === 'hunt') casino.freeze(false);
      }
    }
  },
};
