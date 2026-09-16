// Export sheet: looping preview, size line, Save gif (primary), Save video, Discord-safe, share hint.
// The close button is never disabled: while an encode runs it reads Cancel and aborts it.
// iOS: Safari ignores <a download>, so the encode runs first with progress in the sheet, then an
// "Open to save" button opens the result in its own tab (a user gesture, so the tab is allowed).
import { canRecordVideo } from './engine-bridge.js';
import * as sound from './sound.js';
import { doorStrip } from './doors.js';

const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
export const isIOS = () => /iP(hone|ad|od)/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
const MB = 1024 * 1024;
const REVOKE_MS = 5 * 60 * 1000;
// the sparks are the first save of the page load only: a party that repeats is not a party
let sparked = false;
export const fmtMB = bytes => bytes == null ? '--' : (bytes / MB).toFixed(1);

export function initExportSheet(ctx) {
  const p = ctx.project;
  const host = document.getElementById('export-sheet');
  let off = null, busy = false, discordSafe = false, els = {}, abort = null, ready = null;
  let shown = null, tickRaf = 0, savedTimer = 0, doorsUp = false;

  function open() {
    if (!host.hidden) return;
    discordSafe = !!p.discordSafe; // the toggle lives on the project now
    const { w, h } = p.size;
    host.innerHTML = `
      <div class="sheet" role="dialog" aria-modal="true" aria-label="Export">
        <div class="sheet-head"><div class="sheet-title">Export</div><button type="button" class="sheet-close" aria-label="Close">${closeIcon}</button></div>
        <div class="preview-box"><canvas id="export-preview" width="${w}" height="${h}" aria-label="Looping preview"></canvas></div>
        <div class="size-line" id="size-line"><span><b class="mb" id="size-val">${fmtMB(ctx.lastSize)}</b> MB · <span id="size-meta">${meta()}</span> · <b>${p.code}</b></span></div>
        <div class="progress" id="export-progress" hidden><i></i></div>
        <div class="export-actions">
          <button type="button" class="btn-big primary" id="save-gif">Save gif</button>
          <button type="button" class="btn-big" id="save-video" ${canRecordVideo() ? '' : 'hidden'}>Save video</button>
        </div>
        <div id="ready-slot"></div>
        <label class="tog"><span>Discord-safe<span class="tog-sub">keeps the gif under 8 MB, nothing else changes</span></span><button type="button" class="switch" id="discord-safe" role="switch" aria-checked="${discordSafe}" aria-label="Discord-safe"></button></label>
        <label class="tog" title="Sound"><span>Sound<span class="tog-sub">a tick per roll, a thud on save. off unless you want it</span></span><button type="button" class="switch" id="sound-on" role="switch" aria-checked="${sound.enabled()}" aria-label="Sound"></button></label>
        <div class="share-hint">nothing is uploaded. paste it anywhere, the stamp travels with it</div>
      </div>`;
    host.hidden = false; doorsUp = false; document.getElementById('app').classList.add('sheet-open');
    els = { canvas: host.querySelector('#export-preview'), size: host.querySelector('#size-line'), sizeVal: host.querySelector('#size-val'), meta: host.querySelector('#size-meta'), prog: host.querySelector('#export-progress'), gif: host.querySelector('#save-gif'), vid: host.querySelector('#save-video'), sw: host.querySelector('#discord-safe'), snd: host.querySelector('#sound-on'), ready: host.querySelector('#ready-slot'), close: host.querySelector('.sheet-close') };
    const c = els.canvas.getContext('2d'); const stage = document.getElementById('stage');
    const draw = () => { try { c.drawImage(stage, 0, 0); } catch {} };
    off = p.on('frame', draw); draw(); if (!p.playing) p.play();
    els.close.addEventListener('click', close);
    host.addEventListener('pointerdown', e => { if (e.target === host && !busy) close(); });
    els.sw.addEventListener('click', () => { discordSafe = !discordSafe; p.setDiscordSafe(discordSafe); els.sw.setAttribute('aria-checked', String(discordSafe)); sizeLine(); });
    els.snd.addEventListener('click', () => { sound.setEnabled(!sound.enabled()); els.snd.setAttribute('aria-checked', String(sound.enabled())); });
    els.gif.addEventListener('click', saveGif);
    els.vid.addEventListener('click', saveVideo);
    els.gif.focus();
    sizeLine();
  }
  // busy: the close button is Cancel, and closing means stopping the encode
  function close() {
    if (host.hidden) return;
    if (busy) { abort?.abort(); return; }
    cancelAnimationFrame(tickRaf); clearTimeout(savedTimer); shown = null;
    off?.(); off = null; host.hidden = true; host.innerHTML = ''; document.getElementById('app').classList.remove('sheet-open'); ctx.emit('state');
  }

  // Law XII: a rung is a thing that moved, so the megabytes travel to the new value and land in gold.
  function tickNumber(el, from, to, ms = 420) {
    const t0 = performance.now();
    const step = now => {
      const u = Math.min(1, (now - t0) / ms), e = 1 - Math.pow(1 - u, 3);
      el.textContent = fmtMB(from + (to - from) * e);
      if (u < 1) { tickRaf = requestAnimationFrame(step); return; }
      el.textContent = fmtMB(to);
      el.classList.add('land'); el.addEventListener('animationend', () => el.classList.remove('land'), { once: true });
    };
    tickRaf = requestAnimationFrame(step);
  }
  function setMB(bytes, roll) {
    const el = els.sizeVal; if (!el) return;
    cancelAnimationFrame(tickRaf);
    const from = shown; shown = bytes;
    if (!roll || from == null || bytes == null || reduced() || Math.abs(from - bytes) < MB / 20) { el.textContent = fmtMB(bytes); return; }
    tickNumber(el, from, bytes);
  }

  const RUNGS = ['', '12 fps', '12 fps · two thirds size'];
  function meta() {
    const { w, h } = p.size; const r = p.shrink | 0;
    if (!r) return `${p.frames} frames · ${w}x${h}`;
    const fps = 12, frames = Math.round(p.frames / p.fps * fps), s = r === 2 ? 2 / 3 : 1;
    return `${frames} frames · ${Math.round(w * s)}x${Math.round(h * s)} · ${RUNGS[r]}`;
  }
  function sizeLine(roll) {
    const bytes = ctx.lastSize; const cap = discordSafe ? 8 * MB : 10 * MB; const hot = bytes != null && bytes > cap;
    els.size.classList.toggle('hot', hot); setMB(bytes, roll); els.meta.textContent = meta();
    els.size.querySelector('.shrink')?.remove();
    if (hot && p.shrink < 2) {
      const b = document.createElement('button'); b.type = 'button'; b.className = 'shrink'; b.textContent = discordSafe ? 'over 8 MB, will shrink on save' : 'shrink';
      b.title = 'Drops to 12 fps, then to two thirds size. One tap.';
      b.addEventListener('click', async () => { p.setShrink(p.shrink + 1); ctx.commit('shrink'); ctx.toast(p.shrink === 1 ? 'Down to 12 fps' : 'Down to two thirds size'); await ctx.refreshSize(); sizeLine(true); });
      els.size.appendChild(b);
    } else if (hot) {
      const n = document.createElement('span'); n.className = 'shrink still'; n.textContent = 'still over at the smallest rung. try a shorter length'; els.size.appendChild(n);
    }
  }
  function setBusy(on, label) {
    busy = on; els.gif.disabled = on; els.vid.disabled = on; els.prog.hidden = !on;
    els.close.classList.toggle('cancel', on); els.close.innerHTML = on ? 'Cancel' : closeIcon; els.close.setAttribute('aria-label', on ? 'Cancel the export' : 'Close');
    if (on) { els.ready.innerHTML = ''; const bar = els.prog.querySelector('i'); bar.classList.add('jump'); bar.style.width = '0'; void bar.offsetWidth; bar.classList.remove('jump'); }
    if (label) els.gif.textContent = label;
  }
  const progress = k => { els.prog.querySelector('i').style.width = Math.round(k * 100) + '%'; els.gif.textContent = `encoding ${Math.round(k * 100)}%`; };

  // THE THUD: the gif is ready, so the preview lands like a thing. 340 ms, nothing blocked, nothing to dismiss.
  function thudEl(el) {
    if (!el) return;
    el.classList.add('thud'); el.addEventListener('animationend', () => el.classList.remove('thud'), { once: true });
  }
  function sparkBurst(box) {
    if (sparked || reduced() || !box) return;
    sparked = true;
    for (let i = 0; i < 7; i++) {
      const a = (-90 + (i - 3) * 25) * Math.PI / 180, r = 45 + (i % 3) * 22;
      const s = document.createElement('i');
      s.className = i % 2 ? 'spark gold' : 'spark';
      s.style.setProperty('--dx', Math.round(Math.cos(a) * r) + 'px');
      s.style.setProperty('--dy', Math.round(Math.sin(a) * r) + 'px');
      s.addEventListener('animationend', () => s.remove(), { once: true });
      box.appendChild(s);
    }
  }
  // iOS gets the thud on the Open to save button instead: that is the thing that just arrived.
  function celebrate() {
    const box = host.querySelector('.preview-box');
    sound.thud(); // Law X: the sound and the picture land on the same beat
    if (!isIOS()) thudEl(box);
    setTimeout(() => sparkBurst(box), 40);
  }
  function savedLabel() {
    clearTimeout(savedTimer);
    els.gif.textContent = isIOS() ? 'Ready' : 'Saved'; els.gif.classList.add('saved');
    savedTimer = setTimeout(() => {
      if (!els.gif || !els.gif.isConnected) return;
      els.gif.textContent = 'Save gif'; els.gif.classList.remove('saved');
    }, 1600);
  }

  // THE DOORS: once a save has landed, the room says where the gif leads. Under the save buttons,
  // never over them, and "not now" takes it off for the rest of the session.
  function showDoors() {
    if (doorsUp || host.hidden || !els.ready) return;
    const strip = doorStrip(ctx);
    if (!strip) return;
    doorsUp = true;
    els.ready.insertAdjacentElement('afterend', strip);
  }

  async function saveGif() {
    if (busy) return;
    const name = `remix-${p.code}.gif`;
    setBusy(true, 'encoding 0%'); abort = new AbortController();
    let done = false;
    try {
      let shrunk = null;
      const blob = await p.exportGif({ onProgress: progress, discordSafe, shrink: true, signal: abort.signal, onRung: (r, bytes) => { shrunk = r; setMB(bytes, true); els.gif.textContent = `${fmtMB(bytes)} MB, shrinking`; } });
      ctx.lastSize = blob.size; sizeLine(true); done = true;
      celebrate();
      deliver(blob, name, 'gif');
      if (discordSafe && blob.size > 8 * MB) ctx.toast(`${isIOS() ? 'Ready' : 'Saved ' + name}. Still ${fmtMB(blob.size)} MB at the smallest rung. A shorter length would fit.`, { gold: true, ms: 5000 });
      else if (isIOS()) ctx.toast('Ready. Hold the image to save it.');
      else ctx.toast(shrunk ? `Saved ${name}, shrunk to fit under 8 MB` : `Saved ${name}`);
    } catch (e) { ctx.toast(e?.name === 'AbortError' ? 'Export cancelled' : (e?.message || 'Export failed. Try again.'), { gold: e?.name !== 'AbortError' }); }
    finally { abort = null; setBusy(false, 'Save gif'); if (done) { savedLabel(); showDoors(); } }
  }
  async function saveVideo() {
    if (busy) return;
    setBusy(true); els.vid.textContent = 'recording'; abort = new AbortController();
    try {
      const blob = await p.exportVideo({ signal: abort.signal, onProgress: k => { els.prog.querySelector('i').style.width = Math.round(k * 100) + '%'; els.vid.textContent = `recording ${Math.round(k * 100)}%`; } });
      const ext = /mp4/.test(blob.type) ? 'mp4' : 'webm'; const name = `remix-${p.code}.${ext}`;
      deliver(blob, name, 'video'); ctx.toast(isIOS() ? 'Ready. Hold the video to save it.' : `Saved ${name}`); showDoors();
    } catch (e) { ctx.toast(e?.name === 'AbortError' ? 'Recording cancelled' : (e?.message || 'Video export failed.'), { gold: e?.name !== 'AbortError' }); }
    finally { abort = null; setBusy(false); els.vid.textContent = 'Save video'; }
  }
  function deliver(blob, name, kind) {
    const url = URL.createObjectURL(blob);
    if (isIOS()) {
      // the last result is let go when a new one lands, or after five minutes
      ready?.revoke(); ready = null;
      const page = URL.createObjectURL(new Blob([holdPage(name, url, kind)], { type: 'text/html' }));
      const timer = setTimeout(() => ready?.revoke(), REVOKE_MS);
      ready = { revoke: () => { clearTimeout(timer); URL.revokeObjectURL(url); URL.revokeObjectURL(page); ready = null; } };
      els.ready.innerHTML = `<button type="button" class="btn-big primary" id="open-save">Open to save</button><div class="ios-note">Hold the ${kind === 'video' ? 'video' : 'image'} to save it.</div>`;
      if (kind === 'gif') thudEl(els.ready.querySelector('#open-save'));
      els.ready.querySelector('#open-save').addEventListener('click', () => { if (!window.open(page, '_blank')) ctx.toast('Allow pop-ups for this page, then tap again.', { gold: true }); });
      return;
    }
    const a = document.createElement('a'); a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  }
  const holdPage = (name, url, kind) => `<!doctype html><title>${name}</title><meta name="viewport" content="width=device-width,initial-scale=1"><body style="margin:0;background:#14142B;color:#F2EBDD;font-family:-apple-system,system-ui,sans-serif;display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:100vh;gap:14px;padding:20px;box-sizing:border-box"><div style="font:13px/1.4 ui-monospace,Menlo,monospace;color:#B9B3CE">${name}</div>${kind === 'video'
    ? `<video src="${url}" controls playsinline autoplay loop muted style="display:block;width:100%;max-width:640px;border-radius:10px;background:#000"></video>`
    : `<img src="${url}" alt="${name}" style="display:block;width:100%;max-width:640px;border-radius:10px">`}<div style="font-size:15px;color:#B9B3CE">hold the ${kind === 'video' ? 'video' : 'image'} to save it</div></body>`;
  const closeIcon = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12"/><path d="M18 6 6 18"/></svg>';

  return { open, close, isOpen: () => !host.hidden, isBusy: () => busy, updateSize: () => { if (!host.hidden) { sizeLine(); } } };
}
