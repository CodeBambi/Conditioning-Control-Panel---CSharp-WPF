/* =========================================================================
 * THE TIME CAPSULE - the trophy case in the entrance hall, and what is in it.
 * Community ask (Sept 2): the case had been decoration since the campus was
 * drawn, and the thing the school has to exhibit is its own first night - the
 * original pink Conditioning Dashboard, February 2026, framed under a plaque.
 *
 * An OVERLAY, never a screen (traps 48/50): module-local stage, one Esc rung
 * the shell owns, REMOVED on close and never hidden (trap 27) - bugle.js's
 * lifecycle at z 38, over the campus, under the ceremony and EMI.
 *
 * DUMB. It renders a `t`, one exhibit row and whether the case is sealed; it
 * owns no store, bridge, key or clock, so the unlock rule lives in one place
 * and this file drives headless off a plain object. Every string is a lexicon
 * row (mirrored in C# NeutralLexicon), the plaque line split into two clause
 * rows because a value over 96 chars can never be mod-skinned (trap 26).
 * Sound is `arcademy-sfx` and nothing else (trap 18), on cue names audio.js
 * already holds (trap 115).
 * ========================================================================= */

import { exitBar, signButton } from './exits.js';

/** THE NIGHTS. The shell reads this too - one number, one file. */
export const CAPSULE_NIGHTS = 30;

/**
 * THE DOOR, as arithmetic. PURE, so the one rule the exhibit turns on is
 * assertable without a store, a host or a screen. MONOTONIC BY CONSTRUCTION:
 * `best` only rises and `opened` only latches true, so a streak that breaks in
 * March cannot re-wrap a parcel the player has already unwrapped. The caller
 * banks only when `changed`, which is nothing at all on an ordinary night.
 *
 * @param {?Object} blob  the stored `{opened, best}`, or anything at all
 * @param {number} streakNow  the CURRENT host-owned streak
 * @param {number=} need  nights the case asks for; @returns {opened,best,changed}
 */
export function capsuleDoor(blob, streakNow, need) {
  const o = (blob && typeof blob === 'object' && !Array.isArray(blob)) ? blob : {};
  const wantOpen = !!o.opened;
  const wasBest = Math.max(0, Number(o.best) | 0);
  const gate = Number(need) > 0 ? Math.round(Number(need)) : CAPSULE_NIGHTS;
  const now = Math.max(0, Number(streakNow) | 0);
  const best = Math.max(wasBest, now);
  const opened = wantOpen || best >= gate;
  return { opened, best, changed: best !== wasBest || opened !== wantOpen };
}

/**
 * THE EXHIBITS. A LIST, deliberately, with one row in it: the case is meant to
 * grow, and a second entry costs a row here plus its art and its clause rows.
 * `art` resolves module-relative at render time (the nine-broken-logos bug).
 */
export const EXHIBITS = Object.freeze([
  Object.freeze({
    id: 'capsule_2026_02',
    art: '../art/campus/capsule_2026_02.webp',
    titleKey: 'capsule_title',
    titleText: 'Time Capsule',
    /* Two clauses, one space, one paragraph - vn/lex.js PAPERS's shape. */
    lineKeys: ['capsule_line_2026_02_a', 'capsule_line_2026_02_b'],
    lineText: [
      'The first dashboard. February 2026.',
      'Everything was pink and the DROP button was the size of a doormat.',
    ],
    footKey: 'capsule_footer',
    footText: 'Sealed by the Registrar. Opened at thirty nights.',
  }),
]);

export const STYLE_ID = 'arc-capsule-style';
export const STYLE_HREF = (function resolveSheet() {
  try { return new URL('./capsule.css', import.meta.url).href; }
  catch (e) { return 'shell/capsule.css'; }
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
    if (head && head.appendChild) head.appendChild(link); return true;
  } catch (e) { return false; }
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

/* The one live stage, so a double-click on the case cannot deal two. */
let live = null;

function elem(doc, tag, cls, text) {
  const n = doc.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/**
 * OPEN THE CASE.
 * @param {Object} o
 * @param {Function} o.t       lexicon lookup, t(key, fallback)
 * @param {boolean=} o.sealed  true = the parcel, false = the exhibit
 * @param {number=} o.have     nights on the board (the sealed count)
 * @param {number=} o.need     nights the tag asks for (CAPSULE_NIGHTS)
 * @param {boolean=} o.reducedMotion
 * @param {Element=} o.mount   defaults to document.body
 * @param {Function=} o.onClose, @param {Function=} o.log
 * @returns {?Object} {root, close, closed} - null with nowhere to mount
 */
export function openCapsule(o) {
  const opts = o || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  const mount = opts.mount || doc.body;
  if (!mount || typeof mount.appendChild !== 'function') return null;
  if (live && !live.closed) return live;

  const t = (typeof opts.t === 'function') ? opts.t : (k, f) => f;
  const sealed = !!opts.sealed;
  const reduced = !!opts.reducedMotion;
  const need = Number(opts.need) > 0 ? Math.round(Number(opts.need)) : CAPSULE_NIGHTS;
  const have = Math.max(0, Math.min(need, Number(opts.have) | 0));
  const row = EXHIBITS[0];

  ensureStyles(doc);

  const root = elem(doc, 'div', 'arc-capsulestage' + (reduced ? ' arc-capsule-reduced' : ''));
  try {
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-modal', 'true');
    root.setAttribute('aria-label', t('campus_trophy_case', 'Trophy Case'));
  } catch (e) { /* a double with no setAttribute renders the same */ }

  const box = elem(doc, 'div', 'arc-capsule-box' + (sealed ? ' is-sealed' : ' is-open'));

  if (sealed) {
    /* THE PARCEL: paper, ribbons and a tag, all drawn - a sealed case ships
     * no art, so there is nothing here that can 404. */
    const parcel = elem(doc, 'div', 'arc-capsule-parcel');
    try { parcel.setAttribute('aria-hidden', 'true'); } catch (e) { /* noop */ }
    parcel.appendChild(elem(doc, 'i', 'arc-capsule-ribv'));
    parcel.appendChild(elem(doc, 'i', 'arc-capsule-ribh'));
    parcel.appendChild(elem(doc, 'i', 'arc-capsule-knot'));
    box.appendChild(parcel);

    const tag = elem(doc, 'div', 'arc-capsule-tag');
    tag.appendChild(elem(doc, 'div', 'arc-capsule-tagline',
      t('capsule_sealed_tag', 'opens at 30 nights')));
    /* THE COUNT IS THE POINT OF THE TAG: a sealed thing with no number on it
     * is a locked door, and a locked door is not a promise. */
    tag.appendChild(elem(doc, 'div', 'arc-capsule-count', have + ' / ' + need));
    box.appendChild(tag);
    box.appendChild(elem(doc, 'div', 'arc-capsule-hint',
      t('capsule_sealed_hint', 'The case is wrapped and taped. The tag has a number on it.')));
  } else {
    /* THE FRAME: wood, a brass fillet, a mat and the photograph inside it AS
     * IT WAS - no crop, no blur, no filter over it. */
    const frame = elem(doc, 'div', 'arc-capsule-frame');
    const mat = elem(doc, 'div', 'arc-capsule-mat');
    const pic = doc.createElement('img');
    pic.className = 'arc-capsule-pic';
    try {
      pic.alt = row.titleText;
      pic.decoding = 'async';
      /* ART IS OPTIONAL, the campus's own probe rule: a 404 or a missing
       * decoder flips the box to data-art="off" and the CSS draws an empty
       * mount. The plaque never leans on it - the words render either way. */
      pic.addEventListener('error', () => {
        try { box.setAttribute('data-art', 'off'); } catch (e) { /* noop */ }
        if (typeof opts.log === 'function') {
          try { opts.log('capsule: no exhibit art - drawing the empty mount'); } catch (e2) { /* noop */ }
        }
      });
      pic.src = new URL(row.art, import.meta.url).href;
    } catch (e) { try { pic.src = 'art/campus/' + row.id + '.webp'; } catch (e2) { /* noop */ } }
    mat.appendChild(pic);
    frame.appendChild(mat);
    box.appendChild(frame);

    /* THE BRASS PLAQUE, under the picture where a plaque goes. */
    const plaque = elem(doc, 'div', 'arc-capsule-plaque');
    plaque.appendChild(elem(doc, 'div', 'arc-capsule-ptitle', t(row.titleKey, row.titleText)));
    plaque.appendChild(elem(doc, 'div', 'arc-capsule-pline',
      row.lineKeys.map((k, i) => t(k, row.lineText[i])).join(' ')));
    plaque.appendChild(elem(doc, 'div', 'arc-capsule-pfoot', t(row.footKey, row.footText)));
    box.appendChild(plaque);
  }

  let closed = false;
  const handle = { root, close, get closed() { return closed; } };

  function close() {
    if (closed) return;
    closed = true;
    /* REMOVED, never hidden (trap 27). */
    try { if (root.parentNode) root.parentNode.removeChild(root); } catch (e) { /* noop */ }
    if (live === handle) live = null;
    sfx('paper', 0.18, { pitch: 0.92 });
    try { if (typeof opts.onClose === 'function') opts.onClose(); }
    catch (e) {
      if (typeof opts.log === 'function') {
        try { opts.log('capsule onClose: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ }
      }
    }
  }

  /* exits.js mints the way out like every other back in the school, and Esc is
   * NOT bound here - the shell's ladder owns the key (trap 29). */
  const back = signButton(t('back', 'Back'), close, { dir: 'back' });
  box.appendChild(exitBar([back]));

  /* A press on the dark folds it; the case is read-only, so that costs
   * nothing. */
  try {
    root.addEventListener('click', (e) => { if (e && e.target === root) close(); });
  } catch (e) { /* noop */ }

  root.appendChild(box);
  mount.appendChild(root);
  live = handle;

  sfx(sealed ? 'thud' : 'slide', sealed ? 0.34 : 0.4, sealed ? { pitch: 0.9 } : { pitch: 1.06 });
  try { if (back.focus) back.focus(); } catch (e) { /* noop */ }
  return handle;
}

/** The open case, or null. Test seam and the shell's Esc rung. */
export function currentCapsule() {
  return (live && !live.closed) ? live : null;
}

export default { openCapsule, currentCapsule, ensureStyles, capsuleDoor, EXHIBITS, CAPSULE_NIGHTS };
