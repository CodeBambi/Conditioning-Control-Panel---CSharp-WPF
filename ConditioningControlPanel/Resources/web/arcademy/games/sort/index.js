/* ============================================================================
 * games/sort/index.js - SORT: room 201, East Wing. Family tracking, 120s, not
 * meaty. BUILD-CONTRACT module.
 *
 * A stack of three cards breathes centre stage. Drag right for YOURS, left for
 * everything else. The stamp fades in with the lean, the card flies and THUDS
 * into the wall behind, and the next one springs up. That is the whole verb.
 *
 * IT IS A GAME BECAUSE OF THE CHAIN. Sorting by a fixed rule with no fail state
 * is a soak; the ripe ring is the tempo, and YOU drive it. Rung 0 gives you
 * 2.4s a card, rung 8 gives you 0.75s, and the rung is nothing but your own
 * clean streak (3 / 5 / 8 / 12 / 16 / 21 / 27 / 34). A wrong swipe is one rung
 * down on a 1.5s fade, never to zero and never out. Letting a ring close is a
 * PASS: the card sinks under the stack and comes back later, and it costs the
 * chain nothing - that valve is what lets a player actually look at a card they
 * cannot place instead of guessing at it.
 *
 * THE TWIST IS THAT THE PILES ARE YOURS. Right is TARGET, left is NOISE, and
 * which niches those are is chosen at a DOOR that runs before the class clock
 * (LOT G2's setup.js; this file ships a stub that opens straight through). So
 * the room is "how fast can I tell MINE from the rest", and the player sets
 * their own difficulty by what they pair.
 *
 * THE LEDGER IS THE TAG, NEVER THE PIXELS. deck.js judges a swipe against the
 * tag the HOST stamped on the row. In QUICK SORT (no remote consent, one flat
 * folder) the truth is the row's KIND instead - moving right, still left - and
 * that is the only other truth this class will ever read.
 *
 * FILES
 *   deck.js        PURE: the 55/45 interleave, run caps, the retake cache, judge
 *   chain.js       PURE: rungs, ring times, PERFECT / JUST / ALMOST, the royal
 *   swipe.js       the hand: pointer physics and the keys, which are the same
 *   wall.js        the collage of what you already sorted
 *   grade.js       PURE: .55 accuracy / .30 tempo / .15 PERFECT share + S gate
 *   style.js       the self-injected sheet (plus the door's, concatenated)
 *   lex.js         every row this class can render
 *   setup.js       LOT G2's door. G1 ships a stub that opens immediately.
 *   casino.js / pressure.js / trickster.js   LOT D. ABSENT here on purpose: the
 *                  dynamic imports below fail silently and the room plays.
 *
 * THE LAWS THIS FILE KEEPS
 *   - input honesty: nothing auto-sorts, no card moves itself, no rewind.
 *   - ledger honesty: correctness is deck.judge and nothing else; a wrong swipe
 *     is HONOURED (the card flies anyway) and counted.
 *   - the class NEVER grades itself: it reports {metrics:{composite}, hardGates}
 *     and core/grades.js does the rest.
 *   - the ticket renders BEFORE endClass, because endClass tears this DOM down.
 *   - decks are decoration: no deck writes chain, rung, accuracy or grade, and
 *     a deck that refuses to build is null and a log line.
 *   - decoder ceiling 2 (trap 36): the top card plays, the second holds a still,
 *     the third is a drawn back, and the wall is stills all the way down.
 *
 * CLOCK. `now()` and the timer helpers resolve `performance` / `setTimeout` off
 * the global at CALL time, so a scratch harness can swap in a fake clock and
 * drive a whole 120s class in milliseconds with no test-only code in here.
 * ==========================================================================*/

import { makeRng, hash01, shuffled } from '../../core/rng.js';
import {
  buildDeck, deckFromRows, rowsFromCards, cacheUsable, wrapQuickPool,
  judge, DECK,
} from './deck.js';
import {
  CHAIN, capForTier, ringMsFor, verdictFor, afterClean, afterWrong,
  chimePitch, isMajorRung, isRoyal, ladderFrac,
} from './chain.js';
import { createSwipe, SWIPE, scaleForDepth } from './swipe.js';
import { createWall, WALL } from './wall.js';
import { gradeClass } from './grade.js';
import { SORT_LEX } from './lex.js';
import { SETUP_LEX } from './setup-lex.js';
import { ensureStyle } from './style.js';
import { createSetupDoor } from './setup.js';

const GAME_KEY = 'sort';

/* ----------------------------------------------------------------- clock -- */
function now() {
  try {
    const p = globalThis.performance;
    if (p && typeof p.now === 'function') return p.now();
  } catch (e) { /* fall through */ }
  return Date.now();
}
function laterFn() {
  const f = globalThis.setTimeout;
  return typeof f === 'function' ? f : setTimeout;
}
function clearFn() {
  const f = globalThis.clearTimeout;
  return typeof f === 'function' ? f : clearTimeout;
}
/**
 * The class's timer registry (the Impulse Control pattern). `after` is a
 * one-shot, `every` a self-re-arming CHAIN - never setInterval, so the fake
 * clock drives it too and one cancel kills the whole run. A repeat handle is a
 * STRING, a one-shot an integer; `cancel` takes either.
 */
function createTimers() {
  const live = new Set();
  const repeats = new Map();
  let nextRepeat = 1;
  return {
    after(ms, fn) {
      const id = laterFn()(() => { live.delete(id); try { fn(); } catch (e) { /* noop */ } },
        Math.max(0, Math.round(ms) || 0));
      live.add(id);
      return id;
    },
    every(ms, fn) {
      const key = 'sort-every-' + (nextRepeat++);
      const period = Math.max(8, Math.round(ms) || 16);
      const rec = { timer: 0, dead: false };
      repeats.set(key, rec);
      const arm = () => {
        rec.timer = laterFn()(() => {
          if (rec.dead) return;
          try { fn(); } catch (e) { /* noop */ }
          if (!rec.dead) arm();
        }, period);
      };
      arm();
      return key;
    },
    cancel(id) {
      if (id == null) return;
      if (typeof id === 'string') {
        const rec = repeats.get(id);
        if (rec) { rec.dead = true; try { clearFn()(rec.timer); } catch (e) { /* noop */ } repeats.delete(id); }
        return;
      }
      try { clearFn()(id); } catch (e) { /* noop */ }
      live.delete(id);
    },
    killAll() {
      for (const id of Array.from(live)) { try { clearFn()(id); } catch (e) { /* noop */ } }
      live.clear();
      for (const [k, rec] of Array.from(repeats)) {
        rec.dead = true;
        try { clearFn()(rec.timer); } catch (e) { /* noop */ }
        repeats.delete(k);
      }
    },
    get size() { return live.size + repeats.size; },
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
function clamp01(v) { const x = Number(v); return !isFinite(x) ? 0 : x < 0 ? 0 : x > 1 ? 1 : x; }
function el(tag, cls) {
  try {
    if (typeof document === 'undefined' || !document.createElement) return null;
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    return n;
  } catch (e) { return null; }
}
function setAttr(node, k, v) { try { if (node && node.setAttribute) node.setAttribute(k, String(v)); } catch (e) { /* DOM double */ } }
function setVar(node, k, v) { try { if (node && node.style && node.style.setProperty) node.style.setProperty(k, String(v)); } catch (e) { /* noop */ } }
function addCls(node, c) { try { if (node && node.classList) node.classList.add(c); } catch (e) { /* noop */ } }
function delCls(node, c) { try { if (node && node.classList) node.classList.remove(c); } catch (e) { /* noop */ } }
/** The UTC day the class was seeded on: the shell's seed opens with it. */
function utcDateOf(seed) {
  const m = /^(\d{4}-\d{2}-\d{2})/.exec(String(seed || ''));
  if (m) return m[1];
  try { return new Date().toISOString().slice(0, 10); } catch (e) { return '1970-01-01'; }
}
function isVideoUrl(url, mime) {
  if (mime && /^video\//i.test(String(mime))) return true;
  return /\.(mp4|webm|m4v|mov)(\?|#|$)/i.test(String(url || ''));
}

/* -------------------------------------------------------------- the dials -- */
const RING_TICK_MS = 50;
const RING_TICK_MS_REDUCED = 100;
const SPRING_MS = SWIPE.SPRING_MS;
const INTRO_MS = 900;
const AUTO_SUBMIT_MS = 45000;
/** Live <video> nodes this class may hold at once (trap 36). */
const DECODER_CEILING = 2;
/** Rows the provider preloads ahead, per tag. */
const PREWARM = 6;
/** The claim, when the door never resolved one for us (QUICK SORT floor). */
const QUICK_CLAIM = Object.freeze({ loops: 48, stills: 32, canvasSafe: false });
/** How hard a minor jackpot rolls on a PERFECT at rung 2 or above. */
const MINOR_CHANCE = 0.16;
/** A cue's level may never exceed the tier's ceiling. */
const AUDIO_CEIL = Object.freeze([0.45, 0.6, 0.75, 0.9]);
/** Every kind that could paint over the stack is welded click-safe. */
const CLICK_SAFE_KINDS = Object.freeze({
  flash_burst: 1, gif_burst: 1, gif_rain: 1, sub_flash: 1,
});

/* ============================================================================
 * THE MODULE
 * ==========================================================================*/
export default {
  key: GAME_KEY,
  family: 'tracking',
  meaty: false,
  flagship: false,
  timeBudgetSec: 120,
  title: 'Sort',

  manifest: {
    effectsConsumed: [
      'wash', 'gif_burst', 'sub_flash', 'flash_burst', 'gif_rain', 'crt',
      'ambient_field', 'audio_trigger',
    ],
    /* NULL on purpose: this class claims its own TAGGED pool, because a sort
       whose piles came from the app-wide pull would have no piles. */
    assetNeeds: null,
    boardSizes: null,
    keybinds: [
      { verb: 'left', label_key: 'sort_left_key', default: 'ArrowLeft' },
      { verb: 'right', label_key: 'sort_right_key', default: 'ArrowRight' },
    ],
    settings: [
      {
        key: 'sort_bg_fade', kind: 'range', min: 0, max: 0.8, step: 0.05,
        default: 0.35, label_key: 'sort_bg_fade', hint_key: 'sort_bg_fade_hint',
      },
    ],
    peek: false,
    /** The shell runs `instance.setup()` before beginPlay, outside the clock. */
    setup: true,
  },

  /** Every lexicon row this class can render: the room's plus the door's. */
  lexicon: Object.assign({}, SORT_LEX, SETUP_LEX),

  create(ctx) {
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };
    const t = (k, f) => {
      try { return ctx.lexicon(k, f == null ? SORT_LEX[k] : f); }
      catch (e) { return f == null ? (SORT_LEX[k] || k) : f; }
    };
    const timers = createTimers();
    const reduced = probe('(prefers-reduced-motion: reduce)')
      || !!(ctx.motion && ctx.motion.reducedMotion);

    let S = null;
    let destroyed = false;
    let ended = false;
    let videoCount = 0;
    /* The setup door's answer, parked between setup() and start(). */
    let pending = { pool: null, quick: false, hot: false, thin: false, sources: null };
    const decks = { casino: null, pressure: null, trickster: null };

    const settingOf = (key, dflt) => {
      try {
        const bag = ctx.settings || {};
        return Object.prototype.hasOwnProperty.call(bag, key) ? bag[key] : dflt;
      } catch (e) { return dflt; }
    };
    const metaOf = () => {
      try { return (ctx.store && typeof ctx.store.gameMeta === 'function') ? (ctx.store.gameMeta(GAME_KEY) || {}) : {}; }
      catch (e) { return {}; }
    };
    const mergeMeta = (patch) => {
      try { if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') ctx.store.mergeGameMeta(GAME_KEY, patch); }
      catch (e) { say('meta write failed (the class is unaffected): ' + ((e && e.message) || e)); }
    };

    /* ==================================================================== *
     * THE BUS - one read-only event per moment, and the decks' only input.
     * LOT D's casino / pressure / trickster subscribe here; nothing they do
     * comes back the other way.
     * ==================================================================== */
    const listeners = new Map();
    const bus = {
      on(name, fn) {
        if (typeof fn !== 'function') return () => {};
        const key = String(name);
        let set = listeners.get(key);
        if (!set) { set = new Set(); listeners.set(key, set); }
        set.add(fn);
        return () => set.delete(fn);
      },
      off(name, fn) { const set = listeners.get(String(name)); if (set) set.delete(fn); },
      emit(name, payload) {
        const set = listeners.get(String(name));
        if (!set || !set.size) return;
        for (const fn of Array.from(set)) {
          try { fn(payload); }
          catch (e) { say('deck listener for ' + name + ' threw: ' + ((e && e.message) || e)); }
        }
      },
    };

    /* ==================================================================== *
     * THE ENGINE, AS A DECK SEES IT. Null-safe everywhere, frozen while the
     * class is frozen, and every burst over the stack is welded click-safe -
     * the top card is the ONE thing a press may land on.
     * ==================================================================== */
    const halted = () => destroyed || !S || S.paused || S.suspended;
    const audioCeil = () => AUDIO_CEIL[Math.max(0, Math.min(3, (S ? S.gradeTier : 1) - 1))];
    function weld(kind, opts) {
      const o = Object.assign({}, opts || {});
      if (CLICK_SAFE_KINDS[kind]) { o.clickSafe = true; o.clickable = false; delete o.onPop; }
      return o;
    }
    const deckEngine = {
      fire(kind, opts) {
        if (halted() || !ctx.engine || typeof ctx.engine.fire !== 'function') return null;
        try { return ctx.engine.fire(kind, weld(kind, opts)) || null; }
        catch (e) { say('fire(' + kind + ') failed'); return null; }
      },
      sustain(kind, opts) {
        if (halted() || !ctx.engine || typeof ctx.engine.sustain !== 'function') return null;
        try { return ctx.engine.sustain(kind, weld(kind, opts)) || null; }
        catch (e) { say('sustain(' + kind + ') failed'); return null; }
      },
      stop(kind) {
        try { if (ctx.engine && typeof ctx.engine.stop === 'function') ctx.engine.stop(kind); }
        catch (e) { /* noop */ }
      },
      ceremony(kind, opts) {
        if (halted() || !ctx.engine || typeof ctx.engine.ceremony !== 'function') return null;
        try { return ctx.engine.ceremony(kind, opts || {}) || null; }
        catch (e) { return null; }
      },
      channels() {
        try { return (ctx.engine && typeof ctx.engine.channels === 'function') ? ctx.engine.channels() : null; }
        catch (e) { return null; }
      },
      rewardRoll(opts) {
        try { return (ctx.engine && typeof ctx.engine.rewardRoll === 'function') ? ctx.engine.rewardRoll(opts || {}) : null; }
        catch (e) { return null; }
      },
      /** One cue, through the ONE audio owner, pitched by the chain. */
      audio(name, level, extra) {
        const lv = Math.min(audioCeil(), level == null ? 0.4 : Number(level) || 0);
        return deckEngine.fire('audio_trigger', Object.assign({ name, level: lv }, extra || {}));
      },
    };
    const deckTimers = {
      after(ms, fn) { return timers.after(ms, () => { if (halted()) return; fn(); }); },
      every(ms, fn) { return timers.every(ms, () => { if (halted()) return; fn(); }); },
      clear(id) { timers.cancel(id); },
    };
    function setHeat(h) {
      try { if (ctx.engine && typeof ctx.engine.setHeat === 'function') ctx.engine.setHeat(h); }
      catch (e) { /* noop */ }
    }
    function ceremony(kind, opts) {
      try {
        if (ctx.ceremonies && typeof ctx.ceremonies.reward === 'function') {
          return ctx.ceremonies.reward(kind, opts || {});
        }
      } catch (e) { /* a ceremony must never be the thing that fails */ }
      return false;
    }

    /* ==================================================================== *
     * THE DECK SEAMS (LOT D). Each file is imported DYNAMICALLY and the
     * import failing is the expected case today - G1 ships none of them.
     * A deck that refuses to build is null and one log line.
     * ==================================================================== */
    function deckState() {
      if (!S) return null;
      return {
        gradeTier: S.gradeTier, seed: S.seed, quick: S.quick,
        chain: S.chain, rung: S.rung, rungCap: S.rungCap,
        correct: S.correct, wrong: S.wrong, passed: S.passed, perfect: S.perfect,
        bestRung: S.bestRung, longestChain: S.longestChain,
        heat: S.heat, elapsedMs: S.startedAt ? now() - S.startedAt : 0,
        budgetMs: S.budgetMs, nodes: S.nodes,
      };
    }
    async function buildDecks() {
      const names = ['casino', 'pressure', 'trickster'];
      for (const name of names) {
        let mod = null;
        try { mod = await import('./' + name + '.js'); }
        catch (e) { continue; }                       // expected: LOT D has not landed
        if (!mod) continue;
        const make = typeof mod.create === 'function' ? mod.create
          : (mod.default && typeof mod.default.create === 'function') ? mod.default.create
            : (typeof mod.default === 'function' ? mod.default : null);
        if (!make) { say(name + ' exports no create()'); continue; }
        try {
          decks[name] = make({
            ctx, t, bus,
            S: deckState,
            timers: deckTimers,
            reduced,
            engine: deckEngine,
            log: say,
          }) || null;
          say(name + ' deck built');
        } catch (e) { decks[name] = null; say(name + ' refused: ' + ((e && e.message) || e)); }
      }
      /* A DECK BUILT LATE MUST NOT START BLIND. The imports are async, so the
       * room has already opened (and already set its opening heat) by the time
       * a deck exists; without this it would sit on 0 until the first swipe. */
      if (S && !halted() && (decks.casino || decks.pressure || decks.trickster)) {
        decksCall('setHeat', S.heat);
      }
    }
    function decksCall(method, ...args) {
      for (const name of ['casino', 'pressure', 'trickster']) {
        const d = decks[name];
        if (!d || typeof d[method] !== 'function') continue;
        try { d[method](...args); }
        catch (e) { say(name + '.' + method + ' threw: ' + ((e && e.message) || e)); }
      }
    }

    /* ==================================================================== *
     * SETUP - the door, outside the class clock (contract section 3).
     * ==================================================================== */
    function claimOpts(resolved) {
      return {
        sources: (resolved && Array.isArray(resolved.sources)) ? resolved.sources : [],
        want: { loops: 48, stills: 32 },
        perSourceMin: DECK.PER_SOURCE_MIN,
        /* The provider re-serves a dry tag in a SEEDED shuffle, so this string
         * has to be stable for a day or a retake would re-serve differently.
         * The class seed is not in hand yet (the door runs before start), and
         * the UTC day is exactly what the class seed opens with anyway. */
        seed: GAME_KEY + '|' + utcDateOf(''),
        timeoutMs: 6000,
      };
    }
    /** The tagged claim, with the QUICK SORT floor under it. Never throws. */
    async function claimPool(resolved) {
      const wantQuick = !!(resolved && resolved.quick);
      const tagged = ctx.assets && typeof ctx.assets.claimTagged === 'function';
      if (!wantQuick && tagged && resolved && resolved.sources && resolved.sources.length) {
        try {
          const pool = await ctx.assets.claimTagged(claimOpts(resolved));
          if (pool && typeof pool.next === 'function') {
            try { if (typeof pool.prewarm === 'function') pool.prewarm(PREWARM); } catch (e) { /* noop */ }
            const dead = ['target', 'noise'].filter((tg) => {
              try { return typeof pool.empty === 'function' ? pool.empty(tg) : false; }
              catch (e) { return false; }
            });
            if (!dead.length) return { pool, quick: false };
            say('claimTagged answered with an EMPTY ' + dead.join(' and ') + ' pile - falling back to QUICK SORT');
            try { if (typeof pool.dispose === 'function') pool.dispose(); } catch (e) { /* noop */ }
          }
        } catch (e) { say('claimTagged failed (' + ((e && e.message) || e) + ') - QUICK SORT'); }
      }
      /* QUICK SORT: the ordinary claim, wrapped so the deck speaks one API. */
      try {
        const claim = ctx.assets && typeof ctx.assets.claim === 'function'
          ? await ctx.assets.claim(QUICK_CLAIM) : null;
        if (claim && typeof claim.next === 'function') {
          try { if (typeof claim.prewarm === 'function') claim.prewarm(PREWARM); } catch (e) { /* noop */ }
          return { pool: wrapQuickPool(claim), quick: true };
        }
      } catch (e) { say('claim failed: ' + ((e && e.message) || e)); }
      return { pool: null, quick: true };
    }

    /**
     * The shell calls this BEFORE start(). Resolve true to play, false to leave.
     * LOT G2's real door drops into ./setup.js with no edit here: the contract
     * is createSetupDoor({ctx,t,mount,existing,assets,onPlay,onLeave}) answering
     * {el, setBusy(bool,msgKey), ghost(rows)->Promise, destroy()}.
     */
    function setup() {
      if (destroyed) return Promise.resolve(false);
      ensureStyle();
      const meta = metaOf();
      const existing = (meta && meta.setup) || null;
      let door = null;
      let settled = false;
      return new Promise((resolve) => {
        const finish = (ok) => {
          if (settled) return;
          settled = true;
          try { if (door && typeof door.destroy === 'function') door.destroy(); } catch (e) { /* noop */ }
          door = null;
          resolve(ok);
        };
        /* onPlay can fire MORE THAN ONCE: the door's thin strip offers "add
           another pick", which walks back to the picker and presses PLAY again.
           Each press is a generation; an older claim or ghost that lands late
           is disposed, never dealt. */
        let playGen = 0;
        const disposePending = () => {
          try { if (pending && pending.pool && typeof pending.pool.dispose === 'function') pending.pool.dispose(); }
          catch (e) { /* noop */ }
          pending = { pool: null, quick: false, hot: false, thin: false, sources: null };
        };
        const onPlay = (blob, resolved) => {
          if (settled) return;
          const gen = ++playGen;
          disposePending();
          const res = resolved || { sources: [], hot: false, quick: true };
          /* THE DOOR'S BLOB IS PAGE-OWNED and written HERE, once, so a door
             that crashed after picking cannot leave half a setup behind. */
          if (blob && typeof blob === 'object') {
            mergeMeta({
              setup: Object.assign({}, blob, {
                v: 1, updatedAt: new Date().toISOString(),
              }),
            });
          }
          try { if (door && typeof door.setBusy === 'function') door.setBusy(true, 'sort_dealing'); }
          catch (e) { /* noop */ }
          claimPool(res).then((got) => {
            if (settled || gen !== playGen) {
              try { if (got && got.pool && typeof got.pool.dispose === 'function') got.pool.dispose(); }
              catch (e) { /* noop */ }
              return;
            }
            pending = {
              pool: got.pool,
              quick: !!got.quick,
              hot: !!res.hot,
              thin: false,
              sources: res.sources || [],
            };
            try { if (door && typeof door.setBusy === 'function') door.setBusy(false, ''); }
            catch (e) { /* noop */ }
            /* THIN is only knowable now: the door raises its strip (non-blocking,
               the ghost still runs under it) and may press PLAY again. */
            if (!pending.quick && pending.pool && typeof pending.pool.thin === 'function') {
              const thin = ['target', 'noise'].filter((tg) => {
                try { return !!pending.pool.thin(tg); } catch (e) { return false; }
              });
              if (thin.length) {
                pending.thin = true;
                try { if (door && typeof door.warnThin === 'function') door.warnThin(thin); }
                catch (e) { /* noop */ }
              }
            }
            /* THE GHOST ROUND needs rows, which is why it runs AFTER the claim:
               two of the player's own cards swiping themselves is the rulebook,
               and a mock-up of somebody else's media would not be. */
            const ghost = door && typeof door.ghost === 'function' ? door.ghost : null;
            if (!ghost) { finish(true); return; }
            let done = false;
            const go = () => { if (!done) { done = true; if (gen === playGen) finish(true); } };
            try {
              const r = ghost(ghostRows(pending.pool, pending.quick));
              if (r && typeof r.then === 'function') r.then(go, go); else go();
            } catch (e) { say('ghost round threw: ' + ((e && e.message) || e)); go(); }
            /* a door whose ghost never resolves may not hold the class hostage */
            timers.after(12000, go);
          }, (e) => {
            if (settled || gen !== playGen) return;
            say('the deal failed: ' + ((e && e.message) || e));
            pending = { pool: null, quick: true, hot: false, thin: false, sources: [] };
            finish(true);
          });
        };
        const onLeave = () => finish(false);
        try {
          door = createSetupDoor({
            ctx, t, mount: ctx.root, existing, assets: ctx.assets, onPlay, onLeave,
          }) || null;
        } catch (e) {
          say('the door refused to open (' + ((e && e.message) || e) + ') - QUICK SORT');
          door = null;
        }
        /* NO DOOR IS NOT A CLOSED ROOM. A class that cannot open its own door
           deals the fallback rather than bouncing the player back to campus. */
        if (!door) onPlay(null, { sources: [], hot: false, quick: true });
      });
    }

    /** Two rows for the door's ghost round, one per pile. Never throws. */
    function ghostRows(pool, quick) {
      const out = { target: null, noise: null };
      if (!pool || typeof pool.next !== 'function') return out;
      for (const tag of ['target', 'noise']) {
        try { out[tag] = pool.next(tag, { prefer: tag === 'target' ? 'loop' : 'still' }) || null; }
        catch (e) { out[tag] = null; }
      }
      out.quick = !!quick;
      return out;
    }

    /* ==================================================================== *
     * THE ROOM
     * ==================================================================== */
    function mount() {
      const stage = el('div', 'g-sort');
      if (!stage) return null;
      setAttr(stage, 'data-reduced', reduced ? '1' : '0');
      setAttr(stage, 'data-chase', '0');
      try { if (ctx.root && ctx.root.appendChild) ctx.root.appendChild(stage); } catch (e) { /* noop */ }

      const hud = el('div', 'g-sort-hud');
      const chipChain = chip('is-chain', t('sort_chip_chain', 'Chain'), '0');
      const chipSorted = chip('is-sorted', t('sort_chip_sorted', 'Sorted'), '0');
      const chipClock = chip('is-clock', t('sort_chip_clock', 'Time left'), '2:00');
      const ladder = el('div', 'g-sort-ladder');
      const rungs = [];
      for (let i = 0; i <= CHAIN.MAX_RUNG; i++) {
        const pip = el('i', 'g-sort-rung');
        setAttr(pip, 'data-r', String(i));
        if (ladder && pip) ladder.appendChild(pip);
        rungs.push(pip);
      }
      const spacer = el('div', 'g-sort-spacer');
      if (hud) {
        if (chipChain) hud.appendChild(chipChain.el);
        if (ladder) hud.appendChild(ladder);
        if (spacer) hud.appendChild(spacer);
        if (chipSorted) hud.appendChild(chipSorted.el);
        if (chipClock) hud.appendChild(chipClock.el);
      }

      const playfield = el('div', 'g-sort-stage');
      const halo = el('div', 'g-sort-halo');
      const stack = el('div', 'g-sort-stack');
      const word = el('div', 'g-sort-word');
      if (playfield) {
        if (halo) playfield.appendChild(halo);
        if (stack) playfield.appendChild(stack);
        if (word) playfield.appendChild(word);
      }
      const note = el('div', 'g-sort-note');
      if (note) { note.hidden = true; }

      if (stage) {
        if (playfield) stage.appendChild(playfield);
        if (hud) stage.appendChild(hud);
        if (note) stage.appendChild(note);
      }
      return {
        stage, hud, playfield, stack, halo, word, note, ladder, rungs,
        chipChain, chipSorted, chipClock,
      };
    }
    function chip(cls, label, value) {
      const n = el('div', 'g-sort-chip ' + cls);
      if (!n) return null;
      const l = el('span', 'g-sort-chip-l');
      const b = el('b', '');
      if (l) { l.textContent = label; n.appendChild(l); }
      if (b) { b.textContent = String(value); n.appendChild(b); }
      return { el: n, label: l, value: b, set(v) { if (b) b.textContent = String(v); } };
    }

    /* ------------------------------------------------------------ the ring */
    const SVG_NS = 'http://www.w3.org/2000/svg';
    function svg(tag, cls) {
      try {
        if (typeof document === 'undefined' || typeof document.createElementNS !== 'function') return null;
        const n = document.createElementNS(SVG_NS, tag);
        if (cls && n) { try { n.setAttribute('class', cls); } catch (e) { /* noop */ } }
        return n;
      } catch (e) { return null; }
    }
    /**
     * The ripe ring, drawn ON the card it belongs to. It is an SVG rounded rect
     * driven by stroke-dashoffset off OUR clock, never a CSS animation: the ring
     * has to be able to stop dead the instant a card commits, and a keyframe
     * cannot be asked what time it is.
     *
     * With no SVG (the DOM double) the box still exists and still carries
     * `--sort-ring` and `data-ripe`, which is everything the suite reads and
     * everything reduced motion renders.
     */
    function makeRing() {
      const box = el('div', 'g-sort-ringbox');
      if (!box) return null;
      setAttr(box, 'data-ripe', 'fresh');
      setVar(box, '--sort-ring', '1');
      const root = svg('svg', '');
      if (root) {
        try {
          root.setAttribute('viewBox', '0 0 100 138');
          root.setAttribute('preserveAspectRatio', 'none');
        } catch (e) { /* noop */ }
        const track = svg('rect', 'g-sort-ring-track');
        const arc = svg('rect', 'g-sort-ring-arc');
        for (const r of [track, arc]) {
          if (!r) continue;
          try {
            r.setAttribute('x', '2'); r.setAttribute('y', '2');
            r.setAttribute('width', '96'); r.setAttribute('height', '134');
            r.setAttribute('rx', '14'); r.setAttribute('ry', '14');
            r.setAttribute('pathLength', '1000');
          } catch (e) { /* noop */ }
          root.appendChild(r);
        }
        setVar(box, '--sort-ring-len', '1000');
        box.appendChild(root);
      }
      return {
        el: box,
        /** @param {number} left 1 -> full ring, 0 -> closed */
        set(left, ripe) {
          setVar(box, '--sort-ring', String(Math.round(clamp01(left) * 1000) / 1000));
          setAttr(box, 'data-ripe', ripe || 'fresh');
        },
      };
    }

    /* ------------------------------------------------------------ the card */
    /**
     * One card node. The FACE is the decoder budget's whole story: only the top
     * card may hold a live <video>, the second gets the same url in an <img>
     * (a gif keeps moving, an mp4 simply never paints and the drawn back shows
     * through), and the third is the back alone.
     */
    function mintCard(card, depth) {
      const node = el('div', 'g-sort-card');
      if (!node) return null;
      setAttr(node, 'data-depth', String(depth));
      setAttr(node, 'data-tag', card.tag);
      setAttr(node, 'data-seen', card.seen ? '1' : '0');
      setVar(node, '--sort-depth', String(depth));
      setVar(node, '--sort-scale', String(scaleForDepth(depth)));
      const back = el('div', 'g-sort-back');
      if (back) {
        const h = Math.round(hash01(card.url + '|back') * 360);
        setVar(back, '--sort-back-h', String(h));
        setVar(back, '--sort-back-h2', String((h + 310) % 360));
        node.appendChild(back);
      }
      const face = mintFace(card, depth);
      if (face) node.appendChild(face);
      const yes = stampNode('yes', '♥', t('sort_stamp_yes', 'YES'));
      const no = stampNode('no', '⊘', t('sort_stamp_no', 'NO'));
      if (yes) node.appendChild(yes);
      if (no) node.appendChild(no);
      let ring = null;
      if (depth === 0) {
        ring = makeRing();
        if (ring && ring.el) node.appendChild(ring.el);
      }
      return { node, face, ring, yes, no, card, depth, video: face && face.tagName === 'VIDEO' };
    }
    function stampNode(side, glyph, text) {
      const n = el('div', 'g-sort-stamp ' + side);
      if (!n) return null;
      const g = el('span', 'g-sort-glyph');
      const w = el('span', 'g-sort-word-t');
      if (g) { g.textContent = glyph; n.appendChild(g); }
      if (w) { w.textContent = text; n.appendChild(w); }
      setAttr(n, 'aria-hidden', 'true');
      return n;
    }
    function mintFace(card, depth) {
      if (!card || !card.url) return null;
      if (depth > 1) return null;                       // the third card is a back
      const wantVideo = depth === 0 && isVideoUrl(card.url, card.mime);
      if (wantVideo && videoCount < DECODER_CEILING) {
        const v = el('video', 'g-sort-face');
        if (!v) return null;
        videoCount += 1;
        v.muted = true; v.loop = true; v.autoplay = true; v.playsInline = true;
        try {
          v.setAttribute('muted', ''); v.setAttribute('loop', '');
          v.setAttribute('playsinline', ''); v.setAttribute('preload', 'metadata');
          v.setAttribute('disablepictureinpicture', '');
        } catch (e) { /* DOM double */ }
        try { v.disableRemotePlayback = true; } catch (e) { /* not everywhere */ }
        if (typeof v.addEventListener === 'function') {
          v.addEventListener('error', () => {
            try { v.removeAttribute('src'); if (v.load) v.load(); } catch (e) { /* ignore */ }
            try { if (v.parentNode) v.remove(); } catch (e) { /* ignore */ }
          });
        }
        v.src = card.url;
        if (typeof v.play === 'function') {
          try { const p = v.play(); if (p && p.catch) p.catch(() => {}); } catch (e) { /* autoplay policy */ }
        }
        return v;
      }
      if (wantVideo) return null;                       // budget spent: the back stands
      const img = el('img', 'g-sort-face');
      if (!img) return null;
      img.alt = '';
      try { img.setAttribute('draggable', 'false'); img.setAttribute('decoding', 'async'); }
      catch (e) { /* DOM double */ }
      if (typeof img.addEventListener === 'function') {
        img.addEventListener('error', () => { try { if (img.parentNode) img.remove(); } catch (e) { /* ignore */ } });
      }
      img.src = card.url;
      return img;
    }
    function dropCard(live) {
      if (!live) return;
      if (live.video) {
        videoCount = Math.max(0, videoCount - 1);
        try { const v = live.face; if (v) { if (v.pause) v.pause(); v.removeAttribute('src'); if (v.load) v.load(); } }
        catch (e) { /* noop */ }
      }
      try { if (live.node && live.node.remove) live.node.remove(); } catch (e) { /* noop */ }
    }

    /* ==================================================================== *
     * THE DEAL
     * ==================================================================== */
    /** The next card off the deck; a spent deck is re-shuffled, never empty. */
    function nextCard() {
      if (!S) return null;
      if (!S.cards.length) return null;
      if (S.cursor >= S.cards.length) {
        /* THE PASSES COME BACK FIRST, then the deck is re-shuffled and walked
         * again. Repeats are fine in a sort - they are the whole reason the
         * SEEN trickster is free - and a 0.75s ring burns 160 cards in 120s,
         * so a deck that could run out would be a room that stopped. */
        if (S.passQueue.length) {
          const back = S.passQueue.splice(0, S.passQueue.length);
          S.cards = S.cards.concat(back);
        } else {
          S.cards = shuffled(S.cards, S.rngDeal);
          S.recycles += 1;
        }
        S.cursor = 0;
      }
      const card = S.cards[S.cursor++];
      if (!card) return null;
      return Object.assign({}, card, { dealt: S.dealt++ });
    }
    /** Fill the stack to three and re-seat the depths. */
    function fillStack() {
      if (!S || !S.nodes || !S.nodes.stack) return;
      let guard = 0;
      while (S.live.length < 3 && guard++ < 8) {
        const card = nextCard();
        if (!card) break;
        const live = mintCard(card, S.live.length);
        if (!live) break;
        S.live.push(live);
        try { S.nodes.stack.appendChild(live.node); } catch (e) { /* noop */ }
      }
      reseat();
    }
    function reseat() {
      if (!S) return;
      for (let i = 0; i < S.live.length; i++) {
        const live = S.live[i];
        if (!live) continue;
        live.depth = i;
        setAttr(live.node, 'data-depth', String(i));
        setVar(live.node, '--sort-depth', String(i));
        setVar(live.node, '--sort-scale', String(scaleForDepth(i)));
        /* THE SECOND CARD GROWS A FACE when it becomes the top one; a card that
         * was minted at depth 2 never had one. */
        if (i <= 1 && !live.face) {
          const face = mintFace(live.card, i);
          if (face) {
            live.face = face;
            live.video = face.tagName === 'VIDEO';
            try { live.node.appendChild(face); } catch (e) { /* noop */ }
          }
        }
        if (i === 0 && !live.ring) {
          live.ring = makeRing();
          if (live.ring && live.ring.el) { try { live.node.appendChild(live.ring.el); } catch (e) { /* noop */ } }
        }
      }
    }

    /* ==================================================================== *
     * THE RING CLOCK - ours, not a keyframe's.
     * ==================================================================== */
    function armTop() {
      if (!S || halted() || S.over) return;
      const top = S.live[0];
      if (!top) return;
      S.ringMs = ringMsFor(S.rung);
      S.ringStart = now();
      S.armed = true;
      setVar(S.nodes.stage, '--sort-rung', String(S.rung));
      setAttr(S.nodes.stage, 'data-chase', S.rung >= 6 ? '1' : '0');
      if (top.ring) top.ring.set(1, 'fresh');
      if (S.swipe) S.swipe.enabled(true);
      timers.cancel(S.ringTimer);
      S.ringTimer = timers.every(reduced ? RING_TICK_MS_REDUCED : RING_TICK_MS, ringTick);
      bus.emit('deal', { card: top.card, rung: S.rung, ringMs: S.ringMs, chain: S.chain });
    }
    function ringTick() {
      if (!S || !S.armed || halted() || S.over) return;
      const top = S.live[0];
      if (!top) return;
      const elapsed = now() - S.ringStart;
      const v = verdictFor(elapsed, S.ringMs);
      if (top.ring) top.ring.set(1 - Math.min(1, elapsed / S.ringMs), v.just ? 'just' : v.perfect ? 'ripe' : 'fresh');
      if (elapsed >= S.ringMs) onPass();
    }
    function disarm() {
      if (!S) return;
      S.armed = false;
      timers.cancel(S.ringTimer);
      S.ringTimer = 0;
      if (S.swipe) S.swipe.enabled(false);
    }

    /* ==================================================================== *
     * THE THREE MOMENTS: commit, pass, and the wrong swipe (which is a commit
     * that happens to be wrong - it is HONOURED, never blocked).
     * ==================================================================== */
    function onCommit(dir) {
      if (!S || !S.armed || halted() || S.over) return false;
      const top = S.live[0];
      if (!top) return false;
      const elapsed = now() - S.ringStart;
      const v = verdictFor(elapsed, S.ringMs);
      const correct = judge(top.card, dir, S.quick);
      const rungBefore = S.rung;
      disarm();

      const beat = {
        card: top.card, dir, correct,
        perfect: correct && v.perfect,
        just: correct && v.just,
        almost: correct && v.almost,
        chain: S.chain, rung: S.rung, ringFrac: clamp01(elapsed / S.ringMs),
        elapsedMs: elapsed, ringMs: S.ringMs,
      };

      if (correct) {
        const step = afterClean(S.chain, S.rung, S.rungCap);
        S.chain = step.chain;
        S.rung = step.rung;
        S.correct += 1;
        S.longestChain = Math.max(S.longestChain, S.chain);
        S.bestRung = Math.max(S.bestRung, S.rung);
        if (v.perfect) S.perfect += 1;
        if (v.just) S.just += 1;
        beat.chain = S.chain;
        beat.rung = S.rung;
        cue('bubble_pop', 0.42, { pitch: chimePitch(Math.min(CHAIN.CHIME_CAP, S.chain)) });
        if (v.just) verdictWord(t('sort_just', 'JUST'), 'gold');
        else if (v.perfect) verdictWord(t('sort_perfect', 'PERFECT'), 'gold');
        if (step.rungUp) onRungUp(step.from, step.rung);
        if (v.perfect) rollMinor(beat);
      } else {
        const step = afterWrong(S.chain, S.rung);
        S.chain = step.chain;
        S.rung = step.rung;
        S.wrong += 1;
        S.wrongsSinceRoyalFloor += 1;
        beat.chain = S.chain;
        beat.rung = S.rung;
        wrongBeat(step);
      }
      /* ALMOST is the near-miss you LOST, and it is only ever staged on a
       * CORRECT swipe: telling a player they nearly hit the gold arc on a card
       * they called wrong would be the room talking about the wrong thing. */
      if (correct && v.almost) {
        verdictWord(t('sort_almost', 'ALMOST'), '');
        ceremony('near_miss', { text: t('sort_almost', 'ALMOST'), target: S.nodes.playfield, scale: 0.5 });
      }

      flyOut(top, dir, !correct);
      paintHud();
      heat();
      bus.emit('commit', beat);
      if (!correct) bus.emit('wrong', { card: top.card, rungFrom: rungBefore, rungTo: S.rung });
      if (beat.perfect) bus.emit('perfect', { chain: S.chain, rung: S.rung, just: beat.just });

      S.live.shift();
      reseat();
      timers.after(reduced ? SWIPE.FADE_MS : SPRING_MS, () => {
        if (!S || destroyed || S.over) return;
        fillStack();
        armTop();
      });
      return true;
    }

    function onPass() {
      if (!S || !S.armed || halted() || S.over) return;
      const top = S.live[0];
      if (!top) return;
      disarm();
      S.passed += 1;
      /* A PASS IS NOT AN ERROR. No chain, no rung, no accuracy: the card sinks
       * under the stack and is dealt again later, and that is the whole of it. */
      S.passQueue.push(Object.assign({}, top.card));
      /* a sinking card is not the top card either (see flyOut) */
      setAttr(top.node, 'data-depth', 'x');
      setAttr(top.node, 'data-gone', '1');
      addCls(top.node, 'is-sink');
      verdictWord(t('sort_pass', 'PASSED'), 'grey');
      cue('whisper', 0.22, { pitch: 0.85 });
      bus.emit('pass', { card: top.card, chain: S.chain, rung: S.rung });
      const node = top;
      timers.after(reduced ? SWIPE.FADE_MS : 260, () => dropCard(node));
      S.live.shift();
      reseat();
      paintHud();
      timers.after(reduced ? SWIPE.FADE_MS : SPRING_MS, () => {
        if (!S || destroyed || S.over) return;
        fillStack();
        armTop();
      });
    }

    /** The wrong swipe: grey stamp, muted thud, SHIVER, one rung down on a fade. */
    function wrongBeat(step) {
      verdictWord(t('sort_wrong', 'WRONG'), 'grey');
      cue('bump', 0.3, { pitch: 0.62 });
      const stage = S.nodes.playfield;
      addCls(stage, 'is-shiver');
      timers.after(260, () => delCls(stage, 'is-shiver'));
      addCls(S.nodes.ladder, 'is-fading');
      timers.after(step.fadeMs, () => delCls(S.nodes.ladder, 'is-fading'));
      if (step.rungDown) bus.emit('rung', { from: step.from, to: step.rung, down: true });
    }

    function onRungUp(from, to) {
      bus.emit('rung', { from, to, down: false });
      cue('streak', 0.42, { pitch: chimePitch(Math.min(CHAIN.CHIME_CAP, to)) });
      if (S.wall) S.wall.show(to, false);
      /* THE MAJOR JACKPOTS: rungs 3, 5 and 7, once each per class. */
      if (isMajorRung(to) && S.majorsPaid.indexOf(to) < 0) {
        S.majorsPaid.push(to);
        jackpot(0.55 + 0.12 * S.majorsPaid.length, 'major@r' + to);
      }
      /* THE ROYAL: the first rung 8 with nothing wrong since rung 5. */
      if (to >= CHAIN.ROYAL_CLEAN_FROM && from < CHAIN.ROYAL_CLEAN_FROM) {
        S.wrongsSinceRoyalFloor = 0;
      }
      if (isRoyal({ rung: to, wrongsSinceRoyalFloor: S.wrongsSinceRoyalFloor, royalPaid: S.royal })) {
        S.royal = true;
        verdictWord(t('sort_royal', 'ROYAL'), 'gold');
        jackpot(1, 'royal');
      }
    }

    /** The minor jackpot: rolled on a PERFECT at rung 2 or above. */
    function rollMinor(beat) {
      if (S.rung < CHAIN.MINOR_MIN_RUNG) return;
      const vr = deckEngine.rewardRoll({ success: true, streak: S.chain, heat: S.heat });
      const win = vr && typeof vr === 'object'
        ? !!vr.jackpot
        : (S.rngJack() < MINOR_CHANCE * (0.6 + 0.8 * S.heat));
      if (!win) return;
      jackpot(0.34, 'minor@' + beat.card.i);
    }
    function jackpot(intensity, why) {
      if (halted()) return;
      S.jackpots += 1;
      ceremony('jackpot', {
        text: why === 'royal' ? t('sort_royal', 'ROYAL') : undefined,
        target: S.nodes.playfield,
        intensity,
      });
      cue('jackpot', 0.5, { pitch: why === 'royal' ? 1.35 : (0.95 + 0.3 * clamp01(intensity)) });
      bus.emit('jackpot', { intensity, why, rung: S.rung, chain: S.chain });
      say('jackpot ' + why + ' at rung ' + S.rung);
    }
    function cue(name, level, extra) { deckEngine.audio(name, level, extra); }

    /* ------------------------------------------------------------- the fly */
    function flyOut(live, dir, wrong) {
      if (!live) return;
      const px = S.swipe ? S.swipe.flyPx() : 0;
      /* THE STACK HAS EXACTLY ONE TOP CARD, INCLUDING RIGHT NOW. reseat() seats
       * the new live[0] at data-depth="0", but the node we are throwing kept
       * the same attribute for the ~450ms it spends leaving - so for that whole
       * beat two nodes answered [data-depth="0"], and the one the sheet drew on
       * top (z-index 3, cursor grab) was the card the player had just finished
       * with. Anything reading the DOM for "the card I am being timed on" - the
       * rig, a capture, a probe - got the wrong one. It is not a depth any more
       * the instant it is thrown. */
      setAttr(live.node, 'data-depth', 'x');
      setAttr(live.node, 'data-gone', '1');
      addCls(live.node, 'is-gone');
      if (wrong) addCls(live.node, 'is-wrong');
      delCls(live.node, 'is-held');
      setVar(live.node, '--sort-dx', (dir === 'right' ? px : -px) + 'px');
      setVar(live.node, '--sort-tilt', (dir === 'right' ? SWIPE.TILT_CAP : -SWIPE.TILT_CAP) + 'deg');
      setVar(live.node, '--sort-a-yes', dir === 'right' ? '1' : '0');
      setVar(live.node, '--sort-a-no', dir === 'left' ? '1' : '0');
      const card = live.card;
      const flyMs = reduced ? SWIPE.FADE_MS : SWIPE.FLY_MS;
      const shrinkMs = reduced ? 0 : SWIPE.SHRINK_MS;
      timers.after(flyMs + shrinkMs, () => {
        dropCard(live);
        if (!S || destroyed) return;
        try { if (S.wall) S.wall.land(card, { wrong: !!wrong }); } catch (e) { /* noop */ }
      });
    }

    /* ----------------------------------------------------------- the paint */
    function verdictWord(text, tone) {
      const n = S && S.nodes ? S.nodes.word : null;
      if (!n) return;
      n.textContent = String(text || '');
      setAttr(n, 'data-tone', tone || '');
      delCls(n, 'show');
      /* re-trigger the keyframe: remove, force a reflow, add (styles.css trap 4) */
      try { void (n.offsetWidth); } catch (e) { /* DOM double */ }
      addCls(n, 'show');
    }
    function paintHud() {
      if (!S || !S.nodes) return;
      const n = S.nodes;
      if (n.chipChain) n.chipChain.set(S.chain);
      if (n.chipSorted) n.chipSorted.set(S.correct + S.wrong);
      if (n.chipChain && n.chipChain.el && n.chipChain.el.classList) {
        if (S.rung >= 5) n.chipChain.el.classList.add('is-hot');
        else n.chipChain.el.classList.remove('is-hot');
      }
      for (let i = 0; i < n.rungs.length; i++) {
        const pip = n.rungs[i];
        if (!pip || !pip.classList) continue;
        pip.classList.remove('on', 'at', 'capped');
        if (i > S.rungCap) pip.classList.add('capped');
        else if (i === S.rung) pip.classList.add('at');
        else if (i < S.rung) pip.classList.add('on');
      }
      setVar(n.ladder, '--sort-ladder', String(ladderFrac(S.chain, S.rung, S.rungCap).toFixed(3)));
    }
    function paintClock() {
      if (!S || !S.nodes || !S.nodes.chipClock) return;
      const left = Math.max(0, S.budgetMs - (now() - S.startedAt));
      const secs = Math.ceil(left / 1000);
      const mm = Math.floor(secs / 60);
      const ss = String(secs % 60).padStart(2, '0');
      S.nodes.chipClock.set(mm + ':' + ss);
      if (S.budgetMs > 0 && left <= 0) bell();
    }
    function heat() {
      if (!S) return 0;
      const progress = S.budgetMs > 0 ? clamp01((now() - S.startedAt) / S.budgetMs) : 0;
      const h = clamp01(0.2 + 0.35 * progress + 0.45 * (S.rung / CHAIN.MAX_RUNG));
      S.heat = h;
      setHeat(h);
      decksCall('setHeat', h);
      return h;
    }
    function note(text) {
      if (!S || !S.nodes || !S.nodes.note) return;
      const n = S.nodes.note;
      if (!text) { n.hidden = true; return; }
      n.textContent = String(text);
      n.hidden = false;
    }

    /* ==================================================================== *
     * THE RULES SHEET. SORT's rulebook is the ghost round at the door, so
     * this is deliberately one drawn card between two words. Shown every
     * class by default; with "Skip class tutorials" on, still ONCE PER TIER
     * (the shell's contract, and the memory is the game's own).
     * ==================================================================== */
    function howtoSeenTiers() {
      const m = metaOf();
      return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
    }
    function howto(onDone) {
      const seen = howtoSeenTiers();
      if (ctx.hideTutorial === true && seen.indexOf(S.gradeTier) >= 0) { onDone(); return; }
      const veil = el('div', 'g-sort-howto');
      const card = el('div', 'g-sort-howto-card');
      if (!veil || !card) { onDone(); return; }
      const h = el('div', 'g-sort-howto-h');
      if (h) { h.textContent = t('sort_rules_title', 'One rule'); card.appendChild(h); }
      const demo = el('div', 'g-sort-demo');
      const left = el('div', 'g-sort-demo-side no');
      const mid = el('div', 'g-sort-demo-card');
      const right = el('div', 'g-sort-demo-side yes');
      if (left) {
        const b = el('b', ''); if (b) { b.textContent = t('sort_stamp_no', 'NO'); left.appendChild(b); }
        const s = el('span', ''); if (s) { s.textContent = t('sort_rules_left', 'Left: everything else.'); left.appendChild(s); }
      }
      if (right) {
        const b = el('b', ''); if (b) { b.textContent = t('sort_stamp_yes', 'YES'); right.appendChild(b); }
        const s = el('span', ''); if (s) { s.textContent = t('sort_rules_right', 'Right: yours.'); right.appendChild(s); }
      }
      if (demo) {
        if (left) demo.appendChild(left);
        if (mid) demo.appendChild(mid);
        if (right) demo.appendChild(right);
        card.appendChild(demo);
      }
      const lines = el('div', 'g-sort-howto-lines');
      if (lines) {
        for (const key of ['sort_rules_ring', 'sort_rules_pass', 'sort_rules_keys']) {
          const p = el('p', '');
          if (p) { p.textContent = t(key, SORT_LEX[key]); lines.appendChild(p); }
        }
        card.appendChild(lines);
      }
      const go = el('button', 'btn primary');
      const actions = el('div', 'g-sort-howto-actions');
      if (go) {
        go.textContent = t('sort_rules_go', 'Begin');
        try { if (ctx.exits && typeof ctx.exits.sign === 'function') ctx.exits.sign(go, { dir: 'go' }); }
        catch (e) { /* the sign is decoration */ }
        let done = false;
        const dismiss = () => {
          if (done || !S) return;
          done = true;
          const list = howtoSeenTiers();
          if (list.indexOf(S.gradeTier) < 0) { list.push(S.gradeTier); mergeMeta({ howtoTiers: list }); }
          try { veil.remove(); } catch (e) { /* noop */ }
          onDone();
        };
        if (typeof go.addEventListener === 'function') go.addEventListener('click', dismiss);
        if (actions) { actions.appendChild(go); card.appendChild(actions); }
      }
      veil.appendChild(card);
      try { S.nodes.stage.appendChild(veil); } catch (e) { onDone(); return; }
      S.howtoEl = veil;
      if (!go) onDone();
    }

    /* ==================================================================== *
     * START
     * ==================================================================== */
    function start(classSpec) {
      const spec = classSpec || {};
      const gradeTier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
      const seed = String(spec.seed == null ? (GAME_KEY + '|noseed') : spec.seed);
      const budgetSec = Math.max(0, Math.min(600, Number(spec.timeBudgetSec) || 0));
      const retake = !!spec.retake;
      ensureStyle();

      const nodes = mount();
      if (!nodes) { say('the stage could not be built'); return; }

      S = {
        gradeTier, seed, retake,
        rungCap: capForTier(gradeTier),
        budgetMs: budgetSec > 0 ? budgetSec * 1000 : 0,
        day: utcDateOf(seed),
        nodes,
        rngDeal: makeRng(seed + '|deal'),
        rngJack: makeRng(seed + '|jack'),
        quick: !!pending.quick,
        pool: pending.pool,
        cards: [], cursor: 0, dealt: 0, recycles: 0,
        passQueue: [],
        live: [],
        wall: null,
        swipe: null,
        chain: 0, rung: 0, bestRung: 0, longestChain: 0,
        correct: 0, wrong: 0, passed: 0, perfect: 0, just: 0,
        wrongsSinceRoyalFloor: 0, majorsPaid: [], royal: false, jackpots: 0,
        heat: 0.2,
        armed: false, ringMs: ringMsFor(0), ringStart: 0, ringTimer: 0,
        clockTimer: 0, autoTimer: 0,
        startedAt: now(),
        frozenAt: 0, frozenElapsed: 0,
        paused: false, suspended: false, over: false, submitted: false,
        howtoEl: null, endEl: null, result: null,
        thin: false, hot: !!pending.hot,
      };

      const fade = Number(settingOf('sort_bg_fade', 0.35));
      const capBg = (() => { try { return Number(ctx.caps && ctx.caps.bgIntensity); } catch (e) { return 1; } })();
      setVar(nodes.stage, '--sort-bg-fade',
        String(clamp01(isFinite(fade) ? fade : 0.35) * (isFinite(capBg) && capBg >= 0 ? capBg : 1)));

      S.wall = createWall({
        mount: nodes.stage, tier: gradeTier, reduced, seed, log: say,
        stageOf: () => stageBox(),
      });
      /* the wall lives BEHIND the playfield: it was appended last, so move it */
      try { if (S.wall.el && nodes.stage.insertBefore) nodes.stage.insertBefore(S.wall.el, nodes.playfield); }
      catch (e) { /* the sheet's z-index carries it either way */ }

      S.swipe = createSwipe({
        el: nodes.stack,
        reduced,
        now,
        widthOf: () => cardWidth(),
        viewportOf: () => stageBox().w,
        onGrab: () => {
          if (!S || !S.armed) return;
          const top = S.live[0];
          if (top) addCls(top.node, 'is-held');
          bus.emit('grab', { card: top ? top.card : null, rung: S.rung });
        },
        onDrag: (d) => {
          /* A CARD THAT CANNOT COMMIT DOES NOT ANSWER. The stack is dealt at
           * INTRO_MS before armTop() arms it, and for that first ~900ms the
           * hand was live while the class was not: the stamp tracked the finger
           * and leaned YES while the card itself refused to move or commit. The
           * gesture is gated on the same armed state everything else is. */
          if (!S || !S.armed) return;
          const top = S.live[0];
          if (!top) return;
          setVar(top.node, '--sort-dx', d.dx + 'px');
          setVar(top.node, '--sort-tilt', d.tilt.toFixed(2) + 'deg');
          setVar(top.node, '--sort-a-yes', d.side === 'right' ? d.alpha.toFixed(3) : '0');
          setVar(top.node, '--sort-a-no', d.side === 'left' ? d.alpha.toFixed(3) : '0');
          bus.emit('drag', { dx: d.dx, side: d.side, alpha: d.alpha, card: top.card });
        },
        onRelease: (r) => {
          if (!S || !S.armed) return;
          const top = S.live[0];
          if (!top) return;
          delCls(top.node, 'is-held');
          if (r.commit) return;
          /* THE RUBBER BAND: under both the threshold and the fling, home in
           * 260ms. The class is unchanged; a look is not a swipe. */
          addCls(top.node, 'is-band');
          setVar(top.node, '--sort-dx', '0px');
          setVar(top.node, '--sort-tilt', '0deg');
          setVar(top.node, '--sort-a-yes', '0');
          setVar(top.node, '--sort-a-no', '0');
          timers.after(SWIPE.BAND_MS, () => delCls(top.node, 'is-band'));
        },
        onCommit: (c) => { onCommit(c.dir); },
      });
      /* AND THE HAND STAYS DOWN UNTIL THERE IS A CARD TO PLAY. createSwipe binds
       * enabled, so without this the pointer and the keys are both live over an
       * unarmed stack. armTop() is the one thing that lifts it. */
      S.swipe.enabled(false);

      bindKeys();
      buildDecks();

      /* THE DECK. A retake on the same day and seed re-deals the exact rows the
       * first run dealt (the cache in game meta), because the day's script IS
       * the day's script - it is the same law the shell's seed follows. */
      const meta = metaOf();
      if (retake && cacheUsable(meta.deck, S.day, seed)) {
        const built = deckFromRows(meta.deck.rows, !!meta.deck.quick);
        S.cards = built.cards;
        say('retake: re-dealing the cached deck of ' + built.cards.length);
        openRoom();
      } else if (S.pool) {
        dealFromPool();
        openRoom();
      } else {
        /* No door ran (a shell with no setup hook) - claim the floor ourselves
         * and open when it lands. The ring never starts before a card does. */
        note(t('sort_dealing', 'Dealing your deck'));
        claimPool({ sources: [], quick: true }).then((got) => {
          if (!S || destroyed) return;
          S.pool = got.pool;
          S.quick = !!got.quick;
          dealFromPool();
          note('');
          openRoom();
        }, () => { if (S && !destroyed) openRoom(); });
      }

      say('class started: tier ' + gradeTier + ', cap rung ' + S.rungCap
        + ', ring ' + ringMsFor(0) + '->' + ringMsFor(S.rungCap) + 'ms, budget ' + budgetSec + 's'
        + (S.quick ? ', QUICK SORT' : '') + (retake ? ', retake' : ''));
    }

    function dealFromPool() {
      if (!S || !S.pool) return;
      const built = buildDeck({
        pool: S.pool, seed: S.seed, tier: S.gradeTier, quick: S.quick,
        rng: makeRng(S.seed + '|deck'),
      });
      S.cards = built.cards;
      S.thin = !!built.thin;
      /* THE RETAKE CACHE, written ONCE at the deal. */
      if (S.cards.length) {
        mergeMeta({
          deck: {
            day: S.day, seed: S.seed, quick: S.quick,
            rows: rowsFromCards(S.cards),
          },
        });
      }
      say('dealt ' + built.counts.size + ' cards ('
        + built.counts.target + ' target / ' + built.counts.noise + ' noise, '
        + built.counts.loops + ' loops, max run ' + built.counts.maxRun
        + ', ' + built.counts.distinct + ' distinct)'
        + (built.thin ? ' THIN: ' + built.thinTags.join(' and ') : ''));
    }

    function openRoom() {
      if (!S || destroyed) return;
      if (!S.cards.length) {
        note(t('sort_no_deck', 'No cards to sort. Your attendance is safe.'));
        say('no cards: the class opens empty and the bell still rings');
      }
      if (S.thin) note(t('sort_thin', 'Thin pile: expect repeats.'));
      else if (S.quick && S.cards.length) note(t('sort_quick', SORT_LEX.sort_quick));
      paintHud();
      heat();
      howto(() => {
        if (!S || destroyed || S.over) return;
        S.howtoEl = null;
        /* THE CLOCK STARTS HERE AND NOWHERE ELSE. Not at the door (the shell's
         * setup hook runs outside the budget), not at the rules sheet: a bar
         * draining over a sheet the player is still reading is the shell's most
         * confident lie, and this class has two screens before its first card. */
        S.startedAt = now();
        if (S.budgetMs > 0) S.clockTimer = timers.every(250, paintClock);
        decksCall('start');
        fillStack();
        timers.after(reduced ? 0 : INTRO_MS, () => {
          if (!S || destroyed || S.over) return;
          armTop();
        });
      });
    }

    function stageBox() {
      try {
        const n = S && S.nodes ? S.nodes.stage : null;
        if (n && typeof n.getBoundingClientRect === 'function') {
          const r = n.getBoundingClientRect();
          if (r && r.width > 0) return { w: r.width, h: r.height || r.width * 0.6 };
        }
      } catch (e) { /* noop */ }
      return { w: 1280, h: 720 };
    }
    function cardWidth() {
      try {
        const top = S && S.live[0] ? S.live[0].node : null;
        if (top && typeof top.getBoundingClientRect === 'function') {
          const r = top.getBoundingClientRect();
          if (r && r.width > 0) return r.width;
        }
      } catch (e) { /* noop */ }
      return Math.max(160, Math.min(340, stageBox().w * 0.26));
    }

    /* -------------------------------------------------------------- keys -- */
    let unbindKeys = () => {};
    function bindKeys() {
      const offs = [];
      try {
        if (ctx.keys && typeof ctx.keys.on === 'function') {
          offs.push(ctx.keys.on('left', () => { if (S && S.swipe) S.swipe.key('left'); }));
          offs.push(ctx.keys.on('right', () => { if (S && S.swipe) S.swipe.key('right'); }));
        }
      } catch (e) { say('keybind wiring failed: ' + ((e && e.message) || e)); }
      unbindKeys = () => { for (const off of offs) { try { off(); } catch (e) { /* noop */ } } };
    }

    /* ==================================================================== *
     * THE BELL
     * ==================================================================== */
    function bell() {
      if (!S || S.over) return;
      S.over = true;
      disarm();
      timers.cancel(S.clockTimer);
      S.clockTimer = 0;
      decksCall('end');
      /* THE WALL TAKES THE STAGE for three seconds before anything is said
       * about it. What you sorted is the last thing the room shows you. */
      try { if (S.wall) { S.wall.show(S.rung, true); S.wall.bleed(true); } } catch (e) { /* noop */ }
      for (const live of S.live.slice()) { addCls(live.node, 'is-sink'); }
      timers.after(reduced ? 400 : WALL.BLEED_MS, () => {
        if (!S || destroyed) return;
        for (const live of S.live.splice(0, S.live.length)) dropCard(live);
        showTicket();
      });
      say('bell: ' + S.correct + ' right / ' + S.wrong + ' wrong / ' + S.passed + ' passed, '
        + 'best rung ' + S.bestRung + ', longest chain ' + S.longestChain);
    }

    function showTicket() {
      if (!S || S.endEl) return;
      const g = gradeClass({
        correct: S.correct, wrong: S.wrong, perfect: S.perfect,
        passed: S.passed, bestRung: S.bestRung, rungCap: S.rungCap,
        longestChain: S.longestChain,
      });
      S.result = g;

      const veil = el('div', 'g-sort-end');
      const paper = el('div', 'g-sort-ticket' + (S.royal ? ' is-royal' : ''));
      if (!veil || !paper) { submit(); return; }
      const h = el('div', 'g-sort-ticket-h');
      if (h) { h.textContent = t('sort_ticket_title', 'The sort'); paper.appendChild(h); }
      const rows = el('div', 'g-sort-rows');
      const line = (labelKey, fallback, value) => {
        const r = el('div', 'g-sort-row');
        if (!r) return;
        const l = el('span', ''); if (l) { l.textContent = t(labelKey, fallback); r.appendChild(l); }
        const b = el('b', ''); if (b) { b.textContent = String(value); r.appendChild(b); }
        if (rows) rows.appendChild(r);
      };
      line('sort_ticket_sorted', 'Sorted', g.ticket.sorted);
      line('sort_ticket_perfect', 'Perfect', g.ticket.perfect);
      line('sort_ticket_chain', 'Longest chain', g.ticket.longestChain);
      line('sort_ticket_rung', 'Top rung', g.ticket.topRung);
      line('sort_ticket_passed', 'Passed', g.ticket.passed);
      line('sort_ticket_wrong', 'Wrong', g.ticket.wrong);
      if (rows) paper.appendChild(rows);
      const stampBed = el('div', 'g-sort-ticket-stamp');
      if (stampBed) paper.appendChild(stampBed);
      if (!g.gates.sGate) {
        const hint = el('div', 'g-sort-hint');
        if (hint) { hint.textContent = t('sort_gate_hint', SORT_LEX.sort_gate_hint); paper.appendChild(hint); }
      }
      const go = el('button', 'btn primary');
      const actions = el('div', 'g-sort-ticket-actions');
      if (go) {
        go.textContent = t('sort_submit', 'Submit report');
        try { if (ctx.exits && typeof ctx.exits.sign === 'function') ctx.exits.sign(go, { dir: 'go' }); }
        catch (e) { /* decoration */ }
        if (typeof go.addEventListener === 'function') go.addEventListener('click', submit);
        if (actions) { actions.appendChild(go); paper.appendChild(actions); }
      }
      veil.appendChild(paper);
      try { S.nodes.stage.appendChild(veil); } catch (e) { submit(); return; }
      S.endEl = veil;

      /* THE GRADE THUDS ONTO THE WALL, as an object and not a letter. It has to
       * outlive the stamp's own hold, because it is furniture on the ticket. */
      try {
        const cer = ctx.ceremonies;
        if (cer && typeof cer.stamp === 'function' && stampBed) {
          cer.stamp({
            target: stampBed,
            text: S.royal ? t('sort_royal', 'ROYAL')
              : g.gates.sGate ? t('sort_perfect_class', 'Clean sort')
                : t('sort_ticket_title', 'The sort'),
            tone: S.royal ? 'gild' : 'good',
            hold: 600000,
          });
        }
      } catch (e) { /* a ceremony must never be the thing that fails */ }
      bus.emit('end', { ticket: g.ticket, composite: g.composite, royal: S.royal });

      say('ticket: composite ' + g.composite.toFixed(3)
        + ' (acc ' + g.terms.accuracy.toFixed(2)
        + ' tempo ' + g.terms.tempo.toFixed(2)
        + ' perfect ' + g.terms.perfect.toFixed(2) + ')'
        + ', sGate ' + g.gates.sGate + ', flavorXp ' + g.flavorXp);

      S.autoTimer = timers.after(AUTO_SUBMIT_MS, submit);
    }

    function writeMeta() {
      if (!S || !S.result) return;
      const g = S.result;
      mergeMeta({
        bestChain: Math.max(Number(metaOf().bestChain) || 0, S.longestChain),
        bestRung: Math.max(Number(metaOf().bestRung) || 0, S.bestRung),
        lastPlayedAt: Date.now(),
      });
      return g;
    }

    function submit() {
      if (!S || ended) return;
      ended = true;
      S.submitted = true;
      timers.cancel(S.autoTimer);
      const g = S.result || gradeClass({
        correct: S.correct, wrong: S.wrong, perfect: S.perfect,
        passed: S.passed, bestRung: S.bestRung, rungCap: S.rungCap,
        longestChain: S.longestChain,
      });
      writeMeta();
      try {
        ctx.endClass({
          metrics: { composite: g.composite },
          hardGates: { sGate: !!g.gates.sGate },
          flavorXp: g.flavorXp,
        });
      } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
      say('submitted: composite=' + g.composite.toFixed(3)
        + ' sGate=' + g.gates.sGate + ' flavorXp=' + g.flavorXp);
    }

    /* ==================================================================== *
     * FREEZE / THAW. The decks stop FIRST - a deck must never paint one more
     * frame over a class the player just froze - then the ring, then the
     * hand. Coming back is the same order reversed, and the card in flight
     * keeps its ring: the clock is re-based, never re-rolled, so a freeze can
     * neither steal a PERFECT nor hand one out.
     * ==================================================================== */
    function freeze() {
      if (!S) return;
      decksCall('pause');
      if (S.armed) {
        S.frozenElapsed = now() - S.ringStart;
        timers.cancel(S.ringTimer);
        S.ringTimer = 0;
        if (S.swipe) S.swipe.enabled(false);
      }
      S.frozenAt = now();
    }
    function thaw() {
      if (!S || S.over) return;
      if (S.frozenAt) {
        const gone = now() - S.frozenAt;
        S.startedAt += gone;                    // the bell never rings on a pause
        if (S.armed) S.ringStart = now() - (S.frozenElapsed || 0);
        S.frozenAt = 0;
      }
      if (S.armed) {
        if (S.swipe) S.swipe.enabled(true);
        timers.cancel(S.ringTimer);
        S.ringTimer = timers.every(reduced ? RING_TICK_MS_REDUCED : RING_TICK_MS, ringTick);
      }
      decksCall('resume');
    }

    /* =========================================================== LIFECYCLE */
    return {
      setup,

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
      },

      resume() {
        if (!S || !S.paused) return;
        S.paused = false;
        if (!S.suspended) thaw();
      },

      suspend(on) {
        if (!S) return;
        const want = !!on;
        if (want === S.suspended) return;
        S.suspended = want;
        if (want) freeze();
        /* a class the player ALSO paused stays frozen: the pause outranks the
           host's thaw, and resume() is what wakes the room then. */
        else if (!S.paused) thaw();
      },

      destroy() {
        destroyed = true;
        decksCall('destroy');
        decks.casino = null; decks.pressure = null; decks.trickster = null;
        listeners.clear();
        timers.killAll();
        try { unbindKeys(); } catch (e) { /* noop */ }
        if (S) {
          try { if (S.swipe) S.swipe.destroy(); } catch (e) { /* noop */ }
          for (const live of S.live.splice(0, S.live.length)) dropCard(live);
          try { if (S.wall) S.wall.destroy(); } catch (e) { /* noop */ }
          try { if (S.pool && typeof S.pool.dispose === 'function') S.pool.dispose(); } catch (e) { /* noop */ }
          try { if (S.nodes && S.nodes.stage && S.nodes.stage.remove) S.nodes.stage.remove(); } catch (e) { /* noop */ }
        }
        try { if (pending.pool && pending.pool !== (S && S.pool) && typeof pending.pool.dispose === 'function') pending.pool.dispose(); }
        catch (e) { /* noop */ }
        pending = { pool: null, quick: false, hot: false, thin: false, sources: null };
        videoCount = 0;
        S = null;
      },

      /** For the scratch harness and a future debug overlay. Never the shell's. */
      diagnostics() {
        const dg = (d) => {
          if (!d || typeof d.diagnostics !== 'function') return null;
          try { return d.diagnostics(); } catch (e) { return null; }
        };
        if (!S) {
          return {
            live: false, timers: timers.size, videoCount,
            decks: { casino: dg(decks.casino), pressure: dg(decks.pressure), trickster: dg(decks.trickster) },
          };
        }
        return {
          live: true,
          gradeTier: S.gradeTier, quick: S.quick, retake: S.retake,
          chain: S.chain, rung: S.rung, rungCap: S.rungCap,
          bestRung: S.bestRung, longestChain: S.longestChain,
          correct: S.correct, wrong: S.wrong, passed: S.passed,
          perfect: S.perfect, just: S.just,
          jackpots: S.jackpots, royal: S.royal, majorsPaid: S.majorsPaid.slice(),
          deck: { size: S.cards.length, cursor: S.cursor, dealt: S.dealt, recycles: S.recycles, queued: S.passQueue.length },
          /* A FROZEN RING REPORTS THE TIME IT STOPPED AT, not the wall clock.
           * `ringStart` is only re-based on the thaw, so reading it live during
           * a pause would say the card had been sitting there for the whole
           * freeze - which is the opposite of what the freeze did. */
          ring: {
            armed: S.armed,
            ms: S.ringMs,
            frozen: !!S.frozenAt,
            elapsed: !S.armed ? 0 : (S.frozenAt ? (S.frozenElapsed || 0) : now() - S.ringStart),
          },
          heat: S.heat, over: S.over, submitted: S.submitted,
          videoCount, timers: timers.size,
          wall: S.wall ? S.wall.diagnostics() : null,
          swipe: S.swipe ? S.swipe.diagnostics() : null,
          decks: { casino: dg(decks.casino), pressure: dg(decks.pressure), trickster: dg(decks.trickster) },
        };
      },

      /* ---- harness seams. Never called by the shell. ------------------- */
      __state() { return S; },
      __bus() { return bus; },
      __commit(dir) { return onCommit(dir); },
      __pass() { onPass(); },
      __bell() { bell(); },
      __submit() { submit(); },
      __tick() { ringTick(); paintClock(); },
    };
  },
};
