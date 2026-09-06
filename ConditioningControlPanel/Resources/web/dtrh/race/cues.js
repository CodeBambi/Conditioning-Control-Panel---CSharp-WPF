/* ============================================================================
 * race/cues.js - a chart event turned into the thing the run does about it.
 * Implements CHART.md section `race/cues.js` (the plain mapping, PR c2).
 *
 * One pure function over a flat table: no three, no DOM, no clock, no state, so it
 * runs under node beside chart.js. `cueFor` decides WHICH of the things the run
 * already knows how to spend an event is worth (a bubble, a jump, a mood, a boost)
 * and never HOW: run.js owns every verb, this file only names them.
 *
 * `at` on a spawn is seconds relative to the event's own second: 0 puts the bubble
 * on the spoken word, 0.5 half a second behind it. run.js turns that into a depth
 * at the kart's current speed, which is why the pop lands on the word whatever the
 * player did with the throttle.
 *
 * This is deliberately the flat reading of the table. PR c3 makes it sing: weight
 * and confidence, the act's own colour, the room's bubble bias, EMI's poses. The
 * ctx fields c3 wants (energy, act, room, intensity) are already handed in here so
 * that pass never has to touch run.js again.
 * ==========================================================================*/

import { LANE_H, CEILING_H, ROAD_HALF_W } from './consts.js';

/** A trigger phrase nobody has assigned a bubble to still gets one: the strobe. */
const FALLBACK_TRIGGER = 'flash';
/** The lanes a spoken word may land in. Narrower than the road: the kart has to steer, not lunge. */
const LANE_X = [-1.6, -0.8, 0, 0.8, 1.6];
/** Height of an air bubble over the road, and how much each one of a drop's three climbs. */
const AIR_H = 2.6, AIR_RISE = 0.6;
/** A drop is three golden rings through the air, one on the word and two behind it. */
const DROP_AT = [0.2, 0.5, 0.8], DROP_X = [-1.1, 0, 1.1];
/** A peak dumps this many treats out of the ceiling, this far apart. */
const PEAK_N = 6, PEAK_GAP = 0.25;
/** The chant's two lanes, and the fallback beat when the analyzer sent no period. */
const CHANT_X = 1.2, CHANT_PERIOD = 1.2, CHANT_MAX = 16;
/** A build hands over at most this many seconds of boost. */
const BOOST_CAP = 4;

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const laneX = (rng) => LANE_X[Math.min(LANE_X.length - 1, (rng() * LANE_X.length) | 0)];
const spread = (rng) => clamp((rng() * 2 - 1) * (ROAD_HALF_W - 0.8), -ROAD_HALF_W + 0.6, ROAD_HALF_W - 0.6);

/** Every field CHART.md promises, so run.js never has to test for one. */
function blank() {
  return { spawn: [], jump: 0, mix: null, mood: null, pose: null, toast: null,
    word: null, fog: null, boost: 0, density: null, holdSec: 0 };
}

/** The bubble a spoken trigger wears. The map is built per track from the chart's lexicon. */
function triggerKind(label, kinds) {
  const id = (kinds && typeof kinds.get === 'function') ? kinds.get(label) : null;
  return id || FALLBACK_TRIGGER;
}

/**
 * @param event a chart event (race/chart.js normalizeChart shape)
 * @param ctx { energy, act, room, intensity, rng, triggerKinds } - only rng and triggerKinds are
 *        read in c2; the rest is the colour c3 mixes in.
 * @returns the cue, or null for an event kind this build has nothing to say about.
 */
export function cueFor(event, ctx = {}) {
  if (!event || typeof event.kind !== 'string') return null;
  const rng = typeof ctx.rng === 'function' ? ctx.rng : Math.random;
  const cue = blank();

  switch (event.kind) {
    // the voice said a trigger phrase: its own effect bubble, in a lane, with the word on the chrome
    case 'trigger':
      cue.spawn.push({ kindId: triggerKind(event.label, ctx.triggerKinds), placement: 'lane', x: laneX(rng), h: LANE_H, at: 0 });
      cue.word = event.label || null;
      break;

    // a structure word (drop, deeper, breathe): a plain treat to drive through
    case 'word':
      cue.spawn.push({ kindId: 'treat', placement: 'lane', x: laneX(rng), h: LANE_H, at: 0 });
      break;

    // a number inside a countdown: a golden ring in the air, and the last one throws the kart at it
    case 'count':
      cue.spawn.push({ kindId: 'golden', placement: 'air', x: laneX(rng) * 0.5, h: AIR_H, at: 0 });
      if (event.last) cue.jump = 6;
      break;

    // the drop: a jump, a spiral over the world, and three golden rings climbing away from the word
    case 'drop':
      cue.jump = 7;
      cue.mix = 'spiral';
      cue.mood = 'streamed';
      for (let i = 0; i < DROP_AT.length; i++) {
        cue.spawn.push({ kindId: 'golden', placement: 'air', x: DROP_X[i], h: AIR_H + i * AIR_RISE, at: DROP_AT[i] });
      }
      break;

    // the same phrase over and over: a lane of treats in the chant's own rhythm, side to side
    case 'chant': {
      const reps = clamp(Math.round(Number(event.reps) || 3), 1, CHANT_MAX);
      const period = Number(event.period) > 0 ? Number(event.period) : CHANT_PERIOD;
      for (let k = 0; k < reps; k++) {
        cue.spawn.push({ kindId: 'treat', placement: 'lane', x: (k % 2 ? CHANT_X : -CHANT_X), h: LANE_H, at: k * period });
      }
      cue.word = event.label || null;
      break;
    }

    // the RMS climbing: a push in the back and a thicker road while it lasts
    case 'build':
      cue.boost = clamp(Number(event.dur) || 0, 0, BOOST_CAP);
      cue.density = 1.6;
      break;

    // the top of the climb: treats out of the ceiling
    case 'peak':
      for (let i = 0; i < PEAK_N; i++) {
        cue.spawn.push({ kindId: 'treat', placement: 'rain', x: spread(rng), h: CEILING_H, at: i * PEAK_GAP });
      }
      break;

    // the fall away from a peak: she settles, the road thins out
    case 'release':
      cue.mood = 'calm';
      cue.density = 0.6;
      break;

    // nothing in the file at all: a fogged straight with nothing in it, for as long as the quiet runs
    case 'silence':
      cue.fog = 1;
      cue.density = 0;
      cue.holdSec = Math.max(0, Number(event.dur) || 0);
      break;

    default:
      return null;
  }
  return cue;
}

// self-check: node race/smoke/track-run-check.mjs walks every kind through this table.
