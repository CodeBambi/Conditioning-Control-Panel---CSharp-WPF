/* ============================================================================
 * shell/idcard.js - THE STUDENT ID, front and centre.
 *
 * The campus already had a laminated ID leaning in the bottom-left corner
 * (shell/campus.js's `.campus-idcard`). It was scenery. This file is the two
 * halves that make it a document you can pick up:
 *
 *   THE PARTS      the generic portraits, the chip rungs, the student number
 *                  and the barcode - pure functions, shared with campus.js so
 *                  the furniture card and the spotlight can never disagree
 *                  about what your card says.
 *   THE SPOTLIGHT  `createIdSpotlight()`, built to shell/records.js's shape:
 *                  a fixed z46 veil + key light, the card at 560px under it,
 *                  a six-tile stat sheet beside it, a placard, one focusable
 *                  Close, a Tab trap, and module-local timers that a dismiss
 *                  clears. Esc is NOT handled here - boot.js owns the key and
 *                  shell.js's escapeStep asks `dismiss()` first (trap 48).
 *
 * THREE LAWS THIS FILE KEEPS, and they are the reason it looks the way it does:
 *
 *   1. TRAP 1, THE ECHO. Nothing here decides what the chip says. The chip
 *      paints the rung it is HANDED (`setChipState`) and every click leaves
 *      through `onChip()` for the shell, which posts a frame and waits for the
 *      host to answer. The only optimistic paint is `wait`, and a "waiting"
 *      look is not a result.
 *   2. THE SNOWFLAKE RULE (PRESENCE.md §10). No Discord CDN url and no Discord
 *      user id ever reaches this page. `profile.avatarUrl` is a `data:` URI the
 *      host baked, or a first-party proxy url on the web, or null - and null
 *      draws a generic portrait rather than fetching anything.
 *   3. TRAP 36, TRANSFORMS ONLY. The holo foil drifts and answers the mouse by
 *      `transform`; the name's glint is a translating strip inside a clipping
 *      box, never an animated `background-position`. Every base state is
 *      VISIBLE, so `animation:none` (reduced motion) is a finished still.
 *
 * NOT SHAREABLE, by ruling. `shell/reportcard.js` is the ONE share pipeline
 * (trap 13) and a card wearing a real avatar is a paste-able identity; there is
 * no copy button, no share verb and no image export anywhere below this line.
 * ==========================================================================*/

import { thud } from './punchcard.js';

/* ----------------------------------------------------------------------------
 * DOM + CUE HELPERS (records.js's, verbatim - one shape for one job)
 * -------------------------------------------------------------------------- */

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

function attr(node, name, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(name, value); }
  catch (e) { /* noop */ }
}

function css(node, name, value) {
  try {
    if (node && node.style && typeof node.style.setProperty === 'function') {
      node.style.setProperty(name, value);
    }
  } catch (e) { /* noop */ }
}

/** Reduced motion, either signal: the shell's own class or the OS preference. */
export function idReducedMotion() {
  try {
    const root = (typeof document !== 'undefined') && document.documentElement;
    if (root && root.classList && typeof root.classList.contains === 'function'
      && root.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* noop */ }
  return false;
}

/* ----------------------------------------------------------------------------
 * THE NUMBER
 * -------------------------------------------------------------------------- */

/** FNV-1a over a string -> uint32. Pure, `core`-free, and the same on both
 *  surfaces (the furniture card prints the number the spotlight prints). */
function fnv1a(str) {
  let h = 2166136261;
  const s = String(str == null ? '' : str);
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619) >>> 0;
  }
  return h >>> 0;
}

function hex4(n) { return ('000' + (n >>> 0).toString(16)).slice(-4).toUpperCase(); }

/**
 * THE STUDENT NUMBER, derived on the page and nowhere else (owner ruling 3).
 * The presence `self` opaque id when the host has pushed one - it is stable
 * across devices and it is already an opaque token, so hashing it to eight hex
 * digits leaks nothing it did not already say. With no `self` id yet the number
 * is seeded off the two things a fresh card DOES know (the enrolment date and
 * the name), and it says `temp` in tiny type until the real one lands.
 *
 * @param {?string} selfId    the presence snapshot's `self`, or null
 * @param {?string} enrolled  'yyyy-mm-dd' of the earliest enrolment, or null
 * @param {?string} name      the CCP nickname, or null
 * @returns {{no:string, temp:boolean}}
 */
export function studentNumber(selfId, enrolled, name) {
  const real = selfId != null && String(selfId) !== '';
  const seed = real
    ? 'arcademy-id|' + String(selfId)
    : 'arcademy-id-temp|' + String(enrolled || '') + '|' + String(name || '');
  const a = fnv1a(seed);
  const b = fnv1a(seed + '|2');
  return { no: hex4(a >>> 16) + '-' + hex4(b & 0xffff), temp: !real };
}

/* ----------------------------------------------------------------------------
 * THE GENERIC PORTRAITS
 *
 * Inline SVG, encoded as a `data:` URI so ONE `<img>` carries either the real
 * avatar or the stand-in - the fallback is an `src` swap, never a second node
 * and never a second layout. No fetch, no asset, nothing to 404 (trap 2's
 * neighbour: an offline webview has no network to lose).
 *
 * The stamp type is `monospace`: an SVG loaded through `<img>` is its own
 * document and cannot reach the page's bundled faces, so naming one would be a
 * silent fallback rather than a look.
 * -------------------------------------------------------------------------- */

const PENDING_SVG = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 56 66">'
  + '<defs><pattern id="s" width="4" height="4" patternUnits="userSpaceOnUse">'
  + '<rect width="4" height="2" fill="#000" opacity=".16"/></pattern></defs>'
  + '<rect width="56" height="66" fill="#33305c"/>'
  + '<g shape-rendering="crispEdges" fill="#8c80c4">'
  + '<path d="M20 14h16v18H20z"/><path d="M22 12h12v2H22z"/>'
  + '<path d="M12 42h32v24H12z"/><path d="M16 38h24v4H16z"/><path d="M24 32h8v6h-8z"/></g>'
  + '<g shape-rendering="crispEdges" fill="#b8a6e8">'
  + '<path d="M20 14h16v4H20z"/><path d="M12 42h4v24h-4z"/></g>'
  + '<rect width="56" height="66" fill="url(#s)"/>'
  + '<g transform="translate(28 34) rotate(-14) translate(-25 -11)" opacity=".95">'
  + '<rect x="0" y="0" width="50" height="22" rx="2" fill="none" stroke="#FF69B4" stroke-width="1.8"/>'
  + '<text x="25" y="10" text-anchor="middle" font-family="monospace" font-size="7"'
  + ' font-weight="700" fill="#FF69B4" letter-spacing="1">PHOTO</text>'
  + '<text x="25" y="19" text-anchor="middle" font-family="monospace" font-size="7"'
  + ' font-weight="700" fill="#FF69B4" letter-spacing="1">PENDING</text></g>'
  + '</svg>';

const ANON_SVG = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 56 66">'
  + '<rect width="56" height="66" fill="#1f1d3e"/>'
  + '<g shape-rendering="crispEdges">'
  + '<rect x="20" y="12" width="16" height="16" fill="#e8dcc8"/>'
  + '<rect x="18" y="10" width="20" height="6" fill="#4a3f8a"/>'
  + '<rect x="24" y="19" width="2" height="2" fill="#1a1030"/>'
  + '<rect x="30" y="19" width="2" height="2" fill="#1a1030"/>'
  + '<rect x="16" y="30" width="24" height="24" fill="#b8a6e8"/>'
  + '<rect x="22" y="30" width="12" height="8" fill="#f2ebdd"/>'
  + '<rect x="16" y="54" width="9" height="10" fill="#2e2e55"/>'
  + '<rect x="31" y="54" width="9" height="10" fill="#2e2e55"/></g>'
  + '</svg>';

function dataUri(svg) { return 'data:image/svg+xml,' + encodeURIComponent(svg); }

const PENDING_URI = dataUri(PENDING_SVG);
const ANON_URI = dataUri(ANON_SVG);

/**
 * The stand-in portrait for a profile that has no photo on it.
 * The ANON rung gets the presence pixel student - at that rung "a ghost with no
 * name or picture" is a CHOICE the player made, and their own card should show
 * them what strangers see. Every other rung gets PHOTO PENDING, which is a gap
 * and says so.
 * @param {?string} share  the presenceShare rung
 * @returns {string} a `data:` URI
 */
export function genericPortrait(share) {
  return String(share) === 'anon' ? ANON_URI : PENDING_URI;
}

/** What the photo well ANNOUNCES. A drawn stand-in is a gap, and a gap that a
 *  screen reader cannot hear is a gap that is simply missing. */
export function portraitLabel(t, profile) {
  const tr = typeof t === 'function' ? t : (k, fb) => fb;
  return hasPhoto(profile) ? '' : tr('id_photo_pending', 'Photo pending');
}

/** The `src` one `<img>` should carry for this profile. Never a Discord url. */
export function portraitSrc(profile) {
  const p = profile || {};
  if (hasPhoto(p) && typeof p.avatarUrl === 'string' && p.avatarUrl) return p.avatarUrl;
  return genericPortrait(p.presenceShare);
}

/** True when the card is entitled to wear the real photo (the consent matrix). */
export function hasPhoto(profile) {
  const p = profile || {};
  return !!p.discordLinked && String(p.presenceShare) === 'discord' && !!p.avatarUrl;
}

/* ----------------------------------------------------------------------------
 * THE CHIP
 *
 * ONE switch with three resting rungs and one in-flight look. The rung is
 * derived from the profile; `wait` and `pending` are handed down by the shell
 * and never guessed here (trap 1).
 * -------------------------------------------------------------------------- */

/** @returns {'on'|'use'|'link'} the rung this profile rests on. */
export function chipRung(profile) {
  const p = profile || {};
  if (!p.discordLinked) return 'link';
  return String(p.presenceShare) === 'discord' ? 'on' : 'use';
}

/**
 * The chip's words. The furniture card is 64px wide and takes the SHORT form;
 * the spotlight has room for the whole consent sentence.
 * @param {Function} t
 * @param {string} state  'on' | 'use' | 'link' | 'wait'
 * @param {boolean=} short
 */
export function chipLabel(t, state, short) {
  const tr = typeof t === 'function' ? t : (k, fb) => fb;
  if (short) {
    if (state === 'on') return tr('id_chip_on', 'Photo on');
    if (state === 'use') return tr('id_chip_use', 'Use Discord photo');
    if (state === 'wait') return tr('id_chip_wait', 'Waiting...');
    return tr('id_chip_link', 'Link Discord');
  }
  if (state === 'on') return tr('id_photo_on', 'Discord photo on');
  if (state === 'use') return tr('id_photo_use', 'Use my Discord photo');
  if (state === 'wait') return tr('id_photo_waiting', 'Waiting on Discord...');
  return tr('id_photo_link', 'Link Discord for my photo');
}

/** The consent sentence under the chip (spotlight only). Says what the click does. */
export function chipHint(t, state, web) {
  const tr = typeof t === 'function' ? t : (k, fb) => fb;
  if (state === 'on') {
    return tr('id_photo_hint_off',
      'Your ghost on campus wears this photo too. Tap to take it down (your name stays).');
  }
  return web
    ? tr('id_photo_hint_web',
      'Sends you to Connections to link Discord, then straight back here with the photo on.')
    : tr('id_photo_hint_app',
      'Opens the Discord link-up in the app, then your photo goes on the card and on campus.');
}

/** The Discord glyph, as an inline `<svg>` (the chip's `link` rung wears it). */
function discordGlyph() {
  const ns = 'http://www.w3.org/2000/svg';
  try {
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('class', 'id-chip-glyph');
    svg.setAttribute('aria-hidden', 'true');
    const path = document.createElementNS(ns, 'path');
    path.setAttribute('fill', 'currentColor');
    path.setAttribute('d', 'M19.5 5.3A17 17 0 0 0 15.4 4l-.5 1a15.6 15.6 0 0 0-5.8 0L8.6 4a17 17 0 0 0-4.1'
      + ' 1.3C1.9 9.2 1.2 13 1.5 16.7A17 17 0 0 0 6.6 19l1-1.6a11 11 0 0 1-1.7-.8l.4-.3a12 12 0 0 0 11.4'
      + ' 0l.4.3-1.7.8 1 1.6a17 17 0 0 0 5.1-2.3c.4-4.3-.7-8-3-11.4ZM8.7 14.4c-1 0-1.8-.9-1.8-2s.8-2'
      + ' 1.8-2 1.8.9 1.8 2-.8 2-1.8 2Zm6.6 0c-1 0-1.8-.9-1.8-2s.8-2 1.8-2 1.8.9 1.8 2-.8 2-1.8 2Z');
    svg.appendChild(path);
    return svg;
  } catch (e) { return null; }
}

/** The crest: an arcade token with a star. Drawn, never an asset. */
function crestGlyph(cls) {
  const ns = 'http://www.w3.org/2000/svg';
  try {
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 20 20');
    svg.setAttribute('class', cls || 'id-crest-mark');
    svg.setAttribute('aria-hidden', 'true');
    const ring = document.createElementNS(ns, 'circle');
    ring.setAttribute('cx', '10'); ring.setAttribute('cy', '10'); ring.setAttribute('r', '9');
    ring.setAttribute('fill', 'none'); ring.setAttribute('stroke', 'currentColor');
    ring.setAttribute('stroke-width', '2');
    const star = document.createElementNS(ns, 'path');
    star.setAttribute('fill', 'currentColor');
    star.setAttribute('d', 'M10 5.5l1.3 2.8 3 .3-2.3 2 .7 3-2.7-1.6-2.7 1.6.7-3-2.3-2 3-.3z');
    svg.appendChild(ring); svg.appendChild(star);
    return svg;
  } catch (e) { return null; }
}

/**
 * PAINT ONE CHIP. Shared by both surfaces so the furniture card and the
 * spotlight always wear the same rung. `pending` keeps the LAST label and only
 * greys the chip: the echo has not landed, so there is nothing new to say.
 *
 * @param {Element} chip
 * @param {Function} t
 * @param {string} state  'on'|'use'|'link'|'wait'|'pending'
 * @param {boolean=} short
 */
export function paintChip(chip, t, state, short) {
  if (!chip) return;
  const pending = state === 'pending';
  const rung = pending ? (chip.dataset ? (chip.dataset.rung || 'link') : 'link') : String(state || 'link');
  try { if (chip.dataset && !pending) chip.dataset.rung = rung; } catch (e) { /* noop */ }
  const base = short ? 'id-chip' : 'arc-id-chip id-chip';
  const cls = base + ' is-' + rung + (pending ? ' is-pending' : '');
  try { chip.className = cls; } catch (e) { /* noop */ }
  try { chip.textContent = ''; } catch (e) { /* noop */ }
  const led = el('i', 'id-chip-led');
  attr(led, 'aria-hidden', 'true');
  chip.appendChild(led);
  if (rung === 'link') {
    const g = discordGlyph();
    if (g) chip.appendChild(g);
  }
  const shown = (state === 'wait') ? 'wait' : rung;
  chip.appendChild(el('span', 'id-chip-label', chipLabel(t, shown, short)));
  try { chip.disabled = (state === 'wait' || pending); } catch (e) { /* noop */ }
  attr(chip, 'aria-label', chipLabel(t, shown, false));
}

/* ----------------------------------------------------------------------------
 * PHOTO DAY - the one ceremony on this card (REVEAL tier, once per link).
 * A shutter, a 120ms white flash on the WELL only (never the screen), and the
 * photo developing in from grey. Reduced motion is a plain swap: the classes
 * are simply not added, and the finished state is what was already there.
 * -------------------------------------------------------------------------- */

/**
 * @param {Element} well      the `.id-photo` / `.arc-id-photo` box
 * @param {Function} sfx      (name, level, extra)
 * @param {boolean} still     reduced motion
 * @param {Function} schedule (ms, fn) - the caller's own tracked timer
 */
export function runPhotoDay(well, sfx, still, schedule) {
  const cue = typeof sfx === 'function' ? sfx : () => {};
  const at = typeof schedule === 'function' ? schedule : (ms, fn) => { try { setTimeout(fn, ms); } catch (e) { fn(); } };
  if (!well || !well.classList) return false;
  if (still) return true;   // the photo is simply there - the swap already happened
  cue('shutter', 0.22);
  try {
    well.classList.remove('is-flashing', 'is-developing');
    void well.offsetWidth;                    // one forced layout, so the class re-fires
    well.classList.add('is-flashing');
  } catch (e) { /* noop */ }
  at(120, () => { try { well.classList.add('is-developing'); } catch (e) { /* noop */ } });
  at(900, () => { try { well.classList.remove('is-flashing', 'is-developing'); } catch (e) { /* noop */ } });
  return true;
}

/* ----------------------------------------------------------------------------
 * THE BARCODE (the back). Seeded off the student number, so it is the same bars
 * every night - and it is the one innocent seed on the card: the digits under it
 * spell the number back at you, a little too long.
 * -------------------------------------------------------------------------- */
function paintBarcode(box, seed) {
  if (!box) return;
  try { box.textContent = ''; } catch (e) { return; }
  let h = fnv1a(String(seed));
  for (let i = 0; i < 46; i++) {
    h = (Math.imul(h, 1103515245) + 12345) >>> 0;
    const bar = el('i');
    css(bar, 'width', (1 + ((h >>> 28) % 3)) + 'px');
    css(bar, 'margin-right', (1 + ((h >>> 20) % 2)) + 'px');
    box.appendChild(bar);
  }
}

/* ----------------------------------------------------------------------------
 * THE SPOTLIGHT
 * -------------------------------------------------------------------------- */

/** Milestones the streak tile ghosts when you are one short (ALMOST, honest). */
const MILESTONES = [7, 14, 30];

/** Homeroom is a ROOM NUMBER, not a display string (shell/room.js: 101). */
const HOMEROOM_NO = '101';

/**
 * Build the ID spotlight. Nothing is on screen until `open()`.
 *
 * @param {Object} o
 * @param {Function} o.t              lexicon reader
 * @param {Function=} o.reducedMotion () -> boolean. Defaults to the page's own signals.
 * @param {boolean=} o.lite           init.performanceMode (the lit-down room)
 * @param {Function=} o.isMobile      () -> boolean
 * @param {Function} o.profile        () -> {name, avatarUrl, discordLinked, presenceShare,
 *                                            selfId, web}
 * @param {Function} o.stats          () -> {streak, perfect, stamps, stampCap, sDays,
 *                                            termRoman, tier, enrolled, mastered, cards}
 * @param {Function=} o.frame         () -> 'gold' | 'navy' | '' - a frame bought at
 *                                    the Prize Counter, worn as a class on the card
 * @param {Function=} o.onChip        the ONE chip verb (the shell posts the frame)
 * @param {Function=} o.onClose       fired on EVERY path out (Esc, the veil, the
 *                                    close button, Open Records, a teardown) -
 *                                    the shell's EMI bracket hangs off it, and a
 *                                    bracket with one exit it does not know about
 *                                    is a mascot that never comes back
 * @param {Function=} o.onRecords     "Open Records" on the back
 * @param {Function=} o.onOpenCount   () -> how many opens INCLUDING this one (the shell
 *                                    owns the `idOpens` meta key; the full cue sheet
 *                                    plays only while that is under 3)
 * @param {Function=} o.sfx           (name, level, extra) - shell/audio.js's document cue
 * @param {Function=} o.log
 * @returns {{open, dismiss, isOpen, setProfile, setChipState, photoDay, root}}
 */
export function createIdSpotlight({ t, reducedMotion, lite, isMobile, profile, stats,
  frame, onChip, onRecords, onClose, onOpenCount, sfx, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const tr = typeof t === 'function' ? t : (k, fb) => fb;
  const still = typeof reducedMotion === 'function' ? reducedMotion : idReducedMotion;
  const phone = typeof isMobile === 'function' ? isMobile : () => false;
  const readProfile = typeof profile === 'function' ? profile : () => ({});
  const readStats = typeof stats === 'function' ? stats : () => ({});
  const cue = typeof sfx === 'function' ? sfx : () => {};
  const countOpen = typeof onOpenCount === 'function' ? onOpenCount : () => 1;

  /** The live spotlight, or null. {el, timers:[], from, rm, refs} */
  let spot = null;

  function schedule(ms, fn) {
    if (!spot) return 0;
    const box = spot.el;
    try {
      const id = setTimeout(() => { if (spot && spot.el === box) fn(); }, Math.max(0, ms | 0));
      spot.timers.push(id);
      return id;
    } catch (e) { return 0; }
  }

  /* ------------------------------- dismiss ----------------------------- */

  /**
   * Put the card back. Returns true when one was up (the Esc rung's answer).
   * `silent` skips the exit fade and the cue - a teardown has already taken the
   * ground out from under it. Every queued cue dies here, so a skipped
   * entrance lands on the SETTLED end state and never fires a beat afterwards.
   */
  function dismiss(silent) {
    if (!spot) return false;
    const s = spot;
    spot = null;
    for (const id of s.timers) { try { clearTimeout(id); } catch (e) { /* noop */ } }
    if (s.raf) { try { cancelAnimationFrame(s.raf); } catch (e) { /* noop */ } }
    const drop = () => { try { if (s.el && s.el.remove) s.el.remove(); } catch (e) { /* noop */ } };
    if (silent || s.rm || !(s.el && s.el.classList)) {
      drop();
    } else {
      cue('whoosh', 0.1, { pitch: 0.8 });
      try { s.el.classList.add('is-closing'); } catch (e) { /* noop */ }
      let scheduled = false;
      try { setTimeout(drop, 200); scheduled = true; } catch (e) { /* noop */ }
      if (!scheduled) drop();
    }
    // Focus goes back to the furniture card that opened it.
    if (!silent && s.from) { try { s.from.focus(); } catch (e) { /* noop */ } }
    // EVERY path out is this function, which is what makes the shell's EMI
    // bracket safe: she comes back on the same call that takes the card away.
    try { if (typeof onClose === 'function') onClose(); }
    catch (e) { say('id spotlight onClose threw: ' + ((e && e.message) || e)); }
    return true;
  }

  function isOpen() { return !!spot; }

  /* -------------------------------- paint ------------------------------ */

  function nameFor(p) { return (p && p.name) ? String(p.name) : tr('student', 'Student'); }

  /** The frames the counter stocks. An unknown word paints nothing rather than
   *  a broken class, so a catalog that grows a third frame tomorrow cannot make
   *  the card render as a mystery. */
  const FRAMES = { gold: 'is-frame-gold', navy: 'is-frame-navy' };

  /** Put the bought frame on the card, or take a sold-back one off. The node
   *  arrives by hand at build time (`spot` does not exist yet) and is read off
   *  the live refs on every repaint after that. */
  function paintFrame(node) {
    const big = node || (spot && spot.refs ? spot.refs.big : null);
    if (!big || !big.classList) return;
    let want = '';
    try { want = String((typeof frame === 'function' ? frame() : '') || ''); }
    catch (e) { want = ''; }
    for (const key of Object.keys(FRAMES)) {
      try { big.classList.toggle(FRAMES[key], key === want); } catch (e) { /* noop */ }
    }
  }

  /** Repaint the parts a `profile` frame or a `setting` echo can move. */
  function setProfile() {
    if (!spot) return;
    const p = readProfile() || {};
    const r = spot.refs;
    const real = hasPhoto(p);
    try { if (r.img) r.img.src = portraitSrc(p); } catch (e) { /* noop */ }
    try { if (r.photo) r.photo.classList.toggle('is-real', real); } catch (e) { /* noop */ }
    attr(r.photo, 'aria-label', portraitLabel(tr, p));
    const nm = nameFor(p);
    if (r.name) r.name.textContent = nm;
    if (r.sig) r.sig.textContent = nm;
    const rung = chipRung(p);
    paintChip(r.chip, tr, rung, false);
    if (r.hint) r.hint.textContent = chipHint(tr, rung, !!p.web);
    const num = studentNumber(p.selfId, (readStats() || {}).enrolled, p.name);
    if (r.no) r.no.textContent = num.no;
    if (r.noTemp) r.noTemp.hidden = !num.temp;
    if (r.foilNo) r.foilNo.textContent = num.no;
    paintBarcode(r.barcode, num.no);
    if (r.barcodeNo) r.barcodeNo.textContent = num.no.replace('-', '') + '00';
    /* A frame bought while the card is up lands on the SAME repaint a profile
     * echo takes, so the shell never needs a second verb for it. */
    paintFrame();
  }

  /** The shell hands the chip its in-flight looks (`wait` / `pending`). */
  function setChipState(state) {
    if (!spot) return;
    const p = readProfile() || {};
    paintChip(spot.refs.chip, tr, state, false);
    if (spot.refs.hint) {
      spot.refs.hint.textContent = chipHint(tr, state === 'wait' || state === 'pending' ? chipRung(p) : state, !!p.web);
    }
  }

  /** PHOTO DAY, on the big well. Also re-inks the YEAR stamp (a new photo is a
   *  new card, and the stamp is what says a card is current). */
  function photoDay() {
    if (!spot) return false;
    const rm = spot.rm;
    runPhotoDay(spot.refs.photo, cue, rm, schedule);
    const stamp = spot.refs.stamp;
    if (stamp && stamp.classList && !rm) {
      schedule(450, () => {
        try {
          stamp.classList.remove('is-thud');
          void stamp.offsetWidth;
          stamp.classList.add('is-thud');
        } catch (e) { /* noop */ }
        try { thud(1.15); } catch (e) { /* noop */ }
      });
    }
    return true;
  }

  /* --------------------------------- open ------------------------------ */

  /**
   * Lift the card to the middle of the screen under a key light.
   *   0ms    veil + lamp, the card sweeps in                    ('whoosh')
   *   ~400ms the YEAR stamp lands                               (the thud)
   *   ~700ms the marquee glint crosses the name                 ('slide')
   *   ~750ms the six tiles land 90ms apart, counting up   ('pop' + 1 semitone)
   * Reduced motion mounts the same dialog with one fade, tiles pre-filled and
   * no cues at all. After the third open the ladder is dropped and the tiles
   * arrive already filled - repetition shrinks the party.
   */
  function open(fromEl) {
    dismiss(true);
    const rm = !!still();
    const p = readProfile() || {};
    const s = readStats() || {};
    const opens = (function () { try { return countOpen() | 0; } catch (e) { return 99; } })();
    const compact = opens > 3;
    const real = hasPhoto(p);
    const num = studentNumber(p.selfId, s.enrolled, p.name);
    const nm = nameFor(p);
    const tier = Math.max(1, Math.min(4, Math.round(Number(s.tier) || 1)));
    const term = String(s.termRoman || 'I');

    const box = el('div', 'arc-id' + (rm ? ' arc-id-reduced' : '') + (lite ? ' is-lite' : ''));
    attr(box, 'role', 'dialog');
    attr(box, 'aria-modal', 'true');
    attr(box, 'aria-label', tr('student_id_title', 'Student ID'));

    const veil = el('div', 'arc-id-veil');
    attr(veil, 'aria-hidden', 'true');
    veil.addEventListener('click', () => dismiss());
    box.appendChild(veil);

    const stage = el('div', 'arc-id-stage');
    box.appendChild(stage);

    const lamp = el('div', 'arc-id-lamp');
    attr(lamp, 'aria-hidden', 'true');
    stage.appendChild(lamp);

    const row = el('div', 'arc-id-row');
    stage.appendChild(row);

    /* ------------------------------ the card --------------------------- */
    const wrap = el('div', 'arc-id-cardwrap');
    const big = el('div', 'arc-id-big');
    attr(big, 'title', tr('id_flip', 'Tap the card to turn it over. Esc to put it back.'));
    wrap.appendChild(big);
    row.appendChild(wrap);

    const front = el('div', 'arc-id-face is-front');
    const back = el('div', 'arc-id-face is-back');
    big.appendChild(front);
    big.appendChild(back);

    const refs = {};
    /* THE FRAME the counter sells. A skin on the card the player already has,
     * never a second card - trap 93's rule is that the `.campus-idcard` node is
     * never replaced, and the same reasoning holds one rung in: the frame is a
     * class on `.arc-id-big`, so every ref, every listener and the flip all
     * survive a purchase that settles while the card is open. */
    refs.big = big;
    paintFrame(big);

    function band(host, kindText) {
      const b = el('div', 'arc-id-band');
      const crest = el('span', 'arc-id-crest');
      const mark = crestGlyph('arc-id-crestmark');
      if (mark) crest.appendChild(mark);
      crest.appendChild(el('span', null, tr('arcademy', 'The Arcademy').toUpperCase()));
      b.appendChild(crest);
      b.appendChild(el('span', 'arc-id-kind', kindText));
      host.appendChild(b);
    }

    function foil(host, markText, numText) {
      const f = el('div', 'arc-id-foil');
      attr(f, 'aria-hidden', 'true');
      f.appendChild(el('i', 'arc-id-rainbow'));
      f.appendChild(el('i', 'arc-id-grid'));
      f.appendChild(el('span', 'arc-id-foilmark', markText));
      if (numText != null) {
        const n = el('span', 'arc-id-foilno', numText);
        f.appendChild(n);
        refs.foilNo = n;
      }
      host.appendChild(f);
    }

    /* FRONT */
    const lam = el('i', 'arc-id-lam');
    attr(lam, 'aria-hidden', 'true');
    front.appendChild(lam);
    band(front, tr('student_id_title', 'Student ID').toUpperCase());

    const body = el('div', 'arc-id-body');
    front.appendChild(body);

    const well = el('div', 'arc-id-well');
    body.appendChild(well);

    const photo = el('div', 'arc-id-photo' + (real ? ' is-real' : ''));
    const img = el('img', 'arc-id-photo-img');
    attr(img, 'alt', '');
    attr(img, 'decoding', 'async');
    img.addEventListener('error', () => {
      /* A portrait that will not decode falls back to the drawn one - one swap,
       * no retry storm, and never a second network attempt (ghosts.js's rule). */
      try {
        const fb = genericPortrait((readProfile() || {}).presenceShare);
        if (img.src !== fb) { img.src = fb; photo.classList.remove('is-real'); }
      } catch (e) { /* noop */ }
    });
    img.src = portraitSrc(p);
    photo.appendChild(img);
    const flash = el('i', 'arc-id-flash');
    attr(flash, 'aria-hidden', 'true');
    photo.appendChild(flash);
    attr(photo, 'aria-label', portraitLabel(tr, p));
    well.appendChild(photo);
    refs.photo = photo;
    refs.img = img;

    const chip = el('button', 'arc-id-chip id-chip');
    chip.type = 'button';
    chip.addEventListener('click', (ev) => {
      try { ev.stopPropagation(); } catch (e) { /* noop */ }
      cue('pop', 0.1);
      try { if (typeof onChip === 'function') onChip(); } catch (e) { say('id chip threw: ' + ((e && e.message) || e)); }
    });
    well.appendChild(chip);
    refs.chip = chip;

    const hint = el('div', 'arc-id-hint', chipHint(tr, chipRung(p), !!p.web));
    well.appendChild(hint);
    refs.hint = hint;

    const meta = el('div', 'arc-id-meta');
    body.appendChild(meta);

    const nameEl = el('h3', 'arc-id-name');
    nameEl.appendChild(el('span', 'arc-id-nametext', nm));
    const glint = el('i', 'arc-id-glint');
    attr(glint, 'aria-hidden', 'true');
    nameEl.appendChild(glint);
    meta.appendChild(nameEl);
    refs.name = nameEl.firstChild;

    const noLine = el('div', 'arc-id-num');
    noLine.appendChild(el('span', 'arc-id-numlabel', tr('id_no', 'Student no.').toUpperCase()));
    const noVal = el('i', 'arc-id-numval', num.no);
    noLine.appendChild(noVal);
    const noTemp = el('em', 'arc-id-numtemp', tr('id_no_temp', 'temp'));
    noTemp.hidden = !num.temp;
    noLine.appendChild(noTemp);
    meta.appendChild(noLine);
    refs.no = noVal;
    refs.noTemp = noTemp;

    const rows = el('dl', 'arc-id-rows');
    const rowPair = (k, v) => {
      rows.appendChild(el('dt', null, String(k).toUpperCase()));
      rows.appendChild(el('dd', null, String(v)));
    };
    rowPair(tr('semester', 'Semester'), term);
    rowPair(tr('id_enrolled', 'Enrolled'), s.enrolled || '--');
    rowPair(tr('id_homeroom', 'Homeroom'), HOMEROOM_NO);
    rowPair(tr('id_issued_at', 'Issued at'), tr('id_front_desk', 'Front desk'));
    meta.appendChild(rows);

    const stamp = el('div', 'arc-id-yearstamp');
    stamp.appendChild(el('span', null, tr('id_year', 'Year') + ' ' + tier));
    stamp.appendChild(el('small', null, tr('id_grade_tier', 'Grade tier').toUpperCase()));
    meta.appendChild(stamp);
    refs.stamp = stamp;

    foil(front, tr('arcademy', 'The Arcademy').toUpperCase() + ' ' + term, num.no);

    /* BACK */
    const lam2 = el('i', 'arc-id-lam');
    attr(lam2, 'aria-hidden', 'true');
    back.appendChild(lam2);
    band(back, tr('student_id_title', 'Student ID').toUpperCase());

    const bbody = el('div', 'arc-id-body is-back');
    back.appendChild(bbody);

    const barBox = el('div', 'arc-id-barcode');
    attr(barBox, 'aria-hidden', 'true');
    bbody.appendChild(barBox);
    refs.barcode = barBox;
    const barNo = el('div', 'arc-id-barcodeno', num.no.replace('-', '') + '00');
    bbody.appendChild(barNo);
    refs.barcodeNo = barNo;

    const smallPrint = el('p', 'arc-id-small');
    smallPrint.appendChild(el('b', null, tr('id_back_lost',
      'Lost it? Ask at the front desk. The second one costs you a stamp.')));
    smallPrint.appendChild(document.createElement('br'));
    smallPrint.appendChild(document.createTextNode(
      tr('id_back_valid', 'Good for as long as the lights are on.')));
    bbody.appendChild(smallPrint);

    const mastered = Math.max(0, Math.round(Number(s.mastered) || 0));
    const cards = Math.max(0, Math.round(Number(s.cards) || 0));
    bbody.appendChild(el('div', 'arc-id-punchline',
      tr('id_records_line', 'Records: {n} of {m} cards mastered')
        .replace('{n}', String(mastered)).replace('{m}', String(cards))));

    const recordsBtn = el('button', 'arc-id-recordslink', tr('id_open_records', 'Open Records'));
    recordsBtn.type = 'button';
    recordsBtn.addEventListener('click', (ev) => {
      try { ev.stopPropagation(); } catch (e) { /* noop */ }
      cue('pop', 0.1);
      dismiss(true);
      try { if (typeof onRecords === 'function') onRecords(); } catch (e) { say('id records threw'); }
    });
    bbody.appendChild(recordsBtn);

    const sig = el('div', 'arc-id-sig');
    const sigLine = el('div', 'arc-id-sigline');
    const sigName = el('span', null, nm);
    sigLine.appendChild(sigName);
    sig.appendChild(sigLine);
    bbody.appendChild(sig);
    refs.sig = sigName;

    foil(back, tr('student_id_title', 'Student ID').toUpperCase());

    /* ---------------------------- the stat sheet ----------------------- */
    const sheet = el('div', 'arc-id-sheet');
    row.appendChild(sheet);

    const streak = Math.max(0, Math.round(Number(s.streak) || 0));
    const next = MILESTONES.find((m) => m > streak);
    const toGo = (next && next - streak <= 3 && streak > 0) ? next - streak : 0;

    const tiles = [];
    function tile(idx, cls, value, label, almost) {
      const box2 = el('div', 'arc-id-tile' + (cls ? ' ' + cls : ''));
      css(box2, '--i', String(idx));
      const b = el('b', null, '0');
      if (typeof value === 'number') {
        try { b.dataset.count = String(value); } catch (e) { /* noop */ }
        tiles.push(b);
      } else {
        b.textContent = '';
        b.appendChild(el('span', null, String(value.main)));
        if (value.sub) b.appendChild(el('i', null, value.sub));
      }
      box2.appendChild(b);
      box2.appendChild(el('span', null, String(label).toUpperCase()));
      if (almost) {
        const a = el('div', 'arc-id-almost');
        a.appendChild(el('span', null, tr('id_to_go', '{n} to go').replace('{n}', String(almost.n)).toUpperCase()));
        a.appendChild(el('b', null, '\u2192 ' + String(almost.at)));
        box2.appendChild(a);
      }
      sheet.appendChild(box2);
      return box2;
    }

    tile(0, '', streak, tr('id_stat_streak', 'Attendance streak'),
      toGo ? { n: toGo, at: next } : null);
    tile(1, 'is-gold', Math.max(0, Math.round(Number(s.perfect) || 0)),
      tr('id_stat_perfect', 'Perfect days'));
    tile(2, 'is-lav', Math.max(0, Math.round(Number(s.stamps) || 0)),
      tr('id_stat_stamps', 'Stamps of 100'));
    tile(3, 'is-gold', Math.max(0, Math.round(Number(s.sDays) || 0)),
      tr('id_stat_best', 'S days'));
    tile(4, 'is-text', { main: term, sub: '\u00b7 ' + tr('id_year', 'Year') + ' ' + tier },
      tr('id_stat_semester', 'Term'));
    tile(5, 'is-text', { main: s.enrolled || '--' }, tr('id_enrolled', 'Enrolled'));

    /* ------------------------------ placard ---------------------------- */
    const placard = el('div', 'arc-id-placard');
    placard.appendChild(el('h2', null, tr('student_id_title', 'Student ID')));
    placard.appendChild(el('p', null,
      tr('id_flip', 'Tap the card to turn it over. Esc to put it back.').toUpperCase()));
    stage.appendChild(placard);

    const close = el('button', 'arc-id-close', '✕');
    close.type = 'button';
    attr(close, 'aria-label', tr('id_spot_close', 'Close'));
    close.addEventListener('click', () => dismiss());
    box.appendChild(close);
    refs.close = close;

    /* ------------------------------- the flip -------------------------- */
    function flip() {
      if (!spot) return;
      const on = !big.classList.contains('is-back');
      try {
        if (!spot.rm) {
          big.classList.add('is-flipping');
          schedule(660, () => { try { big.classList.remove('is-flipping'); } catch (e) { /* noop */ } });
        }
        css(big, '--rx', '0deg');
        css(big, '--ry', '0deg');
        big.classList.toggle('is-back', on);
      } catch (e) { /* noop */ }
      cue('slide', 0.12, { pitch: on ? 0.9 : 1.1 });
    }
    big.addEventListener('click', (ev) => {
      try {
        const target = ev && ev.target;
        if (target && typeof target.closest === 'function'
          && target.closest('.id-chip, .arc-id-recordslink')) return;
      } catch (e) { /* noop */ }
      flip();
    });

    /* --------------------------- THE DRIFT (tilt) ---------------------- */
    /* One pointermove on the card, rAF-throttled, writing THREE custom
     * properties and nothing else: the laminate specular and the foil hue both
     * hang off them, so the whole effect is one transform per frame (trap 36).
     * Touch has no pointer to follow, so it keeps the foil's own slow drift. */
    if (!rm && !phone()) {
      const onMove = (ev) => {
        if (!spot || big.classList.contains('is-back')) return;
        let r = null;
        try { r = big.getBoundingClientRect(); } catch (e) { return; }
        if (!r || !(r.width > 0)) return;
        const px = (ev.clientX - r.left) / r.width;
        const py = (ev.clientY - r.top) / r.height;
        if (spot.raf) return;
        try {
          spot.raf = requestAnimationFrame(() => {
            if (!spot) return;
            spot.raf = 0;
            css(big, '--ry', ((px - 0.5) * 12).toFixed(2) + 'deg');
            css(big, '--rx', ((0.5 - py) * 10).toFixed(2) + 'deg');
            css(big, '--mx', (px * 100).toFixed(1) + '%');
            css(big, '--my', (py * 100).toFixed(1) + '%');
          });
        } catch (e) { /* no rAF: the card simply does not tilt */ }
      };
      big.addEventListener('pointermove', onMove);
      big.addEventListener('pointerleave', () => {
        css(big, '--rx', '0deg');
        css(big, '--ry', '0deg');
      });
    }

    /* ------------------------------ the trap --------------------------- */
    /* Tab cycles inside the dialog and never leaves it. Esc is NOT bound here:
     * boot.js owns the key and shell.js's escapeStep asks dismiss() first
     * (trap 48's shape - never a second key ladder). */
    box.addEventListener('keydown', (ev) => {
      if (!ev || ev.key !== 'Tab') return;
      let list = [];
      try { list = Array.prototype.slice.call(box.querySelectorAll('button:not([disabled])')); }
      catch (e) { list = [close]; }
      if (!list.length) list = [close];
      try { ev.preventDefault(); } catch (e) { /* noop */ }
      let i = list.indexOf(document.activeElement);
      if (i < 0) i = 0;
      else i = ev.shiftKey ? (i - 1 + list.length) % list.length : (i + 1) % list.length;
      try { list[i].focus(); } catch (e) { /* noop */ }
    });

    document.body.appendChild(box);
    spot = { el: box, timers: [], from: fromEl || null, rm, refs, raf: 0 };
    paintBarcode(barBox, num.no);
    paintChip(chip, tr, chipRung(p), false);
    try { close.focus(); } catch (e) { /* noop */ }
    if (rm && typeof requestAnimationFrame === 'function') {
      /* THE ONE FADE. `.arc-id-reduced` owns it on the ROOT (every descendant
       * is animation:none), so the beat is a single 120ms arrival. */
      try { requestAnimationFrame(() => { if (spot && spot.el === box) box.classList.add('is-in'); }); }
      catch (e) { /* noop */ }
    }

    /* ------------------------------ the cues --------------------------- */
    /* Reduced motion plays NONE of them and the tiles carry their truth at
     * mount; the compact cut (open 4 and up) keeps the entrance and drops the
     * ladder. Both land on exactly the same finished sheet. */
    if (rm || compact) {
      for (const b of tiles) { try { b.textContent = b.dataset.count; } catch (e) { /* noop */ } }
    }
    if (!rm) {
      cue('whoosh', 0.22);
      if (!compact) {
        schedule(400, () => { try { thud(1); } catch (e) { /* noop */ } });
        schedule(700, () => cue('slide', 0.14));
        tiles.forEach((b, i) => {
          const target = Number(b.dataset.count) || 0;
          schedule(750 + i * 90, () => {
            cue('pop', 0.12, { pitch: 0.85 + Math.min(6, i) * 0.06 });
            countUp(b, target, 420, box);
          });
        });
      }
    }
    return box;
  }

  /** THE BANK, lite: the number ticks up. No particles - a document does not
   *  pay out. Stops dead the moment the spotlight it belongs to is gone. */
  function countUp(node, target, ms, box) {
    if (typeof requestAnimationFrame !== 'function' || typeof performance === 'undefined') {
      try { node.textContent = String(target); } catch (e) { /* noop */ }
      return;
    }
    const t0 = performance.now();
    const step = (now) => {
      if (!spot || spot.el !== box) return;
      const prog = Math.min(1, (now - t0) / ms);
      const eased = 1 - Math.pow(1 - prog, 3);
      try { node.textContent = String(Math.round(target * eased)); } catch (e) { return; }
      if (prog < 1) { try { requestAnimationFrame(step); } catch (e) { /* noop */ } }
    };
    try { requestAnimationFrame(step); } catch (e) { node.textContent = String(target); }
  }

  return {
    open,
    dismiss: (silent) => dismiss(!!silent),
    isOpen,
    setProfile,
    setChipState,
    photoDay,
    /** The live dialog node, or null. Test seam. */
    get root() { return spot ? spot.el : null; },
    destroy() { dismiss(true); },
  };
}

export default createIdSpotlight;
