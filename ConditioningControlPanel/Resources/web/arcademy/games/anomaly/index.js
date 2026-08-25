/* ============================================================================
 * games/anomaly/index.js - ANOMALY (odd-one-out; family: search; 300s).
 *
 * A grid of the SAME loop, tiled N times, playing in lockstep because it IS
 * one url in every tile - one decoder, one clock. Exactly one tile carries a
 * delta: a hue rotation, a mirror, a size, a tilt, a softening, a light step
 * (all on the <img>), or - only when the loop is really a <video> and the grid
 * is 3 wide - a playbackRate or a frame offset. Find it. Tap it. Next.
 *
 * THE THESIS, which is also the difficulty: the Distraction Engine's effects
 * apply UNIFORMLY TO ALL TILES. Every wash, every drift, every glitch is a
 * global change, and a global change is noise. Only LOCAL difference is true.
 * The class literally trains the player to stop reacting to the room.
 *
 * THE ROUND LOOP:
 *   deal    one url -> every tile; every face wears the SAME inline filter and
 *           transform SHAPE (identity values), one face wears the delta
 *   find    a correct FIRST tap advances instantly with a sub-500ms ceremony
 *   miss    a wrong tap burns WRONG_BURN_MS of the round and puts that tile
 *           OUT (it desaturates - warmer/colder, ruling 3); the streak dies
 *   move    at tier 3 the anomaly RELOCATES once mid-round under a glitch_swap,
 *           at tier 4 twice (RELOC_CAP = 2, ruling 1). Tapping where it USED to
 *           be draws the ghost outline - "it moved" - and refunds a second
 *   whiff   the round times out, the answer is revealed, and two whiffs in a
 *           row force ONE breather round (the dossier's comeback hook)
 *   bell    the 300s budget ends the class, never a round count. The plan is
 *           sized for a FAST player (rounds.js FAST_ROUND_MS), so five minutes
 *           of clean tapping still never runs out of seeded rounds
 *
 * THE CRITIC'S TOP FIX IS LAW: the top tiers get harder through relocations,
 * drift and decoy pressure at PERCEPTIBLE deltas - never through a delta
 * approaching invisibility. Every magnitude lives in rounds.js DELTA and is
 * floored by PERCEPT_FLOOR; the scratch suite asserts the whole table.
 *
 * THE ODD INDEX LIVES IN CLOSURE ONLY. It is never a data attribute, never a
 * class, never an aria string, and no deck is ever handed it. What IS in the
 * DOM is the delta itself, as an inline filter/transform on ONE .g-an-face -
 * which is the puzzle, not the answer key. Every other face carries the same
 * inline properties at identity values, so the odd tile is not even the only
 * element with its own render surface (`filter:none` rasterises differently -
 * that would be a tell in itself). The trickster's "melt a NON-odd tile" card
 * is served by meltCandidates(), which filters here and hands out elements.
 *
 * LAWS THIS FILE KEEPS:
 *   I   the ledger is honest - finds, first-tap accuracy, latencies, cleared
 *       rounds and the clock are computed here and never routed through a deck
 *   II  input honesty - a tile's hitbox is its own visual; row_drift moves the
 *       tile and its hitbox together, and every engine one-shot over the grid
 *       is decoration (fireSafe welds clickSafe/clickable off)
 *   III nothing is still - the grid breathes even at tier 1 (the decks)
 *   IV  images over text - the class-rules sheet is DRAWN, GO-only and FREE OF
 *       THE CLOCK; it shows ONCE per grade tier (gameMeta.howtoTiers) and
 *       ctx.hideTutorial skips even that first showing
 *   V   seeded - rounds.js deals the whole show off classSpec.seed; a retake
 *       replays it. Relocations are SCHEDULE-DEALT, never live rng
 *   VI  exits sacred - pause/resume/suspend/destroy (the DV discipline), the
 *       timer registry defers, reducedMotion degrades per the dossier, and
 *       caps.bgIntensity === 0 disarms every deck
 *   VII every string is ctx.lexicon(key, fallback) over lex.js AN_LEX
 *
 * WHAT THIS FILE DOES NOT OWN: grades (core/grades.js via ctx.endClass), XP
 * (C#), the tier (registry + meta), effect strengths (the engine's ceiling
 * rule), the LOOK (style.js), the lighting (casino.js), the lies
 * (trickster.js) and the CCP-effects ladder (pressure.js). The pure modules
 * are rounds.js (the plan) and grade.js (the composite).
 *
 * DECK IMPORTS are NAMESPACE imports on purpose: a missing export from a deck
 * that is still being written must not break the class (a missing FILE still
 * would - that is the DE precedent and the registry's allSettled catches it).
 * ==========================================================================*/

import * as AN_STYLE from './style.js';
import * as AN_CASINO from './casino.js';
import * as AN_TRICKSTER from './trickster.js';
import * as AN_PRESSURE from './pressure.js';
import {
  PLAYTEST, buildPlan, asBreather, relocationTarget, faceStyle, BASE_FACE,
  heatFor, cadenceMs, pitchFor, clampTier,
} from './rounds.js';
import { compositeFor, hardGates, flavorXp, median } from './grade.js';
import { AN_LEX } from './lex.js';
import { makeTaggedRoll } from '../../core/rng.js';

const GAME_KEY = 'anomaly';

/** A url the <img> element cannot show (a webm/mp4 loop). Mirrors
 *  engine/util.js VIDEO_URL_RE; games never import the engine, so the
 *  two-line rule is repeated (the DE precedent). */
const VIDEO_URL_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;
const isVideoUrl = (url) => VIDEO_URL_RE.test(String(url || ''));

/** How many draws to spend looking for a tile-able url before giving up on
 *  media entirely (a video-only pool on a 5x5 grid = plain faces, not 25
 *  <video> elements - the L&F 30Hz lock). */
const MEDIA_TRIES = 5;
/** A round shorter than this cannot be offered before the bell. */
const MIN_ROUND_MS = 2600;
/** The shared ticker. */
const TICK_MS = 110;

/* Diagnostics seams (the DV/DE precedent): the shell never reads these. */
let liveClass = null;
let lastReport = null;
let lastSnapshot = null;

/** Test seam: the scratch harness compresses the clock. Production = 1.
 *  It scales BOTH ends - a timer's real delay AND the logical time the ticker
 *  credits for a real millisecond - so a full class can be played end to end in
 *  under two seconds without the round deadlines drifting out of proportion. */
let timeScale = 1;
export function setTimeScale(f) { const v = Number(f); timeScale = Number.isFinite(v) && v > 0 ? v : 1; }
export function getTimeScale() { return timeScale; }
function scaled(ms) { return Math.max(0, Math.round(ms * timeScale)); }
/** Real elapsed ms -> class ms. Clamped so a stalled tab cannot jump the bell. */
function logical(realMs) { return Math.max(0, Math.min(1000, (Number(realMs) || 0) / timeScale)); }

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
function motionLevelOf(ctx) {
  try {
    const v = Number(ctx && ctx.motion ? ctx.motion.motionLevel : 2);
    return Number.isFinite(v) ? v : 2;
  } catch (e) { return 2; }
}
function coarseOf(ctx) {
  try { if (ctx && ctx.platform && ctx.platform.isTouch) return true; } catch (e) { /* ignore */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(pointer: coarse)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* ignore */ }
  return false;
}

function mmss(sec) {
  const s = Math.max(0, Math.round(sec));
  return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
}

export default {
  key: GAME_KEY,
  family: 'search',
  meaty: false,
  flagship: false,
  /* 300s (owner ruling, the class-length wave; it was 90). Still not an anchor
   * class: `meaty` is the timetable's one-per-night flag and has nothing to do
   * with length any more, which is why the longest class on the board is not
   * the flagged one. Grading is ratios over rounds OFFERED, and the bell was
   * always the end rather than a round count, so the only thing five minutes
   * needed was a plan deep enough to cover it - see rounds.js FAST_ROUND_MS /
   * COUNT_MAX, which size the deal off a fast player rather than off the slow
   * end of the round window. */
  timeBudgetSec: 300,
  orientation: 'portrait',   // phone only; see games/registry.js ORIENTATIONS
  title: 'Anomaly',

  manifest: {
    /* flash_burst / gif_burst are declared ONLY as clickSafe decoration over a
     * tap-precision grid (DECISIONS #9) - fireSafe() welds that on at every
     * call site, so nothing decorative can ever steal a tap. */
    effectsConsumed: [
      'wash', 'glitch_swap', 'row_drift', 'sub_flash',
      'audio_trigger', 'flash_burst', 'bubble_field', 'gif_burst',
    ],
    assetNeeds: { loops: 10, targets: 0, stills: 4, canvasSafe: false },
    boardSizes: null,
    keybinds: null,
    settings: [
      {
        key: 'an_kinds', kind: 'enum', values: ['all', 'gentle'], default: 'all',
        label_key: 'an_kinds', hint_key: 'an_kinds_hint',
      },
    ],
    peek: false,
  },

  create(ctx) {
    const t = (key, fallback) => {
      const fb = fallback == null ? (AN_LEX[key] == null ? key : AN_LEX[key]) : fallback;
      try { const v = ctx.lexicon(key, fb); return v == null ? fb : v; } catch (e) { return fb; }
    };
    const say = (m) => { try { ctx.log('[an] ' + m); } catch (e) { /* noop */ } };

    /* EMI COMMENTARY SEAMS (the heartbeat wave). note() names a moment the
     * mascot may react to - the shell prefixes 'game:' and its own voice engine
     * decides whether the moment is worth a face, a line or nothing at all.
     * Additive, one-way and fully guarded: an older shell has none of it, and a
     * mascot may never break a class. Anomaly takes no hold() window - the find
     * advance is contract-protected dead-air-free space, not a fenced one. */
    const note = (id, extra) => {
      try { if (ctx.mood && typeof ctx.mood.note === 'function') ctx.mood.note(id, extra); }
      catch (e) { /* a mascot may never break a class */ }
    };

    /* ---- lifecycle flags ------------------------------------------------ */
    let dead = false;
    let paused = false;
    let ended = false;
    let reported = false;
    let busy = true;                       // input closed until the sheet + briefing clear

    /* ---- class state ---------------------------------------------------- */
    let spec = null;
    let seed = '';
    let tier = 1;
    let n = 3;
    let plan = null;
    let reduced = false;
    let coarse = false;
    let retake = false;
    let budgetMs = 300000;
    let pool = null;
    let rollLocal = null;

    let casino = null;
    let trickster = null;
    let pressure = null;

    /* ---- the ledger (Law I: computed here, never by a deck) ------------- */
    let roundsOffered = 0;
    let roundsCleared = 0;
    let firstTapFinds = 0;
    let wrongTaps = 0;
    let subSecondFinds = 0;
    let relocatedRounds = 0;
    let relocatedCleared = 0;
    let ghostFinds = 0;                    // "it moved" near-misses drawn
    let relocationsFired = 0;
    let breathers = 0;
    let streak = 0;
    let bestStreak = 0;
    let whiffStreak = 0;
    let currentHeat = 0;
    let bellOn = false;
    let litOn = false;
    let driftOn = false;
    let washHeld = false;
    let bubblesOn = false;
    let subFlashes = 0;
    let jackpots = 0;
    let stallMs = 0;                       // ms since the player's last tap
    let stallSinceReport = 0;              // accumulator for the ~500ms report
    let lifetimeBefore = 0;
    let emiBigGridSeen = false;            // an.bigGrid is a once-per-class seam
    let emiRefusedTaps = 0;                // refusals that survived the bump throttle
    const findTimes = [];
    const recoveryTimes = [];
    const kindsSeen = new Map();           // kind -> {offered, cleared}

    /* ---- round state ---------------------------------------------------- */
    let roundIdx = 0;                      // plan cursor
    let cur = null;                        // the live round
    let mediaMode = 'plain';               // video | img | plain (stage-level, uniform)
    let lastUrl = '';

    /* ---- clock ---------------------------------------------------------- */
    let tickId = 0;
    let lastTick = 0;
    let elapsedMs = 0;

    /* ---- dom ------------------------------------------------------------ */
    let stage = null; let backdrop = null; let hud = null; let gridEl = null;
    let msgEl = null; let wellEl = null; let endEl = null;
    let roundChip = null; let clockChip = null; let streakChip = null;
    let howtoEl = null;
    let msgToken = 0;
    const tileEls = [];
    const faceEls = [];
    const mediaEls = [];
    const tileHandlers = [];

    /* ==================================================================== *
     * TIMERS - every step goes through run() so a suspend freezes the class
     * mid-round and a resume finishes it. `every` simply skips while paused.
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
     * ENGINE - one wrapper, the input-trust law welded on.
     * ==================================================================== */
    function fireSafe(kind, opts) {
      if (dead || paused || !ctx.engine) return null;
      const o = Object.assign({}, opts || {});
      if (kind === 'flash_burst' || kind === 'gif_burst') {
        o.clickSafe = true;             // decoration only over a tap-precision grid
        o.clickable = false;
        delete o.onPop;
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
    /** The engine as a deck sees it: three welded primitives + a READ of the
     *  clamped channels (THE CEILING RULE - a deck asks, it never raises). */
    const deckEngine = {
      fire: fireSafe,
      sustain: sustainSafe,
      stop: stopSafe,
      channels: () => {
        try { return (ctx.engine && typeof ctx.engine.channels === 'function') ? ctx.engine.channels() : null; }
        catch (e) { return null; }
      },
      /* the reward canon: 'jackpot' / 'near_miss' are engine-owned spectacle
       * and are NOT kind-addressed, so they are not fenced by effectsConsumed
       * (only fire/sustain are). The casino's ladder rides this. */
      ceremony: (kind, o) => {
        if (dead || paused || !ctx.engine || typeof ctx.engine.ceremony !== 'function') return null;
        try { return ctx.engine.ceremony(kind, o || {}) || null; } catch (e) { return null; }
      },
    };
    /** The player's own media, as a deck sees it. The pool lands ASYNC, so a
     *  deck gets a LIVE reader, never the pool object. */
    const deckAssets = {
      next(kind) {
        try { return (pool && typeof pool.next === 'function') ? (pool.next(kind) || null) : null; }
        catch (e) { return null; }
      },
    };
    /** bgIntensity 0 is the player's exit: read it LIVE, never a snapshot. */
    function capsArmed() { return !(ctx.caps && Number(ctx.caps.bgIntensity) === 0); }
    /** A cue through the engine; level never above the tier's audio ceiling. */
    function cue(name, level, extra) {
      const ceil = plan ? plan.audioCeil : 0.45;
      const lv = Math.min(ceil, level == null ? 0.4 : level);
      fireSafe('audio_trigger', Object.assign({ name, level: lv }, extra || {}));
    }
    /** THE REFUSED PRESS (the school's chrome vocabulary, W2). A tap that
     *  moves nothing - a dead phase, a frame this round already ate - is a
     *  muted bump, never silence and never a bright tick. THROTTLED: a mashed
     *  grid must not machine-gun one bump per frame. */
    const REFUSE_GAP_MS = 250;
    let lastBumpAt = 0;
    function refused() {
      const now = Date.now();
      if (now - lastBumpAt < REFUSE_GAP_MS) return;
      lastBumpAt = now;
      cue('bump', 0.15);   /* owner 2026-08-24: error cues -50% */
      emiRefusedTaps += 1;
      note('an.refusedTap', { kind: 'tease', n: emiRefusedTaps });
    }

    /* ---- the decks, null-safe ------------------------------------------- */
    function deck(which, method, ...args) {
      const d = which === 'casino' ? casino : which === 'pressure' ? pressure : trickster;
      if (!d || typeof d[method] !== 'function') return undefined;
      try { return d[method](...args); } catch (e) { say(which + '.' + method + ' threw: ' + ((e && e.message) || e)); return undefined; }
    }

    /* ==================================================================== *
     * HEAT - the streak ladder, capped by the grade tier.
     * ==================================================================== */
    function heat() {
      const h = heatFor(streak, tier);
      currentHeat = h;
      try { if (ctx.engine) ctx.engine.setHeat(h); } catch (e) { /* engine is optional */ }
      deck('casino', 'setHeat', h);
      deck('pressure', 'setHeat', h);
    }

    /* ==================================================================== *
     * DOM (the contract's exact shape)
     * ==================================================================== */
    function setPhase(p) { if (stage) stage.setAttribute('data-phase', p); }

    function buildDom() {
      ctx.root.textContent = '';
      stage = el('div', 'g-an-stage');
      stage.setAttribute('data-phase', 'briefing');
      stage.setAttribute('data-n', String(n));
      stage.setAttribute('data-media', mediaMode);
      if (reduced) stage.setAttribute('data-reduced', '1');

      backdrop = el('div', 'g-an-backdrop');
      stage.appendChild(backdrop);

      hud = el('div', 'g-an-hud');
      roundChip = el('span', 'g-an-chip g-an-round', '1');
      roundChip.setAttribute('aria-label', t('an_chip_round', AN_LEX.an_chip_round));
      clockChip = el('span', 'g-an-chip g-an-clock', mmss(budgetMs / 1000));
      clockChip.setAttribute('aria-label', t('an_chip_clock', AN_LEX.an_chip_clock));
      streakChip = el('span', 'g-an-chip g-an-streak', '0');
      streakChip.setAttribute('aria-label', t('an_chip_streak', AN_LEX.an_chip_streak));
      hud.appendChild(roundChip);
      hud.appendChild(clockChip);
      hud.appendChild(streakChip);
      stage.appendChild(hud);

      gridEl = el('div', 'g-an-grid');
      gridEl.style.setProperty('--an-n', String(n));
      for (let i = 0; i < n * n; i++) {
        const tile = el('div', 'g-an-tile');
        tile.setAttribute('data-i', String(i));
        tile.setAttribute('role', 'button');
        const face = el('div', 'g-an-face');
        /* EVERY face wears the identity style, so the odd one is not the only
         * element with inline filter/transform (and not the only one with its
         * own render surface). The delta is a NUMBER change, never a shape one. */
        face.style.filter = BASE_FACE.filter;
        face.style.transform = BASE_FACE.transform;
        tile.appendChild(face);
        const handler = () => onTap(i);
        tile.addEventListener('click', handler);
        tileHandlers.push(handler);
        tileEls.push(tile);
        faceEls.push(face);
        mediaEls.push(null);
        gridEl.appendChild(tile);
      }
      stage.appendChild(gridEl);

      msgEl = el('p', 'g-an-msg');
      stage.appendChild(msgEl);
      wellEl = el('div', 'g-an-flashwell');
      stage.appendChild(wellEl);
      endEl = el('div', 'g-an-end');
      endEl.hidden = true;
      stage.appendChild(endEl);

      ctx.root.appendChild(stage);
    }

    function msg(key, fallback) {
      if (!msgEl) return;
      const text = key ? t(key, fallback) : '';
      msgEl.textContent = text;
      const mine = ++msgToken;
      if (!text) return;
      after(2400, () => { if (msgEl && msgToken === mine) msgEl.textContent = ''; });
    }

    function paintHud() {
      if (roundChip) roundChip.textContent = String(Math.max(1, roundsOffered || 1));
      if (clockChip) clockChip.textContent = mmss(secLeft());
      if (streakChip) streakChip.textContent = String(streak);
    }
    /** The TRUE chip text - what the trickster's Stat Flicker restores. */
    function chipText(which) {
      if (which === 'round') return String(Math.max(1, roundsOffered || 1));
      if (which === 'clock') return mmss(secLeft());
      return String(streak);
    }
    function secLeft() { return Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)); }

    /* ==================================================================== *
     * MEDIA - one url, every tile. mediaEl semantics, copied not imported.
     * ==================================================================== */
    function makeMedia(url) {
      const video = isVideoUrl(url);
      const node = document.createElement(video ? 'video' : 'img');
      node.className = 'g-an-media';
      if (video) {
        try {
          node.muted = true; node.loop = true; node.autoplay = true; node.playsInline = true;
          node.setAttribute('muted', ''); node.setAttribute('loop', '');
          node.setAttribute('autoplay', ''); node.setAttribute('playsinline', '');
          node.setAttribute('preload', 'auto');
          node.disablePictureInPicture = true;
        } catch (e) { /* ignore */ }
      } else {
        try { node.decoding = 'async'; node.alt = ''; node.setAttribute('draggable', 'false'); } catch (e) { /* ignore */ }
      }
      node.src = url;
      if (video && typeof node.play === 'function') {
        try { const p = node.play(); if (p && p.catch) p.catch(() => {}); } catch (e) { /* autoplay policy */ }
      }
      return node;
    }

    /**
     * Deal ONE url for the whole grid. Video is only tile-able on a small grid
     * (the 30Hz lock); on a bigger grid a video-only draw is skipped and, if
     * nothing else turns up, the round runs on PLAIN faces - which the delta
     * kinds all still work on, because they are CSS on the face element.
     */
    function dealUrl() {
      if (!pool || typeof pool.next !== 'function') return null;
      const videoOk = n <= PLAYTEST.VIDEO_GRID_MAX && !reduced && !coarse && motionLevelOf(ctx) > 1;
      let firstVideo = null;
      for (let k = 0; k < MEDIA_TRIES; k++) {
        let a = null;
        try { a = pool.next(reduced || k >= 3 ? 'still' : 'loop'); } catch (e) { a = null; }
        const url = a && a.url ? String(a.url) : '';
        if (!url) continue;
        if (isVideoUrl(url)) {
          if (!firstVideo) firstVideo = url;
          if (videoOk) return url;
          continue;
        }
        return url;
      }
      return videoOk ? firstVideo : null;
    }

    /** Paint one url into every tile, recycling the element when the kind
     *  matches (the L&F paintLook lesson: a <video> teardown is an IPC
     *  round trip, and 9 of them per round would hitch the class). */
    function paintGrid(url) {
      const want = url ? (isVideoUrl(url) ? 'video' : 'img') : 'plain';
      mediaMode = want;
      lastUrl = url || '';
      if (stage) stage.setAttribute('data-media', want);
      for (let i = 0; i < faceEls.length; i++) {
        const face = faceEls[i];
        const existing = mediaEls[i];
        if (want === 'plain') {
          if (existing) { try { existing.remove(); } catch (e) { /* noop */ } mediaEls[i] = null; }
          continue;
        }
        if (existing && existing.tagName === (want === 'video' ? 'VIDEO' : 'IMG')) {
          try {
            existing.src = url;
            if (want === 'video') {
              existing.playbackRate = 1;
              if (existing.load) existing.load();
              if (existing.play) { const p = existing.play(); if (p && p.catch) p.catch(() => {}); }
            }
            continue;
          } catch (e) { /* fall through and replace */ }
        }
        if (existing) { try { existing.remove(); } catch (e) { /* noop */ } }
        const node = makeMedia(url);
        mediaEls[i] = node;
        face.appendChild(node);
      }
    }

    /* ==================================================================== *
     * THE DELTA - inline, on ONE face. The odd index never leaves closure.
     * ==================================================================== */
    function clearFace(i) {
      const face = faceEls[i];
      if (!face) return;
      face.style.filter = BASE_FACE.filter;
      face.style.transform = BASE_FACE.transform;
      const m = mediaEls[i];
      if (m && m.tagName === 'VIDEO') {
        try { m.playbackRate = 1; } catch (e) { /* noop */ }
      }
    }
    function applyFace(i) {
      const face = faceEls[i];
      if (!face || !cur) return;
      face.style.filter = cur.style.filter;
      face.style.transform = cur.style.transform;
      const m = mediaEls[i];
      if (m && m.tagName === 'VIDEO') {
        if (cur.kind === 'speed') { try { m.playbackRate = cur.style.rate; } catch (e) { /* noop */ } }
        if (cur.kind === 'frame') {
          const seek = () => {
            try {
              const dur = Number(m.duration);
              const off = Number(cur.style.offset) || 0;
              m.currentTime = (Number.isFinite(dur) && dur > off) ? off : off;
            } catch (e) { /* noop */ }
          };
          seek();
          try { if (typeof m.addEventListener === 'function') m.addEventListener('loadedmetadata', seek, { once: true }); }
          catch (e) { /* noop */ }
        }
      }
    }

    /* ==================================================================== *
     * ROUNDS
     * ==================================================================== */
    function spentNow() {
      if (!cur) return 0;
      if (paused || dead) return cur.spentMs;
      return cur.spentMs + logical(Date.now() - lastTick);
    }

    function startRound() {
      if (ended || dead) return;
      if (budgetMs - elapsedMs <= MIN_ROUND_MS) { setPhase('verdict'); return; }

      /* THE MODULO IS A BACKSTOP, NOT THE DEAL. rounds.js sizes the plan off a
       * FAST player (FAST_ROUND_MS), so a 300s class cannot walk off the end of
       * it and start replaying the seeded rounds it has already shown. This
       * stays because a plan is a pure function of its inputs and a caller may
       * always hand a budget nobody sized for; wrapping is a better failure
       * than a crash on `undefined`. */
      let r = plan.rounds[roundIdx % plan.rounds.length];
      roundIdx += 1;
      if (whiffStreak >= PLAYTEST.BREATHER_AFTER_WHIFFS) {
        r = asBreather(r, plan);
        whiffStreak = 0;
        breathers += 1;
        note('an.breather', { kind: 'curiosity', n: breathers });
      }

      const url = dealUrl();
      paintGrid(url);

      /* THE VIDEO KINDS: the plan always deals an img-safe kind AND, on a small
       * grid, an alternative that needs a real <video>. Only now - when the
       * round's url is known - can we tell which one this round can wear. */
      const canVideo = mediaMode === 'video' && n <= PLAYTEST.VIDEO_GRID_MAX && !reduced;
      const kind = (canVideo && r.altKind) ? r.altKind : r.kind;
      const delta = (canVideo && r.altKind) ? r.altDelta : r.delta;
      const dir = (canVideo && r.altKind) ? r.altDir : r.dir;

      /* the seeded odd index, clamped to this grid (a plan is dealt for this n
       * already; the modulo is a belt-and-braces guard, never a re-roll) */
      const odd = ((r.oddIndex % (n * n)) + (n * n)) % (n * n);

      cur = {
        round: r,
        kind,
        delta,
        dir,
        style: faceStyle(kind, delta, dir),
        oddIndex: odd,
        startOdd: odd,
        prevOdds: new Set(),
        eliminated: new Set(),
        relocations: (r.relocations || []).map((x) => ({ at: x.at, order: x.order, applied: false })),
        relocated: 0,
        lastRelocAt: -1,
        wrong: 0,
        spentMs: 0,
        remainingMs: r.durationMs,
        done: false,
        ghost: -1,
      };

      /* every tile back to a clean, identical slate, then the one delta */
      for (let i = 0; i < tileEls.length; i++) {
        tileEls[i].classList.remove('is-out', 'is-ghost', 'is-found', 'is-reveal');
        clearFace(i);
      }
      applyFace(odd);

      roundsOffered += 1;
      if (cur.relocations.length) relocatedRounds += 1;
      const kseen = kindsSeen.get(kind) || { offered: 0, cleared: 0 };
      kseen.offered += 1;
      kindsSeen.set(kind, kseen);

      setPhase('round');
      /* THE ROUND FLIP IS AUDIBLE (W2). setPhase writes a CSS attribute and
       * nothing else, so a fresh sheet used to be dealt in silence - an eye
       * that looked away missed the deal entirely. One soft carriage tell. */
      cue('tell', 0.3);
      paintHud();
      busy = false;
      stallMs = 0;
      stallSinceReport = 0;
      deck('trickster', 'stalled', 0);

      deck('casino', 'roundStart', roundsOffered, kind);
      deck('pressure', 'beat', 'round');
      note('an.roundStart', { kind: 'curiosity', n: roundsOffered, word: kind, left: n * n });
      if (r.breather) msg('an_breather', AN_LEX.an_breather);
      else if (roundsOffered === 1) msg('an_play_hint', AN_LEX.an_play_hint);
      say('round ' + roundsOffered + ': ' + kind + ' d=' + delta + (r.breather ? ' BREATHER' : '')
        + ', ' + Math.round(r.durationMs / 100) / 10 + 's, ' + cur.relocations.length + ' reloc, media ' + mediaMode);
    }

    /** THE RELOCATION. Idempotent - the engine's onSwap and our own deadline
     *  backstop both call it (trap 22: onSwap rides the ENGINE's timer
     *  registry and a suspend kills it, so the game keeps its own). */
    function relocate(rec) {
      if (!cur || cur.done || ended || dead || !rec || rec.applied) return;
      rec.applied = true;
      const to = relocationTarget(rec.order, cur.oddIndex, cur.eliminated);
      if (to < 0) { say('relocation had nowhere to land - skipped'); return; }
      cur.prevOdds.add(cur.oddIndex);
      clearFace(cur.oddIndex);
      cur.oddIndex = to;
      applyFace(to);
      cur.relocated += 1;
      cur.lastRelocAt = spentNow();
      relocationsFired += 1;
      deck('casino', 'relocated');
      deck('pressure', 'beat', 'relocate');
      cue('glitch', 0.32);
    }

    function armRelocation(rec) {
      /* the glitch covers EVERY tile - a global change, i.e. noise (the lie) */
      const r = fireSafe('glitch_swap', {
        targets: tileEls.slice(),
        seconds: 0.55,
        onSwap: () => relocate(rec),
        sfx: false,
      });
      const mid = r && Number(r.durMs) > 0 ? Math.round(Number(r.durMs) / 2) : 280;
      /* THE BACKSTOP (trap 22): resolve it ourselves on a deadline whatever the
       * engine's timers do. relocate() is idempotent, so both may fire. */
      after(Math.min(900, mid + 90), () => relocate(rec));
    }

    function onTap(i) {
      if (dead) return;
      /* the briefing, a pause, the verdict hold, the end card: the press is
       * REFUSED, and a refusal is audible (W2) */
      if (paused || ended || busy || !cur || cur.done) { refused(); return; }
      if (cur.eliminated.has(i)) { refused(); return; }
      const latency = Math.max(1, Math.round(spentNow()));
      if (i === cur.oddIndex) found(i, latency);
      else wrong(i, latency);
    }

    function found(i, latency) {
      cur.done = true;
      busy = true;
      stallMs = 0;
      roundsCleared += 1;
      findTimes.push(latency);
      const clean = cur.wrong === 0;
      if (clean) {
        firstTapFinds += 1;
        streak += 1;
        bestStreak = Math.max(bestStreak, streak);
      } else {
        streak = 0;
      }
      whiffStreak = 0;
      if (latency < PLAYTEST.SUBSECOND_MS) subSecondFinds += 1;
      if (cur.relocated > 0) {
        relocatedCleared += 1;
        if (cur.lastRelocAt >= 0) recoveryTimes.push(Math.max(1, Math.round(latency - cur.lastRelocAt)));
        note('an.relocatedCleared', { kind: 'celebrate', n: relocatedCleared, streak, tile: i });
      }
      const kseen = kindsSeen.get(cur.kind) || { offered: 1, cleared: 0 };
      kseen.cleared += 1;
      kindsSeen.set(cur.kind, kseen);

      const tile = tileEls[i];
      if (tile) tile.classList.add('is-found');
      const roll = rollReward();
      if (roll.jackpot) jackpots += 1;

      deck('casino', 'tap', {
        correct: true, i, latencyMs: latency, streak,
        first: clean, jackpot: !!roll.jackpot, kind: cur.kind, relocated: cur.relocated,
      });
      deck('pressure', 'setStreak', streak);
      deck('pressure', 'beat', 'find', { streak, latencyMs: latency, first: clean });
      deck('trickster', 'afterTap', { i, correct: true, moved: false, latencyMs: latency, streak });
      heat();
      litCheck();
      paintHud();

      cue('chime', 0.42, { pitch: pitchFor(streak) });
      fireSafe('flash_burst', { count: roll.jackpot ? 3 : 2, alpha: 0.45 });
      if (roll.jackpot) fireSafe('gif_burst', { count: 3, variant: 'scatter', assetKind: 'loop', holdMs: 700 });
      /* DECK IV, the escalation ladder: every find ticks the 10-segment meter,
       * a streak milestone stamps, and a jackpot roll pays the full beat.
       * All three are SHELL primitives - this game skins nothing of its own. */
      try {
        if (ctx.ceremonies && typeof ctx.ceremonies.streakMeter === 'function') {
          ctx.ceremonies.streakMeter({ target: streakChip, filled: Math.min(10, streak), gold: streak >= PLAYTEST.STREAK_LIT });
        }
        if (ctx.ceremonies && typeof ctx.ceremonies.stamp === 'function' && clean && streak > 0 && streak % 5 === 0) {
          ctx.ceremonies.stamp({ text: t('an_stamp_found', AN_LEX.an_stamp_found), target: hud });
        }
        if (roll.jackpot && ctx.ceremonies && typeof ctx.ceremonies.reward === 'function') {
          ctx.ceremonies.reward('jackpot', { text: t('an_jackpot', AN_LEX.an_jackpot), target: tile || gridEl });
        }
      } catch (e) { /* noop */ }
      msg(latency < PLAYTEST.SUBSECOND_MS ? 'an_found_fast' : 'an_found',
        latency < PLAYTEST.SUBSECOND_MS ? AN_LEX.an_found_fast : AN_LEX.an_found);

      /* SUB-500ms: the correct first tap advances instantly, by contract. */
      after(reduced ? PLAYTEST.ADVANCE_MS_REDUCED : PLAYTEST.ADVANCE_MS, endRound);
    }

    function wrong(i, latency) {
      cur.wrong += 1;
      wrongTaps += 1;
      cur.eliminated.add(i);
      const tile = tileEls[i];
      if (tile) tile.classList.add('is-out');

      /* THE GHOST OUTLINE - "it moved". Tapping where the anomaly WAS before a
       * relocation is a near-miss that TEACHES: outline the old tile, tick the
       * near-miss ceremony and refund a second of the round. (The ghost marks
       * a tile the anomaly has LEFT - it can never name the live one.) */
      const moved = cur.prevOdds.has(i);
      if (moved) {
        ghostFinds += 1;
        cur.ghost = i;
        if (tile) tile.classList.add('is-ghost');
        cur.remainingMs += PLAYTEST.GHOST_REFUND_MS;
        deck('casino', 'almost');
        deck('trickster', 'ghostOutline', i);
        /* shell/ceremonies.js owns the beat: reward('near_miss'), never a
         * ceremony of our own (SYNTHESIS #10 - games skin, never fork). */
        try {
          if (ctx.ceremonies && typeof ctx.ceremonies.reward === 'function') {
            ctx.ceremonies.reward('near_miss', { text: t('an_moved', AN_LEX.an_moved), target: tile || gridEl });
          }
        } catch (e) { /* noop */ }
        cue('near', 0.15);
        msg('an_moved', AN_LEX.an_moved);
        /* the ghost branch owns this tap - an.whiff is the timeout and
         * an.refusedTap is a dead press, so the three can never double-fire */
        note('an.ghostTap', { kind: 'commiserate', n: ghostFinds, tile: i, word: cur.kind });
      } else {
        cur.remainingMs -= PLAYTEST.WRONG_BURN_MS;
        cue('thud', 0.13, { pitch: 0.8 });
        msg('an_wrong', AN_LEX.an_wrong);
      }

      if (streak !== 0) {
        const lostStreak = streak;
        streak = 0; deck('pressure', 'setStreak', 0); heat(); litCheck();
        note('an.streakBroken', { kind: 'commiserate', streak: lostStreak });
      }
      deck('casino', 'tap', {
        correct: false, i, latencyMs: latency, streak,
        first: false, jackpot: false, kind: cur.kind, moved,
      });
      deck('pressure', 'beat', 'miss', { moved, i });
      deck('trickster', 'afterTap', { i, correct: false, moved, latencyMs: latency, streak: 0 });
      paintHud();
      if (cur.remainingMs <= 0) whiff();
    }

    function whiff() {
      if (!cur || cur.done) return;
      cur.done = true;
      busy = true;
      whiffStreak += 1;
      streak = 0;
      deck('pressure', 'setStreak', 0);
      deck('pressure', 'beat', 'miss', { timeout: true });
      heat();
      litCheck();
      const tile = tileEls[cur.oddIndex];
      /* the round is OVER - revealing the answer now teaches without ever
       * having marked the live tile (the class assertion in the suite is
       * about a LIVE round) */
      if (tile) tile.classList.add('is-reveal');
      /* a round nobody touched gets the plain line; one they hunted and missed
       * gets the answer pointed at - the reveal is the lesson either way */
      if (cur.wrong > 0) msg('an_reveal', AN_LEX.an_reveal);
      else msg('an_timeout', AN_LEX.an_timeout);
      cue('thud', 0.12, { pitch: 0.7 });
      paintHud();
      /* THE BREATH AFTER A WHIFF. `busy` is set and `cur.done` with it, so every
       * tap lands in the refusal branch; the reveal is up and the next round is
       * 1100ms away (600 reduced). THE TIMEOUT PATH ONLY: the FIND path advances
       * in 380ms (220 reduced) BY CONTRACT - a correct first tap is promised an
       * instant round - and a 240ms frame there would still be up when the next
       * round armed. A dead moment that has to be shortened is not a dead
       * moment. */
      deadBeatSafe('round_gap');
      note('an.whiff', { kind: 'commiserate', n: whiffStreak, tile: cur.oddIndex, word: cur.kind });
      after(reduced ? PLAYTEST.WHIFF_HOLD_MS_REDUCED : PLAYTEST.WHIFF_HOLD_MS, endRound);
    }

    function endRound() {
      if (ended || dead) return;
      deck('trickster', 'afterRound');
      cur = null;
      setPhase('verdict');
      startRound();
    }

    function litCheck() {
      const want = streak >= PLAYTEST.STREAK_LIT;
      if (want === litOn || !gridEl) return;
      litOn = want;
      if (want) gridEl.classList.add('is-lit');
      else gridEl.classList.remove('is-lit');
      if (want) msg('an_streak_lit', AN_LEX.an_streak_lit);
      if (want) note('an.streakLit', { kind: 'celebrate', streak, n: roundsCleared });
    }

    /** The variable-ratio canon, engine first, seeded local fallback second. */
    function rollReward() {
      try {
        if (ctx.engine && typeof ctx.engine.rewardRoll === 'function') {
          const r = ctx.engine.rewardRoll({ streak });
          if (r && typeof r === 'object') return r;
        }
      } catch (e) { /* fall through */ }
      return rollLocal ? rollLocal() : { fire: false, jackpot: false, nearMiss: false };
    }

    /* ==================================================================== *
     * AMBIENCE - uniform over the WHOLE grid. That is the point: a global
     * change is noise. Nothing here ever touches one tile.
     * ==================================================================== */
    let subTimer = 0;
    let subIdx = 0;
    function openAmbience() {
      heat();
      if (plan.washHold) {
        const h = sustainSafe('wash', { variant: 'pink', sustainForever: true, alpha: 0.18 });
        washHeld = !!h;
      }
      if (plan.bubbles) {
        const h = sustainSafe('bubble_field', { clickSafe: true, variant: 'drift' });
        bubblesOn = !!h;
      }
      if (plan.drift && !reduced) {
        const targets = tileEls.filter((_, i) => (Math.floor(i / n) % 2) === 0);
        const h = sustainSafe('row_drift', Object.assign({ targets, axis: 'x', stagger: false }, plan.drift));
        driftOn = !!h;
      }
      armSubFlash();
    }
    function stopAmbience() {
      if (subTimer) { clearTimer(subTimer); subTimer = 0; }
      for (const k of ['wash', 'bubble_field', 'row_drift']) stopSafe(k);
      washHeld = false; bubblesOn = false; driftOn = false;
    }
    /** sub_flash on the plan's cadence, heat-shortened, seed-jittered. Anchored
     *  to the flashwell, never to a tile - it must not mark anything. */
    function armSubFlash() {
      if (!plan || !plan.subFlashMs || ended) return;
      const ms = cadenceMs(plan.subFlashMs, currentHeat, plan.subJitter[subIdx % plan.subJitter.length]);
      subTimer = after(ms, () => {
        subTimer = 0;
        const r = fireSafe('sub_flash', { anchor: wellEl, variant: subIdx % 2 ? 'scatter' : 'whisper' });
        if (r) subFlashes += 1;
        subIdx += 1;
        armSubFlash();
      });
    }

    /* ==================================================================== *
     * THE CLOCK - one ticker for the class bell, the round deadline and the
     * relocation schedule. Every step reads REAL elapsed time and a pause
     * simply stops feeding it (Law VI).
     * ==================================================================== */
    function startClock() {
      lastTick = Date.now();
      tickId = every(TICK_MS, () => {
        const now = Date.now();
        const dt = logical(now - lastTick);
        lastTick = now;
        elapsedMs += dt;

        if (cur && !cur.done) {
          cur.spentMs += dt;
          cur.remainingMs -= dt;
          for (const rec of cur.relocations) {
            if (!rec.applied && cur.spentMs >= rec.at) { armRelocation(rec); break; }
          }
        }
        /* THE STALL is the player's own: ms since the last TAP, reported to the
         * trickster on a ~500ms cadence (0 resets it). It runs during a live
         * round - that is when a ghost cursor has something to lure. */
        if (!ended) {
          stallMs += dt;
          stallSinceReport += dt;
          if (stallSinceReport >= PLAYTEST.STALL_TICK_MS) {
            stallSinceReport = 0;
            deck('trickster', 'stalled', (cur && !cur.done && !busy) ? stallMs : 0);
          }
        }

        paintHud();
        if (!bellOn && elapsedMs >= budgetMs) { bell(); return; }
        if (!bellOn && budgetMs - elapsedMs <= PLAYTEST.BELL_WARN_SEC * 1000) {
          if (stage && stage.getAttribute('data-warn') !== '1') stage.setAttribute('data-warn', '1');
        }
        if (cur && !cur.done && cur.remainingMs <= 0) whiff();
      });
    }
    function stopClock() { if (tickId) { clearTimer(tickId); tickId = 0; } }

    function bell() {
      if (ended || bellOn) return;
      bellOn = true;
      busy = true;
      /* a round the bell cut short was never OFFERED - grading it would punish
       * the player for the clock (grade.js reads roundsOffered) */
      if (cur && !cur.done) {
        cur.done = true;
        roundsOffered = Math.max(0, roundsOffered - 1);
        if (cur.relocations.length) relocatedRounds = Math.max(0, relocatedRounds - 1);
        const kseen = kindsSeen.get(cur.kind);
        if (kseen) kseen.offered = Math.max(0, kseen.offered - 1);
        note('an.bellMidRound', { kind: 'commiserate', n: roundsOffered, word: cur.kind, streak: bestStreak });
      }
      setPhase('verdict');
      deck('casino', 'bell', true);
      deck('pressure', 'beat', 'bell');
      try {
        if (ctx.ceremonies && typeof ctx.ceremonies.stamp === 'function') {
          ctx.ceremonies.stamp({ text: t('an_stamp_bell', AN_LEX.an_stamp_bell), tone: 'pink', target: hud });
        }
      } catch (e) { /* noop */ }
      msg('an_bell', AN_LEX.an_bell);
      cue('stamp', 0.55);
      after(reduced ? PLAYTEST.CEREMONY_MS_REDUCED : PLAYTEST.CEREMONY_MS, finish);
    }

    /* ==================================================================== *
     * THE END - exactly one endClass, after the end card has been seen.
     * ==================================================================== */
    function finish() {
      if (ended) return;
      ended = true;
      busy = true;
      cur = null;
      stopClock();
      stopAmbience();
      deck('trickster', 'stop');
      deck('casino', 'dimOut', {
        wrongTaps, bestStreak, finds: roundsCleared,
        roundsOffered, roundsCleared, firstTapFinds,
        fail: roundsOffered > 0 && roundsCleared === 0,
      });
      deck('casino', 'stop');
      deck('pressure', 'stop');
      paintHud();                       // truth on every chip, whatever the trickster left

      const graded = compositeFor({
        gradeTier: tier,
        roundsOffered, roundsCleared, firstTapFinds,
        findTimes, relocatedRounds, relocatedCleared, recoveryTimes,
      });
      const gates = hardGates({ roundsOffered, firstTapFinds });
      const fx = flavorXp(subSecondFinds, relocatedCleared);
      /* the sGate IS the perfect class - every offered round on the first tap.
       * finish() runs once (guarded by `ended`), so this cannot repeat. */
      if (gates.sGate) note('an.perfectClass', { kind: 'celebrate', n: firstTapFinds, streak: bestStreak });

      try {
        const prior = (ctx.store && typeof ctx.store.gameMeta === 'function')
          ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
        const finds = Math.max(0, Number(prior.finds) || 0) + roundsCleared;
        ctx.store.mergeGameMeta(GAME_KEY, {
          finds,
          bestStreak: Math.max(Math.max(0, Number(prior.bestStreak) || 0), bestStreak),
          lastSeed: seed,
          lastPlayedAt: Date.now(),
        });
      } catch (e) { say('meta write failed (class unaffected): ' + ((e && e.message) || e)); }

      renderEnd(graded);
      setPhase('ended');

      const report = { metrics: { composite: graded.composite }, hardGates: gates, flavorXp: fx };
      lastReport = Object.assign({}, report, {
        inputs: {
          tier, n, seed, retake, reduced, roundsOffered, roundsCleared, firstTapFinds, wrongTaps,
          subSecondFinds, relocatedRounds, relocatedCleared, relocationsFired, ghostFinds,
          breathers, bestStreak, medianFindMs: graded.median, recoveryMs: graded.recovery,
          terms: graded.terms, elapsedMs, mediaMode,
        },
      });
      try { lastSnapshot = instance.snapshot(); } catch (e) { /* diagnostics only */ }
      say('class over: ' + roundsCleared + '/' + roundsOffered + ' cleared, ' + firstTapFinds
        + ' first-tap, median ' + graded.median + 'ms, streak ' + bestStreak
        + ', relocations ' + relocationsFired + ' -> composite ' + graded.composite.toFixed(3)
        + (gates.sGate ? ' [S gate open]' : ''));

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
      endEl.appendChild(el('h3', 'g-an-end-title', t('an_end_title', AN_LEX.an_end_title)));
      const row = (cls, k, v) => {
        const r = el('div', 'g-an-end-row' + (cls ? ' ' + cls : ''));
        r.appendChild(el('span', 'g-an-end-k', k));
        r.appendChild(el('span', 'g-an-end-v', v));
        endEl.appendChild(r);
        return r;
      };
      row('g-an-end-found', t('an_end_found', AN_LEX.an_end_found), String(roundsCleared));
      row('', t('an_end_rounds', AN_LEX.an_end_rounds), String(roundsOffered));
      row('', t('an_end_accuracy', AN_LEX.an_end_accuracy),
        (roundsOffered > 0 ? Math.round((firstTapFinds / roundsOffered) * 100) : 0) + '%');
      row('', t('an_end_median', AN_LEX.an_end_median),
        graded.median > 0 ? (Math.round(graded.median / 10) / 100).toFixed(2) + 's' : t('an_end_none', AN_LEX.an_end_none));
      row('', t('an_end_streak', AN_LEX.an_end_streak), String(bestStreak));
      row('', t('an_end_tracked', AN_LEX.an_end_tracked),
        relocatedRounds > 0 ? (relocatedCleared + ' / ' + relocatedRounds) : t('an_end_none', AN_LEX.an_end_none));
      const hardest = hardestKind();
      row('', t('an_end_kind', AN_LEX.an_end_kind),
        hardest ? t('an_kind_' + hardest, AN_LEX['an_kind_' + hardest] || hardest) : t('an_end_none', AN_LEX.an_end_none));
      endEl.appendChild(el('p', 'g-an-end-line', t('an_end_line', AN_LEX.an_end_line)));
      /* THE DEBRIEF (W2): .g-an-end animates in as ONE card (g-an-endin) and
       * the rows carry no stagger of their own, so the ladder collapses to a
       * single sheet cue rather than a blip per row nobody could hear apart. */
      cue('slide', 0.35);
    }

    /** The kind with the worst clear rate this class (>=2 offered). */
    function hardestKind() {
      let worst = null;
      let worstRate = 2;
      for (const [kind, s] of kindsSeen) {
        if (s.offered < 2) continue;
        const rate = s.cleared / s.offered;
        if (rate < worstRate) { worstRate = rate; worst = kind; }
      }
      return worst;
    }

    /* ==================================================================== *
     * THE CLASS-RULES SHEET (Deck VI, Law IV) - drawn, GO-only, and FREE OF
     * THE CLOCK (startClock() lives in the GO callback, never above it).
     * THE LAW, uniform across every open class (owner ruling 2026-08-24): the
     * sheet SHOWS the first time this player meets this class at this grade
     * tier and AUTO-SKIPS every later class at that tier, whatever the setting
     * says. The shell's "Skip class tutorials" switch (ctx.hideTutorial) now
     * means "skip even the first showing". No meta = no memory = the sheet
     * shows, which is the fallback we want.
     * ==================================================================== */
    function howtoSeenTiers() {
      try {
        const m = (ctx.store && typeof ctx.store.gameMeta === 'function')
          ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
        return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
      } catch (e) { return []; }
    }
    function rememberHowtoTier() {
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
    function hideHowto() {
      try { if (typeof AN_STYLE.hideAnomalyHowto === 'function') AN_STYLE.hideAnomalyHowto(); } catch (e) { /* noop */ }
      if (howtoEl) { try { howtoEl.remove(); } catch (e) { /* noop */ } howtoEl = null; }
    }
    /** CORE's fallback sheet: the POLICY is ours either way, the VISUALS are
     *  style.js's. These class names are the seam - CREATIVE styles them, and
     *  may replace the markup entirely by exporting showAnomalyHowto(). */
    function fallbackHowto(onGo) {
      if (!stage) return null;
      const sheet = el('div', 'g-an-howto');
      sheet.appendChild(el('h2', 'g-an-hw-title', t('an_howto_title', AN_LEX.an_howto_title)));
      const row = (figCls, caption) => {
        const r = el('div', 'g-an-hw-row');
        const fig = el('span', 'g-an-hw-fig ' + figCls);
        for (let k = 0; k < 4; k++) fig.appendChild(el('i', 'g-an-hw-cell' + (k === 2 ? ' odd' : '')));
        r.appendChild(fig);
        r.appendChild(el('p', 'g-an-hw-cap', caption));
        sheet.appendChild(r);
      };
      row('same', t('an_howto_same', AN_LEX.an_howto_same));
      row('find', t('an_howto_find', AN_LEX.an_howto_find));
      row('lie', t('an_howto_lie', AN_LEX.an_howto_lie));
      const go = el('button', 'g-an-hw-go', t('an_howto_go', AN_LEX.an_howto_go));
      go.type = 'button';
      go.setAttribute('autofocus', '');
      go.addEventListener('click', () => { try { onGo(); } catch (e) { /* noop */ } });
      sheet.appendChild(go);
      stage.appendChild(sheet);
      try { if (typeof go.focus === 'function') go.focus(); } catch (e) { /* noop */ }
      return sheet;
    }
    function howto(onDone) {
      const seen = howtoSeenTiers();
      /* AUTO-SKIP once this tier is on the record; hideTutorial skips the
       * first showing too. The skip path is an instant dismiss: the tier is
       * already banked (or deliberately never wanted), nothing is mounted to
       * hide, and the GO cue belongs to a press that did not happen. */
      if (ctx.hideTutorial === true || seen.indexOf(tier) >= 0) { onDone(); return; }
      let done = false;
      const onGo = () => {
        if (done || dead) return;
        done = true;
        /* THE PRESS THAT STARTS PLAY (W2). The sheet is one page and GO is its
         * only dismissal, so this is the school's start beat, not a page turn. */
        cue('lift', 0.5);
        rememberHowtoTier();
        hideHowto();
        onDone();
      };
      let node = null;
      if (typeof AN_STYLE.showAnomalyHowto === 'function') {
        try {
          node = AN_STYLE.showAnomalyHowto({
            mount: stage, onGo, coarse, t, tier, n,
          }) || null;
        } catch (e) { say('rules sheet refused: ' + ((e && e.message) || e)); node = null; }
      }
      if (!node) node = fallbackHowto(onGo);
      if (!node) { onDone(); return; }
      howtoEl = node;
    }

    /* ==================================================================== *
     * ASSETS
     * ==================================================================== */
    function claimAssets() {
      Promise.resolve()
        .then(() => ctx.assets.claim({ loops: 10, targets: 0, stills: 4, canvasSafe: false }))
        .then((p) => {
          if (dead || !p || typeof p.next !== 'function') return;
          pool = p;
          /* a round already on screen with plain faces gets its media now */
          if (cur && mediaMode === 'plain') run(() => { const u = dealUrl(); if (u) { paintGrid(u); applyFace(cur.oddIndex); } });
        })
        .catch((e) => say('asset claim failed - plain faces: ' + ((e && e.message) || e)));
    }

    /* ==================================================================== *
     * THE MODULE INSTANCE
     * ==================================================================== */
    const instance = {
      start(classSpec) {
        spec = classSpec || { gradeTier: 1, seed: GAME_KEY + '|none', timeBudgetSec: 300 };
        tier = clampTier(spec.gradeTier);
        seed = String(spec.seed == null ? GAME_KEY : spec.seed);
        budgetMs = Math.max(20000, (Number(spec.timeBudgetSec) || 300) * 1000);
        retake = !!spec.retake;
        reduced = probeReduced(ctx);
        coarse = coarseOf(ctx);

        const kindsMode = String(
          (ctx.settings && ctx.settings.an_kinds != null) ? ctx.settings.an_kinds : 'all',
        ).trim().toLowerCase() === 'gentle' ? 'gentle' : 'all';

        plan = buildPlan({
          seed, gradeTier: tier, timeBudgetSec: budgetMs / 1000, kindsMode, reduced, coarse,
        });
        n = plan.n;
        /* twenty-five identical tiles is the whole "oh no" of tier 3+. Once per
         * class only - the grid size never changes once the plan is dealt. */
        if (!emiBigGridSeen && n >= 5) {
          emiBigGridSeen = true;
          note('an.bigGrid', { kind: 'tension', n, left: n * n });
        }

        rollLocal = (() => {
          const roll = makeTaggedRoll(seed + '|an-vr');
          return () => {
            const base = 0.30 + 0.30 * (tier - 1) / 3;
            const chance = Math.min(1, base + Math.min(7, streak) * 0.03);
            const r = roll('fire');
            const fire = r < chance;
            return { fire, jackpot: fire && roll('jack') >= 0.85, nearMiss: !fire && r < chance + 0.08 };
          };
        })();

        try { lifetimeBefore = Math.max(0, Number((ctx.store.gameMeta(GAME_KEY) || {}).finds) || 0); }
        catch (e) { lifetimeBefore = 0; }

        try { if (typeof AN_STYLE.injectAnomalyStyle === 'function') AN_STYLE.injectAnomalyStyle(); }
        catch (e) { say('style inject failed (class unaffected): ' + ((e && e.message) || e)); }
        buildDom();

        const capsOk = capsArmed();
        if (typeof AN_CASINO.createAnCasino === 'function') {
          try {
            casino = createDeckSafely(() => AN_CASINO.createAnCasino({
              seed, tier, stage, board: gridEl, backdrop,
              hud: { root: hud, round: roundChip, clock: clockChip, streak: streakChip },
              timers: deckTimers, reduced, motionLevel: motionLevelOf(ctx), capsOk,
              t, engine: deckEngine, assets: deckAssets, log: say,
            }));
          } catch (e) { casino = null; say('casino refused: ' + ((e && e.message) || e)); }
        }
        if (typeof AN_TRICKSTER.createAnTrickster === 'function') {
          try {
            trickster = createDeckSafely(() => AN_TRICKSTER.createAnTrickster({
              seed, tier, timers: deckTimers, reduced, capsOk, coarse, cue,
              stage, grid: gridEl, budgetSec: Math.round(budgetMs / 1000),
              isHalted: () => dead || paused || ended || busy,
              stats: () => ({
                round: roundsOffered, streak, secLeft: secLeft(), cleared: roundsCleared,
                offered: roundsOffered, phase: stage ? stage.getAttribute('data-phase') : '',
              }),
              chipEl: (which) => (which === 'clock' ? clockChip : which === 'streak' ? streakChip : roundChip),
              chipText,
              /* THE MELT: a NON-odd, still-live tile. The odd index never
               * leaves this closure - the deck is handed ELEMENTS, not an
               * index, and never the one that matters. */
              meltCandidates: () => (cur && !cur.done
                ? tileEls.filter((_, i) => i !== cur.oddIndex && !cur.eliminated.has(i))
                : tileEls.slice()),
              /* the same veto as a predicate (the deck's documented fallback) */
              canMelt: (i) => !(cur && !cur.done && (Number(i) === cur.oddIndex || cur.eliminated.has(Number(i)))),
              tiles: () => tileEls.slice(),
              /* GLITCH-TO-ASSET lives on HUD CHROME, never on a truth node */
              chromeEls: () => [roundChip, clockChip, streakChip, msgEl].filter(Boolean),
              getStill: () => { const a = deckAssets.next('still'); return (a && a.url) || null; },
              assets: deckAssets,
              engine: deckEngine,
              t,
              announce: (text, ms) => {
                if (!msgEl || !text) return;
                msgEl.textContent = String(text);
                const mine = ++msgToken;
                deckTimers.after(Math.max(400, Number(ms) || 1600), () => {
                  if (msgEl && msgToken === mine) msgEl.textContent = '';
                });
              },
              log: say,
            }));
          } catch (e) { trickster = null; say('trickster refused: ' + ((e && e.message) || e)); }
        }
        if (typeof AN_PRESSURE.createAnPressure === 'function') {
          try {
            pressure = createDeckSafely(() => AN_PRESSURE.createAnPressure({
              seed, gradeTier: tier, reduced, motionLevel: motionLevelOf(ctx),
              stage, backdrop, grid: gridEl,
              /* TREMOR RIDES CHROME, NEVER A TRUTH NODE: the grid and its tiles
               * are hitboxes, so they are deliberately NOT in this list. */
              chrome: [hud, roundChip, clockChip, streakChip, msgEl],
              hud: { round: roundChip, clock: clockChip, streak: streakChip },
              engine: deckEngine, assets: deckAssets, timers: deckTimers,
              /* the held-wash url the deck may dress its veil with: a POOL
               * still, read live (the claim lands after start()). CORE builds
               * no hanging strips, so `strips` is left for the deck to find. */
              spiralUrl: () => { const a = deckAssets.next('still'); return (a && a.url) || null; },
              capsOk: capsArmed, t, log: say,
            }));
          } catch (e) { pressure = null; say('pressure refused: ' + ((e && e.message) || e)); }
        }

        claimAssets();
        liveClass = instance;
        lastReport = null;
        lastSnapshot = null;

        say('tier ' + tier + ' grid ' + n + 'x' + n + ', ' + plan.count + ' rounds dealt, budget '
          + Math.round(budgetMs / 1000) + 's, kinds [' + plan.kinds.join(',') + ']'
          + (plan.videoKinds.length ? ' +[' + plan.videoKinds.join(',') + ']' : '')
          + ', ' + plan.relocPerRound + ' reloc/round' + (reduced ? ', reduced' : '')
          + (coarse ? ', coarse' : '') + (retake ? ', RETAKE' : ''));

        /* the sheet first, then one briefing line, then the first grid */
        howto(() => {
          if (dead || ended) return;
          setPhase('briefing');
          msg('an_brief', AN_LEX.an_brief);
          startClock();
          deck('casino', 'start');
          deck('pressure', 'start');
          deck('trickster', 'start');
          openAmbience();
          after(reduced ? PLAYTEST.BRIEF_MS_REDUCED : PLAYTEST.BRIEF_MS, () => {
            if (dead || ended) return;
            startRound();
          });
        });
      },

      pause() {
        if (paused) return;
        paused = true;
        deck('pressure', 'pause');
        deck('casino', 'pause');
        deck('trickster', 'pause');
        if (stage) stage.classList.add('suspended');
      },

      resume() {
        if (!paused) return;
        paused = false;
        if (stage) stage.classList.remove('suspended');
        deck('pressure', 'resume');
        deck('casino', 'resume');
        deck('trickster', 'resume');
        lastTick = Date.now();
        const q = deferred.splice(0);
        for (const fn of q) run(fn);
      },

      /** The shell owns the overlay and the engine's suspend; we just freeze. */
      suspend(on) { if (on) instance.pause(); else instance.resume(); },

      destroy() {
        dead = true;
        stopClock();
        clearTimers();
        stopAmbience();
        hideHowto();
        for (let i = 0; i < tileEls.length; i++) {
          try { tileEls[i].removeEventListener('click', tileHandlers[i]); } catch (e) { /* noop */ }
        }
        try { if (trickster) trickster.destroy(); } catch (e) { /* noop */ }
        trickster = null;
        try { if (casino) casino.destroy(); } catch (e) { /* noop */ }
        casino = null;
        try { if (pressure) pressure.destroy(); } catch (e) { /* noop */ }
        pressure = null;
        if (pool && typeof pool.release === 'function') { try { pool.release(); } catch (e) { /* noop */ } }
        pool = null;
        tileEls.length = 0;
        faceEls.length = 0;
        mediaEls.length = 0;
        tileHandlers.length = 0;
        cur = null;
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
        if (liveClass === instance) liveClass = null;
      },

      /* -------- test / diagnostics seams (never read by the shell) -------- */
      /** Tap tile i as the player would. */
      tap(i) { onTap(i); },
      /** Force the bell (the harness never waits 90 real seconds). */
      ringBell() { bell(); },
      /** The TRUE chip text - what a Stat Flicker restores. */
      chipText(which) { return chipText(which); },

      snapshot() {
        return {
          tier, n, seed, retake, reduced, coarse, mediaMode, lastUrl,
          plan: plan ? {
            count: plan.count, kinds: plan.kinds.slice(), videoKinds: plan.videoKinds.slice(),
            relocPerRound: plan.relocPerRound, heatCap: plan.heatCap, subFlashMs: plan.subFlashMs,
            wash: plan.wash, washHold: plan.washHold, bubbles: plan.bubbles, drift: plan.drift,
          } : null,
          /* DIAGNOSTICS ONLY. The DOM never carries this and no deck is ever
           * handed it - the suite asserts both. */
          odd: cur ? cur.oddIndex : -1,
          kind: cur ? cur.kind : '',
          delta: cur ? cur.delta : 0,
          breather: !!(cur && cur.round && cur.round.breather),
          eliminated: cur ? Array.from(cur.eliminated) : [],
          prevOdds: cur ? Array.from(cur.prevOdds) : [],
          relocatedThisRound: cur ? cur.relocated : 0,
          remainingMs: cur ? Math.round(cur.remainingMs) : 0,
          spentMs: cur ? Math.round(cur.spentMs) : 0,
          roundsOffered, roundsCleared, firstTapFinds, wrongTaps, subSecondFinds,
          relocatedRounds, relocatedCleared, relocationsFired, ghostFinds, breathers,
          streak, bestStreak, whiffStreak, currentHeat, litOn, driftOn, washHeld, bubblesOn,
          subFlashes, jackpots, findTimes: findTimes.slice(), recoveryTimes: recoveryTimes.slice(),
          medianFindMs: median(findTimes),
          elapsedMs, budgetMs, bellOn, ended, reported, busy, paused, dead,
          phase: stage ? stage.getAttribute('data-phase') : null,
          howtoUp: !!howtoEl,
          casino: diag(casino), trickster: diag(trickster), pressure: diag(pressure),
          stage, gridEl, msgEl, endEl, wellEl, hud, roundChip, clockChip, streakChip,
          tiles: tileEls.slice(), faces: faceEls.slice(), media: mediaEls.slice(),
        };
      },
    };

    function diag(d) {
      if (!d || typeof d.diagnostics !== 'function') return null;
      try { return d.diagnostics(); } catch (e) { return null; }
    }
    /** A deck factory may answer null, an object, or throw - all three are fine. */
    function createDeckSafely(make) {
      const d = make();
      return (d && typeof d === 'object') ? d : null;
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
