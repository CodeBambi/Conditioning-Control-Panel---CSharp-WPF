/* ============================================================================
 * THE RECORDS ANNEX. The lab itself: a point-and-click room on four slides
 * (wide control room, monitor wall, clerk's desk, binder shelf), the paper
 * props (the intake sheet and the mascot dossier), and the laptop that zooms
 * into the fake OS (./os.js). Entered only through shell.showAnnex().
 *
 * Responsibilities are split the records.js way: the shell owns the store,
 * EMI, and the bridge; this module receives narrow caps and renders. The
 * shell brackets EMI (setEnabled false/true) and flips the voice's labSeen;
 * this module never imports emi/ or the store.
 *
 * Laws:
 * - Slides are 1376x768, scaled by transform (fitStage). Hotspots are
 *   authored in slide pixels and ride the scale for free.
 * - THE READING SURFACES DO NOT RIDE THAT SCALE (wave 3). A painting scales
 *   fine; 13px type at min(w/1376, h/768) does not - on a 390px phone that is
 *   ~0.28 and body text lands near 3.7px. The OS layer, the paper layer, the
 *   drawer and the step-back pill are mounted on the ROOT, in real CSS px,
 *   and only the slide + its hotspots + the descent stay on the stage. The
 *   laptop zoom still starts at the glass: glassOrigin() walks the bbox
 *   through the stage's own scale and centring into the frame's local space.
 * - The monitor wall reuses createCamWall (lifted, not copied) and composites
 *   feeds under the bezels through the screenmask, red channel to alpha,
 *   hidden until the mask lands (a naked feed over the art reads as a bug).
 * - Esc folds inward-out: the back of a sheet, then the sheet, then the
 *   drawer, then an OS window, then the laptop, then the close-up, and the
 *   shell's own rung walks home from the wide shot.
 * - Punch 4 stamps when the owl page is READ, not when the folder opens.
 * - Assets resolve module-relative (the nine-broken-logos law).
 *
 * THE MATERIAL (wave 2). A document on paper is a document plus its MOUNTS:
 * a photo under a drawn paperclip, a figure on a stapled strip, a sticky, a
 * pen note in the margin, and a dog-eared corner that turns the sheet over.
 * Three things that are not negotiable:
 * - The **run** / __run__ marks are parsed by os.js's parseRuns, IMPORTED,
 *   never re-implemented here. The OS paints them phosphor, paper paints the
 *   same rows as highlighter and pen ink.
 * - THE ONE LIVE THING ON PAPER is the intake sheet's attendance strip, and
 *   it is honest or it is absent: no caps function, a throw, an empty row or
 *   an all-zero row all render NOTHING. A live screen never lies, and a live
 *   strip on paper is held to the same law.
 * - Handwriting is a LOCAL stack ('Segoe Print', 'Comic Sans MS', cursive).
 *   No remote font ever enters this bundle (trap 2).
 * ==========================================================================*/

import { createCamWall } from './cams.js';
import { createAnnexOs, parseRuns } from './os.js';
import { MASCOT_PAGES, INTAKE_SHEET } from './docs.js';
import { renderChart } from './charts.js';

const STAGE_W = 1376;
const STAGE_H = 768;

/* module-relative asset base (campus.js:336's law) */
const ART_BASE = (function resolveArtBase() {
  try { return new URL('../art/annex/', import.meta.url).href; }
  catch (e) { return 'art/annex/'; }
}());
const QUADS_URL = (function resolveQuads() {
  try { return new URL('../art/annex/screen-quads.json', import.meta.url).href; }
  catch (e) { return 'art/annex/screen-quads.json'; }
}());

/** Slide art per view. The quad table still keys the shot names the extractor
 *  used; FEED_KEY maps our filenames back to it. */
const VIEW_ART = Object.freeze({
  wide: 'lab-wide.png',
  monitors: 'lab-monitors.png',
  desk: 'lab-desk.png',
  shelf: 'lab-shelf.png',
});
const FEED_KEY = 'annex_shot_monitors2';
const MASK_FILE = 'lab-monitors-mask.png';
/* cam8 sits mostly behind the laptop; the demo's proven override */
const CAM8_BBOX = Object.freeze([581, 539, 215, 143]);

/** Hotspots per view, in slide pixels: [x, y, w, h, action, lexKey, fallback]. */
const HOTSPOTS = Object.freeze({
  wide: Object.freeze([
    Object.freeze([10, 90, 325, 480, 'monitors', 'annex_hot_monitors', 'the monitors']),
    Object.freeze([850, 240, 165, 275, 'shelf', 'annex_hot_shelf', 'the shelf']),
    Object.freeze([1030, 430, 346, 310, 'desk', 'annex_hot_desk', 'the desk']),
    Object.freeze([1075, 235, 130, 195, 'exit', 'annex_hot_door', 'the stairs']),
  ]),
  monitors: Object.freeze([]),   /* laptop rect comes from the quad table */
  desk: Object.freeze([
    Object.freeze([875, 390, 295, 160, 'dossier', 'annex_hot_folder', 'the folder']),
  ]),
  shelf: Object.freeze([
    Object.freeze([600, 320, 230, 265, 'drawer', 'annex_hot_binder', 'FIELD DATA']),
  ]),
});

/* ============================================================================
 * THE DRAWER (Plate 3). The paper twin of the explorer: tab = folder, row =
 * file, sheet = preview. The three tab NAMES are chrome and go through t();
 * every row LINE below is diegetic, which is why it is frozen here and not in
 * the lexicon - a mod may re-voice a label, never what the binder holds.
 *
 * `open` names the paper a row puts on the desk. `at` is the annotation that
 * teaches the geography: the protocols live on the laptop, and a row that
 * says so is a row the player stops hunting for down here. A row with neither
 * is a row that is simply in the drawer.
 * ==========================================================================*/
const DRAWER = Object.freeze([
  Object.freeze({
    id: 'projects', key: 'annex_tab_projects', fb: 'PROJECTS',
    rows: Object.freeze([
      Object.freeze({ label: 'RM101 to RM105', note: '5 sheets', at: 1 }),
      Object.freeze({ label: 'RM201 to RM203', note: '3 sheets', at: 1 }),
      Object.freeze({ label: 'devices', note: '1 sheet, 1 strip', at: 1 }),
    ]),
  }),
  Object.freeze({
    id: 'fielddata', key: 'annex_tab_fielddata', fb: 'FIELD DATA',
    unread: 'intake',
    rows: Object.freeze([
      Object.freeze({ label: 'subject intake', note: '1 sheet, slip', open: 'intake' }),
      Object.freeze({ label: 'attendance strips', note: '6', open: 'intake' }),
      Object.freeze({ withheld: 1 }),
    ]),
  }),
  Object.freeze({
    id: 'misc', key: 'annex_tab_misc', fb: 'MISC',
    rows: Object.freeze([
      Object.freeze({ label: 'parlour, retired', note: '1 sheet, photo', at: 1 }),
      Object.freeze({ label: 'circulation copies', note: '2 scans', at: 1 }),
      Object.freeze({ label: 'the cups', note: 'a box' }),
    ]),
  }),
]);

/* Abstract silhouettes for the dossier: category shapes, never trade dress.
 * Eyes are white circles because they are redactions, not eyes. */
const SILS = Object.freeze({
  owl: 'M50 8 L38 22 Q18 26 14 52 Q12 78 30 88 L70 88 Q88 78 86 52 Q82 26 62 22 L50 8 Z',
  tiger: 'M22 30 Q14 12 30 14 Q40 4 50 12 Q60 4 70 14 Q86 12 78 30 Q88 46 80 64 Q70 86 50 86 Q30 86 20 64 Q12 46 22 30 Z',
  clip: 'M36 18 Q36 8 48 8 Q60 8 60 18 L60 66 Q60 78 48 78 Q36 78 36 66 L36 30 Q36 24 43 24 Q50 24 50 30 L50 62',
});

export function createAnnexLab(caps) {
  const c = caps || {};
  const t = typeof c.t === 'function' ? c.t : (k, f) => f;
  const lite = !!c.lite;
  const log = typeof c.log === 'function' ? c.log : () => {};
  const doc = document;

  let dead = false;
  let view = null;
  let wall = null;
  let wallStage = null;
  let os = null;
  let osLayer = null;
  let paperLayer = null;
  let drawerLayer = null;
  let paper = null;         /* { sheet, face, back, flip } while a sheet is up */
  let paperBack = null;     /* set while the BACK of the sheet is the side up  */
  let backPill = null;      /* the step-back pill, on the ROOT (wave 3)        */
  let descent = null;
  let quads = null;
  let quadsWanted = false;
  const timers = [];

  /* ONE page-owned blob under the key `annex`, never six keys. `read` is the
   * reading room's set (docId -> 1), `skim` the keyword toggle and `withheld`
   * the high-water count of black bars the player has actually been shown. */
  const st = Object.assign({ visited: 0, os: 0, p1: 0, p2: 0, p3: 0, p4: 0, read: {}, skim: 1, withheld: 0, pbars: 0 },
    (typeof c.annexState === 'function' ? c.annexState() : null) || {});
  if (!st.read || typeof st.read !== 'object') st.read = {};
  function save(patch) {
    Object.assign(st, patch);
    try { if (typeof c.saveAnnex === 'function') c.saveAnnex(patch); }
    catch (e) { log('annex save failed'); }
  }

  /* THE READ SET IS SHARED, THE COUNTER IS NOT. `read` carries the OS's 26
   * document ids AND the paper's own `paper:` ids (the drawer's unread tab
   * edge is one of them). os.js counts only the ids it can see in its TREE,
   * so a `paper:` id can never tick "read n/26" - which is the whole reason
   * it is safe to keep one set instead of two. */
  const PAPER_READ = 'paper:';
  function paperRead(id) { return !!st.read[PAPER_READ + id]; }
  function markPaperRead(id) {
    if (paperRead(id)) return;
    save({ read: Object.assign({}, st.read, { [PAPER_READ + id]: 1 }) });
  }

  /* A black bar downstairs is a black bar upstairs: the withheld counter on
   * the OS taskbar is ONE number for the whole annex. os.js counts its own
   * bars and knows nothing of ours, so the two are composed rather than
   * fought over - the OS is handed the total MINUS the paper's share, and
   * writes back its own count plus that same share. Both are high water
   * marks, so neither can ever walk the number down. */
  const paperBars = {};
  function markPaperBar(id) {
    if (paperBars[id]) return;
    paperBars[id] = 1;
    const n = Object.keys(paperBars).length;
    const was = Number(st.pbars) || 0;
    if (n <= was) return;
    save({ pbars: n, withheld: (Number(st.withheld) || 0) + (n - was) });
  }

  function sfx(name, level, extra) {
    try {
      const detail = Object.assign({ name, level: level == null ? 0.5 : level, bus: 'fx' }, extra || {});
      doc.dispatchEvent(new CustomEvent('arcademy-sfx', { detail }));
    } catch (e) { /* decoration */ }
  }
  function later(fn, ms) {
    const id = setTimeout(() => { if (!dead) fn(); }, ms);
    timers.push(id);
    return id;
  }
  function el(tag, cls, text) {
    const n = doc.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  }

  /* ------------------------------------------------------------ lazy CSS */
  /* corkboard's ensureStyles: three sheets, three ids, loaded once each */
  function ensureSheet(id, rel) {
    try {
      if (doc.getElementById(id)) return;
      const link = doc.createElement('link');
      link.id = id;
      link.rel = 'stylesheet';
      link.href = new URL(rel, import.meta.url).href;
      doc.head.appendChild(link);
    } catch (e) { log('annex sheet failed: ' + rel); }
  }
  ensureSheet('arc-annex-lab-css', './lab.css');
  ensureSheet('arc-annex-os-css', './os.css');
  ensureSheet('arc-annex-cams-css', './cams.css');

  /* ------------------------------------------------------------- chassis */

  const root = el('div', 'al-root' + (lite ? ' al-lite' : ''));
  root.setAttribute('role', 'region');
  root.setAttribute('aria-label', t('annex_room_label', 'The Records Annex'));
  const stage = el('div', 'al-stage');
  root.appendChild(stage);

  /** The stage's own scale, and the root pixel its top-left lands on. ONE
   *  derivation, used by fitStage and by the laptop's zoom origin - two copies
   *  of this arithmetic is how a zoom starts in the wrong corner. */
  function stageBox() {
    const w = root.clientWidth || STAGE_W;
    const h = root.clientHeight || STAGE_H;
    const s = Math.min(w / STAGE_W, h / STAGE_H);
    return { w, h, s, x: (w - STAGE_W * s) / 2, y: (h - STAGE_H * s) / 2 };
  }

  /** THE NARROW RUNG, measured in REAL px because the layers are off the
   *  stage now. Same 700 threshold os.js's own fit() uses, so the paper and
   *  the terminal fold on the same edge. */
  const NARROW_UNDER = 700;

  function fitStage() {
    try {
      const b = stageBox();
      stage.style.transform = 'translate(-50%, -50%) scale(' + b.s + ')';
      root.classList.toggle('al-narrow', b.w > 0 && b.w < NARROW_UNDER);
      if (os) os.fit();
    } catch (e) { /* noop */ }
  }
  const onResize = () => fitStage();
  window.addEventListener('resize', onResize);

  /* --------------------------------------------------------------- views */

  /**
   * THE LAYERS ARE ON THE ROOT, so the stage wipe below no longer takes them
   * with it: a slide change has to put them down BY HAND or a paper sheet
   * survives a walk back into the wide shot and the ladder folds a node
   * nobody remembers opening. Silent on purpose - the slide's own door/paper
   * cue is the sound of the move, and a stack of closes is a stack of noises.
   */
  function clearLayers() {
    if (os) { try { os.destroy(); } catch (e) { /* noop */ } os = null; }
    if (osLayer) { osLayer.remove(); osLayer = null; }
    if (paperLayer) { paperLayer.remove(); paperLayer = null; }
    if (drawerLayer) { drawerLayer.remove(); drawerLayer = null; }
    paper = null;
    paperBack = null;
  }

  function showView(name) {
    if (dead || view === name) return;
    const entering = view !== null;
    view = name;
    if (name !== 'monitors' && wall) wall.stop();

    clearLayers();
    stage.textContent = '';
    const art = el('img', 'al-art');
    art.alt = '';
    art.src = ART_BASE + VIEW_ART[name];
    stage.appendChild(art);

    if (name === 'monitors') mountFeeds();

    (HOTSPOTS[name] || []).forEach((h) => stage.appendChild(hotspot(h)));
    if (name === 'monitors') mountLaptopHotspot();

    /* THE WAY OUT IS ON THE ROOT, not on the slide. Same verb, same DOM, same
     * z above every layer this room mints - it just stopped being multiplied
     * by the stage scale, which on a phone was a 4px pill. */
    if (backPill) { backPill.remove(); backPill = null; }
    if (name !== 'wide') {
      const back = el('button', 'al-back', '‹ ' + t('annex_back', 'step back'));
      back.addEventListener('click', () => stepBack());
      root.appendChild(back);
      backPill = back;
    }
    if (entering) sfx(name === 'wide' ? 'door' : 'paper', name === 'wide' ? 0.3 : 0.2);
    fitStage();
  }

  function hotspot(spec) {
    const [x, y, w, h, action, key, fb] = spec;
    const b = el('button', 'al-hot');
    b.style.left = x + 'px';
    b.style.top = y + 'px';
    b.style.width = w + 'px';
    b.style.height = h + 'px';
    const label = t(key, fb);
    b.setAttribute('aria-label', label);
    b.appendChild(el('span', 'al-hot-tag', label));
    b.addEventListener('click', () => act(action));
    return b;
  }

  function act(action) {
    if (dead) return;
    if (action === 'exit') { requestExit(); return; }
    if (action === 'dossier') { sfx('paper', 0.28); openDossier(); return; }
    if (action === 'drawer') { sfx('paper', 0.24); openDrawer(); return; }
    if (action === 'laptop') { openOs(); return; }
    sfx('blip', 0.16, { pitch: 1.05 });
    showView(action);
  }

  function requestExit() {
    sfx('door', 0.35);
    try { if (typeof c.onExit === 'function') c.onExit(); } catch (e) { /* noop */ }
  }

  /* ------------------------------------------------------------ the wall */

  function loadQuads() {
    if (quadsWanted) return;
    quadsWanted = true;
    fetch(QUADS_URL)
      .then((r) => r.json())
      .then((j) => {
        if (dead) return;
        quads = j && j[FEED_KEY] ? j[FEED_KEY] : null;
        if (view === 'monitors') { mountFeeds(); mountLaptopHotspot(); }
      })
      .catch(() => { quads = null; log('annex quads failed'); });
  }

  function mountFeeds() {
    loadQuads();
    if (!quads || lite === 'never') { /* feeds are decoration; art alone is legal */ }
    if (!quads) return;
    if (stage.querySelector('.al-feeds')) { if (wall) wall.start(); return; }

    const feeds = el('div', 'al-feeds is-waiting');
    stage.appendChild(feeds);

    if (!wall) {
      wall = createCamWall({ t, lite });
      wallStage = el('div');
      wallStage.hidden = true;
      root.appendChild(wallStage);       /* parking lot for un-placed tiles */
      wallStage.appendChild(wall.root);
    }

    /* place tiles by bbox, cam8 by the proven override */
    const screens = Array.isArray(quads.screens) ? quads.screens : [];
    /* every tile places, the laptop included: its locked-terminal card is the
     * diegetic pre-OS screen (raw chroma green through the mask otherwise) */
    screens.forEach((s2) => {
      const tile = wall.tiles && wall.tiles[s2.name];
      if (!tile) return;
      const bb = s2.name === 'cam8' ? CAM8_BBOX : s2.bbox;
      if (!bb) return;
      tile.style.left = bb[0] + 'px';
      tile.style.top = bb[1] + 'px';
      tile.style.width = bb[2] + 'px';
      tile.style.height = bb[3] + 'px';
      feeds.appendChild(tile);
    });

    /* the mask: red channel to alpha, feeds hidden until it lands */
    const mask = new Image();
    mask.onload = () => {
      if (dead) return;
      try {
        const cv = doc.createElement('canvas');
        cv.width = mask.naturalWidth;
        cv.height = mask.naturalHeight;
        const ctx = cv.getContext('2d');
        ctx.drawImage(mask, 0, 0);
        const im = ctx.getImageData(0, 0, cv.width, cv.height);
        const px = im.data;
        for (let i = 0; i < px.length; i += 4) { px[i + 3] = px[i]; }
        ctx.putImageData(im, 0, 0);
        const url = cv.toDataURL();
        feeds.style.maskImage = 'url(' + url + ')';
        feeds.style.webkitMaskImage = 'url(' + url + ')';
        feeds.style.maskSize = '100% 100%';
        feeds.style.webkitMaskSize = '100% 100%';
      } catch (e) { log('annex mask failed'); }
      feeds.classList.remove('is-waiting');
      if (wall) wall.start();
    };
    mask.onerror = () => { if (!dead) { feeds.remove(); log('annex mask missing'); } };
    mask.src = ART_BASE + MASK_FILE;
  }

  function mountLaptopHotspot() {
    if (!quads || view !== 'monitors') return;
    if (stage.querySelector('[data-annex-laptop]')) return;
    const s2 = (quads.screens || []).find((q) => q.name === 'laptop');
    if (!s2 || !s2.bbox) return;
    const b = hotspot([s2.bbox[0] - 14, s2.bbox[1] - 14, s2.bbox[2] + 28, s2.bbox[3] + 42,
      'laptop', 'annex_hot_laptop', 'the laptop']);
    b.dataset.annexLaptop = '1';
    stage.appendChild(b);
  }

  /* --------------------------------------------------------------- the OS */

  /**
   * WHERE THE GLASS IS, ON SCREEN. The laptop's bbox is authored in slide
   * pixels; the frame it has to zoom out of is a root-level box in real ones.
   * So the point is walked through the stage's own scale and centring into
   * root px, and then into the frame's local space, because transform-origin
   * is measured from the frame's own border box.
   *
   * `k` is the glass's on-screen WIDTH over the frame's - the honest starting
   * scale, whatever the two boxes happen to be. A frame with no layout yet
   * answers null and the zoom is simply skipped: a wrong origin reads as the
   * OS flying in from a corner, which is worse than no flight at all.
   */
  function glassOrigin(bbox, frame) {
    try {
      const b = stageBox();
      if (!(b.s > 0)) return null;
      const fr = frame.getBoundingClientRect();
      const rr = root.getBoundingClientRect();
      if (!fr.width || !fr.height) return null;
      const ox = b.x + (bbox[0] + bbox[2] / 2) * b.s;
      const oy = b.y + (bbox[1] + bbox[3] / 2) * b.s;
      const k = Math.max(0.05, Math.min(1, (bbox[2] * b.s) / fr.width));
      return { x: ox - (fr.left - rr.left), y: oy - (fr.top - rr.top), k };
    } catch (e) { return null; }
  }

  function openOs() {
    if (os) return;
    sfx('whoosh', 0.22);
    later(() => sfx('chime', 0.16), 180);

    osLayer = el('div', 'al-oslayer');
    const frame = el('div', 'al-osframe');
    osLayer.appendChild(frame);
    /* the painted room around the laptop is a way out (paperShell's pattern),
     * but ONLY when the press and the release both landed on the backdrop -
     * a window dragged out of the frame reports the layer as the click target
     * (the common ancestor of down and up) and must not read as "leave" */
    let downOnBackdrop = false;
    osLayer.addEventListener('pointerdown', (e) => { downOnBackdrop = e.target === osLayer; });
    osLayer.addEventListener('click', (e) => {
      const clean = downOnBackdrop;
      downOnBackdrop = false;
      if (e.target !== osLayer || !clean) return;
      stepBack();
    });
    root.appendChild(osLayer);

    /* zoom out of the laptop glass: origin at the glass, scale up */
    const s2 = quads && (quads.screens || []).find((q) => q.name === 'laptop');
    if (s2 && s2.bbox && !lite) {
      const g = glassOrigin(s2.bbox, frame);
      if (g) {
        frame.style.transformOrigin = g.x + 'px ' + g.y + 'px';
        frame.classList.add('is-zooming');
        frame.style.transform = 'scale(' + g.k + ')';
        later(() => { frame.style.transform = 'scale(1)'; frame.classList.remove('is-zooming'); }, 30);
      }
    }

    os = createAnnexOs({
      t,
      lite,
      subject: c.subject || {},
      liveFile: c.liveFile,
      fetchStats: c.fetchStats,
      gameName: c.gameName,
      gamesList: c.gamesList,
      osUnlocked: !!st.os,
      onUnlock: () => save({ os: 1 }),
      getPunches: () => ({ p1: !!st.p1, p2: !!st.p2, p3: !!st.p3, p4: !!st.p4 }),
      onPunch: (p) => {
        if (p === 'p1' && !st.p1) save({ p1: 1 });
        else if (p === 'p2' && !st.p2) save({ p2: 1 });
        else if (p === 'p3' && !st.p3) save({ p3: 1 });
      },
      /* the reading room's caps. One write per document, ever. */
      getRead: () => st.read,
      onRead: (id) => {
        if (!id || st.read[id]) return;
        save({ read: Object.assign({}, st.read, { [id]: 1 }) });
      },
      getSkim: () => st.skim !== 0,
      setSkim: (on) => save({ skim: on ? 1 : 0 }),
      getWithheld: () => Math.max(0, (Number(st.withheld) || 0) - (Number(st.pbars) || 0)),
      setWithheld: (n) => save({ withheld: (Number(n) || 0) + (Number(st.pbars) || 0) }),
    });
    frame.appendChild(os.root);
    later(() => { if (os) os.fit(); }, 60);
  }

  /** The pill's verb, and the backdrop's. ONE step, and it never folds an OS
   *  window: escapeStep() walks inward-out (a window at a time), but a player
   *  who reaches for "step back" is leaving the terminal, not tidying it. So
   *  the ladder here is the back of a sheet -> the sheet -> the drawer -> the
   *  whole OS -> the close-up itself. */
  function stepBack() {
    if (dead) return;
    if (paperBack) { turnPaper(false); return; }
    if (paperLayer) { closePaper(); return; }
    if (drawerLayer) { closeDrawer(); return; }
    if (os) { closeOs(); return; }
    if (view && view !== 'wide') { sfx('door', 0.3); showView('wide'); }
  }

  function closeOs() {
    if (!os) return;
    sfx('blip', 0.16, { pitch: 0.9 });
    os.destroy();
    os = null;
    if (osLayer) { osLayer.remove(); osLayer = null; }
  }

  /* ========================================================================
   * THE PAPER MATERIAL KIT (Plate 2)
   * ==================================================================== */

  const SVG_NS = 'http://www.w3.org/2000/svg';

  /** The category silhouette, drawn. ONE path table, two sizes: the dossier's
   *  centred plate and the photo card under the paperclip both come from
   *  here, so a page can never disagree with its own photograph. */
  function silSvg(name, size) {
    const d = SILS[name];
    if (!d) return null;
    const px = size || 96;
    const svg = doc.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('viewBox', '0 0 100 96');
    svg.setAttribute('width', String(px));
    svg.setAttribute('height', String(Math.round(px * 0.96)));
    svg.setAttribute('class', 'al-sil');
    svg.setAttribute('aria-hidden', 'true');
    const path = doc.createElementNS(SVG_NS, 'path');
    path.setAttribute('d', d);
    if (name === 'clip') {
      path.setAttribute('fill', 'none');
      path.setAttribute('stroke', '#141410');
      path.setAttribute('stroke-width', '7');
      path.setAttribute('stroke-linecap', 'round');
    } else {
      path.setAttribute('class', 'al-sil-shape');
    }
    svg.appendChild(path);
    if (name === 'owl') {
      [[38, 46], [62, 46]].forEach(([ex, ey]) => {
        const eye = doc.createElementNS(SVG_NS, 'circle');
        eye.setAttribute('cx', String(ex));
        eye.setAttribute('cy', String(ey));
        eye.setAttribute('r', '9');
        eye.setAttribute('class', 'al-sil-eye');
        svg.appendChild(eye);
      });
    }
    return svg;
  }

  /** The shared parse (os.js), painted in paper's own materials: a **run** is
   *  highlighter, a __run__ is pen underline. Nodes, never innerHTML. */
  function paintRuns(host, text, inline) {
    host.textContent = '';
    parseRuns(text).forEach((runs) => {
      const p = inline ? host : el('p', 'al-p');
      runs.forEach((r) => {
        if (r.kind === 'kw') p.appendChild(el('mark', 'al-mark', r.text));
        else if (r.kind === 'pen') p.appendChild(el('u', 'al-pen', r.text));
        else p.appendChild(doc.createTextNode(r.text));
      });
      if (!inline) host.appendChild(p);
    });
  }

  /** The letterhead row: the head's first line left, the page mark right. Any
   *  further lines of the head sit under it as the typed sub-block. */
  function letterhead(sheet, head, right) {
    const lines = String(head == null ? '' : head).split('\n');
    const lh = el('div', 'al-lh');
    lh.appendChild(el('span', 'al-lh-t', lines[0] || ''));
    lh.appendChild(el('span', 'al-lh-n', right == null ? '' : String(right)));
    sheet.appendChild(lh);
    const rest = lines.slice(1).join('\n').trim();
    if (rest) sheet.appendChild(el('div', 'al-paper-head', rest));
  }

  /* ------------------------------------------------------ the four mounts */

  /** 'clip': a photo card pinned by a drawn paperclip. The silhouette printed
   *  SMALL and dark on a pale ground, the caption in the clerk's own hand. */
  function clipPhoto(att) {
    const box = el('div', 'al-photo');
    box.appendChild(el('span', 'al-clip'));
    const im = el('div', 'al-photo-im');
    const svg = silSvg(att.sil, 58);
    if (svg) im.appendChild(svg);
    box.appendChild(im);
    if (att.caption) box.appendChild(el('small', 'al-photo-cap', att.caption));
    return box;
  }

  /** 'staple': a white strip with two drawn staples, the figure in pen. The
   *  caption is the ink title ABOVE it, the way a clerk labels a cutting. */
  function stapleStrip(att, extraCls) {
    const strip = el('div', 'al-strip' + (extraCls ? ' ' + extraCls : ''));
    if (att.caption) strip.appendChild(el('div', 'al-strip-t', att.caption));
    try {
      strip.appendChild(renderChart(att.chart, { palette: 'pen', caption: att.caption }));
    } catch (e) {
      /* a bad row costs the figure, never the sheet it is stapled to */
      return null;
    }
    return strip;
  }

  /**
   * THE LIVE STRIP, and the only live thing on paper in this room. Honest or
   * absent: no caps function, a throw, an empty row or an all-zero row all
   * render NOTHING - no placeholder, no fake bars, no apology. The caption
   * the data carries already says it was drawn from the file.
   */
  function liveStrip(att) {
    let rows = null;
    try { rows = typeof c.attendance === 'function' ? c.attendance() : null; }
    catch (e) { rows = null; log('annex attendance threw'); }
    if (!Array.isArray(rows) || !rows.length) return null;
    const x = [];
    const y = [];
    let sum = 0;
    for (let i = 0; i < rows.length && i < 6; i++) {
      const v = Number(rows[i]);
      const n = isFinite(v) && v > 0 ? Math.round(v) : 0;
      x.push(i + 1);
      y.push(n);
      sum += n;
    }
    if (!sum) return null;
    return stapleStrip({ chart: { type: 'bars', x, y }, caption: att.caption }, 'al-strip-live');
  }

  /** 'sticky' and 'margin': the two handwritten mounts. A sticky hangs off
   *  the sheet edge; a margin note is written down the right hand side. */
  function stickyNote(text, cls) {
    const n = el('div', 'al-sticky' + (cls ? ' ' + cls : ''), String(text == null ? '' : text));
    return n;
  }

  /**
   * Hang a page's attachments on its sheet. The two STRIPS flow in the body
   * (they are stapled to the page); the photo, the sticky and the margin note
   * are pinned to the SHEET, outside the text column, and get a nudge each so
   * a page with two of a kind cannot stack them on one spot.
   */
  function mountAttachments(sheet, face, list) {
    const off = { sticky: 0, margin: 0, clip: 0 };
    (Array.isArray(list) ? list : []).forEach((att) => {
      if (!att) return;
      if (att.kind === 'chart' && att.mount === 'staple') {
        const strip = stapleStrip(att);
        if (strip) face.appendChild(strip);
        return;
      }
      if (att.kind === 'live') {
        const strip = liveStrip(att);
        if (strip) face.appendChild(strip);
        return;
      }
      if (att.kind === 'image' && att.mount === 'clip') {
        const photo = clipPhoto(att);
        if (off.clip) photo.style.top = (-14 + off.clip * 30) + 'px';
        off.clip += 1;
        /* the photo takes a column out of the first paragraph, the way it does
         * on a real page - the class is what tells the sheet to give it up */
        sheet.classList.add('has-photo');
        sheet.appendChild(photo);
        return;
      }
      if (att.kind === 'note' && att.mount === 'sticky') {
        const note = stickyNote(att.text);
        if (off.sticky) note.style.bottom = (60 + off.sticky * 34) + 'px';
        off.sticky += 1;
        sheet.appendChild(note);
        return;
      }
      if (att.kind === 'note' && att.mount === 'margin') {
        const note = el('div', 'al-margin', String(att.text == null ? '' : att.text));
        if (off.margin) note.style.top = (120 + off.margin * 40) + 'px';
        off.margin += 1;
        sheet.classList.add('has-margin');
        sheet.appendChild(note);
      }
    });
  }

  /* ----------------------------------------------------------- the shell */

  /** The sheet itself: a tone, a tilt, a shadow, and the close the layer has
   *  always had (the backdrop is the room, and the room puts it down). */
  function paperShell(onClose, cls) {
    const layer = el('div', 'al-paperlayer');
    const page = el('div', 'al-paper' + (cls ? ' ' + cls : ''));
    const x = el('button', 'al-paper-x', '×');
    x.setAttribute('aria-label', t('annex_paper_close', 'put it down'));
    x.addEventListener('click', onClose);
    layer.addEventListener('click', (e) => { if (e.target === layer) onClose(); });
    page.appendChild(x);
    layer.appendChild(page);
    root.appendChild(layer);   /* the ROOT, not the stage: paper is read, not painted */
    return { layer, page };
  }

  function closePaper() {
    if (!paperLayer) return;
    sfx('paper', 0.18);
    paperLayer.remove();
    paperLayer = null;
    paperBack = null;
    paper = null;
  }

  /* THE FLIP. A page with a `back` string is a page with two sides: the front
   * carries the material, the back carries the plain typed note somebody
   * filed with it. The turn is a 180ms squeeze and swap; .al-lite and
   * .arc-reduced swap on the spot, because the fold is decoration and the
   * text is not. */
  function armFlip(sheet, face, backText) {
    const flip = el('button', 'al-flip', t('annex_turn_over', 'turn over'));
    flip.addEventListener('click', () => turnPaper(!paperBack));
    sheet.classList.add('has-back');
    sheet.appendChild(flip);
    const back = el('div', 'al-backside');
    back.hidden = true;
    /* THE BACK IS PLAIN. It is a note somebody filed with the page, not a
     * marked-up document: the runs are parsed (so a stray ** never reaches
     * the reader) and then FLATTENED, so no highlighter and no pen. */
    back.textContent = '';
    parseRuns(backText).forEach((runs) => {
      let s = '';
      runs.forEach((r) => { s += r.text; });
      back.appendChild(el('p', 'al-p', s));
    });
    sheet.insertBefore(back, face.nextSibling);
    paper = { sheet, face, back, flip };
    paperBack = null;
  }

  function turnPaper(toBack) {
    if (!paper) return;
    const want = !!toBack;
    if (want === !!paperBack) return;
    sfx('paper', 0.22, { pitch: want ? 1.05 : 0.95 });
    const swap = () => {
      if (dead || !paper) return;
      paperBack = want ? 1 : null;
      paper.face.hidden = want;
      paper.back.hidden = !want;
      /* nothing that was pinned to the FRONT is on the back of a sheet, and
       * neither is the pager - you are holding one page, turned over */
      paper.sheet.classList.toggle('is-back', want);
      paper.flip.textContent = want
        ? t('annex_turn_back', 'turn back')
        : t('annex_turn_over', 'turn over');
      paper.sheet.classList.remove('is-turning');
    };
    if (lite) { swap(); return; }
    paper.sheet.classList.add('is-turning');
    later(swap, 180);
  }

  /* ------------------------------------------------------- the two papers */

  function openIntake() {
    if (paperLayer) return;
    const sub = c.subject || {};
    const { layer, page } = paperShell(closePaper);
    paperLayer = layer;
    paper = null;
    paperBack = null;
    letterhead(page, INTAKE_SHEET.head, t('annex_tab_fielddata', 'FIELD DATA'));

    const face = el('div', 'al-face');
    const dl = el('dl');
    INTAKE_SHEET.fields.forEach((f) => {
      const row = el('div', 'al-sheet-row');
      row.appendChild(el('dt', null, f.k));
      const dd = el('dd');
      paintRuns(dd, fill(f.v, sub), true);
      row.appendChild(dd);
      dl.appendChild(row);
    });
    face.appendChild(dl);
    face.appendChild(el('span', 'al-sheet-stamp', t('annex_stamp_ongoing', 'ONGOING')));
    page.appendChild(face);

    /* the credentials slip is the same -M hand as the summary note, so it is
     * the same object: a sticky, clipped to the right edge */
    if (INTAKE_SHEET.slip) page.appendChild(stickyNote(fill(INTAKE_SHEET.slip, sub), 'is-slip'));
    mountAttachments(page, face, INTAKE_SHEET.attachments);
    markPaperRead('intake');
  }

  function fill(s, sub) {
    return String(s)
      .replace('{code}', sub.code || '—')
      .replace('{date}', sub.date || '—')
      .replace('{password}', sub.password || '—');
  }

  function openDossier() {
    if (paperLayer) return;
    let idx = 0;
    const { layer, page } = paperShell(closePaper, 'is-letter');
    paperLayer = layer;
    const lhHost = el('div');
    const faceHost = el('div', 'al-face');
    const pager = el('div', 'al-pager');
    const prev = el('button', 'al-page-btn', '‹ ' + t('annex_page_prev', 'previous page'));
    const next = el('button', 'al-page-btn', t('annex_page_next', 'next page') + ' ›');
    prev.addEventListener('click', () => { idx = Math.max(0, idx - 1); paint(); });
    next.addEventListener('click', () => { idx = Math.min(MASCOT_PAGES.length - 1, idx + 1); paint(); });
    pager.appendChild(prev);
    pager.appendChild(next);
    page.appendChild(lhHost);
    page.appendChild(faceHost);
    page.appendChild(pager);

    /** Every mount a page pinned to the SHEET rather than to its body, so a
     *  turn of the page does not leave the last page's sticky behind. */
    function clearPinned() {
      const pinned = page.querySelectorAll('.al-photo, .al-sticky, .al-margin, .al-flip, .al-backside');
      for (let i = 0; i < pinned.length; i++) pinned[i].remove();
      page.classList.remove('has-back');
      page.classList.remove('has-photo');
      page.classList.remove('has-margin');
      paper = null;
      paperBack = null;
    }

    function paint() {
      const pg = MASCOT_PAGES[idx];
      clearPinned();
      lhHost.textContent = '';
      letterhead(lhHost, pg.head,
        fmtPage(idx + 1, MASCOT_PAGES.length));
      faceHost.textContent = '';
      faceHost.hidden = false;

      const atts = Array.isArray(pg.attachments) ? pg.attachments : [];
      const hasPhoto = atts.some((a) => a && a.kind === 'image' && a.mount === 'clip');
      /* a page WITHOUT its own photograph keeps the old centred plate; a page
       * that carries one is a photographed page and the plate steps aside */
      if (pg.sil && !hasPhoto) {
        const svg = silSvg(pg.sil, 96);
        if (svg) {
          faceHost.appendChild(svg);
          faceHost.appendChild(el('span', 'al-namebar'));
        }
      }
      const bodyEl = el('div', 'al-paper-body');
      paintRuns(bodyEl, pg.body);
      faceHost.appendChild(bodyEl);

      mountAttachments(page, faceHost, atts);
      if (pg.back) armFlip(page, faceHost, pg.back);

      prev.disabled = idx === 0;
      next.disabled = idx === MASCOT_PAGES.length - 1;
      if (pg.id === 'owl' && !st.p4) save({ p4: 1 });  /* read, not opened */
      if (idx > 0) sfx('paper', 0.16);
    }
    paint();
  }

  /** '{i} / {n}', the one number the letterhead carries. */
  function fmtPage(i, n) {
    return String(t('annex_page_of', '{i} / {n}'))
      .split('{i}').join(String(i))
      .split('{n}').join(String(n));
  }

  /* ========================================================================
   * THE DRAWER (Plate 3). The shelf binder opens a drawer BEFORE it opens a
   * paper: three manila tabs, rows inside them, and the sheet lands on top
   * (z 6 over the drawer's 5) so putting it down returns you to the drawer
   * rather than to the shelf. A layer, not a screen, and one Esc rung.
   * ==================================================================== */

  function openDrawer() {
    if (drawerLayer) return;
    const layer = el('div', 'al-drawerlayer');
    layer.setAttribute('role', 'region');
    layer.setAttribute('aria-label', t('annex_drawer_label', 'the binder drawer'));
    layer.addEventListener('click', (e) => { if (e.target === layer) closeDrawer(); });
    const box = el('div', 'al-drawer');
    const x = el('button', 'al-paper-x', '×');
    x.setAttribute('aria-label', t('annex_drawer_close', 'put it back'));
    x.addEventListener('click', () => closeDrawer());
    box.appendChild(x);
    const tabs = el('div', 'al-tabs');

    DRAWER.forEach((tab) => {
      const unread = tab.unread && !paperRead(tab.unread);
      const col = el('div', 'al-tab' + (unread ? ' is-new' : ''));
      col.appendChild(el('span', 'al-tab-lab', t(tab.key, tab.fb)));
      if (unread) col.appendChild(el('span', 'al-tab-new', t('annex_tab_unread', 'unread')));
      tab.rows.forEach((r) => {
        if (r.withheld) {
          const dead2 = el('div', 'al-row is-mute');
          const bar = el('span', 'al-bar-black');
          bar.setAttribute('role', 'img');
          bar.setAttribute('aria-label', t('annex_row_withheld', 'withheld'));
          dead2.appendChild(bar);
          dead2.appendChild(el('i', 'al-row-n', t('annex_row_withheld', 'withheld')));
          col.appendChild(dead2);
          markPaperBar('drawer:withheld');
          return;
        }
        const live = !!r.open;
        const row = el(live ? 'button' : 'div', 'al-row' + (live ? '' : ' is-mute'));
        row.appendChild(el('span', 'al-row-t', r.label));
        if (r.at) row.appendChild(el('i', 'al-row-at', t('annex_row_terminal', 'on the terminal')));
        if (r.note) row.appendChild(el('i', 'al-row-n', r.note));
        if (live) {
          row.addEventListener('click', () => {
            sfx('paper', 0.28);
            if (r.open === 'intake') openIntake();
            repaintTabs();
          });
        }
        col.appendChild(row);
      });
      tabs.appendChild(col);
    });

    box.appendChild(tabs);
    layer.appendChild(box);
    root.appendChild(layer);   /* the ROOT, same reason the sheet is there */
    drawerLayer = layer;

    /* the amber edge is a READ state, so it goes out the moment the sheet it
     * was about is opened - repainted in place, never a rebuilt drawer */
    function repaintTabs() {
      const cols = tabs.querySelectorAll('.al-tab');
      DRAWER.forEach((tab, i) => {
        const col = cols[i];
        if (!col || !tab.unread) return;
        if (paperRead(tab.unread)) {
          col.classList.remove('is-new');
          const chip = col.querySelector('.al-tab-new');
          if (chip) chip.remove();
        }
      });
    }
  }

  function closeDrawer() {
    if (!drawerLayer) return;
    sfx('paper', 0.18);
    drawerLayer.remove();
    drawerLayer = null;
  }

  /* -------------------------------------------------------------- descent */

  function playDescent(firstVisit) {
    descent = el('div', 'al-descent');
    root.appendChild(descent);
    if (lite || !firstVisit) {
      later(() => dropDescent(), firstVisit ? 400 : 250);
      if (firstVisit) sfx('door', 0.35);
      return;
    }
    sfx('door', 0.35);
    [0.95, 0.86, 0.78, 0.7].forEach((pitch, i) => {
      later(() => sfx('thud', 0.32, { pitch }), 420 + i * 380);
    });
    later(() => dropDescent(), 2200);
  }
  function dropDescent() {
    if (!descent) return;
    const d = descent;
    descent = null;
    d.classList.add('is-out');
    later(() => d.remove(), 460);
  }

  /* ----------------------------------------------------------- the ladder */

  /** Inner Esc rungs, inward-out. False means the shell walks home.
   *  The sheet's BACK is the innermost rung of all: a player who turned a
   *  page over is holding one thing, and Esc turns it back before it puts
   *  anything down (the modal you opened one press ago closes first). */
  function escapeStep() {
    if (descent) { dropDescent(); return true; }
    if (paperBack) { turnPaper(false); return true; }
    if (paperLayer) { closePaper(); return true; }
    if (drawerLayer) { closeDrawer(); return true; }
    if (os) {
      if (os.escapeStep()) return true;
      closeOs();
      return true;
    }
    if (view && view !== 'wide') { sfx('door', 0.3); showView('wide'); return true; }
    return false;
  }

  function destroy() {
    if (dead) return;
    dead = true;
    timers.forEach((id) => clearTimeout(id));
    window.removeEventListener('resize', onResize);
    if (os) { try { os.destroy(); } catch (e) { /* noop */ } os = null; }
    if (wall) { try { wall.destroy(); } catch (e) { /* noop */ } wall = null; }
    try { root.remove(); } catch (e) { /* noop */ }
  }

  /* ----------------------------------------------------------------- boot */

  const firstVisit = !st.visited;
  if (firstVisit) save({ visited: 1 });
  showView('wide');
  playDescent(firstVisit);
  fitStage();

  return { root, escapeStep, destroy, fit: fitStage };
}

export default createAnnexLab;
