/* ============================================================================
 * games/the-deep-end/trickster.js - DECK III of the House Rules: the cards the
 * floor map deals the pool. A sinking game is where your own eyes stop being
 * reliable witnesses, so every card here attacks the READING, never the board.
 *
 *   STAT FLICKER    the score or the depth chip briefly reads slightly off,
 *                   then corrects itself with a static pop. The ledger never
 *                   moves; confidence does. W2: the pop is AUDIBLE now - a
 *                   `decoy` on the correction, through opts.cue (the game's
 *                   clamped helper). It is this deck's only cue: every other
 *                   card here is a lie you READ, and a lie that announces
 *                   itself is not a lie.
 *   CROOKED CLOCK   the clock FACE bends: it races through the boring middle
 *                   (shows less time than you really have) and crawls as the
 *                   bell nears, meeting the truth exactly at the last 20s and
 *                   staying honest from there. The real budget - the bell, the
 *                   grade - is index.js's and exact. SURVIVING A's REPAINT:
 *                   index.js repaints the true mm:ss every tick; a
 *                   MutationObserver on the chip re-bends the face the moment
 *                   truth lands (a microtask - before the frame paints), so the
 *                   honest digits never reach the screen. The observer ignores
 *                   its own write by comparing against the last lie it wrote.
 *                   No observer (DOM double) -> a 250ms poll does the same.
 *   UNRELIABLE LABEL a tile's .g-de-name wears a neighbouring tier's name for
 *                   ~600ms. The glyph (rings that COUNT, drawn by style.js
 *                   from data-tier) is the truth - the card trains Law IV.
 *   THE MELT        stall >= 3s and one shallow tile visibly drips (CSS class
 *                   on the tile; the body and the ink deform, never the
 *                   transform). Snaps back on the next move. Nothing is lost.
 *   GHOST SWIPE     stall >= 4s and a faint cursor trail crosses the board the
 *                   house's way - the WORST direction: the one that drags the
 *                   deepest tile out of its corner. Pure decoration; it cannot
 *                   input, and it dies on the next move.
 *   DEJA RE-DEAL    (tier 3+, rare) right after a move the tiles re-show the
 *                   PREVIOUS move's positions for ~400ms, then slide to truth.
 *                   Transform offsets only (--de-lie-dx/dy, composed by
 *                   style.js; the .g-de-lie class kills the transition so the
 *                   lie snaps on, and the slide carries it home). --r/--c are
 *                   read, never written.
 *
 * DEALING RULE: Stat Flicker, Unreliable Label, Crooked Clock and the cameo
 * are dealt by the seeded plan (budget 2/4/6/8 per tier, never more than 4 a
 * minute, never in the first 14s or the last 25s); a deal whose moment is
 * wrong (halted) re-queues politely, then folds. The Melt and the Ghost are
 * stall-reactive - the stall is the player's own - but WHICH tile melts and
 * WHICH worst direction the ghost takes are the seed's choice.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - flicker/clock write chip TEXT and restore via
 *       chipText (the truth source); the label writes .g-de-name only; the
 *       cameo writes decoration vars; nothing reads or writes score, depth,
 *       chain, the clock budget or the board.
 *   II  input honest  - no card is clickable; the ghost is pointer-events:none
 *       inside the board; hitboxes do not exist here (swipes are the board's).
 *   V   seeded        - per-tag mulberry32 streams off seed+'|de-trickster'
 *       (the DV/L&F discipline; core makeTaggedRoll clusters). A retake
 *       replays the identical deal.
 *   VI  exits sacred  - bgIntensity 0 disarms the deck; reduced motion drops
 *       the flicker pop, the label shiver, the melt's drip and the ghost (the
 *       clock still bends: a number is not motion); every timer lives in the
 *       game's registry AND a local set, so destroy() cannot leak one.
 *   VII strings      - de_trick_melt / de_trick_seen through the t() this
 *       deck is handed (ctx.lexicon or opts.t), neutral fallbacks here; tier
 *       names come from tierName() (A's lexicon). This file invents no text.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const DE_TRICKSTER = Object.freeze({
  /** Dealt card events per class, by tier (the House budget: 2 -> 8). */
  DEALS: Object.freeze({ 1: 2, 2: 4, 3: 6, 4: 8 }),
  /** Never more than 4 a minute; never before the dive has breathed; never
   *  inside the bell's honest zone. */
  MIN_GAP_MS: 15000,
  FIRST_DEAL_MS: 14000,
  TAIL_MS: 25000,
  /** A deal that lands while halted waits politely, then folds. */
  RETRY_MS: 2500,
  RETRY_MAX: 10,

  /** Card weights by tier (flicker / label / cameo; the clock is one slot). */
  WEIGHTS: Object.freeze({
    1: Object.freeze({ flicker: 0.55, label: 0.45, cameo: 0 }),
    2: Object.freeze({ flicker: 0.5, label: 0.5, cameo: 0 }),
    3: Object.freeze({ flicker: 0.42, label: 0.4, cameo: 0.18 }),
    4: Object.freeze({ flicker: 0.38, label: 0.36, cameo: 0.26 }),
  }),
  CLOCK_FROM_TIER: 2,
  CAMEO_FROM_TIER: 3,
  MELT_FROM_TIER: 1,
  GHOST_FROM_TIER: 2,

  /** Stat flicker: how long the wrong number stands. */
  FLICKER_MS: 450,
  /** Crooked clock: how far the face runs ahead at mid-budget (fraction of
   *  the budget), and the honest zone (the bell: the last 20s are true). */
  CLOCK_BEND: 0.14,
  CLOCK_HONEST_SEC: 20,
  CLOCK_RAMP: 0.35,
  CLOCK_POLL_MS: 250,
  /** Unreliable label: how long the lie is worn. */
  LABEL_MS: 600,
  /** The melt / the ghost: stall thresholds. */
  MELT_STALL_MS: 3000,
  GHOST_STALL_MS: 4000,
  /** The cameo: how long the old positions hold; how long an armed cameo
   *  waits for a move before it folds. */
  CAMEO_MS: 400,
  CAMEO_FOLD_MS: 20000,
  SEEN_TAUNT_CHANCE: 0.3,
});

function clampTier(t) { return Math.max(1, Math.min(4, Math.round(Number(t) || 1))); }
function tierOf(tile) {
  try { const n = Number(tile.getAttribute('data-tier')); return Number.isFinite(n) ? n : 0; } catch (e) { return 0; }
}
function hasClass(node, cls) {
  try { return !!(node && node.classList && node.classList.contains(cls)); } catch (e) { return false; }
}
/** A tile's honest row/col, read (never written) off A's inline vars. */
function gridOf(tile) {
  if (!tile || !tile.style) return null;
  try {
    const r = parseFloat(tile.style.getPropertyValue('--r'));
    const c = parseFloat(tile.style.getPropertyValue('--c'));
    if (Number.isFinite(r) && Number.isFinite(c)) return { r, c };
  } catch (e) { /* fall through */ }
  return null;
}

/**
 * The crooked face, pure: displayed seconds-left for a real seconds-left.
 * f(B)=B, f(honest)=honest, monotonic (bend*pi < 1), honest at/after the
 * bell. Ahead through the middle (less time shown than real), crawling as
 * the bell nears, so the face meets the truth instead of snapping to it.
 */
export function bendClock(secLeft, budgetSec, bend, honestSec) {
  const x = Math.max(0, Number(secLeft) || 0);
  const B = Math.max(30, Number(budgetSec) || 300);
  const H = Number.isFinite(honestSec) ? honestSec : DE_TRICKSTER.CLOCK_HONEST_SEC;
  if (x <= H || x >= B) return Math.round(x);
  const A0 = Number.isFinite(bend) ? bend : DE_TRICKSTER.CLOCK_BEND;
  const u = x / B;
  const uh = H / B;
  // the bend fades in above the honest zone, so there is no seam to notice
  const ramp = Math.max(0, Math.min(1, (u - uh) / DE_TRICKSTER.CLOCK_RAMP));
  const A = A0 * ramp * ramp * (3 - 2 * ramp);
  return Math.max(H, Math.round(B * (u - A * Math.sin(Math.PI * u))));
}

/**
 * @param {Object} o
 * @param {string}   o.seed        the class seed (retakes replay)
 * @param {number}   o.tier        1..4
 * @param {Object}   o.timers      {after(ms,fn)->id, every?(ms,fn)->id, clear|cancel(id)}
 * @param {boolean}  o.reduced     reduced motion
 * @param {boolean}  o.capsOk      false when bgIntensity is capped to 0
 * @param {Function} o.isHalted    () => bool (dead/paused/ended)
 * @param {Function} o.stats       () => {moves, depth, secLeft, score, chain, resurfaces} - the TRUTH
 * @param {Function} o.chipEl      (which: 'depth'|'clock'|'score') => element|null
 * @param {Function} o.chipText    (which) => the honest text (repaint source)
 * @param {Function} o.tiles       () => HTMLElement[] (live tiles)
 * @param {Function} o.tierName    (n) => string (A's lexicon)
 * @param {Function=} o.t          ctx.lexicon (optional; English fallbacks here)
 * @param {Function=} o.announce   (text, ms) => void (optional proctor line)
 * @param {number=}  o.budgetSec   class budget (optional; else latched from secLeft)
 * @param {Function=} o.log
 */
export function createDeTrickster(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => (f == null ? k : f);
  const tier = clampTier(opts.tier);
  const reduced = !!opts.reduced;
  const armed = !!opts.capsOk && !!opts.timers && typeof opts.timers.after === 'function'
    && typeof document !== 'undefined';
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;
  const stats = typeof opts.stats === 'function' ? opts.stats : () => null;
  const chipEl = typeof opts.chipEl === 'function' ? opts.chipEl : () => null;
  const chipText = typeof opts.chipText === 'function' ? opts.chipText : () => null;
  const tilesOf = typeof opts.tiles === 'function' ? opts.tiles : () => [];
  const tierName = typeof opts.tierName === 'function' ? opts.tierName : (n) => String(n);
  const announce = typeof opts.announce === 'function' ? opts.announce : null;
  /* W2 - THE CUE ROAD. index.js hands down its own clamped `tick`, so this
     deck asks for sound and never holds a node or raises the tier ceiling.
     THE DECOUPLE (spec 3): sound rides `destroyed`/`stopped` and NOT capsOk -
     but note the honest limit, which is written down rather than faked: this
     deck's cards are dealt by its own timers, and `armed` (capsOk included)
     kills those at CONSTRUCTION. With bgIntensity 0 no card is ever dealt, so
     there is no correction to hear. Nothing is muted by the dial; there is
     simply nothing happening. The road is here for the day a card becomes a
     beat the GAME calls in for. */
  const cue = typeof opts.cue === 'function' ? opts.cue : () => {};
  const sounds = () => !destroyed && !stopped;

  /* timers: the game's registry + a local set (see casino.js). */
  const live = new Set();
  const cancelFn = opts.timers && (opts.timers.clear || opts.timers.cancel);
  function after(ms, fn) {
    if (!armed) return 0;
    let id = 0;
    id = opts.timers.after(ms, () => { live.delete(id); if (!destroyed) { try { fn(); } catch (e) { /* ignore */ } } });
    if (id) live.add(id);
    return id;
  }
  function cancel(id) {
    if (!id) return;
    live.delete(id);
    try { if (typeof cancelFn === 'function') cancelFn.call(opts.timers, id); } catch (e) { /* ignore */ }
  }
  /** every(): the registry's if it has one, else a chain of after()s. */
  function every(ms, fn) {
    if (!armed) return 0;
    if (typeof opts.timers.every === 'function') {
      const id = opts.timers.every(ms, () => { if (!destroyed) { try { fn(); } catch (e) { /* ignore */ } } });
      if (id) live.add(id);
      return id;
    }
    const handle = { id: 0, on: true };
    const tick = () => { if (!handle.on || destroyed) return; try { fn(); } catch (e) { /* ignore */ } handle.id = after(ms, tick); };
    handle.id = after(ms, tick);
    chains.add(handle);
    return handle;
  }
  const chains = new Set();
  function stopEvery(h) {
    if (!h) return;
    if (typeof h === 'object') { h.on = false; cancel(h.id); chains.delete(h); } else cancel(h);
  }

  const seedBase = String(opts.seed || 'de') + '|de-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  let destroyed = false;
  let stopped = false;
  const fired = { flicker: 0, label: 0, cameo: 0, clock: 0 };
  let deals = [];
  const lexiconUsed = new Set();
  function say_t(key, fallback) { lexiconUsed.add(key); return t(key, fallback); }

  /* ------------------------------------------------------------- the deal */
  function buildDeals() {
    const n = DE_TRICKSTER.DEALS[tier] || 2;
    const spanMs = Math.max(60, Number(opts.budgetSec) || 300) * 1000;
    const usable = Math.max(20000, spanMs - DE_TRICKSTER.FIRST_DEAL_MS - DE_TRICKSTER.TAIL_MS);
    const times = [];
    for (let i = 0; i < n; i++) times.push(DE_TRICKSTER.FIRST_DEAL_MS + roll('when') * usable);
    times.sort((a, b) => a - b);
    for (let i = 1; i < times.length; i++) {
      if (times[i] - times[i - 1] < DE_TRICKSTER.MIN_GAP_MS) times[i] = times[i - 1] + DE_TRICKSTER.MIN_GAP_MS;
    }
    // the clock is ONE slot in the first half of the plan (it latches)
    const clockSlot = tier >= DE_TRICKSTER.CLOCK_FROM_TIER && n > 1
      ? Math.floor(roll('clock-slot') * Math.ceil(n / 2)) : -1;
    const w = DE_TRICKSTER.WEIGHTS[tier] || DE_TRICKSTER.WEIGHTS[1];
    return times.map((at, i) => {
      let card;
      if (i === clockSlot) card = 'clock';
      else {
        const r = roll('card');
        card = r < w.flicker ? 'flicker' : r < w.flicker + w.label ? 'label' : 'cameo';
        if (card === 'cameo' && tier < DE_TRICKSTER.CAMEO_FROM_TIER) card = 'label';
      }
      return { at: Math.round(at), card };
    });
  }

  function attempt(deal, tries) {
    if (destroyed || stopped) return;
    // arming the cameo is passive (a flag + a pause-aware fold timer) and it
    // plays on the NEXT move, so it never needs a quiet beat of its own
    if (deal.card === 'cameo') { armCameo(); return; }
    if (isHalted() || !stats()) {
      if (tries < DE_TRICKSTER.RETRY_MAX) after(DE_TRICKSTER.RETRY_MS, () => attempt(deal, tries + 1));
      return;
    }
    if (deal.card === 'flicker') dealFlicker();
    else if (deal.card === 'label') dealLabel();
    else if (deal.card === 'clock') armClock();
    else if (deal.card === 'cameo') armCameo();
  }

  /* -------------------------------------------------------- stat flicker */
  function dealFlicker() {
    const s = stats();
    if (!s) return;
    const which = roll('flick-which') < 0.5 ? 'score' : 'depth';
    const chip = chipEl(which);
    if (!chip || chip.textContent == null) return;
    const truth = chipText(which);
    let lie = null;
    if (which === 'score') {
      // a plausible few points off - the digits in the truth string move
      const n = Number(s.score) || 0;
      const delta = (roll('flick-sign') < 0.5 ? -1 : 1) * (2 + Math.floor(roll('flick-drop') * 14)) * 2;
      const fake = Math.max(0, n + delta);
      lie = (typeof truth === 'string' && /\d/.test(truth)) ? truth.replace(/\d[\d,\s]*/, String(fake)) : String(fake);
    } else {
      // one tier deeper than the truth, for a blink - wishful reading
      const d = Math.max(1, Math.min(11, Number(s.depth) || 1));
      const fake = d >= 11 ? 10 : d + 1;
      lie = tierName(fake);
    }
    if (!lie || lie === chip.textContent) return;
    try { chip.textContent = lie; } catch (e) { return; }
    if (!reduced && chip.classList) chip.classList.add('g-de-statlie');
    fired.flicker += 1;
    after(DE_TRICKSTER.FLICKER_MS, () => {
      try { if (chip.classList) chip.classList.remove('g-de-statlie'); } catch (e) { /* ignore */ }
      const now = chipText(which);
      if (now == null) return;
      const wasLying = chip.textContent === lie;
      try { chip.textContent = now; } catch (e) { return; }
      /* THE STATIC POP, made true: the number snaps back to the ledger and the
         room clicks. Only when the LIE was still standing - a real repaint
         that beat us here is the truth arriving on its own, not a correction. */
      if (wasLying && sounds()) cue('decoy', 0.35);
    });
    say('trickster: stat flicker (' + which + ')');
  }

  /* ------------------------------------------------------- crooked clock */
  let clockArmed = false;
  let clockObs = null;
  let clockPoll = 0;
  let lastLie = null;
  let budgetSeen = Number(opts.budgetSec) || 0;
  let clockLies = 0;

  function formatLike(sec, truth) {
    const s = Math.max(0, Math.round(sec));
    if (typeof truth === 'string' && /^\d+s$/.test(truth.trim())) return s + 's';
    const m = Math.floor(s / 60);
    const r = s % 60;
    const padM = typeof truth === 'string' && /^\d{2}:\d{2}$/.test(truth.trim());
    return (padM ? String(m).padStart(2, '0') : String(m)) + ':' + String(r).padStart(2, '0');
  }

  /** Re-bend the face from the TRUE seconds. Called after every truth repaint. */
  function bendFace() {
    if (!clockArmed || destroyed || stopped) return;
    const chip = chipEl('clock');
    const s = stats();
    if (!chip || !s || !Number.isFinite(Number(s.secLeft))) return;
    const secLeft = Number(s.secLeft);
    if (secLeft > budgetSeen) budgetSeen = secLeft;
    const shown = bendClock(secLeft, budgetSeen, DE_TRICKSTER.CLOCK_BEND, DE_TRICKSTER.CLOCK_HONEST_SEC);
    if (shown === Math.round(secLeft)) { lastLie = null; return; }       // the honest zone
    const truth = chipText('clock');
    const text = formatLike(shown, truth);
    if (chip.textContent === text) return;
    lastLie = text;
    try { chip.textContent = text; clockLies += 1; } catch (e) { /* ignore */ }
  }

  function armClock() {
    if (clockArmed || tier < DE_TRICKSTER.CLOCK_FROM_TIER) return;
    const chip = chipEl('clock');
    if (!chip) return;
    clockArmed = true;
    fired.clock += 1;
    let hooked = false;
    try {
      if (typeof MutationObserver === 'function' && typeof chip.nodeType === 'number') {
        clockObs = new MutationObserver(() => {
          // our own write arrives here too: it reads back as the last lie
          if (chip.textContent === lastLie) return;
          bendFace();
        });
        clockObs.observe(chip, { childList: true, characterData: true, subtree: true });
        hooked = true;
      }
    } catch (e) { clockObs = null; }
    if (!hooked) clockPoll = every(DE_TRICKSTER.CLOCK_POLL_MS, bendFace);
    bendFace();
    say('trickster: the clock goes crooked (' + (hooked ? 'observer' : 'poll') + ')');
  }

  function disarmClock(restore) {
    if (clockObs) { try { clockObs.disconnect(); } catch (e) { /* ignore */ } clockObs = null; }
    if (clockPoll) { stopEvery(clockPoll); clockPoll = 0; }
    if (clockArmed && restore) {
      const chip = chipEl('clock');
      const truth = chipText('clock');
      if (chip && truth != null) { try { chip.textContent = truth; } catch (e) { /* ignore */ } }
    }
    clockArmed = false;
    lastLie = null;
  }

  /* ---------------------------------------------------- unreliable label */
  function liveTiles() {
    let list = [];
    try { list = Array.from(tilesOf() || []); } catch (e) { list = []; }
    return list.filter((n) => n && n.style && !hasClass(n, 'is-gone') && !hasClass(n, 'is-silt') && tierOf(n) >= 1);
  }

  function dealLabel() {
    const pool = liveTiles().filter((n) => n.querySelector && n.querySelector('.g-de-name'));
    if (!pool.length) return;
    const tile = pool[Math.floor(roll('label-pick') * pool.length)];
    const name = tile.querySelector('.g-de-name');
    if (!name) return;
    const real = tierOf(tile);
    let fake = real + (roll('label-dir') < 0.5 ? -1 : 1);
    if (fake < 1) fake = 2;
    if (fake > 11) fake = 10;
    const lie = tierName(fake);
    const was = name.textContent;
    if (!lie || lie === was) return;
    try { name.textContent = lie; } catch (e) { return; }
    if (!reduced && name.classList) name.classList.add('g-de-lielabel');
    fired.label += 1;
    after(DE_TRICKSTER.LABEL_MS, () => {
      try { if (name.classList) name.classList.remove('g-de-lielabel'); } catch (e) { /* ignore */ }
      // truth at restore time: the tile may have merged meanwhile
      const now = tierName(tierOf(tile)) || was;
      try { name.textContent = now; } catch (e) { /* ignore */ }
    });
    say('trickster: unreliable label (tier ' + real + ' wears ' + fake + ')');
  }

  /* ------------------------------------------------------------- the melt */
  let melted = null;
  let meltAnnounced = false;
  let melts = 0;

  function dealMelt() {
    if (melted || tier < DE_TRICKSTER.MELT_FROM_TIER) return;
    const pool = liveTiles();
    if (pool.length < 2) return;
    let low = Infinity;
    for (const n of pool) low = Math.min(low, tierOf(n));
    const shallow = pool.filter((n) => tierOf(n) === low);
    const tile = shallow[Math.floor(roll('melt-pick') * shallow.length)];
    if (!tile || !tile.classList) return;
    try { tile.classList.add('g-de-melt'); } catch (e) { return; }
    melted = tile;
    melts += 1;
    if (announce && !meltAnnounced && !reduced) {
      meltAnnounced = true;
      try { announce(say_t('de_trick_melt', 'The shallows run like wax'), 2000); } catch (e) { /* ignore */ }
    }
    say('trickster: melt (tier ' + low + ')');
  }
  function unmelt() {
    if (!melted) return;
    try { melted.classList.remove('g-de-melt'); } catch (e) { /* ignore */ }
    melted = null;
  }

  /* ------------------------------------------------------------ the ghost */
  let ghost = null;
  let ghosts = 0;

  /** The house-preferred WORST move: the one that pulls the deepest tile
   *  away from the corner it is keeping. Seeded between the two candidates. */
  function worstDirection(pool, board) {
    let deepest = null;
    let best = -1;
    for (const n of pool) { const tv = tierOf(n); if (tv > best) { best = tv; deepest = n; } }
    const g = deepest ? gridOf(deepest) : null;
    if (!g) return null;
    let n = 4;
    try { n = Math.max(2, parseInt(board.style.getPropertyValue('--de-n'), 10) || (typeof getComputedStyle === 'function'
      ? parseInt(getComputedStyle(board).getPropertyValue('--de-n'), 10) : 4) || 4); } catch (e) { n = 4; }
    const top = g.r < n / 2;
    const left = g.c < n / 2;
    const vertical = top ? { gx: 0, gy: 1, ga: 90 } : { gx: 0, gy: -1, ga: 270 };
    const horizontal = left ? { gx: 1, gy: 0, ga: 0 } : { gx: -1, gy: 0, ga: 180 };
    return roll('ghost-axis') < 0.5 ? vertical : horizontal;
  }

  function dealGhost() {
    if (ghost || reduced || tier < DE_TRICKSTER.GHOST_FROM_TIER) return;
    const pool = liveTiles();
    if (!pool.length) return;
    const board = pool[0].parentNode;
    if (!board || !board.appendChild) return;
    const dir = worstDirection(pool, board);
    if (!dir) return;
    let node = null;
    try { node = document.createElement('i'); node.className = 'g-de-ghost'; } catch (e) { return; }
    if (!node.style) return;
    node.style.setProperty('--gx', String(dir.gx));
    node.style.setProperty('--gy', String(dir.gy));
    node.style.setProperty('--ga', String(dir.ga));
    board.appendChild(node);
    ghost = node;
    ghosts += 1;
    say('trickster: ghost swipe (' + (dir.gx ? (dir.gx > 0 ? 'right' : 'left') : (dir.gy > 0 ? 'down' : 'up')) + ')');
  }
  function unghost() {
    if (!ghost) return;
    try { ghost.remove(); } catch (e) { /* ignore */ }
    ghost = null;
  }

  /* ------------------------------------------------------------ the cameo */
  let cameoPending = false;
  let cameoFold = 0;
  let lying = [];
  let prev = new Map();          // data-id -> {r, c} after the last move

  function armCameo() {
    if (tier < DE_TRICKSTER.CAMEO_FROM_TIER || cameoPending) return;
    cameoPending = true;
    if (cameoFold) cancel(cameoFold);
    cameoFold = after(DE_TRICKSTER.CAMEO_FOLD_MS, () => { cameoFold = 0; cameoPending = false; });
    say('trickster: cameo armed');
  }

  function snapToTruth() {
    for (const tile of lying) {
      try {
        tile.style.removeProperty('--de-lie-dx');
        tile.style.removeProperty('--de-lie-dy');
        tile.classList.remove('g-de-lie');
      } catch (e) { /* ignore */ }
    }
    lying = [];
  }

  function playCameo() {
    const pool = liveTiles().filter((n) => !hasClass(n, 'is-new'));
    let staged = 0;
    for (const tile of pool) {
      let id = null;
      try { id = tile.getAttribute('data-id'); } catch (e) { id = null; }
      const was = id != null ? prev.get(String(id)) : null;
      const now = gridOf(tile);
      if (!was || !now || (was.r === now.r && was.c === now.c)) continue;
      try {
        tile.classList.add('g-de-lie');                       // no transition: the lie snaps ON
        tile.style.setProperty('--de-lie-dx', String(was.c - now.c));
        tile.style.setProperty('--de-lie-dy', String(was.r - now.r));
        lying.push(tile);
        staged += 1;
      } catch (e) { /* ignore */ }
    }
    if (!staged) return false;
    fired.cameo += 1;
    // class and vars drop together: the after-change style has the transition,
    // so the 110ms slide carries every tile home
    after(DE_TRICKSTER.CAMEO_MS, snapToTruth);
    if (announce && !reduced && roll('seen') < DE_TRICKSTER.SEEN_TAUNT_CHANCE) {
      try { announce(say_t('de_trick_seen', 'Did you see that?'), 1600); } catch (e) { /* ignore */ }
    }
    say('trickster: deja re-deal cameo (' + staged + ' tiles)');
    return true;
  }

  function snapshot() {
    const next = new Map();
    for (const tile of liveTiles()) {
      let id = null;
      try { id = tile.getAttribute('data-id'); } catch (e) { id = null; }
      const g = gridOf(tile);
      if (id != null && g) next.set(String(id), g);
    }
    prev = next;
  }

  /* ------------------------------------------------------------------ api */
  return {
    /** Deal the class. Call once, when the dive begins. */
    start() {
      if (!armed || destroyed) { say('trickster: disarmed'); return; }
      deals = buildDeals();
      for (const deal of deals) after(deal.at, () => attempt(deal, 0));
      // the clock's budget: the full secLeft at the dive's start (A passes no
      // budgetSec); later reads can only raise it, never shrink it
      try { const s = stats(); if (s && Number(s.secLeft) > budgetSeen) budgetSeen = Number(s.secLeft); } catch (e) { /* ignore */ }
      snapshot();
      say('trickster: dealt ' + deals.length + ' cards ('
        + deals.map((d) => d.card + '@' + Math.round(d.at / 1000) + 's').join(', ') + ')');
    },

    /**
     * index.js calls after every move, after it has written --r/--c and the
     * state classes. Any stall theatre ends; an armed cameo plays; then the
     * positions are remembered for the next one.
     */
    afterMove(result) {
      if (!armed || destroyed) return;
      snapToTruth();
      unmelt();
      unghost();
      const r = result || {};
      // no isHalted() here: A calls in from the middle of its own move, where
      // its busy flag is up by design - a halted class never calls afterMove
      if (!stopped && cameoPending && r.moved !== false) {
        cameoPending = false;
        if (cameoFold) { cancel(cameoFold); cameoFold = 0; }
        playCameo();
      }
      snapshot();
    },

    /** index.js calls every 500ms with ms since the last move; 0 resets. */
    stalled(ms) {
      if (!armed || destroyed || stopped) return;
      const n = Number(ms) || 0;
      if (n <= 0) { unmelt(); unghost(); return; }
      if (isHalted()) return;
      if (n >= DE_TRICKSTER.MELT_STALL_MS) dealMelt();
      if (n >= DE_TRICKSTER.GHOST_STALL_MS) dealGhost();
    },

    /** The class is over: no card may fire again, every lie comes off. */
    stop() {
      stopped = true;
      cameoPending = false;
      if (cameoFold) { cancel(cameoFold); cameoFold = 0; }
      snapToTruth();
      unmelt();
      unghost();
      disarmClock(true);
    },

    destroy() {
      destroyed = true;
      stopped = true;
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      for (const h of Array.from(chains)) stopEvery(h);
      snapToTruth();
      unmelt();
      unghost();
      disarmClock(true);
      prev = new Map();
    },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return {
        armed, tier, deals: deals.slice(), fired: Object.assign({}, fired),
        cueRoad: typeof opts.cue === 'function',
        melts, ghosts, melted: !!melted, ghost: !!ghost,
        clock: { armed: clockArmed, lies: clockLies, budget: budgetSeen, observer: !!clockObs },
        cameoPending, lying: lying.length, lexicon: Array.from(lexiconUsed),
      };
    },
  };
}

export default createDeTrickster;
