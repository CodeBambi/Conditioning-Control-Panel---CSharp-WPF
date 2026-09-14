// The voice lock. The room's copy is short, warm and plain, and the type is never a serif.
// This test reads the files as text so a stray em-dash or a Georgia fallback cannot land quietly.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const FILES = [
  'ui/auto.js', 'ui/export-sheet.js', 'ui/help-content.js', 'ui/app.js', 'ui/dropzone.js', 'ui/doors.js',
  'index.html', 'auto.css', 'remix.css', 'doors.css',
];
const read = f => readFileSync(join(here, '..', f), 'utf8').split(/\r?\n/);

// every hit comes back as "file:line  the line", so a failure names the place
function hits(re, skip) {
  const found = [];
  for (const f of FILES) {
    read(f).forEach((line, i) => {
      if (!re.test(line)) return;
      if (skip && skip(line, f)) return;
      found.push(`${f}:${i + 1}  ${line.trim()}`);
    });
  }
  return found;
}

test('no em-dashes anywhere in the room', () => {
  assert.deepEqual(hits(/—/), []);
});

test('nothing is banked, and nothing banks', () => {
  assert.deepEqual(hits(/\bbank(s|ed|ing)?\b/i), []);
});

// a stack may end in sans-serif and nothing else: strip that word, then any serif left is a real one
test('no serif in a type stack', () => {
  const isStack = line => /font-family|font:|--disp|--body|--mono|--hand/.test(line);
  assert.deepEqual(hits(/\bserif\b/, line => !isStack(line) || !/\bserif\b/.test(line.replace(/sans-serif/g, ''))), []);
});

test('no book faces in a font-family', () => {
  assert.deepEqual(hits(/font-family[^;]*(Georgia|Times|Garamond|Palatino|Baskerville)/i), []);
});
