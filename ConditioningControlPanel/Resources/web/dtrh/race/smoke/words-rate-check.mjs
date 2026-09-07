/* ============================================================================
 * race/smoke/words-rate-check.mjs - the script on the road is drivable.
 *
 *   node race/smoke/words-rate-check.mjs   (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * Pure: no browser, no network, no audio. race/wordBubbles.js is the only place the
 * lane rule is written down and race/cues.js holds the lanes themselves, so the two
 * can be held against every transcript on the shelf without a renderer.
 *
 * What it holds, over ALL of race/words/:
 *   1. the rule table is the rule table (the numbers the owner picked)
 *   2. no five second window of any track asks for more than MAX_CHANGES lane
 *      changes: a lane step is 0.12 s of steering plus its easing, and a road that
 *      asks for more than one a second is a road nobody can read
 *   3. two consecutive bubbles are never closer than MERGE_SEC, because at 22 m/s
 *      the pop box is only poppable for 0.06 s and two bubbles that close are one
 *      pop the player cannot take twice
 *   4. no word bubble lands within HIT_GUARD_SEC of a trigger event: those seconds
 *      belong to the row of five (race/cues.js `case 'trigger'`)
 *   5. no phrase straddles a row, so a line is never cut in half by one
 *   6. one word a bubble, two at the most, never three
 *   7. every phrase sits in ONE lane, one of the five in cues.js LANE_X, and the
 *      lane never steps more than LANE_STEP_MAX between phrases
 *   8. the events the road is built from carry the word and the lane through
 *      chart.js normalizeEvents, and cues.js lays a tracked bubble of one on them
 *
 * It never prints a line of a transcript. Everything below is counted.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { phrasesFrom, wordEventsFrom, rateOf, WORD_RULE, LANE_MID } from '../wordBubbles.js';
import { triggerHits, triggersFromHits, captionWords, SET_BY_ID, TRIGGER_GAP } from '../cloudChart.js';
import { cueFor, LANE_X } from '../cues.js';
import { normalizeChart } from '../chart.js';
import { LANE_X_MAX, LANE_H, makeRng } from '../consts.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');

/** The most lane changes a five second window of road may ask a thumb for. */
const MAX_CHANGES = 6;
const WIN_SEC = 5;
const HERE = dirname(fileURLToPath(import.meta.url));
const WORDS = resolve(HERE, '../words');

/* ---- 1. the rule is what the owner picked -------------------------------- */
eq(WORD_RULE.MERGE_SEC, 0.12, 'two words closer than 0.12 s share one bubble');
eq(WORD_RULE.PHRASE_GAP_SEC, 0.3, 'a silence over 0.3 s ends the phrase');
eq(WORD_RULE.PHRASE_MAX_WORDS, 4, 'and so does a fourth bubble');
eq(WORD_RULE.HIT_GUARD_SEC, 0.4, 'no word bubble within 0.4 s of a trigger');
eq(WORD_RULE.LANE_STEP_MAX, 1, 'a phrase may step one lane, never two');
eq(LANE_X.length, 5, 'and there are five lanes to step between');
eq(LANE_MID, 2, 'a track opens in the middle one');

/* ---- the shelf ----------------------------------------------------------- */
const index = JSON.parse(readFileSync(resolve(WORDS, 'index.json'), 'utf8'));
const rows = Array.isArray(index.rows) ? index.rows : [];
ok(rows.length === 11, 'the shelf has all eleven transcripts on it (' + rows.length + ')');

const table = [];
let worstChangeWindow = 0, worstChangeTrack = '', tightest = Infinity, guardBreaks = 0, straddles = 0,
  laneJumps = 0, offLane = 0, tripled = 0, collisions = 0;

for (const row of rows) {
  const file = JSON.parse(readFileSync(resolve(WORDS, row.file), 'utf8'));
  const durationSec = Number(file.durationSec) || 0;
  // the trigger events the road will actually carry: the fingerprinted hits, thinned against each
  // other by TRIGGER_GAP exactly the way cloudChart.js lays them (no events, so only the triggers
  // come back). Those are the seconds the rows own.
  const triggers = triggersFromHits([], triggerHits(file, durationSec), SET_BY_ID);
  const caps = captionWords(file);
  const phrases = phrasesFrom(caps, triggers, { rng: makeRng(7) });
  const m = rateOf(phrases, WIN_SEC);
  collisions += phrases.collided || 0;

  if (m.worstChanges > worstChangeWindow) { worstChangeWindow = m.worstChanges; worstChangeTrack = row.title; }
  tightest = Math.min(tightest, m.minGap);

  // 4 + 5: the row owns its seconds, and no line is cut in half by one
  for (const p of phrases) {
    if (p.lane < 0 || p.lane >= LANE_X.length || p.x !== LANE_X[p.lane]) offLane++;
    for (const b of p.words) {
      if (b.w.trim().split(/\s+/).length > 2) tripled++;
      for (const h of triggers) {
        const t1 = h.t + Math.max(0, Number(h.dur) || 0);
        if (b.t > h.t - WORD_RULE.HIT_GUARD_SEC && b.t < t1 + WORD_RULE.HIT_GUARD_SEC) guardBreaks++;
      }
    }
    for (const h of triggers) if (p.t < h.t && p.words[p.words.length - 1].t > h.t) straddles++;
  }
  // 7: one step at a time
  for (let i = 1; i < phrases.length; i++) {
    if (Math.abs(phrases[i].lane - phrases[i - 1].lane) > WORD_RULE.LANE_STEP_MAX) laneJumps++;
  }
  table.push({ title: String(row.title || row.cloudId).slice(0, 24), durationSec, triggers: triggers.length,
    bubbles: m.bubbles, phrases: m.phrases, perPhrase: m.perPhrase, minGap: m.minGap,
    changes: m.changes, worstBubbles: m.worstBubbles, worstChanges: m.worstChanges, worstAt: m.worstAt });
}

console.log('\n  track                    |  dur | trig | bubbles | phrases | w/phr | min gap | lane chg | worst 5 s');
console.log('  -------------------------+------+------+---------+---------+-------+---------+----------+-----------');
for (const r of table) {
  console.log('  ' + r.title.padEnd(24) + ' | ' + String(Math.round(r.durationSec)).padStart(4)
    + ' | ' + String(r.triggers).padStart(4) + ' | ' + String(r.bubbles).padStart(7)
    + ' | ' + String(r.phrases).padStart(7) + ' | ' + r.perPhrase.toFixed(2).padStart(5)
    + ' | ' + r.minGap.toFixed(3).padStart(7) + ' | ' + String(r.changes).padStart(8)
    + ' | ' + String(r.worstBubbles).padStart(2) + ' bub, ' + r.worstChanges + ' chg @ ' + Math.round(r.worstAt) + 's');
}
console.log('');

ok(worstChangeWindow <= MAX_CHANGES, `no five second window on the shelf asks for more than ${MAX_CHANGES} lane changes (worst ${worstChangeWindow}, on ${worstChangeTrack})`);
ok(tightest >= WORD_RULE.MERGE_SEC - 1e-9, `two bubbles are never closer than ${WORD_RULE.MERGE_SEC} s (tightest ${tightest.toFixed(3)} s)`);
eq(guardBreaks, 0, 'not one word bubble lands inside a trigger row\'s seconds');
eq(straddles, 0, 'and not one phrase straddles a row');
eq(tripled, 0, 'no bubble carries three words');
eq(offLane, 0, 'every phrase sits in one of the five lanes');
eq(laneJumps, 0, 'and no phrase steps more than one lane from the last');
console.log('  --  ' + collisions + ' transcript collisions dropped across the shelf (three or more words on one second)');

/* ---- 8. the events survive the chart and cues.js lays them ---------------- */
const sample = JSON.parse(readFileSync(resolve(WORDS, rows[0].file), 'utf8'));
const sampleTriggers = triggersFromHits([], triggerHits(sample, sample.durationSec), SET_BY_ID);
const events = wordEventsFrom(captionWords(sample), sampleTriggers, { rng: makeRng(7) });
ok(events.length > 50, 'the opening track turns into ' + events.length + ' word events');
ok(events.every((e) => e.kind === 'word' && typeof e.w === 'string' && e.w && Number.isFinite(e.x)), 'each one carries its word and its lane');
const chart = normalizeChart({
  version: 1, binSec: 0.5, energy: [0.4, 0.4],
  acts: [{ kind: 'triggers', room: 'toybox', t0: 0, t1: sample.durationSec, name: 'triggers' }],
  events: events.concat(sampleTriggers),
  source: { name: 'rate', hash: 'rate', durationSec: sample.durationSec, sampleRate: 16000 },
  analysis: { energy: 'x', words: 'script-align-v1', lexicon: [], generatedAt: '', partial: false },
});
const kept = chart.events.filter((e) => e.kind === 'word');
eq(kept.length, events.length, 'chart.js keeps every word event');
ok(kept.every((e) => typeof e.w === 'string' && e.w), 'and the word on each of them');
ok(kept.every((e) => Number.isFinite(e.x) && Math.abs(e.x) <= LANE_X_MAX), 'and the lane, inside the drivable width');
const lanes = new Set(kept.map((e) => e.x));
ok([...lanes].every((x) => LANE_X.includes(x)), 'and it is one of the five lanes, not a rounded one');

const ctx = { rng: makeRng(3), intensity: 0.5, act: { kind: 'triggers', room: 'toybox' }, room: { id: 'toybox' }, triggerKinds: new Map(), lyrics: true };
let spawned = 0, tracked = 0, chromed = 0, wrongLane = 0;
for (const e of kept) {
  const cue = cueFor(e, ctx);
  if (!cue) continue;
  spawned++;
  if (cue.spawn.length !== 1) { wrongLane++; continue; }
  const sp = cue.spawn[0];
  if (sp.row) tracked++;
  if (sp.x !== e.x || sp.h !== LANE_H || sp.at !== 0 || sp.kindId !== 'treat' || sp.w !== e.w) wrongLane++;
  if (cue.word) chromed++;
}
eq(spawned, kept.length, 'cues.js has a bubble for every word event, and throws none of them out');
eq(tracked, kept.length, 'every one of them is tracked onto its own second (a row of one)');
eq(wrongLane, 0, 'every one is a plain treat, at lane height, on the word, in its phrase\'s lane, wearing its word');
eq(chromed, 0, 'and not one of them goes on the toast rail: the word is read off the bubble');

// the OLD word event, the structure-word treat generate.js lays on a road with no transcript, is
// untouched: it has no `w`, so it falls through to the branch it always had.
const plain = cueFor({ id: 'p', kind: 'word', t: 5, label: 'deeper', conf: 1 }, ctx);
ok(plain && plain.spawn.length === 1 && !plain.spawn[0].row && !plain.spawn[0].w, 'a structure word with no transcript behind it is the loose treat it always was');
ok(cueFor({ id: 'q', kind: 'word', t: 5, label: 'deeper', conf: 0.2 }, ctx) === null, 'and an unsure one is still nothing at all');
ok(TRIGGER_GAP > 0, 'the trigger thinning is still the one cloudChart.js owns (' + TRIGGER_GAP + ' s)');

console.log(fails ? '\nwords-rate-check: ' + fails + ' failed' : '\nwords-rate-check: all good');
process.exit(fails ? 1 : 0);
