/* ============================================================================
 * race/smoke/align-check.mjs - the v2 words files, and the race copies over them.
 *
 *   node race/smoke/align-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * Pure: no browser, no network, no audio. tools/racechart/align.py writes the
 * eleven chart/words/NN Title.words.json files (script-align v2) and their race
 * copies race/words/<cloudId>.json; this holds what a reader may count on:
 *   1. eleven files, each version 1 with the v2 engine stamp and a sha1 source
 *   2. word count == script word count, `i` in order, `w` the script word by the
 *      v1 rule (lowercase, apostrophes kept, punctuation gone), so nothing that
 *      matched a trigger before stops matching now
 *   3. t strictly increasing (v1 had transcript collisions: two words on one
 *      second), every d > 0, every conf in 0.05..1, everything inside the file
 *   4. the race copy race/words.js reads carries the same words at the same
 *      seconds, conf rounded to two places, nothing spare
 *
 * It never prints a word of a transcript. Everything below is counted.
 * ==========================================================================*/

import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');

const WEB = resolve(fileURLToPath(import.meta.url), '..', '..', '..');
const WORDS = resolve(WEB, 'chart', 'words');
const RACE = resolve(WEB, 'race', 'words');
const read = (p) => JSON.parse(readFileSync(p, 'utf8'));
const ENGINE = 'faster-whisper-large-v3 + script-align-v2';
const normalise = (tok) => tok.toLowerCase().replace(/[‘’]/g, "'").replace(/[^a-z0-9']/g, '').replace(/^'+|'+$/g, '');
const halfUp = (v) => Math.floor(v * 100 + 0.5) / 100;

ok(existsSync(WORDS), 'chart/words is there');
const files = existsSync(WORDS) ? readdirSync(WORDS).filter((f) => f.endsWith('.words.json')).sort() : [];
eq(files.length, 11, 'eleven words files');
const index = read(resolve(RACE, 'index.json'));
const rowByHash = new Map(index.rows.map((r) => [String(r.hash).toLowerCase(), r]));

let total = 0;
for (const f of files) {
  const j = read(resolve(WORDS, f));
  const name = f.replace(/\.words\.json$/, '');
  const w = Array.isArray(j.words) ? j.words : [];
  const script = String(j.text || '').split(/\s+/).filter(Boolean);
  total += w.length;
  const src = j.source || {};

  /* ---- 1. the file ------------------------------------------------------- */
  ok(j.version === 1 && j.engine === ENGINE && /^[0-9a-f]{40}$/.test(String(src.hash)) && src.durationSec > 0,
    name + ': version 1, engine "' + ENGINE + '", a sha1 source of ' + src.durationSec + ' s');

  /* ---- 2. the script ------------------------------------------------------ */
  eq(w.length, script.length, name + ': every script word has a row');
  ok(w.every((x, i) => x.i === i), name + ': `i` runs 0..' + (w.length - 1) + ' in order');
  const mism = w.filter((x, i) => x.w !== normalise(script[i] || '')).length;
  eq(mism, 0, name + ': `w` is the script word by the v1 rule');
  ok(w.every((x) => Object.keys(x).length === 5 && typeof x.t === 'number' && typeof x.d === 'number' && typeof x.w === 'string' && typeof x.conf === 'number'),
    name + ': every row is exactly {i, t, d, w, conf}');

  /* ---- 3. the seconds ---------------------------------------------------- */
  const climbing = w.every((x, i) => i === 0 || x.t > w[i - 1].t);
  const shared = w.filter((x, i) => i > 0 && x.t === w[i - 1].t).length;
  ok(climbing && shared === 0, name + ': t strictly increasing, no two words on one second');
  ok(w.every((x) => x.d > 0), name + ': every d > 0');
  ok(w.every((x) => x.conf >= 0.05 && x.conf <= 1), name + ': every conf in 0.05..1');
  ok(w.every((x) => x.t >= 0 && x.t + x.d <= src.durationSec + 2), name + ': every word inside the file');
  const kept = w.filter((x) => x.conf === 0.05).length;
  ok(kept <= w.length * 0.1, name + ': ' + kept + ' of ' + w.length + ' words kept at their v1 second (at most one in ten)');

  /* ---- 4. the race copy -------------------------------------------------- */
  const row = rowByHash.get(String(src.hash).toLowerCase());
  ok(!!row, name + ': race/words/index.json has this hash');
  if (!row) continue;
  const r = read(resolve(RACE, row.file));
  const rw = Array.isArray(r.words) ? r.words : [];
  eq(rw.length, w.length, name + ': the race copy has every word');
  const same = rw.every((x, i) => w[i] && x.t === w[i].t && x.d === w[i].d && x.w === w[i].w && x.conf === halfUp(w[i].conf) && Object.keys(x).length === 4);
  ok(same, name + ': at the same seconds, the same words, conf to two places, nothing spare');
  ok(r.version === 1 && r.hash === String(src.hash).toLowerCase() && Math.abs(r.durationSec - src.durationSec) <= 2,
    name + ': the race copy is version 1 with the same hash and duration');
}
ok(total > 20000, 'the eleven files carry ' + total + ' words between them');

console.log(fails ? '\nalign-check: ' + fails + ' failed' : '\nalign-check: all good');
process.exit(fails ? 1 : 0);
