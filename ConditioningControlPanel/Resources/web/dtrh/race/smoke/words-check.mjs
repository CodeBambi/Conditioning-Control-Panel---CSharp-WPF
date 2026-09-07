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
// the aligner stamp: the race copy of a transcript carries none, so the row's rides along with it,
// and race/cloudChart.js isAligned() reads it to tell a doubtful SECOND from a doubtful word
ok(!!first.engine && byCloudId.engine === first.engine,
  'and hands the aligner stamp off the index row along with it (' + byCloudId.engine + ')');

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

/* ---- 5. the fingerprinted hits, in every file and through the loader ------- */
const setIds = new Set(TRIGGER_SETS.map((s) => s.id));
let fp = 0, kept = 0, filesWithHits = 0;
for (const row of index.rows) {
  const j = read('words/' + row.file);
  if (!Array.isArray(j.hits)) continue;
  filesWithHits++;
  const h = j.hits;
  const shaped = h.every((x) => typeof x.setId === 'string' && setIds.has(x.setId) && typeof x.t === 'number' && x.t >= 0 && x.t <= j.durationSec
    && typeof x.dur === 'number' && x.dur >= 0 && typeof x.score === 'number' && x.score >= 0 && x.score <= 1 && (x.src === 'fp' || x.src === 'words')
    && typeof x.conf === 'number' && x.conf >= 0 && x.conf <= 1 && Object.keys(x).length === 6);
  const sorted = h.every((x, i) => i === 0 || x.t >= h[i - 1].t);
  // the fingerprint refines the scan, it never invents: every hit sits within 0.6 s of a scan hit of its set
  const scan = [];
  for (const set of TRIGGER_SETS) for (const m of detect(j, set, [], { durationSec: j.durationSec })) scan.push({ setId: set.id, t: m.t });
  const refined = h.every((x) => scan.some((s) => s.setId === x.setId && Math.abs(s.t - x.t) <= 0.6));
  const whole = scan.every((s) => h.some((x) => x.setId === s.setId && Math.abs(s.t - x.t) <= 0.6));
  fp += h.filter((x) => x.src === 'fp').length; kept += h.filter((x) => x.src === 'words').length;
  ok(shaped && sorted && refined && whole, row.title + ': ' + h.length + ' hits, shaped, sorted, every one a scan hit refined and no scan hit lost');
}
eq(filesWithHits, index.rows.length, 'every transcript carries its fingerprinted hits');
ok(fp > 0 && kept >= 0, 'between them: ' + fp + ' hits placed by the fingerprint, ' + kept + ' kept at the aligner\'s second');
forgetWords();
const withHits = await loadWords({ cloudId: first.cloudId, fetch: localFetch, indexUrl: INDEX_URL });
ok(Array.isArray(withHits.hits) && withHits.hits.length === read('words/' + first.file).hits.length, 'the loader passes the hits through, every one');
ok(withHits.hits.every((x) => Object.keys(x).length === 6), 'in the shape cloudChart.js reads (setId, t, dur, score, src, conf)');
forgetWords();

console.log(fails ? '\nwords-check: ' + fails + ' failed' : '\nwords-check: all good');
process.exit(fails ? 1 : 0);
