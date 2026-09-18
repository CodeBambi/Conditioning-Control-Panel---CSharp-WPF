/* The clip route in shared/hypno/media.js (2026-09-17, "discard stills"). The card table is dealt thirteen
 * clips now, the wheel eight, roulette four, and the deck is what keeps that from being thirteen video decoders:
 * a clip is routed to clip-source.js at the deck's own edge, a clip nobody drew this tick is PAUSED, `setStill`
 * pauses every clip, and a clip that cannot play fails outright instead of being handed to an <img>. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { isClip } from '../../../room/clip-source.js';

globalThis.deckIsClip = isClip;
const source = (await readFile(new URL('../media.js', import.meta.url), 'utf8'))
  .replace("import { decodedSource } from '../../room/gif-decode.js';", 'const decodedSource = (...args) => globalThis.deckDecode(...args);')
  .replace("import { clipSource, isClip } from '../../room/clip-source.js';", 'const isClip = globalThis.deckIsClip; const clipSource = (...args) => globalThis.deckClip(...args);')
  .replace("'../../room/media-limits.js'", JSON.stringify(new URL('../../../room/media-limits.js', import.meta.url).href));
const { createDeck } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const settle = () => new Promise(resolve => setImmediate(resolve));

function fakeSource(clip) {
  return { canvas: { width: 64, height: 48 }, animated: true, clip, frames: 0, index: 0, ticks: [], disposed: false,
    tick(now, still) { this.ticks.push(!!still); return !still; }, dispose() { this.disposed = true; } };
}

function setup(t, { gifs, clip = async () => fakeSource(true), decode = async () => fakeSource(false) } = {}) {
  const saved = { document: globalThis.document, deckDecode: globalThis.deckDecode, deckClip: globalThis.deckClip, Image: globalThis.Image };
  const images = [], clipCalls = [], decodeCalls = [];
  globalThis.document = { hidden: false };
  globalThis.deckClip = (url, opts) => { clipCalls.push({ url, ...opts }); return clip(url, opts); };
  globalThis.deckDecode = (url, opts) => { decodeCalls.push({ url, ...opts }); return decode(url, opts); };
  globalThis.Image = class { constructor() { images.push(this); } removeAttribute() {} };
  t.after(() => { for (const [k, v] of Object.entries(saved)) { if (v === undefined) delete globalThis[k]; else globalThis[k] = v; } });
  const ctx = { media: async () => ({ seed: 7, gifs }) };
  return { ctx, images, clipCalls, decodeCalls };
}

const A = 'https://ccp.assets/.temp/ccp_temp_remote_a.mp4', B = 'https://ccp.assets/own/b.gif', C = 'https://ccp.assets/.temp/ccp_temp_remote_c.webm';
const dealt = [{ key: 'g0', url: A }, { key: 'g1', url: B }, { key: 'g2', url: C }];

/** Load every dealt key the way a table does: ask for its picture, then let the loads land. */
async function warm(deck, keys) {
  for (const k of keys) deck.image(k);
  for (let i = 0; i < 5; i++) await settle();
}

test('a dealt clip is played at the deck\'s own edge; a GIF is decoded; the card draws either', async t => {
  const { ctx, clipCalls, decodeCalls, images } = setup(t, { gifs: dealt });
  const deck = await createDeck(ctx, { count: 13, maxEdge: 192 });
  t.after(() => deck.dispose());
  await warm(deck, ['g0', 'g1', 'g2']);

  assert.deepEqual(clipCalls.map(c => c.url).sort(), [A, C].sort());
  assert.ok(clipCalls.every(c => c.maxEdge === 192), 'a card face stays at 192 px, not the wall\'s 384');
  assert.ok(clipCalls.every(c => c.signal), 'the deck\'s abort signal rides along');
  assert.deepEqual(decodeCalls.map(c => c.url), [B]);
  assert.ok(deck.image('g0') && deck.image('g2'), 'clip canvases');
  assert.ok(deck.image('g1'), 'the GIF canvas');
  assert.equal(images.length, 0, 'no <img> was spent');
  assert.equal(deck.debug().ready, 3);
});

test('a resident clip nobody drew this tick is paused; the one on the table plays; a GIF is left alone', async t => {
  const sources = new Map();
  const { ctx } = setup(t, { gifs: dealt, clip: async url => { const s = fakeSource(true); sources.set(url, s); return s; }, decode: async url => { const s = fakeSource(false); sources.set(url, s); return s; } });
  const deck = await createDeck(ctx, { count: 13 });
  t.after(() => deck.dispose());
  await warm(deck, ['g0', 'g1', 'g2']);
  for (const s of sources.values()) s.ticks.length = 0;

  deck.tick(1000, ['g0']);   // only the ace is face up
  assert.deepEqual(sources.get(A).ticks, [false], 'the drawn clip advances');
  assert.deepEqual(sources.get(C).ticks, [true], 'the clip nobody drew is paused, or it keeps decoding off the table');
  assert.deepEqual(sources.get(B).ticks, [], 'a GIF costs nothing between ticks and is not sent back to frame one');

  // The next hand turns the other clip up: it resumes, and the ace pauses in its place.
  for (const s of sources.values()) s.ticks.length = 0;
  deck.tick(1100, ['g2']);
  assert.deepEqual(sources.get(C).ticks, [false]);
  assert.deepEqual(sources.get(A).ticks, [true]);
});

test('setStill pauses every clip and holds every GIF, whether or not it was drawn', async t => {
  const sources = new Map();
  const { ctx } = setup(t, { gifs: dealt, clip: async url => { const s = fakeSource(true); sources.set(url, s); return s; }, decode: async url => { const s = fakeSource(false); sources.set(url, s); return s; } });
  const deck = await createDeck(ctx, { count: 13, still: true });
  t.after(() => deck.dispose());
  await warm(deck, ['g0', 'g1', 'g2']);
  for (const s of sources.values()) s.ticks.length = 0;

  deck.tick(2000, ['g0']);
  for (const [url, s] of sources) assert.deepEqual(s.ticks, [true], url + ' is held on a frame under stillness');
  assert.equal(deck.debug().still, true);
});

test('a clip that cannot play fails outright: no <img>, and the card draws nothing rather than a broken still', async t => {
  const { ctx, images } = setup(t, { gifs: [{ key: 'g0', url: A }], clip: async () => null });
  const deck = await createDeck(ctx, { count: 4 });
  t.after(() => deck.dispose());
  await warm(deck, ['g0']);

  assert.equal(images.length, 0, 'an <img> cannot show a video either');
  assert.equal(deck.image('g0'), null);
  assert.equal(deck.debug().failed, 1);
  assert.equal(deck.draw({ save() {}, restore() {}, beginPath() {}, rect() {}, clip() {}, drawImage() {}, globalAlpha: 1 }, 'g0', 0, 0, 10, 10), false);
});

test('dispose releases every clip, so a dismissed table keeps no video decoding', async t => {
  const sources = [];
  const { ctx } = setup(t, { gifs: dealt, clip: async () => { const s = fakeSource(true); sources.push(s); return s; } });
  const deck = await createDeck(ctx, { count: 13 });
  await warm(deck, ['g0', 'g2']);
  assert.equal(sources.length, 2);
  deck.dispose();
  assert.ok(sources.every(s => s.disposed));
});
