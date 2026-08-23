/* ============================================================================
 * games/misdirection/index.js - MISDIRECTION (family 'tracking', 120s).
 *
 * The shell game where the app is the con artist. One shell lifts and shows
 * the target; the arc shuffles while the Distraction Engine attacks exactly
 * the moments that matter; you point at the shell you followed; and then the
 * house asks the only question it cares about - bank it, or ride it.
 *
 * THE ROUND (6-10s, ~10-14 per class, the class ends on the BELL):
 *   reveal   the true shell lifts, previewMs by grade tier (1.6s -> 0.7s)
 *   shuffle  the seeded chain runs; decoy shells lift as bait; at the top
 *            tiers one link happens UNANIMATED under a glitch_swap inside a
 *            wash peak, and a blackout beat rides the wash to its cap
 *   pick     a 4s window - tap a shell, or the bound pick1..pick5 verbs
 *   stake    a correct pick offers bank / ride (md_stake_mode may answer for
 *            you); ride doubles the pot and raises the EFFECT tier one notch,
 *            never the shell count, the swap rate or the preview time
 *   remedial after two misses in a row: long look, clean shuffle, full pot,
 *            and it can never fire twice in a row
 *
 * ---------------------------------------------------------------------------
 * THE TRACKABILITY INVARIANT is this game's spine and it lives in shuffle.js
 * (`verifyRound`): the chain is dealt from the seed BEFORE any effect is
 * chosen, at most ONE link that actually moves the target may be occluded, a
 * truthful tell survives every occlusion, and decoys never lift the true
 * shell. This file's job is to keep that honest in the DOM: the tell is
 * painted on the two shells AND cued through audio (so `ctx.audioAudible ===
 * false` still leaves a visual), and the swap is applied to the model exactly
 * once whether the engine's `onSwap` midpoint arrives or the backstop fires.
 *
 * GREED IS UPWARD ONLY (contract ruling): a busted pot costs the player
 * nothing but the pot. grade.js builds the composite out of accuracy and
 * latency, and the deepest BANKED ride is an additive bonus on top.
 *
 * LAWS THIS FILE KEEPS:
 *   I   the ledger is honest - the target's slot, the pot, the streak, the
 *       clock and the 4s pick window are computed here and never routed
 *       through a deck. The trickster may lie on a `.g-md-tag`; truth is
 *       repainted the moment the round moves on.
 *   II  input honest - the shells are real buttons whose hitboxes only ever
 *       move because the GAME slid them; every engine one-shot over the table
 *       is welded clickSafe (fireSafe) and no deck may steal a tap.
 *   III nothing is still - the arc breathes through the decks even at idle.
 *   IV  images over text - the drawn class-rules sheet is style.js's, shown
 *       through the shell's tutorial policy (hideTutorial + howtoTiers).
 *   V   everything seeded - the chain, the occlusions, the decoys, the decks.
 *       A retake replays the identical table.
 *   VI  the exits are sacred - pause/resume/suspend/destroy freeze the round,
 *       a suspend VOIDS the live round out of every denominator and force-
 *       banks the pot, reducedMotion degrades, bgIntensity 0 disarms the decks.
 *   VII every string is ctx.lexicon(key, fallback) over lex.js MD_LEX.
 *
 * WHAT THIS FILE DOES NOT OWN: grades (core/grades.js via ctx.endClass), XP
 * (C#), the tier (registry + meta), effect strengths (the engine's ceiling
 * rule), the look and the shell transforms (style.js - we only ever write
 * --slot / --x), the lighting (casino.js), the lies (trickster.js) and the
 * CCP-effects ladder (pressure.js). The pure model is shuffle.js and grade.js.
 *
 * ENGINE TARGETING NOTES:
 *   - glitch_swap adds `.ae-glitch{position:relative;animation}` to its
 *     targets, so it may NEVER touch a `.g-md-shell` (style.js owns that
 *     transform). It targets the shells' `.g-md-face` nodes.
 *   - row_drift writes an INLINE transform on its targets, so it takes
 *     `.g-md-arc` and nothing else. style.js must not own the arc's own
 *     transform; shells carry theirs.
 *   - NEVER `engine.stop('wash')` (web CLAUDE.md trap 33) - a wash is stepped
 *     down by re-triggering it at a whisper alpha.
 *
 * THE DECKS ARE OPTIONAL. style/casino/trickster/pressure are loaded through
 * guarded dynamic imports (the shell's own `loadOptional` posture): a deck
 * that is missing or throws leaves a null and the class still runs, silent.
 * Every deck call goes through `deck()`, which is null-safe and try/catch.
 * ==========================================================================*/

import { MD_LEX } from './lex.js';
import {
  PLAYTEST, buildRound, simulate, verifyRound, dialsFor, potAfter, heatFor,
  stakeModeFrom, skinFrom, clamp01,
} from './shuffle.js';
import { compositeFor, hardGates, flavorXp } from './grade.js';

const GAME_KEY = 'misdirection';

/** A url an <img> cannot show (a webm/mp4 loop). Mirrors engine/util.js
 *  VIDEO_URL_RE; games never import the engine, so the rule is repeated. */
const VIDEO_URL_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;

/** How many decoy stills we hold before recycling (assetNeeds.stills = 6). */
const DECOY_POOL = 6;

/* ---------------------------------------------------------------------------
 * THE DECKS - guarded optional imports. A parallel agent owns these four
 * files; until they land (or if one throws) every hook below is null and the
 * game runs undressed. Top-level await keeps the rest of the module fully
 * synchronous, which is what lets start() build the decks in one breath.
 * ------------------------------------------------------------------------ */
const deckLoadNotes = [];

async function loadModule(path) {
  try { return await import(path); }
  catch (e) { deckLoadNotes.push(path + ': ' + ((e && e.message) || e)); return null; }
}
function pickFn(mod, names) {
  if (!mod) return null;
  for (const n of names) { if (typeof mod[n] === 'function') return mod[n]; }
  return null;
}

const styleMod = await loadModule('./style.js');
const casinoMod = await loadModule('./casino.js');
const tricksterMod = await loadModule('./trickster.js');
const pressureMod = await loadModule('./pressure.js');

/** style.js: the whole look, and (Deck VI) the drawn class-rules sheet. */
const injectStyle = pickFn(styleMod, ['injectMisdirectionStyle', 'injectMdStyle', 'injectStyle', 'default']);
/** The sheet BUILDER: it draws into `host` and returns the node; the POLICY
 *  (when it shows, hideTutorial, howtoTiers) and the removal are this file's. */
const styleBuildHowto = pickFn(styleMod, ['buildMdHowto', 'buildHowto', 'showHowto', 'showMdHowto']);
const styleHideHowto = pickFn(styleMod, ['hideHowto', 'hideMdHowto']);
const createMdCasino = pickFn(casinoMod, ['createMdCasino', 'createMisdirectionCasino', 'default']);
const createMdTrickster = pickFn(tricksterMod, ['createMdTrickster', 'createMisdirectionTrickster', 'default']);
const createMdPressure = pickFn(pressureMod, ['createMdPressure', 'createMisdirectionPressure', 'default']);
if (styleMod && !injectStyle) deckLoadNotes.push('style.js: no inject export yet');
if (casinoMod && !createMdCasino) deckLoadNotes.push('casino.js: no factory export yet');
if (tricksterMod && !createMdTrickster) deckLoadNotes.push('trickster.js: no factory export yet');
if (pressureMod && !createMdPressure) deckLoadNotes.push('pressure.js: no factory export yet');

/* ---------------------------------------------------------------------------
 * diagnostics seams (the shell never reads these; the harness does)
 * ------------------------------------------------------------------------ */
let liveClass = null;
let lastReport = null;
let lastSnapshot = null;

/** Test seam: the scratch harness compresses the clock. Production = 1. */
let timeScale = 1;
export function setTimeScale(f) { const v = Number(f); timeScale = Number.isFinite(v) && v > 0 ? v : 1; }
export function getTimeScale() { return timeScale; }
function scaled(ms) { return Math.max(0, Math.round((Number(ms) || 0) * timeScale)); }

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

export default {
  key: GAME_KEY,
  family: 'tracking',
  meaty: false,
  flagship: false,
  timeBudgetSec: 120,
  title: 'Misdirection',

  manifest: {
    /* flash_burst and gif_burst are declared ONLY as clickSafe decoration over
     * a tap-precision table (DECISIONS #9) - fireSafe() welds that on at every
     * call site, so neither can ever eat a pick. */
    effectsConsumed: [
      'wash', 'glitch_swap', 'row_drift', 'bubble_field', 'sub_flash',
      'audio_trigger', 'flash_burst', 'gif_burst', 'ambient_field',
    ],
    /* 1 target loop + 3 loops + 6 decoy stills. Everything is DOM - nothing is
     * ever drawn into a canvas - so the provider may serve remote media, and a
     * missing still falls back to a CSS-FILTERED TWIN of the target. */
    assetNeeds: { loops: 3, targets: 1, stills: 6, canvasSafe: false },
    boardSizes: null,
    keybinds: [
      { verb: 'pick1', label_key: 'md_key_pick1', default: '1' },
      { verb: 'pick2', label_key: 'md_key_pick2', default: '2' },
      { verb: 'pick3', label_key: 'md_key_pick3', default: '3' },
      { verb: 'pick4', label_key: 'md_key_pick4', default: '4' },
      { verb: 'pick5', label_key: 'md_key_pick5', default: '5' },
    ],
    settings: [
      {
        key: 'md_stake_mode', kind: 'enum', values: ['ask', 'bank', 'ride'], default: 'ask',
        label_key: 'md_stake_mode', hint_key: 'md_stake_mode_hint',
      },
      {
        key: 'md_shell_skin', kind: 'enum', values: ['themed', 'minimal', 'contrast'], default: 'themed',
        label_key: 'md_shell_skin', hint_key: 'md_shell_skin_hint',
      },
    ],
    peek: false,
  },

  create(ctx) {
    const t = (key, fallback) => {
      const fb = fallback == null ? (MD_LEX[key] == null ? key : MD_LEX[key]) : fallback;
      try { const v = ctx.lexicon(key, fb); return v == null ? fb : v; } catch (e) { return fb; }
    };
    const say = (m) => { try { ctx.log('[md] ' + m); } catch (e) { /* noop */ } };
    const dev = ctx && ctx.dev === true;

    /* ---- lifecycle flags ------------------------------------------------ */
    let dead = false;
    let paused = false;
    let ended = false;
    let reported = false;
    let busy = true;                  // input closed until the table opens

    /* ---- class state ---------------------------------------------------- */
    let spec = null;
    let seed = '';
    let tier = 1;
    let reduced = false;
    let retake = false;
    let budgetMs = 120000;
    let stakeMode = 'ask';
    let skin = 'themed';
    let devSkipHowto = false;
    let pool = null;
    let capsOkNow = true;

    let casino = null;
    let trickster = null;
    let pressure = null;

    /* ---- the ledger (Law I: computed here, never by a deck) ------------- */
    const rounds = [];                // the graded rows handed to grade.js
    let roundIndex = -1;
    let plan = null;                  // the live round's seeded plan
    let roundToken = 0;               // every round timer checks this
    let idAt = [];                    // slot -> shell id
    let targetId = -1;
    let pickOpenAt = 0;
    let picked = -1;
    let roundVoided = false;
    let roundLive = false;
    let awaitingStake = false;
    let lastWasRemedial = false;
    let consecutiveMisses = 0;
    let streak = 0;
    let bestStreak = 0;
    let pot = { live: 0, rideDepth: 0, banked: 0, deepestBanked: 0, event: '' };
    let bankedBeforeFirstMiss = false;
    let sawFirstMiss = false;
    let voidedRounds = 0;
    let jackpots = 0;
    let blindDealt = 0;
    let blindHits = 0;
    let currentHeat = 0;
    let bellOn = false;
    let driftOn = false;
    let bubblesOn = false;
    let ambientOn = false;
    let stallMs = 0;
    let subIdx = 0;
    let rewardRoll = null;

    /* ---- media ---------------------------------------------------------- */
    let targetUrl = '';
    let targetIsVideo = false;
    const decoyUrls = [];
    let decoyIdx = 0;
    let mediaLogged = false;

    /* ---- clock ---------------------------------------------------------- */
    let clockId = 0;
    let lastTick = 0;
    let elapsedMs = 0;

    /* ---- dom ------------------------------------------------------------ */
    let stage = null; let backdrop = null; let hud = null; let table = null; let arc = null;
    let stakeEl = null; let bankBtn = null; let rideBtn = null;
    let msgEl = null; let well = null; let endEl = null;
    let roundChip = null; let clockChip = null; let potChip = null; let streakChip = null;
    let howtoNode = null;
    const shellEls = [];
    let stallTimer = 0;
    let subTimer = 0;
    let pickTicker = 0;
    let keyOff = [];

    /* ==================================================================== *
     * TIMERS - every step goes through run() so a suspend freezes the round
     * mid-shuffle and a resume finishes it. `every` simply skips while paused.
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

    /** A round step that dies the moment its round is over (void, bell, next). */
    function step(token, fn) {
      return () => { if (dead || ended || token !== roundToken) return; fn(); };
    }

    /* ==================================================================== *
     * ENGINE - one wrapper, the input-trust law welded on.
     * ==================================================================== */
    function fireSafe(kind, opts) {
      if (dead || paused || !ctx.engine) return null;
      const o = Object.assign({}, opts || {});
      if (kind === 'flash_burst' || kind === 'gif_burst') {
        o.clickSafe = true;            // decoration only over a tap-precision arc
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
    function stopSafe(kind) {
      /* NEVER for 'wash' - trap 33. washDown() steps it instead. */
      if (kind === 'wash') { washDown(); return; }
      try { if (ctx.engine) ctx.engine.stop(kind); } catch (e) { /* noop */ }
    }
    /** The engine as a deck sees it: welded primitives + a READ of the clamped
     *  channel vector (THE CEILING RULE - a deck asks, it never raises). */
    const deckEngine = {
      fire: fireSafe,
      sustain: sustainSafe,
      stop: stopSafe,
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
    function motionLevelOf() {
      try { const v = Number(ctx.motion && ctx.motion.motionLevel); return Number.isFinite(v) ? v : 2; }
      catch (e) { return 2; }
    }
    /** A cue through the engine; level never above the tier's audio ceiling. */
    function tick(name, level, extra) {
      const ceil = PLAYTEST.AUDIO_CEIL[tier] || 0.45;
      const lv = Math.min(ceil, level == null ? 0.4 : level);
      fireSafe('audio_trigger', Object.assign({ name, level: lv }, extra || {}));
    }

    /* ---- the decks, null-safe ------------------------------------------- */
    function deck(which, method, ...args) {
      const d = which === 'casino' ? casino : which === 'pressure' ? pressure : trickster;
      if (!d || typeof d[method] !== 'function') return undefined;
      try { return d[method](...args); } catch (e) { say(which + '.' + method + ' threw: ' + ((e && e.message) || e)); return undefined; }
    }

    /* ==================================================================== *
     * HEAT - the class's own ladder (streak + ride depth), capped by tier
     * ==================================================================== */
    function heat() {
      const h = heatFor(streak, pot.rideDepth, tier);
      currentHeat = h;
      try { if (ctx.engine) ctx.engine.setHeat(h); } catch (e) { /* engine is optional */ }
      deck('casino', 'setHeat', h);
      deck('trickster', 'setHeat', h);
      deck('pressure', 'setHeat', h);
      deck('pressure', 'setStreak', streak);
    }

    /* ==================================================================== *
     * DOM (the contract's exact shape)
     * ==================================================================== */
    function setPhase(p) { if (stage) stage.setAttribute('data-phase', p); }
    function setBeat(b) { if (stage) stage.setAttribute('data-beat', String(b || '')); }
    function msg(key, fallback) { if (msgEl) msgEl.textContent = t(key, fallback); }

    function buildDom(n) {
      const root = ctx.root;
      root.textContent = '';
      stage = el('div', 'g-md-stage');
      stage.setAttribute('data-phase', 'briefing');
      stage.setAttribute('data-skin', skin);
      stage.setAttribute('data-beat', '');
      stage.setAttribute('data-shells', String(n));
      if (reduced) stage.setAttribute('data-reduced', '1');
      stage.style.setProperty('--md-n', String(n));

      backdrop = el('div', 'g-md-backdrop');
      backdrop.setAttribute('aria-hidden', 'true');
      backdrop.style.pointerEvents = 'none';
      stage.appendChild(backdrop);

      hud = el('div', 'g-md-hud');
      roundChip = el('span', 'g-md-chip g-md-round', '1');
      roundChip.setAttribute('aria-label', t('md_chip_round', MD_LEX.md_chip_round));
      clockChip = el('span', 'g-md-chip g-md-clock', mmss(budgetMs / 1000));
      clockChip.setAttribute('aria-label', t('md_chip_clock', MD_LEX.md_chip_clock));
      potChip = el('span', 'g-md-chip g-md-pot', '0');
      potChip.setAttribute('aria-label', t('md_chip_pot', MD_LEX.md_chip_pot));
      streakChip = el('span', 'g-md-chip g-md-streak', '0');
      streakChip.setAttribute('aria-label', t('md_chip_streak', MD_LEX.md_chip_streak));
      hud.appendChild(roundChip);
      hud.appendChild(clockChip);
      hud.appendChild(potChip);
      hud.appendChild(streakChip);
      if (retake) hud.appendChild(el('span', 'g-md-chip g-md-retake', t('md_retake', MD_LEX.md_retake)));
      stage.appendChild(hud);

      table = el('div', 'g-md-table');
      arc = el('div', 'g-md-arc');
      arc.style.setProperty('--n', String(n));
      shellEls.length = 0;
      for (let i = 0; i < n; i++) {
        const shell = el('button', 'g-md-shell');
        shell.setAttribute('type', 'button');
        try { shell.type = 'button'; } catch (e) { /* the DOM double has no button semantics */ }
        shell.setAttribute('data-id', String(i));
        shell.setAttribute('data-slot', String(i));
        shell.style.setProperty('--slot', String(i));
        shell.style.setProperty('--x', n > 1 ? (i / (n - 1)).toFixed(4) : '0.5');
        shell.style.setProperty('--md-x', n > 1 ? (i / (n - 1)).toFixed(4) : '0.5');  // registered twin (iOS: unregistered vars in calc() do not transition)
        shell.appendChild(el('span', 'g-md-lid'));
        const face = el('span', 'g-md-face');
        const media = el('img', 'g-md-media');
        armMedia(media, false);
        face.appendChild(media);
        shell.appendChild(face);
        shell.appendChild(el('span', 'g-md-tag', String(i + 1)));
        /* The handler closes over the ELEMENT, not over currentTarget: the
         * slot is read off the node at press time, so a shell that has slid
         * three times still answers for the slot it is standing in. */
        const onClick = (ev) => {
          try { if (ev && typeof ev.preventDefault === 'function') ev.preventDefault(); } catch (e) { /* noop */ }
          const slot = Number(shell.getAttribute('data-slot'));
          run(() => tryPick(slot));
        };
        shell._mdOnClick = onClick;
        shell.addEventListener('click', onClick);
        shellEls.push(shell);
        arc.appendChild(shell);
      }
      table.appendChild(arc);
      stage.appendChild(table);

      /* THE STAKE - two real buttons, always honest, never a modal that traps
       * the exit. They are only reachable while the stake beat is up. */
      stakeEl = el('div', 'g-md-stake');
      stakeEl.hidden = true;
      bankBtn = el('button', 'g-md-btn g-md-bank', t('md_bank', MD_LEX.md_bank));
      bankBtn.setAttribute('type', 'button');
      try { bankBtn.type = 'button'; } catch (e) { /* noop */ }
      bankBtn.addEventListener('click', () => run(() => resolveStake('bank', false)));
      rideBtn = el('button', 'g-md-btn g-md-ride', t('md_ride', MD_LEX.md_ride));
      rideBtn.setAttribute('type', 'button');
      try { rideBtn.type = 'button'; } catch (e) { /* noop */ }
      rideBtn.addEventListener('click', () => run(() => resolveStake('ride', false)));
      stakeEl.appendChild(bankBtn);
      stakeEl.appendChild(rideBtn);
      stage.appendChild(stakeEl);

      msgEl = el('p', 'g-md-msg');
      msgEl.setAttribute('aria-live', 'polite');
      stage.appendChild(msgEl);

      well = el('div', 'g-md-flashwell');
      well.setAttribute('aria-hidden', 'true');
      well.style.pointerEvents = 'none';
      stage.appendChild(well);

      endEl = el('div', 'g-md-end');
      endEl.hidden = true;
      stage.appendChild(endEl);

      root.appendChild(stage);
    }

    function shellAt(slot) {
      for (const s of shellEls) if (Number(s.getAttribute('data-slot')) === slot) return s;
      return null;
    }
    function faceOf(shell) {
      try { for (const k of shell.children || []) if (k.classList && k.classList.contains('g-md-face')) return k; }
      catch (e) { /* ignore */ }
      return null;
    }
    function tagOf(slot) {
      const s = shellAt(slot);
      if (!s) return null;
      try { for (const k of s.children || []) if (k.classList && k.classList.contains('g-md-tag')) return k; }
      catch (e) { /* ignore */ }
      return null;
    }
    /** THE TRUTH on every tag: the tag names the SLOT, which is the key that
     *  picks it. The trickster may lie here; this puts it back. */
    function paintTags() {
      for (const s of shellEls) {
        const slot = Number(s.getAttribute('data-slot')) || 0;
        try {
          for (const k of s.children || []) {
            if (k.classList && k.classList.contains('g-md-tag')) {
              const want = String(slot + 1);
              if (k.textContent !== want) k.textContent = want;
            }
          }
        } catch (e) { /* ignore */ }
        s.setAttribute('aria-label', t('md_shell_aria', MD_LEX.md_shell_aria).replace('{n}', String(slot + 1)));
      }
    }
    /** Position every shell from `idAt`. CORE writes --slot / --x; style.js
     *  owns the transform and the transition they drive. */
    function paintSlots() {
      const n = idAt.length;
      for (let slot = 0; slot < n; slot++) {
        const id = idAt[slot];
        const shell = shellEls[id];
        if (!shell) continue;
        shell.setAttribute('data-slot', String(slot));
        shell.style.setProperty('--slot', String(slot));
        shell.style.setProperty('--x', n > 1 ? (slot / (n - 1)).toFixed(4) : '0.5');
        shell.style.setProperty('--md-x', n > 1 ? (slot / (n - 1)).toFixed(4) : '0.5');  // registered twin (iOS: unregistered vars in calc() do not transition)
      }
      paintTags();
    }
    function clearShellState() {
      for (const s of shellEls) {
        s.classList.remove('is-lifted', 'is-true', 'is-picked', 'is-tell', 'is-decoy', 'is-fake', 'is-swapping');
      }
    }
    function paintHud() {
      if (roundChip) roundChip.textContent = String(roundIndex + 1);
      if (potChip) potChip.textContent = pot.live > 0 ? ('x' + pot.live) : String(pot.banked);
      if (streakChip) streakChip.textContent = String(streak);
      if (clockChip) clockChip.textContent = mmss(secLeft());
    }
    /** The TRUE chip text - what the trickster's Stat Flicker restores to. */
    function chipText(which) {
      if (which === 'round') return String(roundIndex + 1);
      if (which === 'clock') return mmss(secLeft());
      if (which === 'streak') return String(streak);
      return pot.live > 0 ? ('x' + pot.live) : String(pot.banked);
    }

    /* ==================================================================== *
     * MEDIA - ctx.assets ONLY, DOM only, never a canvas.
     * The target is ONE loop, frozen for the class. A decoy wears a pool
     * still; with no stills to draw the decoy wears a CSS-FILTERED TWIN of
     * the target (hue-rotate + mirror), which is the contract's ruling and
     * costs no canvas pass at all.
     * ==================================================================== */
    function armMedia(node, video) {
      if (!node) return;
      try {
        node.setAttribute('alt', '');
        node.setAttribute('draggable', 'false');
        node.draggable = false;
        if (video) {
          node.muted = true; node.loop = true; node.autoplay = true; node.playsInline = true;
          node.setAttribute('muted', ''); node.setAttribute('loop', '');
          node.setAttribute('autoplay', ''); node.setAttribute('playsinline', '');
          node.setAttribute('preload', 'auto');
          node.addEventListener('loadeddata', () => {
            if (dead) return;
            try { const p = node.play(); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch (e) { /* ignore */ }
          });
        } else {
          node.decoding = 'async';
        }
      } catch (e) { /* the DOM double has no media semantics; fine */ }
    }
    /** The media node that can SHOW this url: the face's <img>, swapped for a
     *  <video> when the url is a webm/mp4 loop (engine/util.js mediaEl
     *  semantics, repeated - games never import the engine). */
    function mediaNodeFor(face, url) {
      if (!face) return null;
      let cur = null;
      try { for (const k of face.children || []) if (k.classList && k.classList.contains('g-md-media')) cur = k; }
      catch (e) { /* ignore */ }
      const wantVideo = VIDEO_URL_RE.test(String(url || ''));
      const isVideo = !!(cur && String(cur.tagName || '').toUpperCase() === 'VIDEO');
      if (cur && wantVideo === isVideo) return cur;
      const next = el(wantVideo ? 'video' : 'img', 'g-md-media');
      armMedia(next, wantVideo);
      try {
        if (cur && typeof face.replaceChild === 'function') face.replaceChild(next, cur);
        else face.appendChild(next);
      } catch (e) { try { face.appendChild(next); } catch (e2) { /* ignore */ } }
      return next;
    }
    /** Dress one shell's face. `filter` is the twin's CSS lie (never canvas). */
    function dressFace(shell, url, filter) {
      const face = faceOf(shell);
      if (!face) return;
      if (!url) { clearFace(shell); return; }
      const node = mediaNodeFor(face, url);
      if (!node) return;
      try {
        if (node.getAttribute('src') !== url) node.setAttribute('src', url);
        node.style.setProperty('filter', filter || 'none');
        node.style.setProperty('transform', filter && filter.indexOf('mirror') >= 0 ? 'scaleX(-1)' : 'none');
      } catch (e) { /* ignore */ }
      face.setAttribute('data-dressed', '1');
    }
    function clearFace(shell) {
      const face = faceOf(shell);
      if (!face) return;
      try {
        for (const k of Array.from(face.children || [])) {
          if (k.classList && k.classList.contains('g-md-media')) {
            if (String(k.tagName || '').toUpperCase() === 'VIDEO') { try { k.pause(); } catch (e) { /* noop */ } }
            k.removeAttribute('src');
            try { k.style.setProperty('filter', 'none'); k.style.setProperty('transform', 'none'); } catch (e) { /* noop */ }
          }
        }
        face.setAttribute('data-dressed', '0');
      } catch (e) { /* ignore */ }
    }
    /** A decoy's face: a pool still, else the CSS-filtered twin. */
    function decoyLook(fake) {
      if (decoyUrls.length) {
        const url = decoyUrls[decoyIdx % decoyUrls.length];
        decoyIdx += 1;
        return { url, filter: 'none' };
      }
      if (!targetUrl) return { url: '', filter: 'none' };
      /* THE TWIN. A convincing fake target is a small hue shift; an ordinary
       * decoy is turned far enough that it reads as a different thing. The
       * mirror is applied through the transform, keyed off the filter word. */
      return fake
        ? { url: targetUrl, filter: 'hue-rotate(22deg) saturate(1.05)' }
        : { url: targetUrl, filter: 'hue-rotate(155deg) saturate(0.7) mirror' };
    }
    function claimAssets() {
      Promise.resolve()
        .then(() => (ctx.assets && typeof ctx.assets.claim === 'function')
          ? ctx.assets.claim({ loops: 3, targets: 1, stills: 6, canvasSafe: false })
          : null)
        .then((p) => {
          if (dead || !p || typeof p.next !== 'function') return;
          pool = p;
          dealMedia();
        })
        .catch((e) => say('asset claim failed - shells run bare: ' + ((e && e.message) || e)));
    }
    /** ONE target for the class (the dossier's per-class asset bill) plus the
     *  decoy stills. Called again if a later round still has no target. */
    function dealMedia() {
      if (!pool) return;
      if (!targetUrl) {
        try { const got = pool.next('target'); if (got && got.url) targetUrl = String(got.url); }
        catch (e) { targetUrl = ''; }
        targetIsVideo = VIDEO_URL_RE.test(targetUrl);
      }
      while (decoyUrls.length < DECOY_POOL) {
        let url = '';
        try { const got = pool.next('still'); url = got && got.url ? String(got.url) : ''; } catch (e) { url = ''; }
        /* A decoy is NEVER a video: two playing <video> nodes lock the page to
         * 30Hz (the Lost & Found measurement), and a decoy lift can overlap
         * the target's. Stills only, twins otherwise. */
        if (!url || VIDEO_URL_RE.test(url)) break;
        if (decoyUrls.indexOf(url) >= 0) break;
        decoyUrls.push(url);
      }
      if (!mediaLogged) {
        mediaLogged = true;
        say('media: target ' + (targetUrl ? (targetIsVideo ? 'loop(video)' : 'loop') : 'none')
          + ', ' + decoyUrls.length + ' decoy stills' + (decoyUrls.length ? '' : ' - twins in use'));
      }
    }

    /* ==================================================================== *
     * THE WASH - never stopped, always stepped (trap 33).
     * ==================================================================== */
    function washTo(alpha, holdMs) {
      if (!capsOkNow) return;
      sustainSafe('wash', {
        variant: 'pink',
        alpha: clamp01(alpha),
        strength: clamp01(alpha),
        holdMs: Math.max(120, scaled(holdMs || 900)),
      });
    }
    function washDown() {
      /* Step DOWN by re-triggering at a whisper - the decks' documented
       * step-down, and the only legal way out of a held wash. A room that was
       * never armed has nothing to step down, so it asks for nothing at all. */
      if (!capsOkNow) return;
      sustainSafe('wash', { variant: 'pink', alpha: 0.01, strength: 0.01, holdMs: 120 });
    }

    /* ==================================================================== *
     * THE ROUND
     * ==================================================================== */
    function secLeft() { return Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)); }

    function nextRound() {
      if (dead || ended) return;
      if (elapsedMs >= budgetMs) { bell(); return; }
      roundToken += 1;
      const token = roundToken;
      roundIndex += 1;
      picked = -1;
      roundVoided = false;
      roundLive = true;
      awaitingStake = false;
      stallMs = 0;
      busy = true;

      const remedial = consecutiveMisses >= PLAYTEST.REMEDIAL_AFTER && !lastWasRemedial;
      lastWasRemedial = remedial;
      if (remedial) consecutiveMisses = 0;

      plan = buildRound({
        seed, gradeTier: tier, index: roundIndex,
        rideDepth: pot.rideDepth, remedial, reduced,
      });
      if (dev) {
        const bad = verifyRound(plan);
        if (bad.length) say('DEV: round ' + roundIndex + ' violates the invariant: ' + bad.join('; '));
      }
      if (plan.blind) blindDealt += 1;

      /* reset the arc: shell id i sits in slot i, lids down, faces bare */
      idAt = [];
      for (let i = 0; i < plan.shells; i++) idAt.push(i);
      targetId = idAt[plan.startSlot];
      clearShellState();
      for (const s of shellEls) clearFace(s);
      paintSlots();
      paintHud();
      if (!targetUrl || decoyUrls.length < 1) dealMedia();

      setBeat(remedial ? 'remedial' : '');
      if (remedial) msg('md_remedial_line', MD_LEX.md_remedial_line);
      reveal(token);
    }

    /* ---- 1. REVEAL ------------------------------------------------------ */
    function reveal(token) {
      setPhase('reveal');
      const slot = plan.startSlot;
      const shell = shellAt(slot);
      if (shell) {
        shell.classList.add('is-lifted', 'is-true');
        dressFace(shell, targetUrl, 'none');
      }
      deck('casino', 'reveal', slot);
      tick('reveal', 0.34);
      if (!plan.remedial) msg('md_reveal_line', MD_LEX.md_reveal_line);
      after(plan.dials.previewMs, step(token, () => {
        if (shell) shell.classList.remove('is-lifted');
        after(PLAYTEST.SETTLE_MS, step(token, () => shuffle(token)));
      }));
    }

    /* ---- 2. SHUFFLE ----------------------------------------------------- */
    function shuffle(token) {
      setPhase('shuffle');
      setBeat('');
      msg('md_shuffle_line', MD_LEX.md_shuffle_line);
      const d = plan.dials;
      deck('casino', 'shuffleStart', plan.swaps.length);
      deck('pressure', 'beat', 'shuffle');

      /* The synchronized occlusion: one wash held across the shuffle, pulsing
       * up on the links that matter. Riding raises the alpha, never the pace. */
      if (capsOkNow) washTo(d.washAlpha * 0.5, d.shuffleMs + 400);
      if (capsOkNow && d.bubbles > 0 && !reduced && !bubblesOn) {
        bubblesOn = true;
        sustainSafe('bubble_field', { max: d.bubbles, alpha: 0.28, variant: 'drift', clickSafe: true });
      }

      for (const s of plan.swaps) {
        after(s.at, step(token, () => applySwap(token, s)));
      }
      for (const dec of plan.decoys) {
        after(dec.at, step(token, () => decoyReveal(token, dec)));
      }
      after(d.shuffleMs + 120, step(token, () => openPick(token)));
    }

    /**
     * ONE LINK. The model moves exactly once, whether the engine's midpoint
     * callback arrives or the backstop deadline does (web CLAUDE.md trap 22).
     */
    function applySwap(token, s) {
      const a = s.a; const b = s.b;
      const shellA = shellAt(a); const shellB = shellAt(b);
      let done = false;
      const commit = () => {
        if (done || dead || token !== roundToken) return;
        done = true;
        const tmp = idAt[a]; idAt[a] = idAt[b]; idAt[b] = tmp;
        paintSlots();
        if (shellA) shellA.classList.remove('is-swapping');
        if (shellB) shellB.classList.remove('is-swapping');
        deck('casino', 'swap', a, b, !!s.glitch);
        /* The trickster's beat: one deal per link, budgeted and capped by the
         * deck itself (Deck III's dealing rule), never by this file. */
        deck('trickster', 'afterSwap');
      };

      if (shellA) shellA.classList.add('is-swapping');
      if (shellB) shellB.classList.add('is-swapping');

      if (s.occluded) {
        /* THE TELL - truthful, on both shells AND in the audio, so a blackout
         * can never take the last recoverable piece of information with it.
         * Every occlusion carries one, so the tell never singles out the link
         * that actually mattered. */
        if (shellA) shellA.classList.add('is-tell');
        if (shellB) shellB.classList.add('is-tell');
        tick('tell', 0.3, { pitch: s.tell && s.tell.side === 'left' ? 0.92 : 1.12 });
        after(s.tell ? s.tell.ms : PLAYTEST.TELL_MS, step(token, () => {
          if (shellA) shellA.classList.remove('is-tell');
          if (shellB) shellB.classList.remove('is-tell');
        }));

        if (s.blackout) {
          /* The blackout beat: the wash rides to the round's cap for a breath,
           * then steps back down to the shuffle level. Reduced motion gets a
           * STATIC occluder instead of a strobe, hiding the same information. */
          stage.setAttribute('data-occluding', '1');
          if (!reduced) washTo(plan.dials.washAlpha, plan.dials.blackoutMs);
          msg('md_blind_line', MD_LEX.md_blind_line);
          after(plan.dials.blackoutMs, step(token, () => {
            stage.setAttribute('data-occluding', '0');
            if (!reduced) washTo(plan.dials.washAlpha * 0.5, 700);
          }));
          commit();                       // unanimated: the slide never happens
          return;
        }

        /* A glitch swap: unanimated, under the shared glitch_swap transition,
         * targeting the FACES (never the shells - style.js owns those). */
        const faces = [];
        if (shellA) { const f = faceOf(shellA); if (f) faces.push(f); }
        if (shellB) { const f = faceOf(shellB); if (f) faces.push(f); }
        stage.setAttribute('data-occluding', '1');
        if (!reduced && faces.length) {
          fireSafe('glitch_swap', {
            targets: faces,
            seconds: 0.42,
            onSwap: () => run(commit),
          });
        }
        /* THE BACKSTOP (trap 22): the engine's midpoint rides the engine's own
         * timer registry, and a suspend kills it. We resolve on our deadline
         * whatever happens. */
        after(reduced ? 60 : 220, step(token, () => {
          commit();
          stage.setAttribute('data-occluding', '0');
        }));
        return;
      }

      /* An ordinary link: the shells slide. style.js owns the transition; we
       * move the model at the midpoint so the truth and the look agree. */
      after(reduced ? 40 : 130, step(token, commit));
    }

    /** A decoy lift - always on a shell she is NOT under (shuffle.js law). */
    function decoyReveal(token, dec) {
      const shell = shellAt(dec.slot);
      if (!shell) return;
      const look = decoyLook(!!dec.fake);
      shell.classList.add('is-lifted', 'is-decoy');
      if (dec.fake) shell.classList.add('is-fake');
      dressFace(shell, look.url, look.filter);
      /* The misleading sting: a lift cue pitched from the WRONG side of the
       * table (the Intake audio-gaslighting precedent). Presentation only. */
      tick('lift', 0.26, { pitch: dec.slot < plan.shells / 2 ? 1.14 : 0.9 });
      after(reduced ? 260 : 200, step(token, () => {
        shell.classList.remove('is-lifted', 'is-decoy', 'is-fake');
        clearFace(shell);
      }));
    }

    /* ---- 3. PICK -------------------------------------------------------- */
    function openPick(token) {
      setPhase('pick');
      setBeat('');
      msg('md_pick_line', MD_LEX.md_pick_line);
      busy = false;
      pickOpenAt = Date.now();
      stallMs = 0;
      const d = plan.dials;

      /* The last glance, contaminated - and welded clickSafe over a table the
       * player is about to tap (DECISIONS #9). */
      if (d.pickBurst && capsOkNow) fireSafe('flash_burst', { count: 2, alpha: 0.4 });
      /* row_drift slides the whole ARC so remembered screen positions decay.
       * It writes an inline transform, so it takes the arc and nothing else. */
      if (d.rowDrift && capsOkNow && !reduced && arc) {
        driftOn = true;
        sustainSafe('row_drift', { targets: [arc], axis: 'x', variant: 'sway', amplitudeMult: 0.6 });
      }

      /* The HONEST window, published as a var so style.js can draw a ring and
       * the trickster's Crooked Clock has a truth to lie about. */
      if (pickTicker) clearTimer(pickTicker);
      pickTicker = every(60, () => {
        if (token !== roundToken || !stage) return;
        const p = clamp01((Date.now() - pickOpenAt) / Math.max(1, scaled(d.pickMs)));
        stage.style.setProperty('--md-pick', p.toFixed(3));
      });
      after(d.pickMs, step(token, () => resolvePick(token, -1, true)));
    }

    function tryPick(slot) {
      if (dead || ended || busy || !roundLive || awaitingStake) return;
      if (!plan || !(slot >= 0 && slot < plan.shells)) return;
      resolvePick(roundToken, slot, false);
    }

    function resolvePick(token, slot, timedOut) {
      if (dead || ended || token !== roundToken || !roundLive) return;
      roundLive = false;
      busy = true;
      picked = slot;
      if (pickTicker) { clearTimer(pickTicker); pickTicker = 0; }
      if (driftOn) { stopSafe('row_drift'); driftOn = false; }
      if (bubblesOn) { stopSafe('bubble_field'); bubblesOn = false; }
      washDown();
      setPhase('pick');

      const latencyMs = timedOut ? plan.dials.pickMs : Math.max(0, Date.now() - pickOpenAt);
      const trueSlot = plan.finalSlot;
      const correct = !timedOut && slot >= 0 && idAt[slot] === targetId;

      /* THE LEDGER ROW. A round voided by a suspend never reaches this line. */
      rounds.push({
        index: roundIndex,
        correct,
        latencyMs,
        heavy: !!plan.heavy,
        remedial: !!plan.remedial,
        blind: !!plan.blind,
        timedOut: !!timedOut,
        voided: false,
      });
      if (plan.blind && correct) blindHits += 1;

      /* lift the truth, always - the near-miss beat needs it and so does trust */
      const trueShell = shellAt(trueSlot);
      const pickShell = slot >= 0 ? shellAt(slot) : null;
      if (pickShell) pickShell.classList.add('is-picked', 'is-lifted');
      if (trueShell) {
        trueShell.classList.add('is-true');
        after(correct ? 0 : 260, step(token, () => {
          trueShell.classList.add('is-lifted');
          dressFace(trueShell, targetUrl, 'none');
        }));
      }
      deck('casino', 'pick', { slot, correct, latencyMs, streak });
      deck('trickster', 'afterPick');

      if (correct) {
        streak += 1;
        bestStreak = Math.max(bestStreak, streak);
        consecutiveMisses = 0;
        setBeat('hit');
        msg('md_hit_line', MD_LEX.md_hit_line);
        tick('hit', 0.5, { pitch: Math.min(1.7, 1 + Math.min(7, streak) * 0.06) });
        try { ctx.ceremonies.streakMeter({ target: null, filled: Math.min(10, streak) }); } catch (e) { /* noop */ }
        const bankedBefore = pot.banked;
        pot = potAfter(pot, 'win');

        /* the shared variable-ratio canon: usually a plain lift, sometimes a
         * garnish, rarely the full jackpot - which doubles the pot for free. */
        const roll = rewardOnce();
        if (roll && roll.jackpot) {
          jackpots += 1;
          pot = potAfter(pot, 'double');
          try { ctx.ceremonies.reward('jackpot', { target: table, text: t('md_jackpot', MD_LEX.md_jackpot) }); } catch (e) { /* noop */ }
          msg('md_scholarship', MD_LEX.md_scholarship);
        } else if (roll && roll.fire && capsOkNow) {
          fireSafe('gif_burst', { count: 3, alpha: 0.42 });
        }

        heat();
        paintHud();

        if (pot.event === 'forceBank') {
          /* THE RIDE CAP: five deep force-banks with the jackpot ceremony. */
          setBeat('forcebank');
          /* The casino is told the AMOUNT that just landed, never the running
           * total - its payout light is scaled by the pot, not by the bank. */
          deck('casino', 'bank', pot.banked - bankedBefore);
          try { ctx.ceremonies.reward('jackpot', { target: table, text: t('md_royal', MD_LEX.md_royal) }); } catch (e) { /* noop */ }
          msg('md_ride_cap_line', MD_LEX.md_ride_cap_line);
          if (!sawFirstMiss) bankedBeforeFirstMiss = true;
          after(resolveMs(), step(token, () => { endRound(token); }));
          return;
        }
        after(reduced ? 260 : 420, step(token, () => openStake(token)));
        return;
      }

      /* ---- a miss ------------------------------------------------------ */
      streak = 0;
      consecutiveMisses += 1;
      sawFirstMiss = true;
      const adjacent = slot >= 0 && Math.abs(slot - trueSlot) === 1;
      setBeat(timedOut ? 'timeout' : adjacent ? 'almost' : 'miss');
      if (timedOut) {
        msg('md_timeout_line', MD_LEX.md_timeout_line);
        tick('miss', 0.24);
      } else if (adjacent) {
        /* COSMETIC staging only - the chain was never rigged to land next
         * door, so `almost` reports what happened and never arranges it. */
        deck('casino', 'almost', slot, trueSlot);
        try { ctx.ceremonies.reward('near_miss', { target: table, text: t('md_almost', MD_LEX.md_almost) }); } catch (e) { /* noop */ }
        msg('md_almost_line', MD_LEX.md_almost_line);
        tick('miss', 0.3);
      } else {
        msg('md_miss_line', MD_LEX.md_miss_line);
        tick('miss', 0.3);
      }
      if (pot.live > 0) {
        pot = potAfter(pot, 'bust');
        deck('casino', 'bust');
        msg('md_bust_line', MD_LEX.md_bust_line);
      }
      heat();
      paintHud();
      after(resolveMs(), step(token, () => endRound(token)));
    }

    function resolveMs() { return reduced ? PLAYTEST.RESOLVE_MS_REDUCED : PLAYTEST.RESOLVE_MS; }

    /** The reward canon, engine first, a seeded local fallback second. */
    function rewardOnce() {
      try {
        if (ctx.engine && typeof ctx.engine.rewardRoll === 'function') {
          const r = ctx.engine.rewardRoll({ heat: currentHeat, streak });
          if (r) return r;
        }
      } catch (e) { /* fall through */ }
      return rewardRoll ? rewardRoll() : null;
    }

    /* ---- 4. STAKE ------------------------------------------------------- */
    function openStake(token) {
      if (dead || ended || token !== roundToken) return;
      awaitingStake = true;
      setPhase('stake');
      setBeat('');
      if (stakeEl) stakeEl.hidden = false;
      if (rideBtn) rideBtn.disabled = pot.rideDepth >= PLAYTEST.RIDE_CAP;
      msg('md_stake_line', MD_LEX.md_stake_line);
      deck('casino', 'stake', { ride: false, pot: pot.live });

      if (stakeMode === 'bank') {
        after(PLAYTEST.AUTO_STAKE_MS, step(token, () => resolveStake('bank', true)));
      } else if (stakeMode === 'ride') {
        after(PLAYTEST.AUTO_STAKE_MS, step(token, () => resolveStake('ride', true)));
      } else {
        /* The prompt never traps: on a timeout the SAFE answer wins, because
         * an inattentive player must never be walked into a stake. */
        after(PLAYTEST.STAKE_MS, step(token, () => resolveStake('bank', true)));
      }
    }

    function resolveStake(action, auto) {
      if (dead || ended || !awaitingStake) return;
      awaitingStake = false;
      const token = roundToken;
      if (stakeEl) stakeEl.hidden = true;

      if (action === 'ride' && pot.rideDepth < PLAYTEST.RIDE_CAP) {
        pot = potAfter(pot, 'ride');
        setBeat('ride');
        deck('casino', 'stake', { ride: true, pot: pot.live });
        msg(auto ? 'md_auto_ride_line' : 'md_ride_line',
          auto ? MD_LEX.md_auto_ride_line : MD_LEX.md_ride_line);
        tick('ride', 0.4, { pitch: 1 + Math.min(5, pot.rideDepth) * 0.08 });
      } else {
        const amount = pot.live;
        pot = potAfter(pot, 'bank');
        setBeat('bank');
        deck('casino', 'bank', amount);
        try { ctx.ceremonies.stamp({ text: t('md_stamp_bank', MD_LEX.md_stamp_bank), tone: 'pink', target: table }); } catch (e) { /* noop */ }
        msg(auto && stakeMode === 'bank' ? 'md_auto_bank_line' : 'md_banked_line',
          auto && stakeMode === 'bank' ? MD_LEX.md_auto_bank_line : MD_LEX.md_banked_line);
        tick('bank', 0.45);
        if (!sawFirstMiss) bankedBeforeFirstMiss = true;
      }
      heat();
      paintHud();
      after(reduced ? 320 : 520, step(token, () => endRound(token)));
    }

    /* ---- 5. END OF ROUND ------------------------------------------------ */
    function endRound(token) {
      if (dead || ended || token !== roundToken) return;
      clearShellState();
      for (const s of shellEls) clearFace(s);
      setBeat('');
      if (elapsedMs >= budgetMs) { bell(); return; }
      nextRound();
    }

    /**
     * THE VOID. A suspend (panic, a mandatory video, an audio-only flip) kills
     * the live round: no miss is recorded, the round is excluded from every
     * denominator, the occlusion clears and the STAKED pot force-banks - the
     * dossier's panic rule, applied to every suspend reason.
     */
    function voidLiveRound(why) {
      if (!plan || (!roundLive && !awaitingStake)) return;
      roundToken += 1;                    // every pending step for this round dies
      roundLive = false;
      awaitingStake = false;
      roundVoided = true;
      voidedRounds += 1;
      if (plan.blind) blindDealt = Math.max(0, blindDealt - 1);
      if (pot.live > 0) { pot = potAfter(pot, 'bank'); }
      if (stakeEl) stakeEl.hidden = true;
      if (pickTicker) { clearTimer(pickTicker); pickTicker = 0; }
      if (driftOn) { stopSafe('row_drift'); driftOn = false; }
      if (bubblesOn) { stopSafe('bubble_field'); bubblesOn = false; }
      washDown();
      if (stage) stage.setAttribute('data-occluding', '0');
      setBeat('void');
      clearShellState();
      for (const s of shellEls) clearFace(s);
      paintHud();
      say('round ' + roundIndex + ' voided (' + (why || 'suspend') + ') - excluded from the ledger');
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
        if (clockChip) clockChip.textContent = mmss(secLeft());
        const left = secLeft();
        if (!bellOn && left <= PLAYTEST.BELL_WARN_SEC && elapsedMs < budgetMs) {
          bellOn = true;
          deck('casino', 'bell', true);
          deck('pressure', 'beat', 'bell');
          msg('md_bell_warn', MD_LEX.md_bell_warn);
          tick('sting', 0.4);
        }
        if (elapsedMs >= budgetMs) { stopClock(); run(bell); }
      });
    }
    function stopClock() { if (clockId) { clearTimer(clockId); clockId = 0; } }

    function bell() {
      if (dead || ended) return;
      busy = true;
      /* An unfinished round at the bell was never given a fair pick, so it is
       * simply not recorded - the same posture as a voided round. */
      if (roundLive || awaitingStake) voidLiveRound('bell');
      bellOn = true;
      setPhase('ended');
      deck('casino', 'dimOut');
      deck('pressure', 'dimOut');
      try { ctx.ceremonies.stamp({ text: t('md_stamp_bell', MD_LEX.md_stamp_bell), tone: 'pink', target: table }); } catch (e) { /* noop */ }
      msg('md_bell_line', MD_LEX.md_bell_line);
      tick('stamp', 0.6);
      after(reduced ? 700 : 1100, () => finish());
    }

    /* ==================================================================== *
     * THE END - exactly one endClass, after the end card has been seen
     * ==================================================================== */
    function finish() {
      if (ended) return;
      ended = true;
      busy = true;
      stopClock();
      if (pickTicker) { clearTimer(pickTicker); pickTicker = 0; }
      if (stallTimer) { clearTimer(stallTimer); stallTimer = 0; }
      if (subTimer) { clearTimer(subTimer); subTimer = 0; }
      if (driftOn) { stopSafe('row_drift'); driftOn = false; }
      if (bubblesOn) { stopSafe('bubble_field'); bubblesOn = false; }
      if (ambientOn) { stopSafe('ambient_field'); ambientOn = false; }
      washDown();
      deck('trickster', 'stop');
      deck('casino', 'stop');
      deck('pressure', 'stop');
      paintTags();                         // truth on every tag, whatever the lie left
      paintHud();

      const graded = compositeFor({ rounds, deepestBanked: pot.deepestBanked, gradeTier: tier });
      const gates = hardGates(graded.counts.blindDealt, graded.counts.blindHits);
      const fx = flavorXp(pot.banked);

      try {
        const meta = (ctx.store && typeof ctx.store.gameMeta === 'function') ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
        const bestBank = Math.max(Number(meta.bestBank) || 0, pot.banked);
        const bestRide = Math.max(Number(meta.bestRide) || 0, pot.deepestBanked);
        if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
          ctx.store.mergeGameMeta(GAME_KEY, {
            bestBank, bestRide, lastSeed: seed, lastPlayedAt: Date.now(),
          });
        }
      } catch (e) { say('meta write failed (class unaffected): ' + ((e && e.message) || e)); }

      renderEnd(graded);
      setPhase('ended');

      const report = { metrics: { composite: graded.composite }, hardGates: gates, flavorXp: fx };
      lastReport = Object.assign({}, report, {
        inputs: {
          tier, seed, retake, reduced, stakeMode, skin,
          rounds: rounds.length, voided: voidedRounds,
          banked: pot.banked, deepestBanked: pot.deepestBanked,
          bestStreak, jackpots, bankedBeforeFirstMiss,
          terms: graded.terms, counts: graded.counts, base: graded.base, ride: graded.ride,
          elapsedMs,
        },
      });
      try { lastSnapshot = instance.snapshot(); } catch (e) { /* diagnostics only */ }
      say('class over: ' + graded.counts.hits + '/' + graded.counts.graded + ' picks, '
        + (graded.counts.meanLatencyMs == null ? '-' : graded.counts.meanLatencyMs + 'ms')
        + ', banked ' + pot.banked + ' (deepest ride ' + pot.deepestBanked + '), blind '
        + graded.counts.blindHits + '/' + graded.counts.blindDealt
        + ' -> composite ' + graded.composite.toFixed(3) + (gates.sGate ? '' : ' [S GATE FAILED]'));

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
      endEl.appendChild(el('h3', 'g-md-end-title', t('md_end_title', MD_LEX.md_end_title)));
      const row = (cls, k, v) => {
        const r = el('div', 'g-md-end-row' + (cls ? ' ' + cls : ''));
        r.appendChild(el('span', 'g-md-end-k', k));
        r.appendChild(el('span', 'g-md-end-v', v));
        endEl.appendChild(r);
        return r;
      };
      /* The card LEADS with the banked total, not the misses (dossier). */
      row('g-md-end-bank', t('md_end_banked', MD_LEX.md_end_banked), String(pot.banked));
      row('', t('md_end_picks', MD_LEX.md_end_picks), graded.counts.hits + ' / ' + graded.counts.graded);
      row('', t('md_end_latency', MD_LEX.md_end_latency),
        graded.counts.meanLatencyMs == null ? '-' : (graded.counts.meanLatencyMs + 'ms'));
      row('', t('md_end_deepest', MD_LEX.md_end_deepest), String(pot.deepestBanked));
      row('', t('md_end_streak', MD_LEX.md_end_streak), String(bestStreak));
      row('', t('md_end_rounds', MD_LEX.md_end_rounds), String(rounds.length));
      if (graded.counts.blindDealt > 0) {
        row('g-md-end-blind', t('md_end_blind', MD_LEX.md_end_blind),
          graded.counts.blindHits > 0 ? t('md_end_yes', MD_LEX.md_end_yes) : t('md_end_no', MD_LEX.md_end_no));
        if (graded.counts.blindHits > 0) {
          try { ctx.ceremonies.stamp({ text: t('md_stamp_blind', MD_LEX.md_stamp_blind), target: endEl }); } catch (e) { /* noop */ }
        }
      }
      if (bankedBeforeFirstMiss) {
        endEl.appendChild(el('p', 'g-md-end-note', t('md_end_clean', MD_LEX.md_end_clean)));
      }
    }

    /* ==================================================================== *
     * THE CLASS-RULES SHEET (Deck VI). Policy is the shell's "Skip class
     * tutorials" contract: default shows every class; with the skip on, a
     * class still explains itself ONCE per grade tier, remembered in the
     * game's own meta (never the shell's). Dismissal is the sheet's own GO
     * button, nothing else - binding a pick key here would teach the player
     * to press it at a table that has not been dealt.
     * ==================================================================== */
    function howtoSeenTiers() {
      try {
        const m = (ctx.store && typeof ctx.store.gameMeta === 'function')
          ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
        return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
      } catch (e) { return []; }
    }
    function howto(onDone) {
      const seen = howtoSeenTiers();
      const skip = (dev && devSkipHowto) || (ctx.hideTutorial === true && seen.indexOf(tier) >= 0);
      if (skip) { onDone(); return; }
      if (typeof showHowtoFn !== 'function') { onDone(); return; }
      let done = false;
      let node = null;
      try {
        const labels = pickKeyLabels().slice(0, dialsFor({ gradeTier: tier }).shells);
        node = showHowtoFn({
          host: stage, stage, table, t, tier, reduced,
          keys: labels.join(' '),
          keyLabel: labels[Math.min(2, labels.length - 1)] || '3',
          coarse: !!(ctx.platform && ctx.platform.isTouch),
          onGo: () => {
            if (done || dead) return;
            done = true;
            try {
              const list = howtoSeenTiers();
              if (list.indexOf(tier) < 0) {
                list.push(tier);
                if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
                  ctx.store.mergeGameMeta(GAME_KEY, { howtoTiers: list });
                }
              }
            } catch (e) { /* best effort - the sheet just shows again next time */ }
            hideHowto();
            onDone();
          },
        });
      } catch (e) { say('rules sheet refused: ' + ((e && e.message) || e)); node = null; }
      if (!node) { done = true; onDone(); return; }
      howtoNode = node;
    }
    function hideHowto() {
      try { if (typeof hideHowtoFn === 'function') hideHowtoFn(); } catch (e) { /* noop */ }
      try { if (howtoNode && typeof howtoNode.remove === 'function') howtoNode.remove(); } catch (e) { /* noop */ }
      howtoNode = null;
    }
    /** The bound pick keys, for the sheet's one line of text. */
    function pickKeyLabels() {
      const out = [];
      for (let i = 1; i <= 5; i++) {
        let lbl = String(i);
        try { if (ctx.keys && typeof ctx.keys.labelFor === 'function') lbl = ctx.keys.labelFor('pick' + i) || String(i); }
        catch (e) { /* noop */ }
        out.push(lbl);
      }
      return out;
    }
    /* style.js provides these; both are optional. */
    let showHowtoFn = null;
    let hideHowtoFn = null;

    /* ==================================================================== *
     * INPUT
     * ==================================================================== */
    function bindKeys() {
      keyOff = [];
      if (!ctx.keys || typeof ctx.keys.on !== 'function') return;
      for (let i = 1; i <= 5; i++) {
        const slot = i - 1;
        try {
          const off = ctx.keys.on('pick' + i, () => run(() => tryPick(slot)));
          if (typeof off === 'function') keyOff.push(off);
        } catch (e) { /* a verb the shell refused to declare */ }
      }
    }
    function unbindKeys() {
      for (const off of keyOff) { try { off(); } catch (e) { /* noop */ } }
      keyOff = [];
      for (const s of shellEls) {
        try { if (s._mdOnClick) s.removeEventListener('click', s._mdOnClick); } catch (e) { /* noop */ }
      }
    }

    /* ==================================================================== *
     * THE MODULE INSTANCE
     * ==================================================================== */
    const instance = {
      start(classSpec) {
        spec = classSpec || { gradeTier: 1, seed: GAME_KEY + '|none', timeBudgetSec: 120 };
        tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        seed = String(spec.seed == null ? GAME_KEY : spec.seed);
        budgetMs = Math.max(20000, (Number(spec.timeBudgetSec) || 120) * 1000);
        retake = !!spec.retake;
        reduced = probeReduced(ctx) || motionLevelOf() === 0;
        stakeMode = stakeModeFrom(ctx.settings ? ctx.settings.md_stake_mode : 'ask');
        skin = skinFrom(ctx.settings ? ctx.settings.md_shell_skin : 'themed');
        capsOkNow = capsArmed();
        devSkipHowto = dev && !!spec.devSkipHowto;

        rounds.length = 0;
        roundIndex = -1;
        roundToken = 0;
        streak = 0; bestStreak = 0;
        pot = { live: 0, rideDepth: 0, banked: 0, deepestBanked: 0, event: '' };
        consecutiveMisses = 0;
        lastWasRemedial = false;
        bankedBeforeFirstMiss = false;
        sawFirstMiss = false;
        voidedRounds = 0; jackpots = 0; blindDealt = 0; blindHits = 0;
        elapsedMs = 0; bellOn = false; ended = false; reported = false; dead = false;
        decoyUrls.length = 0; decoyIdx = 0; targetUrl = ''; targetIsVideo = false; mediaLogged = false;

        /* the seeded local reward roll - the floor under engine.rewardRoll */
        {
          let n = 0;
          const base = 0.30 + 0.30 * (tier - 1) / 3;
          rewardRoll = () => {
            n += 1;
            const a = hash(seed + '|md-vr|fire|' + n);
            const b = hash(seed + '|md-vr|jack|' + n);
            const chance = Math.min(0.9, base + Math.min(8, streak) * 0.02);
            const fire = a < chance;
            return { fire, jackpot: fire && b >= 0.85, nearMiss: !fire && a < chance + 0.08 };
          };
        }

        const n = dialsFor({ gradeTier: tier }).shells;
        try { if (typeof injectStyle === 'function') injectStyle(); }
        catch (e) { say('style inject failed (class unaffected): ' + ((e && e.message) || e)); }
        buildDom(n);
        idAt = []; for (let i = 0; i < n; i++) idAt.push(i);
        paintSlots();
        paintHud();

        /* ---- the decks ---------------------------------------------------
         * Built right after mount, each in its own try/catch. A refused deck
         * is a null and a log line; the class runs undressed. */
        const chips = { round: roundChip, clock: clockChip, pot: potChip, streak: streakChip };
        const base = {
          seed, tier, gradeTier: tier,
          reduced, motionLevel: motionLevelOf(),
          timers: deckTimers,
          engine: deckEngine,
          /* LIVE, never a launch snapshot: bgIntensity 0 is the player's exit
           * and it disarms every deck the moment it moves. The decks accept a
           * function or a boolean; the function is the honest one. */
          capsOk: capsArmed,
          budgetSec: Math.round(budgetMs / 1000),
          t,
          log: say,
        };
        try {
          casino = (typeof createMdCasino === 'function') ? (createMdCasino(Object.assign({}, base, {
            stage, backdrop, table, arc, board: arc, frame: table,
            /* `hud` is the CHIP MAP (that is what a deck lights); the element
             * itself rides along as hudEl for a deck that wants the bar. */
            hud: chips, chips, hudEl: hud,
            shells: () => shellEls.slice(),
            msg: msgEl, well, stake: stakeEl,
            assets: deckAssets,
            utcDate: utcDateOf(seed),
          })) || null) : null;
        } catch (e) { casino = null; say('casino refused: ' + ((e && e.message) || e)); }
        try {
          trickster = (typeof createMdTrickster === 'function') ? (createMdTrickster(Object.assign({}, base, {
            stage, arc, table, board: arc, hud: chips, hudEl: hud,
            isHalted: () => dead || paused || ended || busy,
            coarse: !!(ctx.platform && ctx.platform.isTouch),
            stats: () => ({
              round: roundIndex, phase: stage ? stage.getAttribute('data-phase') : 'ended',
              streak, pot: pot.live, banked: pot.banked, rideDepth: pot.rideDepth,
              secLeft: secLeft(), shells: plan ? plan.shells : n,
            }),
            /* the ONE node a lie may land on, plus the truth to restore */
            tagOf,
            chipEl: (which) => (which === 'clock' ? clockChip : which === 'pot' ? potChip
              : which === 'streak' ? streakChip : roundChip),
            chipText,
            shells: () => shellEls.slice(),
            /* THE TRUE SLOT, read-only. The Ghost Cursor lures to a shell NEXT
             * TO her and The Melt refuses to sag the one she is under, so both
             * cards need to know where she is - and neither may ever move her
             * (Law I) or move a hitbox (Law II). */
            targetSlot: () => idAt.indexOf(targetId),
            /* We draw no ring of our own - the honest window is published as
             * --md-pick on the stage and the deck mounts its own crooked one. */
            ringEl: () => null,
            /* the HONEST pick window, so the Crooked Clock has a truth to bend */
            pickWindow: () => ({
              open: !!(plan && roundLive && stage && stage.getAttribute('data-phase') === 'pick'),
              elapsedMs: pickOpenAt ? Math.max(0, Date.now() - pickOpenAt) : 0,
              totalMs: plan ? plan.dials.pickMs : PLAYTEST.PICK_MS,
            }),
            announce: (text, ms) => {
              if (!msgEl || !text) return;
              msgEl.textContent = String(text);
              const mine = msgEl.textContent;
              deckTimers.after(Math.max(400, Number(ms) || 1600), () => {
                if (msgEl && msgEl.textContent === mine) msgEl.textContent = '';
              });
            },
          })) || null) : null;
        } catch (e) { trickster = null; say('trickster refused: ' + ((e && e.message) || e)); }
        try {
          pressure = (typeof createMdPressure === 'function') ? (createMdPressure(Object.assign({}, base, {
            stage, backdrop, table, arc, board: arc,
            /* THE TREMOR rides the chrome, never a truth node: the HUD bar,
             * the proctor line and the stake buttons shake; the arc and the
             * shells the player is tracking never do. */
            chrome: [hud, msgEl, stakeEl].filter(Boolean),
            hud: chips, hudEl: hud,
            assets: deckAssets,
          })) || null) : null;
        } catch (e) { pressure = null; say('pressure refused: ' + ((e && e.message) || e)); }

        /* THE SHEET (Deck VI): style.js draws it, this file owns the POLICY.
         * A casino that exports its own showHowto is the fallback seam. */
        showHowtoFn = null; hideHowtoFn = null;
        if (typeof styleBuildHowto === 'function') {
          showHowtoFn = styleBuildHowto;
          hideHowtoFn = (typeof styleHideHowto === 'function') ? styleHideHowto : null;
        } else if (casino && typeof casino.showHowto === 'function') {
          showHowtoFn = (o) => casino.showHowto(o);
          hideHowtoFn = () => { try { casino.hideHowto(); } catch (e) { /* noop */ } };
        }

        bindKeys();
        claimAssets();
        heat();

        /* the ambient floor: the table is never still (Law III) */
        if (capsOkNow && tier >= 2 && !reduced) {
          ambientOn = true;
          sustainSafe('ambient_field', { kind: 'motes', density: 0.35, alpha: 0.3 });
        }
        /* sub_flash between rounds only - never during a pick window, which
         * would be a lie about the thing the player is being graded on. */
        const subMs = dialsFor({ gradeTier: tier }).subFlashMs;
        if (subMs > 0 && capsOkNow) {
          subTimer = every(subMs, () => {
            if (ended || !stage) return;
            if (stage.getAttribute('data-phase') === 'pick') return;
            subIdx += 1;
            fireSafe('sub_flash', { variant: subIdx % 2 ? 'whisper' : 'centre', alpha: 0.32 });
          });
        }
        stallTimer = every(PLAYTEST.STALL_TICK_MS, () => {
          if (ended || busy || !roundLive) { stallMs = 0; return; }
          stallMs += PLAYTEST.STALL_TICK_MS;
          deck('trickster', 'stalled', stallMs);
        });

        startClock();
        liveClass = instance;
        lastReport = null;
        lastSnapshot = null;

        msg('md_brief', MD_LEX.md_brief);
        howto(() => {
          if (dead || ended) return;
          deck('casino', 'start');
          deck('trickster', 'start');
          deck('pressure', 'start');
          after(reduced ? PLAYTEST.BRIEF_MS_REDUCED : PLAYTEST.BRIEF_MS, () => {
            if (dead || ended) return;
            busy = false;
            nextRound();
          });
        });

        say('tier ' + tier + ', ' + n + ' shells, budget ' + Math.round(budgetMs / 1000) + 's, stake '
          + stakeMode + ', skin ' + skin + (reduced ? ', reduced' : '') + (retake ? ', RETAKE' : '')
          + ', decks ' + (casino ? 'casino ' : '') + (trickster ? 'trickster ' : '') + (pressure ? 'pressure' : '')
          + (deckLoadNotes.length ? ' | ' + deckLoadNotes.join(' | ') : ''));
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
        pickOpenAt = Date.now();          // the window restarts honestly, never mid-flight
        const q = deferred.splice(0);
        for (const fn of q) run(fn);
        /* A voided round is dealt again from the top the moment play resumes. */
        if (roundVoided && !ended && !dead) {
          roundVoided = false;
          msg('md_voided_line', MD_LEX.md_voided_line);
          after(reduced ? 300 : 600, () => { if (!ended && !dead) nextRound(); });
        }
      },

      /** The shell owns the overlay and the engine's suspend; we freeze AND
       *  void, because a round the player could not watch must never be
       *  graded (dossier: the live round voids with no miss recorded). */
      suspend(on) {
        if (on) {
          voidLiveRound('suspend');
          instance.pause();
        } else {
          instance.resume();
        }
      },

      destroy() {
        dead = true;
        stopClock();
        clearTimers();
        unbindKeys();
        hideHowto();
        try { if (trickster) trickster.destroy(); } catch (e) { /* noop */ }
        trickster = null;
        try { if (casino) casino.destroy(); } catch (e) { /* noop */ }
        casino = null;
        try { if (pressure) pressure.destroy(); } catch (e) { /* noop */ }
        pressure = null;
        if (pool && typeof pool.release === 'function') { try { pool.release(); } catch (e) { /* noop */ } }
        pool = null;
        shellEls.length = 0;
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
        if (liveClass === instance) liveClass = null;
      },

      /* -------- test / diagnostics seams (never read by the shell) -------- */
      /** Pick a slot as the player would. */
      pick(slot) { run(() => tryPick(slot)); },
      /** Answer the stake prompt as the player would. */
      stake(action) { run(() => resolveStake(action === 'ride' ? 'ride' : 'bank', false)); },
      /** The live round's seeded plan. */
      plan() { return plan; },
      /** Where she really is, right now. The suite's oracle; nothing else. */
      trueSlot() { return idAt.indexOf(targetId); },
      chipText(which) { return chipText(which); },

      snapshot() {
        return {
          tier, seed, retake, reduced, stakeMode, skin, capsOk: capsOkNow,
          roundIndex, roundToken, roundLive, awaitingStake, roundVoided, busy, paused, ended, reported, dead,
          phase: stage ? stage.getAttribute('data-phase') : null,
          beat: stage ? stage.getAttribute('data-beat') : null,
          occluding: stage ? stage.getAttribute('data-occluding') : null,
          shells: plan ? plan.shells : shellEls.length,
          idAt: idAt.slice(),
          targetId,
          trueSlot: idAt.indexOf(targetId),
          picked,
          plan: plan ? {
            index: plan.index, startSlot: plan.startSlot, finalSlot: plan.finalSlot,
            swaps: plan.swaps.length, hiddenLinks: plan.hiddenLinks, blind: plan.blind,
            heavy: plan.heavy, remedial: plan.remedial, decoys: plan.decoys.length,
            dials: plan.dials,
          } : null,
          rounds: rounds.slice(),
          voidedRounds, blindDealt, blindHits, jackpots,
          streak, bestStreak, pot: Object.assign({}, pot),
          bankedBeforeFirstMiss, consecutiveMisses, lastWasRemedial,
          currentHeat, driftOn, bubblesOn, ambientOn, bellOn,
          targetUrl, decoys: decoyUrls.slice(),
          elapsedMs, budgetMs, secLeft: secLeft(),
          deckLoadNotes: deckLoadNotes.slice(),
          casino: casino && typeof casino.diagnostics === 'function' ? (() => { try { return casino.diagnostics(); } catch (e) { return null; } })() : null,
          trickster: trickster && typeof trickster.diagnostics === 'function' ? (() => { try { return trickster.diagnostics(); } catch (e) { return null; } })() : null,
          pressure: pressure && typeof pressure.diagnostics === 'function' ? (() => { try { return pressure.diagnostics(); } catch (e) { return null; } })() : null,
          stage, arc, table, hud, msgEl, well, endEl, stakeEl, bankBtn, rideBtn,
          roundChip, clockChip, potChip, streakChip,
          shellEls: shellEls.slice(),
        };
      },
    };

    /** The UTC day the class was seeded on (the shell's seed opens with it). */
    function utcDateOf(s) {
      const m = /^(\d{4}-\d{2}-\d{2})/.exec(String(s || ''));
      if (m) return m[1];
      try { return new Date().toISOString().slice(0, 10); } catch (e) { return '1970-01-01'; }
    }

    /** FNV-1a 0..1 - the local reward roll's only source of randomness, and it
     *  is seeded, so a retake rolls the identical rewards. */
    function hash(str) {
      let h = 2166136261 >>> 0;
      const v = String(str);
      for (let i = 0; i < v.length; i++) { h ^= v.charCodeAt(i); h = Math.imul(h, 16777619); }
      return (h >>> 0) / 4294967295;
    }

    return instance;
  },

  /** The live class's state, or null. Never read by the shell. */
  diagnostics() { return liveClass ? liveClass.snapshot() : null; },
  /** The last report handed to endClass (survives teardown). Diagnostics only. */
  get lastReport() { return lastReport; },
  get lastSnapshot() { return lastSnapshot; },

  setTimeScale,
};

/* Re-exported so the rig and the suite can reach the pure model through the
 * one module they already import. Nothing in the shell reads these. */
export { PLAYTEST, buildRound, simulate, verifyRound, dialsFor, potAfter, heatFor };
