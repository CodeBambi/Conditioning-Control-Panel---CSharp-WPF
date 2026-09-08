/* ============================================================================
 * race/smoke/rows-check.mjs - the row a sure trigger cuts across the road.
 *
 *   node race/smoke/rows-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * Pure: no browser, no network, no audio. cues.js is the only place the row's
 * geometry is written down and consts.js is the only place the pop box is, so the
 * two can be held against each other here without a renderer.
 *
 * What it holds:
 *   1. the row is a line across the WHOLE road, one kind, one depth, no gap the
 *      pop box could be threaded through, and there is no x the kart may sit at
 *      where the row misses it. That is the owner's ask: a trigger is not a thing
 *      you steer around.
 *   2. the row wears the preset's own bubble, and a treats or mark preset is a
 *      row of treats rather than an effect
 *   2b. the flash bubble is dark and the WORD carries the flash instead: half the
 *      word pops, off the run's seeded rng, one flash per 250 ms, and the one
 *      preset that meant the flash pours a real one through THE MIX
 *   3. a trigger the spotter only half heard is the single treat it always was
 *   4. one event is one credit however many of its bubbles were popped
 *   5. a worded track halves the peak rain; nothing else on the road moves
 *   7. and a WORD BUBBLE (a row of one, race/cues.js `case 'word'`) lands on its
 *      own word the same way, driven off the real opening transcript through the
 *      opening ramp and a boost: worst inside 0.15 s, median inside 0.08 s
 *   6. the row lands ON the word: driven through race/sync.js with a simulated
 *      kart whose speed changes inside the lookahead, the row is under the kart
 *      within 0.15 s of event.t, and the visible half of the cue fires on the
 *      second, never at the scheduler's 2.5 s early handover
 *   8. the plate flies on the POP or on the second, whichever the player reaches
 *      first, and only ever once
 *   9. the ladder counts LINES: a rung per phrase taken whole, no rung for a
 *      single word, no release for a word driven past, and the quiet holds it
 *
 * It never prints a line of a transcript. Everything below is counted.
 * ==========================================================================*/

import { cueFor, ROW_X, ROW_MAX_GAP, wordFlash, WORD_FLASH, WORD_FLASH_CHANCE, WORD_FLASH_GAP_MS } from '../cues.js';
import { createScheduler, normalizeChart } from '../chart.js';
import { KART_X_MAX, LANE_X_MAX, POP_HIT_X, POP_HIT_D, LANE_H, COMBO_HOLD_SEC, KART_BASE_SPEED, OPEN_PACE, makeRng } from '../consts.js';
import { createScore } from '../score.js';
import { THEME_BY_PRESET, kindForPreset } from '../triggerTheme.js';
import { KIND_BY_ID } from '../bubbleKinds.js';
import { createCueSync, CUE_AHEAD_SEC, LATE_SEC } from '../sync.js';
import { wordEventsFrom } from '../wordBubbles.js';
import { triggerHits, triggersFromHits, captionWords, SET_BY_ID } from '../cloudChart.js';
import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));

/** A transcript as the ROAD sees it: race/words.js hands the index row's aligner stamp along with
 *  the file, and race/cloudChart.js keeps every word of a script-aligned one, whatever its `conf`. */
const WORDS_ROWS = JSON.parse(readFileSync(resolve(HERE, '../words/index.json'), 'utf8')).rows;
const wordsFile = (row) => ({ ...JSON.parse(readFileSync(resolve(HERE, '../words/' + row.file), 'utf8')), engine: row.engine });
/** The opening track: short, and the one the sync numbers have always been read off. */
const openingWords = () => wordsFile(WORDS_ROWS[0]);

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const near = (a, b, eps = 1e-9) => Math.abs(a - b) <= eps;

/** A ctx the way run.js builds one, with the trigger map track.js would have dealt. */
const ctxFor = (kinds, extra = {}) => ({
  rng: makeRng(7), intensity: 0.5, act: { kind: 'triggers', room: 'toybox' }, room: { id: 'toybox' },
  triggerKinds: new Map(Object.entries(kinds || {})), ...extra,
});

/* ---- 1. the geometry ----------------------------------------------------- */
const sure = cueFor({ kind: 'trigger', t: 10, label: 'a phrase', conf: 1, cue: 'blackout' },
  ctxFor({ 'a phrase': kindForPreset('blackout') }));
ok(!!sure, 'a sure trigger is a cue');
const row = sure.spawn.filter((s) => s.row);
eq(row.length, sure.spawn.length, 'every spawn a sure trigger makes belongs to the row');
eq(row.length, ROW_X.length, 'the row is the width cues.js worked out');
ok(row.length >= 3, 'and a row is at least three bubbles wide (' + row.length + ')');
ok(row.every((s) => s.at === 0), 'all of them land on the spoken word, not behind it');
ok(row.every((s) => s.placement === 'lane'), 'all of them sit on the road');
ok(row.every((s) => s.h === LANE_H), 'all of them at lane height, so none can be jumped over');
eq(new Set(row.map((s) => s.kindId)).size, 1, 'the row is one kind of bubble, not a mixture');

const xs = row.map((s) => s.x).sort((a, b) => a - b);
ok(near(xs[0], -LANE_X_MAX), 'the row starts at the far left of the drivable width (' + xs[0].toFixed(3) + ')');
ok(near(xs[xs.length - 1], LANE_X_MAX), 'and ends at the far right (' + xs[xs.length - 1].toFixed(3) + ')');
let widest = 0;
for (let i = 1; i < xs.length; i++) widest = Math.max(widest, xs[i] - xs[i - 1]);
ok(widest <= 2 * POP_HIT_X, 'no gap in it is wider than the pop box (' + widest.toFixed(3) + ' m vs ' + (2 * POP_HIT_X).toFixed(3) + ' m)');
ok(widest <= ROW_MAX_GAP, 'and it keeps the ten percent cues.js holds back (' + ROW_MAX_GAP.toFixed(3) + ' m)');
// the real question: is there anywhere the kart may legally sit where none of them is in reach?
let worst = 0, worstX = 0;
for (let i = 0; i <= 2000; i++) {
  const kx = -KART_X_MAX + (i * KART_X_MAX * 2) / 2000;
  let d = Infinity;
  for (const x of xs) d = Math.min(d, Math.abs(x - kx));
  if (d > worst) { worst = d; worstX = kx; }
}
ok(worst < POP_HIT_X, 'there is no x the kart may steer to where the row misses it (worst ' + worst.toFixed(3)
  + ' m at x ' + worstX.toFixed(2) + ', box ' + POP_HIT_X + ' m)');
console.log('  --  the row: ' + ROW_X.length + ' bubbles, ' + ((LANE_X_MAX * 2) / (ROW_X.length - 1)).toFixed(3)
  + ' m apart, across ' + (LANE_X_MAX * 2).toFixed(2) + ' m of road');

/* ---- 2. the row wears the preset ---------------------------------------- */
let dressed = true;
for (const preset of Object.keys(THEME_BY_PRESET)) {
  const kind = kindForPreset(preset);
  const c = cueFor({ kind: 'trigger', t: 4, label: 'a phrase', conf: 0.9, cue: preset }, ctxFor({ 'a phrase': kind }));
  const r = c.spawn.filter((s) => s.row);
  if (r.length !== ROW_X.length || r.some((s) => s.kindId !== kind) || KIND_BY_ID[kind].spawn === false) {
    ok(false, 'the ' + preset + ' row is a full row of the ' + kind + ' bubble');
    dressed = false;
    break;
  }
}
if (dressed) ok(true, 'every preset in the table lays a full row of its own bubble, and none of them is darkened');
for (const preset of ['treats', 'mark']) {
  const c = cueFor({ kind: 'trigger', t: 4, label: 'a phrase', conf: 0.9, cue: preset }, ctxFor({ 'a phrase': kindForPreset(preset) }));
  eq(c.spawn.filter((s) => s.row && s.kindId === 'treat').length, ROW_X.length, 'a ' + preset + ' trigger is a row of treats, not an effect');
}

/* ---- 2b. THE FLASH: the bubble is dark, the word carries it -------------- */
// The flash bubble went dark on 2026-09-08 (bubbleKinds.js). Two things have to hold after it:
// nothing may dress a row as one, and the preset that MEANT the flash still fires a real one.
eq(KIND_BY_ID.flash.spawn, false, 'the flash bubble is darkened');
ok(Object.keys(THEME_BY_PRESET).every((p) => kindForPreset(p) !== 'flash'), 'and no preset puts one on the road');
const pulse = cueFor({ kind: 'trigger', t: 4, label: 'zap cock drain', conf: 0.9, cue: 'flash-pulse' },
  ctxFor({ 'zap cock drain': kindForPreset('flash-pulse') }));
eq(pulse.spawn.filter((s) => s.row && s.kindId === 'treat').length, ROW_X.length, 'the flash-pulse row is a full line of plain word faces');
eq(pulse.mix, 'flash', 'and its beat pours a real flash through THE MIX, so the strobe recipes keep a door');
eq(cueFor({ kind: 'trigger', t: 4, label: 'good girl', conf: 0.9, cue: 'pink-blink' }, ctxFor({ 'good girl': 'pink' })).mix, null,
  'no other trigger row pours one');
// the odds themselves: half the words the player takes, off the run's own seeded rng, and two
// words taken inside the gap share one flash rather than double-flashing.
const frng = makeRng(0x51ede5);
const N = 20000;
let fired = 0;
for (let i = 0; i < N; i++) if (wordFlash(frng, WORD_FLASH_GAP_MS)) fired++;
ok(Math.abs(fired / N - WORD_FLASH_CHANCE) < 0.02, 'a word pop flashes ' + Math.round((fired / N) * 100) + '% of the time (want ' + Math.round(WORD_FLASH_CHANCE * 100) + '%)');
let capped = 0;
for (let i = 0; i < 500; i++) if (wordFlash(frng, WORD_FLASH_GAP_MS - 1)) capped++;
eq(capped, 0, 'and a word taken inside ' + WORD_FLASH_GAP_MS + ' ms of the last flash takes none');
const seedA = makeRng(7), seedB = makeRng(7);
ok(Array.from({ length: 200 }, () => wordFlash(seedA, 999)).join() === Array.from({ length: 200 }, () => wordFlash(seedB, 999)).join(),
  'one seed, one sequence: a replay of a run flashes on the same words');
ok(WORD_FLASH.durationMult < 0.3 && WORD_FLASH.strength < 30, 'and what it hands payloadFx is a blink, not the old flash bubble');

/* ---- 3. the unsure trigger is what it always was ------------------------- */
const unsure = cueFor({ kind: 'trigger', t: 10, label: 'a phrase', conf: 0.3, cue: 'blackout' },
  ctxFor({ 'a phrase': 'braindrain' }));
eq(unsure.spawn.length, 1, 'a trigger the spotter half heard is still one bubble');
eq(unsure.spawn[0].kindId, 'treat', 'and still a plain treat');
ok(!unsure.spawn[0].row, 'and not a row: a guess must never wall the road off');
eq(unsure.word, null, 'and it stays off the chrome');

/* ---- 4. one event, one credit ------------------------------------------- */
const chart = normalizeChart({
  version: 1, binSec: 0.5, energy: [0.4, 0.4, 0.4, 0.4],
  acts: [{ kind: 'triggers', room: 'toybox', t0: 0, t1: 2, name: 'triggers' }],
  events: [{ kind: 'trigger', t: 1, label: 'a phrase', conf: 1, setId: 'a-set', cue: 'blackout' }],
  source: { name: 'rows', hash: 'rows', durationSec: 2, sampleRate: 16000 },
  analysis: { energy: 'x', words: 'script-align-v1', lexicon: ['a phrase'], generatedAt: '', partial: false },
});
const sched = createScheduler(chart);
const due = sched.update(1, 0, 22);
eq(due.length, 1, 'the scheduler hands the trigger over once');
const id = due[0].event.id;
for (let i = 0; i < ROW_X.length; i++) sched.taken(id);   // the kart popped the whole row on its way through
eq(sched.stats().taken, 1, 'popping every bubble of the row is ONE event taken, not ' + ROW_X.length);
eq(sched.stats().countable, 1, 'and the row is one thing to take');

/* ---- 5. the rain thins out on a worded track ---------------------------- */
const peakOf = (lyrics) => cueFor({ kind: 'peak', t: 5 }, ctxFor(null, { intensity: 1, lyrics })).spawn.length;
const plain = peakOf(false), worded = peakOf(true);
ok(plain > 0 && worded > 0, 'a peak still rains either way (' + plain + ' -> ' + worded + ')');
ok(worded <= Math.round(plain / 2), 'a worded track halves it, so the rows stay the loud thing');
const other = (lyrics) => ['word', 'count', 'drop', 'chant', 'build', 'release', 'silence']
  .map((k) => JSON.stringify(cueFor({ kind: k, t: 5, label: 'deeper', conf: 1, n: 1, of: 3, reps: 3, dur: 4, strength: 1, period: 1.2 },
    ctxFor(null, { lyrics }))));
const moved = other(true).filter((c, i) => c !== other(false)[i]).length;
eq(moved, 0, 'and nothing else on the road changes with the transcript');

/* ---- 6. the row lands on the word, whatever the throttle did --------------- */
// A kart on a 60 Hz clock. The scheduler hands the event over LEAD_SEC (2.5 s) early; the row is
// placed off the speed the kart has THEN and the sync re-places it each frame. `speedAt` is the
// throttle: a boost or a ramp inside the lookahead is exactly what used to move the pop off the word.
const ROW_TOL = 0.15, LEAD = 2.5, DT = 1 / 60;
function drive(speedAt, { sync: withSync = true, eventT = 30, handAt = eventT - LEAD } = {}) {
  const sync = createCueSync({ trace: true });
  const event = { id: 'e1', kind: 'trigger', t: eventT, label: 'a phrase' };
  let t = handAt - 1, d = 0, rowD = null, metAt = null, firedAt = null, handed = false;
  for (let i = 0; i < 6 * 60 && metAt == null; i++) {
    const speed = speedAt(t);
    if (!handed && t >= handAt) {
      handed = true;
      rowD = sync.depthFor(t, d, speed, event.t);
      sync.trackRow(7, event, event.t, rowD, t);
      sync.defer(event, { toast: { text: 'x' } }, t);
    }
    const out = sync.update(t, d, speed);
    if (withSync) for (const m of out.move) if (m.rowId === 7) rowD = m.d;
    if (out.fire.length) firedAt = t;
    if (rowD != null && d >= rowD) metAt = t;
    d += speed * DT; t += DT;
  }
  return { metAt, firedAt, rowD, trace: sync.trace()[0] };
}
const steady = drive(() => 22);
ok(steady.metAt != null && Math.abs(steady.metAt - 30) <= ROW_TOL, `at a steady 22 m/s the row is under the kart on the word (${(steady.metAt - 30).toFixed(3)}s)`);
// a boost 1.5 s before the word: 22 -> 34 m/s (the demo's build), then back down
const boost = (t) => (t >= 28.5 && t < 29.6 ? 34 : 22);
const bare = drive(boost, { sync: false });
ok(bare.metAt != null && bare.metAt - 30 < -0.3, `left where it was placed, a boost inside the lookahead brings the row early (${(bare.metAt - 30).toFixed(2)}s), which is the bug`);
const kept = drive(boost);
ok(kept.metAt != null && Math.abs(kept.metAt - 30) <= ROW_TOL, `re-placed each frame, the row is still under the kart on the word through the boost (${(kept.metAt - 30).toFixed(3)}s)`);
// the opening ramp: a slow kart picking up speed across the whole lookahead
const ramp = drive((t) => 8 + Math.max(0, Math.min(1, (t - 27) / 3)) * 20);
ok(ramp.metAt != null && Math.abs(ramp.metAt - 30) <= ROW_TOL, `and through a ramp 8 -> 28 m/s (${(ramp.metAt - 30).toFixed(3)}s)`);
// a slow: the row would have been late without the nudge
const slow = drive((t) => (t >= 28 ? 12 : 26));
ok(slow.metAt != null && Math.abs(slow.metAt - 30) <= ROW_TOL, `and through a slow 26 -> 12 m/s (${(slow.metAt - 30).toFixed(3)}s)`);
ok(kept.trace && kept.trace.rowAt != null && kept.trace.handedAt != null && kept.trace.handedAt <= 30 - LEAD + DT, 'the trace has the handover and the meeting');
// the visible half waits for the second
ok(kept.firedAt != null && kept.firedAt >= 30 && kept.firedAt < 30 + DT + 1e-9, `the cue fired on the word, not at the handover (${(kept.firedAt - 30).toFixed(3)}s after event.t)`);
ok(kept.trace.firedAt != null && kept.trace.firedAt - kept.trace.handedAt > 2, 'and the trace shows the 2.5 s it was held');
{
  const sync = createCueSync();
  const ev2 = (id, t) => ({ id, kind: 'word', t, label: 'w' });
  sync.defer(ev2('a', 10), { toast: { text: 'a' } }, 8);
  sync.defer(ev2('b', 12), { toast: { text: 'b' } }, 9.5);
  eq(sync.update(9.9, 0, 20).fire.length, 0, 'nothing fires before its second');
  eq(sync.update(9.9, 0, 20).fire.length, 0, 'a paused clock (the same second again) drains nothing');
  eq(sync.update(10.0, 0, 20).fire.length, 1, 'the second arrives, the cue goes');
  eq(sync.pending, 1, 'the later one is still held');
  sync.update(8, 0, 20);   // a seek back 2 s: the scheduler is about to hand b over again
  eq(sync.pending, 0, 'a seek back drops what the scheduler will hand over again');
  sync.defer(ev2('c', 20), { toast: { text: 'c' } }, 18);
  const late = sync.update(20 + LATE_SEC + 0.5, 0, 20);
  eq(late.fire.length, 0, 'a seek forward past LATE_SEC lets a held cue go unspent');
  eq(sync.dropped, 1, 'and counts it');
  const d0 = sync.depthFor(5, 100, 20, 5.1);
  eq(d0, 100 + 20 * CUE_AHEAD_SEC, 'a spawn never lands nearer than CUE_AHEAD_SEC of road');
}

/* ---- 7. a WORD BUBBLE lands on its own word too --------------------------- */
// The row used to be the only thing race/sync.js tracked. A word bubble is a row of ONE (cues.js
// `case 'word'` marks its single spawn `row: true` for exactly this reason), so the same
// re-placement holds it on the second the voice says it, through the opening ramp and through a
// boost. Driven off the REAL opening transcript rather than a made-up one, because the thing being
// measured is whether the road holds three words a second, not whether the arithmetic closes.
{
  const file = openingWords();
  const triggers = triggersFromHits([], triggerHits(file, file.durationSec), SET_BY_ID);
  const events = wordEventsFrom(captionWords(file), triggers, { rng: makeRng(7) })
    .sort((a, b) => a.t - b.t)
    .map((e, i) => ({ ...e, id: 'w' + i }));
  ok(events.length > 50, 'the opening transcript lays ' + events.length + ' word bubbles');

  const wctx = { rng: makeRng(11), intensity: 0.5, act: { kind: 'induction', room: 'teagarden' },
    room: { id: 'teagarden' }, triggerKinds: new Map(), lyrics: true };
  /** The throttle a real lap has over this stretch: the opening ramp, a cruise, then a boost. */
  const speedAt = (t) => (t < 84 ? 15 + Math.max(0, t - 78) * 1.2 : (t > 100 && t < 108) ? 30 : 22);

  /** One 60 Hz run over the worded stretch. `track` off is what a loose spawn does. */
  function driveWords(track) {
    const sy = createCueSync();
    const placed = new Map();        // rowId -> { d, at }
    const err = [];
    let t = events[0].t - LEAD - 1, d = 0, cursor = 0, seq = 0, laid = 0;
    for (let i = 0; i < 200 * 60 && (cursor < events.length || placed.size); i++) {
      const speed = speedAt(t);
      while (cursor < events.length && events[cursor].t - LEAD <= t) {     // the scheduler's handover
        const e = events[cursor++];
        const cue = cueFor(e, wctx);
        const sp = cue && cue.spawn[0];
        if (!sp || !sp.row) continue;
        const at = e.t + (sp.at || 0), dd = sy.depthFor(t, d, speed, at);
        const rowId = ++seq;
        placed.set(rowId, { d: dd, at });
        if (track) sy.trackRow(rowId, e, at, dd, t);
        laid++;
      }
      for (const m of sy.update(t, d, speed).move) { const r = placed.get(m.rowId); if (r) r.d = m.d; }
      for (const [id, r] of placed) if (d >= r.d) { err.push(t - r.at); placed.delete(id); }
      d += speed * DT; t += DT;
    }
    const abs = err.map((x) => Math.abs(x)).sort((a, b) => a - b);
    return { laid, met: abs.length, median: abs.length ? abs[abs.length >> 1] : 99, worst: abs.length ? abs[abs.length - 1] : 99 };
  }

  const kept = driveWords(true);
  ok(kept.laid > 50, 'and the run lays every one of them (' + kept.laid + ')');
  eq(kept.met, kept.laid, 'the kart meets each of them exactly once');
  ok(kept.worst <= ROW_TOL, `every word bubble is under the kart within ${ROW_TOL} s of its word (worst ${kept.worst.toFixed(3)}s)`);
  ok(kept.median <= 0.08, `and the median is inside 0.08 s (${kept.median.toFixed(3)}s over ${kept.met} bubbles)`);
  const loose = driveWords(false);
  ok(loose.worst > kept.worst, `left where it was placed a bubble is ${loose.worst.toFixed(2)}s off its word at worst, which is what the tracking is for`);
  console.log('  --  ' + kept.laid + ' word bubbles, median ' + kept.median.toFixed(3) + 's, worst '
    + kept.worst.toFixed(3) + 's tracked, ' + loose.worst.toFixed(2) + 's loose');
}

/* ---- 8. the plate flies once: on the pop, or on the second ---------------- */
// A row used to plate only when the clock reached event.t, so a player who took it a beat early
// watched the word arrive after they had already popped it. The plate now travels with whichever
// came first. Nothing else in the cue moves: the mix, the mood and the fog still land on the word,
// because those are the file talking and the plate is the player being answered.
{
  const sy = createCueSync();
  const trig = (id, t) => ({ id, kind: 'trigger', t, label: 'a phrase' });
  const cue = { word: 'a phrase', mix: 'blackout' };

  sy.defer(trig('a', 30), cue, 27.5);
  const early = sy.claim('a');
  ok(!!early && early.event.id === 'a' && early.cue.word === 'a phrase', 'a row popped early hands its held cue back to be plated');
  eq(sy.claim('a'), null, 'and the next bubble of the same row claims nothing: one plate, one row');
  const fired = sy.update(30, 0, 22).fire;
  eq(fired.length, 1, 'the rest of that cue still fires on the word, never at the pop');
  eq(fired[0].plated, true, 'and carries word that the plate already flew');

  sy.defer(trig('b', 40), cue, 37.5);
  const onTime = sy.update(40, 0, 22).fire;
  eq(onTime.length, 1, 'a row nobody took still fires on its second');
  eq(onTime[0].plated, false, 'with its plate unspent: a row driven past is still read out');
  eq(sy.claim('b'), null, 'and once a cue has fired there is nothing left to claim');
  eq(sy.claim('never-deferred'), null, 'an id that was never held claims nothing');
}

/* ---- 9. the ladder counts LINES, not words ------------------------------- */
// Driven off the REAL opening transcript, because the number that matters is how long an x8 takes
// on a road that lays three bubbles a second, and no made-up chart has that shape.
{
  const file = openingWords();
  const triggers = triggersFromHits([], triggerHits(file, file.durationSec), SET_BY_ID);
  const events = wordEventsFrom(captionWords(file), triggers, { rng: makeRng(7) }).sort((a, b) => a.t - b.t);
  const lines = new Map();
  for (const e of events) lines.set(e.p, (lines.get(e.p) || 0) + 1);
  ok(events.every((e) => e.p != null), 'every word bubble knows the line it belongs to');
  ok(lines.size > 20 && events.length / lines.size > 1.4,
    events.length + ' word bubbles across ' + lines.size + ' lines (' + (events.length / lines.size).toFixed(1) + ' a line)');

  /** One run over the file. `missEvery` drives past every nth bubble; the quiet holds the ladder. */
  function drive(missEvery, perBubble) {
    const score = createScore();
    let released = 0;
    score.onEvent((e) => { if (e.type === 'combo' && e.lost > 0) released++; });
    const got = new Map(), done = new Set();
    const at = {};
    let i = 0, last = events[0].t;
    for (const e of events) {
      const gap = Math.max(0, e.t - last); last = e.t;
      if (gap > COMBO_HOLD_SEC) score.freezeCombo(gap * 2);   // run.js trackFrame: the quiet holds it
      score.tick(gap);
      if (missEvery && ++i % missEvery === 0) { score.unread(); continue; }   // driven past, run.js onMiss
      if (perBubble) score.pop(10, 'treat');                  // the OLD rule, kept here to be measured
      else {
        score.pop(10, 'treat', { combo: false });
        const g = (got.get(e.p) || 0) + 1;
        got.set(e.p, g);
        if (g >= (lines.get(e.p) || 0) && !done.has(e.p)) { done.add(e.p); score.chain(); }
      }
      for (const m of [4, 8]) if (at[m] == null && score.state.mult >= m) at[m] = e.t - events[0].t;
    }
    return { at, released, mult: score.state.mult, lines: done.size, score: score.state.score };
  }

  const was = drive(0, true), now = drive(0, false);
  ok(was.at[8] != null && was.at[8] < 30, 'a rung per bubble reached x8 in ' + (was.at[8] || 0).toFixed(1) + ' s of the file, which is the thing being fixed');
  ok(now.at[4] != null && was.at[4] != null && now.at[4] > was.at[4] * 2,
    'a rung per line takes ' + now.at[4].toFixed(1) + ' s to reach x4 where a rung per bubble took ' + was.at[4].toFixed(1) + ' s');
  ok(now.at[8] == null, 'and x8 is not something this opening hands out for reading along: the rows and the goldens on the road are the rest of it');
  eq(now.mult, 6, 'a clean read of every line in the file is worth x' + now.mult);
  ok(now.score >= events.length * 10, 'every word still pays its treat (' + now.score + ' points over ' + events.length + ' bubbles)');

  const sloppy = drive(3, false);
  eq(sloppy.released, 0, 'a word bubble driven past never lets the ladder go');
  ok(sloppy.lines < now.lines, 'it just costs the line it was in (' + sloppy.lines + ' lines of ' + now.lines + ')');

  // THE CASE THE ALIGNER LANE FOUND, with true word times on Rapid Induction: "bambi" at 152.32 pops,
  // "as" at 155.86 is driven past, "as" at 157.74 pops. Neither gap reaches COMBO_HOLD_SEC, so the
  // quiet rule never freezes the hold, and the two of them add to 5.42 s. The word that went by has to
  // start the patience again (race/score.js unread(), run.js onMiss) or the ladder times out mid-line.
  const carried = createScore(), naive = createScore();
  let letGoMid = 0;
  carried.onEvent((e) => { if (e.type === 'combo' && e.lost > 0) letGoMid++; });
  carried.chain(); naive.chain();
  carried.tick(3.54); carried.unread();   // the word driven past: no rung, no release, a fresh hold
  naive.tick(3.54);
  carried.tick(1.88); naive.tick(1.88);
  eq(letGoMid, 0, 'a word driven past between two pops 3.54 s and 1.88 s apart never lets the ladder go');
  eq(carried.state.combo, 1, 'the ladder stands exactly where it stood: the missed word stepped nothing');
  eq(naive.state.combo, 0, 'and without it touching the hold clock those two gaps would have timed out');

  const solo = createScore();
  solo.pop(10, 'treat', { combo: false });
  eq(solo.state.combo, 0, 'one word on its own is no rung at all');
  eq(solo.state.score, 10, 'and is still worth its treat');

  // the wordless opening: this file says nothing for over a minute, and a streak carried into it has
  // to survive the drive rather than time out on an empty road
  ok(events[0].t > COMBO_HOLD_SEC * 4, 'the file opens with ' + events[0].t.toFixed(0) + ' s of road before the first word');
  const held = createScore(), letGo = createScore();
  held.chain(); held.chain(); letGo.chain(); letGo.chain();
  for (let t = 0; t < events[0].t; t += 1 / 60) { held.freezeCombo(2 / 60); held.tick(1 / 60); letGo.tick(1 / 60); }
  eq(held.state.combo, 2, 'and the ladder is where the player left it when the first word lands');
  eq(letGo.state.combo, 0, 'unheld it would have let go long before');

  // and the end card counts the same way the ladder does
  const two = normalizeChart({
    version: 1, binSec: 0.5, energy: [0.4, 0.4, 0.4, 0.4],
    acts: [{ kind: 'induction', room: 'teagarden', t0: 0, t1: 5, name: 'induction' }],
    events: [{ kind: 'word', t: 1, w: 'aa', x: 0, p: 0 }, { kind: 'word', t: 1.2, w: 'bb', x: 0, p: 0 },
      { kind: 'word', t: 3, w: 'cc', x: 0, p: 1 }, { kind: 'word', t: 3.2, w: 'dd', x: 0, p: 1 }],
    source: { name: 'lines', hash: 'lines', durationSec: 5, sampleRate: 16000 },
    analysis: { energy: 'x', words: 'script-align-v1', lexicon: [], generatedAt: '', partial: false },
  });
  const sc = createScheduler(two);
  eq(sc.stats().countable, 2, 'four word bubbles in two lines is TWO things to take on the end card');
  sc.taken(two.events[0].id);
  eq(sc.stats().taken, 0, 'half a line read is nothing taken');
  sc.taken(two.events[1].id);
  eq(sc.stats().taken, 1, 'and the whole of it is one');
}

/* ---- 10. the row's line and the words either side of it never share the pop box ---- */
// The guard used to be 0.4 s of quiet either side of a trigger, which took the whole sentence
// around it off the road. It is the phrase's own span now, and the margins either side of that
// span are here for exactly one reason: race/bubbles.js pops on `|rel| < POP_HIT_D`, so two
// bubbles less than 2 x POP_HIT_D of road apart can sit in the box together and one pass takes
// both. Held on the DENSEST transcript on the shelf (202 rows) at the SLOWEST pace the road ever
// cruises (the opening ramp, OPEN_PACE of the base), where a second of file is the fewest metres
// of road and a row and the words either side of it are closest together.
{
  const row = WORDS_ROWS.find((r) => /bubble acceptance/i.test(String(r.title || ''))) || WORDS_ROWS[0];
  const file = wordsFile(row);
  const triggers = triggersFromHits([], triggerHits(file, file.durationSec), SET_BY_ID);
  const words = wordEventsFrom(captionWords(file), triggers, { rng: makeRng(7) });
  ok(triggers.length > 100 && words.length > 2000,
    'the densest road on the shelf: ' + triggers.length + ' rows and ' + words.length + ' word bubbles');
  const events = words.concat(triggers).sort((a, b) => a.t - b.t).map((e, i) => ({ ...e, id: 'e' + i }));

  const speed = OPEN_PACE * KART_BASE_SPEED;
  const sy = createCueSync();
  const live = new Map();                       // rowId -> { d, kind } while it is on the road
  let cursor = 0, t = events[0].t - LEAD - 1, d = 0, seq = 0, sharing = 0, sharedAt = 0, nearest = Infinity, nearestAt = 0;
  for (let i = 0; i < 60 * 60 * 40 && (cursor < events.length || live.size); i++) {
    while (cursor < events.length && events[cursor].t - LEAD <= t) {   // the scheduler's handover
      const e = events[cursor++], id = ++seq, dd = sy.depthFor(t, d, speed, e.t);
      live.set(id, { d: dd, kind: e.kind });
      sy.trackRow(id, e, e.t, dd, t);
    }
    for (const m of sy.update(t, d, speed).move) { const r = live.get(m.rowId); if (r) r.d = m.d; }
    let inBoxWords = 0, rowD = null;
    for (const [id, r] of live) {
      const rel = r.d - d;
      // dropped a whole box behind the box, which is well inside the metres race/bubbles.js keeps
      // a passed bubble for: the word BEFORE a row has to still be here to be measured against it
      if (rel < -3 * POP_HIT_D) { live.delete(id); continue; }
      if (Math.abs(rel) < POP_HIT_D) { if (r.kind === 'trigger') rowD = r.d; else inBoxWords++; }
    }
    if (rowD != null) {
      if (inBoxWords) { sharing++; sharedAt = t; }
      for (const r of live.values()) {
        if (r.kind === 'trigger') continue;
        const gap = Math.abs(r.d - rowD);
        if (gap < nearest) { nearest = gap; nearestAt = t; }
      }
    }
    d += speed * DT; t += DT;
  }
  eq(sharing, 0, 'not one frame has a word bubble and a row bubble in the pop box together'
    + (sharing ? ' (first at ' + sharedAt.toFixed(1) + 's)' : ''));
  ok(nearest >= 2 * POP_HIT_D, 'the nearest a word bubble ever comes to a row is ' + nearest.toFixed(2)
    + ' m of road (at ' + Math.round(nearestAt) + 's), and the pop box is ' + (2 * POP_HIT_D).toFixed(1) + ' m deep');
  console.log('  --  ' + words.length + ' word bubbles around ' + triggers.length + ' rows at '
    + speed.toFixed(1) + ' m/s: nearest approach ' + nearest.toFixed(2) + ' m');
}

console.log(fails ? '\nrows-check: ' + fails + ' failed' : '\nrows-check: all good');
process.exit(fails ? 1 : 0);
