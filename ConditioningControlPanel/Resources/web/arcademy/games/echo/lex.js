/* ============================================================================
 * games/echo/lex.js - every `ec_` lexicon row ECHO renders, with its neutral
 * English fallback. Exported as DATA (the Impulse Control / Deep End
 * precedent, IC_LEX / DE_LEX) so the host's `NeutralLexicon` can be mirrored
 * key-for-key: copy the values, never re-word them.
 *
 * RULES (web CLAUDE.md trap 26 + the Semester II/III contract): a value longer
 * than 96 characters can never be mod-skinned, so every row here is <= 96; a
 * row is rendered ONLY through ctx.lexicon(key, fallback) (Law VII - accents
 * come from the lexicon, never AI). The DECKS (style/casino/trickster/
 * pressure) t() the same keys - this table is the union of index.js's rows and
 * the deck rows the contract pins for Echo.
 *
 * NOTHING here is load-bearing for the mechanic: sequences are stored,
 * compared and graded over pad INDICES 0..5, never over a word or a string
 * (the dossier's mod-agnosticism proof). A pad's word is decoration the
 * trickster is allowed to lie on; the pad's GLYPH is the truth.
 *
 * GROUND-RULES §2 names four product words the Arcademy may never reuse. None
 * appears in any row below; the scratch suite holds the list and greps every
 * CORE file in this folder for it.
 * ==========================================================================*/

export const EC_LEX = Object.freeze({
  /* ---- keybind slot labels (shell/settings.js label_key) ---------------- */
  ec_key_pad1: 'Pad 1',
  ec_key_pad2: 'Pad 2',
  ec_key_pad3: 'Pad 3',
  ec_key_pad4: 'Pad 4',
  ec_key_pad5: 'Pad 5',
  ec_key_pad6: 'Pad 6',

  /* ---- the per-game setting -------------------------------------------- */
  ec_pad_words: 'Pad faces',
  ec_pad_words_hint: 'Pads wear one of your triggers, a plain glyph, or your own media.',

  /* ---- THE TURN (the phase banner + the step strip) --------------------- */
  ec_phase_listen: 'Listen...',
  ec_phase_yours: 'Your turn',
  ec_phase_ready: 'Sit down',
  ec_phase_over: 'Class over',
  ec_phase_aria: 'Whose turn it is',
  ec_steps_aria: 'The sequence, step by step',
  ec_step_aria: 'Step {n}',
  ec_clear: 'Echo held: {n}',
  ec_miss: 'Miss - again',
  ec_late: 'Too late',
  ec_this_one: 'This one',
  ec_stamp_miss: 'MISS',
  ec_stamp_late: 'LATE',
  ec_stamp_clear: 'HELD',

  /* ---- HUD chips (aria labels; the numbers themselves are not strings) -- */
  ec_chip_len: 'Sequence length',
  ec_chip_clock: 'Time left',
  ec_chip_streak: 'Streak',
  ec_chip_best: 'Longest echo',
  ec_retake: 'Retake',
  ec_ring_aria: 'The pads',
  ec_pad_aria: 'Pad {n}',

  /* ---- the drawn class-rules sheet (Deck VI, GO-only dismissal) --------- */
  ec_howto_title: 'Class rules',
  ec_howto_watch: 'The pads play a sequence. Watch it. Listen to it.',
  ec_howto_repeat: 'Then repeat it back, in order, by tap or by key.',
  ec_howto_decoy: 'A pad may light out of turn. Leave that one alone.',
  ec_howto_go: 'Begin',

  /* ---- the proctor line (one at a time) -------------------------------- */
  ec_brief: 'Sit down. Listen first.',
  ec_msg_watch: 'Watch.',
  ec_msg_input: 'Your turn.',
  ec_msg_clear: 'Clean. One longer now.',
  ec_msg_fail: 'Broken. Listen again.',
  ec_msg_timeout: 'Too slow. Listen again.',
  ec_msg_near: 'One short of it.',
  ec_msg_new: 'A new one, then.',
  ec_msg_encore: 'Again. Slower this time.',
  ec_msg_encore_clear: 'Held. Keep going.',
  ec_msg_encore_fail: 'Gone. A new one, then.',
  ec_msg_decoy_warn: 'One of them lies tonight. Do not echo it.',
  ec_msg_resisted: 'You let it pass. Good.',
  ec_msg_bell_warn: 'Last of the class.',
  ec_msg_silent: 'No sound tonight. Watch the light.',

  /* ---- the end card ----------------------------------------------------- */
  ec_end_title: 'Class dismissed',
  ec_end_best: 'Longest echo',
  ec_end_sequences: 'Sequences held',
  ec_end_accuracy: 'Accuracy',
  ec_end_tempo: 'Tempo held',
  ec_end_decoys: 'Decoys resisted',
  ec_end_streak: 'Best run of pads',
  ec_end_encore: 'Encore used',
  ec_end_yes: 'Yes',
  ec_end_no: 'No',
  ec_end_record: 'A new personal best',
  ec_end_line: 'The room keeps the length. You keep the tune.',

  /* ---- the decks (casino / trickster) ----------------------------------- */
  ec_decoy_tell: 'Not this one',
  ec_jackpot: 'Perfect pitch',
  ec_royal: 'The whole melody',
  ec_near_miss: 'So nearly',
  ec_taunt_stall: 'Still there?',
  ec_taunt_slow: 'Slower than you were.',
  ec_taunt_ghost: 'This one. Surely this one.',
  ec_taunt_label: 'Read it again. Or do not.',
});

export default EC_LEX;
