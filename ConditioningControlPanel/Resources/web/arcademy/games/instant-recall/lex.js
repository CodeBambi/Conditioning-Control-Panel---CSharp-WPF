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
 * day pool, an effect name, a sting name) - the effect / sting NAMES are rows
 * too, because they are the only words the option chrome ever shows and a mod
 * must be able to re-voice the whole quiz card.
 *
 * MOSAIC REWORK (2026-08-23). The `ir_fx_*` rows are no longer engine kinds
 * ("Wash", "Scanlines", "Drift") - they are the ten CCP EFFECTS the class now
 * deals, under CCP's own names. The retired rows (`ir_fx_bubble_field`,
 * `ir_fx_wash`, `ir_fx_glitch_swap`, `ir_fx_gif_rain`, `ir_fx_flash_burst`,
 * `ir_fx_gif_burst`, `ir_fx_ambient_field`, `ir_fx_crt`, `ir_fx_row_drift`,
 * `ir_layout_*`, `ir_q_mode`) are gone from HERE but stay in the host's
 * `NeutralLexicon`, which is append-only.
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
  ir_howto_1: 'A wall of your media keeps changing. Effects fire over it.',
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
  ir_hear: 'Hear it',

  /* ---- THE EFFECT POOL (the LAST_EFFECT options) -----------------------
   * These are CCP'S OWN NAMES for its own effects - the ones a player already
   * knows from the app's tabs. An option the player cannot name is not a
   * question, it is a guess (owner ruling, 2026-08-23). Do not invent a new
   * name here: if CCP renames a feature, this row follows it. */
  ir_fx_flash: 'Flash image',
  ir_fx_subliminal: 'Subliminal',
  ir_fx_whisper: 'Whisper',
  ir_fx_corner_gif: 'Corner GIF',
  ir_fx_fullscreen_gif: 'Fullscreen GIF',
  ir_fx_cascade: 'Cascade',
  ir_fx_bubbles: 'Bubbles',
  ir_fx_spiral: 'Spiral',
  ir_fx_pink: 'Pink Filter',
  ir_fx_brain_drain: 'Brain Drain',

  /* ---- sting names (the LAST_STING options) ---------------------------- */
  ir_sting_blip: 'Tick',
  ir_sting_sting: 'Chime',
  ir_sting_pop: 'Pop',
  ir_sting_bump: 'Thud',
  ir_sting_glitch: 'Static',

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
