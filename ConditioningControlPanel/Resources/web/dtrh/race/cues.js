/* ============================================================================
 * race/cues.js - a chart event turned into the thing the run does about it.
 * Implements CHART.md section `race/cues.js`: the mapping (PR c2) and its feel (PR c3).
 *
 * One pure function over a table: no three, no DOM, no clock, no state, so it runs
 * under node beside chart.js. `cueFor` decides WHICH of the things the run already
 * knows how to spend an event is worth (a bubble, a jump, a mood, a pose, a boost)
 * and never HOW: run.js owns every verb, this file only names them.
 *
 * `at` on a spawn is seconds relative to the event's own second: 0 puts the bubble
 * on the spoken word, 0.5 half a second behind it. run.js turns that into a depth
 * at the kart's current speed, which is why the pop lands on the word whatever the
 * player did with the throttle.
 *
 * The feel pass (c3) reads the colour c2 handed in and left alone:
 *   conf     - the word spotter runs a closed grammar and will hear "wake" in the
 *              middle of a trance at full confidence, so a guess is worth less than
 *              a certainty: an unsure trigger is a plain treat, an unsure word is
 *              nothing at all (a guess must never cost the player a miss), a lone
 *              number outside a countdown is nothing, and a wake word before the
 *              track is on its way up is nothing.
 *   room     - an unmapped trigger wears the room's own effect and a peak rains
 *              the room's own bubble, so the file's acts read on the road.
 *   weight / strength / intensity - how much of a cue there is: rings on a drop,
 *              treats in a chant, the size of the rain.
 *   pose / toast - she reaches for a trigger, braces for the last count, cheers a
 *              peak; the count and the drop word go on the chrome.
 * ==========================================================================*/

import { LANE_H, CEILING_H, LANE_X_MAX } from './consts.js';

/** A trigger phrase nobody has assigned a bubble to wears the room's own effect, else this. */
const FALLBACK_TRIGGER = 'flash';
/** Which effect an unmapped trigger wears per room (rooms.js ids; chart.js ACT_ROOM picks them). */
const ROOM_TRIGGER = {
  teagarden: 'flash', undertow: 'spiral', toybox: 'pink', chapel: 'spiral',
  mirrors: 'glitch', greyward: 'freeze', coronation: 'prism', casino: 'lucky',
};
/** What a peak rains per room; anywhere else it is plain treats. */
const ROOM_RAIN = { casino: 'lucky', coronation: 'golden', chapel: 'prism', mirrors: 'prism' };
/** Below this the spotter was guessing: the trigger is a treat, not its effect, and no word on the chrome. */
const TRIGGER_SURE = 0.55;
/** Below this a structure word is nothing; a guess must never cost the player a miss. */
const WORD_SURE = 0.5;
/** A number on its own (the spotter hears "one" in "someone"): only a countdown may count. */
const NUMBER_WORD = /^(\d+|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty)$/;
/** Words that only mean anything on the way up; the spotter hears "wake" mid-trance at conf 1. */
const WAKE_WORDS = new Set(['wake', 'awake', 'waking', 'up', 'open']);
/** Acts a wake word is welcome in (chart.js ACT_KINDS). */
const WAKE_ACTS = new Set(['wake', 'free']);
/** Words that lift: their treat hangs in the air; every other structure word sits in a lane. */
const FLOAT_WORDS = new Set(['float', 'floating', 'up', 'open', 'light', 'rise', 'lift']);
/** The lanes a spoken word may land in. Narrower than the road: the kart has to steer, not lunge. */
const LANE_X = [-1.6, -0.8, 0, 0.8, 1.6];
/** Height of an air bubble over the road, how much each of a drop's rings climbs, a floating word's hang. */
const AIR_H = 2.6, AIR_RISE = 0.6, FLOAT_H = 1.9;
/** A drop is golden rings through the air, one on the word and the rest behind it. */
const DROP_AT = [0.2, 0.5, 0.8], DROP_X = [-1.1, 0, 1.1];
/** A drop this weak is a dip, not a fall: no spiral over the world, fewer rings, a lower jump. */
const DROP_SOFT = 0.6;
/** A peak rains between these many, by intensity, this far apart. */
const PEAK_MIN = 4, PEAK_MAX = 8, PEAK_GAP = 0.25;
/** The chant's two lanes, and the fallback beat when the analyzer sent no period. */
const CHANT_X = 1.2, CHANT_PERIOD = 1.2, CHANT_MAX = 16;
/** Every this-many chant treats, one is gold. */
const CHANT_GOLD_EVERY = 4;
/** A build hands over at most this many seconds of boost. */
const BOOST_CAP = 4;

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const conf01 = (e) => (Number.isFinite(Number(e.conf)) ? clamp(Number(e.conf), 0, 1) : 1);
const weight01 = (e) => (Number.isFinite(Number(e.weight)) ? clamp(Number(e.weight), 0, 1) : 1);
const laneX = (rng) => LANE_X[Math.min(LANE_X.length - 1, (rng() * LANE_X.length) | 0)];
const spread = (rng) => clamp((rng() * 2 - 1) * LANE_X_MAX, -LANE_X_MAX, LANE_X_MAX);
const roomId = (ctx) => (ctx.room && typeof ctx.room.id === 'string' ? ctx.room.id : (ctx.act && ctx.act.room) || null);

/** Every field CHART.md promises, so run.js never has to test for one. */
function blank() {
  return { spawn: [], fx: [], jump: 0, mix: null, mood: null, pose: null, toast: null,
    word: null, fog: null, boost: 0, density: null, holdSec: 0 };
}

/* ---- the hand override (CHART.md) --------------------------------------- */

/** A wall is a full row the kart cannot steer around: the five road lanes plus one over the top. */
const WALL_X = [-2.2, -1.1, 0, 1.1, 2.2];
const WALL_AIR_H = AIR_H;
/** The fields the author may set on a cue, copied straight through when they are present. */
const HAND_FIELDS = ['jump', 'mix', 'mood', 'pose', 'toast', 'word', 'fog', 'boost', 'density', 'holdSec'];

/**
 * The author already said what this second is worth: build the cue from their words, not the table.
 * `cue` has been through chart.js sanitizeCue, so every id, placement and number in here is already
 * legal. The only thing left to do is expand `wall` into the six bubbles it stands for.
 */
function fromHand(hand, ctx) {
  const cue = blank();
  if (Array.isArray(hand.spawn)) {
    for (const sp of hand.spawn) {
      const placement = sp.placement || 'lane';
      const h = (sp.h != null) ? sp.h : (placement === 'rain' ? CEILING_H : placement === 'air' ? AIR_H : LANE_H);
      cue.spawn.push({ kindId: sp.kindId, placement, x: Number(sp.x) || 0, h, at: Number(sp.at) || 0 });
    }
  }
  if (hand.wall) {
    for (const x of WALL_X) cue.spawn.push({ kindId: hand.wall, placement: 'lane', x, h: LANE_H, at: 0 });
    cue.spawn.push({ kindId: hand.wall, placement: 'air', x: 0, h: WALL_AIR_H, at: 0 });
  }
  if (Array.isArray(hand.fx)) cue.fx = hand.fx.map((f) => ({ id: f.id, strength: f.strength, dur: f.dur }));
  for (const k of HAND_FIELDS) if (hand[k] !== undefined) cue[k] = hand[k];
  return cue;
}

/** The bubble a spoken trigger wears: the chart's own map, else the room's effect, else the strobe. */
function triggerKind(label, ctx) {
  const kinds = ctx.triggerKinds;
  const id = (kinds && typeof kinds.get === 'function') ? kinds.get(label) : null;
  return id || ROOM_TRIGGER[roomId(ctx)] || FALLBACK_TRIGGER;
}

/**
 * @param event a chart event (race/chart.js normalizeChart shape)
 * @param ctx { energy, act, room, intensity, rng, triggerKinds }
 * @returns the cue, or null for an event this build has nothing to say about (an unknown
 *          kind, or a word the feel pass decided the spotter only guessed at).
 */
export function cueFor(event, ctx = {}) {
  if (!event || typeof event.kind !== 'string') return null;
  // the author's own reading of this second wins outright: no table, no roll, no room colour.
  // A `mark` with no cue falls through the switch to null, which is the point of a mark.
  if (event.cue) return fromHand(event.cue, ctx);
  const rng = typeof ctx.rng === 'function' ? ctx.rng : Math.random;
  const intensity = Number.isFinite(Number(ctx.intensity)) ? clamp(Number(ctx.intensity), 0, 1) : 0.5;
  const label = typeof event.label === 'string' ? event.label : '';
  const cue = blank();

  switch (event.kind) {
    // the voice said a trigger phrase: its own effect bubble in a lane, the word on the chrome, and
    // she reaches for it. A phrase the spotter only half heard is a plain treat and stays off the chrome.
    case 'trigger': {
      const sure = conf01(event) >= TRIGGER_SURE;
      cue.spawn.push({ kindId: sure ? triggerKind(label, ctx) : 'treat', placement: 'lane', x: laneX(rng), h: LANE_H, at: 0 });
      if (sure) { cue.word = label || null; cue.pose = 'grab'; }
      break;
    }

    // a structure word (drop, deeper, breathe): a treat to drive through; a lifting word hangs in the air.
    // A lone number, a wake word before the way up, or a guess is nothing at all.
    case 'word': {
      if (conf01(event) < WORD_SURE) return null;
      if (NUMBER_WORD.test(label)) return null;
      if (WAKE_WORDS.has(label) && !(ctx.act && WAKE_ACTS.has(ctx.act.kind))) return null;
      const floats = FLOAT_WORDS.has(label);
      cue.spawn.push({ kindId: 'treat', placement: floats ? 'air' : 'lane', x: laneX(rng), h: floats ? FLOAT_H : LANE_H, at: 0 });
      break;
    }

    // a number inside a countdown: a golden ring in the air that sinks toward the road as the count
    // runs down (n is the spoken number, of the run length: ten hangs high, one skims the road; a
    // count going up climbs instead), the number on the chrome, and the last one braces her and
    // throws the kart at it
    case 'count': {
      const of = Math.max(1, Number(event.of) || 1), n = clamp(Number(event.n) || 1, 1, of);
      const sink = of > 1 ? 1 - (n - 1) / (of - 1) : 1;
      cue.spawn.push({ kindId: 'golden', placement: 'air', x: laneX(rng) * 0.5, h: AIR_H - (AIR_H - LANE_H - 0.6) * sink, at: 0 });
      if (label) cue.toast = { text: label, kind: event.last ? 'item' : 'pop' };
      if (event.last) { cue.jump = 6; cue.pose = 'clamp'; }
      break;
    }

    // the drop: a jump, a spiral over the world, and golden rings climbing away from the word. A
    // soft drop (a dip in the voice, not the fall) is a lower jump, two rings and no spiral.
    case 'drop': {
      const strength = Number.isFinite(Number(event.strength)) ? clamp(Number(event.strength), 0, 1) : 1;
      const hard = strength >= DROP_SOFT;
      cue.jump = hard ? 7 : 5;
      cue.mix = hard ? 'spiral' : null;
      cue.mood = 'streamed';
      cue.toast = { text: label || 'drop', kind: hard ? 'jackpot' : 'effect' };
      const rings = hard ? DROP_AT.length : 2;
      for (let i = 0; i < rings; i++) {
        cue.spawn.push({ kindId: 'golden', placement: 'air', x: DROP_X[i], h: AIR_H + i * AIR_RISE, at: DROP_AT[i] });
      }
      break;
    }

    // the same phrase over and over: a lane of treats in the chant's own rhythm, side to side, every
    // fourth one gold, the phrase on the chrome and a cheer. A light chant is a shorter lane.
    case 'chant': {
      const reps = clamp(Math.round((Number(event.reps) || 3) * Math.max(0.5, weight01(event))), 1, CHANT_MAX);
      const period = Number(event.period) > 0 ? Number(event.period) : CHANT_PERIOD;
      for (let k = 0; k < reps; k++) {
        const gold = (k + 1) % CHANT_GOLD_EVERY === 0;
        cue.spawn.push({ kindId: gold ? 'golden' : 'treat', placement: 'lane', x: (k % 2 ? CHANT_X : -CHANT_X), h: LANE_H, at: k * period });
      }
      cue.word = label || null;
      cue.pose = 'cheer';
      break;
    }

    // the RMS climbing: a push in the back and a thicker road while it lasts
    case 'build':
      cue.boost = clamp((Number(event.dur) || 0) * Math.max(0.5, weight01(event)), 0, BOOST_CAP);
      cue.density = 1.6;
      cue.mood = 'streamed';
      cue.pose = 'boost';
      break;

    // the top of the climb: the room's own bubbles out of the ceiling, more the louder the file is, and a cheer
    case 'peak': {
      const n = Math.round(PEAK_MIN + (PEAK_MAX - PEAK_MIN) * intensity);
      const kindId = ROOM_RAIN[roomId(ctx)] || 'treat';
      for (let i = 0; i < n; i++) {
        cue.spawn.push({ kindId, placement: 'rain', x: spread(rng), h: CEILING_H, at: i * PEAK_GAP });
      }
      cue.pose = 'cheer';
      cue.mood = intensity > 0.7 ? 'smug' : 'streamed';
      break;
    }

    // the fall away from a peak: she settles, the road thins out
    case 'release':
      cue.mood = 'calm';
      cue.density = 0.6;
      cue.pose = 'drift';
      break;

    // nothing in the file at all: a fogged straight with nothing in it, for as long as the quiet runs
    case 'silence':
      cue.fog = 1;
      cue.density = 0;
      cue.mood = 'calm';
      cue.holdSec = Math.max(0, Number(event.dur) || 0);
      cue.toast = { text: '. . .', kind: 'item' };
      break;

    default:
      return null;
  }
  return cue;
}

/**
 * The line under "you took N of M" on the end card. Pure so the smoke can read it; null when the
 * track had nothing to take (an energy-only chart with no words in it).
 */
export function resultTag(taken, countable) {
  const of = Math.max(0, Number(countable) || 0), got = clamp(Number(taken) || 0, 0, of);
  if (of <= 0) return null;
  const r = got / of;
  if (got === of) return 'every word';
  if (r >= 0.8) return 'good girl';
  if (r >= 0.5) return 'half of her';
  if (r >= 0.2) return 'she noticed';
  return 'you were not listening';
}

// self-check: node race/smoke/track-run-check.mjs walks every kind through this table.
