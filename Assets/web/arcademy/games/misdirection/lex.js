/* ============================================================================
 * games/misdirection/lex.js - every `md_` lexicon row MISDIRECTION renders,
 * with its neutral English fallback. Exported as DATA (the Impulse Control /
 * Deep End precedent, IC_LEX / DE_LEX) so the host's `NeutralLexicon` can be
 * mirrored key-for-key: copy the values, never re-word them.
 *
 * RULES (web CLAUDE.md trap 26 + the contract): a value longer than 96
 * characters can never be mod-skinned, so every row here is <= 96; a row is
 * rendered ONLY through ctx.lexicon(key, fallback) (Law VII - accents come
 * from the lexicon, never AI). The decks (casino / trickster / pressure) may
 * call the same `t()` with the same keys; every key either side uses lives in
 * THIS table.
 *
 * THE SHELL NOUN is the mod skin's main lever: `md_shell_noun` (cups /
 * canisters / lockers per the mod table) and `md_shell_aria` carry it. The
 * LOGIC never reads a name - it compares slot indices and shell ids.
 * ==========================================================================*/

export const MD_LEX = Object.freeze({
  /* ---- the noun (mod skin: cups / canisters / lockers) ----------------- */
  md_shell_noun: 'Shell',
  /** {n} = the shell's number, 1-based, left to right. */
  md_shell_aria: 'Shell {n}',

  /* ---- setting rows (manifest.settings) -------------------------------- */
  md_stake_mode: 'Stake prompt',
  md_stake_mode_hint: 'Ask after every win, or always bank / always ride without the prompt.',
  md_stake_mode_ask: 'Ask',
  md_stake_mode_bank: 'Always bank',
  md_stake_mode_ride: 'Always ride',
  md_shell_skin: 'Shell skin',
  md_shell_skin_hint: 'Themed shells, plain shapes, or high-contrast rims that stay readable.',
  md_shell_skin_themed: 'Themed',
  md_shell_skin_minimal: 'Minimal',
  md_shell_skin_contrast: 'High contrast',

  /* ---- keybind rows (manifest.keybinds) -------------------------------- */
  md_key_pick1: 'Pick the first shell',
  md_key_pick2: 'Pick the second shell',
  md_key_pick3: 'Pick the third shell',
  md_key_pick4: 'Pick the fourth shell',
  md_key_pick5: 'Pick the fifth shell',

  /* ---- HUD chips (aria labels; the chip TEXT is live data) -------------- */
  md_chip_round: 'Round',
  md_chip_clock: 'Time left',
  md_chip_pot: 'Pot',
  md_chip_streak: 'Streak',

  /* ---- the drawn class-rules sheet (Deck VI, images over text) ---------- */
  md_howto_title: 'Class rules',
  md_howto_watch: 'One shell lifts. What is under it is the only thing you are tracking.',
  md_howto_shuffle: 'They slide and trade places. The room will do its best to blind you.',
  md_howto_pick: 'Point at the shell you followed. Four seconds, every round.',
  md_howto_stake: 'Right? Bank the pot, or ride it double into a dirtier shuffle.',
  md_howto_go: 'Open the table',
  /** {keys} = the bound pick keys, e.g. "1 2 3 4 5". */
  md_howto_keys: 'Keys {keys} pick a shell.',

  /* ---- proctor lines (.g-md-msg, one at a time) ------------------------- */
  md_brief: 'Watch the shell. Keep watching it. Then point at it.',
  md_reveal_line: 'There she is.',
  md_shuffle_line: 'Eyes on her.',
  md_pick_line: 'Where is she?',
  md_hit_line: 'Right where you said she was.',
  md_miss_line: 'Empty. The true lid comes up.',
  md_almost_line: 'One off. She was next door the whole time.',
  md_timeout_line: 'Too slow. The lid comes up anyway.',
  md_remedial_line: 'Slow round. Clean shuffle, full pot.',
  md_blind_line: 'The hand comes over the table.',
  md_bell_warn: 'Twenty seconds.',
  md_bell_line: 'The bell. Hands off the table.',
  md_retake: 'Retake',
  md_voided_line: 'That round is off the books. Your bank is safe.',

  /* ---- the stake prompt (.g-md-stake - real buttons, honest) ------------ */
  md_stake_line: 'Bank it, or ride it double or nothing?',
  md_bank: 'Bank',
  md_ride: 'Ride',
  md_banked_line: 'Banked. Nothing takes that back.',
  md_ride_line: 'Riding. The table gets dirtier.',
  md_ride_cap_line: 'Five deep. The house pays out and the table resets.',
  md_bust_line: 'The pot goes back to the house. Your bank is untouched.',
  md_auto_bank_line: 'Banked for you.',
  md_auto_ride_line: 'Riding for you.',

  /* ---- stamps + ceremonies (Deck II) ------------------------------------ */
  md_stamp_bank: 'BANKED',
  md_stamp_bell: 'BELL',
  md_stamp_blind: 'EYES OPEN',
  md_jackpot: 'JACKPOT',
  md_royal: 'ROYAL',
  md_near_miss: 'SO CLOSE',
  md_almost: 'ONE OFF',
  md_scholarship: 'SCHOLARSHIP ROUND',

  /* ---- the trickster's lines (Deck III, via the deck's own t()) --------- */
  md_trick_seen: 'Did you see that?',
  md_trick_melt: 'The lids run like wax',
  md_trick_feint: 'Nothing moved that time',
  md_trick_hint: 'This one. Surely.',

  /* ---- the end card (.g-md-end) ---------------------------------------- */
  md_end_title: 'Table report',
  md_end_banked: 'Banked',
  md_end_picks: 'Picks',
  md_end_latency: 'Average pick',
  md_end_deepest: 'Deepest ride banked',
  md_end_rounds: 'Rounds',
  md_end_streak: 'Best streak',
  md_end_blind: 'Called through a blackout',
  md_end_clean: 'You banked a round before your first miss.',
  md_end_yes: 'Yes',
  md_end_no: 'No',
});

export default MD_LEX;
