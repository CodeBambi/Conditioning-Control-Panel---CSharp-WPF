/* ============================================================================
 * race/smoke/spiral-pool-check.mjs - node self-check for the bundled spiral pool switch in
 * engine/loomSpirals.js (the race narrows it to LEAN_SPIRALS on the mobile tier).
 *
 *   node race/smoke/spiral-pool-check.mjs      (exits 0 on pass, 1 with a count of failures)
 * ==========================================================================*/

import {
  BUNDLED_SPIRALS, LEAN_SPIRALS, setBundledSpiralPool, getBundledSpiralPool, prefetchSpirals, pickSpiralUrl, setLoomSpirals,
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

if (fails) { console.error(`\nspiral-pool-check: ${fails} failure(s)`); process.exit(1); }
console.log('\nspiral-pool-check: all good');
