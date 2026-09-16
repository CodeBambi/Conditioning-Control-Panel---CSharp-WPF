/* ============================================================
 * The Ministry of Sweet Nothings — the Sorting Parlour, booth logic.
 * All coordinates are normalized [0,1] against the DISPLAYED
 * image rect (boxLayer is fitted to it), which equals the frame
 * the host decoded — so what we send is frame-normalized too.
 *
 * NO USER-FACING COPY LIVES IN THIS FILE. Every readable string comes
 * from the tables in story.js (STORY for the story surfaces, UI_COPY for
 * the chrome). If you are about to type a sentence a Keeper can read,
 * put it in story.js and reference it from here.
 * ============================================================ */
'use strict';

const $ = (id) => document.getElementById(id);

// LOCKED label doctrine (author ruling): the stamp-rack class labels are NOT
// part of the fiction skin and never change with it. Wire stays indices 0-3.
const CLASS_NAMES = ['B**BS', 'PU**Y', 'A**', 'FACE'];
const CLASS_VARS  = ['--c0', '--c1', '--c2', '--c3'];
const RANKS = UI_COPY.rank.titles;   // t0..t4 — the tier NUMBER is the wire value
const MIN_SIDE = 0.012;          // page-side floor (server floor is 0.005)
const MAX_BOXES = 8;
const QUEUE_LOW = 4;             // ask for more casework below this
const BATCH_ASK = 12;

const S = {
  booted: false,
  hasAuth: false,
  admin: false,
  certify: false,              // gold desk armed (admin only)
  demo: false,
  queue: [],                   // [{target, src, dims}]
  current: null,
  seals: [],                   // [{c,x,y,w,h,el}]
  awaiting: false,             // a submit is in flight
  history: [],                 // last filed dossiers [{item, boxes}], newest last (cap 10)
  amending: null,              // history entry currently recalled for amendment
  exhausted: false,
  fetching: false,
  profile: null,
  stats: null,
  quests: null,                // profile.quests (null = old server / no data)
  dayEndsAt: 0,                // client clock target for the shift-rollover countdown
  filedSinceTray: 0,
  seen: new Set(),             // session-level dupe guard
  hashSeen: new Map(),         // gif hash -> last-shown sequence number
  hashSeq: 0,
  shiftStarted: false,
  lockers: [],                 // [{id,name,installed,images}]
  selected: null,              // loaded locker id ('all' = full archive); null = booth idle
  reqLocker: null,             // locker shown in the requisition gate
  catalogueUrl: 'https://discord.com/channels/1456573221489999934/1511409848699584653',
  session: null,               // per-shift ledger counters (set at clock-in; see freshSession)
  pendingNote: null,           // {hand,text} story note queued for the next dossier
};

const SOFT_CAP = 150;          // daily soft XP cap (mirrors server XP.SOFT) — shown in the ledger
// Per-shift counters powering the END-OF-SHIFT LEDGER (Feature 2). Session-scoped,
// reset at clock-in; nothing here goes over the wire.
function freshSession() {
  return { filed: 0, seals: 0, golds: 0, cleans: 0,
    grades: { S: 0, A: 0, B: 0, C: 0 }, best: null, xp: 0, pending: 0 };
}
S.session = freshSession();

/* ================= static copy (the one seam) =================
 * index.html ships with EMPTY chrome; this stamps it from the story.js
 * tables before anything is visible (the #boot curtain is still down).
 *   data-copy       -> textContent
 *   data-copy-html  -> array of lines, escaped, joined with <br>
 *   data-copy-title -> title attribute
 *   data-copy-aria  -> aria-label attribute
 * Paths are rooted at `ui.` (UI_COPY) or `story.` (STORY); numeric
 * segments index arrays. A miss is skipped silently and leaves the
 * element empty — copy must never be able to throw the page. */
const COPY_ROOTS = { ui: UI_COPY, story: STORY };
function copyAt(path) {
  const parts = String(path || '').split('.');
  let v = COPY_ROOTS[parts[0]];
  for (let i = 1; i < parts.length && v != null; i++) v = v[parts[i]];
  return v;
}
function applyStaticCopy(root) {
  (root || document).querySelectorAll('[data-copy]').forEach((el) => {
    const v = copyAt(el.dataset.copy);
    if (typeof v === 'string') el.textContent = v;
  });
  (root || document).querySelectorAll('[data-copy-html]').forEach((el) => {
    const v = copyAt(el.dataset.copyHtml);
    // Author-written constants only, and escaped anyway — <br> is the sole markup.
    if (Array.isArray(v)) el.innerHTML = v.map(escapeHtml).join('<br>');
  });
  (root || document).querySelectorAll('[data-copy-title]').forEach((el) => {
    const v = copyAt(el.dataset.copyTitle);
    if (typeof v === 'string') el.title = v;
  });
  (root || document).querySelectorAll('[data-copy-aria]').forEach((el) => {
    const v = copyAt(el.dataset.copyAria);
    if (typeof v === 'string') el.setAttribute('aria-label', v);
  });
  document.title = UI_COPY.doc.title;
}
applyStaticCopy();

/* ================= boot sequence ================= */
(function boot() {
  const text = UI_COPY.boot.lines(Bridge.isApp);
  const bt = $('bootText');
  let i = 0;
  const typer = setInterval(() => {
    i += 3;
    bt.textContent = text.slice(0, i);
    if (i >= text.length) {
      clearInterval(typer);
      bt.innerHTML = text + '<span class="cursor">▌</span>';
      setTimeout(dismissBoot, 900);
    }
  }, 16);
  $('boot').onclick = () => { clearInterval(typer); dismissBoot(); };
  function dismissBoot() { $('boot').classList.add('gone'); }
})();

/* ================= bridge lifecycle ================= */
Bridge.send('ready');

// Heartbeat off rAF so a wedged page stops beating (host watchdog trips at 20s).
(function heartbeat() {
  let last = 0;
  function tick(t) {
    if (t - last > 4000) { last = t; Bridge.send('heartbeat'); }
    requestAnimationFrame(tick);
  }
  requestAnimationFrame(tick);
})();

Bridge.on('init', (m) => {
  S.booted = true;
  S.hasAuth = !!m.hasAuth;
  S.admin = !!m.admin;
  S.demo = !!m.demo;
  if (S.demo) { $('demoBanner').classList.add('show'); $('subtitle').textContent = UI_COPY.doc.subtitleDemo; }
  if (S.admin) { $('goldDesk').classList.add('show'); $('subtitle').textContent = UI_COPY.doc.subtitleGold; }

  if (!S.hasAuth) { showGate('gateLogin'); return; }
  if (m.indexing && !m.indexing.ready) {
    showGate('gateIndex');
    updateIndexGate(m.indexing);
  } else {
    startShift();
  }
});

Bridge.on('index-progress', (m) => {
  updateIndexGate(m);
  if (m.ready) { hideGate('gateIndex'); startShift(); }
});

Bridge.on('end-run', () => {
  Bridge.send('exit-done');
});

function updateIndexGate(ix) {
  const pct = ix.total > 0 ? Math.round((ix.done / ix.total) * 100) : 0;
  $('gateIndexBar').style.width = pct + '%';
  $('gateIndexPct').textContent = UI_COPY.gates.index.progress({ pct, done: ix.done, total: ix.total });
}

function showGate(id) { $(id).classList.add('show'); }
function hideGate(id) { $(id).classList.remove('show'); }
$('gateLoginClose').onclick = () => Bridge.send('exit');
$('gateNoContentClose').onclick = () => Bridge.send('exit');
$('gateClockoutClose').onclick = () => Bridge.send('exit');

function startShift() {
  if (S.shiftStarted) { Bridge.send('packs'); return; }   // re-entry after a re-index
  S.shiftStarted = true;
  setButtonsEnabled(false);
  setHint(UI_COPY.chests.pick);
  Bridge.send('packs');
  requestStats();
  setInterval(requestStats, 60000);
}

/* ================= keepsake chests (pack rail) =================
 * Fiction: a Keepsake Chest is one Sweetheart's collected pictures.
 * Mechanically unchanged — the ids, the 'all' synthetic entry and every
 * wire field keep their old names on purpose. */
Bridge.on('packs', (m) => {
  S.lockers = m.lockers || [];
  if (m.catalogueUrl) S.catalogueUrl = m.catalogueUrl;
  renderLockers();
  if (S.autoloadPack) {
    const id = S.autoloadPack;
    S.autoloadPack = null;
    if (S.lockers.some((l) => l.id === id && l.installed)) loadLocker(id);
  }
});

function renderLockers() {
  const list = $('lockerList');
  list.innerHTML = '';
  const installed = S.lockers.filter((l) => l.installed);
  if (S.lockers.length === 0) {
    list.innerHTML = `<div class="lockerHint">${escapeHtml(UI_COPY.chests.silent)}</div>`;
    return;
  }
  const entries = [];
  if (installed.length > 0) {
    entries.push({
      id: 'all', name: UI_COPY.chests.allName, installed: true,
      images: installed.reduce((a, l) => a + (l.images || 0), 0),
    });
  }
  entries.push(...S.lockers);

  for (const l of entries) {
    const d = document.createElement('div');
    d.className = 'locker' + (l.installed ? '' : ' ghost') + (S.selected === l.id ? ' sel' : '');
    d.innerHTML =
      '<div class="folder"><div class="sheets"></div></div>' +
      `<div class="lname">${escapeHtml(l.name)}</div>` +
      `<div class="lstat">${escapeHtml(l.installed
        ? (S.selected === l.id ? UI_COPY.chests.statOpen : UI_COPY.chests.statFiled(fmt(l.images)))
        : UI_COPY.chests.statMissing(fmt(l.images)))}</div>` +
      `<div class="loadTag">${escapeHtml(l.installed
        ? (S.selected === l.id ? UI_COPY.chests.tagInPlay : UI_COPY.chests.tagLoad)
        : UI_COPY.chests.tagRequisition)}</div>`;
    d.onclick = () => l.installed ? loadLocker(l.id) : showRequisition(l);
    list.appendChild(d);
  }
}
function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function loadLocker(id) {
  if (S.selected === id || S.awaiting) return;
  S.selected = id;
  S.queue = [];
  S.history = [];              // recalled cases from another locker would be confusing
  S.amending = null;
  updateRewindBtn();
  updateFileBtn();
  S.exhausted = false;
  S.fetching = false;
  resetWireRetries();
  renderLockers();
  const name = id === 'all' ? UI_COPY.chests.allTitle : (S.lockers.find((l) => l.id === id)?.name || id).toUpperCase();
  setHint(UI_COPY.chests.pulling(name));
  if (S.current) { slideOut(() => { S.current = null; requestBatch(); }); }
  else requestBatch();
}

function showRequisition(l) {
  S.reqLocker = l;
  $('reqPackName').textContent = l.name || UI_COPY.gates.requisition.fallbackName;
  showGate('gateRequisition');
}
$('reqOpenCatalogue').onclick = () => {
  if (Bridge.isApp) Bridge.send('open-catalogue');
  else window.open(S.catalogueUrl, '_blank');
};
$('reqClose').onclick = () => hideGate('gateRequisition');

/* ================= casework queue ================= */
// A locker whose targets the booth can't decode (pack uninstalled mid-shift,
// frame index past the end of the gif, decoder drift) answers every pull with
// zero items and exhausted:false. Retry, but with a ceiling — otherwise the
// booth polls the wire every 8s forever, decoding nothing.
const WIRE_RETRY_MAX = 5;
const WIRE_RETRY_MS = [8000, 12000, 20000, 30000, 45000];
let wireRetries = 0;
let wireTimer = 0;

function resetWireRetries() {
  wireRetries = 0;
  if (wireTimer) { clearTimeout(wireTimer); wireTimer = 0; }
}

// The parlour (or the gold desk's draft pile) has nothing left to serve.
function showDrained() {
  if (S.certify) setHint(UI_COPY.desk.drainedDrafts);
  else if (S.lockers.some((l) => l.installed)) setHint(UI_COPY.desk.drainedCases);
  else showGate('gateNoContent');
}

function requestBatch() {
  if (S.fetching || S.exhausted || !S.selected) return;
  S.fetching = true;
  const req = { count: BATCH_ASK };
  if (S.selected !== 'all') req.pack = S.selected;
  if (S.certify) req.certify = true;   // gold desk pulls the AI-proposed drafts
  Bridge.send('next', req);
}

Bridge.on('batch', (m) => {
  S.fetching = false;
  if (m.auth === false) { showGate('gateLogin'); return; }
  if (m.profile) applyProfile(m.profile);
  const fresh = (m.items || []).filter((it) => !S.seen.has(it.target));
  fresh.forEach((it) => S.seen.add(it.target));
  S.queue.push(...fresh);
  // Latch the drained flag whatever the desk is doing — arming it only when the
  // queue happened to be empty meant a batch that landed mid-case was dropped
  // and every following filing pulled the empty wire again.
  if (m.exhausted) S.exhausted = true;
  if (fresh.length) resetWireRetries();
  if (S.exhausted && S.queue.length === 0 && !S.current) { showDrained(); return; }
  if (fresh.length === 0 && !S.exhausted && S.queue.length === 0 && !S.current) {
    // Nothing decodable this pull (server hiccup, or this locker's frames won't
    // open here). Back off, and stop rather than poll the wire forever.
    if (wireRetries >= WIRE_RETRY_MAX) {
      setHint(UI_COPY.desk.unreadable);
      return;
    }
    const wait = WIRE_RETRY_MS[Math.min(wireRetries, WIRE_RETRY_MS.length - 1)];
    wireRetries++;
    setHint(UI_COPY.desk.quiet(wireRetries, WIRE_RETRY_MAX));
    if (wireTimer) clearTimeout(wireTimer);
    wireTimer = setTimeout(() => { wireTimer = 0; requestBatch(); }, wait);
    return;
  }
  if (!S.current) nextCase();
});

// Frames of one GIF share the hash half of the target id. Serve the queued
// case whose file was shown longest ago (never-shown wins outright) so the
// same GIF only comes back once everything else on the desk has cycled.
const hashOf = (t) => String(t).split(':')[0];
function pickNext() {
  if (S.queue.length === 0) return null;
  if (S.queue[0]._stash) { delete S.queue[0]._stash; return S.queue.shift(); }
  let best = 0, bestSeq = Infinity;
  for (let i = 0; i < S.queue.length; i++) {
    const seq = S.hashSeen.get(hashOf(S.queue[i].target));
    const s = seq === undefined ? -1 : seq;
    if (s < bestSeq) { bestSeq = s; best = i; if (s === -1) break; }
  }
  return S.queue.splice(best, 1)[0];
}

function nextCase() {
  rewindBusy = false;          // a fresh landing always unfreezes the recall path
  if (S.queue.length < QUEUE_LOW) requestBatch();
  const item = pickNext();
  if (!item) {
    if (S.exhausted) showDrained();
    else setHint(UI_COPY.desk.waiting);
    return;
  }
  S.hashSeen.set(hashOf(item.target), ++S.hashSeq);
  S.amending = null;
  S.current = item;
  clearSeals();
  clearCaseNote();             // a note clipped to the previous case doesn't ride along
  // Gold drafts arrive pre-boxed by the machine clerk: place the proposed
  // seals so certifying is drag/resize/void, not boxing from scratch.
  const drafted = S.certify && Array.isArray(item.boxes);
  if (drafted) {
    for (const b of item.boxes) {
      const seal = { c: b.c, x: b.x, y: b.y, w: b.w, h: b.h, el: mkSealEl(b.c) };
      layer.appendChild(seal.el);
      styleSeal(seal);
      S.seals.push(seal);
      dressSeal(seal);
      startPixelation(seal);
    }
  }
  setDocket(item);
  const img = $('scanImg');
  img.onload = fitBoxLayer;
  img.src = item.src;
  slideIn();
  setHint(drafted ? UI_COPY.desk.hintDrafted : UI_COPY.desk.hintDraw);
  setButtonsEnabled(true);
  updateFileBtn();
  updateRewindBtn();
  // First real case of the session (casework only, never the gold desk) opens the shift.
  if (!clockedIn && !S.certify) runClockIn();
  renderPendingNote();         // a story beat may have queued a note for this dossier
}

function setDocket(item) {
  const docket = item.target.slice(0, 5).toUpperCase() + '-' + item.target.slice(65);
  $('caseNo').textContent = '#' + docket;
  $('caseTag').textContent = S.certify ? UI_COPY.desk.tagDraft : UI_COPY.desk.tagKeepsake;
  $('caseTag').classList.toggle('goldTag', S.certify);
}

/* dossier slide animations */
const dossier = $('dossier');
function slideOut(cb) {
  dossier.classList.add('out');
  setTimeout(cb, 380);
}
function slideIn() {
  dossier.classList.remove('out');
  dossier.classList.add('in');
  resetZoom();                 // every fresh dossier lands at 1:1
  Sound.play('dossier');
  requestAnimationFrame(() => requestAnimationFrame(() => dossier.classList.remove('in')));
}

/* ================= image fitting ================= */
// boxLayer is pinned to the letterboxed image rect inside the light-table,
// so % positions on it ARE frame-normalized coordinates.
function fitBoxLayer() {
  const wrap = $('scanWrap'), img = $('scanImg'), layer = $('boxLayer');
  const iw = img.naturalWidth || 1, ih = img.naturalHeight || 1;
  const ww = wrap.clientWidth, wh = wrap.clientHeight;
  const scale = Math.min(ww / iw, wh / ih);
  const dw = iw * scale, dh = ih * scale;
  layer.style.left = ((ww - dw) / 2) + 'px';
  layer.style.top = ((wh - dh) / 2) + 'px';
  layer.style.width = dw + 'px';
  layer.style.height = dh + 'px';
}
window.addEventListener('resize', fitBoxLayer);

/* ================= seals (draw / adjust / void) ================= */
const layer = $('boxLayer');
const wrap = $('scanWrap');
let gesture = null;   // {mode:'draw'|'resize', seal, anchor:{x,y}} | {mode:'tap'|'move', seal, ...}

function layerPt(e) {
  const r = layer.getBoundingClientRect();
  if (r.width < 2 || r.height < 2) return null;
  return {
    x: Math.min(Math.max((e.clientX - r.left) / r.width, 0), 1),
    y: Math.min(Math.max((e.clientY - r.top) / r.height, 0), 1),
  };
}

wrap.addEventListener('pointerdown', (e) => {
  // Loupe: press-and-hold superzoom under the cursor (armed via the rail button).
  if (lensMode && e.button === 0 && S.current) {
    gesture = { mode: 'lens' };
    wrap.setPointerCapture(e.pointerId);
    $('loupe').classList.add('show');
    moveLoupe(e);
    e.preventDefault();
    return;
  }
  // Middle-drag pans the zoomed scan.
  if (e.button === 1) {
    if (Zoom.z > 1.001) {
      gesture = { mode: 'pan', sx: e.clientX, sy: e.clientY, tx: Zoom.tx, ty: Zoom.ty };
      wrap.setPointerCapture(e.pointerId);
      e.preventDefault();
    }
    return;
  }
  if (S.awaiting || !S.current) return;
  const t = e.target;
  if (t.classList && t.classList.contains('x')) return;      // ✕ handled on click
  const p = layerPt(e);
  if (!p) return;

  if (t.classList && t.classList.contains('h')) {
    // Corner adjust: anchor = the opposite corner of that seal.
    const el = t.parentElement;
    const seal = S.seals.find((s) => s.el === el);
    if (!seal) return;
    const anchor = {
      x: t.classList.contains('nw') || t.classList.contains('sw') ? seal.x + seal.w : seal.x,
      y: t.classList.contains('nw') || t.classList.contains('ne') ? seal.y + seal.h : seal.y,
    };
    gesture = { mode: 'resize', seal, anchor };
  } else if (t.classList && t.classList.contains('seal')) {
    // Click a placed seal to void it; dragging it >8px instead MOVES the seal
    // (dx/dy = grab offset from the seal's origin, in normalized coords).
    const seal = S.seals.find((s) => s.el === t);
    if (!seal) return;
    gesture = { mode: 'tap', seal, cx: e.clientX, cy: e.clientY, dx: p.x - seal.x, dy: p.y - seal.y };
  } else {
    if (S.seals.length >= MAX_BOXES) { setHint(UI_COPY.desk.sealLimit); Sound.play('denied'); return; }
    const seal = { c: curClass, x: p.x, y: p.y, w: 0, h: 0, el: mkSealEl(curClass) };
    layer.appendChild(seal.el);
    gesture = { mode: 'draw', seal, anchor: p, provisional: true };
  }
  wrap.setPointerCapture(e.pointerId);
  e.preventDefault();
});

wrap.addEventListener('pointermove', (e) => {
  if (!gesture) return;
  if (gesture.mode === 'lens') { moveLoupe(e); return; }
  if (gesture.mode === 'pan') {
    Zoom.tx = gesture.tx + e.clientX - gesture.sx;
    Zoom.ty = gesture.ty + e.clientY - gesture.sy;
    clampPan();
    applyZoom();
    return;
  }
  if (gesture.mode === 'tap') {
    // Exceeding the tap slop converts the gesture into a move, not a cancel.
    if (Math.abs(e.clientX - gesture.cx) + Math.abs(e.clientY - gesture.cy) > 8) {
      gesture = { mode: 'move', seal: gesture.seal, dx: gesture.dx, dy: gesture.dy };
    } else return;
  }
  const p = layerPt(e);
  if (!p) return;
  if (gesture.mode === 'move') {
    const s = gesture.seal;
    s.x = Math.min(Math.max(p.x - gesture.dx, 0), Math.max(0, 1 - s.w));
    s.y = Math.min(Math.max(p.y - gesture.dy, 0), Math.max(0, 1 - s.h));
    styleSeal(s);        // pixelation re-renders live: gesture.seal === s in pxTick
    return;
  }
  const { seal, anchor } = gesture;
  seal.x = Math.min(anchor.x, p.x);
  seal.y = Math.min(anchor.y, p.y);
  seal.w = Math.abs(p.x - anchor.x);
  seal.h = Math.abs(p.y - anchor.y);
  styleSeal(seal);
});

wrap.addEventListener('pointerup', () => {
  if (!gesture) return;
  const g = gesture;
  gesture = null;
  if (g.mode === 'lens') { $('loupe').classList.remove('show'); return; }
  if (g.mode === 'pan') return;
  if (g.mode === 'tap') { voidSeal(g.seal); return; }   // a genuine click still voids
  if (g.mode === 'move') return;                        // moved seal stays put
  const { seal, provisional } = g;
  if (seal.w < MIN_SIDE || seal.h < MIN_SIDE) {
    if (provisional) { seal.el.remove(); return; }
    // resized to nothing: void it
    voidSeal(seal);
    return;
  }
  if (provisional) {
    S.seals.push(seal);
    dressSeal(seal);
    startPixelation(seal);
    Sound.play('seal');
    updateFileBtn();
  }
});

/* ================= zoom rail + loupe ================= */
// The zoom transform lives on #zoomFrame (image + boxLayer together), so
// layerPt's getBoundingClientRect math — and therefore every seal gesture —
// is zoom-agnostic for free.
const zoomFrame = $('zoomFrame');
const Zoom = { z: 1, tx: 0, ty: 0 };
let lensMode = false;
const ZOOM_MAX = 4;
const LOUPE_D = 180, LOUPE_MAG = 3;

function clampPan() {
  Zoom.tx = Math.min(0, Math.max(wrap.clientWidth * (1 - Zoom.z), Zoom.tx));
  Zoom.ty = Math.min(0, Math.max(wrap.clientHeight * (1 - Zoom.z), Zoom.ty));
}
function applyZoom() {
  zoomFrame.style.transform = `translate(${Zoom.tx}px,${Zoom.ty}px) scale(${Zoom.z})`;
  $('zoomSlider').value = Zoom.z;
  $('zoomVal').textContent = Zoom.z.toFixed(1) + '×';
}
function setZoom(nz, px, py) {
  nz = Math.min(ZOOM_MAX, Math.max(1, nz));
  if (px === undefined) { px = wrap.clientWidth / 2; py = wrap.clientHeight / 2; }
  // Keep the content point under the focal point stationary.
  Zoom.tx = px - ((px - Zoom.tx) / Zoom.z) * nz;
  Zoom.ty = py - ((py - Zoom.ty) / Zoom.z) * nz;
  Zoom.z = nz;
  clampPan();
  applyZoom();
}
function resetZoom() { Zoom.z = 1; Zoom.tx = 0; Zoom.ty = 0; applyZoom(); }

$('zoomSlider').addEventListener('input', (e) => setZoom(parseFloat(e.target.value)));
wrap.addEventListener('wheel', (e) => {
  if (!e.ctrlKey) return;
  e.preventDefault();
  const r = wrap.getBoundingClientRect();
  setZoom(Zoom.z * (e.deltaY < 0 ? 1.15 : 1 / 1.15), e.clientX - r.left, e.clientY - r.top);
}, { passive: false });

$('lensBtn').onclick = () => {
  lensMode = !lensMode;
  $('lensBtn').classList.toggle('on', lensMode);
  wrap.classList.toggle('lens', lensMode);
  Sound.play('rack');
  setHint(lensMode ? UI_COPY.desk.hintLensOn : UI_COPY.desk.hintDraw);
};

const loupeCv = $('loupeCv');
loupeCv.width = LOUPE_D;
loupeCv.height = LOUPE_D;
function moveLoupe(e) {
  const r = wrap.getBoundingClientRect();
  const lx = e.clientX - r.left, ly = e.clientY - r.top;
  const loupe = $('loupe');
  const above = ly - LOUPE_D - 26;
  loupe.style.left = Math.min(Math.max(lx - LOUPE_D / 2, 2), r.width - LOUPE_D - 2) + 'px';
  loupe.style.top = (above >= 2 ? above : ly + 26) + 'px';   // flip below near the top edge
  const img = $('scanImg');
  const lr = layer.getBoundingClientRect();
  if (lr.width < 2 || !img.naturalWidth) return;
  const iw = img.naturalWidth, ih = img.naturalHeight;
  const srcW = LOUPE_D / ((lr.width / iw) * LOUPE_MAG);   // superzoom = 3x the on-screen scale
  const sx = ((e.clientX - lr.left) / lr.width) * iw - srcW / 2;
  const sy = ((e.clientY - lr.top) / lr.height) * ih - srcW / 2;
  const ctx = loupeCv.getContext('2d');
  ctx.imageSmoothingEnabled = srcW >= LOUPE_D;   // honest pixels once past 1:1
  ctx.fillStyle = '#0e0d1a';
  ctx.fillRect(0, 0, LOUPE_D, LOUPE_D);
  ctx.drawImage(img, sx, sy, srcW, srcW, 0, 0, LOUPE_D, LOUPE_D);
}

function mkSealEl(c) {
  const d = document.createElement('div');
  d.className = 'seal';
  d.style.color = getComputedStyle(document.documentElement).getPropertyValue(CLASS_VARS[c]);
  return d;
}
function styleSeal(s) {
  s.el.style.left = (s.x * 100) + '%';
  s.el.style.top = (s.y * 100) + '%';
  s.el.style.width = (s.w * 100) + '%';
  s.el.style.height = (s.h * 100) + '%';
}
function dressSeal(s) {
  s.el.innerHTML =
    `<span class="tag"><i>${escapeHtml(UI_COPY.desk.sealTag(CLASS_NAMES[s.c]))}</i></span>` +
    `<span class="x">✕</span>` +
    `<span class="h nw"></span><span class="h ne"></span><span class="h sw"></span><span class="h se"></span>`;
  s.el.querySelector('.x').addEventListener('click', () => voidSeal(s));
}
function voidSeal(s) {
  s.el.remove();
  S.seals = S.seals.filter((x) => x !== s);
  Sound.play('void');
  updateFileBtn();
}
function clearSeals() {
  S.seals.forEach((s) => s.el.remove());
  S.seals = [];
  updateFileBtn();
}

/* ================= seal pixelation ================= */
// A committed seal censors its zone live: the region under it is resampled at
// block resolution onto a canvas — revealed left-to-right on placement, then
// kept "alive" with sub-tile sample jitter + random block flicker. Resizing is
// handled for free: every render reads the seal's current normalized rect.
const PX_TILE = 11;          // css px per pixel block
const PX_REVEAL_MS = 520;    // left-to-right sweep duration
const PX_FLICKER_MS = 90;    // shimmer cadence once revealed
const pxBuf = document.createElement('canvas');
const pxBufCtx = pxBuf.getContext('2d');
let pxLoop = 0;

function startPixelation(seal) {
  const cv = document.createElement('canvas');
  cv.className = 'px';
  seal.el.insertBefore(cv, seal.el.firstChild);
  seal.cv = cv;
  seal.ctx = cv.getContext('2d');
  seal.col = getComputedStyle(seal.el).color;
  seal.born = performance.now();
  seal.lastPx = 0;
  if (!pxLoop) pxLoop = requestAnimationFrame(pxTick);
}

function pxTick(now) {
  const live = S.seals.filter((s) => s.cv && s.el.isConnected);
  if (live.length === 0) { pxLoop = 0; return; }
  for (const s of live) {
    const revealing = now - s.born < PX_REVEAL_MS + 120;
    const resizing = gesture && gesture.seal === s;
    if (revealing || resizing || now - s.lastPx > PX_FLICKER_MS) {
      s.lastPx = now;
      renderSealPx(s, now);
    }
  }
  pxLoop = requestAnimationFrame(pxTick);
}

function renderSealPx(s, now) {
  const img = $('scanImg');
  const iw = img.naturalWidth, ih = img.naturalHeight;
  if (!iw || !ih) return;
  const w = Math.max(1, Math.round(s.w * layer.clientWidth));
  const h = Math.max(1, Math.round(s.h * layer.clientHeight));
  if (s.cv.width !== w) s.cv.width = w;
  if (s.cv.height !== h) s.cv.height = h;
  const tw = Math.max(1, Math.round(w / PX_TILE));
  const th = Math.max(1, Math.round(h / PX_TILE));
  if (pxBuf.width !== tw) pxBuf.width = tw;
  if (pxBuf.height !== th) pxBuf.height = th;

  const sw = Math.max(1, s.w * iw), sh = Math.max(1, s.h * ih);
  // Sub-tile sample jitter makes the blocks breathe frame to frame.
  const sx = Math.min(Math.max(s.x * iw + (Math.random() - 0.5) * (sw / tw) * 0.5, 0), iw - sw);
  const sy = Math.min(Math.max(s.y * ih + (Math.random() - 0.5) * (sh / th) * 0.5, 0), ih - sh);
  pxBufCtx.imageSmoothingEnabled = true;
  pxBufCtx.clearRect(0, 0, tw, th);
  pxBufCtx.drawImage(img, sx, sy, sw, sh, 0, 0, tw, th);

  const ctx = s.ctx;
  ctx.imageSmoothingEnabled = false;
  ctx.clearRect(0, 0, w, h);
  ctx.drawImage(pxBuf, 0, 0, tw, th, 0, 0, w, h);

  // Random blocks pop bright/dark — the censor field never sits still.
  const tileW = w / tw, tileH = h / th;
  const n = Math.max(2, Math.round((tw * th) / 24));
  for (let i = 0; i < n; i++) {
    ctx.fillStyle = Math.random() < 0.5 ? 'rgba(255,255,255,.13)' : 'rgba(10,9,22,.22)';
    ctx.fillRect(Math.floor(Math.random() * tw) * tileW, Math.floor(Math.random() * th) * tileH, tileW, tileH);
  }

  const reveal = Math.min(1, (now - s.born) / PX_REVEAL_MS);
  if (reveal < 1) {
    const edge = Math.round(w * reveal);
    ctx.clearRect(edge, 0, w - edge, h);           // right of the front: the picture still raw
    ctx.fillStyle = s.col || '#fff';               // glowing scan front in the seal's color
    ctx.fillRect(Math.max(0, edge - 2), 0, 3, h);
    ctx.fillStyle = 'rgba(255,255,255,.6)';
    ctx.fillRect(Math.max(0, edge - 1), 0, 1, h);
  }
}

/* ================= stamp rack ================= */
let curClass = 0;
function selectClass(c) {
  if (c !== curClass) Sound.play('rack');
  document.querySelectorAll('.stamp[data-c]').forEach((x) => x.classList.toggle('sel', +x.dataset.c === c));
  curClass = c;
}
document.querySelectorAll('.stamp[data-c]').forEach((b) => {
  b.onclick = () => selectClass(+b.dataset.c);
});

function setButtonsEnabled(on) {
  $('fileBtn').disabled = !on;
}
function setHint(t) { $('hint').textContent = t; }

/* The FILE button is the whole verdict: seals filed, or a clean ruling, or an amendment. */
const fileBtnSub = $('fileBtn').querySelector('small');
function updateFileBtn() {
  const txt = $('fileBtnTxt');
  const R = UI_COPY.rack;
  $('fileBtn').classList.toggle('clean', !S.certify && !S.amending && S.seals.length === 0);
  // Amending outranks certify: file() sends the amend first, so the label must too.
  if (S.amending) { txt.textContent = R.amendTxt; fileBtnSub.textContent = R.amendSub; }
  else if (S.certify) { txt.textContent = R.certifyTxt; fileBtnSub.textContent = R.certifySub; }
  else if (S.seals.length === 0) { txt.textContent = R.cleanTxt; fileBtnSub.textContent = R.cleanSub; }
  else { txt.textContent = R.fileTxt; fileBtnSub.textContent = R.fileSub; }
}

/* ================= filing ================= */
$('fileBtn').onclick = () => {
  if (S.awaiting || !S.current) return;
  // Zero seals IS the filing: the server accepts an empty box list as a clean vote.
  file(S.seals.map((s) => ({ c: s.c, x: r4(s.x), y: r4(s.y), w: r4(s.w), h: r4(s.h) })));
};
const r4 = (v) => Math.round(v * 10000) / 10000;

let submitTimeout = null;
let lastFiling = null;        // {item, boxes} of the plain submit in flight (history candidate)
function file(boxes) {
  const item = S.current;
  S.awaiting = true;
  setButtonsEnabled(false);
  if (S.amending) {
    Bridge.send('submit', { target: item.target, dims: item.dims, boxes, amend: true });
  } else if (S.certify) {
    Bridge.send('gold-set', { target: item.target, boxes });
  } else {
    lastFiling = { item, boxes };
    Bridge.send('submit', { target: item.target, dims: item.dims, boxes });
  }
  submitTimeout = setTimeout(() => {
    // Never strand the booth on a lost message.
    S.awaiting = false;
    setButtonsEnabled(true);
    setHint(UI_COPY.desk.jammed);
    Sound.play('denied');
  }, 12000);
}

Bridge.on('submit-result', (m) => {
  clearTimeout(submitTimeout);
  S.awaiting = false;
  if (m.profile) applyQuests(m.profile);   // quest counters advance on every filing
  if (!S.current || m.target !== S.current.target) return;

  // Amendment verdicts never touch the gold/filed branches or the history log.
  if (S.amending) {
    S.amending = null;                                       // entry stays consumed either way
    if (typeof m.shift_used === 'number') applyShift(m.shift_used, m.shift_cap);
    if (typeof m.trust_tier === 'number') applyRank(m.trust_tier);
    if (m.ok) {
      stamp(UI_COPY.stamps.amended, '#c2337f');
      setTimeout(() => advance(false), 620);
    } else {
      setHint((m.error || UI_COPY.desk.recallDenied).toUpperCase());
      Sound.play('denied');
      setTimeout(() => advance(false), 1100);
    }
    updateRewindBtn();
    return;
  }

  if (!m.ok) {
    if (m.code === 429) { showClockout(m.error); return; }
    if (m.code === 409) { lastFiling = null; advance(false); return; }   // someone got there first
    setButtonsEnabled(true);
    setHint((m.error || UI_COPY.desk.rejected).toUpperCase());
    Sound.play('denied');
    return;
  }
  if (typeof m.shift_used === 'number') applyShift(m.shift_used, m.shift_cap);
  if (typeof m.trust_tier === 'number') applyRank(m.trust_tier);

  if (m.gold) {
    lastFiling = null;                                       // gold grades are final — no recall
    // Ledger: a gold is a filing that struck; grade breakdown + best grade tracked.
    S.session.filed++; S.session.golds++; S.session.seals += S.seals.length;
    if (S.session.grades[m.grade] != null) S.session.grades[m.grade]++;
    S.session.xp += (m.xp || 0);
    updateBestGrade(m.grade);
    // HONEY speaks the grade FIRST (workhorse, verbatim every time), so any rare
    // beat this moment unlocks queues behind her confirmation instead of over it.
    honeySay(HONEY.grade(m.grade));
    Story.onFiling();
    Story.first('gold');
    if (m.grade === 'S') Story.first('s');
    stamp(m.grade === 'C' ? UI_COPY.stamps.reviewed : UI_COPY.stamps.graded,
          m.grade === 'C' ? '#6b6455' : '#b8860b');
    setTimeout(() => showTicket(m), 620);
    setTimeout(() => advance(true), 1000);
  } else {
    if (lastFiling && lastFiling.item.target === m.target) {
      S.history.push(lastFiling);
      if (S.history.length > 10) S.history.shift();
      lastFiling = null;
      updateRewindBtn();
    }
    const clean = S.seals.length === 0;
    // Ledger: a plain filing is out for consensus → counts toward pending ratification.
    S.session.filed++; S.session.seals += S.seals.length; S.session.xp += (m.xp || 0);
    S.session.pending++;
    if (clean) S.session.cleans++;
    // The two workhorse confirmations: a kept case, and a case that needed nothing.
    honeySay(clean ? HONEY.work.clean : HONEY.work.filed);
    Story.onFiling();
    if (clean) Story.first('clean');            // first zero-box vote is a rare-line moment
    stamp(clean ? UI_COPY.stamps.clean : UI_COPY.stamps.filed,
          clean ? 'var(--clean)' : '#c2337f', clean ? 'clean' : 'stamp');
    bumpXp(m.xp);
    setTimeout(() => advance(true), 620);
  }
});

// Best grade ranks S > A > B > C; keep the strongest struck this shift.
const GRADE_RANK = { S: 4, A: 3, B: 2, C: 1 };
function updateBestGrade(g) {
  if (!GRADE_RANK[g]) return;
  if (!S.session.best || GRADE_RANK[g] > GRADE_RANK[S.session.best]) S.session.best = g;
}

Bridge.on('gold-result', (m) => {
  clearTimeout(submitTimeout);
  S.awaiting = false;
  if (!S.current || m.target !== S.current.target) return;
  if (!m.ok) { setButtonsEnabled(true); setHint((m.error || UI_COPY.desk.certifyFailed).toUpperCase()); return; }
  stamp(UI_COPY.stamps.certified, '#b8860b');
  setTimeout(() => advance(true), 620);
});

function advance(counted) {
  if (counted) S.filedSinceTray++;
  if (S.filedSinceTray >= 12) $('trayBadge').classList.add('show');
  slideOut(() => { S.current = null; nextCase(); });
}

function stamp(text, color, cue) {
  const r = $('rubber'), t = $('rubberTxt');
  t.textContent = text;
  t.style.color = t.style.borderColor = color;
  r.classList.remove('go');
  void r.offsetWidth;
  r.classList.add('go');
  Sound.play(cue || 'stamp');
  setTimeout(() => r.classList.remove('go'), 800);
}

/* ================= rewind / recall last filing ================= */
let rewindBusy = false;       // dossier is mid-slide on the recall path

function updateRewindBtn() {
  const b = $('rewindBtn');
  if (S.amending) {
    b.classList.add('show', 'amending');
    b.textContent = UI_COPY.rack.recallBack;
  } else if (S.history.length > 0) {
    b.classList.add('show');
    b.classList.remove('amending');
    b.textContent = UI_COPY.rack.recall;
  } else {
    b.classList.remove('show', 'amending');
  }
}

function recallToggle() {
  if (S.awaiting || gesture || rewindBusy) return;
  // A case on the table with the FILE button dark means a verdict is landing
  // (stamp/advance pending) — recalling now would stash an already-filed case.
  if (S.current && $('fileBtn').disabled) return;
  if (S.amending) {
    // Cancel the amendment: the entry wasn't amended, so it stays recallable.
    const entry = S.amending;
    S.amending = null;
    S.history.push(entry);
    if (S.history.length > 10) S.history.shift();
    S.current = null;
    clearSeals();
    updateRewindBtn();
    rewindBusy = true;
    slideOut(() => { rewindBusy = false; nextCase(); });   // stashed case sits at queue front
    return;
  }
  if (S.history.length === 0) return;
  const entry = S.history.pop();
  S.amending = entry;
  updateRewindBtn();
  if (S.current) {
    S.current._stash = true;                               // survives the LRU pick: comes back first
    S.queue.unshift(S.current);                            // stash the fresh case for later
    S.current = null;
    rewindBusy = true;
    slideOut(() => { rewindBusy = false; beginAmend(entry); });
  } else {
    beginAmend(entry);
  }
}
$('rewindBtn').onclick = recallToggle;

function beginAmend(entry) {
  S.amending = entry;
  S.current = entry.item;
  clearSeals();
  for (const b of entry.boxes) {
    const seal = { c: b.c, x: b.x, y: b.y, w: b.w, h: b.h, el: mkSealEl(b.c) };
    layer.appendChild(seal.el);
    styleSeal(seal);
    S.seals.push(seal);
    dressSeal(seal);
    startPixelation(seal);
  }
  setDocket(entry.item);
  const img = $('scanImg');
  img.onload = fitBoxLayer;
  img.src = entry.item.src;
  slideIn();
  setHint(UI_COPY.desk.hintAmend);
  setButtonsEnabled(true);
  updateFileBtn();
  updateRewindBtn();
}

/* ================= shift ceremony + story surfaces ================= */
/* Feature 1 (clock-in), Feature 4 render hooks (briefing strip, dossier note).
 * All non-blocking: the first case is already on the desk before any of this
 * runs, so the ritual never delays casework. */
let clockedIn = false;         // session-scoped: the ritual fires once, not on locker switches
let briefTimer = 0;
let nudgeTimer = 0;

function raidPctNow() {
  const r = S.stats && S.stats.raid;
  return r && r.total ? Math.round((r.closed / r.total) * 100) : 0;
}
function currentLockerName() {
  if (S.selected === 'all') return UI_COPY.chests.allTitle;
  return (S.lockers.find((l) => l.id === S.selected)?.name || S.selected || '').toUpperCase();
}

// The whole shift-start ceremony: dated punch-card stamp, briefing strip, mail nudge.
function runClockIn() {
  clockedIn = true;
  S.session = freshSession();                 // a fresh ledger for the new shift
  const mail = (S.profile && +S.profile.mail) || 0;
  punchStamp();                               // dated stamp thuds onto the shift card
  showBriefing(STORY.clockIn.briefing({ raidPct: raidPctNow(), mail, locker: currentLockerName() }), 5200);
  honeySay(HONEY.work.clockIn, { queue: true }); // the same good-morning, every shift, forever
  if (mail > 0) showPostNudge();              // POST WAITING → nudge toward the Letterbox
}

function punchStamp() {
  const el = $('punchStamp');
  if (!el) return;
  const date = new Date().toLocaleDateString('en-US', { month: 'short', day: '2-digit' }).toUpperCase();
  $('punchStampDate').textContent = date;
  $('punchStampCap').textContent = STORY.clockIn.onDuty;
  el.classList.remove('go');
  void el.offsetWidth;                        // restart the slam animation
  el.classList.add('go');
  Sound.play('stamp');                        // existing cue only — no new reward sound
  setTimeout(() => el.classList.remove('go'), 2600);
}

// The briefing strip above the desk. Reused by clock-in and by 'briefing' story beats.
function showBriefing(text, ms) {
  const strip = $('briefingStrip');
  if (!strip) return;
  $('briefingText').textContent = text;
  strip.classList.add('show');
  clearTimeout(briefTimer);
  briefTimer = setTimeout(() => strip.classList.remove('show'), ms || 5000);
}
$('briefingStrip').onclick = () => { $('briefingStrip').classList.remove('show'); clearTimeout(briefTimer); };

// A handwritten "POST WAITING" note that points at the Letterbox, tap to open it.
function showPostNudge() {
  const host = $('postWaiting');
  if (!host) return;
  host.innerHTML = '';
  host.appendChild(Story.renderNote('hand_cherish', STORY.clockIn.postWaiting, { sign: false }));
  host.classList.add('show');
  host.onclick = () => { hidePostNudge(); $('trayBtn').click(); };
  clearTimeout(nudgeTimer);
  nudgeTimer = setTimeout(hidePostNudge, 6500);
}
function hidePostNudge() { $('postWaiting').classList.remove('show'); clearTimeout(nudgeTimer); }

/* ---- story note clipped to the dossier edge (Feature 4b attach point) ---- */
function clearCaseNote() { const m = $('caseNote'); if (m) m.innerHTML = ''; }
// Beats fire mid-filing; defer the note to the NEXT dossier so it isn't animated out.
function queueDossierNote(handKey, text) { S.pendingNote = { hand: handKey, text }; }
function renderPendingNote() {
  if (!S.pendingNote) return;
  const mount = $('caseNote');
  if (!mount) { S.pendingNote = null; return; }
  const note = Story.renderNote(S.pendingNote.hand, S.pendingNote.text, { extraClass: 'clipped' });
  note.onclick = () => note.remove();
  mount.innerHTML = '';
  mount.appendChild(note);
  S.pendingNote = null;
  Sound.play('slip');                         // existing paper cue — no new sound
}

/* ================= HONEY — the voice (bible §6A, §6D-bis) =================
 * The performing lead's surface. She has no body, no portrait and (today)
 * no audio: THE TYPOGRAPHIC SURFACE IS CANONICAL and audio is an optional
 * enhancement layer that may be added later. A Keeper playing with sound
 * off must lose nothing, so nothing below depends on a clip existing, and
 * this slice ships zero audio assets.
 *
 * Every word she says lives in story.js (HONEY). Nothing here writes copy.
 *
 * Delivery: one character at a time on a FIXED cadence. Not a flourish —
 * the evenness is the character (§6A: the machine sounds like a machine).
 * Never ease it, never randomise it, never speed it up for long lines.
 *
 * Delivery doctrine — one voice, one thing at a time:
 *   RARE (beat) and CEREMONY (clock-in, goodnight) lines QUEUE. They are
 *     scarce, they are the moment, and they are never dropped or cut off.
 *   PER-FILING CONFIRMATIONS never queue. If she is mid-ceremony or
 *     mid-rare-line the confirmation is dropped (the rare moment owns the
 *     room, and the workhorse comes round again on the very next filing);
 *     otherwise it REPLACES whatever confirmation is on screen, so the
 *     caption always matches the case that just left the desk instead of
 *     lagging a backlog behind the player. */
const HONEY_CHAR_MS = 26;        // per character — flat, unhurried, identical every time
const HONEY_HOLD_MS = 2500;      // dwell after the last character lands
const HONEY_GAP_MS = 420;        // silence between two queued lines

const Honey = (() => {
  const wrap = $('honey');
  const lineEl = $('honeyLine');
  const q = [];                  // queued lines only (rare + ceremony)
  let current = null, typer = 0, holdT = 0;

  // SILENT VO HOOK — the single attachment point for the future audio layer.
  // It does nothing today, on purpose. When clips exist they are same-origin
  // `sfx/honey/<line.vo>.mp3` loaded through the existing Sound facade; until
  // then NOTHING may be fetched from here (the page is CSP-locked to its own
  // origin and a missing asset must never cost a request). Per §6D-bis rule 4
  // the clip may only ever deepen what the type already said — never carry
  // plot of its own, never gate a beat.
  function vo(line) { /* no-op by design — see HONEY in story.js */ void line; }

  function say(line, opts) {
    if (!wrap || !lineEl || !line || !line.text) return;
    const rare = !!(opts && opts.rare);
    const queued = rare || !!(opts && opts.queue);
    if (queued) { q.push({ line, rare }); pump(); return; }
    if (current && current.queued) return;               // she is having her moment
    speak({ line, rare: false, queued: false });         // replace a stale confirmation
  }

  function pump() {
    if (current || q.length === 0) return;
    const it = q.shift();
    speak({ line: it.line, rare: it.rare, queued: true });
  }

  function speak(it) {
    current = it;
    clearInterval(typer);
    clearTimeout(holdT);
    wrap.classList.toggle('rare', it.rare);
    wrap.classList.add('show');
    lineEl.textContent = '';
    vo(it.line);
    const text = it.line.text;
    let i = 0;
    typer = setInterval(() => {
      lineEl.textContent = text.slice(0, ++i);
      if (i >= text.length) {
        clearInterval(typer);
        holdT = setTimeout(done, HONEY_HOLD_MS);
      }
    }, HONEY_CHAR_MS);
  }

  function done() {
    wrap.classList.remove('show');
    current = null;
    setTimeout(pump, HONEY_GAP_MS);
  }

  return { say };
})();

// Global entry point: game.js event sites call this for workhorse lines,
// story.js dispatches 'honey'-surface beats through it for rare ones.
function honeySay(line, opts) { Honey.say(line, opts); }

/* ================= profile / shift / rank ================= */
const shiftbar = $('shiftbar');
for (let i = 0; i < 15; i++) shiftbar.appendChild(document.createElement('i'));

function applyProfile(p) {
  S.profile = p;
  applyShift(p.shift_used, p.shift_cap);
  applyRank(p.trust_tier);
  $('xpVal').textContent = p.xp ?? 0;
  if (p.recert) $('subtitle').textContent = UI_COPY.doc.subtitleRecert;
  applyQuests(p);
}
let capWarned = false;
function applyShift(used, cap) {
  if (typeof used !== 'number' || !cap) return;
  if (S.profile) { S.profile.shift_used = used; S.profile.shift_cap = cap; }
  const cells = shiftbar.children;
  const filled = Math.min(15, Math.round((used / cap) * 15));
  for (let i = 0; i < 15; i++) cells[i].className = i < filled ? 'on' : '';
  const ot = used >= cap;
  // Past the soft cap the Devotion Card stops paying and starts coaxing.
  $('shiftLabel').textContent = ot ? UI_COPY.top.overtime : UI_COPY.top.devotion;
  $('shiftLabel').classList.toggle('ot', ot);
  // Soft cap passed → offer the ledger on demand (Feature 2). Never in certify mode.
  $('clockOutBtn').classList.toggle('show', ot && !S.certify);
  // one honest heads-up entering the last 10 filings — never repeated, no pressure
  if (used < cap - 10) capWarned = false;
  else if (!ot && !capWarned) { capWarned = true; Sound.play('ticktock'); }
}
let knownTier = null;
function applyRank(tier) {
  const t = Math.min(Math.max(tier | 0, 0), 4);
  if (knownTier !== null && t > knownTier) {
    Sound.play('rankUp');
    setTimeout(() => Sound.play('rankSettle'), 700);
    Story.onRank(t);           // genuine in-session tier-up only (respects knownTier===null gotcha)
  }
  knownTier = t;
  $('rankName').textContent = RANKS[t];
  $('rank').classList.toggle('t0', tier === 0);
}
let xpAnim = 0;
function bumpXp(xp) {
  if (!xp || !S.profile) return;
  const from = S.profile.xp || 0;
  S.profile.xp = from + xp;
  const to = S.profile.xp;
  if (to <= from) { $('xpVal').textContent = to; return; }
  // roll the counter with a quiet tick per step — the honest little payday
  cancelAnimationFrame(xpAnim);
  const t0 = performance.now();
  const dur = Math.min(700, 220 + xp * 35);
  let lastTick = -999;
  (function step(t) {
    const k = Math.min(1, (t - t0) / dur);
    $('xpVal').textContent = Math.round(from + (to - from) * k);
    if (t - lastTick > 95 && k < 1) { lastTick = t; Sound.play('xp'); }
    if (k < 1) xpAnim = requestAnimationFrame(step);
  })(t0);
}

/* ---------------- end-of-shift ledger (Feature 2) ----------------
 * All figures come from S.session (client-tracked) + the 150 soft cap.
 * The gate uses the .gate.show pattern, so hotkeys auto-suppress while it's up.
 * terminal=true when the 429 clock-out forced it (no return-to-booth path). */
function renderLedger(terminal) {
  const L = STORY.ledger, s = S.session || freshSession();
  $('ledgerTitle').textContent = L.title;
  $('ledgerSub').textContent = L.sub;
  $('lgFiledLbl').textContent = L.rowFiled;
  $('lgSealsLbl').textContent = L.rowSeals;
  $('lgGoldsLbl').textContent = L.rowGolds;
  $('lgBestLbl').textContent = L.rowBest;
  $('lgXpLbl').textContent = L.rowXp;
  $('lgFiled').textContent = s.filed;
  $('lgSeals').textContent = s.seals;
  $('lgGolds').textContent = s.golds;
  $('lgGrades').textContent = L.gradeBreakdown(s.grades);
  $('lgBest').textContent = s.best || L.noneBest;
  $('lgXp').textContent = s.xp;
  $('lgXpCap').textContent = L.xpCap(s.xp, SOFT_CAP);
  $('lgPending').textContent = L.pending(s.pending);
  $('ledgerReturn').textContent = L.close;
  $('ledgerReturn').style.display = terminal ? 'none' : '';
  $('ledgerPunch').textContent = L.punch;
}
function openLedger(terminal) {
  renderLedger(terminal);
  showGate('gateLedger');
  Sound.play('stamp');                          // one stamp thunk on reveal, then silence
  honeySay(HONEY.work.goodnight, { queue: true }); // the tuck-in line — workhorse, unchanged
}
$('clockOutBtn').onclick = () => openLedger(false);           // on-demand, non-terminal
$('ledgerReturn').onclick = () => hideGate('gateLedger');
$('ledgerPunch').onclick = () => Bridge.send('exit');

function showClockout(msg) {
  // The hard cap forced a clock-out: the ledger IS the clock-out gate now (terminal).
  openLedger(true);
}

/* ================= commendation ticket ================= */
// The Gold-Star Valentine (was: commendation ticket). Accuracy only — a C is a
// gentle note with no sting, and nothing here ever mentions the picture itself.
function showTicket(m) {
  const V = UI_COPY.valentine;
  $('ticketGrade').textContent = m.grade;
  $('ticketGrade').className = 'grade' + (m.grade === 'C' ? ' gC' : '');
  $('ticketHead').textContent = m.grade === 'C' ? V.headC : V.head;
  $('ticketRow1').textContent = V.row1(m.grade);
  $('ticketRow2').textContent = V.row2(m.grade, (m.score ?? 0).toFixed(2));
  $('ticketXp').textContent = V.xp(m.xp || 0);
  bumpXp(m.xp);
  $('ticket').classList.remove('out');          // clear any lingering exit state so it prints in fresh
  $('commend').classList.add('go');
  Sound.play('ticket');
  if (m.grade !== 'C') {
    setTimeout(() => Sound.play('result'), 280);
    if (m.grade === 'S' || m.grade === 'A') setTimeout(() => Sound.play('gradeA'), 700);
  }
}
const TICKET_OUT_MS = 200;    // must match #ticket.out CSS duration
function dismissTicket() {
  if (!$('commend').classList.contains('go')) return;
  const t = $('ticket');
  if (t.classList.contains('out')) return;      // exit already running — ignore second click/keypress
  Sound.play('stamp');                          // rubber-stamp thunk = "pinned to the locker"
  t.classList.add('out');
  setTimeout(() => {
    $('commend').classList.remove('go');
    t.classList.remove('out');
  }, TICKET_OUT_MS);
}
$('ticketClose').onclick = dismissTicket;

/* ================= the Letterbox (was: mail tray) ================= */
$('trayBtn').onclick = () => {
  $('drawer').classList.add('open');
  $('trayBadge').classList.remove('show');
  hidePostNudge();                              // the POST WAITING note has done its job
  S.filedSinceTray = 0;
  $('slips').innerHTML = `<div class="slip empty">${escapeHtml(UI_COPY.letterbox.checking)}</div>`;
  $('trayTotal').textContent = '…';
  Bridge.send('inbox');
};
$('closeDrawer').onclick = () => $('drawer').classList.remove('open');

// A filed Directrice's Note, kept in the Letterbox so it can be re-read.
function memoSlipEl(th) {
  const c = UI_COPY.notes.memos[th];
  const d = document.createElement('div');
  d.className = 'slip memo';
  d.innerHTML = `<span class="qk">${escapeHtml(UI_COPY.notes.slipKicker)}</span>RE: ${escapeHtml(c.subj)}` +
    ` <span class="r">${escapeHtml(UI_COPY.notes.reread)}</span>`;
  d.onclick = () => showMemo(th);
  return d;
}

// One love letter for an inbox entry (quest stipend or casework ratification).
// The page owns all of this copy; the wire sends only { type:'quest', quest, xp }.
function slipElFor(e) {
  const L = UI_COPY.letterbox;
  const d = document.createElement('div');
  if (e.type === 'quest') {
    const q = L.quest[e.quest] || L.quest.other;
    d.className = 'slip quest';
    d.innerHTML = `<span class="qk">${escapeHtml(q.k)}</span>${escapeHtml(q.t)} <b>+${e.xp} XP</b>`;
  } else {
    d.className = 'slip';
    const docket = (e.t || '').slice(0, 5).toUpperCase();
    d.innerHTML = e.clean ? L.cleanSlip(docket, e.xp) : L.ratifiedSlip(docket, e.labels, e.xp);
  }
  return d;
}
// Client-side digest header summarising the ratification slips (cosigners optional).
function digestSlipEl(count, xp, cosigners) {
  const d = document.createElement('div');
  d.className = 'slip digest';
  let html = `<span class="qk">${escapeHtml(STORY.mail.postHeader)}</span>` +
    escapeHtml(STORY.mail.digest({ count, xp, cosigners }));
  if (cosigners.length) html += `<span class="cosign">${escapeHtml(STORY.mail.cosigned(cosigners))}</span>`;
  d.innerHTML = html;
  return d;
}

let mailTimer = 0;
// Morning-post ceremony (Feature 3): purely presentational — same wire payload, revealed
// one slip at a time (~250ms), tap the tray to skip. Doctrine: per-slip cue tiny, warm on
// the digest total only. Ratification slips get a client-side digest header.
Bridge.on('inbox-result', (m) => {
  const slips = $('slips');
  slips.innerHTML = '';
  slips.onclick = null;
  clearTimeout(mailTimer);
  // The Directrice's Notes filed to date sit at the top, newest first (instant).
  memoSeen.slice().sort((a, b) => b - a).forEach((th) => slips.appendChild(memoSlipEl(th)));

  const raw = (m.ok && m.entries) || [];
  const entries = raw.filter((e) => !e.type || e.type === 'quest');   // ignore unknown slip types
  if (entries.length === 0 && memoSeen.length === 0) {
    slips.innerHTML = `<div class="slip empty">${escapeHtml(UI_COPY.letterbox.empty)}</div>`;
    $('trayTotal').textContent = UI_COPY.letterbox.none;
    return;
  }
  if (entries.length === 0) { $('trayTotal').textContent = UI_COPY.letterbox.none; return; }

  // Group ordinary casework payouts for the digest; harvest optional cosigners.
  const rats = entries.filter((e) => !e.type);
  const ratXp = rats.reduce((a, e) => a + (e.xp || 0), 0);
  const cosigners = [];
  rats.forEach((e) => { if (Array.isArray(e.cosigners)) e.cosigners.forEach((n) => { if (n && !cosigners.includes(n)) cosigners.push(n); }); });
  if (rats.some((e) => !e.clean && e.labels > 0)) Story.first('ratified');   // first-ratification beat

  const steps = [];
  if (rats.length) steps.push({ el: digestSlipEl(rats.length, ratXp, cosigners), warm: true });
  entries.forEach((e) => steps.push({ el: slipElFor(e), warm: false }));

  let idx = 0, done = false;
  function finish() {
    if (done) return;
    done = true;
    clearTimeout(mailTimer);
    slips.onclick = null;
    $('trayTotal').textContent = UI_COPY.letterbox.claimed(m.xp);
    bumpXp(m.xp);
  }
  function step() {
    if (idx >= steps.length) { finish(); return; }
    const st = steps[idx++];
    slips.appendChild(st.el);
    slips.scrollTop = slips.scrollHeight;
    if (st.warm) {
      Sound.play('tray');                      // rare = warm chime on the digest total
      // her ratification line — workhorse, verbatim; a once-per-post ceremony, so it queues
      honeySay(HONEY.work.digest, { queue: true });
    } else Sound.play('slip', 0.5);            // frequent = tiny ticket cue at reduced gain
    mailTimer = setTimeout(step, 250);
  }
  slips.onclick = () => {                       // tap the tray to reveal everything at once
    clearTimeout(mailTimer);
    while (idx < steps.length) slips.appendChild(steps[idx++].el);
    slips.scrollTop = slips.scrollHeight;
    finish();
  };
  $('trayTotal').textContent = '…';
  step();
});

/* ================= bureau wire (stats ticker) ================= */
function requestStats() { Bridge.send('stats'); }
Bridge.on('stats-result', (m) => {
  if (!m.ok) return;
  S.stats = m;
  $('caseSub').textContent = UI_COPY.desk.caseSub(fmt(m.total));
  const lines = UI_COPY.ticker.lines({
    ratified: fmt(m.ratified), total: fmt(m.total), subs: fmt(m.subs), contested: m.contested,
  });
  $('tickerText').textContent = '★ ' + lines.join(' ★ ') + ' ★';
  renderRaid();
});
const fmt = (n) => (n ?? 0).toLocaleString('en-US');

/* ================= quests: shift orders, quotas, raid, memos ================= */
/* Diegetic paperwork over the wire contract's profile.quests object. Every
 * surface no-ops invisibly when quests are absent (old server) or in certify
 * mode — the clipboard, the chip and the FULFILLED cue all gate on S.quests. */
const RAFFLE_MAX = 5;              // a bureau-month runs up to 5 raffle tickets
const DAY_LETTERS = ['M', 'T', 'W', 'T', 'F', 'S', 'S'];
let questDayDoneKnown = null;      // null so the first sync never slams a false stamp (cf. knownTier)
let dayTimerInt = 0;

function applyQuests(p) {
  const q = p && p.quests;
  if (!q) { S.quests = null; questDayDoneKnown = null; renderQuests(); return; }
  S.quests = q;
  S.dayEndsAt = Date.now() + (Math.max(0, q.dayEndsInSec | 0)) * 1000;
  if (q.streak) Story.onStreak(q.streak.n);   // streak-milestone beats

  // Rising edge on the daily order: thud the rubber stamp exactly once.
  const done = !!(q.day && q.day.done);
  if (questDayDoneKnown !== null && done && !questDayDoneKnown && !S.certify && !q.frozen) {
    Sound.play('stamp');
  }
  questDayDoneKnown = done;
  if (!dayTimerInt) dayTimerInt = setInterval(tickDayTimer, 1000);
  renderQuests();
}

function tallyHtml(n, target) {
  let h = '';
  for (let g = 0; g * 5 < target; g++) {
    h += '<span class="tg">';
    for (let i = 0; i < 5 && g * 5 + i < target; i++) {
      const idx = g * 5 + i;
      h += `<i class="tm${i === 4 ? ' slash' : ''}${idx < n ? ' on' : ''}"></i>`;
    }
    h += '</span>';
  }
  return h;
}

function pipsHtml(days, minDays) {
  const met = days >= minDays;
  let h = `<div class="pips${met ? ' met' : ''}">`;
  for (let i = 0; i < 7; i++) {
    h += `<span class="pip${i < days ? ' on' : ''}${i === minDays - 1 ? ' req' : ''}">${DAY_LETTERS[i]}</span>`;
  }
  h += '</div>';
  return h;
}

function questBodyHtml() {
  const q = S.quests;
  const O = UI_COPY.orders;
  const d = q.day || {}, w = q.week || {}, mo = q.month || {}, st = q.streak || {};
  const goldT = d.goldTarget || 1;
  const timer = O.rollover(fmtCountdown(Math.max(0, Math.round((S.dayEndsAt - Date.now()) / 1000))));
  const tkts = Math.max(0, w.tickets | 0);
  let ticketGlyphs = '';
  for (let i = 0; i < RAFFLE_MAX; i++) ticketGlyphs += `<span class="tkt${i < tkts ? '' : ' spent'}"></span>`;

  return (
    `<div class="clipForm${q.frozen ? ' frozen' : ''}">` +
      `<div class="clipHdr">${escapeHtml(O.formHdr)}<span class="clipTimer">${escapeHtml(timer)}</span></div>` +

      `<section class="clipSec${!q.frozen && d.done ? ' fulfilled' : ''}">` +
        `<h5>${escapeHtml(O.secDaily)}</h5>` +
        `<div class="clipTask">${escapeHtml(O.task(d.target || 0))}</div>` +
        `<div class="tally">${tallyHtml(d.n || 0, d.target || 0)}</div>` +
        `<div class="clipNum">${escapeHtml(O.filed(d.n || 0, d.target || 0))}</div>` +
        `<div class="clipStretch"><span class="chk${d.goldDone ? ' on' : ''}">${d.goldDone ? '☑' : '☐'}</span>` +
          `${escapeHtml(O.stretch)} <span class="clipNumSm">${d.goldN || 0}/${goldT}</span></div>` +
        `${!q.frozen && d.done ? `<div class="stampFx">${escapeHtml(O.fulfilled)}</div>` : ''}` +
      `</section>` +

      `<section class="clipSec">` +
        `<h5>${escapeHtml(O.secWeek)}</h5>` +
        `<div class="clipNum big">${w.n || 0}/${w.target || 0}</div>` +
        pipsHtml(w.days || 0, w.minDays || 3) +
        `<div class="clipReq">${escapeHtml(O.minDays(w.minDays || 3))}</div>` +
        `<div class="clipTickets">${escapeHtml(O.tickets)} <b>${tkts}/${RAFFLE_MAX}</b> ${ticketGlyphs}</div>` +
      `</section>` +

      `<section class="clipSec">` +
        `<h5>${escapeHtml(O.secMonth)}</h5>` +
        `<div class="clipLine">${escapeHtml(O.filings)} <b>${mo.n || 0}/${mo.target || 0}</b></div>` +
        `<div class="clipLine">${escapeHtml(O.quality)} <b>${mo.quality || 0}/${mo.qualityMin || 0}</b></div>` +
        `${mo.entitled ? `<div class="clipStamp approved">${escapeHtml(O.entitled)}</div>` : ''}` +
      `</section>` +

      `<div class="clipStreak">${escapeHtml(O.streak)} <b>${st.n || 0}</b>` +
        `<small>${escapeHtml(O.stipend(st.bonusXp || 0))}</small></div>` +

      `${q.frozen ? `<div class="suspend">${escapeHtml(O.frozen)}</div>` +
        `<div class="suspendNote">${escapeHtml(O.frozenNote)}</div>` : ''}` +
    `</div>`
  );
}

// Pre-shift / old-server work order: the clipboard is up but carries no orders yet.
// Same manila visual language as questBodyHtml, but touches nothing on S.quests.
function questPlaceholderHtml() {
  const O = UI_COPY.orders;
  return (
    '<div class="clipForm">' +
      `<div class="clipHdr">${escapeHtml(O.formHdr)}</div>` +
      '<section class="clipSec">' +
        `<div class="clipTask">${escapeHtml(O.placeholderTask)}</div>` +
        `<div class="clipNum">${escapeHtml(O.placeholderNote)}</div>` +
      '</section>' +
    '</div>'
  );
}

function renderQuests() {
  const board = $('clipboard'), chip = $('questChip'), gBody = $('gateQuestsBody');
  const oBody = $('ordersDrawerBody'), tab = $('ordersTab');
  // (1) GOLD DESK: no quest paperwork at all — board, chip and phone tab all off.
  if (S.certify) {
    board.classList.remove('on');
    $('app').classList.remove('hasOrders');
    chip.classList.remove('on');
    if (tab) tab.classList.remove('on');
    $('clipboardBody').innerHTML = '';
    if (gBody) gBody.innerHTML = '';
    if (oBody) oBody.innerHTML = '';
    return;
  }
  // (2) PLACEHOLDER: no quest data yet (pre-shift / old server). Keep the desktop
  // clipboard visible so the new orders column isn't a mystery gap, but leave the
  // mobile chip + tab dark until real orders arrive. Never dereference S.quests here.
  if (!S.quests) {
    const ph = questPlaceholderHtml();
    $('clipboardBody').innerHTML = ph;
    if (gBody) gBody.innerHTML = ph;
    if (oBody) oBody.innerHTML = ph;
    board.classList.add('on');
    $('app').classList.add('hasOrders');
    chip.classList.remove('on');
    if (tab) tab.classList.remove('on');
    return;
  }
  // (3) QUESTS PRESENT: full work order across every surface.
  const html = questBodyHtml();
  $('clipboardBody').innerHTML = html;
  board.classList.add('on');
  $('app').classList.add('hasOrders');
  if (gBody) gBody.innerHTML = html;
  if (oBody) oBody.innerHTML = html;
  // mobile chip + phone edge-tab: daily progress, stamped green when the order is fulfilled
  const d = S.quests.day || {};
  const done = !S.quests.frozen && !!d.done;
  const tally = `${d.n || 0}/${d.target || 0}`;
  chip.classList.add('on');
  chip.classList.toggle('done', done);
  chip.querySelector('.qcNum').textContent = tally;
  if (tab) {
    tab.classList.add('on');
    tab.classList.toggle('done', done);
    const t = tab.querySelector('.otTally');
    if (t) t.textContent = tally;
  }
}

function fmtCountdown(s) {
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  if (h > 0) return `${h}H ${m}M`;
  if (m > 0) return `${m}M ${sec}S`;
  return `${sec}S`;
}
function tickDayTimer() {
  if (!S.quests) return;
  const txt = UI_COPY.orders.rollover(fmtCountdown(Math.max(0, Math.round((S.dayEndsAt - Date.now()) / 1000))));
  document.querySelectorAll('.clipTimer').forEach((e) => { e.textContent = txt; });
}

$('questChip').onclick = () => { renderQuests(); showGate('gateQuests'); };
$('gateQuestsClose').onclick = () => hideGate('gateQuests');
$('gateQuests').addEventListener('click', (e) => { if (e.target === $('gateQuests')) hideGate('gateQuests'); });
// phone: SHIFT ORDERS ride a left-edge drawer (mirror of the Letterbox). Re-render
// first (like the chip does) so the freshest work order is inside; toggle handles
// tap-to-close. No scrim — parity with the mail drawer.
$('ordersTab').onclick = () => { renderQuests(); $('ordersDrawer').classList.toggle('open'); };
$('ordersDrawerClose').onclick = () => $('ordersDrawer').classList.remove('open');

/* -------- HOW MUCH HONEY CAN SEE (her sight meter; was the raid bar) -------- */
function renderRaid() {
  const bar = $('raidBar');
  const r = S.stats && S.stats.raid;
  if (!r || !r.total) { bar.classList.remove('on'); return; }
  bar.classList.add('on');
  const pct = Math.max(0, Math.min(100, (r.closed / r.total) * 100));
  $('raidFill').style.width = pct.toFixed(2) + '%';
  $('raidReadout').textContent = UI_COPY.sight.readout(fmt(r.closed), fmt(r.total), pct.toFixed(1));
  document.querySelectorAll('#raidBar .raidTick').forEach((t) => {
    t.classList.toggle('crossed', pct >= +t.dataset.m);
  });
  checkMemos(pct);
}

/* ---------------- the Directrice's Notes (ambient sight-meter beats) ----------------
 * Copy lives in UI_COPY.notes.memos, keyed by sight-meter percentage. The
 * KEY IS PERSISTED (localStorage['bureau-memos'] holds the fired thresholds)
 * — rename the surface all you like, never renumber or rename these keys. */
const MEMOS_KEY = 'bureau-memos';               // storage key: retired-skin name kept on purpose
const MEMO_THRESHOLDS = [10, 25, 50, 75, 100];
let memoSeen = [];
(function loadMemos() {
  try {
    const s = JSON.parse(localStorage.getItem(MEMOS_KEY) || '{}');
    if (Array.isArray(s.seen)) memoSeen = s.seen.filter((n) => MEMO_THRESHOLDS.includes(n));
  } catch { /* fresh terminal */ }
})();
function saveMemos() {
  try { localStorage.setItem(MEMOS_KEY, JSON.stringify({ seen: memoSeen })); } catch { /* private booth */ }
}

function checkMemos(pct) {
  const newly = MEMO_THRESHOLDS.filter((m) => pct >= m && !memoSeen.includes(m));
  if (newly.length === 0) return;
  newly.forEach((m) => memoSeen.push(m));
  memoSeen.sort((a, b) => a - b);
  saveMemos();
  showMemo(Math.max(...newly));   // one popup per update; the rest wait in the mail drawer
}

let memoTyper = 0;
// Shared overlay: sight-meter Directrice's Notes AND 'memo'-surface story beats
// both type in through here. Reuses the valentine-printer cue (no new audio).
// The subject is printed after a CSS-supplied "RE: " — never pass one in.
function openMemo(subject, bodyText) {
  $('memoSubject').textContent = subject;
  const body = $('memoBody');
  body.textContent = '';
  $('memo').classList.add('go');
  Sound.play('ticket');
  clearInterval(memoTyper);
  let i = 0;
  memoTyper = setInterval(() => {
    i += 2;
    body.textContent = bodyText.slice(0, i);
    if (i >= bodyText.length) { clearInterval(memoTyper); body.textContent = bodyText; }
  }, 18);
}
function showMemo(threshold) {
  const copy = UI_COPY.notes.memos[threshold];
  if (!copy) return;
  openMemo(copy.subj, copy.body);
}
$('memoClose').onclick = () => { clearInterval(memoTyper); $('memo').classList.remove('go'); };

/* ================= pack drag-and-drop (keepsake intake) ================= */
// The whole parlour is the drop surface: a pack .zip dropped anywhere is handed to
// the host (real filesystem path via postMessageWithAdditionalObjects) to install.
let dragDepth = 0;
window.addEventListener('dragenter', (e) => {
  if (![...(e.dataTransfer?.types || [])].includes('Files')) return;
  e.preventDefault();
  dragDepth++;
  $('dropZone').classList.add('show');
});
window.addEventListener('dragover', (e) => { e.preventDefault(); });
window.addEventListener('dragleave', (e) => {
  e.preventDefault();
  if (--dragDepth <= 0) { dragDepth = 0; $('dropZone').classList.remove('show'); }
});
window.addEventListener('drop', (e) => {
  e.preventDefault();
  dragDepth = 0;
  $('dropZone').classList.remove('show');
  const zip = [...(e.dataTransfer?.files || [])].find((f) => /\.zip$/i.test(f.name));
  if (!zip) { setHint(UI_COPY.gates.install.notZip); return; }
  showInstallGate(UI_COPY.gates.install.receiving(zip.name));
  Bridge.sendWithFiles('pack-drop', { name: zip.name }, [zip]);
});

function showInstallGate(msg, failed) {
  const G = UI_COPY.gates.install;
  $('installTitle').textContent = failed ? G.titleFailed : G.title;
  $('installMsg').textContent = msg;
  $('installSub').textContent = failed ? G.subFailed : G.sub;
  $('installClose').style.display = failed ? '' : 'none';
  showGate('gateInstall');
}
$('installClose').onclick = () => hideGate('gateInstall');

Bridge.on('pack-install-progress', (m) => showInstallGate(m.msg || '…'));
Bridge.on('pack-install-failed', (m) => showInstallGate(m.error || UI_COPY.gates.install.rejected, true));
Bridge.on('pack-installed', (m) => {
  hideGate('gateInstall');
  hideGate('gateRequisition');
  hideGate('gateNoContent');
  setHint(UI_COPY.gates.install.installed((m.name || '').toUpperCase()));
  // The host follows up with a fresh 'packs' list; auto-load the new locker then.
  S.autoloadPack = m.id;
});

/* ================= gold desk (admin) ================= */
$('goldToggle').onclick = () => {
  if (S.awaiting) return;                 // mid-filing flip would mislabel the verdict
  S.certify = !S.certify;
  Sound.play('gold');
  $('goldToggle').textContent = UI_COPY.top.certify(S.certify);
  $('goldToggle').classList.toggle('on', S.certify);
  document.getElementById('app').classList.toggle('admin', S.certify);
  renderQuests();                         // quest surfaces no-op in certify mode
  if (S.certify) $('clockOutBtn').classList.remove('show');   // no shift ledger at the gold desk
  updateFileBtn();
  $('caseTag').textContent = S.certify ? UI_COPY.desk.tagDraft : UI_COPY.desk.tagKeepsake;
  $('caseTag').classList.toggle('goldTag', S.certify);
  // The two modes draw from different queues (proposal drafts vs casework):
  // dump the desk and repull. Seen-set resets too — a target the Keeper
  // scrolled past as casework may legitimately return as a draft.
  S.queue = [];
  S.seen = new Set();
  S.history = [];
  S.amending = null;
  S.exhausted = false;
  S.fetching = false;
  resetWireRetries();
  updateRewindBtn();
  if (S.current) slideOut(() => { S.current = null; nextCase(); });
  else nextCase();
};

/* ================= parlour settings (options / audio / hotkeys) ================= */
const OPTS_KEY = 'bureau-opts';                 // storage key: retired-skin name kept on purpose
// The four stamp rows are named from the LOCKED class labels; only the last
// two carry fiction copy. Ids ('c0'..'recall') are persisted — never rename them.
const KEY_ACTIONS = [
  { id: 'c0',     label: UI_COPY.keys.stamp(CLASS_NAMES[0]), def: 'Digit1' },
  { id: 'c1',     label: UI_COPY.keys.stamp(CLASS_NAMES[1]), def: 'Digit2' },
  { id: 'c2',     label: UI_COPY.keys.stamp(CLASS_NAMES[2]), def: 'Digit3' },
  { id: 'c3',     label: UI_COPY.keys.stamp(CLASS_NAMES[3]), def: 'Digit4' },
  { id: 'file',   label: UI_COPY.keys.file,   def: 'Space' },
  { id: 'recall', label: UI_COPY.keys.recall, def: 'KeyR'  },
];
const defaultKeys = () => Object.fromEntries(KEY_ACTIONS.map((a) => [a.id, a.def]));
const opts = { volume: 60, mute: false, keys: defaultKeys(), lockersMin: false };
(function loadOpts() {
  try {
    const saved = JSON.parse(localStorage.getItem(OPTS_KEY) || '{}');
    if (typeof saved.volume === 'number') opts.volume = Math.min(100, Math.max(0, saved.volume));
    if (typeof saved.mute === 'boolean') opts.mute = saved.mute;
    if (typeof saved.lockersMin === 'boolean') opts.lockersMin = saved.lockersMin;
    if (saved.keys) for (const a of KEY_ACTIONS) {
      if (typeof saved.keys[a.id] === 'string') opts.keys[a.id] = saved.keys[a.id];
    }
  } catch { /* fresh booth */ }
})();
function saveOpts() {
  try { localStorage.setItem(OPTS_KEY, JSON.stringify(opts)); } catch { /* private booth */ }
}

/* -------- keepsake chests collapse (manual toggle, persisted in opts) -------- */
function applyLockersMin() { $('app').classList.toggle('lockersMin', !!opts.lockersMin); }
$('lockersToggle').onclick = () => {
  opts.lockersMin = !opts.lockersMin;
  applyLockersMin();
  saveOpts();               // whole opts object → survives volume/mute/key writes and vice-versa
  Sound.play('rack');
};
applyLockersMin();          // restore persisted state at boot

/* -------- booth audio --------
 * Real cues live in sfx/ (same-origin, so the CSP allows them): recycled CCP
 * assets + two generated ones, all normalized to one house loudness. They are
 * lazy-fetched on the first user gesture; until a buffer lands, the original
 * procedural synth stands in for seal/stamp/ticket and other cues stay silent.
 * booth_ambience is a seamless 18s room-tone loop faded under everything. */
const Sound = (() => {
  let ctx = null;
  const buffers = {};
  let fetched = false;
  let ambGain = null;
  const rot = {};
  // cue -> {f: file basenames (rotated), s: playback gain scale}
  const CUES = {
    seal:       { f: ['seal_pop1', 'seal_pop2', 'seal_pop3'], s: 0.8 },
    void:       { f: ['void_pop'], s: 0.7 },
    rack:       { f: ['rack_click'], s: 0.45 },
    stamp:      { f: ['stamp_thud'], s: 0.9 },
    clean:      { f: ['clean_knock'], s: 0.85 },
    dossier:    { f: ['dossier_in'], s: 0.5 },
    xp:         { f: ['xp_tick'], s: 0.5 },
    slip:       { f: ['slip_dling'], s: 0.55 },
    tray:       { f: ['tray_chime1', 'tray_chime2', 'tray_chime3'], s: 0.7 },
    ticket:     { f: ['ticket_reveal'], s: 0.8 },
    result:     { f: ['ticket_result'], s: 0.6 },
    gradeA:     { f: ['grade_a'], s: 0.5 },
    rankUp:     { f: ['rank_up'], s: 0.8 },
    rankSettle: { f: ['rank_settle'], s: 0.7 },
    gold:       { f: ['gold_toggle'], s: 0.7 },
    denied:     { f: ['denied'], s: 0.6 },
    rebind:     { f: ['rebind_click'], s: 0.6 },
    ticktock:   { f: ['shift_ticktock'], s: 0.5 },
  };
  const AMB_FILE = 'booth_ambience';
  const AMB_LEVEL = 0.22;

  function ac() {
    if (!ctx) { try { ctx = new (window.AudioContext || window.webkitAudioContext)(); } catch { return null; } }
    if (ctx.state === 'suspended') ctx.resume();
    if (ctx && !fetched) loadAll(ctx);
    return ctx;
  }

  function loadAll(c) {
    fetched = true;
    const names = new Set([AMB_FILE]);
    Object.values(CUES).forEach((q) => q.f.forEach((n) => names.add(n)));
    Promise.all([...names].map(async (n) => {
      try {
        const r = await fetch('sfx/' + n + '.mp3');
        if (!r.ok) return;
        buffers[n] = await c.decodeAudioData(await r.arrayBuffer());
      } catch { /* stay procedural */ }
    })).then(startAmbience);
  }

  function playBuf(name, gscale) {
    const q = CUES[name];
    if (!q) return false;
    const file = q.f.length === 1 ? q.f[0] : q.f[(rot[name] = ((rot[name] || 0) + 1) % q.f.length)];
    const b = buffers[file];
    if (!b || !ctx) return false;
    const src = ctx.createBufferSource();
    src.buffer = b;
    const g = ctx.createGain();
    g.gain.value = (opts.volume / 100) * q.s * (gscale == null ? 1 : gscale);
    src.connect(g).connect(ctx.destination);
    src.start();
    return true;
  }

  function startAmbience() {
    if (!ctx || ambGain || !buffers[AMB_FILE]) return;
    const src = ctx.createBufferSource();
    src.buffer = buffers[AMB_FILE];
    src.loop = true;
    // skip the mp3 encoder padding so the crossfaded loop point stays seamless
    src.loopStart = 0.05;
    src.loopEnd = buffers[AMB_FILE].duration - 0.05;
    ambGain = ctx.createGain();
    ambGain.gain.value = 0;
    src.connect(ambGain).connect(ctx.destination);
    src.start();
    ambience(4);                       // slow first fade-in: the booth wakes up
  }

  function ambience(fade) {
    if (!ambGain || !ctx) return;
    const target = (opts.mute ? 0 : opts.volume / 100) * AMB_LEVEL;
    ambGain.gain.cancelScheduledValues(ctx.currentTime);
    ambGain.gain.setTargetAtTime(target, ctx.currentTime, fade || 0.25);
  }

  function play(name, gscale) {
    if (opts.mute || opts.volume <= 0) return;
    const c = ac();
    if (!c || c.state !== 'running') return;
    if (playBuf(name, gscale)) return;
    const t = c.currentTime;
    const master = c.createGain();
    master.gain.value = opts.volume / 100;
    master.connect(c.destination);
    if (name === 'seal') {
      // soft tick — a seal pressed into the paper
      const o = c.createOscillator(), g = c.createGain();
      o.type = 'sine';
      o.frequency.setValueAtTime(1400, t);
      o.frequency.exponentialRampToValueAtTime(850, t + 0.05);
      g.gain.setValueAtTime(0.16, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.06);
      o.connect(g).connect(master); o.start(t); o.stop(t + 0.08);
    } else if (name === 'stamp') {
      // low thunk — the rubber stamp slams
      const o = c.createOscillator(), g = c.createGain();
      o.type = 'triangle';
      o.frequency.setValueAtTime(150, t);
      o.frequency.exponentialRampToValueAtTime(55, t + 0.12);
      g.gain.setValueAtTime(0.5, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.16);
      o.connect(g).connect(master); o.start(t); o.stop(t + 0.18);
      const nb = c.createBuffer(1, Math.floor(c.sampleRate * 0.05), c.sampleRate);
      const d = nb.getChannelData(0);
      for (let i = 0; i < d.length; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / d.length);
      const ns = c.createBufferSource(); ns.buffer = nb;
      const f = c.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = 900;
      const ng = c.createGain();
      ng.gain.setValueAtTime(0.25, t);
      ng.gain.exponentialRampToValueAtTime(0.001, t + 0.05);
      ns.connect(f).connect(ng).connect(master); ns.start(t); ns.stop(t + 0.05);
    } else if (name === 'ticket') {
      // brief two-note chime — the commendation printer
      [[880, 0], [1318.5, 0.09]].forEach(([f0, dt]) => {
        const o = c.createOscillator(), g = c.createGain();
        o.type = 'sine'; o.frequency.value = f0;
        g.gain.setValueAtTime(0.0001, t + dt);
        g.gain.exponentialRampToValueAtTime(0.14, t + dt + 0.015);
        g.gain.exponentialRampToValueAtTime(0.001, t + dt + 0.18);
        o.connect(g).connect(master); o.start(t + dt); o.stop(t + dt + 0.2);
      });
    }
  }
  return { play, unlock: ac, ambience };
})();
// Warm the AudioContext on any gesture so non-gesture cues (stamp, ticket) can sound.
window.addEventListener('pointerdown', () => Sound.unlock(), true);

/* -------- options panel -------- */
let armedKey = null;          // action id waiting for a key capture
let denyKey = null;           // action id flashing "KEY IN USE"
let denyTimer = 0;

function keyName(code) {
  if (!code) return UI_COPY.keys.unbound;
  if (code === 'Space') return 'SPACE';
  return code.replace(/^Key|^Digit/, '').replace(/([a-z])([A-Z])/g, '$1 $2').toUpperCase();
}
function renderGuideKeys() {
  const g = $('fieldGuide');
  if (!g) return;
  $('fgStamps').textContent = ['c0', 'c1', 'c2', 'c3'].map((id) => keyName(opts.keys[id])).join(' ');
  $('fgFile').textContent = keyName(opts.keys.file);
  $('fgRecall').textContent = keyName(opts.keys.recall);
}
function renderKeyRows() {
  const box = $('keyRows');
  box.innerHTML = '';
  for (const a of KEY_ACTIONS) {
    const row = document.createElement('div');
    row.className = 'keyRow' + (armedKey === a.id ? ' arm' : '') + (denyKey === a.id ? ' deny' : '');
    const kbd = armedKey === a.id ? UI_COPY.keys.arm : denyKey === a.id ? UI_COPY.keys.clash : keyName(opts.keys[a.id]);
    row.innerHTML = `<span class="klabel">${escapeHtml(a.label)}</span><span class="kbd">${escapeHtml(kbd)}</span>`;
    row.onclick = () => { armedKey = a.id; denyKey = null; renderKeyRows(); };
    box.appendChild(row);
  }
}
function renderOpts() {
  $('optVol').value = opts.volume;
  $('optVolVal').textContent = opts.volume;
  $('optMute').checked = opts.mute;
  renderKeyRows();
}
function closeOpts() {
  armedKey = null;
  denyKey = null;
  hideGate('gateOpts');
}
$('optsBtn').onclick = () => { renderOpts(); showGate('gateOpts'); };
$('optsClose').onclick = closeOpts;
$('gateOpts').addEventListener('click', (e) => { if (e.target === $('gateOpts')) closeOpts(); });
$('optVol').oninput = (e) => { opts.volume = +e.target.value; $('optVolVal').textContent = opts.volume; Sound.ambience(); };
$('optVol').onchange = () => { saveOpts(); Sound.play('seal'); };
$('optMute').onchange = (e) => { opts.mute = e.target.checked; saveOpts(); Sound.ambience(); };
$('optReset').onclick = () => { opts.keys = defaultKeys(); armedKey = null; denyKey = null; saveOpts(); renderKeyRows(); renderGuideKeys(); };

function bindKey(code) {
  const act = armedKey;
  armedKey = null;
  if (code === 'Escape') { renderKeyRows(); return; }
  const clash = Object.entries(opts.keys).some(([a, c]) => c === code && a !== act);
  if (clash) {
    denyKey = act;
    Sound.play('denied');
    clearTimeout(denyTimer);
    denyTimer = setTimeout(() => { denyKey = null; renderKeyRows(); }, 900);
  } else {
    opts.keys[act] = code;
    Sound.play('rebind');
    saveOpts();
    renderGuideKeys();
  }
  renderKeyRows();
}

/* -------- global hotkeys -------- */
window.addEventListener('keydown', (e) => {
  if (armedKey) {
    e.preventDefault();
    if (/^(Shift|Control|Alt|Meta)/.test(e.code)) return;   // wait for a real key
    bindKey(e.code);
    return;
  }
  if (e.repeat) return;
  if (document.querySelector('.gate.show')) return;         // includes the options panel
  if ($('drawer').classList.contains('open')) return;
  if ($('ordersDrawer').classList.contains('open')) return; // phone SHIFT ORDERS drawer
  if ($('commend').classList.contains('go')) {                       // ticket up: only dismiss keys act, rest stay dead
    if (e.code === 'Space' || e.code === 'Enter' || e.code === 'Escape') {
      if (e.code === 'Space') e.preventDefault();                    // stop the page scrolling
      dismissTicket();
    }
    return;
  }
  if (!$('boot').classList.contains('gone')) return;
  const k = opts.keys;
  switch (e.code) {
    case k.c0: selectClass(0); break;
    case k.c1: selectClass(1); break;
    case k.c2: selectClass(2); break;
    case k.c3: selectClass(3); break;
    case k.file: e.preventDefault(); $('fileBtn').click(); break;
    case k.recall: recallToggle(); break;
  }
});

$('goldToggle').textContent = UI_COPY.top.certify(S.certify);   // admin bench starts dark
updateFileBtn();
updateRewindBtn();
renderGuideKeys();
renderQuests();      // start hidden until profile.quests lands
renderRaid();        // start hidden until stats.raid lands
