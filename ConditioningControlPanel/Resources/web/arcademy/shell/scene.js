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
 *    can ever rise above EMI at z50);
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
 * THE APRON LINE. Every VN set was painted with its lower third held calm and
 * dark for a dialogue band, so the apron owns that floor. Hotspot rects must
 * keep their business ABOVE it; a row that crosses is WARNED at construction
 * (never thrown - a scene with one badly-placed rect still opens).
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';

/** The plane the VN art was authored on. Overridable, rarely overridden. */
const STAGE_W = 1376;
const STAGE_H = 768;

/* The apron claims the painting's bottom fifth-and-a-bit plus whatever
 * letterbox hangs under it. Held as a FRACTION so a custom stage height moves
 * the line with the art instead of leaving it stranded at 640. */
const APRON_FRACTION = 640 / 768;
/** The band never shrinks below this, even art-to-the-floor (px, real). */
const APRON_MIN = 110;

/** The zoom between slides. One number, mirrored in scene.css. */
const ZOOM_MS = 320;
/** A frame of grace between "in the DOM at the start pose" and "go". */
const ZOOM_ARM_MS = 20;
/** How small a close-up starts, as a fraction of the stage. Clamped so a huge
 *  rect still reads as a zoom and a tiny one does not start at a pinprick. */
const ZOOM_MIN_K = 0.14;
const ZOOM_MAX_K = 0.80;

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

  /* ------------------------------------------------------------ hotspots */

  function placeRect(node, rect) {
    node.style.left = rect[0] + 'px';
    node.style.top = rect[1] + 'px';
    node.style.width = rect[2] + 'px';
    node.style.height = rect[3] + 'px';
  }

  /** Rows are authored above the apron line or the band sits on them. A
   *  crossing row is a WARNING, never a throw: one bad rect must not cost the
   *  player the whole room. Checked once, at construction, for every view -
   *  so it is reported before that slide is ever visited. */
  function auditRects() {
    Object.keys(views).forEach(function (name) {
      const rows = (views[name] && views[name].hotspots) || [];
      for (let i = 0; i < rows.length; i += 1) {
        const r = rows[i];
        if (!Array.isArray(r) || r.length < 4) { log('scene: malformed hotspot row in view ' + name + ' at index ' + i); continue; }
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

    /* THE STEP-BACK PILL, lab.js's, on every close-up. The apron's slab LEAVES
     * THE ROOM; a player two clicks deep needs the other verb, and Esc is not
     * a mouse. Home has none - the apron is the way out from there. */
    if (name !== 'wide') {
      const pill = el('button', 'asc-back', '‹ ' + t('back', 'Back'));
      pill.type = 'button';
      pill.addEventListener('click', function () { showView('wide'); });
      node.appendChild(pill);
    }
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
    for (let i = live.length - 1; i >= 0; i -= 1) { if (live[i].host === prev) live.splice(i, 1); }

    viewName = name;
    const next = buildSlide(name);
    next.style.zIndex = back ? '1' : '2';
    if (prev) prev.style.zIndex = back ? '2' : '1';

    if (reduced || !prev) {
      stage.appendChild(next);
      if (prev) { try { prev.remove(); } catch (e) { /* noop */ } }
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
      later(function () { try { prev.remove(); } catch (e) { /* noop */ } }, ZOOM_MS + ZOOM_ARM_MS + 40);
    }

    applyFlags();
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

  /** Scale to fit, then hang the apron off the painting's floor line and
   *  publish the band height. `--arm-band-h` goes on BOTH root and bar: the
   *  bar is a body-level sibling and cannot inherit a var set on root alone. */
  function fit() {
    if (dead) return;
    const w = root.clientWidth || (root.parentNode && root.parentNode.clientWidth) || stageW;
    const h = root.clientHeight || (root.parentNode && root.parentNode.clientHeight) || stageH;
    const s = Math.min(w / stageW, h / stageH) || 1;
    stage.style.transform = 'translate(-50%,-50%) scale(' + s + ')';

    let bandH = 0;
    if (bar) {
      const artTop = (h - stageH * s) / 2;
      let apronTop = artTop + apronStageTop * s;
      if (h - apronTop < APRON_MIN) apronTop = h - APRON_MIN;
      if (apronTop < 0) apronTop = 0;
      bandH = h - apronTop;
      bar.style.top = apronTop + 'px';
      bar.style.setProperty('--arm-band-h', bandH + 'px');
    }
    root.style.setProperty('--arm-band-h', bandH + 'px');
    return s;
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
    if (typeof window !== 'undefined' && window.removeEventListener) window.removeEventListener('resize', onResize);
    if (sheetScene && sheetScene.removeEventListener) { try { sheetScene.removeEventListener('load', onResize); } catch (e) { /* noop */ } }
    if (sheetRooms && sheetRooms.removeEventListener) { try { sheetRooms.removeEventListener('load', onResize); } catch (e) { /* noop */ } }
    if (overlay) { try { overlay.layer.remove(); } catch (e) { /* noop */ } overlay = null; }
    if (bar) { try { bar.remove(); } catch (e) { /* noop */ } }
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
    destroy: destroy,
  };
}

export default createScene;
