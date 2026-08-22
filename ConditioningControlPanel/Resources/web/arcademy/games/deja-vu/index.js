/* ============================================================================
 * games/deja-vu/index.js - DEJA VU (memory / pairs; family: memory).
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
  dialsFor, buildLayout, buildSwapSchedule, buildDriftSchedule,
  neighborsOf, isAdjacent, lineCells, plainShareFor, heatFor,
  matchedLoopPolicy, compositeFor, flavorXpFor, createReward,
  BOARD_SIZES, BOARD_PAR,
} from './script.js';
import { createDvCasino } from './casino.js';
import { createDvTrickster, DV_TRICKSTER } from './trickster.js';
import { makeTaggedRoll } from '../../core/rng.js';

/** Distinct glyphs so a class is fully playable with ZERO media (the floor
 *  under the poster-frame-only floor). Deliberately font-safe, no emoji. */
const GLYPHS = ['◆', '●', '▲', '■', '✦', '◇', '○', '△', '□', '✥', '✲', '✹'];

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

export default {
  key: 'deja_vu',
  family: 'memory',
  meaty: false,
  flagship: false,
  timeBudgetSec: 90,
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

    /* ---- lifecycle flags ------------------------------------------------- */
    let dead = false;
    let paused = false;
    let ended = false;
    let busy = true;            // input is closed until the preview is over
    const timers = new Set();
    const deferred = [];

    /* ---- class state ---------------------------------------------------- */
    let spec = null;
    let dials = null;
    let layout = null;
    let swaps = [];
    let drifts = [];
    let cells = [];
    let roll = null;
    let rollReward = null;
    let pool = null;
    let pairUrls = [];
    let reduced = false;
    let posterOnly = false;
    let loopPolicy = { play: true, reason: 'auto' };
    let retake = false;

    let casino = null;                  // House Rules Deck II (marquee / almost / ken-burns)
    let trickster = null;               // House Rules Deck III (shuffle / re-deal / flicker)
    let redealLie = null;               // {cell, wearPairId} during the re-deal show
    let lieNode = null;                 // the lie's overlay element
    let watchLie = -1;                  // cell index: flip the liar NEXT and it pays
    let calledLies = 0;

    let attempts = 0;
    let matched = 0;
    let combo = 0;
    let maxCombo = 0;
    let mismatchStreak = 0;
    let tracked = 0;
    let swapsFired = 0;
    let driftsFired = 0;
    let bubblesPopped = 0;
    let settledWindow = 0;
    let jackpots = 0;
    const faceUp = [];
    let flipping = 0;                   // cards mid-flip (a third tap must not land)
    const revealed = [];                // every reveal, in order (near-miss window)
    const swapAttempt = new Map();      // pairId -> attempts count when it moved
    let drumrolled = false;

    /* ---- clock ---------------------------------------------------------- */
    let clockId = 0;
    let lastTick = 0;
    let elapsedMs = 0;
    let playStartedMs = 0;              // measured AFTER the preview (clear time)
    let budgetMs = 90000;

    /* ---- dom ------------------------------------------------------------ */
    let stage = null; let grid = null; let well = null; let hint = null; let bench = null;
    let meterWrap = null; let swapChip = null; let clockChip = null;
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
    /** A cue that must be heard even when every visual is degraded. */
    function tick(name, level, extra) {
      fireSafe('audio_trigger', Object.assign({ name, level: level == null ? 0.45 : level }, extra || {}));
    }
    function progress() {
      if (!dials) return 0;
      const byPairs = matched / Math.max(1, dials.pairs);
      const byTime = budgetMs > 0 ? elapsedMs / budgetMs : 0;
      return Math.max(0, Math.min(1, Math.max(byPairs, byTime)));
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
      if (dials.swapBudget > 0) {
        swapChip = el('span', 'chip num', swapChipText());
        hud.appendChild(swapChip);
      }
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

      cells = [];
      for (let i = 0; i < dials.cells; i++) cells.push(buildCell(i));

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

    /** File a matched pair into the rack (decoration; must never throw). */
    function rackAdd(pairId) {
      if (!rack) return;
      try {
        const glyph = GLYPHS[((pairId % GLYPHS.length) + GLYPHS.length) % GLYPHS.length];
        rack.appendChild(el('span', 'g-dv-slide', glyph));
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

    /** One media node per card, created once and never re-created on a flip. */
    function applyMedia(cell) {
      const url = pairUrls[cell.pairId];
      const glyph = GLYPHS[((cell.pairId % GLYPHS.length) + GLYPHS.length) % GLYPHS.length];
      if (cell.face && cell.face._url === url) return;
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

    /* ---- HUD paint ------------------------------------------------------- */
    function swapChipText() {
      return t('dv_swaps', 'swaps') + ' ' + swapsFired + '/' + dials.swapBudget;
    }
    function clockText() {
      const left = Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000));
      const m = Math.floor(left / 60);
      const s = left % 60;
      return (m > 0 ? m + ':' + String(s).padStart(2, '0') : left + 's');
    }
    function paintHud() {
      if (swapChip) swapChip.textContent = swapChipText();
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
     * PHASE 0 - deal & preview (with the first poison beat)
     * ==================================================================== */
    function deal() {
      layout.dealOrder.forEach((cellIndex, n) => {
        after(n * TIMING.dealStaggerMs, () => {
          const c = cells[cellIndex];
          if (!c) return;
          applyMedia(c);
          c.card.classList.add('dealt');
        });
      });
      after(layout.dealOrder.length * TIMING.dealStaggerMs + 80, preview);
    }

    function preview() {
      setHint('dv_preview_hint', 'Memorize the board.');
      if (grid) grid.classList.add('scanning');       // the machine shows you
      for (const c of cells) {
        applyMedia(c);
        c.card.classList.add('up');
        playFace(c, true);
      }
      tick('sting', 0.4);
      // THE MEMORIZE-POISON BEAT: exactly at preview end -400ms.
      const poisonAt = Math.max(0, dials.previewMs - TIMING.poisonLeadMs);
      if (dials.subFlash) after(poisonAt, () => poison('preview'));
      after(dials.previewMs, previewDown);
    }

    function previewDown() {
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
      });
      if (r) say('sub_flash poison beat (' + where + ')');
    }

    function armPlay() {
      playStartedMs = elapsedMs;
      setHint('dv_play_hint', 'Find the pairs.');
      openAmbience();
      if (casino) casino.start();          // the lab dresses + the marquee lights
      if (trickster) trickster.start();
      const open = () => { busy = false; settled(true); };
      /* FAKE SHUFFLE (House Rules): the pantomime rides the tail of the
       * preview - cards feint trades and land home. Nothing moves, no tell
       * fires, and input stays closed until the theatre leaves the stage. */
      if (trickster) trickster.shuffle(cells, open); else open();
    }

    /* ==================================================================== *
     * PHASE 1 - the attempt loop
     * ==================================================================== */
    function onTap(i) {
      if (dead || paused || ended) return;
      const cell = cells[i];
      if (!cell || cell.state !== 'down') return;
      if (busy || (faceUp.length + flipping) >= 2) return;
      if (cell.pairId < 0) return;                       // a filler cell, never dealt a pair

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
      const pairId = cells[a].pairId;

      /* "tracked through the static": matched within 2 attempts of the pair
       * being displaced by a glitch_swap. The engine celebrates you beating its
       * own trick, so this forces a flourish. */
      let trackedThis = false;
      if (swapAttempt.has(pairId) && (attempts - swapAttempt.get(pairId)) <= 2) {
        trackedThis = true;
        tracked += 1;
        swapAttempt.delete(pairId);
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
        }
        tick('stamp_bad', 0.4);
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
      if (dead || ended) return;
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
      if (dead || ended) return;
      const open = unmatchedCells();
      const enough = open.length > 2;
      /* DEJA RE-DEAL (House Rules, the native signature) outranks the smaller
       * mutations at its one seeded window - it is a settled-board event like
       * they are, and it consumes the window the same way. */
      if (trickster && trickster.redealDue(settledWindow, open.length)) { runRedeal(); return; }
      // `<=` not `===`: a window the player raced past is not a spent budget,
      // it simply fires at the next settled board (the budget is per class).
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
      const glyph = GLYPHS[((wearPairId % GLYPHS.length) + GLYPHS.length) % GLYPHS.length];
      const node = el('div', 'g-dv-lie');
      if (url && !isVideoUrl(url)) {
        const img = el('img');
        img.setAttribute('alt', '');
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
        playFace(c, true);
        showing.push(c);
      }
      say('re-deal: showing ' + showing.length + ' cards'
        + (redealLie ? ' (one is a lie)' : ' (truthful)'));
      after(trickster ? trickster.redealShowMs : 1500, () => {
        for (const c of showing) { c.card.classList.add('flipping'); playFace(c, false); }
        after(reduced ? TIMING.flipReducedMs : TIMING.flipMs, () => {
          for (const c of showing) c.card.classList.remove('flipping', 'up');
          if (grid) grid.classList.remove('scanning', 'rewind');
          const lied = !!redealLie;
          watchLie = lied ? redealLie.cell.index : -1;
          clearLie();
          if (lied) setHint('dv_redeal_hint', 'One of those was a lie.', true);
          else setHint('dv_redeal_gift', 'The machine blinked.', true);
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
     * evaporate - SYNTHESIS #2 promises one swap per class from tier 2. This walk
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
      tick('glitch', 0.55);
      setHint('dv_swap_hint', 'The board is moving.', true);

      after(TIMING.tellMs, () => {
        let swapped = false;
        const doSwap = () => {
          if (swapped) return;
          swapped = true;
          swapCells(a, b);
          swapsFired += 1;
          entry.swappedAt = settledWindow;
          tick('glitch', 0.4);
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
      if (dead || ended) return;
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

    function win() {
      if (ended) return;
      stopAmbience();
      if (casino) { casino.payout(10); casino.bell(false); }
      setHint('dv_clear', 'Board clear.', true);
      try { ctx.ceremonies.stamp({ text: t('dv_stamp_clear', 'CLEAR'), target: grid }); } catch (e) { /* noop */ }
      tick('stamp', 0.8);
      after(TIMING.ceremonyMs, () => finish(false));
    }

    function bell() {
      if (ended) return;
      stopAmbience();
      if (casino) casino.dimOut();           // the bell is never silence
      setHint('dv_bell', 'The bell. Time is up.', true);
      try { ctx.ceremonies.stamp({ text: t('dv_stamp_bell', 'BELL'), tone: 'pink', target: grid }); } catch (e) { /* noop */ }
      tick('stamp_bad', 0.6);
      after(TIMING.ceremonyMs, () => finish(true));
    }

    function finish(timeout) {
      if (ended) return;
      ended = true;
      stopClock();
      stopAmbience();
      if (trickster) trickster.stop();
      if (casino) casino.stop();
      busy = true;
      const elapsedSec = Math.max(0.001, (elapsedMs - playStartedMs) / 1000);
      const scoreIn = {
        tier: dials.tier,
        pairs: playablePairs(),
        matched,
        attempts,
        elapsedSec,
        parSec: dials.parSec,
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
            tier: dials.tier, pairs: playablePairs(), matched, attempts,
            clearTimeSec: Math.round(elapsedSec), maxCombo, tracked, calledLies,
            swapsFired, driftsFired, bubblesPopped, jackpots,
            timeout: !!timeout,
            peekHold: peekHoldMult(),
          },
        },
      };
      lastReport = report;
      try { lastSnapshot = instance.snapshot(); } catch (e) { /* diagnostics only */ }
      say('class over: ' + matched + '/' + playablePairs() + ' pairs, ' + attempts + ' attempts, '
        + Math.round(elapsedSec) + 's (par ' + dials.parSec + 's), combo ' + maxCombo
        + ', tracked ' + tracked + (calledLies ? ', called ' + calledLies + ' lies' : '')
        + ', swaps ' + swapsFired + '/' + dials.swapBudget
        + (timeout ? ', BELL' : '') + ' -> composite ' + score.composite.toFixed(3));
      try {
        ctx.endClass(report);
      } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
    }

    /* ==================================================================== *
     * AMBIENCE (the tier dials that are not mutations)
     * ==================================================================== */
    function openAmbience() {
      heat();
      if (dials.ambient) sustainSafe('ambient_field', { kind: 'motes', density: dials.ambient });
      if (dials.crt) sustainSafe('crt', { level: dials.crt, variant: dials.tier >= 4 ? 'chroma' : 'scanline' });
      if (dials.bubbles) {
        /* The dossier's poppable decoys: popping one costs ~300ms of jiggle and
         * NEVER counts as a flip or touches the grade (the rubric already prices
         * the time). The engine arms its own escape guard on a clickable field. */
        sustainSafe('bubble_field', {
          max: dials.bubbles,
          variant: 'drift',
          onPop: () => {
            bubblesPopped += 1;
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
        if (elapsedMs >= budgetMs) { stopClock(); run(bell); }
      }, scaled(250));
    }
    function stopClock() { if (clockId) { clearInterval(clockId); clockId = 0; } }

    /* ---- assets --------------------------------------------------------- */
    function claimAssets() {
      // NEVER block a draw: the board is already dealing on glyph faces and the
      // urls drop in when the pool resolves (empty remote -> local, silently).
      Promise.resolve()
        .then(() => ctx.assets.claim({
          loops: Math.max(12, dials.pairs + 6), stills: 4, targets: 0, canvasSafe: false,
        }))
        .then((p) => {
          if (dead || !p || typeof p.next !== 'function') return;
          pool = p;
          /* PAIRS MUST STAY LEGIBLE. Two different pairs wearing the same art is
           * an unwinnable board, so a pair only ever gets a url no other pair
           * has; when the pool runs short the leftovers play on their glyph
           * face (always distinct) instead of repeating someone else's clip. */
          const distinct = [];
          const want = dials.pairs;
          for (let n = 0; n < want * 4 && distinct.length < want; n++) {
            const got = p.next('loop');
            const u = got && got.url;
            if (u && distinct.indexOf(u) < 0) distinct.push(u);
          }
          for (let pid = 0; pid < want; pid++) pairUrls[pid] = distinct[pid] || null;
          for (const c of cells) applyMedia(c);
          say(distinct.length >= want
            ? 'asset pool ready (' + distinct.length + ' distinct loops for ' + want + ' pairs)'
            : 'asset pool short (' + distinct.length + '/' + want
              + ' distinct loops) - the rest play on glyph faces');
        })
        .catch((e) => say('asset claim failed - glyph faces stand: ' + ((e && e.message) || e)));
    }

    /* ==================================================================== *
     * THE MODULE INSTANCE
     * ==================================================================== */
    const instance = {
      start(classSpec) {
        spec = classSpec || { gradeTier: 1, seed: 'deja_vu|none', timeBudgetSec: 90 };
        const tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        const seed = String(spec.seed == null ? 'deja_vu' : spec.seed);
        budgetMs = Math.max(20000, (Number(spec.timeBudgetSec) || 90) * 1000);
        reduced = probeReduced();
        posterOnly = reduced;

        const chosen = ctx.settings ? ctx.settings.boardSize : null;
        const pairs = Number(chosen);
        dials = dialsFor(tier, { pairs: Number.isFinite(pairs) ? pairs : null });
        layout = buildLayout(seed, dials);
        swaps = buildSwapSchedule(seed, dials);
        drifts = buildDriftSchedule(seed, dials, swaps);
        roll = makeTaggedRoll(seed + '|dv|play');
        rollReward = createReward(seed);
        loopPolicy = matchedLoopPolicy(ctx.settings && ctx.settings.dv_matched_loops, tier, posterOnly);
        if (loopPolicy.reason === 'ceiling') {
          say('dv_matched_loops: "keep-playing" refused by the quality/tier ceiling - frozen');
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
         * identically on a retake (same seed, same streams). */
        const capsOk = !(ctx.caps && Number(ctx.caps.bgIntensity) === 0);
        casino = createDvCasino({
          seed, stage, bench, grid,
          timers: deckTimers,
          reduced, capsOk,
          log: say,
        });
        trickster = createDvTrickster({
          seed, tier,
          timers: deckTimers,
          reduced, capsOk,
          isHalted: () => dead || paused || ended,
          stats: () => ({
            swaps: swapsFired,
            budget: dials.swapBudget,
            secLeft: Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)),
          }),
          chipEl: (which) => (which === 'swaps' ? swapChip : clockChip),
          chipText: (which) => (which === 'swaps' ? swapChipText() : clockText()),
          log: say,
        });

        wireCram();
        claimAssets();
        startClock();
        deal();

        liveClass = instance;
        lastReport = null;
        lastSnapshot = null;
        say('tier ' + tier + ' board ' + dials.cols + 'x' + dials.rows + ' (' + dials.pairs
          + ' pairs), preview ' + dials.previewMs + 'ms, swap budget ' + dials.swapBudget
          + (dials.drift ? ', drift ' + dials.drift : '') + (retake ? ', RETAKE' : ''));
      },

      pause() {
        if (paused) return;
        paused = true;
        // the lab holds its breath: every CSS animation (sweep, beam, shudder)
        // freezes in place via animation-play-state
        if (stage) stage.classList.add('suspended');
        for (const c of cells) playFace(c, false);
      },

      resume() {
        if (!paused) return;
        paused = false;
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
          seed: spec ? spec.seed : null,
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
          elapsedMs, budgetMs, ended, busy, paused,
          loopPolicy: Object.assign({}, loopPolicy),
          posterOnly, reduced,
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
