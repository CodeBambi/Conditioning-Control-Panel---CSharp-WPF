/* The clip route in stations/slot/media.js (2026-09-17, "discard stills"). Every remote picture the host deals
 * is a webm/mp4 now, so the reel has to PLAY one, hold it on a frame under stillness, and give up on one it
 * cannot play without spending the <img> attempt that is only ever for a picture. The extension test is the
 * real one from room/clip-source.js (the router IS the contract with the host); clipSource and decodedSource
 * are seams, and media-limits.js stays real. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { isClip } from '../../../room/clip-source.js';

globalThis.testIsClip = isClip;
const source = (await readFile(new URL('../media.js', import.meta.url), 'utf8'))
  .replace("import { kindOf } from './symbols.js';", "const kindOf = id => ({kind:id.startsWith('gif')?'gif':'sub',n:Number(id.at(-1))});")
  .replace("import { decodedSource } from '../../room/gif-decode.js';", 'const decodedSource = (...args) => globalThis.testDecode(...args);')
  .replace("import { clipSource, isClip } from '../../room/clip-source.js';", 'const isClip = globalThis.testIsClip; const clipSource = (...args) => globalThis.testClip(...args);')
  .replace("'../../room/media-limits.js'", JSON.stringify(new URL('../../../room/media-limits.js', import.meta.url).href));
const { createMedia } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const flush = () => new Promise(resolve => setImmediate(resolve));

/** A clip source small enough to be honest about: the shape clip-source.js returns, recording its ticks. */
function fakeClip() {
  return { canvas: { width: 64, height: 48 }, animated: true, clip: true, frames: 0, index: 0, ticks: [], disposed: false,
    tick(now, still) { this.ticks.push(!!still); return !still; }, dispose() { this.disposed = true; } };
}

function setup(t, { clip = async () => fakeClip(), decode = async () => { throw Object.assign(new Error('gone'), { name: 'AbortError' }); }, tainted = false } = {}) {
  const previous = Object.fromEntries(['Image', 'location', 'document', 'testDecode', 'testClip'].map(k => [k, Object.getOwnPropertyDescriptor(globalThis, k)]));
  const images = [], logged = [], clipCalls = [], decodeCalls = [];
  globalThis.location = { href: 'https://ccp.game/room/' };
  globalThis.testClip = (url, opts) => { clipCalls.push({ url, ...opts }); return clip(url, opts); };
  globalThis.testDecode = (url, opts) => { decodeCalls.push({ url, ...opts }); return decode(url, opts); };
  // readableCanvas draws the clip's canvas into a 1x1 probe; a tainted one throws on getImageData.
  globalThis.document = { createElement: () => ({ getContext: () => ({ drawImage() {}, getImageData() { if (tainted) throw new Error('tainted'); return {}; } }) }) };
  globalThis.Image = class {
    constructor() { this.dataset = {}; this.complete = false; this.naturalWidth = 0; images.push(this); }
    removeAttribute(name) { if (name === 'src') this.src = ''; }
    remove() { this.removed = true; }
  };
  t.after(() => { for (const [k, d] of Object.entries(previous)) { if (d) Object.defineProperty(globalThis, k, d); else delete globalThis[k]; } });
  const media = createMedia({ append() {} }, (k, f) => f, (level, msg) => logged.push(level + ': ' + msg));
  t.after(() => media.dispose());
  return { media, images, logged, clipCalls, decodeCalls };
}

const CLIP = 'https://ccp.assets/.temp/ccp_temp_remote_a.webm', STILL = 'https://ccp.assets/.temp/ccp_temp_remote_b.webp';

test('the router is the real one: what the host materializes as a clip is what the reel plays', () => {
  for (const url of ['https://ccp.assets/.temp/x.webm', 'https://ccp.assets/.temp/x.mp4', 'https://ccp.assets/.temp/x.MP4', 'https://ccp.assets/x.m4v?v=1'])
    assert.equal(isClip(url), true, url);
  for (const url of ['https://ccp.assets/.temp/x.webp', 'https://ccp.assets/x.gif', 'https://ccp.assets/x.webp#.gif', 'https://ccp.game/backroom/stations/slot/fallback/gif0.webp', ''])
    assert.equal(isClip(url), false, url);
});

test('a dealt clip is played, never decoded, at the reel\'s own edge, and the reel gets its canvas', async t => {
  const clip = fakeClip();
  const { media, images, clipCalls, decodeCalls } = setup(t, { clip: async () => clip, decode: async () => ({ canvas: { width: 8, height: 8 }, tick() { return false; }, dispose() {} }) });
  await media.deal({ gifs: [{ key: 'g0', url: CLIP }, { key: 'g1', url: STILL }] });

  assert.deepEqual(clipCalls.map(c => c.url), [CLIP], 'only the clip went to clipSource');
  assert.equal(clipCalls[0].maxEdge, 256, 'a reel face is painted at 256 px, not the wall\'s 384');
  assert.ok(clipCalls[0].signal, 'the deal\'s abort signal rides along, so a replaced deal cancels the load');
  assert.deepEqual(decodeCalls.map(c => c.url), [STILL], 'only the picture went to decodedSource');
  assert.equal(media.gif(0), clip.canvas);
  assert.equal(images.length, 0, 'no <img> was spent on either');
  assert.equal(media.animated, true);
});

test('stillness holds a clip: gif(i, true) pauses it, gif(i) plays it', async t => {
  const clip = fakeClip();
  const { media } = setup(t, { clip: async () => clip });
  await media.deal({ gifs: [{ key: 'g0', url: CLIP }] });
  media.gif(0, true); media.gif(0); media.gif(0, false); media.gif(0, true);
  // The reel never learned what a pause is: it hands stillFx() through, and clip-source.js does the pausing.
  assert.deepEqual(clip.ticks, [true, false, false, true]);
});

test('a clip that cannot play takes the built-in art at once: no <img>, one warning through the log seam', async t => {
  const { media, images, logged } = setup(t, { clip: async () => null });
  await media.deal({ gifs: [{ key: 'g0', url: CLIP }] });
  assert.equal(images.length, 0, 'an <img> cannot show a video either, so the second attempt is not made');
  assert.equal(media.gif(0), null, 'fallback art');
  assert.deepEqual(logged, ['warn: slot media g0 clip could not play, using built-in art']);
});

test('a tainted clip canvas is disposed and reported once, so the reel texture never throws on upload', async t => {
  const clip = fakeClip();
  const { media, logged } = setup(t, { clip: async () => clip, tainted: true });
  await media.deal({ gifs: [{ key: 'g0', url: CLIP }] });
  assert.equal(clip.disposed, true);
  assert.equal(media.gif(0), null);
  assert.deepEqual(logged, ['warn: slot clip is not CORS-readable, using built-in art']);
});

test('a clip that lands after its deal was replaced is disposed, not adopted', async t => {
  let release;
  const late = fakeClip();
  const { media, logged } = setup(t, { clip: () => new Promise(resolve => { release = () => resolve(late); }) });
  const first = media.deal({ gifs: [{ key: 'g0', url: CLIP }] });
  await flush();
  const second = media.deal({ gifs: [] });   // the player stood up and sat back down before the clip opened
  release();
  await first; await second;
  assert.equal(late.disposed, true, 'the late clip is released, or a dismissed reel keeps a video decoding off screen');
  assert.equal(media.gif(0), null);
  assert.deepEqual(logged, [], 'a replaced deal is not a failure and says nothing');
});

test('a load cancelled by the seat being left is silent', async t => {
  const { media, logged, images } = setup(t, { clip: async (url, { signal }) => { signal.dispatchEvent?.(new Event('abort')); return null; } });
  // The real clipSource returns null on abort; the reel must not call that a failure.
  const pending = media.deal({ gifs: [{ key: 'g0', url: CLIP }] });
  media.dispose();
  await pending;
  assert.equal(images.length, 0);
  assert.deepEqual(logged, []);
});
