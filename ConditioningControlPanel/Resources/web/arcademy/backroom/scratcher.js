/* ============================================================================
 * backroom/scratcher.js - THE SCRATCHER, the cheapest seat on the floor.
 *
 * One free card a day, more for chips. Nine cells under a sheet of foil, and a
 * prize when any ROW comes up three of a kind.
 *
 * THE ANSWER ARRIVES BEFORE THE SCRATCH, AND THAT IS THE WHOLE DESIGN. The
 * server knows what is on the card the moment it takes the stake, so this
 * module asks first and only then lays the foil. A card that decided itself
 * while the thumb was moving would be a card with a thumb on it, and every
 * honest scratcher ever printed was printed before it was sold.
 *
 * WHAT THE FOIL COSTS: nothing a phone cannot pay. The cleared fraction rides
 * an 18x18 OCCUPANCY GRID rather than a pixel readback, because getImageData on
 * every pointermove is the slowest thing a compositor can be asked to do, and
 * it is the one call a tainted or headless canvas can refuse outright.
 *
 * REDUCED MOTION keeps the gesture and drops the MELT. The phone diet has no
 * canvas at all: one tap-to-reveal plate, which is the kindest thing to hand a
 * thumb on a bus anyway. NO CHIP ARITHMETIC LIVES HERE (LEDGER TRUTH) -
 * `kit.settle` folds the answer's own balances in once, when the card is read.
 * ==========================================================================*/

import { fill } from './lex.js';
import { drawSpiral } from '../engine/loom/loomSpiral.js';
import { makeRng } from '../core/rng.js';

/** Every string this cabinet prints, mirrored key for key into
 *  `ArcademyHostService.NeutralLexicon`. Under 96 characters each, or a mod
 *  can never re-voice the row (trap 26). No em-dashes, no raw newlines. */
export const SC_LEX = Object.freeze({
  bk_sc_title:     'The Scratcher',
  bk_sc_lede:      'Three in a row pays. Rub anywhere.',
  bk_sc_buy_free:  'FREE today',
  bk_sc_buy:       '{0} chips',                       /* {0} the stake */
  bk_sc_dealing:   'Printing one.',
  bk_sc_rub:       'Rub the foil.',
  bk_sc_tap:       'tap to reveal',
  bk_sc_pct:       '{0}% scratched',                  /* {0} percent cleared */
  bk_sc_free_note: 'On the house. One a day, every day.',
  bk_sc_win:       'Three in a row. {0} chips.',      /* {0} the prize */
  bk_sc_none:      'No row on that one. The foil was pretty though.',
  bk_sc_odds:      'what a card can pay',
  bk_sc_odds_row:  '{0} chips, one card in {1}',      /* {0} prize {1} one in N */
  bk_sc_odds_rest: 'anything else pays nothing. that is most cards.',
  /* refusals, warm, and every one says that nothing moved */
  bk_sc_poor:      'Not enough chips for a card. The cage is just there.',
  bk_sc_offline:   'The line to the counter is down. Nothing was charged.',
  bk_sc_busy:      'The cashier is with someone. Give her a moment.',
  bk_sc_locked:    'The bank does not serve this account yet. Nothing was charged.',
  bk_sc_refused:   'The counter would not print that one. Nothing moved.',
});

/** The card draws at a fixed logical size and CSS scales it, so a retina phone
 *  and a 1600px desktop clear the same number of occupancy cells. */
const CARD_PX = 252;
const OCC = 18;          // one grid cell per 14 logical pixels, finer than the brush
const BRUSH = 17;
/** THE MELT. Past sixty percent no amount of extra rubbing can change what is
 *  underneath, so the card stops asking for it. */
const MELT_AT = 0.6;
/** Only used until a status frame brings the real paytable. The page never
 *  publishes odds of its own (INPUT HONESTY). */
const ODDS_FLOOR = Object.freeze([
  { prize: 50, odds: 3 }, { prize: 150, odds: 10 },
  { prize: 600, odds: 100 }, { prize: 5000, odds: 2500 },
]);

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** lab.js's lazy sheet, id-guarded so a revisit costs nothing. */
function ensureSheet(id, rel, log) {
  try {
    if (document.getElementById(id)) return;
    const link = document.createElement('link');
    link.id = id; link.rel = 'stylesheet';
    link.href = new URL(rel, import.meta.url).href;
    document.head.appendChild(link);
  } catch (e) { log('backroom: sheet failed ' + rel); }
}

/** The cabinet's own resolver. `kit.t` falls back to BK_LEX and then to the KEY,
 *  and none of these rows are in BK_LEX, so a bare `kit.t` would paint
 *  `bk_sc_win` on the ticket. A host row wins; this table is the floor. */
function makeT(kit) {
  const base = (kit && typeof kit.t === 'function') ? kit.t : null;
  return function scT(key, args) {
    let raw = SC_LEX[key] == null ? key : SC_LEX[key];
    try { const got = base ? base(key) : null; if (got && got !== key) raw = got; }
    catch (e) { /* a dead lexicon still prints English */ }
    return args ? fill(raw, args) : raw;
  };
}

/** A cell's spiral, seeded off the cell's own value so a card always wears the
 *  same three. Drawn ONCE at phase zero: nine turning spirals would be nine
 *  rAF loops on a ticket, and a still spiral is the art anyway. */
function spiralCell(v, px) {
  const cv = document.createElement('canvas');
  cv.width = px; cv.height = px;
  cv.className = 'bk-sc-spiral';
  try {
    const rng = makeRng('bk-sc|' + String(v));
    drawSpiral(cv.getContext('2d'), px, {
      arms: 2 + Math.floor(rng() * 4), turns: 1.1 + rng() * 1.5,
      style: ['log', 'arch', 'ribbon'][Math.floor(rng() * 3)],
      duty: 0.44 + rng() * 0.18, speed: 0, direction: rng() < 0.5 ? -1 : 1,
      colors: ['#ff69b4', '#b8a6e8'], bg: '#2a1b44',
    }, 0);
  } catch (e) { /* an undrawn cell is still a cell */ }
  return cv;
}

/** The published paytable, server first, floor when it is missing or malformed
 *  rather than a number nobody can stand behind. */
function oddsRows(kit) {
  let p = null;
  try { p = (kit.config().paytables || {}).scratcher; } catch (e) { p = null; }
  if (!Array.isArray(p)) return ODDS_FLOOR;
  const rows = p.map((r) => ({
    prize: Math.round(Number(r && r.prize) || 0), odds: Math.round(Number(r && r.odds) || 0),
  })).filter((r) => r.prize > 0 && r.odds > 0);
  return rows.length ? rows : ODDS_FLOOR;
}

/** Which warm sentence a refusal gets. The server's word is never shown raw. */
function refusalKey(status, body) {
  if (status === 403) return 'bk_sc_locked';
  if (!status) return 'bk_sc_offline';
  const r = String((body && (body.reason || body.error)) || '');
  if (r === 'insufficient_chips') return 'bk_sc_poor';
  return r === 'busy' ? 'bk_sc_busy' : 'bk_sc_refused';
}

/** The phone diet. `kit.lite` is the ctx flag the shell projects, but the diet
 *  ITSELF is a class on <html>, and a rig or a web host can arm one without the
 *  other. Both count: a canvas drawn on a phone the sheets already put on
 *  rations is a canvas that ignored the ration. */
function liteMode(kit) {
  if (kit && kit.lite) return true;
  try { return document.documentElement.classList.contains('ae-lite'); }
  catch (e) { return false; }
}

let live = null;   // the one mounted cabinet, for the module-level unmount()

/**
 * mount(root, kit, ctx) -> { unmount }
 *
 * Everything hangs off `root` and comes down with it. Nothing here binds a key:
 * the shell owns the Esc ladder and the floor's `escapeStep` folds this cabinet
 * away, which is how leaving mid-scratch stays legal (Law VI).
 */
export function mount(root, kit, ctx) {
  const t = makeT(kit);
  const log = (typeof kit.log === 'function') ? kit.log : () => {};
  const sfx = (typeof kit.sfx === 'function') ? kit.sfx : () => {};
  const reduced = !!kit.reduced;
  const lite = liteMode(kit);
  ensureSheet('arc-bk-scratcher-css', './scratcher.css', log);

  let dead = false;
  let busy = false;      // a play is in flight, or a card is printed and unread
  let card = null;       // the live result, until the card has been read
  let pending = null;    // the answer body waiting to be banked
  let freeLeft = false;
  let pctSeen = -1;

  try { const s = kit.status(); freeLeft = !!(s && s.free_scratch_available); }
  catch (e) { freeLeft = false; }

  /* ------------------------------------------------------------ the cabinet */
  const wrap = el('div', 'bk-sc');
  const head = el('div', 'bk-sc-head');
  head.appendChild(el('h3', null, t('bk_sc_title')));
  head.appendChild(el('span', 'bk-sc-lede', t('bk_sc_lede')));
  const back = el('button', 'btn', kit.t('bk_back'));
  back.type = 'button';
  back.addEventListener('click', () => { try { kit.toFloor(); } catch (e) { /* noop */ } });
  head.appendChild(back);
  const stage = el('div', 'bk-sc-stage');
  wrap.appendChild(head);
  wrap.appendChild(stage);

  /* THE TICKET: paper, with a foil window cut in it. */
  const ticket = el('div', 'bk-sc-ticket');
  const tHead = el('div', 'bk-sc-thead');
  tHead.appendChild(el('span', null, t('bk_sc_title')));
  const pctEl = el('span', 'bk-sc-pct');
  const window_ = el('div', 'bk-sc-window');
  const grid = el('div', 'bk-sc-grid');
  grid.setAttribute('role', 'group');
  grid.setAttribute('aria-label', t('bk_sc_title'));
  window_.appendChild(grid);
  const tFoot = el('div', 'bk-sc-tfoot');
  tFoot.appendChild(el('span', null, kit.t('bk_sub_scratcher')));
  tFoot.appendChild(pctEl);
  ticket.appendChild(tHead);
  ticket.appendChild(window_);
  ticket.appendChild(tFoot);
  stage.appendChild(ticket);

  /* THE SIDE: the button, the receipt, the dealer, the odds. */
  const side = el('div', 'bk-sc-side');
  const buy = el('button', 'bk-sc-buy');
  buy.type = 'button';
  const say = el('div', 'bk-sc-say');
  say.setAttribute('role', 'status');
  const dealer = el('div', 'bk-dealer');
  const odds = el('div', 'bk-sc-odds');
  odds.appendChild(el('span', 'bk-sc-odds-h', t('bk_sc_odds')));
  for (const r of oddsRows(kit)) {
    odds.appendChild(el('div', null, t('bk_sc_odds_row', [kit.fmtChips(r.prize), kit.fmtChips(r.odds)])));
  }
  odds.appendChild(el('div', 'bk-sc-odds-rest', t('bk_sc_odds_rest')));
  side.appendChild(buy);
  side.appendChild(say);
  side.appendChild(dealer);
  side.appendChild(odds);
  stage.appendChild(side);
  root.appendChild(wrap);

  function stake() {
    if (freeLeft) return 0;   // the server decides what free means; we send nothing
    const v = Number((kit.config().stakes || {}).scratcher);
    return v > 0 ? Math.round(v) : 50;
  }

  function setPct(n) {
    const p = Math.max(0, Math.min(100, Math.round(n)));
    if (p === pctSeen) return;
    pctSeen = p;
    pctEl.textContent = t('bk_sc_pct', [String(p)]);
  }

  /** The one place the button's face is decided, so it can never disagree with
   *  what a press is about to cost. */
  function paintBuy() {
    buy.disabled = busy || dead;
    buy.textContent = busy ? (card ? t('bk_sc_rub') : t('bk_sc_dealing'))
      : (freeLeft ? t('bk_sc_buy_free') : t('bk_sc_buy', [kit.fmtChips(stake())]));
    buy.classList.toggle('bk-sc-free', freeLeft && !busy);
  }

  /* --------------------------------------------------------------- the foil */
  let foil = null;
  let occ = null;
  let hits = 0;
  let rubbing = false;
  let lastPip = 0;

  function clearFoil() {
    if (foil) { try { foil.remove(); } catch (e) { /* noop */ } }
    foil = null; occ = null; hits = 0; rubbing = false;
  }

  /** The diet's plate. No canvas, no gesture budget, one honest tap. */
  function layPlate() {
    const plate = el('button', 'bk-sc-plate', t('bk_sc_tap'));
    plate.type = 'button';
    plate.addEventListener('click', () => { setPct(100); melt(); });
    window_.appendChild(plate);
    foil = plate;
  }

  function layFoil() {
    if (lite) { layPlate(); return; }
    const cv = el('canvas', 'bk-sc-foil');
    cv.width = CARD_PX; cv.height = CARD_PX;
    cv.setAttribute('aria-hidden', 'true');
    let c2 = null;
    try { c2 = cv.getContext('2d'); } catch (e) { c2 = null; }
    if (!c2) { layPlate(); return; }   // no 2d context is a diet we did not choose
    const g = c2.createLinearGradient(0, 0, CARD_PX, CARD_PX);
    g.addColorStop(0, '#ff69b4'); g.addColorStop(0.5, '#c2407f'); g.addColorStop(1, '#ffa8d4');
    c2.fillStyle = g;
    c2.fillRect(0, 0, CARD_PX, CARD_PX);
    c2.fillStyle = 'rgba(255,255,255,.32)';
    c2.font = '700 12px Cascadia Mono, Consolas, monospace';
    c2.textAlign = 'center';
    for (let y = 22; y < CARD_PX; y += 34) for (let x = 0; x < CARD_PX; x += 72) c2.fillText('SCRATCH', x + 36, y);
    c2.globalCompositeOperation = 'destination-out';
    occ = new Uint8Array(OCC * OCC);
    window_.appendChild(cv);
    foil = cv;

    const step = CARD_PX / OCC;
    const mark = (ev) => {
      if (dead || foil !== cv) return;
      let r = null;
      try { r = cv.getBoundingClientRect(); } catch (e) { return; }
      if (!r || !r.width || !r.height) return;
      const x = (ev.clientX - r.left) * CARD_PX / r.width;
      const y = (ev.clientY - r.top) * CARD_PX / r.height;
      c2.beginPath();
      c2.arc(x, y, BRUSH, 0, Math.PI * 2);
      c2.fill();
      // 324 comparisons, and they beat one getImageData by orders. See the head.
      for (let i = 0; i < occ.length; i++) {
        if (occ[i]) continue;
        const dx = ((i % OCC) + 0.5) * step - x;
        const dy = (Math.floor(i / OCC) + 0.5) * step - y;
        if (dx * dx + dy * dy <= BRUSH * BRUSH) { occ[i] = 1; hits++; }
      }
      const frac = hits / occ.length;
      setPct(frac * 100);
      const now = Date.now();
      if (now - lastPip > 190) { lastPip = now; sfx('pip', 0.12); }
      if (frac >= MELT_AT) melt();
    };
    const stop = () => { rubbing = false; };
    cv.addEventListener('pointerdown', (ev) => {
      rubbing = true;
      try { cv.setPointerCapture(ev.pointerId); } catch (e) { /* a mouse needs no capture */ }
      mark(ev);
    });
    cv.addEventListener('pointermove', (ev) => { if (rubbing) mark(ev); });
    cv.addEventListener('pointerup', stop);
    cv.addEventListener('pointercancel', stop);
    cv.addEventListener('lostpointercapture', stop);
  }

  /* ------------------------------------------------------------- the reveal */
  function melt() {
    if (dead || !foil) return;
    const node = foil;
    rubbing = false;
    setPct(100);
    if (reduced) { clearFoil(); settle(); return; }
    sfx('slide', 0.3);
    node.classList.add('bk-sc-melt');
    setTimeout(() => { if (foil === node) clearFoil(); settle(); }, 380);
  }

  /** The card has been read, so NOW the receipt lands. The stake and the prize
   *  arrived on one server frame, so they are shown on one beat rather than
   *  faked as two events the server never sent. */
  function settle() {
    if (dead || !card) return;
    const res = card;
    const body = pending;
    card = null; pending = null;
    const prize = Math.max(0, Math.round(Number(res.prize) || 0));
    if (res.row >= 0 && res.row <= 2) {
      for (let i = 0; i < 3; i++) {
        const cell = grid.children[res.row * 3 + i];
        if (cell) cell.classList.add('bk-sc-hit');
      }
      sfx('thud', 0.4);
    }
    if (prize > 0) {
      say.textContent = t('bk_sc_win', [kit.fmtChips(prize)]);
      say.classList.add('bk-warm');
      sfx('chime', 0.34);
      dealer.textContent = kit.voice.line(prize >= 600 ? 'big' : 'win');
    } else {
      say.textContent = t('bk_sc_none');
      say.classList.remove('bk-warm');
      // Sympathy, but not on every single card: a dealer who commiserates nine
      // times running stops sounding like she means it.
      dealer.textContent = (Math.random() < 0.55) ? kit.voice.line('loss') : '';
    }
    try { if (body) kit.settle(body, prize > 0 ? ticket : null); }
    catch (e) { log('backroom: scratcher settle threw'); }
    busy = false;
    paintBuy();
  }

  /** Nine cells, painted from the server's own grid. */
  function layGrid(cells) {
    grid.textContent = '';
    for (let i = 0; i < 9; i++) {
      const c = cells[i] || {};
      const v = String(c.v == null ? '' : c.v);
      const kind = c.kind === 'spiral' ? 'sp' : (c.kind === 'chip' ? 'ch' : 'wd');
      const cell = el('div', 'bk-sc-cell bk-sc-' + kind);
      cell.appendChild(kind === 'sp' ? spiralCell(v, 46) : el('span', null, v));
      cell.setAttribute('aria-label', (kind === 'sp' ? 'spiral ' : '') + v);
      grid.appendChild(cell);
    }
  }

  /* ---------------------------------------------------------------- the buy */
  function press() {
    if (dead || busy) return;
    busy = true;
    say.textContent = '';
    say.classList.remove('bk-warm');
    dealer.textContent = kit.voice.line('deal');
    clearFoil();
    grid.textContent = '';
    setPct(0);
    paintBuy();
    sfx('blip', 0.28);
    kit.api.play('scratcher', stake(), {}).then((r) => {
      if (dead) return;
      const res = (r.ok && r.body && r.body.result) || null;
      if (!res || !Array.isArray(res.grid)) {
        say.textContent = t(refusalKey(r.status, r.body));
        say.classList.add('bk-warm');
        dealer.textContent = '';
        busy = false;
        paintBuy();
        return;
      }
      if (res.free === true) freeLeft = false;
      card = res;
      pending = r.body;
      layGrid(res.grid);
      layFoil();
      say.textContent = res.free === true ? t('bk_sc_free_note') : t('bk_sc_rub');
      paintBuy();
    });
  }
  buy.addEventListener('click', press);
  paintBuy();
  setPct(0);

  /**
   * Down mid-scratch is a legal way to leave, and the chips moved on the server
   * already: an unread card still banks its answer on the way out, or the header
   * would go on painting a balance the ledger has walked past.
   */
  function unmount() {
    if (dead) return;
    dead = true;
    if (live && live.unmount === unmount) live = null;
    const body = pending;
    card = null; pending = null;
    clearFoil();
    try { if (body) kit.settle(body, null); } catch (e) { /* noop */ }
    try { wrap.remove(); } catch (e) { /* noop */ }
  }

  live = { unmount };
  return live;
}

/** The floor calls this after the instance's own, and a second call is free. */
export function unmount() { if (live) live.unmount(); }

export default { key: 'scratcher', title: SC_LEX.bk_sc_title, mount, unmount, SC_LEX };
