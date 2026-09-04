/* ============================================================================
 * backroom/wheel.js - THE PRIZE WHEEL, the loudest thing on the floor.
 *
 * Five hundred chips buys one spin at seven wedges, two of which are worth
 * waiting for. The server picks the wedge; this module only travels there.
 *
 * THE WIDTHS ARE THE ODDS, AND THAT IS WHY THERE ARE FIFTY SLICES. The face
 * component draws every slice the same size (kit/loomwheel.js, `TAU /
 * wedges.length`), so a seven-entry face would show a two-percent wedge and a
 * thirty-six-percent wedge as identical pie slices. That is a lie a player
 * cannot even see being told. So the face is handed FIFTY slices instead, one
 * per two percent, in seven contiguous runs, and only the middle slice of each
 * run carries a label (buildFace skips an empty one). The arc a wedge occupies
 * is then exactly its published weight, and the gold rule the face strokes
 * between slices turns into a countable odds mark: every tick is two spins in
 * a hundred. INPUT HONESTY costs forty-three extra slices and buys a face a
 * player can audit at a glance.
 *
 * WHERE IT STOPS IS NOT DECIDED HERE. `body.result.wedge` is the server's, and
 * the only thing this file computes is which slice of that run sits under the
 * pin. No rng, no near-miss, no nudge.
 *
 * NO CHIP ARITHMETIC LIVES HERE (LEDGER TRUTH). `kit.settle` folds the answer's
 * own balances in once, when the wheel has stopped and the card has been read.
 * ==========================================================================*/

import { fill } from './lex.js';
import { createLoomWheel } from './kit/loomwheel.js';

/** Every string this cabinet prints, mirrored key for key into
 *  `ArcademyHostService.NeutralLexicon`. Under 96 characters each, or a mod
 *  can never re-voice the row (trap 26). No em-dashes, no raw newlines. */
export const PW_LEX = Object.freeze({
  bk_pw_title:     'The Prize Wheel',
  bk_pw_lede:      'Seven wedges. The widths are the odds, so count the ticks.',
  bk_pw_spin:      'SPIN {0} chips',                  /* {0} the stake */
  bk_pw_spinning:  'Spinning.',
  bk_pw_ready:     'Five hundred chips a spin.',
  /* the face. short, because a label rides its own spoke */
  bk_pw_w_250:     '250 chips',
  bk_pw_w_500:     '500 chips',
  bk_pw_w_card:    'Scratch card',
  bk_pw_w_ins:     'Insurance',
  bk_pw_w_1000:    '1,000 chips',
  bk_pw_w_late:    'Late Slip',
  bk_pw_w_visor:   "Dealer's Visor",
  /* the card the pin caught, one whole sentence each */
  bk_pw_chips:     '{0} chips.',                      /* {0} the payout */
  bk_pw_it_card:   'A scratch card.',
  bk_pw_it_ins:    'An insurance chip.',
  bk_pw_it_late:   'A late slip.',
  bk_pw_it_visor:  "A Dealer's Visor.",
  bk_pw_counter:   'It is waiting at the Prize Counter.',
  bk_pw_full:      'Your shelf is full, so the house paid {0} instead.',
  /* the published table */
  bk_pw_odds:      'the wheel, wedge by wedge',
  bk_pw_odds_row:  '{0}, {1} spins in 100',           /* {0} wedge {1} weight */
  bk_pw_ticks:     'every gold tick on the rim is two spins in a hundred.',
  /* refusals, warm, and every one says that nothing moved */
  bk_pw_poor:      'Not enough chips for a spin. The cage is just there.',
  bk_pw_offline:   'The line to the counter is down. Nothing was charged.',
  bk_pw_busy:      'The wheel is still settling for someone else.',
  bk_pw_locked:    'The bank does not serve this account yet. Nothing was charged.',
  bk_pw_refused:   'The house would not take that spin. Nothing moved.',
});

/** The seven wedges, in the order the server numbers them. `weight` is out of
 *  one hundred and is the ONLY number here a status frame may overrule: the
 *  names are copy and copy belongs to the lexicon, but the odds belong to the
 *  wire. Colours are literals because the face is a canvas and a token cannot
 *  reach one; they alternate so seven runs read as seven wedges. */
const WHEEL = Object.freeze([
  { label: 'bk_pw_w_250',   weight: 36, kind: 'chips', sku: null,           color: '#2b2450' },
  { label: 'bk_pw_w_500',   weight: 20, kind: 'chips', sku: null,           color: '#3a2a55' },
  { label: 'bk_pw_w_card',  weight: 18, kind: 'item',  sku: 'bk_scratcher', color: '#243f52' },
  { label: 'bk_pw_w_ins',   weight: 12, kind: 'item',  sku: 'bk_insurance', color: '#3a2a55' },
  { label: 'bk_pw_w_1000',  weight: 8,  kind: 'chips', sku: null,           color: '#2b2450' },
  { label: 'bk_pw_w_late',  weight: 4,  kind: 'item',  sku: 'late_slip',    color: '#4a2f3f' },
  { label: 'bk_pw_w_visor', weight: 2,  kind: 'item',  sku: 'bk_visor',     color: '#6b4a12' },
]);
/** One slice per this many percent. Two is the coarsest number that still
 *  renders the smallest wedge as a whole slice, and fifty slices is about as
 *  many as a 320px face can stroke before the rim goes solid gold. */
const PER_SLICE = 2;
/** What a sku is called once it is in the player's hands. */
const ITEM_LINE = Object.freeze({
  bk_scratcher: 'bk_pw_it_card', bk_insurance: 'bk_pw_it_ins',
  late_slip: 'bk_pw_it_late', bk_visor: 'bk_pw_it_visor',
});

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

/** The cabinet's own resolver. `kit.t` falls back to BK_LEX and then to the
 *  KEY, and none of these rows are in BK_LEX, so a bare `kit.t` would paint
 *  `bk_pw_counter` on the prize card. A host row wins; this table is the floor. */
function makeT(kit) {
  const base = (kit && typeof kit.t === 'function') ? kit.t : null;
  return function pwT(key, args) {
    let raw = PW_LEX[key] == null ? key : PW_LEX[key];
    try { const got = base ? base(key) : null; if (got && got !== key) raw = got; }
    catch (e) { /* a dead lexicon still prints English */ }
    return args ? fill(raw, args) : raw;
  };
}

/** The published weights, server first, the table above when the wire is
 *  silent or malformed rather than a number nobody can stand behind. Only the
 *  weights move: a paytable that renamed the wedges would be a paytable
 *  writing copy, and copy is the lexicon's job. */
function weights(kit) {
  let p = null;
  try { p = (kit.config().paytables || {}).wheel; } catch (e) { p = null; }
  if (!Array.isArray(p) || p.length !== WHEEL.length) return WHEEL;
  const rows = WHEEL.map((w, i) => {
    const row = p[i];
    const n = Math.round(Number(row && row.weight != null ? row.weight : row) || 0);
    return n > 0 ? Object.assign({}, w, { weight: n }) : null;
  });
  return rows.every(Boolean) ? rows : WHEEL;
}

/**
 * The face. Returns the fifty-odd slices AND, for each wedge, the slice that
 * sits under the pin when that wedge wins: its run's middle, so the wedge is
 * centred rather than caught by an edge.
 */
function buildSlices(rows, t) {
  const slices = [];
  const mid = [];
  for (let i = 0; i < rows.length; i++) {
    const w = rows[i];
    const n = Math.max(1, Math.round(w.weight / PER_SLICE));
    const start = slices.length;
    mid.push(start + Math.floor((n - 1) / 2));
    for (let k = 0; k < n; k++) {
      // Only the middle slice speaks. buildFace skips an empty label, so the
      // rest of a run is pure width, which is the whole point of them.
      slices.push({ label: (start + k === mid[i]) ? t(w.label) : '', color: w.color });
    }
  }
  return { slices, mid };
}

/** Which warm sentence a refusal gets. The server's word is never shown raw. */
function refusalKey(status, body) {
  if (status === 403) return 'bk_pw_locked';
  if (!status) return 'bk_pw_offline';
  const r = String((body && (body.reason || body.error)) || '');
  if (r === 'insufficient_chips') return 'bk_pw_poor';
  return r === 'busy' ? 'bk_pw_busy' : 'bk_pw_refused';
}

/** The phone diet. `kit.lite` is the ctx flag the shell projects, but the diet
 *  ITSELF is a class on <html>, and a rig or a web host can arm one without
 *  the other. Both count. */
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
 * away, which is how leaving mid-spin stays legal (Law VI).
 */
export function mount(root, kit, ctx) {
  const t = makeT(kit);
  const log = (typeof kit.log === 'function') ? kit.log : () => {};
  const sfx = (typeof kit.sfx === 'function') ? kit.sfx : () => {};
  const reduced = !!kit.reduced;
  const lite = liteMode(kit);
  ensureSheet('arc-bk-wheel-css', './wheel.css', log);

  let dead = false;
  let busy = false;
  let pending = null;    // the answer body waiting to be banked

  const rows = weights(kit);
  const face = buildSlices(rows, t);

  /* ------------------------------------------------------------ the cabinet */
  const wrap = el('div', 'bk-pw');
  // The wheel's own reflection of its face and its last landing. The odds copy
  // reads the first, the probe reads both, and neither is a number this file
  // decided: the slices come off the published weights and the wedge off the
  // server.
  wrap.dataset.slices = String(face.slices.length);
  const head = el('div', 'bk-pw-head');
  head.appendChild(el('h3', null, t('bk_pw_title')));
  head.appendChild(el('span', 'bk-pw-lede', t('bk_pw_lede')));
  const back = el('button', 'btn', kit.t('bk_back'));
  back.type = 'button';
  back.addEventListener('click', () => { try { kit.toFloor(); } catch (e) { /* noop */ } });
  head.appendChild(back);
  const stage = el('div', 'bk-pw-stage');
  wrap.appendChild(head);
  wrap.appendChild(stage);

  const host = el('div', 'bk-pw-face');
  stage.appendChild(host);

  /* THE SIDE: the button, the receipt, the dealer, the published table. */
  const side = el('div', 'bk-pw-side');
  const spin = el('button', 'bk-pw-spin');
  spin.type = 'button';
  const say = el('div', 'bk-pw-say', t('bk_pw_ready'));
  say.setAttribute('role', 'status');
  const card = el('div', 'bk-pw-card');
  const note = el('div', 'bk-pw-note');
  const dealer = el('div', 'bk-dealer');
  const odds = el('div', 'bk-pw-odds');
  odds.appendChild(el('span', 'bk-pw-odds-h', t('bk_pw_odds')));
  for (const w of rows) {
    odds.appendChild(el('div', 'bk-pw-odds-row', t('bk_pw_odds_row', [t(w.label), String(w.weight)])));
  }
  odds.appendChild(el('div', 'bk-pw-ticks', t('bk_pw_ticks')));
  side.appendChild(spin);
  side.appendChild(say);
  side.appendChild(card);
  side.appendChild(note);
  side.appendChild(dealer);
  side.appendChild(odds);
  stage.appendChild(side);
  root.appendChild(wrap);

  let wheel = null;
  try {
    wheel = createLoomWheel({
      host, wedges: face.slices, seed: 'backroom-prize-wheel',
      size: lite ? 240 : 320, reduced, lite, sfx, log,
    });
  } catch (e) { log('backroom: wheel face would not build'); }

  function stake() {
    const v = Number((kit.config().stakes || {}).wheel);
    return v > 0 ? Math.round(v) : 500;
  }

  /** The one place the button's face is decided, so it can never disagree with
   *  what a press is about to cost. */
  function paintSpin() {
    spin.disabled = busy || dead;
    spin.textContent = busy ? t('bk_pw_spinning') : t('bk_pw_spin', [kit.fmtChips(stake())]);
  }

  /**
   * The wheel has stopped, so NOW the receipt lands. The stake and the prize
   * arrived on one server frame, so they are shown on one beat rather than
   * faked as two events the server never sent.
   */
  function reveal(res, body) {
    if (dead) return;
    pending = null;
    const paid = Math.max(0, Math.round(Number(res.amount) || 0));
    const sku = res.sku ? String(res.sku) : '';
    const line = ITEM_LINE[sku];
    // A sku that paid CHIPS is the full-shelf case: the pin really did catch
    // the item, the stack was already at its ceiling, and the house bought it
    // back. It is named as what it was rather than quietly redrawn as a chip
    // wedge, because a player who saw the visor go by is owed the sentence.
    const spilled = !!sku && res.kind !== 'item';
    say.textContent = '';
    if (line) {
      card.textContent = t(line);
      note.textContent = spilled ? t('bk_pw_full', [kit.fmtChips(paid)]) : t('bk_pw_counter');
      note.classList.toggle('bk-pw-kept', !spilled);
    } else {
      card.textContent = t('bk_pw_chips', [kit.fmtChips(paid)]);
      note.textContent = '';
    }
    card.classList.add('bk-warm');
    sfx('chime', 0.34);
    dealer.textContent = kit.voice.line((sku === 'bk_visor' || paid >= 1000) ? 'big' : 'win');
    try { kit.settle(body, paid > 0 ? host : null); }
    catch (e) { log('backroom: wheel settle threw'); }
    busy = false;
    paintSpin();
  }

  /* --------------------------------------------------------------- the spin */
  function press() {
    if (dead || busy) return;
    busy = true;
    say.textContent = t('bk_pw_spinning');
    say.classList.remove('bk-warm');
    card.textContent = '';
    card.classList.remove('bk-warm');
    note.textContent = '';
    note.classList.remove('bk-pw-kept');
    dealer.textContent = kit.voice.line('deal');
    paintSpin();
    sfx('blip', 0.28);
    kit.api.play('wheel', stake(), {}).then((r) => {
      if (dead) return;
      const res = (r.ok && r.body && r.body.result) || null;
      if (!res || res.wedge == null) {
        say.textContent = t(refusalKey(r.status, r.body));
        say.classList.add('bk-warm');
        dealer.textContent = '';
        busy = false;
        paintSpin();
        return;
      }
      pending = r.body;
      const i = Math.max(0, Math.min(rows.length - 1, Math.round(Number(res.wedge) || 0)));
      wrap.dataset.wedge = String(i);
      if (!wheel) { reveal(res, r.body); return; }   // a faceless wheel still pays
      wheel.spinTo(face.mid[i], 3200).then(() => reveal(res, r.body));
    });
  }
  spin.addEventListener('click', press);
  paintSpin();

  /**
   * Down mid-spin is a legal way to leave, and the chips moved on the server
   * already: an unrevealed spin still banks its answer on the way out, or the
   * header would go on painting a balance the ledger has walked past.
   */
  function unmount() {
    if (dead) return;
    dead = true;
    if (live && live.unmount === unmount) live = null;
    const body = pending;
    pending = null;
    try { if (wheel) wheel.destroy(); } catch (e) { /* noop */ }
    try { if (body) kit.settle(body, null); } catch (e) { /* noop */ }
    try { wrap.remove(); } catch (e) { /* noop */ }
  }

  live = { unmount };
  return live;
}

/** The floor calls this after the instance's own, and a second call is free. */
export function unmount() { if (live) live.unmount(); }

export default { key: 'wheel', title: PW_LEX.bk_pw_title, mount, unmount, PW_LEX };
