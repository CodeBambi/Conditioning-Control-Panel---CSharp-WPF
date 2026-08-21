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
 * ==========================================================================*/

export const IC_LEX = Object.freeze({
  /* --- the fiction ------------------------------------------------------- */
  ic_tube_title: 'The Drop Tube',
  ic_subject: 'Subject',
  ic_tube_rules: 'Pop every bubble the instant it surfaces. NEVER touch the X.',
  // {key} is substituted with the player's bound POP key.
  ic_go_hint: 'Click the bubble or press {key}. An X means hold still.',
  ic_loading: 'Priming the tube - hold still, subject.',
  ic_bubble_n: 'Bubble',
  ic_incoming: 'INCOMING',

  /* --- per-bubble feedback ---------------------------------------------- */
  ic_pop_perfect: 'PERFECT',
  ic_pop_fast: 'Quick',
  ic_pop_ok: 'Popped',
  ic_denied_pass: 'Withheld',
  ic_denied_hit: 'THAT WAS THE X',
  ic_missed: 'It drifted away',
  ic_new_best: 'NEW BEST',
  ic_streak: 'streak',

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

  /* --- settings + keybind labels (rendered by shell/settings.js) --------- */
  ic_go_key: 'POP key',
  ic_show_rt: 'Show reaction time',
  ic_bg_fade: 'Backdrop visibility',
});

/** Every key this class can render - the list the host lexicon table mirrors. */
export const IC_LEX_KEYS = Object.freeze(Object.keys(IC_LEX));

export default IC_LEX;
