/* ============================================================================
 * ui/opponent.js — the streamer-cam "monitor" that IS the opponent.
 *
 * Everything the other player lets us see lands here: their name, their charge
 * meter, their attention bar, the closeness they CLAIM, their emotes, and — in a
 * centred PROJECTION RECT inside the bezel — a stylized miniature of their
 * screen. The miniature is DOM/CSS only: it is a caricature, never a stream, and
 * no frame of their machine ever crosses the wire.
 *
 * Three things drive the minis, and they are different on purpose:
 *   1. the effect NAMES on their state tick ("Flashes", "Spiral", ...) — their
 *      own draft ramp, the ambience;
 *   2. the payloads WE fired (markPayloadFired / markReceipt, threaded from
 *      ui/arsenal.js through ui/hud.js) — a payload they are enduring never
 *      appears in active_effects, so the sender animates its own window;
 *   3. the emotes WE sent (markEmoteFired) — see the tap below.
 * A payload that comes back `survived` gets the green wash + checkmark: the one
 * piece of motion here that is feedback rather than ambience, and the only one
 * exempt from the two-mini motion budget.
 *
 * …and one thing that is not a mini at all: the FLOATING VIDEO WINDOWS they have
 * up (tick `vwin`), drawn as a little staggered stack of windows in the rect.
 * A count, not an on/off — see VWIN_MAX_MINIS below for why it sits outside the
 * MINIS table.
 *
 * EMOTES ARE NOT PAYLOADS. `t:'emote'` is its own message family: no cost, no
 * rate limiter beyond the sheet's own 5 s, no `accepted` ACK and NO RECEIPT AT
 * ALL. So there is nothing for ui/hud.js to thread the way it threads a fire,
 * and the mini runs on a fixed dwell instead of a receipt lifecycle. Both
 * directions are drawn, in different places, because they mean different things:
 *   OUTBOUND (markEmoteFired) — our line landing on THEIR screen: a speech
 *     bubble inside the projection rect, exactly like the payload minis;
 *   INBOUND  (showEmote)      — their line, spoken AT us: the .gg-mon-bubble on
 *     the bezel, which is not part of their screen and never was.
 *
 * The monitor is also the payload DROP TARGET (ui/arsenal.js hit-tests against
 * the element this module exposes as `dropTarget`) — and, since 2026-08-04, the
 * place an INBOUND payload visibly comes FROM: markInbound() flares the bezel and
 * throws a projectile at our stage that lands as the effect starts. See THE
 * THROW below for why it cannot delay anything.
 *
 * TRUST: every remote-sourced string is written with textContent. The engine
 * already sanitized it; this module never builds markup from it either way.
 *
 * Node-import-safe: no DOM at import, only inside mountOpponent().
 * ==========================================================================*/

import { GoonConnectionHealth } from '../core/match.js';
import { GoonConsts, GoonElement, GoonMatchPhase, GoonPayloadKind, enumName } from '../core/contracts.js';
import { GoonReceiptStatus } from '../core/scoring.js';
import { S } from './strings.js';
import { createPreview, stickerUrl, markFor, throwWord, warmSticker } from './throwPreview.js';

/** Closeness 0-3 -> the word that always rides with the colour. */
export const CLOSENESS_WORDS = Object.freeze(['steady', 'warm', 'close', 'edge']);

/** Emote bubble dwell. */
const EMOTE_MS = 4000;

/**
 * Miniature parts. `key` is the wire name on tick.active_effects; `anim` marks the
 * ones that carry motion — at most ANIM_BUDGET of those run at a time. The two
 * `anim: false` ones (the lock card mockup, the drain veil) are always drawn:
 * they are a STATE, not a motion, and they cost nothing to leave on screen.
 */
const MINIS = Object.freeze([
  { key: 'Flashes', cls: 'gg-mini-flash', anim: true },
  { key: 'Bubbles', cls: 'gg-mini-bubbles', anim: true },
  { key: 'Videos', cls: 'gg-mini-video', anim: true },
  { key: 'Spiral', cls: 'gg-mini-spiral', anim: true },
  { key: 'BouncingText', cls: 'gg-mini-bounce', anim: true },
  { key: 'Subliminals', cls: 'gg-mini-sub', anim: true },
  { key: 'LockCards', cls: 'gg-mini-lock', anim: false },
  { key: 'ToyPatterns', cls: 'gg-mini-toy', anim: true },
  { key: 'BrainDrain', cls: 'gg-mini-drain', anim: false },
  // Not a wire effect name — nothing ever puts 'Emote' in active_effects. It is
  // in this table so the emote rides the SAME machinery as every other mini:
  // one window in `windows`, one budget slot, one is-on/is-anim/is-yours pass.
  { key: 'Emote', cls: 'gg-mini-emote', anim: true },
]);

/** The emote mini's whole life. No receipt ever ends it — see the header. */
export const EMOTE_MINI_MS = 2600;
/** The wiggle+fade that plays out the last of that dwell. */
const EMOTE_MINI_OUTRO_MS = 420;
/** The bubble is ~90 px wide on a 220 px monitor; anything longer is ellipsis. */
const EMOTE_MINI_TEXT_MAX = 22;
/** Two marks of the same line inside this are one send seen twice. */
const EMOTE_MINI_DEDUPE_MS = 400;
/** The synthetic window id: there is only ever one emote on their screen. */
const EMOTE_WINDOW_ID = '#emote';

/**
 * What YOU fired -> the mini that should play while it is landing on them.
 * active_effects only lists their sustained ramp elements; a payload they are
 * enduring never appears there, so the sender animates it from its own window.
 */
export const MINI_FOR_PAYLOAD = Object.freeze({
  [GoonPayloadKind.FlashBurst]: 'Flashes',
  [GoonPayloadKind.SubliminalStorm]: 'Subliminals',
  [GoonPayloadKind.BubbleSwarm]: 'Bubbles',
  [GoonPayloadKind.Video]: 'Videos',
  [GoonPayloadKind.LockCard]: 'LockCards',
  [GoonPayloadKind.ToyPattern]: 'ToyPatterns',
  [GoonPayloadKind.BrainDrain]: 'BrainDrain',
  [GoonPayloadKind.Spiral]: 'Spiral',
});

const ANIM_BUDGET = 2;

/**
 * Receipt statuses that END a payload window.
 *
 * A payload gets TWO receipts, not one: `accepted` the instant the peer admits
 * it, and a terminal one when it is over. Closing on `accepted` closed every
 * window ~60 ms after it opened — i.e. before the lead time had even elapsed,
 * so no payload we fired ever showed on their screen. Anything NOT in this set
 * (a future status, a garbled one) is treated as non-terminal and left to the
 * window's own timer, which is clamped at MAX_WINDOW_MS and cannot strand it.
 */
const TERMINAL_RECEIPTS = new Set([
  GoonReceiptStatus.Survived,
  GoonReceiptStatus.Completed,
  GoonReceiptStatus.RejectedRate,
  GoonReceiptStatus.RejectedFiltered,
]);

/** Terminal-status test that also covers `rejected_*` reasons added later. */
export function isTerminalReceipt(status) {
  const s = String(status || '');
  return TERMINAL_RECEIPTS.has(s) || s.indexOf('rejected') === 0;
}

/**
 * FLOATING VIDEO WINDOWS ON THEIR SCREEN (tick `vwin`, 2026-08-04).
 *
 * Not an entry in MINIS, and that is the whole design note. Every row up there is ONE node that is
 * either on or off, keyed by an effect NAME; this is a COUNT — up to four little windows, drawn as
 * four little windows, appearing and vanishing one at a time as they open and close over there.
 * The wire cannot express it any other way either: half of those windows are ones they popped for
 * themselves off their own bubble field, so they never appear in `active_effects` and the other
 * side has no way to infer them. Hence the optional integer, and hence a stack of its own.
 *
 * It is INFORMATION (batch-3 law): the minis stay drawn while the local machine is "hot" because
 * .gg-mon-proj re-declares --gg-deco-play, and prefers-reduced-motion leaves them visible and
 * still. The count is the datum, so nodes are added and REMOVED synchronously with it — no
 * out-animation is allowed to hold a window open for a few hundred ms and misreport the number.
 */
const VWIN_MAX_MINIS = 4;

/* ---------------------------------------------------------------------------
 * THE THROW — cause and effect for an inbound payload (2026-08-04).
 *
 * "when the opponent fires a payload at us the opponent monitor should
 *  highlight, and the item should fly from the monitor to our field, and THEN
 *  the effect triggers."
 *
 * Until now an inbound payload simply HAPPENED: exec/executor.js rendered it at
 * `fireAtLocalMs` and the only acknowledgement was ui/audio.js's `payload-in`
 * thud. There was no actor. So: their monitor flares, a projectile leaves it,
 * and it lands ON the instant the effect starts.
 *
 * NOTHING HERE MAY DELAY ANYTHING. The engine schedules every payload
 * GoonConsts.MinScheduleBufferMs (1000 ms) ahead so both clients fire it at the
 * same wall instant, and this animation is fitted INSIDE that lead: it starts at
 * `wait - THROW_MS` and impacts at `wait`. The executor's timer, the receipts and
 * the ACKs are untouched — this file cannot reach them and must never want to.
 * A short lead (clock skew, a peer that scheduled tight) shortens the FLIGHT
 * instead of moving the effect: worst case the projectile lands a couple of
 * hundred ms into an effect that already started, which is a cosmetic overlap
 * and not a stalled duel.
 *
 * WHAT FLIES is a live preview of the content that is about to play whenever the
 * payload is backed by media — the clip itself for a Video, the image a burst
 * will throw — with the item sticker painted underneath it until the first frame
 * decodes, and left showing for the kinds that have no content (see
 * ui/throwPreview.js for which are which, and how exact the picture is).
 *
 * EVERY KIND, AND EVERY KIND AS ITSELF (2026-08-05). markInbound was never
 * kind-gated — ui/hud.js hands it every accepted payload and it has always
 * launched one projectile per kind — but for the six kinds with no live preview
 * the ONLY pixels were the item cutout, a ~650 KB PNG whose fetch began when the
 * projectile did and regularly landed after it. What flew was an empty box, and
 * an empty box reads as "nothing was thrown", which is how it was reported. So
 * each projectile now carries, in order of precedence:
 *     the KIND'S GLYPH (text, frame one, no network)  <- the real floor
 *     its STICKER, revealed only once it has decoded — and WARMED at accept, so
 *       the engine's own schedule lead pays for the fetch, never the flight
 *     its LIVE PREVIEW, media kinds only, exactly as before
 *   + a per-kind TINT on the sender's flare, the projectile and the splash
 *   + THEIR WORDS under it for the two payloads that are text (LockCard,
 *     SubliminalStorm) — textContent only, see TRUST above.
 * None of that touches the schedule: the same one timer, the same two constants.
 * ------------------------------------------------------------------------- */

/** The flight, when the schedule lead can pay for it in full. */
export const THROW_MS = 760;
/** …and the shortest one we will draw when it cannot. Never longer than the lead. */
export const THROW_MIN_MS = 380;
/** The splash at the far end. Its own node, so it cannot cancel the flight. */
export const THROW_HIT_MS = 420;
/** How long their monitor stays lit for a throw. Starts with the wind-up. */
export const THROW_FLARE_MS = 620;
/** Where on our screen it lands: the stage's optical centre, not the geometric one. */
export const THROW_TARGET_Y = 0.44;
/** A lead longer than this is a clock we do not believe; the flare fires anyway. */
const MAX_THROW_LEAD_MS = 30000;

/** How long the green pass wash + checkmark hold. Feedback, never ambience. */
const PASS_MS = 1200;
/** Longest payload window we will ever hold a mini open for (engine clamp). */
const MAX_WINDOW_MS = 180000;

/* ---------------------------------------------------------------------------
 * THE LOOSE MONITOR — drag it, wheel it bigger (owner, 2026-08-04: make the
 * opponent monitor draggable and resizable "like the gifs").
 *
 * "Like the gifs" is a specification, not a vibe: exec/flashes.js and
 * exec/videos.js already settled what a grabbable thing on this desk feels like,
 * and this is that grammar, ported rather than reinvented —
 *
 *   · 6 px of slop decides press-vs-drag, and it is decided on POINTERMOVE, so a
 *     tap that never travels is not a drag. Unlike a video window a sub-slop tap
 *     here does NOTHING (the monitor has no click action and must not grow one);
 *     the only exception is the reset, below, which needs two of them.
 *   · the WHEEL resizes, through a CSS custom property that drives WIDTH
 *     (--gg-mon-loose-w, the --gg-vwin-w precedent), 0.5x..2.5x of the docked
 *     size. NEVER `transform: scale`: a scale would fight the drag offset, and
 *     it would resample the bezel art instead of re-laying it out. Width is the
 *     only dial because every number inside the frame is a PERCENTAGE of it
 *     (--gg-mon-face-*, aspect-ratio: 16/9), so the minis re-lay-out for free.
 *   · desktop-only, deliberately. A phone has no wheel and this is not worth a
 *     pinch: the drag alone already works there through pointer events.
 *
 * AND THE ONE THING THE GIFS DID NOT HAVE TO SOLVE: the monitor is a ROW IN A
 * COLUMN. A floating window is born detached; this starts life in the HUD's
 * right-hand column and only leaves it when the player asks. See DETACH-ON-
 * DEMAND in mountOpponent for why that is the choice and what holds its place.
 *
 * Every number below is exported because selftest-hud pins against these rather
 * than re-typing them.
 * ------------------------------------------------------------------------- */

/** <= this much travel and the press was not a drag. exec/videos.js's number. */
export const MON_DRAG_SLOP_PX = 6;
/** Size multiplier per wheel notch. exec/videos.js's WHEEL_STEP. */
export const MON_WHEEL_STEP = 1.08;
/** Wheel clamps, relative to the monitor's DOCKED width. videos.js's SIZE_*_FACTOR. */
export const MON_MIN_FACTOR = 0.5;
export const MON_MAX_FACTOR = 2.5;
/** Never flush against an edge: a monitor you cannot get a finger onto is lost. */
export const MON_EDGE_PAD_PX = 8;
/** The bottom gutter MERCY owns — exec/videos.js MERCY_KEEPOUT_PX, same number,
 *  same reason: the guaranteed way out is never covered, by anything. */
export const MON_MERCY_KEEPOUT_PX = 96;
/** Nothing may be "held" longer than this. exec/videos.js's HOLD_WATCHDOG_MS. */
export const MON_HOLD_WATCHDOG_MS = 30000;
/** Two sub-slop taps inside this window are the RESET gesture. */
export const MON_RESET_TAP_MS = 420;
/** The width var. Falls back to the docked --gg-mon-w when it is not written. */
export const MON_WIDTH_VAR = '--gg-mon-loose-w';
/** On .gg-mon while it is detached; on .gg-mon while it is in hand. */
export const MON_LOOSE_CLASS = 'is-loose';
export const MON_GRABBED_CLASS = 'is-grabbed';
/** On the HOST element ui/hud.js hands us, while it is holding the column open. */
export const MON_HOST_LOOSE_CLASS = 'is-mon-loose';

/**
 * The three pref keys. Position is stored in ABSOLUTE viewport px and re-clamped
 * on the way back in, so a monitor parked on a 4K desk and reopened on a laptop
 * lands on screen rather than off the edge of it.
 */
export const MON_PREF_X = 'monitorX';
export const MON_PREF_Y = 'monitorY';
export const MON_PREF_SCALE = 'monitorScale';
/** x/y sentinel for "never dragged — dock it". Any real position is >= 0. */
export const MON_DOCKED = -1;

/**
 * The monitor's DOCKED width in px — hud.css's `--gg-mon-w: clamp(210px, 22vw,
 * 280px)`, in JS, because the scale factor has to be relative to something and
 * a computed style is not available on every host this file runs on.
 * @param {number} viewportW
 */
export function monitorBaseWidth(viewportW) {
  const w = Number(viewportW) > 0 ? Number(viewportW) : 1280;
  return Math.round(Math.min(280, Math.max(210, w * 0.22)));
}

/** Any number -> a legal scale factor. Pure, total. */
export function clampMonitorScale(scale) {
  const s = Number(scale);
  if (!isFinite(s) || s <= 0) return 1;
  return s < MON_MIN_FACTOR ? MON_MIN_FACTOR : (s > MON_MAX_FACTOR ? MON_MAX_FACTOR : s);
}

/** One wheel gesture: `notches` positive GROWS. Pure, total. */
export function monitorScaleStep(scale, notches) {
  const s = clampMonitorScale(scale);
  const n = Number(notches);
  if (!isFinite(n) || !n) return s;
  return clampMonitorScale(s * Math.pow(MON_WHEEL_STEP, n));
}

/** scale + viewport -> the width to write into MON_WIDTH_VAR. Pure, total. */
export function monitorWidth(scale, viewportW) {
  return Math.round(monitorBaseWidth(viewportW) * clampMonitorScale(scale));
}

/**
 * WHERE IT IS ALLOWED TO BE. Two rules, applied in this order and no other:
 *
 *   1. ON SCREEN, with MON_EDGE_PAD_PX to spare. A monitor bigger than the
 *      viewport is pinned to the top-left rather than pushed off it — a clamp
 *      whose min exceeds its max must resolve to the reachable end.
 *   2. OFF MERCY. exec/videos.js's keepOutRects() refuses to BEAR a floating
 *      window in the bottom-centre gutter (0.28w..0.72w, bottom 96px); the same
 *      rectangle is a hard wall here, because unlike a video window this one can
 *      be parked and left there. The resolution is a single axis — pushed UP,
 *      never sideways — so a drag along the bottom of the screen slides along
 *      the top of the gutter instead of teleporting around the button.
 *
 * Pure and total: no DOM, no rounding surprises, safe to sweep in a test.
 * @returns {{x:number, y:number}} the corrected top-left, rounded
 */
export function clampMonitorPos(x, y, w, h, viewportW, viewportH) {
  const vw = Number(viewportW) > 0 ? Number(viewportW) : 1280;
  const vh = Number(viewportH) > 0 ? Number(viewportH) : 720;
  const ww = Math.max(0, Number(w) || 0);
  const hh = Math.max(0, Number(h) || 0);
  const pad = MON_EDGE_PAD_PX;
  const maxX = Math.max(pad, vw - ww - pad);
  const maxY = Math.max(pad, vh - hh - pad);
  let cx = Number(x); if (!isFinite(cx)) cx = pad;
  let cy = Number(y); if (!isFinite(cy)) cy = pad;
  cx = cx < pad ? pad : (cx > maxX ? maxX : cx);
  cy = cy < pad ? pad : (cy > maxY ? maxY : cy);
  // …and off the one rectangle that is never covered.
  const mx0 = vw * 0.28;
  const mx1 = vw * 0.72;
  const my0 = vh - MON_MERCY_KEEPOUT_PX;
  if (cx < mx1 && cx + ww > mx0 && cy + hh > my0) cy = Math.max(pad, my0 - hh);
  return { x: Math.round(cx), y: Math.round(cy) };
}

// ------------------------------------------------------------------ helpers

const doc = () => (typeof document !== 'undefined' ? document : null);

function el(tag, cls, text) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const n = d.createElement(tag);
  if (cls && n) n.className = cls;
  if (text != null && n) n.textContent = String(text);
  return n;
}

function add(parent, child) {
  if (parent && child && typeof parent.appendChild === 'function') parent.appendChild(child);
  return child;
}

function cls(node, name, on) {
  if (!node || !node.classList) return;
  try { node.classList[on ? 'add' : 'remove'](name); } catch (_e) { /* stub DOM */ }
}

function text(node, value) {
  if (node) node.textContent = value == null ? '' : String(value);
}

function sfx(audio, id) {
  try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ }
}

function createLedger() {
  const list = [];
  return {
    add(fn) { if (typeof fn === 'function') list.push(fn); },
    listen(target, type, fn, opts) {
      if (!target || typeof target.addEventListener !== 'function') return;
      target.addEventListener(type, fn, opts);
      list.push(() => { try { target.removeEventListener(type, fn, opts); } catch (_e) { /* gone */ } });
    },
    interval(ms, fn) {
      if (typeof setInterval !== 'function') return 0;
      const id = setInterval(fn, ms);
      list.push(() => { try { clearInterval(id); } catch (_e) { /* gone */ } });
      return id;
    },
    run() { while (list.length) { const fn = list.pop(); try { fn(); } catch (_e) { /* keep unwinding */ } } },
  };
}

/** Effect names arrive as strings from the wire; be forgiving about codes too. */
function effectName(v) {
  if (typeof v === 'string') return v;
  if (typeof v === 'number') return enumName(GoonElement, v);
  return '';
}

// ------------------------------------------------------------------ mount

/**
 * @param {object} o
 * @param {Element} o.host             where the monitor column is appended
 * @param {object}  o.match            GoonMatchService
 * @param {object}  [o.audio]          {sfx(id)}
 * @param {object}  [o.fx]             chrome-animation budget from ui/hud.js
 * @param {object}  [o.prefs]         ui/prefs.js store — where the monitor's
 *        dragged position and wheel size are remembered. Optional in the same
 *        way every other consumer treats it: no store, no memory, everything
 *        else identical.
 * @returns {{unmount:Function, root:Element|null, dropTarget:Element|null, showEmote:Function}}
 */
export function mountOpponent({ host, match, audio = null, fx = null, prefs = null } = {}) {
  const led = createLedger();
  const root = el('div', 'gg-mon');
  if (!root || !host) {
    return {
      unmount() { led.run(); },
      root: null, dropTarget: null,
      showEmote() {}, markEmoteFired() { return false; },
      isLoose() { return false; }, loosePlacement() { return null; }, redock() { return false; },
    };
  }

  // ---- head: name · connection dot · their score · charge pips ------------
  const head = add(root, el('div', 'gg-mon-head'));
  const dot = add(head, el('i', 'gg-mon-dot'));
  const nameEl = add(head, el('span', 'gg-mon-name', 'opponent'));
  const scoreEl = add(head, el('span', 'gg-mon-score', '0'));
  const pipRow = add(head, el('span', 'gg-mon-pips'));
  const pips = [];
  for (let i = 0; i < GoonConsts.ChargeCap; i++) pips.push(add(pipRow, el('i', 'gg-pip gg-pip--sm')));

  // ---- the screen inside the bezel ---------------------------------------
  // THE STACKING ORDER HERE IS LOAD-BEARING. assets/monitor_frame.png (935x667
  // since 2026-08-04) is a CRT television on a TRANSPARENT background — the
  // surround is a cutout now, but THE SCREEN FACE IS STILL PAINTED OPAQUE BLACK,
  // which is the only part of the old "it is not a cutout" note that this fix
  // ever depended on. So the four layers still go, bottom to top:
  //
  //   .gg-mon-screen  dark glass behind the face; the fallback backdrop  (z1);
  //   .gg-mon-bezel   the art itself                                     (z2);
  //   .gg-mon-proj    the PROJECTION RECT, where every miniature plays   (z3);
  //   .gg-mon-throw   the flare when they throw something at us          (z4).
  //
  // The projection rect used to be a CHILD of .gg-mon-screen, and z-index:1 on
  // that element opens a stacking context — no z-index on a descendant can climb
  // out of it, so every mini rendered underneath the painted black screen and
  // nothing was ever visible. DOM order below and `z-index: 3` in hud.css are
  // two halves of one fix; do not separate them.
  const frame = add(root, el('div', 'gg-mon-frame'));
  add(frame, el('div', 'gg-mon-screen'));

  const bezel = el('img', 'gg-mon-bezel');
  if (bezel) {
    bezel.src = './assets/monitor_frame.png';
    bezel.alt = '';
    bezel.decoding = 'async';
    led.listen(bezel, 'error', () => { cls(frame, 'is-nobezel', true); try { bezel.remove(); } catch (_e) { /* gone */ } });
    add(frame, bezel);
  }

  // …and the projection rect ON TOP of the art, sized (in hud.css, off the
  // --gg-mon-face-* custom properties) to the CRT face the art paints.
  const proj = add(frame, el('div', 'gg-mon-proj'));
  const parts = new Map();
  let emoteIconEl = null;
  let emoteTextEl = null;
  for (const m of MINIS) {
    const node = add(proj, el('div', 'gg-mini ' + m.cls));
    if (m.key === 'Flashes') for (let i = 0; i < 4; i++) add(node, el('i', 'gg-mini-shard'));
    if (m.key === 'Bubbles') for (let i = 0; i < 5; i++) add(node, el('i', 'gg-mini-bub'));
    if (m.key === 'Videos') {
      for (let i = 0; i < 3; i++) add(node, el('i', 'gg-mini-bar'));
      add(node, el('i', 'gg-mini-scan'));
    }
    if (m.key === 'Spiral') add(node, el('i', 'gg-mini-spiral-disc'));
    if (m.key === 'Subliminals') add(node, el('span', 'gg-mini-line', 'deeper'));
    if (m.key === 'BouncingText') add(node, el('span', 'gg-mini-word', 'good girl'));
    if (m.key === 'LockCards') {
      const card = add(node, el('div', 'gg-mini-card'));
      for (let i = 0; i < 3; i++) add(card, el('i', 'gg-mini-scribble'));
      add(card, el('i', 'gg-mini-caret'));
    }
    if (m.key === 'ToyPatterns') add(node, el('i', 'gg-mini-toy-dot'));
    if (m.key === 'BrainDrain') add(node, el('i', 'gg-mini-drain-vig'));
    if (m.key === 'Emote') {
      const bub = add(node, el('div', 'gg-mini-emote-bub'));
      emoteIconEl = add(bub, el('span', 'gg-mini-emote-icon'));
      emoteTextEl = add(bub, el('span', 'gg-mini-emote-text'));
    }
    parts.set(m.key, node);
  }
  // …and their floating video windows, which are a COUNT rather than an effect (see VWIN_MAX_MINIS
  // above). The container is always here and empty; paintWindowMinis fills it.
  const vwinBox = add(proj, el('div', 'gg-mon-vwins'));
  /** @type {Element[]} the live window minis, oldest first — index IS the stagger slot. */
  const vwinMinis = [];

  const idle = add(proj, el('div', 'gg-mini-idle', S.monitor.idle));

  // pass feedback: a subtle green wash over the projection + a checkmark. This
  // pair is EXEMPT from the motion budget and from the "hot" parking rule — it
  // answers something the player did, it is not ambience.
  const passWash = add(proj, el('i', 'gg-mon-pass'));
  const check = add(proj, el('div', 'gg-mon-check', '✓'));
  if (check && check.setAttribute) {
    check.setAttribute('role', 'status');
    check.setAttribute('aria-label', S.monitor.passed);
  }

  // THE THROW FLARE (z4, above the projection). Its own node because it carries
  // a one-shot and everything under it carries loops: one `animation` shorthand
  // per element, or they cancel each other. It is INFORMATION, not chrome — it
  // re-declares --gg-deco-play like the rect does, so a hot local stack cannot
  // park the one flourish that explains where the next 60 seconds came from.
  const flare = add(frame, el('i', 'gg-mon-throw'));

  // emote bubble — one at a time, textContent only
  const bubble = add(frame, el('div', 'gg-mon-bubble'));
  if (bubble) bubble.hidden = true;
  const bubbleIcon = add(bubble, el('span', 'gg-mon-bubble-icon'));
  const bubbleText = add(bubble, el('span', 'gg-mon-bubble-text'));

  // drop hint (arsenal flips this on while an item is armed / dragging)
  const hint = add(frame, el('div', 'gg-mon-hint', S.monitor.dropHint));
  if (hint) hint.hidden = true;

  // ---- attention slim bar ------------------------------------------------
  const attWrap = add(root, el('div', 'gg-mon-att'));
  const attFill = add(attWrap, el('i', 'gg-mon-att-fill'));
  const attLabel = add(root, el('div', 'gg-mon-att-label', 'their focus 100%'));

  // ---- the closeness gauge: what they CLAIM -------------------------------
  const gauge = add(root, el('div', 'gg-mon-close'));
  const gaugeLabel = add(gauge, el('div', 'gg-mon-close-label', 'they claim'));
  const segRow = add(gauge, el('div', 'gg-mon-close-segs'));
  const segs = [];
  for (let i = 0; i < 4; i++) segs.push(add(segRow, el('i', 'gg-mon-close-seg')));
  const gaugeWord = add(gauge, el('div', 'gg-mon-close-word', 'unknown'));

  // ---- abandon countdown / connection word -------------------------------
  const foot = add(root, el('div', 'gg-mon-foot'));
  const connWord = add(foot, el('span', 'gg-mon-conn', 'live'));
  const abandonEl = add(foot, el('span', 'gg-mon-abandon'));
  if (abandonEl) abandonEl.hidden = true;

  add(host, root);

  // ------------------------------------------------------------- painting

  let lastCloseness = null;
  let lastHealth = GoonConnectionHealth.Fresh;
  let emoteTimer = 0;
  let passTimer = 0;
  let lastEmoteMarkAt = -Infinity;
  let lastEmoteMarkKey = null;
  let flareTimer = 0;
  let throwSeq = 0;

  /** Projectiles in the air right now — nodes on <body>, so unmount must sweep them. */
  const inFlight = new Set();
  led.add(() => {
    try { clearTimeout(flareTimer); } catch (_e) { /* gone */ }
    for (const rec of Array.from(inFlight)) dropFlight(rec);
  });

  /** payload id -> {key, open, timers[]} for the payloads WE fired. */
  const windows = new Map();

  function health() {
    const op = match && match.opponent;
    return op ? (op.health | 0) : GoonConnectionHealth.Fresh;
  }

  function stalePrefix() { return health() === GoonConnectionHealth.Fresh ? '' : '~'; }

  function paintMinis(op) {
    const live = new Set();
    for (const raw of (op && op.activeEffects) || []) {
      const n = effectName(raw);
      if (n) live.add(n);
    }
    if (op && op.toyActive) live.add('ToyPatterns');

    // …plus everything WE have in flight at them right now.
    const forced = new Set();
    for (const w of windows.values()) {
      if (!w.open || !w.key) continue;
      forced.add(w.key);
      live.add(w.key);
    }

    // Motion budget: at most ANIM_BUDGET minis actually move, and a payload we
    // fired outranks their ambient ramp for that budget — it is the thing the
    // player just paid for and is waiting to see land.
    const moving = new Set();
    const wants = MINIS.filter((m) => m.anim && live.has(m.key));
    wants.sort((a, b) => (forced.has(b.key) ? 1 : 0) - (forced.has(a.key) ? 1 : 0));
    for (const m of wants) { if (moving.size < ANIM_BUDGET) moving.add(m.key); }

    // An effect name we have no mini for (a newer peer, a garbled tick) must not
    // silently blank the idle word: count what we can actually DRAW.
    let drawn = 0;
    for (const m of MINIS) {
      const node = parts.get(m.key);
      if (!node) continue;
      const on = live.has(m.key);
      if (on) drawn++;
      cls(node, 'is-on', on);
      cls(node, 'is-anim', moving.has(m.key));
      cls(node, 'is-yours', forced.has(m.key));
    }
    // Their floating windows are their own stack, outside the MINIS table and outside the motion
    // budget (four ~24px rects sharing one slow drift each — see the CSS note). They count toward
    // "something is on that screen" all the same: four windows up is not an idle machine.
    drawn += paintWindowMinis(op);
    if (idle) idle.hidden = drawn > 0;
  }

  /**
   * Their floating video windows, one little rounded rect per window, staggered so four read as a
   * drifting STACK rather than a bar. Adds and removes nodes to match the count exactly — the
   * number IS the message, so nothing lingers on its way out.
   *
   * @returns {number} how many are drawn right now
   */
  function paintWindowMinis(op) {
    if (!vwinBox) return 0;
    const raw = op ? Number(op.vwin) : 0;
    const want = Number.isFinite(raw) ? Math.max(0, Math.min(VWIN_MAX_MINIS, Math.trunc(raw))) : 0;

    while (vwinMinis.length > want) {
      const node = vwinMinis.pop();
      try { node.remove(); } catch (_e) { /* stub DOM / already gone */ }
    }
    while (vwinMinis.length < want) {
      const node = add(vwinBox, el('i', 'gg-mon-vwin'));
      if (!node) break;
      // THREE elements, THREE animations — the same shorthand trap the real window solves in
      // exec/fx.css: the wrapper takes the one-shot pop-in, the body the drift loop, the dot the
      // recording pulse. Two of those on one element would silently cancel each other.
      const body = add(node, el('i', 'gg-mon-vwin-body'));
      add(body, el('i', 'gg-mon-vwin-dot'));
      vwinMinis.push(node);
    }
    cls(vwinBox, 'is-on', vwinMinis.length > 0);
    return vwinMinis.length;
  }

  function paintCloseness(op) {
    const v = op && op.closeness;
    const known = v !== null && v !== undefined;
    for (let i = 0; i < segs.length; i++) cls(segs[i], 'is-lit', known && i <= v);
    cls(gauge, 'is-edge', known && v === 3);
    gauge && gauge.setAttribute && gauge.setAttribute('data-gg-close', known ? String(v) : 'none');
    text(gaugeWord, known ? CLOSENESS_WORDS[v] : 'no word yet');

    if (known && v !== lastCloseness) {
      const runIt = () => {
        cls(gauge, 'is-sweep', true);
        setTimeout(() => cls(gauge, 'is-sweep', false), 320);
      };
      if (fx && typeof fx.play === 'function') fx.play(320, runIt); else runIt();
      if (lastCloseness !== null) sfx(audio, 'gg-taunt-up');
    }
    lastCloseness = known ? v : null;
  }

  function paint() {
    const op = (match && match.opponent) || null;
    // The minis paint even with no opponent state yet: a payload window of our
    // own is reason enough for the little screen to be doing something.
    paintMinis(op);
    if (!op) return;
    const p = stalePrefix();

    text(nameEl, op.displayName || 'opponent');
    text(scoreEl, p + String(op.score | 0));
    for (let i = 0; i < pips.length; i++) cls(pips[i], 'is-on', i < (op.charges | 0));

    const pct = Math.max(0, Math.min(100, Number(op.attentionPct) || 0));
    if (attFill && attFill.style) attFill.style.width = pct + '%';
    cls(attWrap, 'is-low', pct < 50);
    text(attLabel, 'their focus ' + p + Math.round(pct) + '%');

    paintCloseness(op);
    paintHealth(op);
  }

  function paintHealth(op) {
    const h = health();
    cls(root, 'is-wobbly', h === GoonConnectionHealth.Wobbly);
    cls(root, 'is-gone', h === GoonConnectionHealth.Dead);
    cls(dot, 'is-wobbly', h === GoonConnectionHealth.Wobbly);
    cls(dot, 'is-gone', h === GoonConnectionHealth.Dead);
    text(connWord, h === GoonConnectionHealth.Fresh ? 'live' : h === GoonConnectionHealth.Wobbly ? 'wobbly' : 'gone');

    // Once their ticks go stale the engine is already counting toward an abandon
    // at GoonConsts.TickDeadMs. Show the same clock rather than a silent freeze.
    let secs = 0;
    if (h !== GoonConnectionHealth.Fresh && op && op.lastTickLocalMs) {
      const age = nowMs() - op.lastTickLocalMs;
      secs = Math.max(0, Math.ceil((GoonConsts.TickDeadMs - age) / 1000));
    }
    if (abandonEl) {
      abandonEl.hidden = h === GoonConnectionHealth.Fresh;
      if (!abandonEl.hidden) text(abandonEl, secs > 0 ? 'abandon in ' + secs + 's' : 'abandoned');
    }
  }

  function nowMs() {
    try {
      if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now();
    } catch (_e) { /* fall through */ }
    return Date.now();
  }

  // ------------------------------------------------------------ subscribe

  function sub(name, fn) {
    if (!match || typeof match[name] !== 'function') return;
    const off = match[name](fn);
    led.add(typeof off === 'function' ? off : null);
  }

  sub('onOpponentStateChanged', () => paint());
  sub('onConnectionHealthChanged', (h) => {
    if (h !== lastHealth) lastHealth = h;
    paint();
  });
  sub('onEmoteReceived', (e) => showEmote(e && e.text, e && e.icon));

  /* ------------------------------------------------ the outgoing-emote tap
   *
   * There is no onEmoteSent/onFired for an emote to arrive on: the engine has
   * `t:'emote'` as a fire-and-forget family (core/match.js sendEmote -> _send,
   * no id, no receipt, no event), and ui/emotes.js calls it directly off the
   * sheet. The only signal that an emote actually went out is the call itself,
   * so we borrow it for the life of the mount and hand it straight back.
   *
   * markEmoteFired() below is the REAL seam. If ui/hud.js ever grows a hook
   * (mountEmotes({ onSent }) -> opponent.markEmoteFired), point it there and
   * delete this block — the dedupe inside markEmoteFired makes an overlap
   * where BOTH fire harmless rather than a double bubble.
   */
  if (match && typeof match.sendEmote === 'function') {
    const original = match.sendEmote;
    const hadOwn = Object.prototype.hasOwnProperty.call(match, 'sendEmote');
    const tapped = function sendEmoteTapped(t, i) {
      const out = original.apply(this, arguments);
      // Mirror the engine's own guard: a send it drops must not draw anything.
      try {
        if (match.phase !== GoonMatchPhase.Idle) markEmoteFired(t, i);
      } catch (_e) { /* a mini must never break a send */ }
      return out;
    };
    try {
      match.sendEmote = tapped;
      led.add(() => {
        try {
          if (match.sendEmote !== tapped) return;      // someone else re-tapped: leave it
          if (hadOwn) match.sendEmote = original;
          else delete match.sendEmote;
        } catch (_e) { /* gone */ }
      });
    } catch (_e) { /* a frozen engine just means no outgoing mini */ }
  }

  // the abandon clock has to tick even when no state arrives (that IS the point)
  led.interval(1000, () => { try { paint(); } catch (_e) { /* never break the HUD */ } });
  paint();

  /** Renders an incoming emote in the bubble. Remote text — textContent ONLY. */
  function showEmote(msg, icon) {
    if (!bubble) return;
    text(bubbleIcon, icon || '');
    text(bubbleText, msg || '');
    bubble.hidden = false;
    cls(bubble, 'is-in', true);
    sfx(audio, 'gg-emote');
    try { clearTimeout(emoteTimer); } catch (_e) { /* gone */ }
    emoteTimer = setTimeout(() => {
      cls(bubble, 'is-in', false);
      bubble.hidden = true;
    }, EMOTE_MS);
    led.add(() => { try { clearTimeout(emoteTimer); } catch (_e) { /* gone */ } });
  }

  /** Arsenal calls this while an item is armed or being dragged. */
  function setTargeted(on) {
    cls(root, 'is-targeted', !!on);
    if (hint) hint.hidden = !on;
  }

  // ------------------------------------------------- payload windows + pass

  function laterOnce(ms, fn) {
    if (typeof setTimeout !== 'function') { fn(); return 0; }
    const id = setTimeout(() => { try { fn(); } catch (_e) { /* never break the HUD */ } }, Math.max(0, ms | 0));
    led.add(() => { try { clearTimeout(id); } catch (_e) { /* gone */ } });
    return id;
  }

  function closeWindow(id) {
    const w = windows.get(id);
    if (!w) return;
    for (const t of w.timers) { try { clearTimeout(t); } catch (_e) { /* gone */ } }
    windows.delete(id);
    paint();
  }

  /**
   * hud.js calls this the moment the engine accepts one of OUR fires. We know
   * the kind, roughly when it lands (the engine schedules a fixed buffer ahead)
   * and how long it runs — that is the whole animation window, and it does not
   * depend on their tick ever mentioning it.
   *
   * @param {object} o {id, kind, durationMs, leadMs}
   */
  function markPayloadFired({ id, kind, durationMs = 0, leadMs = 0 } = {}) {
    const key = MINI_FOR_PAYLOAD[kind];
    if (!key || !id) return;
    closeWindow(id);
    const run = Math.max(1000, Math.min(MAX_WINDOW_MS, durationMs | 0));
    const w = { key, kind, open: false, timers: [] };
    windows.set(id, w);
    w.timers.push(laterOnce(Math.max(0, leadMs | 0), () => {
      const cur = windows.get(id);
      if (!cur) return;
      cur.open = true;
      paint();
    }));
    w.timers.push(laterOnce(Math.max(0, leadMs | 0) + run, () => closeWindow(id)));
  }

  /** Drops the emote window AND the classes only it uses. */
  function closeEmoteWindow() {
    const node = parts.get('Emote');
    cls(node, 'is-out', false);
    cls(node, 'is-lost', false);
    closeWindow(EMOTE_WINDOW_ID);
  }

  /**
   * An emote WE sent, landing on their little screen.
   *
   * Unlike a payload there is no lead (nothing is scheduled: the engine puts it
   * on the wire immediately) and no receipt (the family has none), so the whole
   * lifecycle is local: pop in, sit for EMOTE_MINI_MS, wiggle out. The one
   * failure state that genuinely exists is a peer whose link is already DEAD —
   * that line is not going to be read by anyone, so it greys and drops instead.
   *
   * @param   {string} [msg]  one of ui/emotes.js EMOTE_PRESETS
   * @param   {string} [icon] one of ui/emotes.js EMOTE_ICONS
   * @returns {boolean} whether a bubble was actually drawn
   */
  function markEmoteFired(msg, icon) {
    const node = parts.get('Emote');
    if (!node) return false;

    const line = String(msg == null ? '' : msg);
    const glyph = String(icon == null ? '' : icon);
    const key = line + String.fromCharCode(31) + glyph;
    const at = nowMs();
    if (key === lastEmoteMarkKey && at - lastEmoteMarkAt < EMOTE_MINI_DEDUPE_MS) return false;
    lastEmoteMarkKey = key;
    lastEmoteMarkAt = at;

    closeEmoteWindow();

    // Both of these are OUR OWN canned strings, but they go through textContent
    // for the same reason everything else on this monitor does.
    text(emoteIconEl, glyph || (line ? '' : '💬'));
    text(emoteTextEl, line.length > EMOTE_MINI_TEXT_MAX ? line.slice(0, EMOTE_MINI_TEXT_MAX - 1) + '…' : line);

    const lost = health() === GoonConnectionHealth.Dead;
    cls(node, 'is-lost', lost);

    const w = { key: 'Emote', kind: null, open: true, timers: [] };
    windows.set(EMOTE_WINDOW_ID, w);
    // The bounce-in is CSS: the node goes display:none -> flex, which restarts it.
    // (No cue here — ui/emotes.js already plays `gg-emote` on the send itself,
    // and the arsenal tile plays it again when the sheet opens. Three is a lot.)
    paint();

    if (!lost) {
      w.timers.push(laterOnce(EMOTE_MINI_MS - EMOTE_MINI_OUTRO_MS, () => {
        if (windows.get(EMOTE_WINDOW_ID) === w) cls(node, 'is-out', true);
      }));
    }
    w.timers.push(laterOnce(EMOTE_MINI_MS, () => {
      if (windows.get(EMOTE_WINDOW_ID) === w) closeEmoteWindow();
    }));
    return true;
  }

  /**
   * …and this when a receipt for one of ours comes back. `survived` is the
   * flagship: they took the whole thing and held. That earns the green.
   *
   * NOT every receipt is the end. The engine acks with `accepted` the moment
   * the peer admits the payload — roughly 60 ms after the fire, and 1.4 s
   * BEFORE the window is even due to open. Closing on that ack meant no fired
   * payload ever reached their little screen; only their own ramp did, which is
   * why the monitor looked like it only ever showed one thing.
   */
  function markReceipt(id, status) {
    if (!id) return;
    const s = String(status || '');
    if (!isTerminalReceipt(s)) return;        // `accepted` — it is still landing
    const w = windows.get(id);
    if (s === GoonReceiptStatus.Survived) playPass(w && w.key);
    closeWindow(id);
  }

  /** Green wash + checkmark, and the lock-card mockup lights up with them. */
  function playPass(key) {
    const lock = key === 'LockCards' ? parts.get('LockCards') : null;
    cls(lock, 'is-pass', true);
    cls(passWash, 'is-in', true);
    cls(check, 'is-in', true);
    sfx(audio, 'gg-endured');
    try { clearTimeout(passTimer); } catch (_e) { /* gone */ }
    passTimer = setTimeout(() => {
      cls(lock, 'is-pass', false);
      cls(passWash, 'is-in', false);
      cls(check, 'is-in', false);
    }, PASS_MS);
    led.add(() => { try { clearTimeout(passTimer); } catch (_e) { /* gone */ } });
  }

  // --------------------------------------------------------- the throw at us

  /**
   * The app's own reduced-motion preference, which ui/hud.js writes as `is-calm`
   * on the HUD frame. The projectile lives on <body> — OUTSIDE that subtree — so
   * no descendant selector can reach it and the check has to be made in JS. (The
   * OS-level `prefers-reduced-motion` is handled in hud.css, which can.)
   */
  function isCalm() {
    let n = root;
    for (let i = 0; n && i < 12; i++) {
      try { if (n.classList && n.classList.contains('is-calm')) return true; } catch (_e) { /* stub */ }
      n = n.parentNode;
    }
    return false;
  }

  function rectOf(node) {
    try {
      if (!node || typeof node.getBoundingClientRect !== 'function') return null;
      const r = node.getBoundingClientRect();
      return (r && r.width > 0 && r.height > 0) ? r : null;
    } catch (_e) { return null; }
  }

  /** Where a thrown thing lands on OUR side: over the stage, not over the desk. */
  function stagePoint() {
    const w = (typeof window !== 'undefined' && window) ? Number(window.innerWidth) : 0;
    const h = (typeof window !== 'undefined' && window) ? Number(window.innerHeight) : 0;
    if (!(w > 0) || !(h > 0)) return null;
    return { x: w / 2, y: h * THROW_TARGET_Y };
  }

  /**
   * Their monitor lights up: THEY did this, and it is about to arrive — in the
   * colour of the thing they threw, so the flare and the projectile that leaves
   * it are visibly the same event.
   */
  function flareMonitor(tint) {
    if (tint && root.style && typeof root.style.setProperty === 'function') {
      try { root.style.setProperty('--gg-throw-tint', tint); } catch (_e) { /* stub DOM */ }
    }
    cls(root, 'is-throwing', true);
    try { clearTimeout(flareTimer); } catch (_e) { /* gone */ }
    if (typeof setTimeout === 'function') {
      flareTimer = setTimeout(() => cls(root, 'is-throwing', false), THROW_FLARE_MS);
    }
  }

  /** Take one projectile down: its preview's refcount first, then its node. */
  function dropFlight(rec) {
    if (!rec) return;
    inFlight.delete(rec);
    for (const t of rec.timers) { try { clearTimeout(t); } catch (_e) { /* gone */ } }
    rec.timers.length = 0;
    if (typeof rec.destroy === 'function') { try { rec.destroy(); } catch (_e) { /* already gone */ } }
    try { if (rec.node) rec.node.remove(); } catch (_e) { /* gone */ }
  }

  /**
   * The projectile. Three nodes, three animations, ON PURPOSE: `animation` is a
   * shorthand, so a second live rule on the same element silently cancels the
   * first. The outer travels in X (linear), the middle travels in Y (with a lob
   * partway through — that is the arc), the art spins and swells. The splash at
   * the far end is a FOURTH node for the same reason. Everything the mark adds
   * below rides a TRANSITION, never an animation, for exactly that reason.
   */
  function launch(kind, payload, flightMs) {
    const d = doc();
    if (!d || !d.body) return null;
    const from = rectOf(frame);
    const to = stagePoint();
    if (!from || !to) return null;

    const fly = el('i', 'gg-throw');
    if (!fly) return null;
    const arc = add(fly, el('i', 'gg-throw-arc'));
    const art = add(arc, el('i', 'gg-throw-art'));
    if (!arc || !art) return null;

    // The kind's own colour, on the projectile and (via the rec) on the splash.
    const mark = markFor(kind);
    if (fly.style && typeof fly.style.setProperty === 'function') {
      fly.style.setProperty('--gg-throw-tint', mark.tint);
    }
    try { if (typeof fly.setAttribute === 'function') fly.setAttribute('data-gg-kind', String(kind)); }
    catch (_e) { /* stub DOM */ }

    // THE GLYPH is the real floor — text, painted on frame one, no fetch, no
    // decode, no way to fail. Everything below is allowed to be late.
    add(art, el('i', 'gg-throw-mark', mark.glyph));

    // The sticker sits over the glyph, but ONLY once it has actually decoded:
    // a background-image that has not arrived yet is not a floor, it is a hole,
    // and that hole is the whole bug this pass fixes. `is-art` is the same
    // first-decoded-frame gate .gg-throw-live's `is-ready` already uses, and
    // the fetch itself was started back at accept (markInbound).
    const sticker = stickerUrl(kind);
    if (sticker && art.style && typeof art.style.setProperty === 'function') {
      art.style.setProperty('--gg-throw-sticker', 'url(' + sticker + ')');
    }
    warmSticker(kind, () => cls(fly, 'is-art', true));

    // THEIR WORDS, for the two kinds whose payload IS text. On the ARC, not the
    // art: the art tumbles, and a spinning phrase cannot be read. `is-word`
    // steadies the item for the same reason `is-live` does.
    const word = throwWord(kind, payload);
    if (word) {
      add(arc, el('span', 'gg-throw-word', word));
      cls(fly, 'is-word', true);
    }

    // …and the live preview of what is actually about to play, when there is
    // one. The PAYLOAD goes with it: its `xfer:` tags are what make an inbound
    // preview exact rather than representative — the receiver resolves the same
    // artifact the executor is about to (ui/throwPreview.js, exec/media.js
    // drawFor). Resolved HERE, at animation start, off the same pool.
    const preview = createPreview({ kind, payload, cls: 'gg-throw-live' });
    if (preview) {
      add(art, preview.node);
      cls(fly, 'is-live', true);
    }

    const cx = from.left + from.width / 2;
    const cy = from.top + from.height / 2;
    const spin = ((throwSeq++ % 2) ? -1 : 1) * 210;
    if (fly.style) {
      fly.style.left = Math.round(cx) + 'px';
      fly.style.top = Math.round(cy) + 'px';
      if (typeof fly.style.setProperty === 'function') {
        fly.style.setProperty('--gg-throw-dx', Math.round(to.x - cx) + 'px');
        fly.style.setProperty('--gg-throw-dy', Math.round(to.y - cy) + 'px');
        fly.style.setProperty('--gg-throw-ms', Math.round(flightMs) + 'ms');
        fly.style.setProperty('--gg-throw-spin', spin + 'deg');
      }
    }
    add(d.body, fly);
    return { node: fly, destroy: preview ? preview.destroy : null, timers: [], to, tint: mark.tint };
  }

  /** The splash where it lands. Removed on a timer — animationend is not a promise. */
  function splash(to, tint) {
    const d = doc();
    if (!d || !d.body || !to) return;
    const hit = el('i', 'gg-throw-hit');
    if (!hit) return;
    if (hit.style) {
      hit.style.left = Math.round(to.x) + 'px';
      hit.style.top = Math.round(to.y) + 'px';
      // The impact is the same event as the flight and wears the same colour.
      if (tint && typeof hit.style.setProperty === 'function') hit.style.setProperty('--gg-throw-tint', tint);
    }
    add(d.body, hit);
    laterOnce(THROW_HIT_MS + 80, () => { try { hit.remove(); } catch (_e) { /* gone */ } });
  }

  function throwAtUs(kind, payload, flightMs) {
    flareMonitor(markFor(kind).tint);
    // Reduced motion: the highlight IS the feedback. Nothing travels, and the
    // `payload-in` cue still lands on the beat it always did.
    if (isCalm()) return;
    const rec = launch(kind, payload, flightMs);
    if (!rec) return;
    inFlight.add(rec);
    // Impact: the projectile is spent and the effect is starting in the same
    // frame. It goes on a TIMER rather than animationend, which a parked
    // animation, a reduced-motion override or a re-parent can all swallow.
    rec.timers.push(laterOnce(flightMs, () => {
      const at = rec.to;
      const tint = rec.tint;
      dropFlight(rec);
      splash(at, tint);
    }));
  }

  /**
   * ui/hud.js calls this the moment the engine ACCEPTS an inbound payload, with
   * the same `wait` its own landing cue uses. We do not touch the effect, the
   * receipt or the clock — we fill the lead the engine already reserved.
   *
   * @param   {object} o {kind, waitMs, payload}
   * @returns {boolean} whether anything will be drawn
   */
  function markInbound({ kind = null, waitMs = 0, payload = null } = {}) {
    if (kind === null || kind === undefined) return false;
    // Start the cutout's fetch NOW, at accept — the earliest moment we know a
    // throw is coming. The engine's schedule lead (a second and a half) is dead
    // time we already own, and spending it here is what stops a 650 KB PNG from
    // having to arrive inside a 380 ms flight. Once per kind per page.
    warmSticker(kind);
    const wait = Math.max(0, Math.min(MAX_THROW_LEAD_MS, Number(waitMs) || 0));
    // Fit the flight inside the lead; never push the impact past it by more than
    // the shortest readable throw.
    const flight = wait >= THROW_MS ? THROW_MS : Math.max(THROW_MIN_MS, wait);
    const delay = Math.max(0, wait - flight);
    if (delay <= 0) throwAtUs(kind, payload, flight);
    else laterOnce(delay, () => throwAtUs(kind, payload, flight));
    return true;
  }

  /* =========================================================================
   * THE LOOSE MONITOR — the drag, the wheel, and the hole it leaves behind.
   *
   * DETACH ON DEMAND, not always. The monitor is a row in the HUD's right-hand
   * column and it stays one until the player touches it: docked, it is
   * bit-for-bit the element it was before this feature existed — same box, same
   * flow, same size, no `position: fixed`, no measurement, no timer. The first
   * gesture that MEANS something (a drag past the slop, or a wheel notch) is
   * what takes it off the shelf, and it comes off exactly where it was standing,
   * at exactly the size it was, so nothing on screen moves at the moment of
   * detaching. Always-detached would have been fewer branches and one more bug:
   * the default spot would have to be COMPUTED, and computing "where the column
   * would have put it" is guessing at a layout that is being restructured by
   * somebody else this week.
   *
   * THE HOLE IS THE OTHER HALF. `position: fixed` takes the monitor out of the
   * column's flow, and the receipts under it would jump up by its whole height
   * the frame it leaves. So the moment before it goes, its height is measured
   * and written as a `min-height` on the HOST ui/hud.js already handed us. That
   * keeps the placeholder entirely inside this file — the column can be
   * rearranged around us without a second owner for this number — and it is a
   * MEASUREMENT rather than a constant, so it is right at every viewport.
   *
   * THE TRAPS THE GIFS DOCUMENTED, and where each one landed here:
   *   · "keyframes outrank inline transforms, so a grabbed node needs
   *     `animation: none`, and the live keyframe translate must be FOLDED into
   *     the drag offset first or it jumps 0px→anchor." The monitor's own
   *     flourishes all live on DESCENDANTS (.gg-mon-throw carries the flare;
   *     .gg-mon itself has only an opacity transition) — that is the one-
   *     animation-slot-per-node rule this file already followed — so there is
   *     nothing on the root to fold. `.gg-mon.is-loose` still declares
   *     `transform: none; animation: none` in hud.css, which is what keeps that
   *     true if a keyframe is ever added to the root.
   *   · "the z-lift is a re-append, and a re-append RELEASES pointer capture."
   *     There is no re-append here and there must never be one: the monitor
   *     lifts with a z-index (hud.css), it stays the child of the host it was
   *     mounted into, and unmount() is therefore unchanged. So there is no stale
   *     `lostpointercapture` to swallow — the handler below treats every one of
   *     them as a plain cancel, which is only correct BECAUSE we never re-parent.
   *
   * AND IT NEVER STEALS THE DROP. Firing an item is a drag that STARTS on an
   * arsenal sticker: ui/arsenal.js takes pointer capture on the tile, so no
   * pointerdown ever reaches the monitor during one, and the drop is resolved
   * against getBoundingClientRect() at pointerUP — a LIVE rect, so it follows
   * the monitor wherever it has been dragged. The armed-tap path is a
   * CAPTURE-phase listener on `document`, which runs before anything here can
   * see the event; this handler deliberately does not preventDefault or
   * stopPropagation on pointerdown, so that path is untouched too.
   * ====================================================================== */

  /** The element we were mounted into — the placeholder, once we leave it. */
  const dockHost = (root.parentNode || host);
  /** {x, y, scale} while detached; null while docked. The ONE bit of state. */
  let loose = null;
  /** The ONE live press, if any. */
  let grab = null;
  /** Sub-slop taps: two inside MON_RESET_TAP_MS re-dock. */
  let lastTapAt = -Infinity;

  const winRef = (typeof window !== 'undefined' && window) ? window : null;
  const vpW = () => { const v = winRef ? Number(winRef.innerWidth) : 0; return v > 0 ? v : 1280; };
  const vpH = () => { const v = winRef ? Number(winRef.innerHeight) : 0; return v > 0 ? v : 720; };
  const ptX = (e) => { const v = Number(e && e.clientX); return isFinite(v) ? v : 0; };
  const ptY = (e) => { const v = Number(e && e.clientY); return isFinite(v) ? v : 0; };

  function prefNum(key, fallback) {
    try {
      if (prefs && typeof prefs.get === 'function') {
        const v = Number(prefs.get(key));
        if (isFinite(v)) return v;
      }
    } catch (_e) { /* no store, no memory */ }
    return fallback;
  }
  function prefPut(key, value) {
    try { if (prefs && typeof prefs.set === 'function') prefs.set(key, value); }
    catch (_e) { /* no store, no memory */ }
  }

  /** The one place the detached monitor's geometry is written. */
  function paintLoose() {
    if (!loose || !root.style) return;
    try { root.style.setProperty(MON_WIDTH_VAR, monitorWidth(loose.scale, vpW()) + 'px'); }
    catch (_e) { /* stub DOM */ }
    try { root.style.left = Math.round(loose.x) + 'px'; root.style.top = Math.round(loose.y) + 'px'; }
    catch (_e) { /* stub DOM */ }
  }

  /**
   * Re-apply the two rules to wherever it currently is. Measured LIVE rather
   * than predicted: the height is the frame's 16/9 plus five rows of text whose
   * wrapping depends on the width we just wrote, and nothing but layout knows it.
   */
  function clampLoose() {
    if (!loose) return;
    const r = rectOf(root);
    const w = r ? r.width : monitorWidth(loose.scale, vpW());
    const h = r ? r.height : 0;
    const out = clampMonitorPos(loose.x, loose.y, w, h, vpW(), vpH());
    loose.x = out.x;
    loose.y = out.y;
    paintLoose();
  }

  function persistLoose() {
    if (!loose) return;
    prefPut(MON_PREF_X, Math.round(loose.x));
    prefPut(MON_PREF_Y, Math.round(loose.y));
    prefPut(MON_PREF_SCALE, Number(clampMonitorScale(loose.scale).toFixed(3)));
  }

  /**
   * Take it off the shelf. Idempotent, and a no-op cost while docked — this is
   * the ONLY thing that ever reads the docked layout.
   * @returns {boolean} whether the monitor is detached now
   */
  function detach() {
    if (loose) return true;
    if (!root.classList) return false;
    const r = rectOf(root);
    const base = monitorBaseWidth(vpW());
    // The size it is standing at, expressed in the units the wheel speaks. Using
    // the MEASURED width rather than assuming 1.0 is what makes the detach
    // invisible: the column may be narrower than --gg-mon-w on a small desk.
    const scale = clampMonitorScale((r && r.width > 0 && base > 0) ? r.width / base : 1);
    if (r && r.height > 0 && dockHost && dockHost.style && typeof dockHost.style.setProperty === 'function') {
      // setProperty/removeProperty rather than .minHeight/.removeProperty, so the
      // write and the erase name the SAME property on every host we run on.
      try { dockHost.style.setProperty('min-height', Math.round(r.height) + 'px'); } catch (_e) { /* stub DOM */ }
    }
    cls(dockHost, MON_HOST_LOOSE_CLASS, true);
    loose = { x: r ? r.left : MON_EDGE_PAD_PX, y: r ? r.top : MON_EDGE_PAD_PX, scale };
    cls(root, MON_LOOSE_CLASS, true);
    paintLoose();
    clampLoose();
    return true;
  }

  /** Back to the column, at the column's size. The reset gesture's whole body. */
  function redock(remember) {
    if (!loose) return false;
    loose = null;
    cls(root, MON_LOOSE_CLASS, false);
    cls(root, MON_GRABBED_CLASS, false);
    cls(dockHost, MON_HOST_LOOSE_CLASS, false);
    if (root.style) {
      try {
        root.style.removeProperty(MON_WIDTH_VAR);
        root.style.removeProperty('left');
        root.style.removeProperty('top');
      } catch (_e) { /* stub DOM */ }
    }
    if (dockHost && dockHost.style) {
      try { dockHost.style.removeProperty('min-height'); } catch (_e) { /* stub DOM */ }
    }
    if (remember) {
      prefPut(MON_PREF_X, MON_DOCKED);
      prefPut(MON_PREF_Y, MON_DOCKED);
      prefPut(MON_PREF_SCALE, 1);
    }
    return true;
  }

  /* ------------------------------------------------------------- the press */

  function forgetGrab() {
    if (!grab) return;
    const g = grab;
    grab = null;
    try { clearTimeout(g.watchdog); } catch (_e) { /* gone */ }
    if (typeof root.removeEventListener === 'function') {
      try {
        root.removeEventListener('pointermove', onMonMove);
        root.removeEventListener('pointerup', onMonUp);
        root.removeEventListener('pointercancel', onMonCancel);
        root.removeEventListener('lostpointercapture', onMonCancel);
      } catch (_e) { /* stub DOM */ }
    }
  }

  function endGrab(why) {
    const g = grab;
    if (!g) return;
    forgetGrab();
    if (!g.moved) {
      // A press that never travelled. The monitor has NO click action — the one
      // thing two of them mean is "put it back".
      if (why !== 'up') return;
      const t = nowMs();
      if (t - lastTapAt <= MON_RESET_TAP_MS) {
        lastTapAt = -Infinity;
        if (redock(true)) sfx(audio, 'ui-select');
      } else {
        lastTapAt = t;
      }
      return;
    }
    cls(root, MON_GRABBED_CLASS, false);
    // Only a completed drag is worth remembering. A cancel/watchdog drop leaves
    // it exactly where the hand let go and writes nothing.
    if (why === 'up') persistLoose();
  }

  function onMonDown(e) {
    // A right press is not a drag, and swallowing it here is how a browser gets
    // talked out of its own context menu — which this element does not want to
    // be in the business of suppressing.
    if (e && e.button != null && e.button !== 0) return;
    if (grab) endGrab('restart');
    const x = ptX(e), y = ptY(e);
    grab = {
      pointerId: (e && e.pointerId != null) ? e.pointerId : null,
      x0: x, y0: y, baseX: 0, baseY: 0, moved: false, watchdog: 0,
    };
    if (typeof root.addEventListener === 'function') {
      try {
        root.addEventListener('pointermove', onMonMove);
        root.addEventListener('pointerup', onMonUp);
        root.addEventListener('pointercancel', onMonCancel);
        root.addEventListener('lostpointercapture', onMonCancel);
      } catch (_e) { /* stub DOM */ }
    }
    if (typeof setTimeout === 'function') {
      grab.watchdog = setTimeout(() => { if (grab) endGrab('watchdog'); }, MON_HOLD_WATCHDOG_MS);
    }
    try {
      if (typeof root.setPointerCapture === 'function' && e && e.pointerId != null) root.setPointerCapture(e.pointerId);
    } catch (_e) { /* capture is a nicety; the node listeners still work without it */ }
  }

  const idMatch = (g, e) => !(g.pointerId != null && e && e.pointerId != null && e.pointerId !== g.pointerId);

  function onMonMove(e) {
    const g = grab;
    if (!g || !idMatch(g, e)) return;
    const x = ptX(e), y = ptY(e);
    if (!g.moved) {
      if (Math.hypot(x - g.x0, y - g.y0) <= MON_DRAG_SLOP_PX) return;   // still a press
      if (!detach()) { forgetGrab(); return; }
      g.moved = true;
      g.baseX = loose.x;
      g.baseY = loose.y;
      cls(root, MON_GRABBED_CLASS, true);
    }
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    loose.x = g.baseX + (x - g.x0);
    loose.y = g.baseY + (y - g.y0);
    paintLoose();
    clampLoose();
  }

  function onMonUp(e) {
    const g = grab;
    if (!g || !idMatch(g, e)) return;
    endGrab('up');
  }

  /** pointercancel / lostpointercapture. See the banner: there is no re-append
   *  in this module, so a lost capture is never paperwork — it is a real drop. */
  function onMonCancel(e) {
    const g = grab;
    if (!g || !idMatch(g, e)) return;
    endGrab('cancel');
  }

  /**
   * THE WHEEL. Hovering is the whole gesture (no press), exactly as it is over a
   * floating video window, and it drives WIDTH through MON_WIDTH_VAR — never a
   * transform scale, which would fight the drag offset and resample the art.
   * The top-LEFT corner is the anchor, which is what videos.js does too: a grow
   * that also moved the thing under your cursor is two changes, not one.
   *
   * A wheel over a DOCKED monitor detaches it first. There is no other honest
   * answer — the docked width is the column's width, and resizing in place would
   * reflow the whole right-hand side of the desk.
   */
  function onMonWheel(e) {
    const dy = Number(e && e.deltaY) || 0;
    if (!dy) return;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();   // the page never scrolls
    if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    if (!detach()) return;
    const notches = Math.max(-3, Math.min(3, Math.round(dy / 100) || (dy > 0 ? 1 : -1)));
    const next = monitorScaleStep(loose.scale, -notches);                  // wheel UP grows
    if (Math.abs(next - loose.scale) < 1e-4) return;
    loose.scale = next;
    paintLoose();
    clampLoose();
    persistLoose();
  }

  led.listen(root, 'pointerdown', onMonDown);
  // passive:false or the preventDefault above is ignored and the page scrolls.
  led.listen(root, 'wheel', onMonWheel, { passive: false });
  // The viewport changed under a parked monitor: same two rules, re-applied. The
  // width is re-DERIVED from the scale rather than kept in px, so a monitor at
  // 1.5x is 1.5x of the new --gg-mon-w and not a stale pixel count.
  led.listen(winRef, 'resize', () => {
    if (!loose) return;
    paintLoose();
    clampLoose();
    persistLoose();
  });
  led.add(() => {
    forgetGrab();
    // The placeholder is on somebody else's node: it must not outlive us.
    if (dockHost && dockHost.style) { try { dockHost.style.removeProperty('min-height'); } catch (_e) { /* gone */ } }
    cls(dockHost, MON_HOST_LOOSE_CLASS, false);
  });

  /* WHERE THEY LEFT IT. Deferred one beat because a monitor mounted this frame
   * has no layout yet, and detach() has to measure the docked box to know how
   * big a hole to leave in the column. A stored x/y of MON_DOCKED (the default)
   * means "never dragged" and nothing happens at all. */
  laterOnce(16, () => {
    const x = prefNum(MON_PREF_X, MON_DOCKED);
    const y = prefNum(MON_PREF_Y, MON_DOCKED);
    if (!(x >= 0) || !(y >= 0)) return;
    if (!detach()) return;
    loose.scale = clampMonitorScale(prefNum(MON_PREF_SCALE, 1));
    loose.x = x;
    loose.y = y;
    paintLoose();
    clampLoose();
  });

  return {
    root,
    dropTarget: frame,
    /** The projection rect — exposed so a play-test driver can find the minis. */
    projection: proj,
    /** Detached from the HUD column? selftest-hud and the play-test driver pin these. */
    isLoose: () => !!loose,
    loosePlacement: () => (loose ? { x: loose.x, y: loose.y, scale: loose.scale } : null),
    /** The reset gesture's seam (double-tap calls exactly this). */
    redock: () => redock(true),
    showEmote,
    /** OUR emote, on THEIR screen. The seam a ui/hud.js hook would call. */
    markEmoteFired,
    setTargeted,
    markPayloadFired,
    /** THEIR payload, on ITS way to us: the flare + the projectile. */
    markInbound,
    markReceipt,
    unmount() {
      for (const id of Array.from(windows.keys())) closeWindow(id);
      led.run();
      try { root.remove(); } catch (_e) { /* already gone */ }
    },
  };
}
