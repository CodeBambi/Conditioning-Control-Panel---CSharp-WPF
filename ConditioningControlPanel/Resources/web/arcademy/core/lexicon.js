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

  /* campus (the Direction A hub - shell/campus.js). Room names are diegetic
   * and FIXED to their game (a game always lives in its room); every value
   * stays under the 96-char mod-skin cap (MergeModTable drops longer rows). */
  student: 'Student',
  campus_room_daily_trigger: 'Homeroom',
  campus_room_deja_vu: 'Memory Lab',
  campus_room_impulse_control: 'Discipline Hall',
  campus_room_lost_and_found: 'Lost & Found',
  campus_desc_daily_trigger: 'One word, six chances. The whole school sits the same word today.',
  campus_desc_deja_vu: 'Pairs that move when you blink. The board settles only when you stop looking.',
  campus_desc_impulse_control: 'Hands on the desk. Move only when told - the room will lie to you.',
  campus_desc_lost_and_found: 'Things went missing in a wall of moving pictures. Find them before they move again.',
  campus_records: 'Records',
  campus_desc_records: 'Report card, attendance ledger, grades. Your whole term, in ink.',
  campus_registrar: 'Registrar',
  campus_desc_registrar: 'Every setting is a form. Every consent, a waiver with a stamp.',
  campus_entrance_hall: 'Entrance Hall',
  campus_desc_entrance: 'The notice board carries announcements. The trophy case waits for your diplomas.',
  campus_notice_board: 'Notice Board',
  campus_trophy_case: 'Trophy Case',
  campus_admissions: 'Admissions',
  campus_bell_tower: 'Bell Tower',
  campus_main_gate: 'Main Gate',
  campus_main_hall: 'Main Hall',
  campus_the_quad: 'The Quad',
  campus_front_path: 'Front Path',
  campus_east_wing: 'East Wing',
  campus_west_wing: 'West Wing',
  campus_desc_east: 'You can hear hammering behind the tape.',
  campus_desc_west: 'The boards are older here.',
  campus_sealed: 'Sealed',
  campus_opens_semester_2: 'Opens Semester II',
  campus_semester_3: 'Semester III',
  campus_in_session: 'In Session',
  campus_not_tonight: 'Not tonight',
  campus_next_bell: 'Next Bell',
  campus_step_inside: 'Step inside',
  campus_xp_first: 'First pass of the day pays XP.',
  campus_xp_retake: 'Retakes pay no XP - pride only.',
  campus_hint: 'Hover a room - click to step inside.',
  campus_night_sessions: 'Night Sessions',
  campus_rm: 'RM',
  /* Semester II ghost labels behind the tape (unregistered games get their
   * game_<key> row here, same convention the registry uses once they ship). */
  game_misdirection: 'Misdirection',
  game_instant_recall: 'Instant Recall',
  game_echo: 'Echo',
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
