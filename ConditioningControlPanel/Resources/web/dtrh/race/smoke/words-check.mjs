/* ============================================================================
 * race/smoke/words-check.mjs - the shipped transcripts and the loader over them.
 *
 *   node race/smoke/words-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * Pure: no browser, no network, no audio. The transcripts are read off disk and
 * `race/words.js` is driven through a fetch seam that serves the same directory,
 * so this runs anywhere node does.
 *
 * What it holds:
 *   1. the table lines up with race/levels.json: a row per level, every cloudId a
 *      real level, every hash the hash the transcript itself carries
 *   2. every transcript parses, is version 1, is sorted, and its duration is the
 *      one the level row wrote down
 *   3. the loader answers by cloudId, answers by hash, answers null for a track
 *      that has none, and never throws at a 404 or at junk
 *   4. the detector reads the catalogue over a real transcript: the opening track
 *      really does say the phrase the road is going to be built on
 *
 * It never prints a line of a transcript. Everything below is counted, never quoted.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { TRIGGER_SETS } from '../../chart/editor/triggerSets.js';
import { detect } from '../../chart/maker/triggers.js';
import { loadWords, forgetWords } from '../words.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));
const INDEX_URL = pathToFileURL(resolve(RACE, 'words/index.json')).href;

/** The loader's seam: `file:` urls off this worktree, answered like a web server. */
const localFetch = async (url) => {
  try { return { ok: true, status: 200, json: async () => JSON.parse(readFileSync(fileURLToPath(String(url)), 'utf8')) }; }
  catch (e) { return { ok: false, status: 404, json: async () => { throw e; } }; }
};

/* ---- 1. the table --------------------------------------------------------- */
const index = read('words/index.json');
const levels = read('levels.json').sets[0].levels;
const byId = new Map(levels.map((l) => [String(l.id).toLowerCase(), l]));

eq(index.version, 1, 'the table is version 1');
eq(index.rows.length, 11, 'eleven transcripts, one per level');
eq(new Set(index.rows.map((r) => r.cloudId)).size, index.rows.length, 'every cloudId in the table is its own row');
ok(index.rows.every((r) => byId.has(r.cloudId)), 'every cloudId is a level in race/levels.json');
ok(index.rows.every((r) => r.cloudId === r.cloudId.toLowerCase()), 'every cloudId is lowercase');
ok(index.rows.every((r) => /^[0-9a-f]{40}$/.test(String(r.hash))), 'every hash is a sha1');
ok(index.rows.every((r) => r.file === r.cloudId + '.json'), 'every row names its own file');
ok(index.rows.every((r) => byId.get(r.cloudId).title === r.title && byId.get(r.cloudId).n === r.n),
  "every row carries the level's own title and number");

/* ---- 2. the transcripts --------------------------------------------------- */
let words = 0;
for (const row of index.rows) {
  const j = read('words/' + row.file);
  const lvl = byId.get(row.cloudId);
  const w = Array.isArray(j.words) ? j.words : [];
  words += w.length;
  const sorted = w.every((x, i) => i === 0 || x.t >= w[i - 1].t);
  const shaped = w.length > 0 && w.every((x) => typeof x.t === 'number' && typeof x.d === 'number' && typeof x.w === 'string' && typeof x.conf === 'number');
  const inside = w.every((x) => x.t >= 0 && x.t <= j.durationSec + 2);
  const lean = w.every((x) => Object.keys(x).length === 4);
  ok(j.version === 1 && j.hash === row.hash && shaped && sorted && inside && lean
    && Math.abs(j.durationSec - lvl.durationSec) <= 2,
    row.title + ": version 1, the table's hash, sorted words inside the file, nothing spare");
}
ok(words > 20000, 'the eleven transcripts carry ' + words + ' words between them');

/* ---- 3. the loader -------------------------------------------------------- */
const first = index.rows[0];
const shape = (g) => !!g && g.version === 1 && Array.isArray(g.words) && g.words.length > 0;

forgetWords();
const byCloudId = await loadWords({ cloudId: first.cloudId.toUpperCase(), fetch: localFetch, indexUrl: INDEX_URL });
ok(shape(byCloudId) && byCloudId.hash === first.hash, 'the loader answers by cloudId, whatever case it is given');

forgetWords();
const byHash = await loadWords({ hash: first.hash, fetch: localFetch, indexUrl: INDEX_URL });
ok(shape(byHash) && byHash.words.length === byCloudId.words.length, 'and by hash when the id is not known');

forgetWords();
eq(await loadWords({ cloudId: 'not-a-track', fetch: localFetch, indexUrl: INDEX_URL }), null, 'a track with no transcript is null, not an error');
eq(await loadWords({ cloudId: first.cloudId, fetch: localFetch, indexUrl: INDEX_URL + '.missing' }), null, 'a missing table is null too');
eq(await loadWords({ fetch: localFetch, indexUrl: INDEX_URL }), null, 'and no key at all is null');

let hits = 0;
forgetWords();
const counted = async (url) => { hits++; return localFetch(url); };
await loadWords({ cloudId: first.cloudId, fetch: counted, indexUrl: INDEX_URL });
await loadWords({ cloudId: first.cloudId, fetch: counted, indexUrl: INDEX_URL });
eq(hits, 2, 'the same key is fetched once: one table, one transcript, and the second call is the same promise');

/* ---- 4. the detector over a real transcript ------------------------------- */
const opening = read('words/' + first.file);
const sleepSet = TRIGGER_SETS.find((s) => s.id === 'bambi-sleep');
const found = detect(opening, sleepSet, [], { durationSec: opening.durationSec });
ok(found.length >= 1, first.title + ': the detector hears "' + sleepSet.name + '" ' + found.length + ' times');
ok(found.every((m) => m.t >= 0 && m.t <= opening.durationSec), 'and every one of them sits inside the file');
const all = TRIGGER_SETS.map((s) => detect(opening, s, [], { durationSec: opening.durationSec }).length).reduce((a, b) => a + b, 0);
ok(all >= found.length, 'the whole catalogue over that file: ' + all + ' hits');

console.log(fails ? '\nwords-check: ' + fails + ' failed' : '\nwords-check: all good');
process.exit(fails ? 1 : 0);
