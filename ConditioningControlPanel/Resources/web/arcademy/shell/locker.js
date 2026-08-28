/* ============================================================================
 * shell/locker.js - THE LOCKER, RM 004.
 *
 * The Prize Counter sells. This is where the things go afterwards.
 *
 * Every wave since the economy landed has bolted its own switch onto whatever
 * screen happened to be nearest: the campus look ended up as a fold in Options,
 * the ID frame was decided by a two-line ladder nobody could see, EMI's outfits
 * had a setter with ZERO call sites in the whole school, and the desk toy could
 * only ever be whatever the day seed said it was. Four wardrobes in four
 * buildings. This is the one room they all move into.
 *
 * WHAT IS IN HERE, in the order the doors open:
 *   WEAR        EMI's outfit. `lockerOutfit`, and the widget's `setOutfit` is
 *               the one road it takes.
 *   CARD        the frame on the Student ID. `lockerFrame`.
 *   CAMPUS      the campus look. NOT a key of ours - `campusTheme` is the
 *               shell's and always was; we are handed the same four narrow caps
 *               the Options sheet used to draw, and Options keeps a signpost.
 *   DESK        which toy sits on EMI's tube. `lockerToy`, and "let the desk
 *               choose" is the absence of a pin, not a fifth toy.
 *   IN YOUR BAG the consumables, counted.
 *   ALWAYS ON   everything owned that has no switch: it is on, it says what it
 *               does, and the only one you can poke is the bell.
 *
 * FOUR LAWS, and every one of them is the counter's law one window down:
 *
 *  1. AN UNOWNED THING IS ABSENT. Not dimmed, not padlocked, not named. The
 *     shelf's rule (prizecounter.js, themes.js's `ownedThemes`) and the same
 *     reason: a restock should APPEAR. The single exception is the footer line,
 *     which says how MANY are still at the counter and never which.
 *  2. A PICK IS ONLY EVER A PICK. The wallet is the ownership witness and it is
 *     the shell's; this file writes three page-owned meta keys and nothing
 *     else. It has never seen a bridge, a balance or an sku price.
 *  3. A PICK WHOSE SKU IS NOT OWNED IS IGNORED, and CLEARED when we render and
 *     find it so. An entitlement can lapse; a key that outlives its sku would
 *     otherwise paint a jacket the player no longer has.
 *  4. THE ABSENCE OF A PICK IS NOT "STANDARD". It is "no pick", which falls
 *     through to whatever the bag did before a picker existed - the varsity
 *     jacket for a jacket owner, the gold frame for a gold owner, tonight's
 *     seeded toy for a toy owner. Explicit pick > bag default > standard, and
 *     "Plain" is an explicit pick with its own stored value for exactly that
 *     reason (see FRAME_PLAIN).
 *
 * THE ROOM IS THE RECORDS OFFICE'S SHAPE. One painted view, one thing in it you
 * can touch, and the whole of the wardrobe folded down into a scene overlay -
 * so the Esc ladder is the office's ladder (panel, then the room, then out) and
 * this file binds no key. It borrows the chassis from shell/scene.js and every
 * answer from the caps the shell hands it.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';
import { createScene } from './scene.js';
/* The counter's two registers, single-sourced for the reason its own header
 * gives: a second table of glyphs or sprites in this file is a table that
 * disagrees with the shelf the week somebody restocks it. */
import { spriteUrl, GLYPHS } from './prizecounter.js';
/* The wardrobe's two tables, single-sourced from the mascot that renders them.
 * Neither is state: `OUTFITS` is which sheets exist as art and `TOYS` is which
 * props do. Importing them is what stops this file from carrying a second list
 * that quietly disagrees the night somebody adds the fifth toy. */
import { OUTFITS, TOYS, toyFrames } from '../emi/widget.js';
import { onDeviceChange } from '../core/device.js';

/* ----------------------------------------------------------------------------
 * THE TABLE
 * -------------------------------------------------------------------------- */

/** The painted set. 1376x768, chained off vn-17 under the house camera law. */
export const PLATE = 'vn-21-locker-room.png';

/** THE OPEN DOOR, measured off the plate: the lit bay with the jacket on the
 *  hanger plus the leaf swung out beside it. It is the only thing in the room
 *  you can press, so it is the only thing that breathes. Clear of the apron
 *  line (y 640) on its own. */
export const HOT_LOCKER = Object.freeze([746, 150, 162, 418]);

/** The three page-owned meta keys. Page-owned needs no C# change - the theme
 *  picker's `campusTheme` is the precedent and this is the same road. */
export const OUTFIT_KEY = 'lockerOutfit';
export const FRAME_KEY = 'lockerFrame';
export const TOY_KEY = 'lockerToy';

/**
 * THE EXPLICIT PLAIN. `lockerFrame` unset means NO PICK, which law 4 sends to
 * the ownership ladder (gold if you own gold, else navy, else nothing). A
 * player who owns a frame and would rather wear none needs a value that is not
 * the absence of one, and this is it. It is never written by anything but a
 * press on the Plain tile.
 */
export const FRAME_PLAIN = 'plain';

/** An outfit is wearable only while its sku is in the wallet. `varsity` keeps
 *  the gate it has had since the restock; the other three got skus of their own
 *  in this wave. */
export const OUTFIT_SKU = Object.freeze({
  varsity: 'emi_varsity',
  labcoat: 'emi_labcoat',
  cheer: 'emi_cheer',
  swim: 'emi_swim',
});

/** ...and the frames. */
export const FRAME_SKU = Object.freeze({ gold: 'id_frame_gold', navy: 'id_frame_navy' });

/** The one sku the whole DESK group hangs off. */
export const TOY_SKU = 'emi_desk_toy';

/**
 * THE PASSIVES, in the order they are listed. Owned = on, no toggle, v1 - so
 * the group is a receipt rather than a control panel, and it exists because a
 * thing you bought that never appears anywhere is a thing you will eventually
 * believe you did not buy.
 *
 * ANY OTHER OWNED ROW LANDS HERE TOO. The nine below are the ordered ones; the
 * sweep in `passiveRows` appends every other owned catalog row that no picker
 * group has claimed, so a restock that ships a tenth passive is listed the same
 * night rather than the night somebody remembers to edit this array. That also
 * keeps the footer's count honest: everything owned is somewhere on this page.
 */
export const PASSIVE_SKUS = Object.freeze([
  'confetti_stamp', 'sparkler_steps', 'brass_bell', 'pa_pack', 'poster_drop_1',
  'away_colors', 'honors_lever', 'free_swim_key', 'de_5x5',
]);

/** The skus the picker groups own, so the passive sweep never lists one twice. */
const CLAIMED = Object.freeze((() => {
  const out = Object.create(null);
  for (const k of Object.keys(OUTFIT_SKU)) out[OUTFIT_SKU[k]] = true;
  for (const k of Object.keys(FRAME_SKU)) out[FRAME_SKU[k]] = true;
  out[TOY_SKU] = true;
  return out;
})());

/** The six groups, in the order the panel draws them. */
export const GROUPS = Object.freeze(['wear', 'card', 'campus', 'desk', 'bag', 'always']);

/* ----------------------------------------------------------------------------
 * PLUMBING
 * -------------------------------------------------------------------------- */

/** Module-relative, never page-relative: the room is mounted from the shell and
 *  a `./art/...` would break the moment anything else mounted it. */
function urlFor(rel, fallback) {
  try { return new URL(rel, import.meta.url).href; }
  catch (e) { return fallback; }
}

/** locker.css, linked once and lazily - recordsroom.js's pattern exactly. */
function ensureSheet(doc, log) {
  try {
    if (!doc || typeof doc.createElement !== 'function') return null;
    const had = typeof doc.getElementById === 'function' ? doc.getElementById('lk-styles') : null;
    if (had) return had;
    const link = doc.createElement('link');
    link.id = 'lk-styles';
    link.rel = 'stylesheet';
    link.href = urlFor('./locker.css', 'shell/locker.css');
    const host = doc.head || doc.documentElement || doc.body;
    if (host) host.appendChild(link);
    return link;
  } catch (e) { if (log) log('locker sheet failed'); return null; }
}

/** ONE AUDIO DOOR (trap 18). Every sound this room makes is a REQUEST on the
 *  document; the room owns no node. Only names already in the SOUNDS table are
 *  ever fired - `bell` included, which is the point of the preview below: the
 *  shell installed the brass COSMETIC GETTER on audio.js at build, so the
 *  school bell rings brass while the prize is owned. The `set_bell` bus message
 *  is never sent from here; that one writes the ownership slot itself. */
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

function fill(text, vars) {
  let out = String(text == null ? '' : text);
  for (const k of Object.keys(vars || {})) out = out.split('{' + k + '}').join(String(vars[k]));
  return out;
}

/** The shell's `<html class="arc-reduced">`, read defensively. */
function htmlReduced() {
  try {
    const de = (typeof document !== 'undefined') ? document.documentElement : null;
    if (de && de.classList && de.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* noop */ }
  return false;
}

/** The landscape phone, off <html>'s two marks. Never decided here. */
function isRailViewport() {
  try {
    const html = document.documentElement;
    return !!(html && html.classList && html.classList.contains('arc-mobile')
      && html.getAttribute('data-arc-orient') === 'landscape');
  } catch (e) { return false; }
}

/** How many of a sku the wallet holds. The counter's own reader, in miniature:
 *  the host writes a row three ways and all three have to read. */
function heldOf(inv, sku) {
  if (!inv || typeof inv !== 'object') return 0;
  const row = inv[sku];
  if (row == null || row === false) return 0;
  if (row === true) return 1;
  if (typeof row === 'number') return Number.isFinite(row) && row > 0 ? Math.floor(row) : 0;
  if (typeof row === 'object') {
    const n = Number(row.n);
    if (Number.isFinite(n)) return n > 0 ? Math.floor(n) : 0;
    return 1;
  }
  return 0;
}

/* ----------------------------------------------------------------------------
 * THE CAPS, NORMALISED
 *
 * Every one of these answers junk rather than throwing, because a wardrobe is
 * not worth a white screen: a shell that hands half a bag (the dev host, a
 * suite's double) still gets a room, and the groups whose answers are missing
 * are simply absent - which is law 1 doing its job for a reason it was not
 * written for.
 * -------------------------------------------------------------------------- */

function norm(caps) {
  const c = caps || {};
  const fn = (v, fb) => (typeof v === 'function' ? v : fb);
  const meta = c.meta && typeof c.meta === 'object' ? c.meta : {};
  return {
    t: fn(c.t, lexT),
    log: fn(c.log, () => {}),
    lite: !!c.lite,
    reduced: !!c.reduced,
    ownsSku: fn(c.ownsSku, () => false),
    catalog: fn(c.catalog, () => []),
    inv: fn(c.inv, () => ({})),
    held: fn(c.held, null),
    settingUnlocks: fn(c.settingUnlocks, () => []),
    metaGet: fn(meta.get, () => undefined),
    metaSet: fn(meta.set, (k, v) => v),
    themes: c.themes && typeof c.themes === 'object' ? c.themes : null,
    emi: fn(c.emi, () => null),
    bellOwned: fn(c.bellOwned, () => false),
    refreshIdCard: fn(c.refreshIdCard, () => {}),
    toast: fn(c.toast, () => {}),
    openCounter: fn(c.openCounter, null),
    onBack: fn(c.onBack, () => {}),
    mount: c.mount || null,
    isMobile: fn(c.isMobile, () => false),
  };
}

/* ----------------------------------------------------------------------------
 * THE PICKS - read, clamp, write. Pure-ish: they touch the caps and nothing
 * else, which is what lets `equipFromToast` reuse every one of them with no
 * room on screen.
 * -------------------------------------------------------------------------- */

/** The armed outfit, clamped to what is owned. Null = NO PICK (law 4). */
export function readOutfit(k) {
  let want = null;
  try { const v = k.metaGet(OUTFIT_KEY); want = typeof v === 'string' && v ? v : null; }
  catch (e) { want = null; }
  if (!want || OUTFITS.indexOf(want) < 0) return null;
  return k.ownsSku(OUTFIT_SKU[want]) === true ? want : null;
}

/** The picked frame: 'gold' | 'navy' | FRAME_PLAIN | null (no pick). */
export function readFrame(k) {
  let want = null;
  try { const v = k.metaGet(FRAME_KEY); want = typeof v === 'string' && v ? v : null; }
  catch (e) { want = null; }
  if (!want) return null;
  if (want === FRAME_PLAIN) return FRAME_PLAIN;
  if (!FRAME_SKU[want]) return null;
  return k.ownsSku(FRAME_SKU[want]) === true ? want : null;
}

/** The pinned toy, or null for the nightly rotation. */
export function readToy(k) {
  if (k.ownsSku(TOY_SKU) !== true) return null;
  let want = null;
  try { const v = k.metaGet(TOY_KEY); want = typeof v === 'string' && v ? v : null; }
  catch (e) { want = null; }
  if (!want) return null;
  for (const toy of TOYS) if (toy && toy.key === want) return want;
  return null;
}

/**
 * LAW 3, ENFORCED WHERE IT CAN BE SEEN. Read each key raw; where the clamp
 * disagrees with what is stored, write the clamp back. Never server-side, never
 * on a timer - only here, where a player is looking at the row it is about to
 * change, and only when it actually differs (an idempotent write is still a
 * bridge message).
 */
function clampPicks(k) {
  const pairs = [[OUTFIT_KEY, readOutfit(k)], [FRAME_KEY, readFrame(k)], [TOY_KEY, readToy(k)]];
  for (const [key, clamped] of pairs) {
    let raw;
    try { raw = k.metaGet(key); } catch (e) { raw = undefined; }
    const had = typeof raw === 'string' && raw ? raw : null;
    if (had !== clamped) {
      try { k.metaSet(key, clamped); }
      catch (e) { k.log('locker: could not clear a stale pick (' + key + ')'); }
    }
  }
}

/* ------------------------------------------------------------- the writers */

/** Wear a sheet (or null for no pick). Answers whether it stuck. */
export function equipOutfit(k, name) {
  const want = (typeof name === 'string' && OUTFITS.indexOf(name) >= 0) ? name : null;
  if (want && k.ownsSku(OUTFIT_SKU[want]) !== true) return false;
  try { k.metaSet(OUTFIT_KEY, want); } catch (e) { k.log('locker: outfit write failed'); return false; }
  /* EMI'S ONE ROAD. The widget validates ownership again through the shell's
   * own getters - it is allowed to refuse, and a refusal is not our business to
   * paper over. It persists nothing; the key above is the persistence. */
  try {
    const emi = k.emi();
    if (emi && typeof emi.setOutfit === 'function') emi.setOutfit(want);
  } catch (e) { k.log('locker: setOutfit threw'); }
  return true;
}

/** Wear a frame ('gold' | 'navy' | FRAME_PLAIN | null). */
export function equipFrame(k, id) {
  const want = typeof id === 'string' && id ? id : null;
  if (want && want !== FRAME_PLAIN) {
    if (!FRAME_SKU[want] || k.ownsSku(FRAME_SKU[want]) !== true) return false;
  }
  try { k.metaSet(FRAME_KEY, want); } catch (e) { k.log('locker: frame write failed'); return false; }
  try { k.refreshIdCard(); } catch (e) { /* the card repaints on its next open anyway */ }
  return true;
}

/** Pin a toy (or null to hand the rotation back). */
export function equipToy(k, name) {
  if (k.ownsSku(TOY_SKU) !== true) return false;
  let want = null;
  if (typeof name === 'string' && name) {
    for (const toy of TOYS) if (toy && toy.key === name) want = name;
    if (!want) return false;
  }
  try { k.metaSet(TOY_KEY, want); } catch (e) { k.log('locker: toy write failed'); return false; }
  /* THE RE-ROLL. `setPrizes` with no argument re-reads the getter bag the shell
   * handed the widget at mount, which is where `toyPin()` lives - so the prop
   * on her tube changes under the player's thumb rather than at the next boot. */
  try {
    const emi = k.emi();
    if (emi && typeof emi.setPrizes === 'function') emi.setPrizes();
  } catch (e) { k.log('locker: toy re-roll threw'); }
  return true;
}

/** Lay a campus look. The shell owns the key and the palette; we call back. */
export function equipTheme(k, id) {
  if (!k.themes || typeof k.themes.select !== 'function') return false;
  try { k.themes.select(id); return true; }
  catch (e) { k.log('locker: theme select threw'); return false; }
}

/* ----------------------------------------------------------------------------
 * THE ART
 * -------------------------------------------------------------------------- */

/** EMI's own body sheet, used as the WEAR tile. `body-idle.png` is the frame
 *  she wears at rest, which is the honest still of a wardrobe. */
function outfitArt(name) {
  return urlFor('../art/emi/' + String(name) + '/body-idle.png',
    'art/emi/' + String(name) + '/body-idle.png');
}

/** The standard sheet, one folder up. */
function standardArt() {
  return urlFor('../art/emi/body-idle.png', 'art/emi/body-idle.png');
}

/** A toy's first frame, module-relative. Falls to null when the table has no
 *  art for it, which is the glyph's cue. */
function toyArt(toy) {
  const frames = toyFrames(toy);
  if (!frames.length) return null;
  const raw = String(frames[0]);
  const base = raw.slice(raw.lastIndexOf('/') + 1);
  if (!base) return null;
  return urlFor('../art/emi/toys/' + base, 'art/emi/toys/' + base);
}

/**
 * ONE SPRITE BOX, and the ART IS ALWAYS OPTIONAL. The glyph (or nothing) is
 * painted first and the image covers it; an `onerror` takes the image off and
 * leaves what was under it standing. That is the counter's rule 1 and it is why
 * a plate the artist has not drawn yet is a tile that looks plain rather than a
 * broken picture - which is exactly the state this room shipped into.
 */
function artBox(src, glyph, cls) {
  const box = el('span', 'lk-art' + (cls ? ' ' + cls : ''));
  attr(box, 'aria-hidden', 'true');
  if (glyph) box.appendChild(el('span', 'lk-glyph', glyph));
  if (src) {
    const img = el('img', 'lk-img');
    img.alt = '';
    img.decoding = 'async';
    img.loading = 'lazy';
    img.addEventListener('error', () => { try { img.remove(); } catch (e) { /* noop */ } });
    try { img.src = src; } catch (e) { /* noop */ }
    box.appendChild(img);
  }
  return box;
}

/* ============================================================================
 * THE ROOM
 * ==========================================================================*/

/**
 * @param {object} caps  see `norm` above. The shell builds it (lockerCaps).
 * @returns {?{root, escapeStep, fit, destroy, openPanel, panelUp, scene, groups}}
 */
export function createLocker(caps) {
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;

  const k = norm(caps);
  const t = k.t;
  const log = k.log;
  const reduced = () => k.reduced || htmlReduced();

  ensureSheet(doc, log);

  let dead = false;
  let closePanel = null;        // the live overlay's own close, or null
  let panel = null;             // the panel node while it is up
  let railOn = false;
  let active = null;            // the lit group id, rail mode only
  let offDevice = null;
  const timers = [];
  const groupNodes = [];        // { id, node, tab }

  function later(fn, ms) {
    const id = setTimeout(() => {
      if (dead) return;
      try { fn(); } catch (e) { log('locker timer threw: ' + ((e && e.message) || e)); }
    }, ms);
    timers.push(id);
    return id;
  }

  /* ------------------------------------------------------------- the scene */

  const scene = createScene({
    mount: k.mount,
    lite: k.lite,
    reduced: k.reduced,
    log,
    t,
    label: t('locker_kicker', 'The Locker'),
    views: {
      wide: {
        art: PLATE,
        hotspots: [
          [HOT_LOCKER[0], HOT_LOCKER[1], HOT_LOCKER[2], HOT_LOCKER[3],
            'open', 'locker_hot', 'Your locker', { main: true }],
        ],
      },
    },
    apron: { back: () => { try { k.onBack(); } catch (e) { log('locker back threw'); } } },
    onAction: (action) => { if (action === 'open') openPanel(); },
  });
  if (!scene) return null;

  try { scene.root.classList.add('lk-room'); } catch (e) { /* noop */ }
  sfx('door', 0.18);

  /* THE ARRIVAL. The panel opens on its own - the room IS the wardrobe and a
   * player who walked the length of a school to reach it did not come to look
   * at a door. The BEAT before it is decoration: on a lite or reduced-motion
   * build the panel is simply there, which is the contract's cut (skip the
   * beat, never the plate). */
  later(openPanel, (reduced() || k.lite) ? 0 : 380);

  /* ============================ THE PANEL ============================== */

  /* THE SCRIM IS A SECOND DOOR OUT. `openOverlay` hands back a close, but the
   * layer's own scrim can close it too and the chassis publishes no hook when
   * it does - so "is the panel up" is asked of the DOM rather than of a flag
   * this file would be the last to hear about. Everything below routes through
   * it: a stale flag would swallow the first Esc after a scrim press, which is
   * the exact shape of bug a player reads as the key being broken. */
  function panelUp() {
    if (!panel) return false;
    try { if (panel.isConnected === false) { panel = null; closePanel = null; return false; } }
    catch (e) { /* a DOM double may not carry isConnected - trust the flag */ }
    return true;
  }

  function openPanel() {
    if (dead) return;
    if (panelUp()) return;
    panel = null;
    closePanel = scene.openOverlay('locker', (host) => { buildPanel(host); });
  }

  function shutPanel() {
    if (!panelUp()) { panel = null; closePanel = null; return false; }
    const fn = closePanel;
    closePanel = null;
    panel = null;
    if (typeof fn === 'function') { try { fn(); } catch (e) { /* noop */ } }
    else { try { scene.closeOverlay(); } catch (e) { /* noop */ } }
    return true;
  }

  /** Rebuild the panel in place. Every writer calls this rather than patching a
   *  tile, because half this page reads a wallet the other half can change. */
  function repaint() {
    if (dead || !panelUp()) return;
    const host = panel.parentNode;
    if (!host) return;
    try { panel.remove(); } catch (e) { /* noop */ }
    panel = null;
    buildPanel(host);
  }

  function buildPanel(host) {
    if (dead || !host) return;
    /* LAW 3 runs at render, before a single tile is drawn, so a group cannot
     * paint a pick the wallet has stopped backing. */
    clampPicks(k);

    groupNodes.length = 0;
    panel = el('div', 'lk-panel');
    attr(panel, 'role', 'region');
    attr(panel, 'aria-label', t('locker_title', 'The Locker'));

    const head = el('header', 'lk-head');
    head.appendChild(el('h2', 'lk-title', t('locker_title', 'The Locker')));
    head.appendChild(el('p', 'lk-sub', t('locker_sub', 'Room 004. Nobody else has the combination.')));
    panel.appendChild(head);

    const rail = el('nav', 'lk-rail');
    attr(rail, 'role', 'tablist');
    attr(rail, 'aria-orientation', 'vertical');
    attr(rail, 'aria-label', t('locker_title', 'The Locker'));
    panel.appendChild(rail);

    const body = el('div', 'lk-body');
    panel.appendChild(body);

    const built = {
      wear: buildWear(),
      card: buildCard(),
      campus: buildCampus(),
      desk: buildDesk(),
      bag: buildBag(),
      always: buildAlways(),
    };
    for (const id of GROUPS) {
      const g = built[id];
      if (!g) continue;
      body.appendChild(g.node);
      rail.appendChild(g.tab);
      groupNodes.push(g);
    }

    if (!groupNodes.length) {
      body.appendChild(el('p', 'lk-note',
        t('locker_empty', 'Nothing in here yet. The counter is one window up.')));
    }

    const more = unownedCount();
    if (more > 0) {
      const foot = el('p', 'lk-foot');
      const line = fill(t('locker_more_at_counter', '{n} more at the counter'), { n: more });
      if (k.openCounter) {
        const b = el('button', 'lk-morelink', line);
        b.type = 'button';
        b.addEventListener('click', () => {
          sfx('blip', 0.14);
          try { k.openCounter(); } catch (e) { log('locker: the counter would not open'); }
        });
        foot.appendChild(b);
      } else {
        foot.appendChild(el('span', 'lk-moreflat', line));
      }
      panel.appendChild(foot);
    }

    host.appendChild(panel);
    layout();
    if (railOn) selectGroup(active && built[active] ? active : (groupNodes[0] || {}).id);
  }

  /* ------------------------------------------------------------ the groups */

  /** One group's shell: a heading, a tab for the rail, and a slot. */
  function group(id, title) {
    const node = el('section', 'lk-group');
    attr(node, 'data-lk', id);
    node.appendChild(el('h3', 'lk-gh', title));
    const slot = el('div', 'lk-slot');
    node.appendChild(slot);

    const tab = el('button', 'lk-tab');
    tab.type = 'button';
    attr(tab, 'role', 'tab');
    attr(tab, 'aria-selected', 'false');
    tab.tabIndex = -1;
    tab.appendChild(el('span', 'lk-tab-t', title));
    tab.addEventListener('click', () => selectGroup(id));

    return { id, node, tab, slot };
  }

  /** A row of picks. `rows` are `{id, name, art, glyph, dots, on}`. */
  function tiles(slot, rows, label, onPick) {
    const box = el('div', 'lk-tiles');
    attr(box, 'role', 'radiogroup');
    attr(box, 'aria-label', label);
    for (const r of rows) {
      const b = el('button', 'lk-tile' + (r.on ? ' is-on' : ''));
      b.type = 'button';
      attr(b, 'role', 'radio');
      attr(b, 'aria-checked', r.on ? 'true' : 'false');
      if (r.dots) b.appendChild(r.dots);
      else b.appendChild(artBox(r.art, r.glyph));
      b.appendChild(el('span', 'lk-name', r.name));
      if (r.on) {
        const badge = el('span', 'lk-on', t('locker_selected', 'On'));
        attr(badge, 'aria-hidden', 'true');
        b.appendChild(badge);
      }
      b.addEventListener('click', () => {
        if (r.on) { sfx('blip', 0.1, { pitch: 0.9 }); return; }
        let took = false;
        try { took = onPick(r.id) === true; } catch (e) { took = false; }
        if (!took) { sfx('blip', 0.12, { pitch: 0.82 }); return; }
        sfx('tell', 0.22, { pitch: r.id == null ? 0.92 : 1.08 });
        repaint();
      });
      box.appendChild(b);
    }
    slot.appendChild(box);
    return box;
  }

  /* ---------------------------------------------------------------- WEAR */

  function buildWear() {
    const owned = OUTFITS.filter((n) => k.ownsSku(OUTFIT_SKU[n]) === true);
    /* The usual look on its own is not a choice (the theme row's rule, and the
     * same reason: a radio group with one radio is a label). */
    if (!owned.length) return null;
    const g = group('wear', t('locker_wear', 'Wear'));
    const pick = readOutfit(k);
    const rows = [{
      id: null,
      name: t('locker_outfit_standard', 'The usual'),
      art: standardArt(),
      on: pick == null,
    }];
    for (const n of owned) {
      rows.push({
        id: n,
        name: t('locker_outfit_' + n),
        art: outfitArt(n),
        glyph: null,
        on: pick === n,
      });
    }
    tiles(g.slot, rows, t('locker_wear', 'Wear'), (id) => equipOutfit(k, id));
    return g;
  }

  /* ---------------------------------------------------------------- CARD */

  function buildCard() {
    const owned = Object.keys(FRAME_SKU).filter((id) => k.ownsSku(FRAME_SKU[id]) === true);
    if (!owned.length) return null;
    const g = group('card', t('locker_card', 'Card'));
    /* WHICH TILE IS LIT is the frame the card is actually WEARING, not the raw
     * key: with no pick stored the ownership ladder decides, and lighting
     * "Plain" while the card is visibly gold would be the page lying about
     * itself. `caps.idFrame` is the shell's one answer to that question. */
    const worn = wornFrame();
    const rows = [{
      id: FRAME_PLAIN,
      name: t('locker_frame_plain', 'Plain'),
      glyph: '▯',
      on: worn === '',
    }];
    for (const id of owned) {
      rows.push({
        id,
        name: t('locker_frame_' + id),
        art: spriteUrl(FRAME_SKU[id]),
        glyph: glyphOf(FRAME_SKU[id]),
        on: worn === id,
      });
    }
    tiles(g.slot, rows, t('locker_card', 'Card'), (id) => equipFrame(k, id));
    return g;
  }

  /** What the ID card wears right now: the pick if there is one, else the
   *  ownership ladder the shell has always run. */
  function wornFrame() {
    const pick = readFrame(k);
    if (pick === FRAME_PLAIN) return '';
    if (pick) return pick;
    if (k.ownsSku(FRAME_SKU.gold) === true) return 'gold';
    if (k.ownsSku(FRAME_SKU.navy) === true) return 'navy';
    return '';
  }

  /* -------------------------------------------------------------- CAMPUS */

  function buildCampus() {
    if (!k.themes || typeof k.themes.list !== 'function') return null;
    let list = [];
    try { list = k.themes.list() || []; } catch (e) { list = []; }
    if (list.length < 2) return null;          // House Standard alone is a label
    let current = 'standard';
    try { current = k.themes.current() || 'standard'; } catch (e) { current = 'standard'; }

    const g = group('campus', t('locker_campus', 'Campus'));
    const rows = [];
    for (const entry of list) {
      if (!entry || !entry.id) continue;
      /* THE SWATCH, straight off the theme's own palette - the one place in the
       * school a colour is allowed to travel as a value, because it is
       * DESCRIBING a palette rather than using one. Options drew exactly this
       * before the group moved in here. */
      let sw = null;
      try { sw = k.themes.swatch ? k.themes.swatch(entry.id) : null; } catch (e) { sw = null; }
      let dots = null;
      if (sw) {
        dots = el('span', 'lk-art lk-dots');
        attr(dots, 'aria-hidden', 'true');
        for (const hue of [sw.panel, sw.accent, sw.ink]) {
          const d = el('span', 'lk-dot');
          if (typeof hue === 'string') d.style.setProperty('--dot', hue);
          dots.appendChild(d);
        }
      }
      rows.push({
        id: entry.id,
        name: t(entry.nameKey || '', entry.nameEn || entry.id),
        dots,
        glyph: dots ? null : '▦',
        on: entry.id === current,
      });
    }
    tiles(g.slot, rows, t('locker_campus', 'Campus'), (id) => equipTheme(k, id));
    return g;
  }

  /* ---------------------------------------------------------------- DESK */

  function buildDesk() {
    if (k.ownsSku(TOY_SKU) !== true) return null;
    if (!TOYS.length) return null;
    const g = group('desk', t('locker_desk', 'Desk'));
    const pin = readToy(k);
    const rows = [{
      id: null,
      name: t('locker_toy_auto', 'Let the desk choose'),
      art: spriteUrl(TOY_SKU),
      glyph: glyphOf(TOY_SKU),
      on: pin == null,
    }];
    for (const toy of TOYS) {
      if (!toy || !toy.key) continue;
      rows.push({
        id: toy.key,
        name: t('locker_toy_' + toy.key),
        art: toyArt(toy),
        glyph: glyphOf(TOY_SKU),
        on: pin === toy.key,
      });
    }
    tiles(g.slot, rows, t('locker_desk', 'Desk'), (id) => equipToy(k, id));
    return g;
  }

  /* -------------------------------------------------------- IN YOUR BAG */

  function buildBag() {
    const inv = safeInv();
    const rows = [];
    for (const row of safeCatalog()) {
      if (!row || row.kind !== 'consumable') continue;
      const n = held(inv, row.sku);
      if (n <= 0) continue;
      rows.push({ row, n });
    }
    if (!rows.length) return null;
    const g = group('bag', t('locker_bag', 'In your bag'));
    for (const r of rows) {
      const line = el('div', 'lk-row');
      line.appendChild(artBox(spriteUrl(r.row.sku), glyphOf(r.row.sku), 'lk-art-sm'));
      const text = el('span', 'lk-rowtext');
      text.appendChild(el('b', 'lk-name', rowName(r.row)));
      const blurb = rowBlurb(r.row);
      if (blurb) text.appendChild(el('span', 'lk-line', blurb));
      line.appendChild(text);
      line.appendChild(el('b', 'lk-count', fill(t('locker_held', 'x{n}'), { n: r.n })));
      g.slot.appendChild(line);
    }
    return g;
  }

  /* ----------------------------------------------------------- ALWAYS ON */

  /** Every owned passive: the nine ordered ones, then anything else owned that
   *  no picker group has claimed. See PASSIVE_SKUS' header for why the sweep. */
  function passiveRows() {
    const rows = safeCatalog();
    const bySku = Object.create(null);
    for (const r of rows) if (r && r.sku) bySku[r.sku] = r;
    const out = [];
    const seen = Object.create(null);
    for (const sku of PASSIVE_SKUS) {
      if (k.ownsSku(sku) !== true) continue;
      seen[sku] = true;
      out.push(bySku[sku] || { sku });
    }
    for (const r of rows) {
      if (!r || !r.sku || seen[r.sku] || CLAIMED[r.sku]) continue;
      if (r.kind === 'consumable') continue;         // the bag lists those
      if (String(r.sku).indexOf('theme_') === 0) continue;   // CAMPUS lists those
      if (k.ownsSku(r.sku) !== true) continue;
      seen[r.sku] = true;
      out.push(r);
    }
    return out;
  }

  function buildAlways() {
    const rows = passiveRows();
    if (!rows.length) return null;
    const g = group('always', t('locker_always', 'Always on'));
    for (const row of rows) {
      const line = el('div', 'lk-row');
      line.appendChild(artBox(spriteUrl(row.sku), glyphOf(row.sku), 'lk-art-sm'));
      const text = el('span', 'lk-rowtext');
      text.appendChild(el('b', 'lk-name', rowName(row)));
      const blurb = rowBlurb(row);
      if (blurb) text.appendChild(el('span', 'lk-line', blurb));
      line.appendChild(text);
      /* THE ONE THING IN THIS GROUP YOU CAN POKE. It rings the SCHOOL BELL:
       * audio.js resolves brass off the cosmetic getter the shell installed at
       * build, so the preview is the real sound rather than a second copy of
       * it, and nothing here touches the ownership slot. */
      if (row.sku === 'brass_bell') {
        const b = el('button', 'lk-poke', t('locker_ring_bell', 'Ring it'));
        b.type = 'button';
        b.addEventListener('click', () => sfx('bell', 0.32));
        line.appendChild(b);
      }
      g.slot.appendChild(line);
    }
    return g;
  }

  /* ------------------------------------------------------------- readers */

  function safeCatalog() {
    let rows = [];
    try { rows = k.catalog() || []; } catch (e) { rows = []; }
    return Array.isArray(rows) ? rows.filter((r) => r && r.sku) : [];
  }

  function safeInv() {
    let inv = {};
    try { inv = k.inv() || {}; } catch (e) { inv = {}; }
    return inv && typeof inv === 'object' ? inv : {};
  }

  function held(inv, sku) {
    if (k.held) { try { return Math.max(0, Math.round(Number(k.held(sku)) || 0)); } catch (e) { /* fall through */ } }
    return heldOf(inv, sku);
  }

  /** The shelf's own fallback character, and the plain parcel under it for a
   *  sku the catalog grew before the glyph table did. */
  function glyphOf(sku) {
    const g = Object.prototype.hasOwnProperty.call(GLYPHS, sku) ? GLYPHS[sku] : null;
    return (typeof g === 'string' && g) ? g : '▤';
  }

  function rowName(row) {
    return t(row.nameKey || ('prize_name_' + row.sku), row.nameEn || row.sku);
  }

  function rowBlurb(row) {
    if (!row.blurbKey && !row.blurbEn) return '';
    return t(row.blurbKey || ('prize_blurb_' + row.sku), row.blurbEn || '');
  }

  /** How many rows the counter still holds that the player does not. The ONE
   *  place this room admits an unowned thing exists, and it never names one. */
  function unownedCount() {
    let n = 0;
    for (const row of safeCatalog()) if (k.ownsSku(row.sku) !== true) n += 1;
    return n;
  }

  /* ------------------------------------------------------------- the rail */

  function selectGroup(id) {
    const want = String(id || '');
    if (!groupNodes.some((g) => g.id === want)) return;
    active = want;
    for (const g of groupNodes) {
      const on = g.id === want;
      g.node.classList.toggle('lk-active', on);
      attr(g.tab, 'aria-selected', on ? 'true' : 'false');
      g.tab.tabIndex = on ? 0 : -1;
    }
  }

  /** Read the viewport, flip the mode. Idempotent; the sheet does the drawing. */
  function layout() {
    const want = isRailViewport();
    railOn = want;
    if (!panel) return;
    panel.classList.toggle('lk-rail-on', want);
    if (want) {
      if (!active || !groupNodes.some((g) => g.id === active)) {
        active = (groupNodes[0] || {}).id || null;
      }
      if (active) selectGroup(active);
    } else {
      for (const g of groupNodes) {
        g.node.classList.remove('lk-active');
        g.tab.tabIndex = -1;
      }
    }
  }

  try { offDevice = onDeviceChange(() => { if (!dead) layout(); }); }
  catch (e) { offDevice = null; }

  /* ------------------------------------------------------------- the fold */

  /**
   * THE ESC RUNG. Inward-out, the office's shape: the panel the player opened
   * one press ago folds first, then the chassis answers FALSE at the wide shot
   * and the shell's rung walks out of the building. No key is bound here.
   */
  function escapeStep() {
    if (dead) return false;
    if (shutPanel()) return true;
    try { return !!scene.escapeStep(); } catch (e) { return false; }
  }

  function destroy() {
    if (dead) return;
    dead = true;
    for (const id of timers) { try { clearTimeout(id); } catch (e) { /* noop */ } }
    timers.length = 0;
    if (offDevice) { try { offDevice(); } catch (e) { /* noop */ } offDevice = null; }
    closePanel = null;
    panel = null;
    groupNodes.length = 0;
    /* The apron lives on <body>: scene.destroy() is the ONLY thing that takes
     * it off, which is why the shell's clearScreen has to reach this line. */
    try { scene.destroy(); } catch (e) { /* noop */ }
  }

  return {
    root: scene.root,
    escapeStep,
    destroy,
    fit: () => { try { return scene.fit(); } catch (e) { return null; } },
    /* ------------------------------------------------------- test seams */
    openPanel,
    panelUp,
    scene,
    groups: () => groupNodes.map((g) => g.id),
    rail: () => ({ on: railOn, active }),
  };
}

/* ============================================================================
 * THE TWO SHELL-FACING VERBS
 *
 * The shell owns the screen (clearScreen, the topbar, the stage lock, the Esc
 * ladder), so it owns the OPENER too; this module owns the room. `installLocker`
 * is the seam between them, and it is the shape audio.js's `setBellCosmetic`
 * already uses: one module-level slot, written once at shell build, so a caller
 * anywhere in the page can say "open the locker" or "put this on" without
 * carrying a caps bag it has no business holding.
 * ==========================================================================*/

/** @type {?{open:Function, caps:Function}} */
let INSTALLED = null;

/**
 * Hand this module the shell's opener and its caps factory. Called ONCE, at
 * shell build. Passing null clears it, which is what a teardown wants.
 * @param {?{open:Function, caps:Function}} bag
 */
export function installLocker(bag) {
  INSTALLED = (bag && typeof bag.open === 'function') ? bag : null;
  return INSTALLED;
}

/** The installed caps, normalised, or null. */
function installedCaps() {
  if (!INSTALLED || typeof INSTALLED.caps !== 'function') return null;
  try { return norm(INSTALLED.caps()); } catch (e) { return null; }
}

/**
 * WALK IN. The campus door, the Options signpost and the back of the Student ID
 * all take this one road. `opts.open` lets a caller (a suite, a future room)
 * supply its own opener; everything shipped uses the installed one.
 * @param {object=} opts
 * @returns {boolean} whether a screen actually changed
 */
export function showLocker(opts) {
  const bag = (opts && typeof opts.open === 'function') ? opts : INSTALLED;
  if (!bag) return false;
  try { bag.open(); return true; }
  catch (e) { return false; }
}

/**
 * THE PURCHASE TOAST'S "PUT IT ON" VERB.
 *
 * Answers FALSE when there is nothing to equip, so the toast can hide the verb
 * rather than offer a button that does nothing. `emi_desk_toy` is the honest
 * false in the middle: buying it turns the prop ON by itself and there is no
 * single toy to put on - the pin is a CHOICE, and a choice belongs in the room.
 *
 * @param {string} sku
 * @returns {boolean}
 */
export function equipFromToast(sku) {
  const want = String(sku == null ? '' : sku);
  if (!want) return false;
  const k = installedCaps();
  if (!k) return false;

  /* The toy's sku is an `emi_` name and it is NOT an outfit, so it is answered
   * before the prefix test rather than after it. */
  if (want === TOY_SKU) return false;

  if (want.indexOf('emi_') === 0) {
    const name = want.slice(4);
    if (OUTFITS.indexOf(name) < 0) return false;
    return equipOutfit(k, name);
  }
  if (want.indexOf('id_frame_') === 0) {
    const id = want.slice('id_frame_'.length);
    if (!FRAME_SKU[id]) return false;
    return equipFrame(k, id);
  }
  if (want.indexOf('theme_') === 0) {
    if (!k.themes || typeof k.themes.list !== 'function') return false;
    let list = [];
    try { list = k.themes.list() || []; } catch (e) { list = []; }
    for (const entry of list) {
      if (entry && entry.sku === want) return equipTheme(k, entry.id);
    }
    return false;
  }
  return false;
}

export default createLocker;
