/* shared/hypno/media.js without a page: the deal, the value mapping, stable keys. Drawing is kit-check.mjs's. */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DECK_VALUES, fnv1a, valueIndex, drawableUrl, createDeck } from '../index.js';
import { createMockHost } from './mock-host.js';

const urls = (n) => Array.from({ length: n }, (_, i) => `https://ccp.assets/pool/${i}.gif`);

test('DECK_VALUES is the pure module rank order, T for ten, frozen', () => {
  assert.deepEqual([...DECK_VALUES], 'A23456789TJQK'.split(''));
  assert.ok(Object.isFrozen(DECK_VALUES));
  assert.equal(valueIndex('A'), 0);
  assert.equal(valueIndex('Td'), 9);
  assert.equal(valueIndex('10'), 9);
  assert.equal(valueIndex('qh'), 11);
  assert.equal(valueIndex('Ks'), 12);
  assert.equal(valueIndex('X'), -1);
  assert.equal(valueIndex(null), -1);
});

test('FNV-1a 32 matches the reference vectors', () => {
  assert.equal(fnv1a(''), 0x811c9dc5);
  assert.equal(fnv1a('a'), 0xe40c292c);
  assert.equal(fnv1a('foobar'), 0xbf9cf968);
});

test('a 13 deal asks for 13 and maps A..K in order', async () => {
  const host = createMockHost({ deal: urls(13) });
  const deck = await createDeck(host.ctx);
  assert.equal(host.media[0].count, 13);
  assert.equal(deck.size, 13);
  assert.deepEqual([...deck.keys], urls(13).map((_, i) => 'g' + i));
  assert.equal(deck.keyFor('A'), 'g0');
  assert.equal(deck.keyFor('Td'), 'g9');
  assert.equal(deck.keyFor('Kc'), 'g12');
  assert.equal(deck.seed, 7);
});

test('fewer than 13 pictures cycle: value i wears gifs[i % n]', async () => {
  const host = createMockHost({ deal: urls(5) });
  const deck = await createDeck(host.ctx, { count: 13 });
  assert.equal(deck.size, 5, 'no padding when the pool has pictures');
  assert.deepEqual(DECK_VALUES.map((v) => deck.keyFor(v)), DECK_VALUES.map((_, i) => 'g' + (i % 5)));
  assert.equal(deck.keyAt(7), 'g2');
  assert.equal(deck.keyAt(-1), 'g4');
  assert.equal(deck.keyFor('?'), null);
});

test('wheel and roulette: count 4, pickKey is stable for a result and spreads', async () => {
  const host = createMockHost({ deal: urls(9) });
  const deck = await createDeck(host.ctx, { count: 4 });
  assert.equal(host.media[0].count, 4);
  assert.equal(deck.size, 4);
  assert.equal(deck.pickKey('2026-09-14|deep'), deck.pickKey('2026-09-14|deep'));
  assert.equal(deck.pickKey('x'), deck.keys[fnv1a('x') % 4]);
  const seen = new Set(Array.from({ length: 40 }, (_, i) => deck.pickKey('spin' + i)));
  assert.equal(seen.size, 4);
});

test('an empty pool deals the four fallback loops; no host or a lost reply keeps one key', async () => {
  const deck = await createDeck(createMockHost().ctx);
  assert.equal(deck.size, 4);
  const none = await createDeck({});
  assert.deepEqual([...none.keys], ['g0']);
  assert.equal(none.keyFor('K'), 'g0');
  assert.equal(none.draw({}, 'g0', 0, 0, 10, 10), false);
  const lost = await createDeck({ media: async () => ({ ok: false, reason: 'timeout', gifs: [] }) });
  assert.deepEqual([...lost.keys], ['g0']);
});

test('the deal is read defensively: bad keys, duplicates, more than 13, a bad count', async () => {
  let asked = null;
  const gifs = [{ key: 'g0', url: 'https://ccp.assets/a.gif' }, { key: 'g0', url: 'https://ccp.assets/b.gif' }, { key: '../g1', url: 'x' },
    ...Array.from({ length: 20 }, (_, i) => ({ key: 'g' + (i + 1), url: 'https://evil.example/' + i + '.gif' }))];
  const deck = await createDeck({ media: async (o) => { asked = o; return { seed: 3, gifs }; } }, { count: 99 });
  assert.deepEqual(asked, { count: 13 }, 'a bad count asks for the default 13');
  assert.equal(deck.size, 13);
  assert.equal(deck.keys[0], 'g0');
  assert.equal(deck.image('g5'), null, 'a foreign url is keyed but never drawn');
});

test('drawableUrl: ccp.assets and ccp.game only (no page origin in node)', () => {
  assert.ok(drawableUrl('https://ccp.assets/Flash/a.gif'));
  assert.ok(drawableUrl('https://ccp.game/backroom/stations/slot/fallback/gif0.webp'));
  assert.ok(!drawableUrl('http://ccp.assets/a.gif'));
  assert.ok(!drawableUrl('https://example.com/a.gif'));
  assert.ok(!drawableUrl('C:/Users/x.gif'));
  assert.ok(!drawableUrl(''));
});

test('no decoder: the still comes from a CORS <img>, so a texture upload of image() is not tainted', async () => {
  const imgs = [];
  const canvas = () => ({ width: 0, height: 0, getContext: () => ({ drawImage() {} }) });
  globalThis.document = { createElement: canvas };
  globalThis.Image = class { constructor() { imgs.push(this); this.naturalWidth = 400; this.naturalHeight = 200; this.order = []; }
    set crossOrigin(v) { this.order.push('crossOrigin'); this.cors = v; }
    set src(v) { this.order.push('src'); this.url = v; setTimeout(() => this.onload(), 0); } };
  try {
    const deck = await createDeck(createMockHost({ deal: urls(1) }).ctx, { count: 1 });
    for (let i = 0; i < 20 && !deck.image('g0'); i++) await new Promise((r) => setTimeout(r, 5));
    assert.equal(imgs.length, 1);
    assert.equal(imgs[0].cors, 'anonymous');
    assert.deepEqual(imgs[0].order, ['crossOrigin', 'src'], 'crossOrigin is set before src');
    assert.deepEqual([deck.image('g0').width, deck.image('g0').height], [192, 96]);
    deck.dispose();
  } finally { delete globalThis.document; delete globalThis.Image; }
});
