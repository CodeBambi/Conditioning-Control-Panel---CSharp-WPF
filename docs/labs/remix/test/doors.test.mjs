// The doors: three ways out of the room. The data is the contract, so it is checked here rather
// than in a browser: three doors in the owner's order, absolute https links that all carry the
// tag, and a clip plus a poster on disk for every one of them.
import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { DOORS, clipSrc, posterSrc, doorById, NOT_NOW_KEY, AFTER_LINE, doorsDismissed, dismissDoors } from '../ui/doors.js';

const here = dirname(fileURLToPath(import.meta.url));
const room = f => join(here, '..', f);
const asset = url => room(url.replace(/^\/remix\//, ''));
const read = f => readFileSync(room(f), 'utf8');

test('three doors, in the order the owner picked', () => {
  assert.deepEqual(DOORS.map(d => d.id), ['loom', 'intake', 'desktop']);
});

test('every door has the words a card needs', () => {
  for (const d of DOORS) {
    for (const k of ['eyebrow', 'title', 'line', 'button', 'href']) {
      assert.equal(typeof d[k], 'string', `${d.id}.${k}`);
      assert.ok(d[k].trim().length, `${d.id}.${k} is empty`);
    }
    assert.ok(d.title.length <= 40, `${d.id} title is long`);
    assert.ok(d.line.length <= 60, `${d.id} line is long`);
  }
});

test('the links are absolute https and say where the visit came from', () => {
  for (const d of DOORS) {
    const u = new URL(d.href);
    assert.equal(u.protocol, 'https:', d.id);
    assert.ok(/(^|\.)cclabs\.app$/.test(u.hostname), `${d.id} points off the estate: ${u.hostname}`);
    assert.equal(u.searchParams.get('from'), 'remix', `${d.id} lost the tag`);
  }
});

test('the ids are unique and safe in a path', () => {
  assert.equal(new Set(DOORS.map(d => d.id)).size, DOORS.length);
  for (const d of DOORS) assert.match(d.id, /^[a-z][a-z0-9-]*$/);
});

test('doorById finds a door and shrugs at anything else', () => {
  assert.equal(doorById('loom'), DOORS[0]);
  assert.equal(doorById('nope'), null);
});

test('a clip and a poster are on disk for every door', () => {
  for (const d of DOORS) {
    for (const url of [clipSrc(d), posterSrc(d)]) {
      assert.match(url, /^\/remix\/assets\/doors\//, url);
      const file = asset(url);
      assert.ok(existsSync(file), `missing ${url}`);
      assert.ok(statSync(file).size > 1024, `${url} is empty`);
    }
  }
  assert.equal(clipSrc(DOORS[0]), '/remix/assets/doors/loom.mp4');
  assert.equal(posterSrc(DOORS[0]), '/remix/assets/doors/loom.webp');
});

test('the clips stay small enough to hover without a wait', () => {
  for (const d of DOORS) assert.ok(statSync(asset(clipSrc(d))).size < 1.5 * 1024 * 1024, `${d.id} clip is heavy`);
});

test('the module is wired into the room', () => {
  assert.match(read('index.html'), /href="\/remix\/doors\.css"/);
  assert.match(read('ui/auto.js'), /mountDeck/);
  assert.match(read('ui/auto.js'), /id="side-doors"/);
});

test('a card never loads a clip it was not asked for', () => {
  const src = read('ui/doors.js');
  assert.match(src, /preload = 'none'/);
  assert.match(src, /rel = 'noopener'/);
  assert.match(src, /target = '_blank'/);
});

/* ---- the after-save strip ---- */

test('not now remembers itself under one key, for the session only', () => {
  assert.equal(NOT_NOW_KEY, 'remix.doors.notnow');
  assert.match(read('ui/doors.js'), /sessionStorage\.setItem\(NOT_NOW_KEY/);
  assert.doesNotMatch(read('ui/doors.js'), /localStorage/);
});

test('a missing storage jar is not an error', () => {
  // node has no sessionStorage at all, which is the locked-jar case a browser can also hand us
  assert.equal(typeof globalThis.sessionStorage, 'undefined');
  assert.equal(doorsDismissed(), false);
  assert.doesNotThrow(dismissDoors);
  assert.equal(doorsDismissed(), false);
});

test('the strip says one line and offers a way out', () => {
  assert.equal(AFTER_LINE, 'Your gif carries the address. This is where it leads.');
  const src = read('ui/doors.js');
  assert.match(src, /if \(doorsDismissed\(\)\) return null;/);
  assert.match(src, /'not now'/);
  for (const d of DOORS) assert.ok(src.includes(d.button), `${d.id} lost its button words`);
});

test('the strip comes up after a save and never before one', () => {
  const src = read('ui/export-sheet.js');
  assert.match(src, /import \{ doorStrip \} from '\.\/doors\.js';/);
  assert.match(src, /if \(done\) \{ savedLabel\(\); showDoors\(\); \}/); // the gif path
  assert.equal((src.match(/showDoors\(\)/g) || []).length, 3); // the guard, the gif, the video
  assert.match(src, /showDoors\(\);$/m);
});

test('the row scrolls sideways with no bar on it', () => {
  const css = read('doors.css');
  assert.match(css, /\.door-row \{[^}]*overflow-x:auto/);
  assert.match(css, /\.door-row \{[^}]*scrollbar-width:none/);
  assert.match(css, /\.door-row::-webkit-scrollbar \{ display:none; \}/);
});
