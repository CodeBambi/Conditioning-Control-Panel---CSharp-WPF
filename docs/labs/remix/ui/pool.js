// The pool: every file the user has loaded, however many. The canvas holds eight at a time; the pool
// says which eight. Pins are the ones you chose (tap a gif, it gets a check and stays on every roll);
// the rest of the slots roll from the pool. Under the stage on the Auto page: one slot per gif in use,
// pins first with their check, then the roll's picks, then empty slots, and a count button that opens
// the grid. Only the picks are decoded: the pool holds a file and one small frame per entry.
import { ICONS } from './hud.js';
import { probeMedia, POOL_CAP, pickSet, defaultCount, clampCountFor, sameSet, codeToSeed, randomCode } from './engine-bridge.js';

const CHECK = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m5 12 5 5 9-10"/></svg>';
const CLOSE = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>';
const MAX_ON = 8, APPLY_MS = 220;
let poolSeq = 0;
const nextId = () => 'p' + (++poolSeq).toString(36) + '-' + Math.floor(Math.random() * 1e6).toString(36);
const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches;

export function initPool(ctx) {
  const p = ctx.project;
  const entries = []; // { id, file, name, kind, srcW, srcH, srcFrames, srcDurMs, thumb, colour }
  let pins = [], count = null; // count null = the whole pool, eight at most
  let seq = 0, rolling = false, queued = null, applyTimer = 0, busy = false;
  let strip = null, host = null;
  const byId = (id) => entries.find((e) => e.id === id) || null;
  const currentSet = () => p.tiles.map((t) => t.mediaId);
  const effectiveCount = () => clampCountFor(count == null ? defaultCount(entries.length, pins.length) : count, entries.length, pins.length);
  const setBusy = (on) => { if (busy === on) return; busy = on; ctx.emit('busy', on); };

  // ---- in and out
  async function add(files, { onEach } = {}) {
    let added = 0, first = null;
    for (const f of files) {
      if (entries.length >= POOL_CAP) { ctx.toast(`${POOL_CAP} is the most the pool holds.`, { gold: true }); break; }
      try {
        const m = await probeMedia(f);
        const e = Object.assign({ id: nextId(), file: f }, m);
        entries.push(e); added++; if (!first) first = e;
        ctx.emit('pool');
        if (onEach) await onEach(e, added);
      } catch (err) { ctx.toast((err && err.message) || `Could not read ${f.name}`, { gold: true }); }
    }
    return { added, first };
  }
  function remove(id) {
    const i = entries.findIndex((e) => e.id === id); if (i < 0) return;
    const [e] = entries.splice(i, 1);
    pins = pins.filter((x) => x !== id);
    const used = currentSet().includes(id);
    if (p.hasMedia(id)) p.removeMedia(id);
    ctx.emit('pool');
    if (used && entries.length) apply(); else ctx.commit('remove from pool');
    ctx.toast(`Removed ${e.name}`);
  }
  /** Drop the entry only; the editor's own remove has already taken the clip off the project. */
  function forget(id) { const i = entries.findIndex((e) => e.id === id); if (i < 0) return; entries.splice(i, 1); pins = pins.filter((x) => x !== id); ctx.emit('pool'); }
  function clear() { entries.length = 0; pins = []; count = null; ctx.emit('pool'); }

  // ---- pins and the count
  function togglePin(id) {
    if (!byId(id)) return false;
    if (pins.includes(id)) pins = pins.filter((x) => x !== id);
    else if (pins.length >= MAX_ON) { ctx.toast('Eight is the most that fit on the canvas.', { gold: true }); return false; }
    else pins = pins.concat(id);
    ctx.emit('pool'); apply(); return true;
  }
  function clearPins() { if (!pins.length) return; pins = []; ctx.emit('pool'); apply(); }
  function setCount(n) { const v = clampCountFor(n, entries.length, pins.length); if (v === effectiveCount()) return; count = v; ctx.emit('pool'); apply(); }
  // a pin or a count change lands as a roll on the same code a beat later, so quick taps become one
  function apply() { clearTimeout(applyTimer); applyTimer = setTimeout(() => { if (entries.length) rollSet(p.seed); }, APPLY_MS); }

  // ---- decoding: only what goes on the canvas
  async function decodeInto(ids, onEach) {
    const turn = ++seq;
    const missing = ids.filter((id) => !p.hasMedia(id) && byId(id));
    if (missing.length) setBusy(true);
    for (const id of missing) {
      const e = byId(id); if (!e) continue;
      try { await p.addMedia(e.file, { id, kind: e.kind }); }
      catch (err) { ctx.toast((err && err.message) || `Could not read ${e.name}`, { gold: true }); const i = entries.indexOf(e); if (i > -1) entries.splice(i, 1); pins = pins.filter((x) => x !== id); ctx.emit('pool'); }
      if (turn !== seq) return false;
      if (onEach) onEach(id);
    }
    if (turn !== seq) return false;
    setBusy(false);
    return true;
  }
  /** Exactly these ids on the canvas. Progressive: when the new set only adds to the current one
   *  (the first drop), each clip lands as it decodes; otherwise everything decodes first, then one swap. */
  async function ensureSet(ids, { progressive } = {}) {
    const cur = currentSet();
    const grows = progressive && cur.every((id, i) => ids[i] === id);
    const on = new Set(cur);
    const land = grows ? (id) => { on.add(id); p.setMediaSet(ids.filter((x) => on.has(x))); } : null;
    if (!(await decodeInto(ids, land))) return false;
    if (!sameSet(currentSet(), ids)) p.setMediaSet(ids);
    return true;
  }

  // ---- the roll and the arrows. One at a time; a roll asked for mid-decode waits and goes last
  async function rollSet(seed, opts = {}) {
    if (!entries.length) return null;
    if (rolling) { queued = [seed, opts]; return null; }
    rolling = true;
    try {
      const s = seed == null ? codeToSeed(randomCode()) : (typeof seed === 'string' ? codeToSeed(seed) : seed >>> 0);
      const ids = pickSet({ pool: entries, pins, count, seed: s });
      if (!(await ensureSet(ids, opts))) return null;
      if (opts.before) opts.before();
      const code = p.roll(s, { replace: !!opts.replace });
      if (opts.after) opts.after();
      return code;
    } finally { rolling = false; if (queued) { const [qs, qo] = queued; queued = null; rollSet(qs, qo); } }
  }
  async function walk(dir, { before, after } = {}) {
    const e = p.peekRoll(dir);
    if (!e || rolling) return false;
    rolling = true;
    try {
      if (!(await decodeInto(e.set))) return false;
      if (before) before();
      if (dir < 0) p.rollBack(); else p.rollForward();
      if (after) after();
      return true;
    } finally { rolling = false; }
  }

  // ---- the strip under the stage: one slot per gif in use, then the count
  const thumbCanvas = (e) => {
    const cv = document.createElement('canvas');
    const t = e.thumb; cv.width = t.width || 144; cv.height = t.height || 144;
    try { cv.getContext('2d').drawImage(t, 0, 0); } catch { /* a closed bitmap draws nothing */ }
    return cv;
  };
  function mountStrip(el) { strip = el; render(); }
  function render() {
    if (!strip) return;
    strip.innerHTML = '';
    if (!entries.length) { strip.hidden = true; return; }
    strip.hidden = false;
    const n = effectiveCount(), cur = currentSet();
    const slots = document.createElement('div'); slots.className = 'pool-slots'; slots.setAttribute('role', 'group'); slots.setAttribute('aria-label', 'Gifs in this roll');
    const shown = pins.filter((id) => byId(id)).concat(cur.filter((id) => !pins.includes(id) && byId(id)));
    for (let i = 0; i < n; i++) {
      const id = shown[i], e = id && byId(id);
      const b = document.createElement('button'); b.type = 'button'; b.className = 'pool-slot';
      if (!e) { b.classList.add('empty'); b.title = 'Empty slot. The roll fills it.'; b.setAttribute('aria-label', 'Empty slot, open the pool'); b.innerHTML = ICONS.add; b.addEventListener('click', openManager); }
      else {
        const pinned = pins.includes(id);
        b.classList.add(pinned ? 'pinned' : 'rolled'); b.dataset.id = id;
        b.title = pinned ? `${e.name} stays on every roll. Tap to let it roll.` : `${e.name}. Tap to keep it on every roll.`;
        b.setAttribute('aria-label', b.title); b.setAttribute('aria-pressed', String(pinned));
        b.appendChild(thumbCanvas(e));
        const badge = document.createElement('i'); badge.className = 'pool-check'; badge.innerHTML = CHECK; b.appendChild(badge);
        b.addEventListener('click', () => { if (!togglePin(id)) shiver(b); });
      }
      slots.appendChild(b);
    }
    strip.appendChild(slots);
    const btn = document.createElement('button'); btn.type = 'button'; btn.className = 'pool-count'; btn.setAttribute('aria-expanded', String(!!host)); btn.setAttribute('aria-haspopup', 'dialog');
    btn.innerHTML = `<span>${entries.length > n ? `${n} OF ${entries.length}` : `${entries.length} ${entries.length === 1 ? 'GIF' : 'GIFS'}`}</span>${ICONS.chev}`;
    btn.title = 'Open the pool: pick which gifs to use, and how many'; btn.addEventListener('click', () => (host ? closeManager() : openManager()));
    strip.appendChild(btn);
    if (host) renderManager();
  }
  function shiver(el) { if (reduced()) return; el.classList.remove('shiver'); void el.offsetWidth; el.classList.add('shiver'); el.addEventListener('animationend', () => el.classList.remove('shiver'), { once: true }); }

  // ---- the manager: the grid, the stepper, the pins
  function openManager() {
    if (host || !entries.length) return;
    host = document.createElement('div'); host.className = 'pool-host';
    host.innerHTML = `
      <div class="pool-sheet" role="dialog" aria-modal="true" aria-label="Your gifs">
        <div class="sheet-grab" aria-hidden="true"></div>
        <div class="pool-head">
          <div class="pool-title">Your gifs <span class="pool-n" id="pool-n"></span></div>
          <button type="button" class="pool-close" id="pool-close" aria-label="Close">${CLOSE}</button>
        </div>
        <div class="pool-tools">
          <div class="pool-stepper" role="group" aria-label="How many gifs each roll uses">
            <span class="pool-use">use</span>
            <button type="button" class="pool-step" id="pool-less" aria-label="One fewer">-</button>
            <span class="pool-num" id="pool-num" aria-live="polite">8</span>
            <button type="button" class="pool-step" id="pool-more" aria-label="One more">+</button>
          </div>
          <button type="button" class="chip" id="pool-add">${ICONS.add.replace('<svg', '<svg width="14" height="14"')}<span>Add more</span></button>
          <button type="button" class="chip" id="pool-unpin">Let them all roll</button>
        </div>
        <div class="pool-grid" id="pool-grid" role="group" aria-label="The pool"></div>
        <p class="pool-hint">Tap a gif to keep it on every roll. The other slots roll from the pool.</p>
      </div>`;
    document.body.appendChild(host);
    host.addEventListener('pointerdown', (e) => { if (e.target === host) closeManager(); });
    host.querySelector('#pool-close').addEventListener('click', closeManager);
    host.querySelector('#pool-less').addEventListener('click', () => setCount(effectiveCount() - 1));
    host.querySelector('#pool-more').addEventListener('click', () => setCount(effectiveCount() + 1));
    host.querySelector('#pool-add').addEventListener('click', () => ctx.pickFiles());
    host.querySelector('#pool-unpin').addEventListener('click', clearPins);
    renderManager(); render();
  }
  function closeManager() { if (!host) return; host.remove(); host = null; render(); }
  function renderManager() {
    if (!host) return;
    if (!entries.length) { closeManager(); return; }
    const n = effectiveCount(), cur = new Set(currentSet());
    host.querySelector('#pool-n').textContent = `${entries.length} loaded`;
    host.querySelector('#pool-num').textContent = String(n);
    host.querySelector('#pool-less').disabled = n <= Math.max(1, pins.length);
    host.querySelector('#pool-more').disabled = n >= Math.min(MAX_ON, entries.length);
    const unpin = host.querySelector('#pool-unpin'); unpin.hidden = !pins.length; unpin.textContent = `Let them all roll (${pins.length} kept)`;
    const grid = host.querySelector('#pool-grid'); grid.innerHTML = '';
    for (const e of entries) {
      const pinned = pins.includes(e.id);
      const cell = document.createElement('div'); cell.className = 'pool-cell' + (pinned ? ' pinned' : cur.has(e.id) ? ' rolled' : '');
      const b = document.createElement('button'); b.type = 'button'; b.className = 'pool-pick'; b.setAttribute('aria-pressed', String(pinned));
      b.title = pinned ? `${e.name}: kept on every roll. Tap to let it roll.` : `${e.name}: tap to keep it on every roll.`; b.setAttribute('aria-label', b.title);
      b.appendChild(thumbCanvas(e));
      const badge = document.createElement('i'); badge.className = 'pool-check'; badge.innerHTML = CHECK; b.appendChild(badge);
      if (e.kind !== 'gif') { const k = document.createElement('span'); k.className = 'thumb-kind'; k.textContent = e.kind === 'video' ? 'vid' : 'still'; b.appendChild(k); }
      b.addEventListener('click', () => { if (!togglePin(e.id)) shiver(b); });
      cell.appendChild(b);
      const rm = document.createElement('button'); rm.type = 'button'; rm.className = 'pool-rm'; rm.innerHTML = CLOSE; rm.title = `Remove ${e.name}`; rm.setAttribute('aria-label', rm.title);
      rm.addEventListener('click', () => remove(e.id)); cell.appendChild(rm);
      grid.appendChild(cell);
    }
  }
  ctx.on('pool', () => { render(); renderManager(); });

  return {
    add, remove, forget, clear, togglePin, clearPins, setCount, rollSet, walk, decodeInto, render, mountStrip, openManager, closeManager,
    entries: () => entries, size: () => entries.length, pins: () => pins.slice(), count: effectiveCount, isPinned: (id) => pins.includes(id),
    isBusy: () => busy, isManagerOpen: () => !!host, currentSet,
  };
}
