/* ============================================================================
   shell/lever.js - THE EXTRA CREDIT LEVER, in one place.

   The lever is a wager on a graded run: Standard pays the usual, Extra Credit
   pays half again and asks more, Honors pays double and is the only road to an
   S plus. It has to be reachable from BOTH doorways into a class - the campus
   door card and, for the rooms that have a painted set, the room scene's apron
   - or a player who only ever enters through the painted door would never see
   it exist.

   Two surfaces, one rail. This module owns the words, the lock lines and the
   painting; the two callers own only their own nodes and their own placement.
   It imports NOTHING (not the lexicon, not the store, not the bridge): `t` and
   the lever caps arrive as arguments, so it is importable in bare Node and it
   cannot drift from either host.

   It builds nodes with document.createElement and reads nothing back out of the
   DOM, so the node-double in the test harness carries it fine.
   ==========================================================================*/

/** The rail's rungs, cheapest wager first. The host is free to hand down a
 *  shorter list; this is the fallback when it hands down nothing. */
export const LEVER_POSITIONS = ['standard', 'extra', 'honors'];

/** Per position: [labelKey, labelEn, hintKey, hintEn]. */
export const LEVER_WORDS = {
  standard: ['lever_standard', 'Standard', 'lever_standard_hint',
    'Play it straight. Tickets pay the usual.'],
  extra: ['lever_extra', 'Extra Credit', 'lever_extra_hint',
    'Half again the tickets, and it asks more of you.'],
  honors: ['lever_honors', 'Honors', 'lever_honors_hint',
    'Double tickets, and the only road to an S plus.'],
};

/** What a rung says while it is still locked. Standard never locks. */
export const LEVER_LOCKED = {
  extra: ['lever_extra_locked', 'Earn an A on anything and this one wakes up.'],
  honors: ['lever_honors_locked', 'The counter sells this one for a token.'],
};

/** A locked rung stays ON the rail, dimmed. A player has to be able to see the
 *  thing they have not got yet, or the counter is selling a rumour. */
export function isLocked(pos, unlocks) {
  const un = unlocks || {};
  if (pos === 'extra') return un.extra !== true;
  if (pos === 'honors') return un.honors !== true;
  return false;
}

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/**
 * Rebuild a lever rail from scratch.
 *
 * Rebuilt rather than mutated on every pop, because the unlocks can have moved
 * since the last one (the player walked to the counter and bought the Honors
 * lever between doors) and a rail that remembered which rungs were dim would be
 * the one place in the school where a purchase does not show until a reload.
 *
 * @param {object} nodes  { rail, hint } - both already in the caller's tree.
 * @param {object} lev    caps: positions[], get(), set(pos), unlocks().
 * @param {function} t    lexicon lookup (key, fallback).
 * @param {function} [repaint] called after a successful set(); the caller
 *                    hands its own setLever back so the rail re-reads the pick.
 * @returns {string} the position that is currently pulled.
 */
export function paintLever(nodes, lev, t, repaint) {
  const rail = nodes && nodes.rail;
  const hint = nodes && nodes.hint;
  const say = typeof t === 'function' ? t : ((k, fb) => fb);
  if (!rail) return 'standard';

  const positions = (lev && Array.isArray(lev.positions) && lev.positions.length)
    ? lev.positions : LEVER_POSITIONS;
  let un = {};
  try { un = (lev && typeof lev.unlocks === 'function' ? lev.unlocks() : {}) || {}; }
  catch (e) { un = {}; }
  let pick = 'standard';
  try { pick = String((lev && typeof lev.get === 'function' ? lev.get() : 'standard') || 'standard'); }
  catch (e) { pick = 'standard'; }

  rail.textContent = '';
  const paintHint = (pos, locked) => {
    if (!hint) return;
    const row = locked ? LEVER_LOCKED[pos] : null;
    if (row) { hint.textContent = say(row[0], row[1]); return; }
    const w = LEVER_WORDS[pos];
    hint.textContent = w ? say(w[2], w[3]) : '';
  };

  for (const pos of positions) {
    const w = LEVER_WORDS[pos] || [pos, pos, '', ''];
    const locked = isLocked(pos, un);
    const btn = el('button', 'arc-lever-pos'
      + (pos === pick ? ' is-on' : '') + (locked ? ' is-locked' : ''), say(w[0], w[1]));
    btn.type = 'button';
    if (locked) btn.setAttribute('aria-disabled', 'true');
    btn.addEventListener('click', () => {
      if (locked) { paintHint(pos, true); return; }
      try { if (lev && typeof lev.set === 'function') lev.set(pos); } catch (e) { /* noop */ }
      if (typeof repaint === 'function') repaint();
    });
    /* Hovering a rung says what it costs BEFORE it is pulled - the one thing a
     * three-way switch has to do that a two-way one does not. */
    btn.addEventListener('mouseenter', () => paintHint(pos, locked));
    rail.appendChild(btn);
  }
  paintHint(pick, isLocked(pick, un));
  return pick;
}

/**
 * Build the whole block (title, rail, hint) for a host that has no nodes yet.
 * The campus card builds its own because its nodes are stitched into a card
 * that already exists; the room apron takes this.
 * @returns {{root:HTMLElement, rail:HTMLElement, hint:HTMLElement}}
 */
export function buildLever(t, extraClass) {
  const say = typeof t === 'function' ? t : ((k, fb) => fb);
  const root = el('div', 'arc-lever' + (extraClass ? ' ' + extraClass : ''));
  root.appendChild(el('p', 'arc-lever-title', say('lever_title', 'Extra Credit')));
  const rail = el('div', 'arc-lever-rail');
  const hint = el('p', 'arc-lever-hint', '');
  root.appendChild(rail);
  root.appendChild(hint);
  return { root, rail, hint };
}

export default { LEVER_POSITIONS, LEVER_WORDS, LEVER_LOCKED, isLocked, paintLever, buildLever };
