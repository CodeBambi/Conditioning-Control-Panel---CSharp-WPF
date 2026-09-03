/* ============================================================================
 * shell/timecapsule.js - THE TIME CAPSULE, the one photograph in the trophy case.
 *
 * The community asked for this one (#general, 2026-09-02): the owner posted the
 * very first screenshot the Control Panel ever took, Wobberjockey said put it in
 * the trophy case and call it a time capsule, and the room agreed on the spot.
 * So the case has a small framed picture propped on its bottom shelf now, and
 * this is what happens when somebody picks it up.
 *
 * IT IS AN OVERLAY, NOT A SCREEN. Same bones as the Bugle and the noticeboard:
 * mounted on its own fixed stage, REMOVED rather than hidden (trap 27), one at a
 * time, and it binds nothing by default - house law (shell/exits.js header) is
 * that nothing outside shell.js handles Esc, and a modal the player opened one
 * press ago gets ONE rung at the TOP of escapeStep (trap 48). The shell wires
 * that rung; `bindEscape: true` is the standalone path.
 *
 * THE PICTURE LIVES HERE AND NOWHERE ELSE. campus.js draws the frame in the
 * case as VECTOR, because nothing raster may be drawn into the plan (campus.js's
 * own ruling on the peek plate). The photograph is this overlay's payload.
 *
 * Every visible string routes through the lexicon, so a mod re-voices the plaque
 * without touching this file.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';

/* ----------------------------------------------------------------------------
 * THE SHEET - a real file, linked once and lazily, resolved against THIS MODULE
 * rather than the document (shell modules and the page can sit at different
 * roots - the campus logo bug, campus.js's ART_BASE).
 * -------------------------------------------------------------------------- */
export const STYLE_ID = 'arc-capsule-style';

export const STYLE_HREF = (function resolveSheet() {
  try { return new URL('./timecapsule.css', import.meta.url).href; }
  catch (e) { return 'shell/timecapsule.css'; }
}());

/** The photograph itself. Same base trick as the sheet above. */
export const PHOTO_SRC = (function resolvePhoto() {
  try { return new URL('../art/time-capsule.jpg', import.meta.url).href; }
  catch (e) { return 'art/time-capsule.jpg'; }
}());

/** Link the sheet once. Idempotent, guarded, a no-op on the node DOM double. */
export function ensureStyles(doc) {
  const d = doc || (typeof document !== 'undefined' ? document : null);
  if (!d || typeof d.createElement !== 'function') return false;
  try {
    if (d.getElementById && d.getElementById(STYLE_ID)) return true;
    const link = d.createElement('link');
    link.id = STYLE_ID;
    link.rel = 'stylesheet';
    link.href = STYLE_HREF;
    const head = d.head || d.body || d.documentElement;
    if (!head || typeof head.appendChild !== 'function') return false;
    head.appendChild(link);
    return true;
  } catch (e) { return false; }
}

/* ------------------------------- tiny builders ---------------------------- */

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

function focusSoon(node) {
  try { if (node && typeof node.focus === 'function') node.focus(); }
  catch (e) { /* noop */ }
}

/** One cue through the one door (trap 18). A dropped cue is not an error. */
function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

/* ------------------------------- the overlay ------------------------------ */

/** The one open capsule, or null. Two of these in the air is a bug. */
let live = null;

/**
 * Take the picture out of the case.
 *
 * @param {Object=} opts
 * @param {Object=} opts.mount        where to append (default document.body)
 * @param {boolean=} opts.bindEscape  self-bind Esc (default FALSE - the shell
 *                                    owns the ladder; this is for demos)
 * @param {Function=} opts.onClose    called once, after the stage is gone
 * @returns {?Object} {root, close(), destroy()} - null with no DOM
 */
export function openTimeCapsule(opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  const mount = o.mount || doc.body;
  if (!mount || typeof mount.appendChild !== 'function') return null;

  // ONE PICTURE. A second press is the first one raised, not a second stage.
  if (live && !live.closed) { focusSoon(live.firstButton); return live.handle; }

  ensureStyles(doc);

  const title = t('capsule_title', 'Day One');

  const root = el('div', 'arc-capsulestage');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');
  attr(root, 'aria-label', t('capsule_label', 'Time Capsule') + ' · ' + title);

  const veil = el('div', 'arc-capsule-veil');
  attr(veil, 'aria-hidden', 'true');
  root.appendChild(veil);

  const fig = el('figure', 'arc-capsule');

  /* THE PHOTOGRAPH. `alt` is the picture said out loud, and it is a lexicon row
   * like every other string on the page - a screen reader is a reader. */
  const shot = el('div', 'arc-capsule-shot');
  const img = doc.createElement('img');
  img.className = 'arc-capsule-img';
  attr(img, 'src', PHOTO_SRC);
  attr(img, 'alt', t('capsule_alt',
    'The Conditioning Dashboard on day one: flash, mandatory shorts, visuals, subliminals.'));
  attr(img, 'decoding', 'async');
  /* A picture that fails to load must not leave a broken glyph on a plaque: the
   * frame simply goes empty and the words carry the beat (the campus logo
   * precedent, `data-art="off"`). */
  img.addEventListener('error', function () { attr(shot, 'data-art', 'off'); });
  shot.appendChild(img);
  fig.appendChild(shot);

  const plaque = el('figcaption', 'arc-capsule-plaque');
  plaque.appendChild(el('p', 'arc-capsule-kicker', t('capsule_label', 'Time Capsule').toUpperCase()));
  plaque.appendChild(el('h2', 'arc-capsule-title', title));
  plaque.appendChild(el('p', 'arc-capsule-line', t('capsule_line',
    'The first screenshot ever taken of the Control Panel. Everything here grew out of this.')));
  plaque.appendChild(el('p', 'arc-capsule-fine', t('capsule_fine',
    'Donated to the case by the community, September 2026.')));
  fig.appendChild(plaque);

  /* ------------------------------ the way out ---------------------------- */
  let closed = false;
  let escBound = false;

  function onKey(e) {
    if (!e) return;
    if (e.key !== 'Escape' && e.key !== 'Esc') return;
    try { e.preventDefault(); e.stopPropagation(); } catch (err) { /* noop */ }
    close();
  }

  function close() {
    if (closed) return;
    closed = true;
    if (escBound) {
      try { doc.removeEventListener('keydown', onKey, true); } catch (e) { /* noop */ }
      escBound = false;
    }
    try { root.remove(); } catch (e) { /* noop */ }
    if (live && live.handle === handle) live = null;
    sfx('paper', 0.18, { pitch: 0.92 });
    try { if (typeof o.onClose === 'function') o.onClose(); } catch (e) { /* noop */ }
  }

  const x = el('button', 'arc-capsule-x', '✕');
  x.type = 'button';
  attr(x, 'aria-label', t('back', 'Back'));
  x.addEventListener('click', close);
  fig.appendChild(x);

  /* Three ways out and they are the three a picture in a frame has: the cross,
   * the dusk around it, and Esc (the shell's rung, or `bindEscape` here). */
  veil.addEventListener('click', close);
  root.addEventListener('click', function (e) { if (e && e.target === root) close(); });

  /* ONE focusable thing in here, so the trap is one line - the notice reader's
   * shape. Tab cannot walk out of a dialog it never leaves. */
  root.addEventListener('keydown', function (ev) {
    if (!ev || ev.key !== 'Tab') return;
    try { ev.preventDefault(); } catch (e) { /* noop */ }
    focusSoon(x);
  });

  root.appendChild(fig);
  mount.appendChild(root);

  if (o.bindEscape && typeof doc.addEventListener === 'function') {
    doc.addEventListener('keydown', onKey, true);
    escBound = true;
  }

  sfx('paper', 0.26);
  focusSoon(x);

  const handle = {
    root: root,
    close: close,
    destroy: close,
    get closed() { return closed; },
  };
  live = { handle: handle, firstButton: x, get closed() { return closed; } };
  return handle;
}

/** The open capsule, or null. Test seam and the driver's re-entry guard. */
export function currentTimeCapsule() {
  return (live && !live.closed) ? live.handle : null;
}

/** Put it back on the shelf. Returns true when one was up (the Esc rung). */
export function closeTimeCapsule() {
  const up = currentTimeCapsule();
  if (!up) return false;
  try { up.close(); } catch (e) { /* noop */ }
  return true;
}
