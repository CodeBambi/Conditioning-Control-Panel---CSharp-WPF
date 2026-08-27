/* ============================================================================
 * games/deja-vu/index.js - DEJA VU (memory / pairs; family: memory).
 *
 * THE CLASS IS MANY BOARDS (owner ruling, the class-length wave 2026-08-24).
 * The one-board law this header used to carry is GONE: clear a board, take a
 * short breath, and the machine deals a fresh one - new seeded shuffle, new
 * face draw, the tricksters re-armed - until the 300s bell. The BELL IS THE
 * NORMAL END of a class now, not a punishment; it truncates whatever is live
 * (deal, preview, a flip, a mutation, a celebration), locks input and grades.
 * The grade is boards cleared + accuracy + mutation survival, normalised
 * against what the tier could clear in the budget (script.js, THE ARITHMETIC).
 * Two clocks, and keeping them apart is the whole trick: ONE class-level bell
 * clock (`elapsedMs` vs `budgetMs`, never reset) and a PER-BOARD play window
 * (`boardPlayMs`, re-stamped every time input opens) so a board's own accuracy
 * and clear time are measurable without the bell ever restarting.
 *
 * Pairs matching where the board gaslights you - but HONESTLY. Three laws hold
 * the whole design together (dossier + SYNTHESIS rulings):
 *
 *  1. MUTATIONS ONLY ON A SETTLED BOARD. A tile only ever moves while no
 *     unmatched tile is face-up. Nothing you are looking at is ever displaced,
 *     and locked pairs are exempt from every mutation, forever.
 *  2. THE TELL ALWAYS PRECEDES. 600ms of shudder plus an audio tick before any
 *     swap; a sheen plus a tick before any drift. The tick is emitted even when
 *     the visuals are degraded, so the mechanic survives reduced motion.
 *  3. INPUT TRUST (DECISIONS #9, amended for this game). The board is a
 *     click-precision surface: `flash_burst` over it is non-clickable decoration
 *     (clickSafe:true, clickable:false, no onPop - forced in fireSafe() below),
 *     and every engine one-shot anchored over the grid lands in the
 *     pointer-events:none flash well. A tap always reaches the card.
 *
 * The signature trick costs nothing: sub_flash is NOT on a dumb timer. It is
 * phase-locked to the two moments of memory encoding - preview end (-400ms) and
 * the mismatch flip-back. Same effect budget, adversarial timing.
 *
 * WHAT THIS FILE DOES NOT OWN: grades (core/grades.js via ctx.endClass), XP
 * (C#), the tier (registry + meta), the peek A-cap (shell/peek.js), the streak
 * meter and stamp shapes (shell/ceremonies.js), effect strengths (the engine's
 * ceiling rule - we ask, we never set absolutes). The pure script - layout,
 * swap/drift schedules, the composite - lives in ./script.js so determinism is
 * testable headless; the CSS lives in ./style.js (namespaced .g-dv-*).
 *
 * HOUSE RULES DECKS (0821): casino.js = Deck II (seeded lab identity, marquee
 * chase, the almost, ken-burns on face media); trickster.js = Deck III (fake
 * shuffle, the deja re-deal + called-it bonus, stat flicker). Both are
 * presentation-only by the audit in their headers; the re-deal consumes a
 * settled-board window like the honest mutations do, and its lie is a face
 * OVERLAY - pairIds and hitboxes never move.
 * ==========================================================================*/

import { injectDejaVuStyle } from './style.js';
import {
  TIMING, scaled, setTimeScale, getTimeScale,
  dialsForBoard, buildLayout, buildSwapSchedule, buildDriftSchedule,
  neighborsOf, isAdjacent, lineCells, plainShareFor, heatFor,
  matchedLoopPolicy, compositeFor, flavorXpFor, createReward,
  expectedClears, boardCostSec,
  BOARD_SIZES, BOARD_PAR,
} from './script.js';
import { createDvCasino } from './casino.js';
import { createDvTrickster, DV_TRICKSTER } from './trickster.js';
import { makeTaggedRoll, makeRng, shuffled } from '../../core/rng.js';

/** Distinct glyphs so a class is fully playable with ZERO media (the floor
 *  under the poster-frame-only floor). Deliberately font-safe, no emoji.
 *  PER BOARD the order is re-shuffled (seeded) so two glyph boards in a row do
 *  not wear the same twelve faces in the same twelve places. */
const GLYPHS = ['◆', '●', '▲', '■', '✦', '◇', '○', '△', '□', '✥', '✲', '✹'];

/** The specimen rack keeps filling across boards; past this many chips the
 *  oldest is retired so the aside cannot overflow its frame. */
const RACK_MAX = 24;

/** How many distinct faces the class claims ONCE and then re-deals per board.
 *  Must not exceed manifest.assetNeeds.loops (16) - that is the real ceiling on
 *  distinct faces, and a board only ever wears `pairs` of them. */
const FACE_POOL_WANT = 16;

/** The class-level last call: the final `TIMING.lastCallMs` gets a three-note
 *  drum so the bell is heard coming. Wires TIMING.endgameDrumMs, which was a
 *  dead constant until this wave. */
const LAST_CALL_NOTES = 3;

/** THE TIER AUDIO CEILING (House Book): every cue this class requests is
 *  clamped to its grade tier's ceiling, indexed by gradeTier-1. The clamp
 *  lives inside tick() so no call site - this file's, casino.js's or
 *  trickster.js's - can route around it. Same discipline as
 *  games/anomaly/index.js cue() against plan.audioCeil. */
const AUDIO_CEIL = Object.freeze([0.45, 0.6, 0.75, 0.9]);

/** Refused input (a tap on a locked slide, a third tap while two are up)
 *  answers with ONE muted bump, throttled: a mashed board is a knock, never
 *  a burst. */
const BUMP_MIN_MS = 250;

/** THE TOUCH PLAY-WINDOW (mobile web, ../CLAUDE.md trap 42). On the web host
 *  every remote loop is an mp4 <video>, and 2+ simultaneously PLAYING videos
 *  degrade the compositor on BOTH engines (Chromium locks the page to 30Hz;
 *  iOS caps hardware decode sessions at ~3-4 and the rest stall or fall to
 *  CPU). A 12-20 card preview playing every face at once is the worst case in
 *  the school. So on a coarse-touch device at most TOUCH_PLAY_CAP card videos
 *  play at a time, and the preview ROTATES that window through the board every
 *  TOUCH_PREVIEW_STEP_MS so every face is still seen animating. Desktop
 *  (WebView2, fine pointer) never enters any of these paths. */
const TOUCH_PLAY_CAP = 2;
const TOUCH_PREVIEW_STEP_MS = 1000;
/** The rotation's floor when a beat sizes its own step off its window (0825):
 *  below this the cap-of-2 windows churn decoders faster than iOS spins them
 *  up, and nothing is seen animating at all. */
const TOUCH_PREVIEW_STEP_MIN_MS = 400;

/** THE PREVIEW MEDIA GATE (0825 media-warming): before the memorize clock
 *  arms, each of the board's distinct remote faces gets up to
 *  PREVIEW_GATE_URL_MS on pool.ready(), and the WHOLE gate is capped at
 *  PREVIEW_GATE_CAP_MS so a broken network can never hang the class. */
const PREVIEW_GATE_URL_MS = 1200;
const PREVIEW_GATE_CAP_MS = 1500;

/** The provider's bundled glyph-floor svg (the L&F redress law, ported). */
const PLACEHOLDER_RE = /\/ae-ph-\d+\.svg(\?|#|$)/i;

/** Diagnostics seam (the engine has one too): the live class, for the scratch
 *  harness and any future "what is the board doing" debug overlay. The shell
 *  never reads this. */
let liveClass = null;
/** The last report this game handed to ctx.endClass(). Diagnostics only - the
 *  shell owns the grade; this is how the harness reads the retake flag and the
 *  composite inputs after the class has already been torn down. */
let lastReport = null;
/** The final board state, kept past teardown for the same reason. */
let lastSnapshot = null;

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** Reduced motion, read from the two places the shell publishes it. */
function probeReduced() {
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

function isVideoUrl(url) { return /\.(mp4|m4v|webm|mov)(\?|#|$)/i.test(String(url || '')); }

/** Coarse-touch probe - the canonical device test (../CLAUDE.md trap 42):
 *  pointer:coarse OR maxTouchPoints > 1. The host's own isTouch is OR-ed in at
 *  start() (ctx.platform is create-scope). A Windows touchscreen laptop
 *  matching via maxTouchPoints is the trap's deliberately accepted caveat -
 *  hardware-protective and cheap. */
function probeTouch() {
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(pointer: coarse)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* ignore */ }
  try {
    if (typeof navigator !== 'undefined' && Number(navigator.maxTouchPoints) > 1) return true;
  } catch (e) { /* ignore */ }
  return false;
}

export default {
  key: 'deja_vu',
  family: 'memory',
  meaty: false,
  flagship: false,
  /* THE CLASS LENGTH (class-length wave 2026-08-24). 90 -> 300: at 90s this
   * class was exactly one board and the bell was a failure state. At 300 it is
   * a run of boards and the bell is the whistle. `games/registry.js` GAME_META
   * mirrors this number (the parachute) - the two must move together. */
  timeBudgetSec: 300,
  orientation: 'landscape',   // phone only; see games/registry.js ORIENTATIONS
  title: 'Deja Vu',

  manifest: {
    /* Effects consumed at some tier. flash_burst IS declared (tier 4 pressure
     * beat) because DECISIONS #9 permits it as NON-CLICKABLE decoration, which
     * fireSafe() below enforces on every call rather than trusting a call site. */
    effectsConsumed: [
      'glitch_swap', 'row_drift', 'sub_flash', 'wash', 'bubble_field',
      'flash_burst', 'gif_rain', 'audio_trigger', 'ambient_field', 'crt',
    ],
    /* DOM <img>/<video> tiles only - the game draws NOTHING into canvas/WebGL,
     * so the provider may serve remote loops under the (pre-resolved) remote
     * gate. Spares cover provider failure and no-repeat variety on retakes. */
    assetNeeds: { loops: 16, targets: 0, stills: 4, canvasSafe: false },
    /* Board size in PAIRS. 'auto' = tier-driven (the default, so an untouched
     * setting can never read as below par); playing under your tier's par caps
     * the class at A, and the SHELL computes that cap from this table. */
    boardSizes: { values: ['auto'].concat(BOARD_SIZES), par: Object.assign({}, BOARD_PAR) },
    keybinds: [{ verb: 'peek', label_key: 'dv_cram_key', default: 'KeyC' }],
    settings: [
      {
        key: 'dv_peek_hold', kind: 'range', min: 0.5, max: 2, step: 0.05, fmt: 'mult',
        default: 1, label_key: 'dv_peek_hold', hint_key: 'dv_peek_hold_hint',
      },
      {
        key: 'dv_cram_assist', kind: 'bool', default: true,
        label_key: 'dv_cram_assist', hint_key: 'dv_cram_hint',
      },
      {
        key: 'dv_matched_loops', kind: 'enum', values: ['auto', 'keep-playing', 'freeze'],
        default: 'auto', label_key: 'dv_matched_loops',
      },
    ],
    peek: true,               // Cram Assist IS the shared peek verb (SYNTHESIS #6)
  },

  create(ctx) {
    const t = (key, fallback) => {
      try { return ctx.lexicon(key, fallback); } catch (e) { return fallback; }
    };
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };

    /* EMI COMMENTARY SEAMS (the heartbeat wave). emiNote() names a moment the
     * mascot may react to - the shell prefixes 'game:' and its own voice engine
     * decides whether the moment is worth a face, a line or nothing at all.
     * emiHold() fences a timing-critical window where she may pull faces but
     * never words. Both are additive, one-way and fully guarded: an older shell
     * has neither, and a mascot may never break a class.
     * Named emiNote/emiHold and not note/hold because runSwap already owns a
     * local `note` (the swap tell's DOM label) and a seam fires inside it. */
    const emiNote = (id, extra) => {
      try { if (ctx.mood && typeof ctx.mood.note === 'function') ctx.mood.note(id, extra); }
      catch (e) { /* a mascot may never break a class */ }
    };
    const emiHold = (on) => {
      try { if (ctx.mood && typeof ctx.mood.hold === 'function') ctx.mood.hold(!!on); }
      catch (e) { /* a mascot may never break a class */ }
    };
    /* THE ONE HELD WINDOW: the memorize preview. Held from preview() to
     * previewDown(), and released by every exit path (bell, clear, finish,
     * pause/suspend, destroy) so the fence can never outlive the beat. */
    let emiPreviewHeld = false;
    let emiPopNoted = false;       // one bubble note a board, never one a pop

    /* ---- lifecycle flags ------------------------------------------------- */
    let dead = false;
    let paused = false;
    let ended = false;
    let busy = true;            // input is closed until the preview is over
    const timers = new Set();
    const deferred = [];

    /* ---- class state ---------------------------------------------------- */
    let spec = null;
    let dials = null;            // BOARD N's dials (dialsForBoard, re-derived per board)
    let baseDials = null;        // BOARD 1's dials - the grade's stable reference
    let classSeed = '';          // the class seed; every board is classSeed + '|b<N>'
    let classTier = 1;
    let chosenPairs = null;      // the player's board-size override, or null
    let expectClears = 1;        // the S gate, in boards (script.js expectedClears)
    let layout = null;
    let swaps = [];
    let drifts = [];
    let cells = [];
    let roll = null;
    let rollReward = null;
    let pool = null;
    let facePool = [];           // up to FACE_POOL_WANT distinct urls, claimed ONCE
    let facesDirty = false;      // BREADCRUMB ONLY (diagnostics): the pool landed
                                 // mid-board, so this board played on glyphs.
                                 // Nothing gates on it - dealFaces() runs for
                                 // every board regardless and clears it.
    let facesLocked = false;     // a preview has started: nothing may re-face this board
    let pairUrls = [];           // THIS board's pairId -> url (re-drawn per board)
    let boardGlyphs = GLYPHS.slice();   // THIS board's glyph order (re-shuffled per board)
    let reduced = false;
    let posterOnly = false;
    let touch = false;           // coarse-touch device: TOUCH_PLAY_CAP applies
    let loopPolicy = { play: true, reason: 'auto' };
    let retake = false;
    let ambienceKey = '';        // the sustained-dial signature, so a restart is rare

    /* ---- THE BOARD LOOP -------------------------------------------------- */
    let boardNo = 0;             // 1-based; 0 = nothing dealt yet
    let boardsCleared = 0;       // the class's score, and the grade's main term
    let belled = false;          // the bell has taken the board (one-way)
    /** W3 P0-2: the widest a countdown window may be heard, in ms. */
    const COUNTDOWN_MS = 3000;
    let lastCallDone = false;    // the last-ten-seconds drum has beaten
    const boardLog = [];         // {board, pairs, attempts, clearSec} per cleared board

    let casino = null;                  // House Rules Deck II (marquee / almost / ken-burns)
    let trickster = null;               // House Rules Deck III (shuffle / re-deal / flicker)
    let redealLie = null;               // {cell, wearPairId} during the re-deal show
    let lieNode = null;                 // the lie's overlay element
    let watchLie = -1;                  // cell index: flip the liar NEXT and it pays
    let calledLies = 0;

    /* PER BOARD (reset by startBoard) */
    let attempts = 0;
    let matched = 0;
    let mismatchStreak = 0;
    let settledWindow = 0;
    let swapsFired = 0;              // THIS board's, so the chip reads x/budget
    /* CLASS-LEVEL (never reset). `totalAttempts` / `totalMatched` bank the
     * finished boards; the live board's own two counters are added on top
     * wherever a class total is wanted, so nothing is ever double-counted. */
    let totalAttempts = 0;
    let totalMatched = 0;
    let totalSwaps = 0;              // swaps survived across every finished board
    let casinoLit = false;           // the lab identity is rolled once (see armPlay)
    let combo = 0;              // deliberately CARRIED across boards: clearing a
                                // board is not a reason to break your streak
    let maxCombo = 0;
    let tracked = 0;
    let driftsFired = 0;
    let bubblesPopped = 0;
    let jackpots = 0;
    const faceUp = [];
    let flipping = 0;                   // cards mid-flip (a third tap must not land)
    const revealed = [];                // every reveal, in order (near-miss window)
    const swapAttempt = new Map();      // pairId -> attempts count when it moved
    let drumrolled = false;

    /* ---- clock ---------------------------------------------------------- */
    let clockId = 0;
    let lastTick = 0;
    /* ONE CLASS-LEVEL BELL CLOCK. `elapsedMs` counts the whole class, across
     * every board, and is never reset - it is the only thing `budgetMs` is ever
     * compared against. The two play stamps below are GRADE clocks and have
     * nothing to do with the bell. */
    let elapsedMs = 0;
    let classPlayMs = -1;               // the FIRST board's play start (total live play)
    let boardPlayMs = 0;                // THIS board's play start (its own clear time)
    let budgetMs = 300000;

    /* ---- dom ------------------------------------------------------------ */
    let stage = null; let grid = null; let well = null; let hint = null; let bench = null;
    let meterWrap = null; let swapChip = null; let clockChip = null; let boardChip = null;
    let peekBtn = null; let cramRow = null; let rack = null;

    /* ==================================================================== *
     * TIMERS - every step goes through run() so a suspend freezes the class
     * mid-flip and a resume finishes it (never a half-flipped board).
     * ==================================================================== */
    function run(fn) {
      if (dead) return;
      if (paused) { deferred.push(fn); return; }
      try { fn(); } catch (e) { say('step failed: ' + ((e && e.message) || e)); }
    }
    function after(ms, fn) {
      const id = setTimeout(() => { timers.delete(id); run(fn); }, scaled(ms));
      timers.add(id);
      return id;
    }
    function clearTimers() {
      for (const id of Array.from(timers)) clearTimeout(id);
      timers.clear();
      deferred.length = 0;
    }
    /** The deck modules' timer registry: this class's own run()-gated timers. */
    const deckTimers = {
      after,
      cancel: (id) => { clearTimeout(id); timers.delete(id); },
    };

    /* ==================================================================== *
     * ENGINE - one wrapper, three laws enforced in one place.
     * ==================================================================== */
    /** fire() with the input-trust law welded on. */
    function fireSafe(kind, opts) {
      if (dead || paused) return null;
      const o = Object.assign({}, opts || {});
      if (kind === 'flash_burst' || kind === 'gif_burst') {
        // DECISIONS #9: decoration only over a tap board. Not negotiable at the
        // call site - it is stripped here.
        o.clickSafe = true;
        o.clickable = false;
        delete o.onPop;
      }
      try { return ctx.engine.fire(kind, o) || null; } catch (e) { say('fire(' + kind + ') failed'); return null; }
    }
    function sustainSafe(kind, opts) {
      if (dead || paused) return null;
      try { return ctx.engine.sustain(kind, opts || {}) || null; } catch (e) { return null; }
    }
    function stopSafe(kind) { try { ctx.engine.stop(kind); } catch (e) { /* noop */ } }
    function heat() {
      const h = heatFor(dials, progress());
      try { ctx.engine.setHeat(h); } catch (e) { /* noop */ }
      if (casino) casino.setHeat(h);           // the marquee rides the same scalar
    }
    /** A cue that must be heard even when every visual is degraded. THE ONE
     *  ROAD: every request lands here and is clamped to the grade tier's
     *  audio ceiling, so a level is never louder than the year allows. */
    function tick(name, level, extra) {
      const ceil = AUDIO_CEIL[(dials ? dials.tier : 1) - 1] || AUDIO_CEIL[0];
      const lv = Math.min(ceil, level == null ? 0.45 : level);
      fireSafe('audio_trigger', Object.assign({ name, level: lv }, extra || {}));
    }
    /** The refused-input bump, throttled (BUMP_MIN_MS). */
    let lastBumpAt = 0;
    function refused() {
      const now = Date.now();
      if (now - lastBumpAt < BUMP_MIN_MS) return;
      lastBumpAt = now;
      tick('bump', 0.15);   /* owner 2026-08-24: error cues -50% */
    }
    /** Class progress, 0..1 - the engine's heat curve and the plain-share ramp
     *  both ride it. It is a CLASS number now (boards done against the tier's
     *  expectation), not a board number: a per-board progress would have reset
     *  the heat to nothing every time the player cleared something. */
    function progress() {
      if (!dials) return 0;
      const done = boardsCleared + (matched / Math.max(1, dials.pairs));
      const byBoards = done / Math.max(1, expectClears);
      const byTime = budgetMs > 0 ? elapsedMs / budgetMs : 0;
      return Math.max(0, Math.min(1, Math.max(byBoards, byTime)));
    }

    /** THIS board's glyph, from the board's own seeded glyph order. */
    function glyphFor(pairId) {
      const n = boardGlyphs.length || 1;
      const i = Math.round(Number(pairId) || 0);
      return boardGlyphs[((i % n) + n) % n];
    }

    /* ==================================================================== *
     * BOARD
     * ==================================================================== */
    function buildDom() {
      const root = ctx.root;
      root.textContent = '';
      stage = el('div', 'g-dv-stage');

      /* The lab itself: ambience layer under everything, decoration only.
       * (Corner monitor glows ride the stage background; these are the moving
       * parts - scanlines, the oscilloscope sweep, the vignette.) */
      const lab = el('div', 'g-dv-lab');
      lab.setAttribute('aria-hidden', 'true');
      lab.appendChild(el('span', 'g-dv-scanlines'));
      lab.appendChild(el('span', 'g-dv-sweep'));
      lab.appendChild(el('span', 'g-dv-vig'));
      stage.appendChild(lab);

      /* HUD: the shared 10-segment streak meter, the swap budget, the bell. */
      const hud = el('div', 'g-dv-hud');
      meterWrap = el('span', 'g-dv-meterwrap');
      meterWrap.appendChild(el('span', 'chip', t('streak', 'streak')));
      const meterHost = el('span', 'g-dv-meterhost');
      meterWrap.appendChild(meterHost);
      hud.appendChild(meterWrap);
      /* THE BOARD COUNTER is the class's running score now, so it is always
       * there. The swap chip is ALWAYS MINTED too and merely hidden while the
       * budget is zero: the escalation ladder can hand a tier-1 player a swap
       * on board 2, and a chip that only existed if board 1 had a budget would
       * have left that swap silently unaccounted for. */
      boardChip = el('span', 'chip num', boardChipText());
      hud.appendChild(boardChip);
      swapChip = el('span', 'chip num', swapChipText());
      swapChip.hidden = !(dials.swapBudget > 0);
      hud.appendChild(swapChip);
      clockChip = el('span', 'chip num', clockText());
      hud.appendChild(clockChip);
      if (retake) {
        const r = el('span', 'chip g-dv-retake', t('dv_retake', 'Retake'));
        hud.appendChild(r);
      }
      stage.appendChild(hud);
      meterWrap._host = meterHost;
      paintMeter();

      /* the board + the pointer-events:none well the engine draws into */
      const wrap = el('div', 'g-dv-boardwrap');
      grid = el('div', 'g-dv-grid');
      grid.style.setProperty('--g-dv-cols', String(dials.cols));
      grid.style.setProperty('--g-dv-rows', String(dials.rows));
      grid.setAttribute('role', 'group');
      grid.setAttribute('aria-label', t('game_deja_vu', 'Deja Vu'));
      well = el('div', 'g-dv-flashwell');
      well.setAttribute('aria-hidden', 'true');
      well.style.pointerEvents = 'none';
      wrap.appendChild(grid);
      wrap.appendChild(well);
      stage.appendChild(wrap);
      bench = wrap;                       // the casino's marquee frames the bench

      /* THE CELLS ARE NOT BUILT HERE any more - `startBoard()` mints them, and
       * mints them again for every board. buildDom owns the CHROME (the lab,
       * the HUD, the empty grid, the rack, the hint) and that chrome lives for
       * the whole class. */
      cells = [];

      /* Cram Assist = the shared peek verb, skinned. */
      cramRow = el('div', 'g-dv-cram');
      if (cramEnabled()) {
        peekBtn = el('button', 'arc-peekbtn', t('dv_cram_assist', 'Cram Assist'));
        peekBtn.type = 'button';
        cramRow.appendChild(peekBtn);
      }
      stage.appendChild(cramRow);

      hint = el('p', 'g-dv-hint', t('dv_deal_hint', 'Dealing the board.'));
      stage.appendChild(hint);

      /* The specimen rack: one slide chip per matched pair, filed at the
       * frame's edge. Pure decoration (pointer-events:none in CSS) - locked
       * cells stay on the grid because the mutation laws key off them. */
      rack = el('aside', 'g-dv-rack');
      rack.setAttribute('aria-hidden', 'true');
      rack.appendChild(el('span', 'g-dv-rack-label', t('dv_rack_label', 'Specimens')));
      stage.appendChild(rack);

      root.appendChild(stage);
    }

    /** File a matched pair into the rack (decoration; must never throw).
     *  The rack is CLASS-LEVEL - it keeps filling across boards, because a
     *  growing shelf of specimens is the nicest read on "how far did I get".
     *  It is capped at RACK_MAX chips (a 7-board tier-1 class files 42) and the
     *  oldest slide is retired rather than letting the aside overflow. */
    function rackAdd(pairId) {
      if (!rack) return;
      try {
        /* W3 P2-3: a specimen sliding into the rack is paper on paper. */
        tick('paper', 0.16);
        rack.appendChild(el('span', 'g-dv-slide', glyphFor(pairId)));
        const slides = rack.querySelectorAll ? rack.querySelectorAll('.g-dv-slide') : null;
        if (slides && slides.length > RACK_MAX) slides[0].remove();
      } catch (e) { /* the rack is scenery */ }
    }

    function buildCell(i) {
      const holder = el('div', 'g-dv-cell');
      const card = el('button', 'g-dv-card');
      card.type = 'button';
      card.setAttribute('aria-label', t('dv_card', 'Card') + ' ' + (i + 1));
      card.disabled = false;
      card.addEventListener('click', () => onTap(i));
      holder.appendChild(card);
      grid.appendChild(holder);
      return {
        index: i,
        pairId: layout.slots[i],
        state: 'down',          // 'down' | 'up' | 'locked'
        holder, card,
        face: null,
        wax: null,
      };
    }

    /** THE DEAD URL (0826). A face whose url 404s paints the browser's own
     *  broken-image glyph - a tester saw one on this board. The floor is already
     *  here: a pair with NO url wears its GLYPH face, and a glyph pair plays
     *  exactly like a media pair. So a broken url is dropped from `pairUrls` and
     *  BOTH of the pair's cards are re-applied together - a pair whose two halves
     *  wore different faces would be a worse bug than the one being fixed.
     *  Convicted once (the twin fires its own error on the same url), and
     *  pool.markBroken keeps the row out of the next board's draw. */
    function faceBroke(pairId, url) {
      const u = String(url || '');
      if (dead || !u || pairUrls[pairId] !== u) return;    // already dropped, or stale
      pairUrls[pairId] = null;
      try { if (pool && typeof pool.markBroken === 'function') pool.markBroken(u); }
      catch (e) { /* an optional seam never breaks a class */ }
      say('face failed, glyph pair ' + pairId + ': ' + u);
      for (const c of cells) if (c && c.pairId === pairId) applyMedia(c);
    }

    /** One media node per card, created once and never re-created on a flip. */
    function applyMedia(cell) {
      const url = pairUrls[cell.pairId];
      const glyph = glyphFor(cell.pairId);
      if (cell.face && cell.face._url === url && cell.face._glyph === glyph) return;
      if (cell.face) { try { cell.face.remove(); } catch (e) { /* noop */ } cell.face = null; }
      let node;
      if (url && isVideoUrl(url) && !posterOnly) {
        node = el('video', 'g-dv-face');
        node.muted = true; node.loop = true; node.autoplay = false;
        node.setAttribute('muted', 'true');
        node.setAttribute('playsinline', 'true');
        node.setAttribute('preload', 'metadata');
        node.src = url;
      } else if (url) {
        node = el('img', 'g-dv-face');
        node.setAttribute('alt', '');
        node.onerror = () => faceBroke(cell.pairId, url);
        node.src = url;
      } else {
        node = el('div', 'g-dv-face g-dv-glyph', glyph);
        node.style.setProperty('display', 'flex');
      }
      node._url = url || null;
      node._glyph = glyph;
      cell.face = node;
      cell.card.appendChild(node);
    }

    function playFace(cell, on) {
      const f = cell && cell.face;
      if (!f || f.tagName !== 'VIDEO') return;
      try {
        if (on && !posterOnly) { if (typeof f.play === 'function') { const p = f.play(); if (p && p.catch) p.catch(() => {}); } }
        else if (typeof f.pause === 'function') f.pause();
      } catch (e) { /* a poster frame is an acceptable floor */ }
    }

    /* ==================================================================== *
     * THE TOUCH PLAY-WINDOW (touch only - see TOUCH_PLAY_CAP above).
     * The whole-board show beats (preview, re-deal) still give every card its
     * media element and its 'up' face, but only TOUCH_PLAY_CAP of the video
     * faces PLAY at once; the window rotates through the list on a short
     * interval so every face gets its animated moment, then the beat's own
     * flip-down pauses everything. The steps ride after()/run(), so a suspend
     * defers them and clearTimers() (startBoard, bell, destroy) kills the
     * chain outright. Never entered on desktop: every caller is touch-gated.
     * ==================================================================== */
    let previewing = false;      // a play-window is live (touch only, one-way per beat)
    let previewTicks = false;    // W3 P0-2: the memorize countdown is armed
    let previewList = null;      // the cells the window rotates over
    let previewCursor = 0;
    let previewStepMs = TOUCH_PREVIEW_STEP_MS;   // this beat's rotation step

    function videoFaces(list) {
      const out = [];
      for (const c of list) if (c && c.face && c.face.tagName === 'VIDEO') out.push(c);
      return out;
    }
    function playWindowStart(list, windowMs) {
      previewList = list;
      previewCursor = 0;
      previewing = true;
      /* THE ROTATION ARITHMETIC (0825): a fixed 1000ms step never reached
       * every face inside a shrinking previewMs (tier 3: 10 of 16 faces were
       * NEVER played during the memorize beat). When the caller knows its
       * window, the step is sized so ceil(faces / TOUCH_PLAY_CAP) rotations
       * fit inside it, floored at TOUCH_PREVIEW_STEP_MIN_MS. The cap of 2
       * PLAYING videos (the iOS decode ceiling) is untouched - the window
       * just rotates faster. Callers without a window keep the old step. */
      const vids = videoFaces(list || []);
      const rotations = Math.max(1, Math.ceil(vids.length / TOUCH_PLAY_CAP));
      previewStepMs = Number(windowMs) > 0
        ? Math.max(TOUCH_PREVIEW_STEP_MIN_MS, Math.floor(Number(windowMs) / rotations))
        : TOUCH_PREVIEW_STEP_MS;
      playWindowStep();
    }
    function playWindowStep() {
      if (!previewing || dead || ended || belled) return;
      const vids = videoFaces(previewList || []);
      const n = vids.length;
      if (!n) return;
      for (const c of vids) playFace(c, false);
      for (let k = 0; k < Math.min(TOUCH_PLAY_CAP, n); k++) {
        playFace(vids[(previewCursor + k) % n], true);
      }
      previewCursor = (previewCursor + TOUCH_PLAY_CAP) % n;
      // a board with no more videos than the cap has nothing to rotate
      if (n > TOUCH_PLAY_CAP) after(previewStepMs, playWindowStep);
    }
    function playWindowStop() {
      previewing = false;
      previewList = null;
    }

    /* ---- HUD paint ------------------------------------------------------- */
    function swapChipText() {
      return t('dv_swaps', 'swaps') + ' ' + swapsFired + '/' + dials.swapBudget;
    }
    /** The class's score: boards cleared. Same shape as the swap chip. */
    function boardChipText() {
      return t('dv_boards', 'boards') + ' ' + boardsCleared;
    }
    function clockText() {
      const left = Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000));
      const m = Math.floor(left / 60);
      const s = left % 60;
      return (m > 0 ? m + ':' + String(s).padStart(2, '0') : left + 's');
    }
    function paintHud() {
      if (boardChip) boardChip.textContent = boardChipText();
      if (swapChip) {
        swapChip.textContent = swapChipText();
        // the ladder can open a budget mid-class, so visibility is repainted too
        swapChip.hidden = !(dials && dials.swapBudget > 0);
      }
      if (clockChip) clockChip.textContent = clockText();
    }
    function paintMeter() {
      const host = meterWrap && meterWrap._host;
      if (!host) return;
      // the meter is the SHELL's primitive (10 segments, always) - visible from 2
      if (combo < 2) { host.textContent = ''; return; }
      try {
        ctx.ceremonies.streakMeter({ target: host, filled: combo, gold: combo >= 8 });
      } catch (e) { /* a ceremony must never be the thing that fails */ }
    }
    function setHint(key, fallback, warm) {
      if (!hint) return;
      hint.textContent = t(key, fallback);
      if (warm) hint.classList.add('warm'); else hint.classList.remove('warm');
    }

    /* ==================================================================== *
     * THE BOARD LOOP - the class's spine.
     *
     * startBoard(n) -> deal -> preview -> armPlay -> the attempt loop -> win()
     * -> a short celebration -> startBoard(n+1) -> ... until bell().
     *
     * EVERY BOARD IS ITS OWN SEEDED DEAL. The board seed is
     * `classSeed + '|b<N>'`, which re-rolls the layout, the deal cascade, the
     * swap schedule, the drift schedule, the face draw AND the glyph order - so
     * two boards in a row never wear the same face-to-position map even when
     * the face pool is small enough that the FACES themselves repeat (the pool
     * is 16 and a board wears at most 10 of them). The whole night is still one
     * pure function of the class seed, so a retake replays board for board.
     * ==================================================================== */

    /** This board's seed. Board 1 included: '|b1' is part of the contract. */
    function boardSeed(n) { return classSeed + '|b' + n; }

    /**
     * Draw THIS board's faces and glyphs off the board seed.
     * The urls come from the class's single claimed pool (`facePool`, up to 16
     * distinct); each board shuffles that pool and takes the first `pairs`, so
     * the SUBSET and its pairing both move every board. Short pool = the
     * leftovers play on their glyph face, which is always distinct - the same
     * legibility floor claimAssets has always kept.
     */
    function dealFaces(n) {
      const want = dials.pairs;
      boardGlyphs = shuffled(GLYPHS.slice(), makeRng(classSeed + '|dv|glyphs|b' + n));
      const draw = facePool.length
        ? shuffled(facePool.slice(), makeRng(classSeed + '|dv|faces|b' + n))
        : [];
      pairUrls = [];
      for (let pid = 0; pid < want; pid++) pairUrls[pid] = draw[pid] || null;
      facesDirty = false;
      /* THE IMMINENT BOARD HEADS THE WARM WINDOW (0825). warmManifest /
       * warmCursor consume NO rng (recon-verified) - free on every path. */
      warmBoardFaces();
    }

    /** Hand THIS board's faces to the provider's manifest warmer, cursor at
     *  the top, so the deal/preview about to run is what the rail fetches
     *  first. Replaces the class-wide facePool manifest for the board's
     *  lifetime (one manifest per provider, by design). */
    function warmBoardFaces() {
      if (!pool || typeof pool.warmManifest !== 'function') return;
      try {
        const need = [];
        for (const u of pairUrls) if (u && need.indexOf(u) < 0) need.push(u);
        if (!need.length) return;
        pool.warmManifest(need.map((u) => ({ url: u })));
        if (typeof pool.warmCursor === 'function') pool.warmCursor(0);
      } catch (e) { /* a warm-up never breaks a deal */ }
    }

    /** Mint this board's cells into the (emptied) grid. */
    function buildCells() {
      if (!grid) return;
      try { grid.textContent = ''; } catch (e) { /* noop */ }
      grid.style.setProperty('--g-dv-cols', String(dials.cols));
      grid.style.setProperty('--g-dv-rows', String(dials.rows));
      grid.classList.remove('jiggle', 'scanning', 'rewind');
      cells = [];
      for (let i = 0; i < dials.cells; i++) cells.push(buildCell(i));
    }

    /**
     * Deal board `n`. Also the ONE place the escalation ladder is applied and
     * the ONE place the House tricksters are re-armed.
     * @param {number} n 1-based board number
     */
    function startBoard(n) {
      if (dead || ended || belled) return;
      /* A CLEAN SLATE. Every timer still in flight belongs to the board that
       * just ended (a casino flash, a trickster flicker); none of them may land
       * on the new board. This call is safe from inside a run() step because
       * after() removes its own id before it runs the step. */
      clearTimers();
      if (touch) playWindowStop();   // a dead board's window must not survive it

      boardNo = n;
      /* THE LADDER: board N's dials are the player's tier bumped one gentle
       * notch per CLEARED board, capped at roughly one tier above (see
       * script.js ESCALATION). The pair count never moves - the player's
       * board-size choice is the board all night. */
      dials = dialsForBoard(classTier, boardsCleared, { pairs: chosenPairs });

      const bseed = boardSeed(n);
      layout = buildLayout(bseed, dials);
      swaps = buildSwapSchedule(bseed, dials);
      drifts = buildDriftSchedule(bseed, dials, swaps);

      /* per-board counters (the class totals were banked by the caller) */
      attempts = 0;
      matched = 0;
      mismatchStreak = 0;
      settledWindow = 0;
      swapsFired = 0;
      flipping = 0;
      faceUp.length = 0;
      revealed.length = 0;
      swapAttempt.clear();
      drumrolled = false;
      watchLie = -1;
      emiPopNoted = false;
      clearLie();
      busy = true;                       // input is closed until this preview is over
      facesLocked = false;

      dealFaces(n);
      buildCells();

      /* THE TRICKSTERS RE-ARM PER BOARD. The deck's cards are per-class by
       * construction (one fake shuffle, one re-deal, N flickers), so a class of
       * seven boards with one class-level deck would have shown its signature
       * once in five minutes. A fresh deck per board, seeded on the BOARD seed,
       * deals a fresh shuffle/re-deal window every time - and on the ladder's
       * own deckTier, so the House cards climb with the honest dials and stop
       * at the same cap. The old deck is destroyed first: its pending timers
       * are already cancelled above, and destroy() makes any survivor a no-op. */
      if (trickster) { try { trickster.destroy(); } catch (e) { /* noop */ } }
      trickster = makeTrickster(bseed, dials.deckTier);

      paintHud();
      setHint('dv_deal_hint', 'Dealing the board.');
      say('board ' + n + ': ' + dials.cols + 'x' + dials.rows + ' (' + dials.pairs
        + ' pairs), preview ' + dials.previewMs + 'ms, swaps ' + dials.swapBudget
        + (dials.drift ? ', drift ' + dials.drift : '') + ', bump ' + dials.bump
        + ', deck tier ' + dials.deckTier);
      deal();
    }

    /* ==================================================================== *
     * PHASE 0 - deal & preview (with the first poison beat)
     * ==================================================================== */
    function deal() {
      /* W3 P0-12: the cards LAND. Twelve to twenty of them at the stagger
       * would be a hiss, so the ear gets the first six and the last one - the
       * deal starting and the deal finishing - each jittered off the class
       * stream so no two land on the same note. The re-deal path (runRedeal)
       * is a re-showing, not a deal, and stays out of this. */
      const lastDealt = layout.dealOrder.length - 1;
      // one note for the whole cascade, never one a card
      emiNote('dv.dealCascade', { kind: 'ambient', n: layout.dealOrder.length, left: dials.pairs });
      layout.dealOrder.forEach((cellIndex, n) => {
        const audible = n < 6 || n === lastDealt;
        after(n * TIMING.dealStaggerMs, () => {
          const c = cells[cellIndex];
          if (!c) return;
          if (audible) tick('card_deal', 0.22, { pitch: 1 + (roll('deal') - 0.5) * 0.08 });
          applyMedia(c);
          c.card.classList.add('dealt');
        });
      });
      after(layout.dealOrder.length * TIMING.dealStaggerMs + 80, preview);
    }

    function preview() {
      /* THE MEDIA GATE (0825): the memorize clock must not run against faces
       * still fetching (<video preload="metadata"> moves no frame bytes until
       * play(), so a cold preview was a board of black cards). Hold the deal
       * beat - `busy` is up since startBoard and the "Dealing the board." hint
       * stays - until every distinct remote face reports pool.ready(), capped
       * at PREVIEW_GATE_CAP_MS total so a broken network cannot hang the
       * class. Glyph and placeholder boards gate on nothing and open at once. */
      const gate = previewGate();
      if (!gate) { previewShow(); return; }
      const myBoard = boardNo;
      gate.then(() => run(() => {
        if (dead || ended || belled || boardNo !== myBoard) return;
        previewShow();
      }));
    }

    /** The board's distinct real urls -> one settled-fast promise, or null
     *  when there is nothing to wait on. Never rejects (pool.ready never
     *  does; the race cap resolves regardless). */
    function previewGate() {
      if (!pool || typeof pool.ready !== 'function') return null;
      const need = [];
      for (const u of pairUrls) {
        if (u && !PLACEHOLDER_RE.test(String(u)) && need.indexOf(u) < 0) need.push(u);
      }
      if (!need.length) return null;
      const waits = need.map((u) => {
        try { return pool.ready(u, { timeoutMs: PREVIEW_GATE_URL_MS }); }
        catch (e) { return Promise.resolve(false); }
      });
      const cap = new Promise((res) => { setTimeout(res, scaled(PREVIEW_GATE_CAP_MS)); });
      return Promise.race([Promise.all(waits), cap]);
    }

    function previewShow() {
      /* THE MEMORIZE BEAT IS PER BOARD and it is kept deliberately: it is the
       * whole encoding moment, and it is tier- (and ladder-) dialled, shrinking
       * a notch per cleared board toward tier 4's own floor. It spends bell
       * time; that is priced into the grade's board cost (script.js). */
      facesLocked = true;      // nothing may re-face a board the player is reading
      /* THE FENCE GOES UP. The memorize window is the class's whole mechanic
       * and it is already being attacked on purpose by the poison sub_flash;
       * EMI may pull a face here, never a word. */
      emiPreviewHeld = true; emiHold(true);
      setHint('dv_preview_hint', 'Memorize the board.');
      if (grid) grid.classList.add('scanning');       // the machine shows you
      for (const c of cells) {
        applyMedia(c);
        c.card.classList.add('up');
        if (!touch) playFace(c, true);
      }
      /* On touch the memorize beat plays TOUCH_PLAY_CAP faces at a time and
       * rotates; on desktop every face plays at once, exactly as before. The
       * window is sized to previewMs so EVERY face gets its animated moment
       * before the cards go down (0825). */
      if (touch) playWindowStart(cells.slice(), dials.previewMs);
      /* W3 P1-10: the board coming up is air moving, not a generic sting. */
      tick('whoosh', 0.35);
      /* W3 P0-2: the memorize window is a countdown, so it ticks - one per
       * whole second inside its last third (capped at three), pitch climbing.
       * The whole ladder is scheduled up front off a known window and dies
       * with `previewTicks` at previewDown, or with clearTimers() at the bell. */
      armPreviewTicks(dials.previewMs);
      // THE MEMORIZE-POISON BEAT: exactly at preview end -400ms.
      const poisonAt = Math.max(0, dials.previewMs - TIMING.poisonLeadMs);
      if (dials.subFlash) after(poisonAt, () => poison('preview'));
      after(dials.previewMs, previewDown);
    }

    /** W3 P0-2: the memorize window's own countdown ladder. */
    function armPreviewTicks(ms) {
      previewTicks = true;
      const gate = Math.min(COUNTDOWN_MS, Math.round(ms / 3));
      const notes = Math.floor(gate / 1000);
      for (let k = notes; k >= 1; k--) {
        const step = notes - k;                       // 0 is the first one heard
        after(Math.max(0, ms - k * 1000), () => {
          if (!previewTicks || dead || ended || belled) return;
          tick('clock_tick', Math.min(0.18, 0.1 + 0.04 * step), { pitch: 1 + 0.06 * step });
        });
      }
    }

    function previewDown() {
      previewTicks = false;                           // W3 P0-2: the window is over
      /* W3 P0-13: the board goes face down and the clock starts. The thud is
       * the inverse of the whoosh that opened it. */
      tick('thud', 0.3, { pitch: 1.2 });
      // the faces are going down: the reading is over, so the fence comes down
      emiPreviewHeld = false; emiHold(false);
      if (touch) playWindowStop();                    // the preview's window dies with it
      if (grid) grid.classList.remove('scanning');    // the machine is done showing
      for (const c of cells) {
        c.card.classList.add('flipping');
        playFace(c, false);
      }
      after(TIMING.previewDownMs, () => {
        for (const c of cells) {
          c.card.classList.remove('flipping', 'up');
          c.state = 'down';
        }
        armPlay();
      });
    }

    /** sub_flash, phase-locked (never a dumb cadence), anchored in the well. */
    function poison(where) {
      const r = fireSafe('sub_flash', {
        variant: where === 'preview' ? 'centre' : 'whisper',
        anchor: well,
        sfx: where === 'preview',
        /* VOICE: phase-locked, never a cadence - the preview beat is once per
           board, the mismatch beat once per failed pair (both well over 1400ms). */
        voice: true,
        voiceKey: 'deja-vu-whisper',
      });
      if (r) say('sub_flash poison beat (' + where + ')');
    }

    function armPlay() {
      /* THE TWO CLOCKS. `boardPlayMs` is re-stamped every board (this board's
       * own accuracy window and clear time); `classPlayMs` is stamped ONCE, on
       * the first board, and is the class's total live-play figure. Neither
       * touches `elapsedMs`, which is the bell's and only the bell's. */
      boardPlayMs = elapsedMs;
      if (classPlayMs < 0) classPlayMs = elapsedMs;
      setHint('dv_play_hint', 'Find the pairs.');
      openAmbience();
      /* THE LAB IS DRESSED ONCE A CLASS. casino.start() re-rolls the seeded lab
       * identity (hue, monogram, sweep) off its own stream, so calling it at
       * every board would have re-skinned the room seven times in five minutes
       * and read as seven different classes. The marquee's mount is already
       * self-guarded; the dressing is not. */
      if (casino && !casinoLit) { casinoLit = true; casino.start(); }
      if (trickster) trickster.start();
      const open = () => { busy = false; settled(true); };
      /* FAKE SHUFFLE (House Rules): the pantomime rides the tail of the
       * preview - cards feint trades and land home. Nothing moves, no tell
       * fires, and input stays closed until the theatre leaves the stage. */
      if (trickster) {
        // the return says the pantomime actually played: one note a board, and
        // none at all on the boards where the deck holds the card back
        const feinted = trickster.shuffle(cells, open);
        if (feinted) emiNote('dv.fakeShuffle', { kind: 'tease', n: cells.length });
      } else open();
    }

    /* ==================================================================== *
     * PHASE 1 - the attempt loop
     * ==================================================================== */
    function onTap(i) {
      if (dead || paused || ended || belled) return;      // shell states, not a refusal
      const cell = cells[i];
      // Everything below IS a refused press - a locked or already-turned
      // slide, a third tap while two are up, or a filler seat. One knock.
      if (!cell || cell.state !== 'down') { refused(); return; }
      if (busy || (faceUp.length + flipping) >= 2) { refused(); return; }
      if (cell.pairId < 0) { refused(); return; }         // a filler cell, never dealt a pair

      flipping += 1;
      cell.card.classList.add('flipping');
      tick('blip', 0.35);
      after(reduced ? TIMING.flipReducedMs : TIMING.flipMs, () => {
        flipping = Math.max(0, flipping - 1);
        cell.card.classList.remove('flipping');
        cell.card.classList.add('up');
        cell.state = 'up';
        playFace(cell, true);
        faceUp.push(i);
        revealed.push(i);
        /* CALLED IT: the re-deal lied about one card, and the very next flip
         * went straight to the liar. Flavour bonus only - never composite. */
        if (watchLie >= 0) {
          const called = i === watchLie;
          watchLie = -1;
          if (called) {
            calledLies += 1;
            emiNote('dv.calledTheLie', { kind: 'celebrate', n: calledLies, tile: i, streak: combo });
            setHint('dv_called_it', 'You called the lie.', true);
            tick('streak', 0.7, { pitch: 1.3 });
            rewardBeat(true);
          }
        }
        while (revealed.length > 24) revealed.shift();
        if (faceUp.length >= 2) judge();
      });
    }

    function judge() {
      busy = true;
      attempts += 1;
      /* W3 P0-14: judgeMs is a held breath and it used to be held in silence.
       * A pad under the pause, resolving into the streak or the stamp. */
      tick('pad', 0.2, { pitch: 0.95 });
      const [a, b] = faceUp;
      cells[a].card.classList.add('judge');
      cells[b].card.classList.add('judge');
      after(TIMING.judgeMs, () => {
        cells[a].card.classList.remove('judge');
        cells[b].card.classList.remove('judge');
        if (cells[a].pairId === cells[b].pairId) onMatch(a, b);
        else onMismatch(a, b);
      });
    }

    function onMatch(a, b) {
      matched += 1;
      combo += 1;
      maxCombo = Math.max(maxCombo, combo);
      mismatchStreak = 0;
      /* EMI COLOR: one pair left on the bench = the lean-in. */
      try { if (ctx.mood && unmatchedPairs() === 1) ctx.mood.tense(); } catch (e) { /* noop */ }
      // the same beat, named: the board is down to its guaranteed last pair
      if (unmatchedPairs() === 1) emiNote('dv.lastPair', { kind: 'tension', left: 1, n: matched, streak: combo });
      const pairId = cells[a].pairId;

      /* "tracked through the static": matched within 2 attempts of the pair
       * being displaced by a glitch_swap. The engine celebrates you beating its
       * own trick, so this forces a flourish. */
      let trackedThis = false;
      if (swapAttempt.has(pairId) && (attempts - swapAttempt.get(pairId)) <= 2) {
        trackedThis = true;
        tracked += 1;
        swapAttempt.delete(pairId);
        /* W3 P1-10: the rarest skill this class measures - you followed a card
         * through the static. It lands over the match cue, not under it. */
        after(120, () => tick('chime', 0.45, { pitch: 1.5 }));
        emiNote('dv.trackedThroughStatic', { kind: 'celebrate', n: tracked, tile: pairId, streak: combo });
        setHint('dv_tracked', 'Tracked through the static.', true);
      }

      for (const i of [a, b]) {
        const c = cells[i];
        c.state = 'locked';
        c.card.classList.remove('up');
        c.card.classList.add('locked', 'pulse');
        c.card.disabled = true;
        if (!c.wax) { c.wax = el('span', 'g-dv-wax', '★'); c.holder.appendChild(c.wax); }
        playFace(c, loopPolicy.play);
      }
      rackAdd(pairId);
      try {
        ctx.ceremonies.stamp({ text: t('dv_stamp_match', 'PAIR'), target: cells[b].holder });
      } catch (e) { /* noop */ }
      // pitch ratchet: the shell synthesises the cue, we ride the level up the
      // combo (+1 step per match, capped at the shared 8)
      tick('streak', Math.min(1, 0.4 + 0.07 * Math.min(8, combo)),
        { pitch: 1 + 0.05 * Math.min(8, combo) });      // the chime ladder climbs in pitch
      if (casino) casino.payout(matched);               // a match pays light
      paintMeter();
      paintHud();

      // the last pair is a guaranteed match: the roll only sizes the ceremony
      rewardBeat(trackedThis || matched >= playablePairs());

      after(TIMING.matchLockMs, () => {
        for (const i of [a, b]) cells[i].card.classList.remove('pulse');
        faceUp.length = 0;
        if (matched >= playablePairs()) { win(); return; }
        settled();
      });
    }

    function onMismatch(a, b) {
      combo = 0;
      mismatchStreak += 1;
      /* EMI COLOR: the small >_<, shell-rationed. */
      try { if (ctx.mood) ctx.mood.stumble(); } catch (e) { /* noop */ }
      paintMeter();

      /* the near-miss tease: 'you KNEW that one'. */
      const partner = partnerOf(a);
      // "the last 3 tiles revealed" = the three before THIS attempt's two
      const before = revealed.slice(-5, -2);
      const nearMiss = partner >= 0
        && (before.indexOf(partner) >= 0 || isAdjacent(b, partner, dials.cols, dials.rows));

      const hold = TIMING.mismatchHoldMs * peekHoldMult();
      after(hold, () => {
        for (const i of [a, b]) {
          cells[i].card.classList.add('flipping');
          playFace(cells[i], false);
        }
        // THE SECOND POISON BEAT: the flash lands DURING the flip-back, exactly
        // when you are trying to encode what you just saw.
        if (dials.subFlash) poison('mismatch');
        if (nearMiss) {
          try { ctx.ceremonies.reward('near_miss', { target: cells[b].holder, text: t('dv_near_miss', 'SO CLOSE') }); }
          catch (e) { /* noop */ }
          // the almost: the face you NEEDED haunts the card you picked
          if (casino) casino.almost(cells[b], cells[a]);
          // she watched them see it - fires with the ceremony, once an attempt
          emiNote('dv.nearMissPartner', { kind: 'commiserate', tile: partner, streak: mismatchStreak });
        }
        tick('stamp_bad', 0.2);
        after(reduced ? TIMING.flipReducedMs : TIMING.flipMs, () => {
          for (const i of [a, b]) {
            const c = cells[i];
            c.card.classList.remove('flipping', 'up');
            c.state = 'down';
          }
          faceUp.length = 0;
          if (mismatchStreak >= 3) pressure();
          settled();
        });
      });
    }

    /** Three straight mismatches: offer Cram Assist, and (tier 4) lean on them. */
    function pressure() {
      if (cramEnabled() && peekBtn) {
        peekBtn.classList.add('armed');
        /* W3 P1-10: the assist offering itself. Quiet - it is a door, not a
         * reward, and it costs the class its A. */
        tick('lift', 0.25, { pitch: 0.9 });
        setHint('dv_cram_ready', 'Cram Assist ready. Hold it - it caps this class at A.', true);
      }
      if (dials.burstPressure) {
        // decoration ONLY (fireSafe strips clickability). The dossier's pressure
        // beat survives the input-trust amendment intact.
        fireSafe('flash_burst', { count: 3, alpha: 0.5 });
      }
    }

    /* ==================================================================== *
     * THE SETTLED BOARD - the one window where the board may lie
     * ==================================================================== */
    function settled(first) {
      if (dead || ended || belled) return;
      heat();
      endgameCheck();
      if (first) { busy = false; return; }
      settledWindow += 1;
      // input stays closed across the mutation window: "nothing you are looking
      // at ever moves" is only true if you cannot be looking at anything.
      busy = true;
      after(TIMING.settleMs, mutate);
    }

    function mutate() {
      if (dead || ended || belled) return;
      const open = unmatchedCells();
      const enough = open.length > 2;
      /* DEJA RE-DEAL (House Rules, the native signature) outranks the smaller
       * mutations at its one seeded window - it is a settled-board event like
       * they are, and it consumes the window the same way. */
      if (trickster && trickster.redealDue(settledWindow, open.length)) { runRedeal(); return; }
      // `<=` not `===`: a window the player raced past is not a spent budget,
      // it simply fires at the next settled board. THE BUDGET IS PER BOARD now
      // (a 300s class is many boards); an unspent one dies with its board.
      const swap = swaps.find((s) => !s.done && s.window <= settledWindow);
      if (swap && enough) { runSwap(swap); return; }
      const drift = drifts.find((d) => !d.done && d.window <= settledWindow);
      if (drift && enough) { runDrift(drift); return; }
      garnish();
      busy = false;
    }

    /* ==================================================================== *
     * DEJA RE-DEAL - the machine re-shows the board; one card may be a lie
     * ==================================================================== */
    /** The lie's wardrobe: a face overlay borrowed from another pair. The
     *  card's REAL face and pairId never change; only the shown frame lies. */
    function wearLie(cell, wearPairId) {
      const url = pairUrls[wearPairId];
      const glyph = glyphFor(wearPairId);
      const node = el('div', 'g-dv-lie');
      if (url && !isVideoUrl(url)) {
        const img = el('img');
        img.setAttribute('alt', '');
        /* the same glyph floor the video branch below already takes: a lie that
         * renders as a broken-image icon is a tell, and a very unfair one */
        img.onerror = () => {
          try { img.remove(); } catch (e) { /* noop */ }
          node.textContent = glyph;
          node.classList.add('glyph');
          faceBroke(wearPairId, url);
        };
        img.src = url;
        node.appendChild(img);
      } else {
        // a video face lies as its glyph (no 900ms decoder spin-up for a lie)
        node.textContent = glyph;
        node.classList.add('glyph');
      }
      cell.card.appendChild(node);
      lieNode = node;
    }
    function clearLie() {
      if (lieNode) { try { lieNode.remove(); } catch (e) { /* noop */ } lieNode = null; }
      redealLie = null;
    }

    function runRedeal() {
      if (trickster) trickster.redealFired();
      busy = true;
      redealLie = trickster ? trickster.pickLie(cells) : null;
      try { ctx.ceremonies.stamp({ text: t('dv_redeal_stamp', 'DEJA VU'), tone: 'pink', target: grid }); }
      catch (e) { /* noop */ }
      tick('glitch', 0.5);
      if (grid) grid.classList.add('scanning', 'rewind');
      const showing = [];
      for (const c of cells) {
        if (c.state !== 'down' || c.pairId < 0) continue;
        applyMedia(c);
        if (redealLie && c === redealLie.cell) wearLie(c, redealLie.wearPairId);
        c.card.classList.add('up');
        if (!touch) playFace(c, true);
        showing.push(c);
      }
      /* the re-deal is the preview's beat again: same touch cap, same window */
      if (touch) playWindowStart(showing.slice());
      say('re-deal: showing ' + showing.length + ' cards'
        + (redealLie ? ' (one is a lie)' : ' (truthful)'));
      after(trickster ? trickster.redealShowMs : 1500, () => {
        if (touch) playWindowStop();
        for (const c of showing) { c.card.classList.add('flipping'); playFace(c, false); }
        after(reduced ? TIMING.flipReducedMs : TIMING.flipMs, () => {
          for (const c of showing) c.card.classList.remove('flipping', 'up');
          if (grid) grid.classList.remove('scanning', 'rewind');
          const lied = !!redealLie;
          watchLie = lied ? redealLie.cell.index : -1;
          clearLie();
          /* W3 P1-10: the gift and the lie were the same silence, which is
           * the one thing they must never be - the tell is what the called-it
           * bonus is scored on. */
          if (lied) tick('decoy', 0.3);
          else tick('chime', 0.3, { pitch: 1.2 });
          if (lied) setHint('dv_redeal_hint', 'One of those was a lie.', true);
          else setHint('dv_redeal_gift', 'The machine blinked.', true);
          /* the tensest three seconds in the class, or the one honest gift */
          if (lied) emiNote('dv.redealLied', { kind: 'tension', tile: watchLie, n: settledWindow });
          else emiNote('dv.redealGift', { kind: 'celebrate', n: showing.length, left: Math.floor(showing.length / 2) });
          busy = false;
          endgameCheck();
        });
      });
    }

    /** The plain-share ramp: most settled windows stay quiet, so a mutation lands. */
    function garnish() {
      if (!dials.wash) return;
      const plain = plainShareFor(dials, progress());
      if (roll('garnish') < plain) return;
      const variant = ['pink', 'spiral', 'drain'][Math.floor(roll('washkind') * 3)];
      sustainSafe('wash', { variant });
    }

    /** A cell that may still be moved: unmatched, dealt, face-down. */
    function movable(i) {
      const c = cells[i];
      return !!c && c.state === 'down' && c.pairId >= 0;
    }

    /**
     * The scheduled candidates are tried first; if the player has locked all of
     * them (locked pairs are exempt, forever) the budget must NOT silently
     * evaporate - SYNTHESIS #2 promises one swap per BOARD from tier 2 (it read
     * "per class" when a class was one board). This walk
     * is a pure function of (schedule entry, board state), so it stays
     * deterministic and a retake still replays the same script.
     */
    function fallbackSwapPair(entry) {
      const open = unmatchedCells().filter(movable);
      if (open.length < 2) return null;
      const start = (entry.candidates && entry.candidates[0]) ? entry.candidates[0][0] : 0;
      if (entry.adjacentOnly) {
        for (let n = 0; n < dials.cells; n++) {
          const a = (start + n) % dials.cells;
          if (!movable(a)) continue;
          const ns = neighborsOf(a, dials.cols, dials.rows).filter(movable);
          if (ns.length) return [a, ns[0]];
        }
        return null;      // nothing adjacent is free: a gentle swap has no room
      }
      const i = start % open.length;
      const j = (i + Math.max(1, Math.floor(open.length / 2))) % open.length;
      return i === j ? null : [open[i], open[j]];
    }

    function runSwap(entry) {
      const scheduled = (entry.candidates || []).find(([a, b]) => a !== b && movable(a) && movable(b));
      const legal = scheduled || fallbackSwapPair(entry);
      entry.done = true;
      entry.relocated = !scheduled && !!legal;
      if (!legal) { entry.skipped = 'no legal cells (locked pairs are exempt)'; garnish(); busy = false; return; }
      const [a, b] = legal;
      entry.fired = [a, b];
      entry.adjacent = isAdjacent(a, b, dials.cols, dials.rows);
      busy = true;

      /* THE TELL - 600ms, always, and always audible. */
      const note = el('span', 'g-dv-tellnote', t('dv_swap_tell', 'swap tell'));
      cells[a].card.classList.add('tell');
      cells[b].card.classList.add('tell');
      cells[b].holder.appendChild(note);
      /* W3 P1-10: the TELL. */
      tick('glitch', 0.5);
      setHint('dv_swap_hint', 'The board is moving.', true);
      /* the law says the tell always precedes - she is part of the announcement */
      emiNote('dv.swapTell', { kind: 'tension', tile: a, n: swapsFired + 1,
        left: Math.max(0, dials.swapBudget - swapsFired - 1) });

      after(TIMING.tellMs, () => {
        let swapped = false;
        const doSwap = () => {
          if (swapped) return;
          swapped = true;
          swapCells(a, b);
          swapsFired += 1;
          entry.swappedAt = settledWindow;
          /* W3 P1-10: and the LAND. Two glitches 600ms apart said nothing had
           * happened yet; a body knock says the board settled. */
          tick('thud', 0.3, { pitch: 1.1 });
          paintHud();
        };
        const res = fireSafe('glitch_swap', {
          targets: [cells[a].card, cells[b].card],
          seconds: TIMING.swapMs / 1000,
          onSwap: doSwap,
          sfx: false,                          // our own tick already fired
        });
        /* The engine's onSwap midpoint is the INTENDED trigger, and this is the
         * backstop: the tell already promised the board would move, so the swap
         * must land even if the engine is missing, refused it, got suspended, or
         * was disposed before its own timer ran. doSwap() is idempotent. */
        after(TIMING.swapMs * (res ? 0.6 : 0.45), doSwap);
        after(TIMING.swapMs + 90, () => {
          cells[a].card.classList.remove('tell');
          cells[b].card.classList.remove('tell');
          try { note.remove(); } catch (e) { /* noop */ }
          setHint('dv_play_hint', 'Find the pairs.');
          busy = false;
          endgameCheck();
        });
      });
    }

    /** Trade two cells' contents. The engine never moves a hitbox - we do, and
     *  only while both cards are face-down on a settled board. */
    function swapCells(a, b) {
      const ca = cells[a];
      const cb = cells[b];
      const pid = ca.pairId; ca.pairId = cb.pairId; cb.pairId = pid;
      const fa = ca.face; const fb = cb.face;
      if (fa && fb) {
        ca.card.appendChild(fb);
        cb.card.appendChild(fa);
        ca.face = fb; cb.face = fa;
      } else {
        applyMedia(ca); applyMedia(cb);
      }
      // both displaced pairs are now "trackable" for the bonus
      swapAttempt.set(ca.pairId, attempts);
      swapAttempt.set(cb.pairId, attempts);
    }

    /** The scheduled line, else the next line with room (same deterministic walk
     *  as the swap fallback: a locked-out row must not eat the drift budget). */
    function pickDriftLine(entry) {
      const axes = entry.axis === 'row' ? ['row', 'col'] : ['col', 'row'];
      for (const axis of axes) {
        const count = axis === 'row' ? dials.rows : dials.cols;
        for (let n = 0; n < count; n++) {
          const line = (entry.line + n) % count;
          const open = lineCells(axis, line, dials.cols, dials.rows).filter(movable);
          if (open.length >= 2) return { axis, line, open };
        }
      }
      return null;
    }

    function runDrift(entry) {
      entry.done = true;
      const picked = pickDriftLine(entry);
      if (!picked) { entry.skipped = 'no line has room'; garnish(); busy = false; return; }
      entry.relocated = picked.axis !== entry.axis || picked.line !== entry.line;
      entry.axisFired = picked.axis;
      entry.lineFired = picked.line;
      const line = picked.open;
      entry.fired = line.slice();
      busy = true;

      const sheens = [];
      for (const i of line) {
        const s = el('span', 'g-dv-line-tell');
        cells[i].holder.appendChild(s);
        sheens.push(s);
      }
      tick('wash', 0.4);
      setHint('dv_drift_hint', 'A whole line is sliding.', true);
      const handle = sustainSafe('row_drift', {
        targets: line.map((i) => cells[i].card),
        axis: picked.axis === 'row' ? 'x' : 'y',
        variant: 'slide',
        amplitudeMult: 0.5,
      });

      after(TIMING.driftTellMs, () => {
        if (handle) stopSafe('row_drift');
        rotateLine(line);
        driftsFired += 1;
        tick('glitch', 0.35);
        for (const s of sheens) { try { s.remove(); } catch (e) { /* noop */ } }
        setHint('dv_play_hint', 'Find the pairs.');
        busy = false;
        endgameCheck();
      });
    }

    /** Slide one line by a single cell, wrapping, locked pairs left in place. */
    function rotateLine(line) {
      const ids = line.map((i) => cells[i].pairId);
      const faces = line.map((i) => cells[i].face);
      for (let n = 0; n < line.length; n++) {
        const src = (n + line.length - 1) % line.length;
        const cell = cells[line[n]];
        cell.pairId = ids[src];
        const f = faces[src];
        if (f) { cell.card.appendChild(f); cell.face = f; } else applyMedia(cell);
      }
    }

    /* ==================================================================== *
     * PHASE 2 - endgame, the bell, the ceremony
     * ==================================================================== */
    function endgameCheck() {
      if (dead || ended || belled) return;
      const left = unmatchedCells();
      if (left.length !== 2) return;
      for (const i of left) cells[i].card.classList.add('spot');
      if (!drumrolled) {
        drumrolled = true;
        tick('near_miss', 0.45);
        setHint('dv_endgame', 'Last pair.', true);
        if (casino) casino.bell(true);       // the frame goes gold for the last pair
      }
    }

    function rewardBeat(force) {
      if (!rollReward) return;
      const r = rollReward({ heat: dials.heat, streak: combo, force: !!force });
      if (!r.fire) {
        if (r.nearMiss) { try { ctx.ceremonies.reward('near_miss', { target: grid }); } catch (e) { /* noop */ } }
        return;
      }
      if (r.jackpot) {
        jackpots += 1;
        try { ctx.ceremonies.reward('jackpot', { target: grid, text: t('dv_jackpot', 'JACKPOT') }); } catch (e) { /* noop */ }
        sustainSafe('gif_rain', { clickSafe: true, durationMs: 1400, variant: 'light' });
        tick('jackpot', 0.7);
        return;
      }
      // small flourish, drawn from the wash family (the engine's garnish canon)
      const variant = ['pink', 'spiral', 'drain'][Math.floor(roll('flourish') * 3)];
      sustainSafe('wash', { variant });
    }

    /**
     * A BOARD IS CLEAR. Not the class - the class ends at the bell and nowhere
     * else. Bank the board's numbers, take one short celebration beat, deal the
     * next one. The ambience is NOT stopped here (it is class-level dressing and
     * a gap between boards would read as a fault) and the combo deliberately
     * survives the handover.
     */
    function win() {
      if (dead || ended || belled) return;
      busy = true;                          // the board is over; nothing may land
      emiPreviewHeld = false; emiHold(false);   // no fence survives a board
      boardsCleared += 1;
      const clearSec = Math.max(0, (elapsedMs - boardPlayMs) / 1000);
      boardLog.push({
        board: boardNo, pairs: dials.pairs, attempts, clearSec: Math.round(clearSec * 10) / 10,
      });
      totalAttempts += attempts;
      totalMatched += matched;
      totalSwaps += swapsFired;
      if (casino) { casino.payout(10); casino.bell(false); }
      setHint('dv_clear', 'Board clear.', true);
      try { ctx.ceremonies.stamp({ text: t('dv_stamp_clear', 'CLEAR'), target: grid }); } catch (e) { /* noop */ }
      tick('stamp', 0.8);
      paintHud();
      /* the running score of the class, counted out loud */
      emiNote('dv.boardClear', { kind: 'celebrate', n: boardsCleared, streak: combo,
        left: Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)) });
      say('board ' + boardNo + ' clear in ' + clearSec.toFixed(1) + 's ('
        + attempts + ' attempts) - ' + boardsCleared + ' cleared, '
        + Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)) + 's left');
      /* The next board is dealt on a plain timer, NOT on a "is there time?"
       * test: a board that only half-fits still earns its partial credit, and
       * the bell truncating a fresh deal is a beat this class is built for. */
      after(TIMING.boardClearMs, () => startBoard(boardNo + 1));
    }

    /** The last ten seconds: a three-note drum so the bell is heard coming.
     *  Audio plus one hint line - no CSS, so reduced motion loses nothing and a
     *  0-capped bgIntensity still hears it. */
    function lastCall() {
      if (lastCallDone || dead || ended || belled) return;
      lastCallDone = true;
      setHint('dv_last_call', 'Last ten seconds.', true);
      for (let n = 0; n < LAST_CALL_NOTES; n++) {
        after(n * TIMING.endgameDrumMs, () => tick('near_miss', 0.4 + 0.08 * n, { pitch: 0.9 - 0.06 * n }));
      }
    }

    /**
     * THE BELL. It is the class's normal end and it may fall on ANYTHING - a
     * deal cascade, a preview, a flip mid-air, a swap tell, a re-deal, a clear
     * celebration. So it truncates rather than waits: input is locked first,
     * every pending step is cancelled, the board is left exactly where it stood
     * (its partial progress is worth real credit) and the ceremony is the only
     * timer left alive.
     */
    function bell() {
      if (ended || belled) return;
      belled = true;
      busy = true;                           // 1. input is shut BEFORE anything else
      emiPreviewHeld = false; emiHold(false);  // the bell may fall mid-preview
      stopClock();
      clearTimers();                         // 2. truncate: no step from the live board runs
      if (touch) playWindowStop();
      flipping = 0;
      faceUp.length = 0;
      clearLie();
      watchLie = -1;
      if (grid) grid.classList.remove('scanning', 'rewind', 'jiggle');
      for (const c of cells) { try { c.card.classList.remove('flipping', 'judge', 'tell'); } catch (e) { /* noop */ } }
      if (trickster) { try { trickster.stop(); } catch (e) { /* noop */ } }
      stopAmbience();
      if (casino) casino.dimOut();           // the bell is never silence
      setHint('dv_bell', 'The bell. Class over.', true);
      /* cut off mid-sentence by a school bell - `left` is what the live board
       * still owed, so one pair short of clear reads as one pair short */
      emiNote('dv.bellMidBoard', { kind: 'commiserate', n: boardsCleared,
        left: Math.max(0, playablePairs() - matched) });
      try { ctx.ceremonies.stamp({ text: t('dv_stamp_bell', 'BELL'), tone: 'pink', target: grid }); } catch (e) { /* noop */ }
      /* W3 P0-3: the bell is this class's NORMAL ending (trap 82) and it was
       * playing the loudest error sound in the school. The school's own bell,
       * with the stamp landing after it. */
      tick('bell', 0.5);
      after(420, () => tick('stamp', 0.5));
      after(TIMING.ceremonyMs, () => finish(true));   // 3. the ONE surviving timer
    }

    /** THE ONE endClass. bell() is its only caller now - a board clear deals the
     *  next board instead of ending the class - and `ended` makes it idempotent. */
    function finish(timeout) {
      if (ended) return;
      ended = true;
      emiPreviewHeld = false; emiHold(false);   // the class end always releases
      stopClock();
      stopAmbience();
      if (trickster) trickster.stop();
      if (casino) casino.stop();
      busy = true;
      /* Total live play (deals and previews included), against the CLASS clock.
       * `classPlayMs` is -1 if the bell somehow beat the first armPlay, in which
       * case the whole class was its opening and elapsedMs is the honest read. */
      const elapsedSec = Math.max(0.001, (elapsedMs - Math.max(0, classPlayMs)) / 1000);
      const livePairs = playablePairs();
      const scoreIn = {
        tier: baseDials ? baseDials.tier : dials.tier,
        /* THE CLASS TOTALS: banked boards plus the live board. The live board's
         * own two numbers ride separately so its partial can be priced once. */
        matched: totalMatched + matched,
        attempts: totalAttempts + attempts,
        boardsCleared,
        livePairs,
        liveMatched: matched,
        expectedClears: expectClears,
        maxCombo,
        tracked,
        timeout: !!timeout,
      };
      const score = compositeFor(scoreIn);
      // called lies ride the flavour channel (game-owned; never composite)
      const flavorXp = flavorXpFor(tracked) + calledLies * DV_TRICKSTER.CALLED_IT_XP;
      /* The dossier's SECOND A-cap: a mismatch hold above 1.25x is a timing
       * assist (longer looking = easier memorizing), so it rides the shared
       * rubric's `tempo_assist` reason. The shell owns the cap itself - a game
       * reports the assist, it never grades. (The first A-cap, Cram Assist, is
       * shell-detected through ctx.peek; the third, a below-par board, is
       * shell-computed from the manifest.) */
      const assists = {};
      if (peekHoldMult() > 1.25) assists.tempo_assist = true;

      const report = {
        metrics: { composite: score.composite },
        flavorXp,
        assists,
        /* Designed-not-built in v1 (DECISIONS #6): the shell's ONE share
         * pipeline ignores every payload but Daily Trigger's. The retake flag
         * rides here because the board is globally identical, so only a
         * retake-marked card is comparable. */
        share: {
          game: 'deja_vu',
          retake,
          grade_inputs: {
            tier: scoreIn.tier,
            pairs: baseDials ? baseDials.pairs : dials.pairs,
            boardsCleared,
            expectedClears: expectClears,
            livePairs, liveMatched: matched,
            matched: scoreIn.matched, attempts: scoreIn.attempts,
            playTimeSec: Math.round(elapsedSec), maxCombo, tracked, calledLies,
            swapsFired: totalSwaps + swapsFired,   // CLASS total, not the live board's
            driftsFired, bubblesPopped, jackpots,
            boards: boardLog.slice(),
            timeout: !!timeout,
            peekHold: peekHoldMult(),
          },
        },
      };
      lastReport = report;
      try { lastSnapshot = instance.snapshot(); } catch (e) { /* diagnostics only */ }
      say('class over: ' + boardsCleared + '/' + expectClears + ' boards'
        + (livePairs ? ' + ' + matched + '/' + livePairs + ' on the live one' : '')
        + ', ' + scoreIn.matched + ' pairs in ' + scoreIn.attempts + ' attempts ('
        + (score.accuracy * 100).toFixed(0) + '%, par ' + (score.accuracyScore * 100).toFixed(0) + '%)'
        + ', ' + Math.round(elapsedSec) + 's live, combo ' + maxCombo
        + ', tracked ' + tracked + (calledLies ? ', called ' + calledLies + ' lies' : '')
        + (timeout ? ', BELL' : '') + ' -> composite ' + score.composite.toFixed(3));
      try {
        ctx.endClass(report);
      } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
    }

    /* ==================================================================== *
     * AMBIENCE (the tier dials that are not mutations)
     * ==================================================================== */
    /**
     * Called at every board's armPlay, not once a class.
     *
     * THE RESTART RULE: `engine/sustained.js` answers a repeat sustain of a live
     * kind with the SAME handle and a retune - it does not read the new options.
     * The escalation ladder can raise crt / bubbles / ambient between boards, so
     * when (and only when) that signature actually moves we ask for a restart.
     * Every other board re-uses what is already lit, which is what keeps the
     * room continuous instead of blinking off and on at every handover.
     */
    function openAmbience() {
      heat();
      const key = [dials.ambient, dials.crt, dials.bubbles, dials.tier >= 4 ? 'chroma' : 'scanline'].join('|');
      const restart = ambienceKey !== '' && ambienceKey !== key;
      ambienceKey = key;
      if (dials.ambient) sustainSafe('ambient_field', { kind: 'motes', density: dials.ambient, restart });
      if (dials.crt) sustainSafe('crt', { level: dials.crt, variant: dials.tier >= 4 ? 'chroma' : 'scanline', restart });
      if (dials.bubbles) {
        /* The dossier's poppable decoys: popping one costs ~300ms of jiggle and
         * NEVER counts as a flip or touches the grade (the rubric already prices
         * the time). The engine arms its own escape guard on a clickable field. */
        sustainSafe('bubble_field', {
          max: dials.bubbles,
          variant: 'drift',
          restart,
          onPop: () => {
            bubblesPopped += 1;
            /* ONE note a board, not one a pop: a storm of decoys is still a
             * single act of procrastination as far as she is concerned. */
            if (!emiPopNoted) {
              emiPopNoted = true;
              emiNote('dv.bubblePop', { kind: 'tease', n: bubblesPopped });
            }
            tick('bubble_pop', 0.4);
            if (grid) {
              grid.classList.add('jiggle');
              after(300, () => grid && grid.classList.remove('jiggle'));
            }
          },
          onForceComplete: () => say('bubble field cleared by the escape guard'),
        });
      }
    }
    function stopAmbience() {
      for (const k of ['wash', 'bubble_field', 'ambient_field', 'crt', 'gif_rain', 'row_drift']) stopSafe(k);
    }

    /* ==================================================================== *
     * CRAM ASSIST - the shared peek verb, skinned (SYNTHESIS #6)
     * ==================================================================== */
    function cramEnabled() {
      const v = ctx.settings ? ctx.settings.dv_cram_assist : true;
      return v == null ? true : !!v;
    }
    function peekHoldMult() {
      const v = Number(ctx.settings ? ctx.settings.dv_peek_hold : 1);
      if (!Number.isFinite(v)) return 1;
      return Math.max(0.5, Math.min(2, v));
    }
    function wireCram() {
      if (!cramEnabled() || !peekBtn) return;
      let autoRelease = 0;
      try {
        ctx.peek.setHandlers({
          onReveal: () => {
            for (const c of cells) {
              if (c.state !== 'down' || c.pairId < 0) continue;
              applyMedia(c);
              c.card.classList.add('ghost');
            }
            setHint('dv_cram_on', 'Cramming.', true);
            // the dossier's 800ms window (peek's own 4s ceiling is the backstop)
            autoRelease = after(TIMING.cramRevealMs, () => { try { ctx.peek.release(); } catch (e) { /* noop */ } });
          },
          onHide: () => {
            if (autoRelease) { clearTimeout(autoRelease); timers.delete(autoRelease); autoRelease = 0; }
            for (const c of cells) c.card.classList.remove('ghost');
            setHint('dv_play_hint', 'Find the pairs.');
          },
          onFirstUse: () => {
            say('cram assist used - the shell caps this class at A');
            if (peekBtn) peekBtn.classList.remove('armed');
          },
        });
        ctx.peek.attach(peekBtn);
        ctx.peek.bindKeys(ctx.keys, 'peek');
      } catch (e) { say('cram assist wiring failed (class unaffected): ' + ((e && e.message) || e)); }
    }

    /* ==================================================================== *
     * SMALL HELPERS
     * ==================================================================== */
    function unmatchedCells() {
      const out = [];
      for (const c of cells) if (c.state !== 'locked' && c.pairId >= 0) out.push(c.index);
      return out;
    }
    function unmatchedPairs() { return Math.floor(unmatchedCells().length / 2); }
    function playablePairs() {
      const ids = new Set();
      for (const c of cells) if (c.pairId >= 0) ids.add(c.pairId);
      return Math.max(1, ids.size);
    }
    function partnerOf(i) {
      const pid = cells[i] ? cells[i].pairId : -1;
      if (pid < 0) return -1;
      for (const c of cells) if (c.index !== i && c.pairId === pid) return c.index;
      return -1;
    }

    /* ---- clock ---------------------------------------------------------- */
    function startClock() {
      lastTick = Date.now();
      clockId = setInterval(() => {
        if (dead || ended) return;
        const now = Date.now();
        const dt = now - lastTick;
        lastTick = now;
        if (paused) return;
        elapsedMs += dt / Math.max(0.0001, getTimeScale());
        paintHud();
        // the drum first: it must beat BEFORE the bell it is announcing
        if (!lastCallDone && (budgetMs - elapsedMs) <= TIMING.lastCallMs) run(lastCall);
        if (elapsedMs >= budgetMs) { stopClock(); run(bell); }
      }, scaled(250));
    }
    function stopClock() { if (clockId) { clearInterval(clockId); clockId = 0; } }

    /* ---- assets --------------------------------------------------------- */
    /**
     * ONE CLAIM A CLASS, many boards off it. The class draws up to
     * FACE_POOL_WANT distinct urls into `facePool` and every board shuffles that
     * pool for its own subset (dealFaces). Faces MAY repeat across a long class
     * - the pool is 16 and a tier-1 night is seven 6-pair boards - but WHICH six
     * and where they sit is a fresh seeded draw every time.
     *
     * PAIRS MUST STAY LEGIBLE. Two pairs wearing the same art is an unwinnable
     * board, so the pool holds only DISTINCT urls and a board that runs short
     * plays the leftovers on their glyph faces (always distinct) rather than
     * repeating someone else's clip.
     */
    /** Draw distinct loop urls into facePool. `clean` additionally refuses the
     *  provider's bundled placeholder svgs - the RE-draw path must not refill
     *  with the very floor it is replacing. The first fill keeps the original
     *  accept-anything semantics (a placeholder pool still deals a board). */
    function fillFacePool(clean) {
      if (!pool || typeof pool.next !== 'function') return;
      for (let n = 0; n < FACE_POOL_WANT * 3 && facePool.length < FACE_POOL_WANT; n++) {
        const got = pool.next('loop');
        const u = got && got.url;
        if (!u) continue;
        if (clean && PLACEHOLDER_RE.test(String(u))) continue;
        if (facePool.indexOf(u) < 0) facePool.push(u);
      }
    }

    /** The face pool was built cold: short, or wearing the placeholder floor. */
    function poolDegraded() {
      if (facePool.length < FACE_POOL_WANT) return true;
      for (const u of facePool) if (!u || PLACEHOLDER_RE.test(String(u))) return true;
      return false;
    }

    /** pool.onUpdate landed: a fresh media batch exists (0825). DEGRADED PATH
     *  ONLY - a clean, full facePool returns before ANY rng is consumed, so
     *  the clean path's draw sequence never moves (house law: extra next()
     *  calls are permitted only where the state already serves degraded
     *  results). Contents of facePool are unseeded; the board's seeded shuffle
     *  in dealFaces is untouched, so determinism is safe. */
    function refaceFromUpdate() {
      if (dead || ended || belled || !pool) return;
      if (!poolDegraded()) return;
      facePool = facePool.filter((u) => u && !PLACEHOLDER_RE.test(String(u)));
      const before = facePool.length;
      fillFacePool(true);
      if (facePool.length === before) return;         // the batch had nothing new
      say('media batch landed - face pool now ' + facePool.length + ' distinct');
      /* re-issue the class warm list; dealFaces below narrows it again */
      try {
        if (typeof pool.warmManifest === 'function' && facePool.length) {
          pool.warmManifest(facePool.map((u) => ({ url: u })));
        }
      } catch (e) { /* noop */ }
      /* the same landing law as the first claim: a board the player has
       * memorised or started is never re-faced - the NEXT board gets it. */
      if (facesLocked || faceUp.length || matched > 0) {
        facesDirty = true;
        return;
      }
      dealFaces(boardNo || 1);
      for (const c of cells) applyMedia(c);
    }

    function claimAssets() {
      // NEVER block a draw: the board is already dealing on glyph faces and the
      // urls drop in when the pool resolves (empty remote -> local, silently).
      Promise.resolve()
        .then(() => ctx.assets.claim({
          loops: FACE_POOL_WANT, stills: 4, targets: 0, canvasSafe: false,
        }))
        .then((p) => {
          if (dead || !p || typeof p.next !== 'function') return;
          pool = p;
          fillFacePool();
          say('asset pool ready (' + facePool.length + ' distinct loops; a board wears '
            + (dials ? dials.pairs : '?') + ')');
          /* THE CLASS WARM LIST (0825): facePool is the class's whole media
           * need, so the how-to sheet becomes the idle warm window. Each
           * board's dealFaces re-heads the manifest with its own subset. */
          if (typeof p.warmManifest === 'function' && facePool.length) {
            try { p.warmManifest(facePool.map((u) => ({ url: u }))); } catch (e) { /* noop */ }
          }
          /* THE REVIVED RE-FACE PATH (0825): remote media streams in after the
           * claim resolves; the old guard below ran once inside this .then()
           * and never re-fired. The subscription dies with pool.release(). */
          if (typeof p.onUpdate === 'function') {
            try { p.onUpdate(() => run(refaceFromUpdate)); } catch (e) { /* optional seam */ }
          }
          /* THE LATE POOL. If the player is already reading a preview or a live
           * board, re-facing it would be a lie the tell system never promised -
           * so the draw is banked and the NEXT board is the first with media.
           * Only a board still dealing (nothing memorised yet) is re-faced in
           * place, which is the old behaviour where it was honest. */
          if (facesLocked || faceUp.length || matched > 0) {
            facesDirty = true;
            say('asset pool landed mid-board - the next board is the first with faces');
            return;
          }
          dealFaces(boardNo || 1);
          for (const c of cells) applyMedia(c);
        })
        .catch((e) => say('asset claim failed - glyph faces stand: ' + ((e && e.message) || e)));
    }

    /* ---- the House tricksters, minted per BOARD -------------------------- *
     * One factory so board 1 and board 7 are wired identically. `tier` is the
     * LADDER's deckTier, not the player's raw tier (see startBoard).
     * ---------------------------------------------------------------------- */
    function makeTrickster(seed, tier) {
      const capsOk = !(ctx.caps && Number(ctx.caps.bgIntensity) === 0);
      return createDvTrickster({
        seed, tier,
        timers: deckTimers,
        reduced, capsOk,
        cue: tick,                     // THE CUE ROAD - clamped, never capsOk-gated
        isHalted: () => dead || paused || ended || belled,
        stats: () => ({
          swaps: swapsFired,
          budget: dials.swapBudget,
          secLeft: Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)),
        }),
        chipEl: (which) => (which === 'swaps' ? swapChip : clockChip),
        chipText: (which) => (which === 'swaps' ? swapChipText() : clockText()),
        log: say,
      });
    }

    /* ---- the class rules sheet (Deck VI, Law IV: drawn, not told) --------- */
    /**
     * FOUR vignettes in this lab's own language: the pair turning face-up and
     * locking, the settled board trading two slides AFTER its shudder (law 2 +
     * law 3 of this file, drawn rather than told), the whole board re-dealing
     * without losing a pair, and THE RUN - a clear followed by a fresh board,
     * which is the class-length rule drawn instead of announced. Every figure
     * is CSS on the same slide chrome the board uses, so the sheet costs no
     * media and the fourth row added no CSS at all.
     */
    let howtoEl = null;

    /** Tiers this player has already had the rules sheet for (persisted). */
    function howtoSeenTiers() {
      try {
        const m = (ctx.store && typeof ctx.store.gameMeta === 'function')
          ? (ctx.store.gameMeta('deja_vu') || {}) : {};
        return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
      } catch (e) { return []; }
    }

    function hideHowto() {
      if (howtoEl) { try { howtoEl.remove(); } catch (e) { /* noop */ } }
      howtoEl = null;
    }

    function buildHowto(onGo) {
      const sheet = el('div', 'g-dv-howto');
      sheet.appendChild(el('h2', 'g-dv-hw-title', t('dv_howto_title', 'Class rules')));

      const row = (build, caption) => {
        const r = el('div', 'g-dv-hw-row');
        const fig = el('span', 'g-dv-hw-fig');
        fig.setAttribute('aria-hidden', 'true');
        try { build(fig); } catch (e) { /* a caption alone still teaches */ }
        r.appendChild(fig);
        r.appendChild(el('p', 'g-dv-hw-cap', caption));
        sheet.appendChild(r);
        return r;
      };

      const slide = (cls, glyph) => {
        const card = el('span', 'g-dv-hw-card' + (cls ? ' ' + cls : ''));
        card.appendChild(el('span', 'g-dv-hw-back'));
        card.appendChild(el('span', 'g-dv-hw-face', glyph == null ? '' : glyph));
        return card;
      };

      /* 1 - THE PAIR. Two slides turn over, match, and stay lit. */
      row((fig) => {
        const line = el('span', 'g-dv-hw-line');
        line.appendChild(slide('turn a', GLYPHS[0]));
        line.appendChild(slide('turn b', GLYPHS[0]));
        fig.appendChild(line);
        fig.appendChild(el('span', 'g-dv-hw-lock'));
      }, t('dv_howto_flip', 'Turn two slides. A matching pair stays lit. Anything else turns back over.'));

      /* 2 - THE SWAP. It shudders BEFORE it moves, and only while the board is
         settled - the tell always precedes (law 2). */
      row((fig) => {
        const line = el('span', 'g-dv-hw-line');
        line.appendChild(slide('down'));
        line.appendChild(slide('down tell left'));
        line.appendChild(slide('down tell right'));
        line.appendChild(slide('down'));
        fig.appendChild(line);
      }, t('dv_howto_swap', 'The board only moves while nothing is face up, and it always shudders first.'));

      /* 3 - THE RE-DEAL. The whole board goes down and comes back; the pairs
         are the same pairs, only the seats changed. */
      row((fig) => {
        const grid6 = el('span', 'g-dv-hw-grid');
        for (let i = 0; i < 6; i++) {
          const c = slide('down redeal');
          c.style.setProperty('--dv-hw-i', String(i));
          grid6.appendChild(c);
        }
        grid6.appendChild(el('span', 'g-dv-hw-sweep'));
        fig.appendChild(grid6);
      }, t('dv_howto_redeal', 'Sometimes the whole board re-deals. Same pairs - only the seats change.'));

      /* 4 - THE RUN. A pair locks, and behind it a fresh board drops in. Built
         entirely from the chrome the three rows above already use (turn + lock
         + redeal), so the class length is DRAWN like everything else on this
         sheet and style.js grows nothing. */
      row((fig) => {
        const line = el('span', 'g-dv-hw-line');
        line.appendChild(slide('turn a', GLYPHS[0]));
        line.appendChild(slide('turn b', GLYPHS[0]));
        fig.appendChild(line);
        fig.appendChild(el('span', 'g-dv-hw-lock'));
        const fresh = el('span', 'g-dv-hw-grid');
        for (let i = 0; i < 6; i++) {
          const c = slide('down redeal');
          c.style.setProperty('--dv-hw-i', String(i));
          fresh.appendChild(c);
        }
        fig.appendChild(fresh);
      }, t('dv_howto_boards', 'Clear the board and a fresh one deals. The bell ends class.'));

      const go = el('button', 'g-dv-hw-go', t('dv_howto_go', 'Deal the board'));
      go.type = 'button';
      go.addEventListener('click', () => {
        // THE START PRESS. This one button dismisses the sheet AND deals the
        // board, so it wears the school's start cue, not a page-turn slide -
        // the sheet is a single page and has no turns.
        tick('lift', 0.5);
        try { onGo(); } catch (e) { say('howto go: ' + ((e && e.message) || e)); }
      });
      sheet.appendChild(go);
      try { if (typeof go.focus === 'function') go.focus(); } catch (e) { /* noop */ }
      return sheet;
    }

    /**
     * THE LAW, uniform across every open class (owner ruling 2026-08-24): the
     * sheet SHOWS the first time this player meets the lab at this grade tier
     * and AUTO-SKIPS every later class at that tier, whatever the setting says;
     * the skip means "skip even the first showing" (owner ruling 2026-08-24).
     * No meta = no memory = the sheet shows. Dismissal is its own button and nothing
     * else - Cram Assist is bound to a key the moment the board deals, and a
     * key shortcut here would spend the player's A-cap on a tutorial.
     */
    function howto(onDone) {
      if (ctx.hideTutorial === true || howtoSeenTiers().indexOf(dials ? dials.tier : 1) >= 0) {
        onDone(); return;
      }
      if (!stage) { onDone(); return; }
      const tierNow = dials ? dials.tier : 1;
      let done = false;
      let sheet = null;
      try {
        sheet = buildHowto(() => {
          if (done || dead || ended) return;
          done = true;
          try {
            const seen = howtoSeenTiers();
            if (seen.indexOf(tierNow) < 0) {
              seen.push(tierNow);
              if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
                ctx.store.mergeGameMeta('deja_vu', { howtoTiers: seen });
              }
            }
          } catch (e) { /* best effort - the sheet just shows again next time */ }
          hideHowto();
          onDone();
        });
      } catch (e) { say('rules sheet refused: ' + ((e && e.message) || e)); sheet = null; }
      if (!sheet) { onDone(); return; }
      howtoEl = sheet;
      stage.appendChild(sheet);
    }

    /* ==================================================================== *
     * THE MODULE INSTANCE
     * ==================================================================== */
    const instance = {
      start(classSpec) {
        spec = classSpec || { gradeTier: 1, seed: 'deja_vu|none', timeBudgetSec: 300 };
        const tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        const seed = String(spec.seed == null ? 'deja_vu' : spec.seed);
        classSeed = seed;
        classTier = tier;
        budgetMs = Math.max(20000, (Number(spec.timeBudgetSec) || 300) * 1000);
        reduced = probeReduced();
        posterOnly = reduced;
        /* Coarse touch: the local probe OR the host's own flag (the web shim
         * populates ctx.platform.isTouch; the desktop host answers false). */
        touch = probeTouch() || !!(ctx.platform && ctx.platform.isTouch);

        /* THE BOARD SIZE is the player's for the WHOLE class - the escalation
         * ladder never touches the grid, so a below-par choice is still exactly
         * one A-cap the shell computes from the manifest, not a per-board one. */
        const chosen = ctx.settings ? ctx.settings.boardSize : null;
        const pairs = Number(chosen);
        chosenPairs = Number.isFinite(pairs) ? pairs : null;
        /* Board 1's dials are also the GRADE's reference: `expectedClears` is
         * computed off them once, before the ladder has moved anything, because
         * S_PACE is already the allowance for the ladder making later boards
         * dearer (script.js, THE ARITHMETIC). */
        baseDials = dialsForBoard(tier, 0, { pairs: chosenPairs });
        dials = baseDials;
        expectClears = expectedClears(baseDials, budgetMs / 1000);
        layout = buildLayout(boardSeed(1), dials);
        swaps = buildSwapSchedule(boardSeed(1), dials);
        drifts = buildDriftSchedule(boardSeed(1), dials, swaps);
        /* Class-level streams: the variable-ratio reward and the garnish rolls
         * are the CLASS's, not a board's, so a long run keeps one honest
         * schedule instead of restarting its luck every board. */
        roll = makeTaggedRoll(seed + '|dv|play');
        rollReward = createReward(seed);
        loopPolicy = matchedLoopPolicy(ctx.settings && ctx.settings.dv_matched_loops, tier, posterOnly);
        if (loopPolicy.reason === 'ceiling') {
          say('dv_matched_loops: "keep-playing" refused by the quality/tier ceiling - frozen');
        }
        /* THE TOUCH SETTLE: matchedLoopPolicy's semantics are untouched, but a
         * phone cannot afford matched pairs looping forever - by the end of a
         * board that is `pairs` extra live decoders under the play cap. Same
         * "a knob may use less, never more" direction the ceiling rule takes. */
        if (touch && loopPolicy.play) {
          loopPolicy = { play: false, reason: 'touch' };
          say('dv_matched_loops: matched pairs settle to poster on touch (playing-video cap)');
        }

        /* retake detection: the same seed already played today replays the
         * IDENTICAL script (same layout, same swap schedule) and is flagged. */
        try {
          const meta = ctx.store.gameMeta('deja_vu') || {};
          retake = meta.lastSeed === seed;
          ctx.store.mergeGameMeta('deja_vu', { lastSeed: seed, lastPlayedAt: Date.now() });
        } catch (e) { retake = false; }

        injectDejaVuStyle();
        buildDom();

        /* The House decks (House Rules floor map: the memory lies + the
         * lighting rig). Both disarm on a 0-capped bgIntensity; both replay
         * identically on a retake (same seed, same streams).
         *
         * TWO LIFETIMES, deliberately. The CASINO is the room - one lab
         * identity, one marquee, lit once and never re-dressed, because a class
         * that changed its own hue every board would read as a different class.
         * The TRICKSTER is the deal, and startBoard mints a fresh one per board
         * (see makeTrickster) so its once-a-class cards land once a BOARD. */
        const capsOk = !(ctx.caps && Number(ctx.caps.bgIntensity) === 0);
        casino = createDvCasino({
          seed, stage, bench, grid,
          timers: deckTimers,
          reduced, capsOk,
          cue: tick,                     // THE CUE ROAD - clamped, never capsOk-gated
          log: say,
        });

        claimAssets();

        /* THE SHEET FIRST (Deck VI). Nothing that measures the player runs
           until GO: the clock is not ticking, the board has not dealt and Cram
           Assist is not bound to a key, so a class read at leisure grades
           exactly like one that skipped the sheet. */
        howto(() => {
          if (dead || ended || belled) return;
          wireCram();
          startClock();                  // THE bell clock: one per class, never reset
          startBoard(1);
        });

        liveClass = instance;
        lastReport = null;
        lastSnapshot = null;
        say('tier ' + tier + ': ' + Math.round(budgetMs / 1000) + 's, ' + dials.cols + 'x'
          + dials.rows + ' (' + dials.pairs + ' pairs) a board, ~'
          + boardCostSec(baseDials).toFixed(0) + 's a board -> S at ' + expectClears
          + ' cleared' + (retake ? ', RETAKE' : ''));
      },

      pause() {
        if (paused) return;
        paused = true;
        // a frozen class may sit here forever - the fence must not sit with it
        emiHold(false);
        // the lab holds its breath: every CSS animation (sweep, beam, shudder)
        // freezes in place via animation-play-state
        if (stage) stage.classList.add('suspended');
        for (const c of cells) playFace(c, false);
      },

      resume() {
        if (!paused) return;
        paused = false;
        // the preview the pause interrupted is still the preview: re-fence it
        if (emiPreviewHeld && !dead && !ended && !belled) emiHold(true);
        if (stage) stage.classList.remove('suspended');
        lastTick = Date.now();
        for (const c of cells) if (c.state === 'up' || (c.state === 'locked' && loopPolicy.play)) playFace(c, true);
        const q = deferred.splice(0);
        for (const fn of q) run(fn);
      },

      /** The shell owns the overlay and the engine's suspend; we just freeze. */
      suspend(on) {
        if (on) instance.pause(); else instance.resume();
      },

      destroy() {
        dead = true;
        emiPreviewHeld = false; emiHold(false);   // teardown always releases
        hideHowto();
        if (touch) playWindowStop();
        stopClock();
        clearTimers();
        stopAmbience();
        try { if (trickster) trickster.destroy(); } catch (e) { /* noop */ }
        trickster = null;
        try { if (casino) casino.destroy(); } catch (e) { /* noop */ }
        casino = null;
        clearLie();
        for (const c of cells) playFace(c, false);
        if (pool && typeof pool.release === 'function') { try { pool.release(); } catch (e) { /* noop */ } }
        pool = null;
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
        cells = [];
        if (liveClass === instance) liveClass = null;
      },

      /* -------- diagnostics seam (harness + future debug overlay) -------- */
      snapshot() {
        return {
          tier: dials ? dials.tier : null,
          dials: dials ? Object.assign({}, dials) : null,
          baseDials: baseDials ? Object.assign({}, baseDials) : null,
          seed: spec ? spec.seed : null,
          boardSeed: boardNo ? boardSeed(boardNo) : null,
          boardNo, boardsCleared, expectClears, belled, lastCallDone,
          boards: boardLog.slice(),
          totalAttempts, totalMatched, totalSwaps, casinoLit,
          facePool: facePool.slice(),
          facesDirty, facesLocked,
          retake,
          cols: dials ? dials.cols : 0,
          rows: dials ? dials.rows : 0,
          slots: cells.map((c) => c.pairId),
          states: cells.map((c) => c.state),
          layoutSlots: layout ? layout.slots.slice() : [],
          swaps: swaps.map((s) => ({
            window: s.window, adjacentOnly: s.adjacentOnly, candidates: s.candidates,
            done: !!s.done, fired: s.fired || null, adjacent: s.adjacent === true,
            relocated: !!s.relocated, skipped: s.skipped || null,
          })),
          drifts: drifts.map((d) => ({
            window: d.window, axis: d.axis, line: d.line, done: !!d.done,
            axisFired: d.axisFired || null, lineFired: d.lineFired == null ? null : d.lineFired,
            relocated: !!d.relocated, fired: d.fired || null, skipped: d.skipped || null,
          })),
          attempts, matched, combo, maxCombo, mismatchStreak, tracked,
          swapsFired, driftsFired, bubblesPopped, jackpots, settledWindow,
          calledLies, watchLie,
          trickster: trickster ? trickster.diagnostics() : null,
          casino: casino ? casino.diagnostics() : null,
          faceUp: faceUp.slice(),
          revealed: revealed.slice(),
          elapsedMs, budgetMs, classPlayMs, boardPlayMs, ended, busy, paused,
          loopPolicy: Object.assign({}, loopPolicy),
          posterOnly, reduced, touch,
          pairUrls: pairUrls.slice(),
          cellsDom: cells.map((c) => c.card),
          well,
          grid,
          peekBtn,
        };
      },
    };
    return instance;
  },

  /** The live class's state, or null. Never read by the shell. */
  diagnostics() { return liveClass ? liveClass.snapshot() : null; },

  /** The last report handed to endClass (survives teardown). Diagnostics only. */
  get lastReport() { return lastReport; },

  /** The final board state of the last class (survives teardown). Diagnostics only. */
  get lastSnapshot() { return lastSnapshot; },

  /** Test seam: the scratch harness compresses the clock. Production = 1. */
  setTimeScale,
};
