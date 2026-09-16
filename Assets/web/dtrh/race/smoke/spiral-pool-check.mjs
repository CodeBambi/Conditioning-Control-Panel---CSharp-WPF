/* ============================================================================
 * race/smoke/spiral-pool-check.mjs - node self-check for the bundled spiral pool switch in
 * engine/loomSpirals.js (the race narrows it to LEAN_SPIRALS on the mobile tier), plus the
 * BOOK seam beside it: `setLoomBook` / `pickSpiral`, which is how Racing Thoughts gets a live
 * Loom weave where the Descent gets a gif url. The weave itself is race/smoke/loom-spiral-check.mjs.
 *
 *   node race/smoke/spiral-pool-check.mjs      (exits 0 on pass, 1 with a count of failures)
 * ==========================================================================*/

import {
  BUNDLED_SPIRALS, LEAN_SPIRALS, setBundledSpiralPool, getBundledSpiralPool, prefetchSpirals, pickSpiralUrl, setLoomSpirals,
  setLoomBook, hasLoomBook, pickSpiral,
} from '../../engine/loomSpirals.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

ok(getBundledSpiralPool() === BUNDLED_SPIRALS, 'the pool starts as the whole bundled set (desktop, the Descent)');
ok(LEAN_SPIRALS.length === 2 && LEAN_SPIRALS.every((u) => BUNDLED_SPIRALS.includes(u)), 'LEAN_SPIRALS is two of the bundled urls');
ok(LEAN_SPIRALS.every((u) => /sp[67]\.gif$/.test(u)), 'and they are sp6 + sp7, the two lightest files');

setLoomSpirals([]);
{
  const seen = new Set();
  for (let i = 0; i < 400; i++) seen.add(pickSpiralUrl());
  ok(seen.size === BUNDLED_SPIRALS.length, `the full pool draws every bundled spiral (${seen.size} of ${BUNDLED_SPIRALS.length})`);
}
setBundledSpiralPool(LEAN_SPIRALS);
{
  const seen = new Set();
  for (let i = 0; i < 400; i++) seen.add(pickSpiralUrl());
  ok(seen.size === 2 && [...seen].every((u) => LEAN_SPIRALS.includes(u)), 'the lean pool draws only sp6 + sp7');
  ok(getBundledSpiralPool() !== LEAN_SPIRALS && getBundledSpiralPool().length === 2, 'the setter copies the list');
}
setLoomSpirals([{ slug: 'mine', url: 'ccp.spirals://mine.gif' }]);
{
  let loom = 0;
  for (let i = 0; i < 400; i++) if (pickSpiralUrl() === 'ccp.spirals://mine.gif') loom++;
  ok(loom > 100 && loom < 300, `the Loom's own spirals still mix in about half the time (${loom} of 400)`);
}
setLoomSpirals([]);
setBundledSpiralPool(null);
ok(getBundledSpiralPool() === BUNDLED_SPIRALS, 'null restores the whole set');
setBundledSpiralPool([]);
ok(getBundledSpiralPool() === BUNDLED_SPIRALS, 'so does an empty list');
ok(Array.isArray(prefetchSpirals(LEAN_SPIRALS)) && prefetchSpirals(LEAN_SPIRALS).length === 0, 'prefetch asks for nothing without a DOM and never throws');

/* --- the book seam: pickSpiral() -------------------------------------------------------- */

ok(!hasLoomBook(), 'there is no book until someone sets one (the Descent never does)');
{
  const drawn = [];
  for (let i = 0; i < 100; i++) drawn.push(pickSpiral());
  ok(drawn.every((d) => typeof d === 'string' && BUNDLED_SPIRALS.includes(d)), 'without a book pickSpiral draws plain gif urls, same as pickSpiralUrl');
}

let asked = 0;
setLoomBook(() => { asked++; return { params: { schema: 2, style: 'log' }, id: 'loom:deadbeef' }; });
ok(hasLoomBook(), 'the race hands the book over on run start');
{
  const drawn = [];
  for (let i = 0; i < 200; i++) drawn.push(pickSpiral());
  ok(asked === 200 && drawn.every((d) => d && d.loom === true), 'with a book every draw is a live weave, not a gif');
  ok(drawn.every((d) => d.id === 'loom:deadbeef' && d.params && d.params.schema === 2), 'the weave carries the book params and its id');
  ok(drawn.every((d) => BUNDLED_SPIRALS.includes(d.href)), 'and a bundled gif rides along as the href floor for a lost context');
  ok(drawn.every((d) => d.saved === false), 'a book weave is marked as not the player\'s own');
}
setBundledSpiralPool(LEAN_SPIRALS);
ok(pickSpiral().href !== undefined && LEAN_SPIRALS.includes(pickSpiral().href), 'the floor respects the lean pool on the phone tier');
setBundledSpiralPool(null);

setLoomBook(() => { throw new Error('the book is on fire'); });
{
  const drawn = [];
  for (let i = 0; i < 50; i++) drawn.push(pickSpiral());
  ok(drawn.every((d) => typeof d === 'string'), 'a book that throws falls through to a gif instead of taking the pop with it');
}

setLoomBook(() => ({ params: { schema: 2 }, id: 'loom:bookweave' }));
setLoomSpirals([
  { slug: 'woven', url: 'https://ccp.spirals/loom_woven.gif', params: { schema: 2, style: 'petal' } },
]);
{
  let live = 0; let fromBook = 0;
  for (let i = 0; i < 400; i++) {
    const d = pickSpiral();
    if (d && d.saved === true) live++;
    else if (d && d.loom) fromBook++;
  }
  ok(live > 100 && live < 300, `the player's own weaves take about half the pops (${live} of 400)`);
  ok(live + fromBook === 400, 'and the rest come from the race book, never a stock gif while a book is set');
}
{
  const d = pickSpiral();
  const mine = (() => { for (let i = 0; i < 400; i++) { const x = pickSpiral(); if (x && x.saved) return x; } return null; })();
  ok(mine && mine.params.style === 'petal' && mine.href === 'https://ccp.spirals/loom_woven.gif' && mine.id === 'saved:woven',
    'a saved spiral with params renders live from the params, its gif kept only as the floor');
  ok(d, 'pickSpiral always returns something');
}
setLoomSpirals([{ slug: 'old', url: 'https://ccp.spirals/loom_old.gif' }]);
{
  let mine = 0;
  for (let i = 0; i < 400; i++) { const d = pickSpiral(); if (d === 'https://ccp.spirals/loom_old.gif') mine++; }
  ok(mine > 100 && mine < 300, `a saved spiral with no params sidecar stays a gif url (${mine} of 400)`);
}
setLoomSpirals([]);
setLoomBook(null);
ok(!hasLoomBook() && typeof pickSpiral() === 'string', 'dropping the book on dispose puts the Descent path back');

if (fails) { console.error(`\nspiral-pool-check: ${fails} failure(s)`); process.exit(1); }
console.log('\nspiral-pool-check: all good');
