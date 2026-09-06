/* ============================================================================
 * hud.js - Racing Thoughts HUD. Implements CONTRACT.md "race/hud.js + race/race.css".
 *
 * DOM only, built inside the `.race-hud` div race.html provides. Score rolls
 * up on a short tween (tabular numerals), the multiplier pill pulses on every
 * rung (CHIME LADDER), the combo bar drains over COMBO_HOLD_SEC, the MARQUEE
 * banner rides in as a left ribbon under the score block once per gate in the
 * room's colour, the item slot bounces,
 * speed is a gauge with a boost state. Toasts each carry their own motion (ALMOST shivers,
 * JACKPOT is a gold flash and a REVEAL, BANK flies tokens into the score and
 * ticks the counter as they land). flicker() is the Stat Flicker: the face
 * lies for 450 ms, the ledger never does (Law I). The Brake and the End card
 * are the only pointer targets. Copy is DtRH voice: lowercase, short.
 *
 * THE PICKUP (pass f3): a cube gives its item a card of its own, centre of the
 * view just above the cup. It rolls a `?` while items.js rolls, flips to the
 * decided item the moment items.js arms it (never earlier: Fake Shuffle),
 * holds, then flies into the item slot and leaves the slot lit until the item
 * is used. Under reduced motion the card fades in place instead of flying.
 *
 * Pass three: THE MIXER rail above the item slot shows the live ingredients
 * of the mix (race/cocktail.js) as pixel chips with drain bars and charge
 * pips, and the served recipe's name while one is live. strobe() is the
 * white edge blink under a flash charge; setTint() pinks the chrome.
 * ==========================================================================*/

import { COMBO_HOLD_SEC, MULT_LADDER, KART_MAX_SPEED, KART_BASE_SPEED } from './consts.js';

const FLICK_MS = 450;
const TOAST_HOLD = { pop: 1100, almost: 1300, jackpot: 1800, bank: 1600, item: 1400, effect: 1400, recipe: 1700 };
/** Two "+N" pops inside this window merge into one counting toast ("+30 x3"). */
const POP_MERGE_MS = 600;
/** Two "almost +N" inside this window merge the same way ("almost +50 x2"). */
const ALMOST_MERGE_MS = 900;
/** Chatter kinds (pop, almost) may not open a NEW toast more often than this. */
const CHATTER_GAP_MS = 180;

/** The second-pass rung ladder. consts.js MULT_LADDER is the source of truth;
 * this is the documented fallback for when the import is missing or malformed. */
const NEW_LADDER = [[0, 1], [3, 2], [8, 3], [15, 4], [25, 6], [40, 8]];
const okLadder = (l) => Array.isArray(l) && l.length > 1
  && l.every((r) => Array.isArray(r) && r.length > 1 && isFinite(r[0]) && isFinite(r[1]));

export function createRaceHud(root) {
  const reduced = !!(typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const timers = new Set();
  let raf = 0;
  let disposed = false;

  const el = (cls, parent, text) => {
    const d = document.createElement('div');
    d.className = cls;
    if (text != null) d.textContent = text;
    parent.appendChild(d);
    return d;
  };
  const later = (fn, ms) => {
    const t = setTimeout(() => { timers.delete(t); if (!disposed) fn(); }, ms);
    timers.add(t);
    return t;
  };
  // re-trigger a one-shot animation class even when it is already applied
  const hit = (node, cls) => { node.classList.remove(cls); void node.offsetWidth; node.classList.add(cls); };
  const fmt = (n) => Math.round(n).toLocaleString('en-US');

  // ---- chrome (z3, click-through) ----
  const chrome = el('rh-chrome', root);
  const vignette = el('rh-vignette', chrome);
  const gold = el('rh-gold', chrome);

  const scoreWrap = el('rh-score-wrap rh-plate', chrome);
  el('rh-label', scoreWrap, 'score');
  const scoreRow = el('rh-score-row', scoreWrap);
  const scoreEl = el('rh-score rh-num', scoreRow, '0');
  const multEl = el('rh-mult rh-num', scoreRow, 'x1');
  const rungs = el('rh-rungs', scoreRow);
  const comboRow = el('rh-combo-row is-off', scoreWrap);
  const comboBar = el('rh-combo-bar', comboRow);
  const comboFill = el('rh-combo-fill', comboBar);
  comboFill.style.setProperty('--rh-hold', `${COMBO_HOLD_SEC}s`);
  const comboN = el('rh-num', comboRow, 'combo 0');
  const nextEl = el('rh-next rh-num', scoreWrap, '');
  const bankEl = el('rh-bank rh-num', scoreWrap, 'kept 0');

  // ---- the rung ladder: consts by default, swappable via setLadder ----
  let ladder = okLadder(MULT_LADDER) ? MULT_LADDER : NEW_LADDER;
  let rungEls = [];
  function buildRungs() {
    rungs.textContent = '';
    rungEls = ladder.slice(1).map(() => el('rh-rung', rungs));
  }
  buildRungs();
  /** "next x2 in 3" for the combo we are on, or the top-rung note. */
  function nextRungText(combo) {
    for (let i = 1; i < ladder.length; i++) {
      if (combo < ladder[i][0]) return `next x${ladder[i][1]} in ${ladder[i][0] - combo}`;
    }
    return 'top rung';
  }
  const topRung = () => ladder[ladder.length - 1][1];
  nextEl.textContent = nextRungText(0);

  // MARQUEE: a left ribbon under the score block (never top-centre, where the
  // media lane and the payload cards land). It stays inside .rh-chrome at z3 so
  // a freeze or the mandatory tape still covers it, exactly as before; the lane
  // is what keeps cards off it (mediaLane.js skips the left rail while it is up).
  const banner = el('rh-banner', chrome);
  const bannerName = el('rh-banner-name', banner);
  const bannerTag = el('rh-banner-tag', banner);

  const item = el('rh-item', chrome);
  const itemBox = el('rh-item-box', item, '·');
  const itemName = el('rh-item-name', item, 'no item yet');
  const itemKey = document.createElement('span');
  itemKey.className = 'rh-item-key';
  itemKey.textContent = 'E';
  itemName.appendChild(itemKey);

  // THE PICKUP: the item's own card. No per-item art ships, so the tile IS the picture: the room
  // colour, a soft glow and the glyph big. It rides in the chrome, so a freeze or a tape covers it.
  const pickup = el('rh-pickup', chrome);
  const pickTile = el('rh-pickup-tile', pickup);
  const pickGlyph = el('rh-pickup-glyph', pickTile, '?');
  const pickName = el('rh-pickup-name', pickup, '');

  const speed = el('rh-speed rh-plate', chrome);
  const speedRow = el('rh-speed-row', speed);
  const speedN = document.createElement('span');
  speedN.className = 'rh-speed-n rh-num';
  speedN.textContent = '0';
  const speedU = document.createElement('span');
  speedU.className = 'rh-speed-u';
  speedU.textContent = 'km/h';
  speedRow.append(speedN, speedU);
  const speedBar = el('rh-speed-bar', speed);
  const speedFill = el('rh-speed-fill', speedBar);
  el('rh-speed-mark', speedBar).style.left = `${(KART_BASE_SPEED / KART_MAX_SPEED * 100).toFixed(1)}%`;
  el('rh-speed-tag', speed, 'boost');   // shown only while .is-boost is on
  let speedHot = false;

  // ---- the pickup's own clock: roll -> flip -> hold -> flight -> the slot holds it ----
  const PICK_HOLD_MS = 600, PICK_FLY_MS = 420;
  let pickTimers = [], pickLive = false;
  const pickLater = (fn, ms) => { const t = later(fn, ms); pickTimers.push(t); return t; };
  /** Take the card off screen at once. A second cube may never stack a second card (no orphans). */
  function endPickup() {
    for (const t of pickTimers) { clearTimeout(t); timers.delete(t); }
    pickTimers = [];
    pickLive = false;
    pickup.classList.remove('is-on', 'is-rolling', 'is-flip', 'is-fly');
    pickup.style.transform = ''; pickup.style.transformOrigin = '';
  }
  /** The flight: the slot's rect is measured HERE, so the card lands right at any window size. */
  function flyPickup(onLand) {
    const land = () => {
      pickLive = false;
      item.classList.remove('is-inbound');
      item.classList.add('is-held');
      hit(item, 'is-bounce');
      if (onLand) { try { onLand(); } catch (e) { /* a listener never breaks the run */ } }
    };
    if (reduced) { pickup.classList.add('is-fly'); pickLater(endPickup, 160); land(); return; }
    try {
      const from = pickTile.getBoundingClientRect(), to = itemBox.getBoundingClientRect(), card = pickup.getBoundingClientRect();
      if (from.width > 0 && to.width > 0) {
        const dx = (to.left + to.width / 2) - (from.left + from.width / 2);
        const dy = (to.top + to.height / 2) - (from.top + from.height / 2);
        pickup.style.transformOrigin = `${(from.left + from.width / 2 - card.left).toFixed(1)}px ${(from.top + from.height / 2 - card.top).toFixed(1)}px`;
        pickup.style.transform = `translate(${dx.toFixed(1)}px, ${dy.toFixed(1)}px) scale(${(to.width / from.width).toFixed(3)})`;
      }
    } catch (e) { /* no layout yet: the card fades where it stands */ }
    pickup.classList.add('is-fly');
    pickLater(() => { endPickup(); land(); }, PICK_FLY_MS);
  }

  const toasts = el('rh-toasts', chrome);
  const strobeEl = el('rh-strobe', chrome);

  // ---- THE MIXER: the live ingredients, one chip per category, above the item slot ----
  const mixer = el('rh-mixer', chrome);
  el('rh-mixer-label', mixer, 'the mix');
  const mixRow = el('rh-mixer-row', mixer);
  const mixRecipe = el('rh-mixer-recipe', mixer);
  const mixRecipeName = el('rh-mixer-recipe-name', mixRecipe, '');
  const mixRecipeBar = el('rh-mixer-recipe-bar', mixRecipe);
  const mixRecipeFill = el('rh-mixer-recipe-fill', mixRecipeBar);
  const chips = new Map();   // category -> { el, glyph, n, pips[], fill, charges, depth }
  let lastRecipe = null;
  function chipFor(slot) {
    let c = chips.get(slot.category);
    if (c) return c;
    const node = el(`rh-chip rh-chip--${slot.category}`, mixRow);
    const glyph = el('rh-chip-glyph', node, slot.glyph || '·');
    const n = el('rh-chip-n rh-num', node, '');
    const pipRow = el('rh-chip-pips', node);
    const pips = [];
    for (let i = 0; i < Math.min(5, slot.max || 1); i++) pips.push(el('rh-pip', pipRow));
    const fill = el('rh-chip-fill', el('rh-chip-bar', node));
    c = { el: node, glyph, n, pips, fill, charges: -1, depth: -1 };
    chips.set(slot.category, c);
    hit(node, 'is-in');
    return c;
  }

  // ---- score tween: a self-terminating rAF loop that only spins while a delta is open ----
  let scoreTarget = 0, scoreShown = 0, bankTarget = 0, bankShown = 0;
  let flickUntil = 0;
  function tick() {
    raf = 0;
    let busy = false;
    if (Math.abs(scoreTarget - scoreShown) > 0.5) {
      scoreShown += (scoreTarget - scoreShown) * (reduced ? 1 : 0.22);
      if (Math.abs(scoreTarget - scoreShown) < 0.5) scoreShown = scoreTarget; else busy = true;
      if (performance.now() > flickUntil) scoreEl.textContent = fmt(scoreShown);
    }
    if (Math.abs(bankTarget - bankShown) > 0.5) {
      bankShown += (bankTarget - bankShown) * (reduced ? 1 : 0.2);
      if (Math.abs(bankTarget - bankShown) < 0.5) bankShown = bankTarget; else busy = true;
      bankEl.textContent = `kept ${fmt(bankShown)}`;
    }
    if (busy && !disposed) raf = requestAnimationFrame(tick);
  }
  const wake = () => { if (!raf && !disposed) raf = requestAnimationFrame(tick); };

  let lastMult = 1, lastCombo = 0;
  // the open runs: a live "+N" / "almost +N" toast, when it was last hit, and its running total
  const RUN_OF = {
    pop: { re: /^\+(\d+)$/, merge: POP_MERGE_MS, text: (s, n) => `+${fmt(s)} x${n}` },
    almost: { re: /^almost \+(\d+)$/, merge: ALMOST_MERGE_MS, text: (s, n) => `almost +${fmt(s)} x${n}` },
  };
  const runs = {};
  for (const k of Object.keys(RUN_OF)) runs[k] = { kind: k, ...RUN_OF[k], el: null, at: 0, sum: 0, n: 0, kill: 0 };
  let lastChatterAt = -1e9;
  function fold(run, gain, now) {
    run.at = now; run.sum += gain; run.n += 1;
    const node = run.el;
    node.textContent = run.text(run.sum, run.n);
    node.classList.toggle('is-run', run.n > 1);
    // restart the toast's own hold animation so the merged toast re-pops
    node.style.animation = 'none';
    void node.offsetWidth;
    node.style.animation = '';
    if (run.kill) { clearTimeout(run.kill); timers.delete(run.kill); }
    run.kill = later(() => { node.remove(); if (run.el === node) run.el = null; }, TOAST_HOLD[run.kind] + 60);
  }

  // ---- BANK: tokens fly from low centre into the score; the counter ticks per landing ----
  function bankTokens(text) {
    const n = Math.min(7, 3 + Math.floor(Math.abs(bankTarget - bankShown) / 40));
    const s = scoreEl.getBoundingClientRect();
    const w = window.innerWidth, h = window.innerHeight;
    for (let i = 0; i < n; i++) {
      const tk = el('rh-token', chrome);
      const x0 = w * 0.5 + (Math.random() - 0.5) * 60, y0 = h * 0.66;
      tk.style.left = `${x0}px`;
      tk.style.top = `${y0}px`;
      tk.style.setProperty('--dx', `${s.left + s.width / 2 - x0}px`);
      tk.style.setProperty('--dy', `${s.top + s.height / 2 - y0}px`);
      later(() => tk.classList.add('is-fly'), reduced ? 0 : i * 70);
      later(() => { tk.remove(); hit(bankEl, 'is-pop'); if (i === n - 1) hit(scoreEl, 'is-pop'); }, reduced ? 120 : i * 70 + 640);
    }
    return text;
  }

  // ---- screens (z20, the only pointer targets) ----
  let brakeResolve = null, endResolve = null;
  const brake = el('rh-screen', root);
  const brakeCard = el('rh-card', brake);
  brakeCard.appendChild(Object.assign(document.createElement('h2'), { textContent: 'the brake' }));
  brakeCard.appendChild(Object.assign(document.createElement('p'), { textContent: 'the road waits. nothing is lost.' }));
  const brakeBtns = el('rh-btns', brakeCard);
  const btn = (parent, text, cls, on) => {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = `rh-btn${cls ? ' ' + cls : ''}`;
    b.textContent = text;
    b.addEventListener('click', on);
    parent.appendChild(b);
    return b;
  };
  const settleBrake = (v) => { const r = brakeResolve; brakeResolve = null; brake.classList.remove('is-on'); if (r) r(v); };
  btn(brakeBtns, 'back on the road', '', () => settleBrake('resume'));
  btn(brakeBtns, 'end the run', 'rh-btn--quiet', () => settleBrake('end'));
  el('rh-hints', brakeCard, reduced
    ? 'esc resumes. reduced motion is on: no roll, no jolt, banners fade.'
    : 'esc resumes. your score stays. turn on reduced motion in your system settings for a level camera and no jolts.');

  const count = el('rh-count', root);   // 3 2 1 go: the one HUD element of the intro and the again
  const end = el('rh-screen', root);
  const endCard = el('rh-card', end);
  const endTitle = endCard.appendChild(Object.assign(document.createElement('h2'), { textContent: 'the tea party' }));
  endCard.appendChild(Object.assign(document.createElement('p'), { textContent: 'everybody has won.' }));
  const pbEl = el('rh-pb', endCard, 'personal best');
  const rows = document.createElement('dl');
  rows.className = 'rh-rows';
  endCard.appendChild(rows);
  const endBtns = el('rh-btns', endCard);
  const settleEnd = (v) => { const r = endResolve; endResolve = null; end.classList.remove('is-on'); if (r) r(v); };
  btn(endBtns, 'again', '', () => settleEnd('again'));
  btn(endBtns, 'surface', 'rh-btn--quiet', () => settleEnd('exit'));
  const fmtDur = (sec) => { const m = Math.floor(sec / 60), s = Math.floor(sec % 60); return `${m}:${s < 10 ? '0' : ''}${s}`; };

  const hud = {
    setScore(n) { scoreTarget = Math.max(0, n || 0); wake(); },
    setCombo(combo, mult) {
      combo = combo | 0; mult = mult || 1;
      comboN.textContent = `combo ${combo}`;
      comboRow.classList.toggle('is-off', combo <= 0);
      if (combo > 0 && (combo !== lastCombo || !comboFill.classList.contains('is-draining'))) hit(comboFill, 'is-draining');
      if (combo <= 0) comboFill.classList.remove('is-draining');
      if (mult !== lastMult) {
        multEl.textContent = `x${mult}`;
        multEl.classList.toggle('is-gold', mult >= 6);
        if (mult > lastMult) { hit(multEl, 'is-pulse'); if (mult >= topRung()) hit(scoreEl, 'is-pop'); }
      }
      rungEls.forEach((r, i) => r.classList.toggle('is-lit', mult >= ladder[i + 1][1]));
      // how far the next rung is, so the first one never reads as unreachable
      const txt = nextRungText(combo);
      if (txt !== nextEl.textContent) nextEl.textContent = txt;
      const gap = /in (\d+)$/.exec(txt);
      nextEl.classList.toggle('is-close', !!gap && +gap[1] <= 2);
      nextEl.classList.toggle('is-top', !gap);
      lastMult = mult; lastCombo = combo;
    },
    /** Swap the rung ladder ([[comboAtLeast, mult], ...]). Rebuilds the pips. */
    setLadder(l) {
      if (!okLadder(l)) return;
      ladder = l.map((r) => [Number(r[0]) || 0, Number(r[1]) || 1]);
      buildRungs();
      hud.setCombo(lastCombo, lastMult);
    },
    setSpeed(ms, boosting) {
      const v = Math.max(0, ms || 0);
      speedN.textContent = String(Math.round(v * 3.6));
      speedFill.style.transform = `scaleX(${Math.min(1, v / KART_MAX_SPEED).toFixed(3)})`;
      // run.js passes speed alone, so infer the boost: cruise is capped at
      // KART_BASE_SPEED and only a boost pin lets the kart past it
      const hot = boosting != null ? !!boosting : v > KART_BASE_SPEED + 0.4;
      if (hot !== speedHot) { speedHot = hot; speed.classList.toggle('is-boost', hot); }
    },
    setBank(n) { bankTarget = Math.max(0, n || 0); wake(); },
    banner(name, tagline, colorHex) {
      if (colorHex) chrome.style.setProperty('--rh-room', colorHex);
      bannerName.textContent = (name || '').toLowerCase();
      bannerTag.textContent = (tagline || '').toLowerCase();
      banner.classList.remove('is-on');
      // sit the ribbon under the MEASURED score block, so it never rides up
      // into the bank line on a short window or down into the media lane
      try {
        const b = scoreWrap.getBoundingClientRect();
        if (b && b.bottom > 0) banner.style.top = `${Math.round(b.bottom + 12)}px`;
      } catch (e) { /* no layout yet: the CSS fallback top stands */ }
      later(() => banner.classList.add('is-on'), 40);
      later(() => banner.classList.remove('is-on'), 2600);
    },
    item(glyph, name) {
      itemBox.textContent = glyph == null ? '·' : String(glyph);
      itemName.firstChild.textContent = (name || (glyph == null ? 'no item yet' : '')).toLowerCase();
      itemName.appendChild(itemKey);
      item.classList.toggle('is-armed', glyph != null);
      item.classList.toggle('is-rolling', glyph === '?');
      if (glyph == null) item.classList.remove('is-held', 'is-inbound');
      hit(item, 'is-bounce');
    },
    /** THE PICKUP, on `itemRoll`: the card pops in near the cup with the rolling `?`. */
    pickupRoll() {
      endPickup();
      pickGlyph.textContent = '?';
      pickName.textContent = 'rolling';
      void pickup.offsetWidth;                       // replay the pop when one card follows another
      pickup.classList.add('is-on', 'is-rolling');
      pickLive = true;
    },
    /** On `itemArm`: the card flips to the decided item, holds, then flies into the slot.
     *  `onLand` fires as it lands, for the sound the run brain owns. */
    pickupArm(glyph, name, onLand) {
      if (!pickLive) { hud.pickupRoll(); }
      pickup.classList.remove('is-rolling');
      pickGlyph.textContent = glyph == null ? '?' : String(glyph);
      pickName.textContent = String(name || '').toLowerCase();
      hit(pickup, 'is-flip');
      item.classList.add('is-inbound');              // the slot stays empty until the card lands in it
      pickLater(() => flyPickup(onLand), reduced ? 200 : PICK_HOLD_MS);
    },
    /** On `itemUse` (or a reset): the card and the slot's highlight both go. */
    pickupClear() {
      endPickup();
      item.classList.remove('is-held', 'is-inbound');
    },
    toast(text, kind) {
      kind = TOAST_HOLD[kind] ? kind : 'pop';
      let body = String(text == null ? '' : text);
      // COALESCE: dense chatter used to stack toasts on one spot and read as
      // flicker (the owner counted ~10 a second). Inside its merge window a
      // "+N" pop or an "almost +N" folds into one counting toast ("+30 x3",
      // "almost +50 x2") that re-pops on every hit. Rung toasts ("x4") and every
      // other kind still stand alone. A chatter kind also may not open a NEW
      // toast more often than CHATTER_GAP_MS: a blocked one folds into its live
      // run or is dropped (the score plate already moved).
      const run = runs[kind] || null;
      const m = run ? run.re.exec(body) : null;
      const now = typeof performance === 'object' ? performance.now() : Date.now();
      const liveRun = !!(m && run.el && run.el.parentNode);
      if (liveRun && now - run.at < run.merge) { fold(run, +m[1], now); return; }
      if (run && now - lastChatterAt < CHATTER_GAP_MS) { if (liveRun) fold(run, +m[1], now); return; }
      if (kind === 'bank') body = bankTokens(body);
      if (kind === 'jackpot') hit(gold, 'is-on');
      const t = el(`rh-toast rh-toast--${kind}`, toasts, body);
      t.style.setProperty('--rh-hold', `${TOAST_HOLD[kind]}ms`);
      while (toasts.children.length > 3) toasts.firstChild.remove();
      const kill = later(() => { t.remove(); for (const k of Object.keys(runs)) if (runs[k].el === t) runs[k].el = null; }, TOAST_HOLD[kind] + 60);
      if (run) lastChatterAt = now;
      if (m) Object.assign(run, { el: t, at: now, sum: +m[1], n: 1, kill });
    },
    /** THE MIXER: `state` is cocktail.state(): { live: [slot...], recipe }. Cheap to call every frame. */
    mixer(state) {
      const live = (state && state.live) || [];
      const seen = new Set();
      for (const slot of live) {
        seen.add(slot.category);
        const c = chipFor(slot);
        if (c.charges !== slot.charges || c.depth !== slot.depth) {
          c.n.textContent = slot.charges > 1 ? `x${slot.charges}` : (slot.depth > 1 ? `+${slot.depth - 1}` : '');
          c.pips.forEach((p, i) => p.classList.toggle('is-lit', i < (slot.max > 1 ? Math.max(slot.charges, slot.depth) : 1)));
          if (c.charges >= 0 && (slot.charges > c.charges || slot.depth > c.depth)) hit(c.el, 'is-bump');
          c.charges = slot.charges; c.depth = slot.depth;
        }
        c.el.classList.toggle('is-max', slot.max > 1 && Math.max(slot.charges, slot.depth) >= slot.max);
        c.fill.style.transform = `scaleX(${(slot.frac == null ? 1 : slot.frac).toFixed(3)})`;
      }
      for (const [cat, c] of chips) {
        if (seen.has(cat)) continue;
        chips.delete(cat);
        c.el.classList.add('is-out');
        later(() => c.el.remove(), reduced ? 0 : 260);
      }
      const rc = state && state.recipe;
      const id = rc ? rc.id : null;
      if (id !== lastRecipe) {
        lastRecipe = id;
        mixRecipe.classList.toggle('is-on', !!rc);
        if (rc) { mixRecipeName.textContent = String(rc.name || '').toLowerCase(); hit(mixRecipe, 'is-served'); }
      }
      if (rc) mixRecipeFill.style.transform = `scaleX(${(rc.frac == null ? 1 : rc.frac).toFixed(3)})`;
      mixer.classList.toggle('is-on', live.length > 0 || !!rc);
    },
    /** A flash charge landed or rolled: the white edge blinks, the strobe chip kicks. */
    strobe(charges) {
      hit(strobeEl, 'is-on');
      const c = chips.get('strobe');
      if (c) hit(c.glyph, 'is-kick');
      strobeEl.style.setProperty('--rh-strobe', String(Math.min(1, 0.35 + 0.13 * (charges | 0)).toFixed(2)));
    },
    /** The tint depth (0 = none, 1, 2): the plates, the pill and the item box go pink with the wash. */
    setTint(depth) {
      depth = Math.max(0, Math.min(2, depth | 0));
      chrome.classList.toggle('is-tint-1', depth === 1);
      chrome.classList.toggle('is-tint-2', depth === 2);
    },
    flicker() {
      const lie = Math.floor(scoreShown * (1 + (Math.random() < 0.5 ? -1 : 1) * (0.03 + Math.random() * 0.06)));
      flickUntil = performance.now() + FLICK_MS;
      scoreEl.textContent = fmt(lie);
      hit(scoreEl, 'is-flick');
      later(() => { scoreEl.classList.remove('is-flick'); scoreEl.textContent = fmt(scoreShown); }, FLICK_MS);
    },
    setFraught(v) {
      v = Math.min(1, Math.max(0, v || 0));
      chrome.style.setProperty('--rh-fraught', v.toFixed(3));
      vignette.classList.toggle('is-beat', v > 0.35);
      vignette.style.setProperty('--rh-beat', `${(1.3 - v * 0.6).toFixed(2)}s`);
    },
    /** Shows the Brake. Resolves 'resume' | 'end' when the player picks, or 'resume' on setPaused(false). */
    setPaused(on) {
      if (!on) { settleBrake('resume'); return Promise.resolve('resume'); }
      if (brakeResolve) return Promise.resolve('resume');
      brake.classList.add('is-on');
      return new Promise((res) => { brakeResolve = res; });
    },
    /** 3, 2, 1, go on the HUD. Resolves on GO. onTick(label) fires per beat for sound. */
    countdown(opts = {}) {
      return new Promise((res) => {
        ['3', '2', '1', 'go'].forEach((s, i) => later(() => {
          if (disposed) return;
          count.textContent = s; count.classList.toggle('is-go', s === 'go'); hit(count, 'is-on');
          if (opts.onTick) { try { opts.onTick(s); } catch (e) { /* a listener never breaks the count */ } }
          if (s === 'go') { res(); later(() => { count.classList.remove('is-on'); count.textContent = ''; }, reduced ? 120 : 520); }
        }, reduced ? i * 450 : i * 700));
      });
    },
    showEnd(summary, opts = {}) {
      const s = summary || {};
      end.classList.toggle('is-beside', !!opts.beside);   // the card slides in beside her instead of over her
      settleBrake('resume');
      endTitle.textContent = s.title || 'the tea party';
      pbEl.style.display = s.personalBest ? '' : 'none';
      rows.textContent = '';
      const line = (k, v) => {
        rows.appendChild(Object.assign(document.createElement('dt'), { textContent: k }));
        rows.appendChild(Object.assign(document.createElement('dd'), { textContent: v, className: 'rh-num' }));
      };
      line('score', fmt(s.score || 0));
      line('kept', fmt(s.banked || 0));
      line('best combo', fmt(s.bestCombo || 0));
      line('popped', fmt(s.popped || 0));
      line('laps', fmt(s.laps || 0));
      line('time', fmtDur(s.durationSec || 0));
      if (s.trackName) {   // a charted run: the file, and how many of its words the player met
        line('track', String(s.trackName).replace(/\.[a-z0-9]{2,4}$/i, '').slice(0, 28));
        if (s.countable > 0) line('taken', `${fmt(s.taken || 0)} of ${fmt(s.countable)}`);
      }
      if (endResolve) settleEnd('exit');
      end.classList.add('is-on');
      return new Promise((res) => { endResolve = res; });
    },
    dispose() {
      disposed = true;
      for (const k of Object.keys(runs)) Object.assign(runs[k], { el: null, at: 0, sum: 0, n: 0, kill: 0 });
      settleBrake('resume');
      settleEnd('exit');
      for (const t of timers) clearTimeout(t);
      timers.clear();
      if (raf) cancelAnimationFrame(raf);
      chrome.remove(); brake.remove(); end.remove(); count.remove();
    },
  };
  return hud;
}

// self-check: node --check is the bar; the DOM is required for anything more.
