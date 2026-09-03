/* ============================================================================
 * games/impulse-control/lex.js - this class's lexicon rows, in ONE place.
 *
 * GROUND-RULES §3 / core/lexicon.js: internal keys are neutral and FIXED, mods
 * ship display strings. Every string this class renders goes through
 * ctx.lexicon(key, IC_LEX[key]) so:
 *   - a mod that skins the row wins,
 *   - a mod that does not gets the English below,
 *   - a dead lexicon still renders English (never a raw key).
 *
 * THE DROP TUBE rewrite: the go/no-go assessment's stimset rows died with it.
 * The whole table is exported so the C# NeutralLexicon can mirror it key-for-key
 * (the orchestrator applies the delta reported by the build - this file never
 * touches C#).
 *
 * HOUSE RULES WAVE. Two changes, both Deck VI (images over text):
 *   - `ic_tube_rules` is GONE. The rules are DRAWN now: render.showHowto() draws
 *     three vignettes and captions them with the ic_howto_* rows below, so the
 *     150-character paragraph that no mod could ever re-voice (web CLAUDE.md
 *     trap 26: NeutralLexicon drops a mod string over 96 chars) has no render
 *     site left. Its C# row stays put; nothing reads it.
 *   - the casino deck (casino.js) speaks through `t` like everything else, so
 *     its words live HERE and not in the deck: ic_almost / ic_just / ic_royal /
 *     ic_jackpot / ic_perfect_class / ic_tonight / ic_streak_n / ic_record_ping.
 *     The trickster deck renders no text at all and adds no rows.
 *
 * EVERY VALUE IS <= 96 CHARACTERS, on purpose (trap 26). Keep it that way: a
 * longer row is a row a mod can never skin. Split it into two rather than
 * raising the cap.
 * ==========================================================================*/

export const IC_LEX = Object.freeze({
  /* --- the fiction ------------------------------------------------------- */
  ic_tube_title: 'The Drop Tube',
  ic_subject: 'Subject',
  // {key} is substituted with the player's bound POP key.
  ic_go_hint: 'Click the bubble or press {key}. An X means hold still.',
  ic_loading: 'Priming the tube - hold still, subject.',
  ic_bubble_n: 'Bubble',
  ic_incoming: 'INCOMING',

  /* --- the class rules sheet (drawn vignettes; these are the captions) ---- */
  ic_howto_title: 'Class rules',
  ic_howto_pop: 'A bubble lands in the dish. Pop it at once. The faster you are, the more it pays.',
  ic_howto_x: 'A bubble wearing an X is a trap. Touch nothing until its ring runs out.',
  ic_howto_drift: 'A bubble you miss just drifts off the dish. Nothing is taken from you.',
  ic_howto_go: 'Start the drop',

  /* --- per-bubble feedback ---------------------------------------------- */
  ic_pop_perfect: 'PERFECT',
  ic_pop_fast: 'Quick',
  ic_pop_ok: 'Popped',
  ic_denied_pass: 'Withheld',
  ic_denied_hit: 'THAT WAS THE X',
  ic_missed: 'It drifted away',
  ic_new_best: 'NEW BEST',
  ic_streak: 'streak',

  /* --- the casino deck's vocabulary (casino.js renders these through t) ---- */
  // {n} is substituted with the current pop chain length.
  ic_streak_n: 'chain {n}',
  ic_almost: 'ALMOST',
  ic_just: 'JUST',
  ic_record_ping: 'record',
  ic_jackpot: 'JACKPOT',
  ic_royal: 'ROYAL',
  ic_perfect_class: 'Perfect class',
  ic_tonight: 'tonight only',

  /* --- HUD --------------------------------------------------------------- */
  ic_score: 'Score',
  ic_hold: 'HOLD',
  ic_pop: 'POP',

  /* --- debrief ----------------------------------------------------------- */
  ic_debrief: 'Debrief',
  ic_popped: 'popped',
  ic_median_rt: 'median pop',
  ic_best_rt: 'best pop',
  ic_personal_record: 'personal record',
  ic_baseline: 'baseline',
  ic_restraint: 'restraint',
  ic_x_held: 'X held',
  ic_x_popped: 'X popped',
  ic_drifted: 'drifted',
  ic_baseline_new: 'Baseline established. Later classes are scored against it.',
  ic_gate_hint: 'S needs an untouched X row AND real speed.',
  ic_slip_speed: 'Restraint held. Pops off your record - reassessment recommended.',
  ic_slip_restraint: 'Pops on record. The X got you - reassessment recommended.',
  ic_slip_both: 'Both axes slipped. Reassessment recommended.',
  ic_slip_none: 'Speed and restraint both held. Filed.',
  ic_submit: 'Submit report',
  ic_recalibrate: 'Recalibrate baseline',
  ic_recalibrate_confirm: 'Tap again to confirm',
  ic_recalibrated: 'Baseline cleared - the next class recalibrates.',

  /* --- the cameo deck's folder (cameo.js) --------------------------------
     Seven rows and not one of them is a caption: the folder is a PROP, and
     these are the words printed on it. The tab reads ic_file_tab until the
     shell hands the game a subject code (post-reveal), and from then on the
     tab wears the code and ic_file_stamp_after sits under a redaction bar.
     The note is the 5% roll: a stapled slip instead of a photo, one line
     picked from ic_file_note_1..3. Warm, short, and never a threat. */
  ic_file_tab: 'field notes',
  ic_file_stamp: 'for you',
  ic_file_stamp_after: 'subject',
  ic_file_note_head: 'note to file',
  ic_file_note_1: "i'm not supposed to be in the tube so if anyone asks this note fell out of a bubble on its own",
  ic_file_note_2: "hasta la vista, baby. i wrote it down so i'd get it right this time. did i get it right",
  ic_file_note_3: "saved you the slow bubble again, the one that takes its time, so don't tell the others",

  /* --- settings + keybind labels (rendered by shell/settings.js) --------- */
  ic_go_key: 'POP key',
  ic_show_rt: 'Show reaction time',
  ic_bg_fade: 'Backdrop visibility',
});

/** Every key this class can render - the list the host lexicon table mirrors. */
export const IC_LEX_KEYS = Object.freeze(Object.keys(IC_LEX));

export default IC_LEX;
