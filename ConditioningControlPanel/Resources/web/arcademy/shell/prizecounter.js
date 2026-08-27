/* ============================================================================
 * shell/prizecounter.js - THE PRIZE COUNTER.
 *
 * The one room in the school where the money goes back out. Two surfaces and
 * nothing else: the SHELF, which takes tickets and is stacked with the sort of
 * thing a ticket buys, and the CASE, which is glass, which is locked, and which
 * only opens for a token. Every graded class pays tickets; the first S-rank of
 * your day drops exactly one token in the tray. So the shelf is where a good
 * week goes and the case is where a good NIGHT goes, and the room says that by
 * standing them side by side rather than by explaining it.
 *
 * THERE IS NO ART HERE AND THERE WAS NEVER GOING TO BE. The counter, the
 * shelf timber, the glass, the little price flags: all of it is drawn in
 * prizecounter.css out of borders and gradients, the way the campus draws its
 * buildings. That is a feature and not a shortfall - the room has to survive a
 * catalog the host can change without anybody repainting a plate.
 *
 * THE ECHO LAW IS THE WHOLE FILE (trap 1). Pressing Trade does not buy
 * anything. It sends `prize-buy` up the seam, it puts the row to sleep with a
 * "asking the counter" label, and then it waits. Nothing on this page moves -
 * not a balance, not a badge, not a button - until `settle()` is called with
 * the host's own `wallet-result` frame. If that frame never comes, the row
 * wakes back up unchanged and the wallet reads exactly what it read before.
 * The page proposes; the counter disposes. A page that spent its own money
 * would be a page that can be lied to, and a currency you can lie to is not a
 * currency.
 *
 * NARROW CAPS, THE ANNEX'S LAW. Nothing under this line imports the store, the
 * bridge or EMI. Every fact about the player - what they hold, what they can
 * afford, what is already theirs - arrives as a function in `caps`, and every
 * consequence leaves as a callback. That is what makes the room testable in
 * bare node against the DOM double, which is also why it walks its own children
 * BY INDEX (`findCls`) and never touches `querySelector`.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';
import { exitBar, sign as signExit } from './exits.js';

/* ----------------------------------------------------------------------------
 * THE TABLE
 * -------------------------------------------------------------------------- */

/** The two shop surfaces, in the order the room reads left to right. */
export const SURFACES = Object.freeze(['t', 'k']);

/** How long a pressed row waits for the counter before it wakes back up. The
 *  host answers a prize-buy in the same tick it receives it, so six seconds is
 *  not a timeout, it is a promise that the room can never wedge. */
export const ECHO_WAIT_MS = 6000;

/** Every refusal the host can send back, mapped to the line the counter says.
 *  An unknown reason falls through to the same shrug an unknown sku gets, so a
 *  host that grows a new reason tomorrow degrades to a sentence instead of to
 *  an empty toast. */
export const REFUSALS = Object.freeze({
  poor: ['prize_poor', 'Not quite enough on you for that one yet.'],
  owned: ['prize_owned_msg', 'You have that one already.'],
  full: ['prize_full', 'Your pockets are full of those. Use one first.'],
  locked: ['prize_locked_msg', 'That one stays in the case for now.'],
  unknown: ['prize_unknown', 'The counter does not know that one. Odd.'],
});

/** The glyph each sku wears on its little drawn box. Text, not art: a sku the
 *  catalog grows before this table does gets the plain parcel and still reads
 *  as a thing on a shelf. */
export const GLYPHS = Object.freeze({
  id_frame_gold: '▧',
  id_frame_navy: '▨',
  confetti_stamp: '✶',
  late_slip: '✉',
  honors_lever: '⌇',
  free_swim_key: '⚿',
  de_5x5: '▦',
  jukebox: '♪',
});

/** The token price glyph, straight out of the contract: ◉1 / ◉2 / ◉3. */
export const TOKEN_MARK = '◉';

/* ----------------------------------------------------------------------------
 * PLUMBING
 * -------------------------------------------------------------------------- */

/* ONE AUDIO DOOR (trap 18): shell/audio.js owns the only audio node on the
 * page, so every sound this room makes is a REQUEST on `document`. Only cue
 * names that already exist in the SOUNDS table are ever fired (trap 115) - an
 * invented name degrades to a blip, which is a worse sound than the one you
 * meant and a much worse bug than no sound at all. */
function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

function attr(node, name, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(name, value); }
  catch (e) { /* the DOM double may not carry attributes - never fatal */ }
}

function addCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.add === 'function') node.classList.add(cls); }
  catch (e) { /* noop */ }
}

function dropCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.remove === 'function') node.classList.remove(cls); }
  catch (e) { /* noop */ }
}

function urlFor(rel, fallback) {
  try { return new URL(rel, import.meta.url).href; }
  catch (e) { return fallback; }
}

/** prizecounter.css, linked once and lazily - recordsroom.js's pattern. */
function ensureSheet(doc, log) {
  try {
    if (!doc || typeof doc.createElement !== 'function') return null;
    const had = typeof doc.getElementById === 'function' ? doc.getElementById('pc-styles') : null;
    if (had) return had;
    const link = doc.createElement('link');
    link.id = 'pc-styles';
    link.rel = 'stylesheet';
    link.href = urlFor('./prizecounter.css', 'shell/prizecounter.css');
    const head = doc.head || doc.body || null;
    if (head && typeof head.appendChild === 'function') head.appendChild(link);
    return link;
  } catch (e) {
    if (typeof log === 'function') log('prize counter stylesheet failed to link');
    return null;
  }
}

function htmlReduced() {
  try {
    const de = (typeof document !== 'undefined') ? document.documentElement : null;
    if (de && de.classList && typeof de.classList.contains === 'function'
      && de.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* noop */ }
  return false;
}

/** Walk a subtree for the first node wearing `cls`, BY INDEX and never with a
 *  selector - `querySelector` does not exist in the node double, and a counter
 *  that can only find its own shelf in a browser is a counter with no suite. */
export function findCls(node, cls) {
  if (!node) return null;
  try {
    if (node.classList && typeof node.classList.contains === 'function'
      && node.classList.contains(cls)) return node;
  } catch (e) { /* noop */ }
  const kids = node.children;
  if (!kids) return null;
  for (let i = 0; i < kids.length; i += 1) {
    const hit = findCls(kids[i], cls);
    if (hit) return hit;
  }
  return null;
}

/** Every node under `node` wearing `cls`, in document order, by index. */
export function findAllCls(node, cls, out) {
  const acc = out || [];
  if (!node) return acc;
  try {
    if (node.classList && typeof node.classList.contains === 'function'
      && node.classList.contains(cls)) acc.push(node);
  } catch (e) { /* noop */ }
  const kids = node.children;
  if (!kids) return acc;
  for (let i = 0; i < kids.length; i += 1) findAllCls(kids[i], cls, acc);
  return acc;
}

/* ----------------------------------------------------------------------------
 * THE INVENTORY READER
 *
 * The host is allowed to write an inventory row three different ways over the
 * life of this feature - a bare count, a `{n, at}` bag, a bare `true` for an
 * unlock - and the room must read all three without ever asking which one it
 * got. A cosmetic you own once is `1`; a consumable you hold two of is `2`.
 * -------------------------------------------------------------------------- */

/** How many of `sku` the player is holding, 0 when none. */
export function heldCount(inv, sku) {
  if (!inv || typeof inv !== 'object') return 0;
  const row = inv[sku];
  if (row == null || row === false) return 0;
  if (row === true) return 1;
  if (typeof row === 'number') return Number.isFinite(row) && row > 0 ? Math.floor(row) : 0;
  if (typeof row === 'object') {
    const n = Number(row.n);
    if (Number.isFinite(n)) return n > 0 ? Math.floor(n) : 0;
    return 1;                      // a bag with no count is still a thing owned
  }
  return 0;
}

/* ----------------------------------------------------------------------------
 * THE ROOM
 * -------------------------------------------------------------------------- */

/**
 * createPrizeCounter(caps) -> the handle, or null with no DOM.
 *
 * @param {Object} caps
 *  mount     - where the counter's root goes (the shell's screen).
 *  t / log / lite / reduced
 *  catalog   - () -> [{sku, cur, cost, kind, nameKey, nameEn, blurbKey, blurbEn, locked}]
 *              exactly as the host projected it through init.economy. The page
 *              never invents a row and never prices one.
 *  balance   - () -> {t, k}
 *  inv       - () -> the wallet's `inv` bag (see heldCount above).
 *  unlocks   - () -> {extra, honors} (only read for the badge on honors_lever).
 *  stackMax  - optional (sku) -> how many of a consumable may be held. Purely a
 *              display cap for the "2 of 3" badge; the host is the one that
 *              refuses a third with reason "full".
 *  payday    - optional () -> {gameKey, mult} for tonight's hot room, or null.
 *  gameName  - optional (key) -> display name for that line.
 *  onBuy     - (sku) -> the shell's send of `prize-buy`. Fire and forget: the
 *              answer arrives at settle(), or it never arrives and nothing here
 *              has changed.
 *  onBack    - the way out.
 * @returns {?Object} the handle
 */
export function createPrizeCounter(caps) {
  const c = caps || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;

  const t = typeof c.t === 'function' ? c.t : lexT;
  const log = typeof c.log === 'function' ? c.log : function () {};
  const reduced = () => !!c.reduced || htmlReduced();

  ensureSheet(doc, log);

  let dead = false;
  /** The sku whose row is asleep waiting for an echo, or null. ONE at a time:
   *  a counter clerk serves one customer, and two in-flight buys is two ways to
   *  read the same balance. */
  let pending = null;
  let pendingTimer = null;
  /** The last line the counter said, kept as a test seam. */
  let note = '';

  /* The mirrors. Seeded from caps, and thereafter moved ONLY by settle(). */
  let wallet = readBalance();
  let inv = readInv();
  let unlocks = readUnlocks();

  const rows = new Map();          // sku -> the item element currently painted

  function readBalance() {
    try {
      const b = (typeof c.balance === 'function') ? c.balance() : null;
      const tt = Number(b && b.t); const kk = Number(b && b.k);
      return { t: Number.isFinite(tt) ? tt : 0, k: Number.isFinite(kk) ? kk : 0 };
    } catch (e) { return { t: 0, k: 0 }; }
  }

  function readInv() {
    try {
      const v = (typeof c.inv === 'function') ? c.inv() : null;
      return (v && typeof v === 'object') ? v : {};
    } catch (e) { return {}; }
  }

  function readUnlocks() {
    try {
      const v = (typeof c.unlocks === 'function') ? c.unlocks() : null;
      return (v && typeof v === 'object') ? v : {};
    } catch (e) { return {}; }
  }

  function readCatalog() {
    try {
      const v = (typeof c.catalog === 'function') ? c.catalog() : null;
      return Array.isArray(v) ? v.filter((r) => r && r.sku) : [];
    } catch (e) { log('prize catalog read threw'); return []; }
  }

  function stackMaxFor(sku) {
    try {
      const n = Number((typeof c.stackMax === 'function') ? c.stackMax(sku) : NaN);
      return Number.isFinite(n) && n > 0 ? Math.floor(n) : 0;
    } catch (e) { return 0; }
  }

  /* --------------------------------------------------------------- the root */

  const root = el('div', 'pc-root');
  if (c.lite) addCls(root, 'is-lite');
  if (reduced()) addCls(root, 'is-reduced');

  /* ------------------------------------------------------------ the chrome */

  /**
   * A currency chip. Tickets get a drawn stub, tokens get the mark, and both
   * carry the number FIRST because the number is the thing a player came to
   * read. `cur` is 't' or 'k'.
   */
  function chip(cur) {
    const box = el('span', 'pc-chip pc-chip-' + cur);
    const ico = el('i', cur === 'k' ? 'arc-tok' : 'arc-tick', cur === 'k' ? TOKEN_MARK : null);
    attr(ico, 'aria-hidden', 'true');
    box.appendChild(ico);
    box.appendChild(el('b', 'pc-chip-n', String(cur === 'k' ? wallet.k : wallet.t)));
    box.appendChild(el('span', 'pc-chip-lbl', cur === 'k'
      ? t('wallet_tokens', 'Tokens')
      : t('wallet_tickets', 'Tickets')));
    return box;
  }

  /** The price flag on a row. Tickets read as a plain count beside a stub;
   *  tokens read as the contract's ◉N, which is short enough to sit on glass. */
  function priceTag(item) {
    const flag = el('span', 'pc-price pc-price-' + (item.cur === 'k' ? 'k' : 't'));
    const cost = Math.max(0, Math.round(Number(item.cost) || 0));
    if (item.cur === 'k') {
      flag.appendChild(el('b', 'pc-price-n', TOKEN_MARK + String(cost)));
    } else {
      const stub = el('i', 'arc-tick');
      attr(stub, 'aria-hidden', 'true');
      flag.appendChild(stub);
      flag.appendChild(el('b', 'pc-price-n', String(cost)));
    }
    return flag;
  }

  /* -------------------------------------------------------- the note strip */

  const noteStrip = el('p', 'pc-note');
  attr(noteStrip, 'aria-live', 'polite');

  function say(line) {
    note = String(line == null ? '' : line);
    try { noteStrip.textContent = note; } catch (e) { /* noop */ }
    if (note) addCls(noteStrip, 'is-on'); else dropCls(noteStrip, 'is-on');
  }

  /* ------------------------------------------------------------- one shelf */

  /**
   * Is this row already the player's? An unlock or a cosmetic is owned once and
   * then it is furniture; a consumable is never "owned", it is HELD, and it can
   * be held again tomorrow.
   */
  function isOwned(item) {
    if (item.kind === 'consumable') return false;
    if (heldCount(inv, item.sku) > 0) return true;
    // The two lever unlocks live in `unlocks` as well as in `inv`, because the
    // lever reads them there. Either witness is proof.
    if (item.sku === 'honors_lever' && unlocks.honors === true) return true;
    if (item.sku === 'free_swim_key' && unlocks.freeSwim === true) return true;
    return false;
  }

  function itemNode(item) {
    const owned = isOwned(item);
    const held = item.kind === 'consumable' ? heldCount(inv, item.sku) : 0;
    const cap = item.kind === 'consumable' ? stackMaxFor(item.sku) : 0;
    const locked = item.locked === true;
    const cost = Math.max(0, Math.round(Number(item.cost) || 0));
    const purse = item.cur === 'k' ? wallet.k : wallet.t;
    const poor = !locked && !owned && purse < cost;
    const full = !!(cap && held >= cap);

    let cls = 'pc-item pc-item-' + (item.cur === 'k' ? 'k' : 't');
    if (owned) cls += ' is-owned';
    if (locked) cls += ' is-locked';
    if (poor) cls += ' is-poor';
    if (full) cls += ' is-full';
    if (pending === item.sku) cls += ' is-busy';
    const box = el('article', cls);
    attr(box, 'data-sku', item.sku);

    /* The drawn box on the shelf. Decoration, no pointer events, one glyph. */
    const art = el('div', 'pc-art');
    attr(art, 'aria-hidden', 'true');
    art.appendChild(el('span', 'pc-glyph', GLYPHS[item.sku] || '▤'));
    box.appendChild(art);

    const body = el('div', 'pc-body');
    body.appendChild(el('h4', 'pc-name', t(item.nameKey || ('prize_name_' + item.sku),
      item.nameEn || item.sku)));
    const blurb = item.blurbEn || item.blurbKey
      ? t(item.blurbKey || ('prize_blurb_' + item.sku), item.blurbEn || '')
      : '';
    if (blurb) body.appendChild(el('p', 'pc-blurb', blurb));

    const foot = el('div', 'pc-foot');
    foot.appendChild(priceTag(item));

    if (owned) {
      foot.appendChild(el('span', 'pc-badge pc-badge-owned', t('prize_owned', 'Yours')));
    } else if (locked) {
      foot.appendChild(el('span', 'pc-badge pc-badge-soon', t('prize_soon', 'Arriving soon')));
    } else {
      if (held > 0) {
        foot.appendChild(el('span', 'pc-badge pc-badge-held',
          t('prize_held', 'Holding') + ' ' + String(held) + (cap ? '/' + String(cap) : '')));
      }
      const btn = el('button', 'btn primary pc-buy',
        pending === item.sku ? t('prize_wait', 'Asking the counter') : t('prize_buy', 'Trade'));
      btn.type = 'button';
      if (pending) attr(btn, 'disabled', 'disabled');
      btn.addEventListener('click', () => propose(item));
      foot.appendChild(btn);
    }

    body.appendChild(foot);
    box.appendChild(body);
    rows.set(item.sku, box);
    return box;
  }

  function surface(cur, items) {
    const sect = el('section', 'pc-sect pc-sect-' + cur);
    const head = el('div', 'pc-sect-head');
    head.appendChild(el('h3', 'pc-sect-title', cur === 'k'
      ? t('prize_case', 'Token Case')
      : t('prize_shelf', 'Ticket Shelf')));
    head.appendChild(el('p', 'pc-sect-hint', cur === 'k'
      ? t('prize_case_hint', 'Tokens only. Your first S of the day drops one in the tray.')
      : t('prize_shelf_hint', 'Every graded class pays tickets. This is where they go.')));
    sect.appendChild(head);

    const goods = el('div', 'pc-goods');
    for (const item of items) goods.appendChild(itemNode(item));
    sect.appendChild(goods);
    return sect;
  }

  /* ---------------------------------------------------------- the buy flow */

  /**
   * THE PROPOSAL. Everything this does is cosmetic and reversible: it names the
   * sku it is waiting on, repaints so the row reads asleep, and hands the sku
   * to the shell. It does not touch `wallet`, it does not touch `inv`, and if
   * the answer never comes the watchdog puts the room back exactly as it was.
   */
  function propose(item) {
    if (dead || pending) return;
    if (item.locked === true) { say(t('prize_locked_msg', 'That one stays in the case for now.')); sfx('bump', 0.32); return; }
    pending = item.sku;
    say(t('prize_wait', 'Asking the counter'));
    sfx('commit', 0.4);
    render();
    pendingTimer = setTimeout(() => {
      if (dead || pending !== item.sku) return;
      pending = null; pendingTimer = null;
      // No echo, so no purchase: the room wakes up owing exactly what it owed.
      say(t('prize_quiet', 'The counter went quiet on that one. Try again in a moment.'));
      render();
    }, ECHO_WAIT_MS);
    try { if (typeof c.onBuy === 'function') c.onBuy(item.sku); }
    catch (e) { log('prize buy send threw: ' + ((e && e.message) || e)); }
  }

  /**
   * THE ECHO. The host's `wallet-result` frame, and the ONLY thing in this file
   * allowed to move a balance or a badge.
   * @param {Object} res {ok, sku, reason, wallet:{t,k}, inv, unlocks}
   * @returns {boolean} true when the frame was for a row this room was waiting
   *          on (the shell does not need it, the suite does)
   */
  function settle(res) {
    if (dead) return false;
    const r = res || {};
    const was = pending;
    if (pendingTimer) { try { clearTimeout(pendingTimer); } catch (e) { /* noop */ } pendingTimer = null; }
    pending = null;

    /* The host's numbers win outright. A missing bag means "unchanged", never
     * "empty" - a refusal frame is allowed to carry only the reason. */
    if (r.wallet && typeof r.wallet === 'object') {
      const tt = Number(r.wallet.t); const kk = Number(r.wallet.k);
      wallet = {
        t: Number.isFinite(tt) ? tt : wallet.t,
        k: Number.isFinite(kk) ? kk : wallet.k,
      };
    }
    if (r.inv && typeof r.inv === 'object') inv = r.inv;
    if (r.unlocks && typeof r.unlocks === 'object') unlocks = r.unlocks;

    if (r.ok === true) {
      say(t('prize_bought', 'Wrapped up and yours.'));
      sfx('chime', 0.55);
    } else {
      const row = REFUSALS[String(r.reason || '')] || REFUSALS.unknown;
      say(t(row[0], row[1]));
      sfx('bump', 0.34);
    }
    render();
    return !!was && (!r.sku || r.sku === was);
  }

  /* ------------------------------------------------------------ the render */

  function render() {
    if (dead) return root;
    rows.clear();
    try { root.textContent = ''; } catch (e) { /* noop */ }

    /* THE MARQUEE. The room's own sign, and the wallet reading under it - a
     * shop tells you what you can spend before it tells you what it sells. */
    const marquee = el('div', 'pc-marquee');
    marquee.appendChild(el('h2', 'pc-title', t('prize_counter_title', 'Prize Counter')));
    marquee.appendChild(el('p', 'pc-sub', t('prize_counter_sub',
      'Tickets on the shelf, tokens in the case')));
    const purse = el('div', 'pc-wallet');
    const label = el('span', 'pc-wallet-lbl', t('prize_you_have', 'On you'));
    purse.appendChild(label);
    purse.appendChild(chip('t'));
    purse.appendChild(chip('k'));
    marquee.appendChild(purse);
    root.appendChild(marquee);

    /* TONIGHT'S HOT ROOM. Projected by the host, seeded per UTC day, and shown
     * because a payday nobody knows about is a payday nobody plays. */
    const pd = paydayLine();
    if (pd) root.appendChild(pd);

    root.appendChild(noteStrip);

    const catalog = readCatalog();
    if (!catalog.length) {
      root.appendChild(el('p', 'arc-note pc-bare', t('prize_empty',
        'Shelf is bare tonight. Come back when the truck has been.')));
    } else {
      for (const cur of SURFACES) {
        const items = catalog.filter((r) => (r.cur === 'k' ? 'k' : 't') === cur);
        if (items.length) root.appendChild(surface(cur, items));
      }
    }

    /* THE WAY OUT, and it is sticky (trap 46): the catalog is a scroller and an
     * exit that scrolls away is not an exit. */
    const back = el('button', 'btn primary', t('back', 'Back'));
    back.type = 'button';
    back.addEventListener('click', () => {
      try { if (typeof c.onBack === 'function') c.onBack(); }
      catch (e) { log('prize back threw: ' + ((e && e.message) || e)); }
    });
    signExit(back, { dir: 'back' });
    root.appendChild(exitBar([back]));
    return root;
  }

  function paydayLine() {
    let pd = null;
    try { pd = (typeof c.payday === 'function') ? c.payday() : null; } catch (e) { pd = null; }
    if (!pd || !pd.gameKey) return null;
    const mult = Number(pd.mult) || 0;
    if (mult <= 1) return null;
    let name = String(pd.gameKey);
    try { if (typeof c.gameName === 'function') name = c.gameName(pd.gameKey) || name; }
    catch (e) { /* the key is a fine last resort */ }
    const line = el('div', 'pc-payday');
    line.appendChild(el('span', 'pc-payday-lbl', t('prize_payday_label', 'Hot room tonight')));
    line.appendChild(el('b', 'pc-payday-name', name));
    line.appendChild(el('span', 'pc-payday-mult', mult >= 5
      ? t('prize_payday_5', 'is paying five times over')
      : t('prize_payday_2', 'is paying double')));
    return line;
  }

  /* ------------------------------------------------------------- the mount */

  render();
  try {
    if (c.mount && typeof c.mount.appendChild === 'function') c.mount.appendChild(root);
  } catch (e) { log('prize counter failed to mount'); }

  function destroy() {
    if (dead) return;
    dead = true;
    if (pendingTimer) { try { clearTimeout(pendingTimer); } catch (e) { /* noop */ } pendingTimer = null; }
    pending = null;
    rows.clear();
    try { root.remove(); } catch (e) { /* noop */ }
  }

  return {
    root,
    render,
    settle,
    /** Re-read every cap and repaint. The shell calls this when the wallet moved
     *  for a reason that was not a purchase (a payout, say). */
    refresh() {
      wallet = readBalance();
      inv = readInv();
      unlocks = readUnlocks();
      render();
    },
    /** The sku this room is waiting on, or null (test seam). */
    get pending() { return pending; },
    /** The last line the counter said (test seam). */
    get note() { return note; },
    /** What the room believes it can spend (test seam). Never authored here. */
    get balance() { return { t: wallet.t, k: wallet.k }; },
    /** One painted row by sku, or null (test seam). */
    rowFor(sku) { return rows.get(sku) || null; },
    destroy,
  };
}

export default createPrizeCounter;
