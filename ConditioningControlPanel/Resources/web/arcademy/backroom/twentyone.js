/* ============================================================================
 * backroom/twentyone.js - THE FIRST TABLE.
 *
 * Twenty-one is all the way down. Over it is too deep. E.M.I. deals, the verbs
 * are DEEPER, HOLD and DOUBLE DOWN, and the four suits are four spirals drawn
 * on the spot rather than four image files nobody would ever swap.
 *
 * WHAT IT IS AND IS NOT. A renderer with four buttons on it. It does not
 * shuffle, it does not decide and it never checks whether you went bust: the
 * server draws the deck from the day's seed, resolves the hand and answers with
 * the whole table (LEDGER TRUTH). The one number computed here is the PRINTED
 * TOTAL of cards already dealt, which is arithmetic on a frame, not a ruling.
 *
 * FOUR LAWS IT IS WRITTEN AROUND.
 *  - EXITS SACRED (Law VI). Nothing here binds Escape and nothing here is a
 *    modal. The shell's ladder folds the cabinet first, and leaving mid-hand is
 *    legal: the server resolves an abandoned hand as a HOLD on the next status
 *    call, so walking away is standing, never forfeiting.
 *  - ONE ANSWER AT A TIME. Every verb locks all four buttons until the frame
 *    comes back. A double press at a blackjack table is a second stake, and the
 *    idempotency key only saves you if the second press carried the same one.
 *  - LOSSES GET SYMPATHY. The dealer is warm on a bust and warmer on a bad
 *    beat. No red, no shake, no "unlucky". The house took the chips; it does
 *    not also take a swing.
 *  - REDUCED MOTION CROSSFADES. No card flight, no deal spring, no flip. The
 *    card is simply there, which is the information, delivered instantly.
 * ==========================================================================*/

import { fmtChips } from './kit/chips.js';
import { fill } from './lex.js';

/** The table's own strings. Mirrored key for key into NeutralLexicon; every
 *  value under 96 characters so a mod can re-voice all of them (trap 26). */
export const T1_LEX = Object.freeze({
  bk_t1_title:     'Twenty-One',
  bk_t1_sub:       'twenty-one is all the way down. over it is too deep.',
  bk_t1_dealer:    'dealer',
  bk_t1_you:       'you',
  bk_t1_shows:     'shows',
  /* {0} the stake in chips */
  bk_t1_deal:      'deal, {0} chips',
  bk_t1_hit:       'deeper',
  bk_t1_stand:     'hold',
  bk_t1_double:    'double down',
  bk_t1_back:      'back to the floor',
  /* {0} what a blackjack pays, {1} the number the dealer holds on */
  bk_t1_odds:      'Blackjack pays {0}. Dealer holds on {1}.',
  bk_t1_pays:      '3 to 2',
  /* {0} a hand total counting an ace high */
  bk_t1_soft:      'soft {0}',
  bk_t1_hole:      'face down',
  /* {0} rank, {1} suit */
  bk_t1_card:      '{0} of {1}',
  bk_t1_suit_0:    'coils',
  bk_t1_suit_1:    'curls',
  bk_t1_suit_2:    'deeps',
  bk_t1_suit_3:    'eyes',

  /* ---- what the felt says, one line at a time ---- */
  bk_t1_idle:      'Chips down whenever you are ready.',
  bk_t1_working:   'The shoe is moving.',
  bk_t1_playing:   'Your move.',
  bk_t1_bust:      'Too deep. That one is gone.',
  /* {0} chips paid back */
  bk_t1_won:       'Yours. {0} chips.',
  bk_t1_lost:      'The floor keeps that one.',
  bk_t1_push:      'A push. Your stake comes home untouched.',
  /* {0} chips paid back */
  bk_t1_blackjack: 'Twenty-one on the deal. {0} chips.',
  bk_t1_insured:   'Insurance covered that one. Nothing lost.',

  /* ---- refusals, every one of them warm ---- */
  bk_t1_poor:      'Not enough chips for a hand. The cage is right outside.',
  bk_t1_open:      'There is a hand on the felt already. Play it out.',
  bk_t1_nohand:    'Nothing on the felt yet. Chips down first.',
  bk_t1_bad:       'The table would not take that one. Nothing moved.',
  bk_t1_busy:      'One at a time, love. She is still dealing.',
  bk_t1_offline:   'The line to the table is down. Nothing was staked.',
  bk_t1_locked:    'The bank does not serve this account yet. Nothing was charged.',
  bk_t1_dark:      'The table is closed. Come back when the line is up.',

  bk_t1_aria_you:  'your hand',
  bk_t1_aria_dlr:  'the dealer hand',
});

/** The stake floor. The live number arrives on `config().stakes.twentyone`. */
const STAKE_FLOOR = 25;

/** The four suits, as recipes rather than as art. Turns and direction are the
 *  whole difference between them, which is enough to tell four spirals apart at
 *  eighteen pixels, and two of them run hot so a hand reads like a hand. */
const SUITS = Object.freeze([
  { turns: 2.6, dir: 1, hot: true, dot: false },
  { turns: 1.7, dir: -1, hot: false, dot: false },
  { turns: 3.4, dir: 1, hot: false, dot: false },
  { turns: 2.1, dir: -1, hot: true, dot: true },
]);

const SVG_NS = 'http://www.w3.org/2000/svg';

/** The table's skin, linked on first open and never again, id-guarded exactly
 *  like the floor's own sheet. A sheet that will not load is an ugly table. */
function ensureSheet(log) {
  try {
    if (document.getElementById('arc-bk21-css')) return;
    const link = document.createElement('link');
    link.id = 'arc-bk21-css';
    link.rel = 'stylesheet';
    link.href = new URL('./twentyone.css', import.meta.url).href;
    document.head.appendChild(link);
  } catch (e) { log('twentyone sheet failed'); }
}

/** The table's own resolver, and it needs one: the floor's `bkT` is closed over
 *  BK_LEX and answers a `bk_t1_` key with the KEY, so a machine that leaned on
 *  it would print `bk_t1_hit` on a button. Same house chain either way, a mod's
 *  row first and this table's English underneath, with lex.js's own `fill` on
 *  the slots so a translator can move them inside the sentence. */
function makeT(k, c) {
  const shell = (typeof c.t === 'function') ? c.t : null;     // t(key, fallback)
  const floor = (typeof k.t === 'function') ? k.t : null;     // bkT(key, args)
  return function t(key, args) {
    const en = (T1_LEX[key] == null) ? key : T1_LEX[key];
    let raw = en;
    if (shell) { try { const got = shell(key, en); if (got) raw = String(got); } catch (e) { /* the floor stands */ } }
    else if (floor) { try { const got = floor(key); if (got && got !== key) raw = String(got); } catch (e) { /* noop */ } }
    return args ? fill(raw, args) : raw;
  };
}

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** A spiral, from the middle outwards. One path per card, which is what keeps
 *  a hand of five cheap enough for a phone. */
function spiralPath(spec, size) {
  const c = size / 2;
  const max = c - 1.2;
  const steps = Math.max(12, Math.round(spec.turns * 20));
  let d = '';
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const a = t * spec.turns * Math.PI * 2 * spec.dir;
    const r = max * t;
    d += (i ? 'L' : 'M') + (c + Math.cos(a) * r).toFixed(2) + ' ' + (c + Math.sin(a) * r).toFixed(2);
  }
  return d;
}

/** One suit glyph. Decorative to a screen reader: the card's own aria-label
 *  already says "king of coils", and a second reading of the suit is noise. */
function suitGlyph(s, size) {
  const spec = SUITS[s] || SUITS[0];
  const px = Number(size) || 22;
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', '0 0 ' + px + ' ' + px);
  svg.setAttribute('width', String(px));
  svg.setAttribute('height', String(px));
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('class', 'bk21-suit' + (spec.hot ? ' bk21-suit-hot' : ''));
  const path = document.createElementNS(SVG_NS, 'path');
  path.setAttribute('d', spiralPath(spec, px));
  path.setAttribute('fill', 'none');
  path.setAttribute('stroke', 'currentColor');
  path.setAttribute('stroke-width', '1.5');
  path.setAttribute('stroke-linecap', 'round');
  svg.appendChild(path);
  if (spec.dot) {
    const dot = document.createElementNS(SVG_NS, 'circle');
    dot.setAttribute('cx', String(px / 2));
    dot.setAttribute('cy', String(px / 2));
    dot.setAttribute('r', '1.6');
    dot.setAttribute('fill', 'currentColor');
    svg.appendChild(dot);
  }
  return svg;
}

/** A rank's worth. An ace is eleven here and the total below walks it back
 *  down, which is the only place in this file that resembles a rule. */
function rankValue(r) {
  const v = String(r == null ? '' : r).toUpperCase();
  if (v === 'A') return 11;
  if (v === 'K' || v === 'Q' || v === 'J' || v === 'T') return 10;
  const n = parseInt(v, 10);
  return n > 0 ? Math.min(10, n) : 10;
}

/** `{sum, soft}` for a list of cards. Soft means an ace is still counting
 *  eleven, which is the one thing a printed total has to say out loud. */
function handTotal(cards) {
  let sum = 0;
  let aces = 0;
  for (const c of (Array.isArray(cards) ? cards : [])) {
    const v = rankValue(c && c.r);
    if (v === 11) aces += 1;
    sum += v;
  }
  while (sum > 21 && aces > 0) { sum -= 10; aces -= 1; }
  return { sum, soft: aces > 0 && sum <= 21 };
}

/**
 * mount(root, kit, ctx) -> { unmount }
 * `kit` is the floor's shared surface (backroom/index.js). `ctx` is the shell's
 * own and EVERY field of it is optional: a fixture and a rig hand down a ctx
 * with no emi and no exits, and the table opens anyway.
 */
function mountTable(root, kit, ctx) {
  const k = kit || {};
  const c = ctx || {};
  const t = makeT(k, c);
  const say = (typeof k.log === 'function') ? k.log : () => {};
  const sfx = (typeof k.sfx === 'function') ? k.sfx : () => {};
  const reduced = !!k.reduced;
  const lite = !!k.lite;
  ensureSheet(say);

  let dead = false;
  let busy = false;
  let hand = null;         // the server's last `result`, or null for a clear felt
  let visit = null;        // a borrowed EMI, if the shell lent us one
  const timers = new Set();

  const later = (ms, fn) => {
    const h = setTimeout(() => { timers.delete(h); if (!dead) fn(); }, ms);
    timers.add(h);
    return h;
  };

  /* ------------------------------------------------------------ the table */
  const wrap = el('div', 'bk21' + (lite ? ' bk21-lite' : ''));
  wrap.setAttribute('role', 'region');
  wrap.setAttribute('aria-label', t('bk_t1_title'));

  const hud = el('div', 'bk21-hud');
  hud.appendChild(el('h3', 'bk21-name', t('bk_t1_title')));
  hud.appendChild(el('span', 'bk21-sub', t('bk_t1_sub')));
  hud.appendChild(el('span', 'bk21-spacer'));
  const oddsEl = el('span', 'bk21-odds', t('bk_t1_odds', [t('bk_t1_pays'), '17']));
  hud.appendChild(oddsEl);
  wrap.appendChild(hud);

  const felt = el('div', 'bk21-felt');
  wrap.appendChild(felt);

  /* THE DEALER. A drawn CRT is the floor, not the fallback: she is a screen on
   * a stand at this table whether or not the shell can lend us the real one. */
  const dealer = el('div', 'bk21-dealer');
  const crt = el('div', 'bk21-crt');
  const eyes = el('div', 'bk21-eyes');
  eyes.appendChild(el('i'));
  eyes.appendChild(el('i'));
  crt.appendChild(eyes);
  crt.appendChild(el('div', 'bk21-mouth'));
  crt.setAttribute('aria-hidden', 'true');
  const bubble = el('div', 'bk21-say', t('bk_t1_idle'));
  bubble.setAttribute('role', 'status');
  dealer.appendChild(crt);
  dealer.appendChild(bubble);
  felt.appendChild(dealer);

  const rows = {};
  for (const side of ['dealer', 'player']) {
    const row = el('div', 'bk21-row bk21-row-' + side);
    const cap = el('div', 'bk21-cap');
    const label = el('b', null, t(side === 'dealer' ? 'bk_t1_dealer' : 'bk_t1_you'));
    const count = el('span', 'bk21-count', '');
    cap.appendChild(label);
    cap.appendChild(count);
    const cards = el('div', 'bk21-hand');
    cards.setAttribute('role', 'list');
    cards.setAttribute('aria-label', t(side === 'dealer' ? 'bk_t1_aria_dlr' : 'bk_t1_aria_you'));
    row.appendChild(cap);
    row.appendChild(cards);
    felt.appendChild(row);
    rows[side] = { cards, count };
  }

  /* --------------------------------------------------------------- the verbs */
  const verbs = el('div', 'bk21-verbs');
  const btnDeal = el('button', 'bk21-btn bk21-gold', t('bk_t1_deal', [fmtChips(STAKE_FLOOR)]));
  const btnHit = el('button', 'bk21-btn', t('bk_t1_hit'));
  const btnStand = el('button', 'bk21-btn bk21-ghost', t('bk_t1_stand'));
  const btnDouble = el('button', 'bk21-btn bk21-gold', t('bk_t1_double'));
  const btnBack = el('button', 'bk21-btn bk21-ghost bk21-back', t('bk_t1_back'));
  // `data-act` is the fixture's only handle on this table: the probe presses the
  // same buttons a player does, so a test can never drift from the real verb.
  const ACTS = ['deal', 'hit', 'stand', 'double', 'back'];
  [btnDeal, btnHit, btnStand, btnDouble, btnBack].forEach((b, i) => {
    b.type = 'button';
    b.setAttribute('data-act', ACTS[i]);
    verbs.appendChild(b);
  });
  wrap.appendChild(verbs);

  btnDeal.addEventListener('click', () => act('deal'));
  btnHit.addEventListener('click', () => act('hit'));
  btnStand.addEventListener('click', () => act('stand'));
  btnDouble.addEventListener('click', () => act('double'));
  btnBack.addEventListener('click', () => { if (typeof k.toFloor === 'function') k.toFloor(); });
  try {
    if (c.exits && typeof c.exits.sign === 'function') c.exits.sign(btnBack, { dir: 'back', quiet: true });
  } catch (e) { /* an undressed button still leaves */ }

  root.appendChild(wrap);

  /* ------------------------------------------------------------ the numbers */
  /** The live stake, off the floor's config frame, with the contract's floor
   *  underneath it. The page never publishes a stake of its own. */
  function stake() {
    try {
      const cfg = (typeof k.config === 'function') ? k.config() : null;
      const v = cfg && cfg.stakes && Number(cfg.stakes.twentyone);
      if (v > 0) return Math.round(v);
    } catch (e) { /* the floor has not answered yet */ }
    return STAKE_FLOOR;
  }

  /** The odds line, and it is the REAL one (INPUT HONESTY): built from the
   *  server's paytable when there is one, and from the contract's own numbers
   *  when there is not, which are the same numbers. */
  function paintOdds() {
    let pays = t('bk_t1_pays');
    let holds = '17';
    try {
      const cfg = (typeof k.config === 'function') ? k.config() : null;
      const pt = cfg && cfg.paytables && cfg.paytables.twentyone;
      if (pt && typeof pt === 'object') {
        if (pt.blackjack) pays = String(pt.blackjack).replace(':', ' to ');
        if (Number(pt.stand) > 0) holds = String(Math.round(Number(pt.stand)));
      }
    } catch (e) { /* the published floor stands */ }
    oddsEl.textContent = t('bk_t1_odds', [pays, holds]);
    btnDeal.textContent = t('bk_t1_deal', [fmtChips(stake())]);
  }

  /* ------------------------------------------------------------- the cards */
  function cardNode(card, fresh) {
    const r = String((card && card.r) || '?').toUpperCase();
    const s = Math.max(0, Math.min(3, Math.round(Number(card && card.s) || 0)));
    const node = el('div', 'bk21-card' + (SUITS[s].hot ? ' bk21-hot' : ''));
    node.setAttribute('role', 'listitem');
    node.setAttribute('aria-label', t('bk_t1_card', [r, t('bk_t1_suit_' + s)]));
    node.appendChild(el('b', 'bk21-r', r));
    node.appendChild(suitGlyph(s, 26));
    node.appendChild(el('b', 'bk21-r bk21-r-low', r));
    // The deal spring is DECORATION and says so: still mode gets the card, it
    // simply gets it without the flight (trap 92, the freeze cannot reach JS).
    if (fresh && !reduced) node.classList.add('bk21-in');
    return node;
  }

  function backNode() {
    const node = el('div', 'bk21-card bk21-back');
    node.setAttribute('role', 'listitem');
    node.setAttribute('aria-label', t('bk_t1_hole'));
    return node;
  }

  /** Paint one side. `hidden` appends the hole card: the server sends the
   *  dealer's VISIBLE cards and a flag, never the card it is still holding, so
   *  the face-down rectangle is the page's own furniture and the printed count
   *  is only ever what is on the felt face up. */
  function paintHand(side, cards, hidden, fresh) {
    const box = rows[side].cards;
    const had = box.childElementCount;
    box.textContent = '';
    const list = Array.isArray(cards) ? cards : [];
    list.forEach((card, i) => box.appendChild(cardNode(card, fresh && i >= had - (hidden ? 1 : 0))));
    if (hidden) box.appendChild(backNode());
    const total = handTotal(list);
    const printed = total.soft ? t('bk_t1_soft', [String(total.sum)]) : String(total.sum);
    rows[side].count.textContent = list.length
      ? ((side === 'dealer' && hidden) ? t('bk_t1_shows') + ' ' + printed : printed)
      : '';
  }

  /** Everything the felt shows for the state we are in. Pure paint. */
  function paint(fresh) {
    const open = !!(hand && hand.status === 'playing');
    paintHand('dealer', hand && hand.dealer, !!(hand && hand.dealerHidden), fresh);
    paintHand('player', hand && hand.player, false, fresh);
    wrap.classList.toggle('bk21-open', open);
    const wired = !!(k.api && typeof k.api.wired === 'function' && k.api.wired());
    wrap.classList.toggle('bk21-dark', !wired);
    btnDeal.hidden = open;
    btnHit.hidden = !open;
    btnStand.hidden = !open;
    btnDouble.hidden = !(open && hand && hand.canDouble);
    btnDeal.disabled = dead || busy || !wired;
    btnHit.disabled = dead || busy || !wired;
    btnStand.disabled = dead || busy || !wired;
    btnDouble.disabled = dead || busy || !wired;
    if (!wired) tell(t('bk_t1_dark'), '');
  }

  /** The felt's line, and under it the dealer's. The RESULT is what the player
   *  came to read, so she talks below it rather than over the top of it. */
  function tell(text, bucket) {
    bubble.textContent = String(text == null ? '' : text);
    if (!bucket) return;
    try {
      const line = k.voice ? k.voice.line(bucket) : '';
      if (line) bubble.appendChild(el('div', 'bk21-dealer-line', line));
    } catch (e) { say('twentyone: dealer line failed'); }
  }

  /* ------------------------------------------------------------ the borrow */
  /** E.M.I. is BORROWED, never driven (ctx.emi law): one visit offered, null is
   *  the ordinary answer, and the drawn screen carries the table either way.
   *  Asked once on arrival and once on a blackjack. Nothing waits on her. */
  function borrow() {
    if (dead || visit || !c.emi || typeof c.emi.visit !== 'function') return;
    try {
      visit = c.emi.visit({
        kind: 'stowaway',
        rect: () => (crt.getBoundingClientRect ? crt.getBoundingClientRect() : null),
        onDone: () => { visit = null; crt.classList.remove('bk21-lent'); },
      }) || null;
      if (visit) crt.classList.add('bk21-lent');
    } catch (e) { visit = null; }
  }

  /* -------------------------------------------------------------- the play */
  /** Every refusal reads warmly. Nobody gets scolded at this table. */
  function refusal(r) {
    const body = (r && r.body) || {};
    const reason = String(body.reason || body.code || '');
    if (r && r.status === 403) return t('bk_t1_locked');
    switch (reason) {
      case 'insufficient_chips': return t('bk_t1_poor');
      case 'hand_open': return t('bk_t1_open');
      case 'no_hand': return t('bk_t1_nohand');
      case 'invalid_input':
      case 'invalid_stake': return t('bk_t1_bad');
      case 'busy': return t('bk_t1_busy');
      case 'arcademy_locked': return t('bk_t1_locked');
      case 'offline':
      case 'timeout':
      case 'closed': return t('bk_t1_offline');
      default: return t('bk_t1_bad');
    }
  }

  /** THE LOCK. One answer at a time, every button, every road. */
  function setBusy(on) {
    busy = !!on;
    wrap.classList.toggle('bk21-busy', busy);
    paint(false);
  }

  function act(action) {
    if (dead || busy) return;
    if (!(k.api && typeof k.api.play === 'function' && k.api.wired && k.api.wired())) {
      tell(t('bk_t1_dark'), '');
      return;
    }
    setBusy(true);
    tell(t('bk_t1_working'), '');
    sfx(action === 'deal' || action === 'double' ? 'commit' : 'flap', 0.34);
    // A FRESH playId per press, minted inside the kit. It is an idempotency key,
    // so a retry of THIS press must carry THIS id and a new press must not.
    k.api.play('twentyone', stake(), { action }).then((r) => {
      if (dead) return;
      setBusy(false);
      if (!r || !r.ok) {
        tell(refusal(r), '');
        sfx('bump', 0.26);
        return;
      }
      land(r.body, action);
    });
  }

  /** One answer, folded in: the felt, the balances, the line, the beat. */
  function land(body, action) {
    const res = (body && body.result && typeof body.result === 'object') ? body.result : null;
    hand = res;
    paint(true);
    paintOdds();
    const status = String((res && res.status) || '');
    const payout = Math.max(0, Math.round(Number(body && body.payout) || 0));
    const insured = !!(body && body.insured);

    // THE BANK runs out of the felt on anything that pays, so the money is seen
    // to come off the table. Nothing is banked on a loss: an empty flight of
    // chips leaving the player would be a taunt, and this house does not.
    const pays = payout > 0 || status === 'push';
    if (typeof k.settle === 'function' && body && body.chips != null) k.settle(body, pays ? felt : null);

    if (status === 'playing') {
      tell(t('bk_t1_playing'), action === 'deal' ? 'deal' : '');
      sfx('thud', 0.3);
      return;
    }
    if (status === 'blackjack') {
      tell(t('bk_t1_blackjack', [fmtChips(payout)]), 'big');
      sfx('jackpot', 0.44);
      wrap.classList.add('bk21-beat');
      later(1400, () => wrap.classList.remove('bk21-beat'));
      borrow();
      return;
    }
    if (status === 'won') {
      tell(t('bk_t1_won', [fmtChips(payout)]), 'win');
      sfx('chime', 0.38);
      return;
    }
    if (status === 'push') {
      tell(t('bk_t1_push'), '');
      sfx('tell', 0.26);
      return;
    }
    if (insured) {
      tell(t('bk_t1_insured'), '');
      sfx('tell', 0.3);
      return;
    }
    const bust = handTotal((res && res.player) || []).sum > 21;
    tell(bust ? t('bk_t1_bust') : t('bk_t1_lost'), 'loss');
    sfx('bump', 0.3);
  }

  /* ------------------------------------------------------------- the opening */
  paintOdds();
  paint(false);
  // Resume first, ask second. The floor's snapshot is free and usually already
  // holds the open hand; the fresh status is what settles it either way.
  try {
    const snap = (typeof k.status === 'function') ? k.status() : null;
    if (snap && snap.hand) { hand = snap.hand; paint(false); tell(t('bk_t1_playing'), ''); }
  } catch (e) { /* an empty felt is a legal opening */ }
  if (k.api && typeof k.api.status === 'function') {
    k.api.status().then((r) => {
      if (dead || !r || !r.ok) return;
      hand = (r.body && r.body.hand) || null;
      if (typeof k.settle === 'function') k.settle(r.body, null);
      paintOdds();
      paint(false);
      tell(hand ? t('bk_t1_playing') : t('bk_t1_idle'), '');
    });
  }
  borrow();

  /** Down tools. SAFE MID-HAND, and that is the point: an open hand left on the
   *  felt is resolved by the server as a HOLD on the next status call, so this
   *  sends nothing and takes nothing away. It only stops painting. Safe from
   *  any road, and safe twice. */
  function unmount() {
    if (dead) return;
    dead = true;
    for (const h of timers) { try { clearTimeout(h); } catch (e) { /* noop */ } }
    timers.clear();
    try { if (visit && typeof visit.cancel === 'function') visit.cancel(); } catch (e) { /* noop */ }
    visit = null;
    try { wrap.remove(); } catch (e) { /* noop */ }
  }

  return { unmount, el: wrap };
}

/** The module-level handle backroom/index.js drives. It keeps ONE live table,
 *  because there is one stage on the floor and two would be a bug. */
let live = null;

export const twentyone = {
  key: 'twentyone',
  title: T1_LEX.bk_t1_title,
  lex: T1_LEX,
  mount(root, kit, ctx) {
    if (live) { try { live.unmount(); } catch (e) { /* noop */ } live = null; }
    live = mountTable(root, kit, ctx);
    return live;
  },
  unmount() {
    if (!live) return;
    const m = live;
    live = null;
    try { m.unmount(); } catch (e) { /* noop */ }
  },
};

export default twentyone;
