/* ============================================================================
 * games/echo/index.js - ECHO (Simon; family: memory; Semester II, 105s).
 *
 * Six pads in a ring. The room plays a sequence - each pad lights and sounds
 * its own note - and you play it back. Clear it and the sequence grows by one.
 * Miss and the room simply deals another one: ONE FAIL IS NOT THE CLASS. The
 * class ends on the bell, and it is graded on the longest echo you held, how
 * clean you were, how well you kept tempo, and how many lies you refused.
 *
 * THE ROUND (pinned by the Semester II/III contract):
 *   deal      a sequence of `len` from this attempt's seeded stream
 *   echo      the ring plays it: pad lights + ONE engine audio_trigger recipe
 *             at the pad's own pitch; a DECOY may light at a seam (tier 2+)
 *   seam      the interference beat - engine.beat(), sub_flash word blips,
 *             the input pressure comes up (tier 2+)
 *   input     tap or the declared pad1..pad6 verbs; every clean press ratchets
 *             the pitch +1 semitone (cap 7) so a good run becomes a melody
 *   clear     len + 1, a reward roll, the streak meter
 *   fail      the correct pad shows itself, then EITHER the ENCORE (once per
 *             class: the SAME sequence again at half tempo) or a new sequence
 *             at max(startLen, len - 2)
 *   bell      the real budget. Dim out, end card, endClass exactly once.
 *
 * THE WARM START (the critic's top fix, contract ruling 5). Simon's structural
 * flaw is that every class restarts at 3. Echo opens at
 * clamp(3 + floor(gameMeta.bestLen / 4), 3, 6) + a per-tier bonus, so a
 * veteran never plays the trivial opening and a first class still opens at 3.
 *
 * LAWS THIS FILE KEEPS:
 *   I    the ledger is honest - length, presses, latency, decoys and the clock
 *        are computed HERE and never routed through a deck. The trickster may
 *        lie on a chip FACE or a pad's WORD; the pad's GLYPH and its data-pad
 *        are truth and are repainted.
 *   II   a pad's hitbox never moves and the key under the finger never moves;
 *        every engine one-shot over the ring is decoration (fireSafe() welds
 *        clickSafe on, bubbles are never poppable here).
 *   III  something always breathes: the ambient field runs from the first beat.
 *   IV   the class-rules sheet is DRAWN, GO-only, and honours ctx.hideTutorial
 *        with a once-per-grade-tier memory in gameMeta.howtoTiers (IC's policy).
 *   V    stream, decoy plan, casino, trickster and pressure are all scoped off
 *        the class seed; a retake replays the identical class.
 *   VI   pause / resume / suspend / destroy: the timer registry defers, the
 *        decks ride it, no timer survives destroy, every listener is removed.
 *   VII  every visible string is ctx.lexicon(key, fallback) over lex.js EC_LEX.
 *
 * ctx.audioAudible === false is a FIRST-CLASS MODE here, not an afterthought:
 * Echo is the one class that can be played by ear, so when the ear is gone the
 * LIGHT has to carry the whole signal - pads light SILENT_LIT_MULT longer, the
 * stage wears data-audible="0" for the creative's stronger tell, and the
 * proctor says so once.
 *
 * WHAT THIS FILE DOES NOT OWN: grades (core/grades.js via ctx.endClass), XP
 * (C#), the tier (registry + meta), effect strengths (the engine's ceiling
 * rule), the LOOK (style.js), the lighting (casino.js), the lies
 * (trickster.js) and the CCP-effects ladder (pressure.js). The pure model is
 * sequence.js and the composite grade.js.
 *
 * ENGINE TARGETING NOTE: glitch_swap adds `.ae-glitch{position:relative;
 * animation}` to its targets, so it may never touch a `.g-ec-pad` (style.js
 * owns the pad's transform and the ring's geometry). The decoy shudder targets
 * the pad's FACE nodes (`.g-ec-face` children), never the pad itself.
 * ==========================================================================*/

import { injectEchoStyle } from './style.js';
import { createEcCasino } from './casino.js';
import { createEcTrickster } from './trickster.js';
import { createEcPressure } from './pressure.js';
import { EC_LEX } from './lex.js';
import {
  PLAYTEST, alphabetFor, warmStartLen, nextLenAfterFail, stepMsFor, litMsFor,
  audioCeilFor, pitchFor, ratchetAfterMiss, isNearMiss, heatFor, buildRound,
} from './sequence.js';
import { compositeFor, hardGates, flavorXp } from './grade.js';
import { makeRng, makeTaggedRoll, shuffled } from '../../core/rng.js';

const GAME_KEY = 'echo';

/** A url an <img> cannot show (a webm/mp4 loop). Mirrors engine/util.js
 *  VIDEO_URL_RE; games never import the engine, so the rule is repeated. */
const VIDEO_URL_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;

/** The truthful pad glyphs (Law IV: the glyph never lies, the word may).
 *  Six shapes that read at a glance and carry no meaning of their own. */
const GLYPHS = Object.freeze(['◆', '●', '▲', '■', '★', '✦']);

/** The pad tone. ONE recipe, six pitches (sequence.js PAD_SEMIS).
 *  `sting` is shell/audio.js's triangle 440->520 / 140ms - musical enough to
 *  ratchet. If the orchestrator lands the requested dedicated `pad` recipe,
 *  this ONE line adopts it (an unknown name degrades to `blip`, never to
 *  silence - web CLAUDE.md trap 18). */
const PAD_SFX = 'pad';
/** The decoy's own cue at the TELEGRAPHED tiers only. At tier 3+ a decoy
 *  sounds exactly like a real step - that is the whole point of the trap. */
const DECOY_SFX = 'glitch';
const FAIL_SFX = 'bump';
const CLEAR_SFX = 'streak';

/** The pad-face setting. */
const FACE_MODES = Object.freeze(['words', 'glyphs']);

/** How often the honest window ring repaints (the trickster polls it). */
const WINDOW_TICK_MS = 60;

/* Diagnostics seams (the DV / DE precedent): the live class, the last report,
 * the final snapshot. The shell never reads these; the harness does. */
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

function mmss(secLeft) {
  const s = Math.max(0, secLeft | 0);
  return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
}

/** A pool word, trimmed to something a pad can wear. Never a lexicon row. */
function padWordOf(raw) {
  const s = String(raw == null ? '' : raw).trim();
  if (!s) return '';
  return s.length > 12 ? s.slice(0, 12) : s;
}

export default {
  key: GAME_KEY,
  family: 'memory',
  meaty: false,
  flagship: false,
  timeBudgetSec: 105,
  title: 'Echo',

  manifest: {
    /* audio_trigger is the mechanic here, not a garnish: the pad tones ARE the
     * second half of the signal. Everything else is the tier's dials. */
    effectsConsumed: [
      'audio_trigger', 'wash', 'sub_flash', 'glitch_swap',
      'flash_burst', 'ambient_field', 'bubble_field', 'gif_burst',
    ],
    /* Six loops (one pad face each) + six stills; pads are DOM and nothing is
     * drawn into a canvas, so the provider may serve remote media here. */
    assetNeeds: { loops: 6, targets: 0, stills: 6, canvasSafe: false },
    boardSizes: null,
    /* SYNTHESIS #7: the game DECLARES the verb slots, the shell owns the UI,
     * the storage and the panic-key conflict check. ctx.keys.on('pad3', fn). */
    keybinds: [
      { verb: 'pad1', label_key: 'ec_key_pad1', default: '1' },
      { verb: 'pad2', label_key: 'ec_key_pad2', default: '2' },
      { verb: 'pad3', label_key: 'ec_key_pad3', default: '3' },
      { verb: 'pad4', label_key: 'ec_key_pad4', default: '4' },
      { verb: 'pad5', label_key: 'ec_key_pad5', default: '5' },
      { verb: 'pad6', label_key: 'ec_key_pad6', default: '6' },
    ],
    settings: [
      {
        key: 'ec_pad_words', kind: 'enum', values: FACE_MODES.slice(), default: 'words',
        label_key: 'ec_pad_words', hint_key: 'ec_pad_words_hint',
      },
    ],
    peek: false,
  },

  create(ctx) {
    const t = (key, fallback) => {
      const fb = fallback == null ? (EC_LEX[key] == null ? key : EC_LEX[key]) : fallback;
      try { const v = ctx.lexicon(key, fb); return v == null ? fb : v; } catch (e) { return fb; }
    };
    const say = (m) => { try { ctx.log('[ec] ' + m); } catch (e) { /* noop */ } };

    /* ---- lifecycle flags ------------------------------------------------ */
    let dead = false;
    let paused = false;
    let ended = false;
    let reported = false;
    let busy = true;                    // input closed (briefing / playback / holds)

    /* ---- class state ---------------------------------------------------- */
    let spec = null;
    let seed = '';
    let tier = 1;
    let reduced = false;
    let retake = false;
    let audible = true;
    let budgetMs = 105000;
    let pool = null;
    let rollLocal = null;
    let subRoll = null;                 // the seeded sub_flash word stream

    let casino = null;
    let trickster = null;
    let pressure = null;

    /* ---- the round ------------------------------------------------------ */
    let attempt = 0;                    // the seeded stream index
    let startLen = 3;
    let curLen = 3;
    let round = null;                   // sequence.js buildRound() output
    let stepIdx = 0;                    // playback cursor
    let expectIdx = 0;                  // input cursor
    let inputOpen = false;
    let encoreArmed = true;             // once per class
    let encoreUsed = false;
    let inEncore = false;
    let ratchet = 0;

    /* ---- the ledger (Law I) --------------------------------------------- */
    let bestLen = 0;                    // longest CLEARED echo this class
    let bestReach = 0;                  // deepest correct index in any sequence
    let lifetimeBefore = 0;
    let sequencesDealt = 0;
    let sequencesCleared = 0;
    let fails = 0;
    let timeouts = 0;
    let presses = 0;
    let correctPresses = 0;
    let pressStreak = 0;                // clean presses in a row (the ratchet)
    let bestStreak = 0;
    let clearStreak = 0;
    let decoysPresented = 0;
    let decoysResisted = 0;
    let decoysTaken = 0;
    let latencySum = 0;
    let latencyCount = 0;
    let jackpots = 0;
    let nearMisses = 0;
    let subFlashes = 0;
    let currentHeat = 0;
    let bellOn = false;
    let stallMs = 0;
    let lastPressAt = 0;
    let newRecord = false;

    /* ---- pads / faces --------------------------------------------------- */
    let faceMode = 'words';             // ec_pad_words, after the empty-pool rule
    const padWords = [];                // index -> the word a pad wears (truth)
    const faceUrls = new Map();         // pad -> {url, kind} | {broken:true}
    let faceKind = 'loop';
    let faceLogged = false;

    /* ---- dom ------------------------------------------------------------ */
    let stage = null; let backdrop = null; let hud = null; let ring = null;
    let msgEl = null; let well = null; let endEl = null; let howtoEl = null;
    let lenChip = null; let clockChip = null; let streakChip = null; let bestChip = null;
    /* THE WINDOW RING: the honest face of the soft input timer (tier 3+ only).
     * CORE writes `--ec-k` 1 -> 0 on it and NOTHING else; the trickster's
     * Crooked Clock paints a BENT twin over it and the real one underneath is
     * never touched (Law I). At the untimed tiers the node simply never
     * appears and the card folds itself. */
    let timerEl = null;
    const padEls = [];
    const padTimers = new Map();        // pad -> the timer that un-lights it

    /* ---- clock ---------------------------------------------------------- */
    let clockId = 0;
    let lastTick = 0;
    let elapsedMs = 0;

    /* ---- ambience ------------------------------------------------------- */
    let washOn = false;
    let bubblesOn = false;
    let ambientOn = false;

    /* ==================================================================== *
     * TIMERS - every step goes through run() so a suspend freezes the class
     * mid-playback and a resume finishes it. `every` simply skips while paused.
     * (The Deep End's registry, verbatim in shape - the decks ride it too.)
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
     * ENGINE - one wrapper, the input-trust law and the tier's audio ceiling
     * welded on (the contract's deckEngine weld).
     * ==================================================================== */
    function fireSafe(kind, opts) {
      if (dead || paused || !ctx.engine) return null;
      const o = Object.assign({}, opts || {});
      if (kind === 'flash_burst' || kind === 'gif_burst') {
        /* DECISIONS #9: the ring is a tap-precision surface, so every burst
         * over it is pointer-events:none decoration and never poppable. */
        o.clickSafe = true;
        o.clickable = false;
        delete o.onPop;
      }
      if (kind === 'audio_trigger') {
        const ceil = audioCeilFor(tier);
        o.level = Math.min(ceil, o.level == null ? 0.4 : Number(o.level) || 0);
        /* `pitch` is a pass-through (engine/oneshots.js forwards it and
         * shell/audio.js owns the 0.5..2 clamp) - we never clamp it twice. */
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
    function beatSafe(opts) { try { if (ctx.engine && ctx.engine.beat) ctx.engine.beat(opts || {}); } catch (e) { /* noop */ } }
    function ceremonySafe(kind, opts) {
      if (dead || !ctx.engine || typeof ctx.engine.ceremony !== 'function') return null;
      try { return ctx.engine.ceremony(kind, opts || {}) || null; } catch (e) { return null; }
    }
    /** The engine, as a deck sees it: the welded primitives plus a READ of the
     *  clamped channel vector (THE CEILING RULE - a deck asks, never raises). */
    const deckEngine = {
      fire: fireSafe,
      sustain: sustainSafe,
      stop: stopSafe,
      beat: beatSafe,
      ceremony: ceremonySafe,
      channels: () => {
        try { return (ctx.engine && typeof ctx.engine.channels === 'function') ? ctx.engine.channels() : null; }
        catch (e) { return null; }
      },
    };
    /** The player's own media, as a deck sees it. The pool lands ASYNC, so a
     *  deck gets a LIVE reader rather than the pool object. */
    const deckAssets = {
      next(kind) {
        try { return (pool && typeof pool.next === 'function') ? (pool.next(kind) || null) : null; }
        catch (e) { return null; }
      },
    };
    /** bgIntensity 0 is the player's exit: read it LIVE, never a snapshot. */
    function capsArmed() { return !(ctx.caps && Number(ctx.caps.bgIntensity) === 0); }

    /** A pad tone (or any cue) through the engine, at the tier's ceiling. */
    function tone(name, level, pitch, extra) {
      return fireSafe('audio_trigger', Object.assign(
        { name, level: level == null ? 0.4 : level },
        pitch != null ? { pitch } : {},
        extra || {},
      ));
    }
    /** Deck IV's haptics hook: one call per rung, silent where there is no hardware. */
    function haptic(ms) {
      try {
        if (!ctx.platform || !ctx.platform.hasHaptics) return;
        if (typeof navigator === 'undefined' || typeof navigator.vibrate !== 'function') return;
        navigator.vibrate(Math.max(4, Math.min(40, Number(ms) || 10)));
      } catch (e) { /* a haptic must never be the thing that fails */ }
    }

    /* ---- the decks, null-safe ------------------------------------------- */
    function deck(which, method, ...args) {
      const d = which === 'casino' ? casino : which === 'pressure' ? pressure : trickster;
      if (!d || typeof d[method] !== 'function') return undefined;
      try { return d[method](...args); }
      catch (e) { say(which + '.' + method + ' threw: ' + ((e && e.message) || e)); return undefined; }
    }

    /* ==================================================================== *
     * HEAT - the class's own ladder is the sequence length; gradeTier caps it.
     * ==================================================================== */
    function heat() {
      const h = heatFor(curLen, tier, startLen, pressStreak);
      currentHeat = h;
      try { if (ctx.engine) ctx.engine.setHeat(h); } catch (e) { /* engine is optional */ }
      deck('casino', 'setHeat', h);
      deck('pressure', 'setHeat', h);
      /* The RUNG rides the ladder (the sequence length), the MAGNITUDE rides
       * heat - and this is the one place both are known. The second argument
       * is additive: a deck that declared setStreak(n) simply ignores it. */
      deck('pressure', 'setStreak', curLen, { links: pressStreak, cleared: clearStreak, heat: h });
    }

    /* ==================================================================== *
     * DOM (the contract's exact shape)
     * ==================================================================== */
    function setPhase(p) { if (stage) stage.setAttribute('data-phase', p); }
    function phase() { return stage ? stage.getAttribute('data-phase') : null; }
    function msg(key, fallback) {
      if (!msgEl) return;
      msgEl.textContent = t(key, fallback);
    }

    function buildDom() {
      const root = ctx.root;
      root.textContent = '';
      stage = el('div', 'g-ec-stage');
      stage.setAttribute('data-phase', 'briefing');
      stage.setAttribute('data-tier', String(tier));
      stage.setAttribute('data-faces', faceMode);
      /* The audio tell is a STAGE attribute so style.js can make the light
       * carry the whole signal when the ear cannot (ctx.audioAudible). */
      stage.setAttribute('data-audible', audible ? '1' : '0');
      if (reduced) stage.setAttribute('data-reduced', '1');

      backdrop = el('div', 'g-ec-backdrop');
      backdrop.setAttribute('aria-hidden', 'true');
      backdrop.style.pointerEvents = 'none';
      stage.appendChild(backdrop);

      hud = el('div', 'g-ec-hud');
      lenChip = el('span', 'g-ec-chip g-ec-len', '0');
      lenChip.setAttribute('aria-label', t('ec_chip_len', EC_LEX.ec_chip_len));
      clockChip = el('span', 'g-ec-chip g-ec-clock', clockText());
      clockChip.setAttribute('aria-label', t('ec_chip_clock', EC_LEX.ec_chip_clock));
      streakChip = el('span', 'g-ec-chip g-ec-streak', 'x 0');
      streakChip.setAttribute('aria-label', t('ec_chip_streak', EC_LEX.ec_chip_streak));
      streakChip.hidden = true;
      bestChip = el('span', 'g-ec-chip g-ec-best', '0');
      bestChip.setAttribute('aria-label', t('ec_chip_best', EC_LEX.ec_chip_best));
      hud.appendChild(lenChip);
      hud.appendChild(clockChip);
      hud.appendChild(streakChip);
      hud.appendChild(bestChip);
      timerEl = el('span', 'g-ec-chip g-ec-timer');
      timerEl.setAttribute('aria-hidden', 'true');
      timerEl.hidden = true;
      hud.appendChild(timerEl);
      if (retake) hud.appendChild(el('span', 'g-ec-chip g-ec-retake', t('ec_retake', EC_LEX.ec_retake)));
      stage.appendChild(hud);

      ring = el('div', 'g-ec-ring');
      ring.setAttribute('role', 'group');
      ring.setAttribute('aria-label', t('ec_ring_aria', EC_LEX.ec_ring_aria));
      ring.style.setProperty('--ec-n', String(PLAYTEST.PADS));
      padEls.length = 0;
      for (let i = 0; i < PLAYTEST.PADS; i++) {
        const pad = el('button', 'g-ec-pad');
        pad.setAttribute('type', 'button');
        try { pad.type = 'button'; } catch (e) { /* the DOM double has no button semantics */ }
        pad.setAttribute('data-pad', String(i));
        pad.setAttribute('data-state', 'idle');
        pad.setAttribute('data-glyph', String(i));
        pad.setAttribute('aria-label', t('ec_pad_aria', EC_LEX.ec_pad_aria).replace('{n}', String(i + 1)));
        /* GEOMETRY IS THE CREATIVE'S. index.js only ever writes the vars: the
         * pad's index, the ring's size and the unit-circle position of the
         * slot. style.js owns every transform and transition. */
        const ang = (-90 + (360 / PLAYTEST.PADS) * i);
        pad.style.setProperty('--i', String(i));
        pad.style.setProperty('--ang', String(ang) + 'deg');
        pad.style.setProperty('--x', Math.cos(ang * Math.PI / 180).toFixed(4));
        pad.style.setProperty('--y', Math.sin(ang * Math.PI / 180).toFixed(4));

        const face = el('span', 'g-ec-face');
        const media = el('img', 'g-ec-media');
        armMedia(media, pad, false);
        face.appendChild(media);
        /* THE TRUTH NODE (Law IV + the Unreliable Label card): the glyph is
         * what a pad IS. The word below it is decoration the trickster may
         * borrow; the glyph never moves and is never re-written. */
        face.appendChild(el('span', 'g-ec-glyph', GLYPHS[i] || ''));
        pad.appendChild(face);
        pad.appendChild(el('span', 'g-ec-word', ''));

        pad.addEventListener('pointerdown', onPadDown);
        pad.addEventListener('pointerup', onPadUp);
        pad.addEventListener('pointerleave', onPadUp);
        pad.addEventListener('pointercancel', onPadUp);
        ring.appendChild(pad);
        padEls.push(pad);
      }
      stage.appendChild(ring);

      msgEl = el('p', 'g-ec-msg');
      msgEl.setAttribute('aria-live', 'polite');
      stage.appendChild(msgEl);

      well = el('div', 'g-ec-flashwell');
      well.setAttribute('aria-hidden', 'true');
      well.style.pointerEvents = 'none';
      stage.appendChild(well);

      endEl = el('div', 'g-ec-end');
      endEl.hidden = true;
      stage.appendChild(endEl);

      root.appendChild(stage);
    }

    /* ---- pad faces (Deck VI asset chrome) -------------------------------- */
    function armMedia(node, pad, video) {
      if (!node) return;
      try {
        node.setAttribute('alt', '');
        node.setAttribute('draggable', 'false');
        node.draggable = false;
        if (video) {
          node.muted = true; node.loop = true; node.autoplay = true; node.playsInline = true;
          node.setAttribute('muted', ''); node.setAttribute('loop', ''); node.setAttribute('autoplay', '');
          node.setAttribute('playsinline', ''); node.setAttribute('preload', 'auto');
          node.addEventListener('loadeddata', () => {
            if (dead) return;
            pad.classList.add('is-loaded');
            try { const p = node.play(); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch (e) { /* ignore */ }
          });
        } else {
          node.decoding = 'async';
          node.addEventListener('load', () => { if (!dead) pad.classList.add('is-loaded'); });
        }
        node.addEventListener('error', () => { if (!dead) faceBroken(pad); });
      } catch (e) { /* the double has no img semantics; fine */ }
    }
    function childOf(node, cls) {
      try { for (const k of node.children || []) if (k.classList && k.classList.contains(cls)) return k; } catch (e) { /* ignore */ }
      return null;
    }
    function faceNodeOf(pad) { return childOf(pad, 'g-ec-face'); }
    function mediaOf(pad) { const f = faceNodeOf(pad); return f ? childOf(f, 'g-ec-media') : null; }
    function wordNodeOf(pad) { return childOf(pad, 'g-ec-word'); }
    function glyphNodeOf(pad) { const f = faceNodeOf(pad); return f ? childOf(f, 'g-ec-glyph') : null; }
    /** The glitch_swap target set for a pad: the FACE's children, never the pad. */
    function faceTargets(pad) {
      const f = faceNodeOf(pad);
      if (!f) return [];
      const out = [];
      try { for (const k of f.children || []) out.push(k); } catch (e) { /* ignore */ }
      return out;
    }
    function mediaNodeFor(pad, url) {
      const face = faceNodeOf(pad);
      if (!face) return null;
      const cur = childOf(face, 'g-ec-media');
      const wantVideo = VIDEO_URL_RE.test(String(url || ''));
      const isVideo = !!(cur && String(cur.tagName || '').toUpperCase() === 'VIDEO');
      if (cur && wantVideo === isVideo) return cur;
      const next = el(wantVideo ? 'video' : 'img', 'g-ec-media');
      armMedia(next, pad, wantVideo);
      try {
        if (cur && typeof face.replaceChild === 'function') face.replaceChild(next, cur);
        else face.appendChild(next);
      } catch (e) { try { face.appendChild(next); } catch (e2) { /* ignore */ } }
      return next;
    }
    function faceBroken(pad) {
      const i = Number(pad && pad.getAttribute('data-pad'));
      pad.classList.remove('is-loaded');
      if (i >= 0) {
        faceUrls.set(i, { broken: true });
        say('faces: pad ' + i + ' media failed - plain face for the class');
      }
    }
    /** One url per pad, dealt once and frozen for the class. */
    function dressFaces() {
      if (!pool || typeof pool.next !== 'function') return;
      for (let i = 0; i < padEls.length; i++) {
        const have = faceUrls.get(i);
        if (have) continue;
        let got = null;
        try { got = pool.next(faceKind); } catch (e) { got = null; }
        const url = got && got.url ? String(got.url) : null;
        if (!url) continue;
        faceUrls.set(i, { url, kind: faceKind });
        const pad = padEls[i];
        const media = mediaNodeFor(pad, url) || mediaOf(pad);
        pad.classList.remove('is-loaded');
        try { if (media) media.src = url; } catch (e) { /* ignore */ }
      }
      if (!faceLogged && faceUrls.size) {
        faceLogged = true;
        say('faces: ' + faceKind + ' on ' + faceUrls.size + ' pads');
      }
    }

    /* ---- pad words (may be EMPTY - the glyph fallback is designed in) ---- */
    function assignWords() {
      padWords.length = 0;
      const want = String(ctx.settings && ctx.settings.ec_pad_words != null ? ctx.settings.ec_pad_words : 'words')
        .trim().toLowerCase();
      faceMode = FACE_MODES.indexOf(want) >= 0 ? want : 'words';
      let words = [];
      try {
        const src = Array.isArray(ctx.words) ? ctx.words : [];
        const seen = new Set();
        for (const w of src) {
          const s = padWordOf(w);
          if (!s || seen.has(s.toLowerCase())) continue;
          seen.add(s.toLowerCase());
          words.push(s);
        }
      } catch (e) { words = []; }
      /* THE MAY-BE-EMPTY CONTRACT (the dossier's mod-agnosticism proof): with
       * fewer than six distinct words the pads wear glyphs only, and the whole
       * mechanic still runs. No half-worded ring - that reads as a bug. */
      if (faceMode === 'words' && words.length < PLAYTEST.PADS) {
        faceMode = 'glyphs';
        say('pad words: only ' + words.length + ' in the pool - glyph faces (the mechanic is unchanged)');
      }
      if (faceMode === 'words') {
        const picked = shuffled(words, makeRng(seed + '|ec-words')).slice(0, PLAYTEST.PADS);
        for (let i = 0; i < PLAYTEST.PADS; i++) padWords.push(picked[i] || '');
      } else {
        for (let i = 0; i < PLAYTEST.PADS; i++) padWords.push('');
      }
      if (stage) stage.setAttribute('data-faces', faceMode);
      paintWords();
    }
    /** THE TRUTH on every pad word (the trickster's Unreliable Label restores
     *  exactly this string; the glyph it can never touch). */
    function paintWords() {
      for (let i = 0; i < padEls.length; i++) {
        const w = wordNodeOf(padEls[i]);
        if (w && w.textContent !== (padWords[i] || '')) w.textContent = padWords[i] || '';
      }
    }
    function padWordText(i) { return padWords[i] || ''; }

    /* ---- HUD paint (truth) ---------------------------------------------- */
    function secLeft() { return Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)); }
    function clockText() { return mmss(secLeft()); }
    function lenText() { return String(curLen); }
    function bestText() { return String(Math.max(bestLen, lifetimeBefore)); }
    /** What the trickster's Stat Flicker restores after a lie. */
    function chipText(which) {
      if (which === 'clock') return clockText();
      if (which === 'best') return bestText();
      if (which === 'streak') return 'x ' + pressStreak;
      return lenText();
    }
    function paintHud() {
      if (clockChip) clockChip.textContent = clockText();
      if (lenChip) lenChip.textContent = lenText();
      if (bestChip) bestChip.textContent = bestText();
      paintStreak();
    }
    function paintStreak() {
      if (!streakChip) return;
      if (pressStreak < PLAYTEST.STREAK_VISIBLE) {
        streakChip.hidden = true;
        streakChip.textContent = 'x ' + pressStreak;
        return;
      }
      streakChip.hidden = false;
      streakChip.textContent = 'x ' + pressStreak;
      /* The meter is the SHELL's primitive (10 segments, always); it rides
       * inside the chip so the contract's DOM gains no extra node. */
      try {
        const meter = ctx.ceremonies.streakMeter({
          filled: Math.min(PLAYTEST.STREAK_CAP, pressStreak),
          gold: ratchet >= PLAYTEST.RATCHET_CAP,
        });
        if (meter) streakChip.appendChild(meter);
      } catch (e) { /* a ceremony must never be the thing that fails */ }
    }

    /* ==================================================================== *
     * THE PADS - lighting and state. `data-state` is the contract's enum:
     * idle | lit | pressed | decoy. Nothing else is ever written to it.
     * ==================================================================== */
    function padState(i, state, holdMs) {
      const pad = padEls[i];
      if (!pad) return;
      const prev = padTimers.get(i);
      if (prev) { clearTimer(prev); padTimers.delete(i); }
      pad.setAttribute('data-state', state);
      if (holdMs > 0) {
        const id = after(holdMs, () => {
          padTimers.delete(i);
          const p = padEls[i];
          if (p && p.getAttribute('data-state') === state) p.setAttribute('data-state', 'idle');
        });
        padTimers.set(i, id);
      }
    }
    function clearPads() {
      for (const id of Array.from(padTimers.values())) clearTimer(id);
      padTimers.clear();
      for (const pad of padEls) if (pad) pad.setAttribute('data-state', 'idle');
    }

    /* ==================================================================== *
     * INPUT - tap or the declared pad verbs. Law II: the hitbox is the pad's
     * own, nothing decorative sits over it, and a press outside an input turn
     * is IGNORED (never a fail: the player is not being punished for a reflex
     * during the machine's own turn).
     * ==================================================================== */
    const keyOffs = [];
    let heldByKey = new Set();

    function padIndexOf(node) {
      let n = node;
      let guard = 0;
      while (n && guard < 6) {
        try {
          if (n.classList && n.classList.contains('g-ec-pad')) {
            const i = Number(n.getAttribute('data-pad'));
            return Number.isFinite(i) ? i : -1;
          }
        } catch (e) { /* ignore */ }
        n = n.parentNode;
        guard += 1;
      }
      return -1;
    }
    function onPadDown(e) {
      const i = padIndexOf(e && (e.currentTarget || e.target));
      if (i < 0) return;
      try { if (e && typeof e.preventDefault === 'function') e.preventDefault(); } catch (err) { /* noop */ }
      press(i, 'tap');
    }
    function onPadUp(e) {
      const i = padIndexOf(e && (e.currentTarget || e.target));
      if (i < 0) return;
      releaseVisual(i);
    }
    /** A press that is not a sequence step still pays a tick (Deck IV: a verb
     *  with no sensory echo is a broken slot handle) - but it moves nothing. */
    function press(i, how) {
      if (dead || paused || ended) return;
      if (!inputOpen) {
        padState(i, 'pressed', reduced ? 90 : 130);
        tone(PAD_SFX, 0.16, pitchFor(i, 0));
        return;
      }
      commit(i, how);
    }
    function releaseVisual(i) {
      const pad = padEls[i];
      if (!pad) return;
      if (pad.getAttribute('data-state') === 'pressed' && !padTimers.has(i)) {
        pad.setAttribute('data-state', 'idle');
      }
    }

    function bindInput() {
      /* SYNTHESIS #7: never a raw key - the shell owns the binding, the
       * conflict check and the panic key. The ':up' twin only clears the
       * pressed LOOK, so a held key cannot double-commit. */
      for (let i = 0; i < PLAYTEST.PADS; i++) {
        const verb = 'pad' + (i + 1);
        const idx = i;
        try {
          keyOffs.push(ctx.keys.on(verb, () => {
            if (heldByKey.has(idx)) return;
            heldByKey.add(idx);
            press(idx, 'key');
          }));
          keyOffs.push(ctx.keys.on(verb + ':up', () => {
            heldByKey.delete(idx);
            releaseVisual(idx);
          }));
        } catch (e) { say('keybind ' + verb + ' refused: ' + ((e && e.message) || e)); }
      }
    }
    function unbindInput() {
      for (const off of keyOffs.splice(0)) { try { off(); } catch (e) { /* noop */ } }
      heldByKey = new Set();
      for (const pad of padEls) {
        if (!pad) continue;
        try {
          pad.removeEventListener('pointerdown', onPadDown);
          pad.removeEventListener('pointerup', onPadUp);
          pad.removeEventListener('pointerleave', onPadUp);
          pad.removeEventListener('pointercancel', onPadUp);
        } catch (e) { /* noop */ }
      }
    }

    /* ==================================================================== *
     * THE ROUND
     * ==================================================================== */
    function dealSequence(len) {
      if (dead || ended) return;
      const want = Math.max(PLAYTEST.WARM_MIN, Math.min(PLAYTEST.MAX_LEN, Math.floor(len)));
      curLen = want;
      inEncore = false;
      if (stage) stage.removeAttribute('data-encore');
      round = buildRound({ seed, gradeTier: tier, attempt, len: want, encore: false });
      sequencesDealt += 1;
      expectIdx = 0;
      stepIdx = 0;
      clearPads();
      paintWords();
      paintHud();
      heat();
      setPhase('play');
      /* TIER 2 TELEGRAPHS THE DECOY (SYNTHESIS #2 - the signature twist enters
       * at tier 2, announced; tier 3+ says nothing at all). */
      const telegraphed = round.decoys.some((d) => d.telegraph);
      if (telegraphed) {
        msg('ec_msg_decoy_warn', EC_LEX.ec_msg_decoy_warn);
        if (stage) stage.setAttribute('data-telegraph', '1');
      } else {
        if (stage) stage.removeAttribute('data-telegraph');
        msg('ec_msg_watch', EC_LEX.ec_msg_watch);
      }
      after(telegraphed ? (reduced ? 600 : 900) : (reduced ? 240 : 380), () => startPlayback());
    }

    /** Replay the SAME sequence at half tempo. Once per class (ruling 5). */
    function startEncore() {
      if (dead || ended) return;
      encoreArmed = false;
      encoreUsed = true;
      inEncore = true;
      round = buildRound({ seed, gradeTier: tier, attempt, len: curLen, encore: true });
      expectIdx = 0;
      stepIdx = 0;
      clearPads();
      paintHud();
      setPhase('encore');
      if (stage) stage.setAttribute('data-encore', '1');
      msg('ec_msg_encore', EC_LEX.ec_msg_encore);
      deck('casino', 'encore', true);
      after(reduced ? 500 : 800, () => startPlayback());
    }

    function startPlayback() {
      if (dead || ended || !round) return;
      busy = true;
      inputOpen = false;
      stepIdx = 0;
      setPhase(inEncore ? 'encore' : 'echo');
      if (!audible && sequencesDealt <= 1) msg('ec_msg_silent', EC_LEX.ec_msg_silent);
      else if (!inEncore) msg('ec_msg_watch', EC_LEX.ec_msg_watch);
      playStep();
    }

    function playStep() {
      if (dead || ended || !round) return;
      if (stepIdx >= round.steps.length) { after(round.stepMs, () => seam()); return; }
      const step = round.steps[stepIdx];
      const i = stepIdx;
      stepIdx += 1;
      const lit = litMsFor(round.stepMs, { silent: !audible });
      if (step.decoy) {
        padState(step.pad, 'decoy', lit);
        /* THE DECOY. At the telegraphed tiers it wears its own cue and a
         * shudder so the player can learn the shape of the trap; at tier 3+ it
         * is indistinguishable from a real step - same state class, same tone,
         * same length - and only the SEQUENCE tells you it does not belong. */
        if (step.telegraph) {
          tone(DECOY_SFX, 0.3, 1);
          if (!reduced) {
            const targets = faceTargets(padEls[step.pad]);
            if (targets.length) fireSafe('glitch_swap', { targets, seconds: 0.4, sfx: false, onSwap: () => {} });
          }
        } else {
          tone(PAD_SFX, 0.34, pitchFor(step.pad, ratchet));
        }
        deck('casino', 'padLit', step.pad, i, round.len);
      } else {
        padState(step.pad, 'lit', lit);
        tone(PAD_SFX, 0.34, pitchFor(step.pad, ratchet));
        deck('casino', 'padLit', step.pad, step.index, round.len);
      }
      after(round.stepMs, () => playStep());
    }

    /** THE INTERFERENCE BEAT: the seam between playback and input is the
     *  Distraction Engine's, and only the engine's - it never moves a hitbox. */
    function seam() {
      if (dead || ended || !round) return;
      beatSafe();
      deck('trickster', 'afterPlayback');
      deck('pressure', 'beat', 'seam');
      /* Tier 2+: sub_flash word blips, deliberately drawn from the PAD words
       * so the distraction is semantically confusable with the signal. The
       * engine owns the alpha; we only choose the text. */
      if (tier >= 2 && capsArmed()) {
        const w = padWords.filter(Boolean);
        const pick = w.length && subRoll ? w[Math.floor(subRoll('word') * w.length) % w.length] : null;
        const r = fireSafe('sub_flash', Object.assign(
          { anchor: well, variant: tier >= 3 ? 'scatter' : 'whisper' },
          pick ? { text: pick } : {},
        ));
        if (r) subFlashes += 1;
      }
      after(reduced ? PLAYTEST.SEAM_MS_REDUCED : PLAYTEST.SEAM_MS, () => openInput());
    }

    function openInput() {
      if (dead || ended || !round) return;
      setPhase('input');
      msg('ec_msg_input', EC_LEX.ec_msg_input);
      busy = false;
      inputOpen = true;
      /* A key whose ':up' never arrived (focus loss, a suspend mid-hold) must
       * never lock its pad out of the next turn. */
      try { heldByKey.clear(); } catch (e) { heldByKey = new Set(); }
      stallMs = 0;
      lastPressAt = Date.now();
      armInputWindow();
      /* Tier 2+ pressure DURING the input turn (the dossier's ladder). Every
       * one of these is decoration over a tap surface (fireSafe / sustainSafe
       * weld clickSafe on) - Law II is never spent for a garnish. */
      if (capsArmed()) {
        if (tier >= 2 && !bubblesOn) { bubblesOn = !!sustainSafe('bubble_field', { variant: 'drift', max: 8 }); }
        if (tier >= 3 && !reduced) { sustainSafe('wash', { variant: 'pink', holdMs: 900 }); washOn = true; }
      }
    }

    let windowTimer = 0;
    let windowTick = 0;
    let windowUntil = 0;
    function armInputWindow() {
      if (windowTimer) { clearTimer(windowTimer); windowTimer = 0; }
      if (!round || !round.windowMs) return;
      windowUntil = Date.now() + round.windowMs;
      if (timerEl) {
        timerEl.hidden = false;
        try { timerEl.style.setProperty('--ec-k', '1'); } catch (e) { /* noop */ }
      }
      if (!windowTick) windowTick = every(WINDOW_TICK_MS, paintWindow);
      windowTimer = after(round.windowMs, () => {
        windowTimer = 0;
        if (!inputOpen || dead || ended) return;
        timeouts += 1;
        fail(-1, 'timeout');
      });
    }
    /** The TRUE remaining share of the window, 1 -> 0. The only writer. */
    function paintWindow() {
      if (!timerEl || !round || !round.windowMs || !inputOpen) return;
      const k = Math.max(0, Math.min(1, (windowUntil - Date.now()) / round.windowMs));
      try { timerEl.style.setProperty('--ec-k', k.toFixed(3)); } catch (e) { /* noop */ }
    }
    function disarmInputWindow() {
      if (windowTimer) { clearTimer(windowTimer); windowTimer = 0; }
      if (windowTick) { clearTimer(windowTick); windowTick = 0; }
      windowUntil = 0;
      if (timerEl) {
        timerEl.hidden = true;
        try { timerEl.style.setProperty('--ec-k', ''); } catch (e) { /* noop */ }
      }
    }

    /** ONE press against the truth. Everything here is the ledger (Law I). */
    function commit(i, how) {
      if (!round || !inputOpen) return;
      const now = Date.now();
      const dt = Math.max(0, now - lastPressAt);
      lastPressAt = now;
      stallMs = 0;
      presses += 1;
      latencySum += dt;
      latencyCount += 1;

      /* THE DECOY TRAP: a decoy at this seam, and the finger landed on it. */
      const bait = round.decoys.find((d) => d.afterIndex === expectIdx);
      if (bait && bait.pad === i) {
        decoysPresented += 1;
        decoysTaken += 1;
        padState(i, 'decoy', reduced ? 260 : 420);
        deck('casino', 'padPressed', i, false, pressStreak);
        fail(i, 'decoy');
        return;
      }

      const want = round.seq[expectIdx];
      if (want === i) {
        correctPresses += 1;
        pressStreak += 1;
        bestStreak = Math.max(bestStreak, pressStreak);
        ratchet = Math.min(PLAYTEST.RATCHET_CAP, ratchet + 1);
        padState(i, 'pressed', reduced ? 140 : 200);
        tone(PAD_SFX, 0.42, pitchFor(i, ratchet));
        haptic(8);
        deck('casino', 'padPressed', i, true, pressStreak);
        /* The decoy at this seam was PRESENTED and REFUSED. The game says so:
         * the dossier's resist-sparkle acknowledges the trap you dodged. */
        if (bait) {
          decoysPresented += 1;
          decoysResisted += 1;
          deck('casino', 'decoyResisted');
          deck('trickster', 'afterInput');
          if (decoysResisted === 1) msg('ec_msg_resisted', EC_LEX.ec_msg_resisted);
        }
        expectIdx += 1;
        bestReach = Math.max(bestReach, expectIdx);
        paintStreak();
        deck('pressure', 'setStreak', curLen, { links: pressStreak, cleared: clearStreak, heat: currentHeat });
        if (expectIdx >= round.seq.length) { clearRound(); return; }
        armInputWindow();
        deck('trickster', 'afterInput');
        return;
      }
      padState(i, 'pressed', reduced ? 200 : 300);
      deck('casino', 'padPressed', i, false, pressStreak);
      fail(i, how === 'timeout' ? 'timeout' : 'wrong');
    }

    function clearRound() {
      if (!round) return;
      inputOpen = false;
      busy = true;
      disarmInputWindow();
      const len = round.seq.length;
      if (len > bestLen) { bestLen = len; if (bestLen > lifetimeBefore) newRecord = true; }
      sequencesCleared += 1;
      clearStreak += 1;
      paintHud();
      deck('casino', 'sequenceDone', len);
      deck('pressure', 'beat', 'clear');
      tone(CLEAR_SFX, 0.5, pitchFor(PLAYTEST.PADS - 1, ratchet));
      haptic(18);
      msg(inEncore ? 'ec_msg_encore_clear' : 'ec_msg_clear',
        inEncore ? EC_LEX.ec_msg_encore_clear : EC_LEX.ec_msg_clear);
      rewardBeat();
      stopInputPressure();
      const hold = reduced ? PLAYTEST.CLEAR_HOLD_MS_REDUCED : PLAYTEST.CLEAR_HOLD_MS;
      after(hold, () => { if (!ended) dealSequence(len + 1); });
    }

    function fail(pressedPad, why) {
      if (!round) return;
      inputOpen = false;
      busy = true;
      disarmInputWindow();
      fails += 1;
      const len = round.seq.length;
      const near = isNearMiss(expectIdx, len);
      ratchet = ratchetAfterMiss(ratchet, expectIdx, len);
      pressStreak = 0;
      clearStreak = 0;
      paintStreak();
      deck('casino', 'fail', pressedPad, round.seq[expectIdx]);
      deck('pressure', 'beat', 'fail');
      deck('pressure', 'setStreak', curLen, { links: 0, cleared: 0, heat: currentHeat });
      tone(FAIL_SFX, 0.4, 1);
      stopInputPressure();
      if (near) { nearMisses += 1; ceremonySafe('near_miss', {}); }
      msg(why === 'timeout' ? 'ec_msg_timeout' : near ? 'ec_msg_near' : 'ec_msg_fail',
        why === 'timeout' ? EC_LEX.ec_msg_timeout : near ? EC_LEX.ec_msg_near : EC_LEX.ec_msg_fail);
      /* THE REVEAL: the pad you needed shows itself, softly, AFTER the commit -
       * never during input (that would be a colour tell). */
      const wanted = round.seq[expectIdx];
      if (wanted != null) {
        after(reduced ? 160 : 260, () => {
          padState(wanted, 'lit', PLAYTEST.REVEAL_MS);
          tone(PAD_SFX, 0.26, pitchFor(wanted, 0));
        });
      }
      const hold = reduced ? PLAYTEST.FAIL_HOLD_MS_REDUCED : PLAYTEST.FAIL_HOLD_MS;
      after(hold, () => {
        if (ended || dead) return;
        /* ONE FAIL IS NOT THE CLASS. The encore is the comeback hook (once),
         * then the room simply deals a shorter one and keeps going. */
        if (encoreArmed && !inEncore) { startEncore(); return; }
        if (inEncore) {
          msg('ec_msg_encore_fail', EC_LEX.ec_msg_encore_fail);
          inEncore = false;
          if (stage) stage.removeAttribute('data-encore');
          deck('casino', 'encore', false);
        } else {
          msg('ec_msg_new', EC_LEX.ec_msg_new);
        }
        attempt += 1;                       // a fresh stream: a new melody
        dealSequence(nextLenAfterFail(len, startLen));
      });
    }

    /** The variable-ratio canon on a clear (the engine's, with a seeded local
     *  fallback so a null engine still paces the class). */
    function rewardBeat() {
      let r = null;
      try {
        if (ctx.engine && typeof ctx.engine.rewardRoll === 'function') r = ctx.engine.rewardRoll({}) || null;
      } catch (e) { r = null; }
      if (!r && rollLocal) r = rollLocal();
      if (!r || !r.fire) return;
      if (r.jackpot) {
        jackpots += 1;
        ceremonySafe('jackpot', {});
        if (capsArmed()) fireSafe('flash_burst', { variant: 'scatter' });
      } else {
        if (capsArmed()) fireSafe('gif_burst', { variant: 'single' });
      }
    }

    function stopInputPressure() {
      if (bubblesOn) { stopSafe('bubble_field'); bubblesOn = false; }
      /* NEVER stop('wash') mid-class (trap 33): a held wash is stepped DOWN by
       * re-triggering it at a lower alpha, and the deadline does the rest. */
      if (washOn && capsArmed()) { sustainSafe('wash', { variant: 'pink', alpha: 0.08, holdMs: 300 }); }
    }

    /* ==================================================================== *
     * AMBIENCE (Law III: something always breathes)
     * ==================================================================== */
    function openAmbience() {
      heat();
      if (!capsArmed()) return;
      if (!ambientOn) ambientOn = !!sustainSafe('ambient_field', { kind: tier >= 3 ? 'motes' : 'specks' });
    }
    function stopAmbience() {
      for (const k of ['ambient_field', 'bubble_field', 'wash']) stopSafe(k);
      ambientOn = false; bubblesOn = false; washOn = false;
    }

    /* ==================================================================== *
     * THE CLASS-RULES SHEET (Law IV / Deck VI) - drawn, GO-only, and the
     * shell's "Skip class tutorials" contract: with the skip ON a class still
     * explains itself ONCE PER GRADE TIER, and that memory is the GAME's
     * (gameMeta.howtoTiers), never the shell's. Impulse Control's policy.
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
        if (list.indexOf(tier) >= 0) return;
        list.push(tier);
        if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
          ctx.store.mergeGameMeta(GAME_KEY, { howtoTiers: list });
        }
      } catch (e) { /* best effort - the sheet just shows again next time */ }
    }
    function hideHowto() {
      if (!howtoEl) return;
      try { howtoEl.remove(); } catch (e) { /* noop */ }
      howtoEl = null;
    }
    function howto(onDone) {
      const seen = howtoSeenTiers();
      const skip = (ctx.dev === true && spec && spec.devSkipHowto === true)
        || (ctx.hideTutorial === true && seen.indexOf(tier) >= 0);
      if (skip) { onDone(); return; }
      howtoEl = el('div', 'g-ec-howto');
      howtoEl.setAttribute('role', 'dialog');
      howtoEl.appendChild(el('h3', 'g-ec-howto-title', t('ec_howto_title', EC_LEX.ec_howto_title)));
      const rows = el('div', 'g-ec-howto-rows');
      const drawRow = (art, key, fallback) => {
        const row = el('div', 'g-ec-howto-row');
        const cell = el('span', 'g-ec-howto-art');
        cell.setAttribute('data-art', art);
        cell.setAttribute('aria-hidden', 'true');
        row.appendChild(cell);
        row.appendChild(el('p', 'g-ec-howto-line', t(key, fallback)));
        rows.appendChild(row);
      };
      drawRow('watch', 'ec_howto_watch', EC_LEX.ec_howto_watch);
      drawRow('repeat', 'ec_howto_repeat', EC_LEX.ec_howto_repeat);
      /* The decoy line only appears at the tiers that deal decoys - a rule
       * sheet that explains a mechanic the class cannot show is noise. */
      if ((PLAYTEST.DECOY_MAX[tier] || 0) > 0) drawRow('decoy', 'ec_howto_decoy', EC_LEX.ec_howto_decoy);
      howtoEl.appendChild(rows);
      const go = el('button', 'g-ec-howto-go', t('ec_howto_go', EC_LEX.ec_howto_go));
      go.setAttribute('type', 'button');
      try { go.type = 'button'; } catch (e) { /* the double has no button semantics */ }
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
      try { go.focus(); } catch (e) { /* noop */ }
    }

    /* ==================================================================== *
     * THE CLOCK + THE BELL
     * ==================================================================== */
    function startClock() {
      lastTick = Date.now();
      clockId = every(250, () => {
        if (ended) return;
        const now = Date.now();
        const dt = now - lastTick;
        lastTick = now;
        elapsedMs += dt / Math.max(0.0001, timeScale);
        if (clockChip) clockChip.textContent = clockText();
        const left = secLeft();
        if (!bellOn && left <= PLAYTEST.BELL_WARN_SEC && elapsedMs < budgetMs) {
          bellOn = true;
          deck('casino', 'bell', true);
          deck('pressure', 'beat', 'bell');
          msg('ec_msg_bell_warn', EC_LEX.ec_msg_bell_warn);
          tone('sting', 0.4, 1);
        }
        if (elapsedMs >= budgetMs) { stopClock(); run(bell); }
      });
    }
    function stopClock() { if (clockId) { clearTimer(clockId); clockId = 0; } }

    function bell() {
      if (ended || dead) return;
      finish();
    }

    /* ==================================================================== *
     * THE END - endClass exactly once, and never with a grade of our own.
     * ==================================================================== */
    function finish() {
      if (ended) return;
      ended = true;
      inputOpen = false;
      busy = true;
      disarmInputWindow();
      stopClock();
      clearPads();
      stopAmbience();
      setPhase('ended');
      deck('casino', 'dimOut');
      deck('pressure', 'stop');
      deck('trickster', 'stop');
      try { if (ctx.engine) ctx.engine.setHeat(0); } catch (e) { /* noop */ }

      const meanLatencyMs = latencyCount > 0 ? (latencySum / latencyCount) : 0;
      const stepMs = stepMsFor(tier, 1);
      const graded = compositeFor({
        gradeTier: tier,
        bestLen,
        correct: correctPresses,
        presses,
        meanLatencyMs,
        stepMs,
        decoysPresented,
        decoysResisted,
      });
      const gates = hardGates(encoreUsed);
      const fx = flavorXp(bestLen, tier);

      const lifetimeAfter = Math.max(lifetimeBefore, bestLen);
      try {
        if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
          const prior = (typeof ctx.store.gameMeta === 'function' ? (ctx.store.gameMeta(GAME_KEY) || {}) : {});
          ctx.store.mergeGameMeta(GAME_KEY, {
            bestLen: lifetimeAfter,
            plays: Math.max(0, Number(prior.plays) || 0) + 1,
            lastLen: bestLen,
            lastSeed: seed,
            lastPlayedAt: Date.now(),
            decoysResisted: Math.max(0, Number(prior.decoysResisted) || 0) + decoysResisted,
            sequencesCleared: Math.max(0, Number(prior.sequencesCleared) || 0) + sequencesCleared,
          });
        }
      } catch (e) { say('meta merge failed (the class still grades): ' + ((e && e.message) || e)); }

      renderEnd(graded, meanLatencyMs, stepMs, lifetimeAfter);

      const report = { metrics: { composite: graded.composite }, hardGates: gates, flavorXp: fx };
      lastReport = Object.assign({}, report, {
        inputs: {
          tier, seed, retake, startLen, bestLen, bestReach, lifetimeBefore, lifetimeAfter,
          sequencesDealt, sequencesCleared, fails, timeouts, presses, correctPresses,
          bestStreak, decoysPresented, decoysResisted, decoysTaken,
          meanLatencyMs, stepMs, encoreUsed, elapsedMs, terms: graded.terms, sLen: graded.sLen,
        },
      });
      try { lastSnapshot = instance.snapshot(); } catch (e) { /* diagnostics only */ }
      say('class over: best echo ' + bestLen + ' (lifetime ' + lifetimeAfter + '), '
        + sequencesCleared + '/' + sequencesDealt + ' held, ' + correctPresses + '/' + presses + ' presses, '
        + 'decoys ' + decoysResisted + '/' + decoysPresented + (encoreUsed ? ', ENCORE' : '')
        + ' -> composite ' + graded.composite.toFixed(3));

      after(reduced ? PLAYTEST.END_HOLD_MS_REDUCED : PLAYTEST.END_HOLD_MS, () => {
        if (reported) return;
        reported = true;
        try { ctx.endClass(report); } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
      });
    }

    function renderEnd(graded, meanLatencyMs, stepMs, lifetimeAfter) {
      if (!endEl) return;
      endEl.textContent = '';
      endEl.hidden = false;
      endEl.appendChild(el('h3', 'g-ec-end-title', t('ec_end_title', EC_LEX.ec_end_title)));
      const row = (cls, k, v) => {
        const r = el('div', 'g-ec-end-row' + (cls ? ' ' + cls : ''));
        r.appendChild(el('span', 'g-ec-end-k', k));
        r.appendChild(el('span', 'g-ec-end-v', v));
        endEl.appendChild(r);
        return r;
      };
      const best = row('g-ec-end-best', t('ec_end_best', EC_LEX.ec_end_best), String(bestLen));
      best.setAttribute('data-len', String(bestLen));
      row('', t('ec_end_sequences', EC_LEX.ec_end_sequences), sequencesCleared + ' / ' + sequencesDealt);
      row('', t('ec_end_accuracy', EC_LEX.ec_end_accuracy),
        Math.round((presses > 0 ? correctPresses / presses : 0) * 100) + '%');
      row('', t('ec_end_tempo', EC_LEX.ec_end_tempo), Math.round(graded.terms.tempo * 100) + '%');
      row('', t('ec_end_streak', EC_LEX.ec_end_streak), String(bestStreak));
      if (decoysPresented > 0) {
        row('', t('ec_end_decoys', EC_LEX.ec_end_decoys), decoysResisted + ' / ' + decoysPresented);
      }
      if (encoreUsed) {
        row('g-ec-end-encore', t('ec_end_encore', EC_LEX.ec_end_encore), t('ec_end_yes', EC_LEX.ec_end_yes));
      }
      const tail = el('div', 'g-ec-end-tail');
      tail.setAttribute('data-len', String(lifetimeAfter));
      if (newRecord) tail.appendChild(el('span', 'g-ec-end-record', t('ec_end_record', EC_LEX.ec_end_record)));
      tail.appendChild(el('p', 'g-ec-end-line', t('ec_end_line', EC_LEX.ec_end_line)));
      endEl.appendChild(tail);
    }

    /* ---- assets (never block a draw) ------------------------------------ */
    function claimAssets() {
      Promise.resolve()
        .then(() => ctx.assets.claim({ loops: 6, targets: 0, stills: 6, canvasSafe: false }))
        .then((p) => {
          if (dead || !p || typeof p.next !== 'function') return;
          pool = p;
          run(dressFaces);
        })
        .catch((e) => say('asset claim failed - plain pad faces: ' + ((e && e.message) || e)));
    }

    /* ==================================================================== *
     * THE MODULE INSTANCE
     * ==================================================================== */
    const instance = {
      start(classSpec) {
        spec = classSpec || { gradeTier: 1, seed: GAME_KEY + '|none', timeBudgetSec: 105 };
        tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        seed = String(spec.seed == null ? GAME_KEY : spec.seed);
        budgetMs = Math.max(20000, (Number(spec.timeBudgetSec) || 105) * 1000);
        retake = !!spec.retake;
        reduced = probeReduced(ctx);
        audible = ctx.audioAudible !== false;
        attempt = 0;
        encoreArmed = true;
        encoreUsed = false;
        inEncore = false;
        ratchet = 0;
        newRecord = false;
        faceUrls.clear();
        faceKind = (reduced || motionLevelOf() <= 1) ? 'still' : 'loop';

        try {
          lifetimeBefore = Math.max(0, Math.floor(Number((ctx.store.gameMeta(GAME_KEY) || {}).bestLen) || 0));
        } catch (e) { lifetimeBefore = 0; }
        startLen = warmStartLen(lifetimeBefore, tier);
        curLen = startLen;

        subRoll = makeTaggedRoll(seed + '|ec-sub');
        rollLocal = (() => {
          const roll = makeTaggedRoll(seed + '|ec-vr');
          return () => {
            const base = 0.30 + 0.30 * currentHeat;
            const chance = Math.min(1, base + Math.min(PLAYTEST.STREAK_CAP, clearStreak) * 0.03);
            const r = roll('fire');
            const fire = r < chance;
            return { fire, jackpot: fire && roll('jack') >= 0.85, nearMiss: !fire && r < chance + 0.08 };
          };
        })();

        try { injectEchoStyle(); } catch (e) { say('style inject failed (class unaffected): ' + ((e && e.message) || e)); }
        buildDom();
        assignWords();
        paintHud();

        const capsOk = capsArmed();
        try {
          casino = createEcCasino({
            seed, tier, stage, board: ring, ring, hud, backdrop,
            timers: deckTimers, reduced, capsOk, t, engine: deckEngine, assets: deckAssets,
            padEl: (i) => padEls[i] || null,
            pads: () => padEls.slice(),
            padCount: PLAYTEST.PADS,
            log: say,
          }) || null;
        } catch (e) { casino = null; say('casino refused: ' + ((e && e.message) || e)); }
        try {
          trickster = createEcTrickster({
            seed, tier, timers: deckTimers, reduced, capsOk,
            stage, board: ring, hud, backdrop, engine: deckEngine, assets: deckAssets,
            budgetSec: Math.round(budgetMs / 1000),
            coarse: !!(ctx.platform && ctx.platform.isTouch),
            isHalted: () => dead || paused || ended || busy,
            /* READ-ONLY views of the truth. A deck may READ the ledger (the
             * Ghost Cursor lures to a pad ADJACENT to the due one, which is
             * only a lie worth telling if it knows the truth); it may never
             * write one. Law I is kept because nothing here is a setter. */
            phase: () => phase(),
            nextPad: () => ((inputOpen && round && round.seq[expectIdx] != null) ? round.seq[expectIdx] : -1),
            wordsOn: () => faceMode === 'words',
            t,
            stats: () => ({
              len: curLen, best: Math.max(bestLen, lifetimeBefore), streak: pressStreak,
              secLeft: secLeft(), sequences: sequencesDealt, expect: expectIdx, inputOpen,
            }),
            chipEl: (which) => (which === 'clock' ? clockChip
              : which === 'best' ? bestChip
                : which === 'streak' ? streakChip
                  : which === 'ring' ? timerEl : lenChip),
            chipText,
            /* THE LIE TARGET (Unreliable Label): a pad's WORD may briefly wear
             * another pad's word. The GLYPH is the truth and the deck is never
             * handed it; paintWords() restores the truth on every deal. */
            padEl: (i) => padEls[i] || null,
            pads: () => padEls.slice(),
            wordEl: (i) => wordNodeOf(padEls[i] || null),
            wordText: padWordText,
            restoreWords: paintWords,
            ring,
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
        } catch (e) { trickster = null; say('trickster refused: ' + ((e && e.message) || e)); }
        try {
          pressure = createEcPressure({
            seed,
            gradeTier: tier,
            tier,
            reduced,
            motionLevel: motionLevelOf(),
            stage,
            ring,
            backdrop,
            /* CHROME ONLY (Law I/II): the tremor rides the HUD and the stage
             * frame, never a pad - a pad that moves is a moved hitbox. */
            chrome: [hud, lenChip, clockChip, streakChip, bestChip].filter(Boolean),
            hud: { len: lenChip, clock: clockChip, streak: streakChip, best: bestChip },
            engine: deckEngine,
            assets: deckAssets,
            timers: deckTimers,
            capsOk: capsArmed,
            log: say,
          }) || null;
        } catch (e) { pressure = null; say('pressure refused: ' + ((e && e.message) || e)); }

        bindInput();
        claimAssets();
        startClock();

        every(PLAYTEST.STALL_TICK_MS, () => {
          if (ended || !inputOpen) return;
          stallMs += PLAYTEST.STALL_TICK_MS;
          if (stallMs >= PLAYTEST.STALL_MS) deck('trickster', 'stalled', stallMs);
        });

        msg('ec_brief', EC_LEX.ec_brief);
        howto(() => {
          if (dead || ended) return;
          after(reduced ? PLAYTEST.BRIEF_MS_REDUCED : PLAYTEST.BRIEF_MS, () => {
            if (dead || ended) return;
            deck('casino', 'start');
            deck('pressure', 'start');
            deck('trickster', 'start');
            openAmbience();
            busy = false;
            dealSequence(startLen);
          });
        });

        liveClass = instance;
        lastReport = null;
        lastSnapshot = null;
        say('tier ' + tier + ', ' + alphabetFor(tier) + ' of ' + PLAYTEST.PADS + ' pads in play, warm start '
          + startLen + ' (lifetime best ' + lifetimeBefore + '), budget ' + Math.round(budgetMs / 1000) + 's'
          + (reduced ? ', reduced' : '') + (audible ? '' : ', SILENT (visual tells)')
          + (retake ? ', RETAKE' : '') + ', faces ' + faceMode);
      },

      pause() {
        if (paused) return;
        paused = true;
        deck('pressure', 'pause');
        deck('trickster', 'pause');
        deck('casino', 'pause');
        if (stage) stage.classList.add('suspended');
      },

      resume() {
        if (!paused) return;
        paused = false;
        if (stage) stage.classList.remove('suspended');
        deck('pressure', 'resume');
        deck('trickster', 'resume');
        deck('casino', 'resume');
        lastTick = Date.now();
        lastPressAt = Date.now();
        const q = deferred.splice(0);
        for (const fn of q) run(fn);
      },

      /** The shell owns the overlay and the engine's suspend; we just freeze. */
      suspend(on) { if (on) instance.pause(); else instance.resume(); },

      destroy() {
        dead = true;
        stopClock();
        disarmInputWindow();
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
        if (pool && typeof pool.release === 'function') { try { pool.release(); } catch (e) { /* noop */ } }
        pool = null;
        padTimers.clear();
        padEls.length = 0;
        timerEl = null;
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
        if (liveClass === instance) liveClass = null;
      },

      /* -------- test / diagnostics seams (never read by the shell) -------- */
      /** Press a pad exactly as the finger would. */
      press(i) { press(i, 'tap'); },
      /** The TRUE chip text - what the trickster restores after a lie. */
      chipText(which) { return chipText(which); },
      /** The live round (the suite asserts the dealt plan against sequence.js). */
      round() { return round; },
      /** End the class as the bell would (the suite never waits 105s). */
      ringBell() { run(bell); },

      snapshot() {
        return {
          tier, seed, retake, reduced, audible, faceMode,
          startLen, curLen, attempt, expectIdx, stepIdx, inputOpen, busy, paused, dead, ended, reported,
          phase: phase(),
          encoreArmed, encoreUsed, inEncore, ratchet,
          bestLen, bestReach, lifetimeBefore,
          sequencesDealt, sequencesCleared, fails, timeouts,
          presses, correctPresses, pressStreak, bestStreak, clearStreak,
          decoysPresented, decoysResisted, decoysTaken,
          latencySum, latencyCount, jackpots, nearMisses, subFlashes,
          currentHeat, bellOn, elapsedMs, budgetMs, secLeft: secLeft(),
          padStates: padEls.map((p) => (p ? p.getAttribute('data-state') : null)),
          padWords: padWords.slice(),
          faces: faceUrls.size,
          round: round ? {
            len: round.len, seq: round.seq.slice(), decoys: round.decoys.slice(),
            steps: round.steps.length, stepMs: round.stepMs, windowMs: round.windowMs, encore: round.encore,
          } : null,
          howtoUp: !!howtoEl,
          stage, ring, hud, well, msgEl, endEl, lenChip, clockChip, streakChip, bestChip,
          pads: padEls.slice(),
          casino: casino && typeof casino.diagnostics === 'function' ? (() => { try { return casino.diagnostics(); } catch (e) { return null; } })() : null,
          trickster: trickster && typeof trickster.diagnostics === 'function' ? (() => { try { return trickster.diagnostics(); } catch (e) { return null; } })() : null,
          pressure: pressure && typeof pressure.diagnostics === 'function' ? (() => { try { return pressure.diagnostics(); } catch (e) { return null; } })() : null,
        };
      },
    };

    function motionLevelOf() {
      try { const v = ctx.motion && ctx.motion.motionLevel; return Number.isFinite(Number(v)) ? Number(v) : 2; }
      catch (e) { return 2; }
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
