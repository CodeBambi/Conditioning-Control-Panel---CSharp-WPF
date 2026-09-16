/* ============================================================================
 * jackpot.js - the Jackpot Remix builder page.
 *
 * The desktop app (Services/Remix/JackpotRemixBuilder.cs) hosts this page in a
 * hidden WebView2 and drives it over the web message bridge. One build:
 *
 *   host -> page  { type:'build', id, media:[{ url, name }], seed, seconds, scale, wantLayout? }
 *   page -> host  { type:'built', id, ok:true, base64, bytes, frames, w, h, code, layout, seedUsed,
 *                   timings:{ fetch, decode, roll, encode, total } }
 *              or { type:'built', id, ok:false, error }
 *   host -> page  { type:'cancel', id }
 *
 * `media` is already cycled to eight entries by the host (three gifs arrive as
 * a b c a b c a b); the page fetches each distinct url once and decodes it
 * once per entry, because a tile needs its own media id. The roll is the
 * room's own autoCompose through the project API, so the same seed and the
 * same gifs give the same composition here and on cclabs.app.
 *
 * The frames are drawn with the engine's own Renderer over the project's
 * state, which is exactly what project.drawFrame does, with one difference:
 * `showStamp: false`. The room stamps every export with "cclabs.app/remix";
 * a flash inside the app carries no stamp.
 *
 * The output is a gif: that is the one animated format the engine encodes,
 * and FlashService already plays it. `scale` above 1 draws the export bigger
 * than the engine's 480x270; the strips are decoded at the scaled size too,
 * so it is real detail, not an upscale.
 *
 * `wantLayout` (measurement only) walks seeds upward from `seed` until the
 * roll lands on that layout id, so the spike can time a dear layout on purpose.
 *
 * Query params for a browser desk run without the host:
 *   ?media=a.gif,b.gif&seed=7   (urls relative to this page)
 * ==========================================================================*/

import { createProject, pickOrientation, probeMedia, sizeFor, Renderer, stopDecoder, stopEncoder } from './engine/index.js';
import { encodeGif } from './engine/export.js';

const MAX_TILES = 8;
const DEFAULT_SECONDS = 3;
const SEED_WALK = 400;

const host = (typeof chrome !== 'undefined' && chrome.webview) ? chrome.webview : null;
const post = (msg) => { if (host) host.postMessage(msg); else console.log('[jackpot]', msg); };
const log = (msg, level) => post({ type: 'log', level: level || 'debug', msg: String(msg) });

const builds = new Map(); // id -> AbortController
const now = () => performance.now();

async function fetchFile(url, name) {
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) throw new Error('fetch ' + res.status + ' for ' + name);
  const blob = await res.blob();
  return new File([blob], name || 'clip.gif', { type: blob.type || 'image/gif' });
}

async function build(req) {
  const id = req.id;
  const ctl = new AbortController();
  builds.set(id, ctl);
  const t = { fetch: 0, decode: 0, roll: 0, encode: 0, total: 0 };
  const t0 = now();
  let project = null;
  try {
    const list = (req.media || []).slice(0, MAX_TILES);
    if (!list.length) throw new Error('no media');
    const scale = Math.max(1, Math.min(4, Number(req.scale) || 1));
    const seconds = Number(req.seconds) || DEFAULT_SECONDS;

    // fetch each distinct url once
    const files = new Map();
    const tf = now();
    for (const m of list) {
      if (files.has(m.url)) continue;
      files.set(m.url, await fetchFile(m.url, m.name));
      if (ctl.signal.aborted) throw abort();
    }
    t.fetch = now() - tf;

    // the canvas shape follows the first gif, as the room's Auto page does
    const first = files.get(list[0].url);
    const probe = await probeMedia(first);
    const orientation = pickOrientation(probe.srcW, probe.srcH);
    const base = sizeFor(orientation);
    const size = { w: Math.round(base.w * scale), h: Math.round(base.h * scale) };

    project = createProject({ orientation, seed: req.seed >>> 0, seconds });
    const td = now();
    const ids = [];
    for (const m of list) {
      const media = await project.addMedia(files.get(m.url), { size });
      ids.push(media.id);
      if (ctl.signal.aborted) throw abort();
    }
    t.decode = now() - td;

    const tr = now();
    project.setMediaSet(ids);
    let seedUsed = req.seed >>> 0;
    let code = project.roll(seedUsed);
    if (req.wantLayout) {
      for (let k = 1; k < SEED_WALK && project.layout.mode !== req.wantLayout; k++) {
        seedUsed = (req.seed + k) >>> 0;
        code = project.roll(seedUsed);
      }
    }
    t.roll = now() - tr;

    const renderer = new Renderer();
    const p = project;
    const state = () => ({
      size, orientation, frames: p.frames, fps: p.fps, seed: p.seed, code: p.code,
      tiles: p.tiles, media: (mid) => p.mediaById(mid), layout: p.layout, loop: p.loop, blocks: p.blocks,
      stampCorner: p.stampCorner, captionText: p.captionText,
      mediaColour: p.media.length ? p.media[0].colour : null,
      showStamp: false,
    });
    const te = now();
    const blob = await encodeGif({
      drawFrame: (ctx, i) => renderer.render(ctx, i, state()),
      frames: project.frames,
      masterFps: project.fps,
      fps: project.fps,
      scale: 1,
      size,
      signal: ctl.signal,
    });
    t.encode = now() - te;
    renderer.dispose();
    const base64 = await toBase64(blob);
    t.total = now() - t0;

    post({
      type: 'built', id, ok: true, base64, bytes: blob.size, frames: project.frames,
      w: size.w, h: size.h, code, seedUsed, layout: project.layout.mode, orientation, timings: t,
    });
  } catch (err) {
    t.total = now() - t0;
    post({ type: 'built', id, ok: false, error: String((err && err.message) || err), timings: t });
  } finally {
    builds.delete(id);
    try { if (project) project.dispose(); } catch { /* nothing to keep */ }
    // The decode and encode workers keep their heaps between jobs (measured: the browser tree
    // sat at over a gigabyte after one build). Nothing else runs on this page, so end them;
    // the next build starts fresh ones.
    try { stopDecoder(); stopEncoder(); } catch { /* already gone */ }
  }
}

function abort() {
  const e = new Error('cancelled');
  e.name = 'AbortError';
  return e;
}

function toBase64(blob) {
  return new Promise((resolve, reject) => {
    const fr = new FileReader();
    fr.onload = () => resolve(String(fr.result).split(',')[1] || '');
    fr.onerror = () => reject(new Error('could not read the gif back'));
    fr.readAsDataURL(blob);
  });
}

function onMessage(msg) {
  if (!msg || typeof msg !== 'object') return;
  if (msg.type === 'build') { build(msg); return; }
  if (msg.type === 'cancel') { const c = builds.get(msg.id); if (c) c.abort(); }
}

if (host) {
  host.addEventListener('message', (e) => onMessage(e.data));
} else {
  // desk run in a plain browser
  const q = new URLSearchParams(location.search);
  const media = (q.get('media') || '').split(',').filter(Boolean).map((u) => ({ url: u, name: u.split('/').pop() }));
  if (media.length) {
    const cycled = [];
    for (let i = 0; i < MAX_TILES; i++) cycled.push(media[i % media.length]);
    build({ id: 'desk', media: cycled, seed: Number(q.get('seed')) || 1, scale: Number(q.get('scale')) || 1, wantLayout: q.get('layout') || null });
  }
}

window.addEventListener('error', (e) => log('page error: ' + (e.message || e), 'warn'));
post({ type: 'ready' });
log('jackpot page up');
