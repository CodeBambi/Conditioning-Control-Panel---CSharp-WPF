/* ============================================================================
 * loomStudio.js - THE LOOM's pane module, schema v2: design a spiral, watch
 * it spin live, SAVE it as a seamless-loop GIF into the CCP Spirals library.
 *
 * v2 ("the most advanced spiral editor available"): six weave styles, up to
 * six threads with hard/gradient blending, a counter-rotating second weave,
 * glow, pulse, wobble, hue drift, a centerpiece (dot / star / cross / x / mantra word),
 * presets, and a 🎲. The preview and the encoder share ONE field pipeline
 * (shared/loomField.js - WebGL field + 2D centerpiece composite), so the file
 * always matches the pane; machines without WebGL fall back to the v1 wedge
 * renderer in BOTH places. v1 sidecars re-edit cleanly (normalizeParams2).
 *
 * Mounted in two homes: the Warren's Boudoir pane (crafted unlock, DTRH) and
 * the main app's Loom window (loom.html - always available). The DOM is a
 * studio: a stage card (preview / save / patterns), grouped dial cards, and
 * the rack strip. The Warren pane stacks those vertically untouched; the
 * standalone page lays them out as a two-pane grid (loom.html CSS). Options:
 *   previewSize - canvas backing resolution (288 pane default, window uses 768)
 *   hotkeys    - F/R/ctrl+S/1-6 bindings; ONLY the window turns this on (the
 *                game has its own key handling and must not be shadowed)
 * Same bridge protocol as v1: loom-save / loom-delete out, loom-list /
 * loom-result in. All file authority stays C#-side (DtrhLoomStore).
 *
 * Module state survives warren's refreshWins innerHTML wipes (the Part 1
 * worktable pattern): params/name/armed states live here, render() redraws.
 * ==========================================================================*/

import {
  normalizeParams2, loopMs2, randomParams2, formatDims2,
  createFieldRenderer, composeFrame, drawFallbackFrame,
  LOOM_STYLES, LOOM_SWATCHES,
} from '../shared/loomField.js';

const MAX_SPIRALS = 12;

/** CCP-themed presets: one signature weave per built-in mod + a few classics. */
const PRESETS = [
  ['classic ccp', { layer: { arms: 4, turns: 2, colors: ['#e84393', '#8b5cf6'] }, bg: { color: '#121220' }, glow: 0.25 }],
  ['bambi haze', { layer: { arms: 6, style: 'ribbon', bandMode: 'gradient', colors: ['#ff69b4', '#ffb6c1', '#ff1493'] }, bg: { kind: 'radial', color: '#2a1030', outer: '#12081a' }, pulse: { amp: 0.08, cycles: 1 }, glow: 0.4 }],
  ['sissy swirl', { layer: { arms: 5, turns: 2.5, style: 'golden', colors: ['#9b59b6', '#bb8fce'] }, centerpiece: { kind: 'star', color: '#bb8fce', sizeFrac: 0.16 } }],
  ['drone protocol', { layer: { arms: 10, turns: 3.5, duty: 0.3, colors: ['#00ff41'] }, bg: { color: '#050805' }, wobble: { amp: 0.1, freq: 4, cycles: 1 } }],
  ['locked in', { layer: { arms: 3, turns: 4, colors: ['#e81ca8', '#ff6ec7'] }, layer2: { enabled: true, arms: 3, turns: 1.5, colors: ['#8a0f5e'], direction: -1 }, glow: 0.5 }],
  ['hypno teal', { layer: { arms: 2, turns: 5, colors: ['#40d0c0', '#ffffff'] }, bg: { color: '#0d0d18' } }],
  ['candy tunnel', { layer: { arms: 8, duty: 0.55, style: 'tunnel', colors: ['#ff69b4', '#ffffff', '#8a5cff'] }, pulse: { amp: 0.06, cycles: 2 } }],
  ['void bloom', { layer: { arms: 7, duty: 0.45, style: 'petal', colors: ['#8a5cff'] }, bg: { kind: 'radial', color: '#14060f', outer: '#000000' }, glow: 0.6, hueCycles: 1 }],
];

function presetParams(patch) {
  const d = normalizeParams2({});
  const merged = {
    ...d, ...patch,
    layer: { ...d.layer, ...(patch.layer || {}) },
    layer2: { ...d.layer2, ...(patch.layer2 || {}) },
    bg: { ...d.bg, ...(patch.bg || {}) },
    pulse: { ...d.pulse, ...(patch.pulse || {}) },
    wobble: { ...d.wobble, ...(patch.wobble || {}) },
    centerpiece: { ...d.centerpiece, ...(patch.centerpiece || {}) },
  };
  return normalizeParams2(merged);
}

const slugify = (name) => String(name || '').toLowerCase().trim()
  .replace(/[^a-z0-9_-]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 24);

function b64FromBuffer(buf) {
  const bytes = new Uint8Array(buf);
  let out = '';
  const CHUNK = 0x8000;
  for (let i = 0; i < bytes.length; i += CHUNK) {
    out += String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK));
  }
  return btoa(out);
}

/* Studio-structure CSS both homes need (sections, stage, sub-panel, presets,
 * fullscreen overlay). Injected once so styles.css stays untouched; the
 * standalone page adds its two-pane grid on top (loom.html). */
const STUDIO_CSS = `
.wr-loom-studio > .wr-card { margin-bottom: 10px; }
.loom-controls > .wr-card { margin-bottom: 10px; }
.loom-stage { position: relative; display: flex; align-items: center; justify-content: center; margin-bottom: 8px; }
.loom-stage-inner { position: relative; display: flex; max-width: 100%; }
.loom-stage-inner canvas { object-fit: contain; }
.loom-fs-btn {
  position: absolute; right: 10px; bottom: 12px; z-index: 2;
  width: 34px; height: 34px; border-radius: 10px; cursor: pointer;
  border: 1px solid rgba(255,138,194,0.45); background: rgba(20,16,36,0.66);
  color: #ffd7ec; font-size: 16px; line-height: 1;
}
.loom-fs-btn:hover { background: rgba(255,138,194,0.3); }
.loom-fmt { position: absolute; left: 10px; top: 10px; z-index: 2; display: flex; gap: 5px; }
.loom-fmt-btn {
  width: 30px; height: 30px; border-radius: 9px; cursor: pointer;
  border: 1px solid rgba(255,138,194,0.45); background: rgba(20,16,36,0.66);
  color: #ffd7ec; font-size: 13px; line-height: 1;
}
.loom-fmt-btn:hover { background: rgba(255,138,194,0.3); }
.loom-fmt-btn.is-on { background: rgba(255,138,194,0.42); color: #fff; }
.wr-loom-preview { height: auto; }
.loom-fs-overlay {
  position: fixed; inset: 0; z-index: 9999; background: #000;
  display: flex; align-items: center; justify-content: center; cursor: pointer;
}
.loom-fs-overlay canvas { width: 100vw; height: 100vh; object-fit: cover; }
.loom-fs-hint {
  position: absolute; bottom: 4vh; left: 0; right: 0; text-align: center;
  color: rgba(255,215,236,0.4); font-size: 12px; letter-spacing: 0.3em;
  pointer-events: none; animation: loomFsHint 5s ease forwards;
}
@keyframes loomFsHint { 0%,60% { opacity: 1; } 100% { opacity: 0; } }
.loom-sub {
  margin: 6px 0 4px 8px; padding: 8px 10px 6px;
  border-left: 2px solid rgba(255,138,194,0.4);
  background: rgba(255,138,194,0.06); border-radius: 0 10px 10px 0;
}
.loom-preset { display: inline-flex; align-items: center; gap: 7px; padding: 4px 11px 4px 5px; }
.loom-preset-thumb { width: 26px; height: 26px; border-radius: 8px; display: block; border: 1px solid rgba(255,255,255,0.18); }
.loom-patterns { margin-top: 12px; }
.loom-patterns-lbl { width: auto !important; margin-bottom: 4px; }
.loom-hint { margin-top: 10px; font-size: 0.68rem; letter-spacing: 0.08em; color: #8a86b8; text-align: center; }
.wr-loom-lbl { width: 64px; }
.wr-loom-val { width: 44px; }
.wr-loom-row.is-live .wr-loom-val { color: #fff; text-shadow: 0 0 9px rgba(255,138,194,0.9); }
`;

function ensureStudioCss() {
  if (document.getElementById('loom-studio-css')) return;
  const st = document.createElement('style');
  st.id = 'loom-studio-css';
  st.textContent = STUDIO_CSS;
  document.head.appendChild(st);
}

export function createLoomStudio({ bridge, sfx, previewSize = 288, hotkeys = false }) {
  const DEF = normalizeParams2({});   // dial defaults (double-click reset)
  let params = normalizeParams2({});
  let name = '';
  let saved = [];             // host truth: [{ slug, url, params }]
  let worker = null;
  let jobId = 0;
  let pendingJob = null;      // { id, name, params, overwrite } captured when SAVE was clicked
  let encoding = false;       // a save job is in flight (worker + host round-trip)
  let progress = 0;
  let status = null;          // transient line under the SAVE button
  let overwriteArmed = false; // slug collision: first SAVE arms, second overwrites
  let deleteArmed = null;     // slug whose 🗑 was clicked once
  let lastBody = null;
  let raf = 0, previewCanvas = null, t0 = 0;
  let field = null, fieldFailed = false;   // WebGL preview renderer (null until first tick)
  let fsOverlay = null, fsCanvas = null;   // fullscreen preview (esc wakes you)
  let fsField = null, fsFieldFailed = false;
  let fsKeyHandler = null;
  const presetThumbs = new Map();          // preset name -> dataURL (built once)
  let thumbsBuilt = false;
  const history = [];                      // undo stack: params snapshots (↩ / ctrl+Z)
  const HISTORY_MAX = 40;

  /** Snapshot params BEFORE a change lands, so ↩ can rewind it. */
  function pushHistory() {
    try {
      history.push(JSON.parse(JSON.stringify(params)));
      if (history.length > HISTORY_MAX) history.shift();
    } catch (e) { /* ignore */ }
  }

  function undo() {
    if (!history.length) return;
    params = normalizeParams2(history.pop());
    redraw();
  }

  function ensureWorker() {
    if (worker) return worker;
    try {
      worker = new Worker('/dtrh/engine/loomWorker.js', { type: 'module' });
      worker.onmessage = (e) => onWorker(e.data);
      worker.onerror = () => { encoding = false; status = 'the loom jammed. try again.'; redraw(); };
    } catch (e) {
      worker = null;
    }
    return worker;
  }

  function onWorker(msg) {
    if (msg.id !== jobId) return;
    if (msg.progress != null) {
      progress = msg.progress;
      const bar = lastBody && lastBody.querySelector('.wr-loom-progress');
      if (bar) bar.textContent = `weaving… ${Math.round(progress * 100)}%`;
      return;
    }
    if (msg.error) {
      pendingJob = null;
      encoding = false;
      status = 'the thread snapped: ' + msg.error;
      redraw();
      return;
    }
    if (msg.gif) {
      // hand the finished file to the host; loom-result closes the loop.
      // Send what was true when SAVE was clicked - the pane stays live during
      // the encode, so module state may have drifted since.
      const job = pendingJob;
      if (!job || job.id !== msg.id) return;
      pendingJob = null;
      try {
        bridge.send({
          type: 'loom-save',
          name: job.name,
          overwrite: job.overwrite,
          params: job.params,
          gifBase64: b64FromBuffer(msg.gif),
        });
      } catch (e) {
        encoding = false;
        status = 'the loom jammed. try again.';
        redraw();
      }
    }
  }

  /** Host answered loom-save/loom-delete. A fresh loom-list follows separately. */
  function onResult(res) {
    if (!res) return;
    if (res.op === 'save') {
      encoding = false;
      overwriteArmed = false;
      if (res.ok) { status = `kept as ${res.slug}. the tube knows it now.`; if (sfx) sfx('boon_pick', 0.4); }
      else status = ({
        'cap-reached': `the rack is full — ${MAX_SPIRALS} spirals. forget one first.`,
        'bad-name': 'that name won’t weave. letters and numbers, love.',
        'too-big': 'too heavy to hang. simpler colors, fewer arms.',
        'bad-gif': 'the weave came out wrong. try again.',
        'exists': 'you already keep one by that name. SAVE again to overwrite it.',
      })[res.error] || ('she refused: ' + (res.error || 'unknown'));
      if (!res.ok && res.error === 'exists') overwriteArmed = true;
      redraw();
    } else if (res.op === 'delete') {
      deleteArmed = null;
      status = res.ok ? 'forgotten.' : ('she refused: ' + (res.error || 'unknown'));
      redraw();
    }
  }

  /** Host posted the library (page-ready + after each save/delete). */
  function onList(list) {
    saved = Array.isArray(list) ? list : [];
    redraw();
  }

  function startSave() {
    if (encoding) return;
    const slug = slugify(name);
    if (!slug) { status = 'name it first, love.'; redraw(); return; }
    if (!overwriteArmed && saved.length >= MAX_SPIRALS && !saved.some((s) => s.slug === slug)) {
      status = `the rack is full — ${MAX_SPIRALS} spirals. forget one first.`;
      redraw();
      return;
    }
    const w = ensureWorker();
    if (!w) { status = 'this machine can’t run the loom (no worker).'; redraw(); return; }
    encoding = true;
    progress = 0;
    status = null;
    jobId++;
    const jobParams = normalizeParams2(params);
    pendingJob = { id: jobId, name, params: jobParams, overwrite: overwriteArmed };
    w.postMessage({ id: jobId, params: jobParams });
    redraw();
  }

  // ---- preview loop: phase from wall-clock over loopMs2, so preview speed
  // equals GIF speed. WebGL field composited onto the visible 2D canvas (the
  // exact worker pipeline); no-GL machines fall back to the v1 wedges + the
  // same centerpiece composite. Self-stops when the canvas leaves the DOM. ----
  function ensureField(w, h) {
    if (fieldFailed) return null;
    if (field && field.canvas.width === w && field.canvas.height === h) return field;
    try {
      // format changed: retire the old-aspect context before making a new one
      if (field) {
        try { const lose = field.gl.getExtension('WEBGL_lose_context'); if (lose) lose.loseContext(); } catch (e) { /* ignore */ }
        field = null;
      }
      const glCanvas = document.createElement('canvas');
      glCanvas.width = w; glCanvas.height = h;
      field = createFieldRenderer(glCanvas);
      if (!field) fieldFailed = true;
    } catch (e) {
      field = null;
      fieldFailed = true;
    }
    return field;
  }

  function drawInto(ctx, w, h, phase) {
    const f = ensureField(w, h);
    if (f) composeFrame(ctx, f, params, phase, w, h);
    else drawFallbackFrame(ctx, params, phase, w, h);
  }

  function tickPreview() {
    raf = 0;
    if (!previewCanvas || !previewCanvas.isConnected) return;
    const span = loopMs2(params);
    const phase = ((performance.now() - t0) % span) / span;
    const ctx = previewCanvas.getContext('2d');
    if (ctx) drawInto(ctx, previewCanvas.width, previewCanvas.height, phase);
    // the fullscreen mirror, at its own (higher) resolution
    if (fsCanvas && fsCanvas.isConnected) {
      const fctx = fsCanvas.getContext('2d');
      if (fctx) {
        if (!fsField && !fsFieldFailed && !fieldFailed) {
          try {
            const c = document.createElement('canvas');
            c.width = fsCanvas.width; c.height = fsCanvas.height;
            fsField = createFieldRenderer(c);
            if (!fsField) fsFieldFailed = true;
          } catch (e) { fsFieldFailed = true; }
        }
        if (fsField) composeFrame(fctx, fsField, params, phase, fsCanvas.width, fsCanvas.height);
        else drawFallbackFrame(fctx, params, phase, fsCanvas.width, fsCanvas.height);
      }
    }
    raf = requestAnimationFrame(tickPreview);
  }

  // ---- fullscreen preview: ⛶ (or F) opens a black overlay with the spiral
  // at near-native resolution; esc / a click wakes you. Uses the Fullscreen
  // API when the host allows it, falls back to a fixed overlay when not. ----
  function onFsChange() {
    if (!document.fullscreenElement) exitFullscreen();
  }

  function enterFullscreen() {
    if (fsOverlay) return;
    // Native-resolution long side (capped for ultrawides): the overlay canvas
    // COVERS the screen (object-fit: cover), so render at screen pixels - the
    // field overdraws its corners, so the crop never shows dead edges.
    const long = Math.min(2048, Math.max(screen.width || 1080, screen.height || 1080));
    const dims = formatDims2(params.format, long);
    fsOverlay = document.createElement('div');
    fsOverlay.className = 'loom-fs-overlay';
    fsCanvas = document.createElement('canvas');
    fsCanvas.width = dims.w; fsCanvas.height = dims.h;
    fsOverlay.appendChild(fsCanvas);
    const hint = document.createElement('div');
    hint.className = 'loom-fs-hint';
    hint.textContent = 'esc wakes you';
    fsOverlay.appendChild(hint);
    fsOverlay.addEventListener('click', exitFullscreen);
    document.body.appendChild(fsOverlay);
    document.addEventListener('fullscreenchange', onFsChange);
    fsKeyHandler = (e) => { if (e.key === 'Escape') exitFullscreen(); };
    window.addEventListener('keydown', fsKeyHandler);
    try {
      const p = fsOverlay.requestFullscreen && fsOverlay.requestFullscreen();
      if (p && p.catch) p.catch(() => { /* overlay fallback is already up */ });
    } catch (e) { /* overlay fallback */ }
  }

  function exitFullscreen() {
    if (!fsOverlay) return;
    document.removeEventListener('fullscreenchange', onFsChange);
    if (fsKeyHandler) { window.removeEventListener('keydown', fsKeyHandler); fsKeyHandler = null; }
    try { if (document.fullscreenElement) document.exitFullscreen().catch(() => {}); } catch (e) { /* ignore */ }
    try { fsOverlay.remove(); } catch (e) { /* ignore */ }
    fsOverlay = null;
    fsCanvas = null;
    // free the hi-res GL context instead of letting them pile up per visit
    try {
      const lose = fsField && fsField.gl && fsField.gl.getExtension('WEBGL_lose_context');
      if (lose) lose.loseContext();
    } catch (e) { /* ignore */ }
    fsField = null;
    fsFieldFailed = false;
  }

  // ---- hotkeys (standalone window only - the game keeps its own keys) ----
  function onHotkey(e) {
    if (e.ctrlKey && !e.altKey && (e.key === 's' || e.key === 'S')) {
      e.preventDefault();
      startSave();
      return;
    }
    if (e.ctrlKey && !e.altKey && (e.key === 'z' || e.key === 'Z')) {
      e.preventDefault();
      undo();
      return;
    }
    const tag = e.target && e.target.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    if (e.key === 'f' || e.key === 'F') {
      if (fsOverlay) exitFullscreen(); else enterFullscreen();
    } else if (e.key === 'r' || e.key === 'R') {
      pushHistory();
      const fmt = params.format;
      params = randomParams2();
      params.format = fmt;
      redraw();
    } else {
      const n = parseInt(e.key, 10);
      if (n >= 1 && n <= LOOM_STYLES.length) patch((p) => { p.layer.style = LOOM_STYLES[n - 1]; });
    }
  }
  const hotkeyHandler = hotkeys ? onHotkey : null;
  if (hotkeyHandler) window.addEventListener('keydown', hotkeyHandler);

  function stop() {
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    previewCanvas = null;
    lastBody = null;
    exitFullscreen();
    if (hotkeyHandler) window.removeEventListener('keydown', hotkeyHandler);
    if (encoding && worker) { try { worker.postMessage({ cancel: jobId }); } catch (e) { /* ignore */ } }
    pendingJob = null;
    encoding = false;
  }

  function redraw() {
    if (lastBody && lastBody.isConnected) render(lastBody);
  }

  /** Mutate params through fn, re-normalize, redraw. The one write path. */
  function patch(fn) {
    pushHistory();
    fn(params);
    params = normalizeParams2(params);
    redraw();
  }

  const el = (cls, parent, text) => {
    const d = document.createElement('div');
    d.className = cls;
    if (text != null) d.textContent = text;
    parent.appendChild(d);
    return d;
  };

  function slider(parent, label, min, max, step, get, set, fmt, def) {
    const row = el('wr-loom-row', parent);
    el('wr-loom-lbl', row, label);
    const inp = document.createElement('input');
    inp.type = 'range';
    inp.min = String(min); inp.max = String(max); inp.step = String(step);
    inp.value = String(get());
    const show = () => (fmt ? fmt(get()) : String(get()));
    const val = el('wr-loom-val', row, show());
    inp.addEventListener('input', () => {
      // first tick of a drag: snapshot the pre-drag value for ↩
      if (!row.classList.contains('is-live')) pushHistory();
      row.classList.add('is-live');
      set(parseFloat(inp.value));
      params = normalizeParams2(params);
      val.textContent = show();
    });
    // on release: settle + reveal any dependent dials (breaths, ripples…)
    inp.addEventListener('change', () => { row.classList.remove('is-live'); redraw(); });
    if (def != null) {
      inp.title = 'double-click resets';
      inp.addEventListener('dblclick', () => {
        pushHistory();
        set(def);
        params = normalizeParams2(params);
        redraw();
      });
    }
    row.insertBefore(inp, val);
    return row;
  }

  function chip(parent, label, active, onClick, title) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'wr-craft-chip' + (active ? ' is-on' : '');
    b.textContent = label;
    if (title) b.title = title;
    b.addEventListener('click', onClick);
    parent.appendChild(b);
    return b;
  }

  function colorInput(parent, value, onPick) {
    const inp = document.createElement('input');
    inp.type = 'color';
    inp.value = value;
    inp.className = 'wr-loom-color';
    // 'input' streams live as you drag - apply straight to params so the running
    // preview follows the drag (no redraw: rebuilding the DOM mid-drag would tear
    // down the open OS dialog). 'change' fires once the dialog CLOSES, so it's
    // safe to commit with a full redraw - the same path presets/random take, and
    // the only one that reliably repaints the centerpiece across browsers.
    inp.addEventListener('input', () => onPick(inp.value));
    inp.addEventListener('change', () => { onPick(inp.value); redraw(); });
    parent.appendChild(inp);
    return inp;
  }

  /** Tiny woven thumbnails for the preset chips - rendered once through the
   * SAME field pipeline the preview uses, so each chip shows its true face. */
  function buildPresetThumbs() {
    if (thumbsBuilt) return;
    thumbsBuilt = true;
    // presets are square; runs once at mount, before any format switch
    const f = ensureField(previewSize, previewSize);
    const SIZE = 96;
    for (const [pname, ppatch] of PRESETS) {
      try {
        const q = presetParams(ppatch);
        const c = document.createElement('canvas');
        c.width = SIZE; c.height = SIZE;
        const ctx = c.getContext('2d');
        if (!ctx) return;
        if (f) composeFrame(ctx, f, q, 0.16, SIZE);
        else drawFallbackFrame(ctx, q, 0.16, SIZE);
        presetThumbs.set(pname, c.toDataURL('image/png'));
      } catch (e) { /* chip stays text-only */ }
    }
  }

  function render(body) {
    lastBody = body;
    ensureStudioCss();

    // The whole pane is rebuilt on every state change - keep every scroll
    // position through the wipe or releasing a slider yanks the dials back
    // to the top. Our own scrollers are re-queried after the rebuild; the
    // ancestor scroller (game pane / narrow page) survives the wipe but gets
    // CLAMPED while the content is momentarily empty, so re-assert it too.
    const keepScroll = [];
    for (const sel of ['.loom-controls', '.wr-card.wr-loom']) {
      const n = body.querySelector(sel);
      if (n && n.scrollTop) keepScroll.push([sel, n.scrollTop]);
    }
    let ancestor = body.parentElement;
    while (ancestor && ancestor.scrollHeight <= ancestor.clientHeight + 1) ancestor = ancestor.parentElement;
    const ancestorTop = ancestor ? ancestor.scrollTop : 0;
    const docEl = document.scrollingElement;
    const docTop = docEl ? docEl.scrollTop : 0;

    body.innerHTML = '';
    const studio = el('wr-loom-studio', body);

    /* ---- the stage: preview + name/save + patterns ---- */
    const stage = el('wr-card wr-loom', studio);
    el('wr-card-sub', stage, 'you drew the spiral once, in makeup. now the loom draws them for you.');

    const stageBox = el('loom-stage', stage);
    const stageInner = el('loom-stage-inner', stageBox);
    const dims = formatDims2(params.format, previewSize);
    stageInner.style.aspectRatio = `${dims.w} / ${dims.h}`;
    const pv = document.createElement('canvas');
    pv.className = 'wr-loom-preview';
    pv.width = dims.w; pv.height = dims.h;
    stageInner.appendChild(pv);

    // format set, top-left: the shape of the final gif
    const fmtBox = el('loom-fmt', stageInner);
    const fmtBtn = (label, key, title) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'loom-fmt-btn' + (params.format === key ? ' is-on' : '');
      b.textContent = label;
      b.title = title;
      b.addEventListener('click', () => patch((p) => { p.format = key; }));
      fmtBox.appendChild(b);
    };
    fmtBtn('▣', 'square', 'square · 1:1');
    fmtBtn('▬', 'wide', 'landscape · 16:9');
    fmtBtn('▮', 'tall', 'tiktok · 9:16');

    const fsBtn = document.createElement('button');
    fsBtn.type = 'button';
    fsBtn.className = 'loom-fs-btn';
    fsBtn.textContent = '⛶';
    fsBtn.title = (hotkeys ? 'fullscreen (F)' : 'fullscreen') + ' — esc wakes you';
    fsBtn.addEventListener('click', enterFullscreen);
    stageInner.appendChild(fsBtn);
    previewCanvas = pv;
    if (!t0) t0 = performance.now();
    if (!raf) raf = requestAnimationFrame(tickPreview);

    // name + save
    const saveRow = el('wr-loom-row wr-loom-save', stage);
    const nameInp = document.createElement('input');
    nameInp.type = 'text';
    nameInp.maxLength = 24;
    nameInp.placeholder = 'name your spiral';
    nameInp.className = 'wr-loom-name';
    nameInp.value = name;
    nameInp.addEventListener('input', () => { name = nameInp.value; overwriteArmed = false; });
    saveRow.appendChild(nameInp);
    const save = document.createElement('button');
    save.type = 'button';
    save.className = 'wr-buy wr-craft-go';
    save.disabled = encoding;
    save.textContent = encoding ? 'weaving…' : (overwriteArmed ? 'overwrite?' : 'SAVE');
    save.addEventListener('click', startSave);
    saveRow.appendChild(save);
    if (encoding) el('wr-loom-progress', stage, `weaving… ${Math.round(progress * 100)}%`);
    else if (status) el('wr-loom-status', stage, status);

    // patterns (presets) + surprise, each chip wearing its woven face
    buildPresetThumbs();
    const patterns = el('loom-patterns', stage);
    el('wr-loom-lbl loom-patterns-lbl', patterns, 'patterns');
    const presetRow = el('wr-loom-row wr-loom-styles', patterns);
    for (const [pname, ppatch] of PRESETS) {
      const b = chip(presetRow, '', false, () => {
        pushHistory();
        const fmt = params.format;   // patterns restyle the weave, not the frame shape
        params = presetParams(ppatch);
        params.format = fmt;
        redraw();
      });
      b.classList.add('loom-preset');
      const thumb = presetThumbs.get(pname);
      if (thumb) {
        const im = document.createElement('img');
        im.className = 'loom-preset-thumb';
        im.src = thumb;
        im.alt = '';
        b.appendChild(im);
      }
      const lbl = document.createElement('span');
      lbl.textContent = pname;
      b.appendChild(lbl);
    }
    chip(presetRow, '🎲', false, () => {
      pushHistory();
      const fmt = params.format;
      params = randomParams2();
      params.format = fmt;
      redraw();
    }, 'surprise me');
    const undoChip = chip(presetRow, '↩', false, undo, 'rewind the last change' + (hotkeys ? ' (ctrl+Z)' : ''));
    if (!history.length) undoChip.classList.add('is-off');
    if (hotkeys) el('loom-hint', stage, 'F fullscreen · R surprise me · ctrl+Z rewind · ctrl+S save · 1–6 weave styles');

    /* ---- the dials, grouped ---- */
    const controls = el('loom-controls', studio);
    const sec = (title) => {
      const c = el('wr-card loom-sec', controls);
      el('wr-card-h', c, title);
      return c;
    };

    // THE WEAVE
    const weave = sec('the weave');
    slider(weave, 'arms', 1, 12, 1, () => params.layer.arms, (v) => { params.layer.arms = v; }, null, DEF.layer.arms);
    slider(weave, 'turns', 0.5, 6, 0.25, () => params.layer.turns, (v) => { params.layer.turns = v; }, null, DEF.layer.turns);
    slider(weave, 'body', 0.2, 0.8, 0.05, () => params.layer.duty, (v) => { params.layer.duty = v; }, null, DEF.layer.duty);
    const styleRow = el('wr-loom-row wr-loom-styles', weave);
    el('wr-loom-lbl', styleRow, 'style');
    for (const s of LOOM_STYLES) {
      chip(styleRow, s, params.layer.style === s, () => patch((p) => { p.layer.style = s; }));
    }
    const spinRow = el('wr-loom-row wr-loom-styles', weave);
    el('wr-loom-lbl', spinRow, 'spin');
    chip(spinRow, '↻ inward', params.layer.direction === 1, () => patch((p) => { p.layer.direction = 1; }), 'pulls toward the center');
    chip(spinRow, '↺ outward', params.layer.direction === -1, () => patch((p) => { p.layer.direction = -1; }), 'blooms out from the center');

    // THE THREADS
    const threads = sec('the threads');
    const colRow = el('wr-loom-row wr-loom-colors', threads);
    el('wr-loom-lbl', colRow, 'thread');
    for (const c of LOOM_SWATCHES) {
      const sw = document.createElement('button');
      sw.type = 'button';
      sw.className = 'sf-swatch wr-loom-swatch';
      sw.style.background = c;
      sw.addEventListener('click', () => patch((p) => { p.layer.colors[0] = c; }));
      colRow.appendChild(sw);
    }
    const pickRow = el('wr-loom-row wr-loom-colors', threads);
    params.layer.colors.forEach((c, i) => {
      colorInput(pickRow, c, (v) => { params.layer.colors[i] = v; params = normalizeParams2(params); });
    });
    if (params.layer.colors.length < 6) {
      chip(pickRow, '+ thread', false, () => patch((p) => {
        p.layer.colors.push(p.layer.colors[p.layer.colors.length - 1]);
      }));
    }
    if (params.layer.colors.length > 1) {
      chip(pickRow, '−', false, () => patch((p) => { p.layer.colors.pop(); }), 'pull the last thread');
    }
    chip(pickRow, 'gradient', params.layer.bandMode === 'gradient', () => patch((p) => {
      p.layer.bandMode = p.layer.bandMode === 'gradient' ? 'hard' : 'gradient';
    }), 'blend the threads instead of candy stripes');
    const bgRow = el('wr-loom-row wr-loom-colors', threads);
    el('wr-loom-lbl', bgRow, 'backing');
    colorInput(bgRow, params.bg.color, (v) => { params.bg.color = v; });
    chip(bgRow, 'radial', params.bg.kind === 'radial', () => patch((p) => {
      p.bg.kind = p.bg.kind === 'radial' ? 'solid' : 'radial';
    }), 'vignette toward an edge color');
    if (params.bg.kind === 'radial') colorInput(bgRow, params.bg.outer, (v) => { params.bg.outer = v; });

    // THE MOTION
    const motion = sec('the motion');
    slider(motion, 'speed', 1, 5, 1, () => params.speed, (v) => { params.speed = v; }, null, DEF.speed);
    const l2Row = el('wr-loom-row wr-loom-styles', motion);
    chip(l2Row, 'second weave', params.layer2.enabled, () => patch((p) => {
      p.layer2.enabled = !p.layer2.enabled;
      if (p.layer2.enabled) p.layer2.direction = -p.layer.direction;   // she spins against you
    }), 'a counter-rotating layer of her own');
    if (params.layer2.enabled) {
      const sub = el('loom-sub', motion);
      slider(sub, 'her arms', 1, 12, 1, () => params.layer2.arms, (v) => { params.layer2.arms = v; }, null, DEF.layer2.arms);
      slider(sub, 'her turns', 0.5, 6, 0.25, () => params.layer2.turns, (v) => { params.layer2.turns = v; }, null, DEF.layer2.turns);
      slider(sub, 'her pace', 1, 3, 1, () => params.layer2.speedMul, (v) => { params.layer2.speedMul = v; }, (v) => v + 'x', DEF.layer2.speedMul);
      const l2c = el('wr-loom-row wr-loom-colors', sub);
      el('wr-loom-lbl', l2c, 'her threads');
      params.layer2.colors.forEach((c, i) => {
        colorInput(l2c, c, (v) => { params.layer2.colors[i] = v; params = normalizeParams2(params); });
      });
      if (params.layer2.colors.length < 2) {
        chip(l2c, '+', false, () => patch((p) => { p.layer2.colors.push('#8a5cff'); }));
      } else {
        chip(l2c, '−', false, () => patch((p) => { p.layer2.colors.pop(); }));
      }
    }

    // THE EFFECTS
    const effects = sec('the effects');
    slider(effects, 'glow', 0, 100, 5, () => Math.round(params.glow * 100), (v) => { params.glow = v / 100; }, (v) => v + '%', Math.round(DEF.glow * 100));
    slider(effects, 'pulse', 0, 25, 1, () => Math.round(params.pulse.amp * 100), (v) => { params.pulse.amp = v / 100; }, (v) => (v ? v + '%' : 'off'), Math.round(DEF.pulse.amp * 100));
    if (params.pulse.amp > 0) {
      slider(effects, 'breaths', 1, 4, 1, () => params.pulse.cycles, (v) => { params.pulse.cycles = v; }, null, DEF.pulse.cycles);
    }
    slider(effects, 'wobble', 0, 35, 1, () => Math.round(params.wobble.amp * 100), (v) => { params.wobble.amp = v / 100; }, (v) => (v ? v + '%' : 'off'), Math.round(DEF.wobble.amp * 100));
    if (params.wobble.amp > 0) {
      slider(effects, 'ripples', 1, 6, 1, () => params.wobble.freq, (v) => { params.wobble.freq = v; }, null, DEF.wobble.freq);
    }
    const hueRow = el('wr-loom-row wr-loom-styles', effects);
    el('wr-loom-lbl', hueRow, 'hue drift');
    chip(hueRow, 'still', params.hueCycles === 0, () => patch((p) => { p.hueCycles = 0; }));
    chip(hueRow, 'one turn', params.hueCycles === 1, () => patch((p) => { p.hueCycles = 1; }));
    chip(hueRow, 'two turns', params.hueCycles === 2, () => patch((p) => { p.hueCycles = 2; }));

    // THE CENTERPIECE
    const center = sec('the centerpiece');
    const cpRow = el('wr-loom-row wr-loom-styles', center);
    el('wr-loom-lbl', cpRow, 'kind');
    for (const k of ['none', 'dot', 'star', 'cross', 'x', 'mantra']) {
      chip(cpRow, k === 'mantra' ? 'word' : k, params.centerpiece.kind === k,
        () => patch((p) => { p.centerpiece.kind = k; }));
    }
    if (params.centerpiece.kind !== 'none') {
      const cpOpts = el('wr-loom-row wr-loom-colors', center);
      el('wr-loom-lbl', cpOpts, 'its color');
      colorInput(cpOpts, params.centerpiece.color, (v) => { params.centerpiece.color = v; params = normalizeParams2(params); });
      slider(center, 'its size', 8, 40, 1, () => Math.round(params.centerpiece.sizeFrac * 100),
        (v) => { params.centerpiece.sizeFrac = v / 100; }, (v) => v + '%', Math.round(DEF.centerpiece.sizeFrac * 100));
      if (params.centerpiece.kind === 'mantra') {
        const wordRow = el('wr-loom-row', center);
        el('wr-loom-lbl', wordRow, 'the word');
        const word = document.createElement('input');
        word.type = 'text';
        word.maxLength = 12;
        word.placeholder = 'obey…';
        word.className = 'wr-loom-name';
        word.value = params.centerpiece.text;
        word.addEventListener('input', () => { params.centerpiece.text = word.value.slice(0, 12); });
        wordRow.appendChild(word);
        slider(center, 'flashes', 0, 4, 1, () => params.centerpiece.flashCycles,
          (v) => { params.centerpiece.flashCycles = v; }, (v) => (v === 0 ? 'steady' : String(v)), DEF.centerpiece.flashCycles);
      }
    }

    /* ---- the rack: saved spirals (thumb / re-edit / two-click forget) ---- */
    const rack = el('wr-card wr-loom-rack', studio);
    el('wr-card-sub', rack, `the rack · ${saved.length}/${MAX_SPIRALS} — they hang in the app's spiral library too.`);
    const strip = el('loom-rack-strip', rack);
    if (!saved.length) el('wr-empty-note', strip, 'nothing hangs here yet — name your weave and SAVE it.');
    for (const s of saved) {
      const row = el('wr-row is-owned wr-loom-rackrow', strip);
      const img = document.createElement('img');
      img.className = 'wr-loom-thumb';
      img.src = s.url;
      img.alt = s.slug;
      img.loading = 'lazy';
      row.appendChild(img);
      const mid = el('wr-row-mid', row);
      el('wr-row-name', mid, s.slug);
      const right = el('wr-row-right', row);
      const edit = document.createElement('button');
      edit.type = 'button';
      edit.className = 'wr-craft-chip';
      edit.textContent = '✎';
      edit.title = 're-edit (SAVE overwrites it)';
      edit.addEventListener('click', () => {
        pushHistory();
        if (s.params) params = normalizeParams2(s.params);
        name = s.slug;
        overwriteArmed = true;
        status = `re-editing ${s.slug} — SAVE overwrites it.`;
        redraw();
      });
      right.appendChild(edit);
      const reveal = document.createElement('button');
      reveal.type = 'button';
      reveal.className = 'wr-craft-chip';
      reveal.textContent = '📂';
      reveal.title = 'show the gif file in its folder';
      reveal.addEventListener('click', () => {
        try { bridge.send({ type: 'loom-reveal', slug: s.slug }); } catch (e) { /* ignore */ }
      });
      right.appendChild(reveal);
      const del = document.createElement('button');
      del.type = 'button';
      del.className = 'wr-craft-chip' + (deleteArmed === s.slug ? ' is-armed' : '');
      del.textContent = deleteArmed === s.slug ? 'forget?' : '🗑';
      del.addEventListener('click', () => {
        if (deleteArmed !== s.slug) { deleteArmed = s.slug; redraw(); return; }
        deleteArmed = null;
        try { bridge.send({ type: 'loom-delete', slug: s.slug }); } catch (e) { /* ignore */ }
      });
      right.appendChild(del);
    }

    // restore every scroll position captured before the wipe
    for (const [sel, top] of keepScroll) {
      const n = body.querySelector(sel);
      if (n) n.scrollTop = top;
    }
    if (ancestor) ancestor.scrollTop = ancestorTop;
    if (docEl) docEl.scrollTop = docTop;
  }

  return { render, onList, onResult, stop };
}
