/* ============================================================================
 * backroom/spiral.js - THE SPIRAL, the wheel whose face is a Loom spiral.
 *
 * Twenty-four wedges. Eleven pink, eleven violet, one WORD and one DROP. You
 * back a colour, you back the word, or you back a single wedge, and then the
 * wheel goes up and the pin does not move.
 *
 * THE LAYOUT IS PUBLISHED AND IT IS FIXED (INPUT HONESTY). `SPIRAL_LAYOUT`
 * below is the whole truth of the face: index zero at twelve o'clock, the word
 * at six, the drop at eighteen, colours alternating everywhere else so no two
 * neighbours share a hue. The server pins the SAME table, which is why it is
 * exported: a wheel whose face and whose ledger disagree is a rigged wheel even
 * when nobody meant it to be.
 *
 * AND WHEN THEY DO DISAGREE, THE SERVER WINS. `body.result.kind` is what pays.
 * If the answer says wedge 9 was the word and this table says wedge 9 is pink,
 * the mismatch is logged loudly and the face is repainted to match the answer
 * before the wheel turns, because the alternative is a player watching pink
 * come up and being told they hit the word. The ledger is never argued with and
 * the face is never allowed to lie about it.
 *
 * NOT ONE CHIP IS COUNTED HERE (LEDGER TRUTH). The stake goes up the seam, the
 * balances come back on the answer, and `kit.settle` folds them into the room.
 * The only arithmetic in this file is which wedge to point at.
 *
 * EXITS SACRED (Law VI). Nothing here binds a key, nothing here is modal, and
 * the drop veil is pointer-events:none, so the room's own way out stays live
 * through a spin, through a drop and through a request that never comes back.
 * `unmount()` mid-spin kills the frame loop and answers nothing afterwards.
 * ==========================================================================*/

import { createLoomWheel } from './kit/loomwheel.js';
import { createDropBeat } from './kit/dropbeat.js';
import { fill } from './lex.js';

/**
 * THE PUBLISHED FACE. Index is the wedge the server names; the value is what
 * that wedge is. Eleven pink, eleven violet, the word at 6, the drop at 18,
 * and no two touching wedges the same colour (including across the 23/0 seam).
 * Change this and the server's copy changes in the same breath, or neither.
 */
export const SPIRAL_LAYOUT = Object.freeze([
  'pink', 'violet', 'pink', 'violet', 'pink', 'violet',
  'word',
  'pink', 'violet', 'pink', 'violet', 'pink', 'violet', 'pink', 'violet', 'pink', 'violet', 'pink',
  'drop',
  'violet', 'pink', 'violet', 'pink', 'violet',
]);

/** The stakes the cabinet takes, as a FLOOR only. The live list arrives on the
 *  status frame's config, so the page never publishes a number of its own. */
export const SPIRAL_STAKES = Object.freeze([25, 50, 100]);

/** The published paytable floor, same rule: overridden by config.paytables. */
const PAYS = Object.freeze({ colour: 1, word: 20, number: 20 });

const WEDGES = SPIRAL_LAYOUT.length;

/** The felt hues, as literals for the reason backroom.css keeps its greens as
 *  literals: a casino wheel is a REMEMBERED colour, not a derived one, and a
 *  wheel that reskins with the mod palette stops being readable at a glance. */
const HUES = Object.freeze({
  pink: { color: '#a83a6b', ink: '#ffe6f1' },
  violet: { color: '#4f3f86', ink: '#e6dfff' },
  word: { color: '#f0c24b', ink: '#231a06' },
  drop: { color: '#101024', ink: '#8f8fb8' },
});

/** Every string the cabinet can print. Mirrored key for key into
 *  ArcademyHostService.NeutralLexicon: copy the values, do not re-word them.
 *  Under 96 characters each, or MergeModTable drops the row (trap 26). */
export const SP_LEX = Object.freeze({
  bk_sp_title: 'The Spiral',
  bk_sp_sub: 'eleven pink, eleven violet, one word, one drop.',
  bk_sp_bets: 'the board',
  bk_sp_pink: 'PINK',
  bk_sp_violet: 'VIOLET',
  bk_sp_word: 'THE WORD',
  bk_sp_number: 'A NUMBER',
  bk_sp_word_none: 'the word',
  bk_sp_drop_wedge: 'DROP',
  /* {0} how many wedges, {1} the multiple paid */
  bk_sp_odds_even: '{0} wedges in 24. pays {1} to 1.',
  bk_sp_odds_one: '1 wedge in 24. pays {0} to 1.',
  bk_sp_odds_drop: 'One wedge in twenty-four is the drop. It takes colour and word bets. Only a number on it pays.',
  bk_sp_grid_lbl: 'the twenty-four wedges',
  /* {0} the wedge number */
  bk_sp_grid_one: 'wedge {0}',
  bk_sp_stake: 'stake',
  bk_sp_stake_lbl: 'chips a spin',
  bk_sp_spin: 'SPIN',
  bk_sp_ready: 'Pick a colour, the word, or a wedge. Then spin.',
  bk_sp_working: 'Chips are down.',
  bk_sp_up: 'The wheel is up.',
  bk_sp_need_bet: 'Name a bet first. The wheel will not guess for you.',
  bk_sp_need_wedge: 'Pick which wedge, then spin.',
  /* {0} the wedge number, {1} chips paid */
  bk_sp_won: 'Wedge {0}. {1} chips back.',
  bk_sp_lost: 'Wedge {0}. The floor keeps this one.',
  bk_sp_word_hit: 'Your word came up on wedge {0}. {1} chips.',
  bk_sp_drop_hit: 'Wedge {0}. The drop. Lights down a moment.',
  bk_sp_insured: 'The insurance chip covered that one. Nothing lost.',
  bk_sp_poor: 'Not enough chips for that stake. The cage is out on the floor.',
  bk_sp_bad_stake: 'The wheel does not take that stake tonight.',
  bk_sp_bad_bet: 'The wheel would not take that bet. Nothing moved.',
  bk_sp_busy: 'The wheel is still settling for somebody. One moment.',
  bk_sp_offline: 'The line to the pit is down. Your chips are where you left them.',
  bk_sp_locked: 'The bank does not serve this account yet. Nothing was charged.',
  bk_sp_refused: 'The wheel would not take that one. Nothing moved.',
  bk_sp_dark: 'The wheel is covered. Come back when the line is up.',
});

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** lab.js's lazy sheet, id-guarded so a second visit costs nothing. Copied
 *  rather than imported: the kit does not reach into the shell, and neither
 *  does a cabinet reach into the floor. */
function ensureSheet(id, rel, log) {
  try {
    if (document.getElementById(id)) return;
    const link = document.createElement('link');
    link.id = id;
    link.rel = 'stylesheet';
    link.href = new URL(rel, import.meta.url).href;
    document.head.appendChild(link);
  } catch (e) { log('backroom sheet failed: ' + rel); }
}

/** The floor's `makeT` shape over this cabinet's own table, so a key with no
 *  host row behind it still prints its authored English. */
function makeSpT(base) {
  const b = (typeof base === 'function') ? base : null;
  return function spT(key, args) {
    const en = SP_LEX[key];
    const raw = b ? b(key, en == null ? key : en) : (en == null ? key : en);
    return args ? fill(raw, args) : raw;
  };
}

/** The stakes the pit is taking, off the live config, with the floor behind it. */
function stakesFrom(cfg) {
  const p = (cfg && cfg.paytables && cfg.paytables.spiral) || null;
  const raw = (p && p.stakes) || (cfg && cfg.stakes && cfg.stakes.spiral);
  const list = Array.isArray(raw) ? raw : (Number(raw) > 0 ? [raw] : null);
  if (!list) return SPIRAL_STAKES.slice();
  const seen = [];
  for (const v of list) {
    const n = Math.round(Number(v) || 0);
    if (n > 0 && seen.indexOf(n) < 0) seen.push(n);
  }
  seen.sort((a, b) => a - b);
  return seen.length ? seen.slice(0, 5) : SPIRAL_STAKES.slice();
}

/** The multiples the pit is paying, same law. */
function paysFrom(cfg) {
  const p = (cfg && cfg.paytables && cfg.paytables.spiral) || {};
  const one = (v, floor) => (Number(v) > 0 ? Number(v) : floor);
  return { colour: one(p.colour, PAYS.colour), word: one(p.word, PAYS.word), number: one(p.number, PAYS.number) };
}

/**
 * mount(root, kit, ctx) -> { unmount }
 *
 * `root` is the floor's stage. `kit` is the one object index.js builds. `ctx`
 * is the scene surface, and EVERY field on it is optional: a fixture hands us
 * a ctx with no words, no lexicon and no line to the counter, and the cabinet
 * still paints, still refuses warmly, and still leaves cleanly.
 */
function mount(root, kit, ctx) {
  const k = kit || {};
  const c = ctx || {};
  const note = (typeof k.log === 'function') ? k.log : () => {};
  const sfx = (typeof k.sfx === 'function') ? k.sfx : () => {};
  const fmt = (typeof k.fmtChips === 'function') ? k.fmtChips : ((n) => String(n));
  // The SCENE's plain t(key, fallback), not the floor's bkT: these rows live in
  // this file's own table, so they want the two-argument resolver and its
  // mod-row-then-English chain rather than a lookup in the floor's lexicon.
  const t = makeSpT(c.t);
  const reduced = !!k.reduced;
  const lite = !!k.lite;

  ensureSheet('arc-backroom-spiral-css', './spiral.css', note);

  let dead = false;
  let busy = false;
  let bet = 'pink';          // 'pink' | 'violet' | 'word' | 'number'
  let wedgePick = 0;         // which wedge, when the bet is a number
  let stake = 0;             // set from the live config below
  let word = '';             // the night's word, as far as the page knows it
  const layout = SPIRAL_LAYOUT.slice();   // the server may correct one entry

  /** The night's word: the server's if it sent one, the day pool's if the page
   *  has one, and otherwise an honest placeholder rather than an invented word. */
  function nightWord() {
    const s = (typeof k.status === 'function' && k.status()) || {};
    const fromServer = s.word || (s.config && s.config.word) || (s.result && s.result.word);
    if (fromServer) return String(fromServer);
    try {
      const list = Array.isArray(c.words) ? c.words : null;
      if (list && list.length && list[0]) return String(list[0]);
    } catch (e) { /* a page with no day pool is an ordinary page */ }
    return t('bk_sp_word_none');
  }

  /* ------------------------------------------------------------ the cabinet */
  const box = el('div', 'bk-sp' + (lite ? ' bk-sp-lite' : ''));
  box.setAttribute('role', 'group');
  box.setAttribute('aria-label', t('bk_sp_title'));

  const head = el('div', 'bk-sp-head');
  head.appendChild(el('h3', 'bk-sp-name', t('bk_sp_title')));
  head.appendChild(el('span', 'bk-sp-sub', t('bk_sp_sub')));
  head.appendChild(el('span', 'bk-sp-spacer'));
  const backBtn = el('button', 'bk-sp-back', (typeof k.t === 'function') ? k.t('bk_back') : 'back to the floor');
  backBtn.type = 'button';
  backBtn.addEventListener('click', () => { if (!dead && typeof k.toFloor === 'function') k.toFloor(); });
  try { if (c.exits && typeof c.exits.sign === 'function') c.exits.sign(backBtn, { dir: 'back', quiet: true }); }
  catch (e) { /* an undressed button still walks */ }
  head.appendChild(backBtn);
  box.appendChild(head);

  const body = el('div', 'bk-sp-body');
  box.appendChild(body);

  /* --------------------------------------------------------------- the wheel */
  const wheelBox = el('div', 'bk-sp-wheelbox');
  body.appendChild(wheelBox);
  const wheel = createLoomWheel({
    host: wheelBox,
    size: lite ? 224 : 328,
    seed: 'backroom-spiral',
    wedges: [],
    reduced, lite, sfx, log: note,
  });
  /* The pearl sits ON the pin and never travels: the wheel moves, the marker
   *  does not, which is the only way a player can read the face at a glance. */
  const pearl = el('i', 'bk-sp-pearl');
  pearl.setAttribute('aria-hidden', 'true');
  wheelBox.appendChild(pearl);

  const drop = createDropBeat({ host: box, voice: k.voice, sfx, reduced, lite, log: note });

  /** The face, rebuilt whenever the night's word or a corrected wedge changes. */
  function paintFace() {
    word = nightWord();
    wheel.setWedges(layout.map((kind, i) => {
      const hue = HUES[kind] || HUES.pink;
      let label = String(i);
      if (kind === 'word') label = word.toUpperCase();
      else if (kind === 'drop') label = t('bk_sp_drop_wedge');
      return { label, color: hue.color, ink: hue.ink, drop: kind === 'drop' };
    }));
  }

  /* --------------------------------------------------------------- the board */
  const board = el('div', 'bk-sp-board');
  body.appendChild(board);
  board.appendChild(el('div', 'bk-sp-legend', t('bk_sp_bets')));

  const bets = el('div', 'bk-sp-bets');
  bets.setAttribute('role', 'group');
  bets.setAttribute('aria-label', t('bk_sp_bets'));
  board.appendChild(bets);

  const pays = paysFrom((typeof k.config === 'function' && k.config()) || {});
  const BETS = [
    { id: 'pink', nameKey: 'bk_sp_pink', odds: t('bk_sp_odds_even', [11, pays.colour]) },
    { id: 'violet', nameKey: 'bk_sp_violet', odds: t('bk_sp_odds_even', [11, pays.colour]) },
    { id: 'word', nameKey: 'bk_sp_word', odds: t('bk_sp_odds_one', [pays.word]) },
    { id: 'number', nameKey: 'bk_sp_number', odds: t('bk_sp_odds_one', [pays.number]) },
  ];
  const betBtns = [];
  for (const b of BETS) {
    const btn = el('button', 'bk-sp-bet bk-sp-bet-' + b.id);
    btn.type = 'button';
    btn.appendChild(el('span', 'bk-sp-bet-name', t(b.nameKey)));
    btn.appendChild(el('small', 'bk-sp-bet-odds', b.odds));
    btn.addEventListener('click', () => { if (!busy && !dead) { setBet(b.id); sfx('pip', 0.22); } });
    bets.appendChild(btn);
    betBtns.push({ id: b.id, btn });
  }

  /* THE WEDGE GRID, shown only when the player is backing one. Every cell wears
   * its wedge's own colour, so picking 6 or 18 is picking the word or the drop
   * with the eyes open rather than by accident. */
  const grid = el('div', 'bk-sp-grid');
  grid.setAttribute('role', 'group');
  grid.setAttribute('aria-label', t('bk_sp_grid_lbl'));
  grid.hidden = true;
  board.appendChild(grid);
  const cells = [];
  for (let i = 0; i < WEDGES; i++) {
    const cell = el('button', 'bk-sp-cell bk-sp-' + layout[i], String(i));
    cell.type = 'button';
    cell.setAttribute('aria-label', t('bk_sp_grid_one', [i]));
    cell.addEventListener('click', () => { if (!busy && !dead) { wedgePick = i; setBet('number'); sfx('pip', 0.2); } });
    grid.appendChild(cell);
    cells.push(cell);
  }

  /* -------------------------------------------------------------- the stakes */
  const stakeRow = el('div', 'bk-sp-stakes');
  stakeRow.setAttribute('role', 'group');
  stakeRow.setAttribute('aria-label', t('bk_sp_stake'));
  stakeRow.appendChild(el('span', 'bk-sp-legend', t('bk_sp_stake')));
  board.appendChild(stakeRow);
  const stakeBtns = [];
  function buildStakes() {
    for (const s of stakeBtns) { try { s.btn.remove(); } catch (e) { /* noop */ } }
    stakeBtns.length = 0;
    const list = stakesFrom((typeof k.config === 'function' && k.config()) || {});
    if (list.indexOf(stake) < 0) stake = list[0];
    for (const n of list) {
      const btn = el('button', 'bk-sp-stakebtn', fmt(n));
      btn.type = 'button';
      btn.addEventListener('click', () => { if (!busy && !dead) { stake = n; sfx('pip', 0.22); paint(); } });
      stakeRow.appendChild(btn);
      stakeBtns.push({ n, btn });
    }
  }
  buildStakes();
  stakeRow.appendChild(el('span', 'bk-sp-stake-unit', t('bk_sp_stake_lbl')));

  /* THE HONEST ODDS LINE. Every number on it is the one that pays. */
  board.appendChild(el('p', 'bk-sp-odds', t('bk_sp_odds_drop')));

  const go = el('button', 'bk-sp-go', t('bk_sp_spin'));
  go.type = 'button';
  board.appendChild(go);

  const say = el('div', 'bk-sp-say');
  say.setAttribute('role', 'status');
  board.appendChild(say);
  const dealer = el('div', 'bk-dealer');
  board.appendChild(dealer);

  function setBet(id) {
    bet = id;
    grid.hidden = (id !== 'number');
    paint();
  }

  /** How many chips the room last said we had. Display only: the button's guard
   *  is a courtesy, and the server is still the one that refuses. */
  function chipsNow() {
    const s = (typeof k.status === 'function' && k.status()) || {};
    return Math.max(0, Math.round(Number(s.chips) || 0));
  }

  function paint() {
    for (const b of betBtns) b.btn.setAttribute('aria-pressed', b.id === bet ? 'true' : 'false');
    for (const s of stakeBtns) {
      s.btn.setAttribute('aria-pressed', s.n === stake ? 'true' : 'false');
      s.btn.disabled = busy || dead;
    }
    for (let i = 0; i < cells.length; i++) {
      cells[i].setAttribute('aria-pressed', (bet === 'number' && i === wedgePick) ? 'true' : 'false');
      cells[i].disabled = busy || dead;
    }
    for (const b of betBtns) b.btn.disabled = busy || dead;
    const wired = !!(k.api && typeof k.api.wired === 'function' && k.api.wired());
    box.classList.toggle('bk-sp-dark', !wired);
    backBtn.disabled = false;      // the way back is never disabled, not once
    go.disabled = dead || busy || !wired || stake <= 0;
    // A covered wheel says so instead of sitting there looking open for business.
    if (!wired && (!say.textContent || say.textContent === t('bk_sp_ready'))) say.textContent = t('bk_sp_dark');
  }

  /** Every refusal reads warmly. The pit never scolds a player for being short. */
  function refusal(r) {
    const b = (r && r.body) || {};
    const reason = String(b.reason || b.code || '');
    if (reason === 'casino_closed') return t('bk_closed');   // a 403 too, but the house's, not the account's
    if (r && r.status === 403) return t('bk_sp_locked');
    switch (reason) {
      case 'insufficient_chips': return t('bk_sp_poor');
      case 'invalid_stake': return t('bk_sp_bad_stake');
      case 'invalid_input': return t('bk_sp_bad_bet');
      case 'busy': return t('bk_sp_busy');
      case 'arcademy_locked': return t('bk_sp_locked');
      case 'offline':
      case 'timeout':
      case 'closed': return t('bk_sp_offline');
      default: return t('bk_sp_refused');
    }
  }

  function speak(bucket) {
    try { dealer.textContent = k.voice ? k.voice.line(bucket) : ''; }
    catch (e) { note('backroom: dealer line failed'); }
  }

  /** One spin: the stake up the seam, the answer down, the wheel to the wedge
   *  the answer named, and the balances folded in on the way past. */
  function spin() {
    if (dead || busy) return;
    if (!bet) { say.textContent = t('bk_sp_need_bet'); sfx('bump', 0.26); return; }
    if (bet === 'number' && !(wedgePick >= 0 && wedgePick < WEDGES)) {
      say.textContent = t('bk_sp_need_wedge'); sfx('bump', 0.26); return;
    }
    if (chipsNow() < stake) { say.textContent = t('bk_sp_poor'); sfx('bump', 0.26); return; }

    busy = true;
    paint();
    say.textContent = t('bk_sp_working');
    dealer.textContent = '';
    speak('deal');
    sfx('commit', 0.34);

    const input = { bet: (bet === 'number') ? wedgePick : bet };
    k.api.play('spiral', stake, input).then((r) => {
      if (dead) return;
      if (!r || !r.ok) {
        busy = false;
        say.textContent = refusal(r);
        dealer.textContent = '';
        sfx('bump', 0.28);
        paint();
        return;
      }
      land(r.body || {});
    });
  }
  go.addEventListener('click', spin);

  /** The answer, rendered. The only decision left in this file is how long the
   *  wheel takes to get there, and even that is a constant. */
  function land(bodyIn) {
    const res = (bodyIn.result && typeof bodyIn.result === 'object') ? bodyIn.result : {};
    let at = Math.round(Number(res.wedge) || 0);
    if (!(at >= 0 && at < WEDGES)) { note('backroom spiral: wedge out of range: ' + res.wedge); at = 0; }
    const kind = String(res.kind || layout[at]);

    /* THE FACE IS CORRECTED TO THE ANSWER, never the other way round. A player
     * must never watch pink come up and be told they hit the word. */
    if (HUES[kind] && layout[at] !== kind) {
      note('backroom spiral: server says wedge ' + at + ' is ' + kind + ', face says ' + layout[at]);
      layout[at] = kind;
      try { cells[at].className = 'bk-sp-cell bk-sp-' + kind; } catch (e) { /* noop */ }
    }
    if (res.word) word = String(res.word);
    paintFace();

    say.textContent = t('bk_sp_up');
    wheel.spinTo(at, lite ? 2200 : 3200).then(() => {
      if (dead) return;
      finish(bodyIn, at, kind);
    });
  }

  /** The settle, the sentence and the ceremony, in that order: money first,
   *  because the number is the information and it is never held back for a beat. */
  function finish(bodyIn, at, kind) {
    const payout = Math.max(0, Math.round(Number(bodyIn.payout) || 0));
    if (bodyIn.chips != null && typeof k.settle === 'function') {
      try { k.settle(bodyIn, payout > 0 ? wheelBox : null); } catch (e) { note('backroom spiral: settle failed'); }
    }
    if (kind === 'word') { try { wheel.el.classList.add('bk-sp-wordhit'); } catch (e) { /* noop */ } }
    try { pearl.classList.add('bk-sp-pearl-hit'); } catch (e) { /* noop */ }

    if (kind === 'drop') say.textContent = t('bk_sp_drop_hit', [at]);
    else if (payout > 0 && kind === 'word') say.textContent = t('bk_sp_word_hit', [at, fmt(payout)]);
    else if (payout > 0) say.textContent = t('bk_sp_won', [at, fmt(payout)]);
    else say.textContent = t('bk_sp_lost', [at]);
    if (bodyIn.insured === true) say.textContent += ' ' + t('bk_sp_insured');

    const big = payout >= stake * 5;
    if (kind === 'drop') speak('drop');
    else if (payout > 0) speak(big ? 'big' : 'win');
    else speak('loss');
    if (payout > 0) sfx(big ? 'jackpot' : 'chime', big ? 0.34 : 0.28);
    else if (kind !== 'drop') sfx('thud', 0.22);

    const after = () => {
      if (dead) return;
      busy = false;
      try { wheel.el.classList.remove('bk-sp-wordhit'); } catch (e) { /* noop */ }
      try { pearl.classList.remove('bk-sp-pearl-hit'); } catch (e) { /* noop */ }
      paint();
    };
    // THE DROP IS A BEAT, NOT A GATE. It is capped at 1.6s inside dropbeat.js,
    // it takes no focus and it covers nothing that can be clicked, so the worst
    // it can do to a player in a hurry is happen behind them while they leave.
    if (kind === 'drop') drop.run({ ms: lite ? 900 : 1300 }).then(after, after);
    else after();
  }

  /* ------------------------------------------------------------------ opening */
  say.textContent = t('bk_sp_ready');
  paintFace();
  setBet('pink');
  try { root.appendChild(box); } catch (e) { note('backroom spiral: would not attach'); }
  sfx('slide', 0.22);

  return {
    root: box,
    /** Safe from any road, safe mid-spin, and safe twice. */
    unmount() {
      if (dead) return;
      dead = true;
      busy = false;
      try { wheel.destroy(); } catch (e) { /* noop */ }
      try { drop.destroy(); } catch (e) { /* noop */ }
      try { box.remove ? box.remove() : box.parentNode && box.parentNode.removeChild(box); }
      catch (e) { /* noop */ }
    },
  };
}

/** The floor mounts one cabinet at a time, but a module-level `unmount()` is
 *  part of the machine contract, so the last one mounted is remembered and
 *  torn down here. Calling it twice, or with nothing open, does nothing. */
let live = null;

export const spiral = {
  key: 'spiral',
  title: SP_LEX.bk_sp_title,
  lex: SP_LEX,
  layout: SPIRAL_LAYOUT,
  mount(root, kit, ctx) {
    const handle = mount(root, kit, ctx);
    live = handle;
    return {
      root: handle.root,
      unmount() { if (live === handle) live = null; handle.unmount(); },
    };
  },
  unmount() {
    const h = live;
    live = null;
    if (h) { try { h.unmount(); } catch (e) { /* noop */ } }
  },
};

export default spiral;
