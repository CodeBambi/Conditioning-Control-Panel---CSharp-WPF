/* ============================================================================
 * THE ROOM SCENE. The visual-novel antechamber between the campus door and a
 * class: you walk to the door, the door opens, and you are STANDING IN THE
 * ROOM - the painted set from the VN wave - with the class's own furniture
 * lit and waiting for the click that starts it. It replaces the door card for
 * the rooms that have art, and costs the player nothing: the card was already
 * a screen between door and class, this is the same screen wearing the room.
 *
 * PILOT: Homeroom 101 only (Daily Trigger, art/vn/vn-04). The SCENES table is
 * the whole fan-out surface - a room joins the program by adding a row, and
 * every room without one keeps the door card it always had (campus.js only
 * offers the takeover; shell.js declines it for keys this table lacks).
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

/** Stage plane the art was authored on (the annex's, promoted). */
const STAGE_W = 1376;
const STAGE_H = 768;

/**
 * The fan-out table. Rects are [x, y, w, h] in stage pixels.
 *  - `art`     : file under ../art/vn/ (the VN wave's painted sets).
 *  - `hotspot` : the class furniture - the ONE lit thing, wears the action tag.
 *  - `exit`    : optional painted way out (a door in the art). Same verb as
 *                the back pill; a room without one just keeps the pill.
 */
const SCENES = Object.freeze({
  daily_trigger: Object.freeze({
    art: 'vn-04-homeroom-101.png',
    hotspot: Object.freeze([470, 228, 436, 160]),   // the marquee chalkboard
    exit: Object.freeze([1240, 115, 106, 350]),     // the open corridor door
  }),
});

/** Does this game have a painted room? The shell's decline test. */
export function hasRoomScene(gameKey) {
  return !!SCENES[gameKey];
}

function artUrl(file) {
  try { return new URL('../art/vn/' + file, import.meta.url).href; }
  catch (e) { return 'art/vn/' + file; }
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
  const hot = el('button', 'arm-hot');
  hot.type = 'button';
  placeRect(hot, scene.hotspot);
  const tag = el('span', 'arm-hot-tag',
    String(o.actionLabel || t('begin_class', 'Begin')).toUpperCase());
  hot.appendChild(tag);
  hot.setAttribute('aria-label', (o.actionLabel || '') + ' — ' + (o.name || ''));
  hot.addEventListener('click', () => {
    if (destroyed || entered) return;
    entered = true;
    try { if (o.onEnter) o.onEnter(); }
    catch (e) { entered = false; say('room enter threw: ' + ((e && e.message) || e)); }
  });
  stage.appendChild(hot);

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

  /* ------------------------------ back pill ----------------------------- */
  const back = el('button', 'arm-back', '← ' + t('rake_back_to_campus', 'Back to campus'));
  back.type = 'button';
  back.addEventListener('click', () => { if (!destroyed) { try { if (o.onBack) o.onBack(); } catch (e) { /* noop */ } } });
  root.appendChild(back);

  /* ------------------------------ fit ----------------------------------- */
  function fit() {
    if (destroyed) return;
    const w = root.clientWidth || (root.parentNode && root.parentNode.clientWidth) || STAGE_W;
    const h = root.clientHeight || (root.parentNode && root.parentNode.clientHeight) || STAGE_H;
    const s = Math.min(w / STAGE_W, h / STAGE_H) || 1;
    stage.style.transform = 'translate(-50%,-50%) scale(' + s + ')';
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
  art.addEventListener('load', onResize);
  if (typeof requestAnimationFrame === 'function') requestAnimationFrame(onResize);

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
      try { root.remove(); } catch (e) { /* noop */ }
    },
  };
}
