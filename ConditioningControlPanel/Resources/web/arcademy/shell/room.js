/* ============================================================================
 * THE ROOM SCENE. The visual-novel antechamber between the campus door and a
 * class: you walk to the door, the door opens, and you are STANDING IN THE
 * ROOM - the painted set from the VN wave - with the class's own furniture
 * lit and waiting for the click that starts it. It replaces the door card for
 * the rooms that have art, and costs the player nothing: the card was already
 * a screen between door and class, this is the same screen wearing the room.
 *
 * The SCENES table is the whole fan-out surface - a room joins the program by
 * adding a row, and every room without one keeps the door card it always had
 * (campus.js only offers the takeover; shell.js declines it for keys this
 * table lacks). Five rooms are painted: Homeroom 101 (the pilot), Memory Lab
 * 102, Discipline Hall 103, Lost & Found 104 and The Pool 105.
 *
 * THE FREE SWIM RULE. The door card carried a second, subordinate button for
 * games that declare `manifest.endless`, and the room REPLACES that card - so
 * a game with an endless mode may only join SCENES with a `freeSwim` rect to
 * carry the same offer. Join without one and the button is simply lost.
 *
 * The stage is the annex lab's, promoted: a fixed 1376x768 plane scaled by
 * transform to fit (origin 50% 50% - lab.css's lesson, the rig's 04 shot).
 * Hotspot rects are authored in stage pixels and ride the scale for free.
 *
 * Laws observed:
 *  - narrow caps: this module never sees the store, the bridge or the games
 *    list - the shell hands it strings and two callbacks (onEnter / onBack).
 *  - class-length wave: the room is CARD time, not class time. Nothing here
 *    starts a clock; the budget starts where it always did, in startClass.
 *  - decoration law: the pulse is decoration; .arc-reduced / lite kill it and
 *    the room still reads (static outline, same tags, same buttons).
 *  - nine-broken-logos law: art resolves module-relative via import.meta.url.
 * ==========================================================================*/

import { isMobile, orientation } from '../core/device.js';

/** Stage plane the art was authored on (the annex's, promoted). */
const STAGE_W = 1376;
const STAGE_H = 768;

/* THE APRON LINE. Every VN set shot was generated with its lower third held
 * calm and dark, reserved for a dialogue overlay - so the band of "void" the
 * player sees under the furniture is IN THE PAINTING, not just the letterbox.
 * The apron (the big bottom action band) claims everything below this stage
 * row, plus whatever letterbox hangs under the art. Hotspot rects in SCENES
 * must therefore keep their business ABOVE y=APRON_STAGE_TOP or the apron
 * will sit on them. */
const APRON_STAGE_TOP = 640;
/** The apron never shrinks below this, even art-to-the-floor (px, real). */
const APRON_MIN = 110;
/* THE PHONE'S FLOOR, and why it is a different number. 110px is a quarter of a
 * landscape phone's 390px of glass, which is a band, not an apron: the room it
 * is the front edge OF ends up smaller than the furniture in front of it. 72px
 * still clears the 44px thumb floor for all three slabs (rooms.css sizes them
 * off --arm-band-h and clamps each one at 44) and hands the painting back the
 * 38px it was eating. Desktop keeps 110 exactly. */
const APRON_MIN_MOBILE = 72;

/**
 * The fan-out table. Rects are [x, y, w, h] in stage pixels. Rects must keep
 * their business above y=APRON_STAGE_TOP - the apron band owns the floor.
 *  - `art`      : file under ../art/vn/ (the VN wave's painted sets).
 *  - `hotspot`  : the class furniture - the ONE lit thing, wears the action tag.
 *  - `exit`     : optional painted way out (a door in the art). Same verb as
 *                 the apron's back slab; a room without one just keeps the slab.
 *                 Only rooms whose art actually HAS a door get one - three of
 *                 the five are painted doorless and that stays legal.
 *  - `freeSwim` : optional second furniture, for a game that declares
 *                 `manifest.endless`. Renders only when the shell hands down an
 *                 onFreeSwim; spends the same one-class latch as the hotspot.
 */
const SCENES = Object.freeze({
  daily_trigger: Object.freeze({
    art: 'vn-04-homeroom-101.png',
    hotspot: Object.freeze([470, 228, 436, 160]),   // the marquee chalkboard
    exit: Object.freeze([1240, 115, 106, 350]),     // the open corridor door
  }),
  /* MEMORY LAB 102. The wooden card racks - three shelves of oversized
   * face-down purple card backs, which is the pairs game itself sitting in the
   * furniture. No painted door in this set; the apron's slab is the way out. */
  deja_vu: Object.freeze({
    art: 'vn-05-memory-lab-102.png',
    hotspot: Object.freeze([556, 250, 200, 226]),
  }),
  /* DISCIPLINE HALL 103. THE red button under its glass dome on the croupier
   * table - "don't press it" wants the button, not the table it sits on. The
   * panelled door on the back-right wall is the one real painted exit in the
   * whole set (narrow at 68px: widening it eats the prize wheel's rim). */
  impulse_control: Object.freeze({
    art: 'vn-06-discipline-hall-103.png',
    hotspot: Object.freeze([1235, 395, 120, 125]),
    exit: Object.freeze([1028, 166, 68, 232]),
  }),
  /* LOST & FOUND 104. The central bay of the lost-things wall (bear, mitten,
   * books, thermos, umbrella). The whole wall is 850px wide - 62% of the frame
   * - and a highlight that big reads as a bug, so the target is the densest,
   * best-lit slice of the same wall. Doorless set. */
  lost_and_found: Object.freeze({
    art: 'vn-07-lost-and-found-104.png',
    hotspot: Object.freeze([176, 168, 386, 330]),
  }),
  /* THE POOL 105. Graded runs start in the open water, mid-lane; the free swim
   * starts on the chrome grab-rail ladder, because "just get in" is a ladder
   * and the numbered starting blocks read as the race. The water is a
   * trapezoid forced into a rect - this is the widest purely-water slice.
   * Doorless: the back wall is glazing onto the courtyard, not a way out. */
  the_deep_end: Object.freeze({
    art: 'vn-08-the-pool-105.png',
    hotspot: Object.freeze([490, 368, 400, 104]),
    freeSwim: Object.freeze([335, 408, 132, 140]),
  }),
});

/* THE ONE MOBILE DECISION (core/device.js). JS asks the function, CSS reads the
 * class and the `data-arc-orient` attribute it writes, and this module needs
 * both to agree: the apron's floor and the stage's anchor are computed here and
 * the box they move is styled there.
 *
 * LANDSCAPE ONLY, and that is the half that matters. Portrait is behind the
 * rotate gate (shell/orientgate.js), so it is not designed for - but a room
 * left standing while the phone is turned still has to render something sane,
 * and top-anchoring a 9:19.5 frame would hand the apron two thirds of the
 * screen. Portrait keeps the centred stage and the 110px floor it always had.
 * rooms.css carries the SAME `[data-arc-orient="landscape"]` qualifier on the
 * two rules that move with this flag; drift and the scale walks off-axis.
 *
 * Wrapped because a suite's DOM double has no matchMedia and must get `false`
 * rather than a throw at fit() time. */
function onPhone() {
  try { return !!isMobile() && orientation() === 'landscape'; } catch (e) { return false; }
}

/** Does this game have a painted room? The shell's decline test. */
export function hasRoomScene(gameKey) {
  return !!SCENES[gameKey];
}

function artUrl(file) {
  try { return new URL('../art/vn/' + file, import.meta.url).href; }
  catch (e) { return 'art/vn/' + file; }
}

/**
 * THE PLATE'S URL, for anybody who wants it before the room is built. The one
 * consumer is the shell's prefetch (below): the campus knows tonight's four
 * keys, and a plate that is already in cache when the door opens is the whole
 * difference between a painted room and a lit void. Keeps the SCENES table
 * private - a caller gets a string or null, never the row.
 * @param {string} gameKey
 * @returns {?string}
 */
export function artUrlFor(gameKey) {
  const scene = SCENES[gameKey];
  return scene ? artUrl(scene.art) : null;
}

/**
 * WARM THE ROOMS THAT ARE ON TONIGHT. Called once a boot, from the seam where
 * the campus is shown, with the keys the timetable dealt. Every painted key
 * gets one `<link rel=prefetch as=image>` in <head>; unpainted keys, repeats
 * and a second call are no-ops.
 *
 * `prefetch`, not `preload`: this is art for a screen the player may never
 * open, so it must ride at the lowest priority the browser has and must never
 * warn about an unused preload. A desktop WebView2 window reads these files off
 * the local disk, where the whole thing costs a file open that would have
 * happened anyway - which is why this is safe to run everywhere rather than
 * behind a "is this the web build" test the page has no business taking.
 *
 * @param {Array<string>} gameKeys tonight's board (any order, dupes fine)
 * @returns {number} how many links this call actually added
 */
const prefetched = Object.create(null);
export function prefetchRoomArt(gameKeys) {
  if (!Array.isArray(gameKeys) || typeof document === 'undefined') return 0;
  const head = document.head || document.documentElement;
  if (!head || typeof head.appendChild !== 'function') return 0;
  if (typeof document.createElement !== 'function') return 0;
  let n = 0;
  for (let i = 0; i < gameKeys.length; i += 1) {
    const key = gameKeys[i];
    const href = artUrlFor(key);
    if (!href || prefetched[href]) continue;
    prefetched[href] = true;
    try {
      const link = document.createElement('link');
      link.rel = 'prefetch';
      link.as = 'image';
      link.href = href;
      head.appendChild(link);
      n += 1;
    } catch (e) { /* a browser without prefetch loses nothing but the warm-up */ }
  }
  return n;
}

/* A real file (shell/rooms.css), linked once and lazily, resolved against this
 * module - the corkboard's pattern, byte for byte the same reason: the skin
 * loads the first time a room does and never taxes a boot that opens none. */
function cssUrl() {
  try { return new URL('./rooms.css', import.meta.url).href; }
  catch (e) { return 'shell/rooms.css'; }
}

function ensureStyles(doc) {
  if (!doc) return null;
  const had = doc.getElementById('arm-styles');
  if (had) return had;
  const link = doc.createElement('link');
  link.id = 'arm-styles';
  link.rel = 'stylesheet';
  link.href = cssUrl();
  (doc.head || doc.documentElement).appendChild(link);
  return link;
}

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/**
 * @param {object} opts
 *  gameKey     - registry key; must have a SCENES row (hasRoomScene first).
 *  t           - lexicon lookup (key, fallback).
 *  name        - course display name (shell's gameName - mod-skinned).
 *  plate       - room plate line ("HOMEROOM 101 · RM 101"), pre-uppercased.
 *  statusLine  - tonight's status ("In Session" / "Retake" / "Open"...).
 *  actionLabel - the hotspot's verb ("Begin" / "Retake").
 *  stamp       - today's grade letter, '' when ungraded.
 *  xpLine      - the honest line under the plate (what this run pays).
 *  onEnter     - start the class (the shell walks its own graded path).
 *  onBack      - back to campus.
 *  onOptions   - open this class's options page; absent = no options button.
 *  onFreeSwim  - start the class untimed (Free Swim); absent = no second hero
 *                AND no painted freeSwim rect, however the SCENES row reads.
 *  freeSwimLabel - display label for both free-swim surfaces (t('free_swim')).
 *  lite        - performance mode; kills the pulse like reduced motion does.
 *  log         - shell's say.
 */
export function createRoomScene(opts) {
  const o = opts || {};
  const t = o.t || ((k, fb) => fb);
  const say = typeof o.log === 'function' ? o.log : () => {};
  const scene = SCENES[o.gameKey];
  if (!scene) throw new Error('no room scene for ' + o.gameKey);
  const cssLink = ensureStyles(document);

  let destroyed = false;
  let entered = false;               // ONE class per scene (the end card's latch)

  const root = el('div', 'arm-root' + (o.lite ? ' arm-lite' : ''));
  const stage = el('div', 'arm-stage');
  root.appendChild(stage);

  const art = document.createElement('img');
  art.className = 'arm-art';
  art.alt = '';
  art.draggable = false;
  art.src = artUrl(scene.art);
  stage.appendChild(art);

  /* ------------------------------ hotspots ------------------------------ */
  function placeRect(node, rect) {
    node.style.left = rect[0] + 'px';
    node.style.top = rect[1] + 'px';
    node.style.width = rect[2] + 'px';
    node.style.height = rect[3] + 'px';
  }

  /* THE ONE LIT THING. A <button>, focused on mount, so Enter is the same
   * press the end card taught (its click latch is `entered`, one rung up). */
  const actionText = String(o.actionLabel || t('begin_class', 'Begin')).toUpperCase();

  /* ONE ROOM, ONE CLASS. Up to four surfaces can start one - the furniture
   * hotspot, the apron's hero, and (in a pool) the painted ladder and its
   * subordinate slab - so they all spend the SAME latch: whichever lands first
   * spends it and the rest become no-ops. A double-tap, a race between two
   * surfaces, or a stab at Free Swim after Begin has already dealt can never
   * deal the class twice. A throw hands the latch back. */
  function spend(fn, what) {
    if (destroyed || entered || typeof fn !== 'function') return;
    entered = true;
    try { fn(); }
    catch (e) { entered = false; say('room ' + what + ' threw: ' + ((e && e.message) || e)); }
  }
  function enterClass() { spend(o.onEnter, 'enter'); }
  function freeSwim() { spend(o.onFreeSwim, 'free swim'); }

  /* .arm-main is what BREATHES - the skin keys the pulse on that class rather
   * than on "not the exit", because a pool has a third rect now. */
  const hot = el('button', 'arm-hot arm-main');
  hot.type = 'button';
  placeRect(hot, scene.hotspot);
  const tag = el('span', 'arm-hot-tag', actionText);
  hot.appendChild(tag);
  hot.setAttribute('aria-label', (o.actionLabel || '') + ' — ' + (o.name || ''));
  hot.addEventListener('click', enterClass);
  stage.appendChild(hot);

  /* THE SECOND PIECE OF FURNITURE, when a room paints one and the shell hands
   * down the verb to hang on it (a game with manifest.endless). The pool's
   * ladder: the quiet way in, cool where the lane is lit, and it spends the
   * same latch as the water. A rect with no callback renders NOTHING - the
   * table may carry the geometry for a game whose endless mode is gone. */
  const freeSwimText = String(o.freeSwimLabel || t('free_swim', 'Free Swim')).toUpperCase();
  if (scene.freeSwim && typeof o.onFreeSwim === 'function') {
    const swim = el('button', 'arm-hot arm-swim');
    swim.type = 'button';
    placeRect(swim, scene.freeSwim);
    swim.appendChild(el('span', 'arm-hot-tag', freeSwimText));
    swim.setAttribute('aria-label', freeSwimText + ' — ' + (o.name || ''));
    swim.addEventListener('click', freeSwim);
    stage.appendChild(swim);
  }

  /* The painted way out, when the art has one. Same verb as the pill below -
   * a second door, never a second behaviour. */
  if (scene.exit) {
    const door = el('button', 'arm-hot arm-exit');
    door.type = 'button';
    placeRect(door, scene.exit);
    door.appendChild(el('span', 'arm-hot-tag',
      String(t('rake_back_to_campus', 'Back to campus')).toUpperCase()));
    door.setAttribute('aria-label', t('rake_back_to_campus', 'Back to campus'));
    door.addEventListener('click', () => { if (!destroyed) { try { if (o.onBack) o.onBack(); } catch (e) { /* noop */ } } });
    stage.appendChild(door);
  }

  /* ------------------------------ the plate ----------------------------- */
  /* The door card's facts, worn as a room sign: plate, course, status, the
   * grade already earned tonight, and the honest XP line (trap 23's voice). */
  const plate = el('div', 'arm-plate');
  if (o.plate) plate.appendChild(el('div', 'arm-plate-room', o.plate));
  plate.appendChild(el('h2', 'arm-plate-name', o.name || ''));
  const statusRow = el('div', 'arm-plate-status');
  if (o.statusLine) statusRow.appendChild(el('span', null, String(o.statusLine).toUpperCase()));
  if (o.stamp) statusRow.appendChild(el('span', 'arm-plate-stamp', String(o.stamp).toUpperCase()));
  if (o.statusLine || o.stamp) plate.appendChild(statusRow);
  if (o.xpLine) plate.appendChild(el('p', 'arm-plate-xp', o.xpLine));
  root.appendChild(plate);

  /* --------------------------- the midway apron -------------------------- */
  /* THE MIDWAY APRON. The strip of confetti carpet at the front edge of the
   * room, fenced off from the painting by the campus's checkered route tape.
   * The VN art reserves its whole lower third as a calm dark floor, so the
   * apron owns that floor plus any letterbox under it - fit() anchors its top
   * edge to the painting's APRON_STAGE_TOP row, and everything below is
   * furniture: a giant bulb-ring marquee slab for the verb, a corridor sign
   * home on the left, a control plate on the right. One way back on the rail,
   * one painted door in the art - never a second behaviour. */
  const bar = el('div', 'arm-bar');

  const barLeft = el('div', 'arm-bar-side arm-bar-left');
  const back = el('button', 'arm-bar-ghost arm-slab-back');
  back.type = 'button';
  back.appendChild(el('span', 'arm-glyph arm-glyph-arrow'));
  back.appendChild(el('span', 'arm-slab-text', t('rake_back_to_campus', 'Back to campus')));
  back.addEventListener('click', () => { if (!destroyed) { try { if (o.onBack) o.onBack(); } catch (e) { /* noop */ } } });
  barLeft.appendChild(back);
  bar.appendChild(barLeft);

  /* THE HERO. The same verb the chalkboard tag wears, said out loud on a
   * cabinet-marquee slab - the hotspot is the room's diegetic way in and this
   * is the honest one, for the player who did not find the glowing furniture.
   * Same latch, same call. */
  const heroGroup = el('div', 'arm-hero-group');
  const hero = el('button', 'arm-hero');
  hero.type = 'button';
  hero.appendChild(el('span', 'arm-hero-face', actionText));
  hero.setAttribute('aria-label', (o.actionLabel || '') + ' - ' + (o.name || ''));
  hero.addEventListener('click', enterClass);
  heroGroup.appendChild(hero);

  /* THE SECOND VERB, when a room has one (a Free Swim door into the same
   * class, untimed). Subordinate on purpose: the primary keeps the marquee
   * bulbs and the size; this one is the quiet cyan side door. It spends the
   * SAME latch - a room deals one class, whichever verb lands first. */
  if (typeof o.onFreeSwim === 'function') {
    const alt = el('button', 'arm-hero-alt');
    alt.type = 'button';
    alt.appendChild(el('span', 'arm-hero-face', freeSwimText));
    alt.setAttribute('aria-label', freeSwimText + ' - ' + (o.name || ''));
    alt.addEventListener('click', freeSwim);
    heroGroup.appendChild(alt);
  }
  bar.appendChild(heroGroup);

  const barRight = el('div', 'arm-bar-side arm-bar-right');
  /* The knobs, where a player mid-doorway actually wants them. Absent
   * callback = absent button; the shell decides whether a room has options,
   * not this module (narrow caps). */
  if (typeof o.onOptions === 'function') {
    const cog = el('button', 'arm-bar-ghost arm-slab-opts');
    cog.type = 'button';
    cog.appendChild(el('span', 'arm-glyph arm-glyph-knobs'));
    cog.appendChild(el('span', 'arm-slab-text', t('room_options', 'Class options')));
    cog.addEventListener('click', () => {
      if (destroyed) return;
      try { o.onOptions(); } catch (e) { say('room options threw: ' + ((e && e.message) || e)); }
    });
    barRight.appendChild(cog);
  }
  bar.appendChild(barRight);
  /* The apron mounts on <body>, NOT inside root: .arm-root is its own
   * stacking context at z 10, so a child could never rise above EMI (#arc-emi
   * z 50) no matter its z-index. As a sibling at z 55 the band is the front
   * edge of the stage - she wanders behind it and the buttons can never be
   * squatted on. destroy() owns its removal like root's. */
  if (document.body) document.body.appendChild(bar);
  else root.appendChild(bar);

  /* ------------------------------ fit ----------------------------------- */
  function fit() {
    if (destroyed) return;
    const w = root.clientWidth || (root.parentNode && root.parentNode.clientWidth) || STAGE_W;
    const h = root.clientHeight || (root.parentNode && root.parentNode.clientHeight) || STAGE_H;
    const s = Math.min(w / STAGE_W, h / STAGE_H) || 1;
    /* THE PHONE ANCHORS THE PAINTING TO THE TOP. A centred stage splits its
     * letterbox above and below; above is the ceiling of the room, which is the
     * half a player reads the set from, and below is the calm floor the apron
     * was going to cover anyway. So on a phone the whole void goes to the
     * bottom, under the band. rooms.css moves the box (`top:0`) and the origin
     * (`50% 0`) under the same class - the two have to agree or the scale walks
     * the plane off-axis (lab.css's lesson). Desktop is untouched. */
    const phone = onPhone();
    stage.style.transform = 'translate(-50%,' + (phone ? '0' : '-50%') + ') scale(' + s + ')';
    /* The apron hugs the painting's floor line and swallows the letterbox:
     * top = the APRON_STAGE_TOP row of the scaled stage; bottom = the viewport.
     * Never thinner than the floor for this frame (APRON_MIN / _MOBILE). */
    const artTop = phone ? 0 : (h - STAGE_H * s) / 2;
    let apronTop = artTop + APRON_STAGE_TOP * s;
    if (h - apronTop < (phone ? APRON_MIN_MOBILE : APRON_MIN)) apronTop = h - (phone ? APRON_MIN_MOBILE : APRON_MIN);
    if (apronTop < 0) apronTop = 0;
    bar.style.top = apronTop + 'px';
    /* Published on both: the bar is a body-level sibling, so it cannot
     * inherit a var set on root alone. */
    root.style.setProperty('--arm-band-h', (h - apronTop) + 'px');
    bar.style.setProperty('--arm-band-h', (h - apronTop) + 'px');
  }
  const onResize = () => fit();
  if (typeof window !== 'undefined' && window.addEventListener) {
    window.addEventListener('resize', onResize);
  }
  /* THE FIRST FIT RACES THE LAZY STYLESHEET (the rig's r03 shot: an unstyled
   * root measures as the 1080px column and the room ships as a postcard).
   * Refit when the skin lands, when the art decodes, and once next frame -
   * cheap, idempotent, and the resize listener owns every later change. */
  if (cssLink) cssLink.addEventListener('load', onResize);
  /* THE COLD LOAD. The plate starts at opacity 0 over the stage's painted
   * fallback (rooms.css) and is lit here, on the image's own load - the same
   * listener that was already refitting, one class heavier. A cached plate is
   * in on the first frame; a plate off a phone's network eases onto a lit
   * room-shaped void instead of blinking over a black rectangle. */
  function lightArt() {
    try { art.classList.add('is-in'); } catch (e) { /* DOM double: nothing to light */ }
  }
  art.addEventListener('load', () => { lightArt(); onResize(); });
  art.addEventListener('error', lightArt);   // a 404 must never leave a hidden img
  if (typeof requestAnimationFrame === 'function') requestAnimationFrame(onResize);
  /* THE BELT. `load` cannot fire before this turn ends, so the listener above
   * always sees it - but a decoded-from-cache plate in a browser that skips the
   * event, or a suite that hands us a fake image, must not be left invisible. */
  if (art.complete && art.naturalWidth) lightArt();
  else if (typeof setTimeout === 'function') {
    setTimeout(() => { if (!destroyed && art.complete) lightArt(); }, 1200);
  }

  /* Focus after the caller has appended us (a fresh node not yet in the
   * document ignores focus() in some engines - the end card's lesson). */
  setTimeout(() => {
    if (!destroyed) { try { hot.focus(); } catch (e) { /* noop */ } }
  }, 0);

  return {
    root,
    fit,
    destroy() {
      if (destroyed) return;
      destroyed = true;
      if (typeof window !== 'undefined' && window.removeEventListener) {
        window.removeEventListener('resize', onResize);
      }
      if (cssLink) { try { cssLink.removeEventListener('load', onResize); } catch (e) { /* noop */ } }
      try { bar.remove(); } catch (e) { /* noop */ }
      try { root.remove(); } catch (e) { /* noop */ }
    },
  };
}
