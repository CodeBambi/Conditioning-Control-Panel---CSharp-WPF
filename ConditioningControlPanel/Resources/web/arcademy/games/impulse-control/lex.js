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
 * The whole table is exported so the C# NeutralLexicon can mirror it key-for-key
 * (see Resources/web/arcademy/CLAUDE.md §7 - per-game rows were deliberately left
 * out of the host table until each game agent shipped its own). It is also the
 * list quoted in the build report.
 *
 * STIMSET. `ic_word_go_1..6` / `ic_word_nogo_1..6` are the go/nogo word pairs the
 * dossier keys as `stimset.<modId>.<n>` - pure data, one-letter-off near-twins,
 * neutral by default so an absent mod skin is still a fully playable assessment.
 * Glyph pairs are shipped geometry (stimset.js), not strings, so they need no row.
 * ==========================================================================*/

export const IC_LEX = Object.freeze({
  /* --- the fiction ------------------------------------------------------- */
  ic_assessment: 'Reflex & Compliance Assessment',
  ic_subject: 'Subject',
  ic_baseline_block: 'Calibration',
  ic_assessment_block: 'Block',
  ic_composure_hold: 'Composure hold',
  ic_debrief: 'Debrief',
  ic_interference_log: 'Interference log',
  ic_nogo_share: 'NO-GO share',
  ic_calibrating: 'Calibrating - hold still, subject.',
  // {key} is substituted with the player's bound GO key.
  ic_go_hint: 'Press {key} or tap the aperture when the GO face shows. Its near-twin means withhold.',
  ic_warn_armed: 'INTERFERENCE ARMED',

  /* --- per-response feedback -------------------------------------------- */
  ic_withheld: 'Withheld',
  ic_just_made_it: 'JUST made it',
  ic_almost: 'Almost had you',
  ic_new_best: 'NEW BEST',
  ic_commended: 'COMMENDED',
  ic_block_clear: 'Block clear',
  ic_breather: 'Breathe. The next block runs hotter.',
  ic_hold_intro: 'Composure hold. Withhold, mostly.',

  /* --- error names (the interference log) -------------------------------- */
  ic_err_commission: 'Impulse error',
  ic_err_isi: 'Commission during rest',
  ic_err_miss: 'Missed cue',
  ic_err_late: 'Late response',

  /* --- lie names (attribution) ------------------------------------------ */
  ic_lie_false_cue: 'false go-sting',
  ic_lie_commitment_trap: 'mid-presentation swap',
  ic_lie_priming_flash: 'subliminal priming',
  ic_lie_peripheral_decoy: 'peripheral decoys',
  ic_lie_inverse_audio: 'false error buzzer',

  /* --- debrief lines ---------------------------------------------------- */
  ic_debrief_induced_line: 'You heard it, and you obeyed. Logged as induced, not yours.',
  ic_debrief_clean_line: "No interference was active. That one's yours.",
  // DECISIONS #7: the inverse audio lie is ALWAYS attributed, by name.
  ic_debrief_buzzer_lied: 'That buzzer lied.',
  ic_debrief_buzzer_body: 'A clean GO was answered with the error buzzer to shake your streak. '
    + 'The response was correct. The machine was not.',
  ic_debrief_no_errors: 'No errors. Nothing to attribute.',
  ic_debrief_no_lies: 'No interference was active this round. An honest test.',

  /* --- debrief cells / actions ------------------------------------------ */
  ic_session_median: 'session median',
  ic_personal_record: 'personal record',
  ic_baseline: 'baseline',
  ic_restraint: 'restraint',
  ic_induced: 'induced',
  ic_clean: 'clean',
  ic_baseline_new: 'Baseline established. Later classes are scored against it.',
  ic_slip_speed: 'Restraint held. Reflexes off your record - reassessment recommended.',
  ic_slip_restraint: 'Reflexes on record. Restraint slipped - reassessment recommended.',
  ic_slip_both: 'Both axes slipped. Reassessment recommended.',
  ic_slip_none: 'Speed and restraint both held. Filed.',
  ic_submit: 'Submit report',
  ic_recalibrate: 'Recalibrate baseline',
  ic_recalibrate_confirm: 'Tap again to confirm',
  ic_recalibrated: 'Baseline cleared - the next class recalibrates.',
  ic_legend: 'top row: interference events   bottom row: your errors',

  /* --- settings + keybind labels (rendered by shell/settings.js) --------- */
  ic_go_key: 'GO key',
  ic_stimulus_style: 'Stimulus style',
  ic_show_rt: 'Show reaction time',
  ic_inverse_audio: 'Allow the false error buzzer (Year 4)',

  /* --- stimset: word pairs (go / near-twin nogo) ------------------------- */
  ic_word_go_1: 'OBEY', ic_word_nogo_1: 'OBEV',
  ic_word_go_2: 'GOOD', ic_word_nogo_2: 'G00D',
  ic_word_go_3: 'DEEPER', ic_word_nogo_3: 'DEEPEB',
  ic_word_go_4: 'FOCUS', ic_word_nogo_4: 'FOCVS',
  ic_word_go_5: 'HOLD', ic_word_nogo_5: 'H0LD',
  ic_word_go_6: 'YIELD', ic_word_nogo_6: 'YEILD',
});

/** Every key this class can render - the list the host lexicon table mirrors. */
export const IC_LEX_KEYS = Object.freeze(Object.keys(IC_LEX));

export default IC_LEX;
