/* ============================================================================
 * race/smoke/coverage-check.mjs - all the words she says get displayed.
 *
 *   node race/smoke/coverage-check.mjs   (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * Pure: no browser, no network, no audio. The whole shelf of eleven transcripts is
 * charted with race/cloudChart.js wordedRoad over a synthetic swell (the road's WORDS
 * are what is under test, and cloud-chart-check.mjs already charts real audio), then
 * driven through race/chart.js createScheduler, race/cues.js cueFor and a field that
 * carries race/bubbles.js's own refusals - the pool cap, the density gate, the dark
 * kind - so what this counts is what the player would actually have seen go down.
 *
 * WHY IT EXISTS. The owner, 2026-09-08: "Seems like the words sometimes just get
 * skipped, like we miss some bubbles, i see maybe 1 word out of a phrase... we should
 * recheck after generating the track that actually all the words gets displayed."
 * They were being skipped in the FIELD, not in the chart: `density` is the knob that
 * thins the road's dressing (0.6 on a release, 0 through a silence) and every word
 * bubble was being rolled against it one at a time, so a four word line came out as
 * one bubble sitting on its own. 22 percent of the shelf, 44 percent of the lines.
 *
 * What it holds:
 *   1. the FIELD lays every word bubble the chart asked for. Not most: every one.
 *      This is the regression guard on the fix - the density knob and the pool cap
 *      may never touch a bubble the file asked for.
 *   2. no line is thinned. A line of four goes down as four or the run is broken,
 *      and no line of two or more is reduced to a single bubble.
 *   3. and enough of what she SAYS reaches the road, per track and over the shelf:
 *      a word is on the road when it wears a bubble of its own or when it is inside
 *      a trigger row's own span (the row of five is a line of faces all saying that
 *      phrase). What is left is the guard margin, which is the pop box and not taste.
 *   4. the coverage line the host log gets is the same arithmetic, off the chart's
 *      own `analysis.coverage` stamp, and it survives normalizeChart.
 *
 * It never prints a line of a transcript. Everything below is counted.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { wordedRoad, PEAKS_PER_SEC } from '../cloudChart.js';
import { normalizeChart, createScheduler } from '../chart.js';
import { cueFor } from '../cues.js';
import { coverageOf, coverageLine } from '../wordBubbles.js';
import { KIND_BY_ID } from '../bubbleKinds.js';
import { KART_BASE_SPEED, makeRng } from '../consts.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');

/* ---- the thresholds, and why they are the numbers they are ---------------- *
 * SHELF/TRACK: what share of every word she says reaches the road. The rest is the
 * guard margin either side of a trigger row (race/wordBubbles.js WORD_RULE): a word
 * inside 0.2 s of the wall is inside the same pop box as the wall, so it could never
 * have been taken on its own. Measured over the shelf that is 3.3 percent, worst on
 * the opening track (155 words, 19 trigger rows, so the margins eat 9 percent of it),
 * which is why the per-track floor is lower than the shelf's.
 * FIELD: the field's own floor is 100 percent and always will be. A word the chart
 * laid and the field refused is the bug this file was written for. */
const SHELF_MIN = 0.96, TRACK_MIN = 0.90;
const STEP = 1 / 30;
const CAP = 160, MOBILE_CAP = 100;      // shared/quality.js bubbleCap, desktop and the mobile tier
const DROP_BEHIND = 12;                  // race/bubbles.js: a bubble this far behind the kart is freed

const HERE = dirname(fileURLToPath(import.meta.url));
const WORDS = resolve(HERE, '../words');
const rows = JSON.parse(readFileSync(resolve(WORDS, 'index.json'), 'utf8')).rows;
ok(rows.length === 11, 'the shelf has all eleven transcripts on it (' + rows.length + ')');

/** A curve shaped like a spoken track: a speaking level throughout, swelling every 40 s.
 *  The swell is what puts build/peak/release on the road, and `release` is the event that
 *  turns the density knob down - so the thing that used to eat the words is in every run below. */
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

/**
 * The road driven at KART_BASE_SPEED into a field that refuses exactly what
 * race/bubbles.js refuses. Returns what went down, per word event and per line.
 */
function drive(chart, cap) {
  const sched = createScheduler(chart);
  const rng = makeRng(7);
  const dur = chart.source.durationSec;
  const live = [];                       // { d, n }
  let liveCount = 0, density = 1, hold = 0, holdFrom = 0, kartD = 0;
  const held = [];                       // the visible half of a cue, fired on its own second
  const placed = new Set(), refused = new Map();
  const spawnRow = (n, script) => {
    if (liveCount + n > cap && !(script && n * 2 <= cap)) return 'cap';
    if (!script) {                       // the density knob is the road's dressing, never the file's words
      if (density <= 0) return 'density';
      if (density < 1 && Math.random() >= density) return 'density';
    }
    liveCount += n;
    return null;
  };
  for (let t = 0; t <= dur; t += STEP) {
    if (hold > 0 && (t >= hold || t < holdFrom)) { hold = 0; density = 1; }
    for (const due of sched.update(t, kartD, KART_BASE_SPEED)) {
      const e = due.event;
      const cue = cueFor(e, { intensity: 0.5, rng, act: { kind: 'triggers', room: 'toybox' },
        room: { id: 'toybox' }, triggerKinds: new Map(), lyrics: true, gold: () => false });
      if (!cue) continue;
      const row = cue.spawn.filter((sp) => sp.row);
      if (row.length) {
        const why = spawnRow(row.length, !!row[0].script);
        if (!why) live.push({ d: due.d, n: row.length });
        if (e.kind === 'word') {
          if (why) refused.set(e.id, why); else placed.add(e.id);
        }
      }
      for (const sp of cue.spawn) {
        if (sp.row) continue;
        if (liveCount >= cap && !sp.script) continue;
        if (KIND_BY_ID[sp.kindId] && KIND_BY_ID[sp.kindId].spawn === false) continue;
        liveCount++; live.push({ d: due.d, n: 1 });
      }
      held.push({ at: e.t, cue });
    }
    for (let i = held.length - 1; i >= 0; i--) {
      if (held[i].at > t) continue;
      const { cue, at } = held[i];
      if (cue.density != null) density = cue.density;
      if (cue.holdSec > 0) { holdFrom = at; hold = at + cue.holdSec; }
      held.splice(i, 1);
    }
    kartD += KART_BASE_SPEED * STEP;
    for (let i = live.length - 1; i >= 0; i--) if (live[i].d < kartD - DROP_BEHIND) { liveCount -= live[i].n; live.splice(i, 1); }
  }
  return { placed, refused };
}

/* ---- the shelf ----------------------------------------------------------- */
const table = [];
let shelfSaid = 0, shelfRoad = 0, shelfEvents = 0, shelfPlaced = 0, thinned = 0, emptied = 0, singled = 0;

for (const row of rows) {
  // read the way race/words.js hands a transcript to the road: the file, wearing the aligner stamp
  // off its index row, so cloudChart.js captionWords keeps every word of a script-aligned one.
  const file = { ...JSON.parse(readFileSync(resolve(WORDS, row.file), 'utf8')), engine: row.engine };
  const dur = Number(file.durationSec) || 0;
  const road = wordedRoad({ peaks: swell(dur), durationSec: dur, name: row.title, hash: row.hash, words: file });
  const chart = normalizeChart(road);
  const words = chart.events.filter((e) => e.kind === 'word' && e.w);
  const wordsIn = (e) => String(e.w).trim().split(/\s+/).length;
  const asked = words.reduce((n, e) => n + wordsIn(e), 0);

  const { placed, refused } = drive(chart, CAP);
  const got = words.filter((e) => placed.has(e.id)).reduce((n, e) => n + wordsIn(e), 0);

  // per LINE: a phrase goes down whole or the road is thinning it
  const line = new Map();
  for (const e of words) {
    const key = e.p == null ? 'e' + e.id : 'p' + e.p;
    const rec = line.get(key) || { want: 0, got: 0 };
    rec.want++; rec.got += placed.has(e.id) ? 1 : 0;
    line.set(key, rec);
  }
  let thin = 0, empty = 0, single = 0;
  for (const rec of line.values()) {
    if (rec.got < rec.want) thin++;
    if (rec.got === 0) empty++;
    if (rec.want >= 2 && rec.got === 1) single++;
  }
  thinned += thin; emptied += empty; singled += single;

  const cov = chart.analysis.coverage;
  shelfSaid += cov.said; shelfRoad += cov.onRoad; shelfEvents += asked; shelfPlaced += got;
  table.push({ title: String(row.title).slice(0, 24), cov, asked, got, lines: line.size, thin, empty, single,
    why: [...refused.values()] });
}

console.log('\n  track                    |  said | on road |  pct  | asked | placed | lines | thinned | emptied | 1 of many');
console.log('  -------------------------+-------+---------+-------+-------+--------+-------+---------+---------+----------');
for (const r of table) {
  console.log('  ' + r.title.padEnd(24) + ' | ' + String(r.cov.said).padStart(5) + ' | ' + String(r.cov.onRoad).padStart(7)
    + ' | ' + (100 * r.cov.pct).toFixed(1).padStart(5) + ' | ' + String(r.asked).padStart(5) + ' | ' + String(r.got).padStart(6)
    + ' | ' + String(r.lines).padStart(5) + ' | ' + String(r.thin).padStart(7) + ' | ' + String(r.empty).padStart(7)
    + ' | ' + String(r.single).padStart(8));
}
console.log('');

/* ---- 1. the field lays every word bubble the chart asked for --------------- */
eq(shelfPlaced, shelfEvents, 'the field lays every one of the shelf\'s word bubbles');
for (const r of table) {
  if (r.got !== r.asked) ok(false, r.title + ': the field refused ' + (r.asked - r.got) + ' words (' + [...new Set(r.why)].join(', ') + ')');
}
ok(table.every((r) => r.got === r.asked), 'and not one of the eleven tracks loses a bubble to the density knob or the pool');

/* ---- 2. no line is thinned ------------------------------------------------ */
eq(thinned, 0, 'no line of the road goes down short of a word');
eq(emptied, 0, 'not one line vanishes');
eq(singled, 0, 'and no line of two or more comes out as a single bubble');

/* ---- 2b. the mobile pool is smaller, and still lays the script ------------- */
{
  const file = { ...JSON.parse(readFileSync(resolve(WORDS, rows[2].file), 'utf8')), engine: rows[2].engine };
  const dur = Number(file.durationSec) || 0;
  const chart = normalizeChart(wordedRoad({ peaks: swell(dur), durationSec: dur, name: rows[2].title, hash: rows[2].hash, words: file }));
  const words = chart.events.filter((e) => e.kind === 'word' && e.w);
  const { placed } = drive(chart, MOBILE_CAP);
  eq(words.filter((e) => !placed.has(e.id)).length, 0, 'the busiest track lays every word on the mobile pool too (cap ' + MOBILE_CAP + ')');
}

/* ---- 3. enough of what she says reaches the road --------------------------- */
const worst = table.reduce((a, b) => (a.cov.pct <= b.cov.pct ? a : b));
ok(shelfRoad / shelfSaid >= SHELF_MIN,
  (100 * shelfRoad / shelfSaid).toFixed(1) + ' percent of every word on the shelf is on the road ('
  + shelfRoad + ' of ' + shelfSaid + ', floor ' + (100 * SHELF_MIN).toFixed(0) + ')');
ok(worst.cov.pct >= TRACK_MIN, 'and the thinnest track is ' + worst.title + ' at '
  + (100 * worst.cov.pct).toFixed(1) + ' percent (floor ' + (100 * TRACK_MIN).toFixed(0) + ')');
{
  const margin = table.reduce((n, r) => n + r.cov.margin, 0), piled = table.reduce((n, r) => n + r.cov.piled, 0);
  ok(margin + piled === shelfSaid - shelfRoad,
    'every word not on the road is accounted for: ' + margin + ' in a trigger row\'s margin, ' + piled + ' piled onto one instant');
}

/* ---- 4. the stamp, and the line the host log gets -------------------------- */
{
  const r = table[1];
  const again = coverageOf([], [], []);
  eq(again.said, 0, 'coverageOf of nothing is a clean zero, not a NaN');
  eq(again.pct, 1, 'and reads as full rather than empty');
  ok(typeof r.cov.said === 'number' && typeof r.cov.placed === 'number' && typeof r.cov.pct === 'number',
    'normalizeChart keeps analysis.coverage on the road (' + r.title + ')');
  const line = coverageLine(r.title, r.cov);
  ok(line.startsWith('[race-coverage] '), 'the host log line is stamped [race-coverage]');
  ok(line.includes('(' + r.cov.onRoad + '/' + r.cov.said + ')'), 'and carries the count the table above prints');
  console.log('  --  ' + line);
}

console.log(fails ? '\ncoverage-check: ' + fails + ' failed' : '\ncoverage-check: all good');
process.exit(fails ? 1 : 0);
