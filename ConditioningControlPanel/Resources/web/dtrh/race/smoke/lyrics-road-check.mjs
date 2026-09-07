/* ============================================================================
 * race/smoke/lyrics-road-check.mjs - the road a track with a transcript gets.
 *
 *   node race/smoke/lyrics-road-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * Pure: no browser, no network, no audio. The words are the real transcript this
 * branch ships for the opening track; the curve under them is a synthetic swell
 * this file writes, because the road's WORDS are what is under test here and
 * cloud-chart-check.mjs already charts real audio.
 *
 * What it holds:
 *   1. the theme table: a row per preset the catalogue uses, every kind a real
 *      bubble, no darkened kind ever named
 *   2. the detector over the real transcript, and the hits it hands generate.js
 *   3. the worded road: trigger events with a setId and a cue, a caption track,
 *      analysis.words, and acts that are more than one room
 *   4. what normalizeChart keeps of it: `words`, `setId`, `cue`
 *   5. the wordless road is untouched, and a v1 cached road cannot be served
 *
 * It never prints a line of a transcript. Everything below is counted, never quoted.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { BUBBLE_KINDS, KIND_BY_ID } from '../bubbleKinds.js';
import { normalizeChart } from '../chart.js';
import { THEME_BY_PRESET, PRESETS_IN_USE, kindForPreset, themeFor, FALLBACK_KIND } from '../triggerTheme.js';
import { GENERATOR_ID, WORD_BIN_SEC, BIN_SEC, CAPTION_CONF, captionWords, scanTriggers, triggerHits, wordedRoad, roadFromPeaks, PEAKS_PER_SEC } from '../cloudChart.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');

const RACE = resolve(fileURLToPath(import.meta.url), '../..');
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));
const index = read('words/index.json');
const ROW = index.rows[0];                                  // the opening track: short, and it says the phrase
const WORDS = read('words/' + ROW.file);
const DUR = WORDS.durationSec;

/** A curve shaped like a spoken track: a speaking level throughout, swelling every 40 s. */
function swell(durationSec, perSec = PEAKS_PER_SEC) {
  const n = Math.ceil(durationSec * perSec);
  const peaks = new Float32Array(n * 2);
  for (let i = 0; i < n; i++) {
    const t = i / perSec;
    const a = 0.22 + 0.5 * Math.max(0, Math.sin((t / 40) * Math.PI * 2)) ** 2;
    peaks[i * 2] = -a; peaks[i * 2 + 1] = a;
  }
  return peaks;
}

/* ---- 1. the theme table --------------------------------------------------- */
const rows = Object.entries(THEME_BY_PRESET);
ok(rows.length >= 13, 'the table has a row for every preset the brief named (' + rows.length + ')');
ok(PRESETS_IN_USE.every((p) => !!THEME_BY_PRESET[p]), 'every preset the catalogue uses has a row');
ok(rows.every(([, r]) => !!KIND_BY_ID[r.kind]), 'every row names a real bubble kind');
ok(rows.every(([, r]) => r.theme && r.color && r.ink && r.note), 'every row has a theme, two colours and a note');
eq(new Set(rows.map(([, r]) => r.theme)).size, rows.length, 'no two rows share a plate theme');
const dark = BUBBLE_KINDS.filter((k) => k.spawn === false).map((k) => k.id);
ok(dark.length > 0 && rows.some(([, r]) => dark.includes(r.kind)), 'the table does point a row at a darkened kind (' + dark.join(', ') + ')');
ok(rows.every(([p]) => !dark.includes(kindForPreset(p))), 'and kindForPreset never hands one out');
eq(kindForPreset('video'), FALLBACK_KIND, 'the darkened row falls back to the flash bubble');
eq(kindForPreset('not-a-preset'), null, 'a preset nobody wrote a row for is null, not a guess');
eq(themeFor({ kind: 'trigger', label: 'bambi sleep', setId: 'bambi-sleep', cue: 'blackout' }).theme, 'ink', 'themeFor reads the cue');
eq(themeFor({ kind: 'trigger', label: 'good girl', setId: 'good-girl' }).theme, 'blink', 'and falls back to the set when the cue is gone');
eq(themeFor({ kind: 'trigger', label: 'nothing anyone said', cue: 'nope' }).theme, 'mark', 'and to the mark row when it knows neither');
eq(themeFor(null), null, 'themeFor of nothing is null');

/* ---- 2. the detector over the real transcript ----------------------------- */
const hits = scanTriggers(WORDS, DUR);
ok(hits.length > 0, ROW.title + ': the catalogue lands ' + hits.length + ' hits on the transcript');
ok(hits.every((h, i) => i === 0 || h.t >= hits[i - 1].t), 'the hits come out sorted by time');
ok(hits.every((h) => h.setId && h.id && h.t >= 0 && h.t <= DUR), 'every hit names its set and sits inside the file');
ok(hits.some((h) => h.setId === 'bambi-sleep'), 'and the opening track really does say the phrase the road is built on');

/* ---- 2b. the fingerprinted seconds win over the scan ------------------------ */
{
  const bare = { ...WORDS }; delete bare.hits;
  const scanned = scanTriggers(bare, DUR);
  eq(JSON.stringify(triggerHits(bare, DUR)), JSON.stringify(scanned), 'a words file with no hits gets the live scan, exactly');
  const fp = { ...bare, hits: [{ setId: 'bambi-sleep', t: 10, dur: 0.6, score: 0.9, src: 'fp', conf: 0.7 }, { setId: 'good-girl', t: 20.5, dur: 0.5, score: 0.5, src: 'words', conf: 1 },
    { setId: 'bambi-sleep', t: 30, dur: 0.6, score: 0.95, src: 'fp' }, { setId: 'not-a-set', t: 40, dur: 1, score: 1, src: 'fp' }] };
  const got = triggerHits(fp, DUR);
  eq(got.length, 3, 'a words file with hits gets its hits, and a set the catalogue no longer has is dropped');
  eq(got.map((h) => h.t).join(','), '10,20.5,30', 'in time order, at the fingerprinted seconds, not the scan\'s');
  eq(got.map((h) => h.id).join(','), 'h:bambi-sleep:0,h:good-girl:0,h:bambi-sleep:1', 'numbered per set in the scan\'s own shape');
  eq(got[0].conf, 0.7, 'the phrase keeps the confidence the transcript gave it');
  eq(got[2].conf, 1, 'and a hit with no conf is sure');
  eq(triggerHits({ ...bare, hits: [{ setId: 'not-a-set', t: 1, dur: 1, score: 1, src: 'fp' }] }, DUR).length, scanned.length, 'hits that are all for unknown sets fall back to the scan');
  const road2 = wordedRoad({ peaks: swell(DUR), durationSec: DUR, name: ROW.title, hash: ROW.hash, words: fp });
  const tr2 = road2.events.filter((e) => e.kind === 'trigger');
  ok(tr2.some((e) => e.setId === 'bambi-sleep' && e.t === 10) && tr2.some((e) => e.setId === 'bambi-sleep' && e.t === 30), 'and the worded road lays its triggers at the fingerprinted seconds');
  ok(Array.isArray(WORDS.hits) && WORDS.hits.length > 0, ROW.title + ' ships with ' + (WORDS.hits || []).length + ' fingerprinted hits');
  ok(WORDS.hits.every((h) => scanned.some((s) => s.setId === h.setId && Math.abs(s.t - h.t) <= 0.6)), 'every shipped hit is a hit the scan hears too, within 0.6 s (the fingerprint refines, it does not invent)');
}

/* ---- 3. the worded road --------------------------------------------------- */
const road = wordedRoad({ peaks: swell(DUR), durationSec: DUR, name: ROW.title, hash: ROW.hash, words: WORDS });
const kinds = {};
for (const e of road.events) kinds[e.kind] = (kinds[e.kind] || 0) + 1;
console.log('\n  road: ' + JSON.stringify({ n: road.events.length, kinds, acts: road.acts.map((a) => a.kind), words: road.words.length }) + '\n');

eq(road.binSec, WORD_BIN_SEC, 'the worded road bins at the maker default, not at the wordless quarter second');
eq(road.analysis.words, 'script-align-v1', 'the road says it was built on a transcript');
ok(road.words.length > 100, 'the caption track is the file talking (' + road.words.length + ' words)');
ok(road.words.every((w) => Object.keys(w).length === 3 && typeof w.w === 'string'), 'and it is t, d and w, and nothing else');
ok(road.words.every((w, i) => i === 0 || w.t >= road.words[i - 1].t), 'the caption track is sorted');
eq(captionWords({ words: [{ t: 1, d: 0.2, w: 'x', conf: CAPTION_CONF - 0.01 }] }).length, 0, 'a word the transcript only guessed at is not a caption');
eq(captionWords(null).length, 0, 'and no transcript is an empty caption track, not a throw');

const triggers = road.events.filter((e) => e.kind === 'trigger');
ok(triggers.length > 0, 'the road has trigger events on it (' + triggers.length + '), which the wordless one never had');
ok(triggers.every((e) => e.setId && e.cue && e.weight === 1), 'every trigger carries its setId and its cue');
ok(triggers.every((e) => !!THEME_BY_PRESET[e.cue]), 'every cue is a row in the theme table');
const sleeps = triggers.filter((e) => e.setId === 'bambi-sleep');
ok(sleeps.length >= 1, "'bambi sleep' is on the road " + sleeps.length + ' times');
ok(sleeps.every((e) => e.cue === 'blackout'), 'and it carries the blackout cue');
ok(road.analysis.lexicon.length > 0 && road.analysis.lexicon.every((w) => triggers.some((e) => e.label === w)),
  'the lexicon is the phrases actually heard (' + road.analysis.lexicon.length + ')');
ok(road.acts.length > 1, 'the file is more than one room (' + road.acts.map((a) => a.kind).join(' -> ') + ')');
ok(road.events.some((e) => e.kind === 'peak'), 'the curve is still read: the road has its build, peak and release');
ok(road.events.every((e, i) => i === 0 || e.t >= road.events[i - 1].t), 'and the whole road is sorted');
eq(new Set(road.events.map((e) => e.id)).size, road.events.length, 'every event has its own id');

/* ---- 4. what the chart keeps of it ---------------------------------------- */
const chart = normalizeChart(road);
eq(chart.words.length, road.words.length, 'normalizeChart keeps the caption track');
ok(Object.isFrozen(chart.words), 'and freezes it, so a run cannot scribble on it');
const kept = chart.events.filter((e) => e.kind === 'trigger');
eq(kept.length, triggers.length, 'it keeps every trigger event');
ok(kept.every((e) => e.setId && e.cue), 'with the setId and the cue still on them');
eq(normalizeChart({ ...road, words: 'not an array' }).words.length, 0, 'a malformed caption track is an empty one, not a throw');
eq(normalizeChart({ ...road, words: undefined }).words.length, 0, 'and no caption track at all is the same');
const long = normalizeChart({ ...road, events: [{ kind: 'trigger', t: 1, label: 'x', setId: 'y'.repeat(80) }] });
eq(long.events[0].setId.length, 40, 'a setId is cut to forty characters');

/* ---- 5. the wordless road, and the cache --------------------------------- */
const plain = roadFromPeaks({ peaks: swell(DUR), durationSec: DUR, name: ROW.title, hash: ROW.hash });
eq(plain.analysis.words, 'none', 'a track with no transcript still gets the road it always got');
eq(plain.binSec, BIN_SEC, 'at the quarter second bin it was tuned to');
ok(!plain.events.some((e) => e.kind === 'trigger'), 'and it has no triggers on it, because nobody heard one');
eq(normalizeChart(plain).words.length, 0, 'and no caption track');
eq(GENERATOR_ID, 'web-road-v4', 'the cache key moved again, so a v3 road (a handful of loose treats where the new one carries the whole script) can never be served');

console.log(fails ? '\nlyrics-road-check: ' + fails + ' failed' : '\nlyrics-road-check: all good');
process.exit(fails ? 1 : 0);
