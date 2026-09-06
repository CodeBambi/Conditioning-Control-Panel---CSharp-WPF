/* ============================================================================
 * race/track.js - the loaded track: its clock, its intensity and its acts.
 * Implements the state half of CHART.md section `race/run.js + raceBoot.js`.
 *
 * Split out of run.js so the run brain stays about the world and this stays about
 * the file. Nothing here imports three or touches the DOM, so the whole clock runs
 * under node (`node race/smoke/track-run-check.mjs`).
 *
 * The clock is the file. `clock(t, playing)` is the host's 250 ms tick and is the
 * only thing that ever SETS the second; `step()` walks it forward between ticks off
 * performance.now(), and only while the host says it is playing. It reads the wall
 * itself rather than taking the run's frame delta on purpose: run.js clamps a frame
 * to 0.1 s so one long stall cannot teleport the kart, and a clock that inherited
 * that clamp would fall behind the voice on a slow machine and then be yanked
 * forward by the next host tick, dropping every word in between. `step(dt)` with an
 * explicit delta is the node harness's door in. A pause stops step advancing at all,
 * which is why the Brake, a host pause and a video pop all freeze the voice.
 *
 * One frame may still only carry MAX_STEP_SEC of the file. Integrating between 250 ms
 * ticks is all this is for, so a page that was backgrounded, hitched or fast-forwarded
 * must not leap the clock over whole words on the frame it comes back: it walks, and
 * the host's next tick puts it where the audio actually is.
 * ==========================================================================*/

import { createScheduler } from './chart.js';
import { BUBBLE_KINDS } from './bubbleKinds.js';

/** Intensity never quite reaches zero: even a silent stretch keeps the tube alive. */
const FLOOR = 0.05;
/** Seconds the intensity takes to follow the energy curve (CHART.md: smoothed over 2 s). */
const SMOOTH_SEC = 2;
/** The run ends this far before the last sample rather than on a clock that may never arrive. */
const END_PAD = 0.25;
/** The most of the file one frame may carry on its own: see the header. */
const MAX_STEP_SEC = 1;
/** The effect bubbles a trigger phrase may wear, dealt round robin over the chart's lexicon. */
const TRIGGER_KINDS = ['flash', 'subliminal', 'pink', 'spiral', 'glitch', 'freeze']
  .filter((id) => BUBBLE_KINDS.some((k) => k.id === id));

const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);

/**
 * Every distinct trigger phrase in the chart gets its own bubble, the same one every time the file
 * is loaded: the lexicon and the spoken labels sorted, then dealt round robin. The player learns
 * "good girl is the pink one" over a track and that reading holds for the whole file.
 */
function mapTriggers(chart) {
  const m = new Map();
  const words = new Set();
  for (const w of chart.analysis.lexicon || []) if (w) words.add(String(w).toLowerCase());
  for (const e of chart.events) if (e.kind === 'trigger' && e.label) words.add(e.label);
  [...words].sort().forEach((w, i) => m.set(w, TRIGGER_KINDS[i % TRIGGER_KINDS.length]));
  return m;
}

/**
 * createTrackState() -> the run's one handle on the loaded file. `track` is the CHART.md object
 * ({ chart, sched, t, playing, name, durationSec }) or null for the seeded run.
 */
export function createTrackState(opts = {}) {
  const leadSec = opts.leadSec;
  const now = typeof opts.now === 'function' ? opts.now
    : (typeof performance === 'object' && performance && performance.now) ? () => performance.now() : () => Date.now();
  let track = null, sched = null, triggerKinds = new Map();
  let intensity = FLOOR, act = null, ended = false, mark = 0;

  /** Load a chart (or null to go back to the seeded run). Returns the new `track`. */
  function setTrack(chart) {
    if (!chart) { track = null; sched = null; triggerKinds = new Map(); intensity = FLOOR; act = null; ended = false; return null; }
    sched = createScheduler(chart, leadSec != null ? { leadSec } : {});
    const ch = sched.chart;
    triggerKinds = mapTriggers(ch);
    intensity = Math.max(FLOOR, sched.energyAt(0));
    act = sched.actAt(0);            // the opening act is where we already are, never a change
    ended = false; mark = 0;
    track = { chart: ch, sched, t: 0, playing: false, name: ch.source.name, durationSec: ch.source.durationSec };
    return track;
  }

  /** The words pass landing on a partial chart: keep the clock, adopt only what is still ahead. */
  function replace(chart) {
    if (!track) return setTrack(chart);
    const ch = sched.replace(chart);
    track.chart = ch;
    track.name = ch.source.name;
    track.durationSec = ch.source.durationSec;
    triggerKinds = mapTriggers(ch);
    return track;
  }

  /** The host's clock tick. The only thing that sets the second; `playing` gates step(). */
  function clock(t, playing) {
    if (!track) return;
    const v = Number(t);
    if (isFinite(v)) track.t = Math.max(0, Math.min(track.durationSec, v));
    track.playing = playing !== false;
    mark = now();
  }

  /**
   * One frame. Walks the clock on between host ticks off the wall, eases the intensity toward the
   * energy curve and reports the act. `dt` overrides the wall (the node harness). Null without a track.
   */
  function step(dt) {
    if (!track) return null;
    const at = now();
    const given = Number(dt);
    const d = isFinite(given) && given >= 0 ? given
      : Math.min(MAX_STEP_SEC, Math.max(0, mark ? (at - mark) / 1000 : 0));
    mark = at;
    if (track.playing && !ended) track.t = Math.min(track.durationSec, track.t + d);
    const target = Math.max(FLOOR, sched.energyAt(track.t));
    intensity += (target - intensity) * Math.min(1, d / SMOOTH_SEC);
    const a = sched.actAt(track.t);
    const actChanged = !!a && (!act || a.id !== act.id);
    if (a) act = a;
    if (!ended && track.t >= track.durationSec - END_PAD) ended = true;
    return { t: track.t, dt: d, intensity: Math.max(FLOOR, clamp01(intensity)), act, actChanged, ended };
  }

  return {
    setTrack, replace, clock, step,
    /** Everything the scheduler wants spawned this frame, each with the depth it belongs at. */
    due(kartD, kartSpeed) { return track ? sched.update(track.t, kartD, kartSpeed) : []; },
    /** The player met this event: popped its bubble, took its drop. */
    taken(id) { if (sched) sched.taken(id); },
    stats() { return sched ? sched.stats() : { total: 0, fired: 0, countable: 0, taken: 0, missed: 0 }; },
    /** The end-of-run fields CHART.md adds to the summary and to `run-ended`. */
    summary() {
      if (!track) return null;
      const s = sched.stats();
      return { name: track.name, hash: track.chart.source.hash, durationSec: track.durationSec, taken: s.taken, countable: s.countable };
    },
    /** The host said the file is over, wherever our clock had got to. */
    end() { ended = true; },
    get track() { return track; },
    get act() { return act; },
    get intensity() { return Math.max(FLOOR, clamp01(intensity)); },
    get triggerKinds() { return triggerKinds; },
    get ended() { return ended; },
  };
}

// self-check: node race/smoke/track-run-check.mjs drives a whole demo track through this.
