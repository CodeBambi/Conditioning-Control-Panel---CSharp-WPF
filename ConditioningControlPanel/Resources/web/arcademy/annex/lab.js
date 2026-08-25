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
 * - The monitor wall reuses createCamWall (lifted, not copied) and composites
 *   feeds under the bezels through the screenmask, red channel to alpha,
 *   hidden until the mask lands (a naked feed over the art reads as a bug).
 * - Esc folds inward-out: paper, then OS window, then the laptop, then the
 *   close-up, and the shell's own rung walks home from the wide shot.
 * - Punch 4 stamps when the owl page is READ, not when the folder opens.
 * - Assets resolve module-relative (the nine-broken-logos law).
 * ==========================================================================*/

import { createCamWall } from './cams.js';
import { createAnnexOs } from './os.js';
import { MASCOT_PAGES, INTAKE_SHEET } from './docs.js';

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
    Object.freeze([600, 320, 230, 265, 'intake', 'annex_hot_binder', 'FIELD DATA']),
  ]),
});

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
  let descent = null;
  let quads = null;
  let quadsWanted = false;
  const timers = [];

  const st = Object.assign({ visited: 0, os: 0, p1: 0, p2: 0, p3: 0, p4: 0 },
    (typeof c.annexState === 'function' ? c.annexState() : null) || {});
  function save(patch) {
    Object.assign(st, patch);
    try { if (typeof c.saveAnnex === 'function') c.saveAnnex(patch); }
    catch (e) { log('annex save failed'); }
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

  function fitStage() {
    try {
      const w = root.clientWidth || STAGE_W;
      const h = root.clientHeight || STAGE_H;
      const s = Math.min(w / STAGE_W, h / STAGE_H);
      stage.style.transform = 'translate(-50%, -50%) scale(' + s + ')';
    } catch (e) { /* noop */ }
  }
  const onResize = () => fitStage();
  window.addEventListener('resize', onResize);

  /* --------------------------------------------------------------- views */

  function showView(name) {
    if (dead || view === name) return;
    const entering = view !== null;
    view = name;
    if (name !== 'monitors' && wall) wall.stop();

    stage.textContent = '';
    const art = el('img', 'al-art');
    art.alt = '';
    art.src = ART_BASE + VIEW_ART[name];
    stage.appendChild(art);

    if (name === 'monitors') mountFeeds();

    (HOTSPOTS[name] || []).forEach((h) => stage.appendChild(hotspot(h)));
    if (name === 'monitors') mountLaptopHotspot();

    if (name !== 'wide') {
      const back = el('button', 'al-back', '‹ ' + t('annex_back', 'step back'));
      back.addEventListener('click', () => stepBack());
      stage.appendChild(back);
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
    if (action === 'intake') { sfx('paper', 0.28); openIntake(); return; }
    if (action === 'laptop') { openOs(); return; }
    sfx('blip', 0.16, { pitch: 1.05 });
    showView(action);
  }

  /** The pill's verb, and the backdrop's. ONE step, and it never folds an OS
   *  window: escapeStep() walks inward-out (a window at a time), but a player
   *  who reaches for "step back" is leaving the terminal, not tidying it. So
   *  the ladder here is paper -> the whole OS -> the close-up itself. */
  function stepBack() {
    if (dead) return;
    if (paperLayer) { closePaper(); return; }
    if (os) { closeOs(); return; }
    if (view && view !== 'wide') { sfx('door', 0.3); showView('wide'); }
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
    stage.appendChild(osLayer);

    /* zoom out of the laptop glass: origin at its center, scale up */
    const s2 = quads && (quads.screens || []).find((q) => q.name === 'laptop');
    if (s2 && s2.bbox && !lite) {
      const cx = s2.bbox[0] + s2.bbox[2] / 2;
      const cy = s2.bbox[1] + s2.bbox[3] / 2;
      frame.style.transformOrigin = cx + 'px ' + cy + 'px';
      frame.classList.add('is-zooming');
      frame.style.transform = 'scale(' + (s2.bbox[2] / STAGE_W) + ')';
      later(() => { frame.style.transform = 'scale(1)'; frame.classList.remove('is-zooming'); }, 30);
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
    });
    frame.appendChild(os.root);
    later(() => { if (os) os.fit(); }, 60);
  }

  function closeOs() {
    if (!os) return;
    sfx('blip', 0.16, { pitch: 0.9 });
    os.destroy();
    os = null;
    if (osLayer) { osLayer.remove(); osLayer = null; }
  }

  /* --------------------------------------------------------------- papers */

  function paperShell(onClose) {
    const layer = el('div', 'al-paperlayer');
    const page = el('div', 'al-paper');
    const x = el('button', 'al-paper-x', '×');
    x.setAttribute('aria-label', t('annex_paper_close', 'put it down'));
    x.addEventListener('click', onClose);
    layer.addEventListener('click', (e) => { if (e.target === layer) onClose(); });
    page.appendChild(x);
    layer.appendChild(page);
    stage.appendChild(layer);
    return { layer, page };
  }

  function closePaper() {
    if (!paperLayer) return;
    sfx('paper', 0.18);
    paperLayer.remove();
    paperLayer = null;
  }

  function openIntake() {
    if (paperLayer) return;
    const sub = c.subject || {};
    const { layer, page } = paperShell(closePaper);
    paperLayer = layer;
    page.appendChild(el('div', 'al-paper-head', INTAKE_SHEET.head));
    const dl = el('dl');
    INTAKE_SHEET.fields.forEach((f) => {
      const row = el('div', 'al-sheet-row');
      row.appendChild(el('dt', null, f.k));
      row.appendChild(el('dd', null, fill(f.v, sub)));
      dl.appendChild(row);
    });
    page.appendChild(dl);
    page.appendChild(el('span', 'al-sheet-stamp', t('annex_stamp_ongoing', 'ONGOING')));
    page.appendChild(el('div', 'al-slip', fill(INTAKE_SHEET.slip, sub)));
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
    const { layer, page } = paperShell(closePaper);
    paperLayer = layer;
    const headEl = el('div', 'al-paper-head');
    const silBox = el('div');
    const bodyEl = el('div', 'al-paper-body');
    const pager = el('div', 'al-pager');
    const prev = el('button', 'al-page-btn', '‹ ' + t('annex_page_prev', 'previous page'));
    const next = el('button', 'al-page-btn', t('annex_page_next', 'next page') + ' ›');
    prev.addEventListener('click', () => { idx = Math.max(0, idx - 1); paint(); });
    next.addEventListener('click', () => { idx = Math.min(MASCOT_PAGES.length - 1, idx + 1); paint(); });
    pager.appendChild(prev);
    pager.appendChild(next);
    page.appendChild(headEl);
    page.appendChild(silBox);
    page.appendChild(bodyEl);
    page.appendChild(pager);

    function paint() {
      const pg = MASCOT_PAGES[idx];
      headEl.textContent = pg.head;
      bodyEl.textContent = pg.body;
      silBox.textContent = '';
      if (pg.sil && SILS[pg.sil]) {
        const svg = doc.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 100 96');
        svg.setAttribute('width', '96');
        svg.setAttribute('height', '92');
        svg.setAttribute('class', 'al-sil');
        svg.setAttribute('aria-hidden', 'true');
        const path = doc.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', SILS[pg.sil]);
        if (pg.sil === 'clip') {
          path.setAttribute('fill', 'none');
          path.setAttribute('stroke', '#141410');
          path.setAttribute('stroke-width', '7');
          path.setAttribute('stroke-linecap', 'round');
        } else {
          path.setAttribute('class', 'al-sil-shape');
        }
        svg.appendChild(path);
        if (pg.sil === 'owl') {
          [[38, 46], [62, 46]].forEach(([ex, ey]) => {
            const eye = doc.createElementNS('http://www.w3.org/2000/svg', 'circle');
            eye.setAttribute('cx', ex);
            eye.setAttribute('cy', ey);
            eye.setAttribute('r', '9');
            eye.setAttribute('class', 'al-sil-eye');
            svg.appendChild(eye);
          });
        }
        silBox.appendChild(svg);
        silBox.appendChild(el('span', 'al-namebar'));
      }
      prev.disabled = idx === 0;
      next.disabled = idx === MASCOT_PAGES.length - 1;
      if (pg.id === 'owl' && !st.p4) save({ p4: 1 });  /* read, not opened */
      if (idx > 0) sfx('paper', 0.16);
    }
    paint();
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

  /** Inner Esc rungs, inward-out. False means the shell walks home. */
  function escapeStep() {
    if (descent) { dropDescent(); return true; }
    if (paperLayer) { closePaper(); return true; }
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
