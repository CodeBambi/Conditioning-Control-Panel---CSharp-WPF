/* ============================================================================
 * games/sort/casino.js - DECK II of the House Rules, dealt into SORT. The
 * lighting rig. pressure.js is what the room DOES to you as the chain climbs
 * and trickster.js is what it LIES to you about; this file is what it PAYS.
 *
 *   THE MARQUEE      the room already owns the halo behind the stack (it
 *                    crawls at rung 0 and chases from rung 6 off data-chase).
 *                    The casino hangs the BULBS on it: a ring of lamps whose
 *                    light travels while the bulbs hold still, paced by the
 *                    rung and lit in TONIGHT'S colour - a hue drawn off the
 *                    UTC DATE, not the class seed, so the hall is one colour
 *                    all evening and a different one tomorrow. About one night
 *                    in twenty the draw goes off-arc and the hall is gold.
 *   THE CHIME LADDER the room already rings the ROOT of the ladder on every
 *                    clean swipe (+1 semitone per link, cap 7) and a muted
 *                    thud on a wrong one. The casino does not repeat either -
 *                    it adds LAYERS, which is what the House Rules actually
 *                    asked for: an octave under the root from rung 3, a third
 *                    over it from rung 6. The stack gets deeper as you climb,
 *                    and it never climbs past the cap.
 *   THE PAYOUT LIGHT every THUD into the wall flashes the ring. One per card,
 *                    brighter up the ladder, and it is the beat that makes a
 *                    swipe feel like a coin landing rather than a card leaving.
 *   NEAR-MISS        JUST is the near-miss you WON: gold bulbs and a bright
 *                    ping. ALMOST is the one you lost: one dim pulse, and the
 *                    room has already said the word. RECORD is the one that is
 *                    still in play - your chain is one link off the best chain
 *                    this class has ever held, and the badge says so ONCE.
 *   THE JACKPOT      minor / major / royal arrive on the bus; the room already
 *                    rang the shell ceremony, so the casino DRESSES rather than
 *                    duplicates. Full show three times a class, then compact -
 *                    a machine that shouts at everything is a machine you stop
 *                    hearing. THE ROYAL is exempt and always pays THE REVEAL:
 *                    it happens at most once, and it is a different beat.
 *   SPARKLE          eight drawn specks off the card on a PERFECT. Drawn, so
 *                    it costs no decode, no engine kind and no media.
 *   THE BANK         a token leaves the wall slot the card landed in and is
 *                    paid into the Sorted chip, 500-650ms. The wall is what
 *                    you made; the chip is what you were paid for it.
 *
 * TABLE LAW AUDIT (House Rules)
 *   I   ledger honest - this file never reads or writes chain, rung, accuracy,
 *       the grade or the deck. Every number it renders it was HANDED, and the
 *       one thing it persists is a personal best (game meta `best`), which is
 *       a record of the ledger and never an input to it.
 *   II  input honest  - every node it mints is pointer-events:none and lives
 *       in its own layer or inside the halo, both of which sit behind or over
 *       the stack but never between a finger and the top card. It moves no
 *       hitbox: BOUNCE writes `scale`, an INDIVIDUAL property, because the
 *       room writes `transform` on the card every drag frame.
 *   III never still   - the bulbs turn from the first frame to the bell.
 *   IV  images/glyphs - two strings, both lexicon rows, both a WORD.
 *   V   seeded        - per-tag mulberry32 off seed+'|sort-casino|<tag>',
 *       append-only (a new tag never shifts an old stream; see the
 *       lost-and-found header for why makeTaggedRoll is not used). Tonight's
 *       hue is off the UTC DAY instead, which is the whole point of it.
 *   VI  exits sacred  - bgIntensity 0 disarms the rig; reduced motion keeps
 *       the light and loses the travel; every timer rides the game's registry
 *       (which dies with a freeze) and destroy() leaves no node behind.
 *   VII lexicon       - sort_record_near and sort_jackpot, nothing else.
 *
 * WHAT THE ROOM ALREADY DOES, AND THIS FILE THEREFORE DOES NOT
 *   the SHIVER on a wrong swipe (index.js wrongBeat), the muted thud cue, the
 *   root chime, the near_miss ceremony on an ALMOST, the jackpot ceremony, the
 *   verdict word, the wall THUD keyframe. Doubling any of them would be two
 *   heroes in one beat, which is the one restraint the pitch spends a
 *   paragraph on.
 *
 * THE LATE-BUILD GUARD: index.js imports its decks DYNAMICALLY, so the room can
 * open (and call start()) before this module exists. The deal handler therefore
 * arms the rig if start() never reached it. Idempotent both ways.
 * ==========================================================================*/

import { makeRng, hash01 } from '../../core/rng.js';
import { CHAIN, chimePitch } from './chain.js';

export const CASINO = Object.freeze({
  /** Bulbs on the ring, and the seeded lit/dim pattern period. */
  BULBS: 18,
  /** Marquee period at rung 0 and at the chase, in ms (the room's own halo
   *  runs 7000 / 2400; the bulbs ride the same two numbers on purpose). */
  MQ_MS_SLOW: 7000,
  MQ_MS_FAST: 2400,
  /** Presence band: the frame whispers before it shouts. */
  MQ_A_LO: 0.26,
  MQ_A_HI: 0.82,
  /** The payout flash, and the gold hold on a JUST or a rung up. */
  PAYOUT_MS: 620,
  GOLD_MS: 460,
  /** THE BANK: travel time band, and the chip punch. */
  BANK_MS: Object.freeze([500, 650]),
  BANK_PUNCH_MS: 320,
  /** SPARKLE: specks and their life. */
  SPARKS: 8,
  SPARK_MS: 520,
  /** THE REVEAL, and the badge's own life. */
  REVEAL_MS: 620,
  BADGE_MS: 1100,
  /** The full jackpot show is spent after this many; the royal is exempt.
   *  UNCHANGED at the 180s budget, on purpose. This is a SCARCITY, not a rate:
   *  it is shared by the three MAJOR_RUNGS (chain.js: 3 / 5 / 7, which are
   *  streak-triggered at 8 / 16 / 27 clean swipes and so still land inside the
   *  first ~30 cards however long the class runs) and by every minor roll after
   *  them. A longer class therefore spends the three full shows on its climb
   *  exactly as before and pays the extra minutes in payout lights and deeper
   *  chimes - which is the whole point of the budget. Raising it would make the
   *  interruption the room's default rather than its ceiling. */
  FULL_SHOWS: 3,
  /** The chime STACK: an octave under from rung 3, a third over from rung 6. */
  LOW_FROM_RUNG: 3,
  HIGH_FROM_RUNG: 6,
  LOW_RATIO: 0.5,
  HIGH_RATIO: 1.2599,
  /** A pitch this deck sends may never run past the room's own cap voice. */
  PITCH_CEIL: 1.98,
  /** Tonight's colour: the violet-to-rose arc, plus the rare gold hall. */
  HUE_ARC: Object.freeze([288, 348]),
  GOLD_HUE: 44,
  GOLD_CHANCE: 0.05,
  /** The record ping needs a best worth being one off. */
  RECORD_MIN_BEST: 3,
});

/* ------------------------------------------------------------------ tools -- */
function clamp01(v) { const n = Number(v); return !isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(band, t) { return band[0] + (band[1] - band[0]) * clamp01(t); }
function el(tag, cls) {
  try {
    if (typeof document === 'undefined' || !document.createElement) return null;
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    return n;
  } catch (e) { return null; }
}
function setVar(node, k, v) { try { if (node && node.style && node.style.setProperty) node.style.setProperty(k, String(v)); } catch (e) { /* noop */ } }
function addCls(node, c) { try { if (node && node.classList) node.classList.add(c); } catch (e) { /* noop */ } }
function delCls(node, c) { try { if (node && node.classList) node.classList.remove(c); } catch (e) { /* noop */ } }
function drop(node) { try { if (node && node.remove) node.remove(); } catch (e) { /* noop */ } }
/** The box of a node, in the page's own coordinates. Never throws. */
function boxOf(node) {
  try {
    if (node && typeof node.getBoundingClientRect === 'function') {
      const r = node.getBoundingClientRect();
      if (r) return { x: r.left || 0, y: r.top || 0, w: r.width || 0, h: r.height || 0 };
    }
  } catch (e) { /* noop */ }
  return null;
}
/** The UTC day a seed opens with. Tonight is a DAY, not a class. */
function dayOf(seed) {
  const m = /^(\d{4}-\d{2}-\d{2})/.exec(String(seed || ''));
  if (m) return m[1];
  try { return new Date().toISOString().slice(0, 10); } catch (e) { return '1970-01-01'; }
}

/* ============================================================================
 * THE DECK
 * ==========================================================================*/
export function create(o) {
  const bag = o || {};
  const ctx = bag.ctx || {};
  const bus = bag.bus || { on() { return () => {}; } };
  const readState = typeof bag.S === 'function' ? bag.S : () => null;
  const timers = bag.timers || null;
  const engine = bag.engine || null;
  const reduced = !!bag.reduced;
  const say = typeof bag.log === 'function' ? bag.log : () => {};
  const t = typeof bag.t === 'function' ? bag.t : (k, f) => (f == null ? k : f);

  const armedBase = !!timers && typeof timers.after === 'function'
    && typeof document !== 'undefined';

  /* ---- the caps gate: bgIntensity 0 disarms the whole rig ----------------- */
  function capsOk() {
    let v = null;
    try {
      const ch = engine && typeof engine.channels === 'function' ? engine.channels() : null;
      if (ch && ch.bgIntensity != null) v = Number(ch.bgIntensity);
    } catch (e) { /* noop */ }
    if (v == null) {
      try { if (ctx.caps && ctx.caps.bgIntensity != null) v = Number(ctx.caps.bgIntensity); }
      catch (e) { /* noop */ }
    }
    if (v == null || !isFinite(v)) return true;
    return v > 0.001;
  }

  /* ---- seeded streams, append-only tags ----------------------------------- */
  const seed = (() => { try { return String((readState() || {}).seed || 'sort'); } catch (e) { return 'sort'; } })();
  const seedBase = seed + '|sort-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- state -------------------------------------------------------------- */
  let destroyed = false;
  let started = false;
  let stopped = false;
  let paused = false;
  let heat = 0.2;
  let rung = 0;
  let topNode = null;
  let layer = null;
  let bulbs = null;
  let sparkHost = null;
  let badge = null;
  let goldTimer = 0;
  let payTimer = 0;
  let badgeTimer = 0;
  let fullShows = 0;
  let best = 0;
  let recordPinged = false;
  let tonightHue = 330;
  let tonightGold = false;
  const paid = { chimes: 0, payouts: 0, sparkles: 0, tokens: 0, jackFull: 0, jackCompact: 0, record: 0, just: 0, almost: 0, reveal: 0, bounce: 0 };
  const offs = [];

  const halted = () => destroyed || stopped || paused || !armedBase;
  function after(ms, fn) {
    if (!armedBase || destroyed) return 0;
    try { return timers.after(ms, () => { if (!destroyed) fn(); }); }
    catch (e) { return 0; }
  }
  function cancel(id) {
    if (!id || !timers) return;
    try { if (typeof timers.clear === 'function') timers.clear(id); }
    catch (e) { /* noop */ }
  }
  function nodes() { const s = readState(); return (s && s.nodes) || null; }

  /* ---- the audio stack ---------------------------------------------------- */
  function cue(name, level, pitch) {
    if (halted() || !engine || typeof engine.audio !== 'function') return;
    const p = Math.min(CASINO.PITCH_CEIL, Math.max(0.25, Number(pitch) || 1));
    try { engine.audio(name, level, { pitch: p }); } catch (e) { /* noop */ }
  }

  /* ======================================================== THE MARQUEE ==== */
  function mount() {
    if (layer || !armedBase) return false;
    const n = nodes();
    if (!n || !n.stage) return false;

    /* TONIGHT'S COLOUR. The UTC day, so it is the same for every class you
       play this evening and gone at midnight - scarcity with nothing gated. */
    const day = dayOf(seed);
    tonightGold = hash01(day + '|sort-tonight-gold') < CASINO.GOLD_CHANCE;
    tonightHue = tonightGold
      ? CASINO.GOLD_HUE
      : Math.round(lerp(CASINO.HUE_ARC, hash01(day + '|sort-tonight-hue')));
    setVar(n.stage, '--sort-mq-h', String(tonightHue));

    if (capsOk() && n.halo) {
      bulbs = el('div', 'g-sort-bulbs');
      if (bulbs) {
        const step = 360 / CASINO.BULBS;
        /* a seeded 3-stop pattern, so the ring is a MARQUEE and not a dotted
           circle: some lamps stand bright, some sit half lit */
        const phase = Math.floor(roll('bulb-phase') * 3);
        for (let i = 0; i < CASINO.BULBS; i++) {
          const b = el('i', '');
          if (!b) continue;
          setVar(b, '--sort-bulb', String(Math.round(i * step)));
          setVar(b, '--sort-bulb-on', ((i + phase) % 3 === 0) ? '1' : '0.35');
          bulbs.appendChild(b);
        }
        try { n.halo.appendChild(bulbs); } catch (e) { /* noop */ }
        if (tonightGold) addCls(bulbs, 'is-gold');
      }
    }

    layer = el('div', 'g-sort-cas');
    if (layer) {
      sparkHost = el('div', 'g-sort-spark');
      badge = el('div', 'g-sort-badge');
      if (sparkHost) layer.appendChild(sparkHost);
      if (badge) layer.appendChild(badge);
      try { if (n.playfield) n.playfield.appendChild(layer); else n.stage.appendChild(layer); }
      catch (e) { /* noop */ }
    }
    paint();
    return true;
  }

  /** Pace and presence from the rung and the heat. Idempotent. */
  function paint() {
    const n = nodes();
    if (!n || !n.stage) return;
    const chase = clamp01(rung / Math.max(1, CHAIN.MAX_RUNG));
    const ms = Math.round(CASINO.MQ_MS_SLOW - (CASINO.MQ_MS_SLOW - CASINO.MQ_MS_FAST) * chase);
    setVar(n.stage, '--sort-mq-t', ms + 'ms');
    setVar(n.stage, '--sort-mq-a', (CASINO.MQ_A_LO + (CASINO.MQ_A_HI - CASINO.MQ_A_LO) * clamp01(heat)).toFixed(3));
    setVar(n.stage, '--sort-heat', clamp01(heat).toFixed(3));
  }

  /** THE PAYOUT LIGHT. One flash per THUD, restartable back to back. */
  function payout() {
    if (!bulbs || halted() || reduced) return;
    delCls(bulbs, 'is-payout');
    try { void (bulbs.offsetWidth); } catch (e) { /* DOM double */ }
    addCls(bulbs, 'is-payout');
    paid.payouts += 1;
    cancel(payTimer);
    payTimer = after(CASINO.PAYOUT_MS, () => delCls(bulbs, 'is-payout'));
  }

  /** The gold hold: a JUST, a rung up, a jackpot. Never on a wrong swipe. */
  function gold(ms) {
    if (!bulbs || halted() || tonightGold) return;
    addCls(bulbs, 'is-gold');
    cancel(goldTimer);
    goldTimer = after(ms || CASINO.GOLD_MS, () => { if (!tonightGold) delCls(bulbs, 'is-gold'); });
  }

  /* ============================================================ THE BADGE == */
  /** One word, held back a beat so it never shares the frame with the room's
   *  own verdict word. Tone 'record' recolours it; anything else is gold. */
  function sayBadge(text, tone) {
    if (!badge || halted()) return;
    badge.textContent = String(text || '');
    try { badge.setAttribute('data-tone', tone || ''); } catch (e) { /* noop */ }
    delCls(badge, 'show');
    try { void (badge.offsetWidth); } catch (e) { /* DOM double */ }
    addCls(badge, 'show');
    cancel(badgeTimer);
    badgeTimer = after(CASINO.BADGE_MS, () => { if (badge) delCls(badge, 'show'); });
  }

  /* =========================================================== THE SPARKLE = */
  function sparkle(n) {
    if (!sparkHost || halted() || reduced || !capsOk()) return;
    const count = Math.max(3, Math.min(CASINO.SPARKS, Math.round(n || CASINO.SPARKS)));
    const spin = Math.round(roll('spark') * 360);
    const made = [];
    for (let i = 0; i < count; i++) {
      const s = el('i', '');
      if (!s) continue;
      setVar(s, '--sort-spark-a', (spin + Math.round(i * (360 / count))) + 'deg');
      sparkHost.appendChild(s);
      made.push(s);
    }
    if (!made.length) return;
    paid.sparkles += 1;
    after(CASINO.SPARK_MS + 120, () => { for (const s of made) drop(s); });
  }

  /* ============================================================== THE BANK = */
  /**
   * A token leaves the wall tile the card landed in and is paid into the
   * Sorted chip. With no geometry (a DOM double, a stage that has not laid out
   * yet) the token still mints and still travels: the beat is the point, and a
   * zero-length flight is a coin dropped straight into the till.
   */
  function bank(tile) {
    if (!layer || halted() || reduced || !capsOk()) return;
    const n = nodes();
    const chip = n && n.chipSorted ? n.chipSorted.el : null;
    const home = boxOf(layer);
    const from = boxOf(tile) || boxOf(n && n.stack) || null;
    const to = boxOf(chip) || null;
    const tok = el('i', 'g-sort-token');
    if (!tok) return;
    const ox = home ? home.x : 0;
    const oy = home ? home.y : 0;
    const x0 = from ? Math.round(from.x - ox + from.w / 2) : 0;
    const y0 = from ? Math.round(from.y - oy + from.h / 2) : 0;
    const x1 = to ? Math.round(to.x - ox + to.w / 2) : x0;
    const y1 = to ? Math.round(to.y - oy + to.h / 2) : y0;
    const ms = Math.round(lerp(CASINO.BANK_MS, roll('bank')));
    setVar(tok, '--sort-tok-x0', x0 + 'px');
    setVar(tok, '--sort-tok-y0', y0 + 'px');
    setVar(tok, '--sort-tok-x1', x1 + 'px');
    setVar(tok, '--sort-tok-y1', y1 + 'px');
    setVar(tok, '--sort-tok-ms', ms + 'ms');
    layer.appendChild(tok);
    paid.tokens += 1;
    after(ms + 40, () => {
      drop(tok);
      if (chip) {
        addCls(chip, 'is-paid');
        after(CASINO.BANK_PUNCH_MS, () => delCls(chip, 'is-paid'));
      }
    });
  }

  /* ============================================================ THE REVEAL = */
  function reveal() {
    if (!layer || halted()) return;
    const n = el('div', 'g-sort-reveal');
    if (!n) return;
    layer.appendChild(n);
    paid.reveal += 1;
    after(CASINO.REVEAL_MS + 80, () => drop(n));
  }

  /* ========================================================== THE JACKPOTS = */
  /**
   * The room already rang the shell's jackpot ceremony and its own cue by the
   * time this lands. The casino DRESSES it, and it spends a full show three
   * times a class - after that a win is a payout light and a deeper chime,
   * which is still a win and is no longer an interruption.
   */
  function jackpot(ev) {
    if (halted()) return;
    const royal = ev && ev.why === 'royal';
    const intensity = clamp01(ev && ev.intensity);
    if (royal) {
      /* THE ROYAL IS EXEMPT from the budget: it happens at most once a class
         and it is a different beat, not a louder one. */
      reveal();
      gold(1200);
      sparkle(CASINO.SPARKS);
      cue('jackpot', 0.5, 1.4983);
      paid.jackFull += 1;
      fullShows += 1;
      return;
    }
    if (fullShows < CASINO.FULL_SHOWS) {
      fullShows += 1;
      paid.jackFull += 1;
      gold(Math.round(CASINO.GOLD_MS * (1 + intensity)));
      sparkle(Math.round(4 + 4 * intensity));
      payout();
      sayBadge(t('sort_jackpot', 'JACKPOT'), '');
      cue('jackpot', 0.34, 1.26);
      return;
    }
    paid.jackCompact += 1;
    payout();
    cue('jackpot', 0.22, 1.12);
  }

  /* ======================================================== THE CHIME STACK */
  function chime(chain, atRung) {
    if (halted()) return;
    const root = chimePitch(Math.min(CHAIN.CHIME_CAP, Math.max(0, chain)));
    let layers = 0;
    if (atRung >= CASINO.LOW_FROM_RUNG) {
      cue('bubble_pop', 0.2, root * CASINO.LOW_RATIO);
      layers += 1;
    }
    if (atRung >= CASINO.HIGH_FROM_RUNG) {
      cue('bubble_pop', 0.16, root * CASINO.HIGH_RATIO);
      layers += 1;
    }
    if (layers) paid.chimes += 1;
  }

  /* ========================================================== THE BEST SEEN */
  function loadBest() {
    try {
      const m = (ctx.store && typeof ctx.store.gameMeta === 'function') ? (ctx.store.gameMeta('sort') || {}) : {};
      const a = Number(m.best) || 0;
      const b = Number(m.bestChain) || 0;
      best = Math.max(0, Math.round(Math.max(a, b)));
    } catch (e) { best = 0; }
  }
  function saveBest() {
    const s = readState();
    const got = s ? Math.max(0, Math.round(Number(s.longestChain) || 0)) : 0;
    if (!got || got <= best) return;
    try {
      if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
        ctx.store.mergeGameMeta('sort', { best: got });
      }
    } catch (e) { /* a record is decoration; a class is not */ }
  }

  /* ============================================================== THE WIRE = */
  /** THE LATE-BUILD GUARD (see the header): the deal arms us if start() did not. */
  function ensureStarted() {
    if (started || destroyed || stopped) return;
    api.start();
  }

  function onDeal(ev) {
    ensureStarted();
    topNode = (ev && ev.node) || null;
  }

  function onGrab() {
    if (halted() || reduced || !topNode) return;
    delCls(topNode, 'is-bounce');
    try { void (topNode.offsetWidth); } catch (e) { /* DOM double */ }
    addCls(topNode, 'is-bounce');
    paid.bounce += 1;
  }

  function onCommit(ev) {
    if (halted() || !ev) return;
    if (!ev.correct) return;                 /* the room's muted thud stands alone */
    chime(ev.chain, ev.rung);
    if (ev.just) { paid.just += 1; gold(CASINO.GOLD_MS); cue('streak', 0.3, 1.4983); }
    else if (ev.almost) { paid.almost += 1; payout(); cue('whisper', 0.16, 0.8); }
    /* THE RECORD PING. Once a class, and only when there is a best worth being
       one link off. The room never says this - it does not know your history. */
    if (!recordPinged && best >= CASINO.RECORD_MIN_BEST && ev.chain >= best - 1) {
      recordPinged = true;
      paid.record += 1;
      after(220, () => {
        sayBadge(t('sort_record_near', 'ONE OFF YOUR BEST'), 'record');
        cue('streak', 0.26, 1.26);
      });
    }
  }

  function onPerfect() {
    if (halted()) return;
    sparkle(CASINO.SPARKS);
  }

  function onRung(ev) {
    rung = Math.max(0, Math.round((ev && ev.to) || 0));
    paint();
    if (halted() || !ev || ev.down) return;
    gold(CASINO.GOLD_MS);
  }

  function onLand(ev) {
    if (halted()) return;
    payout();
    if (ev && !ev.wrong) bank(ev.tile);
  }

  /* ================================================================ THE API */
  const api = {
    start() {
      if (destroyed || started) return;
      started = true;
      stopped = false;
      loadBest();
      const ok = mount();
      say('casino: ' + (ok ? 'lit' : 'no stage')
        + ', tonight hue ' + tonightHue + (tonightGold ? ' (GOLD HALL)' : '')
        + ', best chain ' + best + (capsOk() ? '' : ', CAPPED'));
    },

    setHeat(h) {
      const v = Number(h);
      heat = isFinite(v) ? clamp01(v) : heat;
      const s = readState();
      if (s) rung = Math.max(0, Math.round(Number(s.rung) || 0));
      paint();
    },

    pause() {
      paused = true;
      /* the marquee freezes with the room: a rig that kept turning over a
         paused class would be the room insisting it was still playing */
      try { if (bulbs && bulbs.style) bulbs.style.animationPlayState = 'paused'; } catch (e) { /* noop */ }
    },

    resume() {
      paused = false;
      try { if (bulbs && bulbs.style) bulbs.style.animationPlayState = 'running'; } catch (e) { /* noop */ }
    },

    /** The bell. The record is written here, because here is where it is true. */
    end() {
      if (stopped) return;
      stopped = true;
      saveBest();
      cancel(payTimer); cancel(goldTimer); cancel(badgeTimer);
      payTimer = 0; goldTimer = 0; badgeTimer = 0;
      if (bulbs) { delCls(bulbs, 'is-payout'); if (!tonightGold) delCls(bulbs, 'is-gold'); }
      if (badge) delCls(badge, 'show');
      say('casino: closed (' + paid.payouts + ' payouts, ' + paid.tokens + ' banked, '
        + paid.jackFull + ' full shows, ' + paid.jackCompact + ' compact)');
    },

    destroy() {
      if (!destroyed) { try { api.end(); } catch (e) { /* noop */ } }
      destroyed = true;
      for (const off of offs) { try { off(); } catch (e) { /* noop */ } }
      offs.length = 0;
      drop(bulbs); drop(layer);
      bulbs = null; layer = null; sparkHost = null; badge = null; topNode = null;
    },

    diagnostics() {
      return {
        armed: armedBase && capsOk(),
        started, stopped, paused, destroyed, reduced,
        heat, rung, best, recordPinged,
        tonight: { hue: tonightHue, gold: tonightGold },
        marquee: !!bulbs,
        fullShows,
        paid: Object.assign({}, paid),
      };
    },
  };

  /* the wire, last: nothing above may fire before the api object exists */
  offs.push(bus.on('deal', onDeal));
  offs.push(bus.on('grab', onGrab));
  offs.push(bus.on('commit', onCommit));
  offs.push(bus.on('perfect', onPerfect));
  offs.push(bus.on('rung', onRung));
  offs.push(bus.on('land', onLand));
  offs.push(bus.on('jackpot', jackpot));
  offs.push(bus.on('end', () => { try { api.end(); } catch (e) { /* noop */ } }));

  return api;
}

export default { CASINO, create };
