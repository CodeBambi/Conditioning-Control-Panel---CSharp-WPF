import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { isClip, canPlayClips, clipSource } from '../clip-source.js';

/* A video element small enough to be honest about: only what room/clip-source.js touches. The point
 * of this file is to RUN the clip source, not to read it. #1371 shipped a render loop that threw on
 * every frame and the whole 92-file suite stayed green, because the tests read modules instead of
 * executing them. */
function stubDom({ width = 338, height = 450, fails = false, never = false } = {}) {
  const timers = [];
  let now = 0;
  const drawn = [];
  let video = null;
  const canvas = {
    width: 0, height: 0,
    getContext: () => ({ drawImage: (src, x, y, w, h) => drawn.push([w, h]) }),
  };
  const make = (tag) => {
    if (tag === 'canvas') return canvas;
    const node = {
      tagName: tag, attrs: {}, listeners: {}, paused: true, plays: 0, pauses: 0, loads: 0,
      videoWidth: 0, videoHeight: 0, canPlayType: () => 'probably',
      set src(v) {
        node.attrs.src = v;
        if (never) return;
        // A real element fires these asynchronously, which is exactly the race the module has to survive.
        queueMicrotask(() => {
          if (fails) { node.listeners.error?.(); return; }
          node.videoWidth = width; node.videoHeight = height;
          node.listeners.loadedmetadata?.();
        });
      },
      get src() { return node.attrs.src; },
      addEventListener: (type, fn) => { node.listeners[type] = fn; },
      removeEventListener: (type) => { delete node.listeners[type]; },
      removeAttribute: (k) => { delete node.attrs[k]; },
      load: () => { node.loads++; },
      play: () => { node.paused = false; node.plays++; return Promise.resolve(); },
      pause: () => { node.paused = true; node.pauses++; },
    };
    video = node;
    return node;
  };
  globalThis.document = { createElement: make };
  globalThis.setTimeout = (fn, ms) => { timers.push({ at: now + (ms || 0), fn }); return timers.length; };
  globalThis.clearTimeout = () => {};
  const run = (to) => {
    now = to;
    for (;;) {
      const i = timers.findIndex((t) => t.at <= now);
      if (i < 0) return;
      const [t] = timers.splice(i, 1);
      t.fn();
    }
  };
  return { canvas, drawn, run, video: () => video };
}

test('a clip url is recognised, a picture url is left to the GIF decoder', () => {
  for (const url of ['https://ccp.assets/.temp/a.webm', 'https://ccp.assets/.temp/a.mp4',
                     'https://ccp.assets/x.M4V', 'https://ccp.assets/a.webm?v=2']) {
    assert.equal(isClip(url), true, url);
  }
  for (const url of ['https://ccp.assets/a.gif', 'https://ccp.assets/a.webp', 'https://ccp.assets/webm.png',
                     '', null, undefined]) {
    assert.equal(isClip(url), false, String(url));
  }
  /* The web playtest's transcoding hop answers animated WebP, so it is a PICTURE however the clip it
   * was built from was named. Routing it to a <video> element is what stopped the wall screens
   * animating: the element cannot decode a WebP, so every dealt picture fell back to a still. */
  const hop = '/api/clip?u=' + encodeURIComponent('https://cdn.scrolller.com/a/b.mp4');
  assert.equal(isClip(hop), false, hop);
  assert.equal(isClip('/api/clip?u=' + encodeURIComponent('https://cdn.scrolller.com/a/b.webm') + '&e=640'), false);
});

test('a page with no video element gets stills instead of an exception', async () => {
  globalThis.document = { createElement: () => ({}) };   // no canPlayType
  assert.equal(canPlayClips(), false);
  assert.equal(await clipSource('https://ccp.assets/.temp/a.webm'), null);
});

test('a clip opens muted and looping, and is capped to the same edge a GIF is', async () => {
  const dom = stubDom({ width: 1920, height: 1080 });
  const src = await clipSource('https://ccp.assets/.temp/a.webm');
  assert.ok(src, 'the clip opened');
  const v = dom.video();
  // Not preferences: an unmuted video will not autoplay, and the room's sound is the kit's business.
  assert.equal(v.muted, true);
  assert.equal(v.loop, true);
  assert.equal(v.crossOrigin, 'anonymous', 'ccp.assets is CORS-mapped so the canvas stays readable');
  assert.equal(dom.canvas.width, 384, 'MAX_EDGE on the long side');
  assert.equal(dom.canvas.height, 216, 'aspect kept');
  assert.equal(src.animated, true);
  src.dispose();
});

test('the room clock owns the frame rate, and every frame lands in the canvas', async () => {
  const dom = stubDom();
  let frames = 0;
  const src = await clipSource('https://ccp.assets/.temp/a.webm', { onFrame: () => frames++ });
  assert.ok(src);
  const first = dom.drawn.length;
  assert.equal(first, 1, 'the first frame is painted on open so a wall is never blank');
  assert.equal(frames, 0, 'and it does not count as a change');

  assert.equal(src.tick(0, false), true);
  assert.equal(src.tick(1, false), false, 'a second tick in the same millisecond costs nothing');
  assert.equal(src.tick(40, false), false, 'still inside the 12 fps gap');
  assert.equal(src.tick(90, false), true);
  assert.equal(dom.drawn.length, first + 2);
  assert.equal(frames, 2);
  src.dispose();
});

test('still pauses the decoder, it does not merely stop drawing', async () => {
  const dom = stubDom();
  const src = await clipSource('https://ccp.assets/.temp/a.webm');
  const v = dom.video();
  assert.equal(v.paused, false);

  const before = dom.drawn.length;
  assert.equal(src.tick(1000, true), false);
  assert.equal(v.paused, true, 'an off-screen video that keeps decoding is the work the budget exists to stop');
  assert.equal(dom.drawn.length, before);

  src.tick(2000, false);
  assert.equal(v.paused, false, 'and it comes back');
  src.dispose();
});

test('dispose drops the source so the decoder is actually freed', async () => {
  const dom = stubDom();
  const src = await clipSource('https://ccp.assets/.temp/a.webm');
  src.dispose();
  const v = dom.video();
  assert.equal(v.pauses > 0, true);
  assert.equal('src' in v.attrs, false, 'pausing alone leaves the decoder attached');
  assert.equal(v.loads, 1, 'removeAttribute then load() is what frees it');
  assert.equal(src.tick(9999, false), false, 'and a tick after dispose is inert');
});

test('a clip that cannot be decoded is null, so the caller falls back to a still', async () => {
  stubDom({ fails: true });
  assert.equal(await clipSource('https://ccp.assets/.temp/a.webm'), null);
});

test('a clip that never answers gives up on the same budget a GIF gets', async () => {
  const dom = stubDom({ never: true });
  const pending = clipSource('https://ccp.assets/.temp/a.webm');
  dom.run(10_000);
  assert.equal(await pending, null, 'MEDIA_LIMITS.loadMs, not forever');
});

test('an aborted load does not leave a video running', async () => {
  const dom = stubDom({ never: true });
  const controller = new AbortController();
  const pending = clipSource('https://ccp.assets/.temp/a.webm', { signal: controller.signal });
  controller.abort();
  assert.equal(await pending, null);
  assert.equal('src' in dom.video().attrs, false);
});

test('gif.js routes clips to this module AND still routes pictures to the decoder', () => {
  const src = readFileSync(new URL('../gif.js', import.meta.url), 'utf8');
  assert.match(src, /import\s*\{[^}]*\bclipSource\b[^}]*\bisClip\b[^}]*\}\s*from\s*'\.\/clip-source\.js'/,
    'gif.js imports both');
  assert.match(src, /isClip\(url\)\s*\?\s*await clipSource\(/, 'and actually calls it');
  assert.match(src, /:\s*await decodedSource\(/, 'a picture still goes to the GIF decoder');
});

test('this module stays three-free, like gif-decode.js', () => {
  const src = readFileSync(new URL('../clip-source.js', import.meta.url), 'utf8');
  assert.doesNotMatch(src, /from\s*'three'/, "CONTRACT 10.13.D: no three.js import belongs in here");
});
