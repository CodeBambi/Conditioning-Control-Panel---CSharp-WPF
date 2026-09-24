// Self-contained pass over the pure half of exec/background.js (the living backdrop).
//
//   node Resources/web/goon/test/selftest-background.js
//
// Pins: every flavour has its own palette and the house one catches the rest,
// hue mixing takes the short arc, glide approaches without overshoot or snap,
// heat -> params is total and monotone (a rising lead only ever adds life),
// calm is greyer and dimmer than hot, and the dev hooks / event parsing reject junk.

import {
  BG_PALETTES, HOUSE_PALETTE, paletteFor, mixHue, mixPalette, glide, heatParams,
  readBgQuery, heatFromDetail, DEFAULT_HEAT, MAX_STRANDS, MAX_MOTES,
} from '../exec/background.js';
import { FLAVOURS, MINE } from '../ui/flavours.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const near = (a, b, eps = 1e-6) => Math.abs(a - b) <= eps;

/* ---- palettes: one per flavour, all distinct, house for the rest */
{
  for (const f of [...FLAVOURS, MINE]) ok(!!BG_PALETTES[f.id], `flavour ${f.id} has a palette`);
  const keys = Object.keys(BG_PALETTES).map(k => BG_PALETTES[k].hues.join(','));
  ok(new Set(keys).size === keys.length, 'no two palettes share their hues');
  ok(paletteFor('') === HOUSE_PALETTE && paletteFor(null) === HOUSE_PALETTE && paletteFor('nope') === HOUSE_PALETTE, 'unknown or no flavour = house');
  ok(paletteFor('PINK') === BG_PALETTES.pink, 'flavour id is case-insensitive');
  ok(BG_PALETTES.censored.sat < BG_PALETTES.pink.sat / 2, 'Censored is the grey one');
  for (const [id, p] of Object.entries(BG_PALETTES)) {
    ok(p.hues.length === 3 && p.hues.every(h => h >= 0 && h < 360), `${id} hues in range`);
    ok(p.light <= 20, `${id} room stays dark (secondary to media)`, String(p.light));
  }
}

/* ---- hue mix: shortest arc, ends pinned */
{
  ok(near(mixHue(350, 10, 0.5), 0), 'mixHue crosses the 360 seam the short way', String(mixHue(350, 10, 0.5)));
  ok(near(mixHue(10, 350, 0.5), 0), 'mixHue crosses it backwards too');
  ok(near(mixHue(100, 200, 0), 100) && near(mixHue(100, 200, 1), 200), 'mixHue ends pinned');
  ok(near(mixHue(100, 200, 7), 200), 'mixHue clamps k');
  const m = mixPalette(BG_PALETTES.pink, BG_PALETTES.shiny, 0.5);
  ok(m.hues.length === 3 && Number.isFinite(m.sat) && Number.isFinite(m.light), 'mixPalette is whole');
}

/* ---- glide: approaches, never overshoots, frame-rate independent */
{
  let v = 0;
  for (let i = 0; i < 10; i++) v = glide(v, 1, 0.1, 1);
  ok(v > 0.6 && v < 0.7, 'one second at tau 1 covers ~63%', String(v));
  const big = glide(0, 1, 0.5, 1), small = glide(glide(0, 1, 0.25, 1), 1, 0.25, 1);
  ok(near(big, small, 1e-9), 'two half steps = one step');
  let over = false;
  for (let i = 0, x = 0; i < 200; i++) { x = glide(x, 1, 0.1, 0.05); if (x > 1) over = true; }
  ok(!over, 'glide never overshoots');
  ok(glide(0.4, 1, 0, 1) === 0.4 && glide(0.4, 1, -1, 1) === 0.4, 'no time = no move (never a snap)');
  ok(glide(NaN, 0.7, 0.1, 1) === 0.7, 'a junk current adopts the target');
}

/* ---- heat -> params: total, monotone, bounded */
{
  const keys = Object.keys(heatParams(0));
  const falling = new Set(['grain']);
  for (let h = 0; h < 1; h += 0.05) {
    const a = heatParams(h), b = heatParams(h + 0.05);
    for (const k of keys) {
      if (falling.has(k)) ok(b[k] <= a[k] + 1e-12, `${k} falls with heat at ${h.toFixed(2)}`);
      else ok(b[k] >= a[k] - 1e-12, `${k} rises with heat at ${h.toFixed(2)}`);
    }
  }
  const cold = heatParams(0), hot = heatParams(1);
  ok(hot.strands <= MAX_STRANDS && cold.strands >= 1, 'strand count in range');
  ok(hot.motes <= MAX_MOTES && cold.motes > 0, 'mote count in range');
  ok(cold.beads === 0 && cold.clusters === 0 && cold.trails === 0, 'calm has no pearls, trails or clusters');
  ok(hot.beads === 1 && hot.clusters === 1 && hot.trails === 1, 'hot has all of them');
  ok(cold.sat < hot.sat && cold.room < hot.room && cold.rate < hot.rate, 'calm is greyer, darker, slower');
  ok(cold.rate > 0, 'calm still breathes (rate > 0)');
  const junk = heatParams('x'), neg = heatParams(-4), huge = heatParams(9);
  ok(Object.values(junk).every(Number.isFinite), 'junk heat is total');
  ok(near(neg.heat, 0) && near(huge.heat, 1), 'heat clamps');
  ok(heatParams(DEFAULT_HEAT).beads === 0, 'the idle default heat has no pearls yet');
}

/* ---- dev hooks and the gg-heat event */
{
  ok(readBgQuery('?heat=0.8').heat === 0.8, '?heat= pins the heat');
  ok(readBgQuery('?heat=4').heat === 1 && readBgQuery('?heat=x').heat === null, '?heat= clamps and rejects junk');
  ok(readBgQuery('?bgflavour=shiny').flavour === 'shiny' && readBgQuery('?bgflavour=zzz').flavour === null, '?bgflavour= only takes a known id');
  ok(readBgQuery('?bgdebug').debug === true && readBgQuery('').debug === false, '?bgdebug switch');
  ok(readBgQuery(null).heat === null, 'no search string is fine');
  ok(heatFromDetail({ heat: 0.42 }) === 0.42, 'event detail read');
  ok(heatFromDetail({ heat: 3 }) === 1, 'event detail clamps');
  ok(heatFromDetail(null) === null && heatFromDetail({}) === null && heatFromDetail({ heat: 'hot' }) === null, 'junk event is ignored');
}

console.log(`background: ${n - failures}/${n} passed`);
if (failures) { console.error(`${failures} FAILED`); process.exit(1); }
