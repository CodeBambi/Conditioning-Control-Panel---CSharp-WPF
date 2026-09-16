/* ============================================================================
 * race/smoke/chart-check.mjs - node self-check for race/chart.js.
 *
 *   node race/smoke/chart-check.mjs      (exits 0 on pass, 1 with a count of failures)
 *
 * chart.js imports only consts.js, so this needs no three and no DOM: it builds the
 * demo track, walks a scheduler at 60 Hz behind a kart holding KART_BASE_SPEED and
 * checks the promises CHART.md makes. In order: the demo chart normalizes with
 * contiguous acts and every event kind in it; one walk spawns every event exactly
 * once, nothing behind the kart and each spawn a lead of road ahead; actAt never
 * walks backwards and energyAt stays in 0..1; replace() halfway does not re-spawn
 * what already fired and keeps taken ids; a scheduler joined late counts what it
 * skipped as missed; a version 2 chart and a chart with no duration both throw.
 * ==========================================================================*/

import { KART_BASE_SPEED } from '../consts.js';
import { demoChart, normalizeChart, createScheduler, ACT_ROOM, EVENT_KINDS, STRUCTURE_WORDS, DROP_WORDS } from '../chart.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

const DUR = 240, SPEED = KART_BASE_SPEED, STEP = 1 / 60;
const chart = demoChart(DUR);

/* ---- 1. the demo chart -------------------------------------------------- */
{
  ok(chart.version === 1 && chart.source.durationSec === DUR, 'demoChart(240) is a version 1 chart of 240 s');
  ok(!!normalizeChart(chart) && Object.isFrozen(chart.events), 'a normalized chart re-normalizes and its events are frozen');
  ok(chart.acts[0].t0 === 0 && chart.acts[chart.acts.length - 1].t1 === DUR, 'the acts span the file end to end');
  let gap = false, back = false, sorted = true;
  for (let i = 1; i < chart.acts.length; i++) {
    if (chart.acts[i].t0 !== chart.acts[i - 1].t1) gap = true;
    if (chart.acts[i].t0 < chart.acts[i - 1].t0) back = true;
  }
  for (let i = 1; i < chart.events.length; i++) if (chart.events[i].t < chart.events[i - 1].t) sorted = false;
  ok(!gap && !back, 'the acts are contiguous and sorted');
  ok(chart.acts.every((a) => a.room === ACT_ROOM[a.kind]), 'every act sits in its ACT_ROOM');
  ok(EVENT_KINDS.every((k) => chart.events.some((e) => e.kind === k)), 'every event kind is in the demo: ' + EVENT_KINDS.join(', '));
  ok(sorted && new Set(chart.events.map((e) => e.id)).size === chart.events.length, 'events are sorted by t with unique ids');
  const count = chart.events.filter((e) => e.kind === 'count');
  ok(count.length === 10 && count[9].last === true, 'the countdown is ten numbers and the last is flagged');
  ok(DROP_WORDS.every((w) => STRUCTURE_WORDS.includes(w)), 'DROP_WORDS is a subset of STRUCTURE_WORDS');
}

/* ---- 2. one walk at 60 Hz ----------------------------------------------- */
{
  const sched = createScheduler(chart), lead = sched.leadSec, seen = new Map();
  let behind = 0, tooFar = 0, tooNear = 0, d = 0;
  for (let t = 0; t <= DUR + lead; t += STEP, d += SPEED * STEP) {
    for (const hit of sched.update(t, d, SPEED)) {
      seen.set(hit.event.id, (seen.get(hit.event.id) || 0) + 1);
      const ahead = hit.d - d;
      if (ahead <= 0) behind++;
      if (ahead > SPEED * lead + 1e-6) tooFar++;
      if (ahead < SPEED * (lead - STEP) - 1e-6) tooNear++;
    }
  }
  const st = sched.stats();
  ok(seen.size === chart.events.length, 'every event spawned: ' + seen.size + ' of ' + chart.events.length);
  ok([...seen.values()].every((n) => n === 1), 'and none of them twice');
  ok(behind === 0, 'no event ever spawned behind the kart');
  ok(tooFar === 0 && tooNear === 0, 'every spawn sat a lead of road ahead (' + (SPEED * lead).toFixed(1) + ' m, one frame of slack)');
  ok(st.missed === 0, 'a clean walk misses nothing');
  ok(st.fired === st.total && st.countable > 0 && st.countable < st.total, 'stats: fired ' + st.fired + '/' + st.total + ', countable ' + st.countable);
}

/* ---- 3. acts and energy ------------------------------------------------- */
{
  const sched = createScheduler(chart);
  let lastAct = -1, actBack = 0, hole = 0, outOfRange = 0;
  for (let t = 0; t < DUR; t += 0.25) {
    const a = sched.actAt(t);
    if (!a) { hole++; continue; }
    if (a.id < lastAct) actBack++;
    lastAct = a.id;
    const e = sched.energyAt(t);
    if (!(e >= 0 && e <= 1)) outOfRange++;
  }
  const quiet = chart.acts.find((a) => a.kind === 'silence'), wake = chart.acts.find((a) => a.kind === 'wake');
  ok(hole === 0 && actBack === 0, 'actAt covers the file and never walks backwards');
  ok(outOfRange === 0, 'energyAt stays inside 0..1 for the whole file');
  ok(sched.energyAt(-5) === 0 && sched.energyAt(DUR + 5) === 0, 'energyAt is 0 off both ends');
  ok(sched.energyAt(quiet.t0 + 2) < sched.energyAt(wake.t0 + 5), 'the quiet reads lower than the wake');
}

/* ---- 4. replace mid-run, then reset ------------------------------------- */
{
  const sched = createScheduler(chart), first = new Set();
  let d = 0, t = 0, dupes = 0, second = 0;
  for (; t < DUR / 2; t += STEP, d += SPEED * STEP) for (const h of sched.update(t, d, SPEED)) { first.add(h.event.id); sched.taken(h.event.id); }
  sched.replace(demoChart(DUR));                                 // the words pass landing on a partial
  for (; t <= DUR + 3; t += STEP, d += SPEED * STEP) for (const h of sched.update(t, d, SPEED)) { second++; if (first.has(h.event.id)) dupes++; }
  ok(first.size > 0, 'the first half spawned ' + first.size + ' events');
  ok(dupes === 0, 'replace() re-spawned nothing that had already fired');
  ok(second > 0 && first.size + second === chart.events.length, 'and the rest of the track still came through');
  ok(sched.stats().taken === first.size, 'taken ids survive the swap');
  sched.reset();
  ok(sched.stats().fired === 0 && sched.stats().taken === 0 && sched.lastT === 0, 'reset() empties the scheduler');
}

/* ---- 5. joining late, and bad charts ------------------------------------ */
{
  const sched = createScheduler(chart);
  const due = sched.update(120, 0, SPEED);
  ok(due.every((h) => h.dueIn > -0.5), 'joining at 120 s hands out only what is still ahead');
  ok(sched.stats().missed > 0, 'and counts the ' + sched.stats().missed + ' events it skipped as missed');

  const threw = (fn) => { try { fn(); return ''; } catch (e) { return e instanceof Error ? e.message : ''; } };
  const v2 = threw(() => normalizeChart({ version: 2, source: { durationSec: 60 } }));
  ok(v2.startsWith('chart:') && v2.includes('version 2'), 'a version 2 chart throws: ' + v2);
  ok(threw(() => normalizeChart({ version: 1, source: {} })).includes('durationSec'), 'a chart with no duration throws too');
  ok(threw(() => normalizeChart(null)).startsWith('chart:'), 'and null throws a readable Error');
}

console.log(fails ? '\nchart-check: ' + fails + ' failure(s)' : '\nchart-check: all good');
process.exit(fails ? 1 : 0);
