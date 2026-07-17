/* ============================================================================
 * loomStudio.js - THE LOOM's pane module, schema v2: design a spiral, watch
 * it spin live, SAVE it as a seamless-loop GIF into the CCP Spirals library.
 *
 * v2 ("the most advanced spiral editor available"): six weave styles, up to
 * six threads with hard/gradient blending, a counter-rotating second weave,
 * glow, pulse, wobble, hue drift, a centerpiece (dot / eye / mantra word),
 * presets, and a 🎲. The preview and the encoder share ONE field pipeline
 * (shared/loomField.js - WebGL field + 2D centerpiece composite), so the file
 * always matches the pane; machines without WebGL fall back to the v1 wedge
 * renderer in BOTH places. v1 sidecars re-edit cleanly (normalizeParams2).
 *
 * Mounted in two homes: the Warren's Boudoir pane (crafted unlock, DTRH) and
 * the main app's Loom window (loom.html - always available). Same bridge
 * protocol as v1: loom-save / loom-delete out, loom-list / loom-result in.
 * All file authority stays C#-side (DtrhLoomStore).
 *
 * Module state survives warren's refreshWins innerHTML wipes (the Part 1
 * worktable pattern): params/name/armed states live here, render() redraws.
 * ==========================================================================*/

import { drawSpiral } from '../shared/loomSpiral.js';
import {
  normalizeParams2, loopMs2, randomParams2, projectToV1,
  createFieldRenderer, composeFrame, drawCenterpiece,
  LOOM_STYLES, LOOM_SWATCHES,
} from '../shared/loomField.js';

const MAX_SPIRALS = 12;

/** CCP-themed presets: one signature weave per built-in mod + a few classics. */
const PRESETS = [
  ['classic ccp', { layer: { arms: 4, turns: 2, colors: ['#e84393', '#8b5cf6'] }, bg: { color: '#121220' }, glow: 0.25 }],
  ['bambi haze', { layer: { arms: 6, style: 'ribbon', bandMode: 'gradient', colors: ['#ff69b4', '#ffb6c1', '#ff1493'] }, bg: { kind: 'radial', color: '#2a1030', outer: '#12081a' }, pulse: { amp: 0.08, cycles: 1 }, glow: 0.4 }],
  ['sissy swirl', { layer: { arms: 5, turns: 2.5, style: 'golden', colors: ['#9b59b6', '#bb8fce'] }, centerpiece: { kind: 'eye', color: '#bb8fce', sizeFrac: 0.14 } }],
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

export function createLoomStudio({ bridge, sfx }) {
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
  function ensureField(size) {
    if (field || fieldFailed) return field;
    try {
      const glCanvas = document.createElement('canvas');
      glCanvas.width = size; glCanvas.height = size;
      field = createFieldRenderer(glCanvas);
      if (!field) fieldFailed = true;
    } catch (e) {
      field = null;
      fieldFailed = true;
    }
    return field;
  }

  function tickPreview() {
    raf = 0;
    if (!previewCanvas || !previewCanvas.isConnected) return;
    const ctx = previewCanvas.getContext('2d');
    if (ctx) {
      const size = previewCanvas.width;
      const span = loopMs2(params);
      const phase = ((performance.now() - t0) % span) / span;
      const f = ensureField(size);
      if (f) {
        composeFrame(ctx, f, params, phase, size);
      } else {
        drawSpiral(ctx, size, projectToV1(params), phase);
        drawCenterpiece(ctx, params, phase, size);
      }
    }
    raf = requestAnimationFrame(tickPreview);
  }

  function stop() {
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    previewCanvas = null;
    lastBody = null;
    if (encoding && worker) { try { worker.postMessage({ cancel: jobId }); } catch (e) { /* ignore */ } }
    pendingJob = null;
    encoding = false;
  }

  function redraw() {
    if (lastBody && lastBody.isConnected) render(lastBody);
  }

  /** Mutate params through fn, re-normalize, redraw. The one write path. */
  function patch(fn) {
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

  function slider(parent, label, min, max, step, get, set, fmt) {
    const row = el('wr-loom-row', parent);
    el('wr-loom-lbl', row, label);
    const inp = document.createElement('input');
    inp.type = 'range';
    inp.min = String(min); inp.max = String(max); inp.step = String(step);
    inp.value = String(get());
    const show = () => (fmt ? fmt(get()) : String(get()));
    const val = el('wr-loom-val', row, show());
    inp.addEventListener('input', () => {
      set(parseFloat(inp.value));
      params = normalizeParams2(params);
      val.textContent = show();
    });
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
    inp.addEventListener('input', () => onPick(inp.value));
    parent.appendChild(inp);
    return inp;
  }

  function render(body) {
    lastBody = body;
    body.innerHTML = '';
    const card = el('wr-card wr-loom', body);
    el('wr-card-sub', card, 'you drew the spiral once, in makeup. now the loom draws them for you.');

    // preview
    const pv = document.createElement('canvas');
    pv.className = 'wr-loom-preview';
    pv.width = 288; pv.height = 288;
    card.appendChild(pv);
    previewCanvas = pv;
    if (!t0) t0 = performance.now();
    if (!raf) raf = requestAnimationFrame(tickPreview);

    // dials
    slider(card, 'arms', 1, 12, 1, () => params.layer.arms, (v) => { params.layer.arms = v; });
    slider(card, 'turns', 0.5, 6, 0.25, () => params.layer.turns, (v) => { params.layer.turns = v; });
    slider(card, 'body', 0.2, 0.8, 0.05, () => params.layer.duty, (v) => { params.layer.duty = v; });
    slider(card, 'speed', 1, 5, 1, () => params.speed, (v) => { params.speed = v; });

    // weave style chips + direction
    const styleRow = el('wr-loom-row wr-loom-styles', card);
    for (const s of LOOM_STYLES) {
      chip(styleRow, s, params.layer.style === s, () => patch((p) => { p.layer.style = s; }));
    }
    chip(styleRow, params.layer.direction === 1 ? '↻ inward' : '↺ outward', false,
      () => patch((p) => { p.layer.direction = p.layer.direction === 1 ? -1 : 1; }));

    // threads: curated swatches set thread 1; pickers edit each; +/− manage count
    const colRow = el('wr-loom-row wr-loom-colors', card);
    el('wr-loom-lbl', colRow, 'thread');
    for (const c of LOOM_SWATCHES) {
      const sw = document.createElement('button');
      sw.type = 'button';
      sw.className = 'sf-swatch wr-loom-swatch';
      sw.style.background = c;
      sw.addEventListener('click', () => patch((p) => { p.layer.colors[0] = c; }));
      colRow.appendChild(sw);
    }
    const pickRow = el('wr-loom-row wr-loom-colors', card);
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

    // backing
    const bgRow = el('wr-loom-row wr-loom-colors', card);
    el('wr-loom-lbl', bgRow, 'backing');
    colorInput(bgRow, params.bg.color, (v) => { params.bg.color = v; });
    chip(bgRow, 'radial', params.bg.kind === 'radial', () => patch((p) => {
      p.bg.kind = p.bg.kind === 'radial' ? 'solid' : 'radial';
    }), 'vignette toward an edge color');
    if (params.bg.kind === 'radial') colorInput(bgRow, params.bg.outer, (v) => { params.bg.outer = v; });

    // the second weave
    const l2Row = el('wr-loom-row wr-loom-styles', card);
    chip(l2Row, 'second weave', params.layer2.enabled, () => patch((p) => {
      p.layer2.enabled = !p.layer2.enabled;
      if (p.layer2.enabled) p.layer2.direction = -p.layer.direction;   // she spins against you
    }), 'a counter-rotating layer of her own');
    if (params.layer2.enabled) {
      slider(card, 'her arms', 1, 12, 1, () => params.layer2.arms, (v) => { params.layer2.arms = v; });
      slider(card, 'her turns', 0.5, 6, 0.25, () => params.layer2.turns, (v) => { params.layer2.turns = v; });
      slider(card, 'her pace', 1, 3, 1, () => params.layer2.speedMul, (v) => { params.layer2.speedMul = v; }, (v) => v + 'x');
      const l2c = el('wr-loom-row wr-loom-colors', card);
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

    // effects
    el('wr-loom-lbl', el('wr-loom-row', card), 'effects');
    slider(card, 'glow', 0, 100, 5, () => Math.round(params.glow * 100), (v) => { params.glow = v / 100; }, (v) => v + '%');
    slider(card, 'pulse', 0, 25, 1, () => Math.round(params.pulse.amp * 100), (v) => { params.pulse.amp = v / 100; }, (v) => (v ? v + '%' : 'off'));
    if (params.pulse.amp > 0) {
      slider(card, 'breaths', 1, 4, 1, () => params.pulse.cycles, (v) => { params.pulse.cycles = v; });
    }
    slider(card, 'wobble', 0, 35, 1, () => Math.round(params.wobble.amp * 100), (v) => { params.wobble.amp = v / 100; }, (v) => (v ? v + '%' : 'off'));
    if (params.wobble.amp > 0) {
      slider(card, 'ripples', 1, 6, 1, () => params.wobble.freq, (v) => { params.wobble.freq = v; });
    }
    const hueRow = el('wr-loom-row wr-loom-styles', card);
    el('wr-loom-lbl', hueRow, 'hue drift');
    chip(hueRow, 'still', params.hueCycles === 0, () => patch((p) => { p.hueCycles = 0; }));
    chip(hueRow, 'one turn', params.hueCycles === 1, () => patch((p) => { p.hueCycles = 1; }));
    chip(hueRow, 'two turns', params.hueCycles === 2, () => patch((p) => { p.hueCycles = 2; }));

    // centerpiece
    const cpRow = el('wr-loom-row wr-loom-styles', card);
    el('wr-loom-lbl', cpRow, 'centerpiece');
    for (const k of ['none', 'dot', 'eye', 'mantra']) {
      chip(cpRow, k === 'mantra' ? 'word' : k, params.centerpiece.kind === k,
        () => patch((p) => { p.centerpiece.kind = k; }));
    }
    if (params.centerpiece.kind !== 'none') {
      const cpOpts = el('wr-loom-row wr-loom-colors', card);
      colorInput(cpOpts, params.centerpiece.color, (v) => { params.centerpiece.color = v; });
      slider(card, 'its size', 8, 40, 1, () => Math.round(params.centerpiece.sizeFrac * 100),
        (v) => { params.centerpiece.sizeFrac = v / 100; }, (v) => v + '%');
      if (params.centerpiece.kind === 'mantra') {
        const wordRow = el('wr-loom-row', card);
        el('wr-loom-lbl', wordRow, 'the word');
        const word = document.createElement('input');
        word.type = 'text';
        word.maxLength = 12;
        word.placeholder = 'obey…';
        word.className = 'wr-loom-name';
        word.value = params.centerpiece.text;
        word.addEventListener('input', () => { params.centerpiece.text = word.value.slice(0, 12); });
        wordRow.appendChild(word);
        slider(card, 'flashes', 0, 4, 1, () => params.centerpiece.flashCycles,
          (v) => { params.centerpiece.flashCycles = v; }, (v) => (v === 0 ? 'steady' : String(v)));
      }
    }

    // presets + surprise
    const presetRow = el('wr-loom-row wr-loom-styles', card);
    el('wr-loom-lbl', presetRow, 'patterns');
    for (const [pname, ppatch] of PRESETS) {
      chip(presetRow, pname, false, () => { params = presetParams(ppatch); redraw(); });
    }
    chip(presetRow, '🎲', false, () => { params = randomParams2(); redraw(); }, 'surprise me');

    // name + save
    const saveRow = el('wr-loom-row wr-loom-save', card);
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
    if (encoding) el('wr-loom-progress', card, `weaving… ${Math.round(progress * 100)}%`);
    else if (status) el('wr-loom-status', card, status);

    // the rack: saved spirals (thumb / re-edit / two-click forget)
    const rack = el('wr-card wr-loom-rack', body);
    el('wr-card-sub', rack, `the rack · ${saved.length}/${MAX_SPIRALS} — they hang in the app's spiral library too.`);
    for (const s of saved) {
      const row = el('wr-row is-owned wr-loom-rackrow', rack);
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
        if (s.params) params = normalizeParams2(s.params);
        name = s.slug;
        overwriteArmed = true;
        status = `re-editing ${s.slug} — SAVE overwrites it.`;
        redraw();
      });
      right.appendChild(edit);
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
  }

  return { render, onList, onResult, stop };
}
