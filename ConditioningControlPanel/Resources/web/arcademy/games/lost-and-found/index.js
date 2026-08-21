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
 *   trickster.js  House Rules Deck III (melt / ghost cursor / glitch-to-asset)
 *   casino.js     House Rules Deck II (marquee chase / almost / ken-burns)
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
  DENSITY_LEVELS, DENSITY_HARD_CAP, DENSITY_COARSE_CAP,
} from './constants.js';
import { createBoard, paintLook, isVideoUrl, isAnimatedUrl } from './board.js';
import { createHud } from './hud.js';
import { createTrickster } from './trickster.js';
import { createCasino } from './casino.js';
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
    function effectiveDensity() {
      const lvl = String((ctx.settings && ctx.settings.lf_density) || 'medium');
      const mult = DENSITY_LEVELS[lvl] || DENSITY_LEVELS.medium;
      let d = Math.round(dials.density * mult);
      if (coarse) d = Math.min(DENSITY_COARSE_CAP, Math.min(d, Math.round((MOBILE_DENSITY[tier] || d) * mult)));
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
        if (!isAnimatedUrl(got.url)) return got;  // the still we actually asked for
        if (!animated) animated = got;            // a pool that ignores `kind`
      }
      return parkedUrl(targetUrl) || animated;
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
      const s = govGaps.slice().sort((x, y) => x - y);
      const med = s[s.length >> 1];
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
        if (casino) casino.bell(true);       // the frame goes gold for the last hunt
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
      // THE CHIME LADDER (Deck II): each find stacks one more rising layer on
      // the sting, so the class gets audibly richer as it climbs. Capped at 4
      // layers; the fifth find pays its own way (the royal jackpot below).
      for (let L = 0; L < Math.min(finds, 4); L++) {
        timers.after(110 * (L + 1), () => {
          try {
            ctx.engine.fire('audio_trigger', {
              name: 'streak', level: clamp01(0.2 + 0.07 * L + 0.03 * finds), bus: 'fx',
            });
          } catch (e) { /* optional */ }
        });
      }
      if (casino) casino.payout(finds);   // one pulse of the frame, brighter up the ladder
      try { ctx.ceremonies.stamp({ text: t('lf_found', 'Found her'), target: hud && hud.stampAnchor }); } catch (e) { /* optional */ }
      try { ctx.ceremonies.streakMeter({ target: hud && hud.streakMount, filled: cleanStreak, gold: cleanStreak >= 3 }); } catch (e) { /* optional */ }
      if (!reduced) {
        try { ctx.engine.sustain('ambient_field', { kind: cleanStreak >= 3 ? 'goldleaf' : 'confetti' }); } catch (e) { /* optional */ }
        timers.after(900, () => { try { ctx.engine.stop('ambient_field'); } catch (e) { /* ignore */ } });
      }

      /* ---- the variable-reward beat -------------------------------------- */
      /* The LEDGER half of this block is untouched by Deck II: the roll still
         happens on every find and `jackpots` (graded, XP_PER_JACKPOT) still
         moves only when the canon says jackpot. What Deck II changes is the
         SHOW on the fifth find: the final bell always pays a ROYAL jackpot
         visual at intensity 1.0 - engine jackpotSpec's own rarity dial - and a
         same-find canon jackpot folds into it instead of playing twice. */
      const finalFind = finds >= FINDS_PER_CLASS;
      let roll = null;
      try { roll = reward.roll({ heat: heatNow(), success: true, streak: cleanStreak }); } catch (e) { roll = null; }
      if (roll && roll.jackpot) jackpots += 1;
      if (finalFind) {
        try { ctx.ceremonies.reward('jackpot', { intensity: 1, target: hud && hud.stampAnchor, text: t('lf_royal', 'ROYAL PAYOUT') }); } catch (e) { /* optional */ }
        // INPUT TRUST: clickSafe over a click-precision board, always.
        try { ctx.engine.fire('gif_burst', { clickSafe: true, count: 5, assetKind: 'loop' }); } catch (e) { /* optional */ }
      } else if (roll && roll.jackpot) {
        try { ctx.ceremonies.reward('jackpot', { intensity: roll.intensity, target: hud && hud.stampAnchor, text: t('lf_jackpot', 'Jackpot') }); } catch (e) { /* optional */ }
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
      // Deck II near-miss staging: the clicked twin flashes what it ALMOST was
      // (the target's look ghosts through it, slot-reel settle). Presentation
      // only - countWrong() has already done the honest accounting above.
      if (casino) casino.almost(tile, targetLook(), paintLook);
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
      stopGovernor();
      stopEffects();
      if (trickster) trickster.stop();
      if (casino) { casino.stop(); casino.dimOut(); }
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
      if (!complete && !zen) {
        announce(t('lf_timeout', 'Time'), 2000);
        // Deck II: a loss is acknowledged, never silent - a muted stamp and a
        // low thud while the marquee sighs out. Scaled down, still a ceremony.
        try { ctx.ceremonies.stamp({ text: t('lf_timeout', 'Time'), tone: 'pink', target: hud && hud.stampAnchor }); } catch (e) { /* optional */ }
        try { ctx.engine.fire('audio_trigger', { name: 'stamp_bad', level: 0.3, bus: 'fx' }); } catch (e) { /* optional */ }
      }

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

        // Deck III (House Rules): melt / ghost cursor / glitch-to-asset. Seeded
        // deals, presentation-only - it reads game state, it never writes any.
        trickster = createTrickster({
          seed: String(spec.seed || 'lf'),
          tier,
          budgetSec: zen ? 180 : budgetSec,
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
        });

        // Deck II (House Rules): the lighting rig. Same disarm rule as the
        // trickster; the marquee itself lights in startEffects().
        casino = createCasino({
          seed: String(spec.seed || 'lf'),
          tier, density,
          board, hud, timers, reduced, lite: coarse,
          capsOk: !(ctx.caps && Number(ctx.caps.bgIntensity) === 0),
          log: say,
        });

        say('class start: tier ' + tier + ', density ' + density + '/' + dials.density
          + (coarse ? ' (coarse)' : '') + (reduced ? ' (reduced motion)' : '')
          + ', budget ' + (zen ? 'zen' : budgetSec + 's'));

        // learn the display's true frame interval while the wall is still
        // cheap - the briefing frames are the cleanest baseline we ever get
        startGovernor();

        briefing(() => {
          clockStartedAt = Date.now();
          startEffects();
          armLoops();
          if (trickster) trickster.start();
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
          finds, misclicks, misclickStreak, cleanStreak, bestCleanStreak,
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
