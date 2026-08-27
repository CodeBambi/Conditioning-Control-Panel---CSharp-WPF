/* ============================================================================
 * shell/prizebooth.js - THE PRIZE COUNTER, AS A PLACE YOU WALK UP TO.
 *
 * The counter used to be a door on the plan and then, one click later, a shop.
 * There was no corridor in between and no moment of standing at a window, which
 * is the moment the whole feature is about: the shelf is behind glass, somebody
 * is behind the shelf, and a ticket has to be handed over. This is that moment.
 * Two things to touch on one painted set, and neither of them is the shop:
 *
 *   THE WINDOW   the lit service opening under the sign, and it is THE verb of
 *                the room. Pressing it opens shell/prizecounter.js exactly as
 *                it has always opened, unchanged, echo law and all. The booth
 *                is a way in, never a second till.
 *   THE TRAY     the brass tray on the sill, which is where the money is
 *                actually counted. It answers the two questions a shopper asks
 *                before they shop: what is on them, and which room is paying
 *                over the odds tonight. The payday sentence is the SHOP'S, read
 *                through `paydayParts` so the counter and the sill can never
 *                end up saying it two different ways.
 *
 * THE SHUTTER IS ONE FLAG, NOT A SECOND ROOM. `closed` swaps a full-stage patch
 * (vn-19) over the wide plate, takes both rects off the wall with the `when`
 * gate the storeroom door already uses, and stands one hall line under the
 * sign. Same camera, same set, same single Esc rung. A closed counter is the
 * counter with its shutter down, and drawing it any other way would be drawing
 * a different building.
 *
 * NARROW CAPS, THE ANNEX'S LAW. Nothing under this line imports the store, the
 * bridge or EMI. Every fact about the player arrives as a function and every
 * consequence leaves as a callback, which is what makes the room importable in
 * bare node against the DOM double. It walks its own children BY INDEX
 * (`findCls`) and never touches `querySelector`, for the same reason.
 *
 * NO TRACK. shell/ost.js's law is that silence is the default between places
 * and a screen that wants a tune asks for one. This one does not ask, and the
 * shop it opens has never asked either.
 *
 * THE APRON OWNS THE FLOOR. Both rects clear y=640 on their own: the plate
 * paints the counter's front panel and the checker floor down there and there
 * is nothing in either worth pressing. There is no painted door on this wall
 * either (it is a wall with a window cut into it), so the way out is the
 * apron's back slab, which is what every doorless room scene already does.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';
import { createScene } from './scene.js';
import { paydayParts, TOKEN_MARK } from './prizecounter.js';

/* ----------------------------------------------------------------------------
 * THE TABLE
 *
 * Every number here is stage pixels on the 1376x768 plate and every one of them
 * was measured off `art/vn/vn-18-prize-window.png` itself rather than read off
 * the art spec. Nothing in this file may round one for looks: a rect is a
 * measurement of a painting.
 *
 * The two rects STACK rather than overlap. The service opening runs the full
 * width of the frame (x 349..1026 between the jambs), but its outer thirds are
 * the two shelf banks, and the lit mouth between them is x 548..826. The tray
 * sits on the sill directly beneath that mouth, x 608..767, its lip at y 406
 * and the counter's own top edge at y 452. So the window is the mouth and the
 * tray is the sill under it, and no press can land on both.
 * -------------------------------------------------------------------------- */

export const RECTS = Object.freeze({
  /** The lit mouth of the service window, between the two shelf banks. */
  window: Object.freeze([548, 172, 278, 226]),
  /** The brass tray on the sill. Measured at the box's own outline. */
  tray: Object.freeze([604, 400, 168, 54]),
});

/** The shutter, laid over the whole wide plate while `closed` is on. Full
 *  stage: the plate is a redraw of the same camera with one thing changed, so
 *  a partial patch would leave the lit window burning through around it. */
export const SHUTTER_PATCH = Object.freeze([0, 0, 1376, 768]);

/* ----------------------------------------------------------------------------
 * THE ALIVE LAYER'S TABLE
 *
 * scene.js's `fx` contract. Two rows, both decoration, both crop-verified, and
 * both gated `!closed` for a reason that is not performance: a dark sign that
 * breathes is a sign that is still on, and the whole point of the shutter plate
 * is that the power to this window has been turned off at the wall.
 * -------------------------------------------------------------------------- */

/** The navy PRIZE COUNTER plate over the window (measured x 420..910, y 36..130). */
export const FX_SIGN = Object.freeze([420, 34, 490, 100]);
/** The pendant lamp inside the booth and the pool it throws on the back wall. */
export const FX_LAMP = Object.freeze([580, 172, 220, 180]);

/** The room's fx rows, in scene.js's grammar. Exported so a suite can read the
 *  same table the room is built from. */
export function boothFx() {
  return [
    { kind: 'neon', view: 'wide', rect: FX_SIGN, seed: 0x71CE75, when: '!closed' },
    { kind: 'lamp', view: 'wide', rect: FX_LAMP, when: '!closed' },
    { kind: 'tilt', view: 'wide', amp: 2 },
  ];
}

/* ----------------------------------------------------------------------------
 * PLUMBING
 * -------------------------------------------------------------------------- */

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

function urlFor(rel, fallback) {
  try { return new URL(rel, import.meta.url).href; }
  catch (e) { return fallback; }
}

/** prizebooth.css, linked once and lazily - recordsroom.js's pattern. */
function ensureSheet(doc, log) {
  try {
    if (!doc || typeof doc.createElement !== 'function') return null;
    const had = typeof doc.getElementById === 'function' ? doc.getElementById('pb-styles') : null;
    if (had) return had;
    const link = doc.createElement('link');
    link.id = 'pb-styles';
    link.rel = 'stylesheet';
    link.href = urlFor('./prizebooth.css', 'shell/prizebooth.css');
    const host = doc.head || doc.documentElement || doc.body;
    if (host && typeof host.appendChild === 'function') host.appendChild(link);
    return link;
  } catch (e) { if (log) log('prize booth sheet failed to link'); return null; }
}

/** Walk a subtree for the first node wearing `cls`, BY INDEX and never with a
 *  selector - `querySelector` does not exist in the node double, and a room
 *  that can only find its own window in a browser is a room with no suite. */
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

/* ----------------------------------------------------------------------------
 * THE ROOM
 * -------------------------------------------------------------------------- */

/**
 * createPrizeBooth(caps) -> the handle, or null with no DOM.
 *
 * @param {Object} caps
 *  mount     - where the room's root goes (the shell's screen).
 *  t / log / lite / reduced
 *  closed    - is the shutter down right now? The SHELL owns that question
 *              (no economy, a host suspend, an entitlement that lapsed); this
 *              file only draws the answer and re-draws it on setClosed().
 *  balance   - () -> {t, k}, read fresh every time the tray is opened.
 *  payday    - optional () -> {gameKey, mult} for tonight's hot room, or null.
 *  gameName  - optional (key) -> that room's display name.
 *  onShop    - the window's press: the shell shows shell/prizecounter.js.
 *  onBack    - the apron's back slab: the shell walks out to the campus.
 * @returns {?Object} the handle
 */
export function createPrizeBooth(caps) {
  const c = caps || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;

  const t = typeof c.t === 'function' ? c.t : lexT;
  const log = typeof c.log === 'function' ? c.log : function () {};

  ensureSheet(doc, log);

  let dead = false;
  let closed = !!c.closed;
  let closeTill = null;        // the live tray panel's own close, or null
  let shutEl = null;           // the CLOSED word over the shutter, or null
  let shutOff = null;          // its unmount
  let lineEl = null;           // the one hall line under a shut sign, or null

  /* ------------------------------------------------------------- the scene */

  const scene = createScene({
    mount: c.mount,
    lite: !!c.lite,
    reduced: !!c.reduced,
    log: log,
    t: t,
    label: t('campus_room_prizes', 'Prize Counter'),
    views: {
      wide: {
        art: 'vn-18-prize-window.png',
        hotspots: [
          /* THE VERB OF THE ROOM, so it is the only thing that breathes. The
           * `when` gate is the shutter's whole mechanism on this side: closed
           * means the rect is never built, not that it is built and refuses. */
          [RECTS.window[0], RECTS.window[1], RECTS.window[2], RECTS.window[3],
            'shop', 'prize_booth_window', 'The service window', { main: true, when: '!closed' }],
          [RECTS.tray[0], RECTS.tray[1], RECTS.tray[2], RECTS.tray[3],
            'till', 'prize_booth_tray', 'The ticket tray', { when: '!closed' }],
        ],
      },
    },
    patches: [
      { view: 'wide', art: 'vn-19-prize-shutter.png', rect: SHUTTER_PATCH, when: 'closed' },
    ],
    /* Decoration only, and dark with the sign - see the table's own header. */
    fx: boothFx(),
    apron: { back: function () { try { if (c.onBack) c.onBack(); } catch (e) { log('prize booth back threw'); } } },
    onAction: onAction,
  });
  if (!scene) return null;

  try { scene.root.classList.add('pb-room'); } catch (e) { /* noop */ }
  scene.setFlag('closed', closed);
  paintShut();

  /* --------------------------------------------------------- THE ACTIONS */

  function onAction(action) {
    if (dead || closed) return;
    if (action === 'shop') {
      try { if (c.onShop) c.onShop(); } catch (e) { log('prize booth shop threw: ' + ((e && e.message) || e)); }
      return;
    }
    if (action === 'till') openTill();
  }

  /* ------------------------------------------------------------- THE TRAY */

  /**
   * What is on you, and which room is paying over the odds. Both are READ AT
   * OPEN rather than held: a payout can land while the player is standing here,
   * and a tray that answered with the number it was built with would be a tray
   * that lies about a wallet the host has already moved.
   *
   * NOT A SECOND TILL. Nothing in this panel can be pressed and nothing in it
   * proposes a purchase, which is why it may read the wallet at all. The one
   * thing in this school that may move a balance is the shop's settle().
   */
  function openTill() {
    if (dead) return;
    closeTill = scene.openOverlay('till', function (panel) {
      try { panel.classList.add('pb-till'); } catch (e) { /* noop */ }
      panel.appendChild(el('h3', 'pb-till-head', t('prize_booth_tray', 'The ticket tray')));

      const purse = el('div', 'pb-purse');
      purse.appendChild(el('span', 'pb-purse-lbl', t('prize_you_have', 'On you')));
      purse.appendChild(coin('t'));
      purse.appendChild(coin('k'));
      panel.appendChild(purse);

      const pd = paydayParts(read(c.payday), t, c.gameName);
      const line = el('p', 'pb-payday');
      if (pd) {
        line.appendChild(el('span', 'pb-payday-lbl', pd.label));
        line.appendChild(el('b', 'pb-payday-name', pd.name));
        line.appendChild(el('span', 'pb-payday-mult', pd.tail));
      } else {
        line.appendChild(el('span', 'pb-payday-none', t('prize_no_payday',
          'No room is paying over the odds tonight. Every graded class still pays tickets.')));
      }
      panel.appendChild(line);
    });
  }

  /** One currency reading, the counter's own two marks. `cur` is 't' or 'k'. */
  function coin(cur) {
    const b = readBalance();
    const box = el('span', 'pb-coin pb-coin-' + cur);
    const ico = el('i', cur === 'k' ? 'arc-tok' : 'arc-tick', cur === 'k' ? TOKEN_MARK : null);
    attr(ico, 'aria-hidden', 'true');
    box.appendChild(el('b', 'pb-coin-n', String(cur === 'k' ? b.k : b.t)));
    box.appendChild(ico);
    return box;
  }

  function readBalance() {
    try {
      const b = (typeof c.balance === 'function') ? c.balance() : null;
      const tt = Number(b && b.t); const kk = Number(b && b.k);
      return { t: Number.isFinite(tt) ? tt : 0, k: Number.isFinite(kk) ? kk : 0 };
    } catch (e) { return { t: 0, k: 0 }; }
  }

  function read(fn) {
    try { return (typeof fn === 'function') ? fn() : null; } catch (e) { return null; }
  }

  /* ---------------------------------------------------------- THE SHUTTER */

  /**
   * What a shut counter says, and it says ONE thing.
   *
   * THE PLATE ALREADY SAYS THE WORD. vn-19 has a hand-lettered BACK SOON card
   * taped to the slats at the middle of the shutter, which is how a counter
   * like this actually tells you it is shut, so a second stencilled CLOSED laid
   * over the top would be the room saying it twice in two different hands. The
   * word survives as the `title` - the honest cheap tooltip for a thing that
   * cannot be pressed - and as the campus card's status one screen back.
   *
   * What is added is the LINE, and it goes on the lower band of the shutter,
   * under the painted card and well clear of it. HALL-WIDE: nobody is being
   * told the counter is shut because of anything they did.
   *
   * It takes no pointer at all. There is no rect under it to swallow (they are
   * not built while `closed` is on) and nothing here for a keyboard to reach.
   */
  function paintShut() {
    if (dead) return;
    if (closed && !shutEl) {
      shutEl = el('div', 'pb-shut');
      attr(shutEl, 'aria-hidden', 'true');
      attr(shutEl, 'title', t('prize_closed', 'Closed'));
      lineEl = el('p', 'pb-shut-line', t('prize_closed_line',
        'The shutter is down and the sign above it has been switched off at the wall.'));
      shutEl.appendChild(lineEl);
      /* Measured off vn-19, and BELOW the ledge rather than above it. The band
       * between the painted card (bottom edge y 394) and the ledge (y 470) is
       * only two lines tall and the ledge's own shadow runs straight through
       * the second one; the counter's front panel underneath is flat, dark and
       * empty, so the line sits there and reads. Above the apron either way. */
      shutOff = scene.mountInView('wide', shutEl, [400, 486, 576, 56]);
    } else if (!closed && shutEl) {
      try { if (shutOff) shutOff(); } catch (e) { /* noop */ }
      shutEl = null;
      shutOff = null;
      lineEl = null;
    }
  }

  /** The counter opening or closing while somebody is standing at it. A LEVEL
   *  (trap 28), idempotent, and it folds any open tray away on the way down. */
  function setClosed(on) {
    if (dead) return;
    const next = !!on;
    if (closed === next) return;
    closed = next;
    if (closed && closeTill) { try { closeTill(); } catch (e) { /* noop */ } closeTill = null; }
    try { scene.setFlag('closed', closed); } catch (e) { /* noop */ }
    paintShut();
  }

  /* ------------------------------------------------------------- the fold */

  /** ONE RUNG DEEP: the tray panel, then FALSE, at which point the shell's own
   *  rung walks out to the campus. This module binds no key; the shell asks. */
  function escapeStep() {
    if (dead) return false;
    try { return !!scene.escapeStep(); } catch (e) { return false; }
  }

  function destroy() {
    if (dead) return;
    dead = true;
    closeTill = null;
    shutEl = null;
    shutOff = null;
    lineEl = null;
    /* The apron lives on <body>: scene.destroy() is the ONLY thing that takes
     * it off, which is why the shell's clearScreen has to reach this line. */
    try { scene.destroy(); } catch (e) { /* noop */ }
  }

  return {
    root: scene.root,
    escapeStep: escapeStep,
    setClosed: setClosed,
    fit: function () {
      try { return scene.fit(); } catch (e) { return null; }
    },
    destroy: destroy,
    /* ------------------------------------------------------- test seams */
    scene: scene,
    view: function () { return scene.view(); },
    /** Is the shutter down right now? */
    isClosed: function () { return closed; },
    /** Is the tray panel open? */
    tillUp: function () { return !!findCls(scene.root, 'pb-till'); },
    /** The lit window rect, once it has been built (null while shut). */
    windowHot: function () { return findCls(scene.root, 'arm-main'); },
    /** The CLOSED word over the shutter, or null. */
    shutWord: function () { return findCls(scene.root, 'pb-shut'); },
  };
}

export default createPrizeBooth;
