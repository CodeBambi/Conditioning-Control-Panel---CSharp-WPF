/* ============================================================================
 * emi/moments.js - THE ONE TABLE: a shell moment -> what EMI does (agent B).
 *
 * `fireMoment('stamp', {streak: 3})` is the only verb the shell ever needs. The
 * mapping below is the state->moment map from EMI-DESIGN-LOCK.md, in one
 * designer-tunable object: re-point a moment at another chain and nothing else
 * in the page has to know.
 *
 * THREE RULES
 * - An UNKNOWN moment is a no-op, silently. A seam added in a later wave must
 *   never be able to throw inside a shell screen transition.
 * - EMI ABSENT is a no-op too (no layer mounted, the renderer failed, the
 *   player dismissed her). Every call site is one unguarded line for exactly
 *   that reason.
 * - WORDS ONLY IN THE BUBBLE (the locked talk rule). The one moment that talks
 *   is `reportCard`, and its lines are short enough for the pixel bubble.
 *
 * THE VOICE GETS FIRST REFUSAL. `emi/voice.js` is consulted before the table
 * below: a scripted beat or a bark speaks INSTEAD of the wordless reaction, and
 * anything else falls straight through to what this file always did. A voice
 * that is absent, still loading or throwing changes nothing at all.
 * ==========================================================================*/

import { getEmi, voiceMoment } from './index.js';

/**
 * REPORT-CARD LINES, by grade. The one place EMI uses words, so this table IS her
 * voice: lowercase, <= 24 characters, school-flavoured, never explicit, no dashes.
 * She is a school mascot who is FOND of you, with one habit she does not explain:
 * about one line in six is a little too aware of how often you come back. Keep
 * that ratio. Do not write the joke down anywhere; the lines are the whole gag.
 */
export const REPORT_LINES = Object.freeze({
  s: ['top of the class.', 'perfect. show off.', 'gold star. obviously.', 'you came back.',
    'frame this one.', 'the s is for sparkle.'],
  a: ['nice work today.', 'very good, student.', 'a grade. earned it.', 'good. again tomorrow.',
    'an a. i clapped.', 'almost an s. scary.'],
  b: ['solid. keep going.', 'not your best. fine.', 'you always come back.',
    'b for brave. yes it is.'],
  c: ['we all have days.', 'passed. barely.', 'try again tomorrow?',
    'the c builds character.', "i'll see you tomorrow."],
  pass: ['you showed up. good.', 'attendance counts.', 'showing up is a skill.'],
  none: ['same time tomorrow?', 'class dismissed.'],
});

/**
 * ORIENTATION DAY, her three lines (ORIENTATION.md §3.3, drafted through the
 * /emi-lines gauntlet - EMI-ORIENTATION-LINES.md). One row per step of the beat,
 * fired in order and once ever: the beat gates on the page-owned
 * `orientation.seenAt`, so this moment cannot refire.
 *
 * THE LINES HERE ARE THE FALLBACK; the FACES are this table's own. The
 * mod-skinnable rows (`emi_orientation_hi` / `_card` / `_go`) are resolved by
 * shell/orientation.js - which has `t` - and arrive as `payload.line`. This file
 * has never imported the lexicon (REPORT_LINES above are literals for the same
 * reason) and adding an import for three strings would be the first crack in
 * that. Keep the two copies verbatim: the suite fails if they drift.
 */
export const ORIENTATION_LINES = Object.freeze({
  hi: Object.freeze({ line: 'a new student! i did a little spin. you missed it.', face: '(◕‿◕)' }),
  card: Object.freeze({ line: "official! now you have to come back. it's the rules.", face: '(≧◡≦)' }),
  go: Object.freeze({ line: "go! your first class doesn't know how lucky it is.", face: '^_~' }),
});

/** Grades that read as "a good day" for the streak/perfect branches. */
const TOP_GRADES = { s: true, a: true };

/** Days in a row that upgrade a win/stamp from ^_^ to the GLEE chain. */
const STREAK_GLEE = 3;

function pick(list, seedish) {
  if (!Array.isArray(list) || !list.length) return null;
  const n = Math.abs(Math.round(Number(seedish) || 0));
  return list[n % list.length];
}

function gradeKey(g) {
  const k = String(g == null ? '' : g).toLowerCase();
  if (k === 's' || k === 'a' || k === 'b' || k === 'c') return k;
  if (k === 'pass' || k === 'zen') return 'pass';
  return 'none';
}

/* ============================================================================
 * THE TABLE. Each entry is one of:
 *   {chain:'wink'}                            a CHAINS id
 *   {face:'>_<', hold:1200, fx:'hearts'}      a raw face string, held
 *   {pick(payload) -> one of the above}       a branch on the payload
 *   {say(payload) -> {line, face}}            the bubble (talk rule)
 * ==========================================================================*/
export const MOMENTS = Object.freeze({
  /* --- arriving ------------------------------------------------------- */
  /** The board / a room came up. She notices you. */
  greet: { chain: 'wink' },
  /** A class started. GLANCE = "noticing the player" (locked) - and, since the
   *  EMI COLOR wave, the arrival face knows what KIND of room it walked into
   *  (payload.family, from the manifest): scanning in a search room, side-eye
   *  in a tracking room, half-lidded calm at the pool. Unknown family = the
   *  locked glance, exactly as before. */
  classStart: {
    pick(p) {
      /* EMI ASKS: a01's YES makes it a SOFT night, and a soft night is a
       * kinder arrival - one face, over the top of the room's own. It changes
       * nothing else: no line, no chain, no length. */
      if (p && p.soft === true) return { face: '^_^', hold: 1400 };
      const FAMILY_FACE = {
        search: '(◔_◔)', memory: '._.', reflex: 'o_o', comfort: '=_=',
        tracking: '¬_¬', recall: '0_0', puzzle: '(◠‿◠)',
      };
      const f = p && FAMILY_FACE[String(p.family || '')];
      return f ? { face: f, hold: 1400 } : { chain: 'glance' };
    },
  },

  /* --- mid-class (EMI COLOR: the tension mirror) ------------------------
   * FACE ONLY, by design: no bark pool exists on either name and none may be
   * added - a mascot that talks during a clutch moment is a distraction with
   * a fanbase. The games ration these through ctx.mood in shell.js. */
  /** The room got serious. She leans in and stays leaned. */
  tense: { face: 'o_o', hold: 1600 },
  /** The one big moment. Wide eyes, a little shiver, nothing said. */
  clutch: { face: '(⊙_⊙)', hold: 1800, body: 'shiver' },

  /* --- winning -------------------------------------------------------- */
  /** A punch card stamp landed, or a class was won.
   *  streak 3 -> GLEE ((≧◡≦)) · a perfect/S day -> COOL (both from the lock).
   *  The streak beat is the `glee` CHAIN, not a bare frame: it runs up through
   *  ^_^ first, which is what makes the squeeze read as a reaction to the stamp
   *  rather than as a face that was always there. */
  stamp: {
    pick(p) {
      const g = gradeKey(p && p.grade);
      if ((p && p.perfect) || g === 's') return { chain: 'cool' };
      if (p && Number(p.streak) >= STREAK_GLEE) return { chain: 'glee' };
      return { face: '^_^', hold: 1200, fx: 'hearts', body: 'bounce' };
    },
  },
  win: {
    pick(p) {
      const g = gradeKey(p && p.grade);
      if ((p && p.perfect) || g === 's') return { chain: 'cool' };
      if (TOP_GRADES[g] && p && Number(p.streak) >= STREAK_GLEE) return { chain: 'glee' };
      return { face: '^_^', hold: 1200, fx: 'hearts', body: 'bounce' };
    },
  },

  /* --- losing --------------------------------------------------------- */
  /** One wrong answer, one dropped tile. Small. */
  miss: { face: '>_<', hold: 900 },
  /** The class went badly. */
  fail: { chain: 'rage' },
  /** The run is over (a lost streak run, a blown class). */
  runLost: { chain: 'ko' },
  /** The attendance streak broke. The one genuinely sad beat. */
  streakBroken: { chain: 'cry' },

  /* --- waiting -------------------------------------------------------- */
  /** A timer or a load. Small dots, low on the glass (locked). */
  thinking: { chain: 'thinking' },
  /** The PLAYER went quiet, not EMI. */
  idlePlayer: { face: '-_-', hold: 2000 },

  /* --- away ----------------------------------------------------------- */
  /** A suspend, a mandatory video, the window losing focus. Escalates on the
   *  second one in a row, which is the lock's ¬_¬ -> (ಠ‿ಠ) ladder. */
  tabAway: {
    pick(p) {
      return (p && Number(p.count) >= 2) ? { face: '(ಠ‿ಠ)', hold: 1600 } : { chain: 'sus' };
    },
  },
  suspend: {
    pick(p) {
      return (p && Number(p.count) >= 2) ? { face: '(ಠ‿ಠ)', hold: 1600 } : { chain: 'sus' };
    },
  },
  /** ...and coming back. */
  resume: { chain: 'wake' },

  /* --- surprises ------------------------------------------------------ */
  rareDrop: { chain: 'shock' },
  firstUnlock: { chain: 'shock' },

  /* --- the report card ------------------------------------------------ */
  reportCard: {
    say(p) {
      const g = gradeKey(p && p.grade);
      const list = REPORT_LINES[g] || REPORT_LINES.none;
      const line = pick(list, (p && p.seed) != null ? p.seed : Date.now() / 60000);
      return { line, face: TOP_GRADES[g] ? '^_^' : '0_0' };
    },
  },

  /* --- orientation day ------------------------------------------------ */
  /** The school's once-ever hello (shell/orientation.js). `payload.step` picks
   *  the row: hi (a new student walked up) -> card (the ID lands) -> go (the
   *  send-off). An unknown step reads as the greeting rather than as silence,
   *  which is the right failure for a beat that only ever plays once. */
  orientation: {
    say(p) {
      const row = ORIENTATION_LINES[String((p && p.step) || 'hi')] || ORIENTATION_LINES.hi;
      const line = (p && typeof p.line === 'string' && p.line) ? p.line : row.line;
      return { line, face: row.face };
    },
  },

  /* --- the gag -------------------------------------------------------- */
  glitch: { chain: 'glitch' },
});

/* The escalation counter for tabAway/suspend. Session-only by design: coming
 * back tomorrow should not inherit yesterday's side-eye. */
const seen = Object.create(null);

/**
 * Fire one moment. Unknown names, an unmounted EMI and a dismissed EMI are all
 * silent no-ops - a call site is one line and never needs a guard.
 * @param {string} name
 * @param {Object=} payload  {grade, streak, perfect, count, seed, ...}
 * @returns {boolean} true when EMI actually reacted
 */
export function fireMoment(name, payload) {
  const emi = getEmi();
  if (!emi || typeof name !== 'string') return false;
  const spec = Object.prototype.hasOwnProperty.call(MOMENTS, name) ? MOMENTS[name] : null;

  const p = Object.assign({}, payload || {});
  if (name === 'tabAway' || name === 'suspend') {
    seen[name] = (seen[name] || 0) + 1;
    if (p.count == null) p.count = seen[name];
  } else if (name === 'resume') {
    seen.tabAway = 0; seen.suspend = 0;
  }

  /* OFF CHANNELS (W3): the deck has to know when the SCREEN changed hands - a
   * class, a suspend - because "no channel during a class" is a leg of the idle
   * gate and nothing else in EMI tracks it. One line, unknown names ignored. */
  try { if (typeof emi.noteMoment === 'function') emi.noteMoment(name); } catch (e) { /* noop */ }

  /* THE VOICE FIRST. It is asked about EVERY name, including the ones this
   * table has no row for (`exitIntent`, `lockedClick`, the card ceremonies) -
   * which is why the unknown-moment check now sits below this line instead of
   * above it. A true means she spoke and the wordless reaction is skipped. */
  if (voiceMoment(name, p)) return true;
  if (!spec) return false;

  let entry = spec;
  try {
    if (typeof spec.pick === 'function') entry = spec.pick(p) || null;
    if (!entry) return false;
    if (typeof entry.say === 'function' || typeof spec.say === 'function') {
      const mk = typeof entry.say === 'function' ? entry.say : spec.say;
      const out = mk(p) || {};
      if (!out.line) return false;
      return !!emi.say(out.line, { face: out.face, nod: !!out.nod });
    }
    if (entry.chain) return !!emi.emote(entry.chain);
    if (entry.face) {
      return !!emi.emote(entry.face, { hold: entry.hold, fx: entry.fx, body: entry.body });
    }
  } catch (e) { /* a mascot may never break a screen transition */ }
  return false;
}

/** Test seam: forget the ¬_¬ -> (ಠ‿ಠ) escalation. */
export function resetMoments() { for (const k of Object.keys(seen)) delete seen[k]; }

export default fireMoment;
