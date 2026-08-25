/* ============================================================================
 * THE SCENE CHASSIS. A point-and-click ROOM, generalised - the annex lab's
 * room grammar lifted out of `annex/lab.js` and handed to anybody who has a
 * painted set and something for the player to touch. The Records Office is the
 * first tenant; nothing in here knows that, or knows what a record is.
 *
 * WHAT IT OWNS
 *  - the 1376x768 stage and the transform that fits it to any window (lab.js's
 *    fitStage, room.js's fit, the same maths in one place);
 *  - the SLIDES: one painted view at a time, hotspot <button>s authored in
 *    stage pixels riding the scale for free, and the zoom that walks between
 *    them (origin at the rect you clicked, so the close-up grows out of the
 *    thing you touched rather than blooming out of the middle of the screen);
 *  - the PATCHES: small overlaid images that appear when a flag flips, which
 *    is how a painted set changes state without a second full plate;
 *  - the PROPS: a consumer's OWN element pinned to an authored rect inside one
 *    view (`mountInView`), re-seated every time that slide is rebuilt. A patch
 *    is a picture this file loads; a prop is live furniture somebody else owns
 *    (the paper on a corkboard, the spread of a book) and this file only ever
 *    knows where it hangs;
 *  - ONE overlay at a time, mounted at the apron line and taken off by
 *    REMOVAL (trap 27: `[hidden]` loses to any author `display:`);
 *  - the MIDWAY APRON with its BACK slab, on <body> at z55 the way room.js
 *    mounts it (root is its own stacking context at z10, so nothing inside it
 *    can ever rise above EMI at z50) - AND ITS HIDING, see A ZOOM IS A ZOOM;
 *  - every timer it starts, and their cancellation in destroy().
 *
 * WHAT IT DOES NOT OWN, EVER
 *  - the store, the bridge, EMI, the games list. The scene is DUMB: it renders
 *    a table it was handed and calls back. `onAction(action, {view, rect})` is
 *    the whole verb surface - the consumer decides what a click means, and if
 *    that means "go to the corkboard" the consumer calls showView('board').
 *  - the Esc key. It binds NO key listener at all. The shell's escapeStep asks
 *    `scene.escapeStep()`, which folds inward-out and answers FALSE at the
 *    wide shot so the shell's own rung takes over (trap 29's spirit: never
 *    swallow the way out). Hotspots are <button>s, so Enter/Space are free.
 *  - audio nodes. Cues are `arcademy-sfx` CustomEvent REQUESTS on `document`
 *    (trap 18), at small levels, and only for a view change and an overlay
 *    opening or closing. A room is not a slot machine.
 *  - lexicon rows. Consumers pass `[key, fallback]` per hotspot; this file
 *    invents no key (the C# NeutralLexicon mirrors every one and OVERRIDES the
 *    js fallback, so a key minted here would render as a de-snaked stub).
 *
 * LIFTED FROM lab.js, DELIBERATELY UNCHANGED
 *  - the hotspot row shape `[x, y, w, h, action, lexKey, fallback]`;
 *  - the fixed stage + `translate(-50%,-50%) scale(s)` with origin 50% 50%
 *    (lab.css's lesson - any other origin walks the stage off-axis);
 *  - the inward-out Esc fold and its false-at-home answer;
 *  - the step-back pill on every close-up (a mouse-only player must have a way
 *    out of a close-up that is not the apron's leave-the-room slab);
 *  - assets resolved module-relative via `new URL(..., import.meta.url)` - the
 *    nine-broken-logos law. A document-relative base asks the host for
 *    `https://ccp.game/art/...` and silently gets nothing.
 *
 * WHY THE APRON IS DUPLICATED AND NOT IMPORTED. `shell/room.js` is live for
 * five class rooms; lifting its apron builder out would be a refactor of a
 * shipped surface in a wave that only adds one. So the minimal back-slab
 * markup is rebuilt here and rooms.css is linked lazily beside scene.css -
 * same classes, same skin, identical look, zero risk to the five rooms.
 *
 * DECORATION LAW. `.arc-reduced` (the shell's, on <html>) kills the zoom AND
 * the breath; `.arm-lite` (this root carries it when `lite`) kills the breath
 * only. Neither costs the room a hotspot, a tag or a button.
 *
 * THE ALIVE LAYER (W2, 2026-08-25). A painted room that does not move is a
 * photograph. `fx` is a DECLARATIVE TABLE the consumer hands us - the same
 * grammar as `hotspots` and `patches`, authored in the same stage pixels - and
 * this file turns it into one `.asc-fx` layer per view, between the plate and
 * the rects, `pointer-events:none`, riding the same scale.
 *
 *   fx: [ { kind, view, rect:[x,y,w,h] | circle:{cx,cy,r}, when?, seed?, ... } ]
 *
 * SEVEN KINDS, all generic, none of them knowing what a record is:
 *   neon    a sign's halo breath, plus a SEEDED rare stutter (one 120ms dip
 *           every 9-20s - never faster, and never a strobe).
 *   lamp    a warm radial glow over a rect, breathing .96 -> 1 over 4s.
 *   motes   a canvas of drifting dust INSIDE the rect, ~25 particles, edge
 *           masked so it is lit only where the cone is.
 *   window  a route-lamp halo pulse plus a seeded headlight band that crosses
 *           the glass about every 40s.
 *   clock   THE ONE DIEGETIC REAL-TIME ELEMENT: a disc masking the painted
 *           hands and two DOM hands reading the player's own local time.
 *   seam    a cold breath along an open door's edge and a floor pool under it.
 *   tilt    2px of mouse parallax on the painting, desktop pointers only.
 *
 * FOUR LAWS THE RUNTIME KEEPS, and every one of them is a bug somebody else
 * shipped first:
 *   1. `.arc-reduced` gets NO LAYER AT ALL. Not a paused one, not an empty one
 *      - the nodes are never created, so there is nothing to leak and nothing
 *      for the global freeze to argue with.
 *   2. `.arm-lite` keeps the CSS kinds and loses the CANVAS. A lite machine can
 *      afford four keyframes; it cannot afford a per-frame particle loop.
 *   3. EVERYTHING PAUSES when the tab is hidden or the window blurs, and comes
 *      back on return - the class pauses the keyframes, and every timer and
 *      rAF is cleared rather than left running against a page nobody is on.
 *   4. EVERY TIMER IS OWNED. A record clears its own on a view change and on
 *      destroy(); `fxStats().timers` is zero after either, and the suite says
 *      so out loud. An orphan interval in a room the player walked out of is
 *      the one bug a decoration layer is actually able to cause.
 *
 * THE APRON LINE. Every VN set was painted with its lower third held calm and
 * dark for a dialogue band, so the apron owns that floor AT THE WIDE SHOT. A
 * `wide` hotspot row that crosses it is WARNED at construction (never thrown -
 * a scene with one badly-placed rect still opens).
 *
 * A ZOOM IS A ZOOM (owner ruling 2026-08-25), and it is why the audit is now
 * WIDE-ONLY. Walking into a close-up FADES THE APRON OUT (~200ms; .arc-reduced
 * cuts it) and the walk back to `wide` brings it in again. The band is the
 * front edge of the WIDE STAGE, not a permanent floor across every plate: a
 * camera that has moved in on the cork has nothing left to say about leaving
 * the building, and a sheet of paper behind an opaque slab is a sheet of paper
 * nobody can read. So a CLOSE-UP's rects and props are free to run to the
 * bottom of the plate - below 640 is legal there and is not warned about - and
 * `.asc-back`, the step-back pill every close-up draws, is the way out while
 * the band is away. It goes `visibility:hidden` with the fade, so the slab
 * never sits in the tab order of a screen it is not on.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';
import { isMobile, orientation } from '../core/device.js';

/* THE ONE MOBILE DECISION (core/device.js), asked the way room.js asks it, and
 * LANDSCAPE ONLY for room.js's reason: portrait is behind the rotate gate, and
 * top-anchoring a 9:19.5 frame would hand the apron two thirds of the screen.
 * The apron floor and the stage anchor are computed here, the box they move is
 * styled from `html.arc-mobile[data-arc-orient="landscape"]` in scene.css, and
 * the two must not drift. The wrapper is for the DOM double, no matchMedia. */
function onPhone() {
  try { return !!isMobile() && orientation() === 'landscape'; } catch (e) { return false; }
}

/** The plane the VN art was authored on. Overridable, rarely overridden. */
const STAGE_W = 1376;
const STAGE_H = 768;

/* The apron claims the painting's bottom fifth-and-a-bit plus whatever
 * letterbox hangs under it. Held as a FRACTION so a custom stage height moves
 * the line with the art instead of leaving it stranded at 640. */
const APRON_FRACTION = 640 / 768;
/** The band never shrinks below this, even art-to-the-floor (px, real). */
const APRON_MIN = 110;
/** The phone's floor. room.js's number and room.js's reasons - the two chassis
 *  share rooms.css's slab sizing, so they must share the band it sizes off. */
const APRON_MIN_MOBILE = 72;

/** The zoom between slides. One number, mirrored in scene.css. */
const ZOOM_MS = 320;
/** A frame of grace between "in the DOM at the start pose" and "go". */
const ZOOM_ARM_MS = 20;
/** How small a close-up starts, as a fraction of the stage. Clamped so a huge
 *  rect still reads as a zoom and a tiny one does not start at a pinprick. */
const ZOOM_MIN_K = 0.14;
const ZOOM_MAX_K = 0.80;

/* ------------------------------------------------------ THE ALIVE LAYER --
 * Dials, in one place, because "how often does the sign stutter" is a taste
 * question and a taste question belongs at the top of a file, not buried in a
 * factory. Every one of them is a CEILING as much as a value: a sign that
 * dips more often than every nine seconds is a broken sign, and a broken sign
 * is a different room. */
/** The neon's rare stutter: one dip, never closer together than this. */
const FX_NEON_MIN_MS = 9000;
const FX_NEON_MAX_MS = 20000;
/** How long the dip lasts. Short enough to read as a fault, not a blink. */
const FX_NEON_DIP_MS = 120;
/** A car goes past about this often, give or take the jitter. */
const FX_SWEEP_EVERY_MS = 40000;
const FX_SWEEP_JITTER_MS = 9000;
/** ...and takes this long to cross the glass. */
const FX_SWEEP_MS = 1500;
/** The clock re-reads the wall clock this often. The minute hand carries the
 *  seconds as a FRACTION, so it is never parked on a whole minute. */
const FX_CLOCK_MS = 20000;
/** How much dust is in the light. More than this reads as snow. */
const FX_MOTES = 25;

/** A tiny xorshift, so a "random" stutter is the SAME stutter every night for
 *  the same seed - a rare event nobody can reproduce is a bug report. */
function makeRng(seed) {
  let s = (Number(seed) || 0) >>> 0;
  if (!s) s = 0x9E3779B9;
  return function () {
    s ^= (s << 13); s >>>= 0;
    s ^= (s >>> 17);
    s ^= (s << 5); s >>>= 0;
    return s / 4294967296;
  };
}
/** A stable seed for a row that did not bring one. */
function hashStr(str) {
  let h = 2166136261;
  const t = String(str);
  for (let i = 0; i < t.length; i += 1) {
    h ^= t.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

/* ------------------------------------------------------------- small tools */

function urlFor(rel, fallback) {
  try { return new URL(rel, import.meta.url).href; }
  catch (e) { return fallback; }
}

/* A real file, linked once and lazily - the corkboard's pattern, which room.js
 * takes for rooms.css. The ids are shared with room.js on purpose: a night
 * that opens a class room and then the Records Office pays for rooms.css once. */
function ensureSheet(doc, id, rel, fallback, log) {
  try {
    if (!doc || typeof doc.createElement !== 'function') return null;
    const had = typeof doc.getElementById === 'function' ? doc.getElementById(id) : null;
    if (had) return had;
    const link = doc.createElement('link');
    link.id = id;
    link.rel = 'stylesheet';
    link.href = urlFor(rel, fallback);
    const host = doc.head || doc.documentElement || doc.body;
    if (host) host.appendChild(link);
    return link;
  } catch (e) { if (log) log('scene sheet failed: ' + rel); return null; }
}

/** The shell's `<html class="arc-reduced">`, read defensively (the node DOM
 *  double has no documentElement). The `reduced` option wins when passed. */
function htmlReduced() {
  try {
    const de = typeof document !== 'undefined' ? document.documentElement : null;
    return !!(de && de.classList && de.classList.contains('arc-reduced'));
  } catch (e) { return false; }
}

/**
 * createScene(opts) -> the handle documented in the header.
 *
 * @param {object} opts
 *  mount     - element the shell hands us; we append our root to it.
 *  stageW/H  - the authored plane (1376x768).
 *  views     - `{ name: { art, hotspots:[row...] } }`. `wide` is REQUIRED and
 *              is home. A row is `[x, y, w, h, action, lexKey, fallback, opt?]`
 *              and `opt` is `{ main, quiet, tag, when }`:
 *                main  - wears the breath (`.arm-hot.arm-main`). One per view.
 *                quiet - the exit's dark rim; dark until found, honest on hover.
 *                tag   - a LITERAL label, used instead of t(lexKey, fallback).
 *                when  - a flag name; the row is absent until setFlag turns it
 *                        on. A leading `!` inverts it.
 *  patches   - `[{ view, art, rect:[x,y,w,h], when }]`. A small image laid over
 *              the view's plate while the flag is on.
 *  fx        - THE ALIVE LAYER (see the header). `[{ kind, view, rect | circle,
 *              when?, seed?, ...opts }]` in stage px. Every row is decoration:
 *              a bad one costs a breath and never a rect.
 *  apron     - `{ back:fn, label?, lexKey? }` or null. The midway band with the
 *              BACK slab only; no hero, because a facility room starts nothing.
 *  onAction  - (action, {view, rect}) for every hotspot press.
 *  onBack    - the leave-the-room verb (the apron slab, unless it carries its
 *              own `back`). Esc at the wide shot does NOT call it - it answers
 *              false and the shell's rung decides.
 *  reduced   - force the reduced-motion cut (the html class is also honoured).
 *  lite      - performance rung; kills the breath the way reduced does.
 *  log       - the shell's say.
 *  sfx       - (name, level, extra) override; default requests on `document`.
 *  artBase   - url base for plate files. Default `../art/vn/`, module-relative.
 *  t         - lexicon lookup override; default is core/lexicon.js's own.
 *  label     - optional aria-label for the room region.
 */
export function createScene(opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;

  const t = typeof o.t === 'function' ? o.t : lexT;
  const log = typeof o.log === 'function' ? o.log : function () {};
  const stageW = Number(o.stageW) > 0 ? Number(o.stageW) : STAGE_W;
  const stageH = Number(o.stageH) > 0 ? Number(o.stageH) : STAGE_H;
  const apronStageTop = stageH * APRON_FRACTION;
  const lite = !!o.lite;
  const views = o.views || {};
  const patchRows = Array.isArray(o.patches) ? o.patches : [];
  const artBase = typeof o.artBase === 'string' && o.artBase
    ? o.artBase
    : urlFor('../art/vn/', 'art/vn/');

  if (!views.wide) { log('scene: no `wide` view - it is the home shot and it is required'); return null; }

  let dead = false;
  let viewName = null;
  let slide = null;                 // the live `.asc-slide`
  let overlay = null;               // { name, layer, token }
  let overlayToken = 0;
  let lastRect = null;              // the rect the player last pressed
  const origins = Object.create(null); // view -> [cx, cy, k], for the walk back
  const flags = Object.create(null);
  const live = [];                  // flagged nodes on the CURRENT slide
  const props = [];                 // { view, node } - consumer furniture, all views
  const timers = [];

  /* --------------------------------------------------------------- tools */

  function later(fn, ms) {
    const id = setTimeout(function () { if (!dead) { try { fn(); } catch (e) { log('scene timer threw: ' + ((e && e.message) || e)); } } }, ms);
    timers.push(id);
    return id;
  }
  function el(tag, cls, text) {
    const n = doc.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  }
  function reducedNow() { return !!o.reduced || htmlReduced(); }

  /* THE ONE AUDIO DOOR (trap 18): a REQUEST on `document`, never a node. */
  const sfx = typeof o.sfx === 'function' ? o.sfx : function (name, level, extra) {
    try {
      if (typeof doc.dispatchEvent !== 'function') return;
      const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
      if (!Ctor) return;
      doc.dispatchEvent(new Ctor('arcademy-sfx', {
        detail: Object.assign({ name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' }, extra || {}),
      }));
    } catch (e) { /* a cue must never be the thing that throws */ }
  };

  /* --------------------------------------------------------------- flags */

  /** `when` matching. A bare name reads the flag; a leading `!` inverts it;
   *  no `when` at all is always on. */
  function whenOn(when) {
    if (!when) return true;
    const s = String(when);
    if (s.charAt(0) === '!') return !flags[s.slice(1)];
    return !!flags[s];
  }
  function applyFlags() {
    for (let i = 0; i < live.length; i += 1) {
      const row = live[i];
      const want = whenOn(row.when);
      const has = !!row.node.parentNode;
      if (want && !has) row.host.appendChild(row.node);
      else if (!want && has) { try { row.node.remove(); } catch (e) { /* noop */ } }
    }
  }
  function setFlag(name, on) {
    if (dead || !name) return;
    const next = !!on;
    if (flags[name] === next) return;
    flags[name] = next;
    applyFlags();
  }

  /* ------------------------------------------------------------ the room */

  const root = el('div', 'asc-root' + (lite ? ' asc-lite arm-lite' : ''));
  root.setAttribute('role', 'region');
  if (o.label) root.setAttribute('aria-label', String(o.label));
  const stage = el('div', 'asc-stage');
  root.appendChild(stage);

  const sheetScene = ensureSheet(doc, 'asc-styles', './scene.css', 'shell/scene.css', log);
  const sheetRooms = ensureSheet(doc, 'arm-styles', './rooms.css', 'shell/rooms.css', log);

  /* ---------------------------------------------------------- the apron */
  /* The MIDWAY APRON, rebuilt minimal: room.js's markup and rooms.css's
   * classes, the BACK slab alone. Mounted on <body>, not inside root - root is
   * its own stacking context at z10, so a child could never rise above EMI
   * (#arc-emi z50) whatever its z-index. As a body-level sibling at z55 the
   * band is the front edge of the stage. destroy() owns its removal. */
  let bar = null;
  const apron = o.apron && typeof o.apron === 'object' ? o.apron : null;
  function goBack() {
    if (dead) return;
    const fn = (apron && typeof apron.back === 'function') ? apron.back : o.onBack;
    if (typeof fn !== 'function') return;
    try { fn(); } catch (e) { log('scene back threw: ' + ((e && e.message) || e)); }
  }
  if (apron) {
    bar = el('div', 'arm-bar asc-bar' + (lite ? ' arm-lite' : ''));
    const left = el('div', 'arm-bar-side arm-bar-left');
    const back = el('button', 'arm-bar-ghost arm-slab-back');
    back.type = 'button';
    back.appendChild(el('span', 'arm-glyph arm-glyph-arrow'));
    const backText = apron.label != null
      ? String(apron.label)
      : t(apron.lexKey || 'rake_back_to_campus', 'Back to campus');
    back.appendChild(el('span', 'arm-slab-text', backText));
    back.setAttribute('aria-label', backText);
    back.addEventListener('click', goBack);
    left.appendChild(back);
    bar.appendChild(left);
    /* The empty right side is not decoration: the two 1fr sides are what hold
     * the band's rhythm when a language spends more words on the slab. */
    bar.appendChild(el('div', 'arm-bar-side arm-bar-right'));
    if (doc.body) doc.body.appendChild(bar); else root.appendChild(bar);
  }

  /**
   * THE BAND STEPS OFF FOR A CLOSE-UP. One class, and the sheet owns both the
   * ~200ms fade and the `.arc-reduced` cut - the JS is one path, exactly the
   * way the zoom is (`.asc-zoom` + `transition:none`). `visibility` rides the
   * same rule so the slab leaves the tab order rather than lurking invisibly
   * under a plate the player is reading.
   *
   * It is a LEVEL keyed on the view, never a toggle: every showView writes it,
   * so an interrupted walk (a second press mid-zoom) can never strand the band
   * off-screen at the wide shot.
   */
  function apronVisible(show) {
    if (!bar) return;
    try {
      if (bar.classList && typeof bar.classList.toggle === 'function') { bar.classList.toggle('asc-bar-away', !show); return; }
    } catch (e) { /* noop */ }
    /* the node double has no toggle: fall back to the string, idempotently */
    try {
      const has = hasCls(bar, 'asc-bar-away');
      if (!show && !has && bar.classList && bar.classList.add) bar.classList.add('asc-bar-away');
      else if (show && has && bar.classList && bar.classList.remove) bar.classList.remove('asc-bar-away');
    } catch (e) { /* noop */ }
  }

  /* ---------------------------------------------------- the step-back pill */

  /* THE WAY OUT OF A CLOSE-UP, and there is exactly ONE of it. lab.css's
   * `.al-back`, promoted: the apron's slab leaves the room on the way into a
   * close-up (A ZOOM IS A ZOOM), Esc is not a thumb, so a close-up draws a
   * pill of its own.
   *
   * IT HANGS OFF THE ROOT, NEVER OFF THE SLIDE. A slide lives inside
   * `.asc-stage`, which is the 1376x768 plane scaled to fit - so a pill in
   * there is authored in STAGE pixels and every phone rule written for it is
   * multiplied by the fit (~0.5 on a 844x390 window: a 44px thumb target came
   * out 22px tall with 6px type, which is the owner's "there is no way back
   * from the book", 2026-08-25). The apron band has been a screen-pixel
   * sibling since it was written; the pill is the same kind of thing.
   *
   * It comes and goes by REMOVAL (trap 27), never by `hidden`, and it is a
   * LEVEL keyed on the view exactly the way the apron is - every showView
   * writes it, so an interrupted walk can never strand it over the wide shot.
   */
  const backPill = el('button', 'asc-back', '‹ ' + t('back', 'Back'));
  backPill.type = 'button';
  backPill.setAttribute('aria-label', t('back', 'Back'));
  /* The pill runs the SAME fold Esc runs: a panel opened over a close-up is
   * the thing one press ago, so it closes first. At the wide shot escapeStep
   * answers false and does nothing - and the pill is not up there anyway. */
  backPill.addEventListener('click', function () { escapeStep(); });
  let backUp = false;
  function backVisible(show) {
    const want = !!show;
    if (want === backUp) return;
    backUp = want;
    try {
      if (want) root.appendChild(backPill);
      else backPill.remove();
    } catch (e) { /* a way out must never be the thing that throws */ }
  }

  /* ------------------------------------------------------------ hotspots */

  function placeRect(node, rect) {
    node.style.left = rect[0] + 'px';
    node.style.top = rect[1] + 'px';
    node.style.width = rect[2] + 'px';
    node.style.height = rect[3] + 'px';
  }

  /** WIDE rows are authored above the apron line or the band sits on them. A
   *  crossing row is a WARNING, never a throw: one bad rect must not cost the
   *  player the whole room. Checked once, at construction.
   *
   *  ONLY `wide` IS AUDITED (the zoom-is-a-zoom ruling in the header): the band
   *  fades out on the way into any close-up, so a close-up's rects own the
   *  whole plate and a row below 640 there is CORRECT, not a mistake. Every
   *  view is still checked for a malformed row - that is a shape error and has
   *  nothing to do with where the carpet is. */
  function auditRects() {
    Object.keys(views).forEach(function (name) {
      const rows = (views[name] && views[name].hotspots) || [];
      for (let i = 0; i < rows.length; i += 1) {
        const r = rows[i];
        if (!Array.isArray(r) || r.length < 4) { log('scene: malformed hotspot row in view ' + name + ' at index ' + i); continue; }
        if (name !== 'wide') continue;
        const bottom = Number(r[1]) + Number(r[3]);
        if (bottom > apronStageTop) {
          log('scene: hotspot rect crosses the apron line (view=' + name + ' action=' + r[4]
            + ' bottom=' + bottom + ' > ' + apronStageTop + ') - the apron owns the floor');
        }
      }
    });
  }

  function hotspot(row, name) {
    const x = row[0]; const y = row[1]; const w = row[2]; const h = row[3];
    const action = row[4]; const key = row[5]; const fb = row[6];
    const opt = (row[7] && typeof row[7] === 'object') ? row[7] : {};
    /* The still-rim default is the pool ladder's cyan; `main` breathes and
     * `quiet` is the painted exit's dark rim. rooms.css owns all three so a
     * scene and a class room cannot drift apart. */
    const skin = opt.main ? ' arm-main' : (opt.quiet ? ' arm-exit' : ' arm-swim');
    const b = el('button', 'arm-hot asc-hot' + skin);
    b.type = 'button';
    placeRect(b, row);
    const label = opt.tag != null ? String(opt.tag) : String(t(key, fb));
    b.setAttribute('aria-label', label);
    b.appendChild(el('span', 'arm-hot-tag', label));
    b.addEventListener('click', function () {
      if (dead) return;
      lastRect = [x, y, w, h];
      if (typeof o.onAction !== 'function') return;
      try { o.onAction(action, { view: name, rect: [x, y, w, h] }); }
      catch (e) { log('scene action threw: ' + ((e && e.message) || e)); }
    });
    return b;
  }

  /* ------------------------------------------------------- THE ALIVE LAYER */
  /* One `.asc-fx` per slide, built with it and thrown away with it. The table
   * is the CONSUMER's (see the header); everything below is the runtime that
   * reads it, and it knows the word "neon" but not the word "Records". */

  const fxRows = Array.isArray(o.fx) ? o.fx : [];
  /** Live records for the slides currently in the DOM. */
  const fxRecords = [];
  /** Held while the tab is hidden or the window is blurred. */
  let fxHeld = false;
  /** EVERY pending timeout and rAF, counted. The suite asserts it is 0 after a
   *  view change and after destroy(), which is the only assertion that can
   *  catch an orphan. */
  let fxTimerCount = 0;

  /** Arm a record's ONE timer. A record never holds two: every kind here is a
   *  chain (wait -> do -> wait again), so a second handle would be a leak. */
  function fxSet(rec, fn, ms) {
    fxClearTimer(rec);
    rec.timer = setTimeout(function () {
      rec.timer = 0;
      fxTimerCount -= 1;
      if (dead || fxHeld) return;
      try { fn(); } catch (e) { log('scene fx threw: ' + ((e && e.message) || e)); }
    }, ms);
    fxTimerCount += 1;
  }
  function fxClearTimer(rec) {
    if (rec.timer) { try { clearTimeout(rec.timer); } catch (e) { /* noop */ } rec.timer = 0; fxTimerCount -= 1; }
  }
  function fxClearRaf(rec) {
    if (rec.raf) {
      try { if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(rec.raf); }
      catch (e) { /* noop */ }
      rec.raf = 0;
      fxTimerCount -= 1;
    }
  }
  function fxClear(rec) { fxClearTimer(rec); fxClearRaf(rec); }

  /* ---- the kinds. Each one is a small factory returning a RECORD:
   *   { kind, node, timer, raf, pause?, resume?, stop? }
   * `node` may be null (tilt paints nothing of its own). Anything with state
   * beyond a keyframe implements pause/resume, and law 3 does the rest. */

  /** THE SIGN. A halo that breathes, and once in a long while a dip. */
  function fxNeon(row, rect, rng) {
    const node = el('div', 'asc-fx-neon');
    placeRect(node, rect);
    const rec = { kind: 'neon', node: node, timer: 0, raf: 0 };
    function arm() {
      const wait = FX_NEON_MIN_MS + rng() * (FX_NEON_MAX_MS - FX_NEON_MIN_MS);
      fxSet(rec, function () {
        try { if (node.classList && node.classList.add) node.classList.add('is-dip'); } catch (e) { /* noop */ }
        fxSet(rec, function () {
          try { if (node.classList && node.classList.remove) node.classList.remove('is-dip'); } catch (e) { /* noop */ }
          arm();
        }, FX_NEON_DIP_MS);
      }, wait);
    }
    rec.pause = function () {
      fxClear(rec);
      try { if (node.classList && node.classList.remove) node.classList.remove('is-dip'); } catch (e) { /* noop */ }
    };
    rec.resume = arm;
    rec.stop = rec.pause;
    arm();
    return rec;
  }

  /** THE LAMP. Pure keyframes - a warm pool that never quite settles. */
  function fxLamp(row, rect) {
    const node = el('div', 'asc-fx-lamp');
    placeRect(node, rect);
    return { kind: 'lamp', node: node, timer: 0, raf: 0 };
  }

  /** THE WINDOW. A route lamp pulsing outside, and a car about once a minute. */
  function fxWindow(row, rect, rng) {
    const node = el('div', 'asc-fx-window');
    placeRect(node, rect);
    node.appendChild(el('i', 'asc-fx-win-halo'));
    const beam = el('i', 'asc-fx-win-beam');
    node.appendChild(beam);
    const rec = { kind: 'window', node: node, timer: 0, raf: 0 };
    function arm() {
      const wait = (FX_SWEEP_EVERY_MS - FX_SWEEP_JITTER_MS) + rng() * (FX_SWEEP_JITTER_MS * 2);
      fxSet(rec, function () {
        try { if (beam.classList && beam.classList.add) beam.classList.add('is-pass'); } catch (e) { /* noop */ }
        fxSet(rec, function () {
          try { if (beam.classList && beam.classList.remove) beam.classList.remove('is-pass'); } catch (e) { /* noop */ }
          arm();
        }, FX_SWEEP_MS);
      }, wait);
    }
    rec.pause = function () {
      fxClear(rec);
      try { if (beam.classList && beam.classList.remove) beam.classList.remove('is-pass'); } catch (e) { /* noop */ }
    };
    rec.resume = arm;
    rec.stop = rec.pause;
    arm();
    return rec;
  }

  /**
   * THE CLOCK, and it is the only thing in any room that knows what time it is.
   *
   * The plate painted a face and a pair of hands stopped at eleven. A cream
   * disc covers the painted hands ONLY - the numerals are the painting's and
   * stay painted, which is why the disc is a fraction of the bezel and not the
   * whole face - and two DOM hands over it read the player's own wall clock.
   *
   * `now` is injectable so a suite can hand it a frozen Date; the minute hand
   * carries the seconds as a fraction so it is never parked on a whole minute,
   * and the sheet glides it between the twenty-second reads.
   */
  function fxClock(row, circle) {
    const cx = Number(circle.cx); const cy = Number(circle.cy); const r = Number(circle.r);
    if (!(r > 0)) return null;
    const node = el('div', 'asc-fx-clock');
    placeRect(node, [cx - r, cy - r, r * 2, r * 2]);

    /* The mask. Big enough to bury the painted hands, small enough to leave
     * every numeral alone (measured off the plate: the hands reach r16 and the
     * numerals start at r21). */
    const faceR = Number(row.faceR) > 0 ? Number(row.faceR) : Math.round(r * 0.45);
    const disc = el('i', 'asc-fx-clock-face');
    disc.style.left = (r - faceR) + 'px';
    disc.style.top = (r - faceR) + 'px';
    disc.style.width = (faceR * 2) + 'px';
    disc.style.height = (faceR * 2) + 'px';
    if (row.face) disc.style.background = String(row.face);
    node.appendChild(disc);

    const hourLen = Number(row.hourLen) > 0 ? Number(row.hourLen) : Math.round(r * 0.27);
    const minLen = Number(row.minLen) > 0 ? Number(row.minLen) : Math.round(r * 0.39);
    function hand(cls, len, w) {
      const n = el('i', 'asc-fx-clock-hand ' + cls);
      n.style.left = (r - w / 2) + 'px';
      n.style.top = (r - len) + 'px';
      n.style.width = w + 'px';
      n.style.height = len + 'px';
      if (row.ink) n.style.background = String(row.ink);
      return n;
    }
    const hourEl = hand('asc-fx-hour', hourLen, 3);
    const minEl = hand('asc-fx-min', minLen, 2);
    node.appendChild(hourEl);
    node.appendChild(minEl);
    const hub = el('i', 'asc-fx-clock-hub');
    hub.style.left = (r - 3) + 'px';
    hub.style.top = (r - 3) + 'px';
    if (row.ink) hub.style.background = String(row.ink);
    node.appendChild(hub);

    const nowFn = typeof row.now === 'function' ? row.now : function () { return new Date(); };
    const rec = { kind: 'clock', node: node, timer: 0, raf: 0 };
    let ang = { hour: 0, minute: 0 };
    function setHands() {
      let d;
      try { d = nowFn(); } catch (e) { d = new Date(); }
      if (!d || typeof d.getHours !== 'function') d = new Date();
      const h = d.getHours() % 12; const m = d.getMinutes(); const sec = d.getSeconds();
      ang = { minute: (m + sec / 60) * 6, hour: (h + m / 60 + sec / 3600) * 30 };
      try {
        hourEl.style.transform = 'rotate(' + ang.hour.toFixed(2) + 'deg)';
        minEl.style.transform = 'rotate(' + ang.minute.toFixed(2) + 'deg)';
      } catch (e) { /* noop */ }
    }
    function tick() { setHands(); fxSet(rec, tick, FX_CLOCK_MS); }
    rec.pause = function () { fxClear(rec); };
    rec.resume = tick;               // a hidden tab comes back to the right time
    rec.stop = function () { fxClear(rec); };
    /** Test seam: the two angles in degrees, clockwise from twelve. */
    rec.hands = function () { return { hour: ang.hour, minute: ang.minute }; };
    tick();
    return rec;
  }

  /** THE DOOR, ajar. Two pieces of one idea: a cold line down the open edge
   *  and the pool it throws on the floor. Both pure keyframes; the ROOM decides
   *  whether they exist at all, through `when`. */
  function fxSeam(row) {
    const node = el('div', 'asc-fx-seam');
    if (Array.isArray(row.edge) && row.edge.length >= 4) {
      const edge = el('i', 'asc-fx-seam-edge');
      placeRect(edge, row.edge);
      node.appendChild(edge);
    }
    if (Array.isArray(row.pool) && row.pool.length >= 4) {
      const pool = el('i', 'asc-fx-seam-pool');
      placeRect(pool, row.pool);
      node.appendChild(pool);
    }
    return { kind: 'seam', node: node, timer: 0, raf: 0 };
  }

  /**
   * THE DUST. The one canvas in the chassis, and the reason `.arm-lite` is a
   * separate rung from `.arc-reduced`: a lite machine keeps every keyframe
   * above and loses this. The canvas IS the rect (1 canvas px = 1 stage px), so
   * the mask is free at the edges and exact everywhere else.
   */
  function fxMotes(row, rect, rng) {
    if (lite) return null;
    let node = null;
    try { node = doc.createElement('canvas'); } catch (e) { return null; }
    node.className = 'asc-fx-motes';
    try { node.width = rect[2]; node.height = rect[3]; } catch (e) { /* noop */ }
    placeRect(node, rect);
    const rec = { kind: 'motes', node: node, timer: 0, raf: 0 };
    let ctx = null;
    try { ctx = (typeof node.getContext === 'function') ? node.getContext('2d') : null; } catch (e) { ctx = null; }
    if (!ctx || typeof requestAnimationFrame !== 'function') {
      /* No 2d and no frames (the node double): the element still exists so the
       * layer's shape is the same everywhere, and it simply never paints. */
      rec.stop = function () { fxClear(rec); };
      return rec;
    }
    const W = rect[2]; const H = rect[3];
    const n = Number(row.count) > 0 ? Number(row.count) : FX_MOTES;
    const tint = row.tint ? String(row.tint) : '255, 236, 190';
    const ps = [];
    for (let i = 0; i < n; i += 1) {
      ps.push({
        x: rng() * W, y: rng() * H,
        r: 0.6 + rng() * 1.5,
        v: 3 + rng() * 8,            // px a second, upward - dust, not smoke
        a: 0.18 + rng() * 0.42,
        w: rng() * Math.PI * 2,
        s: 0.3 + rng() * 0.7,
      });
    }
    let last = 0;
    function frame(ts) {
      rec.raf = 0;
      fxTimerCount -= 1;
      if (dead || fxHeld) return;
      const dt = last ? Math.min(0.064, (ts - last) / 1000) : 0.016;
      last = ts;
      ctx.clearRect(0, 0, W, H);
      for (let i = 0; i < ps.length; i += 1) {
        const p = ps[i];
        p.y -= p.v * dt;
        p.w += p.s * dt;
        p.x += Math.sin(p.w) * 7 * dt;
        if (p.y < -2) { p.y = H + 2; p.x = rng() * W; }
        if (p.x < -2) p.x = W + 2;
        if (p.x > W + 2) p.x = -2;
        /* THE MASK. Lit only inside the cone: a mote fades out as it nears any
         * edge of the rect, so the dust has no border and the rect never shows. */
        const fx = Math.min(1, Math.min(p.x, W - p.x) / (W * 0.28));
        const fy = Math.min(1, Math.min(p.y, H - p.y) / (H * 0.32));
        const a = p.a * Math.max(0, fx) * Math.max(0, fy);
        if (a <= 0.005) continue;
        ctx.beginPath();
        ctx.fillStyle = 'rgba(' + tint + ', ' + a.toFixed(3) + ')';
        ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
        ctx.fill();
      }
      schedule();
    }
    function schedule() {
      if (dead || fxHeld || rec.raf) return;
      try { rec.raf = requestAnimationFrame(frame); fxTimerCount += 1; } catch (e) { rec.raf = 0; }
    }
    rec.pause = function () { fxClear(rec); try { ctx.clearRect(0, 0, W, H); } catch (e) { /* noop */ } };
    rec.resume = function () { last = 0; schedule(); };
    rec.stop = function () { fxClear(rec); };
    schedule();
    return rec;
  }

  /**
   * THE TILT. Two pixels of parallax under the mouse, and it moves the PAINTING
   * (the plate, the patches, the fx layer) rather than the slide: the slide's
   * transform belongs to the zoom, and a decoration that fights the camera is a
   * decoration that breaks the camera. The rects stay exactly where they were
   * measured, which at two pixels nobody can see and every audit can.
   *
   * Desktop pointers only, by both doors - `isMobile()` (the school's one
   * mobile decision) and `(pointer: fine)`.
   */
  function fxTilt(row, slideNode) {
    try { if (isMobile()) return null; } catch (e) { /* noop */ }
    try {
      if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
        const m = window.matchMedia('(pointer: fine)');
        if (m && m.matches === false) return null;
      }
    } catch (e) { /* noop */ }
    if (typeof window === 'undefined' || !window.addEventListener) return null;
    const amp = Number(row.amp) > 0 ? Number(row.amp) : 2;
    const rec = { kind: 'tilt', node: null, timer: 0, raf: 0 };
    function onMove(ev) {
      if (dead || fxHeld || !ev) return;
      const w = window.innerWidth || 1;
      const h = window.innerHeight || 1;
      /* WHOLE PIXELS. The plate is `image-rendering:pixelated` on a scaled
       * stage: a fractional translate resamples the pixel art into mush, and
       * two pixels of parallax is not worth a soft painting. */
      const dx = Math.round((((ev.clientX || 0) / w) - 0.5) * -2 * amp);
      const dy = Math.round((((ev.clientY || 0) / h) - 0.5) * -2 * amp);
      try {
        slideNode.style.setProperty('--asc-tilt-x', dx + 'px');
        slideNode.style.setProperty('--asc-tilt-y', dy + 'px');
      } catch (e) { /* noop */ }
    }
    window.addEventListener('pointermove', onMove);
    rec.pause = function () { /* a mouse that is not over the window sends nothing */ };
    rec.resume = rec.pause;
    rec.stop = function () {
      try { window.removeEventListener('pointermove', onMove); } catch (e) { /* noop */ }
    };
    return rec;
  }

  /* ---- the dispatch --------------------------------------------------- */

  function makeFx(row, slideNode) {
    const kind = String(row.kind || '');
    const rng = makeRng(row.seed != null ? row.seed : hashStr(kind + ':' + String(row.view)));
    const rect = (Array.isArray(row.rect) && row.rect.length >= 4) ? row.rect : null;
    try {
      if (kind === 'neon') return rect ? fxNeon(row, rect, rng) : null;
      if (kind === 'lamp') return rect ? fxLamp(row, rect) : null;
      if (kind === 'motes') return rect ? fxMotes(row, rect, rng) : null;
      if (kind === 'window') return rect ? fxWindow(row, rect, rng) : null;
      if (kind === 'clock') return (row.circle && row.circle.r) ? fxClock(row, row.circle) : null;
      if (kind === 'seam') return fxSeam(row);
      if (kind === 'tilt') return fxTilt(row, slideNode);
    } catch (e) { log('scene fx ' + kind + ' failed: ' + ((e && e.message) || e)); return null; }
    log('scene: unknown fx kind ' + kind);
    return null;
  }

  /**
   * buildFx(name, slideNode) -> the `.asc-fx` layer for one slide, or NULL.
   *
   * Null under `.arc-reduced` is law 1 and it is deliberate: no layer, no
   * nodes, nothing paused, nothing to argue with the global freeze about.
   */
  function buildFx(name, slideNode) {
    if (!fxRows.length || reducedNow()) return null;
    const layer = el('div', 'asc-fx');
    try { layer.setAttribute('aria-hidden', 'true'); } catch (e) { /* noop */ }
    for (let i = 0; i < fxRows.length; i += 1) {
      const row = fxRows[i];
      if (!row || row.view !== name) continue;
      const rec = makeFx(row, slideNode);
      if (!rec) continue;
      rec.host = slideNode;
      fxRecords.push(rec);
      if (!rec.node) continue;
      /* `when` is the hotspots' own gate, read the same way and joining the
       * same registry - so setFlag('ajar', true) lights the seam mid-visit. */
      if (row.when) live.push({ node: rec.node, when: row.when, host: layer, owner: slideNode });
      if (whenOn(row.when)) layer.appendChild(rec.node);
    }
    return layer;
  }

  /** Stop and forget every record belonging to one slide (never call with a
   *  falsy host unless you mean ALL of them - destroy() does). */
  function dropFx(host) {
    for (let i = fxRecords.length - 1; i >= 0; i -= 1) {
      const rec = fxRecords[i];
      if (host && rec.host !== host) continue;
      fxRecords.splice(i, 1);
      try { if (rec.stop) rec.stop(); } catch (e) { /* noop */ }
      fxClear(rec);
      try { if (rec.node) rec.node.remove(); } catch (e) { /* noop */ }
    }
  }

  /* ---- law 3: a hidden tab, a blurred window --------------------------- */

  function fxSetHold(on) {
    const next = !!on;
    if (fxHeld === next) return;
    fxHeld = next;
    /* add/remove, never `toggle` - the node double's classList has no toggle
     * (trap 49's family: a feature test passes in exactly the world that does
     * not matter) and a suite has to be able to see the hold land. */
    try {
      if (next) { if (root.classList && root.classList.add) root.classList.add('asc-fx-hold'); }
      else if (root.classList && root.classList.remove) root.classList.remove('asc-fx-hold');
    } catch (e) { /* noop */ }
    for (let i = 0; i < fxRecords.length; i += 1) {
      const rec = fxRecords[i];
      try {
        if (next) { if (rec.pause) rec.pause(); }
        else if (rec.resume) rec.resume();
      } catch (e) { /* noop */ }
    }
  }
  function onVis() { try { fxSetHold(doc.hidden === true); } catch (e) { /* noop */ } }
  function onBlur() { fxSetHold(true); }
  function onFocus() { fxSetHold(false); }
  if (typeof doc.addEventListener === 'function') doc.addEventListener('visibilitychange', onVis);
  if (typeof window !== 'undefined' && window.addEventListener) {
    window.addEventListener('blur', onBlur);
    window.addEventListener('focus', onFocus);
  }

  /* -------------------------------------------------------------- slides */

  function buildSlide(name) {
    const def = views[name] || {};
    const node = el('div', 'asc-slide');
    node.setAttribute('data-view', name);

    const art = doc.createElement('img');
    art.className = 'asc-art';
    art.alt = '';
    art.draggable = false;
    art.addEventListener('load', onResize);   // the fit race: refit when it decodes
    art.src = artBase + (def.art || '');
    node.appendChild(art);

    /* PATCHES first (z2 in the sheet), hotspots after (z3) - the stacking is
     * the sheet's, so a flag re-attaching a node cannot reorder the room. */
    for (let i = 0; i < patchRows.length; i += 1) {
      const p = patchRows[i];
      if (!p || p.view !== name || !Array.isArray(p.rect)) continue;
      const im = doc.createElement('img');
      im.className = 'asc-patch';
      im.alt = '';
      im.draggable = false;
      placeRect(im, p.rect);
      im.src = artBase + (p.art || '');
      if (p.when) live.push({ node: im, when: p.when, host: node });
      if (whenOn(p.when)) node.appendChild(im);
    }

    /* THE ALIVE LAYER, between the plate and everything you can touch. Null
     * under reduced motion, and the layer takes no pointer events, so the room
     * is exactly as clickable with it as without it. */
    const fxLayer = buildFx(name, node);
    if (fxLayer) node.appendChild(fxLayer);

    /* THE PROPS, between the patches and the hotspots. A prop is the CONSUMER's
     * node, so it is re-seated (appendChild MOVES it) rather than rebuilt - the
     * paper on the corkboard keeps its scroll position and the book keeps its
     * page across a camera move. Same z as a hotspot (the sheet pins both at 3)
     * and earlier in DOM order, so a rect the room lit still wins the press. */
    for (let i = 0; i < props.length; i += 1) {
      if (props[i].view !== name) continue;
      try { node.appendChild(props[i].node); } catch (e) { /* noop */ }
    }

    const rows = def.hotspots || [];
    for (let i = 0; i < rows.length; i += 1) {
      const r = rows[i];
      if (!Array.isArray(r) || r.length < 4) continue;
      const opt = (r[7] && typeof r[7] === 'object') ? r[7] : {};
      const b = hotspot(r, name);
      if (opt.when) live.push({ node: b, when: opt.when, host: node });
      if (whenOn(opt.when)) node.appendChild(b);
    }

    /* THE STEP-BACK PILL IS NOT IN HERE ANY MORE - see backVisible(). It used
     * to be a child of the slide, which put it inside the SCALED stage: on a
     * phone the fit is ~0.5, so every screen pixel the phone rules asked for
     * came out halved (a 44px thumb target measured 22px, 12px type measured
     * 6px) and the one way out of a close-up read as a smudge. It is chrome,
     * not paint - it hangs off the root now, in screen pixels. */
    return node;
  }

  /** The zoom start scale for a rect: how much of the stage it covers. */
  function kFor(rect) {
    const k = (rect && rect[2] ? rect[2] : stageW * 0.35) / stageW;
    return Math.max(ZOOM_MIN_K, Math.min(ZOOM_MAX_K, k));
  }
  function originFor(rect) {
    if (!rect) return [stageW / 2, stageH / 2, 0.35];
    return [rect[0] + rect[2] / 2, rect[1] + rect[3] / 2, kFor(rect)];
  }

  /**
   * showView(name, o2?) - the camera move.
   *
   * FORWARD (into a close-up) the new slide grows out of the rect the player
   * last pressed: transform-origin at that rect's centre, scale from its own
   * share of the stage up to 1, crossfading over the old plate. BACK is the
   * same move played in reverse - the close-up shrinks into the rect it came
   * from while home fades up underneath. `.arc-reduced` cuts, and the cut is
   * the SHEET's decision (`transition:none`), so the JS is one path.
   */
  function showView(name, o2) {
    if (dead || !views[name] || name === viewName) return;
    const opt2 = o2 || {};
    const prev = slide;
    const prevName = viewName;
    const reduced = reducedNow();
    const back = name === 'wide' || opt2.back === true;

    /* which rect the camera pivots on */
    let org;
    if (Array.isArray(opt2.origin)) org = [opt2.origin[0], opt2.origin[1], opt2.origin[2] || 0.35];
    else if (back && prevName && origins[prevName]) org = origins[prevName];
    else org = originFor(lastRect);
    if (!back) origins[name] = org;
    lastRect = null;

    /* a new slide owns the flag registry: drop the outgoing one's rows */
    for (let i = live.length - 1; i >= 0; i -= 1) {
      if (live[i].host === prev || live[i].owner === prev) live.splice(i, 1);
    }

    viewName = name;
    const next = buildSlide(name);
    next.style.zIndex = back ? '1' : '2';
    if (prev) prev.style.zIndex = back ? '2' : '1';

    if (reduced || !prev) {
      stage.appendChild(next);
      if (prev) { dropFx(prev); try { prev.remove(); } catch (e) { /* noop */ } }
      slide = next;
    } else {
      /* start pose, then a frame, then go - the reflow is read for the same
       * reason the split-flap board reads one (trap 4). */
      const originCss = org[0] + 'px ' + org[1] + 'px';
      if (back) {
        next.style.opacity = '0';
        prev.style.transformOrigin = originCss;
        prev.classList.add('asc-zoom');
      } else {
        next.style.transformOrigin = originCss;
        next.style.transform = 'scale(' + org[2] + ')';
        next.style.opacity = '0';
        next.classList.add('asc-zoom');
      }
      stage.appendChild(next);
      try { void stage.offsetWidth; } catch (e) { /* noop */ }
      slide = next;
      later(function () {
        if (back) {
          prev.style.transform = 'scale(' + org[2] + ')';
          prev.style.opacity = '0';
          next.style.opacity = '1';
        } else {
          next.style.transform = 'scale(1)';
          next.style.opacity = '1';
          prev.classList.add('asc-out');
          prev.style.opacity = '0';
        }
      }, ZOOM_ARM_MS);
      /* The fx go WITH the plate, not before it: an outgoing slide keeps its
       * sign lit for the length of the crossfade, the way a room you are
       * walking away from does. */
      later(function () { dropFx(prev); try { prev.remove(); } catch (e) { /* noop */ } }, ZOOM_MS + ZOOM_ARM_MS + 40);
    }

    applyFlags();
    /* A ZOOM IS A ZOOM: the band belongs to the wide shot and steps off for
     * every close-up. Written on EVERY move, not just the ones that change it,
     * so the level always matches the view that is actually up. */
    apronVisible(name === 'wide');
    /* ...and the pill is the other half of the same law: the band and the pill
     * are never both up, and never both away. */
    backVisible(name !== 'wide');
    if (prevName) sfx(back ? 'door' : 'blip', back ? 0.3 : 0.16, back ? null : { pitch: 1.05 });
    /* Keyboard keeps its place: the room's own verb takes focus once the node
     * is in the document (a fresh node ignores focus() in some engines). */
    later(function () {
      const first = firstHot(next);
      if (first) { try { first.focus(); } catch (e) { /* noop */ } }
    }, 0);
    fit();
  }

  /* Walk `children` BY INDEX and never `Array.isArray` it (trap 49): a browser
   * hands back an HTMLCollection and the node double hands back a real Array,
   * so a feature test on the shape passes in exactly the world that does not
   * matter. `classList.contains` is the same story - guarded, with the string
   * read as the floor. */
  function hasCls(n, cls) {
    try {
      if (n && n.classList && typeof n.classList.contains === 'function') return n.classList.contains(cls);
    } catch (e) { /* noop */ }
    return !!(n && typeof n.className === 'string'
      && (' ' + n.className + ' ').indexOf(' ' + cls + ' ') >= 0);
  }
  function firstHot(node) {
    const kids = node && node.children;
    if (!kids) return null;
    for (let i = 0; i < kids.length; i += 1) {
      if (hasCls(kids[i], 'asc-hot')) return kids[i];
    }
    return null;
  }

  /* --------------------------------------------------------------- props */

  /**
   * mountInView(name, node, rect) - hang a consumer's element inside ONE view,
   * at an authored stage rect, for as long as the scene lives.
   *
   * It is the seam a painted close-up needs and a patch cannot fill: a patch is
   * an image this file loads off a flag, a prop is furniture somebody else owns
   * and keeps (the corkboard's paper, the book's spread). The registry is per
   * VIEW rather than per slide, because a slide is rebuilt on every visit -
   * `buildSlide` re-seats every prop of the view it is building, which is what
   * makes a camera move free of the consumer's business.
   *
   * `rect` is optional: with one the node is placed in stage px, without one
   * the consumer's own sheet owns the geometry. Returns an unmount function.
   */
  function mountInView(name, node, rect) {
    if (dead || !node || !views[name]) return function () {};
    try { if (node.classList && typeof node.classList.add === 'function') node.classList.add('asc-prop'); }
    catch (e) { /* noop */ }
    if (Array.isArray(rect) && rect.length >= 4) placeRect(node, rect);
    const row = { view: String(name), node: node };
    props.push(row);
    if (viewName === row.view && slide) { try { slide.appendChild(node); } catch (e) { /* noop */ } }
    return function unmount() {
      const i = props.indexOf(row);
      if (i >= 0) props.splice(i, 1);
      try { node.remove(); } catch (e) { /* noop */ }
    };
  }

  /* ------------------------------------------------------------ overlays */

  /**
   * openOverlay(name, mountFn) - dim the slide and slide a panel up out of the
   * apron line. The CONSUMER fills the panel; this file supplies the box, the
   * scrim and the lifecycle. Exactly ONE may be up: opening a second closes
   * the first. Returns a close function scoped to THIS overlay, so a stale
   * handle can never take a later panel down.
   *
   * It comes off by REMOVAL, synchronously, never by `hidden` (trap 27) and
   * never behind an out-animation - a panel still on screen after close()
   * returned is a panel the next open would race.
   */
  function openOverlay(name, mountFn) {
    if (dead) return function () {};
    if (overlay) closeOverlay();
    overlayToken += 1;
    const token = overlayToken;
    const layer = el('div', 'asc-overlay');
    layer.setAttribute('data-overlay', String(name || ''));
    const scrim = el('div', 'asc-scrim');
    scrim.addEventListener('click', function () { closeOverlay(); });
    const panel = el('div', 'asc-panel');
    layer.appendChild(scrim);
    layer.appendChild(panel);
    root.appendChild(layer);
    overlay = { name: name, layer: layer, token: token };
    sfx('paper', 0.24);
    if (typeof mountFn === 'function') {
      try { mountFn(panel); } catch (e) { log('scene overlay mount threw: ' + ((e && e.message) || e)); }
    }
    later(function () { try { layer.classList.add('is-up'); } catch (e) { /* noop */ } }, ZOOM_ARM_MS);
    return function () { if (overlay && overlay.token === token) closeOverlay(); };
  }

  function closeOverlay() {
    if (!overlay) return;
    const layer = overlay.layer;
    overlay = null;
    try { layer.remove(); } catch (e) { /* noop */ }
    sfx('paper', 0.18);
  }

  /* ------------------------------------------------------- the Esc fold */

  /** Inward-out, lab.js's shape: the overlay first (it is the thing the player
   *  opened one press ago), then the close-up, and FALSE at home so the
   *  shell's own rung walks out of the room. This module binds no key. */
  function escapeStep() {
    if (dead) return false;
    if (overlay) { closeOverlay(); return true; }
    if (viewName && viewName !== 'wide') { showView('wide'); return true; }
    return false;
  }

  /* ---------------------------------------------------------------- fit */

  /** The last scale `fit()` computed - the stage's, never a prop's own. */
  let lastScale = 1;

  /** Scale to fit, then hang the apron off the painting's floor line and
   *  publish the band height. `--arm-band-h` goes on BOTH root and bar: the
   *  bar is a body-level sibling and cannot inherit a var set on root alone. */
  function fit() {
    if (dead) return;
    const w = root.clientWidth || (root.parentNode && root.parentNode.clientWidth) || stageW;
    const h = root.clientHeight || (root.parentNode && root.parentNode.clientHeight) || stageH;
    const s = Math.min(w / stageW, h / stageH) || 1;
    /* ONE STAGE, ONE SCALE, and a consumer may ask for it. A prop that has to
     * reason in SCREEN pixels (the corkboard's type floor is 10 real pixels,
     * whatever the plate is doing) cannot measure its own host: the miniature
     * on the wide shot carries a second scale of its own, so its rect would
     * answer a different number than the close-up's and the two walls would
     * lay out differently. This is the stage's number, the same for every prop
     * on it, and `scale()` below is the only way to read it. */
    lastScale = s;
    /* THE PHONE ANCHORS THE PAINTING TO THE TOP and takes a 72px band instead
     * of 110 - room.js's change, for room.js's reasons, on the chassis that
     * shares its apron. scene.css moves the box and the origin together under
     * `html.arc-mobile`; the origin must follow the anchor or the scale walks
     * the plane off-axis (this file's own header). The ZOOM is untouched: it
     * lives on `.asc-slide`, one level down, and still turns about its own
     * centre. */
    const phone = onPhone();
    stage.style.transform = 'translate(-50%,' + (phone ? '0' : '-50%') + ') scale(' + s + ')';

    let bandH = 0;
    if (bar) {
      const floor = phone ? APRON_MIN_MOBILE : APRON_MIN;
      const artTop = phone ? 0 : (h - stageH * s) / 2;
      let apronTop = artTop + apronStageTop * s;
      if (h - apronTop < floor) apronTop = h - floor;
      if (apronTop < 0) apronTop = 0;
      bandH = h - apronTop;
      bar.style.top = apronTop + 'px';
      bar.style.setProperty('--arm-band-h', bandH + 'px');
    }
    root.style.setProperty('--arm-band-h', bandH + 'px');
    /* ...AND ON <html>, because a viewport-level modal cannot inherit it.
     * `position:fixed` inside this room is contained by the room (root is
     * fixed, and an overlay panel carries a transform), so anything that has
     * to be measured against the WINDOW - the Records spotlight, a notice
     * reader - is mounted on <body> and has no ancestor of ours to read the
     * band off. One page-level custom property is the seam; destroy() clears
     * it, so a screen after this one never inherits a carpet that left. */
    setBandVar(bandH);
    return s;
  }

  /** The band height as a page fact. Defensive: the node double has no
   *  documentElement and a page with no scene must read 0. */
  function setBandVar(px) {
    try {
      const de = doc.documentElement;
      if (de && de.style && typeof de.style.setProperty === 'function') {
        de.style.setProperty('--arm-band-h', (Number(px) || 0) + 'px');
      }
    } catch (e) { /* noop */ }
  }
  function onResize() { fit(); }

  if (typeof window !== 'undefined' && window.addEventListener) window.addEventListener('resize', onResize);
  /* THE FIRST FIT RACES THE LAZY STYLESHEETS (room.js's r03 shot: an unstyled
   * root measures as the 1080px column and the room ships as a postcard).
   * Refit when each skin lands, when a plate decodes, and once next frame. */
  if (sheetScene && sheetScene.addEventListener) sheetScene.addEventListener('load', onResize);
  if (sheetRooms && sheetRooms.addEventListener) sheetRooms.addEventListener('load', onResize);
  if (typeof requestAnimationFrame === 'function') { try { requestAnimationFrame(onResize); } catch (e) { /* noop */ } }

  /* --------------------------------------------------------------- boot */

  auditRects();
  const host = o.mount || doc.body;
  if (host && typeof host.appendChild === 'function') host.appendChild(root);
  showView('wide');
  fit();

  function destroy() {
    if (dead) return;
    dead = true;
    for (let i = 0; i < timers.length; i += 1) clearTimeout(timers[i]);
    timers.length = 0;
    /* THE ALIVE LAYER GOES FIRST, because law 4 is the one thing in this file
     * that can outlive the room: every record stops its own timer and its own
     * rAF here, and `fxStats().timers` is 0 on the next line. */
    dropFx(null);
    if (typeof doc.removeEventListener === 'function') {
      try { doc.removeEventListener('visibilitychange', onVis); } catch (e) { /* noop */ }
    }
    if (typeof window !== 'undefined' && window.removeEventListener) {
      try { window.removeEventListener('blur', onBlur); } catch (e) { /* noop */ }
      try { window.removeEventListener('focus', onFocus); } catch (e) { /* noop */ }
    }
    if (typeof window !== 'undefined' && window.removeEventListener) window.removeEventListener('resize', onResize);
    if (sheetScene && sheetScene.removeEventListener) { try { sheetScene.removeEventListener('load', onResize); } catch (e) { /* noop */ } }
    if (sheetRooms && sheetRooms.removeEventListener) { try { sheetRooms.removeEventListener('load', onResize); } catch (e) { /* noop */ } }
    if (overlay) { try { overlay.layer.remove(); } catch (e) { /* noop */ } overlay = null; }
    if (bar) { try { bar.remove(); } catch (e) { /* noop */ } }
    /* The carpet goes with the room: the next screen has no band. */
    setBandVar(0);
    try { root.remove(); } catch (e) { /* noop */ }
    live.length = 0;
    /* The props go with the room, but they are NOT ours to destroy - the
     * consumer minted them and owns whatever they hold open. Dropping the
     * registry is the whole of our half. */
    props.length = 0;
  }

  return {
    root: root,
    showView: showView,
    view: function () { return viewName; },
    mountInView: mountInView,
    openOverlay: openOverlay,
    closeOverlay: closeOverlay,
    setFlag: setFlag,
    escapeStep: escapeStep,
    fit: fit,
    /** Stage px -> screen px, as of the last fit. See fit(). */
    scale: function () { return lastScale; },
    destroy: destroy,
    /* ---------------------------------------------- the alive layer's seams */
    /** {records, timers, held} - `timers` MUST be 0 after destroy(). */
    fxStats: function () {
      return { records: fxRecords.length, timers: fxTimerCount, held: fxHeld };
    },
    /** How many live records of one kind (all of them with no argument). */
    fxCount: function (kind) {
      if (!kind) return fxRecords.length;
      let n = 0;
      for (let i = 0; i < fxRecords.length; i += 1) if (fxRecords[i].kind === String(kind)) n += 1;
      return n;
    },
    /** The clock's two hands in degrees, or null if the room has no clock. */
    fxHands: function () {
      for (let i = 0; i < fxRecords.length; i += 1) {
        if (fxRecords[i].kind === 'clock' && fxRecords[i].hands) return fxRecords[i].hands();
      }
      return null;
    },
    /** Drive law 3 from a suite (the DOM double fires no visibilitychange). */
    fxHold: fxSetHold,
  };
}

export default createScene;
