/* sound.test.mjs - which lever and which drum the cabinet picks. node --test, no audio. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pickVariant, VARIANT_KEY } from '../sound.js';
import { DEFAULT_SFX } from '../../../shared/sound/kit.js';

test('the default pair is the owner\'s: a candy lever over a rattle-and-bell drum', () => {
  assert.deepEqual(pickVariant({}), { lever: 'B', reel: 'C' });
  assert.deepEqual(pickVariant({}), { ...DEFAULT_SFX });
  assert.equal(VARIANT_KEY, 'br.sfx.variant');
});

test('the saved pair is read from localStorage, as json or as the compact pair', () => {
  assert.deepEqual(pickVariant({ store: '{"lever":"A","reel":"D"}' }), { lever: 'A', reel: 'D' });
  assert.deepEqual(pickVariant({ store: { lever: 'c', reel: 'b' } }), { lever: 'C', reel: 'B' });
  assert.deepEqual(pickVariant({ store: 'A/D' }), { lever: 'A', reel: 'D' });
  assert.deepEqual(pickVariant({ store: 'ad' }), { lever: 'A', reel: 'D' });
  assert.deepEqual(pickVariant({ store: '{"lever":"A"}' }), { lever: 'A', reel: DEFAULT_SFX.reel }, 'half a pair keeps the other default');
});

test('the query wins over the saved pair, so a dev page can try one without changing it', () => {
  assert.deepEqual(pickVariant({ search: '?lever=A&reel=D' }), { lever: 'A', reel: 'D' });
  assert.deepEqual(pickVariant({ store: 'A/D', search: '?reel=A' }), { lever: 'A', reel: 'A' });
  assert.deepEqual(pickVariant({ search: 'lever=c' }), { lever: 'C', reel: DEFAULT_SFX.reel }, 'with or without the leading ?');
});

test('nonsense never leaves the cabinet silent: it falls back to the default pair', () => {
  for (const store of ['', '  ', 'zz', 'E/F', '{"lever":"E"}', '[1,2]', null, 7]) {
    assert.deepEqual(pickVariant({ store }), { ...DEFAULT_SFX }, 'store ' + JSON.stringify(store));
  }
  assert.deepEqual(pickVariant({ search: '?lever=Q&reel=9' }), { ...DEFAULT_SFX });
  assert.deepEqual(pickVariant(), { ...DEFAULT_SFX }, 'no argument at all');
});
