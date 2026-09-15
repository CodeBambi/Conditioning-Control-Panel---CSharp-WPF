/* shared/text/tests/wrap.test.mjs - the wrapping the marquee, the reel glyphs and the word beat share.
 *   node --test shared/text/tests/wrap.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { wrapLines, fitText } from '../wrap.js';

const joined = (lines) => lines.join(' ').replace(/\s+/g, ' ').trim();

test('a short phrase stays on one line', () => {
  assert.deepEqual(wrapLines('GOOD GIRL', 12, 3), ['GOOD GIRL']);
  assert.deepEqual(wrapLines('DROP', 12, 3), ['DROP']);
});

test('a long phrase wraps on word boundaries and keeps every word', () => {
  const lines = wrapLines('I CANT RESIST MY TRIGGERS', 12, 3);
  assert.ok(lines.length > 1 && lines.length <= 3, `lines: ${lines.length}`);
  for (const l of lines) assert.ok(l.length <= 12, `too wide: ${l}`);
  assert.equal(joined(lines), 'I CANT RESIST MY TRIGGERS');
});

test('never more than maxLines: the width widens instead of dropping text', () => {
  const lines = wrapLines('ONE TWO THREE FOUR FIVE SIX SEVEN EIGHT NINE TEN', 6, 3);
  assert.equal(lines.length, 3);
  assert.equal(joined(lines), 'ONE TWO THREE FOUR FIVE SIX SEVEN EIGHT NINE TEN');
});

test('a single word longer than the line is hard-split, not lost', () => {
  const lines = wrapLines('ABCDEFGHIJKLMNOP', 5, 3);
  assert.ok(lines.length <= 3);
  assert.equal(lines.join(''), 'ABCDEFGHIJKLMNOP');
});

test('minLines forces a long phrase onto two lines', () => {
  const lines = wrapLines('I CANT RESIST MY TRIGGERS', 80, 3, { minLines: 2 });
  assert.ok(lines.length >= 2, `lines: ${lines.length}`);
  assert.equal(joined(lines), 'I CANT RESIST MY TRIGGERS');
});

test('minLines leaves a short phrase alone when it cannot be split', () => {
  assert.deepEqual(wrapLines('DROP', 40, 3, { minLines: 2 }), ['DROP']);
});

test('empty, null and whitespace give one empty line', () => {
  assert.deepEqual(wrapLines('', 10, 3), ['']);
  assert.deepEqual(wrapLines(null, 10, 3), ['']);
  assert.deepEqual(wrapLines('   ', 10, 3), ['']);
});

test('runs of whitespace and newlines collapse to single spaces', () => {
  assert.deepEqual(wrapLines('GOOD\n\n  GIRL', 40, 3), ['GOOD GIRL']);
});

/* fitText drives the same wrap off a measure function: 0.5 units of width per character per size unit. */
const measure = (s, size) => String(s).length * size * 0.5;

test('fitText picks a big size for a short phrase', () => {
  const r = fitText('DROP', { measure, width: 200, height: 200, maxLines: 3, min: 8, max: 96 });
  assert.deepEqual(r.lines, ['DROP']);
  assert.ok(r.width <= 200 && r.height <= 200);
  assert.ok(r.size > 40, `size: ${r.size}`);
});

test('fitText shrinks and wraps a long phrase into the same box', () => {
  const r = fitText('I CANT RESIST MY TRIGGERS', { measure, width: 200, height: 200, maxLines: 3, min: 8, max: 96 });
  assert.ok(r.lines.length >= 2 && r.lines.length <= 3, `lines: ${r.lines.length}`);
  assert.ok(r.width <= 200, `width: ${r.width}`);
  assert.ok(r.height <= 200, `height: ${r.height}`);
  assert.equal(joined(r.lines), 'I CANT RESIST MY TRIGGERS');
});

test('fitText result always fits the box, for many phrase lengths', () => {
  for (let n = 1; n <= 60; n++) {
    const text = Array.from({ length: n }, (_, i) => 'WORD' + (i % 7)).join(' ');
    const r = fitText(text, { measure, width: 220, height: 180, maxLines: 3, min: 8, max: 72 });
    assert.ok(r.lines.length <= 3, `n=${n} lines=${r.lines.length}`);
    if (r.size > 8) {
      assert.ok(r.width <= 220 + 1e-9, `n=${n} width=${r.width}`);
      assert.ok(r.height <= 180 + 1e-9, `n=${n} height=${r.height}`);
    }
  }
});

test('fitText never goes below min and never above max', () => {
  const huge = 'X'.repeat(4000);
  const small = fitText(huge, { measure, width: 100, height: 40, maxLines: 3, min: 9, max: 60 });
  assert.equal(small.size, 9);
  const big = fitText('A', { measure, width: 10000, height: 10000, maxLines: 3, min: 9, max: 60 });
  assert.equal(big.size, 60);
});

test('fitText with no height cares only about the width', () => {
  const r = fitText('GOOD GIRL FOR ME', { measure, width: 300, maxLines: 2, min: 8, max: 200 });
  assert.ok(r.width <= 300);
  assert.ok(r.lines.length <= 2);
});
