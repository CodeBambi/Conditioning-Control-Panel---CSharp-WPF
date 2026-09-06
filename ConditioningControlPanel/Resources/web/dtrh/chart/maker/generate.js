/* ============================================================================
 * chart/maker/generate.js - the road under the triggers (chart/MAKER.md, M5).
 *
 * Pure: no DOM, no window, no clock, so chart/smoke/maker-check.mjs runs it
 * under node. The maker places the trigger recipes by hand; this is everything
 * between them, the part that turns twelve minutes of empty tarmac into a run.
 *
 * It reads two things and writes three:
 *
 *   in    the min/max peaks the waveform already holds, and the aligned words
 *   out   `energy` (0.5 s bins, 0..1), `acts` (which room the racer is in) and
 *         `events` in the kinds race/chart.js already schedules and race/cues.js
 *         already knows how to spend: word, drop, count, chant, build, peak,
 *         release, silence.
 *
 * Nothing here is hand authored, so nothing here carries `hand: true`: the
 * recipes sit on top as hand cues and the density knob leaves them alone while
 * it thins the road. Re-running this replaces the road and never the recipes.
 *
 * This file landed on main ahead of the rest of the maker (PR #873) so the web
 * build could import the road generator early; the maker in this folder is now
 * the one and only copy.
 * ==========================================================================*/

import { DROP_WORDS, STRUCTURE_WORDS } from '../../race/chart.js';

export const BIN_SEC = 0.5;
/** The half second that reads as "full". A gentle percentile, so one loud clap
 *  does not push the whole file down into the quiet end of the curve. */
const LOUD_PCT = 0.92;
/** No two road words land closer than this, and a trigger word wins the slot. */
const WORD_GAP = 2.2;
/** A word plain enough to be road filler: this long, said this clearly, not a filler word. */
const FILL_LEN = 4, FILL_CONF = 0.6;
/** A drop swallows the ones behind it for this long: it is a jump and a spiral. */
const DROP_MERGE = 8, DROP_RUN = 2.5;
/** A count run: three numbers or more, this close, with at most this much filler. */
const COUNT_GAP = 6, COUNT_FILLER = 2, COUNT_MIN = 3;
/** A phrase said three times inside this window, on a beat this tight, is a chant. */
const CHANT_WIN = 20, CHANT_MIN = 3, CHANT_MAX = 16, CHANT_PERIOD_MAX = 5;
/** A climb worth calling a build, and how far apart two of them sit. */
const RISE_MIN = 0.18, RISE_SEC_MIN = 4, RISE_SEC_MAX = 20, BUILD_DUR_MAX = 12, PEAK_GAP_SEC = 20;
/** Nothing said for this long is a silence, and no silence runs longer than this. */
const QUIET_MIN = 4, QUIET_MAX = 20;
/** Acts are read in windows this wide and never end up shorter than this. */
const ACT_WIN = 20, ACT_MIN_SEC = 25, ACT_MAX = 24;

const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);
const r2 = (v) => Math.round(v * 100) / 100;
const r3 = (v) => Math.round(v * 1000) / 1000;
const norm = (w) => String(w || '').toLowerCase().replace(/[^a-z0-9]+/g, '');

const NUMS = { zero: 0, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10 };
for (let i = 0; i <= 10; i++) NUMS[String(i)] = i;
const STRUCT = new Set(STRUCTURE_WORDS);
const DROPS = new Set(DROP_WORDS);
/** Said too often to mean anything on its own, so a run of them is not a chant. */
const DULL = new Set(['the', 'a', 'an', 'and', 'to', 'of', 'you', 'your', 'it', 'is', 'are', 'as', 'in', 'on',
  'that', 'this', 'so', 'for', 'be', 'with', 'my', 'me', 'i', 'we', 'her', 'she', 'all', 'can', 'will', 'just']);

/** The words the acts pass reads, one bucket per act kind it may answer with. */
const ACT_WORDS = {
  induction: ['relax', 'relaxing', 'breathe', 'breath', 'listen', 'focus', 'comfortable', 'settle', 'close', 'voice'],
  deepening: ['deeper', 'deep', 'down', 'sink', 'sinking', 'heavy', 'float', 'floating', 'melt', 'under', 'drop', 'dropping'],
  wake: ['wake', 'awake', 'waking', 'awaken', 'up', 'alert', 'refreshed', 'stretch'],
};

/* ---- the curve ----------------------------------------------------------- */

function percentile(sorted, p) {
  if (!sorted.length) return 0;
  const i = Math.min(sorted.length - 1, Math.max(0, Math.round(p * (sorted.length - 1))));
  return sorted[i];
}

/**
 * The energy curve: one 0..1 number per `binSec` of audio, RMS-ish off the
 * min/max peaks the waveform already keeps, then divided by the loud end of the
 * file rather than by its single loudest moment.
 */
export function energyFromPeaks(peaks, perSec, durationSec, binSec = BIN_SEC) {
  const bins = Math.max(1, Math.ceil((Number(durationSec) || 0) / binSec));
  const out = new Array(bins).fill(0);
  const n = peaks && peaks.length ? Math.floor(peaks.length / 2) : 0;
  if (!n || !(perSec > 0)) return out;
  for (let i = 0; i < bins; i++) {
    const b0 = Math.min(n, Math.floor(i * binSec * perSec));
    const b1 = Math.min(n, Math.max(b0 + 1, Math.floor((i + 1) * binSec * perSec)));
    let sum = 0;
    for (let b = b0; b < b1; b++) { const a = (peaks[b * 2 + 1] - peaks[b * 2]) / 2; sum += a * a; }
    out[i] = b1 > b0 ? Math.sqrt(sum / (b1 - b0)) : 0;
  }
  const loud = percentile(out.filter((v) => v > 0).sort((a, b) => a - b), LOUD_PCT);
  const ref = loud > 0 ? loud : Math.max(...out) || 1;
  return out.map((v) => r2(clamp01(v / ref)));
}

/** A moving average, so a single loud syllable is never a peak of its own. */
function smooth(curve, half = 2) {
  return curve.map((_, i) => {
    let s = 0, c = 0;
    for (let j = Math.max(0, i - half); j <= Math.min(curve.length - 1, i + half); j++) { s += curve[j]; c++; }
    return c ? s / c : 0;
  });
}

/* ---- the words ----------------------------------------------------------- */

/** The words file, flattened to what this pass reads: time, length, the bare word, confidence. */
export function readWords(json) {
  const raw = (json && Array.isArray(json.words)) ? json.words : [];
  return raw
    .filter((w) => w && typeof w.t === 'number' && typeof w.w === 'string')
    .map((w) => ({ t: w.t, d: Number(w.d) || 0.2, k: norm(w.w), conf: typeof w.conf === 'number' ? w.conf : 1 }))
    .filter((w) => w.k)
    .sort((a, b) => a.t - b.t);
}

/** Spoken number runs: "ten, nine, eight..." with a little room for filler between them. */
export function countRuns(ws) {
  const out = [];
  let run = [], filler = 0;
  const flush = () => { if (run.length >= COUNT_MIN) out.push(run); run = []; filler = 0; };
  for (const w of ws) {
    const v = NUMS[w.k];
    if (v === undefined) { if (run.length && ++filler > COUNT_FILLER) flush(); continue; }
    const prev = run[run.length - 1];
    const step = prev ? Math.abs(v - prev.v) : 0;
    if (prev && (w.t - prev.t > COUNT_GAP || step === 0 || step > 3)) flush();
    run.push({ ...w, v });
    filler = 0;
  }
  flush();
  return out;
}

/** Every phrase said CHANT_MIN times or more inside CHANT_WIN seconds, longest run first. */
export function chantRuns(ws) {
  const by = new Map();
  const add = (k, t) => { if (!by.has(k)) by.set(k, []); by.get(k).push(t); };
  for (let i = 0; i < ws.length; i++) {
    if (ws[i].conf < 0.4) continue;
    if (!DULL.has(ws[i].k)) add(ws[i].k, ws[i].t);
    const b = ws[i + 1];
    if (b && b.t - ws[i].t < 2 && !(DULL.has(ws[i].k) && DULL.has(b.k))) add(ws[i].k + ' ' + b.k, ws[i].t);
  }
  const found = [];
  for (const [phrase, ts] of by) {
    if (ts.length < CHANT_MIN) continue;
    if (!phrase.split(' ').some((p) => p.length >= 4)) continue;
    for (let i = 0; i < ts.length;) {
      let j = i;
      while (j + 1 < ts.length && ts[j + 1] - ts[i] <= CHANT_WIN) j++;
      const reps = Math.min(CHANT_MAX, j - i + 1);
      const period = (ts[i + reps - 1] - ts[i]) / Math.max(1, reps - 1);
      // three of anything inside twenty seconds is a coincidence; a chant keeps a beat, and
      // three is only a chant when the phrase is one the script leans on anyway.
      if (reps >= CHANT_MIN && period <= CHANT_PERIOD_MAX && (reps > CHANT_MIN || STRUCT.has(phrase.split(' ')[0]))) {
        found.push({ phrase, t: ts[i], reps, period: r3(period), dur: r3(ts[i + reps - 1] - ts[i]) });
        i = j + 1;
      } else i++;
    }
  }
  found.sort((a, b) => b.reps - a.reps || b.phrase.length - a.phrase.length || a.t - b.t);
  const kept = [];                                  // one chant at a time: the road is a lane, not a chorus
  for (const c of found) if (!kept.some((k) => c.t < k.t + k.dur + 1 && k.t < c.t + c.dur + 1)) kept.push(c);
  return kept.sort((a, b) => a.t - b.t);
}

/* ---- the road ------------------------------------------------------------ */

function dropEvents(ws, countEnds) {
  const near = (t) => countEnds.some((e) => t >= e && t - e <= 3);
  const runs = [];
  let run = null;
  for (const w of ws) {
    if (!DROPS.has(w.k) || (w.k === 'now' && !near(w.t))) continue;
    if (run && w.t - run.last <= DROP_RUN) { run.n++; run.last = w.t; continue; }
    run = { t: w.t, last: w.t, n: 1, label: w.k };
    runs.push(run);
  }
  const kept = [];
  for (const r of runs) {
    const prev = kept[kept.length - 1];
    if (prev && r.t - prev.t < DROP_MERGE) { prev.n += r.n; continue; }
    kept.push(r);
  }
  return kept.map((r) => ({ kind: 'drop', t: r3(r.t), label: r.label, strength: r2(clamp01(0.55 + 0.15 * r.n)), conf: 1, weight: 1 }));
}

function buildEvents(energy, binSec) {
  const sm = smooth(energy);
  const out = [];
  const near = 8;                                   // 4 s either side is a peak's own neighbourhood
  let lastPeak = -1e9;
  for (let i = 1; i < sm.length - 1; i++) {
    if (sm[i] < 0.35) continue;
    let top = true;
    for (let j = Math.max(0, i - near); j <= Math.min(sm.length - 1, i + near); j++) if (sm[j] > sm[i]) { top = false; break; }
    if (!top || i * binSec - lastPeak < PEAK_GAP_SEC) continue;
    let s = i;
    const back = Math.max(0, i - Math.round(RISE_SEC_MAX / binSec));
    for (let j = i; j >= back; j--) if (sm[j] < sm[s]) s = j;
    const rise = sm[i] - sm[s], len = (i - s) * binSec;
    if (rise < RISE_MIN || len < RISE_SEC_MIN) continue;
    lastPeak = i * binSec;
    out.push({ kind: 'build', t: r3(s * binSec), dur: r3(Math.min(BUILD_DUR_MAX, len)), label: 'build', conf: 1, weight: 1 });
    out.push({ kind: 'peak', t: r3(i * binSec), label: 'peak', conf: 1, weight: 1 });
    let e = i;
    const want = sm[i] - rise * 0.5;
    while (e < sm.length - 1 && sm[e] > want && (e - i) * binSec < RISE_SEC_MAX) e++;
    out.push({ kind: 'release', t: r3(Math.max(i + 1, e) * binSec), label: 'release', conf: 1, weight: 1 });
  }
  return out;
}

function silenceEvents(ws, durationSec) {
  const out = [];
  const gap = (t0, t1) => {
    const len = t1 - t0;
    if (len > QUIET_MIN) out.push({ kind: 'silence', t: r3(t0 + 0.15), dur: r3(Math.min(QUIET_MAX, len - 0.3)), label: 'quiet', conf: 1, weight: 1 });
  };
  if (!ws.length) { gap(0, durationSec); return out; }
  gap(0, ws[0].t);
  for (let i = 0; i < ws.length - 1; i++) gap(ws[i].t + ws[i].d, ws[i + 1].t);
  gap(ws[ws.length - 1].t + ws[ws.length - 1].d, durationSec);
  return out;
}

/** The plain treats: every trigger the maker heard first, then the structure words that fit between. */
function wordEvents(ws, hits, setById, busy) {
  const name = (id) => { const s = setById && setById.get ? setById.get(id) : null; return (s && s.name) || id; };
  const confAt = (t) => {
    let best = 1;
    for (const w of ws) { if (w.t > t + 0.6) break; if (t >= w.t - 0.4 && t <= w.t + w.d + 0.4) best = w.conf; }
    return r2(clamp01(best));
  };
  const kept = [];
  const free = (t) => !kept.some((k) => Math.abs(k.t - t) < WORD_GAP) && !busy.some((b) => t >= b[0] && t <= b[1]);
  for (const h of (hits || []).slice().sort((a, b) => a.t - b.t)) {
    if (!free(h.t)) continue;
    kept.push({ kind: 'word', t: r3(h.t), label: name(h.setId), conf: confAt(h.t), weight: 1 });
  }
  for (const w of ws) {
    if (!STRUCT.has(w.k) || w.conf < 0.5 || !free(w.t)) continue;
    kept.push({ kind: 'word', t: r3(w.t), label: w.k, conf: r2(clamp01(w.conf)), weight: 0.8 });
  }
  // and then the plain road: a spoken word every couple of seconds, so the stretch between two
  // triggers is something to drive rather than something to wait through.
  for (const w of ws) {
    if (w.k.length < FILL_LEN || w.conf < FILL_CONF || DULL.has(w.k) || !free(w.t)) continue;
    kept.push({ kind: 'word', t: r3(w.t), label: w.k, conf: r2(clamp01(w.conf)), weight: 0.6 });
  }
  return kept.sort((a, b) => a.t - b.t);
}

/* ---- the acts ------------------------------------------------------------ */

/** Crude on purpose: which words fall where, smoothed, run together, short ones folded in. */
export function actsFrom({ ws, hits, chants, drops, durationSec }) {
  const n = Math.max(1, Math.ceil(durationSec / ACT_WIN));
  const kinds = new Array(n).fill('free');
  for (let i = 0; i < n; i++) {
    const t0 = i * ACT_WIN, t1 = t0 + ACT_WIN;
    const score = { induction: 0, deepening: 0, triggers: 0, mantra: 0, wake: 0 };
    for (const w of ws) {
      if (w.t < t0 || w.t >= t1) continue;
      for (const k of Object.keys(ACT_WORDS)) if (ACT_WORDS[k].includes(w.k)) score[k]++;
    }
    for (const h of hits || []) if (h.t >= t0 && h.t < t1) score.triggers += 1;
    for (const d of drops) if (d.t >= t0 && d.t < t1) score.deepening += 2;
    for (const c of chants) {
      const over = Math.min(t1, c.t + c.dur) - Math.max(t0, c.t);
      if (over > 0) score.mantra += over / 5;
    }
    if (t1 <= durationSec * 0.12) score.induction += 3;
    if (t0 >= durationSec * 0.88) score.wake += 3;
    let best = '', top = 2.5;
    for (const k of ['triggers', 'mantra', 'deepening', 'wake', 'induction']) if (score[k] > top) { top = score[k]; best = k; }
    // a quiet window is not a new room: a script stays where it was until it says otherwise,
    // so nothing scoring carries the last kind forward and only the very first one is a guess.
    kinds[i] = best || (i ? kinds[i - 1] : 'induction');
  }
  for (let i = 1; i < n - 1; i++) if (kinds[i - 1] === kinds[i + 1] && kinds[i] !== kinds[i - 1]) kinds[i] = kinds[i - 1];
  const acts = [];
  for (let i = 0; i < n; i++) {
    const last = acts[acts.length - 1];
    if (last && last.kind === kinds[i]) last.t1 = Math.min(durationSec, (i + 1) * ACT_WIN);
    else acts.push({ kind: kinds[i], t0: i * ACT_WIN, t1: Math.min(durationSec, (i + 1) * ACT_WIN) });
  }
  for (let i = acts.length - 1; i > 0; i--) {
    if (acts[i].t1 - acts[i].t0 >= ACT_MIN_SEC) continue;
    acts[i - 1].t1 = acts[i].t1;
    acts.splice(i, 1);
  }
  while (acts.length > ACT_MAX) {                   // fold the shortest into the one before it
    let s = 1;
    for (let i = 2; i < acts.length; i++) if (acts[i].t1 - acts[i].t0 < acts[s].t1 - acts[s].t0) s = i;
    acts[s - 1].t1 = acts[s].t1;
    acts.splice(s, 1);
  }
  acts[0].t0 = 0;
  acts[acts.length - 1].t1 = durationSec;
  return acts.map((a) => ({ t0: r3(a.t0), t1: r3(a.t1), kind: a.kind, name: a.kind }));
}

/* ---- the whole road ------------------------------------------------------ */

/**
 * Everything between the triggers, from the peaks and the words already in
 * memory. `hits` and `setById` are the maker's own trigger scan, so a trigger
 * the author heard is a treat on the road under the recipe the author placed.
 * Nothing in here is hand authored and nothing in here touches the recipes.
 */
export function generate({ peaks, perSec, durationSec, words, hits = [], setById = null, binSec = BIN_SEC, now = new Date() }) {
  const dur = Math.max(0, Number(durationSec) || 0);
  const energy = energyFromPeaks(peaks, perSec, dur, binSec);
  const ws = readWords(words);
  const events = [];

  const runs = countRuns(ws);
  const countEnds = [];
  for (const run of runs) {
    countEnds.push(run[run.length - 1].t);
    run.forEach((w, i) => events.push({ kind: 'count', t: r3(w.t), label: String(w.v), n: w.v, of: run[0].v,
      last: i === run.length - 1, conf: r2(clamp01(w.conf)), weight: 1 }));
  }
  const drops = dropEvents(ws, countEnds);
  events.push(...drops);
  const chants = chantRuns(ws);
  for (const c of chants) events.push({ kind: 'chant', t: r3(c.t), label: c.phrase, reps: c.reps, period: c.period, dur: c.dur, conf: 1, weight: 1 });

  const busy = events.map((e) => [e.t - 1.2, e.t + (e.dur || 0) + 1.2]);
  events.push(...wordEvents(ws, hits, setById, busy));
  events.push(...buildEvents(energy, binSec));
  events.push(...silenceEvents(ws, dur));

  const road = events
    .filter((e) => e.t >= 0 && e.t <= dur)
    .sort((a, b) => a.t - b.t)
    .map((e, i) => ({ ...e, id: 'g' + i }));
  const acts = actsFrom({ ws, hits, chants, drops, durationSec: dur });

  const counts = { total: road.length, acts: acts.length, bins: energy.length };
  for (const e of road) counts[e.kind] = (counts[e.kind] || 0) + 1;
  return { binSec, energy, acts, events: road, counts, generatedAt: now.toISOString() };
}

/** The one line the status bar says after a run. */
export function roadLine(counts) {
  const order = ['word', 'drop', 'count', 'chant', 'build', 'peak', 'release', 'silence'];
  const bits = order.filter((k) => counts[k]).map((k) => counts[k] + ' ' + k);
  return 'road placed: ' + counts.total + ' of them (' + bits.join(', ') + '), ' + counts.acts + ' rooms';
}
