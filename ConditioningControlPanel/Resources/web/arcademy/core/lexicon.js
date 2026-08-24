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
  family_recall: 'recall', family_puzzle: 'puzzle',

  /* verbs / chrome */
  peek: 'Peek',
  peek_hint: 'Hold to peek. Using it caps this class at A.',
  settings: 'Settings',
  /* The scoped (mid-class) settings page's one line of honesty: class knobs
     are snapshotted at startClass, so a change lands on the NEXT run. */
  applies_next_class: 'Class option changes take effect next class.',
  back: 'Back',
  begin_class: 'Begin',
  /* The shell's own name for endless play. A game that declares
     `manifest.endless` may name its own label_key instead (The Deep End ships
     de_free_swim); this row is what the campus falls back to, and what the
     class chrome's chip always says. */
  free_swim: 'Free Swim',
  free_swim_hint: 'Untimed practice. No grade, no XP, no attendance.',
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
  campus_room_the_deep_end: 'The Pool',
  campus_desc_daily_trigger: 'One word, six chances. The whole school sits the same word today.',
  campus_desc_deja_vu: 'Pairs that move when you blink. The board settles only when you stop looking.',
  campus_desc_impulse_control: 'Hands on the desk. Move only when told - the room will lie to you.',
  campus_desc_lost_and_found: 'Things went missing in a wall of moving pictures. Find them before they move again.',
  campus_desc_the_deep_end: 'Sink tile into tile. The deeper you go, the harder the board is to read.',
  /* Semesters II / III (2026-08-23) */
  campus_room_misdirection: 'The Parlour',
  /* SORT wears plate 201 - the lot-2 rework gave Misdirection's old parlour
     to the front office, so sort built new on the Entrance Hall's west span
     (shell/campus.js), and the 2026-08-24 renumber handed it Misdirection's
     old room number as its substitute. Misdirection's two rows stay: the host
     table is append-only and the class is retired, not deleted. */
  campus_room_sort: 'The Sorting Room',
  campus_room_echo: 'Music Room',
  campus_room_instant_recall: 'Lecture Hall',
  campus_room_anomaly: 'Darkroom',
  campus_room_composure: 'The Studio',
  campus_desc_misdirection: 'Keep your eyes on the one that matters. It will not make that easy.',
  campus_desc_sort: 'Two piles, and you decide what goes in them. Yours to the right.',
  campus_desc_echo: 'It plays a line, you play it back. Then it adds one more, every time.',
  campus_desc_instant_recall: 'Watch the whole hour, then answer for it. You never hear it coming.',
  campus_desc_anomaly: 'Everything in here matches. One thing does not. Find it before it moves.',
  campus_desc_composure: 'Slide the picture back together while the room does its best to blur it.',
  campus_records: 'Records',
  /* punch cards (PUNCHCARD.md §2.3 / §4 / §6). The per-class enrollment flavour
   * lives in shell/enrollment.js's ENROLL_LEX (the IC_LEX precedent: a table
   * exported as data); these are the rows the SHELL renders itself. */
  campus_unlocked: 'Unlocked - open every night',
  campus_unlocked_sign: 'Open',
  campus_unlocked_hint: 'Card complete. This room opens every night, board or no board.',
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
  /* LOT 2 (2026-08-23) made the east wing the FRONT OFFICE - it holds Records
     and the Registrar now, not three new classrooms. Same key, new sentence;
     campus.js carries the identical fallback. */
  campus_desc_east_open: 'The front office. Two counters, one bell, and a queue that is always you.',
  campus_desc_west_open: 'Older boards, deeper rooms. Nobody in here is in any hurry.',
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
  /* --- the punch card + its ceremony (PUNCHCARD §4) ---------------------- */
  punchcard: 'Stamp Card',
  punchcard_holes: '{have} of {need}',
  /* THE LIVE TEXT ZONE on the card face (shell/punchcard.js). The count is the
   * card's own tight form ('3/10'); punchcard_holes stays the prose one the
   * Records docket prints. The eight rotating flavour lines live beside the
   * enrollment copy, in punchcard.js's PHRASE_LEX - one row each so a mod can
   * re-voice them one at a time. */
  punchcard_count: '{have}/{need}',
  punchcard_mastered: 'Mastered',
  punchcard_stamped: 'Stamped for today.',
  /* THE S DOUBLE (owner ruling 2026-08-23): a day the class graded S is worth a
     second hole, and the ceremony says so on the beat that punches it. */
  punchcard_stamped_s: 'Top marks. The card takes a second stamp.',
  punchcard_next_hole: 'Come back tomorrow for the next stamp.',
  punchcard_unlocked_chip: 'Unlocked',
  punchcard_unlocked_title: 'Assignment complete',
  punchcard_unlocked_line: 'This room is now open even when the course is not in session.',
  enroll_kicker: 'Enrollment',
  enroll_next: 'Next',
  enroll_begin: 'Begin class',
  enroll_card_line: 'Every class carries a stamp card. Ten stamps, one a night.',
  enroll_tutorial_line: 'One stamp for finishing your first class.',
  enroll_house_line: 'And one on the house. Welcome to the class.',
  /* DAY ONE IS THREE (owner ruling 2026-08-23), and the third hole says why. */
  enroll_signon_line: 'And one for signing on. The card starts warm.',
  /* --- the Records Office (PUNCHCARD §6) --------------------------------- */
  records_kicker: 'Records Office',
  records_lede: 'Ten cards, ten stamps each. The wall keeps them whether you come back or not.',
  records_enrolled: 'Enrolled',
  records_enrolled_on: 'Enrolled',
  records_unlocked_on: 'Unlocked',
  records_holes_punched: 'Stamps earned',
  records_holes_left: 'Stamps left',
  records_stamps: 'Daily stamps',
  records_no_stamps: 'No daily stamps yet.',
  records_not_enrolled: 'Not enrolled - attend the class',
  records_enroll_hint: 'The first graded finish opens the card and earns three stamps.',
  records_house_note: 'Day one is three stamps: finishing, on the house, signing on.',
  records_flip_hint: 'Pick a card to read its stamps.',
  records_empty_wall: 'Nothing on the wall yet. Attend a class and the first card gets pinned.',

  /* Semester II ghost labels behind the tape (unregistered games get their
   * game_<key> row here, same convention the registry uses once they ship). */
  game_misdirection: 'Misdirection',
  game_sort: 'Sort',
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
