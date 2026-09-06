/* ============================================================================
 * race/smoke/track-run-check.mjs - node self-check for race/track.js + race/cues.js.
 *
 *   node race/smoke/track-run-check.mjs      (exits 0 on pass, 1 with a count of failures)
 *
 * run.js itself cannot be driven here: it builds a WebGLRenderer and reads the DOM
 * on the first line of createRace. So the two halves that CAN be pure are, and this
 * walks them the way run.js does: the demo track at 60 Hz behind a kart holding
 * KART_BASE_SPEED, the scheduler's events run through cueFor, every spawn placed at
 * the same depth run.js computes, into a stub field with bubbles.js's pool rules.
 *
 * In order: setTrack fills the CHART.md track object; the clock only moves while the
 * host says playing and a tick snaps it; intensity follows the energy curve smoothed
 * with a floor; the acts arrive in order, once each, never the opening one twice;
 * every event kind has a cue and every cue field is legal; a whole run spawns
 * everything ahead of the kart; the run ends inside END_PAD of the duration and the
 * summary counts what the player took; replace() keeps the clock and the taken ids.
 * ==========================================================================*/

import { KART_BASE_SPEED, LANE_H, CEILING_H, ROAD_HALF_W, makeRng } from '../consts.js';
import { demoChart, EVENT_KINDS } from '../chart.js';
import { KIND_BY_ID } from '../bubbleKinds.js';
import { createTrackState } from '../track.js';
import { cueFor } from '../cues.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

const DUR = 240, SPEED = KART_BASE_SPEED, STEP = 1 / 60, CUE_AHEAD_SEC = 0.25, CAP = 160;
const MOODS = ['calm', 'streamed', 'fraught', 'smug', 'shock', 'jackpot'];
const PLACEMENTS = ['lane', 'air', 'spawn', 'rain'];
const chart = demoChart(DUR);

/** The bubble field run.js spawns into, with bubbles.js's pool cap and density gate. */
function stubField() {
  const live = [];
  let density = 1;
  return {
    spawnAt({ kindId, placement, d, x, h, eventId }) {
      if (live.length >= CAP) return -1;
      if (density <= 0) return -1;
      live.push({ kindId, placement, d, x, h, eventId });
      return live.length - 1;
    },
    setDensity(m) { density = m; },
    get live() { return live; },
    get density() { return density; },
  };
}

/* ---- 1. setTrack, the clock and the intensity --------------------------- */
{
  const st = createTrackState();
  ok(st.track === null && st.step(1) === null, 'no track: step() is null and the seeded run is untouched');
  const tr = st.setTrack(chart);
  ok(!!tr && tr.chart === chart && tr.name === 'the demo track' && tr.durationSec === DUR, 'setTrack fills { chart, sched, t, playing, name, durationSec }');
  ok(tr.t === 0 && tr.playing === false, 'and the clock starts at 0, stopped');
  ok(st.step(0.5).t === 0, 'a stopped clock does not move on step()');
  st.clock(0, true);
  let n = 0;
  for (let i = 0; i < 60; i++) { st.step(STEP); n++; }
  ok(Math.abs(tr.t - n * STEP) < 1e-6, 'a playing clock integrates the frame time: ' + tr.t.toFixed(3) + ' s');
  st.clock(90, true);
  ok(tr.t === 90, 'a host tick snaps the second');
  st.clock(90, false);
  st.step(1);
  ok(tr.t === 90, 'and playing:false freezes it again (the Brake, a host pause, a video pop)');

  st.clock(0, true);
  let floored = true, tracked = 0;
  for (let t = 0; t < DUR; t += 0.25) {
    const s = st.step(0.25);
    if (s.intensity < 0.05 - 1e-9 || s.intensity > 1) floored = false;
    if (Math.abs(s.intensity - chartEnergy(s.t)) < 0.12) tracked++;
  }
  ok(floored, 'intensity stays inside [0.05, 1] for the whole file');
  ok(tracked > 700, 'and follows the energy curve within 0.12 for ' + tracked + ' of 960 samples');
}
function chartEnergy(t) {
  const i = Math.max(0, Math.min(chart.energy.length - 1, Math.floor(t / chart.binSec)));
  return chart.energy[i];
}

/* ---- 2. the acts -------------------------------------------------------- */
{
  const st = createTrackState();
  st.setTrack(chart);
  st.clock(0, true);
  const changes = [];
  for (let t = 0; t < DUR; t += STEP) { const s = st.step(STEP); if (s.actChanged) changes.push(s.act.id); }
  ok(changes.length === chart.acts.length - 1, 'every act after the opening one announces itself once: ' + changes.length);
  ok(changes.every((id, i) => id === i + 1), 'and they arrive in order: ' + changes.join(', '));
  ok(chart.acts.every((a) => !!a.name && !!a.room), 'every act carries the name and room the MARQUEE needs');
}

/* ---- 3. the cue table --------------------------------------------------- */
{
  const rng = makeRng(7);
  const triggerKinds = createTrackState();
  triggerKinds.setTrack(chart);
  const ctx = { energy: 0.5, act: chart.acts[0], room: null, intensity: 0.5, rng, triggerKinds: triggerKinds.triggerKinds };
  const seen = new Set();
  let bad = '';
  for (const e of chart.events) {
    const cue = cueFor(e, ctx);
    if (!cue) { bad = bad || ('no cue for ' + e.kind); continue; }
    seen.add(e.kind);
    for (const sp of cue.spawn) {
      if (!KIND_BY_ID[sp.kindId]) bad = bad || ('unknown bubble kind ' + sp.kindId + ' from ' + e.kind);
      if (!PLACEMENTS.includes(sp.placement)) bad = bad || ('bad placement ' + sp.placement);
      if (!(Math.abs(sp.x) <= ROAD_HALF_W)) bad = bad || ('x off the road: ' + sp.x);
      if (!(sp.h >= 0 && sp.h <= CEILING_H)) bad = bad || ('h out of the tube: ' + sp.h);
      if (!(sp.at >= 0)) bad = bad || ('at before the word: ' + sp.at);
    }
    if (cue.mood && !MOODS.includes(cue.mood)) bad = bad || ('unknown mood ' + cue.mood);
    if (cue.mix && !KIND_BY_ID[cue.mix]) bad = bad || ('unknown mix kind ' + cue.mix);
    if (cue.boost < 0 || cue.boost > 4) bad = bad || ('boost out of range ' + cue.boost);
  }
  ok(!bad, bad || 'every cue names a real bubble kind, placement, mood and mix');
  ok(EVENT_KINDS.every((k) => seen.has(k)), 'the demo track exercised all nine event kinds');
  ok(cueFor(null, ctx) === null && cueFor({ kind: 'nonsense' }, ctx) === null, 'a junk event is null, never a throw');

  const t = cueFor({ kind: 'trigger', label: 'good girl' }, ctx);
  ok(t.spawn.length === 1 && KIND_BY_ID[t.spawn[0].kindId].kind === 'effect' && t.word === 'good girl', 'a trigger is one effect bubble and the word: ' + t.spawn[0].kindId);
  ok(cueFor({ kind: 'trigger', label: 'never analysed' }, ctx).spawn[0].kindId === 'flash', 'an unmapped phrase falls back to flash');
  const w = cueFor({ kind: 'word', label: 'deeper' }, ctx);
  ok(w.spawn.length === 1 && w.spawn[0].kindId === 'treat' && w.spawn[0].h === LANE_H, 'a structure word is one lane treat');
  const c = cueFor({ kind: 'count', label: '1', n: 1, of: 10, last: true }, ctx);
  ok(c.spawn[0].kindId === 'golden' && c.spawn[0].placement === 'air' && c.jump === 6, 'the last of a countdown is a golden air bubble and a jump');
  const dr = cueFor({ kind: 'drop', strength: 1 }, ctx);
  ok(dr.jump === 7 && dr.mix === 'spiral' && dr.mood === 'streamed' && dr.spawn.length === 3, 'a drop is jump 7, a spiral and three rings');
  ok(dr.spawn.map((s) => s.at).join() === '0.2,0.5,0.8', 'the three land at 0.2, 0.5, 0.8');
  const ch = cueFor({ kind: 'chant', label: 'good girl', reps: 5, period: 2.4 }, ctx);
  ok(ch.spawn.length === 5 && ch.spawn.every((s, i) => Math.abs(s.at - i * 2.4) < 1e-9), 'a chant is one treat per rep on the chant beat');
  ok(new Set(ch.spawn.map((s) => s.x)).size === 2, 'alternating between two lanes');
  const b = cueFor({ kind: 'build', dur: 9 }, ctx);
  ok(b.boost === 4 && b.density === 1.6, 'a build caps the boost at 4 s and thickens the road');
  ok(cueFor({ kind: 'peak' }, ctx).spawn.filter((s) => s.placement === 'rain').length === 6, 'a peak is six rain treats');
  const rl = cueFor({ kind: 'release' }, ctx);
  ok(rl.mood === 'calm' && rl.density === 0.6, 'a release calms her and thins the road');
  const si = cueFor({ kind: 'silence', dur: 12 }, ctx);
  ok(si.fog === 1 && si.density === 0 && si.holdSec === 12, 'silence is fog, nothing on the road, for as long as the quiet runs');
}

/* ---- 4. a whole run ----------------------------------------------------- */
{
  const st = createTrackState();
  st.setTrack(chart);
  st.clock(0, true);
  const field = stubField(), rng = makeRng(3);
  let d = 0, ended = 0, endedAt = 0, behind = 0, hold = 0, spawns = 0;
  for (let t = 0; t <= DUR + 1 && !ended; t += STEP) {
    const s = st.step(STEP);
    d += SPEED * STEP;
    if (hold > 0) { hold -= STEP; if (hold <= 0) field.setDensity(1); }
    for (const due of st.due(d, SPEED)) {
      const cue = cueFor(due.event, { energy: s.intensity, act: s.act, room: null, intensity: s.intensity, rng, triggerKinds: st.triggerKinds });
      if (!cue) continue;
      for (const sp of cue.spawn) {
        const at = d + SPEED * Math.max(due.dueIn + (sp.at || 0), CUE_AHEAD_SEC);
        if (at <= d) behind++;
        if (field.spawnAt({ kindId: sp.kindId, placement: sp.placement, d: at, x: sp.x, h: sp.h, eventId: due.event.id }) >= 0) spawns++;
      }
      if (cue.density != null) field.setDensity(cue.density);
      if (cue.holdSec > 0) hold = cue.holdSec;
      if (due.event.kind === 'trigger' || due.event.kind === 'word') st.taken(due.event.id);
    }
    if (s.ended) { ended = 1; endedAt = s.t; }
  }
  ok(ended === 1, 'the run ended on its own');
  ok(endedAt >= DUR - 0.25 - STEP && endedAt <= DUR, 'and inside the end pad: ' + endedAt.toFixed(2) + ' of ' + DUR + ' s');
  ok(behind === 0, 'no cue bubble was ever placed behind the kart');
  ok(spawns >= 40 && spawns === field.live.length, spawns + ' cue bubbles went in, none of them gated out by a full pool');
  ok(field.live.every((b) => !!b.eventId), 'every one of them carries its event id back for the pop');
  const sum = st.summary();
  const took = chart.events.filter((e) => e.kind === 'trigger' || e.kind === 'word').length;
  ok(sum.taken === took && sum.countable >= took, 'the summary counts ' + sum.taken + ' taken of ' + sum.countable + ' countable');
  ok(sum.name === 'the demo track' && sum.hash === 'demo' && sum.durationSec === DUR, 'and names the file for run-ended');
}

/* ---- 5. the words pass landing live ------------------------------------- */
{
  const st = createTrackState();
  st.setTrack(demoChart({ durationSec: DUR, seed: 1 }));
  st.clock(120, true);
  st.step(STEP);
  st.taken('d0');
  const before = st.stats().taken;
  st.replace(demoChart({ durationSec: DUR, seed: 1 }));
  ok(Math.abs(st.track.t - 120) < 0.1, 'replace() keeps the clock where it was: ' + st.track.t.toFixed(2) + ' s');
  ok(st.stats().taken === before, 'and keeps what the player had already taken');
  ok(st.setTrack(null) === null && st.track === null, 'setTrack(null) hands the seeded run back');
}

console.log(fails ? '\ntrack-run-check: ' + fails + ' failure(s)' : '\ntrack-run-check: all good');
process.exit(fails ? 1 : 0);
