/* ============================================================================
 * backroom/triple.js - TRIPLE TRIGGER, the cabinet.
 *
 * Three reels. Reel one carries the first word of every three-word trigger the
 * player owns, reel two the second, reel three the third. Line all three up on
 * the same trigger and the machine reads it back to you. Line up three
 * DIFFERENT triggers and you have a sentence nobody wrote, and the machine
 * says that too, quieter, and pays a little for the new one.
 *
 * The window itself lives in backroom/triple-reel.js. This file is the cabinet
 * around it: the price, the holds, the odds sheet, the lever, and what the
 * dealer says when the reels come down.
 *
 * THE LAWS THIS FILE IS WRITTEN AROUND.
 *  - LEDGER TRUTH. The reels are theatre. The server drew the outcome before
 *    the first frame turned and `result.reels` says where each one stops. Not
 *    one chip is counted here; the only balance write is `kit.settle` handing
 *    the server's answer back to the room.
 *  - INPUT HONESTY. The odds sheet is rendered from `kit.config()`, so what the
 *    cabinet publishes IS the server's table. `ODDS_FLOOR` is the floor for a
 *    fixture and for the seconds before the first status frame, never a second
 *    opinion about what the machine pays.
 *  - EXITS SACRED (Law VI). Nothing here binds a key, nothing here is a modal,
 *    and "back to the floor" is live during a spin with a stake in flight.
 *  - LOSSES GET SYMPATHY, and not every time. A dealer who commiserates after
 *    every blank pull is a dealer reading from a card.
 *
 * WHY THE HOLDS ARE DEAD UNTIL THE FIRST PULL. A hold costs five chips and
 * keeps a reel where it was. Before the first pull there is nowhere it was, so
 * the house would be selling five chips of nothing. The toggles stay disabled
 * and say why, which is the same instinct as the cage's tree line: this room
 * tells you what you are buying before it takes the money.
 *
 * WHY THE ROYAL DOES NOT SPEAK OUT LOUD. Trigger Mode audio exists, but not
 * here: the clip route is `engine/oneshots.js` `audio_trigger`, fed by the word
 * to url map `shell/shell.js` builds from `init.triggers`, and a Back Room
 * scene is handed no engine and no `ctx.triggers` at all. Minting a new event
 * for a listener nobody has written would be dead code pretending to be a
 * feature. So a royal is a VISUAL and an sfx beat, and the day the seam exists
 * the only change here is one call inside `royal()`.
 * ==========================================================================*/

import { holdRing } from './kit/holdring.js';
import { createDropBeat } from './kit/dropbeat.js';
import { reelRows } from './kit/triggers.js';
import { createReelWindow, REELS } from './triple-reel.js';
import { fill } from './lex.js';

export const key = 'triple';

/* THE PUBLISHED FLOOR (contract §5). The real numbers, here for one reason: a
 * fixture, a dark room and the seconds before the first status frame all need
 * a sheet to show, and a blank paytable on a slot machine reads as a machine
 * with something to hide. The live frame overwrites every one of them the
 * moment it lands. Odds are DENOMINATORS: 400 means one in four hundred. */
const STAKE_FLOOR = 10;
const HOLD_FLOOR = 5;
const MULT_FLOOR = Object.freeze({ royal: 100, scramble: 3, pair: 1.2 });
const ODDS_FLOOR = Object.freeze([
  Object.freeze({ holds: 0, royal: 400, scramble: 10, pair: 4 }),
  Object.freeze({ holds: 1, royal: 250, scramble: 8, pair: 3 }),
  Object.freeze({ holds: 2, royal: 120, scramble: 6, pair: 2.5 }),
]);
/** How often a blank pull gets a word of sympathy. Not always, on purpose. */
const SYMPATHY = 0.45;

/* ----------------------------------------------------------------- strings */
/* Mirrored key for key into ArcademyHostService.NeutralLexicon. Exported as
 * DATA the way lex.js and impulse-control/lex.js do it, so the C# side can be
 * diffed against this table by a scratch script rather than against thirty
 * scattered call sites (trap 123). Two rules when you add a row: under 96
 * characters, or `MergeModTable` drops it and no mod can ever re-voice it
 * (trap 26); and no em-dashes and no raw newlines, both sides of the seam. */
export const TT_LEX = Object.freeze({
  bk_tt_title:      'Triple Trigger',
  bk_tt_sub:        'reel one, reel two, reel three. your words, in your order.',
  bk_tt_window:     'the three reels',
  /* {0} the reel number, 1 to 3 */
  bk_tt_reel_aria:  'reel {0}',
  bk_tt_hold:       'hold',
  /* {0} the reel number */
  bk_tt_hold_aria:  'hold reel {0} where it is',
  bk_tt_pull:       'hold to pull',
  bk_tt_pull_tap:   'pull',
  /* {0} the base stake, {1} what one hold adds */
  bk_tt_price:      '{0} chips a pull, {1} more a hold.',
  /* {0} what this pull will cost */
  bk_tt_stake:      'this pull: {0} chips',
  bk_tt_hold_wait:  'Nothing to hold yet. Pull once and the reels will remember.',
  bk_tt_glyph_note: 'A spiral is a blank. It pays nothing and it is never worth holding.',

  /* ---- the odds sheet. INPUT HONESTY: this is the real table. ---- */
  bk_tt_odds:       'What it pays',
  bk_tt_col_holds:  'holds',
  bk_tt_col_royal:  'royal',
  bk_tt_col_scr:    'scramble',
  bk_tt_col_pair:   'pair',
  /* {0} the multiplier */
  bk_tt_pay_royal:  'ROYAL is one trigger across all three reels. {0}x and the pot.',
  bk_tt_pay_scr:    'SCRAMBLE is three different triggers and no blanks. {0}x.',
  bk_tt_pay_pair:   'PAIR is two reels on the same trigger. {0}x.',
  /* {0} the base stake */
  bk_tt_pay_base:   'Every multiplier is on the {0} chip base. Holds are never multiplied.',
  /* {0} the denominator */
  bk_tt_odds_cell:  '1 in {0}',

  /* ---- the pull ---- */
  bk_tt_rolling:    'Reels are turning.',
  /* {0} the trigger, read across all three reels */
  bk_tt_royal:      'ROYAL. {0}.',
  /* {0} chips out of the pot */
  bk_tt_royal_pot:  'And the pot came with it. {0} chips.',
  bk_tt_scramble:   'I like that one.',
  /* {0} chips paid */
  bk_tt_pair:       'Two of a kind. {0} chips back.',
  bk_tt_none:       'Nothing on the line that time.',
  /* {0} chips paid */
  bk_tt_paid:       'Paid {0} chips.',
  bk_tt_insured:    'Your insurance chip caught that one. Nothing lost.',
  bk_tt_stuck:      'The reels came down on their own. Nothing was staked.',

  /* ---- refusals. Warm, every one of them. ---- */
  bk_tt_poor:       'Not enough chips for that pull. The cage is out on the floor.',
  bk_tt_bad_stake:  'The machine would not take that stake. Nothing moved.',
  bk_tt_bad_input:  'The machine lost the thread of that one. Pull it again.',
  bk_tt_busy:       'Still settling the last one. Give it a breath.',
  bk_tt_offline:    'The line to the floor is down. Nothing was staked.',
  bk_tt_locked:     'The bank does not serve this account yet. Nothing was charged.',
  bk_tt_refused:    'The machine would not take that pull. Nothing moved.',
  bk_tt_dark:       'The reels are unplugged. Come back when the line is up.',
});

/** lex.js's `makeT`, wrapped around this table instead of the floor's. A key
 *  with no host row behind it still renders its authored English; a rig with no
 *  `t` at all renders the table straight. */
function makeTT(t) {
  const base = (typeof t === 'function') ? t : null;
  return function tt(k, args) {
    const en = TT_LEX[k];
    const raw = base ? base(k, en == null ? k : en) : (en == null ? k : en);
    return args ? fill(raw, args) : raw;
  };
}

/* ------------------------------------------------------------------- parts */
function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** index.js's three-test still mode, repeated rather than imported: the kit
 *  usually carries the answer, but a bench that builds its own kit does not,
 *  and a reduced-motion player must not depend on who mounted the cabinet. */
function stillMode(kit, ctx) {
  try { if (kit && kit.reduced) return true; } catch (e) { /* noop */ }
  try { if (ctx && ctx.motion && ctx.motion.reducedMotion) return true; } catch (e) { /* noop */ }
  try {
    if (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches) return true;
  } catch (e) { /* noop */ }
  try { return document.documentElement.classList.contains('arc-reduced'); } catch (e) { return false; }
}

function dietMode(kit) {
  try { if (kit && kit.lite) return true; } catch (e) { /* noop */ }
  try {
    const c = document.documentElement.classList;
    return c.contains('ae-lite') || c.contains('arc-mobile');
  } catch (e) { return false; }
}

/** index.js's lazy sheet, id-guarded so a second visit costs nothing. */
function ensureSheet(log) {
  try {
    if (document.getElementById('arc-backroom-triple-css')) return;
    const link = document.createElement('link');
    link.id = 'arc-backroom-triple-css';
    link.rel = 'stylesheet';
    link.href = new URL('./triple.css', import.meta.url).href;
    document.head.appendChild(link);
  } catch (e) { log('backroom: triple.css would not link'); }
}

/**
 * The paytable, live frame first and the published floor behind it.
 *
 * `config.paytables.triple` is read LOOSELY on purpose. This page ships before
 * the route does, and the one thing that must not happen is a machine showing a
 * blank sheet because a field arrived under a slightly different name. A number
 * under 1 is read as a probability and turned into a denominator, so `0.0025`
 * and `400` both print "1 in 400".
 */
function readPay(cfg) {
  const c = (cfg && typeof cfg === 'object') ? cfg : {};
  const stakes = (c.stakes && typeof c.stakes === 'object') ? c.stakes : {};
  const tp = (c.paytables && typeof c.paytables === 'object' && c.paytables.triple) || {};
  const num = (v, fb) => (Number(v) > 0 ? Number(v) : fb);
  const den = (v, fb) => {
    const n = Number(v);
    if (!(n > 0)) return fb;
    return n < 1 ? (1 / n) : n;
  };
  const base = Math.round(num(stakes.triple, num(tp.stake, STAKE_FLOOR)));
  const hold = Math.round(num(tp.hold, num(tp.holdCost, HOLD_FLOOR)));
  const mult = {
    royal: num(tp.royal && tp.royal.mult, num(tp.royalMult, MULT_FLOOR.royal)),
    scramble: num(tp.scramble && tp.scramble.mult, num(tp.scrambleMult, MULT_FLOOR.scramble)),
    pair: num(tp.pair && tp.pair.mult, num(tp.pairMult, MULT_FLOOR.pair)),
  };
  const src = Array.isArray(tp.rows) ? tp.rows : null;
  const rows = ODDS_FLOOR.map((floorRow, i) => {
    const r = (src && src[i] && typeof src[i] === 'object') ? src[i] : {};
    return {
      holds: floorRow.holds,
      royal: den(r.royal, floorRow.royal),
      scramble: den(r.scramble, floorRow.scramble),
      pair: den(r.pair, floorRow.pair),
    };
  });
  return { base, hold, mult, rows };
}

/** '1 in 2.5', never '1 in 2.5000000001'. */
function odd(n) {
  const v = Number(n) || 0;
  return (Math.abs(v - Math.round(v)) < 0.05) ? String(Math.round(v)) : v.toFixed(1);
}
/** 1.2 prints as 1.2 and 100 prints as 100. A trailing ".0" on a multiplier
 *  reads like a rounding error rather than like a payout. */
function mul(n) {
  const v = Number(n) || 0;
  return (Math.abs(v - Math.round(v)) < 0.005) ? String(Math.round(v)) : String(Math.round(v * 100) / 100);
}

/* The one live instance, so the floor's module-level `unmount()` folds the
 * cabinet that is actually up rather than nothing at all. */
let live = null;

/**
 * mount(root, kit, ctx) -> { unmount() }
 *
 * `kit` is the floor's shared surface (backroom/index.js). Every field is read
 * defensively, because this module is also mounted by a fixture bench that has
 * a pretend cashier and very little else.
 */
export function mount(root, kit, ctx) {
  if (live) { try { live.unmount(); } catch (e) { /* the old one is going anyway */ } }

  const k = kit || {};
  const c = ctx || {};
  const t = makeTT(c.t);
  const log = (typeof k.log === 'function') ? k.log : () => {};
  const sfx = (typeof k.sfx === 'function') ? k.sfx : () => {};
  const fmt = (typeof k.fmtChips === 'function') ? k.fmtChips : ((n) => String(n));
  const api = k.api || null;
  const reduced = stillMode(k, c);
  const lite = dietMode(k);
  const wired = () => !!(api && typeof api.wired === 'function' && api.wired() && typeof api.play === 'function');

  ensureSheet(log);

  let dead = false;
  let busy = false;
  let spun = false;                    // has a pull landed? a hold means nothing before one
  let pay = readPay(typeof k.config === 'function' ? k.config() : null);
  const held = [false, false, false];
  const timers = new Set();

  function later(fn, ms) {
    const h = setTimeout(() => { timers.delete(h); if (!dead) fn(); }, ms);
    timers.add(h);
    return h;
  }

  /* ------------------------------------------------------------------ dom */
  const box = el('div', 'bk-tt');
  const head = el('div', 'bk-tt-head');
  head.appendChild(el('h3', 'bk-tt-title', t('bk_tt_title')));
  head.appendChild(el('span', 'bk-tt-sub', t('bk_tt_sub')));
  head.appendChild(el('span', 'bk-tt-spacer'));
  /* THE WAY BACK IS BUILT BEFORE THE MACHINE IS, and it is never disabled: not
   * while a stake is in flight, not while the reels are turning (Law VI). */
  const back = el('button', 'bk-tt-back', (typeof k.t === 'function') ? k.t('bk_back') : 'back to the floor');
  back.type = 'button';
  back.addEventListener('click', () => {
    try { if (typeof k.toFloor === 'function') k.toFloor(); }
    catch (e) { log('backroom: triple could not step back'); }
  });
  head.appendChild(back);
  box.appendChild(head);

  const stage = el('div', 'bk-tt-stage');
  const cab = el('div', 'bk-tt-cab');
  stage.appendChild(cab);
  box.appendChild(stage);

  const reel = createReelWindow({
    reduced, lite, sfx, log,
    labels: { window: t('bk_tt_window'), reel: (n) => t('bk_tt_reel_aria', [n]) },
    // The ceiling fired without an answer. The reels are already parked; all
    // that is left to do for the player is hand the buttons back and say so.
    onCeiling: () => { busy = false; lock(false); tell(t('bk_tt_stuck')); },
  });
  cab.appendChild(reel.el);

  const holdBar = el('div', 'bk-tt-holds');
  const holdBtns = [];
  for (let i = 0; i < REELS; i++) {
    const b = el('button', 'bk-tt-hold-btn', t('bk_tt_hold'));
    b.type = 'button';
    b.disabled = true;
    b.setAttribute('aria-pressed', 'false');
    b.setAttribute('aria-label', t('bk_tt_hold_aria', [i + 1]));
    b.addEventListener('click', () => toggleHold(i));
    holdBar.appendChild(b);
    holdBtns.push(b);
  }
  cab.appendChild(holdBar);

  const price = el('div', 'bk-tt-price');
  const priceBase = el('span');
  const priceNow = el('b');
  const priceNote = el('span', 'bk-tt-note');
  price.appendChild(priceBase);
  price.appendChild(priceNow);
  price.appendChild(priceNote);
  cab.appendChild(price);

  /* THE LEVER. A press and hold on a desk, a tap on a phone. The ring is the
   * confirm this room uses instead of a dialog, and a dialog is the one thing a
   * casino must never put between a player and the door. But a nine hundred
   * millisecond press with a thumb on a moving bus is a gesture that fails, so
   * the diet gets the tap and keeps the same commit. */
  const pull = el('button', 'bk-tt-pull' + (lite ? '' : ' bk-hold'), lite ? t('bk_tt_pull_tap') : t('bk_tt_pull'));
  pull.type = 'button';
  pull.disabled = true;                // dead until the reels have words on them
  cab.appendChild(pull);
  let ring = null;
  if (lite) pull.addEventListener('click', () => doPull());
  else ring = holdRing(pull, { reduced, sfx, onCommit: () => doPull() });

  const say = el('div', 'bk-tt-say');
  say.setAttribute('role', 'status');
  cab.appendChild(say);

  const sheet = el('div', 'bk-tt-odds');
  stage.appendChild(sheet);

  const drop = createDropBeat({ host: cab, voice: k.voice, sfx, reduced, lite, log });

  /* ---------------------------------------------------------------- paint */

  function heldCount() { return held.reduce((n, h) => n + (h ? 1 : 0), 0); }
  function stakeNow() { return pay.base + (pay.hold * heldCount()); }

  /** The sheet is the SERVER's sheet, re-read on every repaint: the status
   *  frame can land after the cabinet is already open, and an odds table one
   *  frame stale is an odds table that is not honest. */
  function refreshPay() {
    try { pay = readPay(typeof k.config === 'function' ? k.config() : null); }
    catch (e) { /* the floor stands */ }
  }

  function paintPrice() {
    refreshPay();
    priceBase.textContent = t('bk_tt_price', [fmt(pay.base), fmt(pay.hold)]) + ' ';
    priceNow.textContent = t('bk_tt_stake', [fmt(stakeNow())]);
    priceNote.textContent = spun ? t('bk_tt_glyph_note') : t('bk_tt_hold_wait');
  }

  function paintSheet() {
    sheet.textContent = '';
    sheet.appendChild(el('h4', null, t('bk_tt_odds')));
    const table = document.createElement('table');
    const thead = document.createElement('thead');
    const hr = document.createElement('tr');
    for (const col of ['bk_tt_col_holds', 'bk_tt_col_royal', 'bk_tt_col_scr', 'bk_tt_col_pair']) {
      const th = document.createElement('th');
      th.textContent = t(col);
      hr.appendChild(th);
    }
    thead.appendChild(hr);
    table.appendChild(thead);
    const tbody = document.createElement('tbody');
    const now = heldCount();
    for (const r of pay.rows) {
      const tr = document.createElement('tr');
      if (r.holds === now) tr.className = 'bk-tt-now';
      const cells = [String(r.holds), t('bk_tt_odds_cell', [odd(r.royal)]),
        t('bk_tt_odds_cell', [odd(r.scramble)]), t('bk_tt_odds_cell', [odd(r.pair)])];
      for (const v of cells) {
        const td = document.createElement('td');
        td.textContent = v;
        tr.appendChild(td);
      }
      tbody.appendChild(tr);
    }
    table.appendChild(tbody);
    sheet.appendChild(table);
    sheet.appendChild(el('p', null, t('bk_tt_pay_royal', [mul(pay.mult.royal)])));
    sheet.appendChild(el('p', null, t('bk_tt_pay_scr', [mul(pay.mult.scramble)])));
    sheet.appendChild(el('p', null, t('bk_tt_pay_pair', [mul(pay.mult.pair)])));
    sheet.appendChild(el('p', null, t('bk_tt_pay_base', [fmt(pay.base)])));
  }

  function toggleHold(i) {
    if (dead || busy || !spun) return;
    held[i] = !held[i];
    holdBtns[i].setAttribute('aria-pressed', held[i] ? 'true' : 'false');
    sfx('pip', 0.2, { pitch: held[i] ? 1.18 : 0.86 });
    paintPrice();
    paintSheet();
  }

  function lock(on) {
    pull.disabled = on || !wired() || !reel.rows().length;
    for (let i = 0; i < REELS; i++) holdBtns[i].disabled = on || !spun;
  }

  /** The reading first, then the receipt, then the dealer under both. On a
   *  scramble the sentence IS what the player came for, and the cage's law
   *  still holds: the small talk never sits above the thing being read. */
  function tell(text, dealer, warm, read) {
    say.textContent = '';
    say.classList.toggle('bk-warm', !!warm);
    if (read) say.appendChild(el('span', 'bk-tt-read', read));
    if (text) say.appendChild(el('span', null, text));
    if (dealer) say.appendChild(el('div', 'bk-dealer', dealer));
  }

  function dealerLine(bucket) {
    try { return (k.voice && typeof k.voice.line === 'function') ? k.voice.line(bucket) : ''; }
    catch (e) { return ''; }
  }

  /* ----------------------------------------------------------------- play */

  function doPull() {
    if (dead || busy) return;
    if (!wired()) { tell(t('bk_tt_dark')); sfx('bump', 0.28); return; }
    if (!reel.rows().length) return;
    busy = true;
    lock(true);
    const stake = stakeNow();
    /* The wire body, contract §5: which reels stay, and how long the list the
     * kit built is, so the server draws indices into the same reel the player
     * is looking at. */
    const input = { holds: held.slice(), n: reel.rows().length };
    tell(t('bk_tt_rolling'), dealerLine('deal'));
    sfx('tell', 0.22);
    reel.startRoll(held);
    api.play('triple', stake, input).then((r) => {
      if (dead) return;
      if (!r || !r.ok) {
        reel.stop();
        reel.park();
        busy = false;
        lock(false);
        tell(refusal(r));
        sfx('bump', 0.28);
        return;
      }
      const body = (r.body && typeof r.body === 'object') ? r.body : {};
      const res = (body.result && typeof body.result === 'object') ? body.result : {};
      const list = Array.isArray(res.reels) ? res.reels : [-1, -1, -1];
      reel.settle(list).then(() => { if (!dead) finish(body, res, stake); });
    });
  }

  /** The reels are down and the answer is in hand. Everything below is the
   *  BEAT: the chips, the light, and what she says about it. */
  function finish(body, res, stake) {
    busy = false;
    spun = true;
    const payout = Math.max(0, Math.round(Number(body.payout) || 0));
    const line = String(res.line || 'none');

    /* LEDGER TRUTH: the room's balance moves here and nowhere else, and the
     * chips fly out of the WINDOW so the money is seen to come from the machine
     * rather than to appear in the header. */
    try { if (typeof k.settle === 'function') k.settle(body, payout > 0 ? reel.el : null); }
    catch (e) { log('backroom: triple could not settle'); }

    reel.el.classList.toggle('bk-tt-hit', payout > 0);
    reel.el.classList.toggle('bk-tt-royal', line === 'royal');

    const insured = (body.insured === true) ? t('bk_tt_insured') + ' ' : '';
    if (line === 'royal') royal(res, payout);
    else if (line === 'scramble') {
      tell(t('bk_tt_scramble'), dealerLine(payout >= stake * 5 ? 'big' : 'win'), true, reel.sentence());
      sfx('reveal', 0.34);
    } else if (line === 'pair') {
      tell(insured + t('bk_tt_pair', [fmt(payout)]), dealerLine('win'), true);
      sfx('chime', 0.3);
    } else {
      // SYMPATHY, AND NOT EVERY TIME. A warm word after every single blank pull
      // stops being warmth and becomes a script the player can hear working.
      tell(insured + t('bk_tt_none'), (Math.random() < SYMPATHY) ? dealerLine('loss') : '');
      sfx('slide', 0.2);
    }

    lock(false);
    paintPrice();
    paintSheet();
  }

  /**
   * The once-in-a-few-hundred pull: all three reels on the same trigger, so the
   * machine is reading the player's own phrase back at them. The drop beat is
   * this room's way of holding a moment, and it is `pointer-events:none` for
   * every millisecond of it, so the way out never closes (Law VI).
   */
  function royal(res, payout) {
    const potWon = Math.max(0, Math.round(Number(res.potWon) || 0));
    const phrase = reel.phraseAt(0).toUpperCase();
    let text = t('bk_tt_royal', [phrase]) + ' ' + t('bk_tt_paid', [fmt(payout)]);
    if (potWon > 0) text += ' ' + t('bk_tt_royal_pot', [fmt(potWon)]);
    tell(text, dealerLine('royal'), true);
    sfx('jackpot', 0.5);
    try { drop.run({ ms: 1400, text: phrase }); }
    catch (e) { log('backroom: the royal beat would not play'); }
  }

  /** Every refusal reads warmly. The house could not honour a pull and says so;
   *  it never tells a player off for being short. */
  function refusal(r) {
    const body = (r && r.body) || {};
    const reason = String(body.reason || body.code || '');
    if (r && r.status === 403) return t('bk_tt_locked');
    switch (reason) {
      case 'insufficient_chips': return t('bk_tt_poor');
      case 'invalid_stake': return t('bk_tt_bad_stake');
      case 'invalid_input': return t('bk_tt_bad_input');
      case 'busy': return t('bk_tt_busy');
      case 'arcademy_locked': return t('bk_tt_locked');
      case 'offline':
      case 'timeout':
      case 'closed': return t('bk_tt_offline');
      default: return t('bk_tt_refused');
    }
  }

  /* -------------------------------------------------------------- opening */
  paintPrice();
  paintSheet();
  if (!wired()) tell(t('bk_tt_dark'));
  try { root.appendChild(box); } catch (e) { log('backroom: triple has no stage'); }

  /* The reels are built from whatever the host knows about the player. This
   * resolves to the authored rows on a host with no trigger store, so there is
   * no failure path here, only the fallback path, which is the same path. */
  reelRows(api, { t: c.t }).then((list) => {
    if (dead) return;
    reel.setRows(list);
    lock(false);
  });

  // The floor asks the counter as it opens, so `config` usually beats the
  // cabinet here. One late repaint covers the evening it does not.
  later(() => { paintPrice(); paintSheet(); }, 1200);

  const handle = {
    /** Safe from any road, and safe twice: mid roll, mid settle, mid drop, with
     *  a stake in flight. The answer still on the wire resolves into a dead
     *  instance and is dropped, which is correct: the ledger has already moved
     *  and the floor reads it on its next status frame. */
    unmount() {
      if (dead) return;
      dead = true;
      for (const h of timers) { try { clearTimeout(h); } catch (e) { /* noop */ } }
      timers.clear();
      try { reel.destroy(); } catch (e) { /* noop */ }
      try { if (ring) ring.destroy(); } catch (e) { /* noop */ }
      try { drop.destroy(); } catch (e) { /* noop */ }
      try { box.remove(); } catch (e) { /* noop */ }
      if (live === handle) live = null;
    },
  };
  live = handle;
  return handle;
}

/** The floor calls this on the module as well as on the handle. It folds the
 *  cabinet that is actually up, and it is a no-op when there is none. */
export function unmount() {
  if (live) { try { live.unmount(); } catch (e) { /* noop */ } }
}

export const title = TT_LEX.bk_tt_title;

export default { key, title, mount, unmount, TT_LEX };
