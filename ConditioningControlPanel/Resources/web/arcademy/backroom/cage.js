/* ============================================================================
 * backroom/cage.js - THE CAGE, and the choice it makes you look at.
 *
 * Sparkle in, chips out, one hundred to one, ONE WAY. Nothing on this page can
 * turn chips back into sparkle, and the cage says so on the wall rather than
 * letting you discover it at the worst possible moment.
 *
 * THE TREE LINE IS THE POINT OF THIS FILE (owner ruling). The same sparkle that
 * buys chips down here buys skills upstairs, and a casino that quietly competes
 * with a progression tree for the same scarce currency is running a confidence
 * trick. So the counter prints the other price too, every time, in the same
 * breath: "the same 25 sparkle goes toward soft focus on the tree, 15 short of
 * it". The player then chooses, which is the only version of this worth
 * shipping.
 *
 * ONE PRESS COMMITS. There is no confirm dialog, because a modal over a casino
 * counter is a modal over the way out (Law VI). The guard is that the button
 * stays dead until the number is both sane and affordable, and the receipt
 * afterwards says exactly what moved.
 *
 * IT OWNS NO NUMBERS. `paint(snap)` is fed the server's last status frame and
 * `onSettled` hands the answer back up. Nothing in here adds a chip to anything.
 * ==========================================================================*/

import { fmtChips } from './kit/chips.js';

/** The stepper. 500 is a TYPO GUARD on one press, not a lid on the evening. */
export const STEPS = Object.freeze([1, 5, 10, 25, 50]);
export const CAGE_MAX = 500;
const RATE_FLOOR = 100;

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** 'soft_focus' -> 'soft focus'. A skill id is the only name the tree frame
 *  carries, and a raw snake_case id in a sentence reads like a bug. */
function prettyId(id) {
  return String(id == null ? '' : id).replace(/[_-]+/g, ' ').trim();
}

/**
 * createCage({ t, api, voice, sfx, log, onSettled }) ->
 *   { el, paint(snap), amount(), destroy() }
 *
 * `onSettled(body, fromNode)` is called with the answer's body when a pull
 * lands, and `fromNode` is the counter itself, so the floor can fly the chips
 * out of it. The cage never reaches for the header plate directly.
 */
export function createCage(opts) {
  const o = opts || {};
  const t = o.t;
  const api = o.api;
  const note = (typeof o.log === 'function') ? o.log : () => {};
  const sfx = (typeof o.sfx === 'function') ? o.sfx : () => {};
  const onSettled = (typeof o.onSettled === 'function') ? o.onSettled : () => {};

  let snap = null;
  let amount = STEPS[0];
  let busy = false;
  let dead = false;

  const root = el('div', 'bk-cage bk-dark');
  const head = el('div', 'bk-cage-head');
  head.appendChild(el('h3', null, t('bk_cage')));
  head.appendChild(el('span', 'bk-cage-rate', t('bk_cage_rate')));
  root.appendChild(head);

  const steps = el('div', 'bk-steps');
  steps.setAttribute('role', 'group');
  steps.setAttribute('aria-label', t('bk_cage'));
  const stepBtns = [];
  root.appendChild(steps);

  const custom = el('div', 'bk-custom');
  const customIn = el('input');
  customIn.type = 'number';
  customIn.min = '1';
  customIn.max = String(CAGE_MAX);
  customIn.setAttribute('aria-label', t('bk_cage_custom_lbl'));
  custom.appendChild(el('span', 'bk-cage-rate', t('bk_cage_custom')));
  custom.appendChild(customIn);
  root.appendChild(custom);

  const lines = el('div', 'bk-cage-lines');
  const lineHand = el('div');
  const lineGet = el('div');
  const lineTree = el('div', 'bk-tree');
  lines.appendChild(lineHand);
  lines.appendChild(lineGet);
  lines.appendChild(lineTree);
  root.appendChild(lines);

  const go = el('button', 'bk-cage-go', t('bk_cage_go'));
  go.type = 'button';
  root.appendChild(go);

  const receipt = el('div', 'bk-cage-say');
  receipt.setAttribute('role', 'status');
  root.appendChild(receipt);

  for (const n of STEPS) {
    const b = el('button', 'bk-step', String(n));
    b.type = 'button';
    b.setAttribute('aria-pressed', n === amount ? 'true' : 'false');
    b.addEventListener('click', () => { customIn.value = ''; setAmount(n); sfx('pip', 0.22); });
    steps.appendChild(b);
    stepBtns.push({ n, b });
  }
  customIn.addEventListener('input', () => setAmount(Number(customIn.value) || 0));

  function setAmount(n) {
    amount = Math.max(0, Math.min(CAGE_MAX, Math.round(Number(n) || 0)));
    for (const s of stepBtns) s.b.setAttribute('aria-pressed', s.n === amount ? 'true' : 'false');
    paint();
  }

  /** Everything the counter shows, from the last frame and the current number.
   *  Pure: it asks the server nothing and it moves nothing. */
  function paint(next) {
    if (next !== undefined) snap = next;
    const sparkle = snap ? Math.max(0, Math.round(Number(snap.sparkle) || 0)) : 0;
    const rate = (snap && Number(snap.rate) > 0) ? Math.round(Number(snap.rate)) : RATE_FLOOR;

    lineHand.textContent = '';
    lineHand.appendChild(el('span', null, t('bk_cage_hand') + ': '));
    lineHand.appendChild(el('b', null, fmtChips(sparkle)));
    lineGet.textContent = '';
    lineGet.appendChild(el('span', null, t('bk_cage_get') + ': '));
    lineGet.appendChild(el('b', null, fmtChips(amount * rate)));

    const tree = (snap && snap.tree && typeof snap.tree === 'object') ? snap.tree : null;
    const cheap = tree && tree.cheapest_unowned;
    if (!tree) lineTree.textContent = '';
    else if (!cheap) lineTree.textContent = t('bk_cage_tree_done');
    else {
      const cost = Math.max(0, Math.round(Number(cheap.cost) || 0));
      const name = prettyId(cheap.id);
      lineTree.textContent = (amount >= cost)
        ? t('bk_cage_tree_buy', [fmtChips(amount), name])
        : t('bk_cage_tree', [fmtChips(amount), name, fmtChips(cost - amount)]);
    }

    const wired = !!(api && api.wired && api.wired());
    root.classList.toggle('bk-dark', !wired);
    if (!wired) { receipt.textContent = t('bk_cage_dark'); receipt.classList.remove('bk-warm'); }
    go.disabled = dead || !wired || busy || amount < 1 || amount > CAGE_MAX || amount > sparkle;
  }

  /** One press, one id, one receipt. */
  function pull() {
    if (dead || busy || !(api && api.wired && api.wired())) return;
    const n = amount;
    if (!(n >= 1 && n <= CAGE_MAX)) { warm(t('bk_cage_bad')); return; }
    busy = true;
    go.disabled = true;
    receipt.classList.remove('bk-warm');
    receipt.textContent = t('bk_cage_working');
    sfx('commit', 0.36);
    api.cage(n).then((r) => {
      if (dead) return;
      busy = false;
      if (r.ok) {
        const credited = Math.max(0, Math.round(Number(r.body.credited) || (n * RATE_FLOOR)));
        // The floor folds the new balances in and flies the chips out of HERE,
        // so the money is seen to come from the cashier rather than to appear.
        onSettled(r.body, root);
        warm(t('bk_cage_done', [fmtChips(credited)]));
        dealer();
        sfx('jackpot', 0.3);
      } else {
        receipt.textContent = refusal(r);
        receipt.classList.remove('bk-warm');
        sfx('bump', 0.28);
        paint();
      }
    });
  }
  go.addEventListener('click', pull);

  function warm(text) { receipt.textContent = text; receipt.classList.add('bk-warm'); }

  /** The dealer speaks UNDER the receipt. The number is what the player came to
   *  read; she talks over the top of it, never instead of it. */
  function dealer() {
    try {
      const line = o.voice ? o.voice.line('cage') : '';
      if (line) receipt.appendChild(el('div', 'bk-dealer', line));
    } catch (e) { note('backroom: dealer line failed'); }
  }

  /** Every refusal reads warmly. The house never scolds a player for being poor. */
  function refusal(r) {
    const body = (r && r.body) || {};
    const reason = String(body.reason || body.code || '');
    if (reason === 'casino_closed') return t('bk_closed');   // a 403 too, but the house's, not the account's
    if (r && r.status === 403) return t('bk_cage_locked');
    switch (reason) {
      case 'insufficient_sparkle': return t('bk_cage_poor');
      case 'invalid_amount': return t('bk_cage_bad');
      case 'busy': return t('bk_cage_busy');
      case 'force_skills_reset': return t('bk_cage_reset');
      case 'arcademy_locked': return t('bk_cage_locked');
      case 'offline':
      case 'timeout':
      case 'closed': return t('bk_cage_offline');
      default: return t('bk_cage_refused');
    }
  }

  paint(null);

  return {
    el: root,
    paint,
    amount: () => amount,
    /** Goes dead and stays dead. Safe from any road, and safe twice. */
    destroy() { dead = true; go.disabled = true; },
  };
}

export default createCage;
