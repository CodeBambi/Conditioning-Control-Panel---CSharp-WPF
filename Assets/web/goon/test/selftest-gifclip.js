// Self-contained pass over the gif-clip lane (exec/clip.js + media.js drawClip).
//
//   node Resources/web/goon/test/selftest-gifclip.js
//
// Owner, 2026-09-23: on a Scrolller-only deck "gifs and glitch bubbles fullscreen
// gifs do not animate". The online stills are posters by design; the motion lives
// in the GifClip video lane. Pins: drawClip deals ONLY gif clips (online videos and
// the host's online-stamped ones, never a player's own video), prepareClip builds a
// muted looping inline autoplay <video> and reports a frame or a dud exactly once,
// the live cap holds at MAX_LIVE_CLIPS, stopClip frees the slot, and a torn-down
// clip is stopped on the next count.

import { createGoonMediaPool } from '../exec/media.js';
import {
  MAX_LIVE_CLIPS, drawClipHandle, prepareClip, stopClip, liveClipCount, clipRoom, _resetClipsForTests,
} from '../exec/clip.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}

/* ---- a minimal DOM: just what clip.js and the bubbles wash touch */
class FakeNode {
  constructor(tag) {
    this.tagName = String(tag).toUpperCase();
    this.children = [];
    this.parentNode = null;
    this.attrs = {};
    this.listeners = {};
    this.style = { props: {}, setProperty(k, v) { this.props[k] = v; }, getPropertyValue(k) { return this.props[k] || ''; }, removeProperty(k) { delete this.props[k]; } };
    this.className = '';
    this.classList = {
      set: new Set(),
      add: (c) => this.classList.set.add(c),
      remove: (c) => this.classList.set.delete(c),
      contains: (c) => this.classList.set.has(c),
    };
    this.paused = true;
    this.loads = 0;
  }
  get isConnected() {
    let p = this;
    while (p) { if (p === fakeDocument.body) return true; p = p.parentNode; }
    return false;
  }
  get firstChild() { return this.children[0] || null; }
  setAttribute(k, v) { this.attrs[k] = String(v); }
  removeAttribute(k) { delete this.attrs[k]; if (k === 'src') this._src = ''; }
  set src(v) { this._src = v; this.attrs.src = v; }
  get src() { return this._src || ''; }
  addEventListener(t, f) { (this.listeners[t] = this.listeners[t] || []).push(f); }
  removeEventListener() {}
  fire(t) { const ls = this.listeners[t] || []; this.listeners[t] = []; for (const f of ls) f({ type: t }); }
  appendChild(c) { if (c.parentNode) c.remove(); c.parentNode = this; this.children.push(c); return c; }
  insertBefore(c, ref) {
    if (c.parentNode) c.remove();
    c.parentNode = this;
    const i = ref ? this.children.indexOf(ref) : -1;
    if (i < 0) this.children.push(c); else this.children.splice(i, 0, c);
    return c;
  }
  remove() { if (this.parentNode) { const a = this.parentNode.children; const i = a.indexOf(this); if (i >= 0) a.splice(i, 1); this.parentNode = null; } }
  play() { this.paused = false; return Promise.resolve(); }
  pause() { this.paused = true; }
  load() { this.loads++; }
}
const fakeDocument = {
  body: new FakeNode('body'),
  createElement: (t) => new FakeNode(t),
  getElementById: () => null,
};
globalThis.document = fakeDocument;

/* ---- media.js: drawClip deals only gif clips */
{
  const pool = createGoonMediaPool();
  ok(pool.drawClip() === null, 'empty pool: no clip');
  pool.setManifest({
    images: [{ name: 'online30:still', url: 'https://ccp.assets/.temp/s.webp' }],
    videos: [{ name: 'online30:hostclip', url: 'https://ccp.assets/.temp/h.mp4' }, { name: 'mine.mp4', url: 'https://ccp.assets/mine.mp4' }],
  });
  pool.setOnlineLibrary({
    images: [{ name: 'p', url: 'https://ccp.assets/.temp/p.webp' }],
    videos: [{ name: 'c1', url: 'https://ccp.assets/.temp/c1.webm' }, { name: 'c2', url: 'https://ccp.assets/.temp/c2.mp4' }],
  });
  ok(pool.clipCount() === 3, 'three gif clips: two online + the host online-stamped one', String(pool.clipCount()));
  const seen = new Set();
  let prev = '';
  let repeats = 0;
  for (let i = 0; i < 60; i++) {
    const e = pool.drawClip();
    seen.add(e.url);
    if (e.url === prev) repeats++;
    prev = e.url;
    ok(e.kind === 'video', 'a clip is a video entry');
  }
  ok(!seen.has('https://ccp.assets/mine.mp4'), 'a player\'s own video never deals as a gif clip');
  ok(seen.size === 3, 'every clip gets dealt', [...seen].join(','));
  ok(repeats === 0, 'no clip twice in a row', String(repeats));
  const h = pool.acquire(pool.drawClip());
  ok(h && typeof h.url === 'string' && typeof h.release === 'function', 'a clip goes through acquire()');
  pool.setOnlineLibrary({});
  ok(pool.clipCount() === 1, 'online clips leave with the online set');
}

/* ---- clip.js: prepare, cap, stop */
{
  _resetClipsForTests();
  const pool = createGoonMediaPool();
  pool.setOnlineLibrary({ videos: [1, 2, 3, 4, 5, 6].map((i) => ({ name: `c${i}`, url: `https://ccp.assets/.temp/c${i}.webm` })) });

  const got = [];
  const vids = [];
  for (let i = 0; i < MAX_LIVE_CLIPS; i++) {
    const h = drawClipHandle(pool);
    ok(!!h, `clip ${i} drawn under the cap`);
    const v = prepareClip(h, { className: 'gg-clip', timeoutMs: 5000 }, (x) => got.push(x));
    vids.push(v);
  }
  ok(liveClipCount() === MAX_LIVE_CLIPS, 'slots are held while loading', String(liveClipCount()));
  ok(!clipRoom() && drawClipHandle(pool) === null, 'the cap refuses a fifth clip');
  const v0 = vids[0];
  ok(v0.muted && v0.loop && v0.playsInline && v0.autoplay, 'muted, looping, inline, autoplay');
  ok('muted' in v0.attrs && 'loop' in v0.attrs && 'playsinline' in v0.attrs && 'autoplay' in v0.attrs, 'the same four as attributes');
  ok(v0.src.startsWith('https://ccp.assets/.temp/'), 'plays the acquired url');
  v0.fire('loadeddata');
  ok(got.length === 1 && got[0] === v0 && !v0.paused, 'a frame reports the video once and plays it');
  fakeDocument.body.appendChild(v0);          // the caller mounts it
  v0.fire('loadeddata');
  ok(got.length === 1, 'reports only once');
  vids[1].fire('error');
  ok(got.length === 2 && got[1] === null, 'a dud reports null');
  ok(liveClipCount() === MAX_LIVE_CLIPS - 1, 'a dud frees its slot', String(liveClipCount()));
  stopClip(v0);
  ok(v0.paused && !v0.src && liveClipCount() === MAX_LIVE_CLIPS - 2, 'stopClip pauses, drops the source, frees the slot');
  stopClip(v0);
  ok(liveClipCount() === MAX_LIVE_CLIPS - 2, 'stopClip twice is harmless');
  // cancelled while loading: nobody is told
  stopClip(vids[2]);
  vids[2].fire('loadeddata');
  ok(got.length === 2, 'a clip stopped while loading reports nothing');
  stopClip(vids[3]);
  ok(liveClipCount() === 0, 'all slots back');

  // timeout
  const late = [];
  const h = drawClipHandle(pool);
  prepareClip(h, { timeoutMs: 20 }, (x) => late.push(x));
  await new Promise((r) => setTimeout(r, 60));
  ok(late.length === 1 && late[0] === null && liveClipCount() === 0, 'no frame in time = null, slot freed');

  ok(drawClipHandle(null) === null && drawClipHandle({ drawKind() {} }) === null, 'a pool without drawClip = no clip');
  let called = null;
  ok(prepareClip(null, {}, (x) => { called = x; }) === null && called === null, 'no handle = null, reported');
  _resetClipsForTests();
}

/* ---- a clip whose node was torn down without stopClip frees its slot */
{
  _resetClipsForTests();
  const pool = createGoonMediaPool();
  pool.setOnlineLibrary({ videos: [{ name: 'c', url: 'https://ccp.assets/.temp/c.webm' }] });
  let v = null;
  prepareClip(drawClipHandle(pool), {}, (x) => { v = x; });
  const pending = liveClipCount();
  ok(pending === 1, 'a loading clip (not in the DOM yet) keeps its slot', String(pending));
  // fire the frame through the node the module built
  const built = fakeDocument.body.children.length === 0;
  ok(built, 'nothing is mounted before the caller mounts it');
  _resetClipsForTests();
  const v2 = prepareClip(drawClipHandle(pool), {}, (x) => { v = x; });
  v2.fire('loadeddata');
  fakeDocument.body.appendChild(v2);
  ok(liveClipCount() === 1, 'a mounted clip holds its slot');
  v2.remove();                                   // a layer cleared under it
  ok(liveClipCount() === 0 && v2.paused && !v2.src, 'a torn-down clip is stopped and freed on the next count');
  _resetClipsForTests();
}

if (failures) {
  console.error(`selftest-gifclip: ${failures} of ${n} checks FAILED`);
  process.exit(1);
}
console.log(`selftest-gifclip: ${n} checks passed`);
process.exit(0);
