/* ============================================================================
 * race/smoke/catalogue-check.mjs - the trigger catalogue, and the road it lays.
 *
 *   node race/smoke/catalogue-check.mjs   (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * Pure: no browser, no network, no audio. The transcripts are the eleven real
 * ones on race/words/; the curve under them is a synthetic swell, because what
 * is under test here is WHICH ROW the catalogue lays and never how loud the file
 * was when it laid it.
 *
 * What it holds:
 *   1. the catalogue itself: one id, one name, one preset, one theme per set
 *   2. PRECEDENCE: the rule is a real order, and it is the rule that decides the
 *      seconds two sets both claim
 *   3. the countdown mode finds the two countdowns the shelf actually contains
 *   4. the fingerprint merge: a set the words file was never fingerprinted for
 *      still reaches the road, and one it was keeps the truer second
 *   5. the road: how many rows, how many of them wear an effect, and no one kind
 *      swallowing a track
 *
 * It never prints a line of a transcript. Everything below is counted, never quoted.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { TRIGGER_SETS, SET_RANK, rankOf, laysRow, compareHits } from '../../chart/editor/triggerSets.js';
import { findMatches, COUNTDOWN } from '../../chart/maker/triggers.js';
import { KIND_BY_ID } from '../bubbleKinds.js';
import { THEME_BY_PRESET, themeFor, kindForPreset } from '../triggerTheme.js';
import { scanTriggers, triggerHits, wordedRoad, TRIGGER_GAP, FP_SNAP_SEC, PEAKS_PER_SEC } from '../cloudChart.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');

const RACE = resolve(fileURLToPath(import.meta.url), '../..');
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));
const index = read('words/index.json');

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
const loadWords = (row) => ({ ...read('words/' + row.file), engine: row.engine });
const durOf = (w) => Number(w.durationSec) || (w.source && Number(w.source.durationSec)) || 0;

/* ---- 1. the catalogue ----------------------------------------------------- */
{
  const ids = TRIGGER_SETS.map((s) => s.id), names = TRIGGER_SETS.map((s) => String(s.name).toLowerCase());
  eq(new Set(ids).size, ids.length, 'every set id is its own (' + ids.length + ' sets)');
  // the label a row wears IS the set name, and race/track.js maps a kind by that label:
  // two sets sharing one would hand the road one bubble for two different phrases.
  eq(new Set(names).size, names.length, 'and every set name is its own');
  ok(TRIGGER_SETS.every((s) => s.phrase && typeof s.phrase === 'string'), 'every set has a phrase');
  ok(TRIGGER_SETS.every((s) => /^#[0-9a-f]{6}$/i.test(String(s.color))), 'and a six digit hue');
  ok(TRIGGER_SETS.every((s) => ['exact', 'contains', 'regex', 'countdown'].includes(s.mode)), 'and a mode the matcher knows');
  ok(TRIGGER_SETS.every((s) => !!s.preset), 'EVERY SET HAS A PRESET');
  ok(TRIGGER_SETS.every((s) => !!THEME_BY_PRESET[s.preset]), 'AND EVERY PRESET HAS A THEME ROW');
  const bad = TRIGGER_SETS.filter((s) => { if (s.mode !== 'regex') return false; try { new RegExp(s.phrase, 'gi'); return false; } catch { return true; } });
  eq(bad.length, 0, 'every regex set compiles' + (bad.length ? ' (' + bad.map((s) => s.id).join(', ') + ')' : ''));
  // the eight the shelf never says are kept on purpose: they are for audio this shelf does not carry
  for (const id of ['bambi-freeze', 'bambi-reset', 'drip-drop', 'snap-forget', 'zap-cock-drain', 'primped', 'does-as-told', 'giggle-time'])
    ok(TRIGGER_SETS.some((s) => s.id === id), 'the catalogue still carries ' + id + ', which this shelf never says');
  // and the three said too often to be a row keep their id and lose only the road
  const accents = TRIGGER_SETS.filter((s) => !laysRow(s)).map((s) => s.id).sort();
  eq(accents.join(','), 'w-accept,w-relax,w-sleep', 'the three accent words are still sets, and lay no row');
  // a kind the theme table names may be one bubbleKinds.js has not landed yet, but then the row
  // MUST name a fallback that has, or the road would try to lay a bubble that does not exist.
  for (const [preset, row] of Object.entries(THEME_BY_PRESET)) {
    const live = KIND_BY_ID[row.kind];
    ok(!!live || (!!row.fallback && !!KIND_BY_ID[row.fallback]),
      preset + ' names a kind the road can lay (' + row.kind + (live ? '' : ' -> ' + row.fallback) + ')');
  }
  const kinds = Object.keys(THEME_BY_PRESET).map(kindForPreset);
  ok(kinds.every((k) => !!KIND_BY_ID[k] && KIND_BY_ID[k].spawn !== false), 'and kindForPreset only ever hands out one that spawns');
}

/* ---- 2. precedence -------------------------------------------------------- */
{
  // no two sets that lay a row may claim the same phrase outright: the rule below breaks ties
  // between sets that OVERLAP, and two sets with one phrase between them is not a tie, it is a
  // duplicate, and one of them would simply never be seen.
  const phrases = TRIGGER_SETS.filter(laysRow).map((s) => s.mode + ':' + s.phrase);
  eq(new Set(phrases).size, phrases.length, 'no two row sets claim the exact same phrase');
  eq(rankOf({ group: 'named' }), SET_RANK.named, 'a named trigger ranks 0');
  ok(rankOf({ group: 'named' }) < rankOf({ group: 'sequence' }), 'a named trigger outranks a sequence');
  ok(rankOf({ group: 'sequence' }) < rankOf({ group: 'words' }), 'a sequence outranks a word');
  ok(rankOf({ group: 'words' }) < rankOf({ group: 'custom' }), "and a word outranks an author's own set");
  eq(rankOf({ group: 'nonsense' }), SET_RANK.custom, 'a set in no group ranks last, not first');
  // THE ORDER IS AN ORDER. A comparator that reads the raw seconds after deciding two of them are
  // the same tenth is not transitive, and Array#sort hands back a list that is not in time order
  // for one - which is exactly what stopped TRIGGER_GAP thinning anything. Hold it here.
  const a = { t: 1.00, rank: 2, setId: 'b' }, b = { t: 1.04, rank: 0, setId: 'a' }, c = { t: 1.09, rank: 2, setId: 'c' };
  ok(compareHits(a, b) > 0 && compareHits(b, c) < 0 && compareHits(a, c) < 0, 'compareHits is transitive across a tenth boundary');
  eq(Math.sign(compareHits(a, b)), -Math.sign(compareHits(b, a)), 'and antisymmetric');
  // the two the survey called out by name
  const bimbo = { t: 10, rank: rankOf(TRIGGER_SETS.find((s) => s.id === 'bimbo-doll')), nw: 2, setId: 'bimbo-doll' };
  const chant = { t: 10, rank: rankOf(TRIGGER_SETS.find((s) => s.id === 'chant')), nw: 6, setId: 'chant' };
  ok(compareHits(bimbo, chant) < 0, 'bimbo doll beats the chant it makes, by rank and not by spelling');
  const forCock = { t: 20, rank: rankOf(TRIGGER_SETS.find((s) => s.id === 'drop-for-cock')), nw: 3, setId: 'drop-for-cock' };
  const drop = { t: 20, rank: rankOf(TRIGGER_SETS.find((s) => s.id === 'w-drop')), nw: 1, setId: 'w-drop' };
  ok(compareHits(forCock, drop) < 0, 'drop for cock beats the drop inside it');
  // and a tie on rank goes to the longer match, not the alphabet
  ok(compareHits({ t: 5, rank: 2, nw: 3, setId: 'z' }, { t: 5, rank: 2, nw: 1, setId: 'a' }) < 0, 'a longer match beats a shorter one of the same rank');
}

/* ---- 3. the countdown mode ------------------------------------------------ */
{
  const fake = (ws) => ({ words: ws.map((w, i) => ({ w, t: i * 2, d: 0.4 })) });
  eq(findMatches(fake(['five', 'and', 'four', 'now', 'three', 'good']), '', 'countdown').length, 1, 'five four three with words between is one countdown');
  eq(findMatches(fake(['five', 'four']), '', 'countdown').length, 0, 'two numbers is not a countdown');
  eq(findMatches(fake(['one', 'two', 'three', 'four']), '', 'countdown').length, 0, 'counting up is not a countdown');
  eq(findMatches(fake(['ten', 'nine', 'eight']), '', 'countdown').length, 0, 'and nothing above five starts one');
  const far = { words: [{ w: 'five', t: 0, d: 0.4 }, { w: 'four', t: 10, d: 0.4 }, { w: 'three', t: 200, d: 0.4 }] };
  eq(findMatches(far, '', 'countdown').length, 0, 'a number ' + COUNTDOWN.MAX_GAP_SEC + ' s later is a new count, not this one');
  const one = findMatches(fake(['five', 'x', 'four', 'x', 'three', 'x', 'two']), '', 'countdown');
  eq(one.length, 1, 'one countdown is ONE span, not one per number');
  eq(one[0].i0, one[0].i1, 'and it sits on the first number, so it never fences off the words after it');
  // over the real shelf: two, and only two
  const found = [];
  for (const row of index.rows) {
    const w = loadWords(row);
    for (const m of findMatches(w, '', 'countdown')) found.push({ title: row.title, t: Math.round(m.t) });
  }
  eq(found.length, 2, 'the shelf holds exactly two countdowns' + (found.length ? ' (' + found.map((f) => f.title + ' @' + f.t + 's').join(', ') + ')' : ''));
  ok(found.some((f) => f.title === 'Bambi Named and Drained' && Math.abs(f.t - 608) < 30), 'one in Bambi Named and Drained, around 10:08');
  ok(found.some((f) => f.title === 'Bambi Cockslut'), 'and one in Bambi Cockslut');
  // and the old regex, the one the catalogue used to carry, finds neither of them
  const OLD = '\\b(?:ten|nine|eight|seven|six|five|four|three|two|one|10|9|8|7|6|5|4|3|2|1)\\b(?:[ ,]+\\b(?:nine|eight|seven|six|five|four|three|two|one|zero|9|8|7|6|5|4|3|2|1|0)\\b){2,}';
  let old = 0;
  for (const row of index.rows) old += findMatches(loadWords(row), OLD, 'regex').length;
  eq(old, 0, 'and the regex it replaced found none of them, which is why the mode exists');
}

/* ---- 4. the fingerprint merge --------------------------------------------- */
{
  const row = index.rows.find((r) => r.title === 'Bambi Body Lock');
  const words = loadWords(row), dur = durOf(words);
  ok(Array.isArray(words.hits) && words.hits.length > 0, row.title + ' carries fingerprinted hits (' + words.hits.length + ')');
  const fpSets = new Set(words.hits.map((h) => h.setId));
  const hits = triggerHits(words, dur);
  const scan = scanTriggers({ ...words, hits: undefined }, dur);
  eq(hits.length, scan.length, 'the merge lays exactly the scan\'s roll call, no more and no less');
  const fresh = hits.filter((h) => !fpSets.has(h.setId));
  ok(fresh.length > 0, 'and ' + fresh.length + ' of them are sets the fingerprint never saw, which used to reach the road never');
  // a fingerprinted set keeps the truer second: every hit of one lands on a hit in the file
  const kept = hits.filter((h) => fpSets.has(h.setId));
  const snapped = kept.filter((h) => words.hits.some((f) => f.setId === h.setId && Math.abs(f.t - h.t) < 1e-6));
  ok(snapped.length > kept.length * 0.6, 'and ' + snapped.length + ' of ' + kept.length + ' fingerprinted rows sit on the second the audio gave them');
  ok(hits.every((h) => Math.abs(h.t - (scan.find((s) => s.id === h.id) || h).t) <= FP_SNAP_SEC + 1e-6),
    'no row was moved further than the snap window (' + FP_SNAP_SEC + ' s)');
  const bare = { ...words }; delete bare.hits;
  ok(JSON.stringify(triggerHits(bare, dur)) === JSON.stringify(scan), 'a words file with no hits is the live scan, exactly');
  eq(triggerHits({ ...bare, hits: [{ setId: 'not-a-set', t: 1, dur: 1, score: 1, src: 'fp' }] }, dur).length, scan.length,
    'and hits that are all for sets the catalogue lost change nothing');
}

/* ---- 5. the road ---------------------------------------------------------- */
/** The general share cap, and the roof for the three tracks that are genuinely one note. */
const SHARE_CAP = 0.40, ONE_NOTE_CAP = 0.55;
const ONE_NOTE = new Set(['Bambi IQ Lock', 'Bambi Body Lock', 'Bambi Uniformed']);
/** Under this many rows a share is not a share, it is a handful. */
const SHARE_MIN_ROWS = 20;
/** What the survey asked for, as floors: the road is nearly all effect now. */
const SHELF_ROWS_MIN = 900, SHELF_EFFECT_MIN = 850;

const table = [];
for (const row of index.rows) {
  const words = loadWords(row), dur = durOf(words);
  const road = wordedRoad({ peaks: swell(dur), durationSec: dur, name: row.title, hash: row.hash, words });
  const trig = road.events.filter((e) => e.kind === 'trigger');
  const by = {};
  let effect = 0, tight = 0;
  for (let i = 0; i < trig.length; i++) {
    const k = (themeFor(trig[i]) || {}).kind || 'treat';
    by[k] = (by[k] || 0) + 1;
    if (KIND_BY_ID[k] && KIND_BY_ID[k].kind === 'effect') effect++;
    if (i && trig[i].t - trig[i - 1].t < TRIGGER_GAP - 1e-6) tight++;
  }
  table.push({ title: row.title, rows: trig.length, effect, by, tight, sets: new Set(trig.map((e) => e.setId)).size });
}
const shelfRows = table.reduce((a, r) => a + r.rows, 0), shelfEffect = table.reduce((a, r) => a + r.effect, 0);
console.log('  --  ' + table.map((r) => r.title.replace('Bambi ', '') + ' ' + r.effect + '/' + r.rows).join(', '));
ok(shelfRows >= SHELF_ROWS_MIN, 'the shelf lays ' + shelfRows + ' trigger rows (floor ' + SHELF_ROWS_MIN + ')');
ok(shelfEffect >= SHELF_EFFECT_MIN, 'and ' + shelfEffect + ' of them wear an effect (floor ' + SHELF_EFFECT_MIN + ')');
eq(table.reduce((a, r) => a + r.tight, 0), 0, 'no two rows land inside TRIGGER_GAP of each other');
ok(table.every((r) => r.sets >= 4), 'every track hears at least four different sets');
{
  const bi = table.find((r) => r.title === 'Bubble Induction');
  ok(bi && bi.effect >= 40, 'Bubble Induction wears ' + (bi ? bi.effect : 0) + ' effect rows (floor 40, it had 2)');
}
for (const r of table) {
  if (r.rows < SHARE_MIN_ROWS) continue;
  const cap = ONE_NOTE.has(r.title) ? ONE_NOTE_CAP : SHARE_CAP;
  const top = Math.max(...Object.values(r.by)), kind = Object.keys(r.by).find((k) => r.by[k] === top);
  ok(top / r.rows <= cap, r.title + ': no kind past ' + (100 * cap).toFixed(0) + ' percent (' + kind + ' ' + (100 * top / r.rows).toFixed(0) + ')');
}

console.log('');
console.log(fails ? 'catalogue-check: ' + fails + ' failed' : 'catalogue-check: all good');
process.exit(fails ? 1 : 0);
