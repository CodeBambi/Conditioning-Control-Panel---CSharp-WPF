/* ============================================================================
 * games/lost-and-found/trickster.js - DECK III of the House Rules: the three
 * trickster cards the floor map deals this class.
 *
 *   THE MELT        a decoy liquefies and its look runs to another seat - the
 *                   swap primitive wearing wax instead of glitch. Scarcity
 *                   theatre only: board.swapLooks() does the real work, so it
 *                   is byte-for-byte as honest as noise churn.
 *   GHOST CURSOR    a faint cursor echo trails the pointer by a beat; stall
 *                   and it wanders off toward a tile of the house's choosing -
 *                   usually a near-twin, rarely (and honestly) the target, so
 *                   it can never be trusted. Pure suggestion: pointer-events
 *                   none, it cannot click, the real cursor is never hidden.
 *   GLITCH-TO-ASSET a piece of HUD chrome flickers into one of the player's
 *                   own pool stills for ~130ms, then back. Did you see that?
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - nothing here touches finds/misclicks/times/grade.
 *   II  input honest  - every node is pointer-events:none; melts move LOOKS
 *       via swapLooks (hitboxes never move); the target never melts.
 *   V   seeded        - all rolls ride per-tag makeRng streams off
 *       seed+'|lf-trickster', so new draws never shift old streams. Deal SLOTS and
 *       every house choice replay on a retake; only the stall that triggers a
 *       ghost lure is the player's own.
 *   VI  exits sacred  - reduced motion: melts become bare swaps, the ghost and
 *       chrome flickers are off entirely; coarse pointers get no ghost (there
 *       is no cursor to echo); bgIntensity capped to 0 disarms the whole deck;
 *       all timers live in the game's registry so pause/suspend/destroy drop
 *       everything instantly.
 *   VII strings      - lf_melt / lf_trick_seen via ctx.lexicon only.
 *
 * ENGINE PLACEMENT: game-local BY CHOICE, candidate for engine promotion.
 * All three cards are board/hud-aware (swap primitive, live tile rects, chrome
 * anchors + the class's own pool), so an engine kind would need a target
 * contract the engine does not have yet. When a second game wants a melt, lift
 * the choreography into engine/oneshots.js and leave the tile-picking here.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';
import { el, clamp } from './util.js';

export const TRICKSTER = Object.freeze({
  /** Dealt card events per class, by tier (the House budget: 2 -> 8). */
  DEALS: Object.freeze({ 1: 2, 2: 4, 3: 6, 4: 8 }),
  /** Never more than 4 a minute; never before the hunt has breathed. */
  MIN_GAP_MS: 15000,
  FIRST_DEAL_MS: 12000,
  /** A deal that lands mid-ceremony waits politely, then gives up. */
  RETRY_MS: 2500,
  RETRY_MAX: 12,

  /** The melt: sag, drip, and at the midpoint the look runs to its new seat. */
  MELT_MS: 1150,
  MELT_SWAP_AT_MS: 620,
  REFORM_MS: 700,

  /** Chrome flicker: one beat of someone else's memory, then business as usual. */
  CHROME_MS: 130,
  SEEN_TAUNT_CHANCE: 0.3,

  /** The ghost: from tier 2, mouse pointers only. */
  GHOST_FROM_TIER: 2,
  GHOST_TICK_MS: 90,
  GHOST_TRAIL_MS: 430,
  GHOST_STALL_MS: 2600,
  /** How often the lure is honest (points at the real target). Rarely. */
  GHOST_HONEST_LURE: 0.2,
  GHOST_LERP: 0.14,
});

/**
 * @param {Object} o
 * @param {string}   o.seed        the class seed
 * @param {number}   o.tier        1..4
 * @param {number}   o.budgetSec   class budget (zen passes a nominal span)
 * @param {Object}   o.timers      the GAME's timer registry (killAll = we die)
 * @param {Object}   o.board       createBoard() api
 * @param {Object}   o.hud         createHud() api (root + chromeEls)
 * @param {boolean}  o.reduced     reduced motion (melts bare, ghost/chrome off)
 * @param {boolean}  o.coarse      coarse pointer (no ghost - nothing to echo)
 * @param {boolean}  o.capsOk      false when bgIntensity is capped to 0
 * @param {Function=} o.cue        the GAME's clamped cue helper (name, level,
 *        extra). A closure, never the engine.
 * @param {Function} o.getPhase    () => 'idle'|'briefing'|'hunt'|'ceremony'|'done'
 * @param {Function} o.isHalted    () => bool (pause / suspend)
 * @param {Function} o.isClutch    () => bool (the board relents - melts stand down)
 * @param {Function} o.getStill    () => url|null (a pool still for chrome flickers)
 * @param {Function} o.announce    (text, ms) => void (the hud taunt line)
 * @param {Function} o.t           ctx.lexicon
 * @param {Function=} o.log
 */
export function createTrickster(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => f || k;
  const board = opts.board;
  const hud = opts.hud;
  const timers = opts.timers;
  const tier = clamp(opts.tier, 1, 4);
  const reduced = !!opts.reduced;
  const coarse = !!opts.coarse;
  const armed = !!opts.capsOk && !!board && !!hud && !!timers
    && typeof document !== 'undefined';
  const cue = typeof opts.cue === 'function' ? opts.cue : () => {};

  /**
   * Tag-namespaced seeded rolls, one mulberry32 STREAM per tag - the same
   * append-only property as core makeTaggedRoll (a new tag never shifts an old
   * stream), chosen over it deliberately: makeTaggedRoll's trailing-counter
   * hash01 barely avalanches on the final byte, so consecutive rolls of one tag
   * cluster within ~0.4% of each other - every deal of a class would land the
   * same card at minimum spacing. A mulberry32 stream per tag scrambles fully.
   */
  const seedBase = String(opts.seed || 'lf') + '|lf-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };
  const getPhase = typeof opts.getPhase === 'function' ? opts.getPhase : () => 'hunt';
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;
  const isClutch = typeof opts.isClutch === 'function' ? opts.isClutch : () => false;

  let destroyed = false;
  let stopped = false;
  let meltAnnounced = false;

  /* THE bgIntensity DECOUPLE. Same split as casino.js: `armed` still gates the
     visuals (capsOk included), and the cue road gates only on the deck being
     alive. Note what that means HERE, though: every card this deck deals is a
     pure visual it schedules itself, and start() refuses to deal at all when
     !armed - so at bgIntensity 0 there is correctly nothing to hear, because
     there is nothing happening. The split is still written this way so a
     future game-called beat inherits the right gate. */
  const sounds = () => !destroyed && !stopped;

  /* --------------------------------------------------------------- the deal */
  /**
   * Seeded slots, spread over the class, spaced to the per-minute cap. A slot
   * that drifts past the budget simply never fires (the clock kills us first).
   */
  function buildDeals() {
    const n = TRICKSTER.DEALS[tier] || 2;
    const spanMs = clamp(Number(opts.budgetSec) || 120, 60, 300) * 1000;
    const usable = Math.max(20000, spanMs - TRICKSTER.FIRST_DEAL_MS - 8000);
    const times = [];
    for (let i = 0; i < n; i++) times.push(TRICKSTER.FIRST_DEAL_MS + roll('when') * usable);
    times.sort((a, b) => a - b);
    for (let i = 1; i < times.length; i++) {
      if (times[i] - times[i - 1] < TRICKSTER.MIN_GAP_MS) {
        times[i] = times[i - 1] + TRICKSTER.MIN_GAP_MS;
      }
    }
    // The card in each slot is the seed's choice, not the moment's.
    return times.map((at) => ({ at: Math.round(at), card: roll('card') < 0.55 ? 'melt' : 'chrome' }));
  }

  function attempt(deal, tries) {
    if (destroyed || stopped) return;
    if (getPhase() !== 'hunt' || isHalted()) {
      if (tries < TRICKSTER.RETRY_MAX) timers.after(TRICKSTER.RETRY_MS, () => attempt(deal, tries + 1));
      return;
    }
    if (deal.card === 'melt') dealMelt();
    else dealChrome();
  }

  /* --------------------------------------------------------------- the melt */
  /** Seeded pick of a decoy tile. Never the target (Law II). */
  function pickDecoy(not) {
    const decoys = board.tiles.filter((tile) => !tile.target && tile !== not);
    if (!decoys.length) return null;
    return decoys[Math.floor(roll('melt-pick') * decoys.length)] || null;
  }

  function dealMelt() {
    if (isClutch()) return;                     // the board has relented - so do we
    const pairs = tier >= 3 ? 2 : 1;
    let melted = 0;
    for (let i = 0; i < pairs; i++) {
      const a = pickDecoy(null);
      const b = pickDecoy(a);
      if (!a || !b) break;
      melted += 1;
      if (reduced) { board.swapLooks(a, b); continue; }   // bare swap, no theatre
      board.mark(a, 'g-lf-melting', true);
      timers.after(TRICKSTER.MELT_SWAP_AT_MS, () => {
        if (destroyed) return;
        board.swapLooks(a, b);
        board.mark(a, 'g-lf-melting', false);
        board.mark(b, 'g-lf-reform', true);
        timers.after(TRICKSTER.REFORM_MS + 60, () => { if (!destroyed) board.mark(b, 'g-lf-reform', false); });
      });
    }
    if (melted && !meltAnnounced && !reduced) {
      meltAnnounced = true;
      opts.announce(t('lf_melt', 'The wall runs like wax'), 2000);
    }
    if (melted) say('trickster: melt x' + melted);
  }

  /* ------------------------------------------------------ glitch-to-asset */
  function dealChrome() {
    if (reduced) return;                        // a flicker is motion; skip it
    const chrome = (typeof hud.chromeEls === 'function' ? hud.chromeEls() : [])
      .filter((n) => n && typeof n.getBoundingClientRect === 'function');
    const wrap = hud.root;
    if (!chrome.length || !wrap || typeof wrap.getBoundingClientRect !== 'function') return;
    const url = typeof opts.getStill === 'function' ? opts.getStill() : null;
    if (!url) return;                           // no pool yet - the card is a dud
    const host = chrome[Math.floor(roll('chrome-pick') * chrome.length)];
    let rect = null;
    let base = null;
    try { rect = host.getBoundingClientRect(); base = wrap.getBoundingClientRect(); } catch (e) { return; }
    if (!rect || !base || !rect.width || !rect.height) return;

    const node = el('div', 'g-lf-chromeglitch');
    if (!node) return;
    node.style.left = (rect.left - base.left) + 'px';
    node.style.top = (rect.top - base.top) + 'px';
    node.style.width = rect.width + 'px';
    node.style.height = rect.height + 'px';
    const img = el('img');
    if (img) {
      img.alt = '';
      img.setAttribute('draggable', 'false');
      img.src = url;
      node.appendChild(img);
    }
    wrap.appendChild(node);
    // THE CORRECTION: a chip flickers into somebody else's memory and snaps
    // back inside 130ms. A bit-crushed `decoy` on the same frame is what turns
    // "did you see that?" into "did I hear that?" - it is the flicker's own
    // voice, not a second event.
    if (sounds()) cue('decoy', 0.35);
    timers.after(TRICKSTER.CHROME_MS, () => { try { node.remove(); } catch (e) { /* ignore */ } });
    say('trickster: chrome flicker');
    if (roll('seen') < TRICKSTER.SEEN_TAUNT_CHANCE) {
      opts.announce(t('lf_trick_seen', 'Did you see that?'), 1600);
    }
  }

  /* --------------------------------------------------------------- the ghost */
  let ghost = null;
  let ghostTimer = 0;
  let onMove = null;
  const trail = [];             // [{x, y, at}] view-relative pointer history
  let lastMoveAt = 0;
  let lure = null;              // the tile the ghost is currently drifting toward
  let gx = 0;
  let gy = 0;
  let ghostSeen = false;

  function ghostEligible() {
    return armed && !reduced && !coarse && tier >= TRICKSTER.GHOST_FROM_TIER
      && hud.view && typeof hud.view.getBoundingClientRect === 'function'
      && typeof hud.view.addEventListener === 'function';
  }

  /** The visible element copy of a tile (strips wrap; clones drift off-frame). */
  function tileAnchor(tile) {
    if (!tile) return null;
    let view = null;
    try { view = hud.view.getBoundingClientRect(); } catch (e) { return null; }
    let fallback = null;
    for (const node of tile.els) {
      if (!node || typeof node.getBoundingClientRect !== 'function') continue;
      let r = null;
      try { r = node.getBoundingClientRect(); } catch (e) { continue; }
      if (!r || !r.width) continue;
      if (!fallback) fallback = { r, view };
      const cx = r.left + r.width / 2;
      if (cx >= view.left && cx <= view.right) return { r, view };
    }
    return fallback;
  }

  function pickLure() {
    if (roll('lure-honest') < TRICKSTER.GHOST_HONEST_LURE) return board.targetTile();
    const warm = board.tiles.filter((tile) => tile.warm);
    if (warm.length) return warm[Math.floor(roll('lure-warm') * warm.length)];
    return pickDecoy(null);
  }

  function ghostTick() {
    if (destroyed || stopped || !ghost) return;
    // Never moved, or the class is not hunting: the ghost does not exist.
    if (getPhase() !== 'hunt' || isHalted() || !lastMoveAt) {
      ghost.classList.remove('g-lf-ghost-on', 'g-lf-ghost-lure');
      return;
    }
    const now = Date.now();
    const stalled = now - lastMoveAt > TRICKSTER.GHOST_STALL_MS;
    if (!stalled) {
      lure = null;
      if (!trail.length) { ghost.classList.remove('g-lf-ghost-on', 'g-lf-ghost-lure'); return; }
      // trail mode: replay the pointer from GHOST_TRAIL_MS ago
      let pt = trail[0];
      for (const p of trail) { if (now - p.at >= TRICKSTER.GHOST_TRAIL_MS) pt = p; else break; }
      gx = pt.x; gy = pt.y;
      ghost.classList.add('g-lf-ghost-on');
      ghost.classList.remove('g-lf-ghost-lure');
    } else {
      if (!lure) { lure = pickLure(); ghostSeen = true; }
      const a = tileAnchor(lure);
      if (a) {
        const tx = a.r.left - a.view.left + a.r.width / 2;
        const ty = a.r.top - a.view.top + a.r.height / 2;
        gx += (tx - gx) * TRICKSTER.GHOST_LERP;
        gy += (ty - gy) * TRICKSTER.GHOST_LERP;
      }
      ghost.classList.add('g-lf-ghost-on', 'g-lf-ghost-lure');
    }
    ghost.style.transform = 'translate3d(' + gx.toFixed(1) + 'px,' + gy.toFixed(1) + 'px,0)';
    // Prune, but always keep the newest point - a stall must not erase the
    // pointer's last known position out from under the lure handoff.
    while (trail.length > 1 && now - trail[0].at > 2000) trail.shift();
  }

  function armGhost() {
    if (!ghostEligible()) return;
    ghost = el('div', 'g-lf-ghost');
    if (!ghost) return;
    hud.view.appendChild(ghost);
    onMove = (e) => {
      if (destroyed) return;
      let view = null;
      try { view = hud.view.getBoundingClientRect(); } catch (err) { return; }
      const x = Number(e.clientX) - view.left;
      const y = Number(e.clientY) - view.top;
      if (!Number.isFinite(x) || !Number.isFinite(y)) return;
      lastMoveAt = Date.now();
      trail.push({ x, y, at: lastMoveAt });
      lure = null;
    };
    hud.view.addEventListener('pointermove', onMove);
    ghostTimer = timers.every(TRICKSTER.GHOST_TICK_MS, ghostTick);
    say('trickster: ghost armed');
  }

  /* ---------------------------------------------------------------- api */
  return {
    /** Deal the class. Call once, when the hunt begins. */
    start() {
      if (!armed || destroyed) { say('trickster: disarmed'); return; }
      const deals = buildDeals();
      for (const deal of deals) timers.after(deal.at, () => attempt(deal, 0));
      armGhost();
      say('trickster: dealt ' + deals.length + ' cards ('
        + deals.map((d) => d.card + '@' + Math.round(d.at / 1000) + 's').join(', ') + ')');
    },

    /** The class is over: the ghost vanishes, no card may fire again. */
    stop() {
      stopped = true;
      if (ghost && ghost.classList) ghost.classList.remove('g-lf-ghost-on', 'g-lf-ghost-lure');
    },

    destroy() {
      destroyed = true;
      stopped = true;
      if (ghostTimer && timers) { timers.cancel(ghostTimer); ghostTimer = 0; }
      if (onMove && hud && hud.view && hud.view.removeEventListener) {
        try { hud.view.removeEventListener('pointermove', onMove); } catch (e) { /* ignore */ }
      }
      onMove = null;
      if (ghost) { try { ghost.remove(); } catch (e) { /* ignore */ } }
      ghost = null;
    },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return { armed, tier, ghost: !!ghost, ghostSeen, meltAnnounced };
    },
  };
}

export default createTrickster;
