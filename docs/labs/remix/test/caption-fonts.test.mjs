// Four faces ship with the room so a caption reads the same on the phone that
// made it and the phone that opens it. The three system stacks that came
// before them do not move: an old remix has to draw exactly as it did.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { FONTS } from '../engine/effects/util.js';
import { BUNDLED_FONTS, BUNDLED_KEYS, ensureFonts, fontsInUse, fontUrl, fontLoaded } from '../engine/fonts.js';
import { FONTS as CHIPS } from '../ui/panels.js';

const SYSTEM = ['display', 'mono', 'hand'];

test('the table has the three old faces and the four new ones', () => {
  assert.deepEqual(Object.keys(FONTS), SYSTEM.concat(BUNDLED_KEYS));
  assert.equal(Object.keys(FONTS).length, 7);
});

test('the three system stacks are untouched', () => {
  assert.equal(FONTS.display, "'Bahnschrift Condensed','Arial Narrow','Arial Black',Impact,sans-serif");
  assert.equal(FONTS.mono, "'Cascadia Mono',Consolas,monospace");
  assert.equal(FONTS.hand, "'Segoe Print','Bradley Hand',cursive");
});

test('every bundled face leads its own stack and keeps a fallback behind it', () => {
  for (const key of BUNDLED_KEYS) {
    const stack = FONTS[key];
    assert.ok(stack.startsWith(`'${BUNDLED_FONTS[key].family}'`), key + ': ' + stack);
    assert.ok(stack.split(',').length > 1, key + ' has no fallback');
    assert.ok(!/serif|Georgia|Times/.test(stack.replace(/sans-serif/g, '')), key + ' reaches for a serif');
  }
});

test('every bundled file is there, with its licence next to it', () => {
  let total = 0;
  for (const key of BUNDLED_KEYS) {
    const file = fileURLToPath(fontUrl(key));
    const bytes = readFileSync(file);
    assert.equal(bytes.slice(0, 4).toString('latin1'), 'wOF2', key + ' is not a woff2');
    total += bytes.length;
    const licence = file.replace(/\.woff2$/, '.OFL.txt');
    const text = readFileSync(licence, 'utf8');
    assert.ok(text.includes('SIL Open Font License'), key + ' licence is not the OFL');
    assert.ok(statSync(licence).size > 3000, key + ' licence looks truncated');
  }
  assert.ok(total < 500 * 1024, 'the set is over 500 KB: ' + total);
});

test('the chip row offers all seven, each in its own class', () => {
  assert.deepEqual(CHIPS.map((c) => c[0]), Object.keys(FONTS));
  assert.equal(new Set(CHIPS.map((c) => c[2])).size, 7);
  for (const [, label] of CHIPS) assert.ok(label.length <= 8, label + ' is a long chip');
});

test('fontsInUse reads the bundled faces off the timeline, in table order', () => {
  const blocks = [
    { effect: 'caption', params: { font: 'pixel' } },
    { effect: 'caption', params: { font: 'block' } },
    { effect: 'caption', params: { font: 'block' } },
    { effect: 'caption', params: { font: 'hand' } },
    { effect: 'tint', params: { colour: 'pink' } },
  ];
  assert.deepEqual(fontsInUse(blocks), ['block', 'pixel']);
  assert.deepEqual(fontsInUse([]), []);
  assert.deepEqual(fontsInUse(null), []);
  assert.deepEqual(fontsInUse([{ effect: 'caption', params: {} }]), []);
});

test('ensureFonts resolves without a DOM instead of throwing or hanging', async () => {
  assert.equal(await ensureFonts([]), true);
  assert.equal(await ensureFonts(['display']), true, 'a system face asks for no file');
  assert.equal(await ensureFonts('block'), false, 'no FontFace here, so it is not loaded');
  assert.equal(await ensureFonts(BUNDLED_KEYS), false);
  assert.equal(fontLoaded('block'), false);
});

test('a second call is the same promise, not a second load', async () => {
  const a = ensureFonts('script');
  const b = ensureFonts('script');
  assert.equal(await a, await b);
});

test('a bad key is skipped rather than blocking the rest', async () => {
  assert.equal(await ensureFonts(['nope', 'display']), true);
  assert.equal(fontUrl('nope'), null);
});
