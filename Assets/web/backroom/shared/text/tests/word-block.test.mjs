/* shared/text/tests/word-block.test.mjs - callout.js wordBlock(): the big centred word beat wraps a long
 * subliminal / trigger phrase onto 2 or 3 lines and steps the tier font down so the block fits 90% of the
 * viewport. A short word keeps WORD_SIZE_VH on one line, exactly as before.
 *   node --test shared/text/tests/word-block.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { wordBlock, WORD_SIZE_VH, WORD_MAX_LINES, WORD_FIT_VW, ZOOM } from '../../hypno/callout.js';

const PHONE = { w: 390, h: 844 };
const DESK = { w: 1920, h: 1080 };
const joined = (lines) => lines.join(' ').replace(/\s+/g, ' ').trim();

test('a short word keeps the full size and one line', () => {
  for (const view of [PHONE, DESK]) {
    const b = wordBlock('DROP', { view });
    assert.deepEqual(b.lines, ['DROP']);
    assert.equal(b.sizeVh, WORD_SIZE_VH);
  }
});

test('a long trigger phrase wraps and never exceeds WORD_MAX_LINES', () => {
  for (const view of [PHONE, DESK]) {
    const b = wordBlock('I CANT RESIST MY TRIGGERS', { view });
    assert.ok(b.lines.length >= 2, `${view.w}: lines ${b.lines.length}`);
    assert.ok(b.lines.length <= WORD_MAX_LINES);
    assert.equal(joined(b.lines), 'I CANT RESIST MY TRIGGERS');
  }
});

test('the wrapped block fits 90% of the viewport width AT THE ZOOM PEAK', () => {
  for (const view of [PHONE, DESK]) {
    const b = wordBlock('I CANT RESIST MY TRIGGERS AND I NEVER WILL AGAIN', { view });
    const px = b.sizeVh * view.h / 100;
    if (b.sizeVh <= 4) continue;   // already at the floor: nothing left to shrink, the block just overflows
    for (const l of b.lines) {
      // the same 0.5em average glyph the helper measures with
      assert.ok(l.length * px * 0.5 * ZOOM.to <= view.w * WORD_FIT_VW + 1e-6, `${view.w}: "${l}" at ${b.sizeVh}vh zoomed`);
    }
    assert.ok(b.sizeVh <= WORD_SIZE_VH && b.sizeVh >= 4);
  }
});

test('a narrow phone shrinks the tier font more than a desktop does', () => {
  const phrase = 'GOOD GIRL FOR ME ALWAYS AND FOREVER';
  assert.ok(wordBlock(phrase, { view: PHONE }).sizeVh <= wordBlock(phrase, { view: DESK }).sizeVh);
});

test('empty and null give one empty line and never throw', () => {
  assert.deepEqual(wordBlock('', { view: DESK }).lines, ['']);
  assert.deepEqual(wordBlock(null, { view: DESK }).lines, ['']);
});

test('a phrase at the wrap threshold still reads on one line', () => {
  const b = wordBlock('GOOD GIRL', { view: DESK });
  assert.deepEqual(b.lines, ['GOOD GIRL']);
});
