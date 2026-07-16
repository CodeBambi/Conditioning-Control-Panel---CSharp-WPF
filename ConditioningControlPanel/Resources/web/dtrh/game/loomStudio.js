/* ============================================================================
 * loomStudio.js - THE LOOM's pane module (Part 2): design a spiral, watch it
 * spin live, SAVE it as a seamless-loop GIF into the CCP Spirals library.
 *
 * The preview and the encoder share ONE renderer (shared/loomSpiral.js), so
 * the file always matches the pane. Encoding runs in engine/loomWorker.js
 * (module worker); the finished GIF goes to the host as base64 over the
 * bridge (loom-save) and the host answers with loom-result + a fresh
 * loom-list. All file authority is C#-side (DtrhLoomStore): slug rules, the
 * 12-spiral cap, size ceilings, magic-byte checks.
 *
 * Module state survives warren's refreshWins innerHTML wipes (the Part 1
 * worktable pattern): params/name/armed states live here, render() redraws.
 * ==========================================================================*/

import { drawSpiral, normalizeParams, loopMs } from '../shared/loomSpiral.js';

const MAX_SPIRALS = 12;
const PALETTE = ['#ff69b4', '#e56cc0', '#8a5cff', '#00e5ff', '#39ff9d', '#ffd94d', '#ff6a2f', '#ffffff'];

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
  let params = normalizeParams({});
  let name = '';
  let saved = [];             // host truth: [{ slug, url, params }]
  let worker = null;
  let jobId = 0;
  let encoding = false;       // a save job is in flight (worker + host round-trip)
  let progress = 0;
  let status = null;          // transient line under the SAVE button
  let overwriteArmed = false; // slug collision: first SAVE arms, second overwrites
  let deleteArmed = null;     // slug whose 🗑 was clicked once
  let lastBody = null;
  let raf = 0, previewCanvas = null, t0 = 0;

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
      encoding = false;
      status = 'the thread snapped: ' + msg.error;
      redraw();
      return;
    }
    if (msg.gif) {
      // hand the finished file to the host; loom-result closes the loop
      try {
        bridge.send({
          type: 'loom-save',
          name,
          overwrite: overwriteArmed,
          params,
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
    w.postMessage({ id: jobId, params });
    redraw();
  }

  // ---- preview loop: phase from wall-clock over loopMs, so preview speed
  // equals GIF speed. Self-stops when the canvas leaves the DOM. ----
  function tickPreview() {
    raf = 0;
    if (!previewCanvas || !previewCanvas.isConnected) return;
    const ctx = previewCanvas.getContext('2d');
    if (ctx) {
      const span = loopMs(params);
      const phase = ((performance.now() - t0) % span) / span;
      drawSpiral(ctx, previewCanvas.width, params, phase);
    }
    raf = requestAnimationFrame(tickPreview);
  }

  function stop() {
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    previewCanvas = null;
    lastBody = null;
    if (encoding && worker) { try { worker.postMessage({ cancel: jobId }); } catch (e) { /* ignore */ } }
    encoding = false;
  }

  function redraw() {
    if (lastBody && lastBody.isConnected) render(lastBody);
  }

  const el = (cls, parent, text) => {
    const d = document.createElement('div');
    d.className = cls;
    if (text != null) d.textContent = text;
    parent.appendChild(d);
    return d;
  };

  function slider(parent, label, min, max, step, get, set) {
    const row = el('wr-loom-row', parent);
    el('wr-loom-lbl', row, label);
    const inp = document.createElement('input');
    inp.type = 'range';
    inp.min = String(min); inp.max = String(max); inp.step = String(step);
    inp.value = String(get());
    const val = el('wr-loom-val', row, String(get()));
    inp.addEventListener('input', () => {
      set(parseFloat(inp.value));
      params = normalizeParams(params);
      val.textContent = String(get());
    });
    row.insertBefore(inp, val);
    return row;
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
    slider(card, 'arms', 2, 8, 1, () => params.arms, (v) => { params.arms = v; });
    slider(card, 'turns', 0.5, 4, 0.25, () => params.turns, (v) => { params.turns = v; });
    slider(card, 'body', 0.2, 0.8, 0.05, () => params.duty, (v) => { params.duty = v; });
    slider(card, 'speed', 1, 5, 1, () => params.speed, (v) => { params.speed = v; });

    // style chips + direction
    const styleRow = el('wr-loom-row wr-loom-styles', card);
    for (const s of ['log', 'arch', 'ribbon']) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'wr-craft-chip' + (params.style === s ? ' is-on' : '');
      b.textContent = s;
      b.addEventListener('click', () => { params.style = s; redraw(); });
      styleRow.appendChild(b);
    }
    const dir = document.createElement('button');
    dir.type = 'button';
    dir.className = 'wr-craft-chip';
    dir.textContent = params.direction === 1 ? '↻ inward' : '↺ outward';
    dir.addEventListener('click', () => { params.direction = params.direction === 1 ? -1 : 1; redraw(); });
    styleRow.appendChild(dir);

    // colors: curated swatches + free pickers (1-2 colors + bg)
    const colRow = el('wr-loom-row wr-loom-colors', card);
    el('wr-loom-lbl', colRow, 'thread');
    for (const c of PALETTE) {
      const sw = document.createElement('button');
      sw.type = 'button';
      sw.className = 'sf-swatch wr-loom-swatch';
      sw.style.background = c;
      sw.addEventListener('click', () => { params.colors = [c, ...(params.colors.slice(1))].slice(0, params.colors.length); params = normalizeParams(params); redraw(); });
      colRow.appendChild(sw);
    }
    const pickRow = el('wr-loom-row wr-loom-colors', card);
    colorInput(pickRow, params.colors[0], (v) => { params.colors[0] = v; });
    const two = document.createElement('button');
    two.type = 'button';
    two.className = 'wr-craft-chip' + (params.colors.length === 2 ? ' is-on' : '');
    two.textContent = 'second thread';
    two.addEventListener('click', () => {
      params.colors = params.colors.length === 2 ? [params.colors[0]] : [params.colors[0], '#8a5cff'];
      redraw();
    });
    pickRow.appendChild(two);
    if (params.colors.length === 2) colorInput(pickRow, params.colors[1], (v) => { params.colors[1] = v; });
    el('wr-loom-lbl', pickRow, '· backing');
    colorInput(pickRow, params.bg, (v) => { params.bg = v; });

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
        if (s.params) params = normalizeParams(s.params);
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
