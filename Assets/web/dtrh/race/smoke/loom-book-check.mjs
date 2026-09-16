/* ============================================================================
 * race/smoke/loom-book-check.mjs - OUR OWN SPIRALS, the pure half.
 *
 *   node race/smoke/loom-book-check.mjs      (from Resources/web/dtrh; 0 on pass)
 *
 * race/loomBook.js is asked for a spiral in every one of the eight rooms and
 * held to the promises the book makes - every room has a palette of at least
 * two of its OWN colours, every draw normalizes into valid schema-v2 params, no
 * draw rotates its hue away from the room it was woven for, the same seed
 * replays the same book in the same order, a different seed does not, and the
 * road's own phrase reaches the centre of the spiral when it fits. Then the
 * picker seam beside it: with a book every draw is a live weave over a gif
 * floor, without one it is the Descent's plain url, and the player's own saved
 * spirals go params-first.
 *
 * The canvas that draws all this is race/smoke/loom-spiral-check.mjs, which
 * needs a browser. This half needs nothing but node.
 *
 * three resolves off the local vendor copy (race/rooms.js wants it) exactly the
 * way race/smoke/slope-check.mjs does it.
 * ==========================================================================*/

import { register } from 'node:module';
import { readFile } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const DTRH = resolve(RACE, '..');
const WEB = resolve(DTRH, '..');                        // Resources/web

const VENDOR = pathToFileURL(resolve(DTRH, 'vendor/three/') + sep).href;
register('data:text/javascript,' + encodeURIComponent(`
  export function resolve(spec, ctx, next) {
    if (spec === 'three') return { url: ${JSON.stringify(VENDOR)} + 'three.module.min.js', shortCircuit: true };
    if (spec.startsWith('three/addons/')) return { url: ${JSON.stringify(VENDOR)} + 'addons/' + spec.slice('three/addons/'.length), shortCircuit: true };
    return next(spec, ctx);
  }`), import.meta.url);

// three's module build reaches for a document the moment it is imported; nothing here draws.
const el = () => ({ addEventListener() {}, removeEventListener() {}, style: {}, width: 0, height: 0, getContext: () => null, set src(v) {}, get src() { return ''; } });
globalThis.document = globalThis.document || { createElement: el, createElementNS: el, documentElement: {} };

const book = await import('../loomBook.js');
const { normalizeParams2, LOOM_STYLES } = await import('../../shared/loomField.js');
const spirals = await import('../../engine/loomSpirals.js');

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/* ============================================================================
 * 1. the book: every room, its own colours, and the same seed twice
 * ==========================================================================*/
eq(book.BOOK_ROOMS.length, 8, 'the book covers all eight rooms');
const SEED = 90210;
{
  const b = book.createLoomBook({ seed: SEED });
  const seenIds = new Set();
  for (const room of book.BOOK_ROOMS) {
    const pal = book.paletteFor(room);
    ok(pal.threads.length >= 2, `${room}: ${pal.threads.length} threads (${pal.threads.join(' ')})`);
    ok(pal.threads.every((h) => /^#[0-9a-f]{6}$/.test(h)), `${room}: every thread is a hex colour`);
    ok(/^#[0-9a-f]{6}$/.test(pal.ground) && /^#[0-9a-f]{6}$/.test(pal.outer), `${room}: the ground is the room's own haze, twice darkened`);
    const ch = book.characterFor(room);
    ok(ch.styles.length >= 1 && ch.styles.every((s) => LOOM_STYLES.includes(s)), `${room}: its styles are Loom styles`);

    const d = b.draw({ room, word: 'good girl' });
    seenIds.add(d.id);
    ok(d.loom === true && d.params && book.LOOM_ID_RE.test(d.id), `${room}: draws a wrapper with a stable id (${d.id})`);
    const q = d.params;
    eq(JSON.stringify(normalizeParams2(q)), JSON.stringify(q), `${room}: the params come out already normalized`);
    ok(ch.styles.includes(q.layer.style), `${room}: weaves in ${q.layer.style}, one of its own two`);
    ok(q.layer.colors.every((c) => pal.threads.includes(c)), `${room}: every thread is one of the room's (${q.layer.colors.join(',')})`);
    ok(q.layer.colors.length >= 2, `${room}: at least two colours to band against each other`);
    eq(q.hueCycles, 0, `${room}: nothing rotates the room's colour away mid-loop`);
    ok(q.layer.arms >= 2 && q.layer.arms <= 6, `${room}: ${q.layer.arms} arms, inside the book's 2-6`);
    ok(q.layer.turns >= 1.5 && q.layer.turns <= 4, `${room}: ${q.layer.turns} turns, inside the book's 1.5-4`);
    eq(q.centerpiece.kind, 'mantra', `${room}: the road's own phrase is the centrepiece`);
    eq(q.centerpiece.text, 'good girl', `${room}: and it is the phrase that was said`);
    ok(q.bg.kind === 'radial' && q.bg.color === pal.ground, `${room}: over the room's own ground`);
  }
  ok(seenIds.size === book.BOOK_ROOMS.length, `eight rooms, ${seenIds.size} different spirals - no two rooms share a picture`);
}
{
  // THE PROMISE: replay a seed, get the book back
  const a = book.createLoomBook({ seed: SEED });
  const c = book.createLoomBook({ seed: SEED });
  const A = book.BOOK_ROOMS.map((room) => a.draw({ room, word: 'good girl' }).id);
  const C = book.BOOK_ROOMS.map((room) => c.draw({ room, word: 'good girl' }).id);
  eq(C.join(','), A.join(','), 'the same seed weaves the same spirals in the same order');
  const other = book.createLoomBook({ seed: SEED + 1 });
  const O = book.BOOK_ROOMS.map((room) => other.draw({ room, word: 'good girl' }).id);
  ok(O.join(',') !== A.join(','), 'and a different seed opens a different book');
  // the same room drawn twice in a run is not the same picture: the draw counter is in the die
  const rep = book.createLoomBook({ seed: SEED });
  const one = rep.draw({ room: 'chapel' }).id, two = rep.draw({ room: 'chapel' }).id;
  ok(one !== two, 'two spirals in one room are two spirals');
  eq(rep.count(), 2, 'and the book counts what it has dealt');
  rep.reseed(SEED);
  eq(rep.draw({ room: 'chapel' }).id, one, 'reseed puts the book back to its first page ("again" on the same seed)');
}
{
  // the mantra: only what fits, always lowercase, and gone when there is nothing to say
  const b = book.createLoomBook({ seed: 7 });
  eq(book.mantraOf('GOOD GIRL'), 'good girl', 'a phrase is lowercased for the centre');
  eq(book.mantraOf('every thought you have belongs to the panel'), '', 'and a phrase past 12 characters is not a centrepiece');
  const quiet = b.draw({ room: 'teagarden' });
  ok(quiet.params.centerpiece.kind !== 'mantra', 'with nothing said, the middle is not a word');
  eq(quiet.word, '', 'and the wrapper says so');
}
{
  // the picker: the wrapper, the floor under it, and the Descent's untouched url path
  spirals.setLoomSpirals([]);
  spirals.setLoomBook(null);
  ok(typeof spirals.pickSpiral() === 'string', 'with no book, pickSpiral is pickSpiralUrl');
  const b = book.createLoomBook({ seed: 3, room: () => 'casino' });
  spirals.setLoomBook(b.draw);
  ok(spirals.hasLoomBook(), 'the race hands the picker a book');
  let wrapped = 0, floored = 0;
  for (let i = 0; i < 100; i++) {
    const got = spirals.pickSpiral();
    if (got && typeof got === 'object') {
      wrapped++;
      if (got.href && /\.(gif|webp)$/.test(got.href)) floored++;
    }
  }
  eq(wrapped, 100, 'with a book and no saved spirals, every draw is a live weave');
  eq(floored, 100, 'and every one of them carries a bundled gif as its floor');
  // the player's own: params-first, url when there is no sidecar
  spirals.setLoomSpirals([
    { slug: 'mine', url: 'https://ccp.spirals/loom_mine.gif', params: { schema: 2, layer: { arms: 3, colors: ['#ff69b4'] } } },
    { slug: 'plain', url: 'https://ccp.spirals/loom_plain.gif' },
  ]);
  let saved = 0, savedLive = 0, plain = 0;
  for (let i = 0; i < 600; i++) {
    const got = spirals.pickSpiral();
    if (got && typeof got === 'object' && got.saved) { saved++; savedLive++; }
    else if (got === 'https://ccp.spirals/loom_plain.gif') { saved++; plain++; }
  }
  ok(saved > 200 && saved < 400, `the player's own spirals still take about half the draws (${saved} of 600)`);
  ok(savedLive > 0 && plain > 0, `a sidecar is drawn live (${savedLive}) and one without is still its gif (${plain})`);
  spirals.setLoomSpirals([]);
  spirals.setLoomBook(null);
}

if (fails) { console.error(`\nloom-book-check: ${fails} failure(s)`); process.exit(1); }
console.log('\nloom-book-check: all good');
