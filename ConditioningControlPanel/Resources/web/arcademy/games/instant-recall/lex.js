/* ============================================================================
 * games/instant-recall/lex.js - every `ir_` lexicon row INSTANT RECALL renders,
 * with its neutral English fallback. Exported as DATA (the Impulse Control /
 * Deep End precedent, `IC_LEX` / `DE_LEX`) so the host's `NeutralLexicon` can
 * be mirrored key-for-key: copy the values, never re-word them.
 *
 * RULES (web CLAUDE.md trap 26 + the build contract): a value longer than 96
 * characters can never be mod-skinned (`MergeModTable` drops it), so every row
 * here is <= 96; a row is rendered ONLY through ctx.lexicon(key, fallback)
 * (Law VII - accents come from the lexicon, never AI). The DECKS (casino /
 * trickster / pressure) may `t()` the same keys; they never mint their own.
 *
 * The question TEXT is a lexicon row; the ANSWER text is data (a word from the
 * day pool, an effect name, a sting name, a layout name) - the effect / sting /
 * layout NAMES are rows too, because they are the only words the option chrome
 * ever shows and a mod must be able to re-voice the whole quiz card.
 * ==========================================================================*/

export const IR_LEX = Object.freeze({
  /* ---- setting row (manifest.settings) -------------------------------- */
  ir_density: 'Montage density',
  ir_density_hint: 'How thick the stream gets between stops. Calm eases the ceiling, dense rides it.',

  /* ---- HUD chips (aria labels; the chip TEXT is live data) ------------- */
  ir_chip_clock: 'Time left',
  ir_chip_stops: 'Stops',
  ir_chip_density: 'Density',

  /* ---- the class-rules sheet (Law IV: drawn, GO-only dismissal) -------- */
  ir_howto_title: 'The vigil',
  ir_howto_1: 'A montage plays. Triggers fire over it.',
  ir_howto_2: 'Without warning, everything freezes.',
  ir_howto_3: 'Answer what just happened.',
  ir_howto_bell: 'A bell warns you first. For now.',
  ir_howto_nobell: 'No bell. It just stops.',
  ir_howto_go: 'GO',

  /* ---- the vigil ------------------------------------------------------- */
  ir_brief: 'Watch. It stops without warning and asks what you just saw.',
  ir_brief_bell: 'Watch. A bell warns you before every stop.',
  ir_vigil_hint: 'Eyes up. Nothing to click until it freezes.',
  ir_stop_incoming: 'Stop incoming.',
  ir_stop_now: 'FREEZE.',
  ir_answer_hint: 'Tap an answer, or press 1-4.',
  ir_answer_hint3: 'Tap an answer, or press 1-3.',
  ir_resume: 'Resume. Denser now.',
  ir_bell_warn: 'Last stretch.',
  ir_nobell_debrief: 'That one had no bell. From Year 3, none of them do.',

  /* ---- question templates --------------------------------------------- */
  ir_q_last_word: 'What was the last word to flash?',
  ir_q_last_effect: 'What was the last effect?',
  ir_q_last_sting: 'Which sting just played?',
  ir_q_last_two: 'The last two words, in order?',
  ir_q_mode: 'Which layout were you watching?',
  ir_hear: 'Hear it',

  /* ---- effect names (the LAST_EFFECT options) -------------------------- */
  ir_fx_bubble_field: 'Bubbles',
  ir_fx_wash: 'Wash',
  ir_fx_glitch_swap: 'Glitch',
  ir_fx_gif_rain: 'Rain',
  ir_fx_flash_burst: 'Flash',
  ir_fx_gif_burst: 'Burst',
  ir_fx_ambient_field: 'Grain',
  ir_fx_crt: 'Scanlines',
  ir_fx_row_drift: 'Drift',

  /* ---- sting names (the LAST_STING options) ---------------------------- */
  ir_sting_blip: 'Tick',
  ir_sting_sting: 'Chime',
  ir_sting_pop: 'Pop',
  ir_sting_bump: 'Thud',
  ir_sting_glitch: 'Static',

  /* ---- stage layouts (the MODE options) -------------------------------- */
  ir_layout_rows: 'Rows',
  ir_layout_mosaic: 'Mosaic',
  ir_layout_swirl: 'Swirl',

  /* ---- verdicts + the truth replay ------------------------------------- */
  ir_correct: 'VERIFIED',
  ir_wrong: 'MISSED',
  /* the decks t() these three: the casino's near-miss staging, its
   * plant-resisted light, and the top rung of the jackpot ladder. */
  ir_almost: 'ALMOST',
  ir_resisted: 'RESISTED',
  ir_royal: 'ROYAL',
  ir_timeout: 'BLANKED',
  ir_truth: 'It really did.',
  ir_near: 'So close. That one flashed, but earlier.',
  ir_jackpot: 'Photographic Memory',
  ir_gotcha: 'That one flashed while the screen was FROZEN.',
  ir_voided: 'Stop voided. The vigil goes on.',
  ir_corrected: 'Corrected memory.',

  /* ---- end card -------------------------------------------------------- */
  ir_end_title: 'Vigil over',
  ir_end_stops: 'Stops answered',
  ir_end_accuracy: 'Accuracy',
  ir_end_latency: 'Average answer',
  ir_end_streak: 'Best run',
  ir_end_plants: 'Baits dodged',
  ir_end_timeouts: 'Blanked',
  ir_end_none: 'None',
  ir_end_line: 'The stream never stopped. Only you did.',
});

export default IR_LEX;
