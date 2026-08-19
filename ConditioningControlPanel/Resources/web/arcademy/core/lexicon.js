/* ============================================================================
 * core/lexicon.js - the mod display-string table.
 *
 * GROUND-RULES §3: internal system keys are neutral and FIXED; each mod ships a
 * display-string table ("lexicon" is the canonical term - SYNTHESIS #9). The
 * host resolves the active mod's table and hands it over in init.lexicon.
 *
 *   setLexicon(init.lexicon)     once, from boot.js
 *   t('class', 'Class')          everywhere else
 *
 * t() NEVER returns a raw key: an unknown key falls back to the caller's
 * fallback, then to DEFAULT_LEXICON, then to a de-snaked version of the key
 * itself ('perfect_attendance' -> 'Perfect Attendance'). That is the intake/
 * localization lesson - a dead string table must degrade to readable English,
 * not to `btn_start_flashes` on screen.
 *
 * Mods override display strings ONLY. Nothing mechanical may read a lexicon
 * value, and no game may invent a tier name (SYNTHESIS #1: grade_tier display
 * comes from ONE row family, grade_tier_1..4).
 * ==========================================================================*/

/**
 * English defaults for every internal key the SHELL renders. Games add their own
 * keys through their own lexicon entries; anything missing degrades (see t()).
 * Reserved vocabulary (exam / gpa / honor_roll / detention) is present because
 * the strings are designed - the systems are not built in v1.
 */
export const DEFAULT_LEXICON = Object.freeze({
  /* container */
  arcademy: 'The Arcademy',
  semester: 'Semester',

  /* the day */
  timetable: 'Timetable',
  class: 'Class',
  classes: 'Classes',
  homeroom: 'Homeroom',
  period: 'Period',
  report_card: 'Report Card',
  class_suspended: 'Class Suspended',
  class_placeholder: 'Class Placeholder',

  /* performance */
  grade: 'Grade',
  grade_s: 'S', grade_a: 'A', grade_b: 'B', grade_c: 'C', grade_pass: 'PASS',
  grade_tier: 'Year',
  grade_tier_1: 'Year 1', grade_tier_2: 'Year 2', grade_tier_3: 'Year 3', grade_tier_4: 'Year 4',
  attendance: 'Attendance',
  perfect_attendance: 'Perfect Attendance',
  detention: 'Detention',
  diploma: 'Diploma',
  exam: 'Exam',
  gpa: 'GPA',
  honor_roll: 'Honor Roll',

  /* families (timetable chips) */
  family_word: 'word', family_memory: 'memory', family_search: 'search',
  family_tracking: 'tracking', family_reflex: 'reflex', family_comfort: 'comfort',

  /* verbs / chrome */
  peek: 'Peek',
  peek_hint: 'Hold to peek. Using it caps this class at A.',
  settings: 'Settings',
  back: 'Back',
  begin_class: 'Begin',
  leave_class: 'Leave class',
  replay_board: 'Flip the board again',
  share: 'Copy share card',
  shared: 'Copied to clipboard',
  done: 'Done',
  xp: 'XP',
  streak: 'Streak',

  /* share marks (Daily Trigger emoji grid - each mod ships widely-supported emoji) */
  share_hit: '💗',   // pink heart
  share_near: '🌀',  // cyclone
  share_miss: '🖤',  // black heart
});

let table = Object.create(null);

/** Install the host-resolved table. Non-objects are ignored (defaults stand). */
export function setLexicon(next) {
  const out = Object.create(null);
  if (next && typeof next === 'object') {
    for (const k of Object.keys(next)) {
      const v = next[k];
      if (typeof v === 'string' || typeof v === 'number') out[k] = String(v);
    }
  }
  table = out;
  return table;
}

/** De-snake an unknown key so the worst case is still readable English. */
function humanize(key) {
  return String(key || '')
    .replace(/[_.-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/\b[a-z]/g, (c) => c.toUpperCase());
}

/**
 * Resolve a display string.
 * @param {string} key      internal key (neutral, fixed)
 * @param {string} [fallback] caller's English if the mod has no row
 */
export function t(key, fallback) {
  const v = table[key];
  if (typeof v === 'string' && v.length) return v;
  if (typeof fallback === 'string' && fallback.length) return fallback;
  const d = DEFAULT_LEXICON[key];
  if (typeof d === 'string' && d.length) return d;
  return humanize(key);
}

/** True if the active mod actually skinned this key (for "is this authored?" checks). */
export function hasLexicon(key) { return typeof table[key] === 'string' && !!table[key].length; }

/** Grade-tier display via the ONE row family (SYNTHESIS #1). */
export function tierLabel(tier) {
  const n = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  return t('grade_tier_' + n, 'Year ' + n);
}

/** Grade letter display ('S'|'A'|'B'|'C'|'pass'). */
export function gradeLabel(grade) {
  const g = String(grade || '').toLowerCase();
  return t('grade_' + g, g === 'pass' ? 'PASS' : String(grade || '').toUpperCase());
}

/** Family chip display. */
export function familyLabel(family) {
  return t('family_' + String(family || ''), String(family || ''));
}

export default t;
