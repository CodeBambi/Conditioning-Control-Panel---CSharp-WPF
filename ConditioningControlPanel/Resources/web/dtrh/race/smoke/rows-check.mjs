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
 *   3. a trigger the spotter only half heard is the single treat it always was
 *   4. one event is one credit however many of its bubbles were popped
 *   5. a worded track halves the peak rain; nothing else on the road moves
 *   6. the row lands ON the word: driven through race/sync.js with a simulated
 *      kart whose speed changes inside the lookahead, the row is under the kart
 *      within 0.15 s of event.t, and the visible half of the cue fires on the
 *      second, never at the scheduler's 2.5 s early handover
 *
 * It never prints a line of a transcript. Everything below is counted.
 * ==========================================================================*/

import { cueFor, ROW_X, ROW_MAX_GAP } from '../cues.js';
import { createScheduler, normalizeChart } from '../chart.js';
import { KART_X_MAX, LANE_X_MAX, POP_HIT_X, LANE_H, makeRng } from '../consts.js';
import { THEME_BY_PRESET, kindForPreset } from '../triggerTheme.js';
import { KIND_BY_ID } from '../bubbleKinds.js';
import { createCueSync, CUE_AHEAD_SEC, LATE_SEC } from '../sync.js';

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

console.log(fails ? '\nrows-check: ' + fails + ' failed' : '\nrows-check: all good');
process.exit(fails ? 1 : 0);
