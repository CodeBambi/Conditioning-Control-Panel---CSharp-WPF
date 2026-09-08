/* ============================================================================
 * race/cues.js - a chart event turned into the thing the run does about it.
 * Implements CHART.md section `race/cues.js`: the mapping (PR c2) and its feel (PR c3).
 *
 * One pure function over a table: no three, no DOM, no clock, no state, so it runs
 * under node beside chart.js. `cueFor` decides WHICH of the things the run already
 * knows how to spend an event is worth (a bubble, a jump, a mood, a pose, a boost)
 * and never HOW: run.js owns every verb, this file only names them.
 *
 * A spawn marked `row: true` is one bubble of a ROW: the whole set is placed together
 * or not at all (bubbles.js spawnRow), because half a row is a row the kart can drive
 * around, and the point of a row is that it cannot be driven around.
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

import { LANE_H, CEILING_H, LANE_X_MAX, POP_HIT_X } from './consts.js';
import { themeFor } from './triggerTheme.js';

/**
 * The words whose bubble is drawn BIG (race/wordFace.js): the ones the file is actually about.
 * Every other word rides an ordinary bubble, so these read as the beat of the line rather than as
 * sixteen shouts. A phrase that landed in one bubble counts if either half is on the list.
 */
export const ACCENT_WORDS = new Set(['drop', 'sink', 'deeper', 'down', 'relax', 'now', 'empty',
  'blank', 'bump', 'obey', 'pink', 'good', 'girl', 'bambi', 'sleep', 'bimbo']);
/** What is stripped off a transcript word before it is held against that list. */
const ACCENT_TRIM = /^["'(\[]+|[,.;:!?…"')\]]+$/g;

/** Is this bubble's word one of them, and what colour does it want? */
function accentOf(text) {
  return String(text || '').toLowerCase().split(/\s+/).some((part) => ACCENT_WORDS.has(part.replace(ACCENT_TRIM, '')));
}
/** The theme colour of the set this word sits inside, when the road put one on the event. */
function tagInk(event) {
  if (!event || typeof event.cue !== 'string' || !event.cue) return null;
  const row = themeFor(event);
  return row && row.preset === event.cue ? row.color : null;
}

/** A trigger phrase nobody has assigned a bubble to wears the room's own effect, else this.
 *  `pink` since 2026-09-08: it was the flash bubble, and the flash bubble is dark now. Every id
 *  named here has to be one that still SPAWNS, because bubbles.js lays no row at all for a dark
 *  kind and a trigger row that never lands is the one thing this road may not do. */
const FALLBACK_TRIGGER = 'pink';
/** Which effect an unmapped trigger wears per room (rooms.js ids; chart.js ACT_ROOM picks them).
 *  The Tea Garden's was the flash; it takes the whisper card instead, which is the gentler read
 *  of "nothing here fights you" anyway and keeps the room off the pink the Toybox already wears. */
const ROOM_TRIGGER = {
  teagarden: 'subliminal', undertow: 'spiral', toybox: 'pink', chapel: 'spiral',
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
/** The lanes a spoken word may land in. Narrower than the road: the kart has to steer, not lunge.
 *  Exported because race/wordBubbles.js walks a line of word bubbles across these same five lanes
 *  and a second copy of them would be a second road. */
export const LANE_X = [-1.6, -0.8, 0, 0.8, 1.6];
/** THE ROW. A trigger the spotter is sure of is not a bubble in a lane, it is a line of them
 *  across the whole road, because the owner's read of the game is that a trigger word is a thing
 *  that HAPPENS to you: you do not get to steer around the word she just said.
 *  The widest gap the line may leave is ten percent inside the pop box's own width, so the box
 *  cannot be threaded between two of them wherever the kart sits. */
export const ROW_MAX_GAP = 2 * POP_HIT_X * 0.9;
/** How many that takes edge to edge, always odd so one bubble sits dead centre. */
const ROW_N = (() => { const n = Math.ceil((LANE_X_MAX * 2) / ROW_MAX_GAP) + 1; return n % 2 ? n : n + 1; })();
/** The row's own x's: -LANE_X_MAX to +LANE_X_MAX, evenly spaced. */
export const ROW_X = Array.from({ length: ROW_N }, (_, i) => -LANE_X_MAX + (i * LANE_X_MAX * 2) / (ROW_N - 1));
/** Height of an air bubble over the road, how much each of a drop's rings climbs, a floating word's hang. */
const AIR_H = 2.6, AIR_RISE = 0.6, FLOAT_H = 1.9;
/** A drop is golden rings through the air, one on the word and the rest behind it. */
const DROP_AT = [0.2, 0.5, 0.8], DROP_X = [-1.1, 0, 1.1];
/** A drop this weak is a dip, not a fall: no spiral over the world, fewer rings, a lower jump. */
const DROP_SOFT = 0.6;
/** A peak rains between these many, by intensity, this far apart. */
const PEAK_MIN = 4, PEAK_MAX = 8, PEAK_GAP = 0.25;
/** On a track whose road came out of a transcript the rain is halved: the rows are the loud thing
 *  now, and a peak that buries them takes the reading away. Too many bubbles is no bubbles. */
const PEAK_LYRIC_MULT = 0.5;
/** The chant's two lanes, and the fallback beat when the analyzer sent no period. */
const CHANT_X = 1.2, CHANT_PERIOD = 1.2, CHANT_MAX = 16;
/** Every this-many chant treats, one is gold. */
const CHANT_GOLD_EVERY = 4;
/** A build hands over at most this many seconds of boost. */
const BOOST_CAP = 4;
/** THE WORD FLASH (2026-09-08). The flash bubble is dark: the flash is a beat on a WORD now.
 *  Half the word bubbles the player takes pop a brief bloom of light with them, and the other half
 *  are just a word. It is cosmetic and nothing else: it never reaches THE MIX, it is not a strobe
 *  charge, it cannot start a recipe and the pop still scores as the treat it always was.
 *  A `flash-pulse` row is the exception, and it goes the other way: that preset MEANT the flash,
 *  so its beat pours a real one through the mixer (`cue.mix`) instead of this bloom. */
export const WORD_FLASH_CHANCE = 0.5;
/** The preset whose row pours a real flash on its beat rather than the bloom (triggerTheme.js). */
export const FLASH_PRESET = 'flash-pulse';
/** Two words taken inside this many ms share one flash: a line read clean is a sentence, not a strobe. */
export const WORD_FLASH_GAP_MS = 250;
/** What run.js hands payloadFx: one scattered image at ~185 ms, which is a blink and not an effect. */
export const WORD_FLASH = { strength: 12, durationMult: 0.18 };
/**
 * Does this word pop take a flash with it? Pure so the smokes can hold the odds: run.js keeps the
 * clock and the run's own seeded rng, and a pop inside the gap never draws from it, so the cap is a
 * cap and not a re-roll.
 * @param rng the run's seeded rng, @param sinceMs ms since the last flash this run fired
 */
export function wordFlash(rng, sinceMs) {
  if (!(sinceMs >= WORD_FLASH_GAP_MS)) return false;
  return (typeof rng === 'function' ? rng() : Math.random()) < WORD_FLASH_CHANCE;
}

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const conf01 = (e) => (Number.isFinite(Number(e.conf)) ? clamp(Number(e.conf), 0, 1) : 1);
const weight01 = (e) => (Number.isFinite(Number(e.weight)) ? clamp(Number(e.weight), 0, 1) : 1);
const laneX = (rng) => LANE_X[Math.min(LANE_X.length - 1, (rng() * LANE_X.length) | 0)];
const spread = (rng) => clamp((rng() * 2 - 1) * LANE_X_MAX, -LANE_X_MAX, LANE_X_MAX);
const roomId = (ctx) => (ctx.room && typeof ctx.room.id === 'string' ? ctx.room.id : (ctx.act && ctx.act.room) || null);

/** Every field CHART.md promises, so run.js never has to test for one. */
function blank() {
  return { spawn: [], jump: 0, mix: null, mood: null, pose: null, toast: null,
    word: null, fog: null, boost: 0, density: null, holdSec: 0 };
}

/** The bubble a spoken trigger wears: the chart's own map, else the room's effect, else the strobe. */
function triggerKind(label, ctx) {
  const kinds = ctx.triggerKinds;
  const id = (kinds && typeof kinds.get === 'function') ? kinds.get(label) : null;
  return id || ROOM_TRIGGER[roomId(ctx)] || FALLBACK_TRIGGER;
}

/**
 * @param event a chart event (race/chart.js normalizeChart shape)
 * @param ctx { energy, act, room, intensity, rng, triggerKinds, lyrics, gold }
 *            gold() answers true while a trigger row is owed a golden centre (rabbit foot, track.js takeGold)
 * @returns the cue, or null for an event this build has nothing to say about (an unknown
 *          kind, or a word the feel pass decided the spotter only guessed at).
 */
export function cueFor(event, ctx = {}) {
  if (!event || typeof event.kind !== 'string') return null;
  const rng = typeof ctx.rng === 'function' ? ctx.rng : Math.random;
  const intensity = Number.isFinite(Number(ctx.intensity)) ? clamp(Number(ctx.intensity), 0, 1) : 0.5;
  const label = typeof event.label === 'string' ? event.label : '';
  const cue = blank();

  switch (event.kind) {
    // the voice said a trigger phrase: its own effect bubble in a lane, the word on the chrome, and
    // she reaches for it. A phrase the spotter only half heard is a plain treat and stays off the chrome.
    case 'trigger': {
      if (conf01(event) < TRIGGER_SURE) {   // a phrase half heard: one plain treat, off the chrome, easy to miss
        cue.spawn.push({ kindId: 'treat', placement: 'lane', x: laneX(rng), h: LANE_H, at: 0 });
        break;
      }
      const kindId = triggerKind(label, ctx);
      const gold = typeof ctx.gold === 'function' && ctx.gold() === true;   // rabbit foot: a golden in the middle
      // every bubble of the row wears the set's word on its own face (bubbles.js spawnRow), inked
      // in the set's own theme colour, and the middle one is drawn bigger: the row IS the word.
      const row = themeFor(event) || {};
      const ink = row.color || null;
      // the one preset that MEANT the flash: its bubble went dark, so the beat pours the flash
      // itself through THE MIX (run.js cueMix) and the row stays a line of plain white word faces.
      // This is the only door a strobe charge still comes through, so the recipes that need one
      // (pink lightning, snowblind, the full pour) are still on the table.
      if (row.preset === FLASH_PRESET) cue.mix = 'flash';
      for (const x of ROW_X) cue.spawn.push({ kindId: gold && Math.abs(x) < 1e-6 ? 'golden' : kindId, placement: 'lane', x, h: LANE_H, at: 0, row: true, w: label || '', ink, big: true });
      cue.word = label || null;
      cue.pose = 'grab';
      break;
    }

    // a structure word (drop, deeper, breathe): a treat to drive through; a lifting word hangs in the air.
    // A lone number, a wake word before the way up, or a guess is nothing at all.
    case 'word': {
      // A WORD BUBBLE off the transcript (race/wordBubbles.js): one word, one bubble, sitting in
      // the lane its PHRASE was given, so the line of them spells the sentence down one lane and
      // the player steers in the silence between two lines. `row: true` is not a row of five here,
      // it is what buys the bubble its tracking: run.js hands a row to race/sync.js, which re-places
      // it every frame off the speed the kart has NOW, so the kart meets the word when the voice
      // says it whatever the throttle did. A row of one is still a row of one.
      // It carries nothing on the chrome: six hundred words on the toast rail is exactly the noise
      // the owner asked us to take off this road, and the word is read off the bubble's own face.
      if (typeof event.w === 'string' && event.w) {
        const x = Number.isFinite(Number(event.x)) ? clamp(Number(event.x), -LANE_X_MAX, LANE_X_MAX) : 0;
        // the face: every word is painted on its bubble, an accent word on a bigger bubble and in
        // the colour of the set this second belongs to (race/cloudChart.js stamps `cue`), else pink.
        const accent = accentOf(event.w);
        cue.spawn.push({ kindId: 'treat', placement: 'lane', x, h: LANE_H, at: 0, row: true, w: event.w,
          big: accent, ink: accent ? tagInk(event) : null });
        break;
      }
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
      const lyric = ctx.lyrics ? PEAK_LYRIC_MULT : 1;
      const n = Math.max(1, Math.round((PEAK_MIN + (PEAK_MAX - PEAK_MIN) * intensity) * lyric));
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

// self-check: node race/smoke/track-run-check.mjs walks every kind through this table, and
// node race/smoke/rows-check.mjs holds the row's geometry against the pop box.
