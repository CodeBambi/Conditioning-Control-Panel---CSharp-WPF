/* ============================================================================
 * race/chart.js - The Caucus Race track charts: the file is the track.
 * Implements CHART.md section `race/chart.js`.
 *
 * A chart is timestamps and labels, never audio: an energy curve in fixed bins, a list
 * of acts (induction, deepening, triggers, mantra, build, silence, wake, free) and a
 * sorted list of events, the words the voice says. The host analyses the file and posts
 * one of these; the page schedules the whole run off it. Nothing here imports three,
 * touches the DOM or reads a clock, so it runs under `node race/smoke/chart-check.mjs`.
 *
 * The scheduler is the piece that matters. The clock is the file, so an event at
 * track time `te` cannot wait for the kart: it is handed out `leadSec` before its
 * second at the depth the kart will have reached by then, which is why the pop lands
 * on the spoken word whatever the player did with the throttle. An event first seen
 * after its second is dropped and counted as missed, never spawned behind the kart.
 * ==========================================================================*/

import { ROOM_IDS, KART_BASE_SPEED, makeRng } from './consts.js';

export const CHART_VERSION = 1;

/** English v1 structure words. The words pass grammar is this list + the trigger lexicon + [unk]. */
export const STRUCTURE_WORDS = [
  'drop', 'dropping', 'sleep', 'sleepy', 'asleep', 'deeper', 'deep', 'down', 'sink', 'sinking',
  'relax', 'relaxing', 'breathe', 'breath', 'blank', 'empty', 'obey', 'listen', 'focus',
  'surrender', 'melt', 'float', 'floating', 'heavy', 'wake', 'awake', 'waking', 'up', 'open',
  'count', 'zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten',
  'now', 'good', 'girl', 'bimbo', 'doll', 'mind', 'mindless', 'pink', 'spiral', 'trance', 'trigger',
];

/** A structure word outside a countdown that reads as a drop ('now' only right after a count). */
export const DROP_WORDS = ['drop', 'dropping', 'sleep', 'asleep', 'deeper', 'sink', 'sinking', 'now'];

export const EVENT_KINDS = ['trigger', 'word', 'count', 'drop', 'chant', 'build', 'peak', 'release', 'silence'];
export const ACT_KINDS = ['induction', 'deepening', 'triggers', 'mantra', 'build', 'silence', 'wake', 'free'];

/** Default act -> room. The analyzer may override a room to avoid repeating one back to back. */
export const ACT_ROOM = {
  induction: 'teagarden', deepening: 'undertow', triggers: 'toybox', mantra: 'chapel',
  build: 'mirrors', silence: 'greyward', wake: 'coronation', free: 'casino',
};

const LEAD_SEC = 2.5;         // seconds of warning an event gets by default
const MIN_LEAD_SEC = 1.2;     // and the floor, so a caller cannot spawn a bubble on the bumper
const MIN_AHEAD_SEC = 0.25;   // an event never materialises behind the kart
const MISS_SEC = 0.5;         // first seen this late and it is gone, not spawned late
const SEEK_SEC = 1.0;         // a clock that jumps back further than this is a seek
const COUNTABLE = new Set(['trigger', 'word', 'count', 'drop']);

const num = (v, d) => (typeof v === 'number' && isFinite(v) ? v : d);
const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
const clamp01 = (v) => clamp(v, 0, 1);
const str = (v, d = '') => (typeof v === 'string' ? v : d);

/* ---- normalize ---------------------------------------------------------- */

/**
 * Validate a chart off the wire and fill in what the page assumes: contiguous sorted acts, sorted
 * events with unique ids, clamped ranges. Throws a plain Error the caller can put on screen, and
 * freezes the result so a run can never scribble on the host's chart.
 */
export function normalizeChart(json) {
  if (!json || typeof json !== 'object') throw new Error('chart: expected an object');
  const version = num(json.version, CHART_VERSION);
  if (version !== CHART_VERSION) throw new Error('chart: version ' + version + ' is not supported, this build reads version ' + CHART_VERSION);
  const src = (json.source && typeof json.source === 'object') ? json.source : {};
  const durationSec = num(src.durationSec, num(json.durationSec, 0));
  if (!(durationSec > 0)) throw new Error('chart: source.durationSec is missing or not a positive number');

  const binSec = clamp(num(json.binSec, 0.5), 0.05, 5);
  const energy = (Array.isArray(json.energy) ? json.energy : []).map((v) => clamp01(num(v, 0)));
  const an = (json.analysis && typeof json.analysis === 'object') ? json.analysis : {};

  return Object.freeze({
    version: CHART_VERSION, binSec, energy: Object.freeze(energy),
    acts: normalizeActs(Array.isArray(json.acts) ? json.acts : [], durationSec),
    events: normalizeEvents(Array.isArray(json.events) ? json.events : [], durationSec),
    source: Object.freeze({ name: str(src.name, 'track'), hash: str(src.hash, ''), durationSec, sampleRate: num(src.sampleRate, 16000) }),
    analysis: Object.freeze({
      energy: str(an.energy, ''), words: str(an.words, 'none'), generatedAt: str(an.generatedAt, ''), partial: an.partial === true,
      lexicon: Object.freeze((Array.isArray(an.lexicon) ? an.lexicon : []).filter((w) => typeof w === 'string')),
    }),
  });
}

function normalizeActs(raw, durationSec) {
  const out = raw
    .filter((a) => a && typeof a === 'object' && isFinite(num(a.t0, NaN)))
    .map((a) => ({ t0: clamp(num(a.t0, 0), 0, durationSec), t1: clamp(num(a.t1, durationSec), 0, durationSec), src: a }))
    .sort((a, b) => a.t0 - b.t0)
    .map((a, i) => {
      const kind = ACT_KINDS.includes(a.src.kind) ? a.src.kind : 'free';
      const room = ROOM_IDS.includes(a.src.room) ? a.src.room : ACT_ROOM[kind];
      return { id: i, t0: a.t0, t1: a.t1, kind, room, name: str(a.src.name, kind) };
    });
  if (!out.length) out.push({ id: 0, t0: 0, t1: durationSec, kind: 'free', room: ACT_ROOM.free, name: 'free' });
  // Contiguous by construction: the first starts at 0, each one ends where the next begins.
  out[0].t0 = 0;
  for (let i = 0; i < out.length - 1; i++) out[i].t1 = out[i + 1].t0;
  out[out.length - 1].t1 = durationSec;
  return Object.freeze(out.filter((a) => a.t1 > a.t0).map((a, i) => Object.freeze(Object.assign(a, { id: i }))));
}

function normalizeEvents(raw, durationSec) {
  const seen = new Set();
  const out = raw
    .filter((e) => e && typeof e === 'object' && EVENT_KINDS.includes(e.kind) && isFinite(num(e.t, NaN)))
    .map((e, i) => ({ e, i, t: clamp(num(e.t, 0), 0, durationSec) }))
    .sort((a, b) => (a.t - b.t) || (a.i - b.i))
    .map(({ e, t }, i) => {
      let id = str(e.id, '');
      if (!id || seen.has(id)) id = 'e' + i;
      seen.add(id);
      const ev = { id, t, kind: e.kind, label: str(e.label, '').toLowerCase(), conf: clamp01(num(e.conf, 1)),
        dur: clamp(num(e.dur, 0), 0, durationSec - t), weight: clamp01(num(e.weight, 1)) };
      if (e.kind === 'count') { ev.n = num(e.n, 0); ev.of = num(e.of, 0); ev.last = e.last === true; }
      if (e.kind === 'drop') ev.strength = clamp01(num(e.strength, 1));
      if (e.kind === 'chant') { ev.reps = Math.max(1, Math.round(num(e.reps, 3))); ev.period = Math.max(0, num(e.period, 0)); }
      return Object.freeze(ev);
    });
  return Object.freeze(out);
}

/* ---- the demo track ----------------------------------------------------- */

// One act of every kind, so `?chart=demo` walks every room and every cue with no audio at all.
const DEMO_ACTS = [
  { kind: 'induction', frac: 0.18, name: 'the settle', base: 0.34 },
  { kind: 'deepening', frac: 0.16, name: 'the undertow', base: 0.26 },
  { kind: 'triggers', frac: 0.16, name: 'the toybox', base: 0.55 },
  { kind: 'mantra', frac: 0.14, name: 'the chant', base: 0.48 },
  { kind: 'build', frac: 0.14, name: 'the climb', base: 0.4 },
  { kind: 'silence', frac: 0.08, name: 'the quiet', base: 0.03 },
  { kind: 'wake', frac: 0.14, name: 'the way up', base: 0.72 },
];
const DEMO_TRIGGERS = ['good girl', 'bimbo', 'doll', 'spiral', 'blank'];
const SETTLE_WORDS = ['relax', 'breathe', 'listen', 'focus', 'down'];
const SINK_WORDS = ['deeper', 'heavy', 'float', 'sink'];

/**
 * A deterministic synthetic chart carrying every event kind: a settle, a countdown into a drop, a
 * room of triggers, a chant, a climb that peaks and releases, a fogged quiet and a wake. Same seed,
 * same track, forever, so the headless checks and the standalone page agree on what they see. Takes
 * the CHART.md options object or a bare duration in seconds.
 */
export function demoChart(opts = {}) {
  const o = (typeof opts === 'number') ? { durationSec: opts } : (opts || {});
  const durationSec = clamp(num(o.durationSec, 240), 40, 7200);
  const rng = makeRng(num(o.seed, 1) | 0);
  const binSec = 0.5, events = [], acts = [];
  let n = 0, cut = 0;
  const ev = (kind, t, extra) => { events.push(Object.assign({ id: 'd' + (n++), kind, t }, extra)); };

  DEMO_ACTS.forEach((a, i) => {
    const t1 = (i === DEMO_ACTS.length - 1) ? durationSec : Math.min(durationSec, cut + durationSec * a.frac);
    acts.push({ id: i, t0: cut, t1, kind: a.kind, room: ACT_ROOM[a.kind], name: a.name });
    cut = t1;
  });

  for (const a of acts) {
    const len = a.t1 - a.t0;
    if (a.kind === 'induction') {
      for (let k = 0, t = a.t0 + 6; t < a.t1 - 2; t += 8.5, k++) ev('word', t, { label: SETTLE_WORDS[k % SETTLE_WORDS.length], weight: 0.8 });
    } else if (a.kind === 'deepening') {
      for (let k = 0, t = a.t0 + 5; t < a.t1 - 20; t += 7, k++) ev('word', t, { label: SINK_WORDS[k % SINK_WORDS.length], weight: 0.9 });
      const from = Math.max(a.t0 + 2, a.t1 - 17);              // ten, nine, eight, and away
      for (let i = 10; i >= 1; i--) ev('count', from + (10 - i) * 1.4, { label: String(i), n: i, of: 10, last: i === 1 });
      ev('drop', from + 9 * 1.4 + 1.1, { strength: 1, label: 'drop' });
    } else if (a.kind === 'triggers') {
      for (let k = 0, t = a.t0 + 4; t < a.t1 - 2; t += 6.5, k++) ev('trigger', t, { label: DEMO_TRIGGERS[k % DEMO_TRIGGERS.length], conf: 0.7 + rng() * 0.3 });
    } else if (a.kind === 'mantra') {
      const period = 2.4, reps = Math.max(3, Math.min(8, Math.floor((len - 6) / period)));
      ev('chant', a.t0 + 3, { label: 'good girl', reps, period, dur: reps * period });
      ev('trigger', a.t0 + 4 + reps * period, { label: 'doll', conf: 0.9 });
    } else if (a.kind === 'build') {
      const dur = Math.max(6, Math.min(12, len - 8));
      ev('build', a.t0 + 3, { dur });
      ev('peak', a.t0 + 3 + dur, {});
      ev('drop', a.t0 + 3.2 + dur, { strength: 0.85, label: 'sink' });
      ev('release', a.t0 + 5.4 + dur, {});
    } else if (a.kind === 'silence') {
      ev('silence', a.t0 + 0.5, { dur: Math.max(3, len - 1.5) });
    } else if (a.kind === 'wake') {
      ['wake', 'awake', 'up'].forEach((w, k) => ev('word', a.t0 + 3 + k * 4.5, { label: w }));
      ev('trigger', Math.min(a.t1 - 2, a.t0 + 17), { label: 'good girl', conf: 0.95 });
    }
  }

  const bins = Math.ceil(durationSec / binSec);
  const energy = new Array(bins);
  for (let i = 0; i < bins; i++) {
    const t = (i + 0.5) * binSec;
    const a = acts.find((x) => t >= x.t0 && t < x.t1) || acts[acts.length - 1];
    const frac = (t - a.t0) / Math.max(1, a.t1 - a.t0);
    let v = DEMO_ACTS.find((x) => x.kind === a.kind).base;
    if (a.kind === 'build') v = 0.32 + 0.62 * frac;            // the climb is the curve, not a level
    if (a.kind === 'wake') v = 0.55 + 0.35 * frac;
    energy[i] = clamp01(v + (rng() - 0.5) * (a.kind === 'silence' ? 0.02 : 0.08));
  }

  return normalizeChart({
    version: CHART_VERSION, binSec, energy, acts, events,
    source: { name: 'the demo track', hash: 'demo', durationSec, sampleRate: 16000 },
    analysis: { energy: 'demo-v1', words: 'demo-v1', lexicon: DEMO_TRIGGERS, generatedAt: '', partial: false },
  });
}

/* ---- the scheduler ------------------------------------------------------ */

/**
 * Walk a chart against the track clock. `update` hands back everything that has to be spawned on
 * this frame, each with the depth its bubble belongs at, so the run only has to place it.
 */
export function createScheduler(chart, opts = {}) {
  let ch = (chart && chart.version === CHART_VERSION && Object.isFrozen(chart)) ? chart : normalizeChart(chart);
  const leadSec = Math.max(MIN_LEAD_SEC, num(opts.leadSec, LEAD_SEC));
  const fired = new Set(), takenIds = new Set();
  let cursor = 0, lastT = 0, missed = 0;

  // The cursor is the first event nobody has looked at yet: after a seek or a swap it walks past
  // anything already fired, so update() stays a straight run down the list.
  const seekCursor = () => { cursor = 0; while (cursor < ch.events.length && fired.has(ch.events[cursor].id)) cursor++; };

  return {
    /**
     * @param t track seconds, @param kartD the kart's depth, @param kartSpeed metres a second. A
     * function in place of kartD is the CHART.md callback form, fire(event, dueIn). Returns
     * [{ event, dueIn, d }] for everything due this frame, in chart order.
     */
    update(t, kartD, kartSpeed) {
      let fire = null;
      if (typeof kartD === 'function') { fire = kartD; kartD = 0; kartSpeed = KART_BASE_SPEED; }
      const d0 = num(kartD, 0);
      const speed = Math.max(0, num(kartSpeed, KART_BASE_SPEED));
      if (t < lastT - SEEK_SEC) {                              // the host seeked: let the future back in
        for (const e of ch.events) if (e.t > t) fired.delete(e.id);
        seekCursor();
      }
      lastT = t;
      const due = [];
      while (cursor < ch.events.length) {
        const e = ch.events[cursor];
        if (e.t - leadSec > t) break;
        cursor++;
        if (fired.has(e.id)) continue;
        fired.add(e.id);
        const dueIn = e.t - t;
        if (dueIn < -MISS_SEC) { missed++; continue; }         // the voice already said it, let it go
        due.push({ event: e, dueIn, d: d0 + speed * Math.max(dueIn, MIN_AHEAD_SEC) });
        if (fire) fire(e, dueIn);
      }
      return due;
    },
    actAt(t) {
      for (const a of ch.acts) if (t >= a.t0 && t < a.t1) return a;
      return (ch.acts.length && t >= ch.source.durationSec) ? ch.acts[ch.acts.length - 1] : null;
    },
    energyAt(t) {
      const bins = ch.energy.length;
      if (!bins || t < 0 || t > ch.source.durationSec) return 0;
      const p = clamp(t / ch.binSec - 0.5, 0, bins - 1);
      const i = Math.floor(p);
      const a = ch.energy[i], b = ch.energy[Math.min(bins - 1, i + 1)];
      return clamp01(a + (b - a) * (p - i));
    },
    /** Swap in an upgraded chart (the words pass landing on a partial one) without re-firing. */
    replace(next) {
      ch = (next && next.version === CHART_VERSION && Object.isFrozen(next)) ? next : normalizeChart(next);
      for (const e of ch.events) if (e.t <= lastT) fired.add(e.id);   // only the future is adopted
      seekCursor();
      return ch;
    },
    /** The player met this event: popped its bubble, took the drop. */
    taken(id) { if (id) takenIds.add(id); },
    stats() {
      let countable = 0;
      for (const e of ch.events) if (COUNTABLE.has(e.kind)) countable++;
      return { total: ch.events.length, fired: fired.size, countable, taken: takenIds.size, missed };
    },
    reset() { fired.clear(); takenIds.clear(); cursor = 0; lastT = 0; missed = 0; },
    get chart() { return ch; },
    get lastT() { return lastT; },
    get leadSec() { return leadSec; },
  };
}
