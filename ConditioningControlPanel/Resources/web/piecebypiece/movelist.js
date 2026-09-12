/* ============================================================================
 * movelist.js - the moves, written down.
 *
 * A quiet column on the right edge, one row per move number, white then black,
 * read straight off the referee's own history. It is the one part of the HUD
 * that takes a click: tap a move and the two squares it came from and went to
 * light up on the board for a moment, so a player can ask "where was that?"
 * without the board replaying anything.
 *
 * It stays clear of the ramp's video card on purpose. The card is centred and
 * min(72vmin, 88vw) wide (ramp.css .pbp-card), so it never reaches the right
 * edge on a wide screen; on a narrow one the list is closed by default and only
 * its tab shows. It lives in #hud, under #fx, so the card still rides over it.
 *
 *   createMoveList({ bus, game, board, root }) -> { refresh, dispose, debug }
 *
 * Nothing here may throw: a missing node, a missing history and a board with no
 * setHighlights all degrade to a list that simply does less.
 * ==========================================================================*/

/** Every number and key the list decides with. One place, on purpose. */
export const TUNING = Object.freeze({
  litMs: 1200,             // how long a tapped move keeps its squares lit
  wideAt: 1100,            // px, wider than this and the list opens by default
  storeKey: 'pbp-movelist',
});

const T = TUNING;

function reducedMotion() {
  try {
    if (typeof window === 'undefined') return false;
    const pbp = window.PBP;
    if (pbp && pbp.settings && pbp.settings.reducedMotion) return true;
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  } catch { return false; }
}

function remember(value) {
  try { window.localStorage.setItem(T.storeKey, value); } catch { /* private window, no memory */ }
}

function recall() {
  try { return window.localStorage.getItem(T.storeKey); } catch { return null; }
}

export function createMoveList(opts = {}) {
  const bus = opts.bus && typeof opts.bus.on === 'function' ? opts.bus : null;
  const game = opts.game || null;
  const board = opts.board || null;
  const root = opts.root || null;
  const tab = root ? root.querySelector('#moves-tab') : null;
  const list = root ? root.querySelector('#moves-list') : null;
  if (!root || !list) {
    return { refresh() {}, dispose() {}, debug: () => ({ ok: false, why: 'no #moves' }) };
  }

  let drawn = 0;            // half-moves currently on screen
  let lastSan = '';         // so a take-back (same length, different move) redraws
  let selected = null;      // the ply whose squares are lit
  let litTimer = 0;
  const unbind = [];

  const on = (type, fn) => {
    if (!bus) return;
    const safe = (p) => { try { fn(p); } catch (e) { console.warn('[pbp/moves] ' + type + ': ' + (e && e.message)); } };
    try { unbind.push(bus.on(type, safe)); } catch { /* a bus that will not take listeners is still a bus */ }
  };

  /* ---- open and closed ---------------------------------------------------- */

  function setOpen(open, save) {
    root.classList.toggle('open', open);
    root.classList.toggle('closed', !open);
    if (tab) tab.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (save) remember(open ? 'open' : 'closed');
    if (open) scrollToNewest(true);
  }
  const isOpen = () => root.classList.contains('open');

  /* ---- the rows ----------------------------------------------------------- */

  function history() {
    try {
      const h = game && game.rules && game.rules.chess ? game.rules.chess.history({ verbose: true }) : [];
      return Array.isArray(h) ? h : [];
    } catch { return []; }
  }

  function cell(move, ply) {
    const span = document.createElement('span');
    span.className = 'm';
    if (!move) { span.className = 'm gap'; span.textContent = ''; return span; }
    span.textContent = move.san || (move.from + move.to);
    span.dataset.ply = String(ply);
    span.dataset.from = move.from;
    span.dataset.to = move.to;
    return span;
  }

  function draw() {
    const moves = history();
    list.textContent = '';
    if (moves.length === 0) {
      const empty = document.createElement('li');
      empty.className = 'mv empty';
      empty.textContent = 'no moves yet';
      list.appendChild(empty);
    }
    for (let i = 0; i < moves.length; i += 2) {
      const row = document.createElement('li');
      row.className = 'mv';
      const n = document.createElement('span');
      n.className = 'n';
      n.textContent = (i / 2 + 1) + '.';
      row.appendChild(n);
      row.appendChild(cell(moves[i], i));
      row.appendChild(cell(moves[i + 1], i + 1));
      list.appendChild(row);
    }
    drawn = moves.length;
    lastSan = moves.length ? String(moves[moves.length - 1].san || '') : '';
    if (selected != null && selected >= moves.length) clearLit();
    paintSelected();
    scrollToNewest(false);
  }

  function refresh() {
    const moves = history();
    const tail = moves.length ? String(moves[moves.length - 1].san || '') : '';
    if (moves.length === drawn && tail === lastSan) return;
    draw();
  }

  function scrollToNewest(force) {
    if (!isOpen()) return;
    const go = () => {
      try {
        if (reducedMotion() || force) list.scrollTop = list.scrollHeight;
        else list.scrollTo({ top: list.scrollHeight, behavior: 'smooth' });
      } catch { list.scrollTop = list.scrollHeight; }
    };
    try { requestAnimationFrame(go); } catch { go(); }
  }

  /* ---- lighting a move up on the board ------------------------------------ */

  function paintSelected() {
    for (const node of list.querySelectorAll('.m')) {
      node.classList.toggle('sel', selected != null && node.dataset.ply === String(selected));
    }
  }

  function highlight(list2) {
    if (!board || typeof board.setHighlights !== 'function') return;
    // a man in the hand owns the hints; the list never takes them off him
    try { if (board.drag && board.drag.isDragging && board.drag.isDragging()) return; } catch { /* no drag yet */ }
    try { board.setHighlights(list2); } catch { /* the board may not be up */ }
  }

  function clearLit() {
    if (litTimer) { clearTimeout(litTimer); litTimer = 0; }
    selected = null;
    paintSelected();
    highlight([]);
  }

  function lightUp(node) {
    const ply = Number(node.dataset.ply);
    if (!Number.isFinite(ply)) return;
    if (selected === ply) { clearLit(); return; }     // tap the same move again to put it out
    if (litTimer) { clearTimeout(litTimer); litTimer = 0; }
    selected = ply;
    paintSelected();
    highlight([
      { square: node.dataset.from, kind: 'origin' },
      { square: node.dataset.to, kind: 'move' },
    ]);
    litTimer = setTimeout(() => { litTimer = 0; clearLit(); }, T.litMs);
  }

  /* ---- wiring ------------------------------------------------------------- */

  const onClick = (ev) => {
    const node = ev.target && ev.target.closest ? ev.target.closest('.m[data-ply]') : null;
    if (!node) return;
    ev.stopPropagation();
    lightUp(node);
  };
  list.addEventListener('click', onClick);

  // the wheel belongs to the list while the pointer is over it: the canvas
  // behind it takes the wheel as a camera zoom, and reading the moves is not
  // a reason to lose your view of the board
  const onWheel = (ev) => { ev.stopPropagation(); };
  list.addEventListener('wheel', onWheel, { passive: true });

  const onTab = (ev) => { ev.stopPropagation(); setOpen(!isOpen(), true); };
  if (tab) tab.addEventListener('click', onTab);

  const onKey = (ev) => { if (ev.key === 'Escape' && selected != null) clearLit(); };
  try { window.addEventListener('keydown', onKey); } catch { /* no window */ }

  on('turn', () => refresh());
  on('gameover', () => refresh());
  on('local', () => refresh());

  /* ---- start -------------------------------------------------------------- */

  const stored = recall();
  let open;
  if (stored === 'open') open = true;
  else if (stored === 'closed') open = false;
  else open = (typeof window !== 'undefined' ? window.innerWidth : 0) > T.wideAt;
  root.hidden = false;
  setOpen(open, false);
  draw();

  function dispose() {
    for (const off of unbind) { try { off(); } catch { /* already gone */ } }
    unbind.length = 0;
    if (litTimer) { clearTimeout(litTimer); litTimer = 0; }
    list.removeEventListener('click', onClick);
    list.removeEventListener('wheel', onWheel);
    if (tab) tab.removeEventListener('click', onTab);
    try { window.removeEventListener('keydown', onKey); } catch { /* gone */ }
  }

  return {
    refresh,
    dispose,
    setOpen: (v) => setOpen(!!v, true),
    /** Everything a harness needs to prove a state without reading pixels. */
    debug() {
      return {
        open: isOpen(),
        rows: list.querySelectorAll('.mv').length,
        plies: drawn,
        last: lastSan,
        selected,
        stored: recall(),
      };
    },
  };
}

export default createMoveList;
