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
 *                it has always opened, unchanged, echo law and all - but it
 *                opens it HERE NOW, in a panel over this plate, instead of
 *                walking the player off to a screen of their own. The booth is
 *                a way in, never a second till.
 *   THE TRAY     the brass tray on the sill, which is where the money is
 *                actually counted. It answers the two questions a shopper asks
 *                before they shop: what is on them, and which room is paying
 *                over the odds tonight. The payday sentence is the SHOP'S, read
 *                through `paydayParts` so the counter and the sill can never
 *                end up saying it two different ways.
 *
 * THE ALLEY IS AN ARRIVAL, NOT A ROOM (Locker wave, 2026-08-28). vn-17 was a
 * hover thumbnail on the campus plan and nothing else, which is a painted
 * corridor nobody has ever stood in. It is the booth's second view now and it is
 * the FIRST thing you see: about seven tenths of a second of the row of service
 * windows with the lit one at the end, and then the camera pushes into that
 * window and the plate is vn-18. Any press and any key cuts it short, because an
 * arrival you cannot skip is a loading screen with a painting on it.
 *
 * THE CUT GOING IN, THE ZOOM COMING OUT, and both halves are deliberate. The
 * chassis mounts `wide` at build - it is the home shot and it is required - so
 * the walk to the alley would show one frame of the window plate before the
 * corridor arrived. `.pb-arrive` (prizebooth.css) holds the incoming alley at
 * full opacity with no transform, which turns that first move into a CUT that
 * lands on the corridor; the class comes off before the move back, so the walk
 * IN is the chassis's own zoom with its origin on the lit window. The band and
 * the step-back pill sit the beat out - see `html.pb-arriving` in the sheet.
 *
 * LITE AND REDUCED SKIP THE ARRIVAL, NEVER THE PLATE. A machine that asked for
 * less, or a player who did, still walks up to the same window; they simply
 * start at it.
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
 *
 * THE SIGN ON THE RIGHT-HAND WALL (owner ask, 2026-08-28) is the THIRD thing
 * you can touch, and it is deliberately not a fourth verb: it is the corridor
 * carrying on. RM 004 is one window further down this same alley, and until now
 * standing at the counter with a jacket you had just bought meant walking out
 * to the quad, across it and back down here to put it on. The plate hangs at the
 * service window's own eye line so the two read as one row of things at head
 * height, it points RIGHT the way the alley runs, and it is mounted as a scene
 * PROP - shell/alleysign.js owns what it looks like and this file owns only
 * where on the painting it is screwed. `onLocker` is its whole contract; like
 * every other consequence in here it leaves as a callback, and nothing under
 * this line has ever heard of a locker.
 *
 * IT STAYS LIT WITH THE SHUTTER DOWN, and that is the one asymmetry with
 * everything else on this wall. The `!closed` gate gets taken off the window,
 * the tray and both fx rows because the power to THIS window has been cut - but
 * nothing is sold in the Locker, so the Locker never shuts, and a player who
 * walked down a dark alley to a dead counter is exactly the player who most
 * needs the one door on this screen that still opens.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';
import { createScene } from './scene.js';
import { paydayParts, TOKEN_MARK, heldCount, spriteUrl } from './prizecounter.js';
/* THE SIGN DOWN THE ALLEY. One module, two rooms: the Locker mounts the mirror
 * of this at its own end, and shell/alleysign.js's header says why there are
 * two of them. It imports the lexicon and exits.js and nothing else, so the
 * narrow caps this file keeps are not spent by taking it. */
import { alleySign, BOOTH_SIGN_RECT } from './alleysign.js';
/* The counter's motion kit (shell/counterfx.js). `cue` is the one audio door
 * this file has ever needed and `countUp` is THE BANK pointed forwards. */
import { countUp, cue } from './counterfx.js';

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

/* ----------------------------------------------------------------------------
 * THE ARRIVAL'S TABLE
 *
 * Measured off `art/vn/vn-17-prize-alley.png`, which is the same 1376x768 plane
 * as the window plate. The lit shelf under the PRIZES sign (window 003, the one
 * this booth is) sits at x 484..626, y 265..455 on that plate, and that rect is
 * the whole of what the arrival needs: it is where the camera pushes IN to.
 * -------------------------------------------------------------------------- */

/** How long the corridor holds before the camera walks in, in ms. Long enough
 *  to read a lit window at the end of a row, short enough that nobody who has
 *  seen it forty times ever reaches for the skip. */
export const ALLEY_MS = 700;

/** The lit service window inside the alley plate. */
export const ALLEY_WINDOW = Object.freeze([484, 265, 142, 190]);

/** How far the corridor shrinks on its way out. Not `kFor(ALLEY_WINDOW)` (which
 *  would be 0.10 and reads as falling down a hole) - a step forward, not a dive. */
export const ALLEY_K = 0.3;

/** Where the one line under the corridor hangs. Above the apron floor (y 640)
 *  and inside the plate's own dark lower band, which is where a caption on a VN
 *  set has always gone. */
export const ALLEY_HINT = Object.freeze([388, 578, 600, 48]);

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
 * THE HOLDINGS TRAY'S TWO TABLES (counter shortcut wave, 2026-08-30)
 *
 * The tray answered two questions - what is in your purse, and which room is
 * paying over the odds - and never the third one a shopper actually asks at a
 * counter: what am I already carrying. A consumable is invisible the moment it
 * is bought; the shelf turns the row gold and nothing anywhere says you have
 * two late slips in your pocket. That is what these rows are for.
 *
 * BE HONEST ABOUT THE STATE OF THE WORLD. There is exactly ONE consumable on
 * this shelf and it has NO manual verb: `late_slip` is spent by the HOST, by
 * itself, inside the attendance credit on the night a day is missed
 * (ArcademyEconomy.ConsumeLateSlip). So the tray is a WHAT YOU HOLD view with
 * room for a verb, not a verb pretending to have somewhere to go.
 * -------------------------------------------------------------------------- */

/**
 * WHICH CONSUMABLES CAN BE SPENT BY HAND. Deliberately EMPTY, and it is empty
 * because nothing in the school can be. The shape is EQUIP_VERBS's, one row per
 * sku - `sku: ['lexicon_key', 'English']` - so the night a manually-spendable
 * thing lands on the shelf it is one line here and one `onUse` cap in shell.js.
 *
 * A VERB WITH NO `onUse` BEHIND IT IS NOT A BUTTON. The cap is asked at render
 * as well as the table, and a row that names a verb the host cannot answer
 * falls back to the passive line: a control that cannot do the thing it is
 * lettered with is a worse lie than no control at all.
 */
export const USE_VERBS = Object.freeze({});

/** The stand-in for a sku whose sprite has not been drawn yet. A filled block,
 *  not a picture of anything - prizecounter.js's GLYPHS make the same choice
 *  for the same reason: a wrong picture is worse than an obvious placeholder. */
export const HOLD_GLYPH = '▤';

/**
 * WHAT A THING YOU HOLD BUT CANNOT PRESS SAYS. Per sku where the school has
 * something specific to say about how it spends itself, and one general line
 * underneath for anything that arrives later without its own.
 */
export const PASSIVE_LINES = Object.freeze({
  late_slip: ['booth_hold_late_slip', 'It files itself the night you miss one. Nothing to press.'],
});

/** The floor under PASSIVE_LINES. */
export const PASSIVE_DEFAULT = Object.freeze(
  ['booth_hold_passive', 'It spends itself the moment it is needed.']
);

/** The verb pair for a sku, or null. Own-property only - a sku called
 *  'constructor' may not inherit a verb off Object.prototype. */
export function useVerbFor(sku) {
  return Object.prototype.hasOwnProperty.call(USE_VERBS, sku) ? USE_VERBS[sku] : null;
}

/** The passive line for a sku - its own, or the floor. Never null. */
export function passiveLineFor(sku) {
  return Object.prototype.hasOwnProperty.call(PASSIVE_LINES, sku)
    ? PASSIVE_LINES[sku] : PASSIVE_DEFAULT;
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

function addCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.add === 'function') node.classList.add(cls); }
  catch (e) { /* noop */ }
}

function dropCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.remove === 'function') node.classList.remove(cls); }
  catch (e) { /* noop */ }
}

/** The page's own answer about motion, asked the way prizecounter.js asks it:
 *  the shell's class on <html> first, then the media query. The caller's
 *  `reduced` cap outranks neither - all three are ORs. */
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
 *  catalog   - optional () -> the host's catalog rows. THE HOLDINGS TRAY reads
 *              it for the consumable rows and their names; omit it and the
 *              tray simply has nothing to list, which is the honest answer
 *              for a host with no shelf.
 *  inv       - optional () -> the wallet's `inv` bag, for how many are held.
 *  stackMax  - optional (sku) -> how many may be held at once. Display only;
 *              the host is what refuses the third late slip, reason `full`.
 *  onUse     - optional (sku) -> spend one, by hand. THERE IS NO SUCH SKU
 *              TODAY - the one consumable on the shelf burns itself in the
 *              host's attendance path - so the shell does not pass this and
 *              every row draws its passive line instead. It is named here so
 *              the day one arrives the tray already knows what to do with it.
 *  gameName  - optional (key) -> that room's display name.
 *  alley     - play the arrival beat? Default true. The purse chip in the
 *              chrome passes false: it did not walk anywhere, so it does not
 *              arrive anywhere. Lite and reduced motion answer false for it.
 *  onShop    - (panel, close) the window's press. The SHELL fills the panel
 *              with shell/prizecounter.js and keeps `close` for the counter's
 *              own Back button; this file owns the box and the lifecycle and
 *              has never known what a catalog is.
 *  onShopClosed - the panel went away, by any road: the Back button, Esc, the
 *              scrim, or the shutter coming down. The shell tears its counter
 *              down here, once, whichever road it was.
 *  onBack    - the apron's back slab: the shell walks out to the campus.
 *  onLocker  - optional. The sign on the right-hand wall: one door further
 *              down the same alley. Omit it and no sign is hung at all, which
 *              is the honest thing for a host with no locker to point at, and
 *              is also what the bare-Node tests take.
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
  let closeShop = null;        // the live shelf panel's own close, or null
  let shopOn = false;          // is the shelf panel up? (the handle can be stale)
  let shutEl = null;           // the CLOSED word over the shutter, or null
  let shutOff = null;          // its unmount
  let lineEl = null;           // the one hall line under a shut sign, or null
  let alleyUp = false;         // is the arrival beat on screen right now?
  let alleyTimer = null;       // its own hold, cleared from landWindow()
  let hintOff = null;          // the corridor's one line, its unmount
  let signOff = null;          // the LOCKER ROOM plate on the wall, its unmount

  /** Does this arrival get the corridor? The caller's answer first, then the
   *  two decoration rungs: a lite machine and a player who asked for less
   *  motion both start at the window. Neither loses the plate. */
  const wantAlley = c.alley !== false && !c.lite && !htmlReduced() && !c.reduced;

  /* ------------------------------------------------------------- the scene */

  const scene = createScene({
    mount: c.mount,
    lite: !!c.lite,
    reduced: !!c.reduced,
    log: log,
    t: t,
    label: t('campus_room_prizes', 'Prize Counter'),
    views: {
      /* THE CORRIDOR. No hotspots: it is a beat, not a room, and the one thing
       * you can do with it is walk through it. See THE ALLEY IS AN ARRIVAL. */
      alley: { art: 'vn-17-prize-alley.png', hotspots: [] },
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
  hangLockerSign();
  /* SYNCHRONOUS, and that is the whole trick: the chassis has just mounted the
   * window plate and nothing has been painted yet, so the cut to the corridor
   * lands on the first frame the player ever sees. */
  if (wantAlley) enterAlley();

  /* --------------------------------------------------------- THE ACTIONS */

  function onAction(action) {
    if (dead || closed) return;
    /* A press during the arrival is a press to skip it, and nothing else. The
     * corridor has no rects of its own, but a fast finger can land on a window
     * rect one frame into the walk in, and opening the shop out of a beat the
     * player was cutting short is not what they asked for. */
    if (alleyUp) { landWindow(); return; }
    if (action === 'shop') { openShop(); return; }
    if (action === 'till') openTill();
  }

  /* ------------------------------------------------------- THE ARRIVAL */

  /**
   * Down the alley, and then in through the window. Two chassis moves with a
   * hold between them, and the class on the root is what makes the first one a
   * cut instead of a fade across the plate we are trying not to show yet.
   */
  function enterAlley() {
    if (dead || alleyUp) return;
    alleyUp = true;
    addCls(scene.root, 'pb-arrive');
    /* The band and the pill are chrome for a place you have arrived at. Held
     * off the corridor from <html>, because the apron is a body-level sibling
     * of the room and no class on the room can reach it. */
    try { if (doc.documentElement) addCls(doc.documentElement, 'pb-arriving'); } catch (e) { /* noop */ }

    const hint = el('p', 'pb-alley-hint', t('booth_alley_hint',
      'The lit window is down at the end of the row.'));
    attr(hint, 'aria-hidden', 'true');
    /* Mounted BEFORE the move, so the corridor's slide is built with the line
     * already in it. It rides out with the plate rather than being taken off a
     * frame early, which is why nothing unmounts it at landWindow(). */
    try { hintOff = scene.mountInView('alley', hint, ALLEY_HINT.slice()); }
    catch (e) { hintOff = null; }

    try {
      scene.showView('alley', {
        origin: [
          ALLEY_WINDOW[0] + ALLEY_WINDOW[2] / 2,
          ALLEY_WINDOW[1] + ALLEY_WINDOW[3] / 2,
          ALLEY_K,
        ],
      });
    } catch (e) {
      log('prize booth arrival failed: ' + ((e && e.message) || e));
      alleyUp = false;
      dropCls(scene.root, 'pb-arrive');
      try { if (doc.documentElement) dropCls(doc.documentElement, 'pb-arriving'); } catch (e2) { /* noop */ }
      return;
    }

    bindSkip();
    try { alleyTimer = setTimeout(landWindow, ALLEY_MS); } catch (e) { alleyTimer = null; }
  }

  /**
   * The walk in. Idempotent from every road that can reach it - the hold, a
   * press, a key, the Esc fold, a teardown - because three of those can happen
   * in the same tick and only one of them may move the camera.
   *
   * THE CLASS COMES OFF FIRST. It is what was holding the corridor at full
   * opacity with no transform; taking it off before the move is what lets the
   * chassis's own zoom run, and showView reads a reflow before it arms the
   * transition, so the two never collapse into one frame.
   */
  function landWindow() {
    if (dead || !alleyUp) return;
    alleyUp = false;
    if (alleyTimer) { try { clearTimeout(alleyTimer); } catch (e) { /* noop */ } alleyTimer = null; }
    unbindSkip();
    dropCls(scene.root, 'pb-arrive');
    try { if (doc.documentElement) dropCls(doc.documentElement, 'pb-arriving'); } catch (e) { /* noop */ }
    try { scene.showView('wide'); } catch (e) { log('prize booth walk-in failed'); }
  }

  function onSkipPointer() { landWindow(); }

  /** Every key skips EXCEPT Esc, which is the fold's press and not ours: the
   *  fold lands the window itself (escapeStep below) and answers TRUE, so one
   *  Esc during the arrival puts you at the counter rather than back on the
   *  quad. Nothing here calls preventDefault - a beat may not eat a key. */
  function onSkipKey(ev) {
    const k = ev && ev.key;
    if (k === 'Escape' || k === 'Esc') return;
    landWindow();
  }

  function bindSkip() {
    try {
      if (typeof doc.addEventListener !== 'function') return;
      doc.addEventListener('pointerdown', onSkipPointer, true);
      doc.addEventListener('keydown', onSkipKey, true);
    } catch (e) { /* a beat that cannot be skipped is still a beat */ }
  }

  function unbindSkip() {
    try {
      if (typeof doc.removeEventListener !== 'function') return;
      doc.removeEventListener('pointerdown', onSkipPointer, true);
      doc.removeEventListener('keydown', onSkipKey, true);
    } catch (e) { /* noop */ }
  }

  /* ---------------------------------------------------------- THE SHELF */

  /**
   * THE SHOP, IN THE WINDOW. shell/prizecounter.js, mounted in a scene panel
   * over this plate: the Records Office's arrangement, and it exists for the
   * Records Office's reason. A shop you reach by LEAVING the window you are
   * standing at is a shop in a different building.
   *
   * THE PANEL IS OURS AND THE CONTENTS ARE THE SHELL'S. `onShop(panel, close)`
   * hands over the box and the way out; nothing under this line has ever known
   * what a catalog, a wallet or an echo is, and that does not change because
   * the shop moved indoors.
   */
  function openShop() {
    if (dead || closed || shopOn) return;
    shopOn = true;
    closeTill = null;              // the chassis takes any tray panel down for us
    closeShop = scene.openOverlay('shop', function (panel) {
      addCls(panel, 'pb-shop');
      try { if (c.onShop) c.onShop(panel, shutShop); }
      catch (e) { log('prize booth shop mount threw: ' + ((e && e.message) || e)); }
    });
    /* THE SCRIM IS A WAY OUT WE DID NOT WIRE. scene.js closes the overlay on a
     * scrim press without telling anybody, so this is how we hear about it -
     * our listener runs after the chassis's on the same node, by which time the
     * panel is already off and all that is left is to say so. */
    const scrim = findCls(scene.root, 'asc-scrim');
    try {
      if (scrim && typeof scrim.addEventListener === 'function') {
        scrim.addEventListener('click', function () { noteShopGone(); });
      }
    } catch (e) { /* noop */ }
  }

  /** Take the panel down from this side. The shell's Back button lands here. */
  function shutShop() {
    if (!shopOn) return;
    const off = closeShop;
    noteShopGone();
    if (off) { try { off(); } catch (e) { /* noop */ } }
  }

  /** The panel has gone, by whatever road. ONCE (trap 28's shape): the shell
   *  destroys a counter here and a second call would destroy the next one. */
  function noteShopGone() {
    if (!shopOn) return;
    shopOn = false;
    closeShop = null;
    try { if (c.onShopClosed) c.onShopClosed(); }
    catch (e) { log('prize booth shop close threw: ' + ((e && e.message) || e)); }
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
    /* THE SAME PAIR openShop() KEEPS. `onAction` already refuses while the
     * shutter is down and the rects are not even built (scene.js's `when`
     * gate), so this is the belt behind those braces: the tray is a reading
     * of a wallet at a counter, and a counter that is shut is not reading
     * anything out. */
    if (dead || closed) return;
    /* ONE PANEL AT A TIME is the chassis's rule, not ours: opening this takes
     * the shelf down without asking. Saying so first is what keeps the shell's
     * counter from outliving the box it was mounted in. */
    noteShopGone();
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

      /* WHAT YOU ARE CARRYING. The third question, answered under the other
       * two and read at OPEN like both of them - the panel is built fresh
       * every time the tray comes out, so there is nothing here to keep in
       * step with a wallet that moved while it was shut. */
      panel.appendChild(holdings());
    });
  }

  /* ------------------------------------------------------ THE HOLDINGS TRAY */

  /**
   * One row per consumable on you, and an honest sentence when there are none.
   *
   * STILL NOT A SECOND TILL. Every number in here is READ, nothing proposes a
   * purchase, and the only press a row can ever grow is `onUse` - which the
   * shell does not pass, because there is no sku in this school a player
   * spends by hand. See the two tables at the top of the file.
   */
  function holdings() {
    const box = el('section', 'pb-hold-tray');
    box.appendChild(el('h4', 'pb-hold-head', t('booth_holdings', 'What you are holding')));
    const rows = holdingRows();
    if (!rows.length) {
      /* AN EMPTY POCKET IS AN ANSWER. A blank panel reads as a thing that
       * failed to load, exactly the way `prize_no_payday` reads above it. */
      box.appendChild(el('p', 'pb-hold-none', t('booth_hold_none',
        'Nothing in your pockets tonight. The shelf is through the window.')));
      return box;
    }
    const list = el('ul', 'pb-hold-list');
    for (const row of rows) list.appendChild(holdingNode(row));
    box.appendChild(list);
    return box;
  }

  /** The consumables the player actually holds, in the catalog's own order. */
  function holdingRows() {
    const inv = readInv();
    const out = [];
    for (const item of readCatalog()) {
      if (!item || item.kind !== 'consumable') continue;
      let n = 0;
      try { n = heldCount(inv, item.sku); } catch (e) { n = 0; }
      if (n <= 0) continue;
      out.push({ item: item, n: n, cap: stackCapFor(item) });
    }
    return out;
  }

  function readInv() {
    try {
      const v = (typeof c.inv === 'function') ? c.inv() : null;
      return (v && typeof v === 'object') ? v : {};
    } catch (e) { return {}; }
  }

  function readCatalog() {
    try {
      const v = (typeof c.catalog === 'function') ? c.catalog() : null;
      return Array.isArray(v) ? v.filter(function (r) { return r && r.sku; }) : [];
    } catch (e) { log('prize booth catalog read threw'); return []; }
  }

  /** The shell's cap first, then the row's own `max` off the wire. Zero means
   *  the tray shows a bare count instead of `n/max`. */
  function stackCapFor(item) {
    let n = NaN;
    try { if (typeof c.stackMax === 'function') n = Number(c.stackMax(item.sku)); }
    catch (e) { n = NaN; }
    if (!Number.isFinite(n) || n <= 0) n = Number(item && item.max);
    return Number.isFinite(n) && n > 0 ? Math.floor(n) : 0;
  }

  /**
   * ONE THING IN YOUR POCKET: the sprite the shelf sells it under, its name,
   * how many of how many, and then either the one press that spends it or the
   * sentence that says why there is no press.
   */
  function holdingNode(row) {
    const item = row.item;
    const li = el('li', 'pb-hold');
    attr(li, 'data-sku', item.sku);

    /* THE SHELF'S OWN ART, so a thing looks the same in your pocket as it did
     * in the window. A sku with no png yet leaves the glyph standing, which is
     * prizecounter.js's own rule for the same table. */
    let art = null;
    try { art = spriteUrl(item.sku); } catch (e) { art = null; }
    if (art) {
      const img = el('img', 'pb-hold-art');
      try { img.src = art; img.alt = ''; } catch (e) { /* noop */ }
      attr(img, 'aria-hidden', 'true');
      attr(img, 'loading', 'lazy');
      li.appendChild(img);
    } else {
      const glyph = el('i', 'pb-hold-glyph', HOLD_GLYPH);
      attr(glyph, 'aria-hidden', 'true');
      li.appendChild(glyph);
    }

    const text = el('div', 'pb-hold-text');
    const head = el('p', 'pb-hold-line');
    head.appendChild(el('b', 'pb-hold-name', t(item.nameKey, item.nameEn || item.sku)));
    /* `2/3`, never a translated sentence with a number baked into it - the
     * same ruling `prize_held` and `prize_short` are already built on. */
    head.appendChild(el('span', 'pb-hold-n',
      row.cap > 0 ? (row.n + '/' + row.cap) : String(row.n)));
    text.appendChild(head);

    const verb = useVerbFor(item.sku);
    if (verb && typeof c.onUse === 'function') {
      const btn = el('button', 'pb-hold-use', t(verb[0], verb[1]));
      attr(btn, 'type', 'button');
      try {
        if (typeof btn.addEventListener === 'function') {
          btn.addEventListener('click', function () {
            try { c.onUse(item.sku); }
            catch (e) { log('prize booth use threw: ' + ((e && e.message) || e)); }
          });
        }
      } catch (e) { /* a verb that cannot be wired is not offered */ }
      li.appendChild(text);
      li.appendChild(btn);
      return li;
    }

    /* NO VERB, SO SAY WHY. This is the whole of what a player is missing
     * today: a late slip is not a thing you take out and hand over, it is a
     * thing that gets handed over for you on the night you are not here. */
    const passive = passiveLineFor(item.sku);
    text.appendChild(el('p', 'pb-hold-passive', t(passive[0], passive[1])));
    li.appendChild(text);
    return li;
  }

  /** One currency reading, the counter's own two marks. `cur` is 't' or 'k'. */
  function coin(cur) {
    const b = readBalance();
    const box = el('span', 'pb-coin pb-coin-' + cur);
    const ico = el('i', cur === 'k' ? 'arc-tok' : 'arc-tick', cur === 'k' ? TOKEN_MARK : null);
    attr(ico, 'aria-hidden', 'true');
    const total = cur === 'k' ? b.k : b.t;
    const num = el('b', 'pb-coin-n', String(total));
    box.appendChild(num);
    box.appendChild(ico);
    /* THE BANK, POINTED FORWARDS. A number that is simply THERE when the tray
     * opens is a number nobody reads; a number that arrives is one you watch
     * land. Half a second, tabular digits so the row cannot jitter while it
     * runs, and once per open - the panel is built fresh every time the tray
     * comes out, so there is no repaint to guard against.
     *
     * IT COUNTS TO THE HOST'S NUMBER AND STOPS THERE. The tray reads the
     * wallet, it has never moved one, and this changes nothing about that: the
     * last frame of the count is the same digit the panel would have painted
     * flat. Reduced motion gets that digit and no count. */
    countUp(num, total, { reduced: htmlReduced() || !!c.reduced });
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
  /**
   * THE SIGN ON THE RIGHT-HAND WALL, hung once at build and never touched
   * again - it is painted furniture, not state. See the header for why it
   * outlives the shutter.
   *
   * It goes on `wide` only. The corridor is a 700ms beat that ANY press cuts
   * short (bindSkip), so a button mounted in it would be a button that can only
   * ever be used to skip the thing it is mounted in - which is not a sign, it
   * is a trap with a label on it.
   *
   * No `onLocker`, no sign: this file does not know the shell has a Locker and
   * will not draw a door it has not been given.
   */
  function hangLockerSign() {
    if (dead || typeof c.onLocker !== 'function') return;
    let node = null;
    try {
      node = alleySign({
        variant: 'booth',
        t: t,
        log: log,
        onGo: function () { if (!dead) { try { c.onLocker(); } catch (e) { log('prize booth locker sign threw'); } } },
      });
    } catch (e) { log('prize booth could not build the locker sign'); return; }
    if (!node) return;
    try { signOff = scene.mountInView('wide', node, BOOTH_SIGN_RECT.slice()); }
    catch (e) { signOff = null; }
  }

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
      /* THE SHUTTER LANDS. It used to arrive by fade, which is the one thing a
       * steel shutter does not do. The plate's own drop is in prizebooth.css
       * (pb-shut-in now carries the 1.04 -> 1 settle); this is the sound
       * catching it, on the last 60ms of the 420ms fall so the thump and the
       * stop are the same event. Reduced motion has no fall to catch, so the
       * cue lands at once and carries the beat on its own. */
      const shutStill = htmlReduced() || !!c.reduced;
      setTimeout(function () {
        if (dead || !shutEl) return;
        cue('door', 0.3);
      }, shutStill ? 0 : 360);
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
    if (closed) {
      if (closeTill) { try { closeTill(); } catch (e) { /* noop */ } closeTill = null; }
      /* The shutter comes down over a shop somebody is standing in. The panel
       * goes with it and the shell hears about it the same way it hears about
       * every other close, so the counter is never left mounted in a box that
       * is no longer on screen. */
      shutShop();
    }
    try { scene.setFlag('closed', closed); } catch (e) { /* noop */ }
    paintShut();
  }

  /* ------------------------------------------------------------- the fold */

  /**
   * ONE RUNG DEEP, and it is a panel: the shelf or the tray, whichever is up,
   * and then FALSE so the shell's own rung walks out to the campus. This module
   * binds no key for the fold; the shell asks.
   *
   * THE ARRIVAL IS THE ONE THING ABOVE THAT RUNG. A press of Esc while the
   * corridor is on screen means "get on with it", not "let me out of a booth I
   * have not reached yet", so it lands the window and spends the press. Every
   * other key skips it without going through here (bindSkip above).
   *
   * The chassis's own fold would try to walk `alley` back to `wide` as if it
   * were a close-up, which is why landWindow() runs FIRST and answers for it:
   * the corridor is a beat, and a beat is never a place you can be sent back to.
   */
  function escapeStep() {
    if (dead) return false;
    if (alleyUp) { landWindow(); return true; }
    const wasShop = shopOn;
    let took = false;
    try { took = !!scene.escapeStep(); } catch (e) { took = false; }
    if (wasShop && took) noteShopGone();
    return took;
  }

  function destroy() {
    if (dead) return;
    /* The arrival's own two holds go before the flag does, because both of them
     * reach back into a scene that is about to stop existing. */
    if (alleyTimer) { try { clearTimeout(alleyTimer); } catch (e) { /* noop */ } alleyTimer = null; }
    unbindSkip();
    alleyUp = false;
    try { if (doc.documentElement) dropCls(doc.documentElement, 'pb-arriving'); } catch (e) { /* noop */ }
    /* The shell is told the shelf has gone BEFORE the room does, so a counter
     * mounted in the panel is torn down by its owner rather than left holding a
     * watchdog timer over a detached node. */
    noteShopGone();
    dead = true;
    closeTill = null;
    closeShop = null;
    if (hintOff) { try { hintOff(); } catch (e) { /* noop */ } hintOff = null; }
    if (signOff) { try { signOff(); } catch (e) { /* noop */ } signOff = null; }
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
    /** Open the shelf from outside - the purse chip in the chrome arrives with
     *  this already asked for, which is the shortcut half of the same split the
     *  gear and the Front Office door run. Safe on a shut counter (it declines)
     *  and safe twice (it is a level, not a toggle). */
    openShop: openShop,
    /** Put the shelf away from outside. */
    closeShop: shutShop,
    /** Cut the arrival short from outside, if one is running. */
    skipArrival: landWindow,
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
    /** The LOCKER ROOM plate on the right-hand wall, or null if none was hung. */
    lockerSign: function () { return findCls(scene.root, 'ally-sign'); },
    /** Is the shelf panel up? */
    shopUp: function () { return !!shopOn; },
    /** Is the arrival beat on screen? */
    arriving: function () { return !!alleyUp; },
  };
}

export default createPrizeBooth;
