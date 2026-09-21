import { test } from 'node:test';
import assert from 'node:assert/strict';
import { FLAVOURS, flavourHost, currentFlavour, applyFlavour } from './flavours.js';

const shell = (config) => { const calls = []; return { calls, get: () => ({ ...config }), set: async (...a) => { calls.push(a); if (config.refuse) throw new Error('no'); return {}; } }; };

test('four flavours, each a short list of niche names the shell will accept', () => {
  assert.equal(FLAVOURS.length, 4);
  assert.equal(new Set(FLAVOURS.map(f => f.id)).size, 4);
  for (const f of FLAVOURS) {
    assert.ok(f.name && f.line && /^#[0-9a-f]{6}$/.test(f.tint));
    assert.ok(f.subs.length >= 2 && f.subs.length <= 8, 'the shell keeps eight niches at most');
    for (const s of f.subs) assert.match(s, /^[a-zA-Z0-9_]{2,40}$/, 'the shell drops any other shape without a word');
    assert.ok(!/!/.test(f.name + f.line), 'no exclamation marks in chrome');
  }
});

test('no shell, no prompt: the host is only there when it can both answer and switch', () => {
  assert.equal(flavourHost(null), null); assert.equal(flavourHost({}), null);
  assert.equal(flavourHost({ __brMedia: { get() {} } }), null);
  const m = { get() {}, set() {} }; assert.equal(flavourHost({ __brMedia: m }), m);
});

test('the current flavour is recognised whatever the order or case, and a list of the player own is nobody', () => {
  const pink = FLAVOURS.find(f => f.id === 'pink');
  assert.equal(currentFlavour(shell({ mode: 'scrolller', sources: [...pink.subs].reverse().map(s => s.toUpperCase()) })), pink);
  assert.equal(currentFlavour(shell({ mode: 'scrolller', sources: [...pink.subs, 'chastity'] })), null);
  assert.equal(currentFlavour(shell({ mode: 'scrolller', sources: [...pink.subs, 'chastity'], disabledSources: ['chastity'] })), pink, 'a niche switched off does not count');
  assert.equal(currentFlavour(shell({ mode: 'bundled', sources: pink.subs })), null);
  assert.equal(currentFlavour({ get() { throw new Error('x'); } }), null);
});

test('picking a flavour switches the shell to Scrolller with exactly those niches, all switched on', async () => {
  const s = shell({ mode: 'bundled', sources: [] }), shiny = FLAVOURS.find(f => f.id === 'shiny');
  assert.equal(await applyFlavour(s, shiny), true);
  assert.deepEqual(s.calls, [['scrolller', shiny.subs.join(','), []]]);
  assert.equal(await applyFlavour(shell({ refuse: true }), shiny), false, 'a refusal is an answer, never a throw');
  assert.equal(await applyFlavour(s, null), false); assert.equal(await applyFlavour(null, shiny), false);
});
