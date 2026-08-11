/* ============================================================================
 * ui/avatar.js — the avatar BUBBLE, the VS splash, and the `gg-ava` bus.
 *
 * Work Item D owns the DOM; ui/avatarFx.js (Work Item E) owns what it does when
 * something happens to it. The seam between them is frozen in
 * docs/GOON_DISCORD_CONTRACT.md §6 and is repeated here as literals ON PURPOSE:
 * this module must not import E, because the page has to run with E absent.
 *
 *   .gg-ava[data-side="you"|"opp"]     the wrapper — E's idle loop lives here
 *     > .gg-ava-img                    an <img>, when a picture was shared
 *     | .gg-ava-tile                   the initial-letter fallback, otherwise
 *
 * THE BOX IS RESERVED FROM FIRST PAINT. Every bubble is built at its final size
 * with the tile already in it, and the picture — which arrives one bridge round
 * trip later, if at all — REPLACES the tile inside the same box. Nothing on the
 * lobby, the HUD or the recap may move when a data URI lands, because "the card
 * jumped as I was clicking it" is the bug that shape prevents.
 *
 * THE HUE IS DERIVED, NOT STORED. A tile's colour is a hash of the display name
 * folded to 0-359, at a FIXED saturation and lightness picked to sit inside the
 * page's pink/violet neon rather than fight it. Two players called the same
 * thing get the same colour on both machines with nothing on the wire, and no
 * name can ever produce a muddy or invisible tile.
 *
 * NOTHING HERE KNOWS WHAT A SNOWFLAKE IS. It takes a name and maybe a data URI.
 *
 * Import-safe under node: every document touch is inside a function.
 * ==========================================================================*/

/** The event D emits and E consumes. Frozen by the contract (§6). */
export const AVA_EVENT = 'gg-ava';

/** The contract's kinds, in contract order. Anything else is dropped by emit. */
export const AVA_KINDS = Object.freeze([
  'land', 'fire', 'drop', 'pop', 'emote', 'mercy', 'win', 'lose', 'draw', 'cue',
]);

export const AVA_SIDES = Object.freeze(['you', 'opp']);

/** Fixed tile chroma. Only the HUE moves; these two keep every name legible. */
export const TILE_SAT = 58;
export const TILE_LIGHT = 46;

/** How long the VS splash owns the screen, start to gone. */
export const VS_TOTAL_MS = 1600;
/** When inside that it starts flying to the HUD anchors. */
export const VS_FLY_AT_MS = 1120;

const doc = () => (typeof document !== 'undefined' ? document : null);

/**
 * FNV-1a over the name, folded to a hue. Deterministic across machines and
 * across reloads — it is the only identity a tile has.
 * @returns {number} 0..359
 */
export function hueFromName(name) {
  const s = String(name == null ? '' : name);
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i) & 0xff;
    // >>> 0 after every step: JS bit ops are signed and the multiply overflows.
    h = (h + ((h << 1) + (h << 4) + (h << 7) + (h << 8) + (h << 24))) >>> 0;
  }
  return h % 360;
}

/** The one glyph a tile shows. Upper-cased, never more than one code point. */
export function initialOf(name) {
  const s = String(name == null ? '' : name).trim();
  if (!s) return '?';
  // Array.from, not [0]: a name starting with an emoji or an astral CJK glyph
  // would otherwise render half a surrogate pair.
  const first = Array.from(s)[0] || '?';
  try { return first.toUpperCase(); } catch (_e) { return first; }
}

/** `hsl(...)` for a name — exported so the self-test can prove it is stable. */
export function tileColorFor(name) {
  return 'hsl(' + hueFromName(name) + ', ' + TILE_SAT + '%, ' + TILE_LIGHT + '%)';
}

function mk(tag, cls) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const n = d.createElement(tag);
  if (cls) n.className = cls;
  return n;
}

/**
 * One avatar bubble.
 *
 * @param {object} o
 * @param {'you'|'opp'} [o.side]
 * @param {string} [o.name]        display name — the tile letter and the hue
 * @param {string|null} [o.dataUri] a `data:image/...` the host handed us
 * @param {string} [o.size]        a size token -> `data-size` (lobby|mini|splash|plate|last)
 * @param {string} [o.title]       tooltip / aria-label override
 * @returns {HTMLElement|null} the `.gg-ava`, with `setPicture(uri)` on it
 */
export function avatarNode({ side = 'you', name = '', dataUri = null, size = 'lobby', title = null } = {}) {
  const node = mk('div', 'gg-ava');
  if (!node) return null;
  const s = side === 'opp' ? 'opp' : 'you';
  try {
    node.setAttribute('data-side', s);
    node.setAttribute('data-size', String(size || 'lobby'));
    node.setAttribute('role', 'img');
    node.setAttribute('aria-label', String(title || name || (s === 'opp' ? 'opponent' : 'you')));
  } catch (_e) { /* stub DOM */ }

  /** Swap the contents in place — the BOX never changes size (see the header). */
  function setPicture(uri) {
    const useImg = typeof uri === 'string' && uri.slice(0, 5) === 'data:';
    const child = useImg ? mk('img', 'gg-ava-img') : mk('span', 'gg-ava-tile');
    if (!child) return;
    if (useImg) {
      try {
        child.src = uri;
        child.alt = '';
        child.setAttribute('draggable', 'false');
      } catch (_e) { /* stub DOM */ }
    } else {
      child.textContent = initialOf(name);
      try { child.style.setProperty('--gg-ava-hue', String(hueFromName(name))); } catch (_e) { /* stub DOM */ }
    }
    try { node.replaceChildren(child); } catch (_e) { try { node.appendChild(child); } catch (_e2) { /* ignore */ } }
  }

  // The tile goes in NOW, unconditionally, even when a picture is on its way:
  // that is what reserves the box (and what an unlinked player keeps forever).
  setPicture(dataUri);
  try { node.setPicture = setPicture; } catch (_e) { /* frozen stub */ }
  return node;
}

/**
 * A bubble with its name under it — the lobby, the splash and the recap plates
 * all want the pair, and the pair has to be ONE node so a caller cannot put the
 * name somewhere the FX bus would treat as part of the bubble.
 * @returns {{node:HTMLElement|null, ava:HTMLElement|null, setName:Function, setPicture:Function}}
 */
export function avatarSlot(opts = {}) {
  const o = opts || {};
  const ava = avatarNode(o);
  const wrap = mk('div', 'gg-ava-slot');
  const label = mk('span', 'gg-ava-name');
  if (!wrap || !ava) return { node: wrap || null, ava, setName() {}, setPicture() {} };
  if (label) label.textContent = String(o.name || '');
  try {
    wrap.setAttribute('data-side', o.side === 'opp' ? 'opp' : 'you');
    wrap.appendChild(ava);
    if (label && o.withName !== false) wrap.appendChild(label);
  } catch (_e) { /* stub DOM */ }
  return {
    node: wrap,
    ava,
    setName(v) { if (label) label.textContent = String(v || ''); },
    setPicture(uri) { try { ava.setPicture(uri); } catch (_e) { /* ignore */ } },
  };
}

/* ----------------------------------------------------------------------------
 * THE FX BUS.
 *
 * Every dispatch site on the page goes through this ONE function, and it can
 * not throw: a missing detail, a stub DOM, a page with no CustomEvent and an
 * unknown kind all return false instead. A decoration bus that can take the
 * duel down would be a worse trade than having no decoration at all.
 * -------------------------------------------------------------------------- */
export function emitAva(kind, side, meta) {
  try {
    const d = doc();
    if (!d || typeof d.dispatchEvent !== 'function') return false;
    if (typeof CustomEvent !== 'function') return false;
    const k = String(kind || '');
    if (AVA_KINDS.indexOf(k) < 0) return false;
    const detail = { kind: k, side: side === 'opp' ? 'opp' : 'you' };
    if (meta && typeof meta === 'object') detail.meta = meta;
    d.dispatchEvent(new CustomEvent(AVA_EVENT, { detail }));
    return true;
  } catch (_e) { return false; }
}

/* ----------------------------------------------------------------------------
 * THE VS SPLASH — two faces, big, for a second and a half, then gone.
 *
 * IT IS DECORATION AND IT BEHAVES LIKE IT:
 *   * it NEVER delays the countdown. It is mounted alongside the countdown
 *     screen and removed at the Live arm at the latest, whatever it is doing;
 *   * pointer-events: none, so it cannot eat the click that was on its way to
 *     something underneath;
 *   * z55 — over the HUD, UNDER MERCY (z60). Nothing here may ever change that;
 *   * reduced motion skips it ENTIRELY (returns null) rather than showing a
 *     static card for 1.6s that the player cannot dismiss.
 *
 * The fly-out reads the live HUD minis for its target, so the bubbles land
 * where the desk actually put them; with no HUD up yet it falls back to the
 * corners the minis live in, which is close enough for a 300ms transform.
 * -------------------------------------------------------------------------- */

/** Where a mini for `side` is, or the corner it would be in. */
function anchorFor(side) {
  const d = doc();
  const fallback = side === 'opp'
    ? { x: (typeof window !== 'undefined' && window.innerWidth ? window.innerWidth : 1280) - 120, y: 150 }
    : { x: 90, y: 90 };
  if (!d || typeof d.querySelector !== 'function') return fallback;
  try {
    const mini = d.querySelector('.gg-ava--mini[data-side="' + side + '"]');
    if (!mini || typeof mini.getBoundingClientRect !== 'function') return fallback;
    const r = mini.getBoundingClientRect();
    if (!r || (!r.width && !r.height)) return fallback;
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  } catch (_e) { return fallback; }
}

/**
 * @param {object} o
 * @param {{name:string, dataUri:string|null}} o.you
 * @param {{name:string, dataUri:string|null}} o.opp
 * @param {boolean} [o.reduced]      reduced motion -> no splash at all
 * @param {boolean} [o.showOpponent] viewer pref: hide their bubble, keep the tile
 * @param {HTMLElement} [o.host]     defaults to <body>
 * @param {string} [o.vsLabel]
 * @returns {{remove:Function, node:HTMLElement}|null}
 */
export function mountVsSplash({ you = null, opp = null, reduced = false, showOpponent = true,
  host = null, vsLabel = 'VS', totalMs = VS_TOTAL_MS } = {}) {
  if (reduced) return null;
  const d = doc();
  if (!d) return null;
  const parent = host || d.body;
  if (!parent || typeof parent.appendChild !== 'function') return null;

  const root = mk('div', 'gg-vs-splash');
  if (!root) return null;
  try { root.setAttribute('aria-hidden', 'true'); } catch (_e) { /* stub DOM */ }

  const mine = avatarSlot({ side: 'you', name: (you && you.name) || '', dataUri: (you && you.dataUri) || null, size: 'splash' });
  const theirs = avatarSlot({
    side: 'opp',
    name: (opp && opp.name) || '',
    // The viewer pref suppresses their PICTURE, never their presence: an
    // opponent with no bubble at all would read as "nobody is there".
    dataUri: showOpponent ? ((opp && opp.dataUri) || null) : null,
    size: 'splash',
  });
  const vs = mk('span', 'gg-vs-splash-vs');
  if (vs) vs.textContent = String(vsLabel || 'VS');

  try {
    if (mine.node) root.appendChild(mine.node);
    if (vs) root.appendChild(vs);
    if (theirs.node) root.appendChild(theirs.node);
    parent.appendChild(root);
  } catch (_e) { return null; }

  let gone = false;
  const timers = [];
  const later = (fn, ms) => {
    try { timers.push(setTimeout(() => { try { fn(); } catch (_e) { /* ignore */ } }, Math.max(0, ms | 0))); }
    catch (_e) { /* no timers: the splash simply stays until remove() */ }
  };

  function remove() {
    if (gone) return;
    gone = true;
    for (const t of timers) { try { clearTimeout(t); } catch (_e) { /* ignore */ } }
    timers.length = 0;
    try { root.remove(); } catch (_e) { /* already gone */ }
  }

  // Entrance on the next frame so the transition has a "from" to start at.
  const arm = () => { try { root.classList.add('is-in'); } catch (_e) { /* ignore */ } };
  if (typeof requestAnimationFrame === 'function') requestAnimationFrame(arm); else arm();

  const flyAt = Math.max(0, Math.min(totalMs - 200, VS_FLY_AT_MS));
  later(() => {
    if (gone) return;
    // Measure the anchors LATE — the HUD mounts at Live, i.e. after this splash
    // was built, so asking at build time would always get the fallback corners.
    for (const [slot, side] of [[mine, 'you'], [theirs, 'opp']]) {
      const node = slot && slot.node;
      if (!node || typeof node.getBoundingClientRect !== 'function') continue;
      try {
        const from = node.getBoundingClientRect();
        const to = anchorFor(side);
        const dx = Math.round(to.x - (from.left + from.width / 2));
        const dy = Math.round(to.y - (from.top + from.height / 2));
        node.style.setProperty('--gg-vs-dx', dx + 'px');
        node.style.setProperty('--gg-vs-dy', dy + 'px');
      } catch (_e) { /* a stub DOM just fades instead of flying */ }
    }
    try { root.classList.add('is-fly'); } catch (_e) { /* ignore */ }
  }, flyAt);

  later(remove, totalMs);

  return { node: root, remove };
}

export default { avatarNode, avatarSlot, emitAva, mountVsSplash, hueFromName, initialOf };
