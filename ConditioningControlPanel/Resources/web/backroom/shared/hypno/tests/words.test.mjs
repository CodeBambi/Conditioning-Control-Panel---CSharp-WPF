import test from 'node:test';
import assert from 'node:assert/strict';
import { wordBook, wordTexts, showSubs, wordsMsFor, PRESET_WORDS, seedOf } from '../words.js';
import { WORD_MS, WORD_GAP_MS } from '../callout.js';

const fakeCallout = () => { const calls = []; return { calls, word(text, o) { calls.push({ text, ...o }); return { done: Promise.resolve(), plan: null }; } }; };

test('wordBook keeps only dealt sN keys with text; wordTexts falls back to the presets', () => {
  const book = wordBook({ words: [{ key: 's0', text: 'ONLY' }, { key: 's1', text: '' }, { key: 'g0', text: 'nope' }, null, { key: 's2' }] });
  assert.deepEqual([...book.entries()], [['s0', 'ONLY']]);
  assert.deepEqual(wordTexts(['s0', 's1', 's2'], book), ['ONLY', PRESET_WORDS[1], PRESET_WORDS[2]]);
  assert.deepEqual(wordTexts([], book, 2), [PRESET_WORDS[0], PRESET_WORDS[1]]);
  assert.deepEqual(wordTexts(['s0', 's1', 's2', 's3'], book), ['ONLY', PRESET_WORDS[1], PRESET_WORDS[2]]);   // never more than three
  assert.equal(wordsMsFor(0), 0); assert.equal(wordsMsFor(1), WORD_MS); assert.equal(wordsMsFor(3), WORD_MS + 2 * WORD_GAP_MS);
  assert.equal(seedOf('a|b'), seedOf('a|b')); assert.notEqual(seedOf('a|b'), seedOf('a|c'));
});

test('showSubs draws the first sub step\'s words, drops the single and marks pair/cascade wordsShown (the slot\'s rule)', () => {
  const co = fakeCallout();
  const book = wordBook({ words: [{ key: 's0', text: 'DROP' }, { key: 's1', text: 'DEEPER' }] });
  const steps = [{ id: 'fx.gif_burst', symbols: ['g1'], args: { count: 2 } }, { id: 'fx.sub_pair', symbols: ['s1', 's0'], args: undefined }, { id: 'fx.sub_single', symbols: ['s0'] }];
  const out = showSubs(co, steps, { book, gates: { subliminal: true }, seed: 'x' });
  assert.deepEqual(co.calls, [{ text: 'DEEPER', chain: ['DROP'], seed: seedOf('x') }]);
  assert.deepEqual(out.words, ['DEEPER', 'DROP']);
  assert.equal(out.wordsMs, WORD_MS + WORD_GAP_MS);
  assert.deepEqual(out.steps, [{ id: 'fx.gif_burst', symbols: ['g1'], args: { count: 2 } }, { id: 'fx.sub_pair', symbols: ['s1', 's0'], args: { wordsShown: true } }]);
  assert.equal(steps[1].args, undefined, 'the caller\'s list is not mutated');
});

test('showSubs: a single with no keys shows one preset; a cascade with no keys shows three', () => {
  const co = fakeCallout();
  const one = showSubs(co, [{ id: 'fx.sub_single' }], { seed: 1 });
  assert.deepEqual(one.steps, []); assert.deepEqual(one.words, [PRESET_WORDS[0]]);
  const three = showSubs(co, [{ id: 'fx.sub_cascade', symbols: ['g2'] }], {});
  assert.equal(three.words.length, 3); assert.deepEqual(three.steps[0].args, { wordsShown: true });
});

test('showSubs leaves the host the only judge: gate off, no callout, or no sub step', () => {
  const co = fakeCallout();
  const steps = [{ id: 'fx.sub_single', symbols: ['s0'] }];
  for (const [callout, opts] of [[co, { gates: { subliminal: false } }], [null, {}], [{}, {}]]) {
    const out = showSubs(callout, steps, opts);
    assert.equal(out.steps, steps); assert.equal(out.wordsMs, 0); assert.deepEqual(out.words, []);
  }
  assert.equal(co.calls.length, 0);
  const none = showSubs(co, [{ id: 'fx.gif_burst' }], {});
  assert.equal(none.wordsMs, 0); assert.equal(co.calls.length, 0);
});
